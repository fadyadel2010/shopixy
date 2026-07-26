using System;
using System.Collections.Generic;

namespace PointOfSale.API.DTOs;

/// <summary>
/// Request DTO for Purchase Register Report
/// </summary>
public class PurchaseRegisterReportRequestDto
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SubCategoryId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? PaymentMethod { get; set; }
    public bool? Posted { get; set; } // null = all, true = posted only, false = unposted only
}

/// <summary>
/// Response DTO for Purchase Register Report
/// </summary>
public class PurchaseRegisterReportDto
{
    public PurchaseRegisterReportSummaryDto Summary { get; set; } = new();
    public List<PurchaseRegisterReportItemDto> Items { get; set; } = new();
}

/// <summary>
/// Summary KPIs for the Purchase Register Report
/// </summary>
public class PurchaseRegisterReportSummaryDto
{
    public decimal TotalGrossPurchases { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalNetPurchases { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalFinalAmount { get; set; }
    public decimal TotalCashPurchases { get; set; }
    public decimal TotalCreditPurchases { get; set; }
    public decimal TotalBankPurchases { get; set; }
    public decimal TotalVisaPurchases { get; set; }
    public decimal TotalReturnsValue { get; set; }
    public int TotalInvoiceCount { get; set; }
    public int TotalItemsCount { get; set; }
    public string SupplierWithHighestVolume { get; set; } = string.Empty;
}

/// <summary>
/// Individual purchase invoice item for the report
/// </summary>
public class PurchaseRegisterReportItemDto
{
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty; // Using Phone/TaxNumber as code
    public int ItemsCount { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string CreatedByUser { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Posted / Draft
    public bool IsPosted { get; set; }
    public string? LinkedReturnNumber { get; set; } // If invoice has a return linked
    public decimal? LinkedReturnAmount { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
}
