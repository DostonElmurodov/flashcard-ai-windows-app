namespace Mavrylo.Services;

public sealed class AiProviderException : Exception
{
    public AiProviderException(string provider, string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Provider = provider;
        StatusCode = statusCode;
    }

    public string Provider { get; }
    public int? StatusCode { get; }
}
