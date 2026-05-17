using BeServer.Data;
using BeServer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Services;

public class RetrievalVersionService(AppDbContext db)
{
    public async Task<NotebookRetrievalVersion> CreateInitialVersionAsync(Notebook notebook, string userId)
    {
        var preset = await db.RetrievalPresets.SingleAsync(p => p.Key == "general");
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

    public static NotebookRetrievalVersion FromPreset(string notebookId, string userId, RetrievalPreset preset, string? notes) =>
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
            Notes = notes,
        };

    public static NotebookRetrievalVersion Fork(string notebookId, string userId, NotebookRetrievalVersion parent, string? notes) =>
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
            Notes = notes,
        };
}
