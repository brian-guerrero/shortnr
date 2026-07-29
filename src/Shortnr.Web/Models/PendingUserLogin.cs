namespace Shortnr.Web.Models;

public class PendingUserLogin
{
    public required string Issuer { get; init; }
    public required string Subject { get; init; }
    public string? Email { get; init; }
    public string? Name { get; init; }
}
