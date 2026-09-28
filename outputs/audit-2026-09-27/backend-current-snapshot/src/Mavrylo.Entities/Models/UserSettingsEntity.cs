namespace Mavrylo.Models;

public class UserSettingsEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string NativeLanguage { get; set; } = "en";
    public string LearningLanguage { get; set; } = "en";
    public int DailyGoal { get; set; } = 10;
    public bool OnboardingComplete { get; set; }
    public string ReviewDirection { get; set; } = "target_to_native";
    public bool RemindersEnabled { get; set; }
    public string ReminderMinutesJson { get; set; } = "[]";
    public bool ExpandHistoryCards { get; set; }
    public string ThemeAppearance { get; set; } = "system";
    public string AccentPreset { get; set; } = "default";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
