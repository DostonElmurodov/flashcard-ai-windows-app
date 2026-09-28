using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Mavrylo.Tests;
public class AccountSyncTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    [Fact]
    public async Task PrivateNotesSyncAcrossOwnerSessionsButNeverAcrossAccountsWithSameRecordIds()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var alice = factory.CreateClient(); using var secondDevice = factory.CreateClient(); using var bob = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var aliceSubject = Guid.NewGuid().ToString();
        var a = await accounts.SignInAsync(new(aliceSubject, null, null), default, allowCreation: true);
        var a2 = await accounts.SignInAsync(new(aliceSubject, null, null), default, allowCreation: true);
        var b = await accounts.SignInAsync(new(Guid.NewGuid().ToString(), null, null), default, allowCreation: true);
        alice.DefaultRequestHeaders.Authorization = new("Bearer", a.AccessToken);
        secondDevice.DefaultRequestHeaders.Authorization = new("Bearer", a2.AccessToken);
        bob.DefaultRequestHeaders.Authorization = new("Bearer", b.AccessToken);
        var card = new { due = "2026-01-01T00:00:00Z", stability = 0, difficulty = 0, elapsed_days = 0, scheduled_days = 0, reps = 0, lapses = 0, state = 0 };
        async Task Push(HttpClient client, string notes)
        {
            object[] changes = [
                new { kind = "deck", id = "d", base_version = 0, deleted = false, data = new { name = "Deck", active = true, native_language = "en", learning_language = "es", created_at = "2026-01-01T00:00:00Z" } },
                new { kind = "word", id = "w", base_version = 0, deleted = false, data = new { deck_id = "d", word = "hola", translation = "hello", notes, created_at = "2026-01-01T00:00:00Z", card, reverse = card } }
            ];
            var response = await client.PostAsJsonAsync("/owlai/account/sync", new { changes });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("conflicts").EnumerateArray());
        }
        async Task<string?> Notes(HttpClient client)
        {
            var snapshot = await client.GetFromJsonAsync<JsonElement>("/owlai/account/sync?owner_id=" + a.Profile.Id);
            return Assert.Single(snapshot.GetProperty("records").EnumerateArray(), r => r.GetProperty("kind").GetString() == "word")
                .GetProperty("data").GetProperty("notes").GetString();
        }
        await Push(alice, "Alice private note");
        Assert.Empty((await bob.GetFromJsonAsync<JsonElement>("/owlai/account/sync")).GetProperty("records").EnumerateArray());
        Assert.Equal("Alice private note", await Notes(secondDevice));
        await Push(bob, "Bob private note");
        Assert.Equal("Bob private note", await Notes(bob));
        Assert.Equal("Alice private note", await Notes(alice));
        Assert.Equal("Alice private note", await Notes(secondDevice));
    }

    [Theory]
    [InlineData("/owlai/account/register")]
    [InlineData("/owlai/auth/register")]
    [InlineData("/owlai/auth/google")]
    [InlineData("/owlai/auth/apple")]
    public async Task RegistrationWithoutDeviceProofCannotCreateAccount(string path)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(path, new { email = Guid.NewGuid()+"@example.test", password = "password123", confirm_password = "password123" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task InvalidSyncPayloadsAreRejectedWithoutWrites()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<AccountService>().SignInAsync(new(Guid.NewGuid().ToString(), null, null), default, allowCreation: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        string[] invalid = [
            "{\"changes\":[null]}",
            "{\"changes\":[{\"kind\":\"settings\",\"id\":\"x\",\"base_version\":0,\"deleted\":true}]}",
            "{\"changes\":[{\"kind\":\"deck\",\"id\":\"x\",\"base_version\":-1,\"deleted\":true}]}",
            "{\"changes\":[{\"kind\":\"deck\",\"id\":\"x\",\"base_version\":0,\"deleted\":true,\"data\":{}}]}",
            "{\"changes\":[{\"kind\":\"deck\",\"id\":\"x\",\"base_version\":0,\"deleted\":false,\"data\":null}]}",
            "{\"changes\":[{\"kind\":\"word\",\"id\":\"x\",\"base_version\":0,\"deleted\":false,\"data\":{\"created_at\":\"2026-01-01T00:00:00Z\",\"deck_id\":\"d\",\"word\":\"x\",\"translation\":\"y\",\"card\":{\"due\":\"2026-01-01T00:00:00Z\",\"stability\":\"bad\"}}}]}",
            "{\"changes\":[{\"kind\":\"deck\",\"id\":\"x\",\"base_version\":0,\"deleted\":true,\"owner_id\":\"other\"}]}",
            "{\"changes\":null}"
        ];
        foreach (var payload in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/owlai/account/sync", new StringContent(payload, System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        var excessive = Enumerable.Range(0, 501).Select(i => new { kind = "deck", id = i.ToString(), base_version = 0, deleted = true });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/owlai/account/sync", new { changes = excessive })).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/owlai/account/sync")).GetProperty("records").EnumerateArray());
    }
    [Fact]
    public async Task WindowsOptionalNullsAndIosUntranslatedFractionalSchedulesRoundTrip()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<AccountService>().SignInAsync(new(Guid.NewGuid().ToString(), null, null), default, allowCreation: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        var card = new { due = "2026-01-01T00:00:00.000Z", stability = 0, difficulty = 0, elapsed_days = 0.5, scheduled_days = 1.5, reps = 0, lapses = 0, state = 0, last_review = (string?)null, learning_steps = 0 };
        var changes = new object[] {
            new { kind = "deck", id = "d", base_version = 0, deleted = false, data = new { name = "Deck", description = "", active = true, native_language = "en", learning_language = "es", created_at = "2026-01-01T00:00:00Z" } },
            new { kind = "word", id = "w", base_version = 0, deleted = false, data = new { deck_id = "d", word = "hola", native_language = "fr", learning_language = "pt", translation = "", pronunciation = (string?)null, notes = (string?)null, examples = Array.Empty<string>(), created_at = "2026-01-01T00:00:00Z", card, reverse = card } }
        };
        var response = await client.PostAsJsonAsync("/owlai/account/sync", new { changes });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await client.GetFromJsonAsync<JsonElement>("/owlai/account/sync");
        var word = snapshot.GetProperty("records").EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "word").GetProperty("data");
        Assert.Equal("", word.GetProperty("translation").GetString());
        Assert.Equal("fr", word.GetProperty("native_language").GetString());
        Assert.Equal("pt", word.GetProperty("learning_language").GetString());
        Assert.Equal(JsonValueKind.Null, word.GetProperty("notes").ValueKind);
        Assert.Equal(JsonValueKind.Null, word.GetProperty("card").GetProperty("last_review").ValueKind);
        Assert.Equal(1.5, word.GetProperty("reverse").GetProperty("scheduled_days").GetDouble());
    }
    private sealed class IdentityFixture(string subject) : IGoogleIdentityVerifier
    {
        public Task<GoogleIdentity> VerifyAsync(string token, CancellationToken ct) => Task.FromResult(new GoogleIdentity(subject, null, null));
    }
    [Fact]
    public async Task GoogleCannotCreateWithoutProofAndDesktopCannotEnroll()
    {
        var subject = Guid.NewGuid().ToString();
        await using var factory = new ApiFactory(postgres.ConnectionString, services =>
        {
            IosProofTestClient.Configure(services);
            Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<IGoogleIdentityVerifier>(services);
            services.AddSingleton<IGoogleIdentityVerifier>(new IdentityFixture(subject));
        });
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/owlai/account/google/session", "/owlai/account/desktop/google/session" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, new { id_token = "fixture" })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mavrylo.Data.AppDbContext>();
        Assert.False(db.Users.Any(x => x.GoogleSub == subject));
        db.Users.Add(new Mavrylo.Models.AppUser { GoogleSub = subject, Email = "" }); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/owlai/account/google/session", new { id_token = "fixture" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/owlai/account/desktop/google/session", new { id_token = "fixture" })).StatusCode);
        client.WithIosProof(factory);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/owlai/account/google/session", new { id_token = "fixture" })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Device-Authorization");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/owlai/account/desktop/google/session", new { id_token = "fixture" })).StatusCode);
    }
    [Fact]
    public async Task AtomicBatchRejectsOrphansOwnershipDuplicatesAndPreservesUnchangedRecordsOnConflict()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<AccountService>().SignInAsync(new(Guid.NewGuid().ToString(), null, null), default, allowCreation: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        object Deck(string id, long version = 0) => new { kind = "deck", id, base_version = version, deleted = false, data = new { name = "Deck", description = "", active = true, native_language = "en", learning_language = "es", created_at = "2026-01-01T00:00:00Z" } };
        var card = new { due = "2026-01-01T00:00:00Z", stability = 0, difficulty = 0, elapsed_days = 0, scheduled_days = 0, reps = 0, lapses = 0, state = 0 };
        var word = new { kind = "word", id = "w", base_version = 0, deleted = false, data = new { deck_id = "d", word = "hola", translation = "hello", created_at = "2026-01-01T00:00:00Z", card, reverse = card } };
        async Task<HttpResponseMessage> Push(params object[] changes) => await client.PostAsJsonAsync("/owlai/account/sync", new { changes });
        Assert.Equal(HttpStatusCode.BadRequest, (await Push(word)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Push(Deck("d"), Deck("d"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Push(word, Deck("d"))).StatusCode);
        var conflict = await Push(Deck("d", 0), Deck("another"));
        Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        var conflictJson = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(conflictJson.GetProperty("conflicts").EnumerateArray());
        Assert.Equal(2, conflictJson.GetProperty("records").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await Push(new { kind = "deck", id = "d", base_version = 1, deleted = true, data = (object?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/owlai/account/sync", new { owner_id = "someone-else", changes = new[] { Deck("x") } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Push(new { kind = "deck", id = "x", base_version = 0, deleted = false, data = new { owner_id = "someone-else" } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Push(new { kind = "word", id = "w", base_version = 1, deleted = true, data = (object?)null }, new { kind = "deck", id = "d", base_version = 1, deleted = true, data = (object?)null })).StatusCode);
        var records = (await client.GetFromJsonAsync<JsonElement>("/owlai/account/sync")).GetProperty("records");
        Assert.Equal(2, records.GetArrayLength());
        Assert.All(records.EnumerateArray(), x => Assert.True(x.GetProperty("deleted").GetBoolean()));
    }
    [Fact]
    public async Task DesktopRequiresEnrolledExistingAccountAndEnrollmentRequiresDeviceProof()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, IosProofTestClient.Configure);
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mavrylo.Data.AppDbContext>();
        var email = Guid.NewGuid()+"@example.test";
        var user = new Mavrylo.Models.AppUser { Email = email };
        user.PasswordHash = EmailCredentials.Hash(user, "password123");
        db.Users.Add(user); await db.SaveChangesAsync();
        var credentials = new { email, password = "password123" };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/owlai/account/desktop/email/session", credentials)).StatusCode);
        var login = await client.PostAsJsonAsync("/owlai/account/email/session", credentials);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("access_token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/owlai/account/sync")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/owlai/account/sync", new { changes = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/owlai/account/ios/enroll", new {})).StatusCode);
        client.WithIosProof(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/owlai/account/ios/enroll", new {})).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/owlai/account/sync")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/owlai/account/desktop/email/session", credentials)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/owlai/account/desktop/email/session", new { email = "unknown@example.test", password = "password123" })).StatusCode);
    }
    [Fact]
    public async Task AccountIsolationConflictsTombstonesAndRevocationAreEnforced()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var alice = factory.CreateClient(); using var bob = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var a = await accounts.SignInAsync(new(Guid.NewGuid().ToString(), null, null), default, allowCreation: true);
        var b = await accounts.SignInAsync(new(Guid.NewGuid().ToString(), null, null), default, allowCreation: true);
        alice.DefaultRequestHeaders.Authorization = new("Bearer", a.AccessToken);
        bob.DefaultRequestHeaders.Authorization = new("Bearer", b.AccessToken);
        async Task<JsonElement> Push(HttpClient client, long version, bool deleted = false)
        {
            var response = await client.PostAsJsonAsync("/owlai/account/sync", new { changes = new[] { new { kind = "deck", id = "same-id", base_version = version, deleted, data = deleted ? null : new { name = "Deck", description = "", active = true, native_language = "en", learning_language = "es", created_at = "2026-01-01T00:00:00Z" } } } });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        Assert.Equal(a.Profile.Id, (await Push(alice, 0)).GetProperty("owner_id").GetString());
        Assert.Empty((await bob.GetFromJsonAsync<JsonElement>("/owlai/account/sync?owner_id=" + a.Profile.Id)).GetProperty("records").EnumerateArray());
        Assert.Equal(b.Profile.Id, (await Push(bob, 0)).GetProperty("owner_id").GetString());
        var parallel = await Task.WhenAll(Push(alice, 1), Push(alice, 1));
        Assert.Single(parallel, x => x.GetProperty("conflicts").GetArrayLength() == 1);
        Assert.Single(parallel, x => x.GetProperty("conflicts").GetArrayLength() == 0);
        await Push(alice, 2, true);
        var stale = await Push(alice, 2);
        Assert.True(stale.GetProperty("conflicts")[0].GetProperty("deleted").GetBoolean());
        Assert.Equal(3, stale.GetProperty("conflicts")[0].GetProperty("version").GetInt64());
        var bobSnapshot = await bob.GetFromJsonAsync<JsonElement>("/owlai/account/sync?owner_id=" + a.Profile.Id);
        Assert.False(bobSnapshot.GetProperty("records")[0].GetProperty("deleted").GetBoolean());
        Assert.Equal(1, bobSnapshot.GetProperty("records")[0].GetProperty("version").GetInt64());
        await accounts.LogoutAsync(a.RefreshToken, default);
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync("/owlai/account/sync")).StatusCode);
    }
}
