using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>Protocol-owned attempt. Never backfill for an existing unresolved inbox row.</summary>
public sealed class ShippingWebhookClaim : ITenantOwned
{
    public Guid LogId { get; set; }
    public Guid TenantId { get; set; }
    public long Generation { get; set; }
    public Guid Owner { get; set; }
    public DateTime AcquiredAt { get; set; }
    public DateTime LeaseUntil { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string Outcome { get; set; } = "Processing";
    public string? Error { get; set; }
}
