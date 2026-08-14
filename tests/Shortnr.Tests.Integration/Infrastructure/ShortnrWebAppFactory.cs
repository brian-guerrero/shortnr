using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Shortnr.Data;

namespace Shortnr.Tests.Integration.Infrastructure;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for Shortnr.Web integration tests.
/// <para>
/// Each factory instance owns its own isolated SQLite file so test classes do not
/// share state. The real OIDC middleware is replaced by <see cref="TestAuthHandler"/>
/// as the default authentication scheme; tests control auth state via the
/// <see cref="TestAuthState"/> singleton available from <see cref="Services"/>.
/// </para>
/// </summary>
public class ShortnrWebAppFactory : WebApplicationFactory<Program>
{
    public const string TestIssuer = "http://test.issuer";

    private readonly bool _authEnabled;
    private readonly bool _aiInsightsEnabled;
    private readonly DatabaseProvider? _provider;
    private readonly string? _connectionString;
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"shortnr-test-{Guid.NewGuid():N}.db");

    /// <param name="authEnabled">
    /// Value written to <c>Authentication:Enabled</c> in the test configuration.
    /// Defaults to <c>true</c>.
    /// </param>
    /// <param name="aiInsightsEnabled">
    /// Value written to <c>AiInsights:Enabled</c> in the test configuration.
    /// Defaults to <c>false</c> (the feature is opt-in).
    /// </param>
    /// <param name="provider">
    /// Overrides <c>Database:Provider</c> for this factory instance. Defaults to
    /// <c>null</c>, which leaves the isolated SQLite temp-file behavior below untouched.
    /// Pass <see cref="DatabaseProvider.Postgres"/> with <paramref name="connectionString"/>
    /// to point this factory at a real Postgres instance instead (see
    /// <see cref="PostgresAppHostFixture"/>).
    /// </param>
    /// <param name="connectionString">
    /// The connection string to use when <paramref name="provider"/> is set.
    /// </param>
    public ShortnrWebAppFactory(
        bool authEnabled = true,
        bool aiInsightsEnabled = false,
        DatabaseProvider? provider = null,
        string? connectionString = null)
    {
        _authEnabled = authEnabled;
        _aiInsightsEnabled = aiInsightsEnabled;
        _provider = provider;
        _connectionString = connectionString;
    }

    /// <summary>Creates an <see cref="HttpClient"/> with auto-redirect disabled.</summary>
    public HttpClient CreateClientNoRedirect() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Use Development so OIDC RequireHttpsMetadata stays false.
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = $"DataSource={_dbPath}",
                ["Authentication:Enabled"] = _authEnabled ? "true" : "false",
                ["AiInsights:Enabled"] = _aiInsightsEnabled ? "true" : "false",
                ["Authentication:Oidc:Authority"] = TestIssuer,
                ["Authentication:Oidc:ClientId"] = "test-client",
                ["Authentication:Oidc:ClientSecret"] = "test-secret",
                // Keep public-endpoint rate limits out of the way for most tests;
                // rate-limit tests override these on their own factory.
                ["RateLimiting:Shorten:PerMinute"] = "100000",
                ["RateLimiting:Shorten:PerDay"] = "1000000",
                ["RateLimiting:Redirect:PerMinute"] = "100000",
                ["RateLimiting:Redirect:PerDay"] = "1000000",
            });

            if (_provider is not null)
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = _provider.Value.ToString(),
                    ["Database:ConnectionString"] = _connectionString,
                });
            }
        });

        builder.ConfigureTestServices(services =>
        {
            // Give TestAuthHandler access to per-test auth state.
            services.AddSingleton<TestAuthState>();

            // Register the test scheme alongside whatever AddOidcAuthentication added.
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            // Make the test handler the default for all authentication operations so
            // OIDC is never invoked automatically during tests.
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultSignOutScheme = TestAuthHandler.SchemeName;
            });

            // Provide a static OIDC discovery document so the OIDC middleware can
            // build challenge redirect URLs without making real network calls.
            if (_authEnabled)
            {
                services.PostConfigureAll<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>(
                    options =>
                    {
                        options.ConfigurationManager =
                            new StaticConfigurationManager<OpenIdConnectConfiguration>(
                                new OpenIdConnectConfiguration
                                {
                                    AuthorizationEndpoint = $"{TestIssuer}/authorize",
                                    TokenEndpoint = $"{TestIssuer}/token",
                                    EndSessionEndpoint = $"{TestIssuer}/endsession",
                                });
                    });
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        // SQLite WAL mode keeps the main, -shm and -wal files open briefly
        // after the host shuts down. Silently ignore any locked-file errors;
        // the OS will remove the temp files eventually.
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var path = _dbPath + suffix;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
        }
    }
}
