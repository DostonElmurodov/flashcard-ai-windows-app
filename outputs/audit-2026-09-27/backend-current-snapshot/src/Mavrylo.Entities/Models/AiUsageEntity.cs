namespace Mavrylo.Models;

/// <summary>
/// Per-device daily AI call counter — the free-tier cost guard (abuse/cost protection, NOT the
/// product 10-word limit which is enforced on-device). Keyed by App Attest <see cref="KeyId"/>
/// (the only stable identifier present on a protected AI request) + UTC <see cref="Date"/>.
/// </summary>
public class AiUsageEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>App Attest key id of the calling device.</summary>
    public string KeyId { get; set; } = "";

    /// <summary>UTC date bucket, "yyyy-MM-dd".</summary>
    public string Date { get; set; } = "";

    /// <summary>Number of successful AI calls in this bucket.</summary>
    public int Count { get; set; }
}
