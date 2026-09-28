using Mavrylo.Data;
using Mavrylo.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Mavrylo.Tests;

public class IncrementalSyncMigrationTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    [Fact]
    public async Task ExistingLiveRecordsAndTombstonesReceiveCursorWithoutChangingRecordVersions()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        var migrator = db.GetService<IMigrator>();
        const string previous = "20260919173000_RemovePrivateNotesFromPublicSnapshots";
        await migrator.MigrateAsync(previous);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Users" ("Id", "Email", "CreatedAt") VALUES
                ('populated', 'populated@example.test', NOW()), ('empty', 'empty@example.test', NOW());
            INSERT INTO account_sync_records ("UserId", "Kind", "Id", "Version", "Deleted", "Data") VALUES
                ('populated', 'deck', 'live', 9, false, jsonb_build_object('name', 'Existing')),
                ('populated', 'word', 'deleted', 4, true, NULL);
            """);
        await migrator.MigrateAsync();
        var rows = await db.AccountSyncRecords.AsNoTracking().OrderBy(x => x.Kind).ToArrayAsync();
        Assert.All(rows, row => Assert.Equal(1, row.ChangeRevision));
        Assert.Equal(9, rows[0].Version);
        Assert.Contains("Existing", rows[0].Data);
        Assert.Equal(4, rows[1].Version);
        Assert.True(rows[1].Deleted);
        Assert.Null(rows[1].Data);
        Assert.Equal(1, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == "populated")).SyncRevision);
        Assert.Equal(0, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == "empty")).SyncRevision);
        Assert.Equal(2, await db.AccountSyncRecords.CountAsync(x => x.UserId == "populated" && x.ChangeRevision > 0));
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::integer AS "Value" FROM pg_indexes
            WHERE indexname = 'IX_account_sync_records_UserId_ChangeRevision'
            """).SingleAsync());
        await migrator.MigrateAsync(previous);
        Assert.Equal(2, await db.AccountSyncRecords.CountAsync());
        Assert.Equal(13, await db.AccountSyncRecords.SumAsync(x => x.Version));
        await migrator.MigrateAsync();
        Assert.Equal(2, await db.AccountSyncRecords.CountAsync(x => x.ChangeRevision == 1));
    }
}
