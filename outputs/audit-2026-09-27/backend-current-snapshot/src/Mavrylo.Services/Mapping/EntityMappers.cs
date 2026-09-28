using System.Text.Json;
using Mavrylo.Dtos;
using Mavrylo.Models;

namespace Mavrylo.Mapping;

public static class EntityMappers
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static string[] ParseStringArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOpts) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string ToJson(string[]? arr) => JsonSerializer.Serialize(arr ?? Array.Empty<string>());

    public static WordDto ToDto(WordEntity w) => new(
        w.Id, w.Word, w.Translation, w.Pronunciation, w.AudioUrl,
        ParseStringArray(w.ExamplesJson), ParseStringArray(w.ExampleTranslationsJson),
        w.UserNotes, w.CategoryId, w.SourceLanguage, w.TargetLanguage, w.NextReview,
        w.ReviewInterval, w.EaseFactor, w.Repetitions, w.IsMastered, w.PartOfSpeech,
        w.CreatedAt, w.UpdatedAt, w.IsDeleted);

    public static CategoryDto ToDto(CategoryEntity c) => new(
        c.Id, c.Name, c.Emoji, c.Color, c.Order, c.WordCount, c.CreatedAt, c.UpdatedAt, c.IsDeleted);

    public static UserSettingsDto ToDto(UserSettingsEntity s) => new(
        s.Id, s.NativeLanguage, s.LearningLanguage, s.DailyGoal, s.OnboardingComplete,
        s.ReviewDirection, s.RemindersEnabled, ParseIntArray(s.ReminderMinutesJson),
        s.ExpandHistoryCards, s.ThemeAppearance, s.AccentPreset, s.CreatedAt, s.UpdatedAt);

    public static void ApplyUpsert(WordEntity w, WordUpsert u)
    {
        if (u.Word != null) w.Word = u.Word;
        if (u.Translation != null) w.Translation = u.Translation;
        if (u.Pronunciation != null) w.Pronunciation = u.Pronunciation;
        if (u.AudioUrl != null) w.AudioUrl = u.AudioUrl;
        if (u.Examples != null) w.ExamplesJson = ToJson(u.Examples);
        if (u.ExampleTranslations != null) w.ExampleTranslationsJson = ToJson(u.ExampleTranslations);
        if (u.UserNotes != null) w.UserNotes = u.UserNotes;
        if (u.CategoryId != null) w.CategoryId = u.CategoryId;
        if (u.SourceLanguage != null) w.SourceLanguage = u.SourceLanguage;
        if (u.TargetLanguage != null) w.TargetLanguage = u.TargetLanguage;
        if (u.NextReview != null) w.NextReview = u.NextReview;
        if (u.ReviewInterval.HasValue) w.ReviewInterval = u.ReviewInterval.Value;
        if (u.EaseFactor.HasValue) w.EaseFactor = u.EaseFactor.Value;
        if (u.Repetitions.HasValue) w.Repetitions = u.Repetitions.Value;
        if (u.IsMastered.HasValue) w.IsMastered = u.IsMastered.Value;
        if (u.PartOfSpeech != null) w.PartOfSpeech = u.PartOfSpeech;
        w.UpdatedAt = DateTime.UtcNow;
    }

    public static void ApplyUpsert(CategoryEntity c, CategoryUpsert u)
    {
        if (u.Name != null) c.Name = u.Name;
        if (u.Emoji != null) c.Emoji = u.Emoji;
        if (u.Color != null) c.Color = u.Color;
        if (u.Order.HasValue) c.Order = u.Order.Value;
        c.UpdatedAt = DateTime.UtcNow;
    }

    public static void ApplyUpsert(UserSettingsEntity s, UserSettingsUpsert u)
    {
        if (u.NativeLanguage != null) s.NativeLanguage = u.NativeLanguage;
        if (u.LearningLanguage != null) s.LearningLanguage = u.LearningLanguage;
        if (u.DailyGoal.HasValue) s.DailyGoal = u.DailyGoal.Value;
        if (u.OnboardingComplete.HasValue) s.OnboardingComplete = u.OnboardingComplete.Value;
        if (u.ReviewDirection != null) s.ReviewDirection = u.ReviewDirection;
        if (u.RemindersEnabled.HasValue) s.RemindersEnabled = u.RemindersEnabled.Value;
        if (u.ReminderMinutes != null) s.ReminderMinutesJson = JsonSerializer.Serialize(u.ReminderMinutes);
        if (u.ExpandHistoryCards.HasValue) s.ExpandHistoryCards = u.ExpandHistoryCards.Value;
        if (u.ThemeAppearance != null) s.ThemeAppearance = u.ThemeAppearance;
        if (u.AccentPreset != null) s.AccentPreset = u.AccentPreset;
        s.UpdatedAt = DateTime.UtcNow;
    }

    private static int[] ParseIntArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<int[]>(json, JsonOpts) ?? Array.Empty<int>();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }
}
