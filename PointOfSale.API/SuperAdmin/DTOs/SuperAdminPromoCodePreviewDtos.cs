using PointOfSale.Core.Enums;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminPromoCodePreviewRequest
{
    public Guid? PromoId { get; init; }
    public PromoDiscountType? DiscountType { get; init; }
    public decimal? DiscountValue { get; init; }
    public decimal? MaxDiscountAmount { get; init; }
    public int? MaxUsagePerCustomer { get; init; }
    public Guid? ScopeVendorId { get; init; }
    public Guid? ScopeCategoryId { get; init; }
    public decimal BaseCommissionAmount { get; init; }
    public Guid? PreviewVendorId { get; init; }
    public Guid? PreviewCategoryId { get; init; }
}

public sealed class SuperAdminPromoCodePreviewResponse
{
    public decimal BaseCommissionAmount { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal FinalCommissionAmount { get; init; }
    public bool IsApplicable { get; init; }
    public Guid? PreviewVendorId { get; init; }
    public Guid? PreviewCategoryId { get; init; }
}
