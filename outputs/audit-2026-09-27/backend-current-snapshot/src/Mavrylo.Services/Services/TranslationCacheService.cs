using Mavrylo.Data;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mavrylo.Services;

public class TranslationCacheService(AppDbContext db, ILogger<TranslationCacheService> log)
{
    public async Task<string?> TryGetAsync(
        string normalizedWord,
        string nativeLanguage,
        string learningLanguage,
        string cacheKind,
        CancellationToken ct = default)
    {
        var pv = TranslationCacheEntity.CurrentPromptVersion;
        var row = await db.TranslationCache.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.NormalizedKey == normalizedWord
                     && x.NativeLanguage == nativeLanguage
                     && x.LearningLanguage == learningLanguage
                     && x.CacheKind == cacheKind
                     && x.PromptVersion == pv,
                ct);
        if (row == null) return null;

        var now = DateTime.UtcNow;
        try
        {
            await db.TranslationCache.Where(x => x.Id == row.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastHitAt, now), ct);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "translation cache last_hit update skipped");
        }

        TranslationCacheMetrics.RecordHit(cacheKind);
        return row.ResponseJson;
    }

    public async Task SaveAsync(
        string normalizedWord,
        string nativeLanguage,
        string learningLanguage,
        string cacheKind,
        string responseJson,
        string? audioStorageKey = null,
        string? audioContentType = null,
        string? audioUrl = null,
        CancellationToken ct = default)
    {
        var pv = TranslationCacheEntity.CurrentPromptVersion;
        var existing = await db.TranslationCache.FirstOrDefaultAsync(
            x => x.NormalizedKey == normalizedWord
                 && x.NativeLanguage == nativeLanguage
                 && x.LearningLanguage == learningLanguage
                 && x.CacheKind == cacheKind
                 && x.PromptVersion == pv,
            ct);
        if (existing != null)
        {
            ApplyPayload(existing, responseJson, audioStorageKey, audioContentType, audioUrl);
            await db.SaveChangesAsync(ct);
            return;
        }

        db.TranslationCache.Add(new TranslationCacheEntity
        {
            NormalizedKey = normalizedWord,
            NativeLanguage = nativeLanguage,
            LearningLanguage = learningLanguage,
            CacheKind = cacheKind,
            PromptVersion = pv,
            ResponseJson = responseJson,
            AudioStorageKey = audioStorageKey,
            AudioContentType = audioContentType,
            AudioUrl = audioUrl
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            foreach (var entry in db.ChangeTracker.Entries<TranslationCacheEntity>()
                         .Where(e => e.State == EntityState.Added)
                         .ToList())
                entry.State = EntityState.Detached;

            var row = await db.TranslationCache.FirstOrDefaultAsync(
                x => x.NormalizedKey == normalizedWord
                     && x.NativeLanguage == nativeLanguage
                     && x.LearningLanguage == learningLanguage
                     && x.CacheKind == cacheKind
                     && x.PromptVersion == pv,
                ct);
            if (row == null)
            {
                log.LogWarning(ex, "translation cache unique violation but row not found on retry");
                throw;
            }

            ApplyPayload(row, responseJson, audioStorageKey, audioContentType, audioUrl);
            await db.SaveChangesAsync(ct);
        }
    }

    private static void ApplyPayload(
        TranslationCacheEntity row,
        string responseJson,
        string? audioStorageKey,
        string? audioContentType,
        string? audioUrl)
    {
        row.ResponseJson = responseJson;
        row.AudioStorageKey = audioStorageKey ?? row.AudioStorageKey;
        row.AudioContentType = audioContentType ?? row.AudioContentType;
        row.AudioUrl = audioUrl ?? row.AudioUrl;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
        {
            // SQLSTATE 23505 = unique_violation (PostgreSQL)
            if (inner is PostgresException pg && pg.SqlState == "23505")
                return true;
        }

        return false;
    }
}
