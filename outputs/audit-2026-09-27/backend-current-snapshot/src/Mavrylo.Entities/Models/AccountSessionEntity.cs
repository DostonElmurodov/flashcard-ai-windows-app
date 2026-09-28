namespace Mavrylo.Models;

public sealed class AccountSessionEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public sealed class AccountRefreshTokenEntity
{
    public string Hash { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTime? ConsumedAt { get; set; }
}
