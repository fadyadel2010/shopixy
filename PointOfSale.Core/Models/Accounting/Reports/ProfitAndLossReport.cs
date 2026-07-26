using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Models.Accounting.Reports;

/// <summary>
/// Profit & Loss (Income Statement) Report.
/// 
/// Net Profit = Revenue - COGS - Expenses
/// </summary>
public class ProfitAndLossReport
{
    public Guid TenantId { get; init; }
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Revenue accounts (AccountType.Revenue).
    /// </summary>
    public List<ProfitAndLossLine> RevenueLines { get; init; } = new();
    
    /// <summary>
    /// COGS accounts (AccountType.CostOfGoodsSold).
    /// </summary>
    public List<ProfitAndLossLine> CostOfGoodsSoldLines { get; init; } = new();
    
    /// <summary>
    /// Expense accounts (AccountType.Expense).
    /// </summary>
    public List<ProfitAndLossLine> ExpenseLines { get; init; } = new();
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CALCULATED TOTALS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Total Revenue.
    /// </summary>
    public decimal TotalRevenue => RevenueLines.Sum(l => l.Amount);
    
    /// <summary>
    /// Total Cost of Goods Sold.
    /// </summary>
    public decimal TotalCostOfGoodsSold => CostOfGoodsSoldLines.Sum(l => l.Amount);
    
    /// <summary>
    /// Gross Profit = Revenue - COGS.
    /// </summary>
    public decimal GrossProfit => TotalRevenue - TotalCostOfGoodsSold;
    
    /// <summary>
    /// Gross Profit Margin %.
    /// </summary>
    public decimal GrossProfitMargin => TotalRevenue != 0 
        ? Math.Round((GrossProfit / TotalRevenue) * 100, 2) 
        : 0;
    
    /// <summary>
    /// Total Operating Expenses.
    /// </summary>
    public decimal TotalExpenses => ExpenseLines.Sum(l => l.Amount);
    
    /// <summary>
    /// Net Profit = Gross Profit - Expenses.
    /// </summary>
    public decimal NetProfit => GrossProfit - TotalExpenses;
    
    /// <summary>
    /// Net Profit Margin %.
    /// </summary>
    public decimal NetProfitMargin => TotalRevenue != 0 
        ? Math.Round((NetProfit / TotalRevenue) * 100, 2) 
        : 0;
}

public class ProfitAndLossLine
{
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public AccountType AccountType { get; init; }
    
    /// <summary>
    /// Amount for the period.
    /// Positive = normal direction.
    /// </summary>
    public decimal Amount { get; init; }
}
