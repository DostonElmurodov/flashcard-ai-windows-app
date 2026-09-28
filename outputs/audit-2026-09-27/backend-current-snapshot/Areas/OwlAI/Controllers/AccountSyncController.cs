using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace Mavrylo.Areas.OwlAi.Controllers;

[ApiController, Route("owlai/account/sync")]
[Authorize(AuthenticationSchemes = AccountAuth.Scheme)]
[RequestSizeLimit(AccountSyncService.MaxBytes), EnableRateLimiting("account-usage")]
public sealed class AccountSyncController(AccountSyncService sync) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(CancellationToken ct, [FromQuery] long? since = null) => Exchange(null, since, ct);
    [HttpPost]
    public Task<IActionResult> Post(SyncRequest request, CancellationToken ct)
        => request.Changes == null ? Task.FromResult<IActionResult>(BadRequest(new { code = "invalid_sync", error = "changes is required." })) : Exchange(request.Changes, request.Since, ct);
    private async Task<IActionResult> Exchange(SyncChange[]? changes, long? since, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await sync.ExchangeAsync(User.FindFirst("sub")!.Value, User.FindFirst("sid")!.Value, changes, ct, since)); }
        catch (SyncException ex) { return StatusCode(ex.Status, new { code = ex.Code, error = ex.Message }); }
    }
}
