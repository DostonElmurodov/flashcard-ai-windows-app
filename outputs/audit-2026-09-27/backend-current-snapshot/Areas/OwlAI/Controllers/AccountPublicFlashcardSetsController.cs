using Mavrylo.Dtos;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Mavrylo.Areas.OwlAI.Controllers;

[ApiController, Route("owlai/account/public-flashcard-sets")]
[Authorize(AuthenticationSchemes = AccountAuth.Scheme)]
[EnableRateLimiting("account-usage")]
public sealed class AccountPublicFlashcardSetsController(PublicFlashcardSetService sets) : ControllerBase
{
    [HttpPost("publish"), RequestSizeLimit(900000)]
    public async Task<IActionResult> Publish(PublicFlashcardSetUpsertRequest request, CancellationToken ct)
    {
        try { return Ok(await sets.UpsertForAccountAsync(User.FindFirst("sub")!.Value, request, ct)); }
        catch (PublicFlashcardSetValidationException ex) { return BadRequest(new { error = ex.Message }); }
    }
    [HttpPost("unpublish"), RequestSizeLimit(16000)]
    public async Task<IActionResult> Unpublish(PublicFlashcardSetUnpublishRequest request, CancellationToken ct)
    {
        try
        {
            await sets.UnpublishForAccountAsync(User.FindFirst("sub")!.Value, request.ClientSetId, ct);
            return Ok(new PublicFlashcardSetUnpublishResponse(true));
        }
        catch (PublicFlashcardSetValidationException ex) { return BadRequest(new { error = ex.Message }); }
    }
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct) => Ok(await sets.ListAccountMineAsync(User.FindFirst("sub")!.Value, ct));
    [HttpPost("catalog"), RequestSizeLimit(16000)]
    public async Task<IActionResult> Catalog(PublicFlashcardSetCatalogRequest request, CancellationToken ct)
        => Ok(await sets.CatalogAsync(request.Query, request.Limit, ct));
}
