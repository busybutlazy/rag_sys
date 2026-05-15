using BeServer.Data;
using BeServer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Services;

public class ChatMessageService(AppDbContext db)
{
    public async Task<List<object>> BuildContextMessagesAsync(string sessionId, string currentUserInput, Func<string, string> preview)
    {
        var recent = await db.ChatMessages
            .Where(m => m.SessionId == sessionId && (m.Role == "user" || m.Role == "assistant"))
            .OrderByDescending(m => m.Sequence)
            .Select(m => new { role = m.Role, content = m.ContentPreview })
            .Take(20)
            .ToListAsync();

        var selected = new List<object>();
        var completedTurns = 0;
        var hasAssistantForTurn = false;

        foreach (var message in recent)
        {
            selected.Add(message);
            if (message.role == "assistant")
            {
                hasAssistantForTurn = true;
            }
            else if (message.role == "user")
            {
                if (hasAssistantForTurn)
                    completedTurns++;
                hasAssistantForTurn = false;
                if (completedTurns >= 5)
                    break;
            }
        }

        selected.Reverse();
        return selected
            .Append(new { role = "user", content = preview(currentUserInput) })
            .ToList();
    }

    public ChatMessage CreateAssistantMessage(
        string sessionId,
        string userId,
        string notebookId,
        string requestId,
        int sequence,
        string content,
        string preview,
        string? sourcesJson,
        string? tracesJson,
        DateTime createdAt) =>
        new()
        {
            SessionId = sessionId,
            UserId = userId,
            NotebookId = notebookId,
            Role = "assistant",
            Content = content,
            ContentPreview = preview,
            Sequence = sequence,
            RequestId = requestId,
            SourcesJson = sourcesJson,
            TracesJson = tracesJson,
            CreatedAt = createdAt,
        };
}
