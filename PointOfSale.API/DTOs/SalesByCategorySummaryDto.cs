using System;

namespace PointOfSale.API.DTOs;

public class SalesByCategorySummaryDto
{
    public Guid? MainCategoryId { get; set; }
    public string MainCategoryName { get; set; } = string.Empty;
    
    public Guid? SubCategoryId { get; set; }
    public string SubCategoryName { get; set; } = string.Empty;
    
    public decimal TotalQuantitySold { get; set; }
    public decimal GrossSalesAmount { get; set; }
    public decimal TotalDiscounts { get; set; }
    public decimal NetSales { get; set; }
    public decimal TotalReturns { get; set; }
    public decimal NetRevenue { get; set; }
    public decimal TotalCostOfGoodsSold { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal ProfitMarginPercentage { get; set; }
    public int TransactionCount { get; set; }
}

public class SalesByCategoryReportResponseDto
{
    public List<SalesByCategorySummaryDto> Categories { get; set; } = new();
    
    // Aggregate View
    public decimal TotalRevenue { get; set; }
    public decimal TotalProfit { get; set; }
    public string BestPerformingCategory { get; set; } = string.Empty;
    public string WorstPerformingCategory { get; set; } = string.Empty;
}
