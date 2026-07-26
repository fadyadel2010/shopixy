using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.SmartOffers;

public class AdminSmartOfferDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public SmartOfferStatus Status { get; set; }
    public int Priority { get; set; }
    public SmartOfferTriggerType TriggerType { get; set; }
    public SmartOfferPagesScope PagesScope { get; set; }
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
    public string Currency { get; set; } = string.Empty;
    public decimal? CouponMinOrderAmount { get; set; }
    public decimal? CouponMaxDiscountAmount { get; set; }
    public int? UsageLimit { get; set; }
    public int UsageCount { get; set; }
    public int? UsageLimitPerCustomer { get; set; }
    public string EligibilitySummary { get; set; } = string.Empty;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public string? ImageUrl { get; set; }
    public string? BackgroundColor { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class AdminSmartOfferStatsDto
{
    public Guid TenantId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int TotalOffers { get; set; }
    public int ActiveOffers { get; set; }
    public int ExitIntentOffers { get; set; }
}

public class UpsertSmartOfferRequest
{
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
    public DiscountType? CouponDiscountType { get; set; }
    public decimal? CouponDiscountValue { get; set; }
    public decimal? CouponMinOrderAmount { get; set; }
    public decimal? CouponMaxDiscountAmount { get; set; }
    public string? ImageUrl { get; set; }
    public string? BackgroundColor { get; set; }
}

public class StorefrontSmartOfferDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
    public SmartOfferTriggerType TriggerType { get; set; }
    public SmartOfferPagesScope PagesScope { get; set; }
    public int? DelaySeconds { get; set; }
    public int? ScrollPercent { get; set; }
    public bool ShowOncePerSession { get; set; }
    public int? MaxDisplaysPerUser { get; set; }
    public string PopupTitle { get; set; } = string.Empty;
    public string? PopupDescription { get; set; }
    public string ButtonText { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? BackgroundColor { get; set; }
}

public class StorefrontSmartOfferRedeemResponseDto
{
    public string RedeemToken { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }
}
