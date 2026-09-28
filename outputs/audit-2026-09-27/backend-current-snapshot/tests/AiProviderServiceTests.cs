using System.Net;
using System.Text.Json;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mavrylo.Tests;

public class AiProviderServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenAi_DefaultRequests_UseLunaWithReasoningDisabled(bool vision)
    {
        var handler = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json("""{"output":[{"type":"message","content":[{"type":"output_text","text":"{\"translation\":\"яблоко\"}"}]}]}"""));
        using var client = new HttpClient(handler);
        var service = new OpenAiJsonService(
            TestConfig.Create(new Dictionary<string, string?> { ["OpenAI:ApiKey"] = "sk-test" }),
            new FakeHttpClientFactory(client),
            NullLogger<OpenAiJsonService>.Instance);

        var result = vision
            ? await service.CompleteVisionJsonAsync("extract-words", "Return JSON", [1, 2, 3], "image/png")
            : await service.CompleteJsonAsync("translate", "Return JSON");

        Assert.Equal("яблоко", result?.GetProperty("translation").GetString());
        using var request = JsonDocument.Parse(Assert.Single(handler.CapturedRequests).Body!);
        Assert.Equal("gpt-6-luna", request.RootElement.GetProperty("model").GetString());
        Assert.Equal("none", request.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("json_object", request.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task OpenAi_BuildsResponsesRequest_AndParsesOutputTextJson()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json("""{"output_text":"{\"ok\":true,\"source\":\"openai\"}"}"""));
        using var client = new HttpClient(handler);
        var service = new OpenAiJsonService(
            TestConfig.Create(new Dictionary<string, string?> { ["OpenAI:ApiKey"] = "sk-test", ["OpenAI:Model"] = "gpt-test" }),
            new FakeHttpClientFactory(client),
            NullLogger<OpenAiJsonService>.Instance);

        var result = await service.CompleteJsonAsync("word-detail", "Return JSON");

        Assert.True(result?.GetProperty("ok").GetBoolean());
        var request = Assert.Single(handler.CapturedRequests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.openai.com/v1/responses", request.Uri?.ToString());
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization?.Scheme);
        Assert.Contains("\"model\":\"gpt-test\"", request.Body);
        Assert.Contains("\"format\":{\"type\":\"json_object\"}", request.Body);
    }

    [Fact]
    public async Task OpenAi_VisionRequest_IncludesDataUrlWithMimeType()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json("""{"output":[{"content":[{"text":"{\"words\":[\"hola\"]}"}]}]}"""));
        using var client = new HttpClient(handler);
        var service = new OpenAiJsonService(
            TestConfig.Create(new Dictionary<string, string?> { ["OpenAI:ApiKey"] = "sk-test" }),
            new FakeHttpClientFactory(client),
            NullLogger<OpenAiJsonService>.Instance);

        var result = await service.CompleteVisionJsonAsync("extract-words", "prompt", [1, 2, 3], "image/png");

        Assert.Equal("hola", result?.GetProperty("words")[0].GetString());
        Assert.Contains("data:image/png;base64,AQID", handler.CapturedRequests[0].Body);
    }

    [Fact]
    public async Task Gemini_BuildsGenerateContentRequest_AndParsesCandidateTextJson()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json("""{"candidates":[{"content":{"parts":[{"text":"{\"source\":\"gemini\"}"}]}}]}"""));
        using var client = new HttpClient(handler);
        var service = new GeminiJsonService(
            TestConfig.Create(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "gm-test", ["Gemini:Model"] = "gemini-test" }),
            new FakeHttpClientFactory(client),
            NullLogger<GeminiJsonService>.Instance);

        var result = await service.CompleteJsonAsync("analyze-word", "prompt");

        Assert.Equal("gemini", result?.GetProperty("source").GetString());
        var request = Assert.Single(handler.CapturedRequests);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-test:generateContent", request.Uri?.ToString());
        Assert.True(request.Headers.TryGetValue("x-goog-api-key", out var apiKeys));
        Assert.Equal("gm-test", Assert.Single(apiKeys));
        Assert.Contains("\"responseMimeType\":\"application/json\"", request.Body);
    }

    [Fact]
    public async Task ProviderHttpFailures_ThrowProviderExceptionWithStatus()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}", HttpStatusCode.BadGateway));
        using var client = new HttpClient(handler);
        var service = new GeminiJsonService(
            TestConfig.Create(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "gm-test" }),
            new FakeHttpClientFactory(client),
            NullLogger<GeminiJsonService>.Instance);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => service.CompleteJsonAsync("word-detail", "prompt"));

        Assert.Equal("Gemini", ex.Provider);
        Assert.Equal((int)HttpStatusCode.BadGateway, ex.StatusCode);
    }
}
