using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shortnr.Web.Models;
using Shortnr.Web.Services;

namespace Shortnr.Web.Pages;

public class QRModel : PageModel
{
    private readonly QrService _qr;

    public QRModel(QrService qr)
    {
        _qr = qr;
    }

    public IActionResult OnGet(string shortCode)
    {
        if (Request.Headers["HX-Request"].Count == 0) return NotFound();

        var shortUrl = $"{Request.Scheme}://{Request.Host}/{shortCode}";
        var dataUri = _qr.GenerateDataUri(shortUrl);
        return Partial("Shared/_QrCode", new QrCodeViewModel(dataUri, shortCode));
    }
}
