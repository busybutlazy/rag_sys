namespace BeServer.Data.Entities;

public class ReindexJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string NotebookId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? SourceId { get; set; }
    public string Scope { get; set; } = ReindexJobScopes.Source;
    public string TargetRetrievalVersionId { get; set; } = null!;
    public string? PreviousRetrievalVersionId { get; set; }
    public string Status { get; set; } = ReindexJobStatuses.Queued;
    public int SourcesTotal { get; set; }
    public int SourcesSucceeded { get; set; }
    public int SourcesFailed { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public string? LastError { get; set; }
    public DateTime AvailableAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Notebook Notebook { get; set; } = null!;
    public User User { get; set; } = null!;
}

public static class ReindexJobScopes
{
    public const string Source = "source";
    public const string Notebook = "notebook";
}

public static class ReindexJobStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Retrying = "retrying";
    public const string Cancelled = "cancelled";
}
