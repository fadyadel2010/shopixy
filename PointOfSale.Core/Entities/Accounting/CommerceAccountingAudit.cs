using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Accounting;

/// <summary>
/// Audit trail for commerce-to-ledger postings.
/// 
/// PHASE 3.5: Full traceability from business event to journal.
/// 
/// Provides:
/// - Forensic trail for compliance
/// - Quick lookup: "What journal posted this sale?"
/// - Reconciliation support
/// </summary>
public class CommerceAccountingAudit : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Type of business entity.
    /// Examples: "SalesInvoice", "PurchaseInvoice", "Order", "Return"
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string BusinessType { get; set; } = string.Empty;
    
    /// <summary>
    /// ID of the business entity.
    /// </summary>
    public Guid BusinessId { get; set; }
    
    /// <summary>
    /// Human-readable reference (invoice number, order number, etc.)
    /// </summary>
    [MaxLength(100)]
    public string? BusinessReference { get; set; }
    
    /// <summary>
    /// The journal entry that was created.
    /// </summary>
    public Guid JournalId { get; set; }
    
    /// <summary>
    /// Journal number for easy lookup.
    /// </summary>
    [MaxLength(50)]
    public string? JournalNumber { get; set; }
    
    /// <summary>
    /// Operation type performed.
    /// Examples: "Sale", "COGS", "Payment", "Refund", "Settlement"
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string OperationType { get; set; } = string.Empty;
    
    /// <summary>
    /// Total amount posted.
    /// </summary>
    public decimal Amount { get; set; }
    
    /// <summary>
    /// When the posting occurred.
    /// </summary>
    public DateTime PostedAtUtc { get; set; }
    
    /// <summary>
    /// Idempotency key used for this posting.
    /// </summary>
    [MaxLength(255)]
    public string? IdempotencyKey { get; set; }
    
    /// <summary>
    /// Whether this was a duplicate (idempotent) call.
    /// </summary>
    public bool WasIdempotent { get; set; }
    
    /// <summary>
    /// Navigation to the journal entry.
    /// </summary>
    public JournalEntry? Journal { get; set; }
    
    /// <summary>
    /// Creates an audit record for a successful posting.
    /// </summary>
    public static CommerceAccountingAudit Create(
        Guid tenantId,
        string businessType,
        Guid businessId,
        string? businessReference,
        Guid journalId,
        string? journalNumber,
        string operationType,
        decimal amount,
        string? idempotencyKey,
        bool wasIdempotent = false)
    {
        return new CommerceAccountingAudit
        {
            TenantId = tenantId,
            BusinessType = businessType,
            BusinessId = businessId,
            BusinessReference = businessReference,
            JournalId = journalId,
            JournalNumber = journalNumber,
            OperationType = operationType,
            Amount = amount,
            PostedAtUtc = DateTime.UtcNow,
            IdempotencyKey = idempotencyKey,
            WasIdempotent = wasIdempotent,
            CreatedAt = DateTime.UtcNow
        };
    }
}
