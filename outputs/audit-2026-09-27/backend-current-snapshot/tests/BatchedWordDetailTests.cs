using System.Text.Json;
using Mavrylo.Dtos;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mavrylo.Tests;

public class BatchedWordDetailTests
{
    private const string Primary = """{"corrected_word":"hello","translations":["Привет"],"translation":"Привет","pronunciation":"hello","examples":["Hello!"],"example_translations":["Привет!"]}""";
    private const string Secondary = """{"language_code":"tg","translation":"Салом","explanation":"Салом додан."}""";
    private static WordDetailRequest Request(string? target = "tg") => JsonSerializer.Deserialize<WordDetailRequest>(
        JsonSerializer.Serialize(new { word = "hello", learning_language = "en-us", native_language = "ru", secondary_language = target }),
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task FetchesOnlyMissingPartsInOneCall(bool primaryCached, bool secondaryCached)
    {
        using var db = TestDb.Create();
        var cache = new TranslationCacheService(db.Db, NullLogger<TranslationCacheService>.Instance);
        if (primaryCached) await cache.SaveAsync("hello", "ru", "en-us", "word_detail", Primary);
        if (secondaryCached) await cache.SaveAsync("hello", "tg", "en-us", "review_translation", Secondary);
        var provider = new Provider { Json = "{" + string.Join(",", new[] {
            primaryCached ? null : "\"primary\":" + Primary,
            secondaryCached ? null : "\"secondary_translation\":" + Secondary }.Where(x => x != null)) + "}" };
        var service = new WordAiService(provider, cache);
        var result = await service.WordDetailAsync(Request(), default);
        Assert.Equal(200, result.Status);
        using var document = JsonDocument.Parse(result.Json!);
        Assert.Equal("Привет", document.RootElement.GetProperty("translation").GetString());
        Assert.Equal("Салом", document.RootElement.GetProperty("secondary_translation").GetProperty("translation").GetString());
        Assert.Equal(primaryCached && secondaryCached ? 0 : 1, provider.Calls);
        if (provider.Calls > 0)
        {
            Assert.Equal(!primaryCached, provider.Prompt!.Contains("primary ("));
            Assert.Equal(!secondaryCached, provider.Prompt!.Contains("secondary_translation ("));
        }
        provider.Fail = true;
        Assert.Equal(200, (await service.WordDetailAsync(Request(), default)).Status);
        Assert.Equal(primaryCached && secondaryCached ? 0 : 1, provider.Calls);
        Assert.DoesNotContain("secondary_translation", (await cache.TryGetAsync("hello", "ru", "en-us", "word_detail"))!);
        Assert.Equal(200, (await service.ReviewTranslationAsync(new("hello", "ru", "en-us", "tg"), default)).Status);
        Assert.Equal(primaryCached && secondaryCached ? 0 : 1, provider.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unsupported")]
    [InlineData("Russian")]
    public async Task InvalidSecondaryRejectedWithoutProvider(string secondary)
    {
        using var db = TestDb.Create();
        var provider = new Provider();
        var service = new WordAiService(provider, new(db.Db, NullLogger<TranslationCacheService>.Instance));
        Assert.Equal(400, (await service.WordDetailAsync(Request(secondary), default)).Status);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ReusesTranslationsWhenLanguagesChangeRolesAndEnglishAliasChanges()
    {
        using var db = TestDb.Create();
        var cache = new TranslationCacheService(db.Db, NullLogger<TranslationCacheService>.Instance);
        await cache.SaveAsync("hello", "ru", "en-us", "review_translation", """{"language_code":"ru","translation":"Привет","explanation":"Приветствие."}""");
        await cache.SaveAsync("hello", "tg", "en", "word_detail", """{"corrected_word":"hello","translation":"Салом","translations":["Салом"],"example_translations":[" "]}""");
        var provider = new Provider { Fail = true };
        var result = await new WordAiService(provider, cache).WordDetailAsync(Request(), default);
        Assert.Equal(200, result.Status);
        using var doc = JsonDocument.Parse(result.Json!);
        Assert.Equal("Привет", doc.RootElement.GetProperty("translation").GetString());
        Assert.Equal("Салом", doc.RootElement.GetProperty("secondary_translation").GetProperty("translation").GetString());
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task SharedResponseAndCacheOnlyContainLinguisticFields()
    {
        using var db = TestDb.Create();
        var cache = new TranslationCacheService(db.Db, NullLogger<TranslationCacheService>.Instance);
        var primary = Primary[..^1] + ",\"user_notes\":\"private\",\"notes\":\"private\"}";
        var secondary = Secondary[..^1] + ",\"notes\":\"private\"}";
        var provider = new Provider { Json = "{\"primary\":"+primary+",\"secondary_translation\":"+secondary+"}" };
        var result = await new WordAiService(provider, cache).WordDetailAsync(Request(), default);
        Assert.Equal(200, result.Status);
        Assert.DoesNotContain("private", result.Json);
        Assert.DoesNotContain("notes", (await cache.TryGetAsync("hello", "ru", "en-us", "word_detail"))!);
        Assert.DoesNotContain("notes", (await cache.TryGetAsync("hello", "tg", "en-us", "review_translation"))!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptsFlatPrimaryFromProviderWithoutMixingCachedLanguages(bool secondaryCached)
    {
        using var db = TestDb.Create();
        var cache = new TranslationCacheService(db.Db, NullLogger<TranslationCacheService>.Instance);
        if (secondaryCached) await cache.SaveAsync("supply", "tg", "en-us", "review_translation", Secondary);
        // Gemini can flatten primary fields even when the prompt requests a primary object.
        var provider = new Provider { Json = """
            {"corrected_word":"supply","translations":["Снабжение","Поставка"],"translation":"Снабжение",
             "pronunciation":"/səˈplaɪ/","part_of_speech":"noun","examples":["The water supply is low."],
             "example_translations":["Запас воды невелик."],"notes":"private",
             "secondary_translation":{"language_code":"tg","translation":"Таъмин","explanation":"Таъмин кардан."}}
            """ };
        var service = new WordAiService(provider, cache);
        var result = await service.WordDetailAsync(new("supply", "en-us", "ru", "tg"), default);

        Assert.Equal(200, result.Status);
        using var doc = JsonDocument.Parse(result.Json!);
        Assert.Equal("supply", doc.RootElement.GetProperty("corrected_word").GetString());
        Assert.Equal("Снабжение", doc.RootElement.GetProperty("translation").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("translations").GetArrayLength());
        Assert.Equal(secondaryCached ? "Салом" : "Таъмин", doc.RootElement.GetProperty("secondary_translation").GetProperty("translation").GetString());
        Assert.DoesNotContain("private", result.Json);
        var savedPrimary = (await cache.TryGetAsync("supply", "ru", "en-us", "word_detail"))!;
        Assert.DoesNotContain("secondary_translation", savedPrimary);
        Assert.DoesNotContain("notes", savedPrimary);
        provider.Fail = true;
        Assert.Equal(200, (await service.WordDetailAsync(new("supply", "en-us", "ru", "tg"), default)).Status);
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("null", false)]
    [InlineData("{\"language_code\":\"ru\",\"translation\":\"Wrong\",\"explanation\":\"Wrong language\"}", false)]
    [InlineData("{}", true)]
    [InlineData("null", true)]
    [InlineData("{\"language_code\":\"ru\",\"translation\":\"Wrong\",\"explanation\":\"Wrong language\"}", true)]
    public async Task IncompleteBatchIsNotCached(string secondary, bool flatPrimary)
    {
        using var db = TestDb.Create();
        var cache = new TranslationCacheService(db.Db, NullLogger<TranslationCacheService>.Instance);
        var provider = new Provider { Json = (flatPrimary ? Primary[..^1] : "{\"primary\":" + Primary) + ",\"secondary_translation\":" + secondary + "}" };
        Assert.Equal(503, (await new WordAiService(provider, cache).WordDetailAsync(Request(), default)).Status);
        Assert.Null(await cache.TryGetAsync("hello", "ru", "en-us", "word_detail"));
        Assert.Null(await cache.TryGetAsync("hello", "tg", "en-us", "review_translation"));
    }

    [Fact]
    public async Task DoesNotReplaceAnExplicitInvalidPrimaryWithFlatFields()
    {
        using var db = TestDb.Create();
        var cache = new TranslationCacheService(db.Db, NullLogger<TranslationCacheService>.Instance);
        var provider = new Provider { Json = Primary[..^1] + ",\"primary\":null,\"secondary_translation\":" + Secondary + "}" };
        Assert.Equal(503, (await new WordAiService(provider, cache).WordDetailAsync(Request(), default)).Status);
        Assert.Null(await cache.TryGetAsync("hello", "ru", "en-us", "word_detail"));
        Assert.Null(await cache.TryGetAsync("hello", "tg", "en-us", "review_translation"));
    }

    private sealed class Provider : IAiJsonService
    {
        public string Json = Primary;
        public bool Fail;
        public int Calls;
        public string? Prompt;
        public Task<JsonElement?> CompleteJsonAsync(string operationName, string userPrompt, CancellationToken ct = default)
        {
            Calls++; Prompt = userPrompt;
            if (Fail) throw new AiProviderException("test", "unexpected provider call", 503);
            return Task.FromResult<JsonElement?>(JsonSerializer.Deserialize<JsonElement>(Json));
        }
        public Task<JsonElement?> CompleteVisionJsonAsync(string operationName, string prompt, byte[] bytes, string mime, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
