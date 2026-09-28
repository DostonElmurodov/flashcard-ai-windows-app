using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Mavrylo.Services;

/// <summary>
/// Decodes and (in production) verifies a JWS (JSON Web Signature) issued by Apple — used for
/// StoreKit signed transactions/renewal info and App Store Server Notifications v2.
///
/// A JWS is three base64url parts: header.payload.signature. Apple signs with ES256 and puts the
/// signing certificate chain in the header's <c>x5c</c> array (leaf, intermediate(s)). Verification
/// walks that chain to the pinned Apple Root CA - G3 and checks the ES256 signature.
///
/// In Development the chain may be a StoreKitTest local CA that Apple's real servers don't know, so
/// callers may decode without verifying (gated elsewhere by IsDevelopment + #if DEBUG).
/// </summary>
public static class AppleJws
{
    private static readonly Lazy<X509Certificate2> AppleRootCaG3 = new(() =>
        LoadEmbeddedPem("Mavrylo.Resources.Certificates.AppleRootCA-G3.pem"));

    public sealed record DecodeResult(bool Ok, JsonElement Payload, bool SignatureVerified, string? Error)
    {
        public static DecodeResult Fail(string error) => new(false, default, false, error);
    }

    /// <summary>
    /// Decode a JWS. When <paramref name="verifySignature"/> is true the x5c chain is validated to
    /// the Apple Root CA - G3 and the ES256 signature is checked; otherwise only the payload is
    /// parsed (Development convenience).
    /// </summary>
    public static DecodeResult Decode(string jws, bool verifySignature)
    {
        if (string.IsNullOrWhiteSpace(jws))
            return DecodeResult.Fail("empty JWS");

        var parts = jws.Split('.');
        if (parts.Length != 3)
            return DecodeResult.Fail("JWS must have 3 parts");

        JsonElement payload;
        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64Url.Decode(parts[1]));
            payload = JsonDocument.Parse(payloadJson).RootElement.Clone();
        }
        catch (Exception ex)
        {
            return DecodeResult.Fail($"payload parse failed: {ex.Message}");
        }

        if (!verifySignature)
            return new DecodeResult(true, payload, false, null);

        try
        {
            var headerJson = Encoding.UTF8.GetString(Base64Url.Decode(parts[0]));
            using var header = JsonDocument.Parse(headerJson);
            var alg = header.RootElement.TryGetProperty("alg", out var algEl) && algEl.ValueKind == JsonValueKind.String
                ? algEl.GetString()
                : null;
            if (!string.Equals(alg, "ES256", StringComparison.Ordinal))
                return DecodeResult.Fail("JWS alg must be ES256");

            if (!header.RootElement.TryGetProperty("x5c", out var x5cEl) || x5cEl.ValueKind != JsonValueKind.Array || x5cEl.GetArrayLength() == 0)
                return DecodeResult.Fail("JWS header missing x5c chain");

            var chainCerts = new List<X509Certificate2>();
            foreach (var c in x5cEl.EnumerateArray())
                chainCerts.Add(X509CertificateLoader.LoadCertificate(Convert.FromBase64String(c.GetString()!)));

            var leaf = chainCerts[0];
            var intermediates = new X509Certificate2Collection();
            for (int i = 1; i < chainCerts.Count; i++)
                intermediates.Add(chainCerts[i]);

            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(AppleRootCaG3.Value);
            chain.ChainPolicy.ExtraStore.AddRange(intermediates);
            if (!chain.Build(leaf))
            {
                var reasons = string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim()));
                return DecodeResult.Fail($"x5c chain not trusted: {reasons}");
            }

            // Verify ES256 over ASCII(header.payload) using the leaf's public key.
            using var ecdsa = leaf.GetECDsaPublicKey()
                ?? throw new CryptographicException("leaf has no EC public key");
            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var signature = Base64Url.Decode(parts[2]);
            var verified = ecdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            if (!verified)
                return DecodeResult.Fail("JWS signature invalid");

            return new DecodeResult(true, payload, true, null);
        }
        catch (Exception ex)
        {
            return DecodeResult.Fail($"JWS verification error: {ex.Message}");
        }
    }

    private static X509Certificate2 LoadEmbeddedPem(string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded cert not found: '{resourceName}'.");
        using var sr = new StreamReader(stream);
        return X509Certificate2.CreateFromPem(sr.ReadToEnd());
    }
}

/// <summary>Base64url (RFC 4648 §5) decode without padding.</summary>
internal static class Base64Url
{
    public static string Encode(byte[] input)
        => Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static byte[] Decode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
