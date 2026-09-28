using System.Text.Json;

namespace Mavrylo.Services;

public interface IAiJsonService
{
    Task<JsonElement?> CompleteJsonAsync(string operationName, string userPrompt, CancellationToken ct = default);
    Task<JsonElement?> CompleteVisionJsonAsync(string operationName, string systemUserPrompt, byte[] imageBytes, string mimeType, CancellationToken ct = default);
}
