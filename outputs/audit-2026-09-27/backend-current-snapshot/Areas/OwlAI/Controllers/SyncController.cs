using System.Security.Claims;
using Mavrylo.Dtos;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mavrylo.Areas.OwlAI.Controllers;

[Area("OwlAI")]
[ApiController]
[Route("owlai/sync")]
[Authorize]
[ServiceFilter(typeof(Mavrylo.Filters.LegacyAuthGuardFilter))]
public class SyncController(SyncService sync) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException();

    [HttpGet("changes")]
    public async Task<ActionResult<SyncPullResponse>> Changes([FromQuery] DateTime? sinceUtc, CancellationToken ct)
        => Ok(await sync.ChangesAsync(UserId, sinceUtc, ct));

    [HttpPost("push")]
    public async Task<ActionResult<SyncPushResponse>> Push([FromBody] SyncPushRequest req, CancellationToken ct)
        => Ok(await sync.PushAsync(UserId, req, ct));
}
