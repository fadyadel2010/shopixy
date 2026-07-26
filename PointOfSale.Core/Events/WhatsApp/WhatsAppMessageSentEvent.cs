namespace PointOfSale.Core.Events.WhatsApp;

/// <summary>
/// Raised when a message is successfully sent.
/// Used for analytics and delivery tracking.
/// </summary>
public class WhatsAppMessageSentEvent : IntegrationEvent
{
    public string MessageLogId { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string TargetPhone { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
}
