using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.Cart;

internal static class RecoveredCartSnapshotFactory
{
    public static RecoveredCartSnapshotDto FromCartDto(PointOfSale.API.DTOs.Cart.CartDto cart)
    {
        ArgumentNullException.ThrowIfNull(cart);

        return new RecoveredCartSnapshotDto
        {
            CartId = cart.CartId,
            TenantId = cart.TenantId,
            OwnerType = cart.OwnerType,
            OwnerId = cart.OwnerId,
            Items = cart.Items.Select(MapItem).ToList(),
            ItemCount = cart.ItemCount,
            Subtotal = cart.Subtotal,
            DiscountTotal = cart.DiscountTotal,
            TaxTotal = cart.TaxTotal,
            GrandTotal = cart.GrandTotal,
            Currency = cart.Currency,
            LastUpdatedUtc = cart.LastUpdatedUtc,
            ExpiresAtUtc = cart.ExpiresAtUtc,
            AppliedCouponCode = cart.AppliedCouponCode,
            AppliedCoupon = cart.AppliedCoupon == null
                ? null
                : new RecoveredAppliedCouponDto
                {
                    Code = cart.AppliedCoupon.Code,
                    Type = cart.AppliedCoupon.Type,
                    Value = cart.AppliedCoupon.Value,
                    Amount = cart.AppliedCoupon.Amount
                },
            PromotionDiscountTotal = cart.PromotionDiscountTotal,
            CartPromotionDiscountTotal = cart.CartPromotionDiscountTotal,
            AppliedCartPromotion = cart.AppliedCartPromotion == null
                ? null
                : new RecoveredAppliedCartPromotionDto
                {
                    PromotionId = cart.AppliedCartPromotion.PromotionId,
                    Name = cart.AppliedCartPromotion.Name,
                    Description = cart.AppliedCartPromotion.Description,
                    DiscountType = cart.AppliedCartPromotion.DiscountType,
                    DiscountValue = cart.AppliedCartPromotion.DiscountValue,
                    DiscountAmount = cart.AppliedCartPromotion.DiscountAmount,
                    Badge = cart.AppliedCartPromotion.Badge
                },
            TotalDiscount = cart.TotalDiscount
        };
    }

    public static RecoveredCartSnapshotDto FromCheckoutSnapshot(
        CartSnapshot snapshot,
        StorefrontCartCouponState? couponState = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var items = snapshot.Items.Select(item =>
        {
            var lineTotal = item.CartPrice * item.Quantity;
            var lineDiscount = item.CartPrice > 0m &&
                               item.CurrentPrice > 0m &&
                               item.CurrentPrice < item.CartPrice
                ? (item.CartPrice - item.CurrentPrice) * item.Quantity
                : 0m;
            var discountedLineTotal = lineDiscount > 0m
                ? item.CurrentPrice * item.Quantity
                : lineTotal;

            return new RecoveredCartItemSnapshotDto
            {
                ProductId = item.ProductId,
                VariantId = item.VariantId,
                ProductName = item.Name,
                ProductNameAr = item.NameAr,
                SKU = item.SKU,
                UnitPrice = item.CartPrice,
                Quantity = item.Quantity,
                LineTotal = lineTotal,
                ImageUrl = item.ThumbnailImage,
                VariantAttributes = item.VariantAttributes?.Select(attribute => new RecoveredCartVariantAttributeDto
                {
                    Name = attribute.Name,
                    Value = attribute.Value
                }).ToList(),
                HasActivePromotion = lineDiscount > 0m,
                DiscountedUnitPrice = lineDiscount > 0m ? item.CurrentPrice : null,
                LineDiscount = lineDiscount > 0m ? lineDiscount : null,
                DiscountedLineTotal = lineDiscount > 0m ? discountedLineTotal : null
            };
        }).ToList();

        var subtotal = items.Sum(item => item.LineTotal);
        var promotionDiscountTotal = snapshot.PromotionDiscountTotal;
        var cartPromotionDiscountTotal = snapshot.CartPromotionDiscountTotal;
        var discountTotal = snapshot.DiscountTotal;
        var grandTotal = subtotal - promotionDiscountTotal - cartPromotionDiscountTotal - discountTotal;
        if (grandTotal < 0m)
        {
            grandTotal = 0m;
        }

        return new RecoveredCartSnapshotDto
        {
            CartId = snapshot.CartId,
            TenantId = snapshot.TenantId,
            OwnerType = snapshot.OwnerType,
            OwnerId = snapshot.OwnerId,
            Items = items,
            ItemCount = items.Sum(item => item.Quantity),
            Subtotal = subtotal,
            DiscountTotal = discountTotal,
            TaxTotal = 0m,
            GrandTotal = grandTotal,
            Currency = snapshot.Currency,
            LastUpdatedUtc = snapshot.CartUpdatedAtUtc,
            ExpiresAtUtc = snapshot.CartUpdatedAtUtc.AddHours(24),
            AppliedCouponCode = snapshot.AppliedCouponCode,
            AppliedCoupon = BuildCouponSnapshot(snapshot, couponState),
            PromotionDiscountTotal = promotionDiscountTotal,
            CartPromotionDiscountTotal = cartPromotionDiscountTotal,
            AppliedCartPromotion = BuildPromotionSnapshot(snapshot),
            TotalDiscount = promotionDiscountTotal + cartPromotionDiscountTotal + discountTotal
        };
    }

    private static RecoveredCartItemSnapshotDto MapItem(PointOfSale.API.DTOs.Cart.CartItemDto item)
    {
        return new RecoveredCartItemSnapshotDto
        {
            ProductId = item.ProductId,
            VariantId = item.VariantId,
            ProductName = item.Name,
            ProductNameAr = item.NameAr,
            SKU = item.SKU,
            UnitPrice = item.UnitPrice,
            Quantity = item.Quantity,
            LineTotal = item.LineTotal,
            ImageUrl = item.ThumbnailImage,
            AvailableStock = item.AvailableStock,
            VariantAttributes = item.VariantAttributes?.Select(attribute => new RecoveredCartVariantAttributeDto
            {
                Name = attribute.Name,
                Value = attribute.Value
            }).ToList() ?? new List<RecoveredCartVariantAttributeDto>(),
            HasActivePromotion = item.HasActivePromotion,
            PromotionBadge = item.PromotionBadge,
            PromotionDiscountType = item.PromotionDiscountType,
            PromotionDiscountValue = item.PromotionDiscountValue,
            DiscountedUnitPrice = item.DiscountedUnitPrice,
            LineDiscount = item.LineDiscount,
            DiscountedLineTotal = item.DiscountedLineTotal
        };
    }

    private static RecoveredAppliedCouponDto? BuildCouponSnapshot(
        CartSnapshot snapshot,
        StorefrontCartCouponState? couponState)
    {
        if (couponState?.HasAppliedCoupon == true &&
            !string.IsNullOrWhiteSpace(couponState.CouponCode) &&
            !string.IsNullOrWhiteSpace(couponState.DiscountType) &&
            couponState.DiscountValue.HasValue &&
            couponState.DiscountAmount.HasValue)
        {
            return new RecoveredAppliedCouponDto
            {
                Code = couponState.CouponCode,
                Type = couponState.DiscountType,
                Value = couponState.DiscountValue.Value,
                Amount = couponState.DiscountAmount.Value
            };
        }

        if (string.IsNullOrWhiteSpace(snapshot.AppliedCouponCode) || snapshot.DiscountTotal <= 0m)
        {
            return null;
        }

        return new RecoveredAppliedCouponDto
        {
            Code = snapshot.AppliedCouponCode,
            Type = "fixed",
            Value = snapshot.DiscountTotal,
            Amount = snapshot.DiscountTotal
        };
    }

    private static RecoveredAppliedCartPromotionDto? BuildPromotionSnapshot(CartSnapshot snapshot)
    {
        if (!snapshot.AppliedPromotionId.HasValue || snapshot.CartPromotionDiscountTotal <= 0m)
        {
            return null;
        }

        var promotionName = string.IsNullOrWhiteSpace(snapshot.AppliedPromotionName)
            ? "Promotion"
            : snapshot.AppliedPromotionName.Trim();

        return new RecoveredAppliedCartPromotionDto
        {
            PromotionId = snapshot.AppliedPromotionId.Value,
            Name = promotionName,
            Description = null,
            DiscountType = "fixed",
            DiscountValue = snapshot.CartPromotionDiscountTotal,
            DiscountAmount = snapshot.CartPromotionDiscountTotal,
            Badge = promotionName
        };
    }
}
