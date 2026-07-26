namespace PointOfSale.Core.Models.Accounting.Reports;

/// <summary>
/// Clearing Aging Report - Shows pending balances in clearing accounts.
/// 
/// Clearing accounts should periodically zero out.
/// Non-zero balances indicate pending settlements.
/// </summary>
public class ClearingAgingReport
{
    public Guid TenantId { get; init; }
    public DateTime AsOfDate { get; init; }
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Clearing accounts with pending balances.
    /// </summary>
    public List<ClearingAgingLine> Lines { get; init; } = new();
    
    /// <summary>
    /// Total pending amount across all clearing accounts.
    /// </summary>
    public decimal TotalPending => Lines.Sum(l => l.PendingAmount);
    
    /// <summary>
    /// Number of clearing accounts with pending balances.
    /// </summary>
    public int AccountsWithBalance => Lines.Count(l => l.PendingAmount != 0);
    
    /// <summary>
    /// Oldest pending transaction across all accounts.
    /// </summary>
    public DateTime? OldestPending => Lines
        .Where(l => l.OldestTransactionDate.HasValue)
        .Min(l => l.OldestTransactionDate);
    
    /// <summary>
    /// Age of oldest pending transaction in days.
    /// </summary>
    public int? OldestAgeDays => OldestPending.HasValue 
        ? (int)(AsOfDate - OldestPending.Value).TotalDays 
        : null;
}

public class ClearingAgingLine
{
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    
    /// <summary>
    /// Current pending amount (should be zero when fully settled).
    /// </summary>
    public decimal PendingAmount { get; init; }
    
    /// <summary>
    /// Date of oldest uncleared transaction.
    /// </summary>
    public DateTime? OldestTransactionDate { get; init; }
    
    /// <summary>
    /// Age in days of oldest uncleared transaction.
    /// </summary>
    public int? AgeDays { get; init; }
    
    /// <summary>
    /// Number of uncleared transactions.
    /// </summary>
    public int TransactionCount { get; init; }
    
    // Aging buckets
    public decimal Current { get; init; }      // 0-7 days
    public decimal Days8To14 { get; init; }    // 8-14 days
    public decimal Days15To30 { get; init; }   // 15-30 days
    public decimal Over30Days { get; init; }   // 30+ days
}
