using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.API.Services.Payments.ManualPayment;

public static class ManualPaymentFoundationServiceCollectionExtensions
{
    public const string ProviderRegistrationName = "manual_payment_foundation";

    public static IServiceCollection AddManualPaymentFoundation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IManualPaymentFoundationHealthContributor)))
        {
            throw new InvalidOperationException("Manual Payment foundation duplicate registration is not allowed.");
        }

        services.AddOptions<ManualPaymentOptions>()
            .Bind(configuration.GetSection(ManualPaymentOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ManualPaymentOptions>, ManualPaymentOptionsValidator>();
        services.AddSingleton<IManualPaymentFoundationHealthContributor, ManualPaymentFoundationHealthContributor>();
        services.AddSingleton<IManualPaymentOperationalReadinessHealthContributor, ManualPaymentOperationalReadinessHealthContributor>();
        services.TryAddSingleton<ManualPaymentOperationalTelemetry>();
        services.TryAddSingleton<IManualPaymentOperationalTelemetryAuthority>(provider =>
            provider.GetRequiredService<ManualPaymentOperationalTelemetry>());
        services.TryAddSingleton<PaymentRuntimeOrchestrator>();
        services.AddScoped<IManualPaymentProviderAdapter, ManualPaymentProviderAdapter>();
        services.AddScoped<IManualPaymentRuntimeBridge, ManualPaymentRuntimeBridge>();
        services.AddScoped<IManualPaymentEvidenceAdapter, ManualPaymentEvidenceAdapter>();
        services.AddScoped<ManualPaymentTenantConfigurationResolver>();
        services.AddScoped<IManualPaymentTenantConfigurationResolver>(provider =>
            provider.GetRequiredService<ManualPaymentTenantConfigurationResolver>());
        services.AddScoped<IManualPaymentCapabilityAdmissionAuthority>(provider =>
            provider.GetRequiredService<ManualPaymentTenantConfigurationResolver>());
        services.AddScoped<IManualPaymentRuntimeApplicationService, ManualPaymentRuntimeApplicationService>();
        services.AddScoped<IManualPaymentEvidenceApplicationService, ManualPaymentEvidenceApplicationService>();
        services.AddScoped<IManualPaymentExpirationAuthority, ManualPaymentExpirationAuthority>();
        services.AddScoped<IManualPaymentRetentionAuthority, ManualPaymentRetentionAuthority>();
        services.AddScoped<IManualPaymentDurableEvidenceAuthority, ManualPaymentDurableEvidenceAuthority>();
        services.AddScoped<IManualPaymentReviewApplicationService, ManualPaymentReviewApplicationService>();

        return services;
    }
}
