namespace Shortnr.Tests.Unit.Services;

public class BioThemesTests
{
    [Theory]
    [InlineData("default")]
    [InlineData("sunset")]
    [InlineData("ocean")]
    [InlineData("forest")]
    [InlineData("midnight")]
    [InlineData("brutal")]
    public void IsValid_AcceptsEveryPresetTheme(string theme)
    {
        Assert.True(BioThemes.IsValid(theme));
    }

    [Fact]
    public void All_KeepsTheFiveSoftThemesAlongsideBrutal()
    {
        Assert.Equal(
            ["default", "sunset", "ocean", "forest", "midnight", "brutal"],
            BioThemes.All);
    }

    [Fact]
    public void IsValid_RejectsUnknownTheme()
    {
        Assert.False(BioThemes.IsValid("neon"));
        Assert.False(BioThemes.IsValid(null));
    }
}
