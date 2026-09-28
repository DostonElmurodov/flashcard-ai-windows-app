namespace Mavrylo.Models;

/// <summary>
/// An App Attest-registered device. One row per attested App Attest key.
/// Identity is device-based (no login): <see cref="DeviceUuid"/> is the StoreKit
/// appAccountToken stored in the device Keychain.
/// </summary>
public class DeviceEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool RequiresAccountSubscription { get; set; }

    /// <summary>Apple App Attest key identifier (base64). Unique per device key.</summary>
    public string KeyId { get; set; } = "";

    /// <summary>Attested P-256 public key in SPKI (SubjectPublicKeyInfo) DER form.</summary>
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();

    /// <summary>Last accepted App Attest assertion counter (monotonic).</summary>
    public long SignCount { get; set; }

    /// <summary>StoreKit appAccountToken (device-scoped UUID from the Keychain).</summary>
    public string DeviceUuid { get; set; } = "";

    /// <summary>App Attest environment for this key: "development" or "production".</summary>
    public string Environment { get; set; } = "production";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
