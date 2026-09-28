using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Xunit;

namespace Mavrylo.Tests;

public class ChallengeAndUsageTests
{
    [Fact]
    public async Task Challenge_IsSingleUse_PathAndKeyBound_AndExpires()
    {
        using var testDb = TestDb.Create();
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-13T12:00:00Z"));
        var service = new ChallengeService(testDb.Db, time);

        var issued = await service.IssueAssertionAsync("key", "/path");

        Assert.Null(await service.TryConsumeAsync(issued.ChallengeId, ChallengeService.ChallengeKind.Assertion, "other", "/path"));
        Assert.Null(await service.TryConsumeAsync(issued.ChallengeId, ChallengeService.ChallengeKind.Assertion, "key", "/other"));
        Assert.Equal(issued.Nonce, await service.TryConsumeAsync(issued.ChallengeId, ChallengeService.ChallengeKind.Assertion, "key", "/path"));
        Assert.Null(await service.TryConsumeAsync(issued.ChallengeId, ChallengeService.ChallengeKind.Assertion, "key", "/path"));

        var expiring = await service.IssueBootstrapAsync();
        time.Advance(ChallengeService.Ttl.Add(TimeSpan.FromSeconds(1)));
        Assert.Null(await service.TryConsumeAsync(expiring.ChallengeId, ChallengeService.ChallengeKind.Bootstrap));
    }

    [Fact]
    public async Task AiUsage_CountsTodayAndHonorsQuota()
    {
        using var testDb = TestDb.Create();
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-13T12:00:00Z"));
        var service = new AiUsageService(testDb.Db, time);

        Assert.Equal(0, await service.GetTodayCountAsync("key"));
        Assert.False(await service.IsOverQuotaAsync("key", 2));

        await service.IncrementAsync("key");
        await service.IncrementAsync("key");

        Assert.Equal(2, await service.GetTodayCountAsync("key"));
        Assert.True(await service.IsOverQuotaAsync("key", 2));

        time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, await service.GetTodayCountAsync("key"));
    }
}
