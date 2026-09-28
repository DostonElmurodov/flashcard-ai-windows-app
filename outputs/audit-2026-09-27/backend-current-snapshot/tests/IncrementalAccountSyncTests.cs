using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mavrylo.Data;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mavrylo.Tests;

public class IncrementalAccountSyncTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    private const string Route = "/owlai/account/sync";
    private static object Deck(string id, long version = 0, string name = "Deck") => new
    {
        kind = "deck", id, base_version = version, deleted = false,
        data = new { name, active = true, native_language = "en", learning_language = "es", created_at = "2026-01-01T00:00:00Z" }
    };
    private static object Delete(string kind, string id, long version) => new { kind, id, base_version = version, deleted = true, data = (object?)null };
    private static async Task Login(ApiFactory factory, HttpClient client, string? subject = null)
    {
        using var scope = factory.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<AccountService>()
            .SignInAsync(new(subject ?? Guid.NewGuid().ToString(), null, null), default, allowCreation: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
    }
    private static async Task<JsonElement> Push(HttpClient client, long? since, params object[] changes)
    {
        var response = await client.PostAsJsonAsync(Route, new { changes, since });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static long Cursor(JsonElement response) => response.GetProperty("cursor").GetInt64();

    [Fact]
    public async Task IdleCursorChecksReturnNoRecordsAndDoNotReadRecordPayloads()
    {
        var sql = new QueryRecorder();
        await using var factory = new ApiFactory(postgres.ConnectionString,
            services => services.AddDbContext<AppDbContext>(options => options.AddInterceptors(sql)));
        using var client = factory.CreateClient();
        await Login(factory, client);
        var initial = await Push(client, null, Deck("d"));
        var cursor = Cursor(initial);
        Assert.True(cursor > 0);
        sql.Commands.Clear();
        var idle = await client.GetFromJsonAsync<JsonElement>($"{Route}?since={cursor}");
        Assert.Equal(cursor, Cursor(idle));
        Assert.False(idle.GetProperty("is_snapshot").GetBoolean());
        Assert.Empty(idle.GetProperty("records").EnumerateArray());
        Assert.DoesNotContain(sql.Commands, command => command.Contains("account_sync_records", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(sql.Commands, command => command.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeltasIncludeUpdatesAndTombstonesButNotUnchangedRecords()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        await Login(factory, client);
        var initial = await Push(client, null, Deck("a"), Deck("b"), Deck("c"));
        var before = Cursor(initial);
        var updated = await Push(client, before, Deck("b", 1, "Updated"), Delete("deck", "c", 1));
        Assert.False(updated.GetProperty("is_snapshot").GetBoolean());
        Assert.Equal(2, updated.GetProperty("records").GetArrayLength());
        Assert.True(Cursor(updated) > before);
        var delta = await client.GetFromJsonAsync<JsonElement>($"{Route}?since={before}");
        Assert.Equal(2, delta.GetProperty("records").GetArrayLength());
        Assert.DoesNotContain(delta.GetProperty("records").EnumerateArray(), record => record.GetProperty("id").GetString() == "a");
        Assert.Contains(delta.GetProperty("records").EnumerateArray(), record => record.GetProperty("id").GetString() == "c" && record.GetProperty("deleted").GetBoolean());
        var reset = await client.GetFromJsonAsync<JsonElement>($"{Route}?since={Cursor(updated) + 1}");
        Assert.True(reset.GetProperty("is_snapshot").GetBoolean());
        Assert.Equal(3, reset.GetProperty("records").GetArrayLength());
        // Recovery must not apply a pending upload and catch the server cursor
        // up, otherwise the next request could hide the lost history.
        var rejectedUpload = await Push(client, Cursor(updated) + 1, Deck("b", 2, "Not applied"));
        Assert.True(rejectedUpload.GetProperty("is_snapshot").GetBoolean());
        Assert.Equal(Cursor(updated), Cursor(rejectedUpload));
        Assert.Equal("Updated", rejectedUpload.GetProperty("records").EnumerateArray()
            .Single(r => r.GetProperty("id").GetString() == "b").GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task LegacyWritesAdvanceCursorAndLegacyReadsStillReturnFullSnapshots()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        await Login(factory, client);
        var initial = await Push(client, null, Deck("a"));
        var legacy = await client.PostAsJsonAsync(Route, new { changes = new[] { Deck("b") } });
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        var body = await legacy.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("is_snapshot").GetBoolean());
        Assert.Equal(2, body.GetProperty("records").GetArrayLength());
        var delta = await client.GetFromJsonAsync<JsonElement>($"{Route}?since={Cursor(initial)}");
        Assert.Equal("b", Assert.Single(delta.GetProperty("records").EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task IdenticalIncrementalRetryAcknowledgesOnceWithoutIncrementingVersions()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        await Login(factory, client);
        var first = await Push(client, 0, Deck("a"));
        var retry = await Push(client, 0, Deck("a"));
        Assert.Empty(retry.GetProperty("conflicts").EnumerateArray());
        Assert.Equal(Cursor(first), Cursor(retry));
        Assert.Equal(1, Assert.Single(retry.GetProperty("records").EnumerateArray()).GetProperty("version").GetInt64());
        var ack = await Push(client, Cursor(first), Deck("a"));
        Assert.Single(ack.GetProperty("records").EnumerateArray());
        Assert.Equal(Cursor(first), Cursor(ack));
        // Different stale content still conflicts and the whole mixed batch is rejected.
        var conflict = await Push(client, Cursor(first), Deck("a", 0, "Stale edit"), Deck("b"));
        Assert.Single(conflict.GetProperty("conflicts").EnumerateArray());
        Assert.Equal(Cursor(first), Cursor(conflict));
        var snapshot = await client.GetFromJsonAsync<JsonElement>(Route);
        Assert.Single(snapshot.GetProperty("records").EnumerateArray());
        var deleted = await Push(client, Cursor(first), Delete("deck", "a", 1));
        var deletionRetry = await Push(client, Cursor(first), Delete("deck", "a", 1));
        Assert.Empty(deletionRetry.GetProperty("conflicts").EnumerateArray());
        Assert.Equal(Cursor(deleted), Cursor(deletionRetry));
        Assert.True(Assert.Single(deletionRetry.GetProperty("records").EnumerateArray()).GetProperty("deleted").GetBoolean());
    }

    [Fact]
    public async Task CursorAndRecordsRemainAccountScopedAndInvalidCursorsAreRejected()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var alice = factory.CreateClient(); using var bob = factory.CreateClient();
        await Login(factory, alice); await Login(factory, bob);
        await Push(alice, 0, Deck("private"));
        var other = await bob.GetFromJsonAsync<JsonElement>($"{Route}?since=0&owner_id=ignored");
        Assert.Equal(0, Cursor(other));
        Assert.Empty(other.GetProperty("records").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync($"{Route}?since=-1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync(Route, new { changes = Array.Empty<object>(), since = -1 })).StatusCode);
        alice.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync($"{Route}?since=0")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentAccountWritersAllocateDistinctCursorsWithoutMissingRecords()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var first = factory.CreateClient(); using var second = factory.CreateClient();
        var subject = Guid.NewGuid().ToString();
        await Login(factory, first, subject); await Login(factory, second, subject);
        var responses = await Task.WhenAll(Push(first, 0, Deck("a")), Push(second, 0, Deck("b")));
        Assert.Equal(2, responses.Select(Cursor).Distinct().Count());
        var cursor = responses.Min(Cursor);
        var later = await first.GetFromJsonAsync<JsonElement>($"{Route}?since={cursor}");
        Assert.Single(later.GetProperty("records").EnumerateArray());
        Assert.Equal(responses.Max(Cursor), Cursor(later));
    }

    private sealed class QueryRecorder : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
