using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Core.Interfaces.Treasury;

/// <summary>
/// PHASE TLW: Treasury Ledger Read Wiring.
/// 
/// Read-only service for Treasury dashboard and balance queries.
/// ALL data comes from Ledger (Accounts, JournalEntries, LedgerEntries).
/// NO business tables. NO legacy transaction tables.
/// </summary>
public interface ITreasuryReadService : ILedgerReadOnlyService
{
    /// <summary>
    /// Verifies that an account exists for the tenant.
    /// Throws AccountNotFoundException if the account is not found.
    /// </summary>
    Task EnsureAccountExistsAsync(Guid tenantId, string accountCode, CancellationToken ct = default);
    
    /// <summary>
    /// Gets the current balance of an account by its code.
    /// Balance is read directly from Account.CurrentBalance (denormalized from Ledger).
    /// </summary>
    Task<decimal> GetAccountBalanceAsync(Guid tenantId, string accountCode, CancellationToken ct = default);
    
    /// <summary>
    /// Gets account movements (debit/credit entries) for a date range.
    /// Data comes from LedgerEntries joined with JournalEntries.
    /// </summary>
    Task<IReadOnlyList<TreasuryMovementDto>> GetMovementsAsync(
        Guid tenantId,
        string accountCode,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    /// <summary>
    /// Gets account movements across multiple treasury-related accounts for a date range.
    /// </summary>
    Task<IReadOnlyList<TreasuryMovementDto>> GetMovementsAsync(
        Guid tenantId,
        IEnumerable<string> accountCodes,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
    
    /// <summary>
    /// Gets the complete Treasury dashboard data.
    /// All metrics computed from Ledger accounts:
    /// - Cash/Bank balances
    /// - Revenue, Expenses, COGS
    /// - Net Profit
    /// - Pending Clearing balances
    /// </summary>
    Task<TreasuryDashboardDto> GetDashboardAsync(
        Guid tenantId,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
    
    /// <summary>
    /// Gets balances for multiple accounts by their codes.
    /// Useful for dashboard cards.
    /// </summary>
    Task<Dictionary<string, decimal>> GetAccountBalancesAsync(
        Guid tenantId,
        IEnumerable<string> accountCodes,
        CancellationToken ct = default);

    /// <summary>
    /// Gets treasury account snapshots backed by the ledger.
    /// </summary>
    Task<IReadOnlyList<TreasuryAccountSnapshotDto>> GetTreasuryAccountsAsync(
        Guid tenantId,
        TreasuryAccountType? type = null,
        CancellationToken ct = default);
}

/// <summary>
/// Marker interface for services that read ONLY from Ledger.
/// Used for architecture enforcement/testing.
/// </summary>
public interface ILedgerReadOnlyService { }

/// <summary>
/// DTO for a single Treasury movement (ledger entry).
/// </summary>
public class TreasuryMovementDto
{
    public Guid Id { get; set; }
    public Guid JournalId { get; set; }
    public Guid ReferenceId { get; set; }
    public Guid AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? SourceType { get; set; }
    public string? SourceReference { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal BalanceAfter { get; set; }
}

/// <summary>
/// DTO for treasury accounts backed by ledger accounts.
/// </summary>
public class TreasuryAccountSnapshotDto
{
    public Guid AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public decimal CurrentBalance { get; set; }
    public TreasuryAccountType Type { get; set; }
    public string LedgerAccountType { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsClearing { get; set; }
    public bool PreventNegative { get; set; }
    public string? ReferenceCode { get; set; }
    public string? Location { get; set; }
}

/// <summary>
/// DTO for Treasury dashboard data.
/// All values computed from Ledger accounts.
/// </summary>
public class TreasuryDashboardDto
{
    // Cash & Bank
    public decimal CashboxBalance { get; set; }
    public decimal BankBalance { get; set; }
    public decimal TotalCashAndBank { get; set; }
    
    // Income Statement (for date range)
    public decimal Revenue { get; set; }
    public decimal CostOfGoodsSold { get; set; }
    public decimal Expenses { get; set; }
    public decimal NetProfit { get; set; }
    
    // Clearing Accounts (pending settlements)
    public List<ClearingAccountDto> ClearingAccounts { get; set; } = new();
    public decimal TotalPendingClearing { get; set; }
    
    // Period info
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
}

/// <summary>
/// DTO for a clearing account balance.
/// </summary>
public class ClearingAccountDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public string? SourceType { get; set; }
    public string? SourceCode { get; set; }
}
