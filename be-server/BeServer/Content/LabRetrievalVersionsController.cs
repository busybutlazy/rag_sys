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
public class LabRetrievalVersionsController(
    AppDbContext db,
    CurrentUserAccessor currentUser,
    OwnershipService ownership) : ControllerBase
{
    private string UserId => currentUser.UserId;

    [HttpGet("retrieval-presets")]
    public async Task<IActionResult> Presets() =>
        Ok(await db.RetrievalPresets.OrderBy(p => p.Key).ToListAsync());

    [HttpGet("notebooks/{notebookId}/retrieval-versions")]
    public async Task<IActionResult> Versions(string notebookId)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");

        var activeId = await db.Notebooks.Where(n => n.Id == notebookId).Select(n => n.ActiveRetrievalVersionId).SingleAsync();
        var rows = await db.NotebookRetrievalVersions
            .Where(v => v.NotebookId == notebookId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();
        return Ok(rows.Select(v => new
        {
            v.Id,
            v.ParentVersionId,
            v.OriginPresetId,
            v.ChunkSize,
            v.ChunkOverlap,
            v.EmbeddingModel,
            v.EmbeddingDimensions,
            v.DefaultSearchMode,
            v.DefaultTopK,
            v.DefaultHybridAlpha,
            v.EnableGraph,
            v.GraphExtractionModel,
            v.MaxGraphHops,
            v.MaxFactHits,
            v.Notes,
            v.CreatedAt,
            Active = v.Id == activeId,
        }));
    }

    [HttpPost("notebooks/{notebookId}/retrieval-versions")]
    public async Task<IActionResult> Create(string notebookId, [FromBody] CreateRetrievalVersionRequest req)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");

        NotebookRetrievalVersion version;
        if (!string.IsNullOrWhiteSpace(req.PresetKey))
        {
            var preset = await db.RetrievalPresets.SingleOrDefaultAsync(p => p.Key == req.PresetKey);
            if (preset is null)
                return ApiErrors.BadRequest(this, "retrieval.preset_not_found", "Preset not found");
            version = RetrievalVersionService.FromPreset(
                notebookId, UserId, preset, req.Notes,
                req.EnableGraph ?? false, req.GraphExtractionModel, req.MaxGraphHops ?? 1, req.MaxFactHits ?? 8);
        }
        else
        {
            var parent = await db.NotebookRetrievalVersions.SingleOrDefaultAsync(v => v.Id == req.ParentVersionId && v.NotebookId == notebookId);
            if (parent is null)
                return ApiErrors.BadRequest(this, "retrieval.parent_not_found", "Parent version not found");
            version = RetrievalVersionService.Fork(
                notebookId, UserId, parent, req.Notes,
                req.EnableGraph, req.GraphExtractionModel, req.MaxGraphHops, req.MaxFactHits);
        }

        db.NotebookRetrievalVersions.Add(version);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Versions), new { notebookId }, version);
    }

    [HttpPost("notebooks/{notebookId}/retrieval-versions/{versionId}/activate")]
    public async Task<IActionResult> Activate(string notebookId, string versionId)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");
        var notebook = await db.Notebooks.SingleAsync(n => n.Id == notebookId);
        var version = await db.NotebookRetrievalVersions.SingleOrDefaultAsync(v => v.Id == versionId && v.NotebookId == notebookId);
        if (version is null)
            return ApiErrors.NotFound(this, "retrieval.version_not_found", "Retrieval version not found");
        notebook.ActiveRetrievalVersionId = version.Id;
        notebook.UpdatedAt = DateTime.UtcNow;
        var sources = await db.Sources.Where(s => s.NotebookId == notebookId && s.UserId == UserId).ToListAsync();
        foreach (var source in sources)
            source.ActiveRetrievalVersionId = version.Id;
        await db.SaveChangesAsync();
        return Ok(new { notebook.Id, notebook.ActiveRetrievalVersionId, indexed_payload_current = false });
    }
}

public record CreateRetrievalVersionRequest(
    string? PresetKey,
    string? ParentVersionId,
    string? Notes,
    bool? EnableGraph = null,
    string? GraphExtractionModel = null,
    int? MaxGraphHops = null,
    int? MaxFactHits = null);
