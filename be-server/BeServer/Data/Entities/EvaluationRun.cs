namespace BeServer.Data.Entities;

public class EvaluationRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string NotebookId { get; set; } = null!;
    public string? DatasetId { get; set; }
    public string UserId { get; set; } = null!;
    public string RetrievalVersionAId { get; set; } = null!;
    public string RetrievalVersionBId { get; set; } = null!;
    public string SearchModesJson { get; set; } = "[]";
    public int TopK { get; set; } = 5;
    public double HybridAlpha { get; set; } = 0.5;
    public string Status { get; set; } = EvaluationRunStatuses.Queued;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Notebook Notebook { get; set; } = null!;
    public EvaluationDataset? Dataset { get; set; }
    public User User { get; set; } = null!;
    public ICollection<EvaluationResult> Results { get; set; } = [];
}

public static class EvaluationRunStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Failed = "failed";
    public const string Succeeded = "succeeded";
}
