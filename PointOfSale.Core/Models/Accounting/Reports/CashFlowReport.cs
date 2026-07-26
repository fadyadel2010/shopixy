namespace PointOfSale.Core.Models.Accounting.Reports;

/// <summary>
/// Cash Flow Statement (Indirect Method).
/// 
/// Operating + Investing + Financing = Net Change in Cash
/// </summary>
public class CashFlowReport
{
    public Guid TenantId { get; init; }
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    
    // ═══════════════════════════════════════════════════════════════════════════
    // OPENING & CLOSING
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Cash balance at start of period.
    /// </summary>
    public decimal OpeningCashBalance { get; init; }
    
    /// <summary>
    /// Cash balance at end of period.
    /// </summary>
    public decimal ClosingCashBalance { get; init; }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // OPERATING ACTIVITIES (Indirect Method)
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Net profit from P&L.
    /// </summary>
    public decimal NetProfit { get; init; }
    
    /// <summary>
    /// Adjustments for non-cash items (depreciation, amortization, etc.).
    /// </summary>
    public List<CashFlowAdjustment> NonCashAdjustments { get; init; } = new();
    
    /// <summary>
    /// Changes in working capital (receivables, inventory, payables).
    /// </summary>
    public List<CashFlowAdjustment> WorkingCapitalChanges { get; init; } = new();
    
    /// <summary>
    /// Cash from operating activities.
    /// </summary>
    public decimal OperatingCashFlow => 
        NetProfit + 
        NonCashAdjustments.Sum(a => a.Amount) + 
        WorkingCapitalChanges.Sum(a => a.Amount);
    
    // ═══════════════════════════════════════════════════════════════════════════
    // INVESTING ACTIVITIES
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Investing activity items (asset purchases, disposals).
    /// </summary>
    public List<CashFlowItem> InvestingActivities { get; init; } = new();
    
    /// <summary>
    /// Cash from investing activities.
    /// </summary>
    public decimal InvestingCashFlow => InvestingActivities.Sum(i => i.Amount);
    
    // ═══════════════════════════════════════════════════════════════════════════
    // FINANCING ACTIVITIES
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Financing activity items (loans, capital, dividends).
    /// </summary>
    public List<CashFlowItem> FinancingActivities { get; init; } = new();
    
    /// <summary>
    /// Cash from financing activities.
    /// </summary>
    public decimal FinancingCashFlow => FinancingActivities.Sum(i => i.Amount);
    
    // ═══════════════════════════════════════════════════════════════════════════
    // TOTALS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Net change in cash for the period.
    /// </summary>
    public decimal NetCashChange => OperatingCashFlow + InvestingCashFlow + FinancingCashFlow;
    
    /// <summary>
    /// Whether the cash flow reconciles with opening/closing balances.
    /// </summary>
    public bool Reconciles => Math.Abs((OpeningCashBalance + NetCashChange) - ClosingCashBalance) < 0.01m;
}

public class CashFlowAdjustment
{
    public string Description { get; init; } = string.Empty;
    public string? AccountCode { get; init; }
    
    /// <summary>
    /// Positive = adds to cash, Negative = reduces cash.
    /// </summary>
    public decimal Amount { get; init; }
}

public class CashFlowItem
{
    public string Description { get; init; } = string.Empty;
    public string? AccountCode { get; init; }
    public Guid? AccountId { get; init; }
    
    /// <summary>
    /// Positive = cash inflow, Negative = cash outflow.
    /// </summary>
    public decimal Amount { get; init; }
}
