using BeServer.Data;
using BeServer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Services;

public class ReindexJobWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ReindexJobWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextAsync(stoppingToken);
                if (!processed)
                    await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reindex job worker loop failed.");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rag = scope.ServiceProvider.GetRequiredService<RagClient>();
        var graphExtraction = scope.ServiceProvider.GetRequiredService<GraphExtractionService>();
        var now = DateTime.UtcNow;

        var job = await db.ReindexJobs
            .Where(j =>
                (j.Status == ReindexJobStatuses.Queued || j.Status == ReindexJobStatuses.Retrying) &&
                j.AvailableAt <= now &&
                j.AttemptCount < j.MaxAttempts)
            .OrderBy(j => j.AvailableAt)
            .ThenBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
            return false;

        var targetVersion = await db.NotebookRetrievalVersions
            .SingleOrDefaultAsync(v => v.Id == job.TargetRetrievalVersionId, cancellationToken);

        if (targetVersion is null)
        {
            job.Status = ReindexJobStatuses.Cancelled;
            job.LastError = "Target retrieval version no longer exists.";
            job.CompletedAt = now;
            job.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var retrieval = new RagRetrievalConfig(
            targetVersion.Id,
            targetVersion.ChunkSize,
            targetVersion.ChunkOverlap,
            targetVersion.EmbeddingModel,
            targetVersion.EmbeddingDimensions,
            targetVersion.DefaultSearchMode,
            targetVersion.DefaultTopK,
            targetVersion.DefaultHybridAlpha);

        job.Status = ReindexJobStatuses.Running;
        job.AttemptCount += 1;
        job.StartedAt = now;
        job.UpdatedAt = now;
        job.LastError = null;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            if (job.Scope == ReindexJobScopes.Source)
                await ProcessSourceJobAsync(db, rag, graphExtraction, job, targetVersion, retrieval, cancellationToken);
            else
                await ProcessNotebookJobAsync(db, rag, graphExtraction, job, targetVersion, retrieval, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            now = DateTime.UtcNow;
            job.Status = ReindexJobStatuses.Retrying;
            job.LastError = "Worker stopped before reindex completed.";
            job.AvailableAt = now.AddSeconds(30);
            job.UpdatedAt = now;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            now = DateTime.UtcNow;
            var canRetry = job.Scope == ReindexJobScopes.Source && job.AttemptCount < job.MaxAttempts;
            job.Status = canRetry ? ReindexJobStatuses.Retrying : ReindexJobStatuses.Failed;
            job.LastError = ex.Message;
            job.AvailableAt = canRetry ? now.Add(BackoffFor(job.AttemptCount)) : now;
            job.CompletedAt = canRetry ? null : now;
            job.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                ex,
                "Reindex job {JobId} ({Scope}) failed on attempt {Attempt}/{MaxAttempts}.",
                job.Id,
                job.Scope,
                job.AttemptCount,
                job.MaxAttempts);
        }

        return true;
    }

    private async Task ProcessSourceJobAsync(
        AppDbContext db, RagClient rag, GraphExtractionService graphExtraction, ReindexJob job,
        NotebookRetrievalVersion targetVersion, RagRetrievalConfig retrieval, CancellationToken cancellationToken)
    {
        var source = await db.Sources.FirstOrDefaultAsync(
            s => s.Id == job.SourceId && s.NotebookId == job.NotebookId && s.UserId == job.UserId,
            cancellationToken);

        if (source is null)
        {
            job.Status = ReindexJobStatuses.Cancelled;
            job.LastError = "Source no longer exists.";
            job.CompletedAt = DateTime.UtcNow;
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Ingest with target version — old version's chunks are preserved during this call.
        await rag.IngestAsync(source.Id, source.NotebookId, source.UserId,
            source.FilePath ?? "", source.MimeType ?? "application/octet-stream", retrieval);

        job.GraphExtractionStatus = await graphExtraction.ExtractAndIngestAsync(
            source.Id, source.NotebookId, source.UserId, targetVersion, cancellationToken);

        var now = DateTime.UtcNow;
        job.Status = ReindexJobStatuses.Succeeded;
        job.SourcesSucceeded = 1;
        job.SourcesTotal = 1;
        job.CompletedAt = now;
        job.UpdatedAt = now;
        source.LastIndexedRetrievalVersionId = job.TargetRetrievalVersionId;
        source.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessNotebookJobAsync(
        AppDbContext db, RagClient rag, GraphExtractionService graphExtraction, ReindexJob job,
        NotebookRetrievalVersion targetVersion, RagRetrievalConfig retrieval, CancellationToken cancellationToken)
    {
        var sources = await db.Sources
            .Where(s => s.NotebookId == job.NotebookId && s.UserId == job.UserId)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        job.SourcesTotal = sources.Count;
        job.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var graphStatuses = new List<string>();
        foreach (var source in sources)
        {
            try
            {
                await rag.IngestAsync(source.Id, source.NotebookId, source.UserId,
                    source.FilePath ?? "", source.MimeType ?? "application/octet-stream", retrieval);

                var graphStatus = await graphExtraction.ExtractAndIngestAsync(
                    source.Id, source.NotebookId, source.UserId, targetVersion, cancellationToken);
                graphStatuses.Add(graphStatus);

                now = DateTime.UtcNow;
                source.LastIndexedRetrievalVersionId = job.TargetRetrievalVersionId;
                source.UpdatedAt = now;
                job.SourcesSucceeded += 1;
                job.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Graceful shutdown, not a per-source failure -- let it
                // propagate so the outer catch in ProcessNextAsync can mark
                // the job Retrying instead of recording a bogus source failure.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reindex job {JobId}: source {SourceId} failed.", job.Id, source.Id);
                job.SourcesFailed += 1;
                job.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        now = DateTime.UtcNow;
        job.Status = job.SourcesFailed > 0 ? ReindexJobStatuses.Failed : ReindexJobStatuses.Succeeded;
        job.GraphExtractionStatus = GraphExtractionService.Aggregate(graphStatuses);
        if (job.SourcesFailed > 0)
            job.LastError = $"{job.SourcesFailed} of {job.SourcesTotal} sources failed to reindex.";
        job.CompletedAt = now;
        job.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static TimeSpan BackoffFor(int attemptCount)
    {
        var seconds = Math.Min(300, Math.Pow(2, Math.Max(1, attemptCount)) * 5);
        return TimeSpan.FromSeconds(seconds);
    }
}
