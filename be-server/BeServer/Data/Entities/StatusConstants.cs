namespace BeServer.Data.Entities;

public static class SourceStatuses
{
    public const string Uploaded = "uploaded";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Retrying = "retrying";
    public const string Ingested = "ingested";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class ChatRequestStatuses
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class SessionTaskStatuses
{
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Done = "done";
    public const string Cancelled = "cancelled";
}

// Phase 19: tracks the outcome of the optional graph extraction step that
// runs after a successful ingest/reindex. Extraction failure must never
// fail the underlying ingestion -- vector/BM25 retrieval stays usable
// either way, this field is purely observational.
public static class GraphExtractionStatuses
{
    public const string Skipped = "skipped";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}
