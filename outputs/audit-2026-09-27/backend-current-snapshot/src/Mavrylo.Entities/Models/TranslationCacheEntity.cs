namespace Mavrylo.Models;

/// <summary>
/// Shared server cache for AI translation JSON (+ optional TTS metadata).
/// Cache key: NFC+trim+InvariantLower word, lowercased language codes, <see cref="CacheKind"/>, <see cref="PromptVersion"/>.
/// </summary>
public class TranslationCacheEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Unicode NFC, trimmed, lowercased with <see cref="string.ToLowerInvariant"/>.</summary>
    public string NormalizedKey { get; set; } = "";

    /// <summary>User native / explanation language (BCP-47 subset, lowercased).</summary>
    public string NativeLanguage { get; set; } = "";

    /// <summary>Language of the word being analyzed (BCP-47 subset, lowercased).</summary>
    public string LearningLanguage { get; set; } = "";

    /// <summary>e.g. analyze_word, word_detail — distinct response shapes.</summary>
    public string CacheKind { get; set; } = "";

    /// <summary>Invalidate when prompts or models change.</summary>
    public string PromptVersion { get; set; } = TranslationCacheEntity.CurrentPromptVersion;

    public const string CurrentPromptVersion = "2026-05-v5";

    public static class Kinds
    {
        public const string AnalyzeWord = "analyze_word";
        public const string WordDetail = "word_detail";
        public const string ReviewTranslation = "review_translation";
    }

    /// <summary>Exact JSON body returned to clients (preserves pre-cache shape).</summary>
    public string ResponseJson { get; set; } = "{}";

    public string? AudioStorageKey { get; set; }
    public string? AudioContentType { get; set; }

    /// <summary>Optional stable URL for clients when audio is stored and exposed.</summary>
    public string? AudioUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastHitAt { get; set; }
}
