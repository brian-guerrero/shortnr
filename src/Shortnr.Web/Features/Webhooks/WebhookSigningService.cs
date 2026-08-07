using System.Security.Cryptography;
using System.Text;

namespace Shortnr.Web.Features.Webhooks;

public static class WebhookSigningService
{
    public static string GenerateSecret() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static string Sign(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool Verify(string payload, string secret, string signature)
    {
        var expected = Sign(payload, secret);
        return string.Equals(expected, signature, StringComparison.OrdinalIgnoreCase);
    }
}
