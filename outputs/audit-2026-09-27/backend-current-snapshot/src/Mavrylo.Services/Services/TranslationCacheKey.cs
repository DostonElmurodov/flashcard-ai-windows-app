using System.Text;

namespace Mavrylo.Services;

/// <summary>
/// Server cache key normalization (match clients per docs/migration/01-backend-cache-plan.md):
/// Unicode NFC, trim, <see cref="string.ToLowerInvariant"/> for text; language codes trimmed + invariant lower.
/// </summary>
public static class TranslationCacheKey
{
    /// <summary>
    /// Normalizes text for cache keys: NFC, trim, invariant lowercasing (stable for Latin; CJK typically unchanged).
    /// </summary>
    public static string NormalizeWord(string word)
    {
        if (string.IsNullOrEmpty(word)) return "";
        var trimmed = word.Trim();
        var nfc = trimmed.Normalize(NormalizationForm.FormC);
        return nfc.ToLowerInvariant();
    }

    public static string NormalizeLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "en";
        return code.Trim().ToLowerInvariant();
    }
}
