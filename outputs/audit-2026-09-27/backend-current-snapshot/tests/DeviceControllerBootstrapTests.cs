using Mavrylo.Areas.OwlAI.Controllers;
using Mavrylo.Dtos;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mavrylo.Tests;

public class DeviceControllerBootstrapTests
{
    private const string RealKeyId = "real-device-key-abc";

    [Fact]
    public async Task Register_RealDevice_RequiresChallengeId()
    {
        using var testDb = TestDb.Create();
        var controller = CreateController(testDb, out _);
        var request = new AppAttestRegisterRequest(
            RealKeyId,
            Attestation: B64(1, 2, 3),
            Challenge: B64(4, 5, 6),
            ChallengeId: null);

        var result = await controller.Register(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, await testDb.Db.Devices.CountAsync());
    }

    [Fact]
    public async Task Register_RealDevice_RejectsUnknownChallengeId()
    {
        using var testDb = TestDb.Create();
        var controller = CreateController(testDb, out _);
        var request = new AppAttestRegisterRequest(
            RealKeyId,
            Attestation: B64(1, 2, 3),
            Challenge: B64(4, 5, 6),
            ChallengeId: "not-a-real-challenge");

        var result = await controller.Register(request, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(0, await testDb.Db.Devices.CountAsync());
    }

    [Fact]
    public async Task Register_RealDevice_RejectsChallengeByteMismatch()
    {
        using var testDb = TestDb.Create();
        var controller = CreateController(testDb, out var challenges);
        var issued = await challenges.IssueBootstrapAsync();
        var request = new AppAttestRegisterRequest(
            RealKeyId,
            Attestation: B64(1, 2, 3),
            Challenge: Convert.ToBase64String([9, 9, 9, 9]), // not the issued nonce
            ChallengeId: issued.ChallengeId);

        var result = await controller.Register(request, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(0, await testDb.Db.Devices.CountAsync());
    }

    [Fact]
    public async Task Register_RealDevice_HappyPath_RegistersDeviceAndReturnsToken()
    {
        using var testDb = TestDb.Create();
        var controller = CreateController(testDb, out var challenges);
        var issued = await challenges.IssueBootstrapAsync();
        var request = new AppAttestRegisterRequest(
            RealKeyId,
            Attestation: B64(1, 2, 3),
            Challenge: Convert.ToBase64String(issued.Nonce),
            ChallengeId: issued.ChallengeId);

        var result = await controller.Register(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AppAttestRegisterResponse>(ok.Value);
        Assert.False(string.IsNullOrWhiteSpace(response.DeviceToken));
        var device = await testDb.Db.Devices.SingleAsync();
        Assert.Equal(RealKeyId, device.KeyId);
        Assert.Equal("production", device.Environment);
    }

    [Fact]
    public async Task Register_RealDevice_RejectsBootstrapChallengeReuse()
    {
        using var testDb = TestDb.Create();
        var controller = CreateController(testDb, out var challenges);
        var issued = await challenges.IssueBootstrapAsync();
        var request = new AppAttestRegisterRequest(
            RealKeyId,
            Attestation: B64(1, 2, 3),
            Challenge: Convert.ToBase64String(issued.Nonce),
            ChallengeId: issued.ChallengeId);

        var first = await controller.Register(request, CancellationToken.None);
        Assert.IsType<OkObjectResult>(first);

        // Same single-use challenge id replayed (e.g. a captured registration request).
        var replay = await controller.Register(request, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(replay);
    }

    [Theory]
    [InlineData("development")]
    [InlineData("production")]
    public async Task Register_PersistsVerifiedEnvironment(string environment)
    {
        using var testDb = TestDb.Create();
        var verifier = new FakeAppAttestVerifier
        {
            AttestationResult = AppAttestVerifier.AttestationResult.Success([1, 2, 3], 0, environment)
        };
        var controller = CreateController(testDb, out var challenges, verifier);
        var issued = await challenges.IssueBootstrapAsync();
        var request = new AppAttestRegisterRequest(RealKeyId, B64(1, 2, 3),
            Convert.ToBase64String(issued.Nonce), issued.ChallengeId);

        Assert.IsType<OkObjectResult>(await controller.Register(request, CancellationToken.None));

        Assert.Equal(environment, (await testDb.Db.Devices.SingleAsync()).Environment);
    }

    private static DeviceController CreateController(TestDb testDb, out ChallengeService challenges, FakeAppAttestVerifier? suppliedVerifier = null)
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-13T12:00:00Z"));
        challenges = new ChallengeService(testDb.Db, time);
        var verifier = suppliedVerifier ?? new FakeAppAttestVerifier { IsDevelopmentBypassEnabled = true };
        var tokens = new JwtTokenService(TestConfig.Create(), time);
        return new DeviceController(new AppAttestRegistrationService(
            testDb.Db,
            challenges,
            verifier,
            tokens,
            time,
            NullLogger<AppAttestRegistrationService>.Instance));
    }

    private static string B64(params byte[] bytes) => Convert.ToBase64String(bytes);
}
