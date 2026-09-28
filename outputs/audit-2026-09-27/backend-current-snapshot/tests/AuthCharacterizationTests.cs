using System.Net;
using System.Security.Claims;
using Mavrylo.Areas.OwlAI.Controllers;
using Mavrylo.Dtos;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mavrylo.Tests;

// Characterization tests: pin the CURRENT AuthController behavior (legacy email/password + OAuth)
// before it is thinned into AuthService. Direct construction + SQLite TestDb + fakes (no Docker).
public class AuthCharacterizationTests
{
    private static AuthController NewController(
        TestDb db,
        string env = "Development",
        IDictionary<string, string?>? config = null,
        Func<HttpRequestMessage, HttpResponseMessage>? http = null,
        ClaimsPrincipal? user = null)
    {
        var cfg = TestConfig.Create(config);
        var jwt = new JwtTokenService(cfg, TimeProvider.System);
        var handler = new FakeHttpMessageHandler(http ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var auth = new AuthService(db.Db, jwt, cfg, new FakeEnvironment(env), factory, NullLogger<AuthService>.Instance);
        return new AuthController(auth)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) }
            }
        };
    }

    [Fact]
    public async Task Register_then_Login_RoundTripsBCrypt_AndIssuesToken()
    {
        using var db = TestDb.Create();

        var reg = await NewController(db).Register(new RegisterRequest("User@Example.com", "password123"), CancellationToken.None);
        var regResp = Assert.IsType<AuthResponse>(Assert.IsType<OkObjectResult>(reg.Result).Value);
        Assert.False(string.IsNullOrWhiteSpace(regResp.Token));
        Assert.Equal("user@example.com", regResp.User.Email);
        Assert.Equal("user", regResp.User.Role);

        var login = await NewController(db).Login(new LoginRequest("user@example.com", "password123"), CancellationToken.None);
        Assert.IsType<AuthResponse>(Assert.IsType<OkObjectResult>(login.Result).Value);
    }

    [Fact]
    public async Task Register_ShortPassword_BadRequest()
    {
        using var db = TestDb.Create();
        var res = await NewController(db).Register(new RegisterRequest("a@b.com", "short"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(res.Result);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Conflict()
    {
        using var db = TestDb.Create();
        await NewController(db).Register(new RegisterRequest("dup@b.com", "password123"), CancellationToken.None);
        var res = await NewController(db).Register(new RegisterRequest("dup@b.com", "password123"), CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(res.Result);
    }

    [Fact]
    public async Task Login_WrongPassword_Unauthorized()
    {
        using var db = TestDb.Create();
        await NewController(db).Register(new RegisterRequest("x@b.com", "password123"), CancellationToken.None);
        var res = await NewController(db).Login(new LoginRequest("x@b.com", "wrongpass1"), CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(res.Result);
    }

    [Fact]
    public async Task LegacyDisabled_Register_NotFound()
    {
        using var db = TestDb.Create();
        var res = await NewController(db, env: "Production").Register(new RegisterRequest("a@b.com", "password123"), CancellationToken.None);
        Assert.IsType<NotFoundResult>(res.Result);
    }

    [Fact]
    public async Task Apple_WithSignatureValidationRequired_Returns501()
    {
        using var db = TestDb.Create();
        var res = await NewController(db).Apple(new AppleAuthRequest("token", null), CancellationToken.None);
        Assert.Equal(501, Assert.IsType<ObjectResult>(res.Result).StatusCode);
    }

    [Fact]
    public async Task Google_HappyPath_CreatesUser_AndReturnsToken()
    {
        using var db = TestDb.Create();
        var c = NewController(db, http: _ =>
            FakeHttpMessageHandler.Json("""{"sub":"google-123","email":"g@b.com","aud":"client-1"}"""));
        var res = await c.Google(new GoogleAuthRequest("idtoken"), CancellationToken.None);
        var resp = Assert.IsType<AuthResponse>(Assert.IsType<OkObjectResult>(res.Result).Value);
        Assert.Equal("g@b.com", resp.User.Email);
    }

    [Fact]
    public async Task Me_WithoutClaim_Unauthorized_WithClaim_Ok()
    {
        using var db = TestDb.Create();
        var reg = await NewController(db).Register(new RegisterRequest("me@b.com", "password123"), CancellationToken.None);
        var id = Assert.IsType<AuthResponse>(((OkObjectResult)reg.Result!).Value).User.Id;

        var noClaim = await NewController(db).Me(CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(noClaim.Result);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, id) }, "test"));
        var withClaim = await NewController(db, user: principal).Me(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(withClaim.Result);
        Assert.Equal("me@b.com", Assert.IsType<UserDto>(ok.Value).Email);
    }
}
