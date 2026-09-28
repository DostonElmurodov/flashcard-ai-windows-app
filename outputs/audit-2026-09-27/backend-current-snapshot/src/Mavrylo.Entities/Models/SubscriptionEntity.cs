namespace Mavrylo.Models;

/// <summary>
/// A subscription as known to the backend, keyed by Apple's <see cref="OriginalTransactionId"/>
/// (the stable paid identity that survives renewals and restores). Linked to a device via
/// <see cref="DeviceUuid"/> (the StoreKit appAccountToken). The canonical entitlement is computed
/// from these fields by <c>EntitlementService</c>; <see cref="Status"/> stores the last computed /
/// notification-driven status (notably "grace").
/// </summary>
public class SubscriptionEntity
{
    // Claim timestamp survives account deletion; a null owner with a timestamp is a tombstone.
    public string? OwnerAccountId { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? LastAppleEventAt { get; set; }

    /// <summary>Apple original transaction id — the paid identity (primary key).</summary>
    public string OriginalTransactionId { get; set; } = "";

    /// <summary>StoreKit appAccountToken of the device that most recently presented this subscription.</summary>
    public string DeviceUuid { get; set; } = "";

    public string ProductId { get; set; } = "";

    /// <summary>Last known status: free/trial/premium/grace/expired_trial/expired_paid/revoked.</summary>
    public string Status { get; set; } = "free";

    /// <summary>When the (paid) period ends. For grace, this is extended to the grace expiry.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>True while the current period is an introductory free trial.</summary>
    public bool IsTrial { get; set; }

    /// <summary>True once Apple has ever charged (distinguishes expired_trial vs expired_paid). Never reset to false.</summary>
    public bool WasEverPaid { get; set; }

    /// <summary>Whether auto-renew is on (from renewal info / notifications; default true while active).</summary>
    public bool AutoRenew { get; set; } = true;

    /// <summary>StoreKit environment: "Sandbox", "Production", "Xcode", or "LocalTesting".</summary>
    public string Environment { get; set; } = "Production";

    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set when refunded/revoked by Apple.</summary>
    public DateTime? RevokedAt { get; set; }
}
