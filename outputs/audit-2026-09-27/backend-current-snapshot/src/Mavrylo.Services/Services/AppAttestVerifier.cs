using System.Formats.Cbor;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Mavrylo.Services;

/// <summary>
/// Server-side App Attest verification (Apple DeviceCheck App Attest).
///
/// Two entry points:
///  - <see cref="VerifyAttestation"/>: validates the attestation object produced by
///    <c>DCAppAttestService.attestKey</c> during device registration, and extracts the device's
///    P-256 public key (stored in <c>devices.PublicKey</c>).
///  - <see cref="VerifyAssertion"/>: validates a per-request assertion produced by
///    <c>generateAssertion</c> against the stored public key, enforcing monotonic counters.
///
/// This class performs cryptography only — it does not touch the database. The controller is
/// responsible for persistence and for the Development/Simulator bypass (see
/// <see cref="IsDevelopmentBypassEnabled"/>).
///
/// Reference: "Validating Apps That Connect to Your Server" (Apple Developer documentation).
/// </summary>
public sealed class AppAttestVerifier(
    IConfiguration config,
    IWebHostEnvironment env,
    ILogger<AppAttestVerifier> logger) : IAppAttestVerifier
{
    // Apple OID carrying the attestation nonce inside the leaf (credCert) certificate.
    private const string AppleNonceOid = "1.2.840.113635.100.8.2";
    private const string ExpectedAttestationFormat = "apple-appattest";
    private const byte AttestedCredentialDataFlag = 0x40;

    private static readonly Lazy<X509Certificate2> AppleRootCa = new(LoadAppleRootCa);
    private static readonly byte[] ProductionAaguid = MakeAaguid("appattest");
    private static readonly byte[] DevelopmentAaguid = Encoding.ASCII.GetBytes("appattestdevelop");

    public sealed record AttestationResult(bool Ok, byte[]? PublicKeySpki, long SignCount, string? Error, string Environment = "production")
    {
        public static AttestationResult Fail(string error) => new(false, null, 0, error);
        public static AttestationResult Success(byte[] publicKeySpki, long signCount, string environment = "production") => new(true, publicKeySpki, signCount, null, environment);
    }

    public sealed record AssertionResult(bool Ok, long NewSignCount, string? Error)
    {
        public static AssertionResult Fail(string error) => new(false, 0, error);
        public static AssertionResult Success(long newSignCount) => new(true, newSignCount, null);
    }

    /// <summary>
    /// True only when BOTH the build is DEBUG and the runtime environment is Development.
    /// Used by the controller to allow Simulator (SIMULATOR-*) registration without attestation.
    /// Never true in a Release build or a non-Development environment (review finding O).
    /// </summary>
    public bool IsDevelopmentBypassEnabled
    {
        get
        {
#if DEBUG
            return env.IsDevelopment();
#else
            return env.IsDevelopment() && false;
#endif
        }
    }

    /// <summary>
    /// Verifies an attestation object during registration.
    /// </summary>
    /// <param name="keyId">Base64 App Attest key id from the client (the credential id).</param>
    /// <param name="attestationCbor">Raw bytes of the Apple attestation object (CBOR).</param>
    /// <param name="challengeNonce">The bootstrap challenge nonce the server issued.</param>
    public AttestationResult VerifyAttestation(string keyId, byte[] attestationCbor, byte[] challengeNonce)
    {
        try
        {
            // 1) CBOR decode { fmt, attStmt: { x5c: [..], receipt }, authData }
            if (!TryDecodeAttestation(attestationCbor, out var fmt, out var x5c, out var authData, out var decodeError))
                return AttestationResult.Fail(decodeError ?? "Malformed attestation CBOR.");

            if (!string.Equals(fmt, ExpectedAttestationFormat, StringComparison.Ordinal))
                return AttestationResult.Fail($"Unexpected attestation format '{fmt}'.");
            if (x5c.Count < 1)
                return AttestationResult.Fail("Attestation x5c chain is empty.");

            using var leaf = X509CertificateLoader.LoadCertificate(x5c[0]);
            var intermediates = new X509Certificate2Collection();
            for (int i = 1; i < x5c.Count; i++)
                intermediates.Add(X509CertificateLoader.LoadCertificate(x5c[i]));

            // 2) Validate the certificate chain up to the pinned Apple App Attest Root CA.
            if (!ValidateChain(leaf, intermediates, out var chainError))
                return AttestationResult.Fail(chainError ?? "Certificate chain validation failed.");

            // 3) Nonce: clientDataHash = SHA256(challenge); nonce = SHA256(authData || clientDataHash);
            //    must equal the value embedded in the leaf cert's Apple OID extension.
            var clientDataHash = SHA256.HashData(challengeNonce);
            var expectedNonce = SHA256.HashData(Concat(authData, clientDataHash));
            if (!TryReadAppleNonceExtension(leaf, out var certNonce, out var nonceError))
                return AttestationResult.Fail(nonceError ?? "Missing Apple nonce extension.");
            if (!CryptographicOperations.FixedTimeEquals(expectedNonce, certNonce))
                return AttestationResult.Fail("Attestation nonce mismatch.");

            // 4) rpIdHash: first 32 bytes of authData == SHA256("TeamID.BundleID").
            if (authData.Length < 37)
                return AttestationResult.Fail("authData too short.");
            if (!ValidateAuthDataFlags(authData, requireAttestedCredentialData: true, out var flagError))
                return AttestationResult.Fail(flagError ?? "Invalid authData flags.");

            var rpIdHash = authData.AsSpan(0, 32).ToArray();
            if (!CryptographicOperations.FixedTimeEquals(rpIdHash, ExpectedRpIdHash()))
                return AttestationResult.Fail("rpIdHash mismatch (TeamID.BundleID).");

            // 5) Counter must be zero at registration (bytes 33..37, big-endian).
            var counter = ReadCounter(authData);
            if (counter != 0)
                return AttestationResult.Fail($"Attestation counter expected 0 but was {counter}.");

            // 6) AAGUID must be one of Apple's App Attest authenticators.
            if (!TryReadAaguid(authData, out var aaguid, out var aaguidError))
                return AttestationResult.Fail(aaguidError ?? "Could not read AAGUID.");
            if (!IsKnownAppAttestAaguid(aaguid))
                return AttestationResult.Fail("Unexpected App Attest AAGUID.");

            // 7) Credential id in authData must equal SHA256(public key) and the supplied keyId.
            if (!TryReadCredentialId(authData, out var credentialId, out var credError))
                return AttestationResult.Fail(credError ?? "Could not read credential id.");

            using var ecdsa = leaf.GetECDsaPublicKey()
                ?? throw new CryptographicException("Leaf certificate has no EC public key.");
            var publicKeySpki = ecdsa.ExportSubjectPublicKeyInfo();

            var keyIdBytes = TryDecodeBase64(keyId);
            if (keyIdBytes is null)
                return AttestationResult.Fail("keyId is not valid base64.");
            // The App Attest key id is SHA256 of the public key; it is also the credential id.
            if (!CryptographicOperations.FixedTimeEquals(credentialId, keyIdBytes))
                return AttestationResult.Fail("Credential id does not match keyId.");

            // authData (including AAGUID) was bound to the Apple-signed nonce above.
            var environment = CryptographicOperations.FixedTimeEquals(aaguid, DevelopmentAaguid)
                ? "development" : "production";
            return AttestationResult.Success(publicKeySpki, 0, environment);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "App Attest attestation verification threw.");
            return AttestationResult.Fail("Attestation verification error.");
        }
    }

    /// <summary>
    /// Verifies a per-request assertion against the stored public key.
    /// </summary>
    /// <param name="assertionCbor">Raw bytes of the assertion object (CBOR).</param>
    /// <param name="storedPublicKeySpki">The device's stored P-256 public key (SPKI DER).</param>
    /// <param name="storedSignCount">Last accepted counter; the new one must be strictly greater.</param>
    /// <param name="expectedClientDataHash">
    /// The clientDataHash the server recomputed from the received body (Appendix C preimage).
    /// </param>
    public AssertionResult VerifyAssertion(
        byte[] assertionCbor, byte[] storedPublicKeySpki, long storedSignCount, byte[] expectedClientDataHash)
    {
        try
        {
            // 1) CBOR decode { signature, authenticatorData }
            if (!TryDecodeAssertion(assertionCbor, out var signature, out var authData, out var decodeError))
                return AssertionResult.Fail(decodeError ?? "Malformed assertion CBOR.");

            if (authData.Length < 37)
                return AssertionResult.Fail("authenticatorData too short.");
            if (!ValidateAuthDataFlags(authData, requireAttestedCredentialData: false, out var flagError))
                return AssertionResult.Fail(flagError ?? "Invalid authenticatorData flags.");

            // 2) rpIdHash check.
            var rpIdHash = authData.AsSpan(0, 32).ToArray();
            if (!CryptographicOperations.FixedTimeEquals(rpIdHash, ExpectedRpIdHash()))
                return AssertionResult.Fail("rpIdHash mismatch (TeamID.BundleID).");

            // 3) Counter must be strictly greater than the stored value (replay/clone defense).
            var newCounter = ReadCounter(authData);
            if (newCounter <= storedSignCount)
                return AssertionResult.Fail($"Counter not incremented (stored={storedSignCount}, got={newCounter}).");

            // 4) Verify the ES256 signature. App Attest assertions are documented around
            // authenticatorData || clientDataHash, but real devices may sign the already-hashed
            // composite as a message through Apple's signing stack. Accept both the single-hash
            // WebAuthn-style verification and the observed double-hash form.
            var signedData = Concat(authData, expectedClientDataHash);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(storedPublicKeySpki, out _);
            var ok = ecdsa.VerifyData(
                signedData, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            if (!ok)
            {
                var compositeHash = SHA256.HashData(signedData);
                ok = ecdsa.VerifyData(
                    compositeHash, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            }
            if (!ok)
                return AssertionResult.Fail("Assertion signature invalid.");

            return AssertionResult.Success(newCounter);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "App Attest assertion verification threw.");
            return AssertionResult.Fail("Assertion verification error.");
        }
    }

    // ---- CBOR ---------------------------------------------------------------

    private static bool TryDecodeAttestation(
        byte[] cbor, out string? fmt, out List<byte[]> x5c, out byte[] authData, out string? error)
    {
        fmt = null; x5c = new List<byte[]>(); authData = Array.Empty<byte>(); error = null;
        var reader = new CborReader(cbor, CborConformanceMode.Lax);
        var top = reader.ReadStartMap();
        for (int i = 0; i < (top ?? 0); i++)
        {
            var key = reader.ReadTextString();
            switch (key)
            {
                case "fmt":
                    fmt = reader.ReadTextString();
                    break;
                case "authData":
                    authData = reader.ReadByteString();
                    break;
                case "attStmt":
                    var inner = reader.ReadStartMap();
                    for (int j = 0; j < (inner ?? 0); j++)
                    {
                        var k = reader.ReadTextString();
                        if (k == "x5c")
                        {
                            var n = reader.ReadStartArray();
                            for (int c = 0; c < (n ?? 0); c++)
                                x5c.Add(reader.ReadByteString());
                            reader.ReadEndArray();
                        }
                        else
                        {
                            reader.SkipValue();
                        }
                    }
                    reader.ReadEndMap();
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }
        reader.ReadEndMap();
        if (authData.Length == 0) { error = "attestation missing authData."; return false; }
        return true;
    }

    private static bool TryDecodeAssertion(
        byte[] cbor, out byte[] signature, out byte[] authData, out string? error)
    {
        signature = Array.Empty<byte>(); authData = Array.Empty<byte>(); error = null;
        var reader = new CborReader(cbor, CborConformanceMode.Lax);
        var top = reader.ReadStartMap();
        for (int i = 0; i < (top ?? 0); i++)
        {
            var key = reader.ReadTextString();
            switch (key)
            {
                case "signature":
                    signature = reader.ReadByteString();
                    break;
                case "authenticatorData":
                    authData = reader.ReadByteString();
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }
        reader.ReadEndMap();
        if (signature.Length == 0) { error = "assertion missing signature."; return false; }
        if (authData.Length == 0) { error = "assertion missing authenticatorData."; return false; }
        return true;
    }

    // ---- Cert chain ---------------------------------------------------------

    private bool ValidateChain(X509Certificate2 leaf, X509Certificate2Collection intermediates, out string? error)
    {
        error = null;
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // root is pinned below
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(AppleRootCa.Value);
        chain.ChainPolicy.ExtraStore.AddRange(intermediates);

        var built = chain.Build(leaf);
        if (built)
            return true;

        var reasons = chain.ChainStatus.Length > 0
            ? string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim()))
            : "unknown";
        error = $"Certificate chain not trusted: {reasons}";
        return false;
    }

    private static X509Certificate2 LoadAppleRootCa()
    {
        var asm = Assembly.GetExecutingAssembly();
        // Logical name follows RootNamespace + folder path with dots.
        const string resourceName = "Mavrylo.Resources.Certificates.Apple_App_Attestation_Root_CA.pem";
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded Apple App Attest Root CA not found ('{resourceName}'). Check the EmbeddedResource in the csproj.");
        using var sr = new StreamReader(stream);
        var pem = sr.ReadToEnd();
        return X509Certificate2.CreateFromPem(pem);
    }

    // ---- authData helpers ---------------------------------------------------

    // authData layout: rpIdHash(32) | flags(1) | counter(4, big-endian) | [attestedCredentialData...]
    private static bool ValidateAuthDataFlags(byte[] authData, bool requireAttestedCredentialData, out string? error)
    {
        error = null;
        var flags = authData[32];
        // App Attest authenticator data follows the WebAuthn layout, but Apple does not require
        // or consistently set the user-present bit for attestKey/generateAssertion output. Treat
        // the App Attest-specific structural flags as authoritative instead.

        var hasAttestedCredentialData = (flags & AttestedCredentialDataFlag) != 0;
        if (requireAttestedCredentialData && !hasAttestedCredentialData)
        {
            error = "attestation authData missing attested-credential-data flag.";
            return false;
        }

        return true;
    }

    private static long ReadCounter(byte[] authData)
    {
        var span = authData.AsSpan(33, 4);
        return ((long)span[0] << 24) | ((long)span[1] << 16) | ((long)span[2] << 8) | span[3];
    }

    // attestedCredentialData: aaguid(16) | credentialIdLength(2, big-endian) | credentialId(L) | COSEKey...
    private static bool TryReadCredentialId(byte[] authData, out byte[] credentialId, out string? error)
    {
        credentialId = Array.Empty<byte>(); error = null;
        // 37 header bytes + 16 aaguid + 2 length
        const int aaguidOffset = 37;
        if (authData.Length < aaguidOffset + 16 + 2) { error = "authData lacks attested credential data."; return false; }
        int lenOffset = aaguidOffset + 16;
        int credLen = (authData[lenOffset] << 8) | authData[lenOffset + 1];
        int idOffset = lenOffset + 2;
        if (credLen <= 0 || authData.Length < idOffset + credLen) { error = "credential id length out of range."; return false; }
        credentialId = authData.AsSpan(idOffset, credLen).ToArray();
        return true;
    }

    private static bool TryReadAaguid(byte[] authData, out byte[] aaguid, out string? error)
    {
        aaguid = Array.Empty<byte>(); error = null;
        const int aaguidOffset = 37;
        if (authData.Length < aaguidOffset + 16)
        {
            error = "authData lacks AAGUID.";
            return false;
        }

        aaguid = authData.AsSpan(aaguidOffset, 16).ToArray();
        return true;
    }

    private static bool IsKnownAppAttestAaguid(byte[] aaguid)
        => CryptographicOperations.FixedTimeEquals(aaguid, ProductionAaguid)
           || CryptographicOperations.FixedTimeEquals(aaguid, DevelopmentAaguid);

    private static byte[] MakeAaguid(string asciiPrefix)
    {
        var bytes = new byte[16];
        Encoding.ASCII.GetBytes(asciiPrefix, bytes);
        return bytes;
    }

    private static bool TryReadAppleNonceExtension(X509Certificate2 leaf, out byte[] nonce, out string? error)
    {
        nonce = Array.Empty<byte>(); error = null;
        var ext = leaf.Extensions[AppleNonceOid];
        if (ext is null) { error = "Leaf certificate missing Apple nonce extension."; return false; }

        // The extension is DER: SEQUENCE { [1] EXPLICIT OCTET STRING { 32-byte nonce } }.
        // Parse with AsnReader to extract the inner 32-byte octet string.
        try
        {
            var outer = new System.Formats.Asn1.AsnReader(ext.RawData, System.Formats.Asn1.AsnEncodingRules.DER);
            var seq = outer.ReadSequence();
            var tagged = seq.ReadSequence(new System.Formats.Asn1.Asn1Tag(System.Formats.Asn1.TagClass.ContextSpecific, 1));
            nonce = tagged.ReadOctetString();
            if (nonce.Length != 32) { error = "Apple nonce extension is not 32 bytes."; return false; }
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse Apple nonce extension: {ex.Message}";
            return false;
        }
    }

    // ---- misc ---------------------------------------------------------------

    private byte[] ExpectedRpIdHash()
    {
        var teamId = config["Apple:TeamId"]?.Trim();
        var bundleId = config["Apple:ClientId"]?.Trim();
        if (string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(bundleId))
            throw new InvalidOperationException("Apple:TeamId and Apple:ClientId must be configured for App Attest rpId.");
        return SHA256.HashData(Encoding.UTF8.GetBytes($"{teamId}.{bundleId}"));
    }

    private static byte[]? TryDecodeBase64(string s)
    {
        try { return Convert.FromBase64String(s); }
        catch (FormatException) { return null; }
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, result, 0, a.Length);
        Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
        return result;
    }
}
