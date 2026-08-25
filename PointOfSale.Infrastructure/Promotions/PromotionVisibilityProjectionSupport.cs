using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Pricing;
using PointOfSale.Core.Validation;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.Promotions;

public static class PromotionVisibilityProjectionSupport
{
    public static Promotion? SelectWinningProductPromotion(
        IEnumerable<Promotion> promotions,
        DateTime nowUtc)
    {
        // Product visibility is a single-winner overlay today.
        // Higher priority wins; discount value and id only break ties so
        // transport order cannot create non-deterministic visible pricing.
        return promotions
            .Where(promotion => IsEligibleProductPromotion(promotion, nowUtc))
            .OrderByDescending(promotion => promotion.Priority)
            .ThenByDescending(promotion => promotion.DiscountValue)
            .ThenBy(promotion => promotion.Id)
            .FirstOrDefault();
    }

    public static bool IsEligibleProductPromotion(Promotion promotion, DateTime nowUtc)
    {
        if (promotion.Scope != PromotionScope.Product ||
            !promotion.IsActive ||
            promotion.IsDeleted ||
            promotion.StartDate > nowUtc ||
            promotion.EndDate < nowUtc)
        {
            return false;
        }

        return DiscountValidation.GetPromotionValidationError(
                   promotion.DiscountType,
                   promotion.DiscountValue,
                   promotion.MinOrderAmount,
                   promotion.MaxDiscountAmount,
                   promotion.MaxUsage,
                   promotion.StartDate,
                   promotion.EndDate) == null;
    }

    public static Promotion? SelectWinningCategoryPromotion(
        IEnumerable<Promotion> promotions,
        DateTime nowUtc)
    {
        return promotions
            .Where(promotion => IsEligibleCategoryPromotion(promotion, nowUtc))
            .OrderByDescending(promotion => promotion.Priority)
            .ThenByDescending(promotion => promotion.DiscountValue)
            .ThenBy(promotion => promotion.Id)
            .FirstOrDefault();
    }

    public static bool IsEligibleCategoryPromotion(Promotion promotion, DateTime nowUtc)
    {
        if (promotion.Scope != PromotionScope.Category ||
            !promotion.AppliesToId.HasValue ||
            !promotion.IsActive ||
            promotion.IsDeleted ||
            promotion.StartDate > nowUtc ||
            promotion.EndDate < nowUtc)
        {
            return false;
        }

        return DiscountValidation.GetPromotionValidationError(
                   promotion.DiscountType,
                   promotion.DiscountValue,
                   promotion.MinOrderAmount,
                   promotion.MaxDiscountAmount,
                   promotion.MaxUsage,
                   promotion.StartDate,
                   promotion.EndDate) == null;
    }

    public static decimal ComposeVisiblePrice(decimal basePrice, Promotion? promotion)
    {
        if (promotion == null)
        {
            return CurrencyHelper.Round(basePrice);
        }

        return StorefrontPromotionPriceComposer.Compose(
            basePrice,
            promotion.DiscountType,
            promotion.DiscountValue,
            promotion.MaxDiscountAmount);
    }

    public static UpdateDefinition<ProductDocument> BuildApplyProductPromotionUpdate(Promotion promotion)
    {
        return Builders<ProductDocument>.Update
            .Set(document => document.HasActivePromotion, true)
            .Set(document => document.PromotionBadge, BuildPromotionBadge(promotion.DiscountType, promotion.DiscountValue))
            .Set("ActivePromotionId", promotion.Id.ToString())
            .Set(document => document.PromotionDiscountType, ToDiscountTypeString(promotion.DiscountType))
            .Set(document => document.PromotionDiscountValue, promotion.DiscountValue)
            .Set(document => document.PromotionMaxDiscountAmount, promotion.MaxDiscountAmount)
            .Set(document => document.PromotionStartDate, promotion.StartDate)
            .Set(document => document.PromotionEndDate, promotion.EndDate)
            .Set(document => document.SearchBoostOverride, promotion.SearchBoostOverride ?? 0d);
    }

    public static void ApplyProductPromotionOverlay(ProductDocument document, Promotion? promotion)
    {
        if (promotion == null)
        {
            document.HasActivePromotion = false;
            document.PromotionBadge = null;
            document.ActivePromotionId = null;
            document.PromotionDiscountType = null;
            document.PromotionDiscountValue = null;
            document.PromotionMaxDiscountAmount = null;
            document.PromotionStartDate = null;
            document.PromotionEndDate = null;
            document.SearchBoostOverride = 0d;
            return;
        }

        document.HasActivePromotion = true;
        document.PromotionBadge = BuildPromotionBadge(promotion.DiscountType, promotion.DiscountValue);
        document.ActivePromotionId = promotion.Id;
        document.PromotionDiscountType = ToDiscountTypeString(promotion.DiscountType);
        document.PromotionDiscountValue = promotion.DiscountValue;
        document.PromotionMaxDiscountAmount = promotion.MaxDiscountAmount;
        document.PromotionStartDate = promotion.StartDate;
        document.PromotionEndDate = promotion.EndDate;
        document.SearchBoostOverride = promotion.SearchBoostOverride ?? 0d;
    }

    public static UpdateDefinition<ProductDocument> BuildClearProductPromotionUpdate()
    {
        return Builders<ProductDocument>.Update
            .Set(document => document.HasActivePromotion, false)
            .Set(document => document.PromotionBadge, (string?)null)
            .Set("ActivePromotionId", (string?)null)
            .Set(document => document.PromotionDiscountType, (string?)null)
            .Set(document => document.PromotionDiscountValue, (decimal?)null)
            .Set(document => document.PromotionMaxDiscountAmount, (decimal?)null)
            .Set(document => document.PromotionStartDate, (DateTime?)null)
            .Set(document => document.PromotionEndDate, (DateTime?)null)
            .Set(document => document.SearchBoostOverride, 0d);
    }

    public static void ApplyCategoryPromotionOverlay(CategoryDocument document, Promotion? promotion)
    {
        if (promotion == null)
        {
            document.HasActivePromotion = false;
            document.PromotionBannerText = null;
            document.ActivePromotionId = null;
            document.PromotionEndDate = null;
            document.PromotionBannerStyle = null;
            return;
        }

        document.HasActivePromotion = true;
        document.PromotionBannerText = BuildPromotionBadge(promotion.DiscountType, promotion.DiscountValue);
        document.ActivePromotionId = promotion.Id;
        document.PromotionEndDate = promotion.EndDate;
        document.PromotionBannerStyle = promotion.BannerStyle;
    }

    public static string BuildPromotionBadge(DiscountType discountType, decimal discountValue)
    {
        return discountType == DiscountType.Percentage
            ? $"{FormatDiscountValue(discountValue)}% OFF"
            : $"SAVE {(discountValue == Math.Floor(discountValue) ? discountValue.ToString("0") : discountValue.ToString("0.##"))}";
    }

    public static string ToDiscountTypeString(DiscountType discountType)
    {
        return discountType == DiscountType.Percentage ? "percentage" : "fixed";
    }

    private static string FormatDiscountValue(decimal discountValue)
    {
        return discountValue == Math.Floor(discountValue)
            ? discountValue.ToString("0")
            : discountValue.ToString("0.##");
    }
}
