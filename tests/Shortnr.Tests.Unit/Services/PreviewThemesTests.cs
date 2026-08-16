namespace Shortnr.Tests.Unit.Services;

public class PreviewThemesTests
{
    [Theory]
    [InlineData("minimal")]
    [InlineData("brutal")]
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
    [InlineData("BRUTAL")]
    public void IsValid_UnknownThemes_ReturnsFalse(string? theme)
    {
        Assert.False(PreviewThemes.IsValid(theme));
    }

    [Fact]
    public void All_ContainsFourThemes()
    {
        Assert.Equal(4, PreviewThemes.All.Count);
    }

    [Fact]
    public void Default_IsMinimal()
    {
        Assert.Equal("minimal", PreviewThemes.Default);
    }
}
