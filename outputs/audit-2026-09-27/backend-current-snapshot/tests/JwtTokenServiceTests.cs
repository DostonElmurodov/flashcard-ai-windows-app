using System.IdentityModel.Tokens.Jwt;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Xunit;

namespace Mavrylo.Tests;

public class JwtTokenServiceTests
{
    [Fact]
    public void CreateToken_UsesUserAudienceAndClaims()
    {
        var service = new JwtTokenService(TestConfig.Create(), new ManualTimeProvider(DateTimeOffset.Parse("2026-06-13T12:00:00Z")));

        var token = service.CreateToken(new AppUser { Id = "user-id", Email = "test@example.com" });
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(TestConfig.Issuer, jwt.Issuer);
        Assert.Contains(TestConfig.Audience, jwt.Audiences);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == "user-id");
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "test@example.com");
    }

    [Fact]
    public void CreateDeviceToken_UsesDeviceAudienceAndClaims()
    {
        var service = new JwtTokenService(TestConfig.Create(), new ManualTimeProvider(DateTimeOffset.Parse("2026-06-13T12:00:00Z")));

        var (token, expiresAt) = service.CreateDeviceToken("key", EntitlementService.Status.Premium, "otid");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Contains(DeviceAuth.Audience, jwt.Audiences);
        Assert.Contains(jwt.Claims, c => c.Type == "keyId" && c.Value == "key");
        Assert.Contains(jwt.Claims, c => c.Type == "ent" && c.Value == EntitlementService.Status.Premium);
        Assert.Contains(jwt.Claims, c => c.Type == "otid" && c.Value == "otid");
        Assert.Equal(DateTime.Parse("2026-06-13T12:30:00Z").ToUniversalTime(), expiresAt.ToUniversalTime(), TimeSpan.FromSeconds(1));
    }
}
