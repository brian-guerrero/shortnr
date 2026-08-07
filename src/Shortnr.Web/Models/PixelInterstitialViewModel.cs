namespace Shortnr.Web.Models;

/// <summary>
/// Model for the lightweight retargeting interstitial page served when a short
/// link has a pixel attached. Contains no layout, no Pico and no external
/// assets so the page renders fast enough to stay under the sub-200ms bar.
/// </summary>
public sealed record PixelInterstitialViewModel(
    string DestinationUrl,
    string PixelSnippetHtml);
