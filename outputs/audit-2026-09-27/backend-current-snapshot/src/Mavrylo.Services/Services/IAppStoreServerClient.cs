namespace Mavrylo.Services;

public interface IAppStoreServerClient
{
    bool IsLocalVerifyEnabled { get; }
    bool IsServerApiConfigured { get; }

    AppStoreServerClient.VerifiedTransaction VerifyTransaction(string jwsTransaction, string? deviceUuid);
    AppStoreServerClient.VerifiedTransaction VerifyTransactionForClaim(string jwsTransaction, string? deviceUuid)
        => VerifyTransaction(jwsTransaction, deviceUuid);
    Task<AppStoreServerClient.SubscriptionStatusesResult> RefreshSubscriptionForClaimAsync(string originalTransactionId, string? deviceUuid, CancellationToken ct)
        => RefreshSubscriptionAsync(originalTransactionId, deviceUuid, ct);

    Task<AppStoreServerClient.VerifiedTransaction> GetTransactionInfoAsync(
        string transactionId,
        string? deviceUuid = null,
        CancellationToken ct = default);

    Task<AppStoreServerClient.SubscriptionStatusesResult> GetAllSubscriptionStatusesAsync(
        string originalTransactionId,
        string? deviceUuid = null,
        CancellationToken ct = default);

    Task<AppStoreServerClient.SubscriptionStatusesResult> RefreshSubscriptionAsync(
        string originalTransactionId,
        string? deviceUuid = null,
        CancellationToken ct = default);

    AppleJws.DecodeResult DecodeNotification(string signedPayload);

    void ApplyRenewalInfo(Models.SubscriptionEntity sub, string signedRenewalInfo);
}
