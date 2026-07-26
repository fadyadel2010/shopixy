namespace PointOfSale.Core.Events.WhatsApp;

/// <summary>
/// Raised when a message fails after all retries.
/// Used for alerting and fallback triggering.
/// </summary>
public class WhatsAppMessageFailedEvent : IntegrationEvent
{
    public string MessageLogId { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public int Attempts { get; set; }
}
