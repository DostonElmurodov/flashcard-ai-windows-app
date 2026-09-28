using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mavrylo.Data;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace Mavrylo.Tests;
public class EmailAccountTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    [Theory]
    [InlineData("bad", "password123", "password123", "invalid_email")]
    [InlineData("a\n@example.com", "password123", "password123", "invalid_email")]
    [InlineData("a@ex\n.ample.com", "password123", "password123", "invalid_email")]
    [InlineData("a..b@example.com", "password123", "password123", "invalid_email")]
    [InlineData("a@-example.com", "password123", "password123", "invalid_email")]
    [InlineData("é@example.com", "password123", "password123", "invalid_email")]
    [InlineData("a@example.com", "short", "short", "invalid_password")]
    [InlineData("a@example.com", "        ", "        ", "invalid_password")]
    [InlineData("a@example.com", "password123", "different", "password_mismatch")]
    public async Task InvalidRegistrationReturnsSafeCode(string email, string password, string confirmation, string code)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, IosProofTestClient.Configure); using var client = factory.CreateClient().WithIosProof(factory);
        var response = await client.PostAsJsonAsync("/owlai/account/register", new { email, password, confirm_password = confirmation });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
    [Fact]
    public async Task ConcurrentRegistrationNormalizesAndPreservesUnicodePassword_AndSupportsLifecycle()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, IosProofTestClient.Configure); using var client = factory.CreateClient().WithIosProof(factory);
        var email = Guid.NewGuid()+"@example.com"; var password = string.Concat(Enumerable.Repeat("😀", 64));
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/owlai/account/register", new { email = " " + email.ToUpperInvariant() + " ", password, confirm_password = password })));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var session = await responses.Single(r => r.StatusCode == HttpStatusCode.Created).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("email", session.GetProperty("profile").GetProperty("provider").GetString());
        Assert.Equal(email, session.GetProperty("profile").GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/owlai/account/email/session", new { email, password = password[..^2] + "😁" })).StatusCode);
        var login = await client.PostAsJsonAsync("/owlai/account/email/session", new { email, password }); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("access_token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/owlai/account/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/owlai/words")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/owlai/public-flashcard-sets/catalog", new {})).StatusCode);
        var refresh = await client.PostAsJsonAsync("/owlai/account/session/refresh", new { refresh_token = session.GetProperty("refresh_token").GetString() }); Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = await refresh.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("email", rotated.GetProperty("profile").GetProperty("provider").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/owlai/account/session/logout", new { refresh_token = rotated.GetProperty("refresh_token").GetString() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/owlai/account/me")).StatusCode);
        var other = await login.Content.ReadFromJsonAsync<JsonElement>(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", other.GetProperty("access_token").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/owlai/account")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/owlai/account/email/session", new { email, password })).StatusCode);
    }
    [Fact]
    public async Task ExistingPasswordAccountCanLogin_ButGooglePasswordCannot()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, IosProofTestClient.Configure); using var client = factory.CreateClient().WithIosProof(factory);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        var email = Guid.NewGuid()+"@example.com";
        db.Users.Add(new AppUser { Email = email, PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123") });
        db.Users.Add(new AppUser { Email = "google-"+email, GoogleSub = Guid.NewGuid().ToString(), PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123") });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/owlai/account/email/session", new { email, password = "password123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/owlai/account/email/session", new { email = "google-"+email, password = "password123" })).StatusCode);
    }
    [Fact]
    public async Task EmailLoginRejectsAmbiguousHistoricalAddresses()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, IosProofTestClient.Configure); using var client = factory.CreateClient().WithIosProof(factory);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        var email = Guid.NewGuid()+"@example.com";
        foreach (var stored in new[] { email, email.ToUpperInvariant() })
            db.Users.Add(new AppUser { Email = stored, PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123") });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/owlai/account/email/session", new { email, password = "password123" })).StatusCode);
    }
    [Fact]
    public async Task LegacyLoginCanVerifyModernPasswordWithoutServerError()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, IosProofTestClient.Configure); using var client = factory.CreateClient().WithIosProof(factory);
        var email = Guid.NewGuid()+"@example.com";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/owlai/account/register", new { email, password = "password123", confirm_password = "password123" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/owlai/auth/login", new { email, password = "password123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/owlai/auth/login", new { email, password = "incorrect" })).StatusCode);
    }
    [Fact]
    public async Task GoogleEmailBlocksRegistrationWithoutAddingPassword()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, IosProofTestClient.Configure); using var client = factory.CreateClient().WithIosProof(factory);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        var email = Guid.NewGuid()+"@example.com"; var user = new AppUser { Email = " " + email.ToUpperInvariant() + " ", GoogleSub = Guid.NewGuid().ToString() }; db.Users.Add(user); await db.SaveChangesAsync();
        var response = await client.PostAsJsonAsync("/owlai/account/register", new { email, password = "password123", confirm_password = "password123" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("email_exists", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/owlai/account/email/session", new { email, password = "password123" })).StatusCode);
        await db.Entry(user).ReloadAsync(); Assert.Null(user.PasswordHash);
    }
}
