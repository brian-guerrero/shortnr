using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Tests.Integration.Infrastructure;

/// <summary>
/// Owns the per-test <see cref="ShortnrWebAppFactory"/> and the seeding helpers
/// most integration tests need.
/// <para>
/// The factory gets its own SQLite file per instance and xUnit builds a fresh
/// test-class instance per fact, so tests start from an empty database and do
/// not need to clean up after themselves.
/// </para>
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    /// <summary>Override to run a test class with authentication turned off.</summary>
    protected virtual bool AuthEnabled => true;

    /// <summary>Override to opt a test class into the AI insights feature.</summary>
    protected virtual bool AiInsightsEnabled => false;

    protected ShortnrWebAppFactory Factory { get; private set; } = null!;

    public virtual Task InitializeAsync()
    {
        Factory = new ShortnrWebAppFactory(AuthEnabled, AiInsightsEnabled);
        return Task.CompletedTask;
    }

    public virtual Task DisposeAsync() => Factory.DisposeAsync().AsTask();

    /// <summary>Signs the given subject in for subsequent requests.</summary>
    protected void Authenticate(string subject) =>
        Factory.Services.GetRequiredService<TestAuthState>()
            .SetAuthenticatedUser(subject, ShortnrWebAppFactory.TestIssuer);

    /// <summary>Runs <paramref name="action"/> against a scoped <see cref="AppDbContext"/>.</summary>
    protected async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected Task<long> SeedUserAsync(string subject) =>
        WithDbAsync(async db =>
        {
            var user = new User
            {
                Issuer = ShortnrWebAppFactory.TestIssuer,
                Subject = subject,
                CreatedAtUtc = DateTime.UtcNow,
                LastLoginAtUtc = DateTime.UtcNow
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user.Id;
        });

    protected Task<long> SeedLinkAsync(long? ownerUserId, string shortCode, string longUrl,
        long clicks = 0, long? domainId = null, long? workspaceId = null) =>
        WithDbAsync(async db =>
        {
            var link = new ShortenedUrl
            {
                OwnerUserId = ownerUserId,
                WorkspaceId = workspaceId,
                ShortCode = shortCode,
                LongUrl = longUrl,
                DomainId = domainId,
                ClickCount = clicks,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.ShortenedUrls.Add(link);
            await db.SaveChangesAsync();
            return link.Id;
        });

    protected Task<long> SeedDomainAsync(long ownerUserId, string hostname,
        bool verified = true, bool isDefault = false) =>
        WithDbAsync(async db =>
        {
            var domain = new Domain
            {
                OwnerUserId = ownerUserId,
                Hostname = hostname,
                IsVerified = verified,
                IsDefault = isDefault,
                VerificationToken = "test-token",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Domains.Add(domain);
            await db.SaveChangesAsync();
            return domain.Id;
        });

    protected Task SeedClickAsync(long linkId, string? referer = null, string? country = null,
        string? device = null, string? browser = null, DateTime? at = null) =>
        WithDbAsync(async db =>
        {
            db.ClickEvents.Add(new ClickEvent
            {
                ShortenedUrlId = linkId,
                IpAddress = "1.2.3.4",
                UserAgent = "test",
                Referer = referer ?? "",
                ClickedAtUtc = at ?? DateTime.UtcNow,
                CountryName = country,
                DeviceFamily = device,
                Browser = browser
            });
            await db.SaveChangesAsync();
        });

    protected Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path = "/") =>
        TestForms.GetAntiforgeryTokenAsync(client, path);

    protected static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, string token,
        params (string Name, string Value)[] fields) =>
        TestForms.PostFormAsync(client, path, token, fields);
}
