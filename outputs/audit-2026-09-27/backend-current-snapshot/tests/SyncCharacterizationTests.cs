using System.Security.Claims;
using Mavrylo.Areas.OwlAI.Controllers;
using Mavrylo.Dtos;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Mavrylo.Tests;

// Characterization tests: pin the CURRENT SyncController behavior (last-write-wins push, soft
// delete, always-empty conflicts, user isolation) before it is thinned into SyncService.
public class SyncCharacterizationTests
{
    private static SyncController NewController(TestDb db, string userId)
        => new(new SyncService(db.Db))
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

    private static WordUpsert Word(string word, string? translation = null)
        => new(word, translation, null, null, null, null, null, null, "es", "en", null, null, null, null, null, null);

    private static SyncPushRequest PushWords(params WordPushItem[] items)
        => new(null, items.ToList(), null, null);

    private static SyncPullResponse Pull(ActionResult<SyncPullResponse> r)
        => Assert.IsType<SyncPullResponse>(Assert.IsType<OkObjectResult>(r.Result).Value);

    [Fact]
    public async Task Push_CreatesWord_ChangesReturnsIt_WithEmptyConflicts()
    {
        using var db = TestDb.Create();

        var push = await NewController(db, "user-1").Push(
            PushWords(new WordPushItem("w1", false, Word("Hola", "Hello"))), CancellationToken.None);
        var resp = Assert.IsType<SyncPushResponse>(Assert.IsType<OkObjectResult>(push.Result).Value);
        Assert.True(resp.Accepted);
        Assert.Empty(resp.Conflicts);

        var pull = Pull(await NewController(db, "user-1").Changes(null, CancellationToken.None));
        Assert.Single(pull.Words);
        Assert.Equal("Hola", pull.Words[0].Word);
        Assert.Equal("Hello", pull.Words[0].Translation);
    }

    [Fact]
    public async Task Push_OverwritesExistingWord_LastWriteWins()
    {
        using var db = TestDb.Create();
        await NewController(db, "user-1").Push(PushWords(new WordPushItem("w1", false, Word("Hola", "Hello"))), CancellationToken.None);
        await NewController(db, "user-1").Push(PushWords(new WordPushItem("w1", false, Word("Hola", "Hi"))), CancellationToken.None);

        var pull = Pull(await NewController(db, "user-1").Changes(null, CancellationToken.None));
        Assert.Single(pull.Words);
        Assert.Equal("Hi", pull.Words[0].Translation);
    }

    [Fact]
    public async Task Push_SoftDeletesExistingWord()
    {
        using var db = TestDb.Create();
        await NewController(db, "user-1").Push(PushWords(new WordPushItem("w1", false, Word("Hola"))), CancellationToken.None);
        await NewController(db, "user-1").Push(PushWords(new WordPushItem("w1", true, null)), CancellationToken.None);

        var pull = Pull(await NewController(db, "user-1").Changes(null, CancellationToken.None));
        Assert.Single(pull.Words);
        Assert.True(pull.Words[0].IsDeleted);
    }

    [Fact]
    public async Task Push_IsUserIsolated()
    {
        using var db = TestDb.Create();
        await NewController(db, "user-1").Push(PushWords(new WordPushItem("w1", false, Word("A"))), CancellationToken.None);

        var pull = Pull(await NewController(db, "user-2").Changes(null, CancellationToken.None));
        Assert.Empty(pull.Words);
    }
}
