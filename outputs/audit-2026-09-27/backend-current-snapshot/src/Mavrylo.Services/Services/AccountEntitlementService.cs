using Mavrylo.Data;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

public sealed record AccountEntitlement(string Status, string? ProductId, DateTime? ExpiresAt,
    bool IsTrial, bool AutoRenew, bool WasEverPaid, string? Source, DateTime CheckedAt);

public sealed class AccountEntitlementService(AppDbContext db, EntitlementService entitlements,
    IAppStoreServerClient apple, SubscriptionOwnershipService ownership, TimeProvider clock, IHostEnvironment environment)
{
    public static bool IsActive(string status) => status is "trial" or "premium" or "grace";
    private static int Rank(string status) => status switch
    {
        "premium" => 6, "grace" => 5, "trial" => 4, "expired_paid" => 3,
        "revoked" => 2, "expired_trial" => 1, _ => 0
    };

    public async Task<AccountEntitlement> GetAsync(string accountId, CancellationToken ct = default)
    {
        var rows = await db.Subscriptions.AsNoTracking().Where(x => x.OwnerAccountId == accountId && x.ClaimedAt != null).ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (!apple.IsServerApiConfigured || (row.LastCheckedAt > now.AddMinutes(-30) && row.ExpiresAt > now.AddDays(1))) continue;
            var refreshed = await apple.RefreshSubscriptionForClaimAsync(row.OriginalTransactionId, row.DeviceUuid, ct);
            // Match the device resolver's outage policy: never extend cached access on failure.
            if (!refreshed.Ok || refreshed.Subscription == null || refreshed.Subscription.OriginalTransactionId != row.OriginalTransactionId) continue;
            if (!environment.IsDevelopment() && refreshed.Subscription.Environment != "Production") continue;
            refreshed.Subscription.DeviceUuid = row.DeviceUuid;
            rows[index] = await entitlements.UpsertAsync(refreshed.Subscription, ct);
        }
        var selected = rows.Where(x => environment.IsDevelopment() || x.Environment == "Production")
            .OrderByDescending(x => Rank(entitlements.ToEntitlement(x).Status))
            .ThenByDescending(x => x.ExpiresAt).FirstOrDefault();
        var ent = entitlements.ToEntitlement(selected);
        var wasEverPaid = rows.Any(x => x.WasEverPaid);
        if (!IsActive(ent.Status) && wasEverPaid) ent = ent with { Status = "expired_paid" };
        return new(ent.Status, ent.ProductId, ent.ExpiresAt, ent.IsTrial, ent.AutoRenew,
            wasEverPaid, selected == null ? null : "apple", selected?.LastCheckedAt ?? now);
    }

    public async Task<(int Status, object Body)> ClaimAsync(string accountId, string deviceKeyId, string jws, CancellationToken ct)
    {
        var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(x => x.KeyId == deviceKeyId, ct);
        if (device == null) return (401, new { code = "invalid_device", error = "Unknown device." });
        var proof = apple.VerifyTransactionForClaim(jws, device.DeviceUuid);
        if (!proof.Ok || !proof.SignatureVerified || proof.Subscription == null)
            return (400, new { code = "invalid_transaction", error = "Apple transaction verification failed." });
        var canonical = await apple.RefreshSubscriptionForClaimAsync(proof.Subscription.OriginalTransactionId, device.DeviceUuid, ct);
        if (!canonical.Ok || canonical.Subscription == null)
            return (503, new { code = "apple_unavailable", error = "Apple subscription status could not be refreshed." });
        var sub = canonical.Subscription;
        if (sub.OriginalTransactionId != proof.Subscription.OriginalTransactionId
            || (!environment.IsDevelopment() && sub.Environment != "Production")
            || sub.ExpiresAt == null || !IsActive(entitlements.ToEntitlement(sub).Status))
            return (402, new { code = "subscription_inactive", error = "An active verified subscription is required." });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Lock the user first, matching deletion; serialize all upserts for this Apple identity.
        if (db.Database.IsNpgsql())
        {
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {accountId} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (user == null) return (401, new { code = "invalid_account" });
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({sub.OriginalTransactionId}, 431))", ct);
        }
        sub.DeviceUuid = device.DeviceUuid;
        await entitlements.UpsertAsync(sub, ct);
        if (!await ownership.TryClaimAsync(sub.OriginalTransactionId, accountId, ct))
            return (409, new { code = "subscription_already_linked", error = "This subscription is already linked to an account." });
        await db.Devices.Where(x => x.KeyId == deviceKeyId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RequiresAccountSubscription, true), ct);
        await tx.CommitAsync(ct);
        return (200, await GetAsync(accountId, ct));
    }
}
