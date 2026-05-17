namespace BeServer.Data.Entities;

public class IngestionJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SourceId { get; set; } = null!;
    public string NotebookId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string JobType { get; set; } = IngestionJobTypes.Ingest;
    public string Status { get; set; } = IngestionJobStatuses.Queued;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public string? LastError { get; set; }
    public DateTime AvailableAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Notebook Notebook { get; set; } = null!;
}

public static class IngestionJobTypes
{
    public const string Ingest = "ingest";
    public const string DeleteCleanup = "delete_cleanup";
}

public static class IngestionJobStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Retrying = "retrying";
    public const string Cancelled = "cancelled";
}
