using Mavrylo.Data;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Tests.TestSupport;

public static class DbReset
{
    public static async Task ClearAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.AiUsage.ExecuteDeleteAsync(ct);
        await db.Challenges.ExecuteDeleteAsync(ct);
        await db.Devices.ExecuteDeleteAsync(ct);
        await db.DeviceWords.ExecuteDeleteAsync(ct);
        await db.Subscriptions.ExecuteDeleteAsync(ct);
        await db.TranslationCache.ExecuteDeleteAsync(ct);
        await db.Words.ExecuteDeleteAsync(ct);
        await db.Categories.ExecuteDeleteAsync(ct);
        await db.UserSettings.ExecuteDeleteAsync(ct);
        await db.Users.ExecuteDeleteAsync(ct);
    }
}
