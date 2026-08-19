using Microsoft.Extensions.Configuration;

namespace Shortnr.Tests.Unit.Services;

/// <summary>
/// Covers the new <see cref="IThemeCatalog"/> plumbing: that
/// <see cref="ThemeCatalog.Instance"/>'s explicit interface members agree with
/// the static members every other caller uses, and that DI resolves both the
/// built-in and community catalogs through <c>IEnumerable&lt;IThemeCatalog&gt;</c>.
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
        var services = new ServiceCollection();
        services.AddThemingFeature(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var catalogs = provider.GetServices<IThemeCatalog>().ToArray();

        Assert.Equal(2, catalogs.Length);
        Assert.Same(ThemeCatalog.Instance, Assert.Single(catalogs, c => c is ThemeCatalog));
        Assert.IsType<CommunityThemeCatalog>(Assert.Single(catalogs, c => c is CommunityThemeCatalog));
    }
}
