using Mavrylo.Data;
using Mavrylo.Dtos;
using Mavrylo.Mapping;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

/// <summary>User word CRUD. Logic moved verbatim from WordsController; the controller keeps the
/// claim resolution, the "word required" guard, and the HTTP result construction.</summary>
public sealed class WordService(AppDbContext db)
{
    public async Task<List<WordDto>> ListAsync(string userId, string? sort, int limit, CancellationToken ct)
    {
        var q = db.Words.AsNoTracking().Where(w => w.UserId == userId && !w.IsDeleted);
        q = sort == "-created_date" ? q.OrderByDescending(w => w.CreatedAt) : q.OrderBy(w => w.CreatedAt);
        var entities = await q.Take(Math.Clamp(limit, 1, 500)).ToListAsync(ct);
        return entities.ConvertAll(EntityMappers.ToDto);
    }

    public async Task<WordDto> CreateAsync(string userId, WordUpsert body, CancellationToken ct)
    {
        var w = new WordEntity { UserId = userId, Word = body.Word!.Trim() };
        EntityMappers.ApplyUpsert(w, body);
        db.Words.Add(w);
        await db.SaveChangesAsync(ct);
        return EntityMappers.ToDto(w);
    }

    public async Task<WordDto?> PatchAsync(string userId, string id, WordUpsert body, CancellationToken ct)
    {
        var w = await db.Words.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (w == null) return null;
        EntityMappers.ApplyUpsert(w, body);
        await db.SaveChangesAsync(ct);
        return EntityMappers.ToDto(w);
    }

    public async Task<bool> DeleteAsync(string userId, string id, CancellationToken ct)
    {
        var w = await db.Words.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (w == null) return false;
        w.IsDeleted = true;
        w.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
