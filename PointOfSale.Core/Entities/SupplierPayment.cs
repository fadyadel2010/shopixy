using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Immutable supplier-level payment record used for ledger, idempotency,
/// and accounting outbox reconciliation.
/// </summary>
public class SupplierPayment : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this payment.
    /// </summary>
    public Guid TenantId { get; set; }

    public Guid SupplierId { get; set; }

    /// <summary>
    /// Optional invoice reference when the payment is tied to a specific purchase invoice.
    /// Null means this is an account-level supplier payment.
    /// </summary>
    public Guid? PurchaseInvoiceId { get; set; }

    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>
    /// Business idempotency key supplied by the caller.
    /// Unique per tenant.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }

    public Supplier Supplier { get; set; } = null!;
    public PurchaseInvoice? PurchaseInvoice { get; set; }
}
