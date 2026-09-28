using System.Text;
using Mavrylo.Data;
using Mavrylo.Filters;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mavrylo.Tests;

public class AiProtectionFilterTests
{
    [Theory]
    [InlineData("true", 0)]
    [InlineData("false", 402)]
    [InlineData(null, 402)]
    public async Task TestMode_AllowsMoreWordsWithoutChangingEntitlement(string? testMode, int expectedStatus)
    {
        using var testDb = TestDb.Create();
        AddDevice(testDb.Db, "key-1", "device-1");
        for (var i = 0; i < 10; i++)
            testDb.Db.DeviceWords.Add(new DeviceWordEntity { DeviceUuid = "device-1", NormalizedWord = $"word-{i}", NativeLanguage = "en", LearningLanguage = "es", IsActive = true });
        await testDb.Db.SaveChangesAsync();
        var filter = CreateFilter(testDb.Db, new AiProtectionOptions { Enabled = true, RequireAssertion = false }, testMode: testMode);
        var context = CreateContext(testDb.Db, """{"word":"eleventh","native_language":"en","learning_language":"es"}""", DeviceToken("key-1", "free"));
        var called = false;
        await filter.OnResourceExecutionAsync(context, () => { called = true; return Executed(context, new OkResult()); });
        Assert.Equal(expectedStatus == 0, called);
        Assert.Equal(expectedStatus, (context.Result as ObjectResult)?.StatusCode ?? 0);
        Assert.Empty(testDb.Db.Subscriptions);
    }

    [Theory]
    [InlineData("true", 0)]
    [InlineData("false", 429)]
    public async Task TestMode_SkipsExhaustedAiQuota(string testMode, int expectedStatus)
    {
        using var testDb = TestDb.Create();
        AddDevice(testDb.Db, "key-1", "device-1");
        var filter = CreateFilter(testDb.Db, new AiProtectionOptions { Enabled = true, RequireAssertion = false, FreeDailyQuota = 0 }, testMode: testMode);
        var context = CreateContext(testDb.Db, """{"word":"hola"}""", DeviceToken("key-1", "free"));
        await filter.OnResourceExecutionAsync(context, () => Executed(context, new OkResult()));
        Assert.Equal(expectedStatus, (context.Result as ObjectResult)?.StatusCode ?? 0);
        Assert.Empty(testDb.Db.AiUsage);
    }

    [Theory]
    [InlineData(false, 401)]
    [InlineData(true, 403)]
    public async Task TestMode_StillRequiresDeviceAuthenticationAndAssertion(bool token, int expectedStatus)
    {
        using var testDb = TestDb.Create();
        AddDevice(testDb.Db, "key-1", "device-1");
        var filter = CreateFilter(testDb.Db, new AiProtectionOptions { Enabled = true, RequireAssertion = true }, testMode: "true");
        var context = CreateContext(testDb.Db, "{}", token ? DeviceToken("key-1", "free") : null);
        await filter.OnResourceExecutionAsync(context, () => throw new InvalidOperationException("Unverified request passed"));
        Assert.Equal(expectedStatus, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Theory]
    [InlineData("false", false, 401)]
    [InlineData("false", true, 403)]
    [InlineData("true", false, 401)]
    [InlineData("true", true, 403)]
    [InlineData(null, false, 401)]
    public async Task DisabledProtection_StillRequiresDeviceAuthenticationAndAssertion(string? testMode, bool token, int expectedStatus)
    {
        using var testDb = TestDb.Create();
        AddDevice(testDb.Db, "key-1", "device-1");
        var filter = CreateFilter(testDb.Db, new AiProtectionOptions { Enabled = false, RequireAssertion = true }, testMode: testMode);
        var context = CreateContext(testDb.Db, "{}", token ? DeviceToken("key-1", "free") : null);
        var called = false;
        await filter.OnResourceExecutionAsync(context, () => { called = true; return Executed(context, new OkResult()); });
        Assert.False(called);
        Assert.Equal(expectedStatus, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public async Task EnabledProtection_Returns401WhenDeviceTokenMissing()
    {
        using var testDb = TestDb.Create();
        var filter = CreateFilter(testDb.Db, new AiProtectionOptions { Enabled = true, RequireAssertion = false });
        var context = CreateContext(testDb.Db, """{"word":"hola"}""");

        await filter.OnResourceExecutionAsync(context, () => Executed(context, new OkResult()));

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
    }

    [Theory]
    [InlineData("production", "false", true, 402)]
    [InlineData("development", "false", true, 402)]
    [InlineData("development", null, true, 402)]
    [InlineData("development", "false", false, 402)]
    [InlineData("production", "true", true, 0)]
    [InlineData("development", "true", true, 0)]
    [InlineData("development", "true", false, 0)]
    public async Task ExpiredDatabaseEntitlement_OnlyTestModeCanBypass(string environment, string? testMode, bool protectionEnabled, int expectedStatus)
    {
        using var testDb = TestDb.Create();
        AddDevice(testDb.Db, "key-1", "device-1");
        (await testDb.Db.Devices.SingleAsync()).Environment = environment;
        testDb.Db.Subscriptions.Add(new SubscriptionEntity
        {
            OriginalTransactionId = "otid-1",
            DeviceUuid = "device-1",
            ProductId = "com.flashcardai.owlai.premium.monthly",
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            WasEverPaid = true,
            IsTrial = false,
            AutoRenew = false,
            Environment = "Sandbox"
        });
        await testDb.Db.SaveChangesAsync();
        var filter = CreateFilter(testDb.Db, new AiProtectionOptions { Enabled = protectionEnabled, RequireAssertion = false }, testMode: testMode);
        var context = CreateContext(testDb.Db, """{"word":"hola"}""", DeviceToken("key-1", EntitlementService.Status.Premium));

        await filter.OnResourceExecutionAsync(context, () => Executed(context, new OkResult()));

        Assert.Equal(expectedStatus, (context.Result as ObjectResult)?.StatusCode ?? 0);
        Assert.Equal(EntitlementService.Status.ExpiredPaid,
            (await new DeviceContextService(testDb.Db, new EntitlementService(testDb.Db, TimeProvider.System)).ResolveAsync("key-1"))!.Entitlement.Status);
    }

    [Fact]
    public async Task FreeSuccessfulRequest_RewindsBodyForAction_ReservesWord_AndIncrementsUsage()
    {
        using var testDb = TestDb.Create();
        AddDevice(testDb.Db, "key-1", "device-1");
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-13T12:00:00Z"));
        var filter = CreateFilter(testDb.Db, new AiProtectionOptions { Enabled = true, RequireAssertion = false, FreeDailyQuota = 40 }, time);
        var context = CreateContext(
            testDb.Db,
            """{"word":"Hola","native_language":"en","learning_language":"es"}""",
            DeviceToken("key-1", EntitlementService.Status.Free));
        string? bodySeenByAction = null;

        await filter.OnResourceExecutionAsync(context, async () =>
        {
            using var reader = new StreamReader(context.HttpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
            bodySeenByAction = await reader.ReadToEndAsync();
            return new ResourceExecutedContext(ToActionContext(context), context.Filters)
            {
                Result = new OkObjectResult(new { ok = true })
            };
        });

        Assert.Null(context.Result);
        Assert.Contains("\"Hola\"", bodySeenByAction);
        Assert.Equal(1, await testDb.Db.DeviceWords.CountAsync());
        Assert.Equal(1, await new AiUsageService(testDb.Db, time).GetTodayCountAsync("key-1"));
    }

    [Theory]
    [InlineData("production")]
    [InlineData("development")]
    public async Task FreeRequest_Returns429WhenDailyQuotaReached(string environment)
    {
        using var testDb = TestDb.Create();
        AddDevice(testDb.Db, "key-1", "device-1");
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-13T12:00:00Z"));
        (await testDb.Db.Devices.SingleAsync()).Environment = environment;
        await testDb.Db.SaveChangesAsync();
        var usage = new AiUsageService(testDb.Db, time);
        await usage.IncrementAsync("key-1");
        var filter = CreateFilter(testDb.Db, new AiProtectionOptions { Enabled = true, RequireAssertion = false, FreeDailyQuota = 1 }, time);
        var context = CreateContext(testDb.Db, """{"word":"hola"}""", DeviceToken("key-1", EntitlementService.Status.Free));

        await filter.OnResourceExecutionAsync(context, () => Executed(context, new OkResult()));

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
    }

    [Theory]
    [InlineData("development", "false", true, 402)]
    [InlineData("production", "false", true, 402)]
    [InlineData("development", null, true, 402)]
    [InlineData("development", "false", false, 402)]
    [InlineData("development", "true", true, 0)]
    [InlineData("production", "true", true, 0)]
    [InlineData("development", "true", false, 0)]
    public async Task EleventhAiWord_OnlyTestModeCanBypass(string environment, string? testMode, bool protectionEnabled, int expectedStatus)
    {
        using var testDb = TestDb.Create();
        AddDevice(testDb.Db, "key-1", "device-1");
        (await testDb.Db.Devices.SingleAsync()).Environment = environment;
        for (var i = 0; i < 10; i++)
            testDb.Db.DeviceWords.Add(new DeviceWordEntity
            {
                DeviceUuid = "device-1", NormalizedWord = $"word-{i}",
                NativeLanguage = "en", LearningLanguage = "es", IsActive = true
            });
        await testDb.Db.SaveChangesAsync();
        var filter = CreateFilter(testDb.Db, new AiProtectionOptions { Enabled = protectionEnabled, RequireAssertion = false }, testMode: testMode);
        var context = CreateContext(testDb.Db,
            """{"word":"eleventh","native_language":"en","learning_language":"es"}""",
            DeviceToken("key-1", EntitlementService.Status.Free));
        var called = false;

        await filter.OnResourceExecutionAsync(context, () =>
        {
            called = true;
            return Executed(context, new OkResult());
        });

        Assert.Equal(expectedStatus == 0, called);
        Assert.Equal(expectedStatus, (context.Result as ObjectResult)?.StatusCode ?? 0);
        Assert.Empty(testDb.Db.Subscriptions);
    }

    [Theory]
    [InlineData(false, 401)]
    [InlineData(true, 403)]
    public async Task DevelopmentDevice_StillRequiresTokenAndAssertion(bool includeToken, int expectedStatus)
    {
        using var testDb = TestDb.Create();
        AddDevice(testDb.Db, "key-1", "device-1");
        (await testDb.Db.Devices.SingleAsync()).Environment = "development";
        await testDb.Db.SaveChangesAsync();
        var filter = CreateFilter(testDb.Db, new AiProtectionOptions { Enabled = true, RequireAssertion = true });
        var context = CreateContext(testDb.Db, """{"word":"hola"}""",
            includeToken ? DeviceToken("key-1", EntitlementService.Status.Free) : null);
        await filter.OnResourceExecutionAsync(context, () => throw new InvalidOperationException("Unauthorized action ran"));
        Assert.Equal(expectedStatus, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 403)]
    public async Task DevelopmentDevice_OnlyVerifiedAssertionReachesAction(bool assertionValid, int expectedStatus)
    {
        using var testDb = TestDb.Create();
        testDb.Db.Devices.Add(new DeviceEntity
        {
            KeyId = "key-1", DeviceUuid = "device-1", Environment = "development", PublicKey = [1, 2, 3]
        });
        await testDb.Db.SaveChangesAsync();
        var verifier = new FakeAppAttestVerifier
        {
            AssertionResult = assertionValid
                ? AppAttestVerifier.AssertionResult.Success(1)
                : AppAttestVerifier.AssertionResult.Fail("invalid signature")
        };
        var filter = CreateFilter(testDb.Db,
            new AiProtectionOptions { Enabled = true, RequireAssertion = true }, verifier: verifier);
        var context = CreateContext(testDb.Db, """{"word":"hola"}""",
            DeviceToken("key-1", EntitlementService.Status.Free));
        var challenge = await new ChallengeService(testDb.Db, TimeProvider.System)
            .IssueAssertionAsync("key-1", context.HttpContext.Request.Path.Value!);
        context.HttpContext.Request.Headers["X-App-Attest-Key-Id"] = "key-1";
        context.HttpContext.Request.Headers["X-App-Attest-Challenge-Id"] = challenge.ChallengeId;
        context.HttpContext.Request.Headers["X-App-Attest-Assertion"] = Convert.ToBase64String([1, 2, 3]);
        var called = false;

        await filter.OnResourceExecutionAsync(context, () =>
        {
            called = true;
            return Executed(context, new OkResult());
        });

        Assert.Equal(assertionValid, called);
        Assert.Equal(expectedStatus, (context.Result as ObjectResult)?.StatusCode ?? 0);
        Assert.Equal(assertionValid ? 1 : 0, (await testDb.Db.Devices.SingleAsync()).SignCount);
    }

    [Theory]
    [InlineData(false, 401)]
    [InlineData(true, 403)]
    public async Task ReviewTranslation_RequiresDeviceTokenAndAssertion(bool token, int expectedStatus)
    {
        using var testDb = TestDb.Create();
        AddDevice(testDb.Db, "key-1", "device-1");
        var filter = CreateFilter(testDb.Db, new AiProtectionOptions { RequireAssertion = true }, testMode: "true");
        var context = CreateContext(testDb.Db, "{}", token ? DeviceToken("key-1", "free") : null);
        context.HttpContext.Request.Path = "/owlai/ai/review-translation";
        await filter.OnResourceExecutionAsync(context, () => throw new InvalidOperationException("Unverified request passed"));
        Assert.Equal(expectedStatus, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewTranslation_DoesNotReserveMissingOrInactiveWord(bool inactive)
    {
        using var testDb = TestDb.Create();
        AddDevice(testDb.Db, "key-1", "device-1");
        if (inactive)
            testDb.Db.DeviceWords.Add(new DeviceWordEntity { DeviceUuid = "device-1", NormalizedWord = "hola", NativeLanguage = "en", LearningLanguage = "es", IsActive = false });
        await testDb.Db.SaveChangesAsync();
        var filter = CreateFilter(testDb.Db, new AiProtectionOptions { RequireAssertion = false }, testMode: "false");
        var context = CreateContext(testDb.Db,
            """{"word":"hola","native_language":"en","learning_language":"es","secondary_language":"fr"}""", DeviceToken("key-1", "free"));
        context.HttpContext.Request.Path = "/owlai/ai/review-translation";
        var called = false;
        await filter.OnResourceExecutionAsync(context, () => { called = true; return Executed(context, new OkResult()); });
        Assert.False(called);
        Assert.Equal(402, Assert.IsType<ObjectResult>(context.Result).StatusCode);
        Assert.Equal(inactive ? 1 : 0, await testDb.Db.DeviceWords.CountAsync());
        Assert.Empty(testDb.Db.AiUsage);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    public async Task ReviewTranslation_RejectsNonObjectBodyWithoutReservations(string body)
    {
        using var testDb = TestDb.Create();
        AddDevice(testDb.Db, "key-1", "device-1");
        var filter = CreateFilter(testDb.Db, new AiProtectionOptions { RequireAssertion = false }, testMode: "false");
        var context = CreateContext(testDb.Db, body, DeviceToken("key-1", "free"));
        context.HttpContext.Request.Path = "/owlai/ai/review-translation";
        await filter.OnResourceExecutionAsync(context, () => throw new InvalidOperationException("Invalid request passed"));
        Assert.Equal(400, Assert.IsType<ObjectResult>(context.Result).StatusCode);
        Assert.Empty(testDb.Db.DeviceWords);
    }

    private static AiProtectionFilter CreateFilter(
        AppDbContext db,
        AiProtectionOptions options,
        TimeProvider? timeProvider = null,
        IAppAttestVerifier? verifier = null,
        string? testMode = null)
    {
        timeProvider ??= TimeProvider.System;
        return new AiProtectionFilter(
            Options.Create(options),
            TestConfig.Create(new Dictionary<string, string?> { ["TestMode:Enabled"] = testMode }),
            verifier ?? new FakeAppAttestVerifier { IsDevelopmentBypassEnabled = true },
            new ChallengeService(db, timeProvider),
            new DeviceContextService(db, new EntitlementService(db, timeProvider)),
            new DeviceWordService(db, timeProvider),
            new AiUsageService(db, timeProvider),
            timeProvider,
            NullLogger<AiProtectionFilter>.Instance);
    }

    private static ResourceExecutingContext CreateContext(AppDbContext db, string body, string? bearerToken = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(db)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Method = "POST";
        http.Request.Path = "/owlai/ai/word-detail";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        http.Request.ContentLength = Encoding.UTF8.GetByteCount(body);
        if (bearerToken is not null)
            http.Request.Headers.Authorization = $"Bearer {bearerToken}";

        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ResourceExecutingContext(
            actionContext,
            [],
            new List<IValueProviderFactory>());
    }

    private static Task<ResourceExecutedContext> Executed(ResourceExecutingContext context, IActionResult result) =>
        Task.FromResult(new ResourceExecutedContext(ToActionContext(context), context.Filters)
        {
            Result = result
        });

    private static ActionContext ToActionContext(ResourceExecutingContext context) =>
        new(context.HttpContext, context.RouteData, context.ActionDescriptor);

    private static string DeviceToken(string keyId, string entitlement) =>
        new JwtTokenService(TestConfig.Create(), TimeProvider.System)
            .CreateDeviceToken(keyId, entitlement)
            .Token;

    private static void AddDevice(AppDbContext db, string keyId, string deviceUuid)
    {
        db.Devices.Add(new DeviceEntity { KeyId = keyId, DeviceUuid = deviceUuid });
        db.SaveChanges();
    }
}
