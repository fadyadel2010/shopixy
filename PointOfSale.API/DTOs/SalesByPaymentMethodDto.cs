using System;
using System.Collections.Generic;

namespace PointOfSale.API.DTOs;

public class SalesByPaymentMethodRequestDto
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? SalesRepresentativeId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? CustomerId { get; set; }
}

public class SalesByPaymentMethodResponseDto
{
    // Summary totals
    public decimal TotalRevenue { get; set; }
    public decimal TotalReturns { get; set; }
    public decimal NetRevenue { get; set; }
    public int TotalInvoices { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalProfit { get; set; }

    // Breakdown by payment method
    public List<PaymentMethodDetailDto> PaymentMethods { get; set; } = new();
}

public class PaymentMethodDetailDto
{
    public string PaymentMethod { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
    public decimal GrossSales { get; set; }
    public decimal Discounts { get; set; }
    public decimal NetSales { get; set; }
    public decimal Returns { get; set; }
    public decimal NetRevenue { get; set; }
    public decimal COGS { get; set; }
    public decimal Profit { get; set; }
    public decimal PercentageOfTotal { get; set; }
}
