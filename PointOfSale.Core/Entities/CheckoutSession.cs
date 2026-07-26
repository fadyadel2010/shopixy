namespace PointOfSale.Core.Entities;

/// <summary>
/// Represents a temporary checkout attempt before an order is created.
/// 
/// This is NOT an order. It captures partially completed checkout data
/// so merchants can recover potential lost sales.
/// 
/// LIFECYCLE:
/// 1. Active: Customer enters checkout page → session created
/// 2. Updated: Frontend sends progressive autosave (debounced PATCH)
/// 3. Abandoned: Background job detects no activity for 30+ minutes
/// 4. ConvertedToOrder: Customer completes checkout → order created
/// 
/// ARCHITECTURAL RULES:
/// - Never creates or modifies orders
/// - Never reserves inventory
/// - Never processes payments
/// - PostgreSQL is the write model (source of truth)
/// - MongoDB projection for admin dashboard reads
/// - Events published AFTER database commits
/// </summary>
public class CheckoutSession : TenantEntity
{
    /// <summary>
    /// Cart identifier from storefront session.
    /// Links this checkout attempt to the cart being checked out.
    /// </summary>
    public Guid CartId { get; set; }

    /// <summary>
    /// Cryptographically random token for recovery links.
    /// Allows customers to return to checkout via /recover-checkout/{token}.
    /// Generated once at session creation. Indexed for fast lookup.
    /// </summary>
    public string RecoveryToken { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Session ID from storefront (for guest checkouts).
    /// Used to identify the customer even if not authenticated.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Online customer ID (if authenticated).
    /// Null for guest checkout attempts.
    /// </summary>
    public Guid? OnlineCustomerId { get; set; }

    /// <summary>
    /// Customer name (partially filled from checkout form).
    /// May be incomplete if customer leaves early.
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>
    /// Customer phone number.
    /// CRITICAL for recovery outreach (WhatsApp, call).
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Customer email address.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// City from shipping address.
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// Area/district from shipping address.
    /// </summary>
    public string? Area { get; set; }

    /// <summary>
    /// Full address line from shipping address.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Country from shipping address (ISO 3166-1 alpha-2 code).
    /// Should be normalized to 2-letter uppercase format (e.g., "EG", "US", "SA").
    /// Validation and normalization handled at service layer.
    /// Used for shipping/tax calculations and recovery.
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Customer order notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// JSON snapshot of cart items at session creation/update.
    /// Structure: [{ variantId, productName, quantity, price, imageUrl }]
    /// Used for display in admin dashboard and recovery messages.
    /// </summary>
    public string ItemsSnapshotJson { get; set; } = "[]";

    /// <summary>
    /// Cart subtotal at time of snapshot.
    /// </summary>
    public decimal? CartTotal { get; set; }

    /// <summary>
    /// Currency code (e.g., "EGP", "USD").
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Selected shipping method ID (if any).
    /// </summary>
    public Guid? ShippingMethodId { get; set; }

    /// <summary>
    /// Selected payment method code (if any).
    /// </summary>
    public string? PaymentMethodCode { get; set; }

    /// <summary>
    /// Selected wallet type for manual wallet payments (for example, VodafoneCash).
    /// </summary>
    public string? WalletType { get; set; }

    /// <summary>
    /// Customer wallet phone number captured during manual wallet checkout.
    /// </summary>
    public string? WalletPhoneNumber { get; set; }

    /// <summary>
    /// Uploaded payment proof URL captured during manual wallet checkout.
    /// </summary>
    public string? ProofImageUrl { get; set; }

    /// <summary>
    /// Current session status.
    /// Controls lifecycle and prevents invalid transitions.
    /// </summary>
    public Enums.CheckoutSessionStatus Status { get; set; } = Enums.CheckoutSessionStatus.Active;

    // ========================================================================
    // Attribution & Device Tracking
    // ========================================================================

    /// <summary>
    /// Device type: Mobile, Desktop, Tablet.
    /// Derived from User-Agent on session creation.
    /// </summary>
    public string? DeviceType { get; set; }

    /// <summary>
    /// Traffic source: Direct, Instagram, Affiliate, GoogleAds, etc.
    /// Derived from utm_source query param or referrer on session creation.
    /// </summary>
    public string? Source { get; set; }

    // ========================================================================
    // Timestamps
    // ========================================================================

    /// <summary>
    /// When this session was created (customer entered checkout).
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the first autosave (form field change) was received.
    /// Null until the first PATCH call.
    /// Enables: checkoutDuration = LastActivityAtUtc - FirstActivityAtUtc.
    /// </summary>
    public DateTime? FirstActivityAtUtc { get; set; }

    /// <summary>
    /// Last time the session was updated (any field change from autosave).
    /// Used by background scanner to detect abandonment.
    /// </summary>
    public DateTime LastActivityAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When session was marked as abandoned.
    /// Null while Active or ConvertedToOrder.
    /// </summary>
    public DateTime? AbandonedAtUtc { get; set; }

    /// <summary>
    /// When session was converted to an order.
    /// Null while Active or Abandoned.
    /// </summary>
    public DateTime? ConvertedAtUtc { get; set; }

    /// <summary>
    /// The order ID if checkout was completed.
    /// Links this lead to the resulting order.
    /// </summary>
    public Guid? ConvertedOrderId { get; set; }

    /// <summary>
    /// Order number if converted (human-readable, e.g., "ORD-20260312-0001").
    /// </summary>
    public string? ConvertedOrderNumber { get; set; }

    /// <summary>
    /// When this session was invalidated during reconciliation (duplicate resolution).
    /// Null if session has never been invalidated.
    /// Used to exclude already-invalidated duplicates from recoverable session queries.
    /// Prevents non-idempotent reconciliation where invalidated sessions are re-processed.
    /// </summary>
    public DateTime? InvalidatedAtUtc { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; } = null!;
}
