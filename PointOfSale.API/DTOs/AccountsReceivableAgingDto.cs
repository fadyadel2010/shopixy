namespace PointOfSale.API.DTOs;

/// <summary>
/// Accounts Receivable Aging Report Response
/// </summary>
public class AccountsReceivableAgingDto
{
    public DateTime ReportDate { get; set; }
    public decimal TotalOutstanding { get; set; }
    public decimal Current { get; set; }
    public decimal Days1To30 { get; set; }
    public decimal Days31To60 { get; set; }
    public decimal Days61To90 { get; set; }
    public decimal Days90Plus { get; set; }
    public List<CustomerAgingDto> Customers { get; set; } = new();
}

/// <summary>
/// Individual customer aging details
/// </summary>
public class CustomerAgingDto
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public decimal TotalBalance { get; set; }
    public decimal Current { get; set; }
    public decimal Days1To30 { get; set; }
    public decimal Days31To60 { get; set; }
    public decimal Days61To90 { get; set; }
    public decimal Days90Plus { get; set; }
    public DateTime? OldestInvoiceDate { get; set; }
    public int DaysOverdue { get; set; }
}

/// <summary>
/// Request parameters for aging report
/// </summary>
public class AgingReportRequestDto
{
    public DateTime? AsOfDate { get; set; }
    public bool IncludeZeroBalance { get; set; } = false;
}
