using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Mavrylo.Data;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Mavrylo.Services;

public static class AccountAuth
{
    public const string Scheme = "Account";
    public static string Audience(IConfiguration config)
    {
        var audience = config["Account:Audience"]?.Trim() ?? "owl-ai-account";
        if (string.IsNullOrWhiteSpace(audience) || audience.TrimEnd('/') == DeviceAuth.Audience.TrimEnd('/')
            || audience.TrimEnd('/') == config["Jwt:Audience"]?.TrimEnd('/'))
            throw new InvalidOperationException("Account:Audience must be nonempty and distinct from device and legacy JWT audiences.");
        return audience;
    }
}
public sealed record AccountProfile(string Id, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)] string? Email, string? DisplayName, string Provider = "google");
public sealed record AccountSession(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, AccountProfile Profile);

public sealed class AccountService(AppDbContext db, IConfiguration config, TimeProvider clock)
{
    public async Task<AccountSession> RegisterAsync(string? rawEmail, string? password, string? confirmation, CancellationToken ct)
    {
        var email = EmailCredentials.Normalize(rawEmail);
        if (!EmailCredentials.ValidEmail(email)) throw new AccountCredentialsException("invalid_email", "Enter a valid email address.");
        if (!EmailCredentials.ValidPassword(password)) throw new AccountCredentialsException("invalid_password", "Use a password with 8 to 64 characters.");
        if (password != confirmation) throw new AccountCredentialsException("password_mismatch", "Passwords do not match.");
        var user = new AppUser { Email = email, IosEnrolledAt = clock.GetUtcNow().UtcDateTime };
        user.PasswordHash = EmailCredentials.Hash(user, password!);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockEmail(email, ct);
        if (await UsersWithEmail(email).AnyAsync(ct)) throw EmailExists();
        db.Users.Add(user);
        var family = new AccountSessionEntity { UserId = user.Id, ExpiresAt = clock.GetUtcNow().UtcDateTime.AddDays(30) };
        db.AccountSessions.Add(family);
        var result = Issue(user, family);
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        { throw EmailExists(); }
        return result;
    }
    public async Task<AccountSession?> EmailSignInAsync(string? rawEmail, string? password, CancellationToken ct, bool requireEnrolled = false)
    {
        var email = EmailCredentials.Normalize(rawEmail);
        if (!EmailCredentials.ValidEmail(email) || password == null || password.Length > 1024) return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var matches = await db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE lower(btrim(\"Email\", {EmailCredentials.TrimCharacters})) = {email} AND \"GoogleSub\" IS NULL AND \"AppleSub\" IS NULL AND \"PasswordHash\" IS NOT NULL FOR UPDATE").Take(2).ToListAsync(ct);
        var user = matches.Count == 1 ? matches[0] : null;
        if (!EmailCredentials.Verify(user, password)) return null;
        if (requireEnrolled && user!.IosEnrolledAt == null) throw IosRequired();
        // Upgrade admitted legacy bcrypt hashes after proof of the existing password.
        user!.PasswordHash = EmailCredentials.Hash(user, password);
        var family = new AccountSessionEntity { UserId = user.Id, ExpiresAt = clock.GetUtcNow().UtcDateTime.AddDays(30) };
        db.AccountSessions.Add(family);
        var result = Issue(user, family);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return result;
    }
    private IQueryable<AppUser> UsersWithEmail(string email) => db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE lower(btrim(\"Email\", {EmailCredentials.TrimCharacters})) = {email}");
    private Task LockEmail(string email, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({email}, 0))", ct);
    private static AccountCredentialsException EmailExists() => new("email_exists", "An account with this email already exists. Try signing in.", 409);
    // Serialize account operations on the owning user, and refresh/logout on the family row.
    // PostgreSQL transaction locks coordinate across API processes, unlike in-memory locks.
    public async Task<AccountSession> SignInAsync(GoogleIdentity identity, CancellationToken ct, bool allowCreation = false, bool requireEnrolled = false)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!string.IsNullOrWhiteSpace(identity.Email)) await LockEmail(EmailCredentials.Normalize(identity.Email), ct);
        var id = Guid.NewGuid().ToString("N");
        if (allowCreation) await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Users" ("Id", "Email", "GoogleSub", "DisplayName", "CreatedAt")
            VALUES ({id}, {identity.Email ?? ""}, {identity.Subject}, {identity.DisplayName}, {clock.GetUtcNow().UtcDateTime})
            ON CONFLICT ("GoogleSub") DO NOTHING
            """, ct);
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"GoogleSub\" = {identity.Subject} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw IosRequired();
        if (requireEnrolled && user.IosEnrolledAt == null) throw IosRequired();
        if (allowCreation) user.IosEnrolledAt ??= clock.GetUtcNow().UtcDateTime;
        user.Email = identity.Email ?? ""; user.DisplayName = identity.DisplayName;
        var family = new AccountSessionEntity { UserId = user.Id, ExpiresAt = clock.GetUtcNow().UtcDateTime.AddDays(30) };
        db.AccountSessions.Add(family);
        var result = Issue(user, family);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return result;
    }

    public static AccountCredentialsException IosRequired() => new("ios_account_required", "Create or open your account in Owl AI on iPhone first.", 403);
    public async Task EnrollIosAsync(string owner, CancellationToken ct)
    {
        await db.Users.Where(x => x.Id == owner && x.IosEnrolledAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IosEnrolledAt, clock.GetUtcNow().UtcDateTime), ct);
    }

    public async Task<AccountSession?> RefreshAsync(string token, CancellationToken ct)
    {
        var hash = Hash(token);
        var familyId = await db.AccountRefreshTokens.AsNoTracking().Where(x => x.Hash == hash).Select(x => x.SessionId).SingleOrDefaultAsync(ct);
        if (familyId == null) return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var family = await LockFamily(familyId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (family == null || family.RevokedAt != null || family.ExpiresAt <= now) return null;
        var refresh = await db.AccountRefreshTokens.SingleOrDefaultAsync(x => x.Hash == hash, ct);
        if (refresh == null) return null;
        if (refresh.ConsumedAt != null)
        {
            family.RevokedAt = now;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return null;
        }
        var user = await db.Users.SingleAsync(x => x.Id == family.UserId, ct);
        refresh.ConsumedAt = now;
        var result = Issue(user, family);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return result;
    }

    public async Task LogoutAsync(string token, CancellationToken ct)
    {
        var hash = Hash(token);
        var id = await db.AccountRefreshTokens.AsNoTracking().Where(x => x.Hash == hash).Select(x => x.SessionId).SingleOrDefaultAsync(ct);
        if (id == null) return;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var family = await LockFamily(id, ct);
        if (family != null) { family.RevokedAt = clock.GetUtcNow().UtcDateTime; await db.SaveChangesAsync(ct); }
        await tx.CommitAsync(ct);
    }

    public Task<bool> IsActiveAsync(string userId, string sessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return db.AccountSessions.AsNoTracking().AnyAsync(x => x.Id == sessionId && x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > now, ct);
    }
    public async Task<AccountProfile?> ProfileAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && (x.GoogleSub != null || (x.PasswordHash != null && x.AppleSub == null)), ct);
        return user == null ? null : Profile(user);
    }
    public async Task DeleteAsync(string userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Same order as sign-in: user before family. Cascade deletes revoke every session.
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (user != null && (user.GoogleSub != null || (user.PasswordHash != null && user.AppleSub == null)))
        {
            await new SubscriptionOwnershipService(db, clock).TombstoneAsync(userId, ct);
            await db.Words.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.Categories.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.UserSettings.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.Users.Where(x => x.Id == userId).ExecuteDeleteAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
    private Task<AccountSessionEntity?> LockFamily(string id, CancellationToken ct) => db.AccountSessions
        .FromSqlInterpolated($"SELECT * FROM account_sessions WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
    private AccountSession Issue(AppUser user, AccountSessionEntity family)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = new[] { now.AddMinutes(15), family.ExpiresAt }.Min();
        var token = new JwtSecurityToken(config["Jwt:Issuer"], AccountAuth.Audience(config),
            [new Claim("sub", user.Id), new Claim("provider", Provider(user)), new Claim("sid", family.Id), new Claim("token_use", "account")],
            now, expires, new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!.Trim())), SecurityAlgorithms.HmacSha256));
        var refresh = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
        db.AccountRefreshTokens.Add(new AccountRefreshTokenEntity { Hash = Hash(refresh), SessionId = family.Id });
        return new AccountSession(new JwtSecurityTokenHandler().WriteToken(token), expires, refresh, Profile(user));
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static AccountProfile Profile(AppUser user) => new(user.Id, string.IsNullOrEmpty(user.Email) ? null : user.Email, user.DisplayName, Provider(user));
    private static string Provider(AppUser user) => user.GoogleSub != null ? "google" : "email";
}
