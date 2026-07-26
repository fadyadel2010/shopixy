namespace PointOfSale.Core.Events.WhatsApp;

public class WhatsAppKillSwitchActivatedEvent : IntegrationEvent
{
    public Guid TenantId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ActivatedBy { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

public class WhatsAppQrScannedEvent : IntegrationEvent
{
    public Guid AccountId { get; set; }
    public Guid TenantId { get; set; }
    public DateTime TimestampUtc { get; set; }
}
