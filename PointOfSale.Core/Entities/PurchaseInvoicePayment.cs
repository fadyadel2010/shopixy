using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Payment record for purchase invoice
/// </summary>
public class PurchaseInvoicePayment : BaseEntity
{
    public Guid PurchaseInvoiceId { get; set; }
    
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public Guid? FundingInstrumentId { get; set; }
    public Guid? FundingLedgerAccountId { get; set; }
    public int? FundingInstrumentVersion { get; set; }
    
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    
    // Navigation properties
    public PurchaseInvoice PurchaseInvoice { get; set; } = null!;
    public Accounting.SupplierCardFundingInstrument? FundingInstrument { get; set; }
    public Accounting.Account? FundingLedgerAccount { get; set; }
}
