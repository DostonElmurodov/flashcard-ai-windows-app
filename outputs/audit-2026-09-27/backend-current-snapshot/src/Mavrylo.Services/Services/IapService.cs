using Mavrylo.Dtos;
using Mavrylo.Models;

namespace Mavrylo.Services;

/// <summary>StoreKit verify + device-JWT (re)issuance. Orchestration moved verbatim from
/// IapController. Returns (Status, Body); the controller keeps KeyId() + its attributes and maps
/// to the original 400/401/200 results.</summary>
public sealed class IapService(
    IAppStoreServerClient appStore,
    EntitlementService entitlements,
    DeviceContextService deviceContext,
    JwtTokenService tokens,
    TimeProvider timeProvider,
    ILogger<IapService> logger)
{
    public sealed record IapResult(int Status, object? Body);
    private static IapResult Success(object body) => new(200, body);
    private static IapResult Code(int code, object body) => new(code, body);

    public async Task<IapResult> VerifyAsync(string? keyId, IapVerifyRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.JwsTransaction))
            return Code(400, new { error = "jws_transaction is required" });

        if (keyId is null)
            return Code(401, new { error = "invalid device token" });

        var context = await deviceContext.ResolveAsync(keyId, ct);
        if (context is null)
            return Code(401, new { error = "unknown device" });
        var deviceUuid = context.Device.DeviceUuid;

        var verified = appStore.VerifyTransaction(request.JwsTransaction, deviceUuid);
        if (!verified.Ok || verified.Subscription is null)
        {
            logger.LogWarning("iap/verify rejected for {KeyId}: {Error}", keyId, verified.Error);
            return Code(400, new { error = verified.Error ?? "transaction verification failed" });
        }

        var tokenDeviceUuid = verified.Subscription.DeviceUuid;
        var requiresRelink = !string.Equals(tokenDeviceUuid, deviceUuid, StringComparison.Ordinal);
        var canonical = await appStore.RefreshSubscriptionAsync(verified.Subscription.OriginalTransactionId, deviceUuid, ct);
        if (canonical.Ok && canonical.Subscription is not null)
        {
            canonical.Subscription.DeviceUuid = deviceUuid;
            verified = new AppStoreServerClient.VerifiedTransaction(
                true,
                canonical.Subscription,
                verified.SignatureVerified,
                null);
        }
        else if (requiresRelink || appStore.IsServerApiConfigured || !appStore.IsLocalVerifyEnabled)
        {
            logger.LogWarning("iap/verify could not refresh Apple canonical status for {Otid}: {Error}",
                verified.Subscription.OriginalTransactionId, canonical.Error);
            return Code(400, new { error = canonical.Error ?? "Apple subscription refresh failed" });
        }

        var subscription = verified.Subscription
            ?? throw new InvalidOperationException("verified transaction lost its subscription payload");
        subscription.DeviceUuid = deviceUuid;
        var sub = await entitlements.UpsertAsync(subscription, ct);
        if (sub.ClaimedAt != null) await deviceContext.MarkClaimedPurchaseAsync(keyId, ct);
        var (ent, accountId) = await deviceContext.ResolveEntitlementAsync(sub, ct);

        var (token, expiresAt) = tokens.CreateDeviceToken(keyId, ent.Status, sub.OriginalTransactionId);
        return Success(new IapVerifyResponse(ToDto(ent, accountId, sub), token, SecondsUntil(expiresAt)));
    }

    public async Task<IapResult> TokenAsync(string? keyId, CancellationToken ct)
    {
        if (keyId is null)
            return Code(401, new { error = "invalid device token" });

        var context = await deviceContext.ResolveAsync(keyId, ct);
        if (context is null)
            return Code(401, new { error = "unknown device" });

        var sub = await RefreshIfNeededAsync(context.Subscription, context.Device.DeviceUuid, ct);
        var (ent, accountId) = await deviceContext.ResolveEntitlementAsync(sub, ct);
        if (sub == null && context.Device.RequiresAccountSubscription && !AccountEntitlementService.IsActive(ent.Status))
            ent = ent with { Status = "account_required" };

        var (token, expiresAt) = tokens.CreateDeviceToken(keyId, ent.Status, sub?.OriginalTransactionId);
        return Success(new IapTokenResponse(token, SecondsUntil(expiresAt), ToDto(ent, accountId, sub)));
    }

    public async Task<IapResult> EntitlementAsync(string? keyId, CancellationToken ct)
    {
        if (keyId is null)
            return Code(401, new { error = "invalid device token" });

        var context = await deviceContext.ResolveAsync(keyId, ct);
        if (context is null)
            return Code(401, new { error = "unknown device" });

        var sub = await RefreshIfNeededAsync(context.Subscription, context.Device.DeviceUuid, ct);
        var (ent, accountId) = await deviceContext.ResolveEntitlementAsync(sub, ct);
        if (sub == null && context.Device.RequiresAccountSubscription && !AccountEntitlementService.IsActive(ent.Status))
            ent = ent with { Status = "account_required" };
        return Success(new IapEntitlementResponse(ToDto(ent, accountId, sub)));
    }

    private async Task<SubscriptionEntity?> RefreshIfNeededAsync(SubscriptionEntity? sub, string? deviceUuid, CancellationToken ct)
    {
        if (sub is null || !appStore.IsServerApiConfigured || !NeedsRefresh(sub))
            return sub;

        var refreshed = await appStore.RefreshSubscriptionAsync(sub.OriginalTransactionId, deviceUuid, ct);
        if (!refreshed.Ok || refreshed.Subscription is null)
        {
            logger.LogWarning("Apple subscription refresh failed for {Otid}: {Error}", sub.OriginalTransactionId, refreshed.Error);
            return sub;
        }

        refreshed.Subscription.DeviceUuid = deviceUuid ?? refreshed.Subscription.DeviceUuid;
        return await entitlements.UpsertAsync(refreshed.Subscription, ct);
    }

    private bool NeedsRefresh(SubscriptionEntity sub)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (sub.LastCheckedAt <= now.AddMinutes(-30))
            return true;
        if (sub.ExpiresAt is not null && sub.ExpiresAt <= now.AddDays(1))
            return true;
        return false;
    }

    private int SecondsUntil(DateTime utc)
        => Math.Max(0, (int)(utc - timeProvider.GetUtcNow().UtcDateTime).TotalSeconds);

    private static EntitlementDto ToDto(EntitlementService.Entitlement e, string? accountId, SubscriptionEntity? purchase)
        => new(e.Status, e.ProductId, e.ExpiresAt?.ToUniversalTime().ToString("O"), e.IsTrial, e.AutoRenew, e.WasEverPaid,
            accountId == null ? "device" : "account", purchase?.ClaimedAt != null);
}
