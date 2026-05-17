namespace BeServer.Data.Entities;

public class Notebook
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool Archived { get; set; } = false;
    public string? ActiveRetrievalVersionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public ICollection<Source> Sources { get; set; } = [];
    public ICollection<Note> Notes { get; set; } = [];
    public ICollection<ChatSession> ChatSessions { get; set; } = [];
    public ICollection<NotebookRetrievalVersion> RetrievalVersions { get; set; } = [];
}
