using PointOfSale.Core.Enums.CommerceHubFinance;

namespace PointOfSale.API.DTOs.CommerceHub;

public sealed class CommerceHubPlatformFinanceOverviewDto
{
    public decimal CashBalance { get; set; }
    public decimal ReceivablesBalance { get; set; }
    public decimal PayablesBalance { get; set; }
    public decimal NetPosition { get; set; }
    public decimal PlatformBalance { get; set; }
    public decimal TotalCommissions { get; set; }
    public decimal CommissionReversals { get; set; }
    public decimal PromotionExpenses { get; set; }
    public decimal TotalRefunds { get; set; }
    public decimal TotalPayouts { get; set; }
    public decimal NetProfit { get; set; }
    public string Currency { get; set; } = "EGP";
    public int TransactionsCount { get; set; }
    public DateTime? LastTransactionAtUtc { get; set; }
    public List<CommerceHubFinanceBreakdownItemDto> Breakdown { get; set; } = new();
    public List<CommerceHubFinanceActivityDto> RecentTransactions { get; set; } = new();
}

public sealed class CommerceHubVendorFinanceDashboardDto
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
    public string? RequiredAction { get; set; }
    public decimal TotalEarnings { get; set; }
    public decimal TotalCommissions { get; set; }
    public decimal GrossCommissions { get; set; }
    public decimal CommissionReversals { get; set; }
    public decimal TotalRefunds { get; set; }
    public decimal TotalPayouts { get; set; }
    public string Currency { get; set; } = "EGP";
    public int TransactionsCount { get; set; }
    public DateTime? LastTransactionAtUtc { get; set; }
    public List<CommerceHubVendorSettlementDto> RecentSettlements { get; set; } = new();
    public List<CommerceHubVendorRefundImpactDto> RecentRefundImpacts { get; set; } = new();
}

public sealed class CommerceHubVendorRefundImpactDto
{
    public Guid VendorId { get; set; }
    public Guid RefundId { get; set; }
    public string ImpactType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal SignedDebtAmount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Description { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class CommerceHubFinanceStatementQueryDto
{
    public DateTime? FromDateUtc { get; set; }
    public DateTime? ToDateUtc { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public List<CommerceHubFinancialTransactionType> TransactionTypes { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int? Size { get; set; }
}

public sealed class CommerceHubFinanceStatementDto
{
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string Currency { get; set; } = "EGP";
    public DateTime? FromDateUtc { get; set; }
    public DateTime? ToDateUtc { get; set; }
    public List<string> AppliedTransactionTypes { get; set; } = new();
    public decimal CurrentBalance { get; set; }
    public decimal FilteredCredits { get; set; }
    public decimal FilteredDebits { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public bool HasMore { get; set; }
    public List<CommerceHubFinanceActivityDto> Transactions { get; set; } = new();
}

public sealed class CommerceHubFinanceBreakdownItemDto
{
    public string TransactionType { get; set; } = string.Empty;
    public decimal TotalCredits { get; set; }
    public decimal TotalDebits { get; set; }
    public decimal NetAmount { get; set; }
    public int Count { get; set; }
}

public sealed class CommerceHubFinanceActivityDto
{
    public Guid TransactionId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string FinancialBucket { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal SignedAmount { get; set; }
    public decimal? RunningBalance { get; set; }
    public string Currency { get; set; } = "EGP";
    public string ReferenceType { get; set; } = string.Empty;
    public Guid ReferenceId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
