using System.Security.Claims;
using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Content;

[ApiController]
[Route("api/notebooks/{notebookId}/sources")]
[Authorize]
public class SourcesController(
    AppDbContext db,
    RagClient rag,
    IConfiguration config,
    IServiceScopeFactory scopeFactory) : ControllerBase
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

        return Ok(await db.Sources
            .Where(s => s.NotebookId == notebookId && s.UserId == UserId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.Id, s.Title, s.MimeType, s.FileSizeBytes, s.Status, s.CreatedAt })
            .ToListAsync());
    }

    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Upload(string notebookId, IFormFile file)
    {
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
            Status = "uploaded",
        };

        // LOGIC-02: persist DB record first; then write file
        var filePath = Path.Combine(UploadDir, UserId, $"{source.Id}_{safeFileName}");
        source.FilePath = filePath;
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        // Write file after successful DB commit
        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);
        await using (var stream = System.IO.File.Create(filePath))
            await file.CopyToAsync(stream);

        // SEC-02: fire-and-forget using a fresh DI scope (avoids disposed DbContext)
        _ = Task.Run(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = await scopedDb.Sources.FindAsync(source.Id);
            if (s is null) return;
            try
            {
                await rag.IngestAsync(s.Id, notebookId, filePath, s.MimeType);
                s.Status = "ingested";
            }
            catch (Exception ex)
            {
                s.Status = "error";
                Console.Error.WriteLine($"[rag-ingest] source={s.Id} error={ex.Message}");
            }
            s.UpdatedAt = DateTime.UtcNow;
            await scopedDb.SaveChangesAsync();
        });

        return CreatedAtAction(nameof(List), new { notebookId },
            new { source.Id, source.Title, source.Status });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string notebookId, string id)
    {
        var source = await db.Sources.FirstOrDefaultAsync(
            s => s.Id == id && s.NotebookId == notebookId && s.UserId == UserId);
        if (source is null) return NotFound();

        // LOGIC-01: delete from ArangoDB before removing the MySQL record
        try { await rag.DeleteAsync(id); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[rag-delete] source={id} error={ex.Message}");
        }

        if (source.FilePath is not null && System.IO.File.Exists(source.FilePath))
            System.IO.File.Delete(source.FilePath);

        db.Sources.Remove(source);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
