namespace BeServer.Data.Entities;

public class SessionTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Summary { get; set; }
    public string Status { get; set; } = "active";
    public int SortOrder { get; set; }
    public string? StateJson { get; set; }
    public string? CreatedFromRequestId { get; set; }
    public string? UpdatedFromRequestId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public ChatSession Session { get; set; } = null!;
}
