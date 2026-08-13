using System.Net;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Data;

/// <summary>
/// Focused parity suite: behavior that genuinely differs by database provider, run against
/// both Sqlite (the default, always available) and Postgres (provisioned via the AppHost by
/// <see cref="PostgresAppHostFixture"/>, requires Docker). This is not a replacement for the
/// broader single-provider integration suite -- it targets the specific concerns the two
/// providers could plausibly disagree on: the filtered unique index on
/// <c>(DomainId, ShortCode)</c>, timestamp stamping, and one end-to-end round trip through the
/// real HTTP pipeline.
/// <para>
/// Each Postgres-row test case calls <see cref="Skip.If"/> first, so this suite degrades
/// gracefully (not a failure) on a machine without Docker -- the Sqlite row still runs.
/// </para>
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Postgres")]
public class DatabaseProviderParityTests(PostgresAppHostFixture PostgresFixture)
{
    public static TheoryData<DatabaseProvider> Providers => new()
    {
        DatabaseProvider.Sqlite,
        DatabaseProvider.Postgres,
    };

    private ShortnrWebAppFactory CreateFactory(DatabaseProvider provider)
    {
        if (provider == DatabaseProvider.Postgres)
        {
            Skip.If(!PostgresFixture.IsAvailable, PostgresFixture.UnavailableReason);
            return new ShortnrWebAppFactory(authEnabled: false,
                provider: DatabaseProvider.Postgres, connectionString: PostgresFixture.ConnectionString);
        }

        return new ShortnrWebAppFactory(authEnabled: false);
    }

    private static string UniqueCode() => $"pt{Guid.NewGuid():N}"[..10];

    private static string UniqueHostname() => $"{Guid.NewGuid():N}.example.com";

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task DuplicateShortCode_NullDomainId_SecondSaveThrows(DatabaseProvider provider)
    {
        await using var factory = CreateFactory(provider);
        var code = UniqueCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrls.Add(new ShortenedUrl { LongUrl = "https://example.com/one", ShortCode = code });
            await db.SaveChangesAsync();
        }

        using var secondScope = factory.Services.CreateScope();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();
        secondDb.ShortenedUrls.Add(new ShortenedUrl { LongUrl = "https://example.com/two", ShortCode = code });

        // Exercises the filtered-NULL half of the unique index on (DomainId, ShortCode) --
        // the exact index whose HasFilter bracket-quote syntax was invalid on Postgres.
        await Assert.ThrowsAsync<DbUpdateException>(() => secondDb.SaveChangesAsync());
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task SameShortCode_OnDifferentDomains_BothSucceed(DatabaseProvider provider)
    {
        await using var factory = CreateFactory(provider);
        var code = UniqueCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var domainA = new Domain
        {
            Hostname = UniqueHostname(), IsVerified = true, VerificationToken = "tok-a"
        };
        var domainB = new Domain
        {
            Hostname = UniqueHostname(), IsVerified = true, VerificationToken = "tok-b"
        };
        db.Domains.AddRange(domainA, domainB);
        await db.SaveChangesAsync();

        db.ShortenedUrls.AddRange(
            new ShortenedUrl { LongUrl = "https://example.com/a", ShortCode = code, DomainId = domainA.Id },
            new ShortenedUrl { LongUrl = "https://example.com/b", ShortCode = code, DomainId = domainB.Id });

        // Exercises the filtered-NOT-NULL half of the unique index -- confirms the filter is
        // per-domain, not global, on both providers.
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.ShortenedUrls.CountAsync(u => u.ShortCode == code));
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task CreatedAtUtc_IsStampedByAppDbContext_NotDatabaseDefault(DatabaseProvider provider)
    {
        await using var factory = CreateFactory(provider);
        var code = UniqueCode();
        var before = DateTime.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ShortenedUrls.Add(new ShortenedUrl { LongUrl = "https://example.com/stamped", ShortCode = code });
            await db.SaveChangesAsync();
        }

        using var readScope = factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await readDb.ShortenedUrls.SingleAsync(u => u.ShortCode == code);

        Assert.NotEqual(default, saved.CreatedAtUtc);
        Assert.InRange(saved.CreatedAtUtc, before.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task ShortenThenRedirect_RoundTrips(DatabaseProvider provider)
    {
        await using var factory = CreateFactory(provider);
        var code = UniqueCode();
        long linkId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = new ShortenedUrl { LongUrl = "https://example.com/roundtrip", ShortCode = code };
            db.ShortenedUrls.Add(link);
            await db.SaveChangesAsync();
            linkId = link.Id;
        }

        var client = factory.CreateClientNoRedirect();
        var response = await client.GetAsync($"/{code}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://example.com/roundtrip", response.Headers.Location?.ToString());

        await WaitForClickAsync(factory, linkId);
    }

    /// <summary>Polls until the redirect endpoint's background ClickBatchProcessor has
    /// applied the click (it batches DB writes off the request path).</summary>
    private static async Task WaitForClickAsync(ShortnrWebAppFactory factory, long linkId, int timeoutMs = 5000)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var clickCount = await db.ShortenedUrls
                .Where(u => u.Id == linkId)
                .Select(u => u.ClickCount)
                .SingleAsync();
            if (clickCount > 0)
                return;
            await Task.Delay(100);
        }
        Assert.Fail($"ClickCount for link {linkId} was not incremented within {timeoutMs}ms.");
    }
}
