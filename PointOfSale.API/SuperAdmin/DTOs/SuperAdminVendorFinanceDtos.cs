using System.ComponentModel.DataAnnotations;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Enums.CommerceHubFinance;

namespace PointOfSale.API.SuperAdmin.DTOs;

public enum SuperAdminVendorFinanceSortField
{
    LastActivityAt = 0,
    VendorName = 1,
    CurrentBalance = 2,
    TotalEarnings = 3,
    TotalCommissions = 4,
    TotalRefunds = 5,
    TotalPayouts = 6,
    RefundRatio = 7,
    Status = 8
}

public enum SuperAdminVendorFinanceStatus
{
    Active = 0,
    AtRisk = 1,
    Suspended = 2
}

public enum SuperAdminVendorSettlementFlow
{
    VendorPaysPlatform = 0,
    PlatformPaysVendor = 1
}

public sealed class SuperAdminVendorFinanceListQuery
{
    public const int DefaultPage = 1;
    public const int MinPageSize = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const decimal DefaultHighRefundRatioThreshold = 0.25m;
    public const int DefaultInactiveDays = 30;
    public const decimal SuspendedBalanceThreshold = -1000m;

    public int Page { get; init; } = DefaultPage;
    public int PageSize { get; init; } = DefaultPageSize;
    public string? Search { get; init; }
    public bool NegativeBalanceOnly { get; init; }
    public bool HighRefundRatioOnly { get; init; }
    public bool InactiveOnly { get; init; }
    public int InactiveDays { get; init; } = DefaultInactiveDays;
    public decimal HighRefundRatioThreshold { get; init; } = DefaultHighRefundRatioThreshold;
    public SuperAdminVendorFinanceSortField SortBy { get; init; } = SuperAdminVendorFinanceSortField.LastActivityAt;
    public SuperAdminSortDirection SortDirection { get; init; } = SuperAdminSortDirection.Desc;

    public string? Validate()
    {
        if (Page < 1)
        {
            return "Page must be greater than or equal to 1.";
        }

        if (PageSize < MinPageSize || PageSize > MaxPageSize)
        {
            return $"PageSize must be between {MinPageSize} and {MaxPageSize}.";
        }

        if (InactiveDays < 1 || InactiveDays > 3650)
        {
            return "InactiveDays must be between 1 and 3650.";
        }

        if (HighRefundRatioThreshold < 0m || HighRefundRatioThreshold > 5m)
        {
            return "HighRefundRatioThreshold must be between 0 and 5.";
        }

        return null;
    }
}

public sealed class SuperAdminVendorFinanceListResponseDto
{
    public IReadOnlyList<SuperAdminVendorFinanceListItemDto> Vendors { get; init; } = Array.Empty<SuperAdminVendorFinanceListItemDto>();
}

public sealed class SuperAdminVendorFinanceAuditQuery
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = DefaultPage;
    public int PageSize { get; init; } = DefaultPageSize;

    public string? Validate()
    {
        if (Page < 1)
        {
            return "Page must be greater than or equal to 1.";
        }

        if (PageSize < 1 || PageSize > MaxPageSize)
        {
            return $"PageSize must be between 1 and {MaxPageSize}.";
        }

        return null;
    }
}

public sealed class SuperAdminVendorFinanceAuditResponseDto
{
    public IReadOnlyList<SuperAdminVendorFinanceAuditItemDto> Timeline { get; init; } = Array.Empty<SuperAdminVendorFinanceAuditItemDto>();
}

public sealed class SuperAdminVendorFinanceAuditItemDto
{
    public Guid TransactionId { get; init; }
    public Guid VendorId { get; init; }
    public string ActionType { get; init; } = string.Empty;
    public Guid? ActorId { get; init; }
    public string Direction { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal SignedAmount { get; init; }
    public string Currency { get; init; } = "EGP";
    public string Reason { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public Guid ReferenceId { get; init; }
    public string ReferenceType { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class SuperAdminVendorFinanceAlertsQuery
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = DefaultPage;
    public int PageSize { get; init; } = DefaultPageSize;
    public decimal HighRefundRatioThreshold { get; init; } = SuperAdminVendorFinanceListQuery.DefaultHighRefundRatioThreshold;
    public int InactiveDays { get; init; } = SuperAdminVendorFinanceListQuery.DefaultInactiveDays;

    public string? Validate()
    {
        if (Page < 1)
        {
            return "Page must be greater than or equal to 1.";
        }

        if (PageSize < 1 || PageSize > MaxPageSize)
        {
            return $"PageSize must be between 1 and {MaxPageSize}.";
        }

        if (InactiveDays < 1 || InactiveDays > 3650)
        {
            return "InactiveDays must be between 1 and 3650.";
        }

        if (HighRefundRatioThreshold < 0m || HighRefundRatioThreshold > 5m)
        {
            return "HighRefundRatioThreshold must be between 0 and 5.";
        }

        return null;
    }
}

public sealed class SuperAdminVendorFinanceAlertsResponseDto
{
    public IReadOnlyList<SuperAdminVendorFinanceAlertItemDto> Alerts { get; init; } = Array.Empty<SuperAdminVendorFinanceAlertItemDto>();
}

public sealed class SuperAdminVendorFinanceAlertItemDto
{
    public Guid VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public string Severity { get; init; } = "WARNING";
    public string AlertType { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Currency { get; init; } = "EGP";
    public decimal CurrentBalance { get; init; }
    public decimal AmountOwedByVendor { get; init; }
    public decimal RefundRatio { get; init; }
    public bool IsGracePeriodActive { get; init; }
    public DateTime? GraceDeadlineUtc { get; init; }
    public DateTime? LastActivityAt { get; init; }
}

public sealed class SuperAdminVendorFinanceListItemDto
{
    public Guid VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public string? VendorSlug { get; init; }
    public string? ContactEmail { get; init; }
    public string Currency { get; init; } = "EGP";
    public decimal CurrentBalance { get; init; }
    public decimal AmountOwedByVendor { get; init; }
    public decimal TotalEarnings { get; init; }
    public decimal TotalCommissions { get; init; }
    public decimal TotalRefunds { get; init; }
    public decimal TotalPayouts { get; init; }
    public decimal RefundRatio { get; init; }
    public DateTime? LastActivityAt { get; init; }
    public int TransactionsCount { get; init; }
    public string Status { get; init; } = SuperAdminVendorFinanceStatus.Active.ToString().ToUpperInvariant();
    public bool IsAtRisk { get; init; }
    public bool IsSuspended { get; init; }
    public bool IsGracePeriodActive { get; init; }
    public bool IsNearThreshold { get; init; }
    public decimal DebtThresholdAmount { get; init; }
    public int GracePeriodDays { get; init; }
    public DateTime? ThresholdCrossedAtUtc { get; init; }
    public DateTime? GraceDeadlineUtc { get; init; }
    public bool HasHighRefundRatio { get; init; }
    public bool IsInactive { get; init; }
}

public sealed class SuperAdminVendorAdjustmentRequestDto
{
    [Range(typeof(decimal), "0.01", "9999999999999999")]
    public decimal Amount { get; init; }

    [Required]
    public CommerceHubFinancialTransactionDirection Direction { get; init; }

    [MaxLength(10)]
    public string? Currency { get; init; }

    [Required]
    [MaxLength(200)]
    public string IdempotencyKey { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? CorrelationId { get; init; }

    [Required]
    public string Reason { get; init; } = string.Empty;
}

public sealed class SuperAdminVendorSettlementRequestDto
{
    [Range(typeof(decimal), "0.01", "9999999999999999")]
    public decimal Amount { get; init; }

    [Required]
    public SuperAdminVendorSettlementFlow Flow { get; init; }

    [MaxLength(10)]
    public string? Currency { get; init; }

    [Required]
    [MaxLength(200)]
    public string IdempotencyKey { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? CorrelationId { get; init; }

    [Required]
    public string Reason { get; init; } = string.Empty;
}

public sealed class SuperAdminVendorFinancialActionResultDto
{
    public SuperAdminVendorFinancialActionDto Action { get; init; } = new();
    public SuperAdminVendorFinanceListItemDto Vendor { get; init; } = new();
}

public sealed class SuperAdminVendorFinancialActionDto
{
    public Guid VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public string ActionType { get; init; } = string.Empty;
    public string VendorDirection { get; init; } = string.Empty;
    public string? SettlementFlow { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "EGP";
    public string Reason { get; init; } = string.Empty;
    public Guid ReferenceId { get; init; }
    public string ReferenceType { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public Guid? CreatedBy { get; init; }
}
