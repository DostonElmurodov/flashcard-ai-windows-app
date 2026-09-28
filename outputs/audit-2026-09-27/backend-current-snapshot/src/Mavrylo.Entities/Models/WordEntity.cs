namespace Mavrylo.Models;

public class WordEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string Word { get; set; } = "";
    public string? Translation { get; set; }
    public string? Pronunciation { get; set; }
    public string? AudioUrl { get; set; }
    public string ExamplesJson { get; set; } = "[]";
    public string ExampleTranslationsJson { get; set; } = "[]";
    public string? UserNotes { get; set; }
    public string? CategoryId { get; set; }
    public string SourceLanguage { get; set; } = "en";
    public string TargetLanguage { get; set; } = "en";
    public string? NextReview { get; set; }
    public int ReviewInterval { get; set; } = 1;
    public double EaseFactor { get; set; } = 2.5;
    public int Repetitions { get; set; }
    public bool IsMastered { get; set; }
    public string? PartOfSpeech { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
