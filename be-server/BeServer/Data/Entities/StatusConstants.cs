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
