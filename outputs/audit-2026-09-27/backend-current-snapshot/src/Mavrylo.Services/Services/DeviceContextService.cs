using Mavrylo.Data;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

/// <summary>
/// Resolves the authenticated App Attest key id to the server-owned device row and canonical
/// entitlement. Authorization must use this state, not the device JWT's stale UI hint claims.
/// </summary>
public sealed class DeviceContextService(AppDbContext db, EntitlementService entitlements, IHttpContextAccessor? accessor = null)
{
    public sealed record DeviceContext(
        string KeyId,
        DeviceEntity Device,
        SubscriptionEntity? Subscription,
        EntitlementService.Entitlement Entitlement)
    {
        public string? AccountId { get; init; }
    }

    public async Task<DeviceContext?> ResolveAsync(string keyId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            return null;

        var device = await db.Devices.FirstOrDefaultAsync(d => d.KeyId == keyId, ct);
        if (device is null)
            return null;

        var sub = await entitlements.FindForDeviceAsync(device.DeviceUuid, ct);
        var (ent, account) = await ResolveEntitlementAsync(sub, ct);
        if (sub?.ClaimedAt != null && !device.RequiresAccountSubscription)
            await MarkClaimedPurchaseAsync(keyId, ct);
        if (sub == null && device.RequiresAccountSubscription && account == null)
            ent = ent with { Status = "account_required" };
        return new DeviceContext(keyId, device, sub, ent) { AccountId = account };
    }

    public async Task MarkClaimedPurchaseAsync(string keyId, CancellationToken ct)
    {
        await db.Devices.Where(x => x.KeyId == keyId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RequiresAccountSubscription, true), ct);
        var tracked = db.Devices.Local.FirstOrDefault(x => x.KeyId == keyId);
        if (tracked != null) tracked.RequiresAccountSubscription = true;
    }

    public async Task<(EntitlementService.Entitlement Entitlement, string? AccountId)> ResolveEntitlementAsync(SubscriptionEntity? sub, CancellationToken ct)
    {
        var http = accessor?.HttpContext;
        if (http != null && http.Request.Headers.TryGetValue("X-Account-Authorization", out var header))
        {
            var account = await http.RequestServices.GetRequiredService<SharedAccountAuthentication>().AccountAsync(header.ToString(), ct);
            if (account != null)
            {
                var shared = await http.RequestServices.GetRequiredService<AccountEntitlementService>().GetAsync(account, ct);
                if (AccountEntitlementService.IsActive(shared.Status) || sub?.OwnerAccountId == account)
                    return (new(shared.Status, shared.ProductId, shared.ExpiresAt, shared.IsTrial, shared.AutoRenew, shared.WasEverPaid), account);
            }
        }
        return (entitlements.ToDeviceEntitlement(sub), null);
    }
}
