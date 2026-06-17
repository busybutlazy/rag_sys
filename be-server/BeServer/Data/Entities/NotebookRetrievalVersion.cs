namespace BeServer.Data.Entities;

public class NotebookRetrievalVersion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string NotebookId { get; set; } = null!;
    public string CreatedByUserId { get; set; } = null!;
    public string? ParentVersionId { get; set; }
    public string? OriginPresetId { get; set; }
    public int ChunkSize { get; set; }
    public int ChunkOverlap { get; set; }
    public string EmbeddingModel { get; set; } = null!;
    public int EmbeddingDimensions { get; set; }
    public string DefaultSearchMode { get; set; } = "hybrid";
    public int DefaultTopK { get; set; } = 5;
    public double DefaultHybridAlpha { get; set; } = 0.5;
    public bool EnableGraph { get; set; } = false;
    public string? GraphExtractionModel { get; set; }
    public int MaxGraphHops { get; set; } = 1;
    public int MaxFactHits { get; set; } = 8;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Notebook Notebook { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
