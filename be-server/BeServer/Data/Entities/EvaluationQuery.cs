namespace BeServer.Data.Entities;

public class EvaluationQuery
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DatasetId { get; set; } = null!;
    public string QueryText { get; set; } = null!;
    public string? ExpectedAnswerNotes { get; set; }
    public string? GoldSourceNotes { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public EvaluationDataset Dataset { get; set; } = null!;
}
