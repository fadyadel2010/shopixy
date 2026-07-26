using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;
using Stripe;

namespace PointOfSale.API.Services.Payments.Stripe;

public static class StripeFoundationServiceCollectionExtensions
{
    public const string HttpClientName = "StripeOfficialSdk";

    public static IServiceCollection AddStripeFoundation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<StripeOptions>, StripeOptionsValidator>();

        services.AddHttpClient(HttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<StripeOptions>>().Value;
            client.Timeout = options.Timeout;
        });

        services.AddSingleton<StripeClient>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<StripeOptions>>().Value;
            StripeConfiguration.MaxNetworkRetries = options.RetryCount;
            StripeConfiguration.EnableTelemetry = options.TelemetryEnabled;

            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            return new StripeClient(
                options.SecretKey,
                httpClient: new SystemNetHttpClient(httpClient));
        });

        services.AddSingleton<IStripeFoundationHealthContributor, StripeFoundationHealthContributor>();
        services.AddSingleton<IStripeAdvancedOperationsClient, StripeAdvancedOperationsSdkClient>();
        services.AddSingleton<IStripeAdvancedOperationsAdapter, StripeAdvancedOperationsAdapter>();
        services.TryAddSingleton<PaymentRuntimeOrchestrator>();
        services.AddSingleton<IStripeWebhookEvidenceAdapter, StripeWebhookEvidenceAdapter>();
        services.AddScoped<IPaymentProviderAdapter, StripeAdapter>();

        return services;
    }
}
