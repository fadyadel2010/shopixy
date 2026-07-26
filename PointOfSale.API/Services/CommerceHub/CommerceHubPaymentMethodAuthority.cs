using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.CommerceHub;

internal sealed record CommerceHubPaymentMethodAvailabilitySnapshot(
    string Code,
    bool IsAvailable,
    string? UnavailableReasonCode,
    string? UnavailableReason);

internal static class CommerceHubPaymentMethodAuthority
{
    private const string UnavailableReasonCode = "PAYMENT_METHOD_NOT_AVAILABLE";
    private const string UnavailableReason = "This payment method is not enabled for every store in this order.";

    private static readonly string[] OrderedPaymentMethodCodes =
    {
        CommerceHubPaymentMethods.Cod,
        CommerceHubPaymentMethods.Paymob,
        CommerceHubPaymentMethods.Stripe
    };

    public static IReadOnlyList<CommerceHubPaymentMethodAvailabilitySnapshot> BuildAvailability(
        IReadOnlyCollection<Guid> tenantIds,
        IReadOnlyCollection<TenantPaymentMethod> paymentMethods)
    {
        var distinctTenantIds = tenantIds
            .Where(tenantId => tenantId != Guid.Empty)
            .Distinct()
            .ToList();

        var groupedMethods = paymentMethods
            .Where(method => method.TenantId != Guid.Empty && method.IsEnabled && !method.IsDeleted)
            .GroupBy(method => method.TenantId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<TenantPaymentMethod>)group.ToList());

        return OrderedPaymentMethodCodes
            .Select(code =>
            {
                var isAvailable = distinctTenantIds.Any() &&
                    distinctTenantIds.All(tenantId =>
                        groupedMethods.TryGetValue(tenantId, out var methodsForTenant) &&
                        SupportsRequestedMethod(code, methodsForTenant));

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
        IReadOnlyCollection<TenantPaymentMethod> paymentMethods)
    {
        return BuildAvailability(tenantIds, paymentMethods)
            .Any(method =>
                string.Equals(method.Code, requestedPaymentMethod, StringComparison.OrdinalIgnoreCase) &&
                method.IsAvailable);
    }

    private static bool SupportsRequestedMethod(
        string requestedPaymentMethod,
        IReadOnlyCollection<TenantPaymentMethod> methodsForTenant)
    {
        if (string.Equals(requestedPaymentMethod, CommerceHubPaymentMethods.Cod, StringComparison.OrdinalIgnoreCase))
        {
            return methodsForTenant.Any(method =>
                method.SupportsOffline ||
                string.Equals(method.Code, CommerceHubPaymentMethods.Cod, StringComparison.OrdinalIgnoreCase));
        }

        return methodsForTenant.Any(method =>
            method.SupportsOnline &&
            string.Equals(method.Code, requestedPaymentMethod, StringComparison.OrdinalIgnoreCase));
    }
}
