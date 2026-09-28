using Mavrylo.Services;
using MailKit.Security;
using MimeKit;

namespace Mavrylo.Tests.TestSupport;

internal sealed class FakeSmtpClientFactory : ISmtpClientFactory
{
    public FakeSmtpClient Client { get; } = new();

    public ISmtpClient Create() => Client;
}

internal sealed class FakeSmtpClient : ISmtpClient
{
    public bool Connected { get; private set; }
    public bool Authenticated { get; private set; }
    public bool Disconnected { get; private set; }
    public MimeMessage? SentMessage { get; private set; }

    public Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken ct = default)
    {
        Connected = true;
        return Task.CompletedTask;
    }

    public Task AuthenticateAsync(string userName, string password, CancellationToken ct = default)
    {
        Authenticated = true;
        return Task.CompletedTask;
    }

    public Task SendAsync(MimeMessage message, CancellationToken ct = default)
    {
        SentMessage = message;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(bool quit, CancellationToken ct = default)
    {
        Disconnected = true;
        return Task.CompletedTask;
    }

    public void Dispose() { }
}
