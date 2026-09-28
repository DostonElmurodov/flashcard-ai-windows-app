using Mavrylo.Dtos;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Mavrylo.Areas.OwlAI.Controllers;

/// <summary>
/// App Attest device registration and challenge issuance. These routes are intentionally
/// UNauthenticated — the app must register and obtain its first device-JWT without any login
/// (no-login v1). The device-JWT minted here (ent=free) becomes the bearer for AI/IAP calls.
/// </summary>
[ApiController]
[Area("OwlAI")]
[Route("owlai/app-attest")]
[EnableRateLimiting("app-attest")]
// Intentionally unauthenticated: the device has no token yet (no-login bootstrap). Opt out of the
// fail-closed FallbackPolicy; abuse is bounded by the "app-attest" rate limiter.
[AllowAnonymous]
public class DeviceController(AppAttestRegistrationService registration) : ControllerBase
{
    /// <summary>Issue a one-time bootstrap challenge for attestKey.</summary>
    [HttpPost("bootstrap-challenge")]
    public async Task<IActionResult> BootstrapChallenge(CancellationToken ct)
        => Map(await registration.BootstrapChallengeAsync(ct));

    /// <summary>Verify an attestation and register the device; returns the first device-JWT (ent=free).</summary>
    [HttpPost("register")]
    [RequestSizeLimit(128_000)]
    public async Task<IActionResult> Register([FromBody] AppAttestRegisterRequest request, CancellationToken ct)
        => Map(await registration.RegisterAsync(request, ct));

    /// <summary>Issue a one-time assertion challenge bound to a registered keyId + request path.</summary>
    [HttpPost("assertion-challenge")]
    [EnableRateLimiting("app-attest-assertion")]
    [RequestSizeLimit(16_000)]
    public async Task<IActionResult> AssertionChallenge([FromBody] AppAttestAssertionChallengeRequest request, CancellationToken ct)
    {
        // The keyId comes from the App Attest header (the device identifies itself); the challenge
        // is bound to it so it cannot be replayed for a different device or path.
        var keyId = Request.Headers["X-App-Attest-Key-Id"].ToString();
        return Map(await registration.AssertionChallengeAsync(keyId, request, ct));
    }

    private IActionResult Map(AppAttestRegistrationService.DeviceResult r)
    {
        return r.Status switch
        {
            200 => Ok(r.Body),
            400 => BadRequest(r.Body),
            401 => Unauthorized(r.Body),
            _ => StatusCode(r.Status, r.Body)
        };
    }
}
