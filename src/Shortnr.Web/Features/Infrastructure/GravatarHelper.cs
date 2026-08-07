using System.Security.Cryptography;
using System.Text;

namespace Shortnr.Web.Features.Infrastructure;

public static class GravatarHelper
{
    /// <summary>
    /// Returns a Gravatar image URL for the given email address.
    /// Falls back to the "mp" (mystery person) silhouette if no Gravatar is registered.
    /// </summary>
    public static string Url(string? email, int size = 40)
    {
        var hash = Hash(email);
        return $"https://www.gravatar.com/avatar/{hash}?s={size}&d=mp";
    }

    private static string Hash(string? email)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(bytes);
    }
}
