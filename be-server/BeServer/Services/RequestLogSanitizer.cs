using System.Text.Json;

namespace BeServer.Services;

public static class RequestLogSanitizer
{
    private static readonly string[] SensitiveKeys = ["content", "user_input", "assistant_response", "authorization"];

    public static string? Redact(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(RedactElement(doc.RootElement));
    }

    private static object? RedactElement(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => SensitiveKeys.Contains(property.Name, StringComparer.OrdinalIgnoreCase)
                    ? "[REDACTED]"
                    : RedactElement(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(RedactElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString(),
        };
}
