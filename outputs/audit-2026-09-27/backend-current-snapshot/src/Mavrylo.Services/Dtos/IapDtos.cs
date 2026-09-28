namespace Mavrylo.Dtos;

// IAP / StoreKit contracts. JSON is snake_case on the wire. See plan Appendix M.

/// <summary>Canonical entitlement object returned by verify / token / entitlement.</summary>
public record EntitlementDto(
    string Status,
    string? ProductId,
    string? ExpiresAt,
    bool IsTrial,
    bool AutoRenew,
    bool WasEverPaid,
    string? ResolutionSource = null,
    bool? PurchaseIsLinked = null);

/// <summary>POST /iap/verify request: a signed StoreKit transaction JWS.</summary>
public record IapVerifyRequest(string JwsTransaction, string? DeviceUuid = null);

/// <summary>POST /iap/verify response: entitlement + refreshed device-JWT.</summary>
public record IapVerifyResponse(EntitlementDto Entitlement, string AccessToken, int ExpiresIn);

/// <summary>POST /iap/token response: a refreshed device-JWT + current entitlement.</summary>
public record IapTokenResponse(string AccessToken, int ExpiresIn, EntitlementDto Entitlement);

/// <summary>GET /iap/entitlement response.</summary>
public record IapEntitlementResponse(EntitlementDto Entitlement);

/// <summary>App Store Server Notifications v2 envelope.</summary>
public record AppStoreNotificationEnvelope(string SignedPayload);
