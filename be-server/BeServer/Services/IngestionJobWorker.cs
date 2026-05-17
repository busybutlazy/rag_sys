using BeServer.Data;
using BeServer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Services;

public class IngestionJobWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<IngestionJobWorker> logger) : BackgroundService
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
                logger.LogError(ex, "Ingestion job worker loop failed.");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rag = scope.ServiceProvider.GetRequiredService<RagClient>();
        var now = DateTime.UtcNow;

        var job = await db.IngestionJobs
            .Where(j =>
                j.JobType == IngestionJobTypes.Ingest &&
                (j.Status == IngestionJobStatuses.Queued || j.Status == IngestionJobStatuses.Retrying) &&
                j.AvailableAt <= now &&
                j.AttemptCount < j.MaxAttempts)
            .OrderBy(j => j.AvailableAt)
            .ThenBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
            return false;

        var source = await db.Sources.FirstOrDefaultAsync(
            s => s.Id == job.SourceId && s.NotebookId == job.NotebookId && s.UserId == job.UserId,
            cancellationToken);

        if (source is null)
        {
            job.Status = IngestionJobStatuses.Cancelled;
            job.LastError = "Source no longer exists.";
            job.CompletedAt = now;
            job.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        job.Status = IngestionJobStatuses.Running;
        job.AttemptCount += 1;
        job.StartedAt = now;
        job.UpdatedAt = now;
        job.LastError = null;
        source.Status = SourceStatuses.Running;
        source.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await rag.IngestAsync(source.Id, source.NotebookId, source.UserId, source.FilePath ?? "", source.MimeType ?? "application/octet-stream");

            now = DateTime.UtcNow;
            job.Status = IngestionJobStatuses.Succeeded;
            job.CompletedAt = now;
            job.UpdatedAt = now;
            source.Status = SourceStatuses.Ingested;
            source.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            now = DateTime.UtcNow;
            job.Status = IngestionJobStatuses.Retrying;
            job.LastError = "Worker stopped before ingestion completed.";
            job.AvailableAt = now.AddSeconds(30);
            job.UpdatedAt = now;
            source.Status = SourceStatuses.Retrying;
            source.UpdatedAt = now;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            now = DateTime.UtcNow;
            var canRetry = job.AttemptCount < job.MaxAttempts;
            job.Status = canRetry ? IngestionJobStatuses.Retrying : IngestionJobStatuses.Failed;
            job.LastError = ex.Message;
            job.AvailableAt = canRetry ? now.Add(BackoffFor(job.AttemptCount)) : now;
            job.CompletedAt = canRetry ? null : now;
            job.UpdatedAt = now;
            source.Status = canRetry ? SourceStatuses.Retrying : SourceStatuses.Failed;
            source.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                ex,
                "Ingestion job {JobId} failed on attempt {Attempt}/{MaxAttempts}.",
                job.Id,
                job.AttemptCount,
                job.MaxAttempts);
        }

        return true;
    }

    private static TimeSpan BackoffFor(int attemptCount)
    {
        var seconds = Math.Min(300, Math.Pow(2, Math.Max(1, attemptCount)) * 5);
        return TimeSpan.FromSeconds(seconds);
    }
}
