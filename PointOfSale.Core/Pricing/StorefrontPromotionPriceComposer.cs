using PointOfSale.Core.Enums;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Validation;

namespace PointOfSale.Core.Pricing;

/// <summary>
/// Storefront-only promotion price composition.
/// Promotions are not global pricing authority and must not be used by affiliate or CommerceHub pricing.
/// </summary>
public static class StorefrontPromotionPriceComposer
{
    public static decimal Compose(
        decimal basePrice,
        DiscountType discountType,
        decimal discountValue,
        decimal? maxDiscountAmount)
    {
        var validationDate = DateTime.UtcNow;
        if (DiscountValidation.GetPromotionValidationError(
                discountType,
                discountValue,
                minOrderAmount: null,
                maxDiscountAmount,
                maxUsage: null,
                validationDate,
                validationDate) != null)
        {
            return CurrencyHelper.Round(basePrice);
        }

        var discount = discountType == DiscountType.Percentage
            ? basePrice * (discountValue / 100m)
            : discountValue;

        if (maxDiscountAmount.HasValue && discount > maxDiscountAmount.Value)
        {
            discount = maxDiscountAmount.Value;
        }

        discount = CurrencyHelper.Round(Math.Min(discount, basePrice));
        return CurrencyHelper.Round(Math.Max(0m, basePrice - discount));
    }

    public static decimal? TryCompose(
        decimal basePrice,
        string? discountType,
        decimal? discountValue,
        decimal? maxDiscountAmount)
    {
        if (!discountValue.HasValue || !TryParseDiscountType(discountType, out var parsedDiscountType))
        {
            return null;
        }

        var validationDate = DateTime.UtcNow;
        if (DiscountValidation.GetPromotionValidationError(
                parsedDiscountType,
                discountValue.Value,
                minOrderAmount: null,
                maxDiscountAmount,
                maxUsage: null,
                validationDate,
                validationDate) != null)
        {
            return null;
        }

        return Compose(basePrice, parsedDiscountType, discountValue.Value, maxDiscountAmount);
    }

    private static bool TryParseDiscountType(string? discountType, out DiscountType parsed)
    {
        if (string.Equals(discountType, "percentage", StringComparison.OrdinalIgnoreCase))
        {
            parsed = DiscountType.Percentage;
            return true;
        }

        if (string.Equals(discountType, "fixed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(discountType, "fixedamount", StringComparison.OrdinalIgnoreCase))
        {
            parsed = DiscountType.FixedAmount;
            return true;
        }

        parsed = default;
        return false;
    }
}
