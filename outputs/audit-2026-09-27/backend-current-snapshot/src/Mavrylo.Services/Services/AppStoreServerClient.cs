using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mavrylo.Models;

namespace Mavrylo.Services;

/// <summary>
/// Talks to Apple about StoreKit transactions. Production uses App Store Server API calls signed
/// with a short-lived ES256 JWT. Development can still decode local StoreKitTest JWS payloads when
/// running under DEBUG + Development because those local certificates are not Apple trusted.
/// </summary>
public sealed class AppStoreServerClient(
    HttpClient http,
    IConfiguration config,
    IWebHostEnvironment env,
    ILogger<AppStoreServerClient> logger,
    TimeProvider timeProvider) : IAppStoreServerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed record VerifiedTransaction(
        bool Ok,
        SubscriptionEntity? Subscription,
        bool SignatureVerified,
        string? Error)
    {
        public static VerifiedTransaction Fail(string error) => new(false, null, false, error);
    }

    public sealed record SubscriptionStatusesResult(
        bool Ok,
        SubscriptionEntity? Subscription,
        string? Error)
    {
        public static SubscriptionStatusesResult Fail(string error) => new(false, null, error);
    }

    /// <summary>
    /// True only when DEBUG build AND Development environment — allows trusting StoreKitTest JWS
    /// without chain verification so the Simulator flow works. NEVER true in Release or non-Dev.
    /// </summary>
    public bool IsLocalVerifyEnabled
    {
        get
        {
#if DEBUG
            return env.IsDevelopment();
#else
            return env.IsDevelopment() && false;
#endif
        }
    }

    public bool IsServerApiConfigured => !string.IsNullOrWhiteSpace(Opt("IssuerId"))
        && !string.IsNullOrWhiteSpace(Opt("KeyId"))
        && !string.IsNullOrWhiteSpace(Opt("BundleId"))
        && (!string.IsNullOrWhiteSpace(Opt("PrivateKey")) || !string.IsNullOrWhiteSpace(Opt("PrivateKeyPath")));

    /// <summary>
    /// Verify a signed transaction JWS and project it into a subscription record. Does not persist.
    /// </summary>
    public VerifiedTransaction VerifyTransaction(string jwsTransaction, string? deviceUuid)
        => VerifyTransactionCore(jwsTransaction, deviceUuid, !IsLocalVerifyEnabled);

    public VerifiedTransaction VerifyTransactionForClaim(string jwsTransaction, string? deviceUuid)
        => VerifyTransactionCore(jwsTransaction, deviceUuid, true);

    private VerifiedTransaction VerifyTransactionCore(string jwsTransaction, string? deviceUuid, bool requireVerified)
    {
        var decoded = AppleJws.Decode(jwsTransaction, verifySignature: requireVerified);
        if (!decoded.Ok)
            return VerifiedTransaction.Fail(decoded.Error ?? "JWS decode failed");
        if (requireVerified && !decoded.SignatureVerified)
            return VerifiedTransaction.Fail("JWS signature not verified");

        try
        {
            var sub = ProjectTransaction(decoded.Payload, deviceUuid);
            return sub is null
                ? VerifiedTransaction.Fail("transaction missing originalTransactionId/productId")
                : new VerifiedTransaction(true, sub, decoded.SignatureVerified, null);
        }
        catch (InvalidOperationException ex)
        {
            return VerifiedTransaction.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to project StoreKit transaction.");
            return VerifiedTransaction.Fail("transaction projection error");
        }
    }

    /// <summary>Get Apple's canonical signed transaction info for any transaction id.</summary>
    public async Task<VerifiedTransaction> GetTransactionInfoAsync(string transactionId, string? deviceUuid = null, CancellationToken ct = default)
    {
        if (!IsServerApiConfigured)
            return VerifiedTransaction.Fail("App Store Server API is not configured");

        var json = await SendAppleGetAsync($"/inApps/v1/transactions/{Uri.EscapeDataString(transactionId)}", ct);
        if (json is null)
            return VerifiedTransaction.Fail("App Store Server API transaction request failed");

        if (!json.Value.TryGetProperty("signedTransactionInfo", out var signedTx) || signedTx.ValueKind != JsonValueKind.String)
            return VerifiedTransaction.Fail("Apple transaction response missing signedTransactionInfo");

        return VerifyTransaction(signedTx.GetString()!, deviceUuid);
    }

    /// <summary>Get and map all Apple subscription statuses for any transaction id.</summary>
    public async Task<SubscriptionStatusesResult> GetAllSubscriptionStatusesAsync(string originalTransactionId, string? deviceUuid = null, CancellationToken ct = default)
        => await GetSubscriptionStatusesCoreAsync(originalTransactionId, deviceUuid, false, ct);

    public Task<SubscriptionStatusesResult> RefreshSubscriptionForClaimAsync(string originalTransactionId, string? deviceUuid, CancellationToken ct)
        => GetSubscriptionStatusesCoreAsync(originalTransactionId, deviceUuid, true, ct);

    private async Task<SubscriptionStatusesResult> GetSubscriptionStatusesCoreAsync(string originalTransactionId, string? deviceUuid, bool claim, CancellationToken ct)
    {
        if (!IsServerApiConfigured)
            return SubscriptionStatusesResult.Fail("App Store Server API is not configured");

        var json = await SendAppleGetAsync($"/inApps/v1/subscriptions/{Uri.EscapeDataString(originalTransactionId)}", ct);
        if (json is null)
            return SubscriptionStatusesResult.Fail("App Store Server API subscription status request failed");

        var best = SelectBestLastTransaction(json.Value);
        if (best is null)
            return SubscriptionStatusesResult.Fail("Apple subscription status response had no lastTransactions");

        var verified = claim ? VerifyTransactionForClaim(best.Value.SignedTransactionInfo, deviceUuid) : VerifyTransaction(best.Value.SignedTransactionInfo, deviceUuid);
        if (!verified.Ok || verified.Subscription is null)
            return SubscriptionStatusesResult.Fail(verified.Error ?? "Apple subscription transaction verification failed");

        ApplyAppleStatus(verified.Subscription, best.Value.Status);
        if (!string.IsNullOrWhiteSpace(best.Value.SignedRenewalInfo))
            ApplyRenewalInfoCore(verified.Subscription, best.Value.SignedRenewalInfo!, claim || !IsLocalVerifyEnabled);

        return new SubscriptionStatusesResult(true, verified.Subscription, null);
    }

    /// <summary>Refresh the cached subscription from Apple's canonical status endpoint.</summary>
    public Task<SubscriptionStatusesResult> RefreshSubscriptionAsync(string originalTransactionId, string? deviceUuid = null, CancellationToken ct = default)
        => GetAllSubscriptionStatusesAsync(originalTransactionId, deviceUuid, ct);

    /// <summary>Decode a v2 notification's signedPayload (verified in prod, unverified in dev).</summary>
    public AppleJws.DecodeResult DecodeNotification(string signedPayload)
        => AppleJws.Decode(signedPayload, verifySignature: !IsLocalVerifyEnabled);

    public void ApplyRenewalInfo(SubscriptionEntity sub, string signedRenewalInfo)
        => ApplyRenewalInfoCore(sub, signedRenewalInfo, !IsLocalVerifyEnabled);

    private void ApplyRenewalInfoCore(SubscriptionEntity sub, string signedRenewalInfo, bool requireVerified)
    {
        var decoded = AppleJws.Decode(signedRenewalInfo, verifySignature: requireVerified);
        if (!decoded.Ok)
        {
            logger.LogWarning("Unable to decode signedRenewalInfo for {Otid}: {Error}", sub.OriginalTransactionId, decoded.Error);
            return;
        }

        var p = decoded.Payload;
        var autoRenewStatus = GetInt(p, "autoRenewStatus");
        if (autoRenewStatus is not null)
            sub.AutoRenew = autoRenewStatus == 1;

        var graceExpires = GetUnixMs(p, "gracePeriodExpiresDate");
        if (graceExpires is not null && graceExpires > timeProvider.GetUtcNow().UtcDateTime)
        {
            sub.ExpiresAt = graceExpires;
            sub.Status = EntitlementService.Status.Grace;
        }

        var productId = GetString(p, "productId") ?? GetString(p, "autoRenewProductId");
        if (!string.IsNullOrWhiteSpace(productId))
            sub.ProductId = productId!;
    }

    private async Task<JsonElement?> SendAppleGetAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(BaseUrl()), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateServerJwt());

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Apple App Store Server API GET {Path} returned {Status}: {Body}",
                path, (int)response.StatusCode, body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private string CreateServerJwt()
    {
        var keyId = RequiredOpt("KeyId");
        var issuerId = RequiredOpt("IssuerId");
        var bundleId = RequiredOpt("BundleId");
        var now = timeProvider.GetUtcNow();

        var header = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["kid"] = keyId,
            ["typ"] = "JWT"
        }, JsonOptions);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = issuerId,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds(),
            ["aud"] = "appstoreconnect-v1",
            ["bid"] = bundleId
        }, JsonOptions);

        var unsigned = $"{Base64Url.Encode(header)}.{Base64Url.Encode(payload)}";
        using var ecdsa = LoadPrivateKey();
        var signature = ecdsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{unsigned}.{Base64Url.Encode(signature)}";
    }

    private ECDsa LoadPrivateKey()
    {
        var pem = Opt("PrivateKey");
        if (string.IsNullOrWhiteSpace(pem))
        {
            var path = RequiredOpt("PrivateKeyPath");
            pem = File.ReadAllText(path);
        }

        pem = pem.Replace("\\n", "\n", StringComparison.Ordinal);
        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        return ecdsa;
    }

    private string BaseUrl()
    {
        var configured = Opt("BaseUrl");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured!.TrimEnd('/');

        return string.Equals(Opt("Environment"), "Production", StringComparison.OrdinalIgnoreCase)
            ? "https://api.storekit.apple.com"
            : "https://api.storekit-sandbox.apple.com";
    }

    private LastTransaction? SelectBestLastTransaction(JsonElement response)
    {
        if (!response.TryGetProperty("data", out var groups) || groups.ValueKind != JsonValueKind.Array)
            return null;

        LastTransaction? best = null;
        foreach (var group in groups.EnumerateArray())
        {
            if (!group.TryGetProperty("lastTransactions", out var txs) || txs.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var tx in txs.EnumerateArray())
            {
                var signedTx = GetString(tx, "signedTransactionInfo");
                if (string.IsNullOrWhiteSpace(signedTx))
                    continue;

                var current = new LastTransaction(
                    GetInt(tx, "status") ?? 0,
                    signedTx!,
                    GetString(tx, "signedRenewalInfo"));

                if (best is null || Score(current.Status) > Score(best.Value.Status))
                    best = current;
            }
        }
        return best;
    }

    private static int Score(int status) => status switch
    {
        1 => 50, // active
        4 => 40, // billing grace
        3 => 30, // billing retry
        2 => 20, // expired
        5 => 10, // revoked
        _ => 0
    };

    private void ApplyAppleStatus(SubscriptionEntity sub, int status)
    {
        switch (status)
        {
            case 1:
                break;
            case 4:
                sub.Status = EntitlementService.Status.Grace;
                break;
            case 2:
            case 3:
                sub.AutoRenew = false;
                break;
            case 5:
                sub.RevokedAt ??= timeProvider.GetUtcNow().UtcDateTime;
                sub.Status = EntitlementService.Status.Revoked;
                break;
        }
    }

    private SubscriptionEntity? ProjectTransaction(JsonElement p, string? deviceUuid)
    {
        var originalTxId = GetString(p, "originalTransactionId");
        var productId = GetString(p, "productId");
        if (string.IsNullOrEmpty(originalTxId) || string.IsNullOrEmpty(productId))
            return null;

        ValidateBundleId(p);
        ValidateProductId(productId!);

        var expiresAt = GetUnixMs(p, "expiresDate");
        var environment = GetString(p, "environment") ?? (IsLocalVerifyEnabled ? "LocalTesting" : "Production");
        ValidateEnvironment(environment);
        var isTrial = GetInt(p, "offerType") == 1;
        var revokedAt = GetUnixMs(p, "revocationDate");
        var appAccountToken = GetString(p, "appAccountToken");
        if (string.IsNullOrWhiteSpace(appAccountToken) && !IsLocalVerifyEnabled)
            throw new InvalidOperationException("appAccountToken is required");

        return new SubscriptionEntity
        {
            OriginalTransactionId = originalTxId!,
            DeviceUuid = !string.IsNullOrWhiteSpace(appAccountToken) ? appAccountToken! : (deviceUuid ?? ""),
            ProductId = productId!,
            ExpiresAt = expiresAt,
            IsTrial = isTrial,
            WasEverPaid = !isTrial,
            AutoRenew = true,
            Environment = environment,
            LastCheckedAt = timeProvider.GetUtcNow().UtcDateTime,
            RevokedAt = revokedAt,
        };
    }

    private void ValidateBundleId(JsonElement payload)
    {
        var expected = Opt("BundleId");
        if (string.IsNullOrWhiteSpace(expected))
            return;

        var actual = GetString(payload, "bundleId");
        if (string.IsNullOrWhiteSpace(actual))
        {
            if (!IsLocalVerifyEnabled)
                throw new InvalidOperationException("transaction missing bundleId");
            return;
        }

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException("transaction bundleId mismatch");
    }

    private void ValidateEnvironment(string environment)
    {
        var expected = Opt("Environment");
        if (string.IsNullOrWhiteSpace(expected) || IsLocalVerifyEnabled)
            return;

        if (!string.Equals(environment, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("transaction environment mismatch");
    }

    private void ValidateProductId(string productId)
    {
        var allowed = AllowedProductIds();
        if (allowed.Count == 0)
            return;

        if (!allowed.Contains(productId))
            throw new InvalidOperationException("transaction productId is not allowed");
    }

    private HashSet<string> AllowedProductIds()
    {
        var values = config.GetSection("Apple:AppStoreServer:AllowedProductIds")
            .GetChildren()
            .Select(c => c.Value?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!);

        var raw = Opt("AllowedProductIds");
        if (!string.IsNullOrWhiteSpace(raw))
            values = values.Concat(raw!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return values.ToHashSet(StringComparer.Ordinal);
    }

    private string RequiredOpt(string key)
        => Opt(key) ?? throw new InvalidOperationException($"Apple App Store Server option '{key}' is missing.");

    private string? Opt(string key)
        => config[$"Apple:AppStoreServer:{key}"]?.Trim()
            ?? config[$"Apple:{key}"]?.Trim();

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number)
            return null;
        return v.GetInt32();
    }

    private static DateTime? GetUnixMs(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number)
            return null;
        return DateTimeOffset.FromUnixTimeMilliseconds(v.GetInt64()).UtcDateTime;
    }

    private readonly record struct LastTransaction(int Status, string SignedTransactionInfo, string? SignedRenewalInfo);
}
