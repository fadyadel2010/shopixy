using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.Payments.Fawry;

public static class FawryFoundationServiceCollectionExtensions
{
    public const string HttpClientName = "FawryOfficialApi";

    public static IServiceCollection AddFawryFoundation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IFawryFoundationHealthContributor)))
        {
            throw new InvalidOperationException("Fawry foundation duplicate registration is not allowed.");
        }

        services.AddOptions<FawryOptions>()
            .Bind(configuration.GetSection(FawryOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FawryOptions>, FawryOptionsValidator>();

        services.AddHttpClient(HttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<FawryOptions>>().Value;
            client.BaseAddress = options.OfficialApiBaseUri;
            client.Timeout = options.Timeout;
        });

        services.AddSingleton<IFawryFoundationHealthContributor, FawryFoundationHealthContributor>();
        services.AddSingleton<IFawryOperationalReadinessHealthContributor, FawryOperationalReadinessHealthContributor>();

        return services;
    }
}
