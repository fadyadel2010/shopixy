namespace PointOfSale.Core.Events;

/// <summary>
/// Event emitted when a shipping method is created, updated, activated, deactivated, or reordered.
/// 
/// ARCHITECTURE: CQRS Shipping Method Projection
/// 
/// This event triggers the MongoDB projection consumer to re-fetch
/// the shipping method from PostgreSQL and project it to MongoDB.
/// 
/// IMPORTANT:
/// - Payload is MINIMAL - only IDs and timestamp
/// - Consumer ALWAYS re-fetches from PostgreSQL (source of truth)
/// - Event is idempotent by design
/// </summary>
public class ShippingMethodUpdatedEvent : IntegrationEvent
{
    /// <summary>
    /// The shipping method that was updated.
    /// </summary>
    public Guid ShippingMethodId { get; set; }

    /// <summary>
    /// CQRS FIX (Audit CQRS-1): BaseRate is included in the event payload
    /// so the projection consumer does NOT need to read from PostgreSQL.
    /// Null means the consumer should keep the existing value.
    /// </summary>
    public decimal? BaseRate { get; set; }
}

/// <summary>
/// Event emitted when a shipping method is deleted (soft delete).
/// 
/// This triggers the MongoDB projection consumer to remove the shipping method
/// from the MongoDB read model.
/// </summary>
public class ShippingMethodDeletedEvent : IntegrationEvent
{
    /// <summary>
    /// The shipping method that was deleted.
    /// </summary>
    public Guid ShippingMethodId { get; set; }
}
