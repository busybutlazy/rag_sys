using System.Text.Json;
using BeServer.Data;
using BeServer.Data.Entities;
using Microsoft.AspNetCore.Mvc;

namespace BeServer.Content;

[ApiController]
[Route("internal/request-logs")]
public class InternalRequestLogsController(AppDbContext db, IConfiguration config) : ControllerBase
{
    private const int MaxLogJsonChars = 20000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] InternalRequestLogRequest req)
    {
        var expected = config["INTERNAL_SECRET"];
        if (string.IsNullOrWhiteSpace(expected) || Request.Headers["X-Internal-Secret"].ToString() != expected)
            return Unauthorized();

        db.RequestLogs.Add(new RequestLog
        {
            ChatRequestId = BlankToNull(req.ChatRequestId),
            SessionId = BlankToNull(req.SessionId),
            Direction = string.IsNullOrWhiteSpace(req.Direction) ? "outbound" : req.Direction,
            Service = string.IsNullOrWhiteSpace(req.Service) ? "unknown" : req.Service,
            Operation = string.IsNullOrWhiteSpace(req.Operation) ? "unknown" : req.Operation,
            Method = BlankToNull(req.Method),
            Url = BlankToNull(req.Url),
            RequestJson = LimitJson(req.RequestJson),
            ResponseJson = LimitJson(req.ResponseJson),
            StatusCode = req.StatusCode,
            DurationMs = req.DurationMs,
            Error = BlankToNull(req.Error),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? LimitJson(JsonElement? value)
    {
        if (value is null) return null;
        var json = value.Value.GetRawText();
        return json.Length <= MaxLogJsonChars
            ? json
            : JsonSerializer.Serialize(new { truncated = true, preview = json[..MaxLogJsonChars] }, JsonOptions);
    }
}

public record InternalRequestLogRequest(
    string? ChatRequestId,
    string? SessionId,
    string? Direction,
    string? Service,
    string? Operation,
    string? Method,
    string? Url,
    JsonElement? RequestJson,
    JsonElement? ResponseJson,
    int? StatusCode,
    int? DurationMs,
    string? Error);
