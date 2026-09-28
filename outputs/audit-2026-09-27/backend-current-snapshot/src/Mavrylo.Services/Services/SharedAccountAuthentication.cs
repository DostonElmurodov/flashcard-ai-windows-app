using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Mavrylo.Services;

/// <summary>Validates secondary credentials without replacing the primary request principal.</summary>
public sealed class SharedAccountAuthentication(IConfiguration config, AccountService accounts)
{
    public ClaimsPrincipal? ValidateBearer(string bearer, string audience)
    {
        if (!bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            return new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(bearer[7..].Trim(), new TokenValidationParameters
            {
                IgnoreTrailingSlashWhenValidatingAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!.Trim())),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateIssuer = true, ValidIssuer = config["Jwt:Issuer"],
                ValidateAudience = true, ValidAudience = audience,
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero
            }, out _);
        }
        catch (SecurityTokenException) { return null; }
        catch (ArgumentException) { return null; }
    }

    public async Task<string?> AccountAsync(string bearer, CancellationToken ct)
    {
        var principal = ValidateBearer(bearer, AccountAuth.Audience(config));
        var sub = principal?.FindFirst("sub")?.Value;
        var sid = principal?.FindFirst("sid")?.Value;
        return sub != null && sid != null && principal!.FindFirst("token_use")?.Value == "account"
            && principal.FindFirst("provider")?.Value is ("google" or "email")
            && await accounts.IsActiveAsync(sub, sid, ct) ? sub : null;
    }
}
