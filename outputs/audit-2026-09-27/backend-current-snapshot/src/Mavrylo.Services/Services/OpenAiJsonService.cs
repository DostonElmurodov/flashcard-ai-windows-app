using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Mavrylo.Services;

public class OpenAiJsonService(IConfiguration config, IHttpClientFactory httpFactory, ILogger<OpenAiJsonService> log) : IAiProviderJsonService
{
    private readonly string? _apiKey = config["OpenAI:ApiKey"];
    private readonly string _model = config["OpenAI:Model"] ?? "gpt-6-luna";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public string ProviderName => "OpenAI";

    public async Task<JsonElement?> CompleteJsonAsync(string operationName, string userPrompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new AiProviderException("OpenAI", "OpenAI API key is not configured.");
        }
        var body = new
        {
            model = _model,
            reasoning = new { effort = "none" },
            input = userPrompt,
            text = new
            {
                format = new { type = "json_object" }
            }
        };
        return await SendResponsesRequestAsync(operationName, body, ct);
    }

    public async Task<JsonElement?> CompleteVisionJsonAsync(string operationName, string systemUserPrompt, byte[] imageBytes, string mimeType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new AiProviderException("OpenAI", "OpenAI API key is not configured.");
        var b64 = Convert.ToBase64String(imageBytes);
        var body = new
        {
            model = _model,
            reasoning = new { effort = "none" },
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = systemUserPrompt },
                        new { type = "input_image", image_url = $"data:{mimeType};base64,{b64}" }
                    }
                }
            },
            text = new
            {
                format = new { type = "json_object" }
            },
            max_output_tokens = 2048
        };
        return await SendResponsesRequestAsync(operationName, body, ct);
    }

    private async Task<JsonElement?> SendResponsesRequestAsync(string operationName, object body, CancellationToken ct)
    {
        var client = httpFactory.CreateClient();
        client.Timeout = Timeout;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await client.PostAsync("https://api.openai.com/v1/responses", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning(
                "OpenAI request failed for {Operation} with {StatusCode}",
                operationName,
                (int)resp.StatusCode);
            throw new AiProviderException("OpenAI", "OpenAI request failed.", (int)resp.StatusCode);
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!TryReadOutputText(doc.RootElement, out var text) || string.IsNullOrWhiteSpace(text))
        {
            log.LogWarning("OpenAI response did not include output text for {Operation}", operationName);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(text);
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "OpenAI returned non-JSON content for {Operation}", operationName);
            return null;
        }
    }

    private static bool TryReadOutputText(JsonElement root, out string? text)
    {
        text = null;
        if (root.TryGetProperty("output_text", out var outputText))
        {
            text = outputText.GetString();
            return true;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var contentText))
                {
                    text = contentText.GetString();
                    return true;
                }

                if (contentItem.TryGetProperty("type", out var type)
                    && string.Equals(type.GetString(), "output_text", StringComparison.OrdinalIgnoreCase)
                    && contentItem.TryGetProperty("content", out var nestedContent))
                {
                    text = nestedContent.GetString();
                    return true;
                }
            }
        }

        return false;
    }
}
