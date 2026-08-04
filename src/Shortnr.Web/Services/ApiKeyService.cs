using System.Security.Cryptography;

namespace Shortnr.Web.Services;

/// <summary>
/// Generates and hashes API keys. Only the SHA-256 hash is ever persisted; the
/// plaintext key is returned exactly once at creation time.
/// </summary>
public static class ApiKeyService
{
    public const string KeyPrefix = "snr_";

    /// <summary>Generates a new <c>snr_</c>-prefixed key with 32 random bytes of entropy.</summary>
    public static string GenerateKey() =>
        KeyPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    /// <summary>Hex SHA-256 of the key — the value persisted in <c>ApiKey.KeyHash</c>.</summary>
    public static string HashKey(string key)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(key);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
