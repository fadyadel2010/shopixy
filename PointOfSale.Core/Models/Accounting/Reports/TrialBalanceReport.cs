using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Models.Accounting.Reports;

/// <summary>
/// Trial Balance Report - Verifies accounting integrity.
/// 
/// INVARIANT: Total Debits = Total Credits
/// </summary>
public class TrialBalanceReport
{
    public Guid TenantId { get; init; }
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    
    public List<TrialBalanceLine> Lines { get; init; } = new();
    
    // Summary totals
    public decimal TotalDebits => Lines.Sum(l => l.TotalDebits);
    public decimal TotalCredits => Lines.Sum(l => l.TotalCredits);
    public decimal TotalDebitBalances => Lines.Where(l => l.Balance > 0 && l.NormalBalance == NormalBalance.Debit).Sum(l => l.Balance)
                                        + Lines.Where(l => l.Balance < 0 && l.NormalBalance == NormalBalance.Credit).Sum(l => Math.Abs(l.Balance));
    public decimal TotalCreditBalances => Lines.Where(l => l.Balance > 0 && l.NormalBalance == NormalBalance.Credit).Sum(l => l.Balance)
                                         + Lines.Where(l => l.Balance < 0 && l.NormalBalance == NormalBalance.Debit).Sum(l => Math.Abs(l.Balance));
    
    /// <summary>
    /// True if the trial balance is in balance (debits = credits).
    /// </summary>
    public bool IsBalanced => Math.Abs(TotalDebits - TotalCredits) < 0.01m;
    
    /// <summary>
    /// Difference if out of balance.
    /// </summary>
    public decimal Variance => TotalDebits - TotalCredits;
}

public class TrialBalanceLine
{
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public AccountType AccountType { get; init; }
    public NormalBalance NormalBalance { get; init; }
    
    public decimal TotalDebits { get; init; }
    public decimal TotalCredits { get; init; }
    
    /// <summary>
    /// Balance calculated based on normal balance side.
    /// Positive = balance on normal side.
    /// Negative = balance on opposite side (contra).
    /// </summary>
    public decimal Balance { get; init; }
}
