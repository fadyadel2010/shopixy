using PointOfSale.Core.Events;

namespace PointOfSale.Core.Events.AbandonedCart;

/// <summary>
/// Event emitted when customer interacts with their cart.
/// Phase: Smart Abandoned Cart Recovery Engine - Phase 2
/// 
/// EMITTED WHEN:
/// - Item added to cart
/// - Item removed from cart
/// - Quantity changed
/// - Checkout page visited
/// - Cart updated in any way
/// 
/// CONSUMED BY:
/// - CartActivityConsumer (upserts AbandonedCart record)
/// 
/// IDEMPOTENCY:
/// - Multiple events for same cart → upsert (safe)
/// - LastActivityAtUtc always updated to latest
/// </summary>
public class CartActivityEvent : IntegrationEvent
{
    // NOTE: TenantId is inherited from IntegrationEvent base class
    
    /// <summary>
    /// Unique cart identifier from storefront session.
    /// </summary>
    public Guid CartId { get; set; }

    /// <summary>
    /// Storefront session identifier for guest or mixed checkout flows.
    /// </summary>
    public string? SessionId { get; set; }
    
    /// <summary>
    /// Customer ID if authenticated, null for guest.
    /// </summary>
    public Guid? CustomerId { get; set; }
    
    /// <summary>
    /// Customer phone number (required for WhatsApp recovery).
    /// E.164 format.
    /// </summary>
    public string? PhoneNumber { get; set; }
    
    /// <summary>
    /// Customer email (optional).
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Preferred or most recent contact channel inferred from the payload.
    /// Expected values: "phone", "email", or "phone,email".
    /// </summary>
    public string? ContactChannel { get; set; }

    /// <summary>
    /// Whether the provided phone number has been verified server-side.
    /// </summary>
    public bool PhoneVerified { get; set; }

    /// <summary>
    /// Whether the provided email address has been verified server-side.
    /// </summary>
    public bool EmailVerified { get; set; }
    
    /// <summary>
    /// Snapshot of cart contents.
    /// JSON structure: { items: [...], total: 0, currency: "USD" }
    /// </summary>
    public string CartSnapshotJson { get; set; } = "{}";
    
    // NOTE: OccurredAtUtc is inherited from IntegrationEvent base class
}
