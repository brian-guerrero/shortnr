namespace Shortnr.Web.Helpers;

/// <summary>
/// Resolves the client IP for IP-keyed rate limiting. By default uses the direct
/// connection address; when the operator opts into trusting X-Forwarded-For
/// (behind a reverse proxy that strips the header), the left-most forwarded hop
/// is used instead.
/// </summary>
public static class ClientIpResolver
{
    public static string Resolve(HttpContext context, bool trustForwardedFor)
    {
        if (trustForwardedFor)
        {
            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
                return forwarded.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
