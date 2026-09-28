namespace Mavrylo.Services;

public interface IAiProviderJsonService : IAiJsonService
{
    string ProviderName { get; }
}
