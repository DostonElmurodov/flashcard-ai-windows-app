using Mavrylo.Models;
using Mavrylo.Services;

namespace Mavrylo.Tests.TestSupport;

internal sealed class FakeAppStoreServerClient : IAppStoreServerClient
{
    public bool IsLocalVerifyEnabled { get; set; } = true;
    public bool IsServerApiConfigured { get; set; } = true;
    public AppStoreServerClient.VerifiedTransaction VerifyTransactionResult { get; set; } =
        AppStoreServerClient.VerifiedTransaction.Fail("not configured");
    public AppStoreServerClient.SubscriptionStatusesResult SubscriptionStatusesResult { get; set; } =
        AppStoreServerClient.SubscriptionStatusesResult.Fail("not configured");
    public AppleJws.DecodeResult DecodeNotificationResult { get; set; } =
        AppleJws.DecodeResult.Fail("not configured");

    public AppStoreServerClient.VerifiedTransaction VerifyTransaction(string jwsTransaction, string? deviceUuid) =>
        VerifyTransactionResult;

    public Task<AppStoreServerClient.VerifiedTransaction> GetTransactionInfoAsync(
        string transactionId,
        string? deviceUuid = null,
        CancellationToken ct = default) =>
        Task.FromResult(VerifyTransactionResult);

    public Task<AppStoreServerClient.SubscriptionStatusesResult> GetAllSubscriptionStatusesAsync(
        string originalTransactionId,
        string? deviceUuid = null,
        CancellationToken ct = default) =>
        Task.FromResult(SubscriptionStatusesResult);

    public Task<AppStoreServerClient.SubscriptionStatusesResult> RefreshSubscriptionAsync(
        string originalTransactionId,
        string? deviceUuid = null,
        CancellationToken ct = default) =>
        Task.FromResult(SubscriptionStatusesResult);

    public AppleJws.DecodeResult DecodeNotification(string signedPayload) => DecodeNotificationResult;

    public void ApplyRenewalInfo(SubscriptionEntity sub, string signedRenewalInfo) { }
}
