using System.Diagnostics.Metrics;

namespace Mavrylo.Services;

public static class TranslationCacheMetrics
{
    private static readonly Meter Meter = new("Mavrylo", "1.0.0");

    public static readonly Counter<long> Hits =
        Meter.CreateCounter<long>("translation_cache_hit", description: "Translation cache hits on AI endpoints");

    public static readonly Counter<long> Misses =
        Meter.CreateCounter<long>("translation_cache_miss", description: "Translation cache misses on AI endpoints");

    public static void RecordHit(string cacheKind) =>
        Hits.Add(1, new KeyValuePair<string, object?>("cache_kind", cacheKind));

    public static void RecordMiss(string cacheKind) =>
        Misses.Add(1, new KeyValuePair<string, object?>("cache_kind", cacheKind));
}
