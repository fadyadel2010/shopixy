using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class CommerceHubSubOrderStatusHistory : BaseEntity
{
    public Guid SubOrderId { get; set; }
    public CommerceHubSubOrder SubOrder { get; set; } = null!;
    public OrderStatus PreviousStatus { get; set; }
    public OrderStatus NewStatus { get; set; }
    public string? Reason { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}
