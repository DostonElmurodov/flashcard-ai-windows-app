using System.Security.Claims;
using Mavrylo.Dtos;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mavrylo.Areas.OwlAI.Controllers;

[Area("OwlAI")]
[ApiController]
[Route("owlai/words")]
[Authorize]
[ServiceFilter(typeof(Mavrylo.Filters.LegacyAuthGuardFilter))]
public class WordsController(WordService words) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException();

    [HttpGet]
    public async Task<ActionResult<List<WordDto>>> List([FromQuery] string? sort, [FromQuery] int limit = 100, CancellationToken ct = default)
        => Ok(await words.ListAsync(UserId, sort, limit, ct));

    [HttpPost]
    public async Task<ActionResult<WordDto>> Create([FromBody] WordUpsert body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Word)) return BadRequest("word required");
        return CreatedAtAction(nameof(List), await words.CreateAsync(UserId, body, ct));
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<WordDto>> Patch(string id, [FromBody] WordUpsert body, CancellationToken ct)
    {
        var dto = await words.PatchAsync(UserId, id, body, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
        => await words.DeleteAsync(UserId, id, ct) ? NoContent() : NotFound();
}
