using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class ProductRequestIdempotency : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public int? ResponseStatusCode { get; set; }
    public string? ResponseJson { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
