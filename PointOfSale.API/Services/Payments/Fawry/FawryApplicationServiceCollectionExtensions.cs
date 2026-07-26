using Microsoft.Extensions.DependencyInjection.Extensions;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.API.Services.Payments.Fawry;

public static class FawryApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddFawryApplicationContract(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IFawryRuntimeApplicationService)))
        {
            throw new InvalidOperationException("Fawry application contract duplicate registration is not allowed.");
        }

        services.TryAddSingleton<PaymentRuntimeOrchestrator>();
        services.AddScoped<IFawryTenantConfigurationResolver, FawryTenantConfigurationResolver>();
        services.AddScoped<IFawryPaymentAdapter>(serviceProvider =>
            new FawryPaymentAdapter(
                serviceProvider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient(FawryFoundationServiceCollectionExtensions.HttpClientName)));
        services.AddScoped<IFawryWebhookEvidenceAdapter, FawryWebhookEvidenceAdapter>();
        services.AddScoped<IFawryProviderCheckpointStore, FawryProviderCheckpointStore>();
        services.AddScoped<IFawryRuntimeBridge, FawryRuntimeBridge>();
        services.AddScoped<IFawryRuntimeApplicationService, FawryRuntimeApplicationService>();
        services.AddScoped<IFawryWebhookRuntimeIntegrationService, FawryWebhookRuntimeIntegrationService>();

        return services;
    }
}
