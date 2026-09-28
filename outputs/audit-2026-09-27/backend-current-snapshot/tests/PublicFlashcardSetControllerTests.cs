using System.Security.Claims;
using Mavrylo.Areas.OwlAI.Controllers;
using Mavrylo.Dtos;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mavrylo.Tests;

public class PublicFlashcardSetControllerTests
{
    [Fact]
    public async Task Routes_UseResolvedDeviceRowId_ForOwnerScopedOperations()
    {
        using var testDb = TestDb.Create();
        testDb.Db.Devices.Add(new DeviceEntity
        {
            Id = "device-row-id",
            KeyId = "public-catalog-key",
            DeviceUuid = "storekit-device-uuid"
        });
        await testDb.Db.SaveChangesAsync();
        var controller = Controller(testDb, "public-catalog-key");

        var publish = Assert.IsType<OkObjectResult>(
            await controller.Publish(Request("Travel", "Ticket"), default));
        Assert.Equal(PublicFlashcardSetStatus.Pending,
            Assert.IsType<PublicFlashcardSetSubmissionDto>(publish.Value).Status);

        var row = await testDb.Db.PublicFlashcardSets.SingleAsync();
        Assert.Equal("device-row-id", row.OwnerDeviceId);

        var mine = Assert.IsType<OkObjectResult>(await controller.Mine(default));
        Assert.Single(Assert.IsType<List<PublicFlashcardSetSubmissionDto>>(mine.Value));

        var catalog = Assert.IsType<OkObjectResult>(
            await controller.Catalog(new(null, 50), default));
        Assert.Empty(Assert.IsType<List<PublicFlashcardSetCatalogItemDto>>(catalog.Value));

        var unpublish = Assert.IsType<OkObjectResult>(
            await controller.Unpublish(new("cat-u-1"), default));
        Assert.True(
            Assert.IsType<PublicFlashcardSetUnpublishResponse>(unpublish.Value).Unpublished);
        Assert.Empty(await testDb.Db.PublicFlashcardSets.ToListAsync());
    }

    [Fact]
    public async Task Publish_WithoutKeyIdClaim_ReturnsUnknownDeviceUnauthorized()
    {
        using var testDb = TestDb.Create();
        var controller = Controller(testDb, keyId: null);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(
            await controller.Publish(Request("Travel", "Ticket"), default));

        Assert.Equal("{\"error\":\"unknown device\"}",
            System.Text.Json.JsonSerializer.Serialize(unauthorized.Value));
        Assert.Empty(await testDb.Db.PublicFlashcardSets.ToListAsync());
    }

    [Fact]
    public async Task Publish_InvalidRequest_ReturnsValidationMessageAsBadRequest()
    {
        using var testDb = TestDb.Create();
        testDb.Db.Devices.Add(new DeviceEntity
        {
            Id = "device-row-id",
            KeyId = "public-catalog-key",
            DeviceUuid = "storekit-device-uuid"
        });
        await testDb.Db.SaveChangesAsync();
        var controller = Controller(testDb, "public-catalog-key");

        var badRequest = Assert.IsType<BadRequestObjectResult>(
            await controller.Publish(Request("", "Ticket"), default));

        Assert.Contains("Title is required", System.Text.Json.JsonSerializer.Serialize(badRequest.Value));
    }

    private static PublicFlashcardSetsController Controller(TestDb testDb, string? keyId)
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-03T12:00:00Z"));
        var publicSets = new PublicFlashcardSetService(
            testDb.Db,
            time,
            NullLogger<PublicFlashcardSetService>.Instance);
        var context = new DeviceContextService(
            testDb.Db,
            new EntitlementService(testDb.Db, time));
        var controller = new PublicFlashcardSetsController(publicSets, context);
        var claims = keyId is null ? [] : new[] { new Claim("keyId", keyId) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };
        return controller;
    }

    private static PublicFlashcardSetUpsertRequest Request(string title, string word) => new(
        ClientSetId: "cat-u-1",
        Title: title,
        Description: null,
        Cards:
        [
            new PublicFlashcardSetCardDto(
                ClientCardId: "word-1",
                Word: word,
                Translations: ["Billete"],
                Pronunciation: null,
                PartOfSpeech: null,
                Examples: [],
                ExampleTranslations: [],
                Notes: null,
                NativeLanguage: "en-us",
                LearningLanguage: "es")
        ]);
}
