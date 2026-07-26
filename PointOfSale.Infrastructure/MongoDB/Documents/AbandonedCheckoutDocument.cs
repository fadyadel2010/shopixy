using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB read model for Abandoned Checkout projections.
/// 
/// Phase: Abandoned Checkout / Lost Orders
/// 
/// This document is projected from PostgreSQL (write model) via domain events.
/// It is the ONLY source for POS Admin abandoned checkout dashboard reads.
/// 
/// ARCHITECTURAL RULES:
/// - PostgreSQL = Write model (source of truth for checkout sessions)
/// - MongoDB = Read model (Admin dashboard projections)
/// - Admin MUST read from this collection, NOT from SQL
/// - Updates via consumer: CheckoutSessionProjectionConsumer
/// </summary>
[BsonIgnoreExtraElements]
public class AbandonedCheckoutDocument
{
    /// <summary>
    /// MongoDB internal document ID
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? Id { get; set; }

    /// <summary>
    /// The tenant that owns this checkout session.
    /// CRITICAL: Filters all admin queries.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// The PostgreSQL CheckoutSession.Id
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid CheckoutSessionId { get; set; }

    /// <summary>
    /// Cart ID from the storefront session
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid CartId { get; set; }

    /// <summary>
    /// Storefront session ID (for guest checkouts)
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Online customer ID (if authenticated)
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? OnlineCustomerId { get; set; }

    // ============================================================================
    // Customer Info (partially filled from checkout form)
    // ============================================================================

    public string? CustomerName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Area { get; set; }
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string? Notes { get; set; }

    // ============================================================================
    // Cart Snapshot
    // ============================================================================

    /// <summary>
    /// Cart items as deserialized objects for dashboard display.
    /// </summary>
    public List<AbandonedCheckoutItemSnapshot> Items { get; set; } = new();

    /// <summary>
    /// Cart subtotal at time of checkout
    /// </summary>
    public decimal? CartTotal { get; set; }

    /// <summary>
    /// Currency code (e.g., "EGP", "USD")
    /// </summary>
    public string? Currency { get; set; }

    // ============================================================================
    // Checkout Progress
    // ============================================================================

    /// <summary>
    /// Selected shipping method ID
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? ShippingMethodId { get; set; }

    /// <summary>
    /// Selected payment method code
    /// </summary>
    public string? PaymentMethodCode { get; set; }

    /// <summary>
    /// Selected wallet type for manual wallet checkout.
    /// </summary>
    public string? WalletType { get; set; }

    /// <summary>
    /// Customer wallet phone number for manual wallet checkout.
    /// </summary>
    public string? WalletPhoneNumber { get; set; }

    /// <summary>
    /// Uploaded manual wallet proof URL.
    /// </summary>
    public string? ProofImageUrl { get; set; }

    // ============================================================================
    // Status & Lifecycle
    // ============================================================================

    /// <summary>
    /// Current status: Active, Abandoned, ConvertedToOrder
    /// </summary>
    public string Status { get; set; } = "Active";

    // ============================================================================
    // Recovery & Attribution
    // ============================================================================

    /// <summary>
    /// Secure recovery token for checkout recovery links.
    /// Indexed for fast lookup via /recover-checkout/{token}.
    /// </summary>
    public string RecoveryToken { get; set; } = string.Empty;

    /// <summary>
    /// Device type: Mobile, Desktop, Tablet
    /// </summary>
    public string? DeviceType { get; set; }

    /// <summary>
    /// Traffic source: Direct, Instagram, Affiliate, GoogleAds, etc.
    /// </summary>
    public string? Source { get; set; }

    // ============================================================================
    // Timestamps
    // ============================================================================

    /// <summary>
    /// When the customer entered checkout
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When the first form field update was received.
    /// Enables: checkoutDuration = LastActivityAtUtc - FirstActivityAtUtc
    /// </summary>
    public DateTime? FirstActivityAtUtc { get; set; }

    /// <summary>
    /// Last activity (form field update, page interaction)
    /// </summary>
    public DateTime LastActivityAtUtc { get; set; }

    /// <summary>
    /// When session was marked abandoned (by background scanner)
    /// </summary>
    public DateTime? AbandonedAtUtc { get; set; }

    /// <summary>
    /// When checkout completed and order was created
    /// </summary>
    public DateTime? ConvertedAtUtc { get; set; }

    /// <summary>
    /// Order ID if checkout was completed
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? ConvertedOrderId { get; set; }

    /// <summary>
    /// Order number if converted (e.g., "ORD-20260312-0001")
    /// </summary>
    public string? ConvertedOrderNumber { get; set; }

    /// <summary>
    /// Event id that last changed the lifecycle projection.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    [BsonIgnoreIfNull]
    public Guid? LastEventId { get; set; }

    /// <summary>
    /// Source event timestamp used to reject stale lifecycle updates.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastEventOccurredAtUtc { get; set; }

    /// <summary>
    /// Source event version used as a deterministic tie-breaker for equal timestamps.
    /// </summary>
    public int LastEventVersion { get; set; }
}

/// <summary>
/// Snapshot of a single cart item at time of checkout.
/// Embedded within AbandonedCheckoutDocument for display purposes.
/// </summary>
public class AbandonedCheckoutItemSnapshot
{
    [BsonRepresentation(BsonType.String)]
    public Guid? VariantId { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
}
