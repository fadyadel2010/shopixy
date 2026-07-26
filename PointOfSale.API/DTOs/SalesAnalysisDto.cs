namespace PointOfSale.API.DTOs;

public class SalesAnalysisRequestDto
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public class SalesAnalysisDto
{
    public decimal TotalRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalProfit { get; set; }
    public int TotalOrders { get; set; }
    public decimal AverageOrderValue { get; set; }
    
    public List<DailySalesDto> DailySales { get; set; } = new();
    public List<ProductSalesDto> TopProducts { get; set; } = new();
}

public class DailySalesDto
{
    public DateTime Date { get; set; }
    public decimal Revenue { get; set; }
    public decimal Profit { get; set; }
    public int OrderCount { get; set; }
}

public class ProductSalesDto
{
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Revenue { get; set; }
    public decimal Profit { get; set; }
}
