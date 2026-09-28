using System.Security.Claims;
using Mavrylo.Dtos;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mavrylo.Areas.OwlAI.Controllers;

[Area("OwlAI")]
[ApiController]
[Route("owlai/categories")]
[Authorize]
[ServiceFilter(typeof(Mavrylo.Filters.LegacyAuthGuardFilter))]
public class CategoriesController(CategoryService categories) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException();

    [HttpGet]
    public async Task<ActionResult<List<CategoryDto>>> List([FromQuery] string? sort, [FromQuery] int limit = 50, CancellationToken ct = default)
        => Ok(await categories.ListAsync(UserId, sort, limit, ct));

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create([FromBody] CategoryUpsert body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Name)) return BadRequest("name required");
        return CreatedAtAction(nameof(List), await categories.CreateAsync(UserId, body, ct));
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<CategoryDto>> Patch(string id, [FromBody] CategoryUpsert body, CancellationToken ct)
    {
        var dto = await categories.PatchAsync(UserId, id, body, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
        => await categories.DeleteAsync(UserId, id, ct) ? NoContent() : NotFound();
}
