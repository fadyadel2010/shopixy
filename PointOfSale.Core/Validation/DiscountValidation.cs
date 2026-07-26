using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Validation;

public static class DiscountValidation
{
    public const decimal MaxFixedDiscountValue = 1_000_000m;

    public static string? GetValidationError(DiscountType discountType, decimal discountValue)
    {
        if (!Enum.IsDefined(discountType))
        {
            return "Discount type is invalid.";
        }

        if (discountValue <= 0m)
        {
            return "Discount must be > 0";
        }

        if (discountType == DiscountType.Percentage && discountValue > 100m)
        {
            return "Percentage cannot exceed 100%";
        }

        if (discountType == DiscountType.FixedAmount && discountValue > MaxFixedDiscountValue)
        {
            return "Discount too large";
        }

        return null;
    }

    public static string? GetValidationError(string? discountType, decimal discountValue)
    {
        if (!TryParseDiscountType(discountType, out var parsedDiscountType))
        {
            return "Discount type is invalid.";
        }

        return GetValidationError(parsedDiscountType, discountValue);
    }

    public static bool TryValidate(DiscountType discountType, decimal discountValue, out string? errorMessage)
    {
        errorMessage = GetValidationError(discountType, discountValue);
        return errorMessage == null;
    }

    public static bool TryValidate(string? discountType, decimal discountValue, out string? errorMessage)
    {
        errorMessage = GetValidationError(discountType, discountValue);
        return errorMessage == null;
    }

    public static void EnsureValid(DiscountType discountType, decimal discountValue)
    {
        var errorMessage = GetValidationError(discountType, discountValue);
        if (errorMessage != null)
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    public static string? GetCouponValidationError(
        DiscountType discountType,
        decimal discountValue,
        decimal? minOrderAmount,
        decimal? maxDiscountAmount,
        int? maxUsage,
        int? maxUsagePerCustomer,
        DateTime? startDate,
        DateTime? endDate)
    {
        var errorMessage = GetValidationError(discountType, discountValue);
        if (errorMessage != null)
        {
            return errorMessage;
        }

        if (minOrderAmount.HasValue && minOrderAmount.Value < 0m)
        {
            return "Minimum order amount cannot be negative.";
        }

        if (maxDiscountAmount.HasValue && maxDiscountAmount.Value < 0m)
        {
            return "Maximum discount amount cannot be negative.";
        }

        if (maxUsage.HasValue && maxUsage.Value < 0)
        {
            return "Max usage cannot be negative.";
        }

        if (maxUsagePerCustomer.HasValue && maxUsagePerCustomer.Value < 0)
        {
            return "Max usage per customer cannot be negative.";
        }

        if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
        {
            return "Start date cannot be after end date.";
        }

        if (maxUsage.HasValue &&
            maxUsagePerCustomer.HasValue &&
            maxUsagePerCustomer.Value > maxUsage.Value)
        {
            return "Max usage per customer cannot exceed max usage.";
        }

        if (discountType == DiscountType.FixedAmount &&
            maxDiscountAmount.HasValue &&
            maxDiscountAmount.Value < discountValue)
        {
            return "Maximum discount amount cannot be less than the fixed discount value.";
        }

        return null;
    }

    public static string? GetCouponValidationError(
        string? discountType,
        decimal discountValue,
        decimal? minOrderAmount,
        decimal? maxDiscountAmount,
        int? maxUsage,
        int? maxUsagePerCustomer,
        DateTime? startDate,
        DateTime? endDate)
    {
        if (!TryParseDiscountType(discountType, out var parsedDiscountType))
        {
            return "Discount type is invalid.";
        }

        return GetCouponValidationError(
            parsedDiscountType,
            discountValue,
            minOrderAmount,
            maxDiscountAmount,
            maxUsage,
            maxUsagePerCustomer,
            startDate,
            endDate);
    }

    public static bool TryValidateCoupon(
        DiscountType discountType,
        decimal discountValue,
        decimal? minOrderAmount,
        decimal? maxDiscountAmount,
        int? maxUsage,
        int? maxUsagePerCustomer,
        DateTime? startDate,
        DateTime? endDate,
        out string? errorMessage)
    {
        errorMessage = GetCouponValidationError(
            discountType,
            discountValue,
            minOrderAmount,
            maxDiscountAmount,
            maxUsage,
            maxUsagePerCustomer,
            startDate,
            endDate);
        return errorMessage == null;
    }

    public static bool TryValidateCoupon(
        string? discountType,
        decimal discountValue,
        decimal? minOrderAmount,
        decimal? maxDiscountAmount,
        int? maxUsage,
        int? maxUsagePerCustomer,
        DateTime? startDate,
        DateTime? endDate,
        out string? errorMessage)
    {
        errorMessage = GetCouponValidationError(
            discountType,
            discountValue,
            minOrderAmount,
            maxDiscountAmount,
            maxUsage,
            maxUsagePerCustomer,
            startDate,
            endDate);
        return errorMessage == null;
    }

    public static void EnsureCouponValid(
        DiscountType discountType,
        decimal discountValue,
        decimal? minOrderAmount,
        decimal? maxDiscountAmount,
        int? maxUsage,
        int? maxUsagePerCustomer,
        DateTime? startDate,
        DateTime? endDate)
    {
        var errorMessage = GetCouponValidationError(
            discountType,
            discountValue,
            minOrderAmount,
            maxDiscountAmount,
            maxUsage,
            maxUsagePerCustomer,
            startDate,
            endDate);
        if (errorMessage != null)
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    public static string? GetPromotionValidationError(
        DiscountType discountType,
        decimal discountValue,
        decimal? minOrderAmount,
        decimal? maxDiscountAmount,
        int? maxUsage,
        DateTime startDate,
        DateTime endDate)
    {
        var errorMessage = GetValidationError(discountType, discountValue);
        if (errorMessage != null)
        {
            return errorMessage;
        }

        if (minOrderAmount.HasValue && minOrderAmount.Value < 0m)
        {
            return "Minimum order amount cannot be negative.";
        }

        if (maxDiscountAmount.HasValue && maxDiscountAmount.Value < 0m)
        {
            return "Maximum discount amount cannot be negative.";
        }

        if (maxUsage.HasValue && maxUsage.Value < 0)
        {
            return "Max usage cannot be negative.";
        }

        if (startDate > endDate)
        {
            return "Start date cannot be after end date.";
        }

        if (discountType == DiscountType.FixedAmount &&
            maxDiscountAmount.HasValue &&
            maxDiscountAmount.Value < discountValue)
        {
            return "Maximum discount amount cannot be less than the fixed discount value.";
        }

        return null;
    }

    public static bool TryValidatePromotion(
        DiscountType discountType,
        decimal discountValue,
        decimal? minOrderAmount,
        decimal? maxDiscountAmount,
        int? maxUsage,
        DateTime startDate,
        DateTime endDate,
        out string? errorMessage)
    {
        errorMessage = GetPromotionValidationError(
            discountType,
            discountValue,
            minOrderAmount,
            maxDiscountAmount,
            maxUsage,
            startDate,
            endDate);
        return errorMessage == null;
    }

    public static void EnsurePromotionValid(
        DiscountType discountType,
        decimal discountValue,
        decimal? minOrderAmount,
        decimal? maxDiscountAmount,
        int? maxUsage,
        DateTime startDate,
        DateTime endDate)
    {
        var errorMessage = GetPromotionValidationError(
            discountType,
            discountValue,
            minOrderAmount,
            maxDiscountAmount,
            maxUsage,
            startDate,
            endDate);
        if (errorMessage != null)
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    public static bool TryParseDiscountType(string? discountType, out DiscountType parsedDiscountType)
    {
        if (string.IsNullOrWhiteSpace(discountType))
        {
            parsedDiscountType = default;
            return false;
        }

        var normalizedDiscountType = discountType.Trim();
        if (normalizedDiscountType.Equals("fixed", StringComparison.OrdinalIgnoreCase))
        {
            parsedDiscountType = DiscountType.FixedAmount;
            return true;
        }

        return Enum.TryParse(normalizedDiscountType, ignoreCase: true, out parsedDiscountType);
    }
}
