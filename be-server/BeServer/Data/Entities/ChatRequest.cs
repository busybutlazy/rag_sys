namespace BeServer.Data.Entities;

public class ChatRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = null!;
    public string? UserMessageId { get; set; }
    public string? AssistantMessageId { get; set; }
    public string Mode { get; set; } = "chat";
    public string Model { get; set; } = "gpt-4o-mini";
    public string Status { get; set; } = ChatRequestStatuses.Running;
    public string? ContextSnapshotJson { get; set; }
    public string? Error { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int? DurationMs { get; set; }

    public ChatSession Session { get; set; } = null!;
    public ICollection<RequestLog> Logs { get; set; } = [];
}
