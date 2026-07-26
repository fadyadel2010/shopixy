using System;
using System.Collections.Generic;

namespace PointOfSale.API.DTOs;

public class PurchaseReturnReportRequestDto
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? CategoryId { get; set; } // Main Category
    public Guid? SubCategoryId { get; set; } // Sub Category
    public Guid? WarehouseId { get; set; }
    public string? PaymentAdjustment { get; set; }
    public bool? Posted { get; set; }
}

public class PurchaseReturnReportDto
{
    public PurchaseReturnReportSummaryDto Summary { get; set; } = new();
    public List<PurchaseReturnReportItemDto> Items { get; set; } = new();
}

public class PurchaseReturnReportSummaryDto
{
    public decimal TotalReturnedQuantity { get; set; }
    public decimal TotalReturnValue { get; set; }
    public decimal TotalCostReversed { get; set; }
    public decimal NetFinancialImpact { get; set; }
    public string SupplierWithMostReturns { get; set; } = string.Empty;
    public string ProductWithHighestReturnRate { get; set; } = string.Empty;
}

public class PurchaseReturnReportItemDto
{
    public Guid ReturnId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public string OriginalInvoiceNumber { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal QuantityReturned { get; set; } // Base units logic or display units? Prompt says "Return with multiple units -> base quantity correct". Usually report shows display unit & qty.
    public decimal GrossReturnAmount { get; set; }
    public decimal DiscountReversed { get; set; }
    public decimal NetReturnAmount { get; set; }
    public decimal CostReversed { get; set; }
    public string Warehouse { get; set; } = string.Empty;
    public string PaymentAdjustmentType { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string PostingStatus { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
