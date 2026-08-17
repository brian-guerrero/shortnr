namespace Shortnr.Tests.Unit.Services;

public class BioThemesTests
{
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
        Assert.True(BioThemes.IsValid(theme));
    }

    [Fact]
    public void All_ContainsEightThemes()
    {
        Assert.Equal(
            ["default", "sunset", "ocean", "forest", "midnight", "minimal", "corporate", "dark"],
            BioThemes.All);
    }

    [Fact]
    public void IsValid_RejectsUnknownTheme()
    {
        Assert.False(BioThemes.IsValid("neon"));
        Assert.False(BioThemes.IsValid("brutal"));
        Assert.False(BioThemes.IsValid(null));
    }
}
