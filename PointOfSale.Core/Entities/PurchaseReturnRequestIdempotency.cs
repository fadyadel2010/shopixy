using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class PurchaseReturnRequestIdempotency : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? PurchaseAttemptId { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public Guid? ReturnId { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponseJson { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
