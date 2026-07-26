using PointOfSale.Core.Events;

namespace PointOfSale.API.Events;

/// <summary>
/// Base class for all checkout events.
/// All checkout events include tenant, order, cart, correlation, and idempotency context.
/// </summary>
public abstract class CheckoutEventBase : INonAuthoritativeIntegrationEvent
{
    /// <summary>
    /// Unique event identifier.
    /// </summary>
    public Guid EventId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Tenant that owns this checkout.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Order ID (may be empty if order not yet created).
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Cart ID being checked out.
    /// </summary>
    public Guid CartId { get; set; }

    /// <summary>
    /// Correlation ID for request tracing.
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Idempotency key (X-Request-Id).
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// When the event occurred.
    /// </summary>
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Event emitted when checkout process starts.
/// Emitted after lock is acquired.
/// </summary>
public class CheckoutStartedEvent : CheckoutEventBase
{
    /// <summary>
    /// Selected shipping method ID.
    /// </summary>
    public Guid ShippingMethodId { get; set; }

    /// <summary>
    /// Payment method identifier.
    /// </summary>
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>
    /// Cart owner type (session or customer).
    /// </summary>
    public string OwnerType { get; set; } = string.Empty;

    /// <summary>
    /// Cart owner ID.
    /// </summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>
    /// Number of items in cart.
    /// </summary>
    public int ItemCount { get; set; }
}

/// <summary>
/// Event emitted when checkout fails at any step.
/// </summary>
public class CheckoutFailedEvent : CheckoutEventBase
{
    /// <summary>
    /// Step where failure occurred.
    /// </summary>
    public string FailedStep { get; set; } = string.Empty;

    /// <summary>
    /// Error code for programmatic handling.
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Error message.
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Whether inventory was reserved and needs release.
    /// </summary>
    public bool InventoryReserved { get; set; }

    /// <summary>
    /// Total duration before failure.
    /// </summary>
    public long DurationMs { get; set; }
}

/// <summary>
/// Event emitted when order is successfully created.
/// This is a significant business event.
/// 
/// PHASE 10.4.D3: Enhanced with payment method snapshot for COD support.
/// </summary>
public class CheckoutOrderCreatedEvent : CheckoutEventBase
{
    /// <summary>
    /// Order number (human-readable).
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// POS/legacy customer ID. Represents in-store or non-storefront customer accounts.
    /// 
    /// CUSTOMER IDENTITY PRECEDENCE:
    /// - Storefront checkouts: Use OnlineCustomerId (takes precedence when both are present)
    /// - POS/in-store checkouts: Use CustomerId
    /// - Guest orders: Both CustomerId and OnlineCustomerId are null
    /// 
    /// INTERPRETATION RULES (CLASSIFICATION ONLY):
    /// - If OnlineCustomerId is present: Treat as authenticated storefront checkout
    /// - If OnlineCustomerId is null and CustomerId is present: Treat as POS/legacy customer
    /// - If both are null: Treat as guest order
    /// 
    /// CRITICAL SECURITY RULES:
    /// - CustomerId MUST NOT be used for storefront authorization or ownership checks
    /// - CustomerId is for CLASSIFICATION ONLY (identifying POS vs storefront vs guest)
    /// - Storefront-scoped reads (e.g., /api/storefront/account/orders) MUST require non-null OnlineCustomerId
    /// - Security-sensitive storefront operations MUST validate OnlineCustomerId, NOT CustomerId
    /// 
    /// Downstream consumers should check OnlineCustomerId first to identify authenticated
    /// storefront checkouts, and only use CustomerId to classify POS/legacy or guest orders.
    /// </summary>
    public Guid? CustomerId { get; set; }

    /// <summary>
    /// Online customer ID for authenticated storefront checkouts.
    /// 
    /// CUSTOMER IDENTITY PRECEDENCE:
    /// - When present: This takes precedence over CustomerId for customer identification
    /// - Indicates the order originated from an authenticated storefront session
    /// - When null: Check CustomerId to determine if POS customer or guest
    /// 
    /// INTERPRETATION RULES (CLASSIFICATION ONLY):
    /// - If OnlineCustomerId is present: Treat as authenticated storefront checkout
    /// - If OnlineCustomerId is null and CustomerId is present: Treat as POS/legacy customer
    /// - If both are null: Treat as guest order
    /// 
    /// CRITICAL SECURITY RULES:
    /// - OnlineCustomerId is REQUIRED for all storefront authorization and ownership checks
    /// - Storefront-scoped reads (e.g., /api/storefront/account/orders) MUST filter by OnlineCustomerId
    /// - Security-sensitive storefront operations MUST validate non-null OnlineCustomerId
    /// - DO NOT fall back to CustomerId for storefront authorization - it references a different table
    /// 
    /// Downstream consumers should check OnlineCustomerId first to identify authenticated
    /// storefront checkouts, and only use CustomerId to classify POS/legacy or guest orders.
    /// </summary>
    public Guid? OnlineCustomerId { get; set; }

    /// <summary>
    /// Guest email (if guest checkout).
    /// </summary>
    public string? GuestEmail { get; set; }

    /// <summary>
    /// AF-13B: Guest phone (for guest checkout order notifications).
    /// </summary>
    public string? GuestPhone { get; set; }

    /// <summary>
    /// AF-13B: True when order was placed as guest (no authenticated customer).
    /// Consumers must tolerate null CustomerId when this is true.
    /// </summary>
    public bool IsGuestCheckout { get; set; }

    /// <summary>
    /// Order subtotal.
    /// </summary>
    public decimal Subtotal { get; set; }

    /// <summary>
    /// Discount amount applied.
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// Shipping cost.
    /// </summary>
    public decimal ShippingCost { get; set; }

    /// <summary>
    /// Tax amount.
    /// </summary>
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// Order total (includes payment fee).
    /// </summary>
    public decimal Total { get; set; }

    /// <summary>
    /// Currency code.
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Number of items in order.
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// Shipping method name.
    /// </summary>
    public string ShippingMethodName { get; set; } = string.Empty;

    // =======================================================================
    // PHASE 10.4.D3: Payment Method Snapshot
    // =======================================================================

    /// <summary>
    /// Payment method code (e.g., "COD", "STRIPE", "PAYMOB").
    /// </summary>
    public string PaymentMethodCode { get; set; } = string.Empty;

    /// <summary>
    /// Display name of payment method.
    /// </summary>
    public string? PaymentMethodName { get; set; }

    /// <summary>
    /// Payment method fee (already included in Total).
    /// </summary>
    public decimal PaymentFee { get; set; }

    /// <summary>
    /// Whether the order has been paid.
    /// COD orders: false (pending payment on delivery)
    /// </summary>
    public bool IsPaid { get; set; }

    // =======================================================================
    // AF-11: Affiliate Attribution
    // =======================================================================

    /// <summary>
    /// AF-11: Affiliate account that referred this order (null if non-affiliate).
    /// Consumers use this to create AffiliateAttribution and trigger commission.
    /// </summary>
    public Guid? AffiliateAccountId { get; set; }

    /// <summary>
    /// AF-11: Snapshot of referral code used at checkout.
    /// </summary>
    public string? AffiliateReferralCode { get; set; }

    /// <summary>
    /// Order items summary.
    /// </summary>
    public List<CheckoutOrderItemInfo> Items { get; set; } = new();
}

/// <summary>
/// Order item info for checkout event.
/// </summary>
public class CheckoutOrderItemInfo
{
    public Guid ProductId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}

// NOTE: PaymentIntentCreatedEvent is defined in PaymentEvents.cs

/// <summary>
/// Authoritative event emitted when inventory is reserved for checkout.
/// </summary>
public class InventoryReservedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    /// <summary>
    /// Order ID for the reservation.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Cart ID being checked out.
    /// </summary>
    public Guid CartId { get; set; }

    /// <summary>
    /// Reservation ID for tracking.
    /// </summary>
    public Guid ReservationId { get; set; }

    /// <summary>
    /// Items reserved.
    /// </summary>
    public List<InventoryReservationItem> ReservedItems { get; set; } = new();

    /// <summary>
    /// Reservation expiry time.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    public Guid GetAuthoritativeAggregateId()
    {
        if (OrderId != Guid.Empty)
        {
            return OrderId;
        }

        if (CartId != Guid.Empty)
        {
            return CartId;
        }

        throw new InvalidOperationException(
            "GetAuthoritativeAggregateId failed: Both OrderId and CartId are empty. " +
            "InventoryReservedEvent must have at least one valid identifier to prevent aggregate scope corruption.");
    }
}

/// <summary>
/// Inventory reservation item.
/// PHASE 11.4.1: Now includes VariantId for SKU-level tracking.
/// </summary>
public class InventoryReservationItem
{
    public Guid ProductId { get; set; }
    /// <summary>
    /// Variant ID - the actual sellable SKU.
    /// PHASE 11.4.1: This is the source of truth for stock.
    /// </summary>
    public Guid? VariantId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public Guid? WarehouseId { get; set; }
}

/// <summary>
/// Event emitted when inventory reservation is released (on failure/rollback).
/// </summary>
public class InventoryReservationReleasedEvent : CheckoutEventBase
{
    /// <summary>
    /// Reservation ID that was released.
    /// </summary>
    public Guid ReservationId { get; set; }

    /// <summary>
    /// Reason for release.
    /// </summary>
    public string ReleaseReason { get; set; } = string.Empty;
}

/// <summary>
/// Event emitted when price recalculation detects a significant change.
/// </summary>
public class PriceRecalculationEvent : CheckoutEventBase
{
    /// <summary>
    /// Cart subtotal before recalculation.
    /// </summary>
    public decimal OriginalSubtotal { get; set; }

    /// <summary>
    /// Cart subtotal after recalculation.
    /// </summary>
    public decimal RecalculatedSubtotal { get; set; }

    /// <summary>
    /// Percentage difference.
    /// </summary>
    public decimal DifferencePercent { get; set; }

    /// <summary>
    /// Whether the change was accepted or rejected.
    /// </summary>
    public bool Accepted { get; set; }

    /// <summary>
    /// Items with price changes.
    /// </summary>
    public List<PriceChangeItem> PriceChanges { get; set; } = new();
}

/// <summary>
/// Item with price change during recalculation.
/// </summary>
public class PriceChangeItem
{
    public Guid ProductId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public decimal DifferencePercent { get; set; }
}
