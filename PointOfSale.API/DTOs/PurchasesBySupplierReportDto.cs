using System;
using System.Collections.Generic;

namespace PointOfSale.API.DTOs;

public class PurchasesBySupplierRequestDto
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? CategoryId { get; set; } // Main Category
    public Guid? SubCategoryId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? PaymentMethod { get; set; } // "Cash", "Visa", etc.
    public bool OnlyPosted { get; set; } = true;
}

public class PurchasesBySupplierReportDto
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    
    // Top KPIs
    public decimal TotalPurchases { get; set; }
    public decimal TotalReturns { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalOutstanding { get; set; }

    // Charts Data
    public List<SupplierShareDto> SupplierShares { get; set; } = new();
    
    // Main Data
    public List<SupplierReportItemDto> Items { get; set; } = new();
}

public class SupplierShareDto
{
    public string SupplierName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public double Percentage { get; set; }
}

public class SupplierReportItemDto
{
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    
    public int InvoicesCount { get; set; }
    public decimal GrossPurchases { get; set; } // Before discount
    public decimal Discounts { get; set; }
    public decimal NetPurchases { get; set; } // Gross - Discount
    public decimal Returns { get; set; }
    public decimal NetPayable { get; set; } // Net Purchases - Returns
    public decimal PaidAmount { get; set; }
    public decimal OutstandingBalance { get; set; }
    
    public decimal QuantityPurchased { get; set; } // In Base Unit
    public decimal AvgCostPerUnit { get; set; }
    public double ContributionPercentage { get; set; }
}
