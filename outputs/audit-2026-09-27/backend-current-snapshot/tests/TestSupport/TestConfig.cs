using Microsoft.Extensions.Configuration;

namespace Mavrylo.Tests.TestSupport;

internal static class TestConfig
{
    public const string JwtKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const string Issuer = "https://api.test.local";
    public const string Audience = "flashcard-ai-test";

    public static IConfiguration Create(IDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = JwtKey,
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:ExpiresMinutes"] = "60",
            ["Jwt:DeviceExpiresMinutes"] = "30",
            ["AI:Provider"] = "OpenAI",
            ["Apple:ClientId"] = "com.mavrylo.owlai",
            ["Apple:TeamId"] = "TEAMID1234",
            ["Apple:SkipSignatureValidation"] = "false"
        };

        if (overrides != null)
        {
            foreach (var item in overrides)
                values[item.Key] = item.Value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
