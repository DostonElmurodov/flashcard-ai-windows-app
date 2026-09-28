namespace Mavrylo.Services;

public sealed record AiProviderFailure(string Provider, string Summary, int? StatusCode = null);

public interface IDevAlertEmailService
{
    Task NotifyAiProvidersFailedAsync(
        string operationName,
        string requestPath,
        IReadOnlyList<AiProviderFailure> failures,
        CancellationToken ct = default);
}
