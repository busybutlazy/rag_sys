namespace BeServer.Data.Entities;

public class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string NotebookId { get; set; } = null!;
    public string? Title { get; set; }
    public string Content { get; set; } = string.Empty;
    public string NoteType { get; set; } = "human";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Notebook Notebook { get; set; } = null!;
}
