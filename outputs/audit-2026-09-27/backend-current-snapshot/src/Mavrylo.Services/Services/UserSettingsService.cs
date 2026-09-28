using Mavrylo.Data;
using Mavrylo.Dtos;
using Mavrylo.Mapping;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

/// <summary>User settings (one row per user). Logic moved verbatim from UserSettingsController.
/// CreateAsync returns null when a row already exists so the controller can emit the exact 409
/// the iOS client keys on to retry GET+PATCH; PatchAsync returns null when not found.</summary>
public sealed class UserSettingsService(AppDbContext db)
{
    public async Task<List<UserSettingsDto>> ListAsync(string userId, CancellationToken ct)
    {
        var rows = await db.UserSettings.AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);
        return rows.ConvertAll(EntityMappers.ToDto);
    }

    public async Task<UserSettingsDto?> CreateAsync(string userId, UserSettingsUpsert body, CancellationToken ct)
    {
        if (await db.UserSettings.AnyAsync(s => s.UserId == userId, ct))
            return null; // already exists -> controller returns 409
        var s = new UserSettingsEntity { UserId = userId };
        EntityMappers.ApplyUpsert(s, body);
        db.UserSettings.Add(s);
        await db.SaveChangesAsync(ct);
        return EntityMappers.ToDto(s);
    }

    public async Task<UserSettingsDto?> PatchAsync(string userId, string id, UserSettingsUpsert body, CancellationToken ct)
    {
        var s = await db.UserSettings.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (s == null) return null;
        EntityMappers.ApplyUpsert(s, body);
        await db.SaveChangesAsync(ct);
        return EntityMappers.ToDto(s);
    }
}
