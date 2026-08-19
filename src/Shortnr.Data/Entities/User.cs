namespace Shortnr.Data.Entities;

public class User
{
    public long Id { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Name { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastLoginAtUtc { get; set; }

    /// <summary>
    /// Persisted app-wide theme choice, an id validated against
    /// <c>IThemeResolver</c> (preset or community). <see langword="null"/>
    /// means "use <c>ThemeCatalog.Default</c>" — most users never write this
    /// column. Distinct from <c>BioPage.Theme</c>/<c>ShortenedUrl.PreviewTheme</c>,
    /// which theme content the user publishes rather than how the app chrome
    /// looks to them.
    /// </summary>
    public string? PreferredTheme { get; set; }

    public ICollection<ShortenedUrl> ShortenedUrls { get; set; } = [];
}
