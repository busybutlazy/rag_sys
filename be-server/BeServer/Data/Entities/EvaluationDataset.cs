namespace BeServer.Data.Entities;

public class EvaluationDataset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string NotebookId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Notebook Notebook { get; set; } = null!;
    public User User { get; set; } = null!;
    public ICollection<EvaluationQuery> Queries { get; set; } = [];
}
