using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public static class PurchaseRequestIdempotencyStatus
{
    public const string InProgress = "IN_PROGRESS";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
}

public class PurchaseRequestIdempotency : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public Guid? PurchaseInvoiceId { get; set; }
    public string? PurchaseAttemptId { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public string Status { get; set; } = PurchaseRequestIdempotencyStatus.InProgress;
    public int? ResponseStatusCode { get; set; }
    public string? ResponseJson { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
