using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Mavrylo.Services;

public class SmtpDevAlertEmailService(
    IConfiguration config,
    AiAlertCooldown cooldown,
    ISmtpClientFactory smtpClientFactory,
    TimeProvider timeProvider,
    ILogger<SmtpDevAlertEmailService> log) : IDevAlertEmailService
{
    public async Task NotifyAiProvidersFailedAsync(
        string operationName,
        string requestPath,
        IReadOnlyList<AiProviderFailure> failures,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        if (!TryReadConfig(out var settings))
        {
            log.LogWarning("AI provider alert email disabled; SMTP or AlertEmail config is incomplete");
            return;
        }

        if (!cooldown.TryClaim(operationName, now))
        {
            log.LogInformation("AI provider alert email skipped for {Operation}; cooldown is active", operationName);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.From));
        message.To.Add(MailboxAddress.Parse(settings.To));
        message.Subject = $"FlashCard AI provider failure: {operationName}";
        message.Body = new TextPart("plain")
        {
            Text = BuildBody(operationName, requestPath, failures, now)
        };

        try
        {
            using var smtp = smtpClientFactory.Create();
            var secureOptions = settings.EnableSsl ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.Auto;
            await smtp.ConnectAsync(settings.Host, settings.Port, secureOptions, ct);
            if (!string.IsNullOrWhiteSpace(settings.Username) && !string.IsNullOrWhiteSpace(settings.Password))
                await smtp.AuthenticateAsync(settings.Username, settings.Password, ct);
            await smtp.SendAsync(message, ct);
            await smtp.DisconnectAsync(true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "AI provider alert email failed for {Operation}", operationName);
        }
    }

    private bool TryReadConfig(out SmtpSettings settings)
    {
        settings = new SmtpSettings(
            config["Smtp:Host"]?.Trim() ?? "",
            config.GetValue("Smtp:Port", 0),
            config["Smtp:Username"]?.Trim(),
            config["Smtp:Password"],
            config["Smtp:From"]?.Trim() ?? "",
            config.GetValue("Smtp:EnableSsl", true),
            config["AlertEmail:To"]?.Trim() ?? "");

        if (string.IsNullOrWhiteSpace(settings.Host)
            || settings.Port <= 0
            || string.IsNullOrWhiteSpace(settings.From)
            || string.IsNullOrWhiteSpace(settings.To))
            return false;

        return string.IsNullOrWhiteSpace(settings.Username) || !string.IsNullOrWhiteSpace(settings.Password);
    }

    private static string BuildBody(
        string operationName,
        string requestPath,
        IReadOnlyList<AiProviderFailure> failures,
        DateTimeOffset now)
    {
        var lines = new List<string>
        {
            "FlashCard AI could not complete an AI request after all configured providers failed.",
            "",
            $"Operation: {operationName}",
            $"Request path: {requestPath}",
            $"UTC time: {now:O}",
            "",
            "Provider failures:"
        };

        foreach (var failure in failures)
        {
            var status = failure.StatusCode.HasValue ? $" status={failure.StatusCode.Value}" : "";
            lines.Add($"- {failure.Provider}:{status} {failure.Summary}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private sealed record SmtpSettings(
        string Host,
        int Port,
        string? Username,
        string? Password,
        string From,
        bool EnableSsl,
        string To);
}
