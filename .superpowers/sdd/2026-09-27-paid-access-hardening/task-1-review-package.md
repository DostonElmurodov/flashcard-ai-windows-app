# Task 1 review package
Base: 473a39bee299f3df4c7fdcf6f45502f880a9bd8b
Head: 68cbce3c1e9053145283099e8c44c83d3f3a3ced
Scope: regression baseline and partial policy; no product fix expected.

68cbce3 test: define safe paid access audit regressions and policy baseline
 docs/paid-access-policy.md              |  55 ++++++
 tests/PaidAccessAuditRegressionTests.cs | 317 ++++++++++++++++++++++++++++++++
 2 files changed, 372 insertions(+)
diff --git a/docs/paid-access-policy.md b/docs/paid-access-policy.md
new file mode 100644
index 0000000..f124988
--- /dev/null
+++ b/docs/paid-access-policy.md
@@ -0,0 +1,55 @@
+# Paid access contract (local hardening baseline)
+
+Decision ledger: 2026-09-27. This document states the intended contract; it does not claim the audit baseline implements it.
+
+## Approved and pending decisions
+
+- **D1 = B, approved:** iOS purchases and paid use remain available without an account. An account is required for desktop paid access. Attestation, a client UUID, and possession of a copied receipt are different proofs: none alone establishes transferable anonymous ownership. The independent anonymous owner, key recovery, legitimate restore and optional desktop linking contract is pending design. Do not require an account on iOS to resolve this uncertainty.
+- **D2 pending:** numerical limits for free, trial, premium and grace, global guest limits, and monetary budgets are not approved. Synthetic test quotas of zero or one and existing ten-word audit fixtures are test inputs, not proposed commercial defaults.
+- **D3 approved:** previously paid content remains readable and reviewable after paid expiry. New AI, adding and content editing are forbidden; deletion and export remain permitted. `review` here means reviewing saved content; AI enrichment is `new_ai` and is forbidden after expiry.
+
+## State × operation × platform
+
+Each cell applies to backend authorization and both clients where the operation exists. Backend does not serve all local read/review/export operations; clients enforce those local gates. `Allowed` still requires valid operation input and applicable proof. `Pending` must not be implemented as an implicit allowance.
+
+| State | Platform | new_ai | add | edit_content | read | review | delete | export |
+|---|---|---|---|---|---|---|---|---|
+| Active paid, verified owner | iOS anonymous; desktop authenticated account | Allowed, metered (D2 pending) | Allowed | Allowed | Allowed | Allowed | Allowed | Allowed |
+| Active trial / legitimate grace | iOS; desktop | Allowed, metered (D2 pending) | Allowed | Allowed | Allowed | Allowed | Allowed | Allowed |
+| Expired paid, existing content | iOS; desktop | Forbidden | Forbidden | Forbidden | Allowed | Allowed | Allowed | Allowed |
+| Free | iOS; desktop | Limited, D2 pending | Limited, D2 pending | Pending free content policy | Existing allowed content | Existing allowed content | Allowed | Allowed |
+| Expired trial / revoked | iOS; desktop | Forbidden | Forbidden | Forbidden | Pending retention scope | Pending retention scope | Allowed | Allowed |
+| Desktop missing account/session | desktop paid operations | Forbidden | Forbidden paid mutation | Forbidden paid mutation | Existing local retention rules | Existing local retention rules | Allowed local operation | Allowed local operation |
+| Missing/invalid server authentication or request proof | backend operations | Forbidden | Forbidden | Forbidden | Forbidden if protected route | Forbidden if protected route | Forbidden if protected route | Forbidden if protected route |
+
+Production authorization excludes Sandbox and LocalTesting purchase data. Test access is controlled by server configuration, never client flags; test mode does not remove authentication. Lifecycle updates must refer to the exact verified original transaction, preserving ownership. A newer canonical state for that same original transaction may supersede a historical notification; a different transaction may not.
+
+## Server denial contract
+
+For forbidden server operations, the action and all paid providers must receive **zero calls**. No fallback attempt is allowed after a denial.
+
+| Status | Meaning |
+|---|---|
+| 401 | Missing, expired or invalid authenticated session/device token |
+| 403 | Invalid request proof, or caller lacks independently verified ownership |
+| 402 | No applicable active subscription or content entitlement |
+| 409 | Existing purchase ownership conflicts; preserve this established conflict status |
+| 429 | Applicable quota exhausted; exact D2 limits pending |
+| 503 | External verification, canonical identity verification or spend budget unavailable; fail closed |
+
+Malformed input can return 400 and oversized input 413; this denial table does not replace ordinary validation responses.
+
+## Reproducible audit regressions
+
+Run `dotnet test tests/Mavrylo.Tests.csproj --filter FullyQualifiedName~PaidAccess --logger "trx;LogFileName=paid-access-regressions.trx"`. B3 requires Docker PostgreSQL or an explicitly isolated local `OWL_TEST_POSTGRES`; the fixture creates and drops its own uniquely named database. Other scenarios use in-memory SQLite. Apple verification is modeled by fake signed/canonical results and AI by a counting fake; no external Apple or AI request occurs.
+
+| Audit | Safe expectation / controls |
+|---|---|
+| B1 | Newly attested key claiming another device UUID stays free; exhausted synthetic free quota gives 429 and zero provider calls. Valid registration itself remains allowed. |
+| B2 | Unrelated canonical transaction gives 503 without persistence. Copied receipt cannot transfer an existing guest purchase to an unrelated key/device or first-claim desktop ownership without prior independent owner proof (403). Same-device legitimate purchase/restore without account stays allowed; existing account-owner conflict remains 409. Legitimate cross-device anonymous restore/recovery proof mechanism is pending design. |
+| B3 | `word` and `Word` both reserve the same ten-word audit fixture; eleventh request gives 402 without another provider call. Lowercase case is a positive control. |
+| B4 | Independently authenticated account subject shares usage across consumers; a different account remains independent. Anonymous key rotation/global guest guard contract is explicitly skipped pending proof design and D2. Never merge subjects because client UUID strings match. |
+| B5 | Cached Sandbox/LocalTesting cannot grant production AI: 402 and zero provider calls. Production active purchase is a positive control. |
+| B6 | REFUND with unrelated canonical purchase returns 503 for retry and cannot insert that other purchase. Matching canonical active/revoked state remains authoritative. |
+
+Initial red tests are evidence for later fixes. Skipped future contracts are missing coverage, not verified security. This baseline does not test real Apple signatures, real StoreKit restores, device acceptance, production deployment, or actual monetary budgets.
diff --git a/tests/PaidAccessAuditRegressionTests.cs b/tests/PaidAccessAuditRegressionTests.cs
new file mode 100644
index 0000000..e1fc5a7
--- /dev/null
+++ b/tests/PaidAccessAuditRegressionTests.cs
@@ -0,0 +1,317 @@
+using System.Net;
+using System.Text;
+using System.Text.Json;
+using Mavrylo.Data;
+using Mavrylo.Dtos;
+using Mavrylo.Filters;
+using Mavrylo.Models;
+using Mavrylo.Services;
+using Mavrylo.Tests.TestSupport;
+using Microsoft.AspNetCore.Http;
+using Microsoft.AspNetCore.Mvc;
+using Microsoft.AspNetCore.Mvc.Abstractions;
+using Microsoft.AspNetCore.Mvc.Filters;
+using Microsoft.AspNetCore.Mvc.ModelBinding;
+using Microsoft.AspNetCore.Routing;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.DependencyInjection.Extensions;
+using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Options;
+using Xunit;
+
+namespace Mavrylo.Tests;
+
+// Security expectations, intentionally red on audit baseline 473a39b. Fake Apple/AI only.
+public sealed class PaidAccessAuditRegressionTests
+{
+    private static readonly TimeProvider Clock = TimeProvider.System;
+    private static readonly Microsoft.Extensions.Configuration.IConfiguration Config = TestConfig.Create(
+        new Dictionary<string, string?> { ["TestMode:Enabled"] = "false", ["Apple:AppStoreServer:Environment"] = "Production" });
+
+    [Fact]
+    public async Task B1_NewAttestedKeyCannotInheritPurchaseBySupplyingOwnerUuid()
+    {
+        using var fixture = TestDb.Create();
+        var db = fixture.Db;
+        db.Subscriptions.Add(Purchase("owner-purchase", "known-owner"));
+        await db.SaveChangesAsync();
+        var challenges = new ChallengeService(db, Clock);
+        var challenge = await challenges.IssueBootstrapAsync();
+        var registration = new AppAttestRegistrationService(db, challenges, AcceptedAttestation(),
+            new JwtTokenService(Config, Clock), Clock, NullLogger<AppAttestRegistrationService>.Instance);
+        var registered = await registration.RegisterAsync(new("new-key", "AQID",
+            Convert.ToBase64String(challenge.Nonce), challenge.ChallengeId, "known-owner"), default);
+        Assert.Equal(200, registered.Status); // Valid new attestation is allowed; ownership is separate.
+        var outcome = await InvokeAi(db, "new-key", quota: 0);
+        Assert.Equal((429, 0), outcome); // New free subject cannot spend paid entitlement.
+    }
+
+    [Fact]
+    public async Task B2_DeviceVerifyRejectsDifferentCanonicalPurchaseWithoutPersistingIt()
+    {
+        using var fixture = TestDb.Create();
+        var db = fixture.Db;
+        await AddDevice(db, "key", "device");
+        var apple = Apple(Purchase("requested", "device"), Purchase("unrelated", "device"));
+        var result = await Iap(db, apple).VerifyAsync("key", new("synthetic-signed-proof"), default);
+        Assert.Equal(503, result.Status); // External canonical response does not validate requested identity.
+        Assert.Empty(await db.Subscriptions.ToListAsync());
+    }
+
+    [Fact]
+    public async Task B2_CopiedReceiptCannotTransferExistingGuestPurchaseToUnrelatedDevice()
+    {
+        using var fixture = TestDb.Create();
+        var db = fixture.Db;
+        await AddDevice(db, "unrelated-key", "unrelated-device");
+        db.Subscriptions.Add(Purchase("purchase", "original-device"));
+        await db.SaveChangesAsync();
+        var apple = Apple(Purchase("purchase", "original-device"), Purchase("purchase", "original-device"));
+        var result = await Iap(db, apple).VerifyAsync("unrelated-key", new("copied-synthetic-signed-proof"), default);
+        Assert.Equal(403, result.Status);
+        Assert.Equal("original-device", (await db.Subscriptions.AsNoTracking().SingleAsync()).DeviceUuid);
+        Assert.Equal((429, 0), await InvokeAi(db, "unrelated-key", quota: 0));
+    }
+
+    [Fact]
+    public async Task B2_CopiedReceiptCannotFirstClaimDesktopOwnershipWithoutPriorOwnerProof()
+    {
+        using var fixture = TestDb.Create();
+        var db = fixture.Db;
+        await AddDevice(db, "unrelated-key", "unrelated-device");
+        db.Users.Add(new AppUser { Id = "unrelated-account", Email = "unrelated@test.test" });
+        db.Subscriptions.Add(Purchase("purchase", "original-device"));
+        await db.SaveChangesAsync();
+        var apple = Apple(Purchase("purchase", "original-device"), Purchase("purchase", "original-device"));
+        var entitlements = new EntitlementService(db, Clock);
+        var accounts = new AccountEntitlementService(db, entitlements, apple, new(db, Clock), Clock, new FakeEnvironment("Production"));
+        Assert.Equal(403, (await accounts.ClaimAsync("unrelated-account", "unrelated-key", "copied-synthetic-signed-proof", default)).Status);
+        var stored = await db.Subscriptions.AsNoTracking().SingleAsync();
+        Assert.Null(stored.OwnerAccountId);
+        Assert.Equal("original-device", stored.DeviceUuid);
+    }
+
+    [Fact]
+    public async Task PositiveControl_LegitimateSameDevicePurchaseAndRestoreRemainAllowedWithoutAccount()
+    {
+        using var fixture = TestDb.Create();
+        var db = fixture.Db;
+        await AddDevice(db, "key", "device");
+        var apple = Apple(Purchase("purchase", "device"), Purchase("purchase", "device"));
+        var iap = Iap(db, apple);
+        Assert.Equal(200, (await iap.VerifyAsync("key", new("synthetic-signed-proof"), default)).Status);
+        Assert.Equal(200, (await iap.VerifyAsync("key", new("synthetic-signed-proof"), default)).Status);
+        Assert.Single(await db.Subscriptions.ToListAsync());
+        Assert.Equal((200, 1), await InvokeAi(db, "key", quota: 0));
+    }
+
+    [Fact]
+    public async Task PositiveControl_AlreadyClaimedPurchaseCannotChangeAccountOwner()
+    {
+        using var fixture = TestDb.Create();
+        var db = fixture.Db;
+        await AddDevice(db, "key", "device");
+        db.Users.AddRange(new AppUser { Id = "owner", Email = "owner@test.test" }, new AppUser { Id = "other", Email = "other@test.test" });
+        var purchase = Purchase("owned", "device");
+        purchase.OwnerAccountId = "owner";
+        purchase.ClaimedAt = DateTime.UtcNow;
+        db.Subscriptions.Add(purchase);
+        await db.SaveChangesAsync();
+        var apple = Apple(Purchase("owned", "device"), Purchase("owned", "device"));
+        var entitlements = new EntitlementService(db, Clock);
+        var accounts = new AccountEntitlementService(db, entitlements, apple, new(db, Clock), Clock, new FakeEnvironment("Production"));
+        Assert.Equal(409, (await accounts.ClaimAsync("other", "key", "synthetic-signed-proof", default)).Status);
+        Assert.Equal("owner", (await db.Subscriptions.AsNoTracking().SingleAsync()).OwnerAccountId);
+    }
+
+    [Fact]
+    public async Task B4_PositiveControl_ProvenAccountSubjectRetainsUsageAcrossConsumers()
+    {
+        using var fixture = TestDb.Create();
+        var config = TestConfig.Create(new Dictionary<string, string?> { ["AccountAi:DailyQuota"] = "1", ["AccountAi:RequestsPerMinute"] = "10" });
+        Assert.True(await new AccountAiUsageService(fixture.Db, config, Clock).TryConsumeAsync("authenticated-owner", default));
+        Assert.False(await new AccountAiUsageService(fixture.Db, config, Clock).TryConsumeAsync("authenticated-owner", default));
+        Assert.True(await new AccountAiUsageService(fixture.Db, config, Clock).TryConsumeAsync("different-authenticated-owner", default));
+    }
+
+    [Fact(Skip = "B4 key-rotation subject proof/global guest budget contract and D2 amounts pending; sharing a client UUID is not owner proof.")]
+    public void B4_NewKeysCannotResetProvenOwnerUsageOrEscapeGlobalGuestBudget() { }
+
+    [Theory]
+    [InlineData("Sandbox")]
+    [InlineData("LocalTesting")]
+    public async Task B5_CachedNonProductionPurchaseCannotGrantProductionAi(string environment)
+    {
+        using var fixture = TestDb.Create();
+        var db = fixture.Db;
+        await AddDevice(db, "key", "device");
+        var purchase = Purchase("non-production", "device");
+        purchase.Environment = environment;
+        db.Subscriptions.Add(purchase);
+        await db.SaveChangesAsync();
+        Assert.Equal((402, 0), await InvokeAi(db, "key", quota: 0));
+    }
+
+    [Fact]
+    public async Task B6_RefundCannotAcknowledgeSuccessWhileUpdatingUnrelatedPurchase()
+    {
+        using var fixture = TestDb.Create();
+        var db = fixture.Db;
+        var entitlements = new EntitlementService(db, Clock);
+        await entitlements.UpsertAsync(Purchase("refunded", "device"));
+        var refunded = Purchase("refunded", "device");
+        refunded.RevokedAt = DateTime.UtcNow;
+        var apple = Apple(refunded, Purchase("unrelated-active", "device"));
+        apple.DecodeNotificationResult = new(true, JsonSerializer.SerializeToElement(new
+        {
+            notificationType = "REFUND", signedDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
+            data = new { signedTransactionInfo = "synthetic-refund-proof" }
+        }), true, null);
+        var result = await new AppStoreNotificationService(apple, entitlements, Clock,
+            NullLogger<AppStoreNotificationService>.Instance).HandleAsync(new("synthetic-notification"), default);
+        Assert.Equal(503, result); // Retry until matching canonical purchase can be checked.
+        Assert.Null(await entitlements.FindByOriginalTransactionAsync("unrelated-active"));
+        Assert.Equal("refunded", (await db.Subscriptions.SingleAsync()).OriginalTransactionId);
+    }
+
+    [Theory]
+    [InlineData(false)]
+    [InlineData(true)]
+    public async Task PositiveControl_MatchingCanonicalStateWinsOverHistoricalRefund(bool canonicalRevoked)
+    {
+        using var fixture = TestDb.Create();
+        var entitlements = new EntitlementService(fixture.Db, Clock);
+        await entitlements.UpsertAsync(Purchase("purchase", "device"));
+        var historical = Purchase("purchase", "device");
+        historical.RevokedAt = DateTime.UtcNow.AddDays(-1);
+        var canonical = Purchase("purchase", "device");
+        canonical.RevokedAt = canonicalRevoked ? DateTime.UtcNow : null;
+        var apple = Apple(historical, canonical);
+        apple.DecodeNotificationResult = new(true, JsonSerializer.SerializeToElement(new
+        { notificationType = "REFUND", data = new { signedTransactionInfo = "synthetic-proof" } }), true, null);
+        Assert.Equal(200, await new AppStoreNotificationService(apple, entitlements, Clock,
+            NullLogger<AppStoreNotificationService>.Instance).HandleAsync(new("synthetic-notification"), default));
+        Assert.Equal(canonicalRevoked ? "revoked" : "premium", entitlements.ToEntitlement(await entitlements.FindByOriginalTransactionAsync("purchase")).Status);
+    }
+
+    [Theory]
+    [InlineData("expired_paid", 402)]
+    [InlineData("revoked", 402)]
+    [InlineData("free", 429)]
+    public async Task NegativeControls_ForbiddenAiMakesZeroProviderCalls(string state, int expectedStatus)
+    {
+        using var fixture = TestDb.Create();
+        await AddDevice(fixture.Db, "key", "device");
+        if (state != "free")
+        {
+            var purchase = Purchase("purchase", "device");
+            if (state == "expired_paid") purchase.ExpiresAt = DateTime.UtcNow.AddDays(-1);
+            else purchase.RevokedAt = DateTime.UtcNow;
+            fixture.Db.Subscriptions.Add(purchase);
+            await fixture.Db.SaveChangesAsync();
+        }
+        Assert.Equal((expectedStatus, 0), await InvokeAi(fixture.Db, "key", quota: 0));
+    }
+
+    private static SubscriptionEntity Purchase(string id, string device) => new()
+    { OriginalTransactionId = id, DeviceUuid = device, ProductId = "monthly", ExpiresAt = DateTime.UtcNow.AddDays(5), WasEverPaid = true, Environment = "Production" };
+
+    private static FakeAppStoreServerClient Apple(SubscriptionEntity verified, SubscriptionEntity canonical) => new()
+    { IsLocalVerifyEnabled = false, VerifyTransactionResult = new(true, verified, true, null), SubscriptionStatusesResult = new(true, canonical, null) };
+
+    private static IapService Iap(AppDbContext db, IAppStoreServerClient apple)
+    {
+        var entitlements = new EntitlementService(db, Clock);
+        return new(apple, entitlements, new(db, entitlements), new(Config, Clock), Clock, NullLogger<IapService>.Instance);
+    }
+
+    private static FakeAppAttestVerifier AcceptedAttestation() => new()
+    { AttestationResult = AppAttestVerifier.AttestationResult.Success([1, 2, 3], 0, "production"), AssertionResult = AppAttestVerifier.AssertionResult.Success(1) };
+
+    private static async Task AddDevice(AppDbContext db, string key, string device)
+    {
+        db.Devices.Add(new DeviceEntity { KeyId = key, DeviceUuid = device, PublicKey = [1, 2, 3] });
+        await db.SaveChangesAsync();
+    }
+
+    // The action represents one synthetic provider attempt. Every rejection must stop before it.
+    private static async Task<(int Status, int ProviderCalls)> InvokeAi(AppDbContext db, string key, int quota)
+    {
+        var filter = new AiProtectionFilter(Options.Create(new AiProtectionOptions { RequireAssertion = true, FreeDailyQuota = quota }),
+            Config, AcceptedAttestation(), new(db, Clock), new(db, new(db, Clock)), new(db, Clock, Config), new(db, Clock, Config),
+            Clock, NullLogger<AiProtectionFilter>.Instance);
+        using var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
+        var http = new DefaultHttpContext { RequestServices = services };
+        http.Request.Path = "/owlai/ai/word-detail";
+        http.Request.Method = "POST";
+        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"word":"hola"}"""));
+        http.Request.Headers.Authorization = "Bearer " + new JwtTokenService(Config, Clock).CreateDeviceToken(key, "premium").Token;
+        var challenge = await new ChallengeService(db, Clock).IssueAssertionAsync(key, http.Request.Path);
+        http.Request.Headers["X-App-Attest-Key-Id"] = key;
+        http.Request.Headers["X-App-Attest-Challenge-Id"] = challenge.ChallengeId;
+        http.Request.Headers["X-App-Attest-Assertion"] = "AQID";
+        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
+        var context = new ResourceExecutingContext(action, [], new List<IValueProviderFactory>());
+        var providerCalls = 0;
+        await filter.OnResourceExecutionAsync(context, () =>
+        {
+            providerCalls++;
+            return Task.FromResult(new ResourceExecutedContext(action, []) { Result = new OkResult() });
+        });
+        return (providerCalls > 0 ? 200 : (context.Result as ObjectResult)?.StatusCode ?? 500, providerCalls);
+    }
+}
+
+public sealed class PaidAccessJsonAuditRegressionTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
+{
+    [Theory]
+    [InlineData("word")]
+    [InlineData("Word")]
+    public async Task B3_CaseInsensitiveBindingCannotBypassTenWordReservation(string property)
+    {
+        var provider = new CountingProvider();
+        await using var factory = new ApiFactory(postgres.ConnectionString, services =>
+        {
+            services.RemoveAll<IAppAttestVerifier>();
+            services.AddSingleton<IAppAttestVerifier>(new FakeAppAttestVerifier { AssertionResult = AppAttestVerifier.AssertionResult.Success(1) });
+            services.RemoveAll<IAiJsonService>();
+            services.AddSingleton<IAiJsonService>(provider);
+        }, new Dictionary<string, string?> { ["TestMode:Enabled"] = "false", ["AiProtection:RequireAssertion"] = "true", ["AiProtection:FreeDailyQuota"] = "40" });
+        using var client = factory.CreateClient();
+        using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var key = Guid.NewGuid().ToString("N");
+        db.Devices.Add(new DeviceEntity { KeyId = key, DeviceUuid = key, PublicKey = [1, 2, 3], Environment = "production" });
+        await db.SaveChangesAsync();
+        client.DefaultRequestHeaders.Authorization = new("Bearer", scope.ServiceProvider.GetRequiredService<JwtTokenService>().CreateDeviceToken(key, "free").Token);
+        const string path = "/owlai/ai/word-detail";
+        for (var i = 0; i < 11; i++)
+        {
+            var challenge = await scope.ServiceProvider.GetRequiredService<ChallengeService>().IssueAssertionAsync(key, path);
+            using var request = new HttpRequestMessage(HttpMethod.Post, path);
+            request.Headers.Add("X-App-Attest-Key-Id", key);
+            request.Headers.Add("X-App-Attest-Challenge-Id", challenge.ChallengeId);
+            request.Headers.Add("X-App-Attest-Assertion", "AQID");
+            request.Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, string>
+            { [property] = key + i, ["native_language"] = "en", ["learning_language"] = "es" }), Encoding.UTF8, "application/json");
+            using var response = await client.SendAsync(request);
+            Assert.Equal(i < 10 ? HttpStatusCode.OK : HttpStatusCode.PaymentRequired, response.StatusCode);
+            Assert.Equal(Math.Min(i + 1, 10), provider.Calls);
+        }
+        Assert.Equal(10, await db.DeviceWords.CountAsync(x => x.DeviceUuid == key));
+        Assert.Equal(10, await db.AiUsage.Where(x => x.KeyId == key).SumAsync(x => x.Count));
+    }
+
+    private sealed class CountingProvider : IAiJsonService
+    {
+        public int Calls { get; private set; }
+        public Task<JsonElement?> CompleteJsonAsync(string operation, string prompt, CancellationToken ct = default)
+        {
+            Calls++;
+            return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new { translations = new[] { "sample" }, translation = "sample", corrected_word = "sample" }));
+        }
+        public Task<JsonElement?> CompleteVisionJsonAsync(string operation, string prompt, byte[] image, string mime, CancellationToken ct = default)
+            => throw new InvalidOperationException("Unexpected vision provider call");
+    }
+}
