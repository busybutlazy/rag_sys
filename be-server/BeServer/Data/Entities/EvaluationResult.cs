namespace BeServer.Data.Entities;

public class EvaluationResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RunId { get; set; } = null!;
    public string? QueryId { get; set; }
    public string QueryTextSnapshot { get; set; } = null!;
    public string RetrievalVersionId { get; set; } = null!;
    public string Mode { get; set; } = null!;
    public int LatencyMs { get; set; }
    public int ResultCount { get; set; }
    public string ResultsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public EvaluationRun Run { get; set; } = null!;
    public EvaluationQuery? Query { get; set; }
}
