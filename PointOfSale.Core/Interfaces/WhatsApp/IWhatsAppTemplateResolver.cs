using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Interfaces.WhatsApp;

public interface IWhatsAppTemplateResolver
{
    Task<string> ResolveOrderConfirmationAsync(
        Guid tenantId, 
        OrderConfirmationData data, 
        string language = "en",
        CancellationToken ct = default);
    
    Task<string> ResolveOtpAsync(
        Guid tenantId,
        string otpCode,
        string language = "en",
        CancellationToken ct = default);
}

public class OrderConfirmationData
{
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int ItemsCount { get; set; }
    public string StoreName { get; set; } = string.Empty;
}
