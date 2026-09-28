using Mavrylo.Data;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Mavrylo.Tests;

public class RequestedGoogleEnrollmentTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    [Fact]
    public async Task RepairAdmitsOnlyRequestedGoogleAccountAndRejectsAmbiguousMatches()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260918165609_AddIosAccountSync");
        const string requestedEmail = "onedoston@gmail.com";
        var target = new AppUser { GoogleSub = "requested-google", Email = requestedEmail };
        var other = new AppUser { GoogleSub = "other-google", Email = "other@example.test" };
        var emailOnly = new AppUser { Email = requestedEmail, PasswordHash = "unchanged" };
        // This fixture deliberately targets an old schema, before SyncRevision.
        foreach (var user in new[] { target, other, emailOnly })
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Users" ("Id", "Email", "GoogleSub", "PasswordHash", "CreatedAt")
                VALUES ({user.Id}, {user.Email}, {user.GoogleSub}, {user.PasswordHash}, {user.CreatedAt})
                """);
        foreach (var user in new[] { target, other, emailOnly })
            db.AccountSessions.Add(new AccountSessionEntity { UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(10) });
        await db.SaveChangesAsync();
        var service = new AccountService(db, AccountServiceTests.Config(), TimeProvider.System);
        var identity = new GoogleIdentity(target.GoogleSub!, requestedEmail, "Existing user");
        Assert.Null(await db.Users.Where(x => x.Id == target.Id).Select(x => x.IosEnrolledAt).SingleAsync());

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(target.Id, (await service.SignInAsync(identity, default, requireEnrolled: true)).Profile.Id);
        var admittedAt = (await db.Users.FindAsync(target.Id))!.IosEnrolledAt;
        Assert.NotNull(admittedAt);
        Assert.Null((await db.Users.FindAsync(other.Id))!.IosEnrolledAt);
        Assert.Null((await db.Users.FindAsync(emailOnly.Id))!.IosEnrolledAt);
        Assert.Equal(3, await db.Users.CountAsync());
        await Assert.ThrowsAsync<AccountCredentialsException>(() => service.SignInAsync(new(other.GoogleSub!, other.Email, null), default, requireEnrolled: true));
        await Assert.ThrowsAsync<AccountCredentialsException>(() => service.SignInAsync(new("unknown", requestedEmail, null), default, requireEnrolled: true));

        // Reapplying must preserve the original marker, including verified enrollment.
        await migrator.MigrateAsync("20260918165609_AddIosAccountSync");
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(admittedAt, (await db.Users.FindAsync(target.Id))!.IosEnrolledAt);

        // Never choose one of multiple Google identities merely by matching email.
        await migrator.MigrateAsync("20260918165609_AddIosAccountSync");
        var duplicate = new AppUser { GoogleSub = "duplicate-google", Email = requestedEmail };
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Users" ("Id", "Email", "GoogleSub", "CreatedAt")
            VALUES ({duplicate.Id}, {duplicate.Email}, {duplicate.GoogleSub}, {duplicate.CreatedAt})
            """);
        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
        Assert.Contains("Ambiguous requested Google account", error.MessageText);
        Assert.Null(await db.Users.Where(x => x.Id == duplicate.Id).Select(x => x.IosEnrolledAt).SingleAsync());
    }
}
