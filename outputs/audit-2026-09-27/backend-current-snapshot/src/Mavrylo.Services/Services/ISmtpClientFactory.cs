using MailKit.Security;
using MimeKit;

namespace Mavrylo.Services;

public interface ISmtpClientFactory
{
    ISmtpClient Create();
}

public interface ISmtpClient : IDisposable
{
    Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken ct = default);
    Task AuthenticateAsync(string userName, string password, CancellationToken ct = default);
    Task SendAsync(MimeMessage message, CancellationToken ct = default);
    Task DisconnectAsync(bool quit, CancellationToken ct = default);
}
