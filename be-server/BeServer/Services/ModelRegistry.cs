namespace BeServer.Services;

public class ModelRegistry(IConfiguration config)
{
    private static readonly string[] DefaultAllowed = ["gpt-4o-mini"];

    public string ChatDefault    => config["Models:ChatDefault"]    ?? "gpt-4o-mini";
    public string AgentDefault   => config["Models:AgentDefault"]   ?? "gpt-4o-mini";
    public string SummaryDefault => config["Models:SummaryDefault"] ?? "gpt-4o-mini";

    public IReadOnlyList<string> AllowedModels =>
        (config["Models:Allowed"] ?? string.Join(',', DefaultAllowed))
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string Resolve(string? requested, string @default) =>
        string.IsNullOrWhiteSpace(requested) || !AllowedModels.Contains(requested, StringComparer.Ordinal)
            ? @default
            : requested;
}
