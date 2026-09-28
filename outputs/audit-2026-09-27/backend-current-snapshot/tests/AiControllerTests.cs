using System.Text.Json;
using Mavrylo.Areas.OwlAI.Controllers;
using Mavrylo.Dtos;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mavrylo.Tests;

public class AiControllerTests
{
    [Fact]
    public async Task AnalyzeWord_ReturnsValidCacheHitWithoutCallingAi()
    {
        using var testDb = TestDb.Create();
        var cache = Cache(testDb);
        await cache.SaveAsync("hello", "en", "es", TranslationCacheEntity.Kinds.AnalyzeWord, """{"translations":["Hola"]}""");
        var ai = new FakeAiJsonService();
        var controller = new AiController(new WordAiService(ai, cache));

        var result = await controller.AnalyzeWord(new AnalyzeWordRequest("hello", "en", "es"), CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("Hola", content.Content);
        Assert.Equal(0, ai.TextCalls);
    }

    [Fact]
    public async Task WordDetail_MissCallsAi_NormalizesResponse_AndCaches()
    {
        using var testDb = TestDb.Create();
        var cache = Cache(testDb);
        var ai = new FakeAiJsonService
        {
            TextResult = JsonSerializer.Deserialize<JsonElement>("""{"translation":"Hola, Buenas","corrected_word":""}""")
        };
        var controller = new AiController(new WordAiService(ai, cache));

        var result = await controller.WordDetail(new WordDetailRequest(" hello ", "es", "en"), CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("\"corrected_word\":\"hello\"", content.Content);
        Assert.Contains("\"translations\":[\"Hola\",\"Buenas\"]", content.Content);
        Assert.Equal(1, ai.TextCalls);
        Assert.NotNull(await cache.TryGetAsync("hello", "en", "es", TranslationCacheEntity.Kinds.WordDetail));
    }

    [Fact]
    public async Task AnalyzeWord_Returns503WhenAiProviderFails()
    {
        using var testDb = TestDb.Create();
        var ai = new FakeAiJsonService { ThrowText = true };
        var controller = new AiController(new WordAiService(ai, Cache(testDb)));

        var result = await controller.AnalyzeWord(new AnalyzeWordRequest("hello", "en", "es"), CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
    }

    [Fact]
    public async Task ExtractWords_RejectsEmptyImage_AndSniffsJpegMime()
    {
        using var testDb = TestDb.Create();
        var ai = new FakeAiJsonService
        {
            VisionResult = JsonSerializer.Deserialize<JsonElement>("""{"words":[{"word":"mesa","translation":"table"}]}""")
        };
        var controller = new AiController(new WordAiService(ai, Cache(testDb)));

        var bad = await controller.ExtractWords(new FormFile(Stream.Null, 0, 0, "image", "empty.jpg"), "es", "en", CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(bad);

        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00];
        await using var imageStream = new MemoryStream(jpeg);
        var image = new FormFile(imageStream, 0, jpeg.Length, "image", "photo")
        {
            Headers = new HeaderDictionary()
        };
        var ok = await controller.ExtractWords(image, "es", "en", CancellationToken.None);

        var content = Assert.IsType<ContentResult>(ok);
        Assert.Contains("mesa", content.Content);
        Assert.Equal("image/jpeg", ai.LastMimeType);
        Assert.Equal(jpeg, ai.LastImageBytes);
    }

    [Fact]
    public async Task AnalyzeWord_RejectsBlankWord_WithoutCallingAi()
    {
        using var testDb = TestDb.Create();
        var ai = new FakeAiJsonService();
        var controller = new AiController(new WordAiService(ai, Cache(testDb)));

        var result = await controller.AnalyzeWord(new AnalyzeWordRequest("   ", "en", "es"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, ai.TextCalls);
    }

    [Fact]
    public async Task WordDetail_RejectsOverlongWord_WithoutCallingAi()
    {
        using var testDb = TestDb.Create();
        var ai = new FakeAiJsonService();
        var controller = new AiController(new WordAiService(ai, Cache(testDb)));
        var tooLong = new string('a', SupportedLanguages.MaxWordLength + 1);

        var result = await controller.WordDetail(new WordDetailRequest(tooLong, "es", "en"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, ai.TextCalls);
    }

    [Fact]
    public async Task AnalyzeWord_RejectsUnsupportedLanguage_WithoutCallingAi()
    {
        using var testDb = TestDb.Create();
        var ai = new FakeAiJsonService();
        var controller = new AiController(new WordAiService(ai, Cache(testDb)));

        var result = await controller.AnalyzeWord(new AnalyzeWordRequest("hello", "klingon", "es"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, ai.TextCalls);
    }

    [Fact]
    public async Task ExtractWords_RejectsNonImagePayload_WithoutCallingVision()
    {
        using var testDb = TestDb.Create();
        var ai = new FakeAiJsonService
        {
            VisionResult = JsonSerializer.Deserialize<JsonElement>("""{"words":[]}""")
        };
        var controller = new AiController(new WordAiService(ai, Cache(testDb)));
        byte[] notAnImage = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B];
        await using var stream = new MemoryStream(notAnImage);
        var image = new FormFile(stream, 0, notAnImage.Length, "image", "payload.bin")
        {
            Headers = new HeaderDictionary()
        };

        var result = await controller.ExtractWords(image, "es", "en", CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("unsupported image payload", System.Text.Json.JsonSerializer.Serialize(bad.Value));
        Assert.Null(ai.LastImageBytes);
    }

    [Fact]
    public async Task ExtractWords_RejectsUnsupportedLanguage_WithoutCallingVision()
    {
        using var testDb = TestDb.Create();
        var ai = new FakeAiJsonService
        {
            VisionResult = JsonSerializer.Deserialize<JsonElement>("""{"words":[]}""")
        };
        var controller = new AiController(new WordAiService(ai, Cache(testDb)));
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00];
        await using var stream = new MemoryStream(jpeg);
        var image = new FormFile(stream, 0, jpeg.Length, "image", "photo.jpg")
        {
            Headers = new HeaderDictionary()
        };

        var result = await controller.ExtractWords(image, "klingon", "en", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(ai.LastImageBytes);
    }

    private static TranslationCacheService Cache(TestDb testDb) =>
        new(testDb.Db, NullLogger<TranslationCacheService>.Instance);

    private sealed class FakeAiJsonService : IAiJsonService
    {
        public JsonElement? TextResult { get; set; } =
            JsonSerializer.Deserialize<JsonElement>("""{"translations":["Hola"],"corrected_word":"hello"}""");
        public JsonElement? VisionResult { get; set; }
        public bool ThrowText { get; set; }
        public int TextCalls { get; private set; }
        public byte[]? LastImageBytes { get; private set; }
        public string? LastMimeType { get; private set; }

        public Task<JsonElement?> CompleteJsonAsync(string operationName, string userPrompt, CancellationToken ct = default)
        {
            TextCalls += 1;
            if (ThrowText)
                throw new AiProviderException("OpenAI", "failed", 503);
            return Task.FromResult(TextResult);
        }

        public Task<JsonElement?> CompleteVisionJsonAsync(
            string operationName,
            string systemUserPrompt,
            byte[] imageBytes,
            string mimeType,
            CancellationToken ct = default)
        {
            LastImageBytes = imageBytes;
            LastMimeType = mimeType;
            return Task.FromResult(VisionResult);
        }
    }
}
