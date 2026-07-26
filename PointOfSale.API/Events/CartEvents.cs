namespace PointOfSale.API.Events;

/// <summary>
/// Base class for all cart events.
/// All cart events are tenant-scoped and include correlation/idempotency keys.
/// </summary>
public abstract class CartEventBase
{
    /// <summary>
    /// Unique event identifier
    /// </summary>
    public Guid EventId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Tenant that owns the cart
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Cart identifier
    /// </summary>
    public Guid CartId { get; set; }

    /// <summary>
    /// Owner type: "session" or "customer"
    /// </summary>
    public string OwnerType { get; set; } = string.Empty;

    /// <summary>
    /// Owner identifier (session ID or customer ID)
    /// </summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>
    /// Correlation ID for request tracing
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Idempotency key for safe retries
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// When the event occurred
    /// </summary>
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Event emitted when a new cart is created.
/// </summary>
public class CartCreatedEvent : CartEventBase
{
    /// <summary>
    /// Currency of the cart
    /// </summary>
    public string Currency { get; set; } = "USD";
}

/// <summary>
/// Event emitted when an item is added to the cart.
/// </summary>
public class CartItemAddedEvent : CartEventBase
{
    /// <summary>
    /// Product ID that was added
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Variant ID (null for products without variants)
    /// </summary>
    public Guid? VariantId { get; set; }

    /// <summary>
    /// Variant name for display (e.g., "Red / XL")
    /// </summary>
    public string? VariantName { get; set; }

    /// <summary>
    /// Product SKU
    /// </summary>
    public string SKU { get; set; } = string.Empty;

    /// <summary>
    /// Quantity added
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Unit price at time of add (snapshot)
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// New total quantity of this item in cart
    /// </summary>
    public int NewTotalQuantity { get; set; }
}

/// <summary>
/// Event emitted when a cart item quantity is updated.
/// </summary>
public class CartItemUpdatedEvent : CartEventBase
{
    /// <summary>
    /// Product ID that was updated
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Variant ID (null for products without variants)
    /// </summary>
    public Guid? VariantId { get; set; }

    /// <summary>
    /// Variant name for display (e.g., "Red / XL")
    /// </summary>
    public string? VariantName { get; set; }

    /// <summary>
    /// Previous quantity
    /// </summary>
    public int PreviousQuantity { get; set; }

    /// <summary>
    /// New quantity
    /// </summary>
    public int NewQuantity { get; set; }
}

/// <summary>
/// Event emitted when an item is removed from the cart.
/// </summary>
public class CartItemRemovedEvent : CartEventBase
{
    /// <summary>
    /// Product ID that was removed
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Variant ID (null for products without variants)
    /// </summary>
    public Guid? VariantId { get; set; }

    /// <summary>
    /// Variant name for display (e.g., "Red / XL")
    /// </summary>
    public string? VariantName { get; set; }

    /// <summary>
    /// Quantity that was in cart before removal
    /// </summary>
    public int QuantityRemoved { get; set; }
}

/// <summary>
/// Event emitted when a cart is cleared (all items removed).
/// </summary>
public class CartClearedEvent : CartEventBase
{
    /// <summary>
    /// Number of items that were in cart before clearing
    /// </summary>
    public int ItemsCleared { get; set; }

    /// <summary>
    /// Total value that was in cart before clearing
    /// </summary>
    public decimal TotalValueCleared { get; set; }
}

/// <summary>
/// Event emitted when a guest (session) cart is merged into a customer cart.
/// Phase 3.3: Cart Merge (Guest → Customer)
/// </summary>
public class CartMergedEvent
{
    /// <summary>
    /// Unique event identifier
    /// </summary>
    public Guid EventId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Tenant that owns the carts
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Source cart ID (guest cart that was merged)
    /// </summary>
    public Guid SourceCartId { get; set; }

    /// <summary>
    /// Source owner type (always "session" for guest carts)
    /// </summary>
    public string SourceOwnerType { get; set; } = "session";

    /// <summary>
    /// Source owner ID (session ID)
    /// </summary>
    public string SourceOwnerId { get; set; } = string.Empty;

    /// <summary>
    /// Target cart ID (customer cart after merge)
    /// </summary>
    public Guid TargetCartId { get; set; }

    /// <summary>
    /// Target owner type (always "customer")
    /// </summary>
    public string TargetOwnerType { get; set; } = "customer";

    /// <summary>
    /// Target owner ID (customer ID)
    /// </summary>
    public string TargetOwnerId { get; set; } = string.Empty;

    /// <summary>
    /// Number of items merged from guest cart
    /// </summary>
    public int MergedItemCount { get; set; }

    /// <summary>
    /// Number of new items added (didn't exist in customer cart)
    /// </summary>
    public int NewItemsAdded { get; set; }

    /// <summary>
    /// Number of items with updated quantities
    /// </summary>
    public int ItemsUpdated { get; set; }

    /// <summary>
    /// Whether the merge was aborted due to currency mismatch
    /// </summary>
    public bool WasCurrencyMismatch { get; set; }

    /// <summary>
    /// Correlation ID for request tracing
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Idempotency key for safe retries
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// When the event occurred
    /// </summary>
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Event emitted when a coupon is applied to the cart.
/// Phase C2: Coupon Integration
/// 
/// Used for:
/// - Analytics (coupon usage tracking)
/// - Monitoring (coupon application patterns)
/// - Fraud detection (future)
/// 
/// NOT part of correctness path - fire-and-forget.
/// </summary>
public class CartCouponAppliedEvent : CartEventBase
{
    /// <summary>
    /// Applied coupon code
    /// </summary>
    public string CouponCode { get; set; } = string.Empty;

    /// <summary>
    /// Coupon ID
    /// </summary>
    public Guid CouponId { get; set; }

    /// <summary>
    /// Discount type: "percentage" or "fixed"
    /// </summary>
    public string DiscountType { get; set; } = string.Empty;

    /// <summary>
    /// Discount value (percentage or fixed amount)
    /// </summary>
    public decimal DiscountValue { get; set; }

    /// <summary>
    /// Calculated discount amount
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// Cart subtotal at time of application
    /// </summary>
    public decimal CartSubtotal { get; set; }

    /// <summary>
    /// Customer ID (null for guest carts)
    /// </summary>
    public Guid? CustomerId { get; set; }
}

/// <summary>
/// Event emitted when a coupon is removed from the cart.
/// Phase C2: Coupon Integration
/// 
/// Removal can be:
/// - Manual (user clicked remove)
/// - Automatic (coupon invalidated due to cart changes)
/// 
/// Used for analytics and monitoring.
/// NOT part of correctness path - fire-and-forget.
/// </summary>
public class CartCouponRemovedEvent : CartEventBase
{
    /// <summary>
    /// Removed coupon code
    /// </summary>
    public string CouponCode { get; set; } = string.Empty;

    /// <summary>
    /// Coupon ID
    /// </summary>
    public Guid? CouponId { get; set; }

    /// <summary>
    /// Reason for removal
    /// </summary>
    public string RemovalReason { get; set; } = string.Empty;

    /// <summary>
    /// Whether removal was automatic (due to invalidation)
    /// </summary>
    public bool WasAutomatic { get; set; }

    /// <summary>
    /// Cart subtotal at time of removal
    /// </summary>
    public decimal CartSubtotal { get; set; }

    /// <summary>
    /// Customer ID (null for guest carts)
    /// </summary>
    public Guid? CustomerId { get; set; }
}
