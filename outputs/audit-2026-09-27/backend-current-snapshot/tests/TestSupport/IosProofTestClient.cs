using System.Net.Http.Headers;
using Mavrylo.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Mavrylo.Tests.TestSupport;

public static class IosProofTestClient
{
    public static void Configure(IServiceCollection services)
    {
        services.RemoveAll<IAppAttestVerifier>();
        services.AddSingleton<IAppAttestVerifier>(new FakeAppAttestVerifier { IsDevelopmentBypassEnabled = true });
    }
    public static HttpClient WithIosProof(this HttpClient client, ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        client.DefaultRequestHeaders.Add("X-Device-Authorization", "Bearer " + scope.ServiceProvider.GetRequiredService<JwtTokenService>().CreateDeviceToken("SIMULATOR-test", "free").Token);
        return client;
    }
}
