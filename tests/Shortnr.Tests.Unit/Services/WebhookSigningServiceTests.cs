
namespace Shortnr.Tests.Unit.Services;

public class WebhookSigningServiceTests
{
    [Fact]
    public void GenerateSecret_Returns64CharHexString()
    {
        var secret = WebhookSigningService.GenerateSecret();

        Assert.Equal(64, secret.Length);
        Assert.Matches("^[0-9a-f]{64}$", secret);
    }

    [Fact]
    public void GenerateSecret_ReturnsUniqueValues()
    {
        var secret1 = WebhookSigningService.GenerateSecret();
        var secret2 = WebhookSigningService.GenerateSecret();

        Assert.NotEqual(secret1, secret2);
    }

    [Fact]
    public void Sign_ReturnsExpectedFormat()
    {
        var payload = "{\"test\":\"data\"}";
        var secret = "test-secret";

        var signature = WebhookSigningService.Sign(payload, secret);

        Assert.StartsWith("sha256=", signature);
        Assert.Equal(71, signature.Length);
    }

    [Fact]
    public void Sign_IsDeterministic()
    {
        var payload = "{\"test\":\"data\"}";
        var secret = "test-secret";

        var sig1 = WebhookSigningService.Sign(payload, secret);
        var sig2 = WebhookSigningService.Sign(payload, secret);

        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void Sign_DifferentPayloadsProduceDifferentSignatures()
    {
        var secret = "test-secret";
        var payload1 = "{\"test\":\"data1\"}";
        var payload2 = "{\"test\":\"data2\"}";

        var sig1 = WebhookSigningService.Sign(payload1, secret);
        var sig2 = WebhookSigningService.Sign(payload2, secret);

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void Sign_DifferentSecretsProduceDifferentSignatures()
    {
        var payload = "{\"test\":\"data\"}";
        var secret1 = "secret1";
        var secret2 = "secret2";

        var sig1 = WebhookSigningService.Sign(payload, secret1);
        var sig2 = WebhookSigningService.Sign(payload, secret2);

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void Verify_ValidSignature_ReturnsTrue()
    {
        var payload = "{\"test\":\"data\"}";
        var secret = "test-secret";
        var signature = WebhookSigningService.Sign(payload, secret);

        var isValid = WebhookSigningService.Verify(payload, secret, signature);

        Assert.True(isValid);
    }

    [Fact]
    public void Verify_InvalidSignature_ReturnsFalse()
    {
        var payload = "{\"test\":\"data\"}";
        var secret = "test-secret";
        var signature = "sha256=invalid";

        var isValid = WebhookSigningService.Verify(payload, secret, signature);

        Assert.False(isValid);
    }

    [Fact]
    public void Verify_TamperedPayload_ReturnsFalse()
    {
        var secret = "test-secret";
        var originalPayload = "{\"test\":\"data\"}";
        var tamperedPayload = "{\"test\":\"tampered\"}";
        var signature = WebhookSigningService.Sign(originalPayload, secret);

        var isValid = WebhookSigningService.Verify(tamperedPayload, secret, signature);

        Assert.False(isValid);
    }

    [Fact]
    public void Verify_IsCaseInsensitive()
    {
        var payload = "{\"test\":\"data\"}";
        var secret = "test-secret";
        var signature = WebhookSigningService.Sign(payload, secret);
        var upperSignature = signature.ToUpperInvariant();

        var isValid = WebhookSigningService.Verify(payload, secret, upperSignature);

        Assert.True(isValid);
    }
}
