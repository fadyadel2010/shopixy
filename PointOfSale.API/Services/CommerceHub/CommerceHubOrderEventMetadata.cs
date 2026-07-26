using PointOfSale.Core.Entities;
using PointOfSale.Core.Helpers;

namespace PointOfSale.API.Services.CommerceHub;

internal static class CommerceHubOrderEventMetadata
{
    public static int ResolveAggregateEventVersion(CommerceHubMarketplaceOrder marketplaceOrder)
    {
        ArgumentNullException.ThrowIfNull(marketplaceOrder);

        var maxVersion = marketplaceOrder.ProjectionMutationVersion;
        foreach (var subOrder in marketplaceOrder.SubOrders)
        {
            maxVersion = Math.Max(maxVersion, subOrder.ProjectionMutationVersion);
        }

        return ProductEventVersionHelper.ToProductEventVersion(maxVersion);
    }

    public static string BuildMarketplaceStatusIdempotencyKey(
        CommerceHubMarketplaceOrder marketplaceOrder,
        int eventVersion,
        string reason)
    {
        return $"{marketplaceOrder.Id:N}:v{eventVersion}:{marketplaceOrder.Status}:{marketplaceOrder.PaymentStatus}:{NormalizeKeyPart(reason)}";
    }

    public static string BuildMarketplacePaymentIdempotencyKey(
        CommerceHubMarketplaceOrder marketplaceOrder,
        int eventVersion)
    {
        return $"{marketplaceOrder.Id:N}:v{eventVersion}:{marketplaceOrder.Status}:{marketplaceOrder.PaymentStatus}:{NormalizeKeyPart(marketplaceOrder.PaymentReferenceId)}";
    }

    public static string BuildSubOrderStatusIdempotencyKey(
        CommerceHubSubOrder subOrder,
        int eventVersion,
        string reason)
    {
        return $"{subOrder.Id:N}:v{eventVersion}:{subOrder.Status}:{subOrder.PaymentStatus}:{NormalizeKeyPart(reason)}";
    }

    public static string BuildSubOrderPaymentIdempotencyKey(
        CommerceHubSubOrder subOrder,
        int eventVersion)
    {
        return $"{subOrder.Id:N}:v{eventVersion}:{subOrder.Status}:{subOrder.PaymentStatus}:{NormalizeKeyPart(subOrder.PaymentReferenceId)}";
    }

    private static string NormalizeKeyPart(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
}
