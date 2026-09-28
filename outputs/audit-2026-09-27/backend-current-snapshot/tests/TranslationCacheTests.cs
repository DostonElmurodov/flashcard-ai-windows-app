using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mavrylo.Tests;

public class TranslationCacheTests
{
    [Theory]
    [InlineData("  HELLO  ", "hello")]
    [InlineData("Cafe\u0301", "café")]
    public void NormalizeWord_TrimsLowercasesAndNormalizesUnicode(string input, string expected)
    {
        Assert.Equal(expected, TranslationCacheKey.NormalizeWord(input));
    }

    [Fact]
    public async Task SaveAndTryGet_InsertUpdateAndReturnCachedJson()
    {
        using var testDb = TestDb.Create();
        var service = new TranslationCacheService(testDb.Db, NullLogger<TranslationCacheService>.Instance);

        await service.SaveAsync("hello", "en", "es", TranslationCacheEntity.Kinds.WordDetail, """{"translations":["Hola"]}""");
        var first = await service.TryGetAsync("hello", "en", "es", TranslationCacheEntity.Kinds.WordDetail);

        await service.SaveAsync("hello", "en", "es", TranslationCacheEntity.Kinds.WordDetail, """{"translations":["Buenos dias"]}""");
        var second = await service.TryGetAsync("hello", "en", "es", TranslationCacheEntity.Kinds.WordDetail);

        Assert.Contains("Hola", first);
        Assert.Contains("Buenos dias", second);
        Assert.Single(testDb.Db.TranslationCache);
    }
}
