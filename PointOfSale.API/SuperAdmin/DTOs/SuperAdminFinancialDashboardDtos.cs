using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.SuperAdmin.DTOs;

public enum SuperAdminFinancialTrendGroupBy
{
    Day = 0,
    Month = 1
}

public enum SuperAdminPromoAnalyticsSortField
{
    PromoCode = 0,
    UsageCount = 1,
    PromoCost = 2,
    RevenueGenerated = 3,
    NetImpact = 4,
    LastUsedAt = 5
}

public enum SuperAdminVendorPerformanceSortField
{
    VendorName = 0,
    TotalSales = 1,
    GrossCommission = 2,
    PromoCost = 3,
    RefundRate = 4,
    TotalRefundAmount = 5,
    NetContribution = 6,
    OrdersCount = 7
}

public class SuperAdminFinancialDashboardFilterDto
{
    public DateTime? FromDateUtc { get; init; }
    public DateTime? ToDateUtc { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
    public DateTime? SnapshotBoundaryUtc { get; init; }

    [MaxLength(200)]
    public string? VendorSearch { get; init; }

    [MaxLength(64)]
    public string? PromoCode { get; init; }

    public virtual string? Validate()
    {
        if (FromDateUtc.HasValue && ToDateUtc.HasValue && FromDateUtc > ToDateUtc)
        {
            return "FromDateUtc cannot be after ToDateUtc.";
        }

        if (FromDate.HasValue && ToDate.HasValue && FromDate > ToDate)
        {
            return "FromDate cannot be after ToDate.";
        }

        return null;
    }
}

public sealed class SuperAdminFinancialTrendsQueryDto : SuperAdminFinancialDashboardFilterDto
{
    public SuperAdminFinancialTrendGroupBy GroupBy { get; init; } = SuperAdminFinancialTrendGroupBy.Day;
}

public sealed class SuperAdminPromoAnalyticsQueryDto : SuperAdminFinancialDashboardFilterDto
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = DefaultPage;
    public int PageSize { get; init; } = DefaultPageSize;
    public SuperAdminPromoAnalyticsSortField SortBy { get; init; } = SuperAdminPromoAnalyticsSortField.NetImpact;
    public SuperAdminSortDirection SortDirection { get; init; } = SuperAdminSortDirection.Desc;

    public override string? Validate()
    {
        var baseValidation = base.Validate();
        if (baseValidation != null)
        {
            return baseValidation;
        }

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

public sealed class SuperAdminVendorPerformanceQueryDto : SuperAdminFinancialDashboardFilterDto
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = DefaultPage;
    public int PageSize { get; init; } = DefaultPageSize;
    public SuperAdminVendorPerformanceSortField SortBy { get; init; } = SuperAdminVendorPerformanceSortField.NetContribution;
    public SuperAdminSortDirection SortDirection { get; init; } = SuperAdminSortDirection.Desc;

    public override string? Validate()
    {
        var baseValidation = base.Validate();
        if (baseValidation != null)
        {
            return baseValidation;
        }

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

public sealed class SuperAdminRefundAnalysisQueryDto : SuperAdminFinancialDashboardFilterDto
{
    public SuperAdminFinancialTrendGroupBy GroupBy { get; init; } = SuperAdminFinancialTrendGroupBy.Day;
}

public sealed class SuperAdminFinancialIntegrityCheckQueryDto : SuperAdminFinancialDashboardFilterDto
{
    public const int DefaultMaxIssues = 25;
    public const int MaxAllowedIssues = 100;

    public int MaxIssues { get; init; } = DefaultMaxIssues;

    public override string? Validate()
    {
        var baseValidation = base.Validate();
        if (baseValidation != null)
        {
            return baseValidation;
        }

        if (MaxIssues < 1 || MaxIssues > MaxAllowedIssues)
        {
            return $"MaxIssues must be between 1 and {MaxAllowedIssues}.";
        }

        return null;
    }
}

public sealed class SuperAdminFinancialOverviewDto
{
    public DateTime FromDateUtc { get; init; }
    public DateTime ToDateUtc { get; init; }
    public DateTime SnapshotBoundaryUtc { get; init; }
    public string Currency { get; init; } = "EGP";
    public string BaseCurrencyCode { get; init; } = "EGP";
    public decimal? ExchangeRateToBase { get; init; }
    public decimal TotalGrossCommission { get; init; }
    public decimal TotalPromoCost { get; init; }
    public decimal TotalNetCommission { get; init; }
    public decimal TotalRefundAmount { get; init; }
    public decimal RefundImpactOnRevenue { get; init; }
    public decimal NetRevenue { get; init; }
    public int TotalOrdersCount { get; init; }
    public decimal AverageOrderValue { get; init; }
    public int RefundedOrdersCount { get; init; }
    public decimal RefundRate { get; init; }
    public string OrderScope { get; init; } = "SubOrder";
    public string? AppliedVendorFilter { get; init; }
    public string? AppliedPromoCode { get; init; }
    public string DataSource { get; init; } = "LIVE_LEDGER";
    public bool IsEventuallyConsistent { get; init; } = true;
    public string ConsistencyModel { get; init; } =
        "Strong consistency applies to a single order flow. Aggregated dashboard metrics are eventually consistent.";
    public string DataFreshnessNote { get; init; } =
        "Financial data may be slightly delayed due to processing.";
    public DateTime GeneratedAtUtc { get; init; }
    public DateTime? AggregatedThroughUtc { get; init; }
    public SuperAdminFinancialHealthDto FinancialHealth { get; init; } = new();
    public SuperAdminDataFreshnessDto DataFreshness { get; init; } = new();
    public SuperAdminIntegrityPanelDto IntegrityPanel { get; init; } = new();
}

public sealed class SuperAdminFinancialTrendsDto
{
    public DateTime FromDateUtc { get; init; }
    public DateTime ToDateUtc { get; init; }
    public DateTime SnapshotBoundaryUtc { get; init; }
    public string Currency { get; init; } = "EGP";
    public string BaseCurrencyCode { get; init; } = "EGP";
    public decimal? ExchangeRateToBase { get; init; }
    public string GroupBy { get; init; } = SuperAdminFinancialTrendGroupBy.Day.ToString();
    public string DataSource { get; init; } = "LIVE_LEDGER";
    public bool IsEventuallyConsistent { get; init; } = true;
    public string ConsistencyModel { get; init; } =
        "Strong consistency applies to a single order flow. Aggregated dashboard metrics are eventually consistent.";
    public string DataFreshnessNote { get; init; } =
        "Financial data may be slightly delayed due to processing.";
    public DateTime GeneratedAtUtc { get; init; }
    public DateTime? AggregatedThroughUtc { get; init; }
    public SuperAdminDataFreshnessDto DataFreshness { get; init; } = new();
    public IReadOnlyList<SuperAdminFinancialTrendPointDto> Points { get; init; } =
        Array.Empty<SuperAdminFinancialTrendPointDto>();
}

public sealed class SuperAdminFinancialTrendPointDto
{
    public DateTime BucketStartUtc { get; init; }
    public decimal GrossCommission { get; init; }
    public decimal PromoCost { get; init; }
    public decimal NetCommission { get; init; }
    public decimal CommissionReversals { get; init; }
    public decimal NetRevenue { get; init; }
    public decimal RefundAmount { get; init; }
    public int OrdersCount { get; init; }
}

public sealed class SuperAdminPromoAnalyticsResponseDto
{
    public IReadOnlyList<SuperAdminPromoAnalyticsItemDto> Items { get; init; } =
        Array.Empty<SuperAdminPromoAnalyticsItemDto>();
}

public sealed class SuperAdminPromoAnalyticsItemDto
{
    public Guid? PromoCodeId { get; init; }
    public string PromoCode { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int TotalUsageCount { get; init; }
    public decimal TotalPromoCost { get; init; }
    public decimal RevenueGenerated { get; init; }
    public decimal CommissionReversals { get; init; }
    public decimal NetImpact { get; init; }
    public decimal AverageDiscountPerUse { get; init; }
    public decimal? ConversionRate { get; init; }
    public DateTime? LastUsedAtUtc { get; init; }
    public string Currency { get; init; } = "EGP";
}

public sealed class SuperAdminVendorPerformanceResponseDto
{
    public IReadOnlyList<SuperAdminVendorPerformanceItemDto> Items { get; init; } =
        Array.Empty<SuperAdminVendorPerformanceItemDto>();
}

public sealed class SuperAdminVendorPerformanceItemDto
{
    public Guid VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public string? VendorSlug { get; init; }
    public string? ContactEmail { get; init; }
    public int OrdersCount { get; init; }
    public decimal TotalSales { get; init; }
    public decimal GrossCommission { get; init; }
    public decimal PromoCost { get; init; }
    public decimal CommissionReversals { get; init; }
    public decimal TotalRefundAmount { get; init; }
    public int RefundedOrdersCount { get; init; }
    public decimal RefundRate { get; init; }
    public decimal NetContribution { get; init; }
    public string Currency { get; init; } = "EGP";
    public DateTime? LastOrderAtUtc { get; init; }
}

public sealed class SuperAdminRefundAnalysisDto
{
    public DateTime FromDateUtc { get; init; }
    public DateTime ToDateUtc { get; init; }
    public DateTime SnapshotBoundaryUtc { get; init; }
    public string Currency { get; init; } = "EGP";
    public string BaseCurrencyCode { get; init; } = "EGP";
    public decimal? ExchangeRateToBase { get; init; }
    public string GroupBy { get; init; } = SuperAdminFinancialTrendGroupBy.Day.ToString();
    public decimal TotalRefundAmount { get; init; }
    public int TotalRefundCount { get; init; }
    public int RefundedOrdersCount { get; init; }
    public decimal RefundRate { get; init; }
    public decimal PromoReversalAmount { get; init; }
    public string DataSource { get; init; } = "LIVE_LEDGER";
    public bool IsEventuallyConsistent { get; init; } = true;
    public string ConsistencyModel { get; init; } =
        "Strong consistency applies to a single order flow. Aggregated dashboard metrics are eventually consistent.";
    public string DataFreshnessNote { get; init; } =
        "Financial data may be slightly delayed due to processing.";
    public DateTime GeneratedAtUtc { get; init; }
    public DateTime? AggregatedThroughUtc { get; init; }
    public SuperAdminDataFreshnessDto DataFreshness { get; init; } = new();
    public IReadOnlyList<SuperAdminRefundTrendPointDto> Points { get; init; } =
        Array.Empty<SuperAdminRefundTrendPointDto>();
    public IReadOnlyList<SuperAdminRefundVendorItemDto> TopRefundedVendors { get; init; } =
        Array.Empty<SuperAdminRefundVendorItemDto>();
    public IReadOnlyList<SuperAdminRefundItemDto> TopRefundedItems { get; init; } =
        Array.Empty<SuperAdminRefundItemDto>();
    public IReadOnlyList<SuperAdminRefundAlertDto> Alerts { get; init; } =
        Array.Empty<SuperAdminRefundAlertDto>();
}

public sealed class SuperAdminRefundTrendPointDto
{
    public DateTime BucketStartUtc { get; init; }
    public decimal RefundAmount { get; init; }
    public int RefundCount { get; init; }
    public decimal PromoReversalAmount { get; init; }
}

public sealed class SuperAdminRefundVendorItemDto
{
    public Guid VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public decimal RefundAmount { get; init; }
    public int RefundCount { get; init; }
    public decimal PromoReversalAmount { get; init; }
    public string Currency { get; init; } = "EGP";
}

public sealed class SuperAdminRefundItemDto
{
    public Guid? OrderItemId { get; init; }
    public Guid SubOrderId { get; init; }
    public string ItemLabel { get; init; } = string.Empty;
    public string VendorName { get; init; } = string.Empty;
    public decimal RefundAmount { get; init; }
    public int RefundCount { get; init; }
    public int RefundedQuantity { get; init; }
    public string Currency { get; init; } = "EGP";
}

public sealed class SuperAdminRefundAlertDto
{
    public string Severity { get; init; } = "INFO";
    public string AlertType { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime? BucketStartUtc { get; init; }
}

public sealed class SuperAdminFinancialHealthDto
{
    public string Status { get; init; } = "Healthy";
    public int MismatchCount { get; init; }
    public DateTime? LastReportAtUtc { get; init; }
}

public sealed class SuperAdminDataFreshnessDto
{
    public string Source { get; init; } = "LIVE_LEDGER";
    public DateTime? LastUpdatedAtUtc { get; init; }
    public DateTime SnapshotBoundaryUtc { get; init; }
    public bool IsValid { get; init; } = true;
    public bool HasPendingJobs { get; init; }
}

public sealed class SuperAdminIntegrityPanelDto
{
    public string Status { get; init; } = "Healthy";
    public int MismatchCount { get; init; }
    public IReadOnlyList<SuperAdminIntegrityIssueGroupDto> Errors { get; init; } =
        Array.Empty<SuperAdminIntegrityIssueGroupDto>();
    public IReadOnlyList<SuperAdminIntegrityIssueGroupDto> Warnings { get; init; } =
        Array.Empty<SuperAdminIntegrityIssueGroupDto>();
    public IReadOnlyList<SuperAdminAffectedEntityDto> AffectedEntities { get; init; } =
        Array.Empty<SuperAdminAffectedEntityDto>();
}

public sealed class SuperAdminIntegrityIssueGroupDto
{
    public string IssueType { get; init; } = string.Empty;
    public int Count { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public sealed class SuperAdminAffectedEntityDto
{
    public string Severity { get; init; } = "ERROR";
    public string IssueType { get; init; } = string.Empty;
    public string ReferenceType { get; init; } = string.Empty;
    public Guid? ReferenceId { get; init; }
    public string Summary { get; init; } = string.Empty;
    public decimal? ExpectedAmount { get; init; }
    public decimal? ActualAmount { get; init; }
    public string? Currency { get; init; }
    public DateTime? OccurredAtUtc { get; init; }
}

public sealed class SuperAdminFinancialIntegrityCheckDto
{
    public string Status { get; init; } = "OK";
    public DateTime ExecutedAtUtc { get; init; }
    public DateTime SnapshotBoundaryUtc { get; init; }
    public string ScopeDescription { get; init; } = string.Empty;
    public bool SupportsGlobalDebitCreditParity { get; init; }
    public string DebitCreditParityStatus { get; init; } = "NOT_APPLICABLE";
    public string? DebitCreditParityNote { get; init; }
    public int TotalIssueCount { get; init; }
    public int OrphanTransactionsCount { get; init; }
    public int OrderFlowMismatchCount { get; init; }
    public int RefundFlowMismatchCount { get; init; }
    public int PayoutFlowMismatchCount { get; init; }
    public int AdjustmentFlowMismatchCount { get; init; }
    public int PayableMismatchCount { get; init; }
    public int ReceivableMismatchCount { get; init; }
    public int PromoUsageWithoutLedgerCount { get; init; }
    public int PromoLedgerWithoutUsageCount { get; init; }
    public int DuplicatePromoUsageCount { get; init; }
    public int MissingRefundReversalCount { get; init; }
    public int CorrelationIntegrityMismatchCount { get; init; }
    public int MissingPairCount { get; init; }
    public int DuplicateEntryCount { get; init; }
    public int MissingIdempotencyKeyCount { get; init; }
    public int MissingCorrelationIdCount { get; init; }
    public int MultiCurrencyReferenceCount { get; init; }
    public int InvalidEntityScopeCount { get; init; }
    public int AggregateMismatchCount { get; init; }
    public IReadOnlyList<SuperAdminIntegrityIssueGroupDto> Errors { get; init; } =
        Array.Empty<SuperAdminIntegrityIssueGroupDto>();
    public IReadOnlyList<SuperAdminIntegrityIssueGroupDto> Warnings { get; init; } =
        Array.Empty<SuperAdminIntegrityIssueGroupDto>();
    public IReadOnlyList<SuperAdminAffectedEntityDto> AffectedEntities { get; init; } =
        Array.Empty<SuperAdminAffectedEntityDto>();
    public IReadOnlyList<SuperAdminFinancialIntegrityIssueDto> Issues { get; init; } =
        Array.Empty<SuperAdminFinancialIntegrityIssueDto>();
}

public sealed class SuperAdminFinancialIntegrityIssueDto
{
    public string Severity { get; init; } = "WARNING";
    public string IssueType { get; init; } = string.Empty;
    public string ReferenceType { get; init; } = string.Empty;
    public Guid? ReferenceId { get; init; }
    public string Summary { get; init; } = string.Empty;
    public decimal? ExpectedAmount { get; init; }
    public decimal? ActualAmount { get; init; }
    public string? Currency { get; init; }
    public DateTime? OccurredAtUtc { get; init; }
}
