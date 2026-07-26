using PointOfSale.Core.Models.Accounting.Reports;

namespace PointOfSale.Core.Interfaces.Accounting;

/// <summary>
/// Financial reporting service.
/// All reports are sourced exclusively from the ledger (single source of truth).
/// 
/// PHASE 4: Financial Reporting Engine
/// </summary>
public interface IFinancialReportService
{
    // ═══════════════════════════════════════════════════════════════════════════
    // TRIAL BALANCE
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Generates a Trial Balance report for the period.
    /// 
    /// INVARIANT: Total Debits = Total Credits
    /// </summary>
    Task<TrialBalanceReport> GetTrialBalanceAsync(
        Guid tenantId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct = default);
    
    // ═══════════════════════════════════════════════════════════════════════════
    // GENERAL LEDGER
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Generates a General Ledger report for a specific account.
    /// Shows all transactions with running balance.
    /// </summary>
    Task<GeneralLedgerReport> GetGeneralLedgerAsync(
        Guid tenantId,
        Guid accountId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct = default);
    
    // ═══════════════════════════════════════════════════════════════════════════
    // PROFIT & LOSS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Generates a Profit & Loss (Income Statement) for the period.
    /// 
    /// Net Profit = Revenue - COGS - Expenses
    /// </summary>
    Task<ProfitAndLossReport> GetProfitAndLossAsync(
        Guid tenantId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct = default);
    
    // ═══════════════════════════════════════════════════════════════════════════
    // BALANCE SHEET
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Generates a Balance Sheet as of a specific date.
    /// 
    /// INVARIANT: Assets = Liabilities + Equity
    /// </summary>
    Task<BalanceSheetReport> GetBalanceSheetAsync(
        Guid tenantId,
        DateTime asOfDate,
        CancellationToken ct = default);
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CASH FLOW
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Generates a Cash Flow Statement for the period (indirect method).
    /// </summary>
    Task<CashFlowReport> GetCashFlowAsync(
        Guid tenantId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct = default);
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CLEARING AGING
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Generates a Clearing Aging Report showing pending balances in clearing accounts.
    /// </summary>
    Task<ClearingAgingReport> GetClearingAgingAsync(
        Guid tenantId,
        DateTime asOfDate,
        CancellationToken ct = default);
    
    // ═══════════════════════════════════════════════════════════════════════════
    // ACCOUNT SUMMARY
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Gets summary of all accounts with current balances.
    /// </summary>
    Task<List<AccountSummary>> GetAccountSummariesAsync(
        Guid tenantId,
        CancellationToken ct = default);
}

/// <summary>
/// Summary of an account.
/// </summary>
public record AccountSummary
{
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string AccountType { get; init; } = string.Empty;
    public decimal CurrentBalance { get; init; }
    public bool IsClearing { get; init; }
    public bool IsTreasury { get; init; }
}
