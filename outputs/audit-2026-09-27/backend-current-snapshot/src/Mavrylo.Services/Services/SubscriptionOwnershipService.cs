using Mavrylo.Data;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

public sealed class SubscriptionOwnershipService(AppDbContext db, TimeProvider clock)
{
    // A single conditional UPDATE is the ownership arbitration point across all API processes.
    // Never turn a deletion tombstone back into an unclaimed purchase.
    public async Task<bool> TryClaimAsync(string transaction, string account, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var updated = await db.Subscriptions.Where(x => x.OriginalTransactionId == transaction && x.ClaimedAt == null && x.OwnerAccountId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnerAccountId, account).SetProperty(x => x.ClaimedAt, now), ct);
        return updated == 1 || await db.Subscriptions.AsNoTracking().AnyAsync(x => x.OriginalTransactionId == transaction && x.OwnerAccountId == account && x.ClaimedAt != null, ct);
    }

    public Task<int> TombstoneAsync(string account, CancellationToken ct = default)
        => db.Subscriptions.Where(x => x.OwnerAccountId == account)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnerAccountId, (string?)null), ct);
}
