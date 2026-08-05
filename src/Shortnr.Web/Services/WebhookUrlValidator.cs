using System.Net;

namespace Shortnr.Web.Services;

public static class WebhookUrlValidator
{
    public static (bool IsValid, string? Error) Validate(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (false, "URL is required.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (false, "URL must be an absolute URI.");

        if (uri.Scheme != "http" && uri.Scheme != "https")
            return (false, "Only http and https schemes are allowed.");

        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
            return (false, "URL must have a valid host.");

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IsPrivateIp(ip))
                return (false, "Private and internal IP addresses are not allowed.");
        }
        else
        {
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return (false, "localhost is not allowed.");

            if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
                return (false, ".local domains are not allowed.");

            if (host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
                return (false, ".internal domains are not allowed.");
        }

        return (true, null);
    }

    public static bool IsPrivateIp(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Teredo)
            return true;

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            if (bytes[0] == 10)
                return true;

            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;

            if (bytes[0] == 192 && bytes[1] == 168)
                return true;

            if (bytes[0] == 169 && bytes[1] == 254)
                return true;

            if (bytes[0] == 127)
                return true;

            if (bytes[0] == 0)
                return true;

            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                return true;

            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
                return true;

            if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                return true;

            if (bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99)
                return true;
        }

        return false;
    }
}
