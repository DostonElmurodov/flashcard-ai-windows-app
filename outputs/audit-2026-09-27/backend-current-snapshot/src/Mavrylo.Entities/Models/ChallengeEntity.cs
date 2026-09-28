namespace Mavrylo.Models;

/// <summary>
/// Single-use App Attest nonce persisted for replay protection across process restarts.
/// </summary>
public class ChallengeEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public byte[] Nonce { get; set; } = Array.Empty<byte>();
    public string Kind { get; set; } = "";
    public string? KeyId { get; set; }
    public string? RequestPath { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
