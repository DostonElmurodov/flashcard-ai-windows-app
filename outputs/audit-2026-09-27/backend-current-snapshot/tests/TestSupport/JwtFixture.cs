using System.Text;
using System.Text.Json;

namespace Mavrylo.Tests.TestSupport;

internal static class JwtFixture
{
    public static string UnsignedJws(object payload)
    {
        var header = Base64Url.Encode(Encoding.UTF8.GetBytes("""{"alg":"none"}"""));
        var body = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{header}.{body}.signature";
    }

    private static class Base64Url
    {
        public static string Encode(byte[] value) =>
            Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
