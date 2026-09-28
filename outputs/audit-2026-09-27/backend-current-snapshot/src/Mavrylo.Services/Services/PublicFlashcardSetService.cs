using System.Globalization;
using System.Text;
using System.Text.Json;
using Mavrylo.Data;
using Mavrylo.Dtos;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mavrylo.Services;

public sealed class PublicFlashcardSetValidationException(string message) : Exception(message);

public sealed class PublicFlashcardSetDataException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public class PublicFlashcardSetService(
    AppDbContext db,
    TimeProvider timeProvider,
    ILogger<PublicFlashcardSetService> logger)
{
    private const int MaxClientSetIdLength = 100;
    private const int MaxTitleLength = 120;
    private const int MaxDescriptionLength = 1_000;
    private const int MaxCards = 500;
    private const int MaxClientCardIdLength = 100;
    private const int MaxWordLength = 500;
    private const int MaxTranslations = 20;
    private const int MaxTranslationLength = 500;
    private const int MaxPronunciationLength = 500;
    private const int MaxPartOfSpeechLength = 100;
    private const int MaxExamples = 20;
    private const int MaxExampleLength = 2_000;
    private const int MaxNotesLength = 4_000;
    private const int MaxRawLanguageLength = 64;
    private const int MaxSnapshotUtf8Bytes = 800_000;
    private const int DefaultCatalogLimit = 50;
    private const int MaxCatalogLimit = 100;
    private const string OwnerClientIndexName =
        "IX_public_flashcard_sets_OwnerDeviceId_ClientSetId";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<PublicFlashcardSetSubmissionDto> UpsertForAccountAsync(string accountId, PublicFlashcardSetUpsertRequest request, CancellationToken ct)
    {
        RequireNonblank(accountId, "Owner account ID");
        var validated = ValidateAndProject(request);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql())
            await db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {accountId} FOR UPDATE").SingleAsync(ct);
        var row = await db.PublicFlashcardSets.SingleOrDefaultAsync(x => x.OwnerAccountId == accountId && x.ClientSetId == validated.ClientSetId, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (row == null)
        {
            row = new PublicFlashcardSetEntity { OwnerAccountId = accountId, ClientSetId = validated.ClientSetId, CreatedAt = now };
            db.PublicFlashcardSets.Add(row);
        }
        row.Title = validated.Title;
        row.Description = validated.Description;
        row.SnapshotJson = validated.SnapshotJson;
        row.SearchText = validated.SearchText;
        row.WordCount = validated.WordCount;
        row.Status = PublicFlashcardSetStatus.Pending;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(row.Id, row.ClientSetId, row.Title, row.Description, row.WordCount, row.Status, row.CreatedAt, row.UpdatedAt);
    }

    public Task<List<PublicFlashcardSetSubmissionDto>> ListAccountMineAsync(string accountId, CancellationToken ct)
        => db.PublicFlashcardSets.AsNoTracking().Where(x => x.OwnerAccountId == accountId)
            .OrderByDescending(x => x.UpdatedAt).Select(x => new PublicFlashcardSetSubmissionDto(x.Id, x.ClientSetId, x.Title, x.Description, x.WordCount, x.Status, x.CreatedAt, x.UpdatedAt)).ToListAsync(ct);

    public async Task UnpublishForAccountAsync(string accountId, string clientSetId, CancellationToken ct)
    {
        RequireNonblank(accountId, "Owner account ID");
        RequireNonblank(clientSetId, "Client set ID");
        RequireLength(clientSetId, MaxClientSetIdLength, "Client set ID");
        await db.PublicFlashcardSets.Where(x => x.OwnerAccountId == accountId && x.ClientSetId == clientSetId).ExecuteDeleteAsync(ct);
    }

    public async Task<PublicFlashcardSetSubmissionDto> UpsertAsync(
        string ownerDeviceId,
        PublicFlashcardSetUpsertRequest request,
        CancellationToken ct)
    {
        RequireNonblank(ownerDeviceId, "Owner device ID");
        var validated = ValidateAndProject(request);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var updated = await ApplyPendingSnapshotUpdateAsync(
            ownerDeviceId, validated, now, ct);
        if (updated == 0)
        {
            var row = new PublicFlashcardSetEntity
            {
                OwnerDeviceId = ownerDeviceId,
                ClientSetId = validated.ClientSetId,
                Title = validated.Title,
                Description = validated.Description,
                SnapshotJson = validated.SnapshotJson,
                SearchText = validated.SearchText,
                WordCount = validated.WordCount,
                Status = PublicFlashcardSetStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            };

            try
            {
                db.PublicFlashcardSets.Add(row);
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsOwnerClientUniqueViolation(ex))
            {
                db.Entry(row).State = EntityState.Detached;
                await RetryPendingSnapshotUpdateInFreshTransactionAsync(
                    ownerDeviceId, validated, now, ct);
            }
        }

        return await ReadSubmissionAsync(ownerDeviceId, validated.ClientSetId, ct);
    }

    public Task<List<PublicFlashcardSetSubmissionDto>> ListMineAsync(
        string ownerDeviceId,
        CancellationToken ct)
    {
        RequireNonblank(ownerDeviceId, "Owner device ID");
        return db.PublicFlashcardSets
            .AsNoTracking()
            .Where(x => x.OwnerDeviceId == ownerDeviceId)
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Id)
            .Select(x => new PublicFlashcardSetSubmissionDto(
                x.Id,
                x.ClientSetId,
                x.Title,
                x.Description,
                x.WordCount,
                x.Status,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task UnpublishAsync(
        string ownerDeviceId,
        string clientSetId,
        CancellationToken ct)
    {
        RequireNonblank(ownerDeviceId, "Owner device ID");
        RequireNonblank(clientSetId, "Client set ID");
        RequireLength(clientSetId, MaxClientSetIdLength, "Client set ID");

        await db.PublicFlashcardSets
            .Where(x => x.OwnerDeviceId == ownerDeviceId && x.ClientSetId == clientSetId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<List<PublicFlashcardSetCatalogItemDto>> CatalogAsync(
        string? query,
        int? limit,
        CancellationToken ct)
    {
        var normalizedQuery = NormalizeSearch(query);
        var take = Math.Clamp(limit ?? DefaultCatalogLimit, 1, MaxCatalogLimit);
        var rowsQuery = db.PublicFlashcardSets
            .AsNoTracking()
            .Where(x => x.Status == PublicFlashcardSetStatus.Approved);

        if (normalizedQuery.Length > 0)
            rowsQuery = rowsQuery.Where(x => x.SearchText.Contains(normalizedQuery));

        var rows = await rowsQuery
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Id)
            .Take(take)
            .ToListAsync(ct);

        return rows.Select(ToCatalogItem).ToList();
    }

    private Task<int> ApplyPendingSnapshotUpdateAsync(
        string ownerDeviceId,
        ValidatedPublication validated,
        DateTime now,
        CancellationToken ct) =>
        db.PublicFlashcardSets
            .Where(x => x.OwnerDeviceId == ownerDeviceId
                        && x.ClientSetId == validated.ClientSetId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Title, validated.Title)
                .SetProperty(x => x.Description, validated.Description)
                .SetProperty(x => x.SnapshotJson, validated.SnapshotJson)
                .SetProperty(x => x.SearchText, validated.SearchText)
                .SetProperty(x => x.WordCount, validated.WordCount)
                .SetProperty(x => x.Status, PublicFlashcardSetStatus.Pending)
                .SetProperty(x => x.UpdatedAt, now), ct);

    private async Task RetryPendingSnapshotUpdateInFreshTransactionAsync(
        string ownerDeviceId,
        ValidatedPublication validated,
        DateTime now,
        CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is { } failedTransaction)
        {
            await failedTransaction.RollbackAsync(ct);
            await failedTransaction.DisposeAsync();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var updated = await ApplyPendingSnapshotUpdateAsync(
            ownerDeviceId, validated, now, ct);
        if (updated != 1)
            throw new DbUpdateConcurrencyException(
                "The competing public flashcard publication could not be updated.");
        await transaction.CommitAsync(ct);
    }

    private Task<PublicFlashcardSetSubmissionDto> ReadSubmissionAsync(
        string ownerDeviceId,
        string clientSetId,
        CancellationToken ct) =>
        db.PublicFlashcardSets
            .AsNoTracking()
            .Where(x => x.OwnerDeviceId == ownerDeviceId && x.ClientSetId == clientSetId)
            .Select(x => new PublicFlashcardSetSubmissionDto(
                x.Id,
                x.ClientSetId,
                x.Title,
                x.Description,
                x.WordCount,
                x.Status,
                x.CreatedAt,
                x.UpdatedAt))
            .SingleAsync(ct);

    private PublicFlashcardSetCatalogItemDto ToCatalogItem(PublicFlashcardSetEntity row)
    {
        try
        {
            var cards = JsonSerializer.Deserialize<List<PublicFlashcardSetCardDto>>(
                row.SnapshotJson);
            if (cards is null)
                throw new JsonException("The stored snapshot was null.");

            return new PublicFlashcardSetCatalogItemDto(
                row.Id,
                row.Title,
                row.Description,
                row.WordCount,
                // Redact private notes even from snapshots published by older clients.
                cards.Select(card => card with { Notes = null }).ToList(),
                row.UpdatedAt);
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "Public flashcard publication {PublicationId} has a malformed snapshot",
                row.Id);
            throw new PublicFlashcardSetDataException(
                $"Public flashcard publication '{row.Id}' contains malformed data.", ex);
        }
    }

    private static ValidatedPublication ValidateAndProject(
        PublicFlashcardSetUpsertRequest? request)
    {
        if (request is null)
            throw new PublicFlashcardSetValidationException("A publication is required.");

        RequireNonblank(request.ClientSetId, "Client set ID");
        RequireLength(request.ClientSetId, MaxClientSetIdLength, "Client set ID");
        RequireNonblank(request.Title, "Title");
        RequireLength(request.Title, MaxTitleLength, "Title");
        RequireOptionalLength(request.Description, MaxDescriptionLength, "Description");

        if (request.Cards is null)
            throw new PublicFlashcardSetValidationException("Cards are required.");
        if (request.Cards.Count is < 1 or > MaxCards)
            throw new PublicFlashcardSetValidationException(
                $"Cards must contain between 1 and {MaxCards} items.");

        var cardIds = new HashSet<string>(StringComparer.Ordinal);
        var cards = new List<PublicFlashcardSetCardDto>(request.Cards.Count);
        foreach (var inputCard in request.Cards)
        {
            if (inputCard is null)
                throw new PublicFlashcardSetValidationException("Cards cannot contain null items.");

            RequireNonblank(inputCard.ClientCardId, "Card ID");
            RequireLength(inputCard.ClientCardId, MaxClientCardIdLength, "Card ID");
            if (!cardIds.Add(inputCard.ClientCardId))
                throw new PublicFlashcardSetValidationException("Card IDs must be unique.");

            RequireNonblank(inputCard.Word, "Word");
            RequireLength(inputCard.Word, MaxWordLength, "Word");
            var translations = ValidateTranslations(inputCard.Translations);
            RequireOptionalLength(
                inputCard.Pronunciation, MaxPronunciationLength, "Pronunciation");
            RequireOptionalLength(
                inputCard.PartOfSpeech, MaxPartOfSpeechLength, "Part of speech");
            var (examples, exampleTranslations) = ValidateExamples(
                inputCard.Examples, inputCard.ExampleTranslations);
            RequireOptionalLength(inputCard.Notes, MaxNotesLength, "Notes");
            var nativeLanguage = ValidateLanguage(inputCard.NativeLanguage, "Native language");
            var learningLanguage = ValidateLanguage(inputCard.LearningLanguage, "Learning language");

            cards.Add(new PublicFlashcardSetCardDto(
                inputCard.ClientCardId,
                inputCard.Word,
                translations,
                inputCard.Pronunciation,
                inputCard.PartOfSpeech,
                examples,
                exampleTranslations,
                // Personal notes belong only to the owner's account sync data.
                null,
                nativeLanguage,
                learningLanguage));
        }

        string snapshotJson;
        try
        {
            snapshotJson = JsonSerializer.Serialize(cards);
            if (StrictUtf8.GetByteCount(snapshotJson) > MaxSnapshotUtf8Bytes)
                throw new PublicFlashcardSetValidationException(
                    $"The card snapshot cannot exceed {MaxSnapshotUtf8Bytes} UTF-8 bytes.");
        }
        catch (EncoderFallbackException ex)
        {
            throw new PublicFlashcardSetValidationException(
                $"The card snapshot must contain valid UTF-8 text: {ex.Message}");
        }

        var searchableValues = new List<string?>
        {
            request.Title,
            request.Description
        };
        foreach (var card in cards)
        {
            searchableValues.Add(card.Word);
            searchableValues.AddRange(card.Translations);
        }

        return new ValidatedPublication(
            request.ClientSetId,
            request.Title,
            request.Description,
            snapshotJson,
            NormalizeSearch(string.Join(' ', searchableValues)),
            cards.Count);
    }

    private static List<string> ValidateTranslations(List<string>? values)
    {
        if (values is null)
            throw new PublicFlashcardSetValidationException("Translations are required.");
        if (values.Count is < 1 or > MaxTranslations)
            throw new PublicFlashcardSetValidationException(
                $"Translations must contain between 1 and {MaxTranslations} items.");

        foreach (var value in values)
        {
            RequireNonblank(value, "Translation");
            RequireLength(value, MaxTranslationLength, "Translation");
        }

        return [.. values];
    }

    private static (List<string> Examples, List<string?> ExampleTranslations) ValidateExamples(
        List<string>? examples,
        List<string?>? exampleTranslations)
    {
        if (examples is null || exampleTranslations is null)
            throw new PublicFlashcardSetValidationException(
                "Examples and example translations are required.");
        if (examples.Count > MaxExamples)
            throw new PublicFlashcardSetValidationException(
                $"Examples cannot contain more than {MaxExamples} items.");
        if (examples.Count != exampleTranslations.Count)
            throw new PublicFlashcardSetValidationException(
                "Examples and example translations must have matching counts.");

        foreach (var example in examples)
        {
            if (example is null)
                throw new PublicFlashcardSetValidationException(
                    "Examples cannot contain null items.");
            RequireLength(example, MaxExampleLength, "Example");
        }

        foreach (var translation in exampleTranslations)
        {
            if (translation is not null)
                RequireLength(translation, MaxExampleLength, "Example translation");
        }

        return ([.. examples], [.. exampleTranslations]);
    }

    private static string ValidateLanguage(string? value, string field)
    {
        RequireNonblank(value, field);
        RequireLength(value, MaxRawLanguageLength, field);
        if (!SupportedLanguages.TryNormalize(value, out var normalized))
            throw new PublicFlashcardSetValidationException($"{field} is unsupported.");
        return normalized;
    }

    private static void RequireNonblank(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new PublicFlashcardSetValidationException($"{field} is required.");
    }

    private static void RequireLength(string? value, int maxLength, string field)
    {
        if (value is null || value.Length > maxLength)
            throw new PublicFlashcardSetValidationException(
                $"{field} cannot exceed {maxLength} characters.");
    }

    private static void RequireOptionalLength(string? value, int maxLength, string field)
    {
        if (value?.Length > maxLength)
            throw new PublicFlashcardSetValidationException(
                $"{field} cannot exceed {maxLength} characters.");
    }

    private static string NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var normalized = new StringBuilder(decomposed.Length);
        var needsSpace = false;
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
                continue;

            if (char.IsWhiteSpace(character))
            {
                needsSpace = normalized.Length > 0;
                continue;
            }

            if (needsSpace)
            {
                normalized.Append(' ');
                needsSpace = false;
            }

            normalized.Append(char.ToLowerInvariant(character));
        }

        return normalized.ToString();
    }

    private static bool IsOwnerClientUniqueViolation(DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.UniqueViolation
                && string.Equals(
                    postgres.ConstraintName,
                    OwnerClientIndexName,
                    StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private sealed record ValidatedPublication(
        string ClientSetId,
        string Title,
        string? Description,
        string SnapshotJson,
        string SearchText,
        int WordCount);
}
