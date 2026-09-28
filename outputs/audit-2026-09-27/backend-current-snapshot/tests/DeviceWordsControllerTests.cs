using System.Security.Claims;
using Mavrylo.Areas.OwlAI.Controllers;
using Mavrylo.Dtos;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Mavrylo.Tests;

public class DeviceWordsControllerTests
{
    [Theory]
    [InlineData("development", "false", false)]
    [InlineData("production", "false", false)]
    [InlineData("development", null, false)]
    [InlineData("development", "true", true)]
    [InlineData("production", "true", true)]
    public async Task Upsert_EleventhWord_OnlyTestModeCanBypass(string environment, string? testMode, bool accepted)
    {
        using var testDb = TestDb.Create();
        testDb.Db.Devices.Add(new DeviceEntity { KeyId = "key", DeviceUuid = "device", Environment = environment });
        // Neither the authenticated development key nor another key can grant test access.
        testDb.Db.Devices.Add(new DeviceEntity { KeyId = "other", DeviceUuid = "device", Environment = "development" });
        await testDb.Db.SaveChangesAsync();
        var config = TestConfig.Create(new Dictionary<string, string?> { ["TestMode:Enabled"] = testMode });
        var words = new DeviceWordService(testDb.Db, TimeProvider.System, config);
        for (var i = 0; i < 10; i++)
            await words.UpsertAsync("device", Request($"word-{i}"), EntitlementService.Status.Free, CancellationToken.None);
        var controller = new DeviceWordsController(words,
            new DeviceContextService(testDb.Db, new EntitlementService(testDb.Db, TimeProvider.System)), config)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("keyId", "key")], "test"))
                }
            }
        };

        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.Upsert(Request("eleventh"), CancellationToken.None));

        Assert.Equal(accepted ? 200 : 402, result.StatusCode);
        Assert.Equal(accepted ? 11 : 10, await words.CountActiveAsync("device", CancellationToken.None));
        Assert.Empty(testDb.Db.Subscriptions);
    }

    private static DeviceWordUpsertRequest Request(string word) =>
        new(null, word, word, "en", "es", null, null, null, null);
}
