using DnsClient;

namespace Shortnr.Web.Features.Domains;

/// <summary>
/// Verifies a custom domain by fetching its well-known verification file over
/// HTTP (or by DNS TXT record) and comparing the served token with the stored
/// one. Custom domains are expected to point (CNAME/A record) at this instance,
/// so the file fetch resolves back into the app's own
/// /.well-known/shortnr-verify.txt endpoint.
/// </summary>
public sealed class DomainVerifierService(HttpClient httpClient, ITxtDnsResolver txtDns)
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

    /// <summary>
    /// Verifies via a DNS TXT record at <c>_shortnr-verify.{hostname}</c>
    /// containing the verification token. The leading record name is prefixed so
    /// it never collides with the host's own existing TXT records.
    /// </summary>
    public async Task<bool> VerifyByTxtAsync(string hostname, string expectedToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await txtDns.GetTxtRecordsAsync($"_shortnr-verify.{hostname}", cancellationToken);
            return records.Any(record => record.Trim() == expectedToken);
        }
        catch (DnsResponseException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
