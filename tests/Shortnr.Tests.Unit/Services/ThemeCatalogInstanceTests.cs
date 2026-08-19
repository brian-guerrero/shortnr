using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Shortnr.Tests.Unit.Services;

/// <summary>
/// Covers the new <see cref="IThemeCatalog"/> plumbing: that
/// <see cref="ThemeCatalog.Instance"/>'s explicit interface members agree with
/// the static members every other caller uses, and that DI resolves both the
/// built-in and community catalogs (plus <see cref="IThemeResolver"/> and the
/// startup installer) through <c>AddThemingFeature</c>.
/// Does not touch <see cref="ThemeCatalogTests"/>'s static-API coverage.
/// </summary>
public class ThemeCatalogInstanceTests
{
    [Fact]
    public async Task Instance_GetThemesAsync_MatchesAll()
    {
        var themes = await ThemeCatalog.Instance.GetThemesAsync();

        Assert.Equal(ThemeCatalog.All, themes);
    }

    [Theory]
    [InlineData("sunset")]
    [InlineData("unknown")]
    [InlineData(null)]
    public async Task Instance_FindAsync_MatchesFind(string? id)
    {
        Assert.Equal(ThemeCatalog.Find(id), await ThemeCatalog.Instance.FindAsync(id));
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("nope")]
    public async Task Instance_IsValidAsync_MatchesIsValid(string id)
    {
        Assert.Equal(ThemeCatalog.IsValid(id), await ThemeCatalog.Instance.IsValidAsync(id));
    }

    [Fact]
    public async Task Instance_GetCssAsync_ReturnsNull()
    {
        // Preset CSS ships as static wwwroot files, not through this method.
        Assert.Null(await ThemeCatalog.Instance.GetCssAsync("default"));
    }

    [Fact]
    public void AddThemingFeature_RegistersBothCatalogsAsIThemeCatalog()
    {
        using var provider = BuildThemingProvider();

        var catalogs = provider.GetServices<IThemeCatalog>().ToArray();

        Assert.Equal(2, catalogs.Length);
        Assert.Same(ThemeCatalog.Instance, Assert.Single(catalogs, c => c is ThemeCatalog));
        Assert.IsType<CommunityThemeCatalog>(Assert.Single(catalogs, c => c is CommunityThemeCatalog));
    }

    [Fact]
    public void AddThemingFeature_RegistersThemeResolverAndInstallerService()
    {
        using var provider = BuildThemingProvider();

        Assert.NotNull(provider.GetRequiredService<IThemeResolver>());
        Assert.Contains(
            provider.GetServices<IHostedService>(),
            s => s is CommunityThemeInstallerService);
    }

    private static ServiceProvider BuildThemingProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
        services.AddThemingFeature(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Shortnr.Tests.Unit";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
    }
}

/// <summary>
/// <see cref="ThemeResolver"/> against fake <see cref="IThemeCatalog"/>s —
/// pure in-memory, no network — covering aggregation order and the
/// find-triggers-install coupling described on <see cref="ThemeResolver"/>.
/// </summary>
public class ThemeResolverTests
{
    [Fact]
    public async Task GetAllThemesAsync_ConcatenatesEveryCatalogInOrder()
    {
        var first = new FakeThemeCatalog([new Theme("a", "A", IsDark: false)]);
        var second = new FakeThemeCatalog([new Theme("b", "B", IsDark: false)]);
        var resolver = new ThemeResolver([first, second]);

        var themes = await resolver.GetAllThemesAsync();

        Assert.Equal(["a", "b"], themes.Select(t => t.Id));
        Assert.Equal(0, first.CssRequests);
        Assert.Equal(0, second.CssRequests);
    }

    [Fact]
    public async Task FindAsync_ReturnsFirstCatalogsMatch()
    {
        var withA = new FakeThemeCatalog([new Theme("a", "A", IsDark: false)]);
        var withB = new FakeThemeCatalog([new Theme("b", "B", IsDark: false)]);
        var resolver = new ThemeResolver([withA, withB]);

        var found = await resolver.FindAsync("b");

        Assert.Equal("b", found?.Id);
    }

    [Fact]
    public async Task FindAsync_InstallsCssOnlyForCommunityThemes()
    {
        var preset = new FakeThemeCatalog([new Theme("preset", "Preset", IsDark: false, IsCommunity: false)]);
        var community = new FakeThemeCatalog([new Theme("community", "Community", IsDark: false, IsCommunity: true)]);
        var resolver = new ThemeResolver([preset, community]);

        await resolver.FindAsync("preset");
        await resolver.FindAsync("community");

        Assert.Equal(0, preset.CssRequests);
        Assert.Equal(1, community.CssRequests);
    }

    [Fact]
    public async Task IsValidAsync_And_ResolveAsync_FallBackForUnknownId()
    {
        var resolver = new ThemeResolver([new FakeThemeCatalog([new Theme("a", "A", IsDark: false)])]);

        Assert.False(await resolver.IsValidAsync("missing"));
        Assert.Equal(ThemeCatalog.Default, await resolver.ResolveAsync("missing"));
    }

    private sealed class FakeThemeCatalog(IReadOnlyList<Theme> themes) : IThemeCatalog
    {
        public int CssRequests { get; private set; }

        public Task<IReadOnlyList<Theme>> GetThemesAsync(CancellationToken ct = default) =>
            Task.FromResult(themes);

        public Task<Theme?> FindAsync(string? id, CancellationToken ct = default) =>
            Task.FromResult(themes.FirstOrDefault(t => t.Id == id));

        public Task<bool> IsValidAsync(string? id, CancellationToken ct = default) =>
            Task.FromResult(themes.Any(t => t.Id == id));

        public Task<string?> GetCssAsync(string id, CancellationToken ct = default)
        {
            CssRequests++;
            return Task.FromResult<string?>(null);
        }
    }
}
