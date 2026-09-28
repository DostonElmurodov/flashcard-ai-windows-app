using System.Text.Json;
using Mavrylo.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mavrylo.Tests;

public class FallbackAiJsonServiceTests
{
    [Fact]
    public async Task OpenAiSuccessSkipsGemini()
    {
        var openAi = FakeProvider.Success("OpenAI", """{"ok":true}""");
        var gemini = FakeProvider.Success("Gemini", """{"ok":false}""");
        var alerts = new FakeDevAlertEmailService();
        var service = Service(openAi, gemini, alerts);

        var result = await service.CompleteJsonAsync("word-detail", "prompt");

        Assert.True(result?.GetProperty("ok").GetBoolean());
        Assert.Equal(1, openAi.TextCalls);
        Assert.Equal(0, gemini.TextCalls);
        Assert.Equal(0, alerts.Calls);
    }

    [Fact]
    public async Task OpenAiFailureCallsGemini()
    {
        var openAi = FakeProvider.Failure("OpenAI");
        var gemini = FakeProvider.Success("Gemini", """{"provider":"gemini"}""");
        var alerts = new FakeDevAlertEmailService();
        var service = Service(openAi, gemini, alerts);

        var result = await service.CompleteJsonAsync("word-detail", "prompt");

        Assert.Equal("gemini", result?.GetProperty("provider").GetString());
        Assert.Equal(1, openAi.TextCalls);
        Assert.Equal(1, gemini.TextCalls);
        Assert.Equal(0, alerts.Calls);
    }

    [Fact]
    public async Task ConfiguredGeminiPrimaryCallsOpenAiSecond()
    {
        var openAi = FakeProvider.Success("OpenAI", """{"provider":"openai"}""");
        var gemini = FakeProvider.Failure("Gemini");
        var alerts = new FakeDevAlertEmailService();
        var service = Service(openAi, gemini, alerts, "Gemini");

        var result = await service.CompleteJsonAsync("word-detail", "prompt");

        Assert.Equal("openai", result?.GetProperty("provider").GetString());
        Assert.Equal(1, gemini.TextCalls);
        Assert.Equal(1, openAi.TextCalls);
        Assert.Equal(0, alerts.Calls);
    }

    [Fact]
    public async Task OpenAiNullCallsGemini()
    {
        var openAi = FakeProvider.Null("OpenAI");
        var gemini = FakeProvider.Success("Gemini", """{"provider":"gemini"}""");
        var alerts = new FakeDevAlertEmailService();
        var service = Service(openAi, gemini, alerts);

        var result = await service.CompleteJsonAsync("analyze-word", "prompt");

        Assert.Equal("gemini", result?.GetProperty("provider").GetString());
        Assert.Equal(1, openAi.TextCalls);
        Assert.Equal(1, gemini.TextCalls);
        Assert.Equal(0, alerts.Calls);
    }

    [Fact]
    public async Task BothProvidersFailTriggersAlertAndReturnsNull()
    {
        var openAi = FakeProvider.Failure("OpenAI");
        var gemini = FakeProvider.Failure("Gemini");
        var alerts = new FakeDevAlertEmailService();
        var service = Service(openAi, gemini, alerts);

        var result = await service.CompleteJsonAsync("extract-words", "prompt");

        Assert.Null(result);
        Assert.Equal(1, alerts.Calls);
        Assert.Equal("extract-words", alerts.OperationName);
        Assert.Equal(["OpenAI", "Gemini"], alerts.Failures.Select(f => f.Provider).ToArray());
    }

    [Fact]
    public void AlertCooldownPreventsDuplicateClaimsInsideFifteenMinutes()
    {
        var cooldown = new AiAlertCooldown();
        var now = DateTimeOffset.Parse("2026-05-23T12:00:00Z");

        Assert.True(cooldown.TryClaim("word-detail", now));
        Assert.False(cooldown.TryClaim("word-detail", now.AddMinutes(1)));
        Assert.True(cooldown.TryClaim("word-detail", now.AddMinutes(16)));
    }

    private static FallbackAiJsonService Service(
        IAiProviderJsonService openAi,
        IAiProviderJsonService gemini,
        IDevAlertEmailService alerts,
        string? provider = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["AI:Provider"] = provider ?? "OpenAI"
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new FallbackAiJsonService(
            [openAi, gemini],
            config,
            alerts,
            NullLogger<FallbackAiJsonService>.Instance);
    }

    private sealed class FakeProvider(string providerName, Func<Task<JsonElement?>> complete) : IAiProviderJsonService
    {
        public string ProviderName { get; } = providerName;
        public int TextCalls { get; private set; }
        public int VisionCalls { get; private set; }

        public static FakeProvider Success(string providerName, string json) =>
            new(providerName, () => Task.FromResult<JsonElement?>(JsonSerializer.Deserialize<JsonElement>(json)));

        public static FakeProvider Failure(string providerName) =>
            new(providerName, () => throw new AiProviderException(providerName, "Provider failed.", 500));

        public static FakeProvider Null(string providerName) =>
            new(providerName, () => Task.FromResult<JsonElement?>(null));

        public Task<JsonElement?> CompleteJsonAsync(string operationName, string userPrompt, CancellationToken ct = default)
        {
            TextCalls += 1;
            return complete();
        }

        public Task<JsonElement?> CompleteVisionJsonAsync(string operationName, string systemUserPrompt, byte[] imageBytes, string mimeType, CancellationToken ct = default)
        {
            VisionCalls += 1;
            return complete();
        }
    }

    private sealed class FakeDevAlertEmailService : IDevAlertEmailService
    {
        public int Calls { get; private set; }
        public string? OperationName { get; private set; }
        public IReadOnlyList<AiProviderFailure> Failures { get; private set; } = [];

        public Task NotifyAiProvidersFailedAsync(string operationName, string requestPath, IReadOnlyList<AiProviderFailure> failures, CancellationToken ct = default)
        {
            Calls += 1;
            OperationName = operationName;
            Failures = failures;
            return Task.CompletedTask;
        }
    }
}
