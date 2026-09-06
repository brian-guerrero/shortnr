using System.Text.RegularExpressions;

namespace Shortnr.Tests.Integration.Infrastructure;

/// <summary>
/// Antiforgery-aware form posting. Razor Pages reject a POST without a token
/// lifted from a prior GET, so every page test needs this pair.
/// </summary>
public static partial class TestForms
{
    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryToken();

    /// <summary>GETs <paramref name="path"/> and returns its antiforgery token.</summary>
    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path = "/")
    {
        var page = await client.GetAsync(path);
        var html = await page.Content.ReadAsStringAsync();
        var match = AntiforgeryToken().Match(html);
        Assert.True(match.Success, $"Antiforgery token not found in {path}.");
        return match.Groups[1].Value;
    }

    public static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, string token,
        params (string Name, string Value)[] fields) =>
        client.PostAsync(path, BuildForm(token, fields));

    /// <summary>Posts to the home page, which is where the shorten form lives.</summary>
    public static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string token,
        params (string Name, string Value)[] fields) =>
        PostFormAsync(client, "/", token, fields);

    public static FormUrlEncodedContent BuildForm(string token, params (string Name, string Value)[] fields)
    {
        var pairs = fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)).ToList();
        pairs.Add(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        return new FormUrlEncodedContent(pairs);
    }
}
