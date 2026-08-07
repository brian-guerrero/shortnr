using Microsoft.Extensions.Options;

namespace Shortnr.Web.Features.Email;

public static class EmailModule
{
    public static IServiceCollection AddEmailFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<EmailService>();
        return services;
    }
}