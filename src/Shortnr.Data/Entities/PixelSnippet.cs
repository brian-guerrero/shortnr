namespace Shortnr.Data.Entities;

/// <summary>
/// A reusable retargeting-pixel snippet that a Smart Link can reference. The
/// two template rows (Meta Pixel, Google Ads) are seeded and substitute the
/// link's pixel ID into the <c>{{PIXEL_ID}}</c> placeholder; the "custom
/// snippet" row is used verbatim for advanced users who paste their own HTML.
/// </summary>
public class PixelSnippet
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SnippetTemplate { get; set; } = string.Empty;
    public bool IsCustom { get; set; }
}
