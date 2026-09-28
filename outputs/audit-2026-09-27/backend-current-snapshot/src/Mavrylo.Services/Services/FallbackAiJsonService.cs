using System.Text.Json;

namespace Mavrylo.Services;

public class FallbackAiJsonService(
    IEnumerable<IAiProviderJsonService> providers,
    IConfiguration config,
    IDevAlertEmailService alerts,
    ILogger<FallbackAiJsonService> log) : IAiJsonService
{
    private static readonly string[] ProviderFallbackOrder = ["OpenAI", "Gemini"];

    public Task<JsonElement?> CompleteJsonAsync(string operationName, string userPrompt, CancellationToken ct = default)
    {
        return CompleteWithFallbackAsync(operationName, ai => ai.CompleteJsonAsync(operationName, userPrompt, ct), ct);
    }

    public Task<JsonElement?> CompleteVisionJsonAsync(string operationName, string systemUserPrompt, byte[] imageBytes, string mimeType, CancellationToken ct = default)
    {
        return CompleteWithFallbackAsync(
            operationName,
            ai => ai.CompleteVisionJsonAsync(operationName, systemUserPrompt, imageBytes, mimeType, ct),
            ct);
    }

    private async Task<JsonElement?> CompleteWithFallbackAsync(
        string operationName,
        Func<IAiProviderJsonService, Task<JsonElement?>> complete,
        CancellationToken ct)
    {
        var orderedProviders = OrderedProviders().ToArray();
        var failures = new List<AiProviderFailure>();

        foreach (var provider in orderedProviders)
        {
            try
            {
                var result = await complete(provider);
                if (result != null)
                {
                    if (failures.Count > 0)
                        log.LogInformation("{Provider} fallback succeeded for {Operation}", provider.ProviderName, operationName);
                    return result;
                }

                failures.Add(new AiProviderFailure(provider.ProviderName, "Provider returned no usable JSON."));
                log.LogWarning("{Provider} returned no usable JSON for {Operation}", provider.ProviderName, operationName);
            }
            catch (AiProviderException ex)
            {
                failures.Add(new AiProviderFailure(provider.ProviderName, ex.Message, ex.StatusCode));
                log.LogWarning(
                    ex,
                    "{Provider} failed for {Operation} with status {StatusCode}",
                    provider.ProviderName,
                    operationName,
                    ex.StatusCode);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failures.Add(new AiProviderFailure(provider.ProviderName, SanitizeException(ex)));
                log.LogWarning(ex, "{Provider} failed for {Operation}", provider.ProviderName, operationName);
            }
        }

        await alerts.NotifyAiProvidersFailedAsync(operationName, $"/api/ai/{operationName}", failures, ct);
        return null;
    }

    private IEnumerable<IAiProviderJsonService> OrderedProviders()
    {
        var available = providers.ToDictionary(p => p.ProviderName, StringComparer.OrdinalIgnoreCase);
        var primaryProvider = config["AI:Provider"]?.Trim();
        if (string.IsNullOrWhiteSpace(primaryProvider))
            primaryProvider = "OpenAI";

        if (available.TryGetValue(primaryProvider, out var primary))
        {
            yield return primary;
        }
        else
        {
            log.LogWarning("Configured AI provider '{Provider}' is not registered. Falling back to OpenAI.", primaryProvider);
            primaryProvider = "OpenAI";
            if (available.TryGetValue(primaryProvider, out primary))
                yield return primary;
        }

        foreach (var providerName in ProviderFallbackOrder)
        {
            if (string.Equals(providerName, primaryProvider, StringComparison.OrdinalIgnoreCase))
                continue;
            if (available.TryGetValue(providerName, out var fallback))
                yield return fallback;
        }
    }

    private static string SanitizeException(Exception ex)
    {
        return ex switch
        {
            TaskCanceledException => "Provider request timed out.",
            HttpRequestException => "Provider transport request failed.",
            JsonException => "Provider returned malformed JSON.",
            _ => "Provider request failed."
        };
    }
}
