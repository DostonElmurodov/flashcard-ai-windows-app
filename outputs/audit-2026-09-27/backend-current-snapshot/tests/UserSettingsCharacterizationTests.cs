using System.Security.Claims;
using Mavrylo.Areas.OwlAI.Controllers;
using Mavrylo.Dtos;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Mavrylo.Tests;

// Characterization tests: pin the CURRENT UserSettings behavior (the client-facing 409 retry
// contract especially) before the controller is thinned into UserSettingsService.
public class UserSettingsCharacterizationTests
{
    private static UserSettingsController NewController(TestDb db, string userId = "user-1")
    {
        var c = new UserSettingsController(new UserSettingsService(db.Db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "test"))
                }
            }
        };
        return c;
    }

    private static UserSettingsUpsert Upsert(string? native = null, string? learning = null)
        => new(native, learning, null, null, null, null, null, null, null, null);

    [Fact]
    public async Task Create_DuplicateReturns409_List_Patch_RoundTrip()
    {
        using var db = TestDb.Create();

        var created = await NewController(db).Create(
            new UserSettingsUpsert("en", "es", 20, true, "forward", true, new[] { 540 }, false, "system", "blue"),
            CancellationToken.None);
        var createdResult = Assert.IsType<CreatedAtActionResult>(created.Result);
        var dto = Assert.IsType<UserSettingsDto>(createdResult.Value);
        Assert.Equal("en", dto.NativeLanguage);

        // Duplicate create -> 409 with the EXACT message the iOS client keys on to retry GET+PATCH.
        var dup = await NewController(db).Create(Upsert(), CancellationToken.None);
        var conflict = Assert.IsType<ConflictObjectResult>(dup.Result);
        Assert.Equal("Settings already exist; use PATCH", conflict.Value);

        var list = await NewController(db).List(CancellationToken.None);
        var listOk = Assert.IsType<OkObjectResult>(list.Result);
        Assert.Single(Assert.IsType<List<UserSettingsDto>>(listOk.Value));

        var patched = await NewController(db).Patch(dto.Id, Upsert(native: "ru"), CancellationToken.None);
        var patchOk = Assert.IsType<OkObjectResult>(patched.Result);
        Assert.Equal("ru", Assert.IsType<UserSettingsDto>(patchOk.Value).NativeLanguage);
    }

    [Fact]
    public async Task Patch_UnknownId_NotFound()
    {
        using var db = TestDb.Create();
        var res = await NewController(db).Patch("nope", Upsert(native: "en"), CancellationToken.None);
        Assert.IsType<NotFoundResult>(res.Result);
    }
}
