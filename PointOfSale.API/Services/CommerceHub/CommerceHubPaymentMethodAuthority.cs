using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.Payments.ProviderContracts;

namespace PointOfSale.API.Services.CommerceHub;

internal sealed record CommerceHubPaymentMethodAvailabilitySnapshot(
    string Code,
    bool IsAvailable,
    string? UnavailableReasonCode,
    string? UnavailableReason);

internal static class CommerceHubPaymentMethodAuthority
{
    private const string UnavailableReasonCode = "PAYMENT_METHOD_NOT_AVAILABLE";
    private const string UnavailableReason = "This payment method is not available from the CommerceHub platform.";

    private static readonly string[] OrderedPaymentMethodCodes =
    {
        CommerceHubPaymentMethods.Cod,
        CommerceHubPaymentMethods.Paymob,
        CommerceHubPaymentMethods.Stripe
    };

    public static IReadOnlyList<CommerceHubPaymentMethodAvailabilitySnapshot> BuildAvailability(
        IReadOnlyCollection<Guid> tenantIds,
        IEnumerable<IPaymentProviderAdapter> providerAdapters)
    {
        var hasStore = tenantIds.Any(tenantId => tenantId != Guid.Empty);
        var adapters = providerAdapters
            .GroupBy(adapter => adapter.ProviderCode.Normalized, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        return OrderedPaymentMethodCodes
            .Select(code =>
            {
                var isAvailable = hasStore &&
                    (CommerceHubPaymentMethods.IsCod(code) || IsProviderExecutable(code, adapters));

                return new CommerceHubPaymentMethodAvailabilitySnapshot(
                    code,
                    isAvailable,
                    isAvailable ? null : UnavailableReasonCode,
                    isAvailable ? null : UnavailableReason);
            })
            .ToList();
    }

    public static bool IsRequestedMethodAvailable(
        string requestedPaymentMethod,
        IReadOnlyCollection<Guid> tenantIds,
        IEnumerable<IPaymentProviderAdapter> providerAdapters)
    {
        return BuildAvailability(tenantIds, providerAdapters)
            .Any(method =>
                string.Equals(method.Code, requestedPaymentMethod, StringComparison.OrdinalIgnoreCase) &&
                method.IsAvailable);
    }

    public static bool IsProviderExecutable(
        string providerCode,
        IEnumerable<IPaymentProviderAdapter> providerAdapters)
    {
        var normalized = new ProviderCode(providerCode).Normalized;
        var adapter = providerAdapters.LastOrDefault(candidate =>
            string.Equals(candidate.ProviderCode.Normalized, normalized, StringComparison.OrdinalIgnoreCase));
        if (adapter?.Capabilities is not { } capabilities)
        {
            return false;
        }

        return capabilities.SupportsPaymentSessionCreation &&
            capabilities.SupportsWebhookVerification &&
            (!string.Equals(providerCode, CommerceHubPaymentMethods.Paymob, StringComparison.OrdinalIgnoreCase) ||
             capabilities.SupportsCallbackParsing);
    }

    private static bool IsProviderExecutable(
        string providerCode,
        IReadOnlyDictionary<string, IPaymentProviderAdapter> providerAdapters)
    {
        var normalized = new ProviderCode(providerCode).Normalized;
        return providerAdapters.TryGetValue(normalized, out var adapter) &&
            IsProviderExecutable(providerCode, new[] { adapter });
    }
}
