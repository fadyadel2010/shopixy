using System;
using System.Collections.Generic;

namespace PointOfSale.API.DTOs;

public class SalesReturnReportRequestDto
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? SalesRepresentativeId { get; set; }
    public string? PaymentMethod { get; set; }
}

public class SalesReturnReportDto
{
    public SalesReturnReportSummaryDto Summary { get; set; } = new();
    public List<SalesReturnReportItemDto> Items { get; set; } = new();
}

public class SalesReturnReportSummaryDto
{
    public decimal TotalReturnsAmount { get; set; }
    public decimal TotalCostReversed { get; set; }
    public decimal NetFinancialImpact { get; set; }
    public decimal TotalQuantityReturned { get; set; }
    public string MostReturnedProduct { get; set; } = string.Empty;
    public string CustomerWithMostReturns { get; set; } = string.Empty;
}

public class SalesReturnReportItemDto
{
    public Guid ReturnId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public string OriginalInvoiceNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty; // Barcode or SKU
    public string Unit { get; set; } = string.Empty;
    public decimal QuantityReturned { get; set; }
    public decimal GrossReturnAmount { get; set; }
    public decimal DiscountReturned { get; set; }
    public decimal NetReturnAmount { get; set; }
    public decimal CostOfReturnedItems { get; set; }
    public decimal ProfitImpact { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
