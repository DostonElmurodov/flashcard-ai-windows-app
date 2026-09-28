using Mavrylo.Services;

namespace Mavrylo.Tests.TestSupport;

internal sealed class FakeAppAttestVerifier : IAppAttestVerifier
{
    public bool IsDevelopmentBypassEnabled { get; set; }
    public AppAttestVerifier.AttestationResult AttestationResult { get; set; } = new(true, [], 0, null);
    public AppAttestVerifier.AssertionResult AssertionResult { get; set; } = new(true, 1, null);

    public AppAttestVerifier.AttestationResult VerifyAttestation(string keyId, byte[] attestationCbor, byte[] challengeNonce) =>
        AttestationResult;

    public AppAttestVerifier.AssertionResult VerifyAssertion(
        byte[] assertionCbor,
        byte[] storedPublicKeySpki,
        long storedSignCount,
        byte[] expectedClientDataHash) =>
        AssertionResult;
}
