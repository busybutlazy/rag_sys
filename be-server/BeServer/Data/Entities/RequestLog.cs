namespace BeServer.Data.Entities;

public class RequestLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? ChatRequestId { get; set; }
    public string? SessionId { get; set; }
    public string Direction { get; set; } = null!;
    public string Service { get; set; } = null!;
    public string Operation { get; set; } = null!;
    public string? Method { get; set; }
    public string? Url { get; set; }
    public string? RequestJson { get; set; }
    public string? ResponseJson { get; set; }
    public int? StatusCode { get; set; }
    public int? DurationMs { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ChatRequest? ChatRequest { get; set; }
    public ChatSession? Session { get; set; }
}
