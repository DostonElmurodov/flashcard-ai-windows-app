using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Mavrylo.Dtos;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mavrylo.Areas.OwlAI.Controllers;

[Area("OwlAI")]
[ApiController]
[Route("owlai/auth")]
public class AuthController(AuthService auth) : ControllerBase
{
    // Login-style endpoints: reachable without a pre-existing token (you cannot hold one before
    // logging in). Whether legacy auth is enabled is enforced inside AuthService (404 when off).
    // Explicit [AllowAnonymous] so the fail-closed FallbackPolicy does not turn these into 401.
    [AllowAnonymous]
    [HttpPost("register"), ServiceFilter(typeof(Mavrylo.Filters.AccountClaimProofFilter))]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest req, CancellationToken ct)
        => Map(await auth.RegisterAsync(req, ct));

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest req, CancellationToken ct)
        => Map(await auth.LoginAsync(req, ct));

    [AllowAnonymous]
    [HttpPost("google"), ServiceFilter(typeof(Mavrylo.Filters.AccountClaimProofFilter))]
    public async Task<ActionResult<AuthResponse>> Google([FromBody] GoogleAuthRequest req, CancellationToken ct)
        => Map(await auth.GoogleAsync(req, ct));

    [AllowAnonymous]
    [HttpPost("apple"), ServiceFilter(typeof(Mavrylo.Filters.AccountClaimProofFilter))]
    public async Task<ActionResult<AuthResponse>> Apple([FromBody] AppleAuthRequest req, CancellationToken ct)
        => Map(await auth.AppleAsync(req, ct));

    [Authorize]
    [HttpGet("me")]
    [ServiceFilter(typeof(Mavrylo.Filters.LegacyAuthGuardFilter))]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Map(await auth.MeAsync(sub, ct));
    }

    // Maps the service result to the exact original result helpers (preserving result types and
    // bodies: parameterless Unauthorized()/NotFound() vs the with-body variants, and 501).
    private ActionResult Map(AuthService.AuthResult r) => r.Status switch
    {
        200 => Ok(r.Body),
        400 => r.Body is null ? BadRequest() : BadRequest(r.Body),
        401 => r.Body is null ? Unauthorized() : Unauthorized(r.Body),
        404 => NotFound(),
        409 => Conflict(r.Body),
        501 => StatusCode(501, r.Body),
        _ => StatusCode(r.Status, r.Body)
    };
}
