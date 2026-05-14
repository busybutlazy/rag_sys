namespace BeServer.Data.Entities;

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string NotebookId { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string ContentPreview { get; set; } = null!;
    public int Sequence { get; set; }
    public string? RequestId { get; set; }
    public string? SourcesJson { get; set; }
    public string? TracesJson { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ChatSession Session { get; set; } = null!;
    public User User { get; set; } = null!;
    public Notebook Notebook { get; set; } = null!;
}
