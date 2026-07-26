namespace PointOfSale.API.DTOs;

/// <summary>
/// Accounts Payable Aging Report Response
/// </summary>
public class AccountsPayableAgingDto
{
    public DateTime ReportDate { get; set; }
    public decimal TotalOutstanding { get; set; }
    public decimal Current { get; set; }
    public decimal Days1To30 { get; set; }
    public decimal Days31To60 { get; set; }
    public decimal Days61To90 { get; set; }
    public decimal Days90Plus { get; set; }
    public List<SupplierAgingDto> Suppliers { get; set; } = new();
}

/// <summary>
/// Individual supplier aging details
/// </summary>
public class SupplierAgingDto
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
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
