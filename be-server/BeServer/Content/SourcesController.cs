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
public class SourcesController(AppDbContext db, RagClient rag, IConfiguration config) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")!;

    private string UploadDir => config["UPLOAD_DIR"] ?? "/app/uploads";

    [HttpGet]
    public async Task<IActionResult> List(string notebookId) =>
        Ok(await db.Sources
            .Where(s => s.NotebookId == notebookId && s.UserId == UserId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.Id, s.Title, s.MimeType, s.FileSizeBytes, s.Status, s.CreatedAt })
            .ToListAsync());

    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Upload(string notebookId, IFormFile file)
    {
        var nb = await db.Notebooks.FirstOrDefaultAsync(n => n.Id == notebookId && n.UserId == UserId);
        if (nb is null) return NotFound(new { error = "Notebook not found" });

        var source = new Source
        {
            UserId = UserId,
            NotebookId = notebookId,
            Title = file.FileName,
            MimeType = file.ContentType,
            FileSizeBytes = file.Length,
            Status = "uploaded",
        };

        var dir = Path.Combine(UploadDir, UserId);
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, $"{source.Id}_{Path.GetFileName(file.FileName)}");
        await using (var stream = System.IO.File.Create(filePath))
            await file.CopyToAsync(stream);

        source.FilePath = filePath;
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        // Fire-and-forget ingest; rag-server stores raw doc metadata in ArangoDB
        _ = Task.Run(async () =>
        {
            try
            {
                await rag.IngestAsync(source.Id, filePath, file.ContentType);
                source.Status = "ingested";
                source.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            catch
            {
                source.Status = "error";
                source.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
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
        if (source.FilePath is not null && System.IO.File.Exists(source.FilePath))
            System.IO.File.Delete(source.FilePath);
        db.Sources.Remove(source);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
