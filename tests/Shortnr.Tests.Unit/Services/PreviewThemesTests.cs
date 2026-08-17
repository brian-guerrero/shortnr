namespace Shortnr.Tests.Unit.Services;

public class PreviewThemesTests
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
    public void IsValid_KnownThemes_ReturnsTrue(string theme)
    {
        Assert.True(PreviewThemes.IsValid(theme));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("brutal")]
    [InlineData("NONE")]
    public void IsValid_UnknownThemes_ReturnsFalse(string? theme)
    {
        Assert.False(PreviewThemes.IsValid(theme));
    }

    [Fact]
    public void All_ContainsEightThemes()
    {
        Assert.Equal(8, PreviewThemes.All.Count);
    }

    [Fact]
    public void Default_IsDefault()
    {
        Assert.Equal("default", PreviewThemes.Default);
    }
}
