namespace BeServer.Services;

public class ModelRegistry(IConfiguration config)
{
    private static readonly string[] DefaultAllowed = ["gpt-4o-mini"];

    public string ChatDefault => config["Models:ChatDefault"] ?? "gpt-4o-mini";
    public string AgentDefault => config["Models:AgentDefault"] ?? "gpt-4o-mini";
    public string SummaryDefault => config["Models:SummaryDefault"] ?? "gpt-4o-mini";

    public IReadOnlyList<string> AllowedModels =>
        (config["Models:Allowed"] ?? string.Join(',', DefaultAllowed))
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string ResolvePreset(string? preset, string mode) =>
        preset switch
        {
            "agent_default" => AgentDefault,
            "summary_default" => SummaryDefault,
            "chat_default" or null or "" => mode == "agent" ? AgentDefault : ChatDefault,
            _ => mode == "agent" ? AgentDefault : ChatDefault,
        };

    public string Resolve(string? requested, string @default) =>
        string.IsNullOrWhiteSpace(requested) || !AllowedModels.Contains(requested, StringComparer.Ordinal)
            ? @default
            : requested;
}
