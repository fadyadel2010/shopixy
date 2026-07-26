using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class CommerceHubSubOrderItem : BaseEntity
{
    public Guid SubOrderId { get; set; }
    public CommerceHubSubOrder SubOrder { get; set; } = null!;
    public Guid ListingId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public Guid CategoryId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string? VariantAttributesJson { get; set; }
    public string? ImageUrl { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal PlatformCommissionRate { get; set; }
    public decimal PlatformCommissionAmount { get; set; }
    public decimal FinalPlatformCommissionAmount { get; set; }
    public decimal PromoDiscountAmount { get; set; }
    public decimal Total { get; set; }
    public CommerceHubOrderItemStatus Status { get; set; } = CommerceHubOrderItemStatus.Pending;
    public DateTime? DeliveredAt { get; set; }
}
