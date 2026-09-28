using System.Text;
using System.Text.Json;

namespace Mavrylo.Services;

public class GeminiJsonService(IConfiguration config, IHttpClientFactory httpFactory, ILogger<GeminiJsonService> log) : IAiProviderJsonService
{
    private readonly string? _apiKey = config["Gemini:ApiKey"];
    private readonly string _model = config["Gemini:Model"] ?? "gemini-2.5-flash";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public string ProviderName => "Gemini";

    public Task<JsonElement?> CompleteJsonAsync(string operationName, string userPrompt, CancellationToken ct = default)
    {
        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = userPrompt }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json"
            }
        };

        return GenerateJsonAsync(operationName, body, ct);
    }

    public Task<JsonElement?> CompleteVisionJsonAsync(string operationName, string systemUserPrompt, byte[] imageBytes, string mimeType, CancellationToken ct = default)
    {
        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = systemUserPrompt },
                        new
                        {
                            inlineData = new
                            {
                                mimeType,
                                data = Convert.ToBase64String(imageBytes)
                            }
                        }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json"
            }
        };

        return GenerateJsonAsync(operationName, body, ct);
    }

    private async Task<JsonElement?> GenerateJsonAsync(string operationName, object body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new AiProviderException("Gemini", "Gemini API key is not configured.");
        }

        var client = httpFactory.CreateClient();
        client.Timeout = Timeout;
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(_model)}:generateContent");
        req.Headers.Add("x-goog-api-key", _apiKey);

        var json = JsonSerializer.Serialize(body);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning(
                "Gemini request failed for {Operation} with {StatusCode}",
                operationName,
                (int)resp.StatusCode);
            throw new AiProviderException("Gemini", "Gemini request failed.", (int)resp.StatusCode);
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!TryReadText(doc.RootElement, out var text) || string.IsNullOrWhiteSpace(text))
        {
            log.LogWarning("Gemini response did not include text content for {Operation}", operationName);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(text);
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Gemini returned non-JSON content for {Operation}", operationName);
            return null;
        }
    }

    private static bool TryReadText(JsonElement root, out string? text)
    {
        text = null;
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return false;
        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content))
            return false;
        if (!content.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
            return false;

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textEl))
            {
                text = textEl.GetString();
                return true;
            }
        }

        return false;
    }
}
