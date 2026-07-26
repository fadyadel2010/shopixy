using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class PosCartOutboxMessage : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CartId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int CartVersion { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? ProcessingError { get; set; }
    public uint RowVersion { get; set; }
}
