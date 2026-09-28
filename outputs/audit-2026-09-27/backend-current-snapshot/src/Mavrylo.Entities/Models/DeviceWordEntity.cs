namespace Mavrylo.Models;

/// <summary>
/// Word inventory owned by an App Attest registered device. This is the no-login source used for
/// backend free-limit enforcement now and future device/account sync later.
/// </summary>
public class DeviceWordEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceUuid { get; set; } = "";
    public string? ClientWordId { get; set; }
    public string NormalizedWord { get; set; } = "";
    public string DisplayWord { get; set; } = "";
    public string NativeLanguage { get; set; } = "en";
    public string LearningLanguage { get; set; } = "en";
    public string? Translation { get; set; }
    public string? Pronunciation { get; set; }
    public string? PartOfSpeech { get; set; }
    public string? DetailJson { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
