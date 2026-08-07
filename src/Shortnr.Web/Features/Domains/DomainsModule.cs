using DnsClient;

namespace Shortnr.Web.Features.Domains;

public static class DomainsModule
{
    public static IServiceCollection AddDomainsFeature(this IServiceCollection services)
    {
        services.AddSingleton<IDnsQuery>(new LookupClient());
        services.AddSingleton<ITxtDnsResolver, DnsClientTxtResolver>();
        services.AddHttpClient<DomainVerifierService>(client => client.Timeout = TimeSpan.FromSeconds(15));
        return services;
    }
}