using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mavrylo.Data;
using Mavrylo.Dtos;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mavrylo.Tests;

public class SharedSubscriptionPostgresTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    private AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);

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
            new SubscriptionEntity { OriginalTransactionId = "account-"+id, DeviceUuid = "other-"+id, OwnerAccountId = session.Profile.Id, ClaimedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(20), WasEverPaid = true },
            new SubscriptionEntity { OriginalTransactionId = "guest-"+id, DeviceUuid = id, ExpiresAt = DateTime.UtcNow.AddDays(10), WasEverPaid = true });
        await db.SaveChangesAsync();
        apple.VerifyTransactionResult = new(true, new SubscriptionEntity { OriginalTransactionId = "guest-"+id, DeviceUuid = id, ExpiresAt = DateTime.UtcNow.AddDays(10), WasEverPaid = true }, true, null);
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
            Assert.False(entitlement.GetProperty("purchase_is_linked").GetBoolean());
        }
        client.DefaultRequestHeaders.Remove("X-Account-Authorization");
        var guest = await client.GetFromJsonAsync<JsonElement>("/owlai/iap/entitlement");
        Assert.Equal("device", guest.GetProperty("entitlement").GetProperty("resolution_source").GetString());
        Assert.Equal("premium", guest.GetProperty("entitlement").GetProperty("status").GetString());
        Assert.False(guest.GetProperty("entitlement").GetProperty("purchase_is_linked").GetBoolean());
        Assert.True(await new SubscriptionOwnershipService(db, TimeProvider.System).TryClaimAsync("guest-"+id, session.Profile.Id));
        var linked = await client.GetFromJsonAsync<JsonElement>("/owlai/iap/entitlement");
        Assert.Equal("account_required", linked.GetProperty("entitlement").GetProperty("status").GetString());
        Assert.True(linked.GetProperty("entitlement").GetProperty("purchase_is_linked").GetBoolean());
        client.DefaultRequestHeaders.Add("X-Account-Authorization", "Bearer "+session.AccessToken);
        var linkedAccount = await client.GetFromJsonAsync<JsonElement>("/owlai/iap/entitlement");
        Assert.Equal("account", linkedAccount.GetProperty("entitlement").GetProperty("resolution_source").GetString());
        Assert.True(linkedAccount.GetProperty("entitlement").GetProperty("purchase_is_linked").GetBoolean());
    }

    [Fact]
    public async Task ConcurrentFullFirstClaimsSerializeBeforeInsertAndCannotTransferPurchase()
    {
        await using var setup = Database();
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
            var service = new AccountEntitlementService(db, new(db, TimeProvider.System), apple, new(db, TimeProvider.System), TimeProvider.System, new FakeEnvironment("Production"));
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
    {
        await using var setup = Database();
        await setup.Database.MigrateAsync();
        var account = Guid.NewGuid().ToString("N");
        var config = TestConfig.Create(new Dictionary<string,string?> { ["AccountAi:DailyQuota"] = "10", ["AccountAi:RequestsPerMinute"] = "5" });
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-09-08T10:00:00Z"));
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var db = Database();
            return await new AccountAiUsageService(db, config, clock).TryConsumeAsync(account, default);
        }));
        Assert.Equal(5, results.Count(x => x));
        Assert.Equal(5, await setup.AiUsage.Where(x => x.KeyId == "account:"+account && x.Date == "2026-09-08").Select(x => x.Count).SingleAsync());
    }

    [Fact]
    public async Task ConcurrentClaimsHaveExactlyOneOwnerAndDeletionRetainsTombstone()
    {
        await using var db = Database();
        await db.Database.MigrateAsync();
        var otid = Guid.NewGuid().ToString("N");
        var a = new AppUser { Email = otid + "a@example.test", PasswordHash = "unused" };
        var b = new AppUser { Email = otid + "b@example.test", PasswordHash = "unused" };
        db.Users.AddRange(a, b);
        db.Subscriptions.Add(new SubscriptionEntity { OriginalTransactionId = otid });
        await db.SaveChangesAsync();
        await using var first = Database();
        await using var second = Database();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Claim(AppDbContext context, string owner) { await gate.Task; return await new SubscriptionOwnershipService(context, TimeProvider.System).TryClaimAsync(otid, owner); }
        var left = Claim(first, a.Id); var right = Claim(second, b.Id);
        gate.SetResult();
        var results = await Task.WhenAll(left, right);
        Assert.Single(results, x => x);
        var owner = (await db.Subscriptions.AsNoTracking().SingleAsync(x => x.OriginalTransactionId == otid)).OwnerAccountId!;
        Assert.True(await new SubscriptionOwnershipService(db, TimeProvider.System).TryClaimAsync(otid, owner));
        await db.Users.Where(x => x.Id == owner).ExecuteDeleteAsync();
        var tombstone = await db.Subscriptions.AsNoTracking().SingleAsync(x => x.OriginalTransactionId == otid);
        Assert.Null(tombstone.OwnerAccountId);
        Assert.NotNull(tombstone.ClaimedAt);
        Assert.False(await new SubscriptionOwnershipService(db, TimeProvider.System).TryClaimAsync(otid, owner == a.Id ? b.Id : a.Id));
    }

    [Fact]
    public async Task AccountPublicationsNeverAdoptDeviceOrOtherAccountPublications()
    {
        await using var db = Database();
        await db.Database.MigrateAsync();
        var a = new AppUser { Email = Guid.NewGuid() + "@example.test" };
        var b = new AppUser { Email = Guid.NewGuid() + "@example.test" };
        var device = new DeviceEntity { KeyId = Guid.NewGuid().ToString() };
        db.Users.AddRange(a, b); db.Devices.Add(device); await db.SaveChangesAsync();
        var service = new PublicFlashcardSetService(db, TimeProvider.System, NullLogger<PublicFlashcardSetService>.Instance);
        var request = new PublicFlashcardSetUpsertRequest("shared-id", "Set", null, [new("card", "cat", ["gato"], null, null, [], [], null, "es", "en")]);
        var devicePublication = await service.UpsertAsync(device.Id, request, default);
        var accountPublication = await service.UpsertForAccountAsync(a.Id, request, default);
        var otherPublication = await service.UpsertForAccountAsync(b.Id, request, default);
        Assert.Equal(3, new[] { devicePublication.Id, accountPublication.Id, otherPublication.Id }.Distinct().Count());
        Assert.Equal("pending", accountPublication.Status);
        await service.UnpublishForAccountAsync(a.Id, "shared-id", default);
        Assert.Single(await service.ListMineAsync(device.Id, default));
        Assert.Single(await service.ListAccountMineAsync(b.Id, default));
        Assert.Empty(await service.ListAccountMineAsync(a.Id, default));
    }

    [Fact]
    public async Task ClaimApiRequiresBothIdentities_ThenLogoutAndDeletionCloseSharedAccess()
    {
        var otid = Guid.NewGuid().ToString("N");
        var purchase = new SubscriptionEntity { OriginalTransactionId = otid, ProductId = "monthly", DeviceUuid = "device-"+otid, ExpiresAt = DateTime.UtcNow.AddDays(10), WasEverPaid = true };
        var apple = new FakeAppStoreServerClient { VerifyTransactionResult = new(true, purchase, true, null), SubscriptionStatusesResult = new(true, purchase, null) };
        await using var factory = new ApiFactory(postgres.ConnectionString, services =>
        {
            services.RemoveAll<IAppStoreServerClient>(); services.AddSingleton<IAppStoreServerClient>(apple);
            services.RemoveAll<IAppAttestVerifier>(); services.AddSingleton<IAppAttestVerifier>(new FakeAppAttestVerifier { IsDevelopmentBypassEnabled = true });
        }, new Dictionary<string,string?> { ["AccountAi:DailyQuota"] = "200", ["AccountAi:RequestsPerMinute"] = "30", ["AiProtection:Enabled"] = "true", ["AiProtection:RequireAssertion"] = "false" });
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var first = await accounts.SignInAsync(new("apple-owner-"+otid, null, "Owner"), default, allowCreation: true);
        var second = await accounts.SignInAsync(new("apple-stranger-"+otid, null, "Other"), default, allowCreation: true);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var keyId = "SIMULATOR-"+otid;
        db.Devices.Add(new DeviceEntity { KeyId = keyId, DeviceUuid = purchase.DeviceUuid });
        await db.SaveChangesAsync();
        var deviceJwt = scope.ServiceProvider.GetRequiredService<JwtTokenService>().CreateDeviceToken(keyId, "free").Token;

        client.DefaultRequestHeaders.Authorization = new("Bearer", deviceJwt);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/owlai/account/entitlement")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", first.AccessToken);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/owlai/account/ai/analyze-word", new { word = "cat" })).StatusCode);
        var body = new { account_id = first.Profile.Id, jws_transaction = "proof" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/owlai/account/subscription/apple/claim", body)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Device-Authorization", "Bearer " + deviceJwt);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/owlai/account/subscription/apple/claim", new { account_id = second.Profile.Id, jws_transaction = "proof" })).StatusCode);
        foreach (var repeat in Enumerable.Range(0, 2))
        {
            var response = await client.PostAsJsonAsync("/owlai/account/subscription/apple/claim", body);
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            Assert.Equal("premium", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/owlai/account/ai/analyze-word", new { word = "" })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", deviceJwt);
        client.DefaultRequestHeaders.Add("X-Account-Authorization", "Bearer " + first.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "" })).StatusCode);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        Assert.Equal(2, await db.AiUsage.AsNoTracking().Where(x => x.KeyId == "account:"+first.Profile.Id && x.Date == today).Select(x => x.Count).SingleAsync());
        client.DefaultRequestHeaders.Remove("X-Account-Authorization");
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "" })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", second.AccessToken);
        var conflict = await client.PostAsJsonAsync("/owlai/account/subscription/apple/claim", new { account_id = second.Profile.Id, jws_transaction = "proof" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("subscription_already_linked", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        client.DefaultRequestHeaders.Authorization = new("Bearer", first.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/owlai/account/session/logout", new { refresh_token = first.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/owlai/account/entitlement")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", deviceJwt);
        client.DefaultRequestHeaders.Add("X-Account-Authorization", "Bearer " + first.AccessToken);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "" })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Account-Authorization");
        await accounts.DeleteAsync(first.Profile.Id, default);
        client.DefaultRequestHeaders.Authorization = new("Bearer", second.AccessToken);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/owlai/account/subscription/apple/claim", new { account_id = second.Profile.Id, jws_transaction = "proof" })).StatusCode);
    }
}
