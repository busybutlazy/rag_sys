using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace BeServer.Content;

[ApiController]
[Route("api/notebooks/{notebookId}/sources")]
[Authorize]
public class SourcesController(
    AppDbContext db,
    RagClient rag,
    IConfiguration config,
    CurrentUserAccessor currentUser,
    OwnershipService ownership) : ControllerBase
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
    private static readonly Dictionary<string, HashSet<string>> AllowedExtensionsByMime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = [".pdf"],
        ["text/plain"] = [".txt"],
        ["text/markdown"] = [".md", ".markdown"],
        ["text/x-markdown"] = [".md", ".markdown"],
        ["text/csv"] = [".csv"],
        ["application/json"] = [".json"],
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = [".docx"],
    };

    private string UserId => currentUser.UserId;

    private string UploadDir => config["UPLOAD_DIR"] ?? "/app/uploads";
    private long MaxUploadBytes => config.GetValue<long?>("UPLOAD_MAX_FILE_BYTES") ?? 50L * 1024 * 1024;
    private long UserStorageQuotaBytes => config.GetValue<long?>("UPLOAD_USER_STORAGE_QUOTA_BYTES") ?? 250L * 1024 * 1024;
    private int NotebookSourceLimit => config.GetValue<int?>("UPLOAD_NOTEBOOK_SOURCE_LIMIT") ?? 100;

    [HttpGet]
    public async Task<IActionResult> List(string notebookId)
    {
        // Verify notebook belongs to user (LOGIC-05)
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");

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
        if (file.Length > MaxUploadBytes)
            return BadRequest(new { error = $"File exceeds upload limit of {MaxUploadBytes} bytes." });

        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");

        // SEC-01: sanitize filename — strip path separators and null bytes
        var safeFileName = Path.GetFileName(file.FileName)
            .Replace("..", "")
            .Trim()
            .Replace('\0', '_');
        if (string.IsNullOrWhiteSpace(safeFileName)) safeFileName = "upload";

        var sourceCount = await db.Sources.CountAsync(s => s.NotebookId == notebookId && s.UserId == UserId);
        if (sourceCount >= NotebookSourceLimit)
            return BadRequest(new { error = $"Notebook source limit of {NotebookSourceLimit} reached." });

        var usedBytes = await db.Sources
            .Where(s => s.UserId == UserId)
            .SumAsync(s => s.FileSizeBytes ?? 0);
        if (usedBytes + file.Length > UserStorageQuotaBytes)
            return BadRequest(new { error = "User storage quota exceeded." });

        var originalContentType = NormalizeMime(file.ContentType);
        var extension = Path.GetExtension(safeFileName);
        await using var detectionStream = file.OpenReadStream();
        var detectedMimeType = await DetectMimeTypeAsync(detectionStream, extension);
        if (detectedMimeType is null || !AllowedMimeTypes.Contains(detectedMimeType))
            return BadRequest(new { error = "File type could not be validated or is not allowed." });

        if (!AllowedExtensionsByMime.TryGetValue(detectedMimeType, out var allowedExtensions) ||
            !allowedExtensions.Contains(extension))
            return BadRequest(new { error = $"File extension '{extension}' does not match detected type '{detectedMimeType}'." });

        if (!IsClaimCompatible(originalContentType, detectedMimeType))
            return BadRequest(new { error = $"Claimed file type '{originalContentType}' does not match detected type '{detectedMimeType}'." });

        var source = new Source
        {
            UserId = UserId,
            NotebookId = notebookId,
            Title = file.FileName,
            MimeType = NormalizeForIngestion(detectedMimeType),
            OriginalContentType = originalContentType,
            DetectedMimeType = detectedMimeType,
            FileSizeBytes = file.Length,
            Status = SourceStatuses.Queued,
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
            source.Status = SourceStatuses.Failed;
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

    private static string NormalizeMime(string? contentType) =>
        (contentType ?? "").Split(';')[0].Trim().ToLowerInvariant();

    private static string NormalizeForIngestion(string detectedMimeType) =>
        detectedMimeType == "text/x-markdown" ? "text/markdown" : detectedMimeType;

    private static bool IsClaimCompatible(string claimed, string detected)
    {
        if (claimed == detected) return true;
        return detected == "text/markdown" && claimed == "text/x-markdown"
            || detected == "text/x-markdown" && claimed == "text/markdown";
    }

    private static async Task<string?> DetectMimeTypeAsync(Stream stream, string extension)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        if (bytes.Length >= 5 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-")
            return "application/pdf";

        if (IsDocx(bytes))
            return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        if (!LooksLikeUtf8Text(bytes))
            return null;

        var text = Encoding.UTF8.GetString(bytes);
        try
        {
            using var _ = JsonDocument.Parse(text);
            return "application/json";
        }
        catch (JsonException)
        {
            // text-like formats continue below
        }

        return extension.ToLowerInvariant() switch
        {
            ".txt" => "text/plain",
            ".md" or ".markdown" => "text/markdown",
            ".csv" => "text/csv",
            _ => null,
        };
    }

    private static bool IsDocx(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B)
            return false;
        try
        {
            using var ms = new MemoryStream(bytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
            return archive.GetEntry("[Content_Types].xml") is not null &&
                   archive.Entries.Any(e => e.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool LooksLikeUtf8Text(byte[] bytes)
    {
        if (bytes.Any(b => b == 0))
            return false;
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    [HttpGet("{id}/ingestion-job")]
    public async Task<IActionResult> GetIngestionJob(string notebookId, string id)
    {
        if (!await ownership.SourceExistsAsync(notebookId, id))
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
        try { await rag.DeleteAsync(id, UserId); }
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

    [HttpPost("{id}/reingest")]
    public async Task<IActionResult> Reingest(string notebookId, string id)
    {
        var source = await db.Sources.FirstOrDefaultAsync(
            s => s.Id == id && s.NotebookId == notebookId && s.UserId == UserId);
        if (source is null) return ApiErrors.NotFound(this, "source.not_found", "Source not found");

        if (source.FilePath is null || !System.IO.File.Exists(source.FilePath))
            return ApiErrors.BadRequest(this, "source.file_missing", "Source file is no longer available for re-ingestion");

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
            job.LastError = "Superseded by manual re-ingest request.";
            job.CompletedAt = now;
            job.UpdatedAt = now;
        }

        var newJob = new IngestionJob
        {
            SourceId = source.Id,
            NotebookId = notebookId,
            UserId = UserId,
            JobType = IngestionJobTypes.Ingest,
            Status = IngestionJobStatuses.Queued,
            AvailableAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.IngestionJobs.Add(newJob);
        source.Status = SourceStatuses.Queued;
        source.UpdatedAt = now;
        await db.SaveChangesAsync();

        return Accepted(new { jobId = newJob.Id, sourceId = source.Id, status = newJob.Status });
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
