using System.Security.Cryptography;
using Mavrylo.Data;
using Mavrylo.Dtos;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

/// <summary>App Attest registration and challenge issuance. Logic moved verbatim from
/// DeviceController; the controller keeps attributes, header reads, and HTTP result construction.</summary>
public sealed class AppAttestRegistrationService(
    AppDbContext db,
    ChallengeService challenges,
    IAppAttestVerifier verifier,
    JwtTokenService tokens,
    TimeProvider timeProvider,
    ILogger<AppAttestRegistrationService> logger)
{
    private const string EntitlementFree = "free";
    private const string SimulatorKeyIdPrefix = "SIMULATOR-";

    public sealed record DeviceResult(int Status, object? Body = null);

    public async Task<DeviceResult> BootstrapChallengeAsync(CancellationToken ct)
    {
        var issued = await challenges.IssueBootstrapAsync(ct);
        return new DeviceResult(200, new AppAttestBootstrapChallengeResponse(Convert.ToBase64String(issued.Nonce), issued.ChallengeId));
    }

    public async Task<DeviceResult> RegisterAsync(AppAttestRegisterRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.KeyId))
            return new DeviceResult(400, new { error = "key_id is required" });

        var isSimulator = request.KeyId.StartsWith(SimulatorKeyIdPrefix, StringComparison.Ordinal);

        byte[] publicKeySpki;
        long signCount;
        string environment;

        if (isSimulator)
        {
            // Development/Simulator path: App Attest is unsupported on the Simulator. Accept the
            // SIMULATOR-* keyId without attestation, but ONLY when the dev bypass is enabled
            // (DEBUG build AND ASPNETCORE_ENVIRONMENT=Development). Never in production.
            if (!verifier.IsDevelopmentBypassEnabled)
                return new DeviceResult(401, new { error = "simulator registration not allowed in this environment" });

            publicKeySpki = Array.Empty<byte>(); // no real key on the Simulator
            signCount = 0;
            environment = "development";
            logger.LogInformation("Registering SIMULATOR device {KeyId} (dev bypass).", request.KeyId);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Attestation)
                || string.IsNullOrWhiteSpace(request.Challenge)
                || string.IsNullOrWhiteSpace(request.ChallengeId))
            {
                return new DeviceResult(400, new { error = "attestation, challenge, and challenge_id are required" });
            }

            var attestationBytes = TryFromBase64(request.Attestation);
            var challengeBytes = TryFromBase64(request.Challenge);
            if (attestationBytes is null || challengeBytes is null)
                return new DeviceResult(400, new { error = "attestation and challenge must be base64" });

            var issuedNonce = await challenges.TryConsumeAsync(
                request.ChallengeId,
                ChallengeService.ChallengeKind.Bootstrap,
                ct: ct);
            if (issuedNonce is null)
                return new DeviceResult(401, new { error = "unknown, expired, or reused bootstrap challenge" });

            if (!CryptographicOperations.FixedTimeEquals(issuedNonce, challengeBytes))
                return new DeviceResult(401, new { error = "bootstrap challenge mismatch" });

            var result = verifier.VerifyAttestation(request.KeyId, attestationBytes, challengeBytes);
            if (!result.Ok || result.PublicKeySpki is null)
            {
                logger.LogWarning("Attestation rejected for {KeyId}: {Error}", request.KeyId, result.Error);
                return new DeviceResult(401, new { error = result.Error ?? "attestation failed" });
            }

            publicKeySpki = result.PublicKeySpki;
            signCount = result.SignCount;
            environment = result.Environment;
        }

        var deviceUuid = string.IsNullOrWhiteSpace(request.DeviceUuid)
            ? Guid.NewGuid().ToString()
            : request.DeviceUuid!.Trim();

        var existing = await db.Devices.FirstOrDefaultAsync(d => d.KeyId == request.KeyId, ct);
        if (existing is null)
        {
            db.Devices.Add(new DeviceEntity
            {
                KeyId = request.KeyId,
                PublicKey = publicKeySpki,
                SignCount = signCount,
                DeviceUuid = deviceUuid,
                Environment = environment,
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
                LastSeenAt = timeProvider.GetUtcNow().UtcDateTime,
            });
        }
        else
        {
            // Re-registration with the same key: refresh key material + last seen.
            existing.PublicKey = publicKeySpki;
            existing.SignCount = signCount;
            existing.Environment = environment;
            existing.LastSeenAt = timeProvider.GetUtcNow().UtcDateTime;
            if (!string.IsNullOrWhiteSpace(request.DeviceUuid))
                existing.DeviceUuid = deviceUuid;
        }
        await db.SaveChangesAsync(ct);

        var (token, expiresAt) = tokens.CreateDeviceToken(request.KeyId, EntitlementFree);
        return new DeviceResult(200, new AppAttestRegisterResponse(token, expiresAt.ToUniversalTime().ToString("O")));
    }

    public async Task<DeviceResult> AssertionChallengeAsync(string? keyId, AppAttestAssertionChallengeRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RequestPath))
            return new DeviceResult(400, new { error = "request_path is required" });

        if (string.IsNullOrWhiteSpace(keyId))
            return new DeviceResult(400, new { error = "missing X-App-Attest-Key-Id header" });

        var known = await db.Devices.AnyAsync(d => d.KeyId == keyId, ct);
        if (!known)
            return new DeviceResult(401, new { error = "unknown device key" });

        var issued = await challenges.IssueAssertionAsync(keyId, request.RequestPath, ct);
        return new DeviceResult(200, new AppAttestAssertionChallengeResponse(
            Convert.ToBase64String(issued.Nonce), issued.ChallengeId));
    }

    private static byte[]? TryFromBase64(string s)
    {
        try { return Convert.FromBase64String(s); }
        catch (FormatException) { return null; }
    }
}
