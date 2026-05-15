using BeServer.Data;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Services;

public class OwnershipService(AppDbContext db, CurrentUserAccessor currentUser)
{
    public Task<bool> NotebookExistsAsync(string notebookId) =>
        db.Notebooks.AnyAsync(n => n.Id == notebookId && n.UserId == currentUser.UserId && !n.Archived);

    public Task<bool> SessionExistsAsync(string notebookId, string sessionId) =>
        db.ChatSessions.AnyAsync(s =>
            s.Id == sessionId &&
            s.UserId == currentUser.UserId &&
            s.NotebookId == notebookId &&
            !s.Archived);

    public Task<bool> SourceExistsAsync(string notebookId, string sourceId) =>
        db.Sources.AnyAsync(s =>
            s.Id == sourceId &&
            s.UserId == currentUser.UserId &&
            s.NotebookId == notebookId);
}
