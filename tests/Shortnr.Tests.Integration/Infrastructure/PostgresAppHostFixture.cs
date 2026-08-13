extern alias ShortnrAppHost;

using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Xunit;

namespace Shortnr.Tests.Integration.Infrastructure;

/// <summary>
/// Boots a real Postgres instance via <see cref="DistributedApplicationTestingBuilder"/>
/// against the real <c>Shortnr.AppHost</c> model (<c>db-provider=Postgres</c>), so the parity
/// suite exercises the AppHost's own provisioning wiring, not just a bare container. One
/// instance is shared by the whole "Postgres" xunit collection -- the AppHost is expensive
/// enough to boot that per-test creation would dominate the suite's runtime.
/// </summary>
public class PostgresAppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan Timeout =
        Environment.GetEnvironmentVariable("CI") is not null
            ? TimeSpan.FromMinutes(5)
            : TimeSpan.FromSeconds(60);

    private DistributedApplication? _app;

    public string ConnectionString { get; private set; } = "";

    /// <summary>
    /// False when Postgres/Docker isn't reachable on this machine. Tests should skip their
    /// Postgres-provider case rather than fail when this is false, so a bare `dotnet test`
    /// still passes on a machine without a container runtime -- the Sqlite case still runs.
    /// </summary>
    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            var appHost = await DistributedApplicationTestingBuilder
                .CreateAsync<ShortnrAppHost::Projects.Shortnr_AppHost>(
                    ["Parameters:db-provider=Postgres", "IsTestRun=true"]);
            // ^ must be passed as args to CreateAsync, not set on appHost.Configuration
            // afterwards -- Program.cs's db-provider/IsTestRun branches that shape the
            // resource graph already ran by the time CreateAsync returns. IsTestRun=true
            // means only "postgres-test" + "shortnr-web" start -- no dex/mailpit containers
            // pulled just to get a connection string -- and "postgres-test" is a distinct
            // container/volume from the "postgres" resource dotnet run/aspire run use, so
            // this suite never shares data with (or resets) the local dev database.

            _app = await appHost.BuildAsync().WaitAsync(Timeout);
            await _app.StartAsync().WaitAsync(Timeout);

            using var cts = new CancellationTokenSource(Timeout);
            await _app.ResourceNotifications.WaitForResourceHealthyAsync("postgres-test", cts.Token);

            ConnectionString = await _app.GetConnectionStringAsync("shortnr-db", cts.Token)
                ?? throw new InvalidOperationException("No connection string for 'shortnr-db'.");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseProvider(DatabaseProvider.Postgres, ConnectionString);
            await using var db = new AppDbContext(optionsBuilder.Options);
            await db.Database.MigrateAsync(cts.Token);

            // One-time clean slate: "postgres-test" is ContainerLifetime.Persistent (see
            // Shortnr.AppHost/Program.cs), so it can carry rows across separate test runs on
            // the same machine. Individual tests still use GUID-based codes/domains so they
            // don't collide with each other within a run.
            db.ClickEvents.RemoveRange(db.ClickEvents);
            db.ShortenedUrls.RemoveRange(db.ShortenedUrls);
            db.Domains.RemoveRange(db.Domains);
            db.Users.RemoveRange(db.Users);
            await db.SaveChangesAsync(cts.Token);

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            // No Docker / daemon unreachable on this machine -- degrade to skip rather than
            // fail every Postgres-tagged test. CI's Category=Postgres job (which always has
            // Docker) still exercises this for real.
            IsAvailable = false;
            UnavailableReason = $"Postgres AppHost unavailable: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresAppHostFixture>;
