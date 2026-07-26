using System;
using System.Collections.Generic;

namespace PointOfSale.API.DTOs;

/// <summary>
/// Request DTO for Cost Analysis Report
/// </summary>
public class CostAnalysisReportRequestDto
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SubCategoryId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? WarehouseId { get; set; }
}

/// <summary>
/// Response DTO for Cost Analysis Report
/// </summary>
public class CostAnalysisReportDto
{
    public CostAnalysisReportSummaryDto Summary { get; set; } = new();
    public List<CostAnalysisReportItemDto> Items { get; set; } = new();
}

/// <summary>
/// Summary KPIs for the Cost Analysis Report
/// </summary>
public class CostAnalysisReportSummaryDto
{
    /// <summary>
    /// Product with the highest cost increase over the period
    /// </summary>
    public string HighestCostIncreaseProduct { get; set; } = "-";
    public decimal HighestCostIncreasePercentage { get; set; }
    
    /// <summary>
    /// Product with the largest cost variance (Max - Min) / Avg
    /// </summary>
    public string LargestCostVarianceProduct { get; set; } = "-";
    public decimal LargestCostVariancePercentage { get; set; }
    
    /// <summary>
    /// Total purchase value across all products
    /// </summary>
    public decimal TotalPurchaseValue { get; set; }
    
    /// <summary>
    /// Average cost per unit across all products
    /// </summary>
    public decimal AverageCostPerUnit { get; set; }
    
    /// <summary>
    /// Total number of purchase invoices
    /// </summary>
    public int TotalInvoiceCount { get; set; }
    
    /// <summary>
    /// Total quantity purchased in base units
    /// </summary>
    public decimal TotalQuantityPurchased { get; set; }
}

/// <summary>
/// Individual product cost analysis
/// </summary>
public class CostAnalysisReportItemDto
{
    public Guid ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string SubCategoryName { get; set; } = string.Empty;
    
    /// <summary>
    /// Total purchased quantity in base unit
    /// </summary>
    public decimal TotalQuantityPurchased { get; set; }
    
    /// <summary>
    /// Average purchase cost per unit = Total Net Value / Total Base Qty
    /// </summary>
    public decimal AverageCostPerUnit { get; set; }
    
    /// <summary>
    /// Most recent purchase cost per unit
    /// </summary>
    public decimal LastPurchaseCost { get; set; }
    
    /// <summary>
    /// Date of last purchase
    /// </summary>
    public DateTime? LastPurchaseDate { get; set; }
    
    /// <summary>
    /// Minimum purchase cost per unit
    /// </summary>
    public decimal MinPurchaseCost { get; set; }
    
    /// <summary>
    /// Maximum purchase cost per unit
    /// </summary>
    public decimal MaxPurchaseCost { get; set; }
    
    /// <summary>
    /// Cost Variance % = (Max - Min) / Avg × 100
    /// </summary>
    public decimal CostVariancePercentage { get; set; }
    
    /// <summary>
    /// Total net purchase value
    /// </summary>
    public decimal TotalPurchaseValue { get; set; }
    
    /// <summary>
    /// Number of purchase invoices for this product
    /// </summary>
    public int InvoiceCount { get; set; }
    
    /// <summary>
    /// Supplier with the highest purchase quantity/value
    /// </summary>
    public string TopSupplierName { get; set; } = string.Empty;
    public Guid? TopSupplierId { get; set; }
    
    /// <summary>
    /// Cost trend: "Increasing", "Decreasing", or "Stable"
    /// </summary>
    public string CostTrend { get; set; } = "Stable";
    
    /// <summary>
    /// Cost change percentage (comparing recent to older purchases)
    /// </summary>
    public decimal CostChangePercentage { get; set; }
    
    /// <summary>
    /// Detailed purchase history for drill-down
    /// </summary>
    public List<ProductPurchaseHistoryDto> PurchaseHistory { get; set; } = new();
    
    /// <summary>
    /// Monthly cost trend data for charting
    /// </summary>
    public List<MonthlyCostTrendDto> MonthlyCostTrend { get; set; } = new();
    
    /// <summary>
    /// Cost breakdown by supplier
    /// </summary>
    public List<SupplierCostBreakdownDto> SupplierCostBreakdown { get; set; } = new();
}

/// <summary>
/// Individual purchase record for drill-down
/// </summary>
public class ProductPurchaseHistoryDto
{
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    
    /// <summary>
    /// Quantity in the purchased unit
    /// </summary>
    public decimal Quantity { get; set; }
    
    /// <summary>
    /// Quantity converted to base unit
    /// </summary>
    public decimal BaseQuantity { get; set; }
    
    /// <summary>
    /// Unit price at time of purchase
    /// </summary>
    public decimal UnitPrice { get; set; }
    
    /// <summary>
    /// Cost per base unit
    /// </summary>
    public decimal CostPerBaseUnit { get; set; }
    
    /// <summary>
    /// Total line value
    /// </summary>
    public decimal TotalValue { get; set; }
}

/// <summary>
/// Monthly aggregated cost data for trend charts
/// </summary>
public class MonthlyCostTrendDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public decimal AverageCost { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalValue { get; set; }
    public int InvoiceCount { get; set; }
}

/// <summary>
/// Average cost breakdown by supplier
/// </summary>
public class SupplierCostBreakdownDto
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal AverageCost { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalValue { get; set; }
    public int InvoiceCount { get; set; }
    public decimal ContributionPercentage { get; set; }
}
