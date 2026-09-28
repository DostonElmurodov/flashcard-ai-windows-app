using System.IdentityModel.Tokens.Jwt;
using Mavrylo.Data;
using Mavrylo.Dtos;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

/// <summary>Legacy email/password + Google/Apple auth. Logic moved verbatim from AuthController.
/// Returns (Status, Body); the controller maps to the exact original result helpers (incl.
/// parameterless Unauthorized()/NotFound() vs the with-body variants, and the 501 path).</summary>
public sealed class AuthService(
    AppDbContext db,
    JwtTokenService jwt,
    IConfiguration config,
    IWebHostEnvironment env,
    IHttpClientFactory httpFactory,
    ILogger<AuthService> log)
{
    public sealed record AuthResult(int Status, object? Body);

    private static AuthResult Success(object body) => new(200, body);
    private static AuthResult Code(int code, object? body = null) => new(code, body);

    public async Task<AuthResult> RegisterAsync(RegisterRequest req, CancellationToken ct)
    {
        if (!LegacyAuthEnabled()) return Code(404);

        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 8)
            return Code(400, "Invalid email or password");

        if (await db.Users.AnyAsync(u => u.Email == req.Email.ToLowerInvariant(), ct))
            return Code(409, "Email taken");

        var user = new AppUser
        {
            Email = req.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password)
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var token = jwt.CreateToken(user);
        return Success(new AuthResponse(token, new UserDto(user.Id, user.Email, "user")));
    }

    public async Task<AuthResult> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        if (!LegacyAuthEnabled()) return Code(404);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == req.Email.Trim().ToLowerInvariant() && u.PasswordHash != null, ct);
        if (user?.PasswordHash == null || !EmailCredentials.Verify(user, req.Password))
            return Code(401);
        var token = jwt.CreateToken(user);
        return Success(new AuthResponse(token, new UserDto(user.Id, user.Email, "user")));
    }

    public async Task<AuthResult> GoogleAsync(GoogleAuthRequest req, CancellationToken ct)
    {
        if (!LegacyAuthEnabled()) return Code(404);

        if (string.IsNullOrWhiteSpace(req.IdToken)) return Code(400);
        var client = httpFactory.CreateClient();
        var resp = await client.GetAsync($"https://oauth2.googleapis.com/tokeninfo?id_token={Uri.EscapeDataString(req.IdToken)}", ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning("Google tokeninfo failed");
            return Code(401);
        }
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        var sub = root.GetProperty("sub").GetString();
        var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
        if (string.IsNullOrEmpty(sub)) return Code(401);
        var allowed = config["Google:ClientIds"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
        if (allowed.Length > 0 && root.TryGetProperty("aud", out var aud))
        {
            var audience = aud.GetString();
            if (!allowed.Contains(audience)) return Code(401, "Invalid audience");
        }
        var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSub == sub, ct);
        if (user == null)
        {
            user = new AppUser
            {
                Email = (email ?? $"{sub}@google.placeholder").ToLowerInvariant(),
                GoogleSub = sub
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }
        return Success(new AuthResponse(jwt.CreateToken(user), new UserDto(user.Id, user.Email, "user")));
    }

    public async Task<AuthResult> AppleAsync(AppleAuthRequest req, CancellationToken ct)
    {
        if (!LegacyAuthEnabled()) return Code(404);

        if (string.IsNullOrWhiteSpace(req.IdentityToken)) return Code(400);
        var skip = config.GetValue("Apple:SkipSignatureValidation", false);
        if (!skip)
        {
            log.LogWarning("Apple JWKS validation not implemented");
            return Code(501, "Configure Apple JWKS validation or Apple:SkipSignatureValidation for development");
        }
        JwtSecurityToken jwtToken;
        try
        {
            jwtToken = new JwtSecurityTokenHandler().ReadJwtToken(req.IdentityToken);
        }
        catch
        {
            return Code(401, "Invalid token");
        }
        var sub = jwtToken.Subject;
        if (string.IsNullOrEmpty(sub)) return Code(401);
        var email = jwtToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value
            ?? $"{sub}@apple.placeholder";
        var user = await db.Users.FirstOrDefaultAsync(u => u.AppleSub == sub, ct);
        if (user == null)
        {
            user = new AppUser { Email = email.ToLowerInvariant(), AppleSub = sub };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }
        return Success(new AuthResponse(jwt.CreateToken(user), new UserDto(user.Id, user.Email, "user")));
    }

    public async Task<AuthResult> MeAsync(string? sub, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sub)) return Code(401);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == sub, ct);
        return user == null ? Code(401) : Success(new UserDto(user.Id, user.Email, "user"));
    }

    private bool LegacyAuthEnabled()
        => config.GetValue("LegacyAuth:Enabled", env.IsDevelopment());
}
