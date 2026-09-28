namespace Mavrylo.Services;

/// <summary>
/// Configuration for AI endpoint protection (bound from the "AiProtection" config section).
///
/// AI authentication and commercial gates are always enforced. TestMode:Enabled is the sole
/// commercial testing switch; it does not bypass authentication.
/// </summary>
public sealed class AiProtectionOptions
{
    public const string SectionName = "AiProtection";

    /// <summary>
    /// Deprecated rollout setting, retained for configuration compatibility. Ignored by the filter;
    /// false no longer disables enforcement. Production startup still requires true.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// When true, require a fresh App Attest assertion on each protected call. Independent of
    /// <see cref="Enabled"/> so assertion can be relaxed for local development while token +
    /// entitlement are enforced. Production startup requires true.
    /// </summary>
    public bool RequireAssertion { get; set; } = true;

    /// <summary>Free-tier daily AI call quota per device (keyed by keyId). Pure cost/abuse guard.</summary>
    public int FreeDailyQuota { get; set; } = 40;
}
