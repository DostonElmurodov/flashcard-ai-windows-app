using Mavrylo.Data;
using Mavrylo.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
namespace Mavrylo.Tests;
public class AccountMigrationTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    [Fact]
    public async Task DuplicateLegacySubjectsAbortWithoutChangingUsers()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260803172328_AddPublicFlashcardSets");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Users" ("Id", "Email", "GoogleSub", "CreatedAt") VALUES
            ('legacy-one', 'one@example.com', 'duplicate', NOW()), ('legacy-two', 'two@example.com', 'duplicate', NOW())
            """);
        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
        Assert.Contains("Duplicate legacy Google subjects", error.MessageText);
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::integer AS \"Value\" FROM \"Users\"").SingleAsync());
        Assert.DoesNotContain("20260906225646_AddGoogleAccounts", await db.Database.GetAppliedMigrationsAsync());
    }
}
