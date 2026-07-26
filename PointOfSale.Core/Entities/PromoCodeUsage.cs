namespace PointOfSale.Core.Entities;

/// <summary>
/// Immutable source of truth for CommerceHub promo consumption.
/// One record is created for every successfully applied promo on a marketplace order.
/// </summary>
public class PromoCodeUsage : BaseEntity
{
    public Guid PromoCodeId { get; set; }
    public PromoCode PromoCode { get; set; } = null!;
    public Guid MarketplaceOrderId { get; set; }
    public CommerceHubMarketplaceOrder MarketplaceOrder { get; set; } = null!;
    public Guid? CustomerId { get; set; }
    public string PromoCodeSnapshot { get; set; } = string.Empty;
    public decimal DiscountAmount { get; set; }
    public decimal EligibleBaseCommissionAmount { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime AppliedAtUtc { get; set; }
}
