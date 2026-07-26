namespace PointOfSale.API.DTOs;

public class DailyCloseReportDto
{
    public DateTime Date { get; set; }
    
    // Cash Drawer Summary
    public decimal OpeningCashBalance { get; set; }
    public decimal CashSales { get; set; }
    public decimal CashReturns { get; set; }
    public decimal CashExpenses { get; set; }
    public decimal CashPurchases { get; set; }
    public decimal ManualCashIn { get; set; }
    public decimal ManualCashOut { get; set; }
    public decimal ExpectedClosingCash { get; set; }

    // Card/Bank Summary
    public decimal CardSales { get; set; }
    public decimal CardReturns { get; set; }
    
    // Total Sales Summary
    public decimal TotalSales { get; set; } // Cash + Card + Credit
    public int TotalTransactions { get; set; }
    
    // Details
    public List<CashMovementDto> Movements { get; set; } = new();
}

public class CashMovementDto
{
    public DateTime Time { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; } // Positive In, Negative Out
    public string Reference { get; set; } = string.Empty;
}
