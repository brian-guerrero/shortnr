using System.Text.RegularExpressions;

namespace Shortnr.Web.Services;

/// <summary>
/// Shared short-code / vanity-slug rules used by both the Index page and the
/// public API so validation and generation stay consistent.
/// </summary>
public static partial class ShortLinkCodes
{
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$")]
    private static partial Regex SlugPattern();

    /// <summary>Validates a user-supplied vanity slug (1–64 chars, alnum/_/-).</summary>
    public static bool IsValidSlug(string slug) => SlugPattern().IsMatch(slug);

    /// <summary>Generates a random 6-char code from the alphanumeric alphabet.</summary>
    public static string GenerateCode()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return string.Create(6, chars, (span, c) =>
        {
            var random = Random.Shared;
            for (var i = 0; i < span.Length; i++)
                span[i] = c[random.Next(c.Length)];
        });
    }

    /// <summary>
    /// Returns a code guaranteed not to exist per <paramref name="existsAsync"/>.
    /// Retries up to 10 generated codes, then falls back to a 12-char Guid-derived
    /// code that sits outside the 6-char generated space.
    /// </summary>
    public static async Task<string> GenerateUniqueCodeAsync(Func<string, Task<bool>> existsAsync)
    {
        for (var i = 0; i < 10; i++)
        {
            var candidate = GenerateCode();
            if (!await existsAsync(candidate))
                return candidate;
        }

        return Guid.NewGuid().ToString("N")[..12];
    }
}
