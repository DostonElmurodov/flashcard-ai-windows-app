using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Mavrylo.Services;

public sealed class MailKitSmtpClientFactory : ISmtpClientFactory
{
    public ISmtpClient Create() => new MailKitSmtpClientAdapter(new SmtpClient());
}

internal sealed class MailKitSmtpClientAdapter(SmtpClient inner) : ISmtpClient
{
    public Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken ct = default)
        => inner.ConnectAsync(host, port, options, ct);

    public Task AuthenticateAsync(string userName, string password, CancellationToken ct = default)
        => inner.AuthenticateAsync(userName, password, ct);

    public Task SendAsync(MimeMessage message, CancellationToken ct = default)
        => inner.SendAsync(message, ct);

    public Task DisconnectAsync(bool quit, CancellationToken ct = default)
        => inner.DisconnectAsync(quit, ct);

    public void Dispose() => inner.Dispose();
}
