using Mavrylo.Data;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

public sealed class AccountAiUsageService(AppDbContext db, IConfiguration config, TimeProvider clock)
{
    public async Task<bool> TryConsumeAsync(string account, CancellationToken ct)
    {
        if (TestModePolicy.IsEnabled(config)) return true;
        var daily = config.GetValue<int>("AccountAi:DailyQuota");
        var minute = config.GetValue<int>("AccountAi:RequestsPerMinute");
        if (daily <= 0 || minute <= 0) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var key = "account:" + account;
        // Database counters, shared across device/account routes and all API replicas.
        if (!await Consume(key, now.ToString("yyyy-MM-dd"), daily, ct)
            || !await Consume(key, now.ToString("yyyy-MM-dd'T'HH:mm"), minute, ct)) return false;
        await tx.CommitAsync(ct);
        return true;
    }
    private async Task<bool> Consume(string key, string bucket, int limit, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ai_usage ("Id", "KeyId", "Date", "Count") VALUES ({id}, {key}, {bucket}, 1)
            ON CONFLICT ("KeyId", "Date") DO UPDATE SET "Count" = ai_usage."Count" + 1
            WHERE ai_usage."Count" < {limit}
            """, ct) == 1;
    }
}
