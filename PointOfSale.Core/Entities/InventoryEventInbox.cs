using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class InventoryEventInbox : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid EventId { get; set; }
    public InventoryEventType EventType { get; set; }
    public Guid? ReferenceId { get; set; }
    public Guid? ReferenceLineId { get; set; }
    public string? PayloadHash { get; set; }
    public DateTime FirstReceivedAtUtc { get; set; }
    public DateTime? LastReceivedAtUtc { get; set; }
    public DateTime? AppliedAtUtc { get; set; }
    public InventoryEventInboxStatus Status { get; set; } = InventoryEventInboxStatus.Pending;
    public int ReplayCount { get; set; }
    public Guid? StockMovementId { get; set; }
    public string? CorrelationId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? Source { get; set; }
}
