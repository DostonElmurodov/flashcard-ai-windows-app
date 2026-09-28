using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Xunit;

namespace Mavrylo.Tests;

public class EntitlementServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 13, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, false, false, null, "premium")]
    [InlineData(1, true, false, null, "trial")]
    [InlineData(1, false, true, null, "premium")]
    [InlineData(1, false, true, "grace", "grace")]
    [InlineData(-1, true, false, null, "expired_trial")]
    [InlineData(-1, false, true, null, "expired_paid")]
    [InlineData(1, false, true, "revoked", "revoked")]
    public void ComputeStatus_CoversCanonicalStates(int? expiryDays, bool isTrial, bool wasEverPaid, string? marker, string expected)
    {
        var sub = new SubscriptionEntity
        {
            OriginalTransactionId = "otid",
            ProductId = "product",
            ExpiresAt = expiryDays is null ? null : Now.AddDays(expiryDays.Value),
            IsTrial = isTrial,
            WasEverPaid = wasEverPaid,
            Status = marker == "grace" ? EntitlementService.Status.Grace : EntitlementService.Status.Free,
            RevokedAt = marker == "revoked" ? Now : null
        };

        Assert.Equal(expected, EntitlementService.ComputeStatus(sub, Now));
    }

    [Fact]
    public async Task Upsert_KeepsWasEverPaidSticky_AndRelinksDevice()
    {
        using var testDb = TestDb.Create();
        var time = new ManualTimeProvider(new DateTimeOffset(Now));
        var service = new EntitlementService(testDb.Db, time);

        await service.UpsertAsync(new SubscriptionEntity
        {
            OriginalTransactionId = "otid",
            DeviceUuid = "device-a",
            ProductId = "monthly",
            ExpiresAt = Now.AddDays(10),
            IsTrial = false,
            WasEverPaid = true
        });

        await service.UpsertAsync(new SubscriptionEntity
        {
            OriginalTransactionId = "otid",
            DeviceUuid = "device-b",
            ProductId = "monthly",
            ExpiresAt = Now.AddDays(-1),
            IsTrial = true,
            WasEverPaid = false
        });

        var sub = await service.FindByOriginalTransactionAsync("otid");
        Assert.NotNull(sub);
        Assert.True(sub!.WasEverPaid);
        Assert.Equal("device-b", sub.DeviceUuid);
        Assert.Equal(EntitlementService.Status.ExpiredPaid, service.ToEntitlement(sub).Status);
    }
}
