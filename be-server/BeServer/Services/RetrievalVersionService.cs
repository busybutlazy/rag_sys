using BeServer.Data;
using BeServer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Services;

public class RetrievalVersionService(AppDbContext db)
{
    public async Task<NotebookRetrievalVersion> CreateInitialVersionAsync(Notebook notebook, string userId)
    {
        var preset = await db.RetrievalPresets.FirstOrDefaultAsync(p => p.Key == "general")
            ?? await db.RetrievalPresets.FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("No retrieval presets are seeded. Run the be-server seed step first.");
        var version = FromPreset(notebook.Id, userId, preset, notes: "Initial notebook version");
        db.NotebookRetrievalVersions.Add(version);
        notebook.ActiveRetrievalVersionId = version.Id;
        return version;
    }

    public async Task<NotebookRetrievalVersion?> GetActiveVersionAsync(string notebookId) =>
        await db.Notebooks
            .Where(n => n.Id == notebookId && n.ActiveRetrievalVersionId != null)
            .SelectMany(n => db.NotebookRetrievalVersions.Where(v => v.Id == n.ActiveRetrievalVersionId))
            .SingleOrDefaultAsync();

    public static NotebookRetrievalVersion FromPreset(
        string notebookId,
        string userId,
        RetrievalPreset preset,
        string? notes,
        bool enableGraph = false,
        string? graphExtractionModel = null,
        int maxGraphHops = 1,
        int maxFactHits = 8) =>
        new()
        {
            NotebookId = notebookId,
            CreatedByUserId = userId,
            OriginPresetId = preset.Id,
            ChunkSize = preset.ChunkSize,
            ChunkOverlap = preset.ChunkOverlap,
            EmbeddingModel = preset.EmbeddingModel,
            EmbeddingDimensions = preset.EmbeddingDimensions,
            DefaultSearchMode = preset.DefaultSearchMode,
            DefaultTopK = preset.DefaultTopK,
            DefaultHybridAlpha = preset.DefaultHybridAlpha,
            EnableGraph = enableGraph,
            GraphExtractionModel = graphExtractionModel,
            MaxGraphHops = maxGraphHops,
            MaxFactHits = maxFactHits,
            Notes = notes,
        };

    // Forking inherits the parent's graph settings by default (an
    // unrelated config fork shouldn't silently turn graph off), but the
    // caller can explicitly override any of them -- that's the only way
    // to flip EnableGraph for a notebook, since versions are immutable
    // after creation.
    public static NotebookRetrievalVersion Fork(
        string notebookId,
        string userId,
        NotebookRetrievalVersion parent,
        string? notes,
        bool? enableGraph = null,
        string? graphExtractionModel = null,
        int? maxGraphHops = null,
        int? maxFactHits = null) =>
        new()
        {
            NotebookId = notebookId,
            CreatedByUserId = userId,
            ParentVersionId = parent.Id,
            OriginPresetId = parent.OriginPresetId,
            ChunkSize = parent.ChunkSize,
            ChunkOverlap = parent.ChunkOverlap,
            EmbeddingModel = parent.EmbeddingModel,
            EmbeddingDimensions = parent.EmbeddingDimensions,
            DefaultSearchMode = parent.DefaultSearchMode,
            DefaultTopK = parent.DefaultTopK,
            DefaultHybridAlpha = parent.DefaultHybridAlpha,
            EnableGraph = enableGraph ?? parent.EnableGraph,
            GraphExtractionModel = graphExtractionModel ?? parent.GraphExtractionModel,
            MaxGraphHops = maxGraphHops ?? parent.MaxGraphHops,
            MaxFactHits = maxFactHits ?? parent.MaxFactHits,
            Notes = notes,
        };
}
