using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shortnr.Web.Features.Theming;

public sealed class ThemeCatalogOptions
{
    public const string SectionName = "Theming";
    public string CommunityCatalogUrl { get; set; } =
        "https://raw.githubusercontent.com/brian-guerrero/shortnr-themes/main/index.json";
}

public sealed record CommunityThemeManifestEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
    [property: JsonPropertyName("sha256")] string Sha256);

public interface ICommunityThemeCatalog
{
    Task<IReadOnlyList<CommunityThemeManifestEntry>> GetThemesAsync(CancellationToken ct = default);
    Task<string?> GetCssAsync(string id, CancellationToken ct = default);
}

/// <summary>Fetches the public manifest and validates downloaded CSS before caching it.</summary>
public sealed class CommunityThemeCatalog(
    HttpClient http,
    IOptions<ThemeCatalogOptions> options,
    ILogger<CommunityThemeCatalog> logger) : ICommunityThemeCatalog, IThemeCatalog
{
    private readonly ConcurrentDictionary<string, string> cssCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim manifestLock = new(1, 1);
    private IReadOnlyList<CommunityThemeManifestEntry>? manifest;

    public async Task<IReadOnlyList<CommunityThemeManifestEntry>> GetThemesAsync(CancellationToken ct = default)
    {
        if (manifest is not null) return manifest;
        await manifestLock.WaitAsync(ct);
        try
        {
            if (manifest is not null) return manifest;
            var entries = await http.GetFromJsonAsync<List<CommunityThemeManifestEntry>>(
                options.Value.CommunityCatalogUrl, ct) ?? [];
            manifest = entries
                .Where(IsSafeManifestEntry)
                .GroupBy(entry => entry.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            return manifest;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Unable to load community theme catalog");
            return [];
        }
        finally { manifestLock.Release(); }
    }

    public async Task<string?> GetCssAsync(string id, CancellationToken ct = default)
    {
        if (!ThemeIdIsSafe(id)) return null;
        var entry = (await GetThemesAsync(ct)).FirstOrDefault(theme => theme.Id == id);
        if (entry is null) return null;
        if (cssCache.TryGetValue(id, out var cached)) return cached;

        try
        {
            var css = await http.GetStringAsync(entry.DownloadUrl, ct);
            var bytes = Encoding.UTF8.GetBytes(css);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(entry.Sha256.ToLowerInvariant())))
                throw new InvalidDataException($"Checksum mismatch for community theme '{id}'");
            if (css.Contains("@import", StringComparison.OrdinalIgnoreCase) ||
                css.Contains("url(", StringComparison.OrdinalIgnoreCase) ||
                !css.Contains($"[data-theme=\"{id}\"]", StringComparison.Ordinal))
                throw new InvalidDataException($"Unsafe CSS rejected for community theme '{id}'");
            cssCache[id] = css;
            return css;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            logger.LogWarning(ex, "Unable to load community theme {ThemeId}", id);
            return null;
        }
    }

    async Task<IReadOnlyList<Theme>> IThemeCatalog.GetThemesAsync(CancellationToken ct) =>
        [.. (await GetThemesAsync(ct)).Select(ToTheme)];

    async Task<Theme?> IThemeCatalog.FindAsync(string? id, CancellationToken ct)
    {
        if (id is null) return null;
        var entry = (await GetThemesAsync(ct)).FirstOrDefault(e => e.Id == id);
        return entry is null ? null : ToTheme(entry);
    }

    async Task<bool> IThemeCatalog.IsValidAsync(string? id, CancellationToken ct) =>
        id is not null && (await GetThemesAsync(ct)).Any(e => e.Id == id);

    // IThemeCatalog.GetCssAsync needs no separate implementation: the public
    // GetCssAsync(string, CancellationToken) above already matches its
    // signature exactly and implicitly satisfies both interfaces.

    /// <summary>
    /// Known limitation: <see cref="CommunityThemeManifestEntry"/> has no
    /// <c>isDark</c> field, so community themes always map to
    /// <c>IsDark: false</c> here. Fixing this needs the remote manifest
    /// schema to grow an <c>isDark</c> field — out of scope until something
    /// actually surfaces community themes in a picker.
    /// </summary>
    private static Theme ToTheme(CommunityThemeManifestEntry entry) => new(
        entry.Id,
        entry.Name,
        IsDark: false,
        Author: entry.Author,
        Description: entry.Description,
        IsCommunity: true);

    private static bool IsSafeManifestEntry(CommunityThemeManifestEntry entry) =>
        ThemeIdIsSafe(entry.Id) && Uri.TryCreate(entry.DownloadUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) &&
        entry.Sha256.Length == 64 && entry.Sha256.All(Uri.IsHexDigit);

    private static bool ThemeIdIsSafe(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 64 &&
        id.All(ch => char.IsLower(ch) || char.IsDigit(ch) || ch == '-') &&
        !id.StartsWith('-') && !id.EndsWith('-') && !id.Contains("--", StringComparison.Ordinal);
}

public static class CommunityThemeEndpoints
{
    public static IEndpointRouteBuilder MapCommunityThemeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/themes/community").WithTags("Themes");
        group.MapGet("", async (ICommunityThemeCatalog catalog, CancellationToken ct) =>
            Results.Ok(await catalog.GetThemesAsync(ct)));
        group.MapGet("/{id}.css", async (string id, ICommunityThemeCatalog catalog, CancellationToken ct) =>
        {
            var css = await catalog.GetCssAsync(id, ct);
            return css is null ? Results.NotFound() : Results.Text(css, "text/css; charset=utf-8");
        });
        return endpoints;
    }
}
