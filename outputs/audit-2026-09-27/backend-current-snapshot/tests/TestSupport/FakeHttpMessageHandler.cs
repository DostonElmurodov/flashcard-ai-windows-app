using System.Net;

namespace Mavrylo.Tests.TestSupport;

internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<CapturedRequest> CapturedRequests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        CapturedRequests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri,
            request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
        return respond(request);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    public sealed record CapturedRequest(
        HttpMethod Method,
        Uri? Uri,
        IReadOnlyDictionary<string, string[]> Headers,
        string? Body);
}
