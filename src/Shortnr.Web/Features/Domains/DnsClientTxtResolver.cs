using DnsClient;

namespace Shortnr.Web.Features.Domains;

/// <summary>
/// Resolves TXT records for a DNS name. Thin seam over a DNS resolver so domain
/// verification stays testable without real network calls.
/// </summary>
public interface ITxtDnsResolver
{
    Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// DnsClient.NET-backed TXT resolver. DnsClient.NET is Apache-2.0 licensed.
/// </summary>
public sealed class DnsClientTxtResolver(IDnsQuery dns) : ITxtDnsResolver
{
    public async Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken cancellationToken = default)
    {
        var response = await dns.QueryAsync(name, QueryType.TXT, QueryClass.IN, cancellationToken);
        return response.Answers.TxtRecords()
            .Select(r => string.Concat(r.Text))
            .ToList();
    }
}
