namespace PointOfSale.API.DTOs.Storefront;

/// <summary>
/// Storefront-facing cart snapshot used by abandoned-cart recovery.
/// The shape intentionally mirrors the storefront cart store contract so the
/// client can hydrate recovered state without lossy translation.
/// </summary>
public sealed class RecoveredCartSnapshotDto
{
    public Guid CartId { get; set; }
    public Guid TenantId { get; set; }
    public string OwnerType { get; set; } = "session";
    public string OwnerId { get; set; } = string.Empty;
    public List<RecoveredCartItemSnapshotDto> Items { get; set; } = new();
    public int ItemCount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime LastUpdatedUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string? AppliedCouponCode { get; set; }
    public RecoveredAppliedCouponDto? AppliedCoupon { get; set; }
    public decimal PromotionDiscountTotal { get; set; }
    public decimal CartPromotionDiscountTotal { get; set; }
    public RecoveredAppliedCartPromotionDto? AppliedCartPromotion { get; set; }
    public decimal TotalDiscount { get; set; }
}

public sealed class RecoveredCartItemSnapshotDto
{
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductNameAr { get; set; }
    public string SKU { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public string? ImageUrl { get; set; }
    public int? AvailableStock { get; set; }
    public List<RecoveredCartVariantAttributeDto>? VariantAttributes { get; set; }
    public bool HasActivePromotion { get; set; }
    public string? PromotionBadge { get; set; }
    public string? PromotionDiscountType { get; set; }
    public decimal? PromotionDiscountValue { get; set; }
    public decimal? DiscountedUnitPrice { get; set; }
    public decimal? LineDiscount { get; set; }
    public decimal? DiscountedLineTotal { get; set; }
}

public sealed class RecoveredCartVariantAttributeDto
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class RecoveredAppliedCouponDto
{
    public string Code { get; set; } = string.Empty;
    public string Type { get; set; } = "fixed";
    public decimal Value { get; set; }
    public decimal Amount { get; set; }
}

public sealed class RecoveredAppliedCartPromotionDto
{
    public Guid PromotionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DiscountType { get; set; } = "fixed";
    public decimal DiscountValue { get; set; }
    public decimal DiscountAmount { get; set; }
    public string Badge { get; set; } = string.Empty;
}
