using Microsoft.AspNetCore.DataProtection;
using Shortnr.Data.Entities;

namespace Shortnr.Tests.Unit.Services;

public class SocialTokenEncryptionTests
{
    private static SocialTokenEncryptionService CreateEncryptionService()
    {
        var provider = DataProtectionProvider.Create("test-app");
        return new SocialTokenEncryptionService(provider);
    }

    [Fact]
    public void Encrypt_ProducesCiphertext()
    {
        var service = CreateEncryptionService();
        var plaintext = "my-secret-oauth-token-abc123";

        var ciphertext = service.Encrypt(plaintext);

        Assert.NotEqual(plaintext, ciphertext);
        Assert.StartsWith("CfDJ", ciphertext);
    }

    [Fact]
    public void Encrypt_Decrypt_RoundTrip_MatchesOriginal()
    {
        var service = CreateEncryptionService();
        var plaintext = "my-secret-oauth-token-abc123";

        var ciphertext = service.Encrypt(plaintext);
        var decrypted = service.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_DifferentInputsProduceDifferentCiphertext()
    {
        var service = CreateEncryptionService();

        var cipher1 = service.Encrypt("token-a");
        var cipher2 = service.Encrypt("token-b");

        Assert.NotEqual(cipher1, cipher2);
    }

    [Fact]
    public void Encrypt_SameInputProducesDifferentCiphertextEachTime()
    {
        var service = CreateEncryptionService();
        var plaintext = "same-token";

        var cipher1 = service.Encrypt(plaintext);
        var cipher2 = service.Encrypt(plaintext);

        // Data Protection uses random IVs, so same plaintext produces different ciphertext
        Assert.NotEqual(cipher1, cipher2);
        // But both decrypt to the same plaintext
        Assert.Equal(plaintext, service.Decrypt(cipher1));
        Assert.Equal(plaintext, service.Decrypt(cipher2));
    }

    [Fact]
    public void IsEncrypted_TrueForCiphertext()
    {
        var service = CreateEncryptionService();
        var ciphertext = service.Encrypt("token");

        Assert.True(SocialTokenEncryptionService.IsEncrypted(ciphertext));
    }

    [Fact]
    public void IsEncrypted_FalseForPlaintext()
    {
        Assert.False(SocialTokenEncryptionService.IsEncrypted("plain-oauth-token"));
        Assert.False(SocialTokenEncryptionService.IsEncrypted("eyJhbGciOiJSUzI1NiJ9"));
        Assert.False(SocialTokenEncryptionService.IsEncrypted(""));
        Assert.False(SocialTokenEncryptionService.IsEncrypted(null!));
    }

    [Fact]
    public void DifferentPurpose_ProducesDifferentProtector()
    {
        var provider = DataProtectionProvider.Create("test-app");
        var service1 = new SocialTokenEncryptionService(provider);

        // A different purpose (e.g., workspace tokens) can't decrypt social tokens
        var protector2 = provider.CreateProtector("shortnr-workspace-tokens");
        var ciphertext = service1.Encrypt("secret");

        var ex = Assert.ThrowsAny<Exception>(() => protector2.Unprotect(ciphertext));
        Assert.NotNull(ex);
    }
}
