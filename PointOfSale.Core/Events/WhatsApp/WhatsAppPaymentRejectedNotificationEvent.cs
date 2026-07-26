namespace PointOfSale.Core.Events.WhatsApp;

/// <summary>
/// Event emitted when admin rejects a manual payment.
/// Triggers WhatsApp notification to customer with rejection reason.
/// NON-BLOCKING: WhatsApp failures do not affect payment processing.
/// </summary>
public class WhatsAppPaymentRejectedNotificationEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid PaymentIntentId { get; set; }
    public string CustomerPhoneNumber { get; set; } = string.Empty;
    public string RejectionReason { get; set; } = string.Empty;
}
