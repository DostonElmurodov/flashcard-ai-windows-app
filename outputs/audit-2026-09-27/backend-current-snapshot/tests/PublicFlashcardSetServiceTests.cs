using System.Text.Json;
using Mavrylo.Dtos;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mavrylo.Tests;

public class PublicFlashcardSetServiceTests
{
    [Fact]
    public async Task PublicationNeverStoresPrivateNotesAndCatalogRedactsLegacyNotes()
    {
        using var testDb = TestDb.Create();
        var service = Service(testDb);
        var request = Request("Travel", "Ticket");
        request = request with { Cards = [request.Cards[0] with { Notes = "owner private note" }] };
        await service.UpsertAsync("device-a", request, default);
        var row = await testDb.Db.PublicFlashcardSets.SingleAsync();
        Assert.DoesNotContain("owner private note", row.SnapshotJson);
        Assert.Null(Assert.Single(JsonSerializer.Deserialize<List<PublicFlashcardSetCardDto>>(row.SnapshotJson)!).Notes);

        // Older clients/publications may already contain private notes.
        row.SnapshotJson = JsonSerializer.Serialize(request.Cards);
        row.Status = PublicFlashcardSetStatus.Approved;
        await testDb.Db.SaveChangesAsync();
        Assert.Null(Assert.Single(Assert.Single(await service.CatalogAsync(null, 50, default)).Cards).Notes);
    }

    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.Parse("2026-08-03T12:00:00Z");

    [Fact]
    public async Task Model_RejectsDuplicateOwnerAndClientSetId()
    {
        using var testDb = TestDb.Create();
        testDb.Db.Devices.Add(Device("device-row-a"));
        await testDb.Db.SaveChangesAsync();
        testDb.Db.PublicFlashcardSets.AddRange(
            Row("first", "device-row-a", "cat-u-1"),
            Row("second", "device-row-a", "cat-u-1"));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => testDb.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Model_RejectsStatusesOutsideThePublicCatalogWorkflow()
    {
        using var testDb = TestDb.Create();
        testDb.Db.Devices.Add(Device("device-row-a"));
        await testDb.Db.SaveChangesAsync();
        var row = Row("rejected", "device-row-a", "cat-u-1");
        row.Status = "rejected";
        testDb.Db.PublicFlashcardSets.Add(row);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => testDb.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Upsert_CreatesPending_AndReplacementReturnsApprovedToPending()
    {
        using var testDb = TestDb.Create();
        var service = Service(testDb);

        var first = await service.UpsertAsync("device-a", Request("Travel", "Ticket"), default);
        var row = await testDb.Db.PublicFlashcardSets.SingleAsync();
        row.Status = PublicFlashcardSetStatus.Approved;
        await testDb.Db.SaveChangesAsync();
        var replaced = await service.UpsertAsync("device-a", Request("Travel 2", "Airport"), default);
        await testDb.Db.Entry(row).ReloadAsync();

        Assert.Equal(PublicFlashcardSetStatus.Pending, first.Status);
        Assert.Equal(PublicFlashcardSetStatus.Pending, replaced.Status);
        Assert.Equal("Travel 2", row.Title);
        Assert.Contains("airport", row.SearchText);
        Assert.Equal(FixedNow.UtcDateTime, row.CreatedAt);
        Assert.Equal(FixedNow.UtcDateTime, row.UpdatedAt);
        Assert.Single(await testDb.Db.PublicFlashcardSets.ToListAsync());
    }

    [Fact]
    public async Task Catalog_ReturnsOnlyApproved_AndSearchesDescriptionWordAndTranslation()
    {
        using var testDb = TestDb.Create();
        var service = Service(testDb);
        await service.UpsertAsync(
            "device-a", Request("Travel", "Ticket", "airport phrases", "Billete"), default);
        await service.UpsertAsync(
            "device-b", Request("Food", "Bread", "restaurant", "Pan"), default);
        var rows = await testDb.Db.PublicFlashcardSets.OrderBy(x => x.Title).ToListAsync();
        rows.Single(x => x.Title == "Travel").Status = PublicFlashcardSetStatus.Approved;
        await testDb.Db.SaveChangesAsync();

        Assert.Single(await service.CatalogAsync("airport", 50, default));
        Assert.Single(await service.CatalogAsync("billete", 50, default));
        Assert.Empty(await service.CatalogAsync("restaurant", 50, default));
    }

    [Fact]
    public async Task Catalog_SearchIsTrimmedCaseAndDiacriticInsensitiveAcrossAllSearchableFields()
    {
        using var testDb = TestDb.Create();
        var service = Service(testDb);
        var request = Request("Café Travel", "Départ", "Crème guide", "Billeté");
        await service.UpsertAsync("device-a", request, default);
        var row = await testDb.Db.PublicFlashcardSets.SingleAsync();
        row.Status = PublicFlashcardSetStatus.Approved;
        await testDb.Db.SaveChangesAsync();

        Assert.Single(await service.CatalogAsync(" cafe ", 50, default));
        Assert.Single(await service.CatalogAsync(" CREME ", 50, default));
        Assert.Single(await service.CatalogAsync("depart", 50, default));
        Assert.Single(await service.CatalogAsync("billete", 50, default));
    }

    [Fact]
    public async Task Unpublish_UsesOwnerAndClientSetId()
    {
        using var testDb = TestDb.Create();
        var service = Service(testDb);
        await service.UpsertAsync("device-a", Request("Travel", "Ticket"), default);

        await service.UnpublishAsync("device-b", "cat-u-1", default);
        Assert.Single(await testDb.Db.PublicFlashcardSets.ToListAsync());
        await service.UnpublishAsync("device-a", "cat-u-1", default);
        await service.UnpublishAsync("device-a", "cat-u-1", default);
        Assert.Empty(await testDb.Db.PublicFlashcardSets.ToListAsync());
    }

    [Fact]
    public async Task ListMine_ReturnsOnlyTheOwnersSubmissions()
    {
        using var testDb = TestDb.Create();
        var service = Service(testDb);
        await service.UpsertAsync("device-a", Request("Travel", "Ticket"), default);
        await service.UpsertAsync("device-b", Request("Food", "Bread"), default);

        var mine = await service.ListMineAsync("device-a", default);

        var submission = Assert.Single(mine);
        Assert.Equal("Travel", submission.Title);
        Assert.Equal("cat-u-1", submission.ClientSetId);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("Valid", 0)]
    public async Task Upsert_RejectsInvalidTitleOrEmptyCards(string title, int cardCount)
    {
        using var testDb = TestDb.Create();
        var service = Service(testDb);
        var request = Request(title, "Ticket") with
        {
            Cards = cardCount == 0 ? [] : Request(title, "Ticket").Cards
        };

        await Assert.ThrowsAsync<PublicFlashcardSetValidationException>(
            () => service.UpsertAsync("device-a", request, default));
    }

    [Fact]
    public async Task Upsert_RejectsEveryInvalidFieldAndCountBoundary()
    {
        using var testDb = TestDb.Create();
        var service = Service(testDb);
        var invalidCases = new (string Name, Func<PublicFlashcardSetUpsertRequest, PublicFlashcardSetUpsertRequest> Mutate)[]
        {
            ("blank client set id", r => r with { ClientSetId = " \t" }),
            ("client set id too long", r => r with { ClientSetId = Text(101) }),
            ("title too long", r => r with { Title = Text(121) }),
            ("description too long", r => r with { Description = Text(1_001) }),
            ("too many cards", r => r with { Cards = Cards(501) }),
            ("blank card id", r => WithCard(r, Card() with { ClientCardId = " " })),
            ("card id too long", r => WithCard(r, Card() with { ClientCardId = Text(101) })),
            ("blank word", r => WithCard(r, Card() with { Word = "\n" })),
            ("word too long", r => WithCard(r, Card() with { Word = Text(501) })),
            ("empty translations", r => WithCard(r, Card() with { Translations = [] })),
            ("too many translations", r => WithCard(r, Card() with { Translations = Values(21, "t") })),
            ("blank translation", r => WithCard(r, Card() with { Translations = [" "] })),
            ("translation too long", r => WithCard(r, Card() with { Translations = [Text(501)] })),
            ("pronunciation too long", r => WithCard(r, Card() with { Pronunciation = Text(501) })),
            ("part of speech too long", r => WithCard(r, Card() with { PartOfSpeech = Text(101) })),
            ("too many examples", r => WithCard(r, Card() with
            {
                Examples = Values(21, "e"), ExampleTranslations = NullableValues(21, "g")
            })),
            ("example too long", r => WithCard(r, Card() with
            {
                Examples = [Text(2_001)], ExampleTranslations = ["gloss"]
            })),
            ("example translation too long", r => WithCard(r, Card() with
            {
                Examples = ["example"], ExampleTranslations = [Text(2_001)]
            })),
            ("notes too long", r => WithCard(r, Card() with { Notes = Text(4_001) })),
            ("native language raw value too long", r => WithCard(r, Card() with { NativeLanguage = Text(65) })),
            ("learning language raw value too long", r => WithCard(r, Card() with { LearningLanguage = Text(65) })),
            ("unsupported native language", r => WithCard(r, Card() with { NativeLanguage = "xx" })),
            ("unsupported learning language", r => WithCard(r, Card() with { LearningLanguage = "xx" }))
        };

        foreach (var (name, mutate) in invalidCases)
        {
            var error = await Record.ExceptionAsync(
                () => service.UpsertAsync("device-a", mutate(Request("Valid", "Word")), default));
            Assert.True(error is PublicFlashcardSetValidationException,
                $"Case '{name}' returned {error?.GetType().Name ?? "no error"}.");
        }
    }

    [Fact]
    public async Task Upsert_AcceptsEveryExactFieldAndCountBoundary()
    {
        using var testDb = TestDb.Create();
        var service = Service(testDb);
        var validCases = new PublicFlashcardSetUpsertRequest[]
        {
            Request(Text(120), "Word") with { ClientSetId = Text(100), Description = Text(1_000) },
            Request("Cards", "Word") with { Cards = Cards(500) },
            Request("Card fields", "Word") with { Cards = [Card() with
            {
                ClientCardId = Text(100),
                Word = Text(500),
                Translations = Values(20, Text(500)),
                Pronunciation = Text(500),
                PartOfSpeech = Text(100),
                Notes = Text(4_000),
                NativeLanguage = "English (USA)",
                LearningLanguage = "Spanish"
            }] },
            Request("Examples", "Word") with { Cards = [Card() with
            {
                Examples = Values(20, Text(2_000)),
                ExampleTranslations = NullableValues(20, Text(2_000))
            }] }
        };

        foreach (var request in validCases)
            await service.UpsertAsync("device-a", request, default);
    }

    [Fact]
    public async Task Upsert_DefensivelyRejectsNullCollectionsAndElements()
    {
        using var testDb = TestDb.Create();
        var service = Service(testDb);
        var invalidCases = new (string Name, PublicFlashcardSetUpsertRequest Request)[]
        {
            ("cards", Request("Valid", "Word") with { Cards = null! }),
            ("card", Request("Valid", "Word") with { Cards = [null!] }),
            ("translations", WithCard(Request("Valid", "Word"), Card() with { Translations = null! })),
            ("translation element", WithCard(Request("Valid", "Word"), Card() with { Translations = [null!] })),
            ("examples", WithCard(Request("Valid", "Word"), Card() with { Examples = null! })),
            ("example element", WithCard(Request("Valid", "Word"), Card() with
            {
                Examples = [null!], ExampleTranslations = [null]
            })),
            ("example translations", WithCard(Request("Valid", "Word"), Card() with { ExampleTranslations = null! }))
        };

        foreach (var (name, request) in invalidCases)
        {
            var error = await Record.ExceptionAsync(
                () => service.UpsertAsync("device-a", request, default));
            Assert.True(error is PublicFlashcardSetValidationException,
                $"Case '{name}' returned {error?.GetType().Name ?? "no error"}.");
        }
    }

    [Fact]
    public async Task Upsert_RejectsDuplicateCardIdsEmptyTranslationsAndMisalignedExamples()
    {
        using var testDb = TestDb.Create();
        var service = Service(testDb);
        var duplicate = Request("Valid", "Word") with { Cards = [Card(), Card()] };
        var emptyTranslation = WithCard(
            Request("Valid", "Word"), Card() with { Translations = ["Valid", "\t"] });
        var misaligned = WithCard(Request("Valid", "Word"), Card() with
        {
            Examples = ["one"], ExampleTranslations = []
        });

        await Assert.ThrowsAsync<PublicFlashcardSetValidationException>(
            () => service.UpsertAsync("device-a", duplicate, default));
        await Assert.ThrowsAsync<PublicFlashcardSetValidationException>(
            () => service.UpsertAsync("device-a", emptyTranslation, default));
        await Assert.ThrowsAsync<PublicFlashcardSetValidationException>(
            () => service.UpsertAsync("device-a", misaligned, default));
    }

    [Fact]
    public async Task Upsert_PreservesNullExampleGlossesAndNormalizesLanguages()
    {
        using var testDb = TestDb.Create();
        var service = Service(testDb);
        var request = WithCard(Request("Valid", "Word"), Card() with
        {
            Examples = ["A ticket"],
            ExampleTranslations = [null],
            NativeLanguage = " English (USA) ",
            LearningLanguage = "ES"
        });
        await service.UpsertAsync("device-a", request, default);
        var row = await testDb.Db.PublicFlashcardSets.SingleAsync();
        row.Status = PublicFlashcardSetStatus.Approved;
        await testDb.Db.SaveChangesAsync();

        var item = Assert.Single(await service.CatalogAsync(null, null, default));
        var card = Assert.Single(item.Cards);
        Assert.Null(Assert.Single(card.ExampleTranslations));
        Assert.Equal("en-us", card.NativeLanguage);
        Assert.Equal("es", card.LearningLanguage);
    }

    [Fact]
    public async Task Upsert_RejectsSnapshotOverEightHundredThousandUtf8Bytes()
    {
        using var testDb = TestDb.Create();
        var service = Service(testDb);
        var cards = Enumerable.Range(0, 500)
            .Select(i => Card() with
            {
                ClientCardId = $"card-{i}",
                Examples = [Text(2_000)],
                ExampleTranslations = [null]
            })
            .ToList();
        var request = Request("Oversize", "Word") with { Cards = cards };

        await Assert.ThrowsAsync<PublicFlashcardSetValidationException>(
            () => service.UpsertAsync("device-a", request, default));
    }

    [Fact]
    public async Task Catalog_ClampsLimitToOneHundredAndOrdersByUpdatedAtThenId()
    {
        using var testDb = TestDb.Create();
        _ = Service(testDb);
        var snapshot = JsonSerializer.Serialize(Request("Catalog", "Word").Cards);
        testDb.Db.PublicFlashcardSets.AddRange(Enumerable.Range(0, 105).Select(i => new PublicFlashcardSetEntity
        {
            Id = $"publication-{i:D3}",
            OwnerDeviceId = "device-a",
            ClientSetId = $"set-{i:D3}",
            Title = $"Set {i:D3}",
            SnapshotJson = snapshot,
            SearchText = $"set {i:D3} word translation",
            WordCount = 1,
            Status = PublicFlashcardSetStatus.Approved,
            CreatedAt = FixedNow.UtcDateTime,
            UpdatedAt = i >= 103 ? FixedNow.UtcDateTime.AddMinutes(1) : FixedNow.UtcDateTime
        }));
        await testDb.Db.SaveChangesAsync();
        var service = Service(testDb, seed: false);

        var catalog = await service.CatalogAsync(null, 500, default);

        Assert.Equal(100, catalog.Count);
        Assert.Equal("publication-103", catalog[0].Id);
        Assert.Equal("publication-104", catalog[1].Id);
        Assert.Equal("publication-000", catalog[2].Id);
        Assert.Equal("publication-097", catalog[^1].Id);
    }

    [Fact]
    public async Task Catalog_ThrowsDataExceptionForMalformedPersistedSnapshot()
    {
        using var testDb = TestDb.Create();
        var service = Service(testDb);
        var row = Row("broken-publication", "device-a", "cat-u-broken");
        row.Status = PublicFlashcardSetStatus.Approved;
        row.SnapshotJson = "not-json";
        testDb.Db.PublicFlashcardSets.Add(row);
        await testDb.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<PublicFlashcardSetDataException>(
            () => service.CatalogAsync(null, null, default));

        Assert.IsNotType<PublicFlashcardSetValidationException>(error);
    }

    private static PublicFlashcardSetService Service(TestDb testDb, bool seed = true)
    {
        if (seed)
        {
            testDb.Db.Devices.AddRange(Device("device-a"), Device("device-b"));
            testDb.Db.SaveChanges();
        }

        return new PublicFlashcardSetService(
            testDb.Db,
            new ManualTimeProvider(FixedNow),
            NullLogger<PublicFlashcardSetService>.Instance);
    }

    private static DeviceEntity Device(string id) => new()
    {
        Id = id,
        KeyId = $"key-{id}",
        DeviceUuid = $"uuid-{id}"
    };

    private static PublicFlashcardSetUpsertRequest Request(
        string title,
        string word,
        string? description = null,
        string translation = "Translation",
        string clientSetId = "cat-u-1") => new(
            ClientSetId: clientSetId,
            Title: title,
            Description: description,
            Cards: [Card(word, translation)]);

    private static PublicFlashcardSetCardDto Card(
        string word = "Ticket",
        string translation = "Billete") => new(
            ClientCardId: "word-1",
            Word: word,
            Translations: [translation],
            Pronunciation: null,
            PartOfSpeech: null,
            Examples: [],
            ExampleTranslations: [],
            Notes: null,
            NativeLanguage: "en-us",
            LearningLanguage: "es");

    private static PublicFlashcardSetUpsertRequest WithCard(
        PublicFlashcardSetUpsertRequest request,
        PublicFlashcardSetCardDto card) => request with { Cards = [card] };

    private static List<PublicFlashcardSetCardDto> Cards(int count) =>
        Enumerable.Range(0, count)
            .Select(i => Card() with { ClientCardId = $"word-{i}" })
            .ToList();

    private static List<string> Values(int count, string value) =>
        Enumerable.Repeat(value, count).ToList();

    private static List<string?> NullableValues(int count, string? value) =>
        Enumerable.Repeat(value, count).ToList();

    private static string Text(int length) => new('x', length);

    private static PublicFlashcardSetEntity Row(
        string id,
        string owner,
        string clientSetId) => new()
    {
        Id = id,
        OwnerDeviceId = owner,
        ClientSetId = clientSetId,
        Title = "Travel",
        SnapshotJson = "[]",
        SearchText = "travel",
        WordCount = 1,
        Status = PublicFlashcardSetStatus.Pending,
        CreatedAt = FixedNow.UtcDateTime,
        UpdatedAt = FixedNow.UtcDateTime
    };
}
