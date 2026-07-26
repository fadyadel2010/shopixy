namespace PointOfSale.Core.Events.WhatsApp;

/// <summary>
/// Event emitted when admin approves a manual payment.
/// Triggers WhatsApp notification to customer.
/// NON-BLOCKING: WhatsApp failures do not affect payment processing.
/// SENSITIVE: Contains customer phone number - bypasses outbox persistence.
/// </summary>
public class WhatsAppPaymentApprovedNotificationEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent, ISensitiveIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid PaymentIntentId { get; set; }
    public string CustomerPhoneNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
