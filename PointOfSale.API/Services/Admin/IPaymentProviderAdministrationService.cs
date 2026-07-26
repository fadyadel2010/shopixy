using PointOfSale.API.DTOs.Admin;

namespace PointOfSale.API.Services.Admin;

public interface IPaymentProviderAdministrationService
{
    IReadOnlyList<PaymentProviderSummaryDto> GetProviders();

    PaymentProviderDashboardDto GetDashboard(
        IReadOnlyList<PaymentProviderAdministrationActivityDto>? recentActivity = null);

    PaymentProviderConfigurationDto GetProviderConfiguration(string providerCode);

    PaymentProviderOperationalConfiguration GetProviderOperationalConfiguration(string providerCode);

    PaymentProviderConfigurationSaveResult SaveProviderConfiguration(
        string providerCode,
        PaymentProviderConfigurationRequest request);

    PaymentProviderConfigurationDto RecordProviderActionResult(
        string providerCode,
        PaymentProviderActionResultDto result);
}

public sealed record PaymentProviderOperationalConfiguration(
    string ProviderCode,
    string DisplayName,
    bool Enabled,
    string Environment,
    IReadOnlyDictionary<string, object?> Configuration,
    IReadOnlyDictionary<string, string> Credentials,
    IReadOnlyList<PaymentProviderCapabilityDto> Capabilities,
    PaymentProviderHealthDto CurrentHealth);
