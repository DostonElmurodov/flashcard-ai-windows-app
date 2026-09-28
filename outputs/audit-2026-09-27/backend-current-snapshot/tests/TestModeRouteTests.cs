using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mavrylo.Data;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Mavrylo.Tests;

public class TestModeRouteTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("not-a-boolean", false)]
    public async Task PublicFlagReflectsServerConfigurationWithoutCaching(string? value, bool expected)
    {
        await using var factory = Factory(value);
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/owlai/config/feature-flags");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, payload.GetProperty("test_mode").GetBoolean());
        Assert.Single(payload.EnumerateObject());
    }

    [Fact]
    public async Task TestModeRemovesAccountSubscriptionAndRequestLimitsAndCanBeTurnedOff()
    {
        await using var factory = Factory("false");
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var session = await accounts.SignInAsync(new("test-mode-" + Guid.NewGuid(), null, "Tester"), default, allowCreation: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        const string path = "/owlai/account/ai/analyze-word";
        // An empty word reaches real input validation without paying for an AI call.
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync(path, new { word = "" })).StatusCode);
        var config = factory.Services.GetRequiredService<IConfiguration>();
        config["TestMode:Enabled"] = "true";
        for (var i = 0; i < 65; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new { word = "" })).StatusCode);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.AiUsage.AnyAsync(x => x.KeyId == "account:" + session.Profile.Id));
        Assert.False(await db.Subscriptions.AnyAsync(x => x.OwnerAccountId == session.Profile.Id));
        var ent = await client.GetFromJsonAsync<JsonElement>("/owlai/account/entitlement");
        Assert.Equal("free", ent.GetProperty("status").GetString());

        // A forged client header cannot keep test mode enabled after the server turns it off.
        config["TestMode:Enabled"] = "false";
        client.DefaultRequestHeaders.Add("X-Test-Mode", "true");
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync(path, new { word = "" })).StatusCode);
    }

    [Fact]
    public async Task TestModeAllowsRepeatedAssertionChallengesButKeepsPasswordAttemptThrottle()
    {
        await using var factory = Factory("true");
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = "test-mode-proof-" + Guid.NewGuid();
        db.Devices.Add(new DeviceEntity { KeyId = key, DeviceUuid = key, Environment = "production" });
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Add("X-App-Attest-Key-Id", key);
        for (var i = 0; i < 25; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/owlai/app-attest/assertion-challenge", new { request_path = "/owlai/ai/word-detail" })).StatusCode);
        for (var i = 0; i < 20; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/owlai/account/email/session", new { email = "absent@example.test", password = "incorrect-password" })).StatusCode);
        var rejected = await client.PostAsJsonAsync("/owlai/account/email/session", new { email = "absent@example.test", password = "incorrect-password" });
        Assert.Contains(rejected.StatusCode, new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.TooManyRequests });
    }

    [Fact]
    public async Task TestModeDoesNotAuthorizeAnonymousOrRevokedAccounts()
    {
        await using var factory = Factory("true");
        using var client = factory.CreateClient();
        const string path = "/owlai/account/ai/analyze-word";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, new { word = "" })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var session = await accounts.SignInAsync(new("revoked-test-mode-" + Guid.NewGuid(), null, "Tester"), default, allowCreation: true);
        await accounts.LogoutAsync(session.RefreshToken, default);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, new { word = "" })).StatusCode);
    }

    [Fact]
    public async Task TestModeAllowsDeviceWordsAndAiBeyondQuotaWithoutGrantingAPurchase()
    {
        await using var factory = Factory("true");
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N");
        var key = "SIMULATOR-" + id;
        db.Devices.Add(new DeviceEntity { KeyId = key, DeviceUuid = id, Environment = "production", RequiresAccountSubscription = true });
        await db.SaveChangesAsync();
        var token = scope.ServiceProvider.GetRequiredService<JwtTokenService>().CreateDeviceToken(key, "account_required").Token;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        for (var i = 0; i < 12; i++)
        {
            var response = await client.PostAsJsonAsync("/owlai/device-words/upsert", new {
                normalized_word = "word-" + i, display_word = "word-" + i, native_language = "en", learning_language = "es"
            });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(12, await db.DeviceWords.CountAsync(x => x.DeviceUuid == id));
        for (var i = 0; i < 65; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "" })).StatusCode);
        Assert.False(await db.AiUsage.AnyAsync(x => x.KeyId == key));
        Assert.False(await db.Subscriptions.AnyAsync(x => x.DeviceUuid == id));
        factory.Services.GetRequiredService<IConfiguration>()["TestMode:Enabled"] = "false";
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "" })).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DevelopmentDeviceCannotRetainTestAccessWhenServerTurnsItOff(bool expired)
    {
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
                WasEverPaid = true, Environment = "Sandbox"
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
        {
            config["TestMode:Enabled"] = testMode.ToString();
            Assert.Equal(testMode ? HttpStatusCode.BadRequest : HttpStatusCode.PaymentRequired,
                (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "" })).StatusCode);
            Assert.Equal(testMode ? HttpStatusCode.OK : HttpStatusCode.PaymentRequired,
                (await client.PostAsJsonAsync("/owlai/device-words/upsert", new
                {
                    normalized_word = Guid.NewGuid().ToString("N"), display_word = "New word", native_language = "en", learning_language = "es"
                })).StatusCode);
        }
        Assert.Equal(11, await db.DeviceWords.CountAsync(x => x.DeviceUuid == id));
        Assert.False(await db.AiUsage.AnyAsync(x => x.KeyId == key));
        var entitlement = await scope.ServiceProvider.GetRequiredService<DeviceContextService>().ResolveAsync(key);
        Assert.Equal(expired ? "expired_paid" : "free", entitlement!.Entitlement.Status);
    }

    private ApiFactory Factory(string? testMode, bool protectionEnabled = true) => new(postgres.ConnectionString, services =>
    {
        services.RemoveAll<IAppAttestVerifier>();
        services.AddSingleton<IAppAttestVerifier>(new FakeAppAttestVerifier { IsDevelopmentBypassEnabled = true });
    }, new Dictionary<string, string?> {
        ["TestMode:Enabled"] = testMode,
        ["AiProtection:Enabled"] = protectionEnabled.ToString(),
        ["AiProtection:RequireAssertion"] = "false",
        ["AiProtection:FreeDailyQuota"] = "0",
        ["AccountAi:DailyQuota"] = "0",
        ["AccountAi:RequestsPerMinute"] = "0"
    });
}
