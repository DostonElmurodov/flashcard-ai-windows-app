using Mavrylo.Data;
using Mavrylo.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mavrylo.Tests.TestSupport;

public sealed class ApiFactory(
    string connectionString,
    Action<IServiceCollection>? configureTestServices = null,
    IDictionary<string, string?>? configurationOverrides = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        // Test hosts should not write the machine-wide Windows Event Log.
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Jwt:Key", TestConfig.JwtKey);
        builder.UseSetting("Jwt:Issuer", TestConfig.Issuer);
        builder.UseSetting("Jwt:Audience", TestConfig.Audience);
        builder.UseSetting("AiProtection:Enabled", "false");

        if (configurationOverrides is not null)
        {
            foreach (var item in configurationOverrides)
                builder.UseSetting(item.Key, item.Value);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(connectionString));
            configureTestServices?.Invoke(services);
        });
    }
}
