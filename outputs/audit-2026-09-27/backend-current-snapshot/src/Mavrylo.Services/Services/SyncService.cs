using Mavrylo.Data;
using Mavrylo.Dtos;
using Mavrylo.Mapping;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

/// <summary>Pull/push sync. Logic moved verbatim from SyncController (last-write-wins by the
/// client payload; conflicts is always empty, preserved as-is).</summary>
public sealed class SyncService(AppDbContext db)
{
    public async Task<SyncPullResponse> ChangesAsync(string userId, DateTime? sinceUtc, CancellationToken ct)
    {
        var since = sinceUtc ?? DateTime.MinValue;
        var wordRows = await db.Words.AsNoTracking()
            .Where(w => w.UserId == userId && w.UpdatedAt > since)
            .ToListAsync(ct);
        var words = wordRows.ConvertAll(EntityMappers.ToDto);
        var catRows = await db.Categories.AsNoTracking()
            .Where(c => c.UserId == userId && c.UpdatedAt > since)
            .ToListAsync(ct);
        var cats = catRows.ConvertAll(EntityMappers.ToDto);
        var setRows = await db.UserSettings.AsNoTracking()
            .Where(s => s.UserId == userId && s.UpdatedAt > since)
            .ToListAsync(ct);
        var settings = setRows.ConvertAll(EntityMappers.ToDto);
        return new SyncPullResponse(DateTime.UtcNow, words, cats, settings);
    }

    /// <summary>Last-write-wins per entity by UpdatedAt from client payload.</summary>
    public async Task<SyncPushResponse> PushAsync(string userId, SyncPushRequest req, CancellationToken ct)
    {
        var conflicts = new List<object>();
        if (req.Words != null)
        {
            foreach (var item in req.Words)
            {
                if (string.IsNullOrEmpty(item.Id)) continue;
                var existing = await db.Words.FirstOrDefaultAsync(w => w.Id == item.Id && w.UserId == userId, ct);
                if (item.IsDeleted == true)
                {
                    if (existing != null)
                    {
                        existing.IsDeleted = true;
                        existing.UpdatedAt = DateTime.UtcNow;
                    }
                    continue;
                }
                if (item.Data == null) continue;
                if (existing == null)
                {
                    var w = new WordEntity { Id = item.Id, UserId = userId, Word = item.Data.Word ?? "" };
                    EntityMappers.ApplyUpsert(w, item.Data);
                    db.Words.Add(w);
                }
                else
                {
                    EntityMappers.ApplyUpsert(existing, item.Data);
                }
            }
        }
        if (req.Categories != null)
        {
            foreach (var item in req.Categories)
            {
                if (string.IsNullOrEmpty(item.Id)) continue;
                var existing = await db.Categories.FirstOrDefaultAsync(c => c.Id == item.Id && c.UserId == userId, ct);
                if (item.IsDeleted == true)
                {
                    if (existing != null)
                    {
                        existing.IsDeleted = true;
                        existing.UpdatedAt = DateTime.UtcNow;
                    }
                    continue;
                }
                if (item.Data == null) continue;
                if (existing == null)
                {
                    var c = new CategoryEntity { Id = item.Id, UserId = userId, Name = item.Data.Name ?? "" };
                    EntityMappers.ApplyUpsert(c, item.Data);
                    db.Categories.Add(c);
                }
                else EntityMappers.ApplyUpsert(existing, item.Data);
            }
        }
        if (req.UserSettings != null)
        {
            foreach (var item in req.UserSettings)
            {
                if (item.Data == null) continue;
                UserSettingsEntity? existing;
                if (!string.IsNullOrEmpty(item.Id))
                    existing = await db.UserSettings.FirstOrDefaultAsync(s => s.Id == item.Id && s.UserId == userId, ct);
                else
                    existing = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);
                if (existing == null)
                {
                    var s = new UserSettingsEntity { UserId = userId };
                    if (!string.IsNullOrEmpty(item.Id)) s.Id = item.Id;
                    EntityMappers.ApplyUpsert(s, item.Data);
                    db.UserSettings.Add(s);
                }
                else EntityMappers.ApplyUpsert(existing, item.Data);
            }
        }
        await db.SaveChangesAsync(ct);
        return new SyncPushResponse(true, conflicts);
    }
}
