using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Mavrylo.Data;
using Mavrylo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Mavrylo.Filters;

/// <summary>
/// Protects AI endpoints. Implemented as an <see cref="IAsyncResourceFilter"/> so it runs BEFORE
/// model binding — it must read the raw request body to recompute the App Attest assertion
/// clientDataHash (the body is consumed by binding otherwise), and extract-words is a multipart
/// upload up to 20 MB.
///
/// Pipeline: device-JWT (401) → assertion (403) → entitlement (402) → free quota
/// (429) → action. Successful free calls increment the daily quota afterward. Rate limiting runs
/// earlier as middleware (UseRateLimiter) so a throttled request never buffers the body.
///
/// The legacy <see cref="AiProtectionOptions.Enabled"/> option cannot disable enforcement.
/// Only server-owned test mode bypasses commercial gates, after request authentication.
/// </summary>
public sealed class AiProtectionFilter(
    IOptions<AiProtectionOptions> options,
    IConfiguration config,
    IAppAttestVerifier verifier,
    ChallengeService challenges,
    DeviceContextService deviceContext,
    DeviceWordService deviceWords,
    AiUsageService usage,
    TimeProvider timeProvider,
    ILogger<AiProtectionFilter> logger) : IAsyncResourceFilter
{
    private const long MaxBufferedBodyBytes = 20_000_000;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var opts = options.Value;
        var http = context.HttpContext;
        var path = http.Request.Path.Value ?? "";

        // 1) Device-JWT (aud=device). Validate and extract keyId. The ent claim is UI-only.
        var (keyId, tokenOk) = ValidateDeviceToken(http);
        if (!tokenOk || keyId is null)
        {
            context.Result = Problem(http, StatusCodes.Status401Unauthorized, "missing or invalid device token");
            return;
        }

        var resolved = await deviceContext.ResolveAsync(keyId, http.RequestAborted);
        if (resolved is null)
        {
            context.Result = Problem(http, StatusCodes.Status401Unauthorized, "unknown device");
            return;
        }
        var ent = resolved.Entitlement.Status;

        // Buffer the raw body once (needed for assertion hash and to let binding re-read it).
        byte[] body;
        try
        {
            body = await ReadAndRewindBodyAsync(http);
        }
        catch (IOException)
        {
            context.Result = Problem(http, StatusCodes.Status413PayloadTooLarge, "request body too large");
            return;
        }

        // 2) Assertion (App Attest). Skipped in Development for SIMULATOR-* keys, or if RequireAssertion is off.
        var isSimulator = keyId.StartsWith("SIMULATOR-", StringComparison.Ordinal);
        var skipAssertion = !opts.RequireAssertion || (verifier.IsDevelopmentBypassEnabled && isSimulator);
        if (!skipAssertion)
        {
            var assertionError = await VerifyAssertionAsync(http, keyId, path, body);
            if (assertionError is not null)
            {
                context.Result = Problem(http, StatusCodes.Status403Forbidden, assertionError);
                return;
            }
        }

        // Test access starts only after the device and its request proof are verified.
        if (TestModePolicy.IsEnabled(config))
        {
            await next();
            return;
        }

        // 3) Entitlement gate. Active states may call AI; expired/revoked → 402 (paywall).
        if (ent == "account_required")
        {
            context.Result = Problem(http, StatusCodes.Status402PaymentRequired, "Sign in to the subscription owner account.");
            return;
        }
        if (!IsEntitlementActiveForAi(ent))
        {
            context.Result = Problem(http, StatusCodes.Status402PaymentRequired, "no active entitlement");
            return;
        }

        if (resolved.AccountId != null)
        {
            if (!AccountEntitlementService.IsActive(ent))
            {
                context.Result = Problem(http, StatusCodes.Status402PaymentRequired, "no active shared entitlement");
                return;
            }
            if (!await http.RequestServices.GetRequiredService<AccountAiUsageService>().TryConsumeAsync(resolved.AccountId, http.RequestAborted))
            {
                http.Response.Headers.RetryAfter = "60";
                context.Result = Problem(http, StatusCodes.Status429TooManyRequests, "Account AI limit reached.");
                return;
            }
            await next();
            return;
        }
        var isFree = string.Equals(ent, EntitlementService.Status.Free, StringComparison.Ordinal);
        if (isFree)
        {
            var wordContext = DeviceWordService.TryReadWordContext(body);
            DeviceWordService.AiReservationResult reservation;
            try
            {
                // Secondary review enriches an existing card and must never reserve another word.
                reservation = path.TrimEnd('/').Equals("/owlai/ai/review-translation", StringComparison.OrdinalIgnoreCase)
                    ? await deviceWords.CheckExistingAiWordAsync(keyId, wordContext.Word,
                        wordContext.NativeLanguage, wordContext.LearningLanguage, http.RequestAborted)
                    : await deviceWords.TryReserveAiSlotAsync(keyId, ent, wordContext.Word,
                        wordContext.NativeLanguage, wordContext.LearningLanguage, http.RequestAborted);
            }
            catch (InvalidOperationException ex)
            {
                context.Result = Problem(http, StatusCodes.Status400BadRequest, ex.Message);
                return;
            }
            if (!reservation.Allowed)
            {
                context.Result = Problem(http, StatusCodes.Status402PaymentRequired, reservation.Error ?? "free plan word limit reached");
                return;
            }
        }

        // 4) Daily cost guard applies to free access.
        if (isFree && !await usage.TryConsumeAsync(keyId, opts.FreeDailyQuota, http.RequestAborted))
        {
            context.Result = Problem(http, StatusCodes.Status429TooManyRequests, "daily AI limit reached");
            return;
        }

        await next();
    }

    private (string? keyId, bool ok) ValidateDeviceToken(HttpContext http)
    {
        var auth = http.Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return (null, false);
        var token = auth["Bearer ".Length..].Trim();

        var jwtKey = config["Jwt:Key"]?.Trim();
        if (string.IsNullOrEmpty(jwtKey))
            return (null, false);

        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ValidIssuer = config["Jwt:Issuer"],
                ValidateIssuer = true,
                ValidAudience = DeviceAuth.Audience,
                ValidateAudience = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            }, out _);

            var keyId = principal.FindFirst("keyId")?.Value;
            if (string.IsNullOrEmpty(keyId))
                return (null, false);
            return (keyId, true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Device token validation failed.");
            return (null, false);
        }
    }

    private async Task<string?> VerifyAssertionAsync(HttpContext http, string keyId, string path, byte[] body)
    {
        var assertionB64 = http.Request.Headers["X-App-Attest-Assertion"].ToString();
        var challengeId = http.Request.Headers["X-App-Attest-Challenge-Id"].ToString();
        var headerKeyId = http.Request.Headers["X-App-Attest-Key-Id"].ToString();
        if (string.IsNullOrWhiteSpace(assertionB64) || string.IsNullOrWhiteSpace(challengeId))
            return "missing assertion headers";
        if (!string.Equals(headerKeyId, keyId, StringComparison.Ordinal))
            return "assertion keyId does not match token";

        // Consume the single-use challenge bound to this keyId + path.
        var nonce = await challenges.TryConsumeAsync(
            challengeId,
            ChallengeService.ChallengeKind.Assertion,
            keyId,
            path,
            http.RequestAborted);
        if (nonce is null)
            return "unknown or expired challenge";

        byte[] assertion;
        try { assertion = Convert.FromBase64String(assertionB64); }
        catch (FormatException) { return "assertion is not base64"; }

        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        var device = await db.Devices.FirstOrDefaultAsync(d => d.KeyId == keyId, http.RequestAborted);
        if (device is null || device.PublicKey.Length == 0)
            return "device not registered for assertion";

        var clientDataHash = AppAttestClientData.ComputeAssertionHash(nonce, body, path, challengeId);
        var result = verifier.VerifyAssertion(assertion, device.PublicKey, device.SignCount, clientDataHash);
        if (!result.Ok)
            return result.Error ?? "assertion invalid";

        // Persist the new (monotonic) counter.
        device.SignCount = result.NewSignCount;
        device.LastSeenAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(http.RequestAborted);
        return null;
    }

    private static bool IsEntitlementActiveForAi(string? ent) => ent switch
    {
        EntitlementService.Status.Free => true,    // free gets AI for the first 10 words (on-device gate)
        EntitlementService.Status.Trial => true,
        EntitlementService.Status.Premium => true,
        EntitlementService.Status.Grace => true,
        _ => false, // expired_trial / expired_paid / revoked → 402
    };

    private static async Task<byte[]> ReadAndRewindBodyAsync(HttpContext http)
    {
        if (http.Request.ContentLength is > MaxBufferedBodyBytes)
            throw new IOException("request body too large");

        http.Request.EnableBuffering(bufferThreshold: 64 * 1024, bufferLimit: MaxBufferedBodyBytes);
        using var ms = new MemoryStream();
        await http.Request.Body.CopyToAsync(ms);
        http.Request.Body.Position = 0;
        return ms.ToArray();
    }

    private static ObjectResult Problem(HttpContext http, int status, string detail)
    {
        http.Response.StatusCode = status;
        return new ObjectResult(new { error = detail }) { StatusCode = status };
    }
}
