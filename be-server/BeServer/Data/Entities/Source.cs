namespace BeServer.Data.Entities;

public class Source
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string NotebookId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? FilePath { get; set; }
    public string? MimeType { get; set; }
    public string? OriginalContentType { get; set; }
    public string? DetectedMimeType { get; set; }
    public long? FileSizeBytes { get; set; }
    public string Status { get; set; } = "uploaded";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Notebook Notebook { get; set; } = null!;
}
