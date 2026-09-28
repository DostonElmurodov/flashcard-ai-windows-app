using Mavrylo.Data;
using Mavrylo.Dtos;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mavrylo.Areas.OwlAI.Controllers;

[ApiController]
[Area("OwlAI")]
[Route("owlai/device-words")]
[Authorize(AuthenticationSchemes = DeviceAuth.Scheme)]
[ServiceFilter(typeof(Mavrylo.Filters.AppAttestAssertionFilter))]
public class DeviceWordsController(DeviceWordService deviceWords, DeviceContextService deviceContext,
    IConfiguration? configuration = null) : ControllerBase
{
    [HttpGet("count")]
    public async Task<IActionResult> Count(CancellationToken ct)
    {
        var context = await Context(ct);
        if (context is null)
            return Unauthorized(new { error = "unknown device" });

        return Ok(new DeviceWordCountResponse(
            await deviceWords.CountActiveAsync(context.Device.DeviceUuid, ct),
            DeviceWordService.FreeLimit));
    }

    [HttpPost("upsert")]
    [RequestSizeLimit(64_000)]
    public async Task<IActionResult> Upsert([FromBody] DeviceWordUpsertRequest request, CancellationToken ct)
    {
        var context = await Context(ct);
        if (context is null)
            return Unauthorized(new { error = "unknown device" });

        DeviceWordMutationResponse result;
        if (context.Entitlement.Status == "account_required" && !TestModePolicy.IsEnabled(configuration))
            return StatusCode(402, new { code = "account_required", error = "Sign in to the subscription owner account." });
        try
        {
            result = await deviceWords.UpsertAsync(
                context.Device.DeviceUuid,
                request,
                context.Entitlement.Status,
                ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return result.Accepted ? Ok(result) : StatusCode(StatusCodes.Status402PaymentRequired, result);
    }

    [HttpPost("delete")]
    [RequestSizeLimit(16_000)]
    public async Task<IActionResult> Delete([FromBody] DeviceWordDeleteRequest request, CancellationToken ct)
    {
        var context = await Context(ct);
        if (context is null)
            return Unauthorized(new { error = "unknown device" });

        try
        {
            return Ok(await deviceWords.DeleteAsync(context.Device.DeviceUuid, request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private async Task<DeviceContextService.DeviceContext?> Context(CancellationToken ct)
    {
        var keyId = User.FindFirst("keyId")?.Value;
        return string.IsNullOrWhiteSpace(keyId) ? null : await deviceContext.ResolveAsync(keyId, ct);
    }
}
