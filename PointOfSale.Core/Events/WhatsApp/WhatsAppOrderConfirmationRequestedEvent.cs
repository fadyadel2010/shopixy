using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Events.WhatsApp;

/// <summary>
/// Triggered when order is created AND tenant has WhatsApp order confirmation enabled.
/// Domain layer checks feature flags before emitting this event.
/// </summary>
public class WhatsAppOrderConfirmationRequestedEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal OrderTotal { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemInfo> Items { get; set; } = new();
    public string? ShippingAddress { get; set; }
    public DateTime? EstimatedDelivery { get; set; }
    
    /// <summary>
    /// Store name for template personalization.
    /// </summary>
    public string StoreName { get; set; } = string.Empty;
}
