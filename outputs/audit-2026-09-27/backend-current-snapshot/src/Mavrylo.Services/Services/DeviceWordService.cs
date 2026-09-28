using System.Data;
using System.Text.Json;
using Mavrylo.Data;
using Mavrylo.Dtos;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

public sealed class DeviceWordService(AppDbContext db, TimeProvider timeProvider, IConfiguration? configuration = null)
{
    public const int FreeLimit = 10;

    public async Task<string?> FindDeviceUuidAsync(string keyId, CancellationToken ct)
        => await db.Devices
            .Where(d => d.KeyId == keyId)
            .Select(d => d.DeviceUuid)
            .FirstOrDefaultAsync(ct);

    public async Task<int> CountActiveAsync(string deviceUuid, CancellationToken ct)
        => await db.DeviceWords.CountAsync(w => w.DeviceUuid == deviceUuid && w.IsActive, ct);

    public async Task<DeviceWordMutationResponse> UpsertAsync(string deviceUuid, DeviceWordUpsertRequest request, string entitlement, CancellationToken ct)
    {
        var normalized = Normalize(request.NormalizedWord);
        var native = NormalizeLanguage(request.NativeLanguage);
        var learning = NormalizeLanguage(request.LearningLanguage);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("normalized_word is required");
        if (normalized.Length > SupportedLanguages.MaxWordLength)
            throw new InvalidOperationException("normalized_word is too long");
        if (!string.IsNullOrWhiteSpace(request.DisplayWord) && request.DisplayWord.Trim().Length > SupportedLanguages.MaxWordLength)
            throw new InvalidOperationException("display_word is too long");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existing = await FindExistingAsync(deviceUuid, request.ClientWordId, normalized, native, learning, ct);
        if (existing is null && !TestModePolicy.IsEnabled(configuration)
            && entitlement is not (EntitlementService.Status.Free
                or EntitlementService.Status.Trial or EntitlementService.Status.Premium or EntitlementService.Status.Grace))
            return new DeviceWordMutationResponse(false, await CountActiveAsync(deviceUuid, ct), FreeLimit);

        if (existing is null && IsFreeLimited(entitlement))
        {
            var count = await CountActiveAsync(deviceUuid, ct);
            if (count >= FreeLimit)
                return new DeviceWordMutationResponse(false, count, FreeLimit);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var word = existing ?? new DeviceWordEntity
        {
            DeviceUuid = deviceUuid,
            CreatedAt = now
        };

        word.ClientWordId = string.IsNullOrWhiteSpace(request.ClientWordId) ? word.ClientWordId : request.ClientWordId.Trim();
        word.NormalizedWord = normalized;
        word.DisplayWord = string.IsNullOrWhiteSpace(request.DisplayWord) ? request.NormalizedWord.Trim() : request.DisplayWord.Trim();
        word.NativeLanguage = native;
        word.LearningLanguage = learning;
        word.Translation = TrimToNull(request.Translation);
        word.Pronunciation = TrimToNull(request.Pronunciation);
        word.PartOfSpeech = TrimToNull(request.PartOfSpeech);
        word.DetailJson = TrimToNull(request.DetailJson);
        word.IsActive = true;
        word.UpdatedAt = now;

        if (existing is null)
            db.DeviceWords.Add(word);

        await db.SaveChangesAsync(ct);
        var activeCount = await CountActiveAsync(deviceUuid, ct);
        await tx.CommitAsync(ct);
        return new DeviceWordMutationResponse(true, activeCount, FreeLimit);
    }

    public async Task<DeviceWordMutationResponse> DeleteAsync(string deviceUuid, DeviceWordDeleteRequest request, CancellationToken ct)
    {
        var normalized = Normalize(request.NormalizedWord ?? "");
        var native = NormalizeLanguage(request.NativeLanguage ?? "");
        var learning = NormalizeLanguage(request.LearningLanguage ?? "");
        var word = await FindExistingAsync(deviceUuid, request.ClientWordId, normalized, native, learning, ct);
        if (word is not null)
        {
            db.DeviceWords.Remove(word);
            await db.SaveChangesAsync(ct);
        }

        return new DeviceWordMutationResponse(true, await CountActiveAsync(deviceUuid, ct), FreeLimit);
    }

    public async Task<AiReservationResult> TryReserveAiSlotAsync(
        string keyId,
        string entitlement,
        string? word,
        string? nativeLanguage,
        string? learningLanguage,
        CancellationToken ct)
    {
        if (!IsFreeLimited(entitlement))
            return AiReservationResult.Allow();

        var deviceUuid = await FindDeviceUuidAsync(keyId, ct);
        if (deviceUuid is null)
            return AiReservationResult.Deny("device not registered");

        var normalized = Normalize(word ?? "");
        var native = NormalizeLanguage(nativeLanguage ?? "en");
        var learning = NormalizeLanguage(learningLanguage ?? "en");
        if (normalized.Length > SupportedLanguages.MaxWordLength)
            return AiReservationResult.Deny("word is too long");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var exists = await db.DeviceWords.AnyAsync(w =>
                w.DeviceUuid == deviceUuid
                && w.NormalizedWord == normalized
                && w.NativeLanguage == native
                && w.LearningLanguage == learning
                && w.IsActive, ct);
            if (exists)
            {
                await tx.CommitAsync(ct);
                return AiReservationResult.Allow();
            }
        }

        var count = await CountActiveAsync(deviceUuid, ct);
        if (count >= FreeLimit)
            return AiReservationResult.Deny("free plan word limit reached");

        // Reserve a slot before the expensive AI call, so a custom client cannot bypass the
        // backend limit by calling AI repeatedly and never syncing the resulting word.
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            db.DeviceWords.Add(new DeviceWordEntity
            {
                DeviceUuid = deviceUuid,
                NormalizedWord = normalized,
                DisplayWord = word?.Trim() ?? normalized,
                NativeLanguage = native,
                LearningLanguage = learning,
                IsActive = true,
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
                UpdatedAt = timeProvider.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        return AiReservationResult.Allow();
    }

    public async Task<AiReservationResult> CheckExistingAiWordAsync(
        string keyId, string? word, string? nativeLanguage, string? learningLanguage, CancellationToken ct)
    {
        var normalized = Normalize(word ?? "");
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > SupportedLanguages.MaxWordLength)
            throw new InvalidOperationException("a valid word is required");
        if (string.IsNullOrWhiteSpace(nativeLanguage) || string.IsNullOrWhiteSpace(learningLanguage))
            throw new InvalidOperationException("native and learning languages are required");
        var native = NormalizeLanguage(nativeLanguage);
        var learning = NormalizeLanguage(learningLanguage);
        var deviceUuid = await FindDeviceUuidAsync(keyId, ct);
        var exists = deviceUuid != null && await db.DeviceWords.AsNoTracking().AnyAsync(w =>
            w.DeviceUuid == deviceUuid && w.NormalizedWord == normalized
            && w.NativeLanguage == native && w.LearningLanguage == learning && w.IsActive, ct);
        return exists ? AiReservationResult.Allow() : AiReservationResult.Deny("review requires an existing active word");
    }

    public static (string? Word, string? NativeLanguage, string? LearningLanguage) TryReadWordContext(byte[] body)
    {
        if (body.Length == 0)
            return (null, null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, null, null);
            return (
                ReadString(root, "word"),
                ReadString(root, "native_language") ?? ReadString(root, "nativeLanguage"),
                ReadString(root, "learning_language") ?? ReadString(root, "learningLanguage")
            );
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private async Task<DeviceWordEntity?> FindExistingAsync(
        string deviceUuid,
        string? clientWordId,
        string normalized,
        string native,
        string learning,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(clientWordId))
        {
            var byClientId = await db.DeviceWords.FirstOrDefaultAsync(w =>
                w.DeviceUuid == deviceUuid && w.ClientWordId == clientWordId.Trim(), ct);
            if (byClientId is not null)
                return byClientId;
        }

        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return await db.DeviceWords.FirstOrDefaultAsync(w =>
            w.DeviceUuid == deviceUuid
            && w.NormalizedWord == normalized
            && w.NativeLanguage == native
            && w.LearningLanguage == learning, ct);
    }

    private bool IsFreeLimited(string entitlement)
        => !TestModePolicy.IsEnabled(configuration)
           && (string.Equals(entitlement, EntitlementService.Status.Free, StringComparison.Ordinal)
               || string.Equals(entitlement, EntitlementService.Status.ExpiredTrial, StringComparison.Ordinal));

    private static string Normalize(string value)
        => value.Trim().ToLowerInvariant();

    private static string NormalizeLanguage(string value)
    {
        if (SupportedLanguages.TryNormalize(value, out var normalized))
            return normalized;
        throw new InvalidOperationException("unsupported language");
    }

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public sealed record AiReservationResult(bool Allowed, string? Error)
    {
        public static AiReservationResult Allow() => new(true, null);
        public static AiReservationResult Deny(string error) => new(false, error);
    }
}
