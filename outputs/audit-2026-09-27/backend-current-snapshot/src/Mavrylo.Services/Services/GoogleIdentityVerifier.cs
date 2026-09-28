using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
namespace Mavrylo.Services;

public sealed record GoogleIdentity(string Subject, string? Email, string? DisplayName);
public interface IGoogleIdentityVerifier
{
    Task<GoogleIdentity> VerifyAsync(string token, CancellationToken ct);
}
public sealed class GoogleUnavailableException : Exception;

/// <summary>Validates locally against Google's cached public keys; ID tokens never enter a URL.</summary>
public sealed class GoogleIdentityVerifier(IHttpClientFactory clients, IConfiguration config, TimeProvider clock) : IGoogleIdentityVerifier
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private ICollection<SecurityKey> keys = [];
    private DateTimeOffset expiresAt;
    private DateTimeOffset fetchedAt;
    public async Task<GoogleIdentity> VerifyAsync(string token, CancellationToken ct)
    {
        var audiences = (config["Account:GoogleClientIds"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (audiences.Length == 0) throw new GoogleUnavailableException();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16384) throw new SecurityTokenException("Invalid identity token.");
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 16384 };
        try
        {
            await LoadKeys(false, ct);
            try { return Validate(); }
            catch (SecurityTokenSignatureKeyNotFoundException)
            {
                await LoadKeys(true, ct);
                return Validate();
            }
        }
        catch (HttpRequestException) { throw new GoogleUnavailableException(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new GoogleUnavailableException(); }
        catch (System.Text.Json.JsonException) { throw new GoogleUnavailableException(); }
        catch (ArgumentException) { throw new SecurityTokenException("Invalid identity token."); }

        GoogleIdentity Validate()
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, IssuerSigningKeys = keys,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ValidateIssuer = true, ValidIssuers = ["accounts.google.com", "https://accounts.google.com"],
                ValidateAudience = true, ValidAudiences = audiences,
                RequireExpirationTime = true, ValidateLifetime = true, RequireSignedTokens = true,
                ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (nbf, exp, _, _) => exp.HasValue && exp.Value > clock.GetUtcNow().UtcDateTime && (!nbf.HasValue || nbf.Value <= clock.GetUtcNow().UtcDateTime)
            }, out _);
            var sub = principal.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(sub) || sub.Length > 255 || principal.FindFirst("email_verified")?.Value != "true")
                throw new SecurityTokenException("Invalid identity claims.");
            return new GoogleIdentity(sub, principal.FindFirst("email")?.Value, principal.FindFirst("name")?.Value);
        }
    }
    private async Task LoadKeys(bool force, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (keys.Count > 0 && expiresAt > now && (!force || fetchedAt.AddMinutes(1) > now)) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await clients.CreateClient().GetAsync("https://www.googleapis.com/oauth2/v3/certs", timeout.Token);
            response.EnsureSuccessStatusCode();
            keys = new JsonWebKeySet(await response.Content.ReadAsStringAsync(timeout.Token)).GetSigningKeys();
            if (keys.Count == 0) throw new GoogleUnavailableException();
            fetchedAt = now;
            var age = response.Headers.CacheControl?.MaxAge ?? TimeSpan.FromHours(1);
            expiresAt = now.Add(TimeSpan.FromSeconds(Math.Clamp(age.TotalSeconds, 60, 86400)));
        }
        finally { gate.Release(); }
    }
}
