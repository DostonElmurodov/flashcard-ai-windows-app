using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
namespace Mavrylo.Tests;
public class AccountRouteTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    [Fact]
    public async Task AuthenticationRequiresExactAudienceIncludingTrailingSlash()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, services =>
        { IosProofTestClient.Configure(services); services.RemoveAll<IGoogleIdentityVerifier>(); services.AddSingleton<IGoogleIdentityVerifier>(new GoogleFixture()); });
        using var client = factory.CreateClient().WithIosProof(factory);
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        var login = await client.PostAsJsonAsync("/owlai/account/google/session", new { id_token = "fixture" });
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        var cases = new[]
        {
            (tokens.CreateToken(new AppUser()), "/owlai/words", false),
            (tokens.CreateDeviceToken("device", EntitlementService.Status.Free).Token, "/owlai/public-flashcard-sets/catalog", true),
            (session.GetProperty("access_token").GetString()!, "/owlai/account/me", false)
        };
        foreach (var (token, path, post) in cases)
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            jwt.Payload["aud"] = jwt.Audiences.Single() + "/";
            var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(TestConfig.JwtKey));
            var signing = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
            var altered = handler.WriteToken(new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(new System.IdentityModel.Tokens.Jwt.JwtHeader(signing), jwt.Payload));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", altered);
            using var response = post ? await client.PostAsJsonAsync(path, new {}) : await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
    [Fact]
    public async Task SessionsUseSnakeCase_RejectOtherTokens_AndLogoutRevokesAccess()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, services =>
        { IosProofTestClient.Configure(services); services.RemoveAll<IGoogleIdentityVerifier>(); services.AddSingleton<IGoogleIdentityVerifier>(new GoogleFixture()); });
        using var client = factory.CreateClient().WithIosProof(factory);
        using var scope = factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        foreach (var token in new[] { jwt.CreateToken(new AppUser()), jwt.CreateDeviceToken("device", EntitlementService.Status.Free).Token })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/owlai/account/me")).StatusCode);
        }
        var response = await client.PostAsJsonAsync("/owlai/account/google/session", new { id_token = "fixture" });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var access = json.GetProperty("access_token").GetString(); var refresh = json.GetProperty("refresh_token").GetString();
        Assert.Equal("google", json.GetProperty("profile").GetProperty("provider").GetString());
        Assert.True(json.GetProperty("access_token_expires_at").GetDateTime() < DateTime.UtcNow.AddMinutes(16));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/owlai/account/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/owlai/words")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/owlai/public-flashcard-sets/catalog", new {})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/owlai/account/session/logout", new { refresh_token = refresh })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/owlai/account/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/owlai/account/session/refresh", new { refresh_token = refresh })).StatusCode);
    }
    [Theory]
    [InlineData("/owlai/account/google/session")]
    [InlineData("/owlai/account/desktop/google/session")]
    public async Task MissingGoogleConfigurationLeavesHealthAvailable(string path)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, configurationOverrides: new Dictionary<string,string?> { ["Account:GoogleClientIds"] = "" });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(path, new { id_token = "fixture" })).StatusCode);
    }
    private sealed class GoogleFixture : IGoogleIdentityVerifier
    {
        public Task<GoogleIdentity> VerifyAsync(string token, CancellationToken ct) => Task.FromResult(new GoogleIdentity("route-"+Guid.NewGuid(), null, "Test User"));
    }
}
