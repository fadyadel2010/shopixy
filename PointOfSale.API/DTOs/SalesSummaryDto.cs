using System;
using System.Collections.Generic;

namespace PointOfSale.API.DTOs;

public class SalesSummaryRequestDto
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SalesRepresentativeId { get; set; }
    public string? PaymentMethod { get; set; } // Cash, Visa, Bank, Credit, etc.
}

public class SalesSummaryDto
{
    // High-level Metrics
    public decimal TotalSalesGross { get; set; }
    public decimal TotalDiscounts { get; set; }
    public decimal NetSales { get; set; }
    public decimal TotalReturns { get; set; }
    public decimal NetRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalProfit { get; set; }
    public int InvoiceCount { get; set; }
    public decimal AverageInvoiceValue { get; set; }

    // Chart Data
    public List<DailySalesSummaryDto> DailySales { get; set; } = new();

    // Tables
    public List<TopProductDto> TopProducts { get; set; } = new();
    public List<TopCustomerDto> TopCustomers { get; set; } = new();
    public List<PaymentMethodSummaryDto> SalesByPaymentMethod { get; set; } = new();
}

public class DailySalesSummaryDto
{
    public DateTime Date { get; set; }
    public decimal Sales { get; set; }
    public decimal Returns { get; set; }
    public decimal Net { get; set; }
    public decimal Profit { get; set; }
}

public class TopProductDto
{
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal SalesAmount { get; set; }
}

public class TopCustomerDto
{
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalSales { get; set; }
    public int InvoiceCount { get; set; }
}

public class PaymentMethodSummaryDto
{
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Count { get; set; }
}
