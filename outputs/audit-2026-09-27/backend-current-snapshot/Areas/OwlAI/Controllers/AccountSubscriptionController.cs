using System.ComponentModel.DataAnnotations;
using Mavrylo.Filters;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Mavrylo.Areas.OwlAI.Controllers;

[ApiController, Route("owlai/account")]
[Authorize(AuthenticationSchemes = AccountAuth.Scheme)]
[EnableRateLimiting("account-usage")]
public sealed class AccountSubscriptionController(AccountEntitlementService subscriptions) : ControllerBase
{
    public sealed record AppleClaimRequest([Required, StringLength(100)] string AccountId, [Required, StringLength(30000)] string JwsTransaction);

    [HttpGet("entitlement")]
    public async Task<IActionResult> Entitlement(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await subscriptions.GetAsync(User.FindFirst("sub")!.Value, ct));
    }

    [HttpPost("subscription/apple/claim"), RequestSizeLimit(40000)]
    [ServiceFilter(typeof(AccountClaimProofFilter))]
    public async Task<IActionResult> Claim(AppleClaimRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var account = User.FindFirst("sub")!.Value;
        if (!string.Equals(account, request.AccountId, StringComparison.Ordinal))
            return StatusCode(403, new { code = "account_mismatch", error = "The claim account must match the authenticated account." });
        var result = await subscriptions.ClaimAsync(account, (string)HttpContext.Items["ClaimDeviceKeyId"]!, request.JwsTransaction, ct);
        return StatusCode(result.Status, result.Body);
    }
}
