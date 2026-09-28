using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Mavrylo.Models;
using Microsoft.IdentityModel.Tokens;

namespace Mavrylo.Services;

public class JwtTokenService(IConfiguration config, TimeProvider timeProvider)
{
    public string CreateToken(AppUser user)
    {
        var jwtKey = config["Jwt:Key"]?.Trim();
        if (string.IsNullOrEmpty(jwtKey))
            throw new InvalidOperationException("Jwt:Key is missing or empty.");
        var keyBytes = Encoding.UTF8.GetBytes(jwtKey);
        if (keyBytes.Length < 32)
            throw new InvalidOperationException("Jwt:Key is too short; use at least 32 UTF-8 bytes.");
        var key = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, "user")
        };
        var minutes = int.TryParse(config["Jwt:ExpiresMinutes"], out var m) ? m : 10080;
        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: timeProvider.GetUtcNow().UtcDateTime.AddMinutes(minutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Mints a short-lived device-JWT (aud = "device") for an attested device. This is the ONLY
    /// device access token: <c>register</c> issues the first one (ent=free) and /iap/verify and
    /// /iap/token refresh it. Signed HS256 with the same Jwt:Key as user tokens but a distinct
    /// audience so the two can never be substituted. See Appendix M.
    /// </summary>
    /// <param name="keyId">App Attest key id (becomes the subject + keyId claim).</param>
    /// <param name="entitlement">Entitlement status: free/trial/premium/grace/expired_*/revoked.</param>
    /// <param name="originalTransactionId">Apple original transaction id, or null when none yet.</param>
    public (string Token, DateTime ExpiresAt) CreateDeviceToken(
        string keyId, string entitlement, string? originalTransactionId = null)
    {
        var jwtKey = config["Jwt:Key"]?.Trim();
        if (string.IsNullOrEmpty(jwtKey))
            throw new InvalidOperationException("Jwt:Key is missing or empty.");
        var keyBytes = Encoding.UTF8.GetBytes(jwtKey);
        if (keyBytes.Length < 32)
            throw new InvalidOperationException("Jwt:Key is too short; use at least 32 UTF-8 bytes.");
        var key = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, keyId),
            new("keyId", keyId),
            new("ent", entitlement),
        };
        if (!string.IsNullOrEmpty(originalTransactionId))
            claims.Add(new Claim("otid", originalTransactionId));

        var minutes = int.TryParse(config["Jwt:DeviceExpiresMinutes"], out var dm)
            ? dm
            : DeviceAuth.DefaultLifetimeMinutes;
        var expiresAt = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(minutes);
        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: DeviceAuth.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
