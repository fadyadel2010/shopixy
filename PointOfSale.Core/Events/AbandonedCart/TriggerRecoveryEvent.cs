using PointOfSale.Core.Events;

namespace PointOfSale.Core.Events.AbandonedCart;

/// <summary>
/// Event emitted when an abandoned cart is ready for recovery workflow.
/// Phase: Smart Abandoned Cart Recovery Engine - Phase 2
/// 
/// EMITTED BY:
/// - AbandonedCartScannerJob (background job)
/// 
/// CONSUMED BY:
/// - RecoveryOrchestrationConsumer (persists a native durable schedule)
/// 
/// IDEMPOTENCY:
/// - Repeated events are harmless because the SQL schedule uses a deterministic
///   tenant/cart idempotency key.
/// </summary>
public class TriggerRecoveryEvent : IntegrationEvent
{
    // NOTE: TenantId is inherited from IntegrationEvent base class
    
    /// <summary>
    /// Abandoned cart record ID.
    /// </summary>
    public Guid AbandonedCartId { get; set; }
    
    /// <summary>
    /// Cart identifier.
    /// </summary>
    public Guid CartId { get; set; }
}
