using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Mavrylo.Services;
using Microsoft.IdentityModel.Tokens;
using Xunit;
namespace Mavrylo.Tests;
public class GoogleIdentityVerifierTests
{
    [Theory]
    [InlineData("client", "https://accounts.google.com", true, 5, "subject", true)]
    [InlineData("wrong", "https://accounts.google.com", true, 5, "subject", false)]
    [InlineData("client", "evil", true, 5, "subject", false)]
    [InlineData("client", "https://accounts.google.com", false, 5, "subject", false)]
    [InlineData("client", "https://accounts.google.com", true, -1, "subject", false)]
    [InlineData("client", "https://accounts.google.com", true, 5, "", false)]
    public async Task ValidatesSignedClaims(string audience, string issuer, bool verified, int minutes, string subject, bool accepted)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test" };
        var publicKey = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "test" });
        var json = System.Text.Json.JsonSerializer.Serialize(new { keys = new[] { publicKey } });
        var verifier = new GoogleIdentityVerifier(new Factory(json), AccountServiceTests.Config(), TimeProvider.System);
        var jwt = new JwtSecurityToken(issuer, audience,
            [new Claim("sub", subject), new Claim("email_verified", verified ? "true" : "false", ClaimValueTypes.Boolean)],
            DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(minutes), new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        if (accepted) Assert.Equal(subject, (await verifier.VerifyAsync(token, default)).Subject);
        else await Assert.ThrowsAnyAsync<SecurityTokenException>(() => verifier.VerifyAsync(token, default));
    }
    [Fact]
    public async Task RejectsWrongSignature()
    {
        using var rsa = RSA.Create(2048); using var other = RSA.Create(2048);
        var publicKey = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "test" });
        var verifier = new GoogleIdentityVerifier(new Factory(System.Text.Json.JsonSerializer.Serialize(new { keys = new[] { publicKey } })), AccountServiceTests.Config(), TimeProvider.System);
        var jwt = new JwtSecurityToken("https://accounts.google.com", "client", [new Claim("sub", "subject"), new Claim("email_verified", "true")], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), new SigningCredentials(new RsaSecurityKey(other) { KeyId = "test" }, SecurityAlgorithms.RsaSha256));
        await Assert.ThrowsAnyAsync<SecurityTokenException>(() => verifier.VerifyAsync(new JwtSecurityTokenHandler().WriteToken(jwt), default));
    }
    private sealed class Factory(string json) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(json));
    }
    private sealed class Handler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
