using Mavrylo.Dtos;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Xunit;

namespace Mavrylo.Tests;

public class DeviceWordServiceTests
{
    [Fact]
    public async Task FreeUser_CanCreateTenWords_AndEleventhIsRejected()
    {
        using var testDb = TestDb.Create();
        var service = new DeviceWordService(testDb.Db, new ManualTimeProvider(DateTimeOffset.Parse("2026-06-13T12:00:00Z")));

        for (var i = 0; i < DeviceWordService.FreeLimit; i++)
        {
            var result = await service.UpsertAsync("device", Request($"word-{i}"), EntitlementService.Status.Free, CancellationToken.None);
            Assert.True(result.Accepted);
        }

        var rejected = await service.UpsertAsync("device", Request("word-10"), EntitlementService.Status.Free, CancellationToken.None);

        Assert.False(rejected.Accepted);
        Assert.Equal(DeviceWordService.FreeLimit, rejected.ActiveWordCount);
    }

    [Fact]
    public async Task PremiumUser_CanExceedFreeLimit()
    {
        using var testDb = TestDb.Create();
        var service = new DeviceWordService(testDb.Db, TimeProvider.System);

        for (var i = 0; i <= DeviceWordService.FreeLimit; i++)
        {
            var result = await service.UpsertAsync("device", Request($"word-{i}"), EntitlementService.Status.Premium, CancellationToken.None);
            Assert.True(result.Accepted);
        }

        Assert.Equal(DeviceWordService.FreeLimit + 1, await service.CountActiveAsync("device", CancellationToken.None));
    }

    [Theory]
    [InlineData("expired_trial", "false", false)]
    [InlineData("expired_paid", "false", false)]
    [InlineData("revoked", "false", false)]
    [InlineData("unknown", "false", false)]
    [InlineData("expired_trial", "true", true)]
    [InlineData("expired_paid", "true", true)]
    [InlineData("revoked", "true", true)]
    [InlineData("unknown", "true", true)]
    public async Task InactiveEntitlement_OnlyTestModeAllowsNewWords(string entitlement, string testMode, bool accepted)
    {
        using var testDb = TestDb.Create();
        var config = TestConfig.Create(new Dictionary<string, string?> { ["TestMode:Enabled"] = testMode });
        var service = new DeviceWordService(testDb.Db, TimeProvider.System, config);
        var result = await service.UpsertAsync("device", Request("new"), entitlement, CancellationToken.None);
        Assert.Equal(accepted, result.Accepted);
        Assert.Equal(accepted ? 1 : 0, await service.CountActiveAsync("device", CancellationToken.None));
    }

    [Theory]
    [InlineData("premium")]
    [InlineData("trial")]
    [InlineData("grace")]
    public async Task TestModeFalse_PreservesRealActiveEntitlements(string entitlement)
    {
        using var testDb = TestDb.Create();
        var config = TestConfig.Create(new Dictionary<string, string?> { ["TestMode:Enabled"] = "false" });
        var service = new DeviceWordService(testDb.Db, TimeProvider.System, config);
        for (var i = 0; i < 11; i++)
            Assert.True((await service.UpsertAsync("device", Request($"word-{i}"), entitlement, CancellationToken.None)).Accepted);
        Assert.Equal(11, await service.CountActiveAsync("device", CancellationToken.None));
    }

    [Theory]
    [InlineData("free")]
    [InlineData("expired_trial")]
    [InlineData("expired_paid")]
    [InlineData("revoked")]
    public async Task TestModeFalse_PreservesUpdatesToExistingWords(string entitlement)
    {
        using var testDb = TestDb.Create();
        var config = TestConfig.Create(new Dictionary<string, string?> { ["TestMode:Enabled"] = "false" });
        var service = new DeviceWordService(testDb.Db, TimeProvider.System, config);
        for (var i = 0; i < 10; i++)
            await service.UpsertAsync("device", Request($"word-{i}"), "premium", CancellationToken.None);
        var result = await service.UpsertAsync("device", Request("word-0") with { Translation = "updated" }, entitlement, CancellationToken.None);
        Assert.True(result.Accepted);
        Assert.Equal(10, result.ActiveWordCount);
        Assert.Equal("updated", testDb.Db.DeviceWords.Single(x => x.NormalizedWord == "word-0").Translation);
    }

    [Fact]
    public async Task AiReservation_ReservesSlot_AndAllowsExistingWord()
    {
        using var testDb = TestDb.Create();
        testDb.Db.Devices.Add(new DeviceEntity { KeyId = "key", DeviceUuid = "device" });
        await testDb.Db.SaveChangesAsync();
        var service = new DeviceWordService(testDb.Db, TimeProvider.System);

        var first = await service.TryReserveAiSlotAsync("key", EntitlementService.Status.Free, "Hello", "EN", "ES", CancellationToken.None);
        var second = await service.TryReserveAiSlotAsync("key", EntitlementService.Status.Free, "hello", "en", "es", CancellationToken.None);

        Assert.True(first.Allowed);
        Assert.True(second.Allowed);
        Assert.Equal(1, await service.CountActiveAsync("device", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesMatchingWord()
    {
        using var testDb = TestDb.Create();
        var service = new DeviceWordService(testDb.Db, TimeProvider.System);
        await service.UpsertAsync("device", Request("hello", "client-1"), EntitlementService.Status.Free, CancellationToken.None);

        var result = await service.DeleteAsync("device", new DeviceWordDeleteRequest("client-1", null, null, null), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal(0, result.ActiveWordCount);
    }

    [Fact]
    public void TryReadWordContext_HandlesCamelSnakeAndInvalidJson()
    {
        var snake = DeviceWordService.TryReadWordContext("""{"word":"hola","native_language":"en","learning_language":"es"}"""u8.ToArray());
        var camel = DeviceWordService.TryReadWordContext("""{"word":"hola","nativeLanguage":"en","learningLanguage":"es"}"""u8.ToArray());
        var invalid = DeviceWordService.TryReadWordContext("not json"u8.ToArray());

        Assert.Equal(("hola", "en", "es"), snake);
        Assert.Equal(("hola", "en", "es"), camel);
        Assert.Null(invalid.Word);
    }

    private static DeviceWordUpsertRequest Request(string word, string? clientId = null) =>
        new(clientId, word, word, "en", "es", "translation", "pron", "noun", "{}");
}
