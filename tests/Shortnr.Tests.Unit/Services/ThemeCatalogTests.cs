namespace Shortnr.Tests.Unit.Services;

/// <summary>
/// Locks in the invariant the old hand-mirrored BioThemes.All / PreviewThemes.All
/// pair was trying to enforce: bio pages and the redirect-preview page offer the
/// same eight themes, and every catalog entry actually has its palette. Bio and
/// the redirect-preview page share one palette file per theme (both layouts link
/// <c>Theme.PreviewStylesheetPath</c>), so there is only one palette to check —
/// see <see cref="EveryTheme_HasAPalette"/>.
/// </summary>
public class ThemeCatalogTests
{
    private static readonly string[] ExpectedIds =
        ["default", "sunset", "ocean", "forest", "midnight", "minimal", "corporate", "dark"];

    [Fact]
    public void Ids_AreTheEightPresetThemesInOrder()
    {
        Assert.Equal(ExpectedIds, ThemeCatalog.Ids);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("sunset")]
    [InlineData("ocean")]
    [InlineData("forest")]
    [InlineData("midnight")]
    [InlineData("minimal")]
    [InlineData("corporate")]
    [InlineData("dark")]
    public void IsValid_AcceptsEveryPresetTheme(string theme)
    {
        Assert.True(ThemeCatalog.IsValid(theme));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("neon")]
    [InlineData("brutal")]
    [InlineData("NONE")]
    public void IsValid_RejectsUnknownTheme(string? theme)
    {
        Assert.False(ThemeCatalog.IsValid(theme));
    }

    [Fact]
    public void Default_IsTheSharedNeutralPalette()
    {
        Assert.Equal("default", ThemeCatalog.DefaultId);
        Assert.Equal("default", ThemeCatalog.Default.Id);
    }

    [Fact]
    public void Resolve_FallsBackToDefaultForUnknownOrMissingTheme()
    {
        Assert.Equal("sunset", ThemeCatalog.Resolve("sunset").Id);
        Assert.Equal(ThemeCatalog.Default, ThemeCatalog.Resolve("neon"));
        Assert.Equal(ThemeCatalog.Default, ThemeCatalog.Resolve(null));
        Assert.Equal(ThemeCatalog.Default, ThemeCatalog.Resolve(""));
    }

    [Fact]
    public void Themes_HaveDistinctIdsAndLabels()
    {
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.All.Select(t => t.Id).Distinct().Count());
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.All.Select(t => t.Label).Distinct().Count());
        Assert.All(ThemeCatalog.All, t => Assert.False(string.IsNullOrWhiteSpace(t.Label)));
    }

    [Fact]
    public void DarkThemes_AreMidnightAndDark()
    {
        Assert.Equal(
            ["midnight", "dark"],
            ThemeCatalog.All.Where(t => t.IsDark).Select(t => t.Id));
    }

    // --- the shared-vocabulary invariant ---------------------------------
    // Bio and the redirect-preview page both link Theme.PreviewStylesheetPath
    // — the same file, not a palette apiece — so there is exactly one palette
    // per theme to check. This test catches a theme added to the catalog
    // without its CSS.

    [Fact]
    public void EveryTheme_HasAPalette()
    {
        var webRoot = Path.Combine(FindRepoRoot(), "src", "Shortnr.Web", "wwwroot");

        var missing = ThemeCatalog.All
            .Where(t => !File.Exists(Path.Combine(webRoot, t.PreviewStylesheetPath.Replace('/', Path.DirectorySeparatorChar))))
            .Select(t => t.Id)
            .ToArray();

        Assert.Empty(missing);
    }

    /// <summary>
    /// Walks up from the test assembly's output folder to the repo root. The
    /// theme palettes are static assets, so there is nothing compiled to assert
    /// against — the tests have to read the source tree.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Shortnr.Web", "Shortnr.Web.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root above '{AppContext.BaseDirectory}'.");
    }
}
