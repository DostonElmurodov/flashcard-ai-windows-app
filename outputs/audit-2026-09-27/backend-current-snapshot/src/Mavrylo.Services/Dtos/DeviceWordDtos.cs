namespace Mavrylo.Dtos;

public record DeviceWordUpsertRequest(
    string? ClientWordId,
    string NormalizedWord,
    string DisplayWord,
    string NativeLanguage,
    string LearningLanguage,
    string? Translation,
    string? Pronunciation,
    string? PartOfSpeech,
    string? DetailJson);

public record DeviceWordDeleteRequest(
    string? ClientWordId,
    string? NormalizedWord,
    string? NativeLanguage,
    string? LearningLanguage);

public record DeviceWordCountResponse(int ActiveWordCount, int FreeLimit);

public record DeviceWordMutationResponse(bool Accepted, int ActiveWordCount, int FreeLimit);
