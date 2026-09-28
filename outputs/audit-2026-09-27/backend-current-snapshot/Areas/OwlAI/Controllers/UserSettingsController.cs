using System.Security.Claims;
using Mavrylo.Dtos;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mavrylo.Areas.OwlAI.Controllers;

[Area("OwlAI")]
[ApiController]
[Route("owlai/user-settings")]
[Authorize]
[ServiceFilter(typeof(Mavrylo.Filters.LegacyAuthGuardFilter))]
public class UserSettingsController(UserSettingsService settings) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException();

    [HttpGet]
    public async Task<ActionResult<List<UserSettingsDto>>> List(CancellationToken ct)
        => Ok(await settings.ListAsync(UserId, ct));

    [HttpPost]
    public async Task<ActionResult<UserSettingsDto>> Create([FromBody] UserSettingsUpsert body, CancellationToken ct)
    {
        var dto = await settings.CreateAsync(UserId, body, ct);
        return dto is null ? Conflict("Settings already exist; use PATCH") : CreatedAtAction(nameof(List), dto);
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<UserSettingsDto>> Patch(string id, [FromBody] UserSettingsUpsert body, CancellationToken ct)
    {
        var dto = await settings.PatchAsync(UserId, id, body, ct);
        return dto is null ? NotFound() : Ok(dto);
    }
}
