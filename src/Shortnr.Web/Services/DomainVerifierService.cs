namespace Shortnr.Web.Services;

/// <summary>
/// Verifies a custom domain by fetching its well-known verification file over
/// HTTP and comparing the served token with the stored one. Custom domains are
/// expected to point (CNAME/A record) at this instance, so the fetch resolves
/// back into the app's own /.well-known/shortnr-verify.txt endpoint.
/// </summary>
public class DomainVerifierService(HttpClient httpClient)
{
    public async Task<bool> VerifyAsync(string hostname, string expectedToken, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"http://{hostname}/.well-known/shortnr-verify.txt", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return body.Trim() == expectedToken;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}
