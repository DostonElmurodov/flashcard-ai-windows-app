using Mavrylo.Data;
using Mavrylo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Filters;

/// <summary>
/// Requires a fresh App Attest assertion for device-JWT routes outside AI, such as /iap/*.
/// Runs before model binding so the server hashes the exact received body.
/// </summary>
public sealed class AppAttestAssertionFilter(
    IAppAttestVerifier verifier,
    ChallengeService challenges,
    TimeProvider timeProvider,
    ILogger<AppAttestAssertionFilter> logger) : IAsyncResourceFilter
{
    private const long MaxBufferedBodyBytes = 1_000_000;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var keyId = http.User.FindFirst("keyId")?.Value;
        if (string.IsNullOrWhiteSpace(keyId))
        {
            context.Result = Problem(StatusCodes.Status401Unauthorized, "invalid device token");
            return;
        }

        var isSimulator = keyId.StartsWith("SIMULATOR-", StringComparison.Ordinal);
        if (verifier.IsDevelopmentBypassEnabled && isSimulator)
        {
            await next();
            return;
        }

        var assertionB64 = http.Request.Headers["X-App-Attest-Assertion"].ToString();
        var challengeId = http.Request.Headers["X-App-Attest-Challenge-Id"].ToString();
        var headerKeyId = http.Request.Headers["X-App-Attest-Key-Id"].ToString();
        if (string.IsNullOrWhiteSpace(assertionB64) || string.IsNullOrWhiteSpace(challengeId))
        {
            context.Result = Problem(StatusCodes.Status403Forbidden, "missing assertion headers");
            return;
        }
        if (!string.Equals(headerKeyId, keyId, StringComparison.Ordinal))
        {
            context.Result = Problem(StatusCodes.Status403Forbidden, "assertion keyId does not match token");
            return;
        }

        var path = http.Request.Path.Value ?? "";
        byte[] body;
        try
        {
            body = await ReadAndRewindBodyAsync(http);
        }
        catch (IOException)
        {
            context.Result = Problem(StatusCodes.Status413PayloadTooLarge, "request body too large");
            return;
        }

        var nonce = await challenges.TryConsumeAsync(
            challengeId,
            ChallengeService.ChallengeKind.Assertion,
            keyId,
            path,
            http.RequestAborted);
        if (nonce is null)
        {
            context.Result = Problem(StatusCodes.Status403Forbidden, "unknown or expired challenge");
            return;
        }

        byte[] assertion;
        try { assertion = Convert.FromBase64String(assertionB64); }
        catch (FormatException)
        {
            context.Result = Problem(StatusCodes.Status403Forbidden, "assertion is not base64");
            return;
        }

        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        var device = await db.Devices.FirstOrDefaultAsync(d => d.KeyId == keyId, http.RequestAborted);
        if (device is null || device.PublicKey.Length == 0)
        {
            context.Result = Problem(StatusCodes.Status403Forbidden, "device not registered for assertion");
            return;
        }

        var clientDataHash = AppAttestClientData.ComputeAssertionHash(nonce, body, path, challengeId);
        var result = verifier.VerifyAssertion(assertion, device.PublicKey, device.SignCount, clientDataHash);
        if (!result.Ok)
        {
            logger.LogWarning("App Attest assertion rejected for {KeyId}: {Error}", keyId, result.Error);
            context.Result = Problem(StatusCodes.Status403Forbidden, result.Error ?? "assertion invalid");
            return;
        }

        device.SignCount = result.NewSignCount;
        device.LastSeenAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(http.RequestAborted);
        await next();
    }

    private static async Task<byte[]> ReadAndRewindBodyAsync(HttpContext http)
    {
        if (http.Request.ContentLength is > MaxBufferedBodyBytes)
            throw new IOException("request body too large");

        http.Request.EnableBuffering(bufferThreshold: 32 * 1024, bufferLimit: MaxBufferedBodyBytes);
        using var ms = new MemoryStream();
        await http.Request.Body.CopyToAsync(ms);
        http.Request.Body.Position = 0;
        return ms.ToArray();
    }

    private static ObjectResult Problem(int status, string detail)
        => new(new { error = detail }) { StatusCode = status };
}
