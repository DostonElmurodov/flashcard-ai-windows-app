namespace Mavrylo.Services;

public static class SupportedLanguages
{
    public const int MaxWordLength = 120;
    public const int MaxLanguageCodeLength = 24;

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "English (USA)",
        ["en-us"] = "English (USA)",
        ["es"] = "Spanish",
        ["tr"] = "Turkish",
        ["ru"] = "Russian",
        ["it"] = "Italian",
        ["de"] = "German",
        ["fr"] = "French",
        ["ja"] = "Japanese",
        ["zh"] = "Chinese",
        ["yue"] = "Cantonese",
        ["pt"] = "Portuguese",
        ["hi"] = "Hindi",
        ["bn"] = "Bengali",
        ["id"] = "Indonesian",
        ["ur"] = "Urdu",
        ["vi"] = "Vietnamese",
        ["ko"] = "Korean",
        ["uk"] = "Ukrainian",
        ["pl"] = "Polish",
        ["tg"] = "Tajik",
        ["uz"] = "Uzbek",
        ["az"] = "Azerbaijani",
        ["kk"] = "Kazakh",
        ["hy"] = "Armenian",
        ["ar"] = "Arabic"
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["english"] = "en",
        ["english (usa)"] = "en-us",
        ["spanish"] = "es",
        ["turkish"] = "tr",
        ["russian"] = "ru",
        ["italian"] = "it",
        ["german"] = "de",
        ["french"] = "fr",
        ["japanese"] = "ja",
        ["chinese"] = "zh",
        ["cantonese"] = "yue",
        ["portuguese"] = "pt",
        ["hindi"] = "hi",
        ["bengali"] = "bn",
        ["indonesian"] = "id",
        ["urdu"] = "ur",
        ["vietnamese"] = "vi",
        ["korean"] = "ko",
        ["ukrainian"] = "uk",
        ["polish"] = "pl",
        ["tajik"] = "tg",
        ["uzbek"] = "uz",
        ["azerbaijani"] = "az",
        ["kazakh"] = "kk",
        ["armenian"] = "hy",
        ["arabic"] = "ar"
    };

    public static bool TryNormalize(string? codeOrName, out string normalized)
    {
        normalized = NormalizeSyntax(codeOrName);
        if (normalized.Length == 0)
            normalized = "en";
        if (normalized.Length > MaxLanguageCodeLength)
            return false;

        if (Aliases.TryGetValue(normalized, out var alias))
            normalized = alias;

        return DisplayNames.ContainsKey(normalized);
    }

    public static string DisplayName(string codeOrName)
        => TryNormalize(codeOrName, out var normalized)
            ? DisplayNames[normalized]
            : throw new InvalidOperationException("unsupported language");

    private static string NormalizeSyntax(string? codeOrName)
        => (codeOrName ?? "")
            .Trim()
            .ToLowerInvariant()
            .Replace("_", "-", StringComparison.Ordinal);
}
