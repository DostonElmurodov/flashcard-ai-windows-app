using System.ComponentModel.DataAnnotations;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
namespace Mavrylo.Areas.OwlAi.Controllers;

[ApiController]
[Route("owlai/account")]
[Authorize(AuthenticationSchemes = AccountAuth.Scheme)]
[RequestSizeLimit(20_000)]
[EnableRateLimiting("account")]
public sealed class AccountController(AccountService accounts, IGoogleIdentityVerifier google) : ControllerBase
{
    public sealed record RegisterRequest(string? Email, string? Password, string? ConfirmPassword);
    public sealed record EmailRequest(string? Email, string? Password);
    [AllowAnonymous, HttpPost("register"), ServiceFilter(typeof(Mavrylo.Filters.AccountClaimProofFilter))]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { return StatusCode(201, await accounts.RegisterAsync(request.Email, request.Password, request.ConfirmPassword, ct)); }
        catch (AccountCredentialsException ex) { return StatusCode(ex.Status, new { code = ex.Code, error = ex.Message }); }
    }
    [AllowAnonymous, HttpPost("email/session")]
    public async Task<IActionResult> Email(EmailRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await accounts.EmailSignInAsync(request.Email, request.Password, ct);
        return result == null ? Unauthorized(new { code = "invalid_credentials", error = "Email or password is incorrect." }) : Ok(result);
    }
    public sealed record GoogleRequest([Required, StringLength(16384, MinimumLength = 1)] string IdToken);
    public sealed record RefreshRequest([Required, StringLength(256, MinimumLength = 1)] string RefreshToken);
    [AllowAnonymous, HttpPost("google/session"), ServiceFilter(typeof(Mavrylo.Filters.OptionalAccountProofFilter))]
    public async Task<IActionResult> Google(GoogleRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await accounts.SignInAsync(await google.VerifyAsync(request.IdToken, ct), ct, allowCreation: HttpContext.Items.ContainsKey("ClaimDeviceKeyId"))); }
        catch (AccountCredentialsException ex) { return StatusCode(ex.Status, new { code = ex.Code, error = ex.Message }); }
        catch (GoogleUnavailableException) { return StatusCode(503, new { error = "Google sign-in is temporarily unavailable." }); }
        catch (SecurityTokenException) { return Unauthorized(); }
    }
    [HttpPost("ios/enroll"), ServiceFilter(typeof(Mavrylo.Filters.AccountClaimProofFilter))]
    public async Task<IActionResult> Enroll(CancellationToken ct)
    {
        await accounts.EnrollIosAsync(User.FindFirst("sub")!.Value, ct);
        return NoContent();
    }
    [AllowAnonymous, HttpPost("desktop/email/session")]
    public async Task<IActionResult> DesktopEmail(EmailRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var session = await accounts.EmailSignInAsync(request.Email, request.Password, ct, requireEnrolled: true);
            if (session == null) throw AccountService.IosRequired();
            return Ok(session);
        }
        catch (AccountCredentialsException ex) { return StatusCode(ex.Status, new { code = ex.Code, error = ex.Message }); }
    }
    [AllowAnonymous, HttpPost("desktop/google/session")]
    public async Task<IActionResult> DesktopGoogle(GoogleRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await accounts.SignInAsync(await google.VerifyAsync(request.IdToken, ct), ct, requireEnrolled: true)); }
        catch (AccountCredentialsException ex) { return StatusCode(ex.Status, new { code = ex.Code, error = ex.Message }); }
        catch (GoogleUnavailableException) { return StatusCode(503, new { error = "Google sign-in is temporarily unavailable." }); }
        catch (SecurityTokenException) { return Unauthorized(); }
    }
    [AllowAnonymous, HttpPost("session/refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await accounts.RefreshAsync(request.RefreshToken, ct);
        return result == null ? Unauthorized() : Ok(result);
    }
    [AllowAnonymous, HttpPost("session/logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await accounts.LogoutAsync(request.RefreshToken, ct); return NoContent();
    }
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var profile = await accounts.ProfileAsync(User.FindFirst("sub")!.Value, ct);
        return profile == null ? Unauthorized() : Ok(profile);
    }
    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        await accounts.DeleteAsync(User.FindFirst("sub")!.Value, ct); return NoContent();
    }
}
