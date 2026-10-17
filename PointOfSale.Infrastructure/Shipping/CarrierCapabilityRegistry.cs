using PointOfSale.Core.Constants;

namespace PointOfSale.Infrastructure.Shipping;

public enum CarrierProductionCapability
{
    ProductionReady,
    Partial,
    Unavailable,
    NotCertified
}

public enum CarrierConnectionTestMode
{
    Unsupported,
    LocalOnly,
    Remote
}

public sealed record CarrierCredentialFieldDescriptor(
    string Key,
    string Label,
    bool IsSecret,
    bool Required,
    string Target);

public sealed record CarrierCapabilityDescriptor(
    string CarrierCode,
    string DisplayName,
    CarrierProductionCapability ProductionCapability,
    IReadOnlyList<CarrierCredentialFieldDescriptor> CredentialFields,
    bool EnvironmentSupported,
    bool ConnectionTestSupported,
    CarrierConnectionTestMode ConnectionTestMode,
    IReadOnlyList<string> SupportedServiceLevels,
    bool RequiresOriginReadiness,
    bool RequiresGeographyMapping,
    bool SupportsWebhooks,
    IReadOnlyList<string> WebhookTrustModes);

public interface ICarrierCapabilityRegistry
{
    CarrierCapabilityDescriptor? GetCapability(string carrierCode);
    IReadOnlyList<CarrierCapabilityDescriptor> GetAllCapabilities();
}

public sealed class CarrierCapabilityRegistry : ICarrierCapabilityRegistry
{
    private readonly IShippingCarrierRegistry _carrierRegistry;

    public CarrierCapabilityRegistry(IShippingCarrierRegistry carrierRegistry)
    {
        _carrierRegistry = carrierRegistry;
    }

    public CarrierCapabilityDescriptor? GetCapability(string carrierCode)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
        {
            return null;
        }

        var normalized = CarrierCodes.Normalize(carrierCode);
        return BuildCapability(normalized);
    }

    public IReadOnlyList<CarrierCapabilityDescriptor> GetAllCapabilities() =>
        CarrierCodes.AllCodes
            .Select(BuildCapability)
            .Where(capability => capability != null)
            .Cast<CarrierCapabilityDescriptor>()
            .OrderBy(capability => capability.CarrierCode)
            .ToList();

    private CarrierCapabilityDescriptor? BuildCapability(string carrierCode)
    {
        var adapter = _carrierRegistry.GetAdapter(carrierCode);
        var serviceLevels = (adapter?.GetSupportedServiceLevels() ?? Array.Empty<ServiceLevelInfo>())
            .Select(level => level.Code)
            .ToList();
        var displayName = adapter?.DisplayName ?? carrierCode;

        return carrierCode switch
        {
            CarrierCodes.Manual => new CarrierCapabilityDescriptor(
                CarrierCodes.Manual,
                displayName,
                CarrierProductionCapability.ProductionReady,
                Array.Empty<CarrierCredentialFieldDescriptor>(),
                EnvironmentSupported: false,
                ConnectionTestSupported: false,
                CarrierConnectionTestMode.Unsupported,
                serviceLevels,
                RequiresOriginReadiness: false,
                RequiresGeographyMapping: false,
                SupportsWebhooks: false,
                Array.Empty<string>()),

            CarrierCodes.Bosta => new CarrierCapabilityDescriptor(
                CarrierCodes.Bosta,
                displayName,
                CarrierProductionCapability.Partial,
                new[]
                {
                    new CarrierCredentialFieldDescriptor("apiKey", "API key / token", true, true, "ApiKey"),
                    new CarrierCredentialFieldDescriptor("webhookSecret", "Webhook Authorization header value", true, false, "WebhookSecret"),
                    new CarrierCredentialFieldDescriptor("codCollectionCurrency", "COD collection currency", false, false, "ExtraConfig.codCollectionCurrency"),
                    new CarrierCredentialFieldDescriptor("businessLocationId", "Bosta business location ID", false, false, "ExtraConfig.businessLocationId")
                },
                EnvironmentSupported: true,
                ConnectionTestSupported: true,
                CarrierConnectionTestMode.Remote,
                serviceLevels,
                RequiresOriginReadiness: false,
                RequiresGeographyMapping: false,
                SupportsWebhooks: true,
                new[] { "shared_secret" }),

            CarrierCodes.Aramex => new CarrierCapabilityDescriptor(
                CarrierCodes.Aramex,
                displayName,
                CarrierProductionCapability.Partial,
                new[]
                {
                    new CarrierCredentialFieldDescriptor("apiKey", "Aramex username", true, true, "ApiKey"),
                    new CarrierCredentialFieldDescriptor("apiSecret", "Aramex password", true, true, "ApiSecret"),
                    new CarrierCredentialFieldDescriptor("accountId", "Account number", false, true, "AccountId"),
                    new CarrierCredentialFieldDescriptor("accountPin", "Account PIN", true, true, "ExtraConfig.AccountPin"),
                    new CarrierCredentialFieldDescriptor("accountEntity", "Account entity", false, true, "ExtraConfig.AccountEntity"),
                    new CarrierCredentialFieldDescriptor("accountCountryCode", "Account country code", false, true, "ExtraConfig.AccountCountryCode")
                },
                EnvironmentSupported: true,
                ConnectionTestSupported: true,
                CarrierConnectionTestMode.Remote,
                serviceLevels,
                RequiresOriginReadiness: false,
                RequiresGeographyMapping: true,
                SupportsWebhooks: false,
                Array.Empty<string>()),

            CarrierCodes.Dhl => new CarrierCapabilityDescriptor(
                CarrierCodes.Dhl,
                displayName,
                CarrierProductionCapability.Partial,
                new[]
                {
                    new CarrierCredentialFieldDescriptor("apiKey", "MyDHL Basic Auth username / API key", true, true, "ApiKey"),
                    new CarrierCredentialFieldDescriptor("apiSecret", "MyDHL Basic Auth password / API secret", true, true, "ApiSecret"),
                    new CarrierCredentialFieldDescriptor("accountId", "DHL Express account number (operations)", false, false, "AccountId")
                },
                EnvironmentSupported: true,
                ConnectionTestSupported: true,
                CarrierConnectionTestMode.Remote,
                serviceLevels,
                RequiresOriginReadiness: false,
                RequiresGeographyMapping: true,
                SupportsWebhooks: false,
                Array.Empty<string>()),

            CarrierCodes.Mylerz => new CarrierCapabilityDescriptor(
                CarrierCodes.Mylerz,
                displayName,
                CarrierProductionCapability.Unavailable,
                new[]
                {
                    new CarrierCredentialFieldDescriptor("apiKey", "Mylerz username", true, true, "ApiKey"),
                    new CarrierCredentialFieldDescriptor("apiSecret", "Mylerz password", true, true, "ApiSecret")
                },
                EnvironmentSupported: false,
                ConnectionTestSupported: true,
                CarrierConnectionTestMode.Remote,
                serviceLevels,
                RequiresOriginReadiness: false,
                RequiresGeographyMapping: true,
                SupportsWebhooks: false,
                Array.Empty<string>()),

            CarrierCodes.FedEx => new CarrierCapabilityDescriptor(
                CarrierCodes.FedEx,
                displayName,
                CarrierProductionCapability.NotCertified,
                new[]
                {
                    new CarrierCredentialFieldDescriptor("apiKey", "Project client ID", true, true, "ApiKey"),
                    new CarrierCredentialFieldDescriptor("apiSecret", "Project client secret", true, true, "ApiSecret"),
                    new CarrierCredentialFieldDescriptor("accountId", "FedEx account number", false, true, "AccountId")
                },
                EnvironmentSupported: true,
                ConnectionTestSupported: true,
                CarrierConnectionTestMode.Remote,
                serviceLevels,
                RequiresOriginReadiness: false,
                RequiresGeographyMapping: true,
                SupportsWebhooks: false,
                Array.Empty<string>()),

            _ => adapter == null
                ? null
                : new CarrierCapabilityDescriptor(
                    carrierCode,
                    displayName,
                    CarrierProductionCapability.Partial,
                    new[]
                    {
                        new CarrierCredentialFieldDescriptor("apiKey", "API key", true, true, "ApiKey")
                    },
                    EnvironmentSupported: true,
                    ConnectionTestSupported: true,
                    CarrierConnectionTestMode.Remote,
                    serviceLevels,
                    RequiresOriginReadiness: false,
                    RequiresGeographyMapping: false,
                    SupportsWebhooks: false,
                    Array.Empty<string>())
        };
    }
}
