using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Validation;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Tenant-scoped marketing popup offer, independent from Promotions.
/// </summary>
public class SmartOffer : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public SmartOfferStatus Status { get; set; } = SmartOfferStatus.Inactive;
    public int Priority { get; set; }
    public SmartOfferTriggerType TriggerType { get; set; }
    public SmartOfferPagesScope PagesScope { get; set; } = SmartOfferPagesScope.All;
    public int? DelaySeconds { get; set; }
    public int? ScrollPercent { get; set; }
    public bool ShowOncePerSession { get; set; }
    public int? MaxDisplaysPerUser { get; set; }
    public string PopupTitle { get; set; } = string.Empty;
    public string? PopupDescription { get; set; }
    public string ButtonText { get; set; } = string.Empty;
    public string? CouponCode { get; set; }
    public Guid? GeneratedCouponId { get; set; }
    public DiscountType? CouponDiscountType { get; set; }
    public decimal? CouponDiscountValue { get; set; }
    public decimal? CouponMinOrderAmount { get; set; }
    public decimal? CouponMaxDiscountAmount { get; set; }
    public string? ImageUrl { get; set; }
    public string? BackgroundColor { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    /// <summary>
    /// Backend-owned lifecycle concurrency token. PostgreSQL maps this to xmin.
    /// </summary>
    public uint RowVersion { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CouponCode))
        {
            if (CouponDiscountType.HasValue ||
                CouponDiscountValue.HasValue ||
                CouponMinOrderAmount.HasValue ||
                CouponMaxDiscountAmount.HasValue)
            {
                throw new InvalidOperationException("Coupon code is required when coupon settings are provided.");
            }

            return;
        }

        if (!CouponDiscountValue.HasValue)
        {
            throw new InvalidOperationException("Coupon discount value must be provided when a coupon code is configured.");
        }

        DiscountValidation.EnsureValid(
            CouponDiscountType ?? DiscountType.Percentage,
            CouponDiscountValue.Value);
    }
}
