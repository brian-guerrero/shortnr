using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shortnr.Data;

namespace Shortnr.Web.Pages;

public class QRModel : PageModel
{
    private readonly QrService _qr;
    private readonly AppDbContext _db;

    public string ShortCode { get; private set; } = "";
    public string ShortUrl { get; private set; } = "";
    public string LongUrl { get; private set; } = "";
    public string DataUri { get; private set; } = "";

    public QRModel(QrService qr, AppDbContext db)
    {
        _qr = qr;
        _db = db;
    }

    public async Task<IActionResult> OnGet(string shortCode)
    {
        var link = await ShortUrlHelper.ResolveAsync(_db, Request.Host.Host, shortCode);
        if (link is null) return NotFound();

        ShortCode = shortCode;
        ShortUrl = ShortUrlHelper.Build(Request.Scheme, Request.Host.Host, link);
        LongUrl = link.LongUrl;
        DataUri = _qr.GenerateDataUri(ShortUrl);

        if (Request.Headers["HX-Request"].Count > 0)
            return Partial("Shared/_QrCode", new QrCodeViewModel(DataUri, shortCode));

        return Page();
    }
}
