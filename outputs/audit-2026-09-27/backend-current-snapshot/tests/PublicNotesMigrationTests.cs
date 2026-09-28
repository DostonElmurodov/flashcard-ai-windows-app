using System.Text.Json.Nodes;
using Mavrylo.Data;
using Mavrylo.Models;
using Mavrylo.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Mavrylo.Tests;

public class PublicNotesMigrationTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    [Fact]
    public async Task MigrationPurgesOnlyPublicNotesPreservesOrderAndPrivateDataAndCannotRestoreNotes()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        var migrator = db.GetService<IMigrator>();
        const string previous = "20260918165609_AddIosAccountSync";
        await migrator.MigrateAsync(previous);
        // Seed the historical schema explicitly; today's EF model includes later columns.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Users" ("Id", "Email", "CreatedAt") VALUES ('notes-owner', 'owner@example.test', NOW());
            INSERT INTO account_sync_records ("UserId", "Kind", "Id", "Version", "Deleted", "Data")
            VALUES ('notes-owner', 'word', 'private-sync', 7, false, jsonb_build_object('notes', 'private sync note', 'word', 'hello'));
            """);
        db.Words.Add(new WordEntity { Id = "private-word", UserId = "notes-owner", Word = "hello", UserNotes = "private word note" });
        const string legacy = """[{"ClientCardId":"second","Notes":"secret","Word":"hello","Translations":["hola"],"Extra":{"kept":true}},{"ClientCardId":"first","notes":"secret 2","user_notes":"secret 3","UserNotes":"secret 4","userNotes":"secret 5","Examples":["keep me"]},null,3,"notes",["keep"]]""";
        const string cleaned = """[{"ClientCardId":"second","Word":"hello","Translations":["hola"],"Extra":{"kept":true}},{"ClientCardId":"first","Examples":["keep me"]},null,3,"notes",["keep"]]""";
        foreach (var (id, json, status) in new[] {
            ("approved", legacy, PublicFlashcardSetStatus.Approved),
            ("pending", legacy, PublicFlashcardSetStatus.Pending),
            ("empty", "[]", PublicFlashcardSetStatus.Pending),
            ("malformed", "not json", PublicFlashcardSetStatus.Pending),
            ("non-array", "{\"unexpected\":true}", PublicFlashcardSetStatus.Pending) })
            db.PublicFlashcardSets.Add(new PublicFlashcardSetEntity { Id = id, OwnerAccountId = "notes-owner", ClientSetId = id, Title = id, SnapshotJson = json, Status = status });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await migrator.MigrateAsync();
        async Task AssertPurged()
        {
            var rows = await db.PublicFlashcardSets.AsNoTracking().ToDictionaryAsync(x => x.Id);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(cleaned), JsonNode.Parse(rows["approved"].SnapshotJson)));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(cleaned), JsonNode.Parse(rows["pending"].SnapshotJson)));
            Assert.Equal("[]", rows["empty"].SnapshotJson);
            Assert.Equal("not json", rows["malformed"].SnapshotJson);
            Assert.Equal("{\"unexpected\":true}", rows["non-array"].SnapshotJson);
            Assert.Equal("private word note", (await db.Words.AsNoTracking().SingleAsync()).UserNotes);
            var sync = await db.AccountSyncRecords.AsNoTracking().Select(x => new { x.Data, x.Version }).SingleAsync();
            Assert.Equal("private sync note", JsonNode.Parse(sync.Data!)!["notes"]!.GetValue<string>());
            Assert.Equal(7, sync.Version);
        }
        await AssertPurged();
        // A downgrade cannot resurrect removed secrets; replaying Up is idempotent.
        await migrator.MigrateAsync(previous);
        await AssertPurged();
        await migrator.MigrateAsync();
        await AssertPurged();
    }
}
