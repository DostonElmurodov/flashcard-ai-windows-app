using Mavrylo.Data;
using Mavrylo.Dtos;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mavrylo.Areas.OwlAI.Controllers;

[ApiController]
[Area("OwlAI")]
[Route("owlai/public-flashcard-sets")]
[Authorize(AuthenticationSchemes = DeviceAuth.Scheme)]
[ServiceFilter(typeof(Mavrylo.Filters.AppAttestAssertionFilter))]
public class PublicFlashcardSetsController(
    PublicFlashcardSetService publicSets,
    DeviceContextService deviceContext) : ControllerBase
{
    [HttpPost("publish")]
    [RequestSizeLimit(900_000)]
    public async Task<IActionResult> Publish(
        [FromBody] PublicFlashcardSetUpsertRequest request,
        CancellationToken ct)
    {
        var context = await Context(ct);
        if (context is null)
            return Unauthorized(new { error = "unknown device" });

        try
        {
            return Ok(await publicSets.UpsertAsync(context.Device.Id, request, ct));
        }
        catch (PublicFlashcardSetValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var context = await Context(ct);
        if (context is null)
            return Unauthorized(new { error = "unknown device" });

        return Ok(await publicSets.ListMineAsync(context.Device.Id, ct));
    }

    [HttpPost("unpublish")]
    public async Task<IActionResult> Unpublish(
        [FromBody] PublicFlashcardSetUnpublishRequest request,
        CancellationToken ct)
    {
        var context = await Context(ct);
        if (context is null)
            return Unauthorized(new { error = "unknown device" });

        try
        {
            await publicSets.UnpublishAsync(context.Device.Id, request.ClientSetId, ct);
            return Ok(new PublicFlashcardSetUnpublishResponse(true));
        }
        catch (PublicFlashcardSetValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("catalog")]
    public async Task<IActionResult> Catalog(
        [FromBody] PublicFlashcardSetCatalogRequest request,
        CancellationToken ct)
    {
        var context = await Context(ct);
        if (context is null)
            return Unauthorized(new { error = "unknown device" });

        return Ok(await publicSets.CatalogAsync(request.Query, request.Limit, ct));
    }

    private async Task<DeviceContextService.DeviceContext?> Context(CancellationToken ct)
    {
        var keyId = User.FindFirst("keyId")?.Value;
        return string.IsNullOrWhiteSpace(keyId) ? null : await deviceContext.ResolveAsync(keyId, ct);
    }
}
