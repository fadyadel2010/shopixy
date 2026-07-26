using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Enums.CommerceHubFinance;

namespace PointOfSale.API.DTOs.CommerceHub;

public sealed class CommerceHubVendorFinancialControlDto
{
    public Guid VendorId { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal AmountOwedByVendor { get; set; }
    public string DebtStatus { get; set; } = "ACTIVE";
    public bool IsAtRisk { get; set; }
    public bool IsSuspendedFromNewOrders { get; set; }
    public bool IsGracePeriodActive { get; set; }
    public bool IsNearThreshold { get; set; }
    public decimal HardLimitAmount { get; set; }
    public int GracePeriodDays { get; set; }
    public DateTime? ThresholdCrossedAtUtc { get; set; }
    public DateTime? GraceDeadlineUtc { get; set; }
    public decimal MinimumPaymentToRestoreOrdering { get; set; }
    public decimal TotalSettlementPaymentsToPlatform { get; set; }
    public string Currency { get; set; } = "EGP";
    public int TransactionsCount { get; set; }
    public DateTime? LastTransactionAtUtc { get; set; }
    public string? RequiredAction { get; set; }
    public List<CommerceHubVendorSettlementDto> RecentSettlements { get; set; } = new();
}

public sealed class CommerceHubVendorSettlementDto
{
    public Guid SettlementId { get; set; }
    public Guid VendorId { get; set; }
    public string SettlementType { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal SignedAmount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string IdempotencyKey { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public Guid? RecordedByUserId { get; set; }
    public string? RecordedByUserEmail { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class CommerceHubVendorFinancialControlActionResultDto
{
    public CommerceHubVendorSettlementDto Settlement { get; set; } = new();
    public CommerceHubVendorFinancialControlDto Control { get; set; } = new();
}

public sealed class CommerceHubRecordSettlementPaymentRequest
{
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    
    [Required]
    [MinLength(1)]
    public string IdempotencyKey { get; set; } = string.Empty;
    
    [Required]
    [MinLength(1)]
    public string ExternalReference { get; set; } = string.Empty;
    
    public string? Notes { get; set; }
}

public sealed class CommerceHubAdjustVendorBalanceRequest
{
    public decimal Amount { get; set; }
    public CommerceHubFinancialTransactionDirection Direction { get; set; }
    public string? Currency { get; set; }
    
    [Required]
    [MinLength(1)]
    public string IdempotencyKey { get; set; } = string.Empty;
    
    [Required]
    [MinLength(1)]
    public string ExternalReference { get; set; } = string.Empty;
    
    public string? Notes { get; set; }
}

public sealed class CommerceHubClearVendorDebtRequest
{
    public string? Currency { get; set; }
    
    [Required]
    [MinLength(1)]
    public string IdempotencyKey { get; set; } = string.Empty;
    
    [Required]
    [MinLength(1)]
    public string ExternalReference { get; set; } = string.Empty;
    
    public string? Notes { get; set; }
}
