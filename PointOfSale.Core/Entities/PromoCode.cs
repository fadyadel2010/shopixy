using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class PromoCode : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public PromoDiscountType DiscountType { get; set; } = PromoDiscountType.Percentage;
    public decimal DiscountValue { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public int? UsageLimit { get; set; }
    public int? MaxUsagePerCustomer { get; set; }
    public int UsedCount { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? VendorId { get; set; }
    public Guid? CategoryId { get; set; }

    public ICollection<PromoCodeUsage> Usages { get; set; } = new List<PromoCodeUsage>();
}
