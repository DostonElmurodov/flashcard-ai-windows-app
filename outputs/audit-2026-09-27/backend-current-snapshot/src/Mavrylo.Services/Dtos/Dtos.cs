namespace Mavrylo.Dtos;

public record RegisterRequest(string Email, string Password);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, UserDto User);
public record UserDto(string Id, string Email, string Role);

public record GoogleAuthRequest(string IdToken);
public record AppleAuthRequest(string IdentityToken, string? Nonce);

public record WordDto(
    string Id, string Word, string? Translation, string? Pronunciation, string? AudioUrl,
    string[] Examples, string[] ExampleTranslations, string? UserNotes, string? CategoryId,
    string SourceLanguage, string TargetLanguage, string? NextReview, int ReviewInterval,
    double EaseFactor, int Repetitions, bool IsMastered, string? PartOfSpeech,
    DateTime CreatedDate, DateTime UpdatedDate, bool IsDeleted = false);

public record WordUpsert(
    string? Word, string? Translation, string? Pronunciation, string? AudioUrl,
    string[]? Examples, string[]? ExampleTranslations, string? UserNotes, string? CategoryId,
    string? SourceLanguage, string? TargetLanguage, string? NextReview, int? ReviewInterval,
    double? EaseFactor, int? Repetitions, bool? IsMastered, string? PartOfSpeech);

public record CategoryDto(string Id, string Name, string? Emoji, string? Color, int Order, int WordCount, DateTime CreatedDate, DateTime UpdatedDate, bool IsDeleted = false);
public record CategoryUpsert(string? Name, string? Emoji, string? Color, int? Order);

public record UserSettingsDto(
    string Id,
    string NativeLanguage,
    string LearningLanguage,
    int DailyGoal,
    bool OnboardingComplete,
    string ReviewDirection,
    bool RemindersEnabled,
    int[] ReminderMinutes,
    bool ExpandHistoryCards,
    string ThemeAppearance,
    string AccentPreset,
    DateTime CreatedDate,
    DateTime UpdatedDate);

public record UserSettingsUpsert(
    string? NativeLanguage,
    string? LearningLanguage,
    int? DailyGoal,
    bool? OnboardingComplete,
    string? ReviewDirection,
    bool? RemindersEnabled,
    int[]? ReminderMinutes,
    bool? ExpandHistoryCards,
    string? ThemeAppearance,
    string? AccentPreset);

public record SyncPullResponse(DateTime ServerTimeUtc, List<WordDto> Words, List<CategoryDto> Categories, List<UserSettingsDto> UserSettings);
public record SyncPushRequest(string? DeviceId, List<WordPushItem>? Words, List<CategoryPushItem>? Categories, List<UserSettingsPushItem>? UserSettings);
public record WordPushItem(string? Id, bool? IsDeleted, WordUpsert? Data);
public record CategoryPushItem(string? Id, bool? IsDeleted, CategoryUpsert? Data);
public record UserSettingsPushItem(string? Id, UserSettingsUpsert? Data);
public record SyncPushResponse(bool Accepted, List<object> Conflicts);

public record ReviewTranslationRequest(string Word, string? NativeLanguage, string? LearningLanguage, string? SecondaryLanguage);
public record AnalyzeWordRequest(string Word, string? NativeLanguage, string? LearningLanguage);
public record WordDetailRequest(string Word, string? LearningLanguage, string? NativeLanguage, string? SecondaryLanguage = null);
