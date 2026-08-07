namespace Shortnr.Web.Features.Webhooks;

public static class WebhooksModule
{
    public static IServiceCollection AddWebhooksFeature(this IServiceCollection services)
    {
        services.AddSingleton(Channel.CreateUnbounded<WebhookDeliveryRecord>());
        services.AddHostedService<WebhookDeliveryService>();
        services.AddSingleton<WebhookEventDispatcher>();
        services.AddHttpClient("WebhookDelivery", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.MaxResponseContentBufferSize = 1024 * 1024;
        });
        return services;
    }
}