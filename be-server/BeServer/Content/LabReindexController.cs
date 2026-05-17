using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Content;

[ApiController]
[Route("api/lab")]
[Authorize(Policy = "DevAdminOnly")]
public class LabReindexController(
    AppDbContext db,
    CurrentUserAccessor currentUser,
    OwnershipService ownership) : ControllerBase
{
    private string UserId => currentUser.UserId;

    [HttpGet("notebooks/{notebookId}/reindex-jobs")]
    public async Task<IActionResult> ListJobs(string notebookId)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");

        var jobs = await db.ReindexJobs
            .Where(j => j.NotebookId == notebookId && j.UserId == UserId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();

        return Ok(jobs.Select(ToDto));
    }

    [HttpGet("reindex-jobs/{jobId}")]
    public async Task<IActionResult> GetJob(string jobId)
    {
        var job = await db.ReindexJobs.SingleOrDefaultAsync(j => j.Id == jobId && j.UserId == UserId);
        if (job is null)
            return ApiErrors.NotFound(this, "reindex.job_not_found", "Reindex job not found");
        return Ok(ToDto(job));
    }

    [HttpPost("notebooks/{notebookId}/reindex")]
    public async Task<IActionResult> QueueNotebookReindex(string notebookId, [FromBody] QueueReindexRequest req)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");

        var notebook = await db.Notebooks.SingleAsync(n => n.Id == notebookId);
        var targetVersion = await db.NotebookRetrievalVersions
            .SingleOrDefaultAsync(v => v.Id == req.TargetVersionId && v.NotebookId == notebookId);
        if (targetVersion is null)
            return ApiErrors.BadRequest(this, "retrieval.version_not_found", "Target retrieval version not found");

        var job = new ReindexJob
        {
            NotebookId = notebookId,
            UserId = UserId,
            Scope = ReindexJobScopes.Notebook,
            TargetRetrievalVersionId = req.TargetVersionId,
            PreviousRetrievalVersionId = notebook.ActiveRetrievalVersionId,
            MaxAttempts = 1,
        };
        db.ReindexJobs.Add(job);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetJob), new { jobId = job.Id }, ToDto(job));
    }

    [HttpPost("notebooks/{notebookId}/sources/{sourceId}/reindex")]
    public async Task<IActionResult> QueueSourceReindex(string notebookId, string sourceId, [FromBody] QueueReindexRequest req)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");

        var source = await db.Sources.SingleOrDefaultAsync(s => s.Id == sourceId && s.NotebookId == notebookId && s.UserId == UserId);
        if (source is null)
            return ApiErrors.NotFound(this, "source.not_found", "Source not found");

        var targetVersion = await db.NotebookRetrievalVersions
            .SingleOrDefaultAsync(v => v.Id == req.TargetVersionId && v.NotebookId == notebookId);
        if (targetVersion is null)
            return ApiErrors.BadRequest(this, "retrieval.version_not_found", "Target retrieval version not found");

        var job = new ReindexJob
        {
            NotebookId = notebookId,
            UserId = UserId,
            SourceId = sourceId,
            Scope = ReindexJobScopes.Source,
            TargetRetrievalVersionId = req.TargetVersionId,
            PreviousRetrievalVersionId = source.LastIndexedRetrievalVersionId,
        };
        db.ReindexJobs.Add(job);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetJob), new { jobId = job.Id }, ToDto(job));
    }

    [HttpPost("reindex-jobs/{jobId}/promote")]
    public async Task<IActionResult> Promote(string jobId)
    {
        var job = await db.ReindexJobs.SingleOrDefaultAsync(j => j.Id == jobId && j.UserId == UserId);
        if (job is null)
            return ApiErrors.NotFound(this, "reindex.job_not_found", "Reindex job not found");
        if (job.Status != ReindexJobStatuses.Succeeded)
            return ApiErrors.BadRequest(this, "reindex.not_succeeded", "Only succeeded jobs can be promoted");

        var notebook = await db.Notebooks.SingleAsync(n => n.Id == job.NotebookId);
        notebook.ActiveRetrievalVersionId = job.TargetRetrievalVersionId;
        notebook.UpdatedAt = DateTime.UtcNow;

        var sources = await db.Sources
            .Where(s => s.NotebookId == job.NotebookId && s.UserId == UserId)
            .ToListAsync();
        foreach (var source in sources)
        {
            source.ActiveRetrievalVersionId = job.TargetRetrievalVersionId;
            source.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return Ok(new { notebook.Id, notebook.ActiveRetrievalVersionId, promoted_from_job = job.Id });
    }

    [HttpPost("reindex-jobs/{jobId}/cancel")]
    public async Task<IActionResult> Cancel(string jobId)
    {
        var job = await db.ReindexJobs.SingleOrDefaultAsync(j => j.Id == jobId && j.UserId == UserId);
        if (job is null)
            return ApiErrors.NotFound(this, "reindex.job_not_found", "Reindex job not found");
        if (job.Status != ReindexJobStatuses.Queued && job.Status != ReindexJobStatuses.Retrying)
            return ApiErrors.BadRequest(this, "reindex.not_cancellable", "Only queued or retrying jobs can be cancelled");

        job.Status = ReindexJobStatuses.Cancelled;
        job.CompletedAt = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToDto(job));
    }

    [HttpDelete("notebooks/{notebookId}/retrieval-versions/{versionId}/payload")]
    public async Task<IActionResult> PruneVersionPayload(string notebookId, string versionId, [FromServices] RagClient rag)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");

        var notebook = await db.Notebooks.SingleAsync(n => n.Id == notebookId && n.UserId == UserId);
        var version = await db.NotebookRetrievalVersions
            .SingleOrDefaultAsync(v => v.Id == versionId && v.NotebookId == notebookId);
        if (version is null)
            return ApiErrors.NotFound(this, "retrieval.version_not_found", "Retrieval version not found");
        if (notebook.ActiveRetrievalVersionId == versionId)
            return ApiErrors.BadRequest(this, "retrieval.active_payload_not_prunable", "Active retrieval payload cannot be pruned.");

        await rag.DeleteNotebookVersionChunksAsync(notebookId, UserId, versionId);
        return NoContent();
    }

    private static object ToDto(ReindexJob j) => new
    {
        j.Id,
        j.Scope,
        j.SourceId,
        j.NotebookId,
        j.TargetRetrievalVersionId,
        j.PreviousRetrievalVersionId,
        j.Status,
        j.SourcesTotal,
        j.SourcesSucceeded,
        j.SourcesFailed,
        j.LastError,
        j.AttemptCount,
        j.StartedAt,
        j.CompletedAt,
        j.CreatedAt,
    };
}

public record QueueReindexRequest(string TargetVersionId);
