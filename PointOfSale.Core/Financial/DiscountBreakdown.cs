namespace PointOfSale.Core.Financial;

/// <summary>
/// Immutable discount breakdown used by checkout pricing and persisted order snapshots.
/// </summary>
public readonly record struct DiscountBreakdown(
    decimal CouponDiscount,
    decimal PromotionDiscount,
    decimal ManualDiscount)
{
    public decimal TotalDiscount => Math.Round(
        CouponDiscount + PromotionDiscount + ManualDiscount,
        2,
        MidpointRounding.AwayFromZero);
}
