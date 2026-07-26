using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Models.Accounting.Reports;

/// <summary>
/// Balance Sheet Report.
/// 
/// INVARIANT: Assets = Liabilities + Equity
/// </summary>
public class BalanceSheetReport
{
    public Guid TenantId { get; init; }
    public DateTime AsOfDate { get; init; }
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Asset accounts (AccountType.Asset).
    /// </summary>
    public List<BalanceSheetLine> AssetLines { get; init; } = new();
    
    /// <summary>
    /// Liability accounts (AccountType.Liability).
    /// </summary>
    public List<BalanceSheetLine> LiabilityLines { get; init; } = new();
    
    /// <summary>
    /// Equity accounts (AccountType.Equity).
    /// </summary>
    public List<BalanceSheetLine> EquityLines { get; init; } = new();
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CALCULATED TOTALS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Total Assets.
    /// </summary>
    public decimal TotalAssets => AssetLines.Sum(l => l.Balance);
    
    /// <summary>
    /// Total Liabilities.
    /// </summary>
    public decimal TotalLiabilities => LiabilityLines.Sum(l => l.Balance);
    
    /// <summary>
    /// Total Equity.
    /// </summary>
    public decimal TotalEquity => EquityLines.Sum(l => l.Balance);
    
    /// <summary>
    /// Liabilities + Equity (should equal Total Assets).
    /// </summary>
    public decimal TotalLiabilitiesAndEquity => TotalLiabilities + TotalEquity;
    
    /// <summary>
    /// Whether the balance sheet equation holds.
    /// Assets = Liabilities + Equity
    /// </summary>
    public bool IsBalanced => Math.Abs(TotalAssets - TotalLiabilitiesAndEquity) < 0.01m;
    
    /// <summary>
    /// Difference if out of balance.
    /// </summary>
    public decimal Variance => TotalAssets - TotalLiabilitiesAndEquity;
    
    // ═══════════════════════════════════════════════════════════════════════════
    // LIQUIDITY RATIOS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Current Ratio = Current Assets / Current Liabilities.
    /// </summary>
    public decimal? CurrentRatio { get; init; }
    
    /// <summary>
    /// Debt-to-Equity Ratio = Total Liabilities / Total Equity.
    /// </summary>
    public decimal? DebtToEquityRatio => TotalEquity != 0 
        ? Math.Round(TotalLiabilities / TotalEquity, 2) 
        : null;
}

public class BalanceSheetLine
{
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public AccountType AccountType { get; init; }
    
    /// <summary>
    /// Current balance.
    /// </summary>
    public decimal Balance { get; init; }
    
    /// <summary>
    /// Whether this is a current (short-term) item.
    /// </summary>
    public bool IsCurrent { get; init; }
}
