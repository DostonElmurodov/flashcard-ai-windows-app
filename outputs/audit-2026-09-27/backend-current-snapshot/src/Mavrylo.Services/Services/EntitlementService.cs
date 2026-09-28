using Mavrylo.Data;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

/// <summary>
/// Computes the canonical entitlement (the state machine) from a subscription record, and
/// persists/links subscriptions. The entitlement object is what the device-JWT's <c>ent</c> claim
/// and the /iap/* responses carry. See plan: entitlement state machine + Appendix M.
/// </summary>
public sealed class EntitlementService(AppDbContext db, TimeProvider timeProvider)
{
    public static class Status
    {
        public const string Free = "free";
        public const string Trial = "trial";
        public const string Premium = "premium";
        public const string Grace = "grace";
        public const string ExpiredTrial = "expired_trial";
        public const string ExpiredPaid = "expired_paid";
        public const string Revoked = "revoked";
    }

    /// <summary>Canonical entitlement returned to the client (snake_case on the wire).</summary>
    public sealed record Entitlement(
        string Status,
        string? ProductId,
        DateTime? ExpiresAt,
        bool IsTrial,
        bool AutoRenew,
        bool WasEverPaid)
    {
        public static Entitlement Free() => new(EntitlementService.Status.Free, null, null, false, false, false);
    }

    /// <summary>Upsert a verified subscription (keyed by OriginalTransactionId) and re-link the device.</summary>
    public async Task<SubscriptionEntity> UpsertAsync(SubscriptionEntity incoming, CancellationToken ct = default)
    {
        if (!db.Database.IsNpgsql()) return await UpsertCoreAsync(incoming, ct);
        var ownsTransaction = db.Database.CurrentTransaction == null;
        await using var tx = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({incoming.OriginalTransactionId}, 431))", ct);
        var result = await UpsertCoreAsync(incoming, ct);
        if (tx != null) await tx.CommitAsync(ct);
        return result;
    }

    private async Task<SubscriptionEntity> UpsertCoreAsync(SubscriptionEntity incoming, CancellationToken ct)
    {
        var existing = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.OriginalTransactionId == incoming.OriginalTransactionId, ct);

        if (existing is null)
        {
            incoming.Status = ComputeStatus(incoming);
            db.Subscriptions.Add(incoming);
            await db.SaveChangesAsync(ct);
            return incoming;
        }

        if (incoming.LastAppleEventAt != null && existing.LastAppleEventAt >= incoming.LastAppleEventAt)
            return existing;
        if (incoming.LastAppleEventAt != null) existing.LastAppleEventAt = incoming.LastAppleEventAt;
        existing.Status = incoming.Status;
        existing.ProductId = incoming.ProductId;
        existing.ExpiresAt = incoming.ExpiresAt;
        existing.IsTrial = incoming.IsTrial;
        existing.WasEverPaid = existing.WasEverPaid || incoming.WasEverPaid; // sticky
        existing.AutoRenew = incoming.AutoRenew;
        existing.Environment = incoming.Environment;
        existing.LastCheckedAt = timeProvider.GetUtcNow().UtcDateTime;
        if (incoming.RevokedAt is not null)
            existing.RevokedAt = incoming.RevokedAt;
        if (!string.IsNullOrWhiteSpace(incoming.DeviceUuid))
            existing.DeviceUuid = incoming.DeviceUuid; // re-link on restore to a new device
        existing.Status = ComputeStatus(existing);
        await db.SaveChangesAsync(ct);
        return existing;
    }

    /// <summary>Most relevant subscription for a device UUID (latest expiry wins).</summary>
    public async Task<SubscriptionEntity?> FindForDeviceAsync(string deviceUuid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceUuid))
            return null;
        return await db.Subscriptions
            .Where(s => s.DeviceUuid == deviceUuid)
            .OrderByDescending(s => s.ExpiresAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<SubscriptionEntity?> FindByOriginalTransactionAsync(string originalTransactionId, CancellationToken ct = default)
        => await db.Subscriptions.FirstOrDefaultAsync(s => s.OriginalTransactionId == originalTransactionId, ct);

    /// <summary>Project a subscription (or null) into the canonical entitlement, recomputing status.</summary>
    public Entitlement ToEntitlement(SubscriptionEntity? sub)
    {
        if (sub is null)
            return Entitlement.Free();
        var status = ComputeStatus(sub, timeProvider.GetUtcNow().UtcDateTime);
        return new Entitlement(status, sub.ProductId, sub.ExpiresAt, sub.IsTrial, sub.AutoRenew, sub.WasEverPaid);
    }

    public Entitlement ToDeviceEntitlement(SubscriptionEntity? sub)
        => sub?.ClaimedAt != null ? new Entitlement("account_required", sub.ProductId, sub.ExpiresAt, false, sub.AutoRenew, sub.WasEverPaid) : ToEntitlement(sub);

    /// <summary>
    /// The state machine. Active = not expired. Grace is preserved from <see cref="SubscriptionEntity.Status"/>
    /// (it is set by billing-retry notifications, not derivable from expiry alone).
    /// </summary>
    public static string ComputeStatus(SubscriptionEntity s, DateTime? now = null)
    {
        var ts = now ?? DateTime.UtcNow;

        if (s.RevokedAt is not null)
            return Status.Revoked;

        var active = s.ExpiresAt is null || s.ExpiresAt > ts;
        if (active)
        {
            // Honor an explicit grace status carried from a billing-retry notification.
            if (string.Equals(s.Status, Status.Grace, StringComparison.Ordinal))
                return Status.Grace;
            return s.IsTrial ? Status.Trial : Status.Premium;
        }

        // Expired: distinguish never-paid (trial only) vs previously paid.
        return s.WasEverPaid ? Status.ExpiredPaid : Status.ExpiredTrial;
    }
}
