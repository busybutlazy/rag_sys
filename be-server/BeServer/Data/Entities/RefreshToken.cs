namespace BeServer.Data.Entities;

public class RefreshToken
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string TokenHash { get; set; } = null!;
    public string FamilyId { get; set; } = Guid.NewGuid().ToString();
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedByIp { get; set; }
    public string? RevokedByIp { get; set; }

    public User User { get; set; } = null!;
}
