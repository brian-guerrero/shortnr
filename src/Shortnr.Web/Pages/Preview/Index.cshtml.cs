using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Shortnr.Web.Pages.Preview;

public class PreviewModel : PageModel
{
    public string DestinationUrl { get; set; } = "";
    public string DestinationHost { get; set; } = "";
    public string Theme { get; set; } = ThemeCatalog.DefaultId;

    public IActionResult OnGet(string? url, string? theme, string? host)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return RedirectToPage("/Index");

        DestinationUrl = url;
        DestinationHost = host ?? uri.Host;
        Theme = ThemeCatalog.Resolve(theme).Id;
        return Page();
    }
}
