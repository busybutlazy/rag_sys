using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Content;

[ApiController]
[Route("api/notebooks/{notebookId}/chat-sessions")]
[Authorize]
public class ChatSessionsController(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    CurrentUserAccessor currentUser,
    OwnershipService ownership,
    ChatMessageService chatMessages,
    ModelRegistry modelRegistry,
    ILogger<ChatSessionsController> logger) : ControllerBase
{
    private const int PreviewLength = 150;
    private const int MaxLogJsonChars = 20000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private string UserId => currentUser.UserId;

    [HttpGet]
    public async Task<IActionResult> List(string notebookId)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");

        var sessions = await db.ChatSessions
            .Where(s => s.UserId == UserId && s.NotebookId == notebookId && !s.Archived)
            .OrderByDescending(s => s.LastMessageAt ?? s.UpdatedAt)
            .Select(s => new
            {
                s.Id,
                s.Title,
                s.Mode,
                s.ActiveTaskId,
                s.LastMessageAt,
                s.CreatedAt,
                s.UpdatedAt,
                MessageCount = db.ChatMessages.Count(m => m.SessionId == s.Id),
            })
            .ToListAsync();

        return Ok(sessions);
    }

    [HttpPost]
    public async Task<IActionResult> Create(string notebookId, [FromBody] CreateChatSessionRequest req)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");

        var now = DateTime.UtcNow;
        var session = new ChatSession
        {
            UserId = UserId,
            NotebookId = notebookId,
            Title = string.IsNullOrWhiteSpace(req.Title) ? "New chat" : req.Title.Trim(),
            Mode = NormalizeMode(req.Mode),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Messages), new { notebookId, sessionId = session.Id }, new
        {
            session.Id,
            session.Title,
            session.Mode,
            session.CreatedAt,
            session.UpdatedAt,
            MessageCount = 0,
        });
    }

    [HttpGet("{sessionId}/messages")]
    public async Task<IActionResult> Messages(string notebookId, string sessionId)
    {
        if (!await ownership.SessionExistsAsync(notebookId, sessionId))
            return ApiErrors.NotFound(this, "session.not_found", "Chat session not found");

        var rows = await db.ChatMessages
            .Where(m => m.SessionId == sessionId && m.UserId == UserId && m.NotebookId == notebookId)
            .OrderBy(m => m.Sequence)
            .Select(m => new
            {
                m.Id,
                m.Role,
                m.Content,
                m.ContentPreview,
                m.Sequence,
                m.RequestId,
                m.SourcesJson,
                m.TracesJson,
                m.MetadataJson,
                m.CreatedAt,
            })
            .ToListAsync();

        return Ok(rows.Select(m => new
        {
            m.Id,
            m.Role,
            m.Content,
            m.ContentPreview,
            m.Sequence,
            m.RequestId,
            Sources = FromJsonElement(m.SourcesJson),
            Traces = FromJsonElement(m.TracesJson),
            Metadata = FromJsonElement(m.MetadataJson),
            m.CreatedAt,
        }));
    }

    [HttpGet("{sessionId}/tasks")]
    public async Task<IActionResult> Tasks(string notebookId, string sessionId)
    {
        if (!await ownership.SessionExistsAsync(notebookId, sessionId))
            return ApiErrors.NotFound(this, "session.not_found", "Chat session not found");

        var rows = await db.SessionTasks
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.SortOrder)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.Summary,
                t.Status,
                t.SortOrder,
                t.StateJson,
                t.CreatedAt,
                t.UpdatedAt,
                t.CompletedAt,
            })
            .ToListAsync();

        return Ok(rows.Select(t => new
        {
            t.Id,
            t.Title,
            t.Summary,
            t.Status,
            t.SortOrder,
            State = FromJsonElement(t.StateJson),
            t.CreatedAt,
            t.UpdatedAt,
            t.CompletedAt,
        }));
    }

    [HttpPost("{sessionId}/runs")]
    public async Task Run(string notebookId, string sessionId, [FromBody] ChatRunRequest req)
    {
        var session = await db.ChatSessions.FirstOrDefaultAsync(
            s => s.Id == sessionId && s.UserId == UserId && s.NotebookId == notebookId && !s.Archived);
        if (session is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (string.IsNullOrWhiteSpace(req.Content))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { error = "content is required" });
            return;
        }

        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.ContentType = "text/event-stream";

        var now = DateTime.UtcNow;
        var mode = NormalizeMode(req.Mode ?? session.Mode);
        var model = modelRegistry.Resolve(
            req.Model,
            mode == "agent" ? modelRegistry.AgentDefault : modelRegistry.ChatDefault);
        var nextSequence = (await db.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .MaxAsync(m => (int?)m.Sequence) ?? 0) + 1;

        var contextMessages = await chatMessages.BuildContextMessagesAsync(sessionId, req.Content, text => Preview(text));
        var requestEntity = new ChatRequest
        {
            SessionId = sessionId,
            Mode = mode,
            Model = model,
            Status = ChatRequestStatuses.Running,
            ContextSnapshotJson = ToLimitedJson(contextMessages),
            StartedAt = now,
        };
        var userMessage = new ChatMessage
        {
            SessionId = sessionId,
            UserId = UserId,
            NotebookId = notebookId,
            Role = "user",
            Content = req.Content.Trim(),
            ContentPreview = Preview(req.Content),
            Sequence = nextSequence,
            RequestId = requestEntity.Id,
            CreatedAt = now,
        };
        requestEntity.UserMessageId = userMessage.Id;

        db.ChatRequests.Add(requestEntity);
        db.ChatMessages.Add(userMessage);
        session.Mode = mode;
        session.LastMessageAt = now;
        session.UpdatedAt = now;
        if (string.IsNullOrWhiteSpace(session.Title) || session.Title == "New chat")
            session.Title = Preview(req.Content, 64);
        await db.SaveChangesAsync();

        var aiRequestBody = new
        {
            messages = contextMessages,
            model,
            notebook_id = notebookId,
            request_id = requestEntity.Id,
            session_id = sessionId,
        };
        var endpoint = mode == "agent" ? "/agent/run" : "/chat/completions";
        var aiBase = config["AI_SERVER_URL"] ?? "http://ai-server:8002";
        var sw = Stopwatch.StartNew();
        var requestLog = new RequestLog
        {
            ChatRequestId = requestEntity.Id,
            SessionId = sessionId,
            Direction = "outbound",
            Service = "ai-server",
            Operation = mode == "agent" ? "agent.run" : "chat.completions",
            Method = "POST",
            Url = $"{aiBase}{endpoint}",
            RequestJson = RequestLogSanitizer.Redact(ToLimitedJson(aiRequestBody)),
            CreatedAt = DateTime.UtcNow,
        };
        db.RequestLogs.Add(requestLog);
        await db.SaveChangesAsync();

        var assistant = new StringBuilder();
        JsonElement? sources = null;
        var traceEvents = new List<JsonElement>();
        string? runError = null;

        try
        {
            var client = httpClientFactory.CreateClient("ai-server");
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpReq.Content = new StringContent(JsonSerializer.Serialize(aiRequestBody, JsonOptions), Encoding.UTF8, "application/json");
            if (Request.Headers.Authorization.Count > 0)
                httpReq.Headers.TryAddWithoutValidation("Authorization", Request.Headers.Authorization.ToString());
            httpReq.Headers.TryAddWithoutValidation("X-Correlation-Id", HttpContext.TraceIdentifier);

            using var aiRes = await client.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
            requestLog.StatusCode = (int)aiRes.StatusCode;
            aiRes.EnsureSuccessStatusCode();

            await using var stream = await aiRes.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream && !HttpContext.RequestAborted.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(HttpContext.RequestAborted);
                if (line is null) break;
                if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

                var payload = line[6..];
                if (payload == "[DONE]") break;

                CaptureEvent(payload, assistant, ref sources, traceEvents, ref runError);
                await Response.WriteAsync($"{line}\n\n", HttpContext.RequestAborted);
                await Response.Body.FlushAsync(HttpContext.RequestAborted);

                if (runError is not null) break;
            }

            requestEntity.Status = runError is null ? ChatRequestStatuses.Completed : ChatRequestStatuses.Failed;
            requestEntity.Error = runError;
            requestLog.ResponseJson = RequestLogSanitizer.Redact(ToLimitedJson(new
            {
                assistant = Preview(assistant.ToString(), 1000),
                sources,
                traces = traceEvents,
                error = runError,
            }));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Chat run failed for request {RequestId}", requestEntity.Id);
            runError = ex.Message;
            requestEntity.Status = ChatRequestStatuses.Failed;
            requestEntity.Error = ex.Message;
            requestLog.Error = ex.Message;
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { error = ex.Message })}\n\n");
        }
        finally
        {
            sw.Stop();
            var completedAt = DateTime.UtcNow;
            requestEntity.CompletedAt = completedAt;
            requestEntity.DurationMs = (int)sw.ElapsedMilliseconds;
            requestLog.DurationMs = (int)sw.ElapsedMilliseconds;

            if (assistant.Length > 0 || runError is null)
            {
                var assistantMessage = chatMessages.CreateAssistantMessage(
                    sessionId,
                    UserId,
                    notebookId,
                    requestEntity.Id,
                    nextSequence + 1,
                    assistant.ToString(),
                    Preview(assistant.ToString()),
                    sources.HasValue ? ToLimitedJson(sources.Value) : null,
                    traceEvents.Count > 0 ? ToLimitedJson(traceEvents) : null,
                    completedAt);
                db.ChatMessages.Add(assistantMessage);
                requestEntity.AssistantMessageId = assistantMessage.Id;
            }

            session.LastMessageAt = completedAt;
            session.UpdatedAt = completedAt;
            await db.SaveChangesAsync();

            if (runError is null)
                await UpdateSessionState(session, requestEntity.Id, req.Content.Trim(), assistant.ToString());

            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync();
        }
    }

    private async Task UpdateSessionState(ChatSession session, string requestId, string userInput, string assistantResponse)
    {
        var aiBase = config["AI_SERVER_URL"] ?? "http://ai-server:8002";
        var body = new
        {
            request_id = requestId,
            user_id = session.UserId,
            notebook_id = session.NotebookId,
            session_id = session.Id,
            prev_session_state = FromJsonElement(session.SessionStateJson),
            user_input = userInput,
            assistant_response = assistantResponse,
        };
        var sw = Stopwatch.StartNew();
        var log = new RequestLog
        {
            ChatRequestId = requestId,
            SessionId = session.Id,
            Direction = "outbound",
            Service = "ai-server",
            Operation = "session-state.update",
            Method = "POST",
            Url = $"{aiBase}/session-state/update",
            RequestJson = RequestLogSanitizer.Redact(ToLimitedJson(body)),
            CreatedAt = DateTime.UtcNow,
        };
        db.RequestLogs.Add(log);

        try
        {
            var client = httpClientFactory.CreateClient("ai-server");
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/session-state/update");
            httpReq.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            var secret = string.IsNullOrWhiteSpace(config["AI_INTERNAL_SECRET"])
                ? config["INTERNAL_SECRET"]
                : config["AI_INTERNAL_SECRET"];
            if (!string.IsNullOrWhiteSpace(secret))
                httpReq.Headers.Add("X-Internal-Secret", secret);
            httpReq.Headers.TryAddWithoutValidation("X-Correlation-Id", HttpContext.TraceIdentifier);

            using var res = await client.SendAsync(httpReq);
            log.StatusCode = (int)res.StatusCode;
            var json = await res.Content.ReadAsStringAsync();
            log.ResponseJson = RequestLogSanitizer.Redact(LimitJson(json));
            res.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(json);
            var normalized = NormalizeSessionState(doc.RootElement);
            session.SessionStateJson = ToLimitedJson(normalized.State);
            await ProjectTasks(session, requestId, normalized.Tasks);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Session state update failed for request {RequestId}", requestId);
            log.Error = ex.Message;
        }
        finally
        {
            sw.Stop();
            log.DurationMs = (int)sw.ElapsedMilliseconds;
            await db.SaveChangesAsync();
        }
    }

    private async Task ProjectTasks(ChatSession session, string requestId, List<SessionTaskProjection> tasks)
    {
        var existing = await db.SessionTasks.Where(t => t.SessionId == session.Id).ToDictionaryAsync(t => t.Id);
        var now = DateTime.UtcNow;
        var seen = new HashSet<string>();

        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            if (!existing.TryGetValue(task.Id, out var entity))
            {
                entity = new SessionTask
                {
                    Id = task.Id,
                    SessionId = session.Id,
                    CreatedFromRequestId = requestId,
                    CreatedAt = now,
                };
                db.SessionTasks.Add(entity);
            }

            entity.Title = task.Title;
            entity.Summary = task.Summary;
            entity.Status = task.Status;
            entity.SortOrder = i;
            entity.StateJson = ToLimitedJson(task.State);
            entity.UpdatedFromRequestId = requestId;
            entity.UpdatedAt = now;
            entity.CompletedAt = task.Status is SessionTaskStatuses.Done or SessionTaskStatuses.Cancelled ? entity.CompletedAt ?? now : null;
            seen.Add(task.Id);
        }

        foreach (var stale in existing.Values.Where(t => !seen.Contains(t.Id)))
        {
            stale.Status = SessionTaskStatuses.Cancelled;
            stale.UpdatedFromRequestId = requestId;
            stale.UpdatedAt = now;
            stale.CompletedAt ??= now;
        }

        var active = tasks.FirstOrDefault(t => t.Status == SessionTaskStatuses.Active);
        session.ActiveTaskId = active?.Id;
    }

    private static (Dictionary<string, object?> State, List<SessionTaskProjection> Tasks) NormalizeSessionState(JsonElement root)
    {
        var state = JsonSerializer.Deserialize<Dictionary<string, object?>>(root.GetRawText(), JsonOptions) ?? [];
        var tasks = new List<SessionTaskProjection>();
        if (root.TryGetProperty("topic_stack", out var stack) && stack.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in stack.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString()
                    : null;
                var title = item.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String
                    ? titleProp.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(title)) title = "Untitled task";
                if (string.IsNullOrWhiteSpace(id)) id = $"task-{Guid.NewGuid():N}";
                var status = item.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.String
                    ? statusProp.GetString()
                    : SessionTaskStatuses.Paused;
                if (status is not (SessionTaskStatuses.Active or SessionTaskStatuses.Paused or SessionTaskStatuses.Done or SessionTaskStatuses.Cancelled)) status = SessionTaskStatuses.Paused;
                var summary = item.TryGetProperty("summary", out var summaryProp) && summaryProp.ValueKind == JsonValueKind.String
                    ? summaryProp.GetString()
                    : null;
                tasks.Add(new SessionTaskProjection(id, title, summary, status, JsonSerializer.Deserialize<Dictionary<string, object?>>(item.GetRawText(), JsonOptions) ?? []));
            }
        }

        if (tasks.Count == 0)
            tasks.Add(new SessionTaskProjection($"task-{Guid.NewGuid():N}", "Conversation", null, SessionTaskStatuses.Active, []));

        var activeSeen = false;
        for (var i = 0; i < tasks.Count; i++)
        {
            if (tasks[i].Status == SessionTaskStatuses.Active)
            {
                if (!activeSeen) activeSeen = true;
                else tasks[i] = tasks[i] with { Status = SessionTaskStatuses.Paused };
            }
        }
        if (!activeSeen)
        {
            var idx = tasks.FindIndex(t => t.Status == SessionTaskStatuses.Paused);
            tasks[idx >= 0 ? idx : 0] = tasks[idx >= 0 ? idx : 0] with { Status = SessionTaskStatuses.Active };
        }

        state["topic_stack"] = tasks.Select(t =>
        {
            var item = new Dictionary<string, object?>(t.State)
            {
                ["id"] = t.Id,
                ["title"] = t.Title,
                ["summary"] = t.Summary,
                ["status"] = t.Status,
            };
            return item;
        }).ToList();
        return (state, tasks);
    }

    private static void CaptureEvent(
        string payload,
        StringBuilder assistant,
        ref JsonElement? sources,
        List<JsonElement> traceEvents,
        ref string? runError)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (root.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String)
            assistant.Append(token.GetString());
        if (root.TryGetProperty("sources", out var sourceProp))
            sources = sourceProp.Clone();
        if (root.TryGetProperty("trace", out var trace))
            traceEvents.Add(trace.Clone());
        if (root.TryGetProperty("tool_result", out var toolResult))
            traceEvents.Add(toolResult.Clone());
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            runError = error.GetString();
    }

    private static string NormalizeMode(string? mode) =>
        string.Equals(mode, "agent", StringComparison.OrdinalIgnoreCase) ? "agent" : "chat";

    private static string Preview(string value, int length = PreviewLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= length ? trimmed : trimmed[..length];
    }

    private static object? FromJsonElement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText(), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string ToLimitedJson(object? value) =>
        LimitJson(JsonSerializer.Serialize(value, JsonOptions));

    private static string LimitJson(string json) =>
        json.Length <= MaxLogJsonChars
            ? json
            : JsonSerializer.Serialize(new { truncated = true, preview = json[..MaxLogJsonChars] }, JsonOptions);
}

public record CreateChatSessionRequest(string? Title, string? Mode);
public record ChatRunRequest(string Content, string? Mode, string? Model);

internal record SessionTaskProjection(
    string Id,
    string Title,
    string? Summary,
    string Status,
    Dictionary<string, object?> State);
