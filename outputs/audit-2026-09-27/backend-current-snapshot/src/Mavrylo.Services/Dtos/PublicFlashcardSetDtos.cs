namespace Mavrylo.Dtos;

public record PublicFlashcardSetCardDto(
    string ClientCardId,
    string Word,
    List<string> Translations,
    string? Pronunciation,
    string? PartOfSpeech,
    List<string> Examples,
    List<string?> ExampleTranslations,
    string? Notes,
    string NativeLanguage,
    string LearningLanguage);

public record PublicFlashcardSetUpsertRequest(
    string ClientSetId,
    string Title,
    string? Description,
    List<PublicFlashcardSetCardDto> Cards);

public record PublicFlashcardSetSubmissionDto(
    string Id,
    string ClientSetId,
    string Title,
    string? Description,
    int WordCount,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record PublicFlashcardSetCatalogRequest(string? Query, int? Limit);

public record PublicFlashcardSetCatalogItemDto(
    string Id,
    string Title,
    string? Description,
    int WordCount,
    List<PublicFlashcardSetCardDto> Cards,
    DateTime UpdatedAt);

public record PublicFlashcardSetUnpublishRequest(string ClientSetId);
public record PublicFlashcardSetUnpublishResponse(bool Unpublished);
