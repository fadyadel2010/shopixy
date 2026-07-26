using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Tenant-scoped phone risk profile used for storefront order blocking.
/// Phase 1 only supports explicit merchant block/unblock decisions.
/// </summary>
public class PhoneRiskProfile : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public int SpamReports { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockedReason { get; set; }
    public Guid? BlockedByUserId { get; set; }
    public DateTime? BlockedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
