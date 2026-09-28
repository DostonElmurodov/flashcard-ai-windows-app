using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Xunit;
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
        testDb.Db.Subscriptions.Add(new SubscriptionEntity { OriginalTransactionId = "stale", OwnerAccountId = "stale-owner", ClaimedAt = now, ExpiresAt = now.AddDays(4), LastCheckedAt = now.AddHours(-1), WasEverPaid = true });
        await testDb.Db.SaveChangesAsync();
        var apple = new FakeAppStoreServerClient { SubscriptionStatusesResult = new(true, new SubscriptionEntity { OriginalTransactionId = "stale", ExpiresAt = now.AddDays(4), RevokedAt = now, WasEverPaid = true }, null) };
        var service = new AccountEntitlementService(testDb.Db, new(testDb.Db, TimeProvider.System), apple, new(testDb.Db, TimeProvider.System), TimeProvider.System, new FakeEnvironment());
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
            new SubscriptionEntity { OriginalTransactionId = "rank-premium", OwnerAccountId = "rank-owner", ClaimedAt = now, ExpiresAt = now.AddDays(1), WasEverPaid = true },
            new SubscriptionEntity { OriginalTransactionId = "rank-trial", OwnerAccountId = "rank-owner", ClaimedAt = now, ExpiresAt = now.AddDays(10), IsTrial = true });
        await testDb.Db.SaveChangesAsync();
        var service = new AccountEntitlementService(testDb.Db, new(testDb.Db, TimeProvider.System), new FakeAppStoreServerClient(), new(testDb.Db, TimeProvider.System), TimeProvider.System, new FakeEnvironment());
        Assert.Equal("premium", (await service.GetAsync("rank-owner")).Status);
    }
    [Fact]
    public async Task OlderOrRepeatedAppleNotificationCannotOverwriteNewerState()
    {
        using var testDb = TestDb.Create();
        var service = new EntitlementService(testDb.Db, TimeProvider.System);
        var now = DateTime.UtcNow;
        await service.UpsertAsync(new SubscriptionEntity { OriginalTransactionId = "events", LastAppleEventAt = now, ExpiresAt = now.AddDays(-1), AutoRenew = false });
        foreach (var signedAt in new[] { now.AddSeconds(-1), now })
            await service.UpsertAsync(new SubscriptionEntity { OriginalTransactionId = "events", LastAppleEventAt = signedAt, ExpiresAt = now.AddDays(10), AutoRenew = true });
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
            new SubscriptionEntity { OriginalTransactionId = "valid", OwnerAccountId = "owner", ClaimedAt = now, ExpiresAt = now.AddDays(1), WasEverPaid = true },
            new SubscriptionEntity { OriginalTransactionId = "revoked", OwnerAccountId = "owner", ClaimedAt = now, ExpiresAt = now.AddDays(20), RevokedAt = now },
            new SubscriptionEntity { OriginalTransactionId = "sandbox", OwnerAccountId = "owner", ClaimedAt = now, ExpiresAt = now.AddDays(40), Environment = "Sandbox" });
        await testDb.Db.SaveChangesAsync();
        var service = new AccountEntitlementService(testDb.Db, new(testDb.Db, TimeProvider.System), new FakeAppStoreServerClient(), new(testDb.Db, TimeProvider.System), TimeProvider.System, new FakeEnvironment("Production"));
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
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-09-08T10:00:00Z"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["AccountAi:DailyQuota"] = "2", ["AccountAi:RequestsPerMinute"] = "1" }).Build();
        var service = new AccountAiUsageService(testDb.Db, config, clock);
        Assert.True(await service.TryConsumeAsync("owner", default));
        Assert.False(await service.TryConsumeAsync("owner", default));
        Assert.Equal(1, await testDb.Db.AiUsage.Where(x => x.KeyId == "account:owner" && x.Date == "2026-09-08").Select(x => x.Count).SingleAsync());
        Assert.True(await service.TryConsumeAsync("other", default));
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
        var purchase = new SubscriptionEntity { OriginalTransactionId = "purchase", Environment = environment, ExpiresAt = DateTime.UtcNow.AddDays(3) };
        var apple = new FakeAppStoreServerClient { VerifyTransactionResult = new(true, purchase, signed, null), SubscriptionStatusesResult = new(true, purchase, null) };
        var service = new AccountEntitlementService(testDb.Db, new(testDb.Db, TimeProvider.System), apple, new(testDb.Db, TimeProvider.System), TimeProvider.System, new FakeEnvironment("Production"));
        var response = await service.ClaimAsync("owner", "key", "signed-proof", default);
        Assert.Equal(expected, response.Status);
        Assert.Empty(await testDb.Db.Subscriptions.ToListAsync());
    }

    [Fact]
    public async Task RestoreCannotTransferOwnerOrMakeClaimedPurchaseGuestAccessible()
    {
        using var testDb = TestDb.Create();
        var service = new EntitlementService(testDb.Db, TimeProvider.System);
        var sub = await service.UpsertAsync(new SubscriptionEntity { OriginalTransactionId = "owned", DeviceUuid = "first", ExpiresAt = DateTime.UtcNow.AddDays(5) });
        testDb.Db.Users.Add(new AppUser { Id = "owner", Email = "owner@test.test" }); sub.OwnerAccountId = "owner"; sub.ClaimedAt = DateTime.UtcNow;
        await testDb.Db.SaveChangesAsync();
        await service.UpsertAsync(new SubscriptionEntity { OriginalTransactionId = "owned", DeviceUuid = "second", ExpiresAt = DateTime.UtcNow.AddDays(7), OwnerAccountId = "attacker" });
        Assert.Equal("owner", sub.OwnerAccountId);
        Assert.Equal("account_required", service.ToDeviceEntitlement(sub).Status);
    }
    [Fact]
    public async Task AtomicOwnershipIsIdempotentAndTombstoneCannotBeClaimed()
    {
        using var testDb = TestDb.Create();
        testDb.Db.Users.AddRange(new AppUser { Id = "alice", Email = "alice@test.test" }, new AppUser { Id = "bob", Email = "bob@test.test" });
        testDb.Db.Subscriptions.Add(new SubscriptionEntity { OriginalTransactionId = "purchase" });
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
