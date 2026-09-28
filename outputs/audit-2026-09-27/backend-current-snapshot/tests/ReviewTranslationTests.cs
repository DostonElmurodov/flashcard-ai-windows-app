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

public class ReviewTranslationTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    private const string DevicePath = "/owlai/ai/review-translation";
    private const string AccountPath = "/owlai/account/ai/review-translation";

    [Theory]
    [InlineData("/owlai/ai/word-detail")]
    [InlineData("/owlai/account/ai/word-detail")]
    public async Task BundledWordDetailUsesOneCallAndStillRequiresAuthentication(string path)
    {
        var provider = new Provider { Json = """{"primary":{"corrected_word":"hola","translation":"Hello","translations":["Hello"]},"secondary_translation":{"language_code":"fr","translation":"Bonjour","explanation":"Une salutation."}}""" };
        await using var factory = Factory(provider);
        using var anonymous = factory.CreateClient();
        var request = Request(Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(path, request)).StatusCode);
        using var client = path.Contains("/account/") ? factory.CreateClient() : await DeviceClient(factory);
        if (path.Contains("/account/"))
        {
            using var scope = factory.Services.CreateScope();
            var session = await scope.ServiceProvider.GetRequiredService<AccountService>()
                .SignInAsync(new("batch-" + Guid.NewGuid(), null, "Batch"), default, allowCreation: true);
            client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        }
        var response = await client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bonjour", body.GetProperty("secondary_translation").GetProperty("translation").GetString());
        Assert.Equal(1, provider.Calls);
        provider.Throw = true;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(path, request)).StatusCode);
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData(DevicePath)]
    [InlineData(AccountPath)]
    public async Task RequiresAuthenticationEvenInTestMode(string path)
    {
        await using var factory = Factory(new Provider());
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, Request())).StatusCode);
    }

    [Theory]
    [InlineData("", "en", "es", "fr")]
    [InlineData("hola", "unknown", "es", "fr")]
    [InlineData("hola", "en", "unknown", "fr")]
    [InlineData("hola", "en", "es", "unknown")]
    [InlineData("hola", "en", "es", null)]
    [InlineData("hola", "en", "es", " ")]
    [InlineData("hola", "en-us", "es", "English")]
    [InlineData("hola", "fr", "es", "French")]
    public async Task RejectsInvalidInputBeforeProvider(string word, string native, string source, string? target)
    {
        var provider = new Provider();
        await using var factory = Factory(provider);
        using var client = await DeviceClient(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(DevicePath,
            new { word, native_language = native, learning_language = source, secondary_language = target })).StatusCode);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task RejectsOverlongWords()
    {
        await using var factory = Factory(new Provider());
        using var client = await DeviceClient(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(DevicePath, Request(new string('x', 121)))).StatusCode);
    }

    [Fact]
    public async Task CachesCanonicalResultByWordSourceAndTargetSeparatelyFromPrimaryContent()
    {
        var provider = new Provider();
        await using var factory = Factory(provider);
        using var client = await DeviceClient(factory);
        var word = Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<TranslationCacheService>();
        await cache.SaveAsync(word, "fr", "es", TranslationCacheEntity.Kinds.WordDetail, """{"translation":"primary"}""");
        var first = await client.PostAsJsonAsync(DevicePath, Request(word, target: "French"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var result = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("fr", result.GetProperty("language_code").GetString());
        Assert.Equal("Bonjour", result.GetProperty("translation").GetString());
        Assert.Equal("Une salutation.", result.GetProperty("explanation").GetString());
        Assert.Contains("Spanish", provider.Prompt);
        Assert.Contains("French", provider.Prompt);
        Assert.Equal("review-translation", provider.Operation);
        provider.Throw = true;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(DevicePath, Request(" " + word.ToUpperInvariant() + " "))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(DevicePath, Request(word, native: "de"))).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(DevicePath, Request(word, source: "it"))).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(DevicePath, Request(word, target: "ru"))).StatusCode);
        Assert.Equal(3, provider.Calls);
        Assert.Single(await db.TranslationCache.Where(x => x.NormalizedKey == word && x.CacheKind == "review_translation").ToListAsync());
        Assert.False(await db.DeviceWords.AnyAsync(x => x.NormalizedWord == word));
        Assert.False(await db.Words.AnyAsync(x => x.Word == word));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"language_code\":\"fr\",\"translation\":\" \",\"explanation\":\"ok\"}")]
    [InlineData("{\"language_code\":\"fr\",\"translation\":\"ok\",\"explanation\":12}")]
    [InlineData("{\"language_code\":\"ru\",\"translation\":\"ok\",\"explanation\":\"ok\"}")]
    public async Task InvalidProviderResponseIsNotCachedAndCanRetry(string json)
    {
        var provider = new Provider { Json = json };
        await using var factory = Factory(provider);
        using var client = await DeviceClient(factory);
        var word = Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(DevicePath, Request(word))).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.TranslationCache.AnyAsync(x => x.NormalizedKey == word));
        provider.Json = Provider.Valid;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(DevicePath, Request(word))).StatusCode);
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{}")]
    [InlineData("{\"language_code\":\"ru\",\"translation\":\"hello\",\"explanation\":\"greeting\"}")]
    public async Task MalformedCacheIsRegeneratedAndProviderFailureRemainsRetryable(string cached)
    {
        var provider = new Provider { Throw = true };
        await using var factory = Factory(provider);
        using var client = await DeviceClient(factory);
        var word = Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TranslationCacheService>().SaveAsync(word, "fr", "es", "review_translation", cached);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(DevicePath, Request(word))).StatusCode);
        provider.Throw = false;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(DevicePath, Request(word))).StatusCode);
        provider.Throw = true;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(DevicePath, Request(word))).StatusCode);
        Assert.Equal(2, provider.Calls);
    }

    [Theory]
    [InlineData(false, 402)]
    [InlineData(true, 200)]
    public async Task FreeDeviceRequiresExistingPrimaryWordAndNeverReservesSecondary(bool hasWord, int expected)
    {
        await using var factory = Factory(new Provider(), testMode: false);
        var id = Guid.NewGuid().ToString("N");
        using var client = await DeviceClient(factory, id);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (hasWord)
        {
            for (var i = 0; i < 10; i++)
                db.DeviceWords.Add(new DeviceWordEntity { DeviceUuid = id, NormalizedWord = i == 0 ? "hola" : "word-" + i, NativeLanguage = "en", LearningLanguage = "es", IsActive = true });
            await db.SaveChangesAsync();
        }
        Assert.Equal(expected, (int)(await client.PostAsJsonAsync(DevicePath, Request())).StatusCode);
        Assert.Equal(hasWord ? 10 : 0, await db.DeviceWords.CountAsync(x => x.DeviceUuid == id));
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync(DevicePath, Request(native: "fr", target: "de"))).StatusCode);
        Assert.Equal(hasWord ? 10 : 0, await db.DeviceWords.CountAsync(x => x.DeviceUuid == id));
        Assert.Equal(hasWord ? 1 : 0, await db.AiUsage.Where(x => x.KeyId == "SIMULATOR-" + id).SumAsync(x => x.Count));
    }

    [Fact]
    public async Task ExistingFreeWordStillRequiresDailyQuota()
    {
        await using var factory = Factory(new Provider(), testMode: false);
        var id = Guid.NewGuid().ToString("N");
        using var client = await DeviceClient(factory, id);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.DeviceWords.Add(new DeviceWordEntity { DeviceUuid = id, NormalizedWord = "hola", NativeLanguage = "en", LearningLanguage = "es", IsActive = true });
        await db.SaveChangesAsync();
        factory.Services.GetRequiredService<IConfiguration>()["AiProtection:FreeDailyQuota"] = "0";
        // Options are captured on first resolution, after this update.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync(DevicePath, Request())).StatusCode);
    }

    [Fact]
    public async Task AccountRoutePreservesSubscriptionAndRevokedSessionGates()
    {
        await using var factory = Factory(new Provider(), testMode: false);
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var session = await accounts.SignInAsync(new("review-" + Guid.NewGuid(), null, "Reviewer"), default, allowCreation: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync(AccountPath, Request())).StatusCode);
        factory.Services.GetRequiredService<IConfiguration>()["TestMode:Enabled"] = "true";
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(AccountPath, Request())).StatusCode);
        await accounts.LogoutAsync(session.RefreshToken, default);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(AccountPath, Request())).StatusCode);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("target")]
    public async Task EnglishAliasesShareCanonicalCache(string position)
    {
        var provider = new Provider { Json = position == "target"
            ? """{"language_code":"English","translation":"Hello","explanation":"A greeting."}"""
            : Provider.Valid };
        await using var factory = Factory(provider);
        using var client = await DeviceClient(factory);
        var word = Guid.NewGuid().ToString("N");
        var first = await client.PostAsJsonAsync(DevicePath, Request(word, native: "de",
            source: position == "source" ? "en" : "es", target: position == "target" ? "en" : "fr"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var result = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(position == "target" ? "en-us" : "fr", result.GetProperty("language_code").GetString());
        provider.Throw = true;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(DevicePath, Request(word, native: "de",
            source: position == "source" ? "en-us" : "es", target: position == "target" ? "en-us" : "fr"))).StatusCode);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task SubscribedAccountStillEnforcesUsageQuota()
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
            ProductId = "com.flashcardai.owlai.premium.monthly", Environment = "Sandbox"
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
    {
        services.RemoveAll<IAiJsonService>();
        services.AddSingleton<IAiJsonService>(provider);
    }, new Dictionary<string, string?> { ["TestMode:Enabled"] = testMode.ToString(), ["AiProtection:RequireAssertion"] = "false", ["AiProtection:FreeDailyQuota"] = "40" });

    private static object Request(string word = "hola", string native = "en", string source = "es", string target = "fr") =>
        new { word, native_language = native, learning_language = source, secondary_language = target };

    private static async Task<HttpClient> DeviceClient(ApiFactory factory, string? id = null)
    {
        var client = factory.CreateClient();
        id ??= Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Devices.Add(new DeviceEntity { KeyId = "SIMULATOR-" + id, DeviceUuid = id, Environment = "production" });
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", scope.ServiceProvider.GetRequiredService<JwtTokenService>().CreateDeviceToken("SIMULATOR-" + id, "free").Token);
        return client;
    }

    private sealed class Provider : IAiJsonService
    {
        public const string Valid = """{"language_code":"French","translation":" Bonjour ","explanation":" Une salutation. "}""";
        public string Json { get; set; } = Valid;
        public bool Throw { get; set; }
        public int Calls { get; private set; }
        public string? Prompt { get; private set; }
        public string? Operation { get; private set; }
        public Task<JsonElement?> CompleteJsonAsync(string operationName, string userPrompt, CancellationToken ct = default)
        {
            Calls++;
            Prompt = userPrompt;
            Operation = operationName;
            if (Throw) throw new AiProviderException("test", "failure", 503);
            return Task.FromResult<JsonElement?>(JsonSerializer.Deserialize<JsonElement>(Json));
        }
        public Task<JsonElement?> CompleteVisionJsonAsync(string operationName, string systemUserPrompt, byte[] imageBytes, string mimeType, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
