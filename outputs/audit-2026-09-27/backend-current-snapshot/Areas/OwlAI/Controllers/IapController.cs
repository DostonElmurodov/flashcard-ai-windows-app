using Mavrylo.Dtos;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mavrylo.Areas.OwlAI.Controllers;

/// <summary>
/// In-app purchase verification and device-JWT (re)issuance.
///
/// Identity comes from the device-JWT (aud=device) minted at registration: its <c>keyId</c> claim
/// names the device, and we look up the device's <c>DeviceUuid</c> to find the subscription. All
/// routes require the device scheme (a valid device-JWT). Assertion enforcement is added in Phase 2.
/// </summary>
[ApiController]
[Area("OwlAI")]
[Route("owlai/iap")]
[Authorize(AuthenticationSchemes = DeviceAuth.Scheme)]
[ServiceFilter(typeof(Mavrylo.Filters.AppAttestAssertionFilter))]
public class IapController(IapService iap) : ControllerBase
{
    /// <summary>Verify a signed transaction, upsert the subscription, and return a refreshed token.</summary>
    [HttpPost("verify")]
    [RequestSizeLimit(256_000)]
    public async Task<IActionResult> Verify([FromBody] IapVerifyRequest request, CancellationToken ct)
        => Map(await iap.VerifyAsync(KeyId(), request, ct));

    /// <summary>Refresh the device-JWT with the device's current entitlement.</summary>
    [HttpPost("token")]
    public async Task<IActionResult> Token(CancellationToken ct)
        => Map(await iap.TokenAsync(KeyId(), ct));

    /// <summary>Return the device's canonical entitlement.</summary>
    [HttpGet("entitlement")]
    public async Task<IActionResult> Entitlement(CancellationToken ct)
        => Map(await iap.EntitlementAsync(KeyId(), ct));

    private string? KeyId()
    {
        var keyId = User.FindFirst("keyId")?.Value;
        return string.IsNullOrWhiteSpace(keyId) ? null : keyId;
    }

    private IActionResult Map(IapService.IapResult r)
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
