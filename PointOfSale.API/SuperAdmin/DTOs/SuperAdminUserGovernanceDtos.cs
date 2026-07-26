using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminUserGovernanceListQuery
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = DefaultPage;
    public int PageSize { get; init; } = DefaultPageSize;
    public string? Search { get; init; }
    public UserGovernanceRole? Role { get; init; }
    public UserGovernanceStatus? Status { get; init; }
    public UserGovernanceRiskLevel? RiskLevel { get; init; }
    public DateTime? CreatedFromUtc { get; init; }
    public DateTime? CreatedToUtc { get; init; }
    public bool? HasDebt { get; init; }

    public string? Validate()
    {
        if (Page <= 0)
        {
            return "Page must be greater than zero.";
        }

        if (PageSize <= 0 || PageSize > MaxPageSize)
        {
            return $"PageSize must be between 1 and {MaxPageSize}.";
        }

        if (CreatedFromUtc.HasValue && CreatedToUtc.HasValue && CreatedFromUtc > CreatedToUtc)
        {
            return "CreatedFromUtc must be less than or equal to CreatedToUtc.";
        }

        return null;
    }
}

public sealed class SuperAdminUsersListResponseDto
{
    public IReadOnlyList<SuperAdminGovernedUserListItemDto> Users { get; init; } = Array.Empty<SuperAdminGovernedUserListItemDto>();
}

public sealed class SuperAdminGovernedUserListItemDto
{
    public Guid Id { get; init; }
    public string Role { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? VendorSlug { get; init; }
    public string Status { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;
    public bool IsKycVerified { get; init; }
    public bool IsMarketplaceApproved { get; init; }
    public bool HasDebt { get; init; }
    public int SuspiciousFlags { get; init; }
    public decimal VendorDebt { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? LastLoginAtUtc { get; init; }
}

public sealed class SuperAdminGovernedUserDetailsDto
{
    public Guid Id { get; init; }
    public string Role { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;
    public bool IsKycVerified { get; init; }
    public bool IsMarketplaceApproved { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? LastLoginAtUtc { get; init; }
    public string? StatusReason { get; init; }
    public SuperAdminGovernedUserBasicInfoDto BasicInfo { get; init; } = new();
    public SuperAdminGovernedUserFinancialSnapshotDto FinancialSnapshot { get; init; } = new();
    public SuperAdminGovernedUserActivityDto Activity { get; init; } = new();
    public SuperAdminGovernedUserRiskDto Risk { get; init; } = new();
    public SuperAdminGovernedUserAccessInfoDto AccessInfo { get; init; } = new();
    public IReadOnlyList<SuperAdminGovernedUserAuditItemDto> AuditTimeline { get; init; } = Array.Empty<SuperAdminGovernedUserAuditItemDto>();
}

public sealed class SuperAdminGovernedUserBasicInfoDto
{
    public string Name { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public string? Email { get; init; }
    public string? VendorSlug { get; init; }
}

public sealed class SuperAdminGovernedUserFinancialSnapshotDto
{
    public decimal VendorDebt { get; init; }
    public decimal VendorPayable { get; init; }
    public decimal TotalRevenue { get; init; }
    public decimal TotalRefunds { get; init; }
    public string Currency { get; init; } = "EGP";
}

public sealed class SuperAdminGovernedUserActivityDto
{
    public int OrdersCount { get; init; }
    public int RefundCount { get; init; }
    public int ReturnCount { get; init; }
}

public sealed class SuperAdminGovernedUserRiskDto
{
    public string RiskLevel { get; init; } = string.Empty;
    public decimal RefundRate { get; init; }
    public decimal DisputeRate { get; init; }
    public int FailedPayments { get; init; }
    public int SuspiciousFlags { get; init; }
    public IReadOnlyList<string> Signals { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Alerts { get; init; } = Array.Empty<string>();
    public DateTime EvaluatedAtUtc { get; init; }
}

public sealed class SuperAdminGovernedUserAccessInfoDto
{
    public string? LastIp { get; init; }
    public string? DeviceFingerprint { get; init; }
}

public sealed class SuperAdminGovernedUserAuditItemDto
{
    public Guid Id { get; init; }
    public string ActionType { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public Guid? AdminId { get; init; }
    public string ActorType { get; init; } = string.Empty;
    public string BeforeState { get; init; } = "{}";
    public string AfterState { get; init; } = "{}";
    public DateTime TimestampUtc { get; init; }
}

public sealed class SuperAdminUserGovernanceActionRequestDto
{
    [Required]
    [StringLength(1000, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class SuperAdminVendorCertificationActionRequestDto
{
    public bool Enabled { get; set; }

    [Required]
    [StringLength(1000, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class SuperAdminBlacklistListResponseDto
{
    public IReadOnlyList<SuperAdminGovernanceBlacklistItemDto> Entries { get; init; } = Array.Empty<SuperAdminGovernanceBlacklistItemDto>();
}

public sealed class SuperAdminGovernanceBlacklistItemDto
{
    public Guid Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public string ValueDisplay { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public Guid? CreatedByAdminId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public Guid? RevokedByAdminId { get; init; }
    public DateTime? RevokedAtUtc { get; init; }
    public string? RevokedReason { get; init; }
}

public sealed class SuperAdminUpsertBlacklistRequestDto
{
    [Required]
    public UserGovernanceBlacklistType Type { get; set; }

    [Required]
    [StringLength(320, MinimumLength = 2)]
    public string Value { get; set; } = string.Empty;

    [Required]
    [StringLength(1000, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
}
