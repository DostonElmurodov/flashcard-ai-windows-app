using Mavrylo.Data;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Mavrylo.Tests;

/// <summary>
/// Concurrency safety for the atomic guards added in the security pass. These run against a real
/// Postgres container because SQLite's in-memory single connection cannot exercise true concurrent
/// transactions or serializable isolation. The property under test is "never exceeds the limit"
/// (no bypass), which is the security-relevant invariant.
/// </summary>
public class ConcurrencyTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    private static readonly ManualTimeProvider Time = new(DateTimeOffset.Parse("2026-06-13T12:00:00Z"));

    [Fact]
    public async Task ConcurrentBootstrapChallengeConsume_HasExactlyOneWinner()
    {
        await EnsureSchemaAsync();

        string challengeId;
        await using (var seed = NewContext())
        {
            var issued = await new ChallengeService(seed, Time).IssueBootstrapAsync();
            challengeId = issued.ChallengeId;
        }

        var results = await RunConcurrently(16, async () =>
        {
            await using var db = NewContext();
            var nonce = await new ChallengeService(db, Time).TryConsumeAsync(
                challengeId, ChallengeService.ChallengeKind.Bootstrap);
            return nonce is not null;
        });

        Assert.Equal(1, results.Count(won => won));
    }

    [Fact]
    public async Task ConcurrentAiUsageConsume_NeverExceedsQuota()
    {
        await EnsureSchemaAsync();
        const int quota = 5;
        var keyId = $"ai-usage-{Guid.NewGuid():N}";

        var results = await RunConcurrently(20, async () =>
        {
            await using var db = NewContext();
            return await SafeAsync(() => new AiUsageService(db, Time).TryConsumeAsync(keyId, quota));
        });

        var granted = results.Count(ok => ok);
        await using var verify = NewContext();
        var stored = await new AiUsageService(verify, Time).GetTodayCountAsync(keyId);

        Assert.True(granted <= quota, $"granted {granted} exceeded quota {quota}");
        Assert.True(stored <= quota, $"stored {stored} exceeded quota {quota}");
        Assert.Equal(granted, stored);
        Assert.True(granted >= 1, "expected at least one successful reservation");
    }

    [Fact]
    public async Task ConcurrentFreeWordReservation_NeverExceedsFreeLimit()
    {
        await EnsureSchemaAsync();
        var keyId = $"word-key-{Guid.NewGuid():N}";
        var deviceUuid = $"device-{Guid.NewGuid():N}";
        await using (var seed = NewContext())
        {
            seed.Devices.Add(new DeviceEntity { KeyId = keyId, DeviceUuid = deviceUuid });
            await seed.SaveChangesAsync();
        }

        await RunConcurrently(20, async i =>
        {
            await using var db = NewContext();
            return await SafeAsync(async () =>
            {
                var result = await new DeviceWordService(db, Time).TryReserveAiSlotAsync(
                    keyId, EntitlementService.Status.Free, $"word-{i}", "en", "es", CancellationToken.None);
                return result.Allowed;
            });
        });

        await using var verify = NewContext();
        var active = await new DeviceWordService(verify, Time).CountActiveAsync(deviceUuid, CancellationToken.None);

        Assert.True(active >= 1, "expected at least one reserved word");
        Assert.True(active <= DeviceWordService.FreeLimit, $"active {active} exceeded free limit {DeviceWordService.FreeLimit}");
    }

    // Serializable transactions surface conflicts as serialization failures; a caller that loses the
    // race must never overspend, so treating a conflict as "not granted" is the safe outcome.
    private static async Task<bool> SafeAsync(Func<Task<bool>> action)
    {
        try { return await action(); }
        catch (DbUpdateException) { return false; }
        catch (PostgresException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private async Task EnsureSchemaAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private static Task<bool[]> RunConcurrently(int count, Func<Task<bool>> action)
        => RunConcurrently(count, _ => action());

    private static async Task<bool[]> RunConcurrently(int count, Func<int, Task<bool>> action)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, count)
            .Select(i => Task.Run(async () =>
            {
                await gate.Task;
                return await action(i);
            }))
            .ToArray();
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }
}
