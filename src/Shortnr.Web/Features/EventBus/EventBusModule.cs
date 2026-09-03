using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Shortnr.Web.Features.EventBus;

public static class EventBusModule
{
    public static IServiceCollection AddEventBusFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EventBusOptions>(configuration.GetSection("EventBus"));

        // Registered unconditionally: in the InProcess (default) provider it is never used;
        // in the RabbitMQ provider it is the shared IConnection the publisher and health
        // check talk to.
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<EventBusPublisher>();
        services.AddHealthChecks();

        // The RabbitMQ health check is registered lazily (via IConfigureOptions) rather than
        // eagerly against the IConfiguration captured here: test hosts' ConfigureAppConfiguration
        // overrides only merge into builder.Configuration when builder.Build() runs, so reading
        // EventBus:Provider at registration time would miss a test factory's RabbitMQ override
        // and silently never register the check — leaving /health/rabbitmq mapped but empty.
        // This mirrors the AddDbContext lazy-resolution pattern in Program.cs and the PRD-017
        // Redis health-check registration in InfrastructureModule.
        services.AddSingleton<IConfigureOptions<HealthCheckServiceOptions>>(sp =>
            new ConfigureOptions<HealthCheckServiceOptions>(options =>
            {
                if (EventBusProviderHelper.ResolveProvider(sp.GetRequiredService<IConfiguration>())
                    != EventBusProvider.RabbitMQ)
                    return;

                options.Registrations.Add(new HealthCheckRegistration(
                    "rabbitmq",
                    _ => new RabbitMqHealthCheck(sp.GetRequiredService<RabbitMqConnectionProvider>()),
                    failureStatus: null,
                    tags: ["rabbitmq"]));
            }));

        return services;
    }
}
