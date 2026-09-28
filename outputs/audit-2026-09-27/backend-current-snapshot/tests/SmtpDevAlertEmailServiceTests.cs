using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mavrylo.Tests;

public class SmtpDevAlertEmailServiceTests
{
    [Fact]
    public async Task NotifyAiProvidersFailedAsync_SendsConfiguredEmailAndAuthenticatesWhenCredentialsExist()
    {
        var smtp = new FakeSmtpClientFactory();
        var service = Service(smtp, DateTimeOffset.Parse("2026-06-13T12:00:00Z"));

        await service.NotifyAiProvidersFailedAsync(
            "word-detail",
            "/owlai/ai/word-detail",
            [new AiProviderFailure("OpenAI", "bad gateway", 502), new AiProviderFailure("Gemini", "timeout")]);

        Assert.True(smtp.Client.Connected);
        Assert.True(smtp.Client.Authenticated);
        Assert.True(smtp.Client.Disconnected);
        Assert.NotNull(smtp.Client.SentMessage);
        Assert.Equal("FlashCard AI provider failure: word-detail", smtp.Client.SentMessage!.Subject);
        var body = smtp.Client.SentMessage.TextBody;
        Assert.Contains("Request path: /owlai/ai/word-detail", body);
        Assert.Contains("- OpenAI: status=502 bad gateway", body);
        Assert.Contains("- Gemini: timeout", body);
    }

    [Fact]
    public async Task NotifyAiProvidersFailedAsync_RespectsCooldown()
    {
        var smtp = new FakeSmtpClientFactory();
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-13T12:00:00Z"));
        var service = Service(smtp, time);

        await service.NotifyAiProvidersFailedAsync("word-detail", "/one", [new AiProviderFailure("OpenAI", "failed")]);
        var first = smtp.Client.SentMessage;
        await service.NotifyAiProvidersFailedAsync("word-detail", "/two", [new AiProviderFailure("OpenAI", "failed again")]);

        Assert.Same(first, smtp.Client.SentMessage);
    }

    [Fact]
    public async Task NotifyAiProvidersFailedAsync_DoesNotCreateSmtpClientWhenConfigIncomplete()
    {
        var smtp = new CountingSmtpClientFactory();
        var service = new SmtpDevAlertEmailService(
            TestConfig.Create(new Dictionary<string, string?> { ["Smtp:Host"] = "", ["AlertEmail:To"] = "dev@example.com" }),
            new AiAlertCooldown(),
            smtp,
            TimeProvider.System,
            NullLogger<SmtpDevAlertEmailService>.Instance);

        await service.NotifyAiProvidersFailedAsync("word-detail", "/path", [new AiProviderFailure("OpenAI", "failed")]);

        Assert.Equal(0, smtp.CreateCalls);
    }

    private static SmtpDevAlertEmailService Service(FakeSmtpClientFactory smtp, DateTimeOffset now) =>
        Service(smtp, new ManualTimeProvider(now));

    private static SmtpDevAlertEmailService Service(FakeSmtpClientFactory smtp, TimeProvider timeProvider) =>
        new(
            TestConfig.Create(new Dictionary<string, string?>
            {
                ["Smtp:Host"] = "smtp.example.com",
                ["Smtp:Port"] = "587",
                ["Smtp:Username"] = "user",
                ["Smtp:Password"] = "password",
                ["Smtp:From"] = "alerts@example.com",
                ["Smtp:EnableSsl"] = "true",
                ["AlertEmail:To"] = "dev@example.com"
            }),
            new AiAlertCooldown(),
            smtp,
            timeProvider,
            NullLogger<SmtpDevAlertEmailService>.Instance);

    private sealed class CountingSmtpClientFactory : ISmtpClientFactory
    {
        public int CreateCalls { get; private set; }

        public ISmtpClient Create()
        {
            CreateCalls += 1;
            return new FakeSmtpClient();
        }
    }
}
