using System.Text.Json;
using Mavrylo.Dtos;
using Mavrylo.Models;

namespace Mavrylo.Services;

/// <summary>App Store Server Notifications v2 processing. Moved verbatim from
/// AppStoreNotificationsController. Returns the HTTP status code (200/400/500); the controller
/// maps it to the original bodyless results.</summary>
public sealed class AppStoreNotificationService(
    IAppStoreServerClient appStore,
    EntitlementService entitlements,
    TimeProvider timeProvider,
    ILogger<AppStoreNotificationService> logger)
{
    public async Task<int> HandleAsync(AppStoreNotificationEnvelope envelope, CancellationToken ct)
    {
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.SignedPayload))
            return 400;

        var decoded = appStore.DecodeNotification(envelope.SignedPayload);
        if (!decoded.Ok)
        {
            logger.LogWarning("Rejected App Store notification: {Error}", decoded.Error);
            // 400 tells Apple to retry; for a malformed/unverifiable payload that's acceptable.
            return 400;
        }

        try
        {
            var root = decoded.Payload;
            var notificationType = GetString(root, "notificationType") ?? "";
            var subtype = GetString(root, "subtype");

            if (!root.TryGetProperty("data", out var data))
            {
                logger.LogInformation("Notification {Type}/{Subtype} had no data block.", notificationType, subtype);
                return 200;
            }

            var signedTx = GetString(data, "signedTransactionInfo");
            if (string.IsNullOrWhiteSpace(signedTx))
            {
                logger.LogInformation("Notification {Type} had no signedTransactionInfo.", notificationType);
                return 200;
            }

            var verified = appStore.VerifyTransaction(signedTx, deviceUuid: null);
            if (!verified.Ok || verified.Subscription is null)
            {
                logger.LogWarning("Notification {Type}: transaction projection failed: {Error}", notificationType, verified.Error);
                return 200; // don't ask Apple to retry a payload we can't project
            }

            var signedRenewalInfo = GetString(data, "signedRenewalInfo");
            if (!string.IsNullOrWhiteSpace(signedRenewalInfo))
                appStore.ApplyRenewalInfo(verified.Subscription, signedRenewalInfo!);

            var canonical = await appStore.RefreshSubscriptionAsync(verified.Subscription.OriginalTransactionId, verified.Subscription.DeviceUuid, ct);
            if (canonical.Ok && canonical.Subscription is not null)
            {
                verified = new AppStoreServerClient.VerifiedTransaction(
                    true,
                    canonical.Subscription,
                    verified.SignatureVerified,
                    null);
            }

            var subscription = verified.Subscription
                ?? throw new InvalidOperationException("verified notification lost its subscription payload");
            if (root.TryGetProperty("signedDate", out var signedDate) && signedDate.TryGetInt64(out var signedMilliseconds))
                subscription.LastAppleEventAt = DateTimeOffset.FromUnixTimeMilliseconds(signedMilliseconds).UtcDateTime;
            // A fresh canonical response wins over historical notification semantics.
            if (!canonical.Ok)
            {
                if (appStore.IsServerApiConfigured) return 503; // Apple retries; do not resurrect from stale payloads.
                ApplyNotificationSemantics(subscription, notificationType, subtype, timeProvider.GetUtcNow().UtcDateTime);
            }
            await entitlements.UpsertAsync(subscription, ct);

            logger.LogInformation("Processed App Store notification {Type}/{Subtype} for {Otid}.",
                notificationType, subtype, subscription.OriginalTransactionId);
            return 200;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing App Store notification.");
            return 500;
        }
    }

    /// <summary>Translate notification type/subtype into status/flags the state machine respects.</summary>
    private static void ApplyNotificationSemantics(SubscriptionEntity sub, string type, string? subtype, DateTime now)
    {
        switch (type)
        {
            case "REFUND":
            case "REVOKE":
                sub.RevokedAt = now;
                sub.Status = EntitlementService.Status.Revoked;
                break;
            case "DID_FAIL_TO_RENEW":
                // subtype GRACE_PERIOD means still in grace (treat as premium); otherwise it has lapsed.
                sub.Status = string.Equals(subtype, "GRACE_PERIOD", StringComparison.Ordinal)
                    ? EntitlementService.Status.Grace
                    : sub.Status;
                break;
            case "EXPIRED":
                // Leave status to ComputeStatus (expired_trial vs expired_paid by WasEverPaid).
                sub.AutoRenew = false;
                break;
            case "DID_CHANGE_RENEWAL_STATUS":
                sub.AutoRenew = !string.Equals(subtype, "AUTO_RENEW_DISABLED", StringComparison.Ordinal);
                break;
            // DID_RENEW / SUBSCRIBED / OFFER_REDEEMED: the fresh transaction fields already reflect truth.
        }
    }

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
