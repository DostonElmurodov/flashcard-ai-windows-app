using System.Security.Cryptography;
using System.Text;

namespace Mavrylo.Services;

/// <summary>
/// Recomputes the App Attest assertion clientDataHash from the SERVER-RECEIVED request, matching the
/// iOS client contract (version 1). Must hash the raw body the server actually received, so this is
/// called from a resource filter that buffers the body before model binding.
///
/// Preimage (iOS <c>AIIntegrityCoordinator.assertionClientDataHash</c>):
///   clientDataHash = SHA256( challengeBytes || 0x1F || SHA256(requestBody) || 0x1F
///                            || UTF8(requestPath) || 0x1F || UTF8(challengeId) )
/// </summary>
public static class AppAttestClientData
{
    private const byte Separator = 0x1F;

    public static byte[] ComputeAssertionHash(byte[] challengeBytes, byte[] requestBody, string requestPath, string challengeId)
    {
        var bodyHash = SHA256.HashData(requestBody);
        using var ms = new MemoryStream();
        ms.Write(challengeBytes);
        ms.WriteByte(Separator);
        ms.Write(bodyHash);
        ms.WriteByte(Separator);
        ms.Write(Encoding.UTF8.GetBytes(requestPath));
        ms.WriteByte(Separator);
        ms.Write(Encoding.UTF8.GetBytes(challengeId));
        return SHA256.HashData(ms.ToArray());
    }
}
