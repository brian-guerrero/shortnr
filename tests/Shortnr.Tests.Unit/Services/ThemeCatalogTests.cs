namespace Shortnr.Tests.Unit.Services;

/// <summary>
/// Locks in the invariant the old hand-mirrored BioThemes.All / PreviewThemes.All
/// pair was trying to enforce: bio pages and the redirect-preview page offer the
/// same eight themes, and every catalog entry actually has both of its palettes.
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
    // Both surfaces render a theme from the same catalog entry, so a theme can
    // only be offered on one of them if its palette is missing. These two tests
    // catch that: add a theme to the catalog without its CSS and they fail.

    [Fact]
    public void EveryTheme_HasARedirectPreviewPalette()
    {
        var webRoot = Path.Combine(FindRepoRoot(), "src", "Shortnr.Web", "wwwroot");

        var missing = ThemeCatalog.All
            .Where(t => !File.Exists(Path.Combine(webRoot, t.PreviewStylesheetPath.Replace('/', Path.DirectorySeparatorChar))))
            .Select(t => t.Id)
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryTheme_HasABioPalette()
    {
        var layout = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Shortnr.Web", "Pages", "Bio", "_BioLayout.cshtml"));

        var missing = ThemeCatalog.All
            .Where(t => !layout.Contains($"[data-bio-theme=\"{t.Id}\"]", StringComparison.Ordinal))
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
