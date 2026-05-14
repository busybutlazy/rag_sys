namespace BeServer.Data.Entities;

public class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string NotebookId { get; set; } = null!;
    public string? Title { get; set; }
    public string Mode { get; set; } = "chat";
    public string? SessionStateJson { get; set; }
    public string? ActiveTaskId { get; set; }
    public bool Archived { get; set; } = false;
    public DateTime? LastMessageAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Notebook Notebook { get; set; } = null!;
    public ICollection<ChatMessage> Messages { get; set; } = [];
    public ICollection<ChatRequest> Requests { get; set; } = [];
    public ICollection<SessionTask> Tasks { get; set; } = [];
}
