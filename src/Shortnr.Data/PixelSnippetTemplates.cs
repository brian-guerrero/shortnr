namespace Shortnr.Data;

/// <summary>
/// Seeded retargeting-pixel templates referenced by <see cref="Entities.PixelSnippet"/>.
/// Both templates substitute the link's pixel ID into the <c>{{PIXEL_ID}}</c>
/// placeholder at interstitial-render time. The templates are the vendor-standard
/// Meta Pixel / Google Ads base snippets (fbq / gtag).
/// </summary>
public static class PixelSnippetTemplates
{
    /// <summary>The placeholder token replaced with a link's pixel ID.</summary>
    public const string Placeholder = "{{PIXEL_ID}}";

    public const string MetaPixel = """
        <script>
        !function(f,b,e,v,n,t,s){if(f.fbq)return;n=f.fbq=function(){n.callMethod?n.callMethod.apply(n,arguments):n.queue.push(arguments)};if(!f._fbq)f._fbq=n;n.push=n;n.loaded=!0;n.version='2.0';n.queue=[];t=b.createElement(e);t.async=!0;t.src=v;s=b.getElementsByTagName(e)[0];s.parentNode.insertBefore(t,s)}(window,document,'script','https://connect.facebook.net/en_US/fbevents.js');
        fbq('init', '{{PIXEL_ID}}');
        fbq('track', 'PageView');
        </script>
        """;

    public const string GoogleAds = """
        <script async src="https://www.googletagmanager.com/gtag/js?id={{PIXEL_ID}}"></script>
        <script>
        window.dataLayer = window.dataLayer || [];
        function gtag(){dataLayer.push(arguments);}
        gtag('js', new Date());
        gtag('config', '{{PIXEL_ID}}');
        </script>
        """;
}
