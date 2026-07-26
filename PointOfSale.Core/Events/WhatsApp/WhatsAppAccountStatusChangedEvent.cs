using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Events.WhatsApp;

/// <summary>
/// Raised when WhatsApp account status changes.
/// Used for admin notifications and session management.
/// </summary>
public class WhatsAppAccountStatusChangedEvent : IntegrationEvent
{
    public Guid AccountId { get; set; }
    public WhatsAppAccountStatus OldStatus { get; set; }
    public WhatsAppAccountStatus NewStatus { get; set; }
    public string? Reason { get; set; }
}
