using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Mavrylo.Data;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Mavrylo.Tests;

public class PublicFlashcardSetRouteTests(PostgresContainerFixture postgres)
    : IClassFixture<PostgresContainerFixture>
{
    private const string SimulatorKeyId = "SIMULATOR-PUBLIC-CATALOG";
    private const string UnknownRealKeyId = "REAL-UNKNOWN-PUBLIC-CATALOG";

    [Fact]
    public async Task Routes_RejectWrongAuthentication_AndRequireAppAttestForRealDevices()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        using var missingToken = await PostJsonAsync(client, "/owlai/public-flashcard-sets/catalog", CatalogJson);
        Assert.Equal(HttpStatusCode.Unauthorized, missingToken.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Tokens().CreateToken(new AppUser { Id = "user-id", Email = "user@example.com" }));
        using var userToken = await PostJsonAsync(client, "/owlai/public-flashcard-sets/catalog", CatalogJson);
        Assert.Equal(HttpStatusCode.Unauthorized, userToken.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Tokens().CreateDeviceToken("REAL-PUBLIC-CATALOG", EntitlementService.Status.Free).Token);
        using var noAssertion = await PostJsonAsync(client, "/owlai/public-flashcard-sets/catalog", CatalogJson);
        Assert.Equal(HttpStatusCode.Forbidden, noAssertion.StatusCode);

        var challengeId = await IssueAssertionChallengeAsync(
            factory,
            UnknownRealKeyId,
            "/owlai/public-flashcard-sets/catalog");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Tokens().CreateDeviceToken(UnknownRealKeyId, EntitlementService.Status.Free).Token);
        using var unknownRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/owlai/public-flashcard-sets/catalog")
        {
            Content = new StringContent(CatalogJson, Encoding.UTF8, "application/json")
        };
        unknownRequest.Headers.Add("X-App-Attest-Key-Id", UnknownRealKeyId);
        unknownRequest.Headers.Add("X-App-Attest-Challenge-Id", challengeId);
        unknownRequest.Headers.Add("X-App-Attest-Assertion", "AQ==");

        using var unknownDevice = await client.SendAsync(unknownRequest);
        Assert.Equal(HttpStatusCode.Forbidden, unknownDevice.StatusCode);
        Assert.Contains(
            "device not registered for assertion",
            await unknownDevice.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SimulatorDevice_ReachesEveryRoute_UsesSnakeCase_AndCatalogIsApprovedOnly()
    {
        await using var factory = SimulatorFactory();
        using var client = factory.CreateClient();
        await SeedSimulatorAsync(factory, includeApprovedCatalogItem: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Tokens().CreateDeviceToken(SimulatorKeyId, EntitlementService.Status.Free).Token);

        using var publish = await PostJsonAsync(
            client, "/owlai/public-flashcard-sets/publish", PublishJson);
        var publishBody = await publish.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        string pendingPublicationId;
        using (var json = JsonDocument.Parse(publishBody))
        {
            pendingPublicationId = json.RootElement.GetProperty("id").GetString()!;
            Assert.Equal("cat-u-1", json.RootElement.GetProperty("client_set_id").GetString());
            Assert.Equal("pending", json.RootElement.GetProperty("status").GetString());
            Assert.False(json.RootElement.TryGetProperty("ClientSetId", out _));
        }

        using var mine = await client.GetAsync("/owlai/public-flashcard-sets/mine");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        using (var json = JsonDocument.Parse(await mine.Content.ReadAsStringAsync()))
        {
            Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
            Assert.Equal("cat-u-1", json.RootElement[0].GetProperty("client_set_id").GetString());
        }

        using var catalog = await PostJsonAsync(
            client, "/owlai/public-flashcard-sets/catalog", CatalogJson);
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        using (var json = JsonDocument.Parse(await catalog.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, json.RootElement.GetArrayLength());
            Assert.Equal(
                "approved-publication",
                json.RootElement[0].GetProperty("id").GetString());
            Assert.DoesNotContain(
                json.RootElement.EnumerateArray(),
                item => item.GetProperty("id").GetString() == pendingPublicationId);
        }

        using var unpublish = await PostJsonAsync(
            client,
            "/owlai/public-flashcard-sets/unpublish",
            """{"client_set_id":"cat-u-1"}""");
        Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);
        using (var json = JsonDocument.Parse(await unpublish.Content.ReadAsStringAsync()))
            Assert.True(json.RootElement.GetProperty("unpublished").GetBoolean());
    }

    [Fact]
    public async Task Publish_OverNineHundredThousandBytes_ReturnsPayloadTooLarge()
    {
        await using var factory = SimulatorFactory();
        // RequestSizeLimit is enforced by Kestrel; the in-memory TestServer
        // does not enforce transport request-body limits.
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        await SeedSimulatorAsync(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Tokens().CreateDeviceToken(SimulatorKeyId, EntitlementService.Status.Free).Token);
        using var content = new StringContent(new string('x', 900_001), Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/owlai/public-flashcard-sets/publish", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    // Route tests explicitly model a permitted simulator. The real verifier's
    // simulator bypass is intentionally disabled in Release, which CI builds.
    private ApiFactory SimulatorFactory() => new(postgres.ConnectionString, services =>
    {
        services.RemoveAll<IAppAttestVerifier>();
        services.AddSingleton<IAppAttestVerifier>(new FakeAppAttestVerifier
        {
            IsDevelopmentBypassEnabled = true
        });
    });

    private static JwtTokenService Tokens() =>
        new(TestConfig.Create(), TimeProvider.System);

    private static Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client,
        string path,
        string json) =>
        client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task<string> IssueAssertionChallengeAsync(
        ApiFactory factory,
        string keyId,
        string requestPath)
    {
        using var scope = factory.Services.CreateScope();
        var challenges = scope.ServiceProvider.GetRequiredService<ChallengeService>();
        var issued = await challenges.IssueAssertionAsync(keyId, requestPath);
        return issued.ChallengeId;
    }

    private static async Task SeedSimulatorAsync(
        ApiFactory factory,
        bool includeApprovedCatalogItem = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.PublicFlashcardSets
            .Where(x => x.OwnerDeviceId == "public-catalog-device-row"
                        || x.OwnerDeviceId == "approved-catalog-owner")
            .ExecuteDeleteAsync();
        if (!await db.Devices.AnyAsync(x => x.KeyId == SimulatorKeyId))
        {
            db.Devices.Add(new DeviceEntity
            {
                Id = "public-catalog-device-row",
                KeyId = SimulatorKeyId,
                DeviceUuid = "public-catalog-storekit-uuid",
                Environment = "development"
            });
        }
        if (!await db.Devices.AnyAsync(x => x.Id == "approved-catalog-owner"))
        {
            db.Devices.Add(new DeviceEntity
            {
                Id = "approved-catalog-owner",
                KeyId = "approved-catalog-key",
                DeviceUuid = "approved-catalog-uuid"
            });
        }
        await db.SaveChangesAsync();

        if (!includeApprovedCatalogItem)
            return;

        db.PublicFlashcardSets.Add(new PublicFlashcardSetEntity
        {
            Id = "approved-publication",
            OwnerDeviceId = "approved-catalog-owner",
            ClientSetId = "approved-client-set",
            Title = "Approved Travel",
            Description = "Reviewed travel vocabulary",
            SnapshotJson = ApprovedSnapshotJson,
            SearchText = "approved travel reviewed vocabulary passport pasaporte",
            WordCount = 1,
            Status = PublicFlashcardSetStatus.Approved,
            CreatedAt = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
    }

    private const string CatalogJson = """{"query":null,"limit":50}""";

    private const string ApprovedSnapshotJson = """
        [{
          "ClientCardId": "approved-card",
          "Word": "Passport",
          "Translations": ["Pasaporte"],
          "Pronunciation": null,
          "PartOfSpeech": null,
          "Examples": [],
          "ExampleTranslations": [],
          "Notes": null,
          "NativeLanguage": "en-us",
          "LearningLanguage": "es"
        }]
        """;

    private const string PublishJson = """
        {
          "client_set_id": "cat-u-1",
          "title": "Travel",
          "description": "Airport phrases",
          "cards": [
            {
              "client_card_id": "word-1",
              "word": "Ticket",
              "translations": ["Billete"],
              "pronunciation": null,
              "part_of_speech": null,
              "examples": [],
              "example_translations": [],
              "notes": null,
              "native_language": "en-us",
              "learning_language": "es"
            }
          ]
        }
        """;
}
