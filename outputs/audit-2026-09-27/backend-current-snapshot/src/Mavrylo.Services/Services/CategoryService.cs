using Mavrylo.Data;
using Mavrylo.Dtos;
using Mavrylo.Mapping;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

/// <summary>Category CRUD. Logic moved verbatim from CategoriesController; the controller keeps
/// the claim resolution, the "name required" guard, and the HTTP result construction.</summary>
public sealed class CategoryService(AppDbContext db)
{
    public async Task<List<CategoryDto>> ListAsync(string userId, string? sort, int limit, CancellationToken ct)
    {
        var q = db.Categories.AsNoTracking().Where(c => c.UserId == userId && !c.IsDeleted);
        q = sort == "order" ? q.OrderBy(c => c.Order) : q.OrderBy(c => c.CreatedAt);
        var entities = await q.Take(Math.Clamp(limit, 1, 200)).ToListAsync(ct);
        return entities.ConvertAll(EntityMappers.ToDto);
    }

    public async Task<CategoryDto> CreateAsync(string userId, CategoryUpsert body, CancellationToken ct)
    {
        var c = new CategoryEntity { UserId = userId, Name = body.Name!.Trim() };
        EntityMappers.ApplyUpsert(c, body);
        db.Categories.Add(c);
        await db.SaveChangesAsync(ct);
        return EntityMappers.ToDto(c);
    }

    public async Task<CategoryDto?> PatchAsync(string userId, string id, CategoryUpsert body, CancellationToken ct)
    {
        var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (c == null) return null;
        EntityMappers.ApplyUpsert(c, body);
        await db.SaveChangesAsync(ct);
        return EntityMappers.ToDto(c);
    }

    public async Task<bool> DeleteAsync(string userId, string id, CancellationToken ct)
    {
        var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (c == null) return false;
        c.IsDeleted = true;
        c.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
