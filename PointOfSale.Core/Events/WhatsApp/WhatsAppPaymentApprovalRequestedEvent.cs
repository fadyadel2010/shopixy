namespace PointOfSale.Core.Events.WhatsApp;

/// <summary>
/// Event emitted when a manual payment is submitted and requires admin review.
/// Triggers WhatsApp notification to admin.
/// NON-BLOCKING: WhatsApp failures do not affect payment processing.
/// </summary>
public class WhatsAppPaymentApprovalRequestedEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid PaymentIntentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string? WalletType { get; set; }
    public string? AdminPhoneNumber { get; set; }
}
