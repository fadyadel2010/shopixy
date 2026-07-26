namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminFinanceReportQueryDto
{
    public DateTime? FromDateUtc { get; init; }
    public DateTime? ToDateUtc { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
}

public sealed class SuperAdminFinanceCashflowDto
{
    public DateTime? FromDateUtc { get; init; }
    public DateTime? ToDateUtc { get; init; }
    public decimal OnlinePaymentsIn { get; init; }
    public decimal VendorSettlementsIn { get; init; }
    public decimal CashInTotal { get; init; }
    public decimal RefundsOut { get; init; }
    public decimal VendorPayoutsOut { get; init; }
    public decimal CashOutTotal { get; init; }
    public decimal NetCashFlow { get; init; }
    public string Currency { get; init; } = "EGP";
    public int TransactionsCount { get; init; }
    public IReadOnlyList<SuperAdminFinanceCashflowPointDto> Series { get; init; } = Array.Empty<SuperAdminFinanceCashflowPointDto>();
}

public sealed class SuperAdminFinanceCashflowPointDto
{
    public DateTime DateUtc { get; init; }
    public decimal OnlinePaymentsIn { get; init; }
    public decimal VendorSettlementsIn { get; init; }
    public decimal CashInTotal { get; init; }
    public decimal RefundsOut { get; init; }
    public decimal VendorPayoutsOut { get; init; }
    public decimal CashOutTotal { get; init; }
    public decimal NetCashFlow { get; init; }
}

public sealed class SuperAdminFinanceProfitAndLossDto
{
    public DateTime? FromDateUtc { get; init; }
    public DateTime? ToDateUtc { get; init; }
    public decimal Revenue { get; init; }
    public decimal RefundLosses { get; init; }
    public decimal NetProfit { get; init; }
    public string Currency { get; init; } = "EGP";
    public int TransactionsCount { get; init; }
}

public sealed class SuperAdminFinanceReconciliationDto
{
    public string Status { get; init; } = "UNKNOWN";
    public decimal PlatformNetBalance { get; init; }
    public decimal VendorsNetBalance { get; init; }
    public int TotalTransactions { get; init; }
    public int MissingIdempotencyKeyCount { get; init; }
    public int MissingCorrelationIdCount { get; init; }
    public int DuplicateIdempotencyKeyCount { get; init; }
    public int DuplicateReferenceConstraintCount { get; init; }
    public int MissingPairCount { get; init; }
    public int DuplicateEntryCount { get; init; }
    public int CorrelationIntegrityMismatchCount { get; init; }
    public int PayableMismatchCount { get; init; }
    public int ReceivableMismatchCount { get; init; }
    public int OrphanTransactionsCount { get; init; }
    public int OrderFlowMismatchCount { get; init; }
    public int RefundFlowMismatchCount { get; init; }
    public int PayoutFlowMismatchCount { get; init; }
    public int AdjustmentFlowMismatchCount { get; init; }
    public int MultiCurrencyReferenceCount { get; init; }
    public int InvalidEntityScopeCount { get; init; }
    public DateTime ExecutedAtUtc { get; init; }
}
