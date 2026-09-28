namespace Mavrylo.Services;

public interface IAppAttestVerifier
{
    bool IsDevelopmentBypassEnabled { get; }

    AppAttestVerifier.AttestationResult VerifyAttestation(
        string keyId,
        byte[] attestationCbor,
        byte[] challengeNonce);

    AppAttestVerifier.AssertionResult VerifyAssertion(
        byte[] assertionCbor,
        byte[] storedPublicKeySpki,
        long storedSignCount,
        byte[] expectedClientDataHash);
}
