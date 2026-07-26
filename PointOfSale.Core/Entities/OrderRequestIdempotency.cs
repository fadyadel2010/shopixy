using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class OrderRequestIdempotency : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string? SnapshotHash { get; set; }
    public Guid? OrderId { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponsePayload { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ClaimToken { get; set; }
    public DateTime? ClaimTimestampUtc { get; set; }
}
