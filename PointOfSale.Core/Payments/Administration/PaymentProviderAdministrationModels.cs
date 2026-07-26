namespace PointOfSale.Core.Payments.Administration;

public sealed record PaymentProviderCapability(
    string Code,
    string DisplayName,
    string Description,
    bool Supported,
    bool RequiresConfiguration);

public sealed record PaymentProviderConfiguration(
    string ProviderCode,
    string DisplayName,
    string Description,
    bool Enabled,
    string Environment,
    IReadOnlyList<PaymentProviderCapability> Capabilities,
    IReadOnlyDictionary<string, object?> Configuration,
    string Status,
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record PaymentProviderRegistration(
    string ProviderCode,
    string DisplayName,
    string Description,
    IReadOnlyList<PaymentProviderCapability> Capabilities,
    IReadOnlyDictionary<string, object?> Metadata);
