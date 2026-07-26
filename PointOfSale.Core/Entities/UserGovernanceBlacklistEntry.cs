using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public sealed class UserGovernanceBlacklistEntry
{
    public Guid Id { get; set; }
    public UserGovernanceBlacklistType Type { get; set; }
    public string ValueNormalized { get; set; } = string.Empty;
    public string ValueDisplay { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Guid? CreatedByAdminId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid? RevokedByAdminId { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedReason { get; set; }
}
