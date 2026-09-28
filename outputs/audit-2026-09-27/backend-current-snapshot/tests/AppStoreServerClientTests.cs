using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mavrylo.Tests;

public class AppStoreServerClientTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void SharedClaimAlwaysRejectsUnsignedTransaction(string environment)
    {
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}")));
        var appStore = Client(client, environment, DateTimeOffset.UtcNow);
        var jws = JwtFixture.UnsignedJws(new { originalTransactionId = "claim", productId = "com.mavrylo.monthly", environment = "LocalTesting" });
        Assert.False(appStore.VerifyTransactionForClaim(jws, "device").Ok);
    }
    [Fact]
#if DEBUG
    public void VerifyTransaction_DevelopmentDecodesUnsignedLocalJws()
    {
        var now = DateTimeOffset.Parse("2026-06-13T12:00:00Z");
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}")));
        var appStore = Client(client, "Development", now);
        var jws = JwtFixture.UnsignedJws(new
        {
            originalTransactionId = "otid-1",
            productId = "com.mavrylo.monthly",
            expiresDate = now.AddDays(7).ToUnixTimeMilliseconds(),
            offerType = 1,
            environment = "LocalTesting",
            appAccountToken = "device-from-token"
        });

        var result = appStore.VerifyTransaction(jws, "device-fallback");

        Assert.True(result.Ok);
        Assert.False(result.SignatureVerified);
        Assert.Equal("otid-1", result.Subscription?.OriginalTransactionId);
        Assert.Equal("device-from-token", result.Subscription?.DeviceUuid);
        Assert.True(result.Subscription?.IsTrial);
        Assert.False(result.Subscription?.WasEverPaid);
    }
#else
    public void VerifyTransaction_ReleaseDevelopmentRejectsUnsignedLocalJws()
    {
        var now = DateTimeOffset.Parse("2026-06-13T12:00:00Z");
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}")));
        var appStore = Client(client, "Development", now);
        var jws = JwtFixture.UnsignedJws(new
        {
            originalTransactionId = "otid-1",
            productId = "com.flashcardai.owlai.premium.monthly",
            expiresDate = now.AddDays(7).ToUnixTimeMilliseconds(),
            offerType = 1,
            environment = "LocalTesting",
            appAccountToken = "device-from-token"
        });

        var result = appStore.VerifyTransaction(jws, "device-fallback");

        Assert.False(result.Ok);
        Assert.Contains("ES256", result.Error);
    }
#endif

    [Fact]
    public void VerifyTransaction_ProductionRejectsUnsignedJws()
    {
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}")));
        var appStore = Client(client, "Production", DateTimeOffset.Parse("2026-06-13T12:00:00Z"));
        var jws = JwtFixture.UnsignedJws(new { originalTransactionId = "otid", productId = "product" });

        var result = appStore.VerifyTransaction(jws, "device");

        Assert.False(result.Ok);
        Assert.Contains("ES256", result.Error);
    }

#if DEBUG
    [Fact]
    public void ApplyRenewalInfo_UsesGraceExpiryAndAutoRenewStatus()
    {
        var now = DateTimeOffset.Parse("2026-06-13T12:00:00Z");
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}")));
        var appStore = Client(client, "Development", now);
        var sub = new SubscriptionEntity
        {
            OriginalTransactionId = "otid",
            ProductId = "old",
            ExpiresAt = now.AddDays(1).UtcDateTime,
            AutoRenew = true,
            Status = EntitlementService.Status.Premium
        };
        var renewalJws = JwtFixture.UnsignedJws(new
        {
            autoRenewStatus = 0,
            gracePeriodExpiresDate = now.AddDays(3).ToUnixTimeMilliseconds(),
            productId = "new-product"
        });

        appStore.ApplyRenewalInfo(sub, renewalJws);

        Assert.False(sub.AutoRenew);
        Assert.Equal("new-product", sub.ProductId);
        Assert.Equal(EntitlementService.Status.Grace, sub.Status);
        Assert.Equal(now.AddDays(3).UtcDateTime, sub.ExpiresAt);
    }
#endif

    [Fact]
    public void AppleJwsDecode_ReturnsHelpfulFailuresForMalformedPayloads()
    {
        Assert.Contains("3 parts", AppleJws.Decode("not-a-jws", verifySignature: false).Error);
        Assert.Contains("payload parse failed", AppleJws.Decode("a.b.c", verifySignature: false).Error);
    }

#if DEBUG
    [Fact]
    public void VerifyTransaction_RejectsDisallowedProductId()
    {
        var now = DateTimeOffset.Parse("2026-06-13T12:00:00Z");
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}")));
        var appStore = ClientWith(client, "Development", now, new Dictionary<string, string?>
        {
            ["Apple:AppStoreServer:AllowedProductIds:0"] = "com.flashcardai.owlai.premium.monthly"
        });
        var jws = JwtFixture.UnsignedJws(new
        {
            originalTransactionId = "otid-1",
            productId = "com.evil.unlimited",
            environment = "LocalTesting",
            appAccountToken = "device-1"
        });

        var result = appStore.VerifyTransaction(jws, "device-fallback");

        Assert.False(result.Ok);
        Assert.Contains("productId", result.Error);
    }

    [Fact]
    public void VerifyTransaction_RejectsBundleIdMismatch()
    {
        var now = DateTimeOffset.Parse("2026-06-13T12:00:00Z");
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}")));
        var appStore = ClientWith(client, "Development", now, new Dictionary<string, string?>
        {
            ["Apple:AppStoreServer:BundleId"] = "com.flashcardai.owlai"
        });
        var jws = JwtFixture.UnsignedJws(new
        {
            originalTransactionId = "otid-1",
            productId = "com.flashcardai.owlai.premium.monthly",
            bundleId = "com.someone.else",
            environment = "LocalTesting",
            appAccountToken = "device-1"
        });

        var result = appStore.VerifyTransaction(jws, "device-fallback");

        Assert.False(result.Ok);
        Assert.Contains("bundleId", result.Error);
    }
#endif

    private static AppStoreServerClient Client(HttpClient http, string environment, DateTimeOffset now) =>
        ClientWith(http, environment, now, null);

    private static AppStoreServerClient ClientWith(
        HttpClient http,
        string environment,
        DateTimeOffset now,
        IDictionary<string, string?>? configOverrides) =>
        new(
            http,
            TestConfig.Create(configOverrides),
            new FakeEnvironment(environment),
            NullLogger<AppStoreServerClient>.Instance,
            new ManualTimeProvider(now));
}
