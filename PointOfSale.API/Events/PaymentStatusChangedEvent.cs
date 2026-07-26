using PointOfSale.Core.Enums;

namespace PointOfSale.API.Events;

public class PaymentStatusChangedEvent
{
    public Guid PaymentIntentId { get; set; }
    public Guid OrderId { get; set; }
    public PaymentProvider Provider { get; set; }
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
