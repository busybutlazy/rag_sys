namespace BeServer.Data.Entities;

public class RetrievalPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int ChunkSize { get; set; }
    public int ChunkOverlap { get; set; }
    public string EmbeddingModel { get; set; } = null!;
    public int EmbeddingDimensions { get; set; }
    public string DefaultSearchMode { get; set; } = "hybrid";
    public int DefaultTopK { get; set; } = 5;
    public double DefaultHybridAlpha { get; set; } = 0.5;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
