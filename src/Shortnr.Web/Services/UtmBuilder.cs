using Microsoft.AspNetCore.WebUtilities;
using Shortnr.Web.Models;

namespace Shortnr.Web.Services;

/// <summary>
/// Appends UTM campaign parameters onto a destination URL before it is stored.
/// Values the user supplied are merged over any query parameters already present
/// in the URL (the form is the source of truth for a populated field); fields the
/// user left blank never touch existing query parameters.
/// </summary>
public static class UtmBuilder
{
    /// <summary>
    /// Returns <paramref name="url"/> with the non-empty UTM components appended as
    /// query parameters. The URL is returned unchanged when <paramref name="utm"/>
    /// is null or empty.
    /// </summary>
    public static string AppendUtm(string url, UtmParameters? utm)
    {
        if (utm is null || utm.IsEmpty)
            return url;

        var builder = new UriBuilder(url);
        var values = QueryHelpers.ParseQuery(builder.Query)
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

        SetIfPresent(values, "utm_source", utm.Source);
        SetIfPresent(values, "utm_medium", utm.Medium);
        SetIfPresent(values, "utm_campaign", utm.Campaign);
        SetIfPresent(values, "utm_term", utm.Term);
        SetIfPresent(values, "utm_content", utm.Content);

        builder.Query = QueryHelpers.AddQueryString("", values).TrimStart('?');
        return builder.Uri.ToString();
    }

    private static void SetIfPresent(Dictionary<string, string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values[key] = value;
    }
}
