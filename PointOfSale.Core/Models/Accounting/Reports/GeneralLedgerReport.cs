namespace PointOfSale.Core.Models.Accounting.Reports;

/// <summary>
/// General Ledger Report - Per-account transaction history.
/// </summary>
public class GeneralLedgerReport
{
    public Guid TenantId { get; init; }
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Opening balance at FromDate.
    /// </summary>
    public decimal OpeningBalance { get; init; }
    
    /// <summary>
    /// Transaction entries in chronological order.
    /// </summary>
    public List<GeneralLedgerEntry> Entries { get; init; } = new();
    
    /// <summary>
    /// Closing balance at ToDate.
    /// </summary>
    public decimal ClosingBalance { get; init; }
    
    /// <summary>
    /// Total debits in the period.
    /// </summary>
    public decimal TotalDebits => Entries.Sum(e => e.DebitAmount);
    
    /// <summary>
    /// Total credits in the period.
    /// </summary>
    public decimal TotalCredits => Entries.Sum(e => e.CreditAmount);
}

public class GeneralLedgerEntry
{
    public DateTime JournalDate { get; init; }
    public string JournalNumber { get; init; } = string.Empty;
    public Guid JournalId { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public string? SourceReference { get; init; }
    public string? Description { get; init; }
    
    public decimal DebitAmount { get; init; }
    public decimal CreditAmount { get; init; }
    public decimal BalanceAfter { get; init; }
}
