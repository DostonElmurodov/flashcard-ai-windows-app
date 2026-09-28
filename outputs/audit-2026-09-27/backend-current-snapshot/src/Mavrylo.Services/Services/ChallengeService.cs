using System.Security.Cryptography;
using Mavrylo.Data;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

/// <summary>
/// Issues and consumes single-use App Attest challenges (nonces).
///
/// Two kinds:
///  - <b>bootstrap</b>: used once during attestation/registration. Not yet bound to a keyId
///    (the device has no registered key when it bootstraps).
///  - <b>assertion</b>: bound to a keyId + request path, used once per protected call.
///
/// Stored in the database with a short TTL (~2 min) and consumed atomically so a captured challenge
/// cannot be replayed.
/// </summary>
public class ChallengeService(AppDbContext db, TimeProvider timeProvider)
{
    public enum ChallengeKind { Bootstrap, Assertion }

    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);
    private const int NonceBytes = 32;

    public sealed record IssuedChallenge(string ChallengeId, byte[] Nonce);

    /// <summary>Issue a bootstrap challenge for attestation (not bound to a key yet).</summary>
    public Task<IssuedChallenge> IssueBootstrapAsync(CancellationToken ct = default)
        => IssueAsync(ChallengeKind.Bootstrap, keyId: null, requestPath: null, ct);

    /// <summary>Issue an assertion challenge bound to a registered keyId + the path it will be used on.</summary>
    public Task<IssuedChallenge> IssueAssertionAsync(string keyId, string requestPath, CancellationToken ct = default)
        => IssueAsync(ChallengeKind.Assertion, keyId, requestPath, ct);

    /// <summary>
    /// Atomically consume a challenge. Returns the nonce bytes on success, or null if the challenge
    /// is unknown, already used, expired, or does not match the expected kind/keyId/path binding.
    /// A consumed challenge is removed so it can never be reused (single-use).
    /// </summary>
    public async Task<byte[]?> TryConsumeAsync(
        string challengeId,
        ChallengeKind expectedKind,
        string? keyId = null,
        string? requestPath = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(challengeId))
            return null;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var kind = expectedKind.ToString();
        var row = await db.Challenges
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.Id == challengeId
                && c.Kind == kind
                && c.KeyId == keyId
                && c.RequestPath == requestPath
                && c.ConsumedAt == null
                && c.ExpiresAt > now, ct);
        if (row is null)
            return null;

        var updated = await db.Challenges
            .Where(c =>
                c.Id == challengeId
                && c.Kind == kind
                && c.KeyId == keyId
                && c.RequestPath == requestPath
                && c.ConsumedAt == null
                && c.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), ct);

        return updated == 1 ? row.Nonce : null;
    }

    private async Task<IssuedChallenge> IssueAsync(
        ChallengeKind kind,
        string? keyId,
        string? requestPath,
        CancellationToken ct)
    {
        var row = new ChallengeEntity
        {
            Nonce = RandomNumberGenerator.GetBytes(NonceBytes),
            Kind = kind.ToString(),
            KeyId = keyId,
            RequestPath = requestPath,
            ExpiresAt = timeProvider.GetUtcNow().UtcDateTime.Add(Ttl)
        };
        db.Challenges.Add(row);
        await db.SaveChangesAsync(ct);
        return new IssuedChallenge(row.Id, row.Nonce);
    }
}
