using System.Security.Claims;
using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace BeServer.Content;

[ApiController]
[Route("api/notebooks/{notebookId}/sources")]
[Authorize]
public class SourcesController(
    AppDbContext db,
    RagClient rag,
    IConfiguration config) : ControllerBase
{
    private static readonly HashSet<string> AllowedMimeTypes =
    [
        "application/pdf",
        "text/plain",
        "text/markdown",
        "text/x-markdown",
        "text/csv",
        "application/json",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    ];

    private string UserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException("JWT is missing user identity claim.");
            return id;
        }
    }

    private string UploadDir => config["UPLOAD_DIR"] ?? "/app/uploads";

    [HttpGet]
    public async Task<IActionResult> List(string notebookId)
    {
        // Verify notebook belongs to user (LOGIC-05)
        if (!await db.Notebooks.AnyAsync(n => n.Id == notebookId && n.UserId == UserId))
            return NotFound(new { error = "Notebook not found" });

        var sources = await db.Sources
            .Where(s => s.NotebookId == notebookId && s.UserId == UserId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SourceDto(
                s.Id,
                s.Title,
                s.MimeType,
                s.FileSizeBytes,
                s.Status,
                s.CreatedAt,
                db.IngestionJobs
                    .Where(j => j.SourceId == s.Id && j.JobType == IngestionJobTypes.Ingest)
                    .OrderByDescending(j => j.CreatedAt)
                    .Select(j => new IngestionJobDto(
                        j.Id,
                        j.Status,
                        j.AttemptCount,
                        j.MaxAttempts,
                        j.LastError,
                        j.UpdatedAt))
                    .FirstOrDefault()))
            .ToListAsync();

        return Ok(sources);
    }

    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Upload(string notebookId, IFormFile file)
    {
        if (file.Length == 0)
            return BadRequest(new { error = "File is empty." });

        // SEC-03: MIME type allowlist — strip charset/boundary params before checking
        var mimeType = file.ContentType.Split(';')[0].Trim().ToLowerInvariant();
        if (!AllowedMimeTypes.Contains(mimeType))
            return BadRequest(new { error = $"File type '{mimeType}' is not allowed." });

        var nb = await db.Notebooks.FirstOrDefaultAsync(n => n.Id == notebookId && n.UserId == UserId);
        if (nb is null) return NotFound(new { error = "Notebook not found" });

        // SEC-01: sanitize filename — strip path separators and null bytes
        var safeFileName = Path.GetFileName(file.FileName)
            .Replace("..", "")
            .Trim()
            .Replace('\0', '_');
        if (string.IsNullOrWhiteSpace(safeFileName)) safeFileName = "upload";

        var source = new Source
        {
            UserId = UserId,
            NotebookId = notebookId,
            Title = file.FileName,
            MimeType = mimeType,
            FileSizeBytes = file.Length,
            Status = IngestionJobStatuses.Queued,
        };

        // LOGIC-02: persist DB record first; then write file
        var filePath = Path.Combine(UploadDir, UserId, $"{source.Id}_{safeFileName}");
        source.FilePath = filePath;
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        // Write file after successful DB commit
        try
        {
            var dir = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(dir);
            await using (var stream = System.IO.File.Create(filePath))
                await file.CopyToAsync(stream);
        }
        catch (Exception ex)
        {
            source.Status = IngestionJobStatuses.Failed;
            source.UpdatedAt = DateTime.UtcNow;
            var failedJob = new IngestionJob
            {
                SourceId = source.Id,
                NotebookId = notebookId,
                UserId = UserId,
                JobType = IngestionJobTypes.Ingest,
                Status = IngestionJobStatuses.Failed,
                AttemptCount = 0,
                MaxAttempts = 3,
                LastError = $"File write failed: {ex.Message}",
                CompletedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.IngestionJobs.Add(failedJob);
            await db.SaveChangesAsync();
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "File write failed", source.Id, jobId = failedJob.Id });
        }

        var job = new IngestionJob
        {
            SourceId = source.Id,
            NotebookId = notebookId,
            UserId = UserId,
            JobType = IngestionJobTypes.Ingest,
            Status = IngestionJobStatuses.Queued,
            AttemptCount = 0,
            MaxAttempts = 3,
            AvailableAt = DateTime.UtcNow,
        };
        db.IngestionJobs.Add(job);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new { notebookId },
            new SourceDto(
                source.Id,
                source.Title,
                source.MimeType,
                source.FileSizeBytes,
                source.Status,
                source.CreatedAt,
                new IngestionJobDto(job.Id, job.Status, job.AttemptCount, job.MaxAttempts, job.LastError, job.UpdatedAt)));
    }

    [HttpGet("{id}/ingestion-job")]
    public async Task<IActionResult> GetIngestionJob(string notebookId, string id)
    {
        if (!await db.Sources.AnyAsync(s => s.Id == id && s.NotebookId == notebookId && s.UserId == UserId))
            return NotFound();

        var job = await db.IngestionJobs
            .Where(j => j.SourceId == id && j.NotebookId == notebookId && j.UserId == UserId)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new IngestionJobDto(
                j.Id,
                j.Status,
                j.AttemptCount,
                j.MaxAttempts,
                j.LastError,
                j.UpdatedAt))
            .FirstOrDefaultAsync();

        return job is null ? NotFound() : Ok(job);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string notebookId, string id)
    {
        var source = await db.Sources.FirstOrDefaultAsync(
            s => s.Id == id && s.NotebookId == notebookId && s.UserId == UserId);
        if (source is null) return NotFound();

        var now = DateTime.UtcNow;
        var activeJobs = await db.IngestionJobs
            .Where(j =>
                j.SourceId == id &&
                j.NotebookId == notebookId &&
                j.UserId == UserId &&
                j.JobType == IngestionJobTypes.Ingest &&
                (j.Status == IngestionJobStatuses.Queued ||
                 j.Status == IngestionJobStatuses.Retrying ||
                 j.Status == IngestionJobStatuses.Running))
            .ToListAsync();
        foreach (var job in activeJobs)
        {
            job.Status = IngestionJobStatuses.Cancelled;
            job.LastError = "Source was deleted before ingestion completed.";
            job.CompletedAt = now;
            job.UpdatedAt = now;
        }

        // LOGIC-01: delete from ArangoDB before removing the MySQL record
        try { await rag.DeleteAsync(id); }
        catch (Exception ex)
        {
            db.IngestionJobs.Add(new IngestionJob
            {
                SourceId = source.Id,
                NotebookId = notebookId,
                UserId = UserId,
                JobType = IngestionJobTypes.DeleteCleanup,
                Status = IngestionJobStatuses.Failed,
                AttemptCount = 1,
                MaxAttempts = 1,
                LastError = ex.Message,
                CompletedAt = now,
                UpdatedAt = now,
            });
            Console.Error.WriteLine($"[rag-delete] source={id} error={ex.Message}");
        }

        if (source.FilePath is not null && System.IO.File.Exists(source.FilePath))
            System.IO.File.Delete(source.FilePath);

        db.Sources.Remove(source);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record SourceDto(
    string Id,
    string Title,
    string? MimeType,
    long? FileSizeBytes,
    string Status,
    DateTime CreatedAt,
    IngestionJobDto? IngestionJob);

public record IngestionJobDto(
    string Id,
    string Status,
    int AttemptCount,
    int MaxAttempts,
    string? LastError,
    DateTime UpdatedAt);
