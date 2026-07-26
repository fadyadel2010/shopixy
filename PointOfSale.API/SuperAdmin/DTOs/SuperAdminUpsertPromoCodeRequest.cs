using PointOfSale.Core.Enums;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminUpsertPromoCodeRequest
{
    public string? Code { get; init; }
    public PromoDiscountType? DiscountType { get; init; }
    public decimal? DiscountValue { get; init; }
    public decimal? MaxDiscountAmount { get; init; }
    public int? UsageLimit { get; init; }
    public int? MaxUsagePerCustomer { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public bool? IsActive { get; init; }
    public Guid? VendorId { get; init; }
    public Guid? CategoryId { get; init; }
}
