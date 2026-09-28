using Mavrylo.Data;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Mavrylo.Tests;
public class AccountServiceTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
    internal static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Jwt:Key"] = new string('x', 64), ["Jwt:Issuer"] = "tests", ["Jwt:Audience"] = "legacy", ["Account:GoogleClientIds"] = "client" }).Build();
    AccountService Service(AppDbContext db) => new(db, Config(), TimeProvider.System);
    [Fact]
    public async Task ConcurrentLoginUsesSubjectNeverEmail_AndRefreshReplayRevokesFamily()
    {
        await using var db = Db(); await db.Database.MigrateAsync();
        var subject = Guid.NewGuid().ToString(); var identity = new GoogleIdentity(subject, "same@example.com", "Name");
        db.Users.Add(new AppUser { Email = identity.Email! }); await db.SaveChangesAsync();
        async Task<AccountSession> Login() { await using var scope = Db(); return await Service(scope).SignInAsync(identity, default, allowCreation: true); }
        var sessions = await Task.WhenAll(Login(), Login());
        Assert.Equal(sessions[0].Profile.Id, sessions[1].Profile.Id);
        Assert.Equal(2, await db.Users.CountAsync(x => x.Email == identity.Email));
        Assert.DoesNotContain(sessions[0].RefreshToken, (await db.AccountRefreshTokens.ToListAsync()).Select(x => x.Hash));
        async Task<AccountSession?> Refresh() { await using var scope = Db(); return await Service(scope).RefreshAsync(sessions[0].RefreshToken, default); }
        var rotated = await Task.WhenAll(Refresh(), Refresh());
        Assert.Single(rotated, x => x != null);
        Assert.Null(await Service(db).RefreshAsync(rotated.Single(x => x != null)!.RefreshToken, default));
        Assert.NotNull(await Service(db).RefreshAsync(sessions[1].RefreshToken, default));
    }
    [Fact]
    public async Task RefreshDoesNotExtendAbsoluteFamilyLifetime()
    {
        await using var db = Db(); await db.Database.MigrateAsync();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var service = new AccountService(db, Config(), time);
        var session = await service.SignInAsync(new GoogleIdentity(Guid.NewGuid().ToString(), null, null), default, allowCreation: true);
        time.Advance(TimeSpan.FromDays(29));
        var rotated = await service.RefreshAsync(session.RefreshToken, default);
        Assert.NotNull(rotated);
        time.Advance(TimeSpan.FromDays(1));
        Assert.Null(await service.RefreshAsync(rotated.RefreshToken, default));
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken);
        Assert.False(await service.IsActiveAsync(session.Profile.Id, jwt.Claims.Single(x => x.Type == "sid").Value, default));
    }
    [Fact]
    public async Task DeleteRemovesOnlyOwnedAccountData()
    {
        await using var db = Db(); await db.Database.MigrateAsync();
        var session = await Service(db).SignInAsync(new GoogleIdentity(Guid.NewGuid().ToString(), null, null), default, allowCreation: true);
        var other = new AppUser { Email = Guid.NewGuid()+"@example.com" }; db.Users.Add(other);
        db.Words.Add(new WordEntity { UserId = session.Profile.Id });
        db.Words.Add(new WordEntity { UserId = other.Id });
        var device = new DeviceEntity { KeyId = Guid.NewGuid().ToString(), DeviceUuid = Guid.NewGuid().ToString() }; db.Devices.Add(device);
        await db.SaveChangesAsync();
        await Service(db).DeleteAsync(session.Profile.Id, default);
        Assert.False(await db.Users.AnyAsync(x => x.Id == session.Profile.Id));
        Assert.False(await db.Words.AnyAsync(x => x.UserId == session.Profile.Id));
        Assert.True(await db.Words.AnyAsync(x => x.UserId == other.Id));
        Assert.True(await db.Devices.AnyAsync(x => x.Id == device.Id));
        Assert.Null(await Service(db).RefreshAsync(session.RefreshToken, default));
    }
}
