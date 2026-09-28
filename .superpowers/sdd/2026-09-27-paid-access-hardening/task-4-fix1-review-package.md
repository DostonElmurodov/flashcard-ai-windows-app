# Task 4 fix round 1 — base 4db300428adccf820584aeb0860afd73af3b673f; head 5c875dfe0f05b076abcda6da86e5ebd151652970
5c875df Reject infinite subscription expiry and validate claimed device projection
 docs/apple-subscription-validation.md              |  2 +
 .../Services/EntitlementService.cs                 |  7 ++-
 .../Services/SubscriptionValidationPolicy.cs       |  4 +-
 tests/SharedSubscriptionPostgresTests.cs           | 62 ++++++++++++++++++++++
 tests/SubscriptionLifecycleTests.cs                | 28 ++++++++++
 5 files changed, 101 insertions(+), 2 deletions(-)
diff --git a/docs/apple-subscription-validation.md b/docs/apple-subscription-validation.md
index 6be78dd..d1d3fe2 100644
--- a/docs/apple-subscription-validation.md
+++ b/docs/apple-subscription-validation.md
@@ -1,15 +1,17 @@
 # Apple subscription validation and rollout boundary
 
 The device and account resolvers apply the same server-owned purchase policy. A usable row requires a nonempty original transaction ID, an explicitly allowed product, a finite expiry, and an environment matching `Apple:AppStoreServer:Environment` (legacy `Apple:Environment` fallback). Missing environment configuration defaults to Production and a missing product allow-list admits no products. Allowed product IDs support comma-separated values or configuration children. Signed transaction projections also require the auto-renewable subscription type and validate the configured bundle ID.
 
+Finite expiry excludes PostgreSQL `infinity` and `-infinity`, which Npgsql maps to `DateTime.MaxValue` and `DateTime.MinValue`. Those rows follow the same quarantine and no-mutation rules below. Device validation runs before claimed-purchase projection, so invalid claimed/tombstoned rows cannot expose validated paid history or `account_required`; valid claimed rows retain the existing ownership protection.
+
 Sandbox must run with explicitly configured Sandbox server settings and separate database/usage budgets. A client flag never selects the purchase environment. Unsigned local StoreKit is available only in DEBUG + Development with an explicit LocalTesting/Xcode server environment; Release never enables that path. Creating and validating the isolated App Review/Sandbox deployment remains a rollout prerequisite.
 
 DTO shapes remain unchanged. An existing invalid device purchase now returns inactive `invalid_subscription`, with no validated paid-history claim; a missing purchase still returns `free`. Client unknown/inactive-state handling must be integrated before release. Account selection excludes invalid rows, and it does not turn revoked purchases into expired_paid merely because a historical paid flag exists. An independent valid purchase can still grant access or retain ordinary expired-paid content rights. Existing claimed-purchase/account-required behavior remains intact while anonymous ownership recovery is redesigned separately.
 
 An inapplicable stored row is quarantined during refresh: its metadata, paid-history flag, device link and claim/tombstone fields are preserved. Verify/claim returns HTTP503 with `subscription_reconciliation_required`; cached reads deny that row, and notifications return503 so delivery retries. A newly verified Apple response does not automatically repair that historical row. **Before enabling enforcement, inventory and reconcile such legacy rows against trusted Apple evidence without deleting purchases/cards/tombstones or requiring repurchase.** This release gate includes potentially legitimate historical purchasers whose rows are incomplete or whose product configuration changed.
 
 Canonical Apple status selection filters by original transaction ID before ranking and rechecks the signed ID before persistence. Renewal information must match the same purchase, current product and environment; a future renewal product does not rewrite the purchased product. Only a matching finite grace end can extend grace. Expired/retry/revoked canonical states cannot be promoted by stale renewal metadata. During API/network failure, cached access is never extended, and a state-changing notification is not acknowledged as successfully processed. There is no local durable notification queue; recovery relies on Apple's delivery retry and operational history reconciliation if that retry window is exhausted.
 
 Writes are serialized by the existing PostgreSQL purchase lock. Tracked rows are reloaded inside the lock, so earlier context reads cannot defeat event ordering. Dated newer signed transaction/renewal state wins; older or undated input cannot overwrite an established dated state. Repeating the same canonical proof may relink a restore device without rewriting lifecycle fields or ownership. Notification/account/token background refreshes preserve device linkage; token/entitlement responses reselect any independent active purchase after a refresh revokes the previously selected one.
 
 JWS verification uses ES256, embedded Apple Root G3 trust, Apple leaf/intermediate signing-purpose OIDs, and offline certificate validity at the signed payload date. The public decoder never accepts a configurable test root. Reference fixtures are pinned and attributed in `tests/Fixtures/Apple/README.md`. This is offline verification: online OCSP/revocation checks are not implemented, OS certificate-date boundaries are strict (the Node reference allows a 60-second skew), and passing mock/chain tests does not establish physical-device/App Review purchase acceptance. That acceptance still requires the isolated deployment/device matrix.
diff --git a/src/Mavrylo.Services/Services/EntitlementService.cs b/src/Mavrylo.Services/Services/EntitlementService.cs
index 0b0eb5d..49630a6 100644
--- a/src/Mavrylo.Services/Services/EntitlementService.cs
+++ b/src/Mavrylo.Services/Services/EntitlementService.cs
@@ -122,25 +122,30 @@ public sealed class EntitlementService(AppDbContext db, TimeProvider timeProvide
     /// <summary>Project a subscription (or null) into the canonical entitlement, recomputing status.</summary>
     public Entitlement ToEntitlement(SubscriptionEntity? sub)
     {
         if (sub is null)
             return Entitlement.Free();
         if (!IsApplicable(sub))
             return new Entitlement(Status.Invalid, sub.ProductId, sub.ExpiresAt, false, false, false);
         var status = ComputeStatus(sub, timeProvider.GetUtcNow().UtcDateTime);
         return new Entitlement(status, sub.ProductId, sub.ExpiresAt, sub.IsTrial, sub.AutoRenew, sub.WasEverPaid);
     }
 
     public Entitlement ToDeviceEntitlement(SubscriptionEntity? sub)
-        => sub?.ClaimedAt != null ? new Entitlement("account_required", sub.ProductId, sub.ExpiresAt, false, sub.AutoRenew, sub.WasEverPaid) : ToEntitlement(sub);
+    {
+        var entitlement = ToEntitlement(sub);
+        return sub?.ClaimedAt != null && entitlement.Status != Status.Invalid
+            ? entitlement with { Status = "account_required", IsTrial = false }
+            : entitlement;
+    }
 
     public bool IsApplicable(SubscriptionEntity sub) => validation.IsApplicable(sub);
 
     public static int Rank(string status) => status switch
     {
         Status.Premium => 6, Status.Grace => 5, Status.Trial => 4, Status.ExpiredPaid => 3,
         Status.Revoked or "account_required" => 2, Status.ExpiredTrial => 1, _ => 0
     };
 
     /// <summary>
     /// The state machine. Active = not expired. Grace is preserved from <see cref="SubscriptionEntity.Status"/>
     /// (it is set by billing-retry notifications, not derivable from expiry alone).
diff --git a/src/Mavrylo.Services/Services/SubscriptionValidationPolicy.cs b/src/Mavrylo.Services/Services/SubscriptionValidationPolicy.cs
index 916d529..7d721c4 100644
--- a/src/Mavrylo.Services/Services/SubscriptionValidationPolicy.cs
+++ b/src/Mavrylo.Services/Services/SubscriptionValidationPolicy.cs
@@ -21,16 +21,18 @@ public sealed class SubscriptionValidationPolicy(IConfiguration config, IHostEnv
 
     public bool AllowsProduct(string? productId)
     {
         if (string.IsNullOrWhiteSpace(productId)) return false;
         var section = config.GetSection("Apple:AppStoreServer:AllowedProductIds");
         if (!section.Exists()) section = config.GetSection("Apple:AllowedProductIds");
         var allowed = section.GetChildren().Select(x => x.Value?.Trim())
             .Concat((section.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
         return allowed.Contains(productId, StringComparer.Ordinal);
     }
 
     public bool IsApplicable(SubscriptionEntity sub) => !string.IsNullOrWhiteSpace(sub.OriginalTransactionId)
-        && AllowsEnvironment(sub.Environment) && AllowsProduct(sub.ProductId) && sub.ExpiresAt.HasValue;
+        && AllowsEnvironment(sub.Environment) && AllowsProduct(sub.ProductId)
+        // Npgsql maps PostgreSQL +/-infinity to these DateTime sentinels.
+        && sub.ExpiresAt is { } expiry && expiry > DateTime.MinValue && expiry < DateTime.MaxValue;
 
     private string? Option(string key) => config[$"Apple:AppStoreServer:{key}"]?.Trim() ?? config[$"Apple:{key}"]?.Trim();
 }
diff --git a/tests/SharedSubscriptionPostgresTests.cs b/tests/SharedSubscriptionPostgresTests.cs
index 2209585..f73c0df 100644
--- a/tests/SharedSubscriptionPostgresTests.cs
+++ b/tests/SharedSubscriptionPostgresTests.cs
@@ -10,24 +10,86 @@ using Mavrylo.Tests.TestSupport;
 using Microsoft.EntityFrameworkCore;
 using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.DependencyInjection.Extensions;
 using Microsoft.Extensions.Logging.Abstractions;
 using Xunit;
 
 namespace Mavrylo.Tests;
 
 public class SharedSubscriptionPostgresTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
 {
     private AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
 
+    [Theory]
+    [InlineData(true)]
+    [InlineData(false)]
+    public async Task ProviderInfinityExpiryCannotGrantDeviceOrAccountEntitlement(bool positive)
+    {
+        await using var db = Database(); await db.Database.MigrateAsync();
+        var id = Guid.NewGuid().ToString("N");
+        var expiry = await ReadInfinity(db, positive);
+        Assert.Equal(positive ? DateTime.MaxValue : DateTime.MinValue, expiry);
+        db.Users.Add(new AppUser { Id = id, Email = id + "@example.test" });
+        db.Devices.Add(new DeviceEntity { KeyId = id, DeviceUuid = id });
+        db.Subscriptions.AddRange(
+            new SubscriptionEntity { OriginalTransactionId = "guest-" + id, DeviceUuid = id, ProductId = "monthly", ExpiresAt = expiry, WasEverPaid = true },
+            new SubscriptionEntity { OriginalTransactionId = "account-" + id, DeviceUuid = "other-" + id, ProductId = "monthly", ExpiresAt = expiry,
+                WasEverPaid = true, OwnerAccountId = id, ClaimedAt = DateTime.UtcNow });
+        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
+
+        var entitlements = new EntitlementService(db, TimeProvider.System, TestConfig.SubscriptionPolicy());
+        var device = await new DeviceContextService(db, entitlements).ResolveAsync(id);
+        Assert.Equal("invalid_subscription", device!.Entitlement.Status);
+        Assert.False(device.Entitlement.WasEverPaid);
+        var account = await new AccountEntitlementService(db, entitlements, new FakeAppStoreServerClient { IsServerApiConfigured = false },
+            new(db, TimeProvider.System), TimeProvider.System).GetAsync(id);
+        Assert.Equal("free", account.Status); Assert.False(account.WasEverPaid);
+    }
+
+    [Theory]
+    [InlineData(true, false)]
+    [InlineData(true, true)]
+    [InlineData(false, false)]
+    [InlineData(false, true)]
+    public async Task ProviderInfinityExpiryQuarantinesRefreshWithoutMutatingEvidence(bool positive, bool tombstone)
+    {
+        await using var db = Database(); await db.Database.MigrateAsync();
+        var id = Guid.NewGuid().ToString("N"); var expiry = await ReadInfinity(db, positive);
+        if (!tombstone) db.Users.Add(new AppUser { Id = id, Email = id + "@example.test" });
+        db.Subscriptions.Add(new SubscriptionEntity
+        {
+            OriginalTransactionId = id, DeviceUuid = "original-device", ProductId = "monthly", ExpiresAt = expiry,
+            WasEverPaid = true, OwnerAccountId = tombstone ? null : id, ClaimedAt = DateTime.UtcNow.AddDays(-3),
+            LastAppleEventAt = DateTime.UtcNow.AddDays(-2), Status = "premium", AutoRenew = false
+        });
+        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
+        var before = JsonSerializer.Serialize(await db.Subscriptions.AsNoTracking().SingleAsync(x => x.OriginalTransactionId == id));
+        var service = new EntitlementService(db, TimeProvider.System, TestConfig.SubscriptionPolicy());
+
+        await Assert.ThrowsAsync<SubscriptionReconciliationRequiredException>(() => service.UpsertAsync(new SubscriptionEntity
+        {
+            OriginalTransactionId = id, DeviceUuid = "replacement-device", ProductId = "monthly", ExpiresAt = DateTime.UtcNow.AddDays(7),
+            LastAppleEventAt = DateTime.UtcNow, WasEverPaid = false, AutoRenew = true
+        }));
+
+        var after = JsonSerializer.Serialize(await db.Subscriptions.AsNoTracking().SingleAsync(x => x.OriginalTransactionId == id));
+        Assert.Equal(before, after);
+    }
+
+    private static Task<DateTime> ReadInfinity(AppDbContext db, bool positive)
+    {
+        var value = positive ? "infinity" : "-infinity";
+        return db.Database.SqlQuery<DateTime>($"SELECT {value}::timestamptz AS \"Value\"").SingleAsync();
+    }
+
     [Fact]
     public async Task StaleTrackedRowCannotOverwriteNewerAppleStateAfterTakingPurchaseLock()
     {
         await using var stale = Database(); await stale.Database.MigrateAsync();
         var id = Guid.NewGuid().ToString("N"); var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
         SubscriptionEntity State(DateTime signedAt, bool revoked) => new()
         { OriginalTransactionId = id, ProductId = "monthly", Environment = "Production", ExpiresAt = now.AddDays(5), LastAppleEventAt = signedAt, RevokedAt = revoked ? now : null, WasEverPaid = true };
         var staleService = new EntitlementService(stale, TimeProvider.System, TestConfig.SubscriptionPolicy());
         await staleService.UpsertAsync(State(now.AddDays(-2), false));
         await using (var newer = Database())
             await new EntitlementService(newer, TimeProvider.System, TestConfig.SubscriptionPolicy()).UpsertAsync(State(now, false));
         await staleService.UpsertAsync(State(now.AddDays(-1), true));
diff --git a/tests/SubscriptionLifecycleTests.cs b/tests/SubscriptionLifecycleTests.cs
index d71ae26..a6cbe87 100644
--- a/tests/SubscriptionLifecycleTests.cs
+++ b/tests/SubscriptionLifecycleTests.cs
@@ -5,24 +5,52 @@ using Mavrylo.Services;
 using Mavrylo.Tests.TestSupport;
 using Microsoft.EntityFrameworkCore;
 using Microsoft.Extensions.Logging.Abstractions;
 using Xunit;
 
 namespace Mavrylo.Tests;
 
 public sealed class SubscriptionLifecycleTests
 {
     private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
     private static readonly TimeProvider Clock = new ManualTimeProvider(new DateTimeOffset(Now));
 
+    [Theory]
+    [InlineData(false, false)]
+    [InlineData(false, true)]
+    [InlineData(true, false)]
+    [InlineData(true, true)]
+    public async Task DeviceResolverValidatesClaimedAndTombstonedPurchaseBeforeProjectingHistory(bool invalid, bool tombstone)
+    {
+        using var fixture = TestDb.Create(); var service = Entitlements(fixture.Db);
+        var saved = Purchase("claimed"); saved.ClaimedAt = Now.AddDays(-3);
+        saved.Environment = invalid ? "Sandbox" : "Production";
+        if (!tombstone)
+        {
+            fixture.Db.Users.Add(new AppUser { Id = "owner", Email = "owner@test.test" });
+            saved.OwnerAccountId = "owner";
+        }
+        fixture.Db.Devices.Add(new DeviceEntity { KeyId = "key", DeviceUuid = "device" });
+        fixture.Db.Subscriptions.Add(saved); await fixture.Db.SaveChangesAsync();
+        var before = JsonSerializer.Serialize(await fixture.Db.Subscriptions.AsNoTracking().SingleAsync());
+
+        var resolved = await new DeviceContextService(fixture.Db, service).ResolveAsync("key");
+
+        Assert.Equal(invalid ? "invalid_subscription" : "account_required", resolved!.Entitlement.Status);
+        Assert.Equal(!invalid, resolved.Entitlement.WasEverPaid);
+        Assert.Equal(!invalid, resolved.Entitlement.AutoRenew);
+        Assert.True(resolved.Device.RequiresAccountSubscription);
+        Assert.Equal(before, JsonSerializer.Serialize(await fixture.Db.Subscriptions.AsNoTracking().SingleAsync()));
+    }
+
     [Fact]
     public async Task ExplicitSandboxServerWorksForAccountAndDeviceButRejectsProductionHistory()
     {
         using var fixture = TestDb.Create();
         var row = Purchase("sandbox"); row.Environment = "Sandbox";
         await Own(fixture.Db, row);
         var service = new EntitlementService(fixture.Db, Clock, TestConfig.SubscriptionPolicy("Sandbox"));
         var accounts = new AccountEntitlementService(fixture.Db, service, new FakeAppStoreServerClient(), new(fixture.Db, Clock), Clock);
         Assert.Equal("premium", (await accounts.GetAsync("owner")).Status);
         Assert.Equal("premium", service.ToEntitlement(row).Status);
         Assert.Equal("invalid_subscription", service.ToEntitlement(Purchase("production")).Status);
     }
