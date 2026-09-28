# Task 4 review package — base 1c31750305e44914bc3c8c7fbdf453d1e8eec7b4; head 4db300428adccf820584aeb0860afd73af3b673f
4db3004 Harden Apple subscription identity lifecycle and environment validation
 Program.cs                                         |   1 +
 docs/apple-subscription-validation.md              |  15 ++
 src/Mavrylo.Services/Mavrylo.Services.csproj       |   1 +
 .../Services/AccountEntitlementService.cs          |  30 +--
 .../Services/AppStoreNotificationService.cs        |  17 +-
 .../Services/AppStoreServerClient.cs               |  88 +++----
 src/Mavrylo.Services/Services/AppleJws.cs          |  64 +++--
 .../Services/EntitlementService.cs                 |  71 ++++--
 src/Mavrylo.Services/Services/IapService.cs        |  21 +-
 .../Services/SubscriptionValidationPolicy.cs       |  36 +++
 tests/AiProtectionFilterTests.cs                   |   6 +-
 tests/AiRequestInterpretationTests.cs              |   2 +-
 tests/AppStoreServerClientTests.cs                 | 109 ++++++++-
 tests/AppleJwsTests.cs                             |  89 +++++++
 tests/DeviceWordsControllerTests.cs                |   2 +-
 tests/EntitlementServiceTests.cs                   |   4 +-
 tests/Fixtures/Apple/LICENSE.txt                   |   7 +
 tests/Fixtures/Apple/README.md                     |   9 +
 tests/Fixtures/Apple/chain-vectors.json            |  12 +
 tests/Fixtures/Apple/renewalInfo                   |   1 +
 tests/Fixtures/Apple/testCA.der                    | Bin 0 -> 390 bytes
 tests/Fixtures/Apple/testNotification              |   1 +
 tests/Fixtures/Apple/transactionInfo               |   1 +
 tests/Mavrylo.Tests.csproj                         |   1 +
 tests/PaidAccessAuditRegressionTests.cs            |  16 +-
 tests/PublicFlashcardSetControllerTests.cs         |   2 +-
 tests/ReviewTranslationTests.cs                    |   2 +-
 tests/SharedSubscriptionPostgresTests.cs           |  24 +-
 tests/SharedSubscriptionTests.cs                   |  38 +--
 tests/SubscriptionLifecycleTests.cs                | 259 +++++++++++++++++++++
 tests/TestModeRouteTests.cs                        |   2 +-
 tests/TestSupport/ApiFactory.cs                    |   2 +
 tests/TestSupport/TestConfig.cs                    |   7 +-
 33 files changed, 796 insertions(+), 144 deletions(-)
diff --git a/Program.cs b/Program.cs
index 8157e63..7e3f0c8 100644
--- a/Program.cs
+++ b/Program.cs
@@ -23,24 +23,25 @@ builder.Services.AddScoped<IAiProviderJsonService>(sp => sp.GetRequiredService<G
 builder.Services.AddScoped<IAiJsonService, FallbackAiJsonService>();
 builder.Services.AddSingleton<AiAlertCooldown>();
 builder.Services.AddSingleton<ISmtpClientFactory, MailKitSmtpClientFactory>();
 builder.Services.AddSingleton<IDevAlertEmailService, SmtpDevAlertEmailService>();
 builder.Services.AddScoped<TranslationCacheService>();
 builder.Services.AddMemoryCache();
 builder.Services.AddScoped<ChallengeService>();
 builder.Services.AddSingleton<AppAttestVerifier>();
 builder.Services.AddSingleton<IAppAttestVerifier>(sp => sp.GetRequiredService<AppAttestVerifier>());
 builder.Services.AddHttpClient<AppStoreServerClient>();
 builder.Services.AddScoped<IAppStoreServerClient>(sp => sp.GetRequiredService<AppStoreServerClient>());
 builder.Services.AddScoped<EntitlementService>();
+builder.Services.AddSingleton<SubscriptionValidationPolicy>();
 builder.Services.AddScoped<DeviceContextService>();
 builder.Services.AddScoped<AiUsageService>();
 builder.Services.AddScoped<DeviceWordService>();
 builder.Services.AddScoped<PublicFlashcardSetService>();
 builder.Services.AddScoped<WordService>();
 builder.Services.AddScoped<CategoryService>();
 builder.Services.AddScoped<UserSettingsService>();
 builder.Services.AddScoped<AuthService>();
 builder.Services.AddScoped<AccountService>();
 builder.Services.AddScoped<AccountSyncService>();
 builder.Services.AddScoped<Mavrylo.Filters.OptionalAccountProofFilter>();
 builder.Services.AddHttpContextAccessor();
diff --git a/docs/apple-subscription-validation.md b/docs/apple-subscription-validation.md
new file mode 100644
index 0000000..6be78dd
--- /dev/null
+++ b/docs/apple-subscription-validation.md
@@ -0,0 +1,15 @@
+# Apple subscription validation and rollout boundary
+
+The device and account resolvers apply the same server-owned purchase policy. A usable row requires a nonempty original transaction ID, an explicitly allowed product, a finite expiry, and an environment matching `Apple:AppStoreServer:Environment` (legacy `Apple:Environment` fallback). Missing environment configuration defaults to Production and a missing product allow-list admits no products. Allowed product IDs support comma-separated values or configuration children. Signed transaction projections also require the auto-renewable subscription type and validate the configured bundle ID.
+
+Sandbox must run with explicitly configured Sandbox server settings and separate database/usage budgets. A client flag never selects the purchase environment. Unsigned local StoreKit is available only in DEBUG + Development with an explicit LocalTesting/Xcode server environment; Release never enables that path. Creating and validating the isolated App Review/Sandbox deployment remains a rollout prerequisite.
+
+DTO shapes remain unchanged. An existing invalid device purchase now returns inactive `invalid_subscription`, with no validated paid-history claim; a missing purchase still returns `free`. Client unknown/inactive-state handling must be integrated before release. Account selection excludes invalid rows, and it does not turn revoked purchases into expired_paid merely because a historical paid flag exists. An independent valid purchase can still grant access or retain ordinary expired-paid content rights. Existing claimed-purchase/account-required behavior remains intact while anonymous ownership recovery is redesigned separately.
+
+An inapplicable stored row is quarantined during refresh: its metadata, paid-history flag, device link and claim/tombstone fields are preserved. Verify/claim returns HTTP503 with `subscription_reconciliation_required`; cached reads deny that row, and notifications return503 so delivery retries. A newly verified Apple response does not automatically repair that historical row. **Before enabling enforcement, inventory and reconcile such legacy rows against trusted Apple evidence without deleting purchases/cards/tombstones or requiring repurchase.** This release gate includes potentially legitimate historical purchasers whose rows are incomplete or whose product configuration changed.
+
+Canonical Apple status selection filters by original transaction ID before ranking and rechecks the signed ID before persistence. Renewal information must match the same purchase, current product and environment; a future renewal product does not rewrite the purchased product. Only a matching finite grace end can extend grace. Expired/retry/revoked canonical states cannot be promoted by stale renewal metadata. During API/network failure, cached access is never extended, and a state-changing notification is not acknowledged as successfully processed. There is no local durable notification queue; recovery relies on Apple's delivery retry and operational history reconciliation if that retry window is exhausted.
+
+Writes are serialized by the existing PostgreSQL purchase lock. Tracked rows are reloaded inside the lock, so earlier context reads cannot defeat event ordering. Dated newer signed transaction/renewal state wins; older or undated input cannot overwrite an established dated state. Repeating the same canonical proof may relink a restore device without rewriting lifecycle fields or ownership. Notification/account/token background refreshes preserve device linkage; token/entitlement responses reselect any independent active purchase after a refresh revokes the previously selected one.
+
+JWS verification uses ES256, embedded Apple Root G3 trust, Apple leaf/intermediate signing-purpose OIDs, and offline certificate validity at the signed payload date. The public decoder never accepts a configurable test root. Reference fixtures are pinned and attributed in `tests/Fixtures/Apple/README.md`. This is offline verification: online OCSP/revocation checks are not implemented, OS certificate-date boundaries are strict (the Node reference allows a 60-second skew), and passing mock/chain tests does not establish physical-device/App Review purchase acceptance. That acceptance still requires the isolated deployment/device matrix.
diff --git a/src/Mavrylo.Services/Mavrylo.Services.csproj b/src/Mavrylo.Services/Mavrylo.Services.csproj
index 414ff23..a660120 100644
--- a/src/Mavrylo.Services/Mavrylo.Services.csproj
+++ b/src/Mavrylo.Services/Mavrylo.Services.csproj
@@ -1,20 +1,21 @@
 <Project Sdk="Microsoft.NET.Sdk">
   <PropertyGroup>
     <TargetFramework>net10.0</TargetFramework>
     <Nullable>enable</Nullable>
     <ImplicitUsings>enable</ImplicitUsings>
     <RootNamespace>Mavrylo</RootNamespace>
   </PropertyGroup>
   <ItemGroup>
+    <InternalsVisibleTo Include="Mavrylo.Tests" />
     <FrameworkReference Include="Microsoft.AspNetCore.App" />
   </ItemGroup>
   <ItemGroup><!-- replicate the Web SDK implicit usings the moved files rely on -->
     <Using Include="System.Net.Http.Json" />
     <Using Include="Microsoft.AspNetCore.Builder" />
     <Using Include="Microsoft.AspNetCore.Hosting" />
     <Using Include="Microsoft.AspNetCore.Http" />
     <Using Include="Microsoft.AspNetCore.Routing" />
     <Using Include="Microsoft.Extensions.Configuration" />
     <Using Include="Microsoft.Extensions.DependencyInjection" />
     <Using Include="Microsoft.Extensions.Hosting" />
     <Using Include="Microsoft.Extensions.Logging" />
diff --git a/src/Mavrylo.Services/Services/AccountEntitlementService.cs b/src/Mavrylo.Services/Services/AccountEntitlementService.cs
index f4b9ee4..0e742a8 100644
--- a/src/Mavrylo.Services/Services/AccountEntitlementService.cs
+++ b/src/Mavrylo.Services/Services/AccountEntitlementService.cs
@@ -1,77 +1,77 @@
 using Mavrylo.Data;
 using Mavrylo.Models;
 using Microsoft.EntityFrameworkCore;
 
 namespace Mavrylo.Services;
 
 public sealed record AccountEntitlement(string Status, string? ProductId, DateTime? ExpiresAt,
     bool IsTrial, bool AutoRenew, bool WasEverPaid, string? Source, DateTime CheckedAt);
 
 public sealed class AccountEntitlementService(AppDbContext db, EntitlementService entitlements,
-    IAppStoreServerClient apple, SubscriptionOwnershipService ownership, TimeProvider clock, IHostEnvironment environment)
+    IAppStoreServerClient apple, SubscriptionOwnershipService ownership, TimeProvider clock)
 {
     public static bool IsActive(string status) => status is "trial" or "premium" or "grace";
-    private static int Rank(string status) => status switch
-    {
-        "premium" => 6, "grace" => 5, "trial" => 4, "expired_paid" => 3,
-        "revoked" => 2, "expired_trial" => 1, _ => 0
-    };
 
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
-            if (!environment.IsDevelopment() && refreshed.Subscription.Environment != "Production") continue;
+            if (!entitlements.IsApplicable(refreshed.Subscription)) continue;
             refreshed.Subscription.DeviceUuid = row.DeviceUuid;
-            rows[index] = await entitlements.UpsertAsync(refreshed.Subscription, ct);
+            try { rows[index] = await entitlements.UpsertAsync(refreshed.Subscription, ct, relinkDevice: false); }
+            catch (SubscriptionReconciliationRequiredException) { /* Retain quarantined evidence; projection denies it. */ }
         }
-        var selected = rows.Where(x => environment.IsDevelopment() || x.Environment == "Production")
-            .OrderByDescending(x => Rank(entitlements.ToEntitlement(x).Status))
+        var applicable = rows.Where(entitlements.IsApplicable).ToList();
+        var selected = applicable
+            .OrderByDescending(x => EntitlementService.Rank(entitlements.ToEntitlement(x).Status))
             .ThenByDescending(x => x.ExpiresAt).FirstOrDefault();
         var ent = entitlements.ToEntitlement(selected);
-        var wasEverPaid = rows.Any(x => x.WasEverPaid);
-        if (!IsActive(ent.Status) && wasEverPaid) ent = ent with { Status = "expired_paid" };
+        var wasEverPaid = applicable.Any(x => x.WasEverPaid);
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
-            || (!environment.IsDevelopment() && sub.Environment != "Production")
-            || sub.ExpiresAt == null || !IsActive(entitlements.ToEntitlement(sub).Status))
+            || !entitlements.IsApplicable(sub)
+            || !IsActive(entitlements.ToEntitlement(sub).Status))
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
-        await entitlements.UpsertAsync(sub, ct);
+        try { await entitlements.UpsertAsync(sub, ct); }
+        catch (SubscriptionReconciliationRequiredException)
+        {
+            return (503, new { code = "subscription_reconciliation_required", error = "Stored subscription needs reconciliation." });
+        }
         if (!await ownership.TryClaimAsync(sub.OriginalTransactionId, accountId, ct))
             return (409, new { code = "subscription_already_linked", error = "This subscription is already linked to an account." });
         await db.Devices.Where(x => x.KeyId == deviceKeyId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RequiresAccountSubscription, true), ct);
         await tx.CommitAsync(ct);
         return (200, await GetAsync(accountId, ct));
     }
 }
diff --git a/src/Mavrylo.Services/Services/AppStoreNotificationService.cs b/src/Mavrylo.Services/Services/AppStoreNotificationService.cs
index 56ec973..2e25cf5 100644
--- a/src/Mavrylo.Services/Services/AppStoreNotificationService.cs
+++ b/src/Mavrylo.Services/Services/AppStoreNotificationService.cs
@@ -40,57 +40,64 @@ public sealed class AppStoreNotificationService(
 
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
-                return 200; // don't ask Apple to retry a payload we can't project
+                return 400; // Never acknowledge an unprocessed purchase state change.
             }
 
             var signedRenewalInfo = GetString(data, "signedRenewalInfo");
             if (!string.IsNullOrWhiteSpace(signedRenewalInfo))
                 appStore.ApplyRenewalInfo(verified.Subscription, signedRenewalInfo!);
 
             var canonical = await appStore.RefreshSubscriptionAsync(verified.Subscription.OriginalTransactionId, verified.Subscription.DeviceUuid, ct);
             if (canonical.Ok && canonical.Subscription is not null)
             {
+                if (canonical.Subscription.OriginalTransactionId != verified.Subscription.OriginalTransactionId
+                    || !entitlements.IsApplicable(canonical.Subscription)) return 503;
                 verified = new AppStoreServerClient.VerifiedTransaction(
                     true,
                     canonical.Subscription,
                     verified.SignatureVerified,
                     null);
             }
 
             var subscription = verified.Subscription
                 ?? throw new InvalidOperationException("verified notification lost its subscription payload");
-            if (root.TryGetProperty("signedDate", out var signedDate) && signedDate.TryGetInt64(out var signedMilliseconds))
+            if (!canonical.Ok && root.TryGetProperty("signedDate", out var signedDate) && signedDate.TryGetInt64(out var signedMilliseconds))
                 subscription.LastAppleEventAt = DateTimeOffset.FromUnixTimeMilliseconds(signedMilliseconds).UtcDateTime;
             // A fresh canonical response wins over historical notification semantics.
-            if (!canonical.Ok)
+            if (!canonical.Ok || canonical.Subscription is null)
             {
-                if (appStore.IsServerApiConfigured) return 503; // Apple retries; do not resurrect from stale payloads.
+                if (appStore.IsServerApiConfigured || !appStore.IsLocalVerifyEnabled) return 503; // Apple retries; do not resurrect from stale payloads.
                 ApplyNotificationSemantics(subscription, notificationType, subtype, timeProvider.GetUtcNow().UtcDateTime);
             }
-            await entitlements.UpsertAsync(subscription, ct);
+            await entitlements.UpsertAsync(subscription, ct, relinkDevice: false);
 
             logger.LogInformation("Processed App Store notification {Type}/{Subtype} for {Otid}.",
                 notificationType, subtype, subscription.OriginalTransactionId);
             return 200;
         }
+        catch (SubscriptionReconciliationRequiredException ex)
+        {
+            logger.LogWarning(ex, "Apple notification requires stored subscription reconciliation; delivery must retry.");
+            return 503;
+        }
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
diff --git a/src/Mavrylo.Services/Services/AppStoreServerClient.cs b/src/Mavrylo.Services/Services/AppStoreServerClient.cs
index 4a4bc65..6813c96 100644
--- a/src/Mavrylo.Services/Services/AppStoreServerClient.cs
+++ b/src/Mavrylo.Services/Services/AppStoreServerClient.cs
@@ -10,24 +10,25 @@ namespace Mavrylo.Services;
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
+    private readonly SubscriptionValidationPolicy validation = new(config, env);
 
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
@@ -37,25 +38,25 @@ public sealed class AppStoreServerClient(
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
-            return env.IsDevelopment();
+            return env.IsDevelopment() && validation.ExpectedEnvironment is "LocalTesting" or "Xcode";
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
@@ -116,77 +117,100 @@ public sealed class AppStoreServerClient(
     public Task<SubscriptionStatusesResult> RefreshSubscriptionForClaimAsync(string originalTransactionId, string? deviceUuid, CancellationToken ct)
         => GetSubscriptionStatusesCoreAsync(originalTransactionId, deviceUuid, true, ct);
 
     private async Task<SubscriptionStatusesResult> GetSubscriptionStatusesCoreAsync(string originalTransactionId, string? deviceUuid, bool claim, CancellationToken ct)
     {
         if (!IsServerApiConfigured)
             return SubscriptionStatusesResult.Fail("App Store Server API is not configured");
 
         var json = await SendAppleGetAsync($"/inApps/v1/subscriptions/{Uri.EscapeDataString(originalTransactionId)}", ct);
         if (json is null)
             return SubscriptionStatusesResult.Fail("App Store Server API subscription status request failed");
 
-        var best = SelectBestLastTransaction(json.Value);
+        var best = SelectBestLastTransaction(json.Value, originalTransactionId);
         if (best is null)
             return SubscriptionStatusesResult.Fail("Apple subscription status response had no lastTransactions");
 
         var verified = claim ? VerifyTransactionForClaim(best.Value.SignedTransactionInfo, deviceUuid) : VerifyTransaction(best.Value.SignedTransactionInfo, deviceUuid);
         if (!verified.Ok || verified.Subscription is null)
             return SubscriptionStatusesResult.Fail(verified.Error ?? "Apple subscription transaction verification failed");
+        if (verified.Subscription.OriginalTransactionId != originalTransactionId)
+            return SubscriptionStatusesResult.Fail("Apple subscription originalTransactionId mismatch");
+        if (best.Value.Status is < 1 or > 5)
+            return SubscriptionStatusesResult.Fail("Apple subscription status is unknown");
 
-        ApplyAppleStatus(verified.Subscription, best.Value.Status);
         if (!string.IsNullOrWhiteSpace(best.Value.SignedRenewalInfo))
-            ApplyRenewalInfoCore(verified.Subscription, best.Value.SignedRenewalInfo!, claim || !IsLocalVerifyEnabled);
+            ApplyRenewalInfoCore(verified.Subscription, best.Value.SignedRenewalInfo!, claim || !IsLocalVerifyEnabled, best.Value.Status == 4);
+        ApplyAppleStatus(verified.Subscription, best.Value.Status);
 
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
 
-    private void ApplyRenewalInfoCore(SubscriptionEntity sub, string signedRenewalInfo, bool requireVerified)
+    private void ApplyRenewalInfoCore(SubscriptionEntity sub, string signedRenewalInfo, bool requireVerified, bool allowGrace = true)
     {
         var decoded = AppleJws.Decode(signedRenewalInfo, verifySignature: requireVerified);
         if (!decoded.Ok)
         {
             logger.LogWarning("Unable to decode signedRenewalInfo for {Otid}: {Error}", sub.OriginalTransactionId, decoded.Error);
             return;
         }
 
         var p = decoded.Payload;
+        var productId = GetString(p, "productId");
+        if (GetString(p, "originalTransactionId") != sub.OriginalTransactionId
+            || GetString(p, "environment") != sub.Environment
+            || !validation.AllowsEnvironment(sub.Environment) || !validation.AllowsProduct(productId)
+            || productId != sub.ProductId)
+        {
+            logger.LogWarning("Ignored renewal info with mismatched purchase identity, environment or product for {Otid}.", sub.OriginalTransactionId);
+            return;
+        }
         var autoRenewStatus = GetInt(p, "autoRenewStatus");
         if (autoRenewStatus is not null)
             sub.AutoRenew = autoRenewStatus == 1;
 
         var graceExpires = GetUnixMs(p, "gracePeriodExpiresDate");
-        if (graceExpires is not null && graceExpires > timeProvider.GetUtcNow().UtcDateTime)
+        if (allowGrace && sub.RevokedAt is null && graceExpires is not null && graceExpires > timeProvider.GetUtcNow().UtcDateTime)
         {
             sub.ExpiresAt = graceExpires;
             sub.Status = EntitlementService.Status.Grace;
         }
+        var signedAt = GetUnixMs(p, "signedDate");
+        if (signedAt > sub.LastAppleEventAt || sub.LastAppleEventAt == null) sub.LastAppleEventAt = signedAt;
 
-        var productId = GetString(p, "productId") ?? GetString(p, "autoRenewProductId");
-        if (!string.IsNullOrWhiteSpace(productId))
-            sub.ProductId = productId!;
+        // autoRenewProductId describes a future renewal, not the purchased entitlement.
     }
 
     private async Task<JsonElement?> SendAppleGetAsync(string path, CancellationToken ct)
+    {
+        try { return await SendAppleGetCoreAsync(path, ct); }
+        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
+        {
+            logger.LogWarning(ex, "Apple subscription API unavailable.");
+            return null;
+        }
+    }
+
+    private async Task<JsonElement?> SendAppleGetCoreAsync(string path, CancellationToken ct)
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
@@ -236,42 +260,43 @@ public sealed class AppStoreServerClient(
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
 
-        return string.Equals(Opt("Environment"), "Production", StringComparison.OrdinalIgnoreCase)
+        return validation.ExpectedEnvironment == "Production"
             ? "https://api.storekit.apple.com"
             : "https://api.storekit-sandbox.apple.com";
     }
 
-    private LastTransaction? SelectBestLastTransaction(JsonElement response)
+    internal static LastTransaction? SelectBestLastTransaction(JsonElement response, string expectedOriginalTransactionId)
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
+                if (GetString(tx, "originalTransactionId") != expectedOriginalTransactionId) continue;
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
@@ -287,138 +312,123 @@ public sealed class AppStoreServerClient(
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
-                sub.Status = EntitlementService.Status.Grace;
+                // Only matching, verified renewal info can establish the finite grace end.
+                if (sub.Status != EntitlementService.Status.Grace)
+                    sub.ExpiresAt = timeProvider.GetUtcNow().UtcDateTime;
                 break;
             case 2:
             case 3:
                 sub.AutoRenew = false;
+                if (sub.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime)
+                    sub.ExpiresAt = timeProvider.GetUtcNow().UtcDateTime;
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
-        var environment = GetString(p, "environment") ?? (IsLocalVerifyEnabled ? "LocalTesting" : "Production");
+        if (expiresAt is null) throw new InvalidOperationException("transaction missing expiresDate");
+        var type = GetString(p, "type");
+        if (type != "Auto-Renewable Subscription") throw new InvalidOperationException("transaction must be an auto-renewable subscription");
+        var environment = GetString(p, "environment") ?? "";
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
+            LastAppleEventAt = GetUnixMs(p, "signedDate"),
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
-        var expected = Opt("Environment");
-        if (string.IsNullOrWhiteSpace(expected) || IsLocalVerifyEnabled)
-            return;
-
-        if (!string.Equals(environment, expected, StringComparison.OrdinalIgnoreCase))
+        if (!validation.AllowsEnvironment(environment))
             throw new InvalidOperationException("transaction environment mismatch");
     }
 
     private void ValidateProductId(string productId)
     {
-        var allowed = AllowedProductIds();
-        if (allowed.Count == 0)
-            return;
-
-        if (!allowed.Contains(productId))
+        if (!validation.AllowsProduct(productId))
             throw new InvalidOperationException("transaction productId is not allowed");
     }
 
-    private HashSet<string> AllowedProductIds()
-    {
-        var values = config.GetSection("Apple:AppStoreServer:AllowedProductIds")
-            .GetChildren()
-            .Select(c => c.Value?.Trim())
-            .Where(v => !string.IsNullOrWhiteSpace(v))
-            .Select(v => v!);
-
-        var raw = Opt("AllowedProductIds");
-        if (!string.IsNullOrWhiteSpace(raw))
-            values = values.Concat(raw!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
-
-        return values.ToHashSet(StringComparer.Ordinal);
-    }
-
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
 
-    private readonly record struct LastTransaction(int Status, string SignedTransactionInfo, string? SignedRenewalInfo);
+    internal readonly record struct LastTransaction(int Status, string SignedTransactionInfo, string? SignedRenewalInfo);
 }
diff --git a/src/Mavrylo.Services/Services/AppleJws.cs b/src/Mavrylo.Services/Services/AppleJws.cs
index 58568fd..2180c36 100644
--- a/src/Mavrylo.Services/Services/AppleJws.cs
+++ b/src/Mavrylo.Services/Services/AppleJws.cs
@@ -24,95 +24,125 @@ public static class AppleJws
 
     public sealed record DecodeResult(bool Ok, JsonElement Payload, bool SignatureVerified, string? Error)
     {
         public static DecodeResult Fail(string error) => new(false, default, false, error);
     }
 
     /// <summary>
     /// Decode a JWS. When <paramref name="verifySignature"/> is true the x5c chain is validated to
     /// the Apple Root CA - G3 and the ES256 signature is checked; otherwise only the payload is
     /// parsed (Development convenience).
     /// </summary>
     public static DecodeResult Decode(string jws, bool verifySignature)
+        => DecodeCore(jws, verifySignature, AppleRootCaG3.Value);
+
+    internal static DecodeResult DecodeCore(string jws, bool verifySignature, X509Certificate2 trustedRoot)
     {
         if (string.IsNullOrWhiteSpace(jws))
             return DecodeResult.Fail("empty JWS");
 
         var parts = jws.Split('.');
         if (parts.Length != 3)
             return DecodeResult.Fail("JWS must have 3 parts");
 
         JsonElement payload;
         try
         {
             var payloadJson = Encoding.UTF8.GetString(Base64Url.Decode(parts[1]));
-            payload = JsonDocument.Parse(payloadJson).RootElement.Clone();
+            using var document = JsonDocument.Parse(payloadJson);
+            payload = document.RootElement.Clone();
         }
         catch (Exception ex)
         {
             return DecodeResult.Fail($"payload parse failed: {ex.Message}");
         }
 
         if (!verifySignature)
             return new DecodeResult(true, payload, false, null);
 
+        var chainCerts = new List<X509Certificate2>();
         try
         {
             var headerJson = Encoding.UTF8.GetString(Base64Url.Decode(parts[0]));
             using var header = JsonDocument.Parse(headerJson);
             var alg = header.RootElement.TryGetProperty("alg", out var algEl) && algEl.ValueKind == JsonValueKind.String
                 ? algEl.GetString()
                 : null;
             if (!string.Equals(alg, "ES256", StringComparison.Ordinal))
                 return DecodeResult.Fail("JWS alg must be ES256");
 
-            if (!header.RootElement.TryGetProperty("x5c", out var x5cEl) || x5cEl.ValueKind != JsonValueKind.Array || x5cEl.GetArrayLength() == 0)
+            if (!header.RootElement.TryGetProperty("x5c", out var x5cEl) || x5cEl.ValueKind != JsonValueKind.Array || x5cEl.GetArrayLength() != 3)
                 return DecodeResult.Fail("JWS header missing x5c chain");
 
-            var chainCerts = new List<X509Certificate2>();
             foreach (var c in x5cEl.EnumerateArray())
                 chainCerts.Add(X509CertificateLoader.LoadCertificate(Convert.FromBase64String(c.GetString()!)));
 
             var leaf = chainCerts[0];
-            var intermediates = new X509Certificate2Collection();
-            for (int i = 1; i < chainCerts.Count; i++)
-                intermediates.Add(chainCerts[i]);
-
-            using var chain = new X509Chain();
-            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
-            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
-            chain.ChainPolicy.CustomTrustStore.Add(AppleRootCaG3.Value);
-            chain.ChainPolicy.ExtraStore.AddRange(intermediates);
-            if (!chain.Build(leaf))
-            {
-                var reasons = string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim()));
-                return DecodeResult.Fail($"x5c chain not trusted: {reasons}");
-            }
+            // Offline verification follows Apple's reference: certificates must be valid when
+            // the payload was signed. This date is trusted only after the signature also succeeds.
+            var effectiveDate = payload.TryGetProperty("signedDate", out var signedDate)
+                ? DateTimeOffset.FromUnixTimeMilliseconds(signedDate.GetInt64()).UtcDateTime : DateTime.UtcNow;
+            if (!VerifyCertificateChain(leaf, [chainCerts[1]], trustedRoot, effectiveDate, out var error))
+                return DecodeResult.Fail(error!);
 
             // Verify ES256 over ASCII(header.payload) using the leaf's public key.
             using var ecdsa = leaf.GetECDsaPublicKey()
                 ?? throw new CryptographicException("leaf has no EC public key");
+            if (ecdsa.KeySize != 256) return DecodeResult.Fail("ES256 requires a P-256 key");
             var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
             var signature = Base64Url.Decode(parts[2]);
             var verified = ecdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256,
                 DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
             if (!verified)
                 return DecodeResult.Fail("JWS signature invalid");
 
             return new DecodeResult(true, payload, true, null);
         }
         catch (Exception ex)
         {
             return DecodeResult.Fail($"JWS verification error: {ex.Message}");
         }
+        finally
+        {
+            foreach (var certificate in chainCerts) certificate.Dispose();
+        }
+    }
+
+    internal static bool VerifyCertificateChain(X509Certificate2 leaf, X509Certificate2[] intermediates,
+        X509Certificate2 trustedRoot, DateTime effectiveDate, out string? error)
+    {
+        using var chain = new X509Chain();
+        if (intermediates.Length != 1
+            || leaf.Extensions["1.2.840.113635.100.6.11.1"] == null
+            || intermediates[0].Extensions["1.2.840.113635.100.6.2.1"] == null)
+        {
+            error = "x5c signing purpose is invalid";
+            return false;
+        }
+        // Deliberately offline. Enabling OS revocation is not equivalent to Apple's OCSP checks.
+        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
+        chain.ChainPolicy.DisableCertificateDownloads = true;
+        chain.ChainPolicy.VerificationTime = effectiveDate;
+        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
+        chain.ChainPolicy.CustomTrustStore.Add(trustedRoot);
+        chain.ChainPolicy.ExtraStore.AddRange(intermediates);
+        if (!chain.Build(leaf) || chain.ChainElements.Count != 3
+            || !chain.ChainElements[1].Certificate.RawData.AsSpan().SequenceEqual(intermediates[0].RawData)
+            || !chain.ChainElements[2].Certificate.RawData.AsSpan().SequenceEqual(trustedRoot.RawData))
+        {
+            error = "x5c chain not trusted: " + string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim()));
+            return false;
+        }
+        error = null;
+        return true;
     }
 
     private static X509Certificate2 LoadEmbeddedPem(string resourceName)
     {
         var asm = Assembly.GetExecutingAssembly();
         using var stream = asm.GetManifestResourceStream(resourceName)
             ?? throw new InvalidOperationException($"Embedded cert not found: '{resourceName}'.");
         using var sr = new StreamReader(stream);
         return X509Certificate2.CreateFromPem(sr.ReadToEnd());
     }
 }
 
diff --git a/src/Mavrylo.Services/Services/EntitlementService.cs b/src/Mavrylo.Services/Services/EntitlementService.cs
index f17ad41..0b0eb5d 100644
--- a/src/Mavrylo.Services/Services/EntitlementService.cs
+++ b/src/Mavrylo.Services/Services/EntitlementService.cs
@@ -1,131 +1,168 @@
 using Mavrylo.Data;
 using Mavrylo.Models;
 using Microsoft.EntityFrameworkCore;
 
 namespace Mavrylo.Services;
 
+public sealed class SubscriptionReconciliationRequiredException()
+    : InvalidOperationException("Stored Apple subscription requires reconciliation before it can be refreshed.");
+
 /// <summary>
 /// Computes the canonical entitlement (the state machine) from a subscription record, and
 /// persists/links subscriptions. The entitlement object is what the device-JWT's <c>ent</c> claim
 /// and the /iap/* responses carry. See plan: entitlement state machine + Appendix M.
 /// </summary>
-public sealed class EntitlementService(AppDbContext db, TimeProvider timeProvider)
+public sealed class EntitlementService(AppDbContext db, TimeProvider timeProvider, SubscriptionValidationPolicy validation)
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
+        public const string Invalid = "invalid_subscription";
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
-    public async Task<SubscriptionEntity> UpsertAsync(SubscriptionEntity incoming, CancellationToken ct = default)
+    public async Task<SubscriptionEntity> UpsertAsync(SubscriptionEntity incoming, CancellationToken ct = default, bool relinkDevice = true)
     {
-        if (!db.Database.IsNpgsql()) return await UpsertCoreAsync(incoming, ct);
+        if (!IsApplicable(incoming)) throw new InvalidOperationException("Subscription identity, environment, product and finite expiry must be valid.");
+        if (!db.Database.IsNpgsql()) return await UpsertCoreAsync(incoming, relinkDevice, ct);
         var ownsTransaction = db.Database.CurrentTransaction == null;
         await using var tx = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;
         await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({incoming.OriginalTransactionId}, 431))", ct);
-        var result = await UpsertCoreAsync(incoming, ct);
+        var result = await UpsertCoreAsync(incoming, relinkDevice, ct);
         if (tx != null) await tx.CommitAsync(ct);
         return result;
     }
 
-    private async Task<SubscriptionEntity> UpsertCoreAsync(SubscriptionEntity incoming, CancellationToken ct)
+    private async Task<SubscriptionEntity> UpsertCoreAsync(SubscriptionEntity incoming, bool relinkDevice, CancellationToken ct)
     {
         var existing = await db.Subscriptions
             .FirstOrDefaultAsync(s => s.OriginalTransactionId == incoming.OriginalTransactionId, ct);
 
         if (existing is null)
         {
-            incoming.Status = ComputeStatus(incoming);
+            incoming.Status = ComputeStatus(incoming, timeProvider.GetUtcNow().UtcDateTime);
             db.Subscriptions.Add(incoming);
             await db.SaveChangesAsync(ct);
             return incoming;
         }
 
-        if (incoming.LastAppleEventAt != null && existing.LastAppleEventAt >= incoming.LastAppleEventAt)
+        // The context may have read this row before another request took the advisory lock.
+        // Re-read under the lock so event ordering and quarantine use current persisted state.
+        if (db.Database.IsNpgsql())
+        {
+            if (ReferenceEquals(existing, incoming)) incoming = (SubscriptionEntity)db.Entry(incoming).CurrentValues.ToObject();
+            await db.Entry(existing).ReloadAsync(ct);
+        }
+
+        // Preserve inapplicable historical evidence for explicit reconciliation. In particular,
+        // never convert Sandbox history or an unvalidated paid marker into production history.
+        if (!IsApplicable(existing)) throw new SubscriptionReconciliationRequiredException();
+
+        if (existing.LastAppleEventAt != null
+            && (incoming.LastAppleEventAt == null || existing.LastAppleEventAt >= incoming.LastAppleEventAt))
+        {
+            // A repeat canonical proof may restore a device, but must not replay lifecycle fields.
+            if (relinkDevice && incoming.LastAppleEventAt == existing.LastAppleEventAt
+                && !string.IsNullOrWhiteSpace(incoming.DeviceUuid) && incoming.DeviceUuid != existing.DeviceUuid)
+            {
+                existing.DeviceUuid = incoming.DeviceUuid;
+                await db.SaveChangesAsync(ct);
+            }
             return existing;
+        }
         if (incoming.LastAppleEventAt != null) existing.LastAppleEventAt = incoming.LastAppleEventAt;
         existing.Status = incoming.Status;
         existing.ProductId = incoming.ProductId;
         existing.ExpiresAt = incoming.ExpiresAt;
         existing.IsTrial = incoming.IsTrial;
-        existing.WasEverPaid = existing.WasEverPaid || incoming.WasEverPaid; // sticky
+        existing.WasEverPaid = existing.WasEverPaid || incoming.WasEverPaid;
         existing.AutoRenew = incoming.AutoRenew;
         existing.Environment = incoming.Environment;
         existing.LastCheckedAt = timeProvider.GetUtcNow().UtcDateTime;
-        if (incoming.RevokedAt is not null)
-            existing.RevokedAt = incoming.RevokedAt;
-        if (!string.IsNullOrWhiteSpace(incoming.DeviceUuid))
+        existing.RevokedAt = incoming.RevokedAt;
+        if (relinkDevice && !string.IsNullOrWhiteSpace(incoming.DeviceUuid))
             existing.DeviceUuid = incoming.DeviceUuid; // re-link on restore to a new device
-        existing.Status = ComputeStatus(existing);
+        existing.Status = ComputeStatus(existing, timeProvider.GetUtcNow().UtcDateTime);
         await db.SaveChangesAsync(ct);
         return existing;
     }
 
-    /// <summary>Most relevant subscription for a device UUID (latest expiry wins).</summary>
+    /// <summary>Prefer usable independent purchases, then retained paid history, then other inactive records.</summary>
     public async Task<SubscriptionEntity?> FindForDeviceAsync(string deviceUuid, CancellationToken ct = default)
     {
         if (string.IsNullOrWhiteSpace(deviceUuid))
             return null;
-        return await db.Subscriptions
+        var rows = await db.Subscriptions
             .Where(s => s.DeviceUuid == deviceUuid)
-            .OrderByDescending(s => s.ExpiresAt)
-            .FirstOrDefaultAsync(ct);
+            .ToListAsync(ct);
+        return rows.OrderByDescending(s => Rank(ToDeviceEntitlement(s).Status)).ThenByDescending(s => s.ExpiresAt).FirstOrDefault();
     }
 
     public async Task<SubscriptionEntity?> FindByOriginalTransactionAsync(string originalTransactionId, CancellationToken ct = default)
         => await db.Subscriptions.FirstOrDefaultAsync(s => s.OriginalTransactionId == originalTransactionId, ct);
 
     /// <summary>Project a subscription (or null) into the canonical entitlement, recomputing status.</summary>
     public Entitlement ToEntitlement(SubscriptionEntity? sub)
     {
         if (sub is null)
             return Entitlement.Free();
+        if (!IsApplicable(sub))
+            return new Entitlement(Status.Invalid, sub.ProductId, sub.ExpiresAt, false, false, false);
         var status = ComputeStatus(sub, timeProvider.GetUtcNow().UtcDateTime);
         return new Entitlement(status, sub.ProductId, sub.ExpiresAt, sub.IsTrial, sub.AutoRenew, sub.WasEverPaid);
     }
 
     public Entitlement ToDeviceEntitlement(SubscriptionEntity? sub)
         => sub?.ClaimedAt != null ? new Entitlement("account_required", sub.ProductId, sub.ExpiresAt, false, sub.AutoRenew, sub.WasEverPaid) : ToEntitlement(sub);
 
+    public bool IsApplicable(SubscriptionEntity sub) => validation.IsApplicable(sub);
+
+    public static int Rank(string status) => status switch
+    {
+        Status.Premium => 6, Status.Grace => 5, Status.Trial => 4, Status.ExpiredPaid => 3,
+        Status.Revoked or "account_required" => 2, Status.ExpiredTrial => 1, _ => 0
+    };
+
     /// <summary>
     /// The state machine. Active = not expired. Grace is preserved from <see cref="SubscriptionEntity.Status"/>
     /// (it is set by billing-retry notifications, not derivable from expiry alone).
     /// </summary>
     public static string ComputeStatus(SubscriptionEntity s, DateTime? now = null)
     {
         var ts = now ?? DateTime.UtcNow;
 
         if (s.RevokedAt is not null)
             return Status.Revoked;
 
-        var active = s.ExpiresAt is null || s.ExpiresAt > ts;
+        if (s.ExpiresAt is null) return Status.Invalid;
+        var active = s.ExpiresAt > ts;
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
diff --git a/src/Mavrylo.Services/Services/IapService.cs b/src/Mavrylo.Services/Services/IapService.cs
index 42d14ec..f68bf7d 100644
--- a/src/Mavrylo.Services/Services/IapService.cs
+++ b/src/Mavrylo.Services/Services/IapService.cs
@@ -34,42 +34,50 @@ public sealed class IapService(
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
+            if (canonical.Subscription.OriginalTransactionId != verified.Subscription.OriginalTransactionId
+                || !entitlements.IsApplicable(canonical.Subscription))
+                return Code(503, new { error = "Apple subscription identity or state mismatch" });
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
-        var sub = await entitlements.UpsertAsync(subscription, ct);
+        SubscriptionEntity sub;
+        try { sub = await entitlements.UpsertAsync(subscription, ct); }
+        catch (SubscriptionReconciliationRequiredException)
+        {
+            return Code(503, new { code = "subscription_reconciliation_required", error = "Stored subscription needs reconciliation." });
+        }
         if (sub.ClaimedAt != null) await deviceContext.MarkClaimedPurchaseAsync(keyId, ct);
         var (ent, accountId) = await deviceContext.ResolveEntitlementAsync(sub, ct);
 
         var (token, expiresAt) = tokens.CreateDeviceToken(keyId, ent.Status, sub.OriginalTransactionId);
         return Success(new IapVerifyResponse(ToDto(ent, accountId, sub), token, SecondsUntil(expiresAt)));
     }
 
     public async Task<IapResult> TokenAsync(string? keyId, CancellationToken ct)
     {
         if (keyId is null)
             return Code(401, new { error = "invalid device token" });
 
@@ -99,32 +107,39 @@ public sealed class IapService(
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
-        if (!refreshed.Ok || refreshed.Subscription is null)
+        if (!refreshed.Ok || refreshed.Subscription is null
+            || refreshed.Subscription.OriginalTransactionId != sub.OriginalTransactionId
+            || !entitlements.IsApplicable(refreshed.Subscription))
         {
             logger.LogWarning("Apple subscription refresh failed for {Otid}: {Error}", sub.OriginalTransactionId, refreshed.Error);
             return sub;
         }
 
         refreshed.Subscription.DeviceUuid = deviceUuid ?? refreshed.Subscription.DeviceUuid;
-        return await entitlements.UpsertAsync(refreshed.Subscription, ct);
+        try
+        {
+            var updated = await entitlements.UpsertAsync(refreshed.Subscription, ct, relinkDevice: false);
+            return string.IsNullOrWhiteSpace(deviceUuid) ? updated : await entitlements.FindForDeviceAsync(deviceUuid, ct);
+        }
+        catch (SubscriptionReconciliationRequiredException) { return sub; }
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
 
diff --git a/src/Mavrylo.Services/Services/SubscriptionValidationPolicy.cs b/src/Mavrylo.Services/Services/SubscriptionValidationPolicy.cs
new file mode 100644
index 0000000..916d529
--- /dev/null
+++ b/src/Mavrylo.Services/Services/SubscriptionValidationPolicy.cs
@@ -0,0 +1,36 @@
+using Mavrylo.Models;
+
+namespace Mavrylo.Services;
+
+/// <summary>Server-owned purchase boundary, shared by signed projections and cached entitlements.</summary>
+public sealed class SubscriptionValidationPolicy(IConfiguration config, IHostEnvironment host)
+{
+    public string ExpectedEnvironment => Option("Environment") ?? "Production";
+
+    public bool AllowsEnvironment(string? environment)
+    {
+        if (!string.Equals(environment, ExpectedEnvironment, StringComparison.Ordinal)) return false;
+        if (environment is "Production" or "Sandbox") return true;
+        if (!host.IsDevelopment()) return false;
+#if DEBUG
+        return environment is "LocalTesting" or "Xcode";
+#else
+        return false;
+#endif
+    }
+
+    public bool AllowsProduct(string? productId)
+    {
+        if (string.IsNullOrWhiteSpace(productId)) return false;
+        var section = config.GetSection("Apple:AppStoreServer:AllowedProductIds");
+        if (!section.Exists()) section = config.GetSection("Apple:AllowedProductIds");
+        var allowed = section.GetChildren().Select(x => x.Value?.Trim())
+            .Concat((section.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
+        return allowed.Contains(productId, StringComparer.Ordinal);
+    }
+
+    public bool IsApplicable(SubscriptionEntity sub) => !string.IsNullOrWhiteSpace(sub.OriginalTransactionId)
+        && AllowsEnvironment(sub.Environment) && AllowsProduct(sub.ProductId) && sub.ExpiresAt.HasValue;
+
+    private string? Option(string key) => config[$"Apple:AppStoreServer:{key}"]?.Trim() ?? config[$"Apple:{key}"]?.Trim();
+}
diff --git a/tests/AiProtectionFilterTests.cs b/tests/AiProtectionFilterTests.cs
index 644bfdc..c239247 100644
--- a/tests/AiProtectionFilterTests.cs
+++ b/tests/AiProtectionFilterTests.cs
@@ -113,35 +113,35 @@ public class AiProtectionFilterTests
         using var testDb = TestDb.Create();
         AddDevice(testDb.Db, "key-1", "device-1");
         (await testDb.Db.Devices.SingleAsync()).Environment = environment;
         testDb.Db.Subscriptions.Add(new SubscriptionEntity
         {
             OriginalTransactionId = "otid-1",
             DeviceUuid = "device-1",
             ProductId = "com.flashcardai.owlai.premium.monthly",
             ExpiresAt = DateTime.UtcNow.AddDays(-1),
             WasEverPaid = true,
             IsTrial = false,
             AutoRenew = false,
-            Environment = "Sandbox"
+            Environment = "Production"
         });
         await testDb.Db.SaveChangesAsync();
         var filter = CreateFilter(testDb.Db, new AiProtectionOptions { Enabled = protectionEnabled, RequireAssertion = false }, testMode: testMode);
         var context = CreateContext(testDb.Db, """{"word":"hola"}""", DeviceToken("key-1", EntitlementService.Status.Premium));
 
         await filter.OnResourceExecutionAsync(context, () => Executed(context, new OkResult()));
 
         Assert.Equal(expectedStatus, (context.Result as ObjectResult)?.StatusCode ?? 0);
         Assert.Equal(EntitlementService.Status.ExpiredPaid,
-            (await new DeviceContextService(testDb.Db, new EntitlementService(testDb.Db, TimeProvider.System)).ResolveAsync("key-1"))!.Entitlement.Status);
+            (await new DeviceContextService(testDb.Db, new EntitlementService(testDb.Db, TimeProvider.System, TestConfig.SubscriptionPolicy())).ResolveAsync("key-1"))!.Entitlement.Status);
     }
 
     [Fact]
     public async Task FreeSuccessfulRequest_RewindsBodyForAction_ReservesWord_AndIncrementsUsage()
     {
         using var testDb = TestDb.Create();
         AddDevice(testDb.Db, "key-1", "device-1");
         var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-13T12:00:00Z"));
         var filter = CreateFilter(testDb.Db, new AiProtectionOptions { Enabled = true, RequireAssertion = false, FreeDailyQuota = 40 }, time);
         var context = CreateContext(
             testDb.Db,
             """{"word":"Hola","native_language":"en","learning_language":"es"}""",
@@ -333,25 +333,25 @@ public class AiProtectionFilterTests
         AppDbContext db,
         AiProtectionOptions options,
         TimeProvider? timeProvider = null,
         IAppAttestVerifier? verifier = null,
         string? testMode = null)
     {
         timeProvider ??= TimeProvider.System;
         return new AiProtectionFilter(
             Options.Create(options),
             TestConfig.Create(new Dictionary<string, string?> { ["TestMode:Enabled"] = testMode }),
             verifier ?? new FakeAppAttestVerifier { IsDevelopmentBypassEnabled = true },
             new ChallengeService(db, timeProvider),
-            new DeviceContextService(db, new EntitlementService(db, timeProvider)),
+            new DeviceContextService(db, new EntitlementService(db, timeProvider, TestConfig.SubscriptionPolicy())),
             new DeviceWordService(db, timeProvider),
             new AiUsageService(db, timeProvider),
             timeProvider,
             NullLogger<AiProtectionFilter>.Instance);
     }
 
     private static ResourceExecutingContext CreateContext(AppDbContext db, string body, string? bearerToken = null)
     {
         var jsonOptions = new JsonOptions();
         jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
         var mvcOptions = new MvcOptions();
         mvcOptions.InputFormatters.Add(new SystemTextJsonInputFormatter(jsonOptions, NullLogger<SystemTextJsonInputFormatter>.Instance));
diff --git a/tests/AiRequestInterpretationTests.cs b/tests/AiRequestInterpretationTests.cs
index c55f385..379ca91 100644
--- a/tests/AiRequestInterpretationTests.cs
+++ b/tests/AiRequestInterpretationTests.cs
@@ -236,25 +236,25 @@ public sealed class AiRequestInterpretationTests(PostgresContainerFixture postgr
     private static async Task<string> Device(AppDbContext db, HttpClient client, IServiceScope scope)
     {
         var key = Guid.NewGuid().ToString("N");
         db.Devices.Add(new() { KeyId = key, DeviceUuid = key }); await db.SaveChangesAsync();
         client.DefaultRequestHeaders.Authorization = new("Bearer", scope.ServiceProvider.GetRequiredService<JwtTokenService>().CreateDeviceToken(key, "free").Token);
         return key;
     }
 
     private static async Task<string> Account(AppDbContext db, HttpClient client, IServiceScope scope)
     {
         var session = await scope.ServiceProvider.GetRequiredService<AccountService>().SignInAsync(new(Guid.NewGuid().ToString("N"), null, "Owner"), default, allowCreation: true);
         db.Subscriptions.Add(new() { OriginalTransactionId = Guid.NewGuid().ToString("N"), DeviceUuid = "owner-device", OwnerAccountId = session.Profile.Id,
-            ClaimedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(5), WasEverPaid = true, Environment = "Production" });
+            ClaimedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(5), WasEverPaid = true, ProductId = "monthly", Environment = "Production" });
         await db.SaveChangesAsync();
         client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
         return session.Profile.Id;
     }
 
     private sealed class Provider : IAiJsonService
     {
         public int Calls;
         public Task<JsonElement?> CompleteJsonAsync(string operation, string prompt, CancellationToken ct = default)
         {
             Calls++;
             return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new { translations = new[] { "sample" }, translation = "sample", corrected_word = "sample", words = new[] { "sample" }, language_code = "fr", explanation = "sample" }));
diff --git a/tests/AppStoreServerClientTests.cs b/tests/AppStoreServerClientTests.cs
index be71850..f8fc3b2 100644
--- a/tests/AppStoreServerClientTests.cs
+++ b/tests/AppStoreServerClientTests.cs
@@ -22,24 +22,25 @@ public class AppStoreServerClientTests
 #if DEBUG
     public void VerifyTransaction_DevelopmentDecodesUnsignedLocalJws()
     {
         var now = DateTimeOffset.Parse("2026-06-13T12:00:00Z");
         using var client = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}")));
         var appStore = Client(client, "Development", now);
         var jws = JwtFixture.UnsignedJws(new
         {
             originalTransactionId = "otid-1",
             productId = "com.mavrylo.monthly",
             expiresDate = now.AddDays(7).ToUnixTimeMilliseconds(),
             offerType = 1,
+            type = "Auto-Renewable Subscription",
             environment = "LocalTesting",
             appAccountToken = "device-from-token"
         });
 
         var result = appStore.VerifyTransaction(jws, "device-fallback");
 
         Assert.True(result.Ok);
         Assert.False(result.SignatureVerified);
         Assert.Equal("otid-1", result.Subscription?.OriginalTransactionId);
         Assert.Equal("device-from-token", result.Subscription?.DeviceUuid);
         Assert.True(result.Subscription?.IsTrial);
         Assert.False(result.Subscription?.WasEverPaid);
@@ -72,49 +73,141 @@ public class AppStoreServerClientTests
     {
         using var client = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}")));
         var appStore = Client(client, "Production", DateTimeOffset.Parse("2026-06-13T12:00:00Z"));
         var jws = JwtFixture.UnsignedJws(new { originalTransactionId = "otid", productId = "product" });
 
         var result = appStore.VerifyTransaction(jws, "device");
 
         Assert.False(result.Ok);
         Assert.Contains("ES256", result.Error);
     }
 
 #if DEBUG
+    [Theory]
+    [InlineData(2, true, false)]
+    [InlineData(3, true, false)]
+    [InlineData(4, false, false)]
+    [InlineData(4, true, true)]
+    [InlineData(5, true, false)]
+    [InlineData(0, true, false)]
+    public async Task CanonicalStatusCannotGainAccessFromInconsistentExpiryOrRenewal(int status, bool renewal, bool active)
+    {
+        var now = DateTimeOffset.UtcNow;
+        var signed = JwtFixture.UnsignedJws(new { originalTransactionId = "A", productId = "monthly", environment = "LocalTesting", expiresDate = now.AddDays(1).ToUnixTimeMilliseconds(), type = "Auto-Renewable Subscription" });
+        var signedRenewal = renewal ? JwtFixture.UnsignedJws(new { originalTransactionId = "A", productId = "monthly", environment = "LocalTesting", gracePeriodExpiresDate = now.AddDays(3).ToUnixTimeMilliseconds() }) : null;
+        var json = System.Text.Json.JsonSerializer.Serialize(new { data = new[] { new { lastTransactions = new[] { new { originalTransactionId = "A", status, signedTransactionInfo = signed, signedRenewalInfo = signedRenewal } } } } });
+        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
+        using var http = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(json)));
+        var client = ConfiguredClient(http, now, key.ExportPkcs8PrivateKeyPem());
+        var result = await client.RefreshSubscriptionAsync("A");
+        Assert.Equal(active, result.Ok && AccountEntitlementService.IsActive(EntitlementService.ComputeStatus(result.Subscription!, now.UtcDateTime)));
+    }
+
+    [Fact]
+    public async Task NetworkOutageReturnsRefreshFailureInsteadOfThrowing()
+    {
+        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
+        using var http = new HttpClient(new FakeHttpMessageHandler(_ => throw new HttpRequestException("offline")));
+        Assert.False((await ConfiguredClient(http, DateTimeOffset.UtcNow, key.ExportPkcs8PrivateKeyPem()).RefreshSubscriptionAsync("A")).Ok);
+    }
+
+    private static AppStoreServerClient ConfiguredClient(HttpClient http, DateTimeOffset now, string key) =>
+        ClientWith(http, "Development", now, new Dictionary<string, string?>
+        { ["Apple:AppStoreServer:IssuerId"] = "test", ["Apple:AppStoreServer:KeyId"] = "test", ["Apple:AppStoreServer:BundleId"] = "test", ["Apple:AppStoreServer:PrivateKey"] = key });
+
+    [Theory]
+    [InlineData("missing-expiry")]
+    [InlineData("missing-environment")]
+    [InlineData("wrong-type")]
+    public void VerifyTransaction_RejectsIncompleteSubscription(string defect)
+    {
+        var now = DateTimeOffset.UtcNow;
+        using var http = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}")));
+        var payload = new Dictionary<string, object?>
+        {
+            ["originalTransactionId"] = "A", ["productId"] = "monthly", ["environment"] = "LocalTesting",
+            ["expiresDate"] = now.AddDays(1).ToUnixTimeMilliseconds(), ["type"] = "Auto-Renewable Subscription"
+        };
+        if (defect == "missing-expiry") payload.Remove("expiresDate");
+        if (defect == "missing-environment") payload.Remove("environment");
+        if (defect == "wrong-type") payload["type"] = "Consumable";
+        Assert.False(Client(http, "Development", now).VerifyTransaction(JwtFixture.UnsignedJws(payload), "device").Ok);
+    }
+
+    [Theory]
+    [InlineData(false)]
+    [InlineData(true)]
+    public async Task SubscriptionStatusSelectsRequestedOriginalBeforeRankingAndChecksSignedIdentity(bool mismatchedSignedIdentity)
+    {
+        var now = DateTimeOffset.UtcNow;
+        string Tx(string id) => JwtFixture.UnsignedJws(new
+        { originalTransactionId = id, productId = "monthly", environment = "LocalTesting", expiresDate = now.AddDays(5).ToUnixTimeMilliseconds(), type = "Auto-Renewable Subscription" });
+        var response = System.Text.Json.JsonSerializer.Serialize(new { data = new[] { new { lastTransactions = new[]
+        {
+            new { originalTransactionId = "B", status = 1, signedTransactionInfo = Tx("B") },
+            new { originalTransactionId = "A", status = 5, signedTransactionInfo = Tx(mismatchedSignedIdentity ? "B" : "A") }
+        } } } });
+        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
+        using var http = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(response)));
+        var client = ClientWith(http, "Development", now, new Dictionary<string, string?>
+        { ["Apple:AppStoreServer:IssuerId"] = "test", ["Apple:AppStoreServer:KeyId"] = "test", ["Apple:AppStoreServer:BundleId"] = "test", ["Apple:AppStoreServer:PrivateKey"] = key.ExportPkcs8PrivateKeyPem() });
+        var result = await client.RefreshSubscriptionAsync("A");
+        if (mismatchedSignedIdentity) Assert.False(result.Ok);
+        else { Assert.True(result.Ok, result.Error); Assert.Equal("A", result.Subscription!.OriginalTransactionId); Assert.NotNull(result.Subscription.RevokedAt); }
+    }
+
+    [Theory]
+    [InlineData("other", "LocalTesting", "monthly")]
+    [InlineData("A", "Sandbox", "monthly")]
+    [InlineData("A", "LocalTesting", "unknown")]
+    public void RenewalCannotExtendAnotherIdentityEnvironmentOrProduct(string id, string environment, string product)
+    {
+        var now = DateTimeOffset.UtcNow;
+        using var http = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}")));
+        var client = ClientWith(http, "Development", now, new Dictionary<string, string?> { ["Apple:AppStoreServer:AllowedProductIds"] = "monthly" });
+        var sub = new SubscriptionEntity { OriginalTransactionId = "A", ProductId = "monthly", Environment = "LocalTesting", ExpiresAt = now.AddDays(-1).UtcDateTime };
+        var renewal = JwtFixture.UnsignedJws(new { originalTransactionId = id, environment, productId = product, gracePeriodExpiresDate = now.AddDays(5).ToUnixTimeMilliseconds() });
+        client.ApplyRenewalInfo(sub, renewal);
+        Assert.Equal(now.AddDays(-1).UtcDateTime, sub.ExpiresAt); Assert.Equal("monthly", sub.ProductId);
+    }
+
     [Fact]
     public void ApplyRenewalInfo_UsesGraceExpiryAndAutoRenewStatus()
     {
         var now = DateTimeOffset.Parse("2026-06-13T12:00:00Z");
         using var client = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}")));
         var appStore = Client(client, "Development", now);
         var sub = new SubscriptionEntity
         {
             OriginalTransactionId = "otid",
-            ProductId = "old",
+            ProductId = "monthly",
+            Environment = "LocalTesting",
             ExpiresAt = now.AddDays(1).UtcDateTime,
             AutoRenew = true,
             Status = EntitlementService.Status.Premium
         };
         var renewalJws = JwtFixture.UnsignedJws(new
         {
             autoRenewStatus = 0,
+            originalTransactionId = "otid",
+            environment = "LocalTesting",
             gracePeriodExpiresDate = now.AddDays(3).ToUnixTimeMilliseconds(),
-            productId = "new-product"
+            productId = "monthly",
+            autoRenewProductId = "future-product"
         });
 
         appStore.ApplyRenewalInfo(sub, renewalJws);
 
         Assert.False(sub.AutoRenew);
-        Assert.Equal("new-product", sub.ProductId);
+        Assert.Equal("monthly", sub.ProductId);
         Assert.Equal(EntitlementService.Status.Grace, sub.Status);
         Assert.Equal(now.AddDays(3).UtcDateTime, sub.ExpiresAt);
     }
 #endif
 
     [Fact]
     public void AppleJwsDecode_ReturnsHelpfulFailuresForMalformedPayloads()
     {
         Assert.Contains("3 parts", AppleJws.Decode("not-a-jws", verifySignature: false).Error);
         Assert.Contains("payload parse failed", AppleJws.Decode("a.b.c", verifySignature: false).Error);
     }
 
@@ -165,20 +258,24 @@ public class AppStoreServerClientTests
         Assert.False(result.Ok);
         Assert.Contains("bundleId", result.Error);
     }
 #endif
 
     private static AppStoreServerClient Client(HttpClient http, string environment, DateTimeOffset now) =>
         ClientWith(http, environment, now, null);
 
     private static AppStoreServerClient ClientWith(
         HttpClient http,
         string environment,
         DateTimeOffset now,
-        IDictionary<string, string?>? configOverrides) =>
-        new(
+        IDictionary<string, string?>? configOverrides)
+    {
+        var values = new Dictionary<string, string?> { ["Apple:AppStoreServer:Environment"] = environment == "Development" ? "LocalTesting" : "Production" };
+        if (configOverrides != null) foreach (var pair in configOverrides) values[pair.Key] = pair.Value;
+        return new(
             http,
-            TestConfig.Create(configOverrides),
+            TestConfig.Create(values),
             new FakeEnvironment(environment),
             NullLogger<AppStoreServerClient>.Instance,
             new ManualTimeProvider(now));
+    }
 }
diff --git a/tests/AppleJwsTests.cs b/tests/AppleJwsTests.cs
new file mode 100644
index 0000000..19fa1e7
--- /dev/null
+++ b/tests/AppleJwsTests.cs
@@ -0,0 +1,89 @@
+using System.Security.Cryptography;
+using System.Security.Cryptography.X509Certificates;
+using System.Text;
+using System.Text.Json;
+using Mavrylo.Services;
+using Xunit;
+
+namespace Mavrylo.Tests;
+
+public sealed class AppleJwsTests
+{
+    private static readonly DateTime EffectiveDate = DateTimeOffset.FromUnixTimeMilliseconds(1761962975000).UtcDateTime;
+
+    [Theory]
+    [InlineData("LEAF_CERT", "INTERMEDIATE_CA", "ROOT_CA", true)]
+    [InlineData("REAL_APPLE_SIGNING_CERTIFICATE", "REAL_APPLE_INTERMEDIATE", "REAL_APPLE_ROOT", true)]
+    [InlineData("LEAF_CERT_INVALID_OID", "INTERMEDIATE_CA", "ROOT_CA", false)]
+    [InlineData("LEAF_CERT_FOR_INTERMEDIATE_CA_INVALID_OID", "INTERMEDIATE_CA_INVALID_OID", "ROOT_CA", false)]
+    [InlineData("LEAF_CERT", "INTERMEDIATE_CA", "REAL_APPLE_ROOT", false)]
+    public void OfficialAppleChainVectorsValidateTrustAndSigningPurpose(string leafName, string intermediateName, string rootName, bool valid)
+    {
+        using var leaf = Certificate(leafName); using var intermediate = Certificate(intermediateName); using var root = Certificate(rootName);
+        Assert.Equal(valid, AppleJws.VerifyCertificateChain(leaf, [intermediate], root, EffectiveDate, out _));
+    }
+
+    [Theory]
+    [InlineData(2020)]
+    [InlineData(2035)]
+    public void OfficialChainOutsideSigningTimeValidityIsRejected(int year)
+    {
+        using var leaf = Certificate("LEAF_CERT"); using var intermediate = Certificate("INTERMEDIATE_CA"); using var root = Certificate("ROOT_CA");
+        Assert.False(AppleJws.VerifyCertificateChain(leaf, [intermediate], root, new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc), out _));
+    }
+
+    [Theory]
+    [InlineData("transactionInfo")]
+    [InlineData("renewalInfo")]
+    [InlineData("testNotification")]
+    public void OfficialSignedFixturesPassOnlyWithTheirTestRoot(string name)
+    {
+        using var root = X509CertificateLoader.LoadCertificate(File.ReadAllBytes(Path.Combine(FixturePath, "testCA.der")));
+        var jws = File.ReadAllText(Path.Combine(FixturePath, name)).Trim();
+        var decoded = AppleJws.DecodeCore(jws, true, root);
+        Assert.True(decoded.Ok, decoded.Error); Assert.True(decoded.SignatureVerified);
+        Assert.False(AppleJws.Decode(jws, true).Ok); // Runtime entry point never trusts fixtures.
+        var parts = jws.Split('.');
+        parts[1] = Base64Url.Encode(Encoding.UTF8.GetBytes("{\"environment\":\"Production\",\"signedDate\":1672956154000}"));
+        Assert.False(AppleJws.DecodeCore(string.Join('.', parts), true, root).Ok);
+    }
+
+    [Fact]
+    public void OfflineVerificationUsesAuthenticatedSigningDateForExpiredCertificates()
+    {
+        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
+        using var root = Request("CN=Root", rootKey, true, null).CreateSelfSigned(EffectiveDate.AddYears(-1), EffectiveDate.AddYears(10));
+        using var intermediateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
+        using var intermediateWithoutKey = Request("CN=Intermediate", intermediateKey, true, "1.2.840.113635.100.6.2.1")
+            .Create(root, EffectiveDate.AddDays(-10), EffectiveDate.AddDays(10), [1]);
+        using var intermediate = intermediateWithoutKey.CopyWithPrivateKey(intermediateKey);
+        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
+        using var leaf = Request("CN=Leaf", leafKey, false, "1.2.840.113635.100.6.11.1")
+            .Create(intermediate, EffectiveDate.AddDays(-1), EffectiveDate.AddDays(1), [2]);
+        string Signed(DateTime date)
+        {
+            var header = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", x5c = new[] { Convert.ToBase64String(leaf.RawData), Convert.ToBase64String(intermediate.RawData), Convert.ToBase64String(root.RawData) } }));
+            var payload = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(new { signedDate = new DateTimeOffset(date).ToUnixTimeMilliseconds() }));
+            var content = header + "." + payload;
+            return content + "." + Base64Url.Encode(leafKey.SignData(Encoding.ASCII.GetBytes(content), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
+        }
+        var valid = AppleJws.DecodeCore(Signed(EffectiveDate), true, root);
+        Assert.True(valid.Ok, valid.Error);
+        Assert.False(AppleJws.DecodeCore(Signed(EffectiveDate.AddYears(2)), true, root).Ok);
+    }
+
+    private static CertificateRequest Request(string name, ECDsa key, bool ca, string? purpose)
+    {
+        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256);
+        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, false, 0, true));
+        request.CertificateExtensions.Add(new X509KeyUsageExtension(ca ? X509KeyUsageFlags.KeyCertSign : X509KeyUsageFlags.DigitalSignature, true));
+        if (purpose != null) request.CertificateExtensions.Add(new X509Extension(purpose, [0x05, 0x00], false));
+        return request;
+    }
+    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Apple");
+    private static X509Certificate2 Certificate(string name)
+    {
+        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixturePath, "chain-vectors.json")));
+        return X509CertificateLoader.LoadCertificate(Convert.FromBase64String(doc.RootElement.GetProperty(name + "_BASE64_ENCODED").GetString()!));
+    }
+}
diff --git a/tests/DeviceWordsControllerTests.cs b/tests/DeviceWordsControllerTests.cs
index 018555d..016abf4 100644
--- a/tests/DeviceWordsControllerTests.cs
+++ b/tests/DeviceWordsControllerTests.cs
@@ -21,25 +21,25 @@ public class DeviceWordsControllerTests
     public async Task Upsert_EleventhWord_OnlyTestModeCanBypass(string environment, string? testMode, bool accepted)
     {
         using var testDb = TestDb.Create();
         testDb.Db.Devices.Add(new DeviceEntity { KeyId = "key", DeviceUuid = "device", Environment = environment });
         // Neither the authenticated development key nor another key can grant test access.
         testDb.Db.Devices.Add(new DeviceEntity { KeyId = "other", DeviceUuid = "device", Environment = "development" });
         await testDb.Db.SaveChangesAsync();
         var config = TestConfig.Create(new Dictionary<string, string?> { ["TestMode:Enabled"] = testMode });
         var words = new DeviceWordService(testDb.Db, TimeProvider.System, config);
         for (var i = 0; i < 10; i++)
             await words.UpsertAsync("device", Request($"word-{i}"), EntitlementService.Status.Free, CancellationToken.None);
         var controller = new DeviceWordsController(words,
-            new DeviceContextService(testDb.Db, new EntitlementService(testDb.Db, TimeProvider.System)), config)
+            new DeviceContextService(testDb.Db, new EntitlementService(testDb.Db, TimeProvider.System, TestConfig.SubscriptionPolicy())), config)
         {
             ControllerContext = new ControllerContext
             {
                 HttpContext = new DefaultHttpContext
                 {
                     User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("keyId", "key")], "test"))
                 }
             }
         };
 
         var result = Assert.IsAssignableFrom<ObjectResult>(await controller.Upsert(Request("eleventh"), CancellationToken.None));
 
diff --git a/tests/EntitlementServiceTests.cs b/tests/EntitlementServiceTests.cs
index cb69965..5c537e7 100644
--- a/tests/EntitlementServiceTests.cs
+++ b/tests/EntitlementServiceTests.cs
@@ -1,25 +1,25 @@
 using Mavrylo.Models;
 using Mavrylo.Services;
 using Mavrylo.Tests.TestSupport;
 using Xunit;
 
 namespace Mavrylo.Tests;
 
 public class EntitlementServiceTests
 {
     private static readonly DateTime Now = new(2026, 6, 13, 12, 0, 0, DateTimeKind.Utc);
 
     [Theory]
-    [InlineData(null, false, false, null, "premium")]
+    [InlineData(null, false, false, null, "invalid_subscription")]
     [InlineData(1, true, false, null, "trial")]
     [InlineData(1, false, true, null, "premium")]
     [InlineData(1, false, true, "grace", "grace")]
     [InlineData(-1, true, false, null, "expired_trial")]
     [InlineData(-1, false, true, null, "expired_paid")]
     [InlineData(1, false, true, "revoked", "revoked")]
     public void ComputeStatus_CoversCanonicalStates(int? expiryDays, bool isTrial, bool wasEverPaid, string? marker, string expected)
     {
         var sub = new SubscriptionEntity
         {
             OriginalTransactionId = "otid",
             ProductId = "product",
@@ -29,25 +29,25 @@ public class EntitlementServiceTests
             Status = marker == "grace" ? EntitlementService.Status.Grace : EntitlementService.Status.Free,
             RevokedAt = marker == "revoked" ? Now : null
         };
 
         Assert.Equal(expected, EntitlementService.ComputeStatus(sub, Now));
     }
 
     [Fact]
     public async Task Upsert_KeepsWasEverPaidSticky_AndRelinksDevice()
     {
         using var testDb = TestDb.Create();
         var time = new ManualTimeProvider(new DateTimeOffset(Now));
-        var service = new EntitlementService(testDb.Db, time);
+        var service = new EntitlementService(testDb.Db, time, TestConfig.SubscriptionPolicy());
 
         await service.UpsertAsync(new SubscriptionEntity
         {
             OriginalTransactionId = "otid",
             DeviceUuid = "device-a",
             ProductId = "monthly",
             ExpiresAt = Now.AddDays(10),
             IsTrial = false,
             WasEverPaid = true
         });
 
         await service.UpsertAsync(new SubscriptionEntity
diff --git a/tests/Fixtures/Apple/LICENSE.txt b/tests/Fixtures/Apple/LICENSE.txt
new file mode 100644
index 0000000..0525687
--- /dev/null
+++ b/tests/Fixtures/Apple/LICENSE.txt
@@ -0,0 +1,7 @@
+Copyright 2023 Apple Inc.
+
+Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
\ No newline at end of file
diff --git a/tests/Fixtures/Apple/README.md b/tests/Fixtures/Apple/README.md
new file mode 100644
index 0000000..61f80ed
--- /dev/null
+++ b/tests/Fixtures/Apple/README.md
@@ -0,0 +1,9 @@
+# Apple verification reference fixtures
+
+Pinned upstream: apple/app-store-server-library-node commit `bb0c0f874494321ea2d005329c3dc2188e893d41`.
+
+- `chain-vectors.json` extracts certificate constants unchanged from [jws_verification.test.ts](https://github.com/apple/app-store-server-library-node/blob/bb0c0f874494321ea2d005329c3dc2188e893d41/tests/unit-tests/jws_verification.test.ts).
+- `testCA.der`, `transactionInfo`, `renewalInfo`, and `testNotification` are unmodified files from [tests/resources](https://github.com/apple/app-store-server-library-node/tree/bb0c0f874494321ea2d005329c3dc2188e893d41/tests/resources).
+- `LICENSE.txt` is the upstream MIT license; copyright Apple Inc. 2023.
+
+These mock payloads do not represent purchases. Real Apple chain constants test trust/purpose compatibility, not physical StoreKit acceptance. The fixed reference effective date is Unix milliseconds `1761962975000`. Test roots are accessible only through internal verification methods; the public runtime decoder always pins the embedded Apple Root G3. Verification is offline at authenticated signedDate; online OCSP is not implemented or claimed equivalent.
diff --git a/tests/Fixtures/Apple/chain-vectors.json b/tests/Fixtures/Apple/chain-vectors.json
new file mode 100644
index 0000000..ff847fd
--- /dev/null
+++ b/tests/Fixtures/Apple/chain-vectors.json
@@ -0,0 +1,12 @@
+{
+  "LEAF_CERT_FOR_INTERMEDIATE_CA_INVALID_OID_BASE64_ENCODED": "MIIBnzCCAUagAwIBAgIBDjAKBggqhkjOPQQDAzBFMQswCQYDVQQGEwJVUzELMAkGA1UECAwCQ0ExEjAQBgNVBAcMCUN1cGVydGlubzEVMBMGA1UECgwMSW50ZXJtZWRpYXRlMB4XDTIzMDEwNTIxMzY1OFoXDTMzMDEwMTIxMzY1OFowPTELMAkGA1UEBhMCVVMxCzAJBgNVBAgMAkNBMRIwEAYDVQQHDAlDdXBlcnRpbm8xDTALBgNVBAoMBExlYWYwWTATBgcqhkjOPQIBBggqhkjOPQMBBwNCAATitYHEaYVuc8g9AjTOwErMvGyPykPa+puvTI8hJTHZZDLGas2qX1+ErxgQTJgVXv76nmLhhRJH+j25AiAI8iGsoy8wLTAJBgNVHRMEAjAAMA4GA1UdDwEB/wQEAwIHgDAQBgoqhkiG92NkBgsBBAIFADAKBggqhkjOPQQDAwNHADBEAiAUAs+gzYOsEXDwQquvHYbcVymyNqDtGw9BnUFp2YLuuAIgXxQ3Ie9YU0cMqkeaFd+lyo0asv9eyzk6stwjeIeOtTU=",
+  "INTERMEDIATE_CA_INVALID_OID_BASE64_ENCODED": "MIIBnjCCAUWgAwIBAgIBDTAKBggqhkjOPQQDAzA2MQswCQYDVQQGEwJVUzETMBEGA1UECAwKQ2FsaWZvcm5pYTESMBAGA1UEBwwJQ3VwZXJ0aW5vMB4XDTIzMDEwNTIxMzYxNFoXDTMzMDEwMTIxMzYxNFowRTELMAkGA1UEBhMCVVMxCzAJBgNVBAgMAkNBMRIwEAYDVQQHDAlDdXBlcnRpbm8xFTATBgNVBAoMDEludGVybWVkaWF0ZTBZMBMGByqGSM49AgEGCCqGSM49AwEHA0IABBUN5V9rKjfRiMAIojEA0Av5Mp0oF+O0cL4gzrTF178inUHugj7Et46NrkQ7hKgMVnjogq45Q1rMs+cMHVNILWqjNTAzMA8GA1UdEwQIMAYBAf8CAQAwDgYDVR0PAQH/BAQDAgEGMBAGCiqGSIb3Y2QGAgIEAgUAMAoGCCqGSM49BAMDA0cAMEQCIFROtTE+RQpKxNXETFsf7Mc0h+5IAsxxo/X6oCC/c33qAiAmC5rn5yCOOEjTY4R1H1QcQVh+eUwCl13NbQxWCuwxxA==",
+  "LEAF_CERT_BASE64_ENCODED": "MIIBoDCCAUagAwIBAgIBDDAKBggqhkjOPQQDAzBFMQswCQYDVQQGEwJVUzELMAkGA1UECAwCQ0ExEjAQBgNVBAcMCUN1cGVydGlubzEVMBMGA1UECgwMSW50ZXJtZWRpYXRlMB4XDTIzMDEwNTIxMzEzNFoXDTMzMDEwMTIxMzEzNFowPTELMAkGA1UEBhMCVVMxCzAJBgNVBAgMAkNBMRIwEAYDVQQHDAlDdXBlcnRpbm8xDTALBgNVBAoMBExlYWYwWTATBgcqhkjOPQIBBggqhkjOPQMBBwNCAATitYHEaYVuc8g9AjTOwErMvGyPykPa+puvTI8hJTHZZDLGas2qX1+ErxgQTJgVXv76nmLhhRJH+j25AiAI8iGsoy8wLTAJBgNVHRMEAjAAMA4GA1UdDwEB/wQEAwIHgDAQBgoqhkiG92NkBgsBBAIFADAKBggqhkjOPQQDAwNIADBFAiBX4c+T0Fp5nJ5QRClRfu5PSByRvNPtuaTsk0vPB3WAIAIhANgaauAj/YP9s0AkEhyJhxQO/6Q2zouZ+H1CIOehnMzQ",
+  "ROOT_CA_BASE64_ENCODED": "MIIBgjCCASmgAwIBAgIJALUc5ALiH5pbMAoGCCqGSM49BAMDMDYxCzAJBgNVBAYTAlVTMRMwEQYDVQQIDApDYWxpZm9ybmlhMRIwEAYDVQQHDAlDdXBlcnRpbm8wHhcNMjMwMTA1MjEzMDIyWhcNMzMwMTAyMjEzMDIyWjA2MQswCQYDVQQGEwJVUzETMBEGA1UECAwKQ2FsaWZvcm5pYTESMBAGA1UEBwwJQ3VwZXJ0aW5vMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEc+/Bl+gospo6tf9Z7io5tdKdrlN1YdVnqEhEDXDShzdAJPQijamXIMHf8xWWTa1zgoYTxOKpbuJtDplz1XriTaMgMB4wDAYDVR0TBAUwAwEB/zAOBgNVHQ8BAf8EBAMCAQYwCgYIKoZIzj0EAwMDRwAwRAIgemWQXnMAdTad2JDJWng9U4uBBL5mA7WI05H7oH7c6iQCIHiRqMjNfzUAyiu9h6rOU/K+iTR0I/3Y/NSWsXHX+acc",
+  "LEAF_CERT_INVALID_OID_BASE64_ENCODED": "MIIBoDCCAUagAwIBAgIBDzAKBggqhkjOPQQDAzBFMQswCQYDVQQGEwJVUzELMAkGA1UECAwCQ0ExEjAQBgNVBAcMCUN1cGVydGlubzEVMBMGA1UECgwMSW50ZXJtZWRpYXRlMB4XDTIzMDEwNTIxMzczMVoXDTMzMDEwMTIxMzczMVowPTELMAkGA1UEBhMCVVMxCzAJBgNVBAgMAkNBMRIwEAYDVQQHDAlDdXBlcnRpbm8xDTALBgNVBAoMBExlYWYwWTATBgcqhkjOPQIBBggqhkjOPQMBBwNCAATitYHEaYVuc8g9AjTOwErMvGyPykPa+puvTI8hJTHZZDLGas2qX1+ErxgQTJgVXv76nmLhhRJH+j25AiAI8iGsoy8wLTAJBgNVHRMEAjAAMA4GA1UdDwEB/wQEAwIHgDAQBgoqhkiG92NkBgsCBAIFADAKBggqhkjOPQQDAwNIADBFAiAb+7S3i//bSGy7skJY9+D4VgcQLKFeYfIMSrUCmdrFqwIhAIMVwzD1RrxPRtJyiOCXLyibIvwcY+VS73HYfk0O9lgz",
+  "REAL_APPLE_INTERMEDIATE_BASE64_ENCODED": "MIIDFjCCApygAwIBAgIUIsGhRwp0c2nvU4YSycafPTjzbNcwCgYIKoZIzj0EAwMwZzEbMBkGA1UEAwwSQXBwbGUgUm9vdCBDQSAtIEczMSYwJAYDVQQLDB1BcHBsZSBDZXJ0aWZpY2F0aW9uIEF1dGhvcml0eTETMBEGA1UECgwKQXBwbGUgSW5jLjELMAkGA1UEBhMCVVMwHhcNMjEwMzE3MjAzNzEwWhcNMzYwMzE5MDAwMDAwWjB1MUQwQgYDVQQDDDtBcHBsZSBXb3JsZHdpZGUgRGV2ZWxvcGVyIFJlbGF0aW9ucyBDZXJ0aWZpY2F0aW9uIEF1dGhvcml0eTELMAkGA1UECwwCRzYxEzARBgNVBAoMCkFwcGxlIEluYy4xCzAJBgNVBAYTAlVTMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEbsQKC94PrlWmZXnXgtxzdVJL8T0SGYngDRGpngn3N6PT8JMEb7FDi4bBmPhCnZ3/sq6PF/cGcKXWsL5vOteRhyJ45x3ASP7cOB+aao90fcpxSv/EZFbniAbNgZGhIhpIo4H6MIH3MBIGA1UdEwEB/wQIMAYBAf8CAQAwHwYDVR0jBBgwFoAUu7DeoVgziJqkipnevr3rr9rLJKswRgYIKwYBBQUHAQEEOjA4MDYGCCsGAQUFBzABhipodHRwOi8vb2NzcC5hcHBsZS5jb20vb2NzcDAzLWFwcGxlcm9vdGNhZzMwNwYDVR0fBDAwLjAsoCqgKIYmaHR0cDovL2NybC5hcHBsZS5jb20vYXBwbGVyb290Y2FnMy5jcmwwHQYDVR0OBBYEFD8vlCNR01DJmig97bB85c+lkGKZMA4GA1UdDwEB/wQEAwIBBjAQBgoqhkiG92NkBgIBBAIFADAKBggqhkjOPQQDAwNoADBlAjBAXhSq5IyKogMCPtw490BaB677CaEGJXufQB/EqZGd6CSjiCtOnuMTbXVXmxxcxfkCMQDTSPxarZXvNrkxU3TkUMI33yzvFVVRT4wxWJC994OsdcZ4+RGNsYDyR5gmdr0nDGg=",
+  "LEAF_CERT_PUBLIC_KEY_BASE64_ENCODED": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE4rWBxGmFbnPIPQI0zsBKzLxsj8pD2vqbr0yPISUx2WQyxmrNql9fhK8YEEyYFV7++p5i4YUSR/o9uQIgCPIhrA==",
+  "REAL_APPLE_ROOT_BASE64_ENCODED": "MIICQzCCAcmgAwIBAgIILcX8iNLFS5UwCgYIKoZIzj0EAwMwZzEbMBkGA1UEAwwSQXBwbGUgUm9vdCBDQSAtIEczMSYwJAYDVQQLDB1BcHBsZSBDZXJ0aWZpY2F0aW9uIEF1dGhvcml0eTETMBEGA1UECgwKQXBwbGUgSW5jLjELMAkGA1UEBhMCVVMwHhcNMTQwNDMwMTgxOTA2WhcNMzkwNDMwMTgxOTA2WjBnMRswGQYDVQQDDBJBcHBsZSBSb290IENBIC0gRzMxJjAkBgNVBAsMHUFwcGxlIENlcnRpZmljYXRpb24gQXV0aG9yaXR5MRMwEQYDVQQKDApBcHBsZSBJbmMuMQswCQYDVQQGEwJVUzB2MBAGByqGSM49AgEGBSuBBAAiA2IABJjpLz1AcqTtkyJygRMc3RCV8cWjTnHcFBbZDuWmBSp3ZHtfTjjTuxxEtX/1H7YyYl3J6YRbTzBPEVoA/VhYDKX1DyxNB0cTddqXl5dvMVztK517IDvYuVTZXpmkOlEKMaNCMEAwHQYDVR0OBBYEFLuw3qFYM4iapIqZ3r6966/ayySrMA8GA1UdEwEB/wQFMAMBAf8wDgYDVR0PAQH/BAQDAgEGMAoGCCqGSM49BAMDA2gAMGUCMQCD6cHEFl4aXTQY2e3v9GwOAEZLuN+yRhHFD/3meoyhpmvOwgPUnPWTxnS4at+qIxUCMG1mihDK1A3UT82NQz60imOlM27jbdoXt2QfyFMm+YhidDkLF1vLUagM6BgD56KyKA==",
+  "REAL_APPLE_SIGNING_CERTIFICATE_BASE64_ENCODED": "MIIEMTCCA7agAwIBAgIQR8KHzdn554Z/UoradNx9tzAKBggqhkjOPQQDAzB1MUQwQgYDVQQDDDtBcHBsZSBXb3JsZHdpZGUgRGV2ZWxvcGVyIFJlbGF0aW9ucyBDZXJ0aWZpY2F0aW9uIEF1dGhvcml0eTELMAkGA1UECwwCRzYxEzARBgNVBAoMCkFwcGxlIEluYy4xCzAJBgNVBAYTAlVTMB4XDTI1MDkxOTE5NDQ1MVoXDTI3MTAxMzE3NDcyM1owgZIxQDA+BgNVBAMMN1Byb2QgRUNDIE1hYyBBcHAgU3RvcmUgYW5kIGlUdW5lcyBTdG9yZSBSZWNlaXB0IFNpZ25pbmcxLDAqBgNVBAsMI0FwcGxlIFdvcmxkd2lkZSBEZXZlbG9wZXIgUmVsYXRpb25zMRMwEQYDVQQKDApBcHBsZSBJbmMuMQswCQYDVQQGEwJVUzBZMBMGByqGSM49AgEGCCqGSM49AwEHA0IABNnVvhcv7iT+7Ex5tBMBgrQspHzIsXRi0Yxfek7lv8wEmj/bHiWtNwJqc2BoHzsQiEjP7KFIIKg4Y8y0/nynuAmjggIIMIICBDAMBgNVHRMBAf8EAjAAMB8GA1UdIwQYMBaAFD8vlCNR01DJmig97bB85c+lkGKZMHAGCCsGAQUFBwEBBGQwYjAtBggrBgEFBQcwAoYhaHR0cDovL2NlcnRzLmFwcGxlLmNvbS93d2RyZzYuZGVyMDEGCCsGAQUFBzABhiVodHRwOi8vb2NzcC5hcHBsZS5jb20vb2NzcDAzLXd3ZHJnNjAyMIIBHgYDVR0gBIIBFTCCAREwggENBgoqhkiG92NkBQYBMIH+MIHDBggrBgEFBQcCAjCBtgyBs1JlbGlhbmNlIG9uIHRoaXMgY2VydGlmaWNhdGUgYnkgYW55IHBhcnR5IGFzc3VtZXMgYWNjZXB0YW5jZSBvZiB0aGUgdGhlbiBhcHBsaWNhYmxlIHN0YW5kYXJkIHRlcm1zIGFuZCBjb25kaXRpb25zIG9mIHVzZSwgY2VydGlmaWNhdGUgcG9saWN5IGFuZCBjZXJ0aWZpY2F0aW9uIHByYWN0aWNlIHN0YXRlbWVudHMuMDYGCCsGAQUFBwIBFipodHRwOi8vd3d3LmFwcGxlLmNvbS9jZXJ0aWZpY2F0ZWF1dGhvcml0eS8wHQYDVR0OBBYEFIFioG4wMMVA1ku9zJmGNPAVn3eqMA4GA1UdDwEB/wQEAwIHgDAQBgoqhkiG92NkBgsBBAIFADAKBggqhkjOPQQDAwNpADBmAjEA+qXnREC7hXIWVLsLxznjRpIzPf7VHz9V/CTm8+LJlrQepnmcPvGLNcX6XPnlcgLAAjEA5IjNZKgg5pQ79knF4IbTXdKv8vutIDMXDmjPVT3dGvFtsGRwXOywR2kZCdSrfeot",
+  "INTERMEDIATE_CA_BASE64_ENCODED": "MIIBnzCCAUWgAwIBAgIBCzAKBggqhkjOPQQDAzA2MQswCQYDVQQGEwJVUzETMBEGA1UECAwKQ2FsaWZvcm5pYTESMBAGA1UEBwwJQ3VwZXJ0aW5vMB4XDTIzMDEwNTIxMzEwNVoXDTMzMDEwMTIxMzEwNVowRTELMAkGA1UEBhMCVVMxCzAJBgNVBAgMAkNBMRIwEAYDVQQHDAlDdXBlcnRpbm8xFTATBgNVBAoMDEludGVybWVkaWF0ZTBZMBMGByqGSM49AgEGCCqGSM49AwEHA0IABBUN5V9rKjfRiMAIojEA0Av5Mp0oF+O0cL4gzrTF178inUHugj7Et46NrkQ7hKgMVnjogq45Q1rMs+cMHVNILWqjNTAzMA8GA1UdEwQIMAYBAf8CAQAwDgYDVR0PAQH/BAQDAgEGMBAGCiqGSIb3Y2QGAgEEAgUAMAoGCCqGSM49BAMDA0gAMEUCIQCmsIKYs41ullssHX4rVveUT0Z7Is5/hLK1lFPTtun3hAIgc2+2RG5+gNcFVcs+XJeEl4GZ+ojl3ROOmll+ye7dynQ="
+}
diff --git a/tests/Fixtures/Apple/renewalInfo b/tests/Fixtures/Apple/renewalInfo
new file mode 100644
index 0000000..d14a20d
--- /dev/null
+++ b/tests/Fixtures/Apple/renewalInfo
@@ -0,0 +1 @@
+eyJ4NWMiOlsiTUlJQm9EQ0NBVWFnQXdJQkFnSUJEREFLQmdncWhrak9QUVFEQXpCRk1Rc3dDUVlEVlFRR0V3SlZVekVMTUFrR0ExVUVDQXdDUTBFeEVqQVFCZ05WQkFjTUNVTjFjR1Z5ZEdsdWJ6RVZNQk1HQTFVRUNnd01TVzUwWlhKdFpXUnBZWFJsTUI0WERUSXpNREV3TlRJeE16RXpORm9YRFRNek1ERXdNVEl4TXpFek5Gb3dQVEVMTUFrR0ExVUVCaE1DVlZNeEN6QUpCZ05WQkFnTUFrTkJNUkl3RUFZRFZRUUhEQWxEZFhCbGNuUnBibTh4RFRBTEJnTlZCQW9NQkV4bFlXWXdXVEFUQmdjcWhrak9QUUlCQmdncWhrak9QUU1CQndOQ0FBVGl0WUhFYVlWdWM4ZzlBalRPd0VyTXZHeVB5a1BhK3B1dlRJOGhKVEhaWkRMR2FzMnFYMStFcnhnUVRKZ1ZYdjc2bm1MaGhSSkgrajI1QWlBSThpR3NveTh3TFRBSkJnTlZIUk1FQWpBQU1BNEdBMVVkRHdFQi93UUVBd0lIZ0RBUUJnb3Foa2lHOTJOa0Jnc0JCQUlGQURBS0JnZ3Foa2pPUFFRREF3TklBREJGQWlCWDRjK1QwRnA1bko1UVJDbFJmdTVQU0J5UnZOUHR1YVRzazB2UEIzV0FJQUloQU5nYWF1QWovWVA5czBBa0VoeUpoeFFPLzZRMnpvdVorSDFDSU9laG5NelEiLCJNSUlCbnpDQ0FVV2dBd0lCQWdJQkN6QUtCZ2dxaGtqT1BRUURBekEyTVFzd0NRWURWUVFHRXdKVlV6RVRNQkVHQTFVRUNBd0tRMkZzYVdadmNtNXBZVEVTTUJBR0ExVUVCd3dKUTNWd1pYSjBhVzV2TUI0WERUSXpNREV3TlRJeE16RXdOVm9YRFRNek1ERXdNVEl4TXpFd05Wb3dSVEVMTUFrR0ExVUVCaE1DVlZNeEN6QUpCZ05WQkFnTUFrTkJNUkl3RUFZRFZRUUhEQWxEZFhCbGNuUnBibTh4RlRBVEJnTlZCQW9NREVsdWRHVnliV1ZrYVdGMFpUQlpNQk1HQnlxR1NNNDlBZ0VHQ0NxR1NNNDlBd0VIQTBJQUJCVU41VjlyS2pmUmlNQUlvakVBMEF2NU1wMG9GK08wY0w0Z3pyVEYxNzhpblVIdWdqN0V0NDZOcmtRN2hLZ01WbmpvZ3E0NVExck1zK2NNSFZOSUxXcWpOVEF6TUE4R0ExVWRFd1FJTUFZQkFmOENBUUF3RGdZRFZSMFBBUUgvQkFRREFnRUdNQkFHQ2lxR1NJYjNZMlFHQWdFRUFnVUFNQW9HQ0NxR1NNNDlCQU1EQTBnQU1FVUNJUUNtc0lLWXM0MXVsbHNzSFg0clZ2ZVVUMFo3SXM1L2hMSzFsRlBUdHVuM2hBSWdjMisyUkc1K2dOY0ZWY3MrWEplRWw0R1orb2psM1JPT21sbCt5ZTdkeW5RPSIsIk1JSUJnakNDQVNtZ0F3SUJBZ0lKQUxVYzVBTGlINXBiTUFvR0NDcUdTTTQ5QkFNRE1EWXhDekFKQmdOVkJBWVRBbFZUTVJNd0VRWURWUVFJREFwRFlXeHBabTl5Ym1saE1SSXdFQVlEVlFRSERBbERkWEJsY25ScGJtOHdIaGNOTWpNd01UQTFNakV6TURJeVdoY05Nek13TVRBeU1qRXpNREl5V2pBMk1Rc3dDUVlEVlFRR0V3SlZVekVUTUJFR0ExVUVDQXdLUTJGc2FXWnZjbTVwWVRFU01CQUdBMVVFQnd3SlEzVndaWEowYVc1dk1Ga3dFd1lIS29aSXpqMENBUVlJS29aSXpqMERBUWNEUWdBRWMrL0JsK2dvc3BvNnRmOVo3aW81dGRLZHJsTjFZZFZucUVoRURYRFNoemRBSlBRaWphbVhJTUhmOHhXV1RhMXpnb1lUeE9LcGJ1SnREcGx6MVhyaVRhTWdNQjR3REFZRFZSMFRCQVV3QXdFQi96QU9CZ05WSFE4QkFmOEVCQU1DQVFZd0NnWUlLb1pJemowRUF3TURSd0F3UkFJZ2VtV1FYbk1BZFRhZDJKREpXbmc5VTR1QkJMNW1BN1dJMDVIN29IN2M2aVFDSUhpUnFNak5melVBeWl1OWg2ck9VL0sraVRSMEkvM1kvTlNXc1hIWCthY2MiXSwidHlwIjoiSldUIiwiYWxnIjoiRVMyNTYifQ.eyJlbnZpcm9ubWVudCI6IlNhbmRib3giLCJzaWduZWREYXRlIjoxNjcyOTU2MTU0MDAwfQ.FbK2OL-t6l4892W7fzWyus_g9mIl2CzWLbVt7Kgcnt6zzVulF8bzovgpe0v_y490blROGixy8KDoe2dSU53-Xw
\ No newline at end of file
diff --git a/tests/Fixtures/Apple/testCA.der b/tests/Fixtures/Apple/testCA.der
new file mode 100644
index 0000000..54a42b6
Binary files /dev/null and b/tests/Fixtures/Apple/testCA.der differ
diff --git a/tests/Fixtures/Apple/testNotification b/tests/Fixtures/Apple/testNotification
new file mode 100644
index 0000000..7bb78cf
--- /dev/null
+++ b/tests/Fixtures/Apple/testNotification
@@ -0,0 +1 @@
+eyJ4NWMiOlsiTUlJQm9EQ0NBVWFnQXdJQkFnSUJDekFLQmdncWhrak9QUVFEQWpCTk1Rc3dDUVlEVlFRR0V3SlZVekVUTUJFR0ExVUVDQXdLUTJGc2FXWnZjbTVwWVRFU01CQUdBMVVFQnd3SlEzVndaWEowYVc1dk1SVXdFd1lEVlFRS0RBeEpiblJsY20xbFpHbGhkR1V3SGhjTk1qTXdNVEEwTVRZek56TXhXaGNOTXpJeE1qTXhNVFl6TnpNeFdqQkZNUXN3Q1FZRFZRUUdFd0pWVXpFVE1CRUdBMVVFQ0F3S1EyRnNhV1p2Y201cFlURVNNQkFHQTFVRUJ3d0pRM1Z3WlhKMGFXNXZNUTB3Q3dZRFZRUUtEQVJNWldGbU1Ga3dFd1lIS29aSXpqMENBUVlJS29aSXpqMERBUWNEUWdBRTRyV0J4R21GYm5QSVBRSTB6c0JLekx4c2o4cEQydnFicjB5UElTVXgyV1F5eG1yTnFsOWZoSzhZRUV5WUZWNysrcDVpNFlVU1Ivbzl1UUlnQ1BJaHJLTWZNQjB3Q1FZRFZSMFRCQUl3QURBUUJnb3Foa2lHOTJOa0Jnc0JCQUlUQURBS0JnZ3Foa2pPUFFRREFnTklBREJGQWlFQWtpRVprb0ZNa2o0Z1huK1E5alhRWk1qWjJnbmpaM2FNOE5ZcmdmVFVpdlFDSURKWVowRmFMZTduU0lVMkxXTFRrNXRYVENjNEU4R0pTWWYvc1lSeEVGaWUiLCJNSUlCbHpDQ0FUMmdBd0lCQWdJQkJqQUtCZ2dxaGtqT1BRUURBakEyTVFzd0NRWURWUVFHRXdKVlV6RVRNQkVHQTFVRUNBd0tRMkZzYVdadmNtNXBZVEVTTUJBR0ExVUVCd3dKUTNWd1pYSjBhVzV2TUI0WERUSXpNREV3TkRFMk1qWXdNVm9YRFRNeU1USXpNVEUyTWpZd01Wb3dUVEVMTUFrR0ExVUVCaE1DVlZNeEV6QVJCZ05WQkFnTUNrTmhiR2xtYjNKdWFXRXhFakFRQmdOVkJBY01DVU4xY0dWeWRHbHViekVWTUJNR0ExVUVDZ3dNU1c1MFpYSnRaV1JwWVhSbE1Ga3dFd1lIS29aSXpqMENBUVlJS29aSXpqMERBUWNEUWdBRUZRM2xYMnNxTjlHSXdBaWlNUURRQy9reW5TZ1g0N1J3dmlET3RNWFh2eUtkUWU2Q1BzUzNqbzJ1UkR1RXFBeFdlT2lDcmpsRFdzeXo1d3dkVTBndGFxTWxNQ013RHdZRFZSMFRCQWd3QmdFQi93SUJBREFRQmdvcWhraUc5Mk5rQmdJQkJBSVRBREFLQmdncWhrak9QUVFEQWdOSUFEQkZBaUVBdm56TWNWMjY4Y1JiMS9GcHlWMUVoVDNXRnZPenJCVVdQNi9Ub1RoRmF2TUNJRmJhNXQ2WUt5MFIySkR0eHF0T2pKeTY2bDZWN2QvUHJBRE5wa21JUFcraSIsIk1JSUJYRENDQVFJQ0NRQ2ZqVFVHTERuUjlqQUtCZ2dxaGtqT1BRUURBekEyTVFzd0NRWURWUVFHRXdKVlV6RVRNQkVHQTFVRUNBd0tRMkZzYVdadmNtNXBZVEVTTUJBR0ExVUVCd3dKUTNWd1pYSjBhVzV2TUI0WERUSXpNREV3TkRFMk1qQXpNbG9YRFRNek1ERXdNVEUyTWpBek1sb3dOakVMTUFrR0ExVUVCaE1DVlZNeEV6QVJCZ05WQkFnTUNrTmhiR2xtYjNKdWFXRXhFakFRQmdOVkJBY01DVU4xY0dWeWRHbHViekJaTUJNR0J5cUdTTTQ5QWdFR0NDcUdTTTQ5QXdFSEEwSUFCSFB2d1pmb0tMS2FPclgvV2U0cU9iWFNuYTVUZFdIVlo2aElSQTF3MG9jM1FDVDBJbzJwbHlEQjMvTVZsazJ0YzRLR0U4VGlxVzdpYlE2WmM5VjY0azB3Q2dZSUtvWkl6ajBFQXdNRFNBQXdSUUloQU1USGhXdGJBUU4waFN4SVhjUDRDS3JEQ0gvZ3N4V3B4NmpUWkxUZVorRlBBaUIzNW53azVxMHpjSXBlZnZZSjBNVS95R0dIU1dlejBicTBwRFlVTy9ubUR3PT0iXSwidHlwIjoiSldUIiwiYWxnIjoiRVMyNTYifQ.eyJkYXRhIjp7ImFwcEFwcGxlSWQiOjEyMzQsImVudmlyb25tZW50IjoiU2FuZGJveCIsImJ1bmRsZUlkIjoiY29tLmV4YW1wbGUifSwibm90aWZpY2F0aW9uVVVJRCI6IjlhZDU2YmQyLTBiYzYtNDJlMC1hZjI0LWZkOTk2ZDg3YTFlNiIsInNpZ25lZERhdGUiOjE2ODEzMTQzMjQwMDAsIm5vdGlmaWNhdGlvblR5cGUiOiJURVNUIn0.VVXYwuNm2Y3XsOUva-BozqatRCsDuykA7xIe_CCRw6aIAAxJ1nb2sw871jfZ6dcgNhUuhoZ93hfbc1v_5zB7Og
\ No newline at end of file
diff --git a/tests/Fixtures/Apple/transactionInfo b/tests/Fixtures/Apple/transactionInfo
new file mode 100644
index 0000000..3ddf0b0
--- /dev/null
+++ b/tests/Fixtures/Apple/transactionInfo
@@ -0,0 +1 @@
+eyJ4NWMiOlsiTUlJQm9EQ0NBVWFnQXdJQkFnSUJDekFLQmdncWhrak9QUVFEQWpCTk1Rc3dDUVlEVlFRR0V3SlZVekVUTUJFR0ExVUVDQXdLUTJGc2FXWnZjbTVwWVRFU01CQUdBMVVFQnd3SlEzVndaWEowYVc1dk1SVXdFd1lEVlFRS0RBeEpiblJsY20xbFpHbGhkR1V3SGhjTk1qTXdNVEEwTVRZek56TXhXaGNOTXpJeE1qTXhNVFl6TnpNeFdqQkZNUXN3Q1FZRFZRUUdFd0pWVXpFVE1CRUdBMVVFQ0F3S1EyRnNhV1p2Y201cFlURVNNQkFHQTFVRUJ3d0pRM1Z3WlhKMGFXNXZNUTB3Q3dZRFZRUUtEQVJNWldGbU1Ga3dFd1lIS29aSXpqMENBUVlJS29aSXpqMERBUWNEUWdBRTRyV0J4R21GYm5QSVBRSTB6c0JLekx4c2o4cEQydnFicjB5UElTVXgyV1F5eG1yTnFsOWZoSzhZRUV5WUZWNysrcDVpNFlVU1Ivbzl1UUlnQ1BJaHJLTWZNQjB3Q1FZRFZSMFRCQUl3QURBUUJnb3Foa2lHOTJOa0Jnc0JCQUlUQURBS0JnZ3Foa2pPUFFRREFnTklBREJGQWlFQWtpRVprb0ZNa2o0Z1huK1E5alhRWk1qWjJnbmpaM2FNOE5ZcmdmVFVpdlFDSURKWVowRmFMZTduU0lVMkxXTFRrNXRYVENjNEU4R0pTWWYvc1lSeEVGaWUiLCJNSUlCbHpDQ0FUMmdBd0lCQWdJQkJqQUtCZ2dxaGtqT1BRUURBakEyTVFzd0NRWURWUVFHRXdKVlV6RVRNQkVHQTFVRUNBd0tRMkZzYVdadmNtNXBZVEVTTUJBR0ExVUVCd3dKUTNWd1pYSjBhVzV2TUI0WERUSXpNREV3TkRFMk1qWXdNVm9YRFRNeU1USXpNVEUyTWpZd01Wb3dUVEVMTUFrR0ExVUVCaE1DVlZNeEV6QVJCZ05WQkFnTUNrTmhiR2xtYjNKdWFXRXhFakFRQmdOVkJBY01DVU4xY0dWeWRHbHViekVWTUJNR0ExVUVDZ3dNU1c1MFpYSnRaV1JwWVhSbE1Ga3dFd1lIS29aSXpqMENBUVlJS29aSXpqMERBUWNEUWdBRUZRM2xYMnNxTjlHSXdBaWlNUURRQy9reW5TZ1g0N1J3dmlET3RNWFh2eUtkUWU2Q1BzUzNqbzJ1UkR1RXFBeFdlT2lDcmpsRFdzeXo1d3dkVTBndGFxTWxNQ013RHdZRFZSMFRCQWd3QmdFQi93SUJBREFRQmdvcWhraUc5Mk5rQmdJQkJBSVRBREFLQmdncWhrak9QUVFEQWdOSUFEQkZBaUVBdm56TWNWMjY4Y1JiMS9GcHlWMUVoVDNXRnZPenJCVVdQNi9Ub1RoRmF2TUNJRmJhNXQ2WUt5MFIySkR0eHF0T2pKeTY2bDZWN2QvUHJBRE5wa21JUFcraSIsIk1JSUJYRENDQVFJQ0NRQ2ZqVFVHTERuUjlqQUtCZ2dxaGtqT1BRUURBekEyTVFzd0NRWURWUVFHRXdKVlV6RVRNQkVHQTFVRUNBd0tRMkZzYVdadmNtNXBZVEVTTUJBR0ExVUVCd3dKUTNWd1pYSjBhVzV2TUI0WERUSXpNREV3TkRFMk1qQXpNbG9YRFRNek1ERXdNVEUyTWpBek1sb3dOakVMTUFrR0ExVUVCaE1DVlZNeEV6QVJCZ05WQkFnTUNrTmhiR2xtYjNKdWFXRXhFakFRQmdOVkJBY01DVU4xY0dWeWRHbHViekJaTUJNR0J5cUdTTTQ5QWdFR0NDcUdTTTQ5QXdFSEEwSUFCSFB2d1pmb0tMS2FPclgvV2U0cU9iWFNuYTVUZFdIVlo2aElSQTF3MG9jM1FDVDBJbzJwbHlEQjMvTVZsazJ0YzRLR0U4VGlxVzdpYlE2WmM5VjY0azB3Q2dZSUtvWkl6ajBFQXdNRFNBQXdSUUloQU1USGhXdGJBUU4waFN4SVhjUDRDS3JEQ0gvZ3N4V3B4NmpUWkxUZVorRlBBaUIzNW53azVxMHpjSXBlZnZZSjBNVS95R0dIU1dlejBicTBwRFlVTy9ubUR3PT0iXSwidHlwIjoiSldUIiwiYWxnIjoiRVMyNTYifQ.eyJlbnZpcm9ubWVudCI6IlNhbmRib3giLCJidW5kbGVJZCI6ImNvbS5leGFtcGxlIiwic2lnbmVkRGF0ZSI6MTY3Mjk1NjE1NDAwMH0.PnHWpeIJZ8f2Q218NSGLo_aR0IBEJvC6PxmxKXh-qfYTrZccx2suGl223OSNAX78e4Ylf2yJCG2N-FfU-NIhZQ
\ No newline at end of file
diff --git a/tests/Mavrylo.Tests.csproj b/tests/Mavrylo.Tests.csproj
index 1e087a4..3973791 100644
--- a/tests/Mavrylo.Tests.csproj
+++ b/tests/Mavrylo.Tests.csproj
@@ -13,18 +13,19 @@
     <PackageReference Include="coverlet.collector" Version="6.0.4">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
     <PackageReference Include="xunit" Version="2.9.3" />
     <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
     <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.9" />
   </ItemGroup>
   <ItemGroup>
+    <None Update="Fixtures/Apple/**/*" CopyToOutputDirectory="PreserveNewest" />
     <ProjectReference Include="../Mavrylo.csproj" />
     <ProjectReference Include="../src/Mavrylo.Entities/Mavrylo.Entities.csproj" />
     <ProjectReference Include="../src/Mavrylo.Data/Mavrylo.Data.csproj" />
     <ProjectReference Include="../src/Mavrylo.Services/Mavrylo.Services.csproj" />
   </ItemGroup>
 </Project>
diff --git a/tests/PaidAccessAuditRegressionTests.cs b/tests/PaidAccessAuditRegressionTests.cs
index ff006c2..74df29c 100644
--- a/tests/PaidAccessAuditRegressionTests.cs
+++ b/tests/PaidAccessAuditRegressionTests.cs
@@ -76,26 +76,26 @@ public sealed class PaidAccessAuditRegressionTests
     }
 
     [Fact]
     public async Task B2_CopiedReceiptCannotFirstClaimDesktopOwnershipWithoutPriorOwnerProof()
     {
         using var fixture = TestDb.Create();
         var db = fixture.Db;
         await AddDevice(db, "unrelated-key", "unrelated-device");
         db.Users.Add(new AppUser { Id = "unrelated-account", Email = "unrelated@test.test" });
         db.Subscriptions.Add(Purchase("purchase", "original-device"));
         await db.SaveChangesAsync();
         var apple = Apple(Purchase("purchase", "original-device"), Purchase("purchase", "original-device"));
-        var entitlements = new EntitlementService(db, Clock);
-        var accounts = new AccountEntitlementService(db, entitlements, apple, new(db, Clock), Clock, new FakeEnvironment("Production"));
+        var entitlements = new EntitlementService(db, Clock, TestConfig.SubscriptionPolicy());
+        var accounts = new AccountEntitlementService(db, entitlements, apple, new(db, Clock), Clock);
         Assert.Equal(403, (await accounts.ClaimAsync("unrelated-account", "unrelated-key", "copied-synthetic-signed-proof", default)).Status);
         var stored = await db.Subscriptions.AsNoTracking().SingleAsync();
         Assert.Null(stored.OwnerAccountId);
         Assert.Equal("original-device", stored.DeviceUuid);
     }
 
     [Fact]
     public async Task PositiveControl_LegitimateSameDevicePurchaseAndRestoreRemainAllowedWithoutAccount()
     {
         using var fixture = TestDb.Create();
         var db = fixture.Db;
         await AddDevice(db, "key", "device");
@@ -111,26 +111,26 @@ public sealed class PaidAccessAuditRegressionTests
     public async Task PositiveControl_AlreadyClaimedPurchaseCannotChangeAccountOwner()
     {
         using var fixture = TestDb.Create();
         var db = fixture.Db;
         await AddDevice(db, "key", "device");
         db.Users.AddRange(new AppUser { Id = "owner", Email = "owner@test.test" }, new AppUser { Id = "other", Email = "other@test.test" });
         var purchase = Purchase("owned", "device");
         purchase.OwnerAccountId = "owner";
         purchase.ClaimedAt = DateTime.UtcNow;
         db.Subscriptions.Add(purchase);
         await db.SaveChangesAsync();
         var apple = Apple(Purchase("owned", "device"), Purchase("owned", "device"));
-        var entitlements = new EntitlementService(db, Clock);
-        var accounts = new AccountEntitlementService(db, entitlements, apple, new(db, Clock), Clock, new FakeEnvironment("Production"));
+        var entitlements = new EntitlementService(db, Clock, TestConfig.SubscriptionPolicy());
+        var accounts = new AccountEntitlementService(db, entitlements, apple, new(db, Clock), Clock);
         Assert.Equal(409, (await accounts.ClaimAsync("other", "key", "synthetic-signed-proof", default)).Status);
         Assert.Equal("owner", (await db.Subscriptions.AsNoTracking().SingleAsync()).OwnerAccountId);
     }
 
     [Fact]
     public async Task B4_PositiveControl_ProvenAccountSubjectRetainsUsageAcrossConsumers()
     {
         using var fixture = TestDb.Create();
         var config = TestConfig.Create(new Dictionary<string, string?> { ["AccountAi:DailyQuota"] = "1", ["AccountAi:RequestsPerMinute"] = "10" });
         Assert.True(await new AccountAiUsageService(fixture.Db, config, Clock).TryConsumeAsync("authenticated-owner", default));
         Assert.False(await new AccountAiUsageService(fixture.Db, config, Clock).TryConsumeAsync("authenticated-owner", default));
         Assert.True(await new AccountAiUsageService(fixture.Db, config, Clock).TryConsumeAsync("different-authenticated-owner", default));
@@ -150,48 +150,48 @@ public sealed class PaidAccessAuditRegressionTests
         var purchase = Purchase("non-production", "device");
         purchase.Environment = environment;
         db.Subscriptions.Add(purchase);
         await db.SaveChangesAsync();
         Assert.Equal((402, 0), await InvokeAi(db, "key", quota: 0));
     }
 
     [Fact]
     public async Task B6_RefundCannotAcknowledgeSuccessWhileUpdatingUnrelatedPurchase()
     {
         using var fixture = TestDb.Create();
         var db = fixture.Db;
-        var entitlements = new EntitlementService(db, Clock);
+        var entitlements = new EntitlementService(db, Clock, TestConfig.SubscriptionPolicy());
         await entitlements.UpsertAsync(Purchase("refunded", "device"));
         var refunded = Purchase("refunded", "device");
         refunded.RevokedAt = DateTime.UtcNow;
         var apple = Apple(refunded, Purchase("unrelated-active", "device"));
         apple.DecodeNotificationResult = new(true, JsonSerializer.SerializeToElement(new
         {
             notificationType = "REFUND", signedDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
             data = new { signedTransactionInfo = "synthetic-refund-proof" }
         }), true, null);
         var result = await new AppStoreNotificationService(apple, entitlements, Clock,
             NullLogger<AppStoreNotificationService>.Instance).HandleAsync(new("synthetic-notification"), default);
         Assert.Equal(503, result); // Retry until matching canonical purchase can be checked.
         Assert.Null(await entitlements.FindByOriginalTransactionAsync("unrelated-active"));
         Assert.Equal("refunded", (await db.Subscriptions.SingleAsync()).OriginalTransactionId);
     }
 
     [Theory]
     [InlineData(false)]
     [InlineData(true)]
     public async Task PositiveControl_MatchingCanonicalStateWinsOverHistoricalRefund(bool canonicalRevoked)
     {
         using var fixture = TestDb.Create();
-        var entitlements = new EntitlementService(fixture.Db, Clock);
+        var entitlements = new EntitlementService(fixture.Db, Clock, TestConfig.SubscriptionPolicy());
         await entitlements.UpsertAsync(Purchase("purchase", "device"));
         var historical = Purchase("purchase", "device");
         historical.RevokedAt = DateTime.UtcNow.AddDays(-1);
         var canonical = Purchase("purchase", "device");
         canonical.RevokedAt = canonicalRevoked ? DateTime.UtcNow : null;
         var apple = Apple(historical, canonical);
         apple.DecodeNotificationResult = new(true, JsonSerializer.SerializeToElement(new
         { notificationType = "REFUND", data = new { signedTransactionInfo = "synthetic-proof" } }), true, null);
         Assert.Equal(200, await new AppStoreNotificationService(apple, entitlements, Clock,
             NullLogger<AppStoreNotificationService>.Instance).HandleAsync(new("synthetic-notification"), default));
         Assert.Equal(canonicalRevoked ? "revoked" : "premium", entitlements.ToEntitlement(await entitlements.FindByOriginalTransactionAsync("purchase")).Status);
     }
@@ -214,42 +214,42 @@ public sealed class PaidAccessAuditRegressionTests
         }
         Assert.Equal((expectedStatus, 0), await InvokeAi(fixture.Db, "key", quota: 0));
     }
 
     private static SubscriptionEntity Purchase(string id, string device) => new()
     { OriginalTransactionId = id, DeviceUuid = device, ProductId = "monthly", ExpiresAt = DateTime.UtcNow.AddDays(5), WasEverPaid = true, Environment = "Production" };
 
     private static FakeAppStoreServerClient Apple(SubscriptionEntity verified, SubscriptionEntity canonical) => new()
     { IsLocalVerifyEnabled = false, VerifyTransactionResult = new(true, verified, true, null), SubscriptionStatusesResult = new(true, canonical, null) };
 
     private static IapService Iap(AppDbContext db, IAppStoreServerClient apple)
     {
-        var entitlements = new EntitlementService(db, Clock);
+        var entitlements = new EntitlementService(db, Clock, TestConfig.SubscriptionPolicy());
         return new(apple, entitlements, new(db, entitlements), new(Config, Clock), Clock, NullLogger<IapService>.Instance);
     }
 
     private static FakeAppAttestVerifier AcceptedAttestation() => new()
     { AttestationResult = AppAttestVerifier.AttestationResult.Success([1, 2, 3], 0, "production"), AssertionResult = AppAttestVerifier.AssertionResult.Success(1) };
 
     private static async Task AddDevice(AppDbContext db, string key, string device)
     {
         db.Devices.Add(new DeviceEntity { KeyId = key, DeviceUuid = device, PublicKey = [1, 2, 3] });
         await db.SaveChangesAsync();
     }
 
     // The action represents one synthetic provider attempt. Every rejection must stop before it.
     private static async Task<(int Status, int ProviderCalls)> InvokeAi(AppDbContext db, string key, int quota)
     {
         var filter = new AiProtectionFilter(Options.Create(new AiProtectionOptions { RequireAssertion = true, FreeDailyQuota = quota }),
-            Config, AcceptedAttestation(), new(db, Clock), new(db, new(db, Clock)), new(db, Clock, Config), new(db, Clock, Config),
+            Config, AcceptedAttestation(), new(db, Clock), new(db, new(db, Clock, TestConfig.SubscriptionPolicy())), new(db, Clock, Config), new(db, Clock, Config),
             Clock, NullLogger<AiProtectionFilter>.Instance);
         var jsonOptions = new JsonOptions();
         jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
         var mvcOptions = new MvcOptions();
         mvcOptions.InputFormatters.Add(new SystemTextJsonInputFormatter(jsonOptions, NullLogger<SystemTextJsonInputFormatter>.Instance));
         using var services = new ServiceCollection().AddSingleton(db)
             .AddSingleton<IModelMetadataProvider>(new EmptyModelMetadataProvider())
             .AddSingleton<IOptions<MvcOptions>>(Options.Create(mvcOptions)).BuildServiceProvider();
         var http = new DefaultHttpContext { RequestServices = services };
         http.Request.Path = "/owlai/ai/word-detail";
         http.Request.Method = "POST";
         http.Request.ContentType = "application/json";
diff --git a/tests/PublicFlashcardSetControllerTests.cs b/tests/PublicFlashcardSetControllerTests.cs
index f02578d..69bccf3 100644
--- a/tests/PublicFlashcardSetControllerTests.cs
+++ b/tests/PublicFlashcardSetControllerTests.cs
@@ -82,25 +82,25 @@ public class PublicFlashcardSetControllerTests
         Assert.Contains("Title is required", System.Text.Json.JsonSerializer.Serialize(badRequest.Value));
     }
 
     private static PublicFlashcardSetsController Controller(TestDb testDb, string? keyId)
     {
         var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-03T12:00:00Z"));
         var publicSets = new PublicFlashcardSetService(
             testDb.Db,
             time,
             NullLogger<PublicFlashcardSetService>.Instance);
         var context = new DeviceContextService(
             testDb.Db,
-            new EntitlementService(testDb.Db, time));
+            new EntitlementService(testDb.Db, time, TestConfig.SubscriptionPolicy()));
         var controller = new PublicFlashcardSetsController(publicSets, context);
         var claims = keyId is null ? [] : new[] { new Claim("keyId", keyId) };
         controller.ControllerContext = new ControllerContext
         {
             HttpContext = new DefaultHttpContext
             {
                 User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
             }
         };
         return controller;
     }
 
diff --git a/tests/ReviewTranslationTests.cs b/tests/ReviewTranslationTests.cs
index 0ddd187..879c86b 100644
--- a/tests/ReviewTranslationTests.cs
+++ b/tests/ReviewTranslationTests.cs
@@ -236,25 +236,25 @@ public class ReviewTranslationTests(PostgresContainerFixture postgres) : IClassF
     {
         var provider = new Provider();
         await using var factory = Factory(provider, testMode: false);
         using var client = factory.CreateClient();
         using var scope = factory.Services.CreateScope();
         var session = await scope.ServiceProvider.GetRequiredService<AccountService>()
             .SignInAsync(new("review-quota-" + Guid.NewGuid(), null, "Reviewer"), default, allowCreation: true);
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
         db.Subscriptions.Add(new SubscriptionEntity
         {
             OriginalTransactionId = Guid.NewGuid().ToString("N"), OwnerAccountId = session.Profile.Id,
             ClaimedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(3), WasEverPaid = true,
-            ProductId = "com.flashcardai.owlai.premium.monthly", Environment = "Sandbox"
+            ProductId = "com.flashcardai.owlai.premium.monthly", Environment = "Production"
         });
         await db.SaveChangesAsync();
         client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
         var config = factory.Services.GetRequiredService<IConfiguration>();
         config["AccountAi:DailyQuota"] = "1";
         config["AccountAi:RequestsPerMinute"] = "5";
         Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(AccountPath, Request())).StatusCode);
         Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync(AccountPath, Request())).StatusCode);
         Assert.False(await db.DeviceWords.AnyAsync(x => x.DeviceUuid == session.Profile.Id));
     }
 
     private ApiFactory Factory(Provider provider, bool testMode = true) => new(postgres.ConnectionString, services =>
diff --git a/tests/SharedSubscriptionPostgresTests.cs b/tests/SharedSubscriptionPostgresTests.cs
index c671863..2209585 100644
--- a/tests/SharedSubscriptionPostgresTests.cs
+++ b/tests/SharedSubscriptionPostgresTests.cs
@@ -10,45 +10,61 @@ using Mavrylo.Tests.TestSupport;
 using Microsoft.EntityFrameworkCore;
 using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.DependencyInjection.Extensions;
 using Microsoft.Extensions.Logging.Abstractions;
 using Xunit;
 
 namespace Mavrylo.Tests;
 
 public class SharedSubscriptionPostgresTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
 {
     private AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
 
+    [Fact]
+    public async Task StaleTrackedRowCannotOverwriteNewerAppleStateAfterTakingPurchaseLock()
+    {
+        await using var stale = Database(); await stale.Database.MigrateAsync();
+        var id = Guid.NewGuid().ToString("N"); var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
+        SubscriptionEntity State(DateTime signedAt, bool revoked) => new()
+        { OriginalTransactionId = id, ProductId = "monthly", Environment = "Production", ExpiresAt = now.AddDays(5), LastAppleEventAt = signedAt, RevokedAt = revoked ? now : null, WasEverPaid = true };
+        var staleService = new EntitlementService(stale, TimeProvider.System, TestConfig.SubscriptionPolicy());
+        await staleService.UpsertAsync(State(now.AddDays(-2), false));
+        await using (var newer = Database())
+            await new EntitlementService(newer, TimeProvider.System, TestConfig.SubscriptionPolicy()).UpsertAsync(State(now, false));
+        await staleService.UpsertAsync(State(now.AddDays(-1), true));
+        var persisted = await stale.Subscriptions.AsNoTracking().SingleAsync(x => x.OriginalTransactionId == id);
+        Assert.Null(persisted.RevokedAt); Assert.Equal(now, persisted.LastAppleEventAt);
+    }
+
     [Fact]
     public async Task DeviceEntitlementProvenanceDoesNotConfuseAccountAccessWithGuestPurchaseOwnership()
     {
         var id = Guid.NewGuid().ToString("N");
         var apple = new FakeAppStoreServerClient { IsServerApiConfigured = false };
         await using var factory = new ApiFactory(postgres.ConnectionString, services =>
         {
             services.RemoveAll<IAppStoreServerClient>(); services.AddSingleton<IAppStoreServerClient>(apple);
             services.RemoveAll<IAppAttestVerifier>(); services.AddSingleton<IAppAttestVerifier>(new FakeAppAttestVerifier { IsDevelopmentBypassEnabled = true });
         });
         using var client = factory.CreateClient();
         using var scope = factory.Services.CreateScope();
         var session = await scope.ServiceProvider.GetRequiredService<AccountService>().SignInAsync(new("provenance-"+id, null, "Owner"), default, allowCreation: true);
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
         var keyId = "SIMULATOR-"+id;
         db.Devices.Add(new DeviceEntity { KeyId = keyId, DeviceUuid = id });
         db.Subscriptions.AddRange(
-            new SubscriptionEntity { OriginalTransactionId = "account-"+id, DeviceUuid = "other-"+id, OwnerAccountId = session.Profile.Id, ClaimedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(20), WasEverPaid = true },
-            new SubscriptionEntity { OriginalTransactionId = "guest-"+id, DeviceUuid = id, ExpiresAt = DateTime.UtcNow.AddDays(10), WasEverPaid = true });
+            new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "account-"+id, DeviceUuid = "other-"+id, OwnerAccountId = session.Profile.Id, ClaimedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(20), WasEverPaid = true },
+            new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "guest-"+id, DeviceUuid = id, ExpiresAt = DateTime.UtcNow.AddDays(10), WasEverPaid = true });
         await db.SaveChangesAsync();
-        apple.VerifyTransactionResult = new(true, new SubscriptionEntity { OriginalTransactionId = "guest-"+id, DeviceUuid = id, ExpiresAt = DateTime.UtcNow.AddDays(10), WasEverPaid = true }, true, null);
+        apple.VerifyTransactionResult = new(true, new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "guest-"+id, DeviceUuid = id, ExpiresAt = DateTime.UtcNow.AddDays(10), WasEverPaid = true }, true, null);
         var deviceToken = scope.ServiceProvider.GetRequiredService<JwtTokenService>().CreateDeviceToken(keyId, "premium").Token;
         client.DefaultRequestHeaders.Authorization = new("Bearer", deviceToken);
         client.DefaultRequestHeaders.Add("X-Account-Authorization", "Bearer "+session.AccessToken);
         var shared = await client.GetFromJsonAsync<JsonElement>("/owlai/iap/entitlement");
         Assert.Equal("account", shared.GetProperty("entitlement").GetProperty("resolution_source").GetString());
         Assert.False(shared.GetProperty("entitlement").GetProperty("purchase_is_linked").GetBoolean());
         foreach (var path in new[] { "/owlai/iap/token", "/owlai/iap/verify" })
         {
             var response = await client.PostAsJsonAsync(path, new { jws_transaction = "fixture" });
             Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
             var entitlement = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entitlement");
             Assert.Equal("account", entitlement.GetProperty("resolution_source").GetString());
@@ -76,25 +92,25 @@ public class SharedSubscriptionPostgresTests(PostgresContainerFixture postgres)
         await setup.Database.MigrateAsync();
         var otid = Guid.NewGuid().ToString("N");
         var a = new AppUser { Email = otid+"a@example.test" };
         var b = new AppUser { Email = otid+"b@example.test" };
         var device = new DeviceEntity { KeyId = otid, DeviceUuid = otid };
         setup.Users.AddRange(a, b); setup.Devices.Add(device); await setup.SaveChangesAsync();
         var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
         async Task<int> Claim(string owner)
         {
             await using var db = Database();
             var purchase = new SubscriptionEntity { OriginalTransactionId = otid, DeviceUuid = otid, ProductId = "monthly", ExpiresAt = DateTime.UtcNow.AddDays(7) };
             var apple = new FakeAppStoreServerClient { VerifyTransactionResult = new(true, purchase, true, null), SubscriptionStatusesResult = new(true, purchase, null) };
-            var service = new AccountEntitlementService(db, new(db, TimeProvider.System), apple, new(db, TimeProvider.System), TimeProvider.System, new FakeEnvironment("Production"));
+            var service = new AccountEntitlementService(db, new(db, TimeProvider.System, TestConfig.SubscriptionPolicy()), apple, new(db, TimeProvider.System), TimeProvider.System);
             await gate.Task;
             return (await service.ClaimAsync(owner, otid, "proof", default)).Status;
         }
         var left = Claim(a.Id); var right = Claim(b.Id); gate.SetResult();
         var statuses = await Task.WhenAll(left, right);
         Assert.Equal(new[] { 200, 409 }, statuses.Order().ToArray());
         var row = await setup.Subscriptions.AsNoTracking().SingleAsync(x => x.OriginalTransactionId == otid);
         Assert.Equal(statuses[0] == 200 ? a.Id : b.Id, row.OwnerAccountId);
     }
 
     [Fact]
     public async Task ConcurrentAccountAiReservationsCannotExceedMinuteLimit()
diff --git a/tests/SharedSubscriptionTests.cs b/tests/SharedSubscriptionTests.cs
index e7ac20f..caffad5 100644
--- a/tests/SharedSubscriptionTests.cs
+++ b/tests/SharedSubscriptionTests.cs
@@ -5,69 +5,69 @@ using Xunit;
 using Microsoft.EntityFrameworkCore;
 using Microsoft.Extensions.Configuration;
 
 namespace Mavrylo.Tests;
 public class SharedSubscriptionTests
 {
     [Fact]
     public async Task StaleAccountPurchaseRefreshesCanonicalRevocationWithoutDeviceRequest()
     {
         using var testDb = TestDb.Create();
         var now = DateTime.UtcNow;
         testDb.Db.Users.Add(new AppUser { Id = "stale-owner", Email = "stale@test.test" });
-        testDb.Db.Subscriptions.Add(new SubscriptionEntity { OriginalTransactionId = "stale", OwnerAccountId = "stale-owner", ClaimedAt = now, ExpiresAt = now.AddDays(4), LastCheckedAt = now.AddHours(-1), WasEverPaid = true });
+        testDb.Db.Subscriptions.Add(new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "stale", OwnerAccountId = "stale-owner", ClaimedAt = now, ExpiresAt = now.AddDays(4), LastCheckedAt = now.AddHours(-1), WasEverPaid = true });
         await testDb.Db.SaveChangesAsync();
-        var apple = new FakeAppStoreServerClient { SubscriptionStatusesResult = new(true, new SubscriptionEntity { OriginalTransactionId = "stale", ExpiresAt = now.AddDays(4), RevokedAt = now, WasEverPaid = true }, null) };
-        var service = new AccountEntitlementService(testDb.Db, new(testDb.Db, TimeProvider.System), apple, new(testDb.Db, TimeProvider.System), TimeProvider.System, new FakeEnvironment());
+        var apple = new FakeAppStoreServerClient { SubscriptionStatusesResult = new(true, new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "stale", ExpiresAt = now.AddDays(4), RevokedAt = now, WasEverPaid = true }, null) };
+        var service = new AccountEntitlementService(testDb.Db, new(testDb.Db, TimeProvider.System, TestConfig.SubscriptionPolicy()), apple, new(testDb.Db, TimeProvider.System), TimeProvider.System);
         Assert.False(AccountEntitlementService.IsActive((await service.GetAsync("stale-owner")).Status));
         Assert.NotNull((await testDb.Db.Subscriptions.SingleAsync()).RevokedAt);
     }
     [Fact]
     public async Task AccountStatusUsesProductPrecedenceBeforeExpiry()
     {
         using var testDb = TestDb.Create();
         testDb.Db.Users.Add(new AppUser { Id = "rank-owner", Email = "rank@test.test" });
         var now = DateTime.UtcNow;
         testDb.Db.Subscriptions.AddRange(
-            new SubscriptionEntity { OriginalTransactionId = "rank-premium", OwnerAccountId = "rank-owner", ClaimedAt = now, ExpiresAt = now.AddDays(1), WasEverPaid = true },
-            new SubscriptionEntity { OriginalTransactionId = "rank-trial", OwnerAccountId = "rank-owner", ClaimedAt = now, ExpiresAt = now.AddDays(10), IsTrial = true });
+            new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "rank-premium", OwnerAccountId = "rank-owner", ClaimedAt = now, ExpiresAt = now.AddDays(1), WasEverPaid = true },
+            new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "rank-trial", OwnerAccountId = "rank-owner", ClaimedAt = now, ExpiresAt = now.AddDays(10), IsTrial = true });
         await testDb.Db.SaveChangesAsync();
-        var service = new AccountEntitlementService(testDb.Db, new(testDb.Db, TimeProvider.System), new FakeAppStoreServerClient(), new(testDb.Db, TimeProvider.System), TimeProvider.System, new FakeEnvironment());
+        var service = new AccountEntitlementService(testDb.Db, new(testDb.Db, TimeProvider.System, TestConfig.SubscriptionPolicy()), new FakeAppStoreServerClient(), new(testDb.Db, TimeProvider.System), TimeProvider.System);
         Assert.Equal("premium", (await service.GetAsync("rank-owner")).Status);
     }
     [Fact]
     public async Task OlderOrRepeatedAppleNotificationCannotOverwriteNewerState()
     {
         using var testDb = TestDb.Create();
-        var service = new EntitlementService(testDb.Db, TimeProvider.System);
+        var service = new EntitlementService(testDb.Db, TimeProvider.System, TestConfig.SubscriptionPolicy());
         var now = DateTime.UtcNow;
-        await service.UpsertAsync(new SubscriptionEntity { OriginalTransactionId = "events", LastAppleEventAt = now, ExpiresAt = now.AddDays(-1), AutoRenew = false });
+        await service.UpsertAsync(new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "events", LastAppleEventAt = now, ExpiresAt = now.AddDays(-1), AutoRenew = false });
         foreach (var signedAt in new[] { now.AddSeconds(-1), now })
-            await service.UpsertAsync(new SubscriptionEntity { OriginalTransactionId = "events", LastAppleEventAt = signedAt, ExpiresAt = now.AddDays(10), AutoRenew = true });
+            await service.UpsertAsync(new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "events", LastAppleEventAt = signedAt, ExpiresAt = now.AddDays(10), AutoRenew = true });
         var stored = await service.FindByOriginalTransactionAsync("events");
         Assert.False(stored!.AutoRenew);
         Assert.Equal(now.AddDays(-1), stored.ExpiresAt);
     }
     [Fact]
     public async Task ActivePurchaseWinsOverLaterRevokedPurchaseAndSandboxNeverGrantsProduction()
     {
         using var testDb = TestDb.Create();
         testDb.Db.Users.Add(new AppUser { Id = "owner", Email = "owner@test.test" });
         var now = DateTime.UtcNow;
         testDb.Db.Subscriptions.AddRange(
-            new SubscriptionEntity { OriginalTransactionId = "valid", OwnerAccountId = "owner", ClaimedAt = now, ExpiresAt = now.AddDays(1), WasEverPaid = true },
-            new SubscriptionEntity { OriginalTransactionId = "revoked", OwnerAccountId = "owner", ClaimedAt = now, ExpiresAt = now.AddDays(20), RevokedAt = now },
-            new SubscriptionEntity { OriginalTransactionId = "sandbox", OwnerAccountId = "owner", ClaimedAt = now, ExpiresAt = now.AddDays(40), Environment = "Sandbox" });
+            new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "valid", OwnerAccountId = "owner", ClaimedAt = now, ExpiresAt = now.AddDays(1), WasEverPaid = true },
+            new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "revoked", OwnerAccountId = "owner", ClaimedAt = now, ExpiresAt = now.AddDays(20), RevokedAt = now },
+            new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "sandbox", OwnerAccountId = "owner", ClaimedAt = now, ExpiresAt = now.AddDays(40), Environment = "Sandbox" });
         await testDb.Db.SaveChangesAsync();
-        var service = new AccountEntitlementService(testDb.Db, new(testDb.Db, TimeProvider.System), new FakeAppStoreServerClient(), new(testDb.Db, TimeProvider.System), TimeProvider.System, new FakeEnvironment("Production"));
+        var service = new AccountEntitlementService(testDb.Db, new(testDb.Db, TimeProvider.System, TestConfig.SubscriptionPolicy()), new FakeAppStoreServerClient(), new(testDb.Db, TimeProvider.System), TimeProvider.System);
         var entitlement = await service.GetAsync("owner");
         Assert.Equal("premium", entitlement.Status);
         Assert.Equal(now.AddDays(1), entitlement.ExpiresAt);
         Assert.Equal("free", (await service.GetAsync("stranger")).Status);
         await new SubscriptionOwnershipService(testDb.Db, TimeProvider.System).TombstoneAsync("owner");
         Assert.Equal("free", (await service.GetAsync("owner")).Status);
     }
 
     [Fact]
     public async Task AccountMinuteAndDailyQuotaAreSharedAndDoNotConsumeWhenRejected()
     {
         using var testDb = TestDb.Create();
@@ -81,48 +81,48 @@ public class SharedSubscriptionTests
     }
 
     [Theory]
     [InlineData(false, "Production", 400)]
     [InlineData(true, "Sandbox", 402)]
     [InlineData(true, "Xcode", 402)]
     [InlineData(true, "LocalTesting", 402)]
     public async Task ClaimRejectsUnverifiedAndNonProductionPurchase(bool signed, string environment, int expected)
     {
         using var testDb = TestDb.Create();
         testDb.Db.Devices.Add(new DeviceEntity { KeyId = "key", DeviceUuid = "device" });
         await testDb.Db.SaveChangesAsync();
-        var purchase = new SubscriptionEntity { OriginalTransactionId = "purchase", Environment = environment, ExpiresAt = DateTime.UtcNow.AddDays(3) };
+        var purchase = new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "purchase", Environment = environment, ExpiresAt = DateTime.UtcNow.AddDays(3) };
         var apple = new FakeAppStoreServerClient { VerifyTransactionResult = new(true, purchase, signed, null), SubscriptionStatusesResult = new(true, purchase, null) };
-        var service = new AccountEntitlementService(testDb.Db, new(testDb.Db, TimeProvider.System), apple, new(testDb.Db, TimeProvider.System), TimeProvider.System, new FakeEnvironment("Production"));
+        var service = new AccountEntitlementService(testDb.Db, new(testDb.Db, TimeProvider.System, TestConfig.SubscriptionPolicy()), apple, new(testDb.Db, TimeProvider.System), TimeProvider.System);
         var response = await service.ClaimAsync("owner", "key", "signed-proof", default);
         Assert.Equal(expected, response.Status);
         Assert.Empty(await testDb.Db.Subscriptions.ToListAsync());
     }
 
     [Fact]
     public async Task RestoreCannotTransferOwnerOrMakeClaimedPurchaseGuestAccessible()
     {
         using var testDb = TestDb.Create();
-        var service = new EntitlementService(testDb.Db, TimeProvider.System);
-        var sub = await service.UpsertAsync(new SubscriptionEntity { OriginalTransactionId = "owned", DeviceUuid = "first", ExpiresAt = DateTime.UtcNow.AddDays(5) });
+        var service = new EntitlementService(testDb.Db, TimeProvider.System, TestConfig.SubscriptionPolicy());
+        var sub = await service.UpsertAsync(new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "owned", DeviceUuid = "first", ExpiresAt = DateTime.UtcNow.AddDays(5) });
         testDb.Db.Users.Add(new AppUser { Id = "owner", Email = "owner@test.test" }); sub.OwnerAccountId = "owner"; sub.ClaimedAt = DateTime.UtcNow;
         await testDb.Db.SaveChangesAsync();
-        await service.UpsertAsync(new SubscriptionEntity { OriginalTransactionId = "owned", DeviceUuid = "second", ExpiresAt = DateTime.UtcNow.AddDays(7), OwnerAccountId = "attacker" });
+        await service.UpsertAsync(new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "owned", DeviceUuid = "second", ExpiresAt = DateTime.UtcNow.AddDays(7), OwnerAccountId = "attacker" });
         Assert.Equal("owner", sub.OwnerAccountId);
         Assert.Equal("account_required", service.ToDeviceEntitlement(sub).Status);
     }
     [Fact]
     public async Task AtomicOwnershipIsIdempotentAndTombstoneCannotBeClaimed()
     {
         using var testDb = TestDb.Create();
         testDb.Db.Users.AddRange(new AppUser { Id = "alice", Email = "alice@test.test" }, new AppUser { Id = "bob", Email = "bob@test.test" });
-        testDb.Db.Subscriptions.Add(new SubscriptionEntity { OriginalTransactionId = "purchase" });
+        testDb.Db.Subscriptions.Add(new SubscriptionEntity { ProductId = "monthly", OriginalTransactionId = "purchase" });
         await testDb.Db.SaveChangesAsync();
         var ownership = new SubscriptionOwnershipService(testDb.Db, TimeProvider.System);
         Assert.True(await ownership.TryClaimAsync("purchase", "alice"));
         Assert.True(await ownership.TryClaimAsync("purchase", "alice"));
         Assert.False(await ownership.TryClaimAsync("purchase", "bob"));
         await ownership.TombstoneAsync("alice");
         Assert.False(await ownership.TryClaimAsync("purchase", "alice"));
         Assert.False(await ownership.TryClaimAsync("purchase", "bob"));
     }
 }
diff --git a/tests/SubscriptionLifecycleTests.cs b/tests/SubscriptionLifecycleTests.cs
new file mode 100644
index 0000000..d71ae26
--- /dev/null
+++ b/tests/SubscriptionLifecycleTests.cs
@@ -0,0 +1,259 @@
+using System.Text.Json;
+using Mavrylo.Data;
+using Mavrylo.Models;
+using Mavrylo.Services;
+using Mavrylo.Tests.TestSupport;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging.Abstractions;
+using Xunit;
+
+namespace Mavrylo.Tests;
+
+public sealed class SubscriptionLifecycleTests
+{
+    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
+    private static readonly TimeProvider Clock = new ManualTimeProvider(new DateTimeOffset(Now));
+
+    [Fact]
+    public async Task ExplicitSandboxServerWorksForAccountAndDeviceButRejectsProductionHistory()
+    {
+        using var fixture = TestDb.Create();
+        var row = Purchase("sandbox"); row.Environment = "Sandbox";
+        await Own(fixture.Db, row);
+        var service = new EntitlementService(fixture.Db, Clock, TestConfig.SubscriptionPolicy("Sandbox"));
+        var accounts = new AccountEntitlementService(fixture.Db, service, new FakeAppStoreServerClient(), new(fixture.Db, Clock), Clock);
+        Assert.Equal("premium", (await accounts.GetAsync("owner")).Status);
+        Assert.Equal("premium", service.ToEntitlement(row).Status);
+        Assert.Equal("invalid_subscription", service.ToEntitlement(Purchase("production")).Status);
+    }
+
+    [Fact]
+    public async Task InvalidCachedHistoryIsPreservedAndQuarantinedDuringRefresh()
+    {
+        using var fixture = TestDb.Create(); var service = Entitlements(fixture.Db);
+        var invalid = Purchase("A"); invalid.Environment = "Sandbox";
+        invalid.ClaimedAt = Now.AddDays(-3);
+        fixture.Db.Subscriptions.Add(invalid); await fixture.Db.SaveChangesAsync();
+        var valid = Purchase("A"); valid.IsTrial = true; valid.WasEverPaid = false; valid.ExpiresAt = Now.AddDays(-1);
+        await Assert.ThrowsAsync<SubscriptionReconciliationRequiredException>(() => service.UpsertAsync(valid));
+        var stored = (await service.FindByOriginalTransactionAsync("A"))!;
+        Assert.Equal("Sandbox", stored.Environment); Assert.True(stored.WasEverPaid);
+        Assert.Equal(Now.AddDays(-3), stored.ClaimedAt); Assert.Null(stored.OwnerAccountId);
+        Assert.Equal(Now.AddDays(5), stored.ExpiresAt);
+        Assert.Equal("invalid_subscription", service.ToEntitlement(stored).Status);
+    }
+
+    [Fact]
+    public async Task QuarantinedHistoryProducesHandledNotificationAndIapRetryWithoutMutation()
+    {
+        using var fixture = TestDb.Create(); var service = Entitlements(fixture.Db);
+        var invalid = Purchase("A"); invalid.ProductId = "legacy-unknown";
+        fixture.Db.Subscriptions.Add(invalid); fixture.Db.Devices.Add(new DeviceEntity { KeyId = "key", DeviceUuid = "device" }); await fixture.Db.SaveChangesAsync();
+        var apple = Notification(Purchase("A"), Purchase("A"));
+        Assert.Equal(503, await new AppStoreNotificationService(apple, service, Clock, NullLogger<AppStoreNotificationService>.Instance).HandleAsync(new("proof"), default));
+        var iap = new IapService(apple, service, new(fixture.Db, service), new(TestConfig.Create(), Clock), Clock, NullLogger<IapService>.Instance);
+        Assert.Equal(503, (await iap.VerifyAsync("key", new("proof"), default)).Status);
+        Assert.Equal(200, (await iap.TokenAsync("key", default)).Status);
+        Assert.Equal("legacy-unknown", (await fixture.Db.Subscriptions.SingleAsync()).ProductId);
+        Assert.True(invalid.WasEverPaid);
+    }
+
+    [Theory]
+    [InlineData("Sandbox", "monthly", 5)]
+    [InlineData("LocalTesting", "monthly", 5)]
+    [InlineData("", "monthly", 5)]
+    [InlineData("Production", "unknown", 5)]
+    [InlineData("Production", "monthly", null)]
+    public void InvalidCachedPurchaseGrantsNeitherActiveAccessNorPaidHistory(string environment, string product, int? days)
+    {
+        using var fixture = TestDb.Create();
+        var sub = Purchase("purchase"); sub.Environment = environment; sub.ProductId = product;
+        sub.ExpiresAt = days.HasValue ? Now.AddDays(days.Value) : null;
+        var entitlement = Entitlements(fixture.Db).ToEntitlement(sub);
+        Assert.False(AccountEntitlementService.IsActive(entitlement.Status));
+        Assert.False(entitlement.WasEverPaid);
+    }
+
+    [Theory]
+    [InlineData("premium", 1, false, "premium")]
+    [InlineData("trial", 1, true, "trial")]
+    [InlineData("grace", 1, false, "grace")]
+    [InlineData("premium", 0, false, "expired_paid")]
+    [InlineData("grace", 0, false, "expired_paid")]
+    [InlineData("revoked", 1, false, "revoked")]
+    public void ValidLifecycleUsesFiniteExpiryAndPreservesTrialGrace(string marker, int days, bool trial, string expected)
+    {
+        using var fixture = TestDb.Create();
+        var sub = Purchase("purchase"); sub.Status = marker; sub.ExpiresAt = Now.AddDays(days);
+        sub.IsTrial = trial; sub.WasEverPaid = !trial; sub.RevokedAt = marker == "revoked" ? Now : null;
+        Assert.Equal(expected, Entitlements(fixture.Db).ToEntitlement(sub).Status);
+    }
+
+    [Theory]
+    [InlineData(false, "expired_paid")]
+    [InlineData(true, "revoked")]
+    public async Task AccountOutageRetainsOrdinaryPaidExpiryButDoesNotUndoRevocation(bool revoked, string expected)
+    {
+        using var fixture = TestDb.Create();
+        var row = Purchase("owned"); row.ExpiresAt = Now.AddDays(-1); row.RevokedAt = revoked ? Now : null;
+        await Own(fixture.Db, row);
+        var ent = await Account(fixture.Db, new FakeAppStoreServerClient()).GetAsync("owner");
+        Assert.Equal(expected, ent.Status); Assert.True(ent.WasEverPaid);
+        Assert.Single(await fixture.Db.Subscriptions.ToListAsync());
+    }
+
+    [Fact]
+    public async Task InvalidAccountHistoryCannotTurnExpiredTrialIntoExpiredPaid()
+    {
+        using var fixture = TestDb.Create();
+        var trial = Purchase("trial"); trial.IsTrial = true; trial.WasEverPaid = false; trial.ExpiresAt = Now.AddDays(-1);
+        var invalid = Purchase("sandbox"); invalid.Environment = "Sandbox";
+        await Own(fixture.Db, trial, invalid);
+        var ent = await Account(fixture.Db, new FakeAppStoreServerClient()).GetAsync("owner");
+        Assert.Equal("expired_trial", ent.Status); Assert.False(ent.WasEverPaid);
+        Assert.Equal(2, await fixture.Db.Subscriptions.CountAsync());
+    }
+
+    [Theory]
+    [InlineData(false)]
+    [InlineData(true)]
+    public async Task ActiveIndependentPurchaseWinsForDeviceAndAccount(bool account)
+    {
+        using var fixture = TestDb.Create();
+        var active = Purchase("B"); active.ExpiresAt = Now.AddDays(1);
+        var revoked = Purchase("A"); revoked.RevokedAt = Now; revoked.ExpiresAt = Now.AddDays(20);
+        if (account) await Own(fixture.Db, active, revoked);
+        else { fixture.Db.Subscriptions.AddRange(active, revoked); await fixture.Db.SaveChangesAsync(); }
+        var entitlements = Entitlements(fixture.Db);
+        Assert.Equal("premium", account ? (await Account(fixture.Db, new FakeAppStoreServerClient()).GetAsync("owner")).Status
+            : entitlements.ToDeviceEntitlement(await entitlements.FindForDeviceAsync("device")).Status);
+    }
+
+    [Fact]
+    public async Task LaterVerifiedActiveStateClearsEarlierRevocationWithoutChangingTombstone()
+    {
+        using var fixture = TestDb.Create(); var service = Entitlements(fixture.Db);
+        var revoked = Purchase("A"); revoked.RevokedAt = Now.AddDays(-1); revoked.LastAppleEventAt = Now.AddDays(-1); revoked.ClaimedAt = Now.AddDays(-5);
+        await service.UpsertAsync(revoked);
+        var active = Purchase("A"); active.LastAppleEventAt = Now;
+        await service.UpsertAsync(active);
+        var stored = (await service.FindByOriginalTransactionAsync("A"))!;
+        Assert.Null(stored.RevokedAt); Assert.Equal("premium", service.ToEntitlement(stored).Status);
+        Assert.Equal(Now.AddDays(-5), stored.ClaimedAt); Assert.Null(stored.OwnerAccountId);
+        Assert.Equal("account_required", service.ToDeviceEntitlement(stored).Status);
+        var old = Purchase("A"); old.RevokedAt = Now.AddDays(-1); old.LastAppleEventAt = Now.AddDays(-1);
+        await service.UpsertAsync(old); await service.UpsertAsync(active);
+        Assert.Null(stored.RevokedAt); Assert.Single(await fixture.Db.Subscriptions.ToListAsync());
+    }
+
+    [Fact]
+    public async Task RefundOfAChangesOnlyAAndReplayDoesNotResurrectOrDuplicateIt()
+    {
+        using var fixture = TestDb.Create(); var service = Entitlements(fixture.Db);
+        await service.UpsertAsync(Purchase("A")); await service.UpsertAsync(Purchase("B"));
+        var refund = Purchase("A"); refund.RevokedAt = Now;
+        var apple = Notification(refund, refund);
+        var notifications = new AppStoreNotificationService(apple, service, Clock, NullLogger<AppStoreNotificationService>.Instance);
+        Assert.Equal(200, await notifications.HandleAsync(new("proof"), default));
+        Assert.Equal(200, await notifications.HandleAsync(new("proof"), default));
+        Assert.Equal("revoked", service.ToEntitlement(await service.FindByOriginalTransactionAsync("A")).Status);
+        Assert.Equal("premium", service.ToEntitlement(await service.FindByOriginalTransactionAsync("B")).Status);
+        Assert.Equal(2, await fixture.Db.Subscriptions.CountAsync());
+    }
+
+    [Fact]
+    public async Task LifecycleNotificationPreservesRestoredDeviceLink()
+    {
+        using var fixture = TestDb.Create(); var service = Entitlements(fixture.Db);
+        var saved = Purchase("A"); saved.DeviceUuid = "restored-device"; await service.UpsertAsync(saved);
+        var canonical = Purchase("A"); canonical.RevokedAt = Now;
+        var apple = Notification(canonical, canonical);
+        Assert.Equal(200, await new AppStoreNotificationService(apple, service, Clock, NullLogger<AppStoreNotificationService>.Instance).HandleAsync(new("proof"), default));
+        Assert.Equal("restored-device", (await service.FindByOriginalTransactionAsync("A"))!.DeviceUuid);
+    }
+
+    [Fact]
+    public async Task UndatedUpdateCannotOverwriteEstablishedDatedAppleState()
+    {
+        using var fixture = TestDb.Create(); var service = Entitlements(fixture.Db);
+        var saved = Purchase("A"); saved.RevokedAt = Now; saved.LastAppleEventAt = Now; await service.UpsertAsync(saved);
+        await service.UpsertAsync(Purchase("A"));
+        Assert.Equal("revoked", service.ToEntitlement(await service.FindByOriginalTransactionAsync("A")).Status);
+    }
+
+    [Fact]
+    public async Task SameDatedCanonicalRestoreCanRelinkWithoutRewritingLifecycleOrClaim()
+    {
+        using var fixture = TestDb.Create(); var service = Entitlements(fixture.Db);
+        var saved = Purchase("A"); saved.LastAppleEventAt = Now; saved.ClaimedAt = Now.AddDays(-2);
+        await service.UpsertAsync(saved);
+        var restored = Purchase("A"); restored.LastAppleEventAt = Now; restored.DeviceUuid = "restored"; restored.ExpiresAt = Now.AddDays(100);
+        var result = await service.UpsertAsync(restored);
+        Assert.Equal("restored", result.DeviceUuid); Assert.Equal(Now.AddDays(5), result.ExpiresAt);
+        Assert.Equal(Now.AddDays(-2), result.ClaimedAt); Assert.Null(result.OwnerAccountId);
+        Assert.Equal("account_required", service.ToDeviceEntitlement(result).Status);
+    }
+
+    [Fact]
+    public async Task NotificationOutageMustRetryThenPersistMatchingRefund()
+    {
+        using var fixture = TestDb.Create(); var service = Entitlements(fixture.Db);
+        await service.UpsertAsync(Purchase("A")); var refund = Purchase("A"); refund.RevokedAt = Now;
+        var apple = Notification(refund, refund); apple.SubscriptionStatusesResult = AppStoreServerClient.SubscriptionStatusesResult.Fail("outage");
+        var notifications = new AppStoreNotificationService(apple, service, Clock, NullLogger<AppStoreNotificationService>.Instance);
+        Assert.Equal(503, await notifications.HandleAsync(new("proof"), default));
+        apple.SubscriptionStatusesResult = new(true, refund, null);
+        Assert.Equal(200, await notifications.HandleAsync(new("proof"), default));
+        Assert.Equal("revoked", service.ToEntitlement(await service.FindByOriginalTransactionAsync("A")).Status);
+    }
+
+    [Theory]
+    [InlineData(false)]
+    [InlineData(true)]
+    public async Task IapCachedRefreshCannotPersistUnrelatedCanonicalPurchase(bool token)
+    {
+        using var fixture = TestDb.Create(); var service = Entitlements(fixture.Db);
+        fixture.Db.Devices.Add(new DeviceEntity { KeyId = "key", DeviceUuid = "device" });
+        var row = Purchase("A"); row.LastCheckedAt = Now.AddHours(-1); await service.UpsertAsync(row);
+        var apple = new FakeAppStoreServerClient { SubscriptionStatusesResult = new(true, Purchase("B"), null) };
+        var iap = new IapService(apple, service, new(fixture.Db, service), new(TestConfig.Create(), Clock), Clock, NullLogger<IapService>.Instance);
+        if (token) await iap.TokenAsync("key", default); else await iap.EntitlementAsync("key", default);
+        Assert.Null(await service.FindByOriginalTransactionAsync("B")); Assert.Single(await fixture.Db.Subscriptions.ToListAsync());
+    }
+
+    [Theory]
+    [InlineData(false)]
+    [InlineData(true)]
+    public async Task RefreshRevokingSelectedPurchaseStillReturnsIndependentActivePurchase(bool token)
+    {
+        using var fixture = TestDb.Create(); var service = Entitlements(fixture.Db);
+        fixture.Db.Devices.Add(new DeviceEntity { KeyId = "key", DeviceUuid = "device" });
+        var a = Purchase("A"); a.ExpiresAt = Now.AddDays(20); a.LastCheckedAt = Now.AddHours(-1);
+        await service.UpsertAsync(a); await service.UpsertAsync(Purchase("B"));
+        var revoked = Purchase("A"); revoked.RevokedAt = Now;
+        var apple = new FakeAppStoreServerClient { SubscriptionStatusesResult = new(true, revoked, null) };
+        var iap = new IapService(apple, service, new(fixture.Db, service), new(TestConfig.Create(), Clock), Clock, NullLogger<IapService>.Instance);
+        var result = token ? await iap.TokenAsync("key", default) : await iap.EntitlementAsync("key", default);
+        var dto = JsonSerializer.SerializeToElement(result.Body);
+        Assert.Equal("premium", dto.GetProperty("Entitlement").GetProperty("Status").GetString());
+        Assert.NotNull((await service.FindByOriginalTransactionAsync("A"))!.RevokedAt);
+    }
+
+    private static EntitlementService Entitlements(AppDbContext db) => new(db, Clock, TestConfig.SubscriptionPolicy());
+    private static AccountEntitlementService Account(AppDbContext db, IAppStoreServerClient apple)
+        => new(db, Entitlements(db), apple, new(db, Clock), Clock);
+    private static SubscriptionEntity Purchase(string id) => new()
+    { OriginalTransactionId = id, DeviceUuid = "device", ProductId = "monthly", Environment = "Production", ExpiresAt = Now.AddDays(5), LastCheckedAt = Now, WasEverPaid = true };
+    private static async Task Own(AppDbContext db, params SubscriptionEntity[] rows)
+    {
+        db.Users.Add(new AppUser { Id = "owner", Email = "owner@test.test" });
+        foreach (var row in rows) { row.OwnerAccountId = "owner"; row.ClaimedAt = Now; db.Subscriptions.Add(row); }
+        await db.SaveChangesAsync();
+    }
+    private static FakeAppStoreServerClient Notification(SubscriptionEntity transaction, SubscriptionEntity canonical) => new()
+    {
+        IsLocalVerifyEnabled = false, VerifyTransactionResult = new(true, transaction, true, null), SubscriptionStatusesResult = new(true, canonical, null),
+        DecodeNotificationResult = new(true, JsonSerializer.SerializeToElement(new
+        { notificationType = "REFUND", signedDate = new DateTimeOffset(Now).ToUnixTimeMilliseconds(), data = new { signedTransactionInfo = "signed" } }), true, null)
+    };
+}
diff --git a/tests/TestModeRouteTests.cs b/tests/TestModeRouteTests.cs
index 7653235..f17f762 100644
--- a/tests/TestModeRouteTests.cs
+++ b/tests/TestModeRouteTests.cs
@@ -134,25 +134,25 @@ public class TestModeRouteTests(PostgresContainerFixture postgres) : IClassFixtu
         await using var factory = Factory("false", protectionEnabled: false);
         using var client = factory.CreateClient();
         using var scope = factory.Services.CreateScope();
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
         var id = Guid.NewGuid().ToString("N");
         var key = "SIMULATOR-" + id;
         db.Devices.Add(new DeviceEntity { KeyId = key, DeviceUuid = id, Environment = "development" });
         if (expired)
             db.Subscriptions.Add(new SubscriptionEntity
             {
                 OriginalTransactionId = id, DeviceUuid = id,
                 ProductId = "com.flashcardai.owlai.premium.monthly", ExpiresAt = DateTime.UtcNow.AddDays(-1),
-                WasEverPaid = true, Environment = "Sandbox"
+                WasEverPaid = true, Environment = "Production"
             });
         for (var i = 0; i < 10; i++)
             db.DeviceWords.Add(new DeviceWordEntity
             {
                 DeviceUuid = id, NormalizedWord = "word-" + i, NativeLanguage = "en", LearningLanguage = "es", IsActive = true
             });
         await db.SaveChangesAsync();
         client.DefaultRequestHeaders.Authorization = new("Bearer", scope.ServiceProvider.GetRequiredService<JwtTokenService>()
             .CreateDeviceToken(key, "premium").Token);
         client.DefaultRequestHeaders.Add("X-Test-Mode", "true");
         var config = factory.Services.GetRequiredService<IConfiguration>();
         foreach (var testMode in new[] { false, true, false })
diff --git a/tests/TestSupport/ApiFactory.cs b/tests/TestSupport/ApiFactory.cs
index 2241722..840c257 100644
--- a/tests/TestSupport/ApiFactory.cs
+++ b/tests/TestSupport/ApiFactory.cs
@@ -17,24 +17,26 @@ public sealed class ApiFactory(
     : WebApplicationFactory<Program>
 {
     protected override void ConfigureWebHost(IWebHostBuilder builder)
     {
         builder.UseEnvironment(Environments.Development);
         // Test hosts should not write the machine-wide Windows Event Log.
         builder.ConfigureLogging(logging => logging.ClearProviders());
         builder.UseSetting("ConnectionStrings:Default", connectionString);
         builder.UseSetting("Jwt:Key", TestConfig.JwtKey);
         builder.UseSetting("Jwt:Issuer", TestConfig.Issuer);
         builder.UseSetting("Jwt:Audience", TestConfig.Audience);
         builder.UseSetting("AiProtection:Enabled", "false");
+        builder.UseSetting("Apple:AppStoreServer:Environment", "Production");
+        builder.UseSetting("Apple:AppStoreServer:AllowedProductIds", TestConfig.Create()["Apple:AppStoreServer:AllowedProductIds"]);
 
         if (configurationOverrides is not null)
         {
             foreach (var item in configurationOverrides)
                 builder.UseSetting(item.Key, item.Value);
         }
 
         builder.ConfigureServices(services =>
         {
             services.RemoveAll<DbContextOptions<AppDbContext>>();
             services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(connectionString));
             configureTestServices?.Invoke(services);
diff --git a/tests/TestSupport/TestConfig.cs b/tests/TestSupport/TestConfig.cs
index 498453c..cdc844c 100644
--- a/tests/TestSupport/TestConfig.cs
+++ b/tests/TestSupport/TestConfig.cs
@@ -11,26 +11,31 @@ internal static class TestConfig
     public static IConfiguration Create(IDictionary<string, string?>? overrides = null)
     {
         var values = new Dictionary<string, string?>
         {
             ["Jwt:Key"] = JwtKey,
             ["Jwt:Issuer"] = Issuer,
             ["Jwt:Audience"] = Audience,
             ["Jwt:ExpiresMinutes"] = "60",
             ["Jwt:DeviceExpiresMinutes"] = "30",
             ["AI:Provider"] = "OpenAI",
             ["Apple:ClientId"] = "com.mavrylo.owlai",
             ["Apple:TeamId"] = "TEAMID1234",
-            ["Apple:SkipSignatureValidation"] = "false"
+            ["Apple:SkipSignatureValidation"] = "false",
+            ["Apple:AppStoreServer:Environment"] = "Production",
+            ["Apple:AppStoreServer:AllowedProductIds"] = "monthly,product,com.mavrylo.monthly,com.flashcardai.owlai.premium.monthly,com.flashcardai.owlai.premium.yearly"
         };
 
         if (overrides != null)
         {
             foreach (var item in overrides)
                 values[item.Key] = item.Value;
         }
 
         return new ConfigurationBuilder()
             .AddInMemoryCollection(values)
             .Build();
     }
+
+    public static Mavrylo.Services.SubscriptionValidationPolicy SubscriptionPolicy(string environment = "Production")
+        => new(Create(new Dictionary<string, string?> { ["Apple:AppStoreServer:Environment"] = environment }), new FakeEnvironment("Production"));
 }
