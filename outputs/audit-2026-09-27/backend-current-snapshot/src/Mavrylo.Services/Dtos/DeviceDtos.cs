namespace Mavrylo.Dtos;

// App Attest / device-registration contracts. JSON is snake_case on the wire
// (global JsonNamingPolicy.SnakeCaseLower). See plan Appendix M.

/// <summary>Response to bootstrap-challenge: a base64 nonce for attestKey plus its single-use id.</summary>
public record AppAttestBootstrapChallengeResponse(string Challenge, string ChallengeId);

/// <summary>
/// Register request. <c>KeyId</c> is the App Attest key id (base64). For the Development /
/// Simulator path, KeyId is "SIMULATOR-&lt;uuid&gt;" and Attestation/Challenge may be empty.
/// </summary>
public record AppAttestRegisterRequest(
    string KeyId,
    string? Attestation = null,
    string? Challenge = null,
    string? ChallengeId = null,
    string? DeviceUuid = null);

/// <summary>Register response: the first device-JWT (ent=free) + its expiry.</summary>
public record AppAttestRegisterResponse(string DeviceToken, string ExpiresAt);

/// <summary>Assertion-challenge request: the path the assertion will be used on.</summary>
public record AppAttestAssertionChallengeRequest(string RequestPath);

/// <summary>Assertion-challenge response: a base64 nonce bound to keyId+path, plus its id.</summary>
public record AppAttestAssertionChallengeResponse(string Challenge, string ChallengeId);
