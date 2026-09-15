namespace PointOfSale.Core.Models.Accounting;

/// <summary>
/// T-08 — E-commerce / CommerceHub merchandise-cost (COGS) authority.
/// Forward-looking policy:
/// - Storefront: ProductVariant.CostPrice (moving weighted average) frozen at
///   checkout into immutable OrderItem.CostPrice.
/// - CommerceHub: ProductVariant.CostPrice frozen at checkout into immutable
///   CommerceHubSubOrderItem.UnitCostSnapshot / CostAmountSnapshot.
/// A historical line's COGS must never change because the master cost moves later.
/// FIFO/LIFO, live-cost-at-refund, and live-cost-at-delivery are prohibited.
/// </summary>
public static class EcommerceCogsPolicy
{
    public const string StorefrontCogsEventType = "EcommerceCogs";
    public const string StorefrontCogsReversalEventType = "EcommerceCogsReversal";
    public const string CommerceHubCogsEventType = "CommerceHubCogs";
    public const string CommerceHubCogsReversalEventType = "CommerceHubCogsReversal";

    public const string StorefrontCogsSourceType = "Order_COGS";
    public const string StorefrontCogsReversalSourceType = "Order_COGS_Reversal";
    public const string CommerceHubCogsSourceType = "CommerceHub_COGS";
    public const string CommerceHubCogsReversalSourceType = "CommerceHub_COGS_Reversal";

    /// <summary>
    /// Storefront sale COGS key. The commit event id is mandatory: delivery can
    /// replay and partial commits may exist, so OrderId alone is prohibited.
    /// </summary>
    public static string BuildStorefrontCogsKey(Guid tenantId, Guid orderId, string commitEventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitEventId);
        return $"ECOM_COGS:{tenantId}:{orderId}:{commitEventId.Trim()}";
    }

    public static string BuildStorefrontCogsReversalKey(Guid tenantId, Guid orderId, string returnEventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(returnEventId);
        return $"ECOM_COGS_REV:{tenantId}:{orderId}:{returnEventId.Trim()}";
    }

    public static string BuildCommerceHubCogsKey(Guid tenantId, Guid subOrderId, string deliveryCommitEventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryCommitEventId);
        return $"CH_COGS:{tenantId}:{subOrderId}:{deliveryCommitEventId.Trim()}";
    }

    public static string BuildCommerceHubCogsReversalKey(Guid tenantId, Guid subOrderId, string returnEventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(returnEventId);
        return $"CH_COGS_REV:{tenantId}:{subOrderId}:{returnEventId.Trim()}";
    }
}

/// <summary>
/// One Storefront COGS line: frozen snapshot cost for a single OrderItem.
/// Quantity is decimal because reservation commit quantities are base-unit
/// decimals (unit conversions may fractionalize the ordered integer quantity).
/// </summary>
public sealed record EcommerceCogsLineDetail(
    Guid OrderItemId,
    Guid? VariantId,
    decimal Quantity,
    decimal UnitCostSnapshot,
    decimal CostAmount);

/// <summary>
/// Verified primitives for a Storefront sale COGS posting.
/// Dr 5010 COGS / Cr 1310 Inventory.
/// </summary>
public sealed record EcommerceCogsPostingRequest(
    Guid TenantId,
    Guid OrderId,
    string OrderNumber,
    string CommitEventId,
    string Currency,
    DateTime CommitOccurredAtUtc,
    IReadOnlyList<EcommerceCogsLineDetail> Lines,
    decimal TotalCost)
{
    public string IdempotencyKey =>
        EcommerceCogsPolicy.BuildStorefrontCogsKey(TenantId, OrderId, CommitEventId);
}

/// <summary>
/// One Storefront COGS reversal line: original snapshot cost relief for a refund.
/// Amounts must reuse the original per-line snapshot, never live cost.
/// </summary>
public sealed record EcommerceCogsReversalLineDetail(
    Guid OrderItemId,
    decimal ReturnQuantity,
    decimal ReversalAmount);

/// <summary>
/// Verified primitives for a Storefront COGS reversal posting.
/// Dr 1310 Inventory / Cr 5010 COGS.
/// </summary>
public sealed record EcommerceCogsReversalPostingRequest(
    Guid TenantId,
    Guid OrderId,
    string OrderNumber,
    string ReturnEventId,
    string Currency,
    DateTime ReturnOccurredAtUtc,
    IReadOnlyList<EcommerceCogsReversalLineDetail> Lines,
    decimal TotalReversal)
{
    public string IdempotencyKey =>
        EcommerceCogsPolicy.BuildStorefrontCogsReversalKey(TenantId, OrderId, ReturnEventId);
}

/// <summary>
/// Verified primitives for a CommerceHub sale COGS posting (one item delivery-commit).
/// Dr 5010 COGS / Cr 1310 Inventory.
/// </summary>
public sealed record CommerceHubCogsPostingRequest(
    Guid TenantId,
    Guid MarketplaceOrderId,
    Guid SubOrderId,
    string SubOrderNumber,
    Guid SubOrderItemId,
    string DeliveryCommitEventId,
    string Currency,
    DateTime CommitOccurredAtUtc,
    decimal Quantity,
    decimal UnitCostSnapshot,
    decimal CostAmount)
{
    public string IdempotencyKey =>
        EcommerceCogsPolicy.BuildCommerceHubCogsKey(TenantId, SubOrderId, DeliveryCommitEventId);
}

/// <summary>
/// T-08 durable COGS outbox payloads. These contracts are shared by the enqueue
/// sites (inside the source inventory transaction) and the outbox processor
/// (which re-verifies every snapshot against immutable source state before posting).
/// Line ordering is by ascending line id; JSON comparison is order-insensitive for
/// objects so key-identical replays dedupe while changed payloads conflict.
/// </summary>
public sealed record EcommerceCogsOutboxLine(
    Guid OrderItemId,
    Guid? VariantId,
    decimal Quantity,
    decimal UnitCostSnapshot,
    decimal CostAmount);

public sealed record EcommerceCogsOutboxPayload(
    Guid OrderId,
    string CommitEventId,
    string Currency,
    DateTime CommitOccurredAtUtc,
    List<EcommerceCogsOutboxLine> Lines,
    decimal TotalCost);

public sealed record EcommerceCogsReversalOutboxLine(
    Guid OrderItemId,
    decimal ReturnQuantity,
    decimal ReversalAmount);

public sealed record EcommerceCogsReversalOutboxPayload(
    Guid OrderId,
    string ReturnEventId,
    string Currency,
    DateTime ReturnOccurredAtUtc,
    List<EcommerceCogsReversalOutboxLine> Lines,
    decimal TotalReversal);

public sealed record CommerceHubCogsOutboxPayload(
    Guid MarketplaceOrderId,
    Guid SubOrderId,
    Guid SubOrderItemId,
    string DeliveryCommitEventId,
    string Currency,
    DateTime CommitOccurredAtUtc,
    decimal Quantity,
    decimal UnitCostSnapshot,
    decimal CostAmount);

public sealed record CommerceHubCogsReversalOutboxPayload(
    Guid MarketplaceOrderId,
    Guid SubOrderId,
    Guid SubOrderItemId,
    Guid ReturnId,
    string ReturnEventId,
    string Currency,
    DateTime ReturnOccurredAtUtc,
    decimal ResellableQuantity,
    decimal UnitCostSnapshot,
    decimal ReversalAmount);

/// <summary>
/// Verified primitives for a CommerceHub COGS reversal posting (resellable return only).
/// Dr 1310 Inventory / Cr 5010 COGS.
/// </summary>
public sealed record CommerceHubCogsReversalPostingRequest(
    Guid TenantId,
    Guid MarketplaceOrderId,
    Guid SubOrderId,
    string SubOrderNumber,
    Guid SubOrderItemId,
    Guid ReturnId,
    string ReturnEventId,
    string Currency,
    DateTime ReturnOccurredAtUtc,
    decimal ResellableQuantity,
    decimal UnitCostSnapshot,
    decimal ReversalAmount)
{
    public string IdempotencyKey =>
        EcommerceCogsPolicy.BuildCommerceHubCogsReversalKey(TenantId, SubOrderId, ReturnEventId);
}
