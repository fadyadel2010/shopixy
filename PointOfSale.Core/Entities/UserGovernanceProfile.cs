using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public sealed class UserGovernanceProfile
{
    public Guid Id { get; set; }
    public Guid SubjectId { get; set; }
    public UserGovernanceRole Role { get; set; }
    public UserGovernanceStatus Status { get; set; } = UserGovernanceStatus.Active;
    public UserGovernanceRiskLevel RiskLevel { get; set; } = UserGovernanceRiskLevel.Low;
    public bool IsKycVerified { get; set; }
    public DateTime? KycVerifiedAtUtc { get; set; }
    public Guid? KycVerifiedByAdminId { get; set; }
    public string? KycVerificationReason { get; set; }
    public bool IsMarketplaceApproved { get; set; }
    public DateTime? MarketplaceApprovedAtUtc { get; set; }
    public Guid? MarketplaceApprovedByAdminId { get; set; }
    public string? MarketplaceApprovalReason { get; set; }
    public int SuspiciousFlags { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public DateTime? StatusChangedAtUtc { get; set; }
    public DateTime? LastRiskEvaluatedAtUtc { get; set; }
    public Guid? LastModifiedByAdminId { get; set; }
    public string? StatusReason { get; set; }
    public string? LastRiskReason { get; set; }
    public string? LastIpAddress { get; set; }
    public string? DeviceFingerprint { get; set; }
}
