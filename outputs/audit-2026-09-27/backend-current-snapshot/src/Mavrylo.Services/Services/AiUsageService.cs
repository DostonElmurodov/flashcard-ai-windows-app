using System.Data;
using Mavrylo.Data;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

/// <summary>
/// Free-tier AI cost guard: counts successful AI calls per device (keyId) per UTC day. This is
/// abuse/cost protection only — the product 10-word limit is enforced on-device. trial/premium/
/// grace devices are unlimited; only "free" is throttled (caller decides based on entitlement).
/// </summary>
public sealed class AiUsageService(AppDbContext db, TimeProvider timeProvider, IConfiguration? configuration = null)
{
    private string Today() => timeProvider.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd");

    /// <summary>Current count for a device today (0 if none).</summary>
    public async Task<int> GetTodayCountAsync(string keyId, CancellationToken ct = default)
    {
        var date = Today();
        return await db.AiUsage
            .Where(u => u.KeyId == keyId && u.Date == date)
            .Select(u => u.Count)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Returns true if the device is at/over its daily quota.</summary>
    public async Task<bool> IsOverQuotaAsync(string keyId, int quota, CancellationToken ct = default)
        => !TestModePolicy.IsEnabled(configuration) && await GetTodayCountAsync(keyId, ct) >= quota;

    /// <summary>
    /// Atomically reserve one free-tier AI call for today. Returns false when the quota is already
    /// exhausted. Reservations happen before the provider call to avoid concurrent overspend.
    /// </summary>
    public async Task<bool> TryConsumeAsync(string keyId, int quota, CancellationToken ct = default)
    {
        if (TestModePolicy.IsEnabled(configuration)) return true;
        if (quota <= 0)
            return false;

        var date = Today();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var row = await db.AiUsage.FirstOrDefaultAsync(u => u.KeyId == keyId && u.Date == date, ct);
            if (row is null)
            {
                db.AiUsage.Add(new AiUsageEntity { KeyId = keyId, Date = date, Count = 1 });
            }
            else
            {
                if (row.Count >= quota)
                    return false;
                row.Count += 1;
            }

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return true;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
            }
        }

        return false;
    }

    /// <summary>Increment today's counter for a device (creates the row if needed).</summary>
    public async Task IncrementAsync(string keyId, CancellationToken ct = default)
    {
        var date = Today();
        var row = await db.AiUsage.FirstOrDefaultAsync(u => u.KeyId == keyId && u.Date == date, ct);
        if (row is null)
        {
            db.AiUsage.Add(new AiUsageEntity { KeyId = keyId, Date = date, Count = 1 });
        }
        else
        {
            row.Count += 1;
        }
        await db.SaveChangesAsync(ct);
    }
}
