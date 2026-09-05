using Microsoft.AspNetCore.DataProtection;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// Encrypts and decrypts social-account OAuth tokens at rest using ASP.NET Data Protection.
/// Uses a dedicated purpose string ("shortnr-social-tokens") to isolate social-token
/// encryption from other Data Protection uses (auth cookies, workspace tokens, etc.).
/// </summary>
public sealed class SocialTokenEncryptionService(IDataProtectionProvider dataProtectionProvider)
{
    private const string Purpose = "shortnr-social-tokens";

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(Purpose);

    /// <summary>
    /// Encrypts a plaintext token and returns the ciphertext string.
    /// </summary>
    public string Encrypt(string plaintext) =>
        _protector.Protect(plaintext);

    /// <summary>
    /// Decrypts a ciphertext string back to the original plaintext.
    /// </summary>
    public string Decrypt(string ciphertext) =>
        _protector.Unprotect(ciphertext);

    /// <summary>
    /// Returns true if the value looks like ciphertext (base64-encoded DPAPI payload).
    /// Used by the migration to distinguish already-encrypted rows from plaintext.
    /// </summary>
    public static bool IsEncrypted(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Data Protection produces payloads that start with "CfDJ" (the version header).
        // Plaintext tokens from OAuth providers never start with this prefix.
        return value.StartsWith("CfDJ", StringComparison.Ordinal);
    }
}
