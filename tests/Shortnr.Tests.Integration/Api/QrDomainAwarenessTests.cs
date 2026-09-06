using System.Net;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Api;

/// <summary>
/// Verifies QR generation is domain-aware: a link on a verified custom domain
/// produces a QR code encoding that domain's URL (on both the /qr page and the
/// /api/qr PNG endpoint), while default-host links keep the instance host.
/// </summary>
public class QrDomainAwarenessTests : IntegrationTestBase
{
    protected override bool AuthEnabled => false;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var domain = new Domain
        {
            Hostname = "go.example.com",
            IsVerified = true,
            IsDefault = true,
            VerificationToken = "tok-abc",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Domains.Add(domain);
        db.SaveChanges();

        db.ShortenedUrls.AddRange(
            new ShortenedUrl { LongUrl = "https://example.com/default", ShortCode = "default", CreatedAtUtc = DateTime.UtcNow },
            new ShortenedUrl { LongUrl = "https://example.com/custom", ShortCode = "custom", DomainId = domain.Id, CreatedAtUtc = DateTime.UtcNow });
        db.SaveChanges();
    }

    [Fact]
    public async Task QrPage_CustomDomainLink_DisplaysDomainScopedUrl()
    {
        var response = await Factory.CreateClient().GetAsync("/qr/custom");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("http://go.example.com/custom", body);
        Assert.DoesNotContain("localhost/custom", body);
    }

    [Fact]
    public async Task QrPage_DefaultLink_UsesInstanceHost()
    {
        var response = await Factory.CreateClient().GetAsync("/qr/default");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("http://localhost/default", body);
    }

    [Fact]
    public async Task QrPage_UnknownCode_ReturnsNotFound()
    {
        var response = await Factory.CreateClient().GetAsync("/qr/nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task QrApi_CustomDomainLink_EncodesDomainScopedUrl()
    {
        var client = Factory.CreateClient();
        var response = await client.GetAsync("/api/qr/custom");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        var qr = Factory.Services.GetRequiredService<QrService>();
        var expected = qr.GeneratePng("http://go.example.com/custom");
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public async Task QrApi_DefaultLink_EncodesInstanceHostUrl()
    {
        var client = Factory.CreateClient();
        var response = await client.GetAsync("/api/qr/default");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        var qr = Factory.Services.GetRequiredService<QrService>();
        var expected = qr.GeneratePng("http://localhost/default");
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public async Task QrApi_UnknownCode_ReturnsNotFound()
    {
        var response = await Factory.CreateClient().GetAsync("/api/qr/nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
