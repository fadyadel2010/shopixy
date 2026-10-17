using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Promotions;

public interface ICartDiscountAuthority
{
    Task ReconcileAsync(CartDto cart, Guid? customerId, string correlationId, CancellationToken cancellationToken);
}

// Cart totals are estimates without shipping/payment selections, but their
// discount policy must agree with checkout: one SQL-evaluated winner, no stacking.
public sealed class CartDiscountAuthority(ApplicationDbContext db, IPromotionEngine engine) : ICartDiscountAuthority
{
    public async Task ReconcileAsync(CartDto cart, Guid? customerId, string correlationId, CancellationToken cancellationToken)
    {
        cart.Subtotal = Math.Round(cart.Items.Sum(i => i.UnitPrice * i.Quantity), 2, MidpointRounding.AwayFromZero);
        var ids = cart.Items.Select(i => i.ProductId).Distinct().ToArray();
        var categories = await db.Products.AsNoTracking().Where(p => p.TenantId == cart.TenantId && !p.IsDeleted && ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.CategoryId, cancellationToken);
        var result = await engine.CalculateAsync(new CartSnapshotForPromotion
        {
            TenantId = cart.TenantId, CustomerId = customerId,
            Subtotal = cart.Items.Sum(i => i.UnitPrice * i.Quantity),
            Items = cart.Items.Select(i => new CartItemForPromotion { ProductId = i.ProductId, VariantId = i.VariantId,
                CategoryId = categories.GetValueOrDefault(i.ProductId), UnitPrice = i.UnitPrice, Quantity = i.Quantity }).ToList(),
            AppliedCouponId = cart.AppliedCoupon?.CouponId, AppliedCouponCode = cart.AppliedCouponCode,
            PrevalidatedCouponDiscountAmount = cart.AppliedCoupon?.Amount,
            PrevalidatedCouponDiscountType = cart.AppliedCoupon?.Type, PrevalidatedCouponDiscountValue = cart.AppliedCoupon?.Value
        }, DateTime.UtcNow, correlationId, cancellationToken);
        // Derivative product badges are not additional price reductions.
        foreach (var item in cart.Items) { item.HasActivePromotion = false; item.DiscountedUnitPrice = null; }
        cart.ApplyCartPromotion(null);
        if (result.HasPromotion && result.PromotionType == PromotionType.Offer)
        {
            var offer = await db.Promotions.AsNoTracking().SingleAsync(p => p.TenantId == cart.TenantId && p.Id == result.PromotionId, cancellationToken);
            cart.ClearCoupon(); // persisted coupon intent is unchanged; checkout revalidates it independently
            cart.ApplyCartPromotion(new AppliedCartPromotionInfo { PromotionId = result.PromotionId!.Value, Name = result.PromotionName,
                Description = result.Description, DiscountAmount = result.DiscountAmount,
                DiscountType = offer.DiscountType == DiscountType.Percentage ? "percentage" : "fixed", DiscountValue = offer.DiscountValue });
        }
        else if (!result.HasPromotion) cart.ClearCoupon();
        cart.GrandTotal = Math.Round(cart.Subtotal - cart.TotalDiscount + cart.TaxTotal, 2, MidpointRounding.AwayFromZero);
    }
}
