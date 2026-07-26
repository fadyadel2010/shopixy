using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Accounting;

/// <summary>
/// An immutable record of a financial transaction.
/// 
/// INVARIANTS:
/// - Once posted, cannot be modified (immutable)
/// - Sum(LedgerEntries.DebitAmount) == Sum(LedgerEntries.CreditAmount)
/// - Must have at least 2 ledger entries
/// - IdempotencyKey is unique per tenant
/// - Status transitions: POSTED -> REVERSED only
/// </summary>
public class JournalEntry : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    
    #region Identity
    
    /// <summary>
    /// Sequential journal number. Format: JE-YYYY-NNNNNN.
    /// Auto-generated, unique per tenant.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string JournalNumber { get; private set; } = string.Empty;
    
    /// <summary>
    /// Business date of the transaction.
    /// May differ from PostedAtUtc (e.g., backdated entries).
    /// </summary>
    public DateTime JournalDate { get; private set; }
    
    /// <summary>
    /// Exact timestamp when journal was committed to database.
    /// </summary>
    public DateTime PostedAtUtc { get; private set; }
    
    #endregion
    
    #region Source Tracking
    
    /// <summary>
    /// Type of originating document.
    /// Examples: "SalesInvoice", "Order", "Settlement", "Transfer", "Adjustment".
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string SourceType { get; private set; } = string.Empty;
    
    /// <summary>
    /// ID of the originating document.
    /// </summary>
    public Guid SourceId { get; private set; }
    
    /// <summary>
    /// Human-readable reference from source.
    /// Examples: "INV-2026-001234", "ORD-2026-005678".
    /// </summary>
    [MaxLength(100)]
    public string? SourceReference { get; set; }
    
    #endregion
    
    #region Description
    
    /// <summary>
    /// Narrative description of the transaction.
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Description { get; private set; } = string.Empty;
    
    #endregion
    
    #region Idempotency
    
    /// <summary>
    /// Unique key to prevent duplicate postings.
    /// Format depends on source type.
    /// CRITICAL: Checked before every posting attempt.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string IdempotencyKey { get; private set; } = string.Empty;
    
    #endregion
    
    #region Status
    
    /// <summary>
    /// Current status. Immutable except for reversal transition.
    /// </summary>
    public JournalStatus Status { get; private set; } = JournalStatus.Posted;
    
    /// <summary>
    /// If this journal was reversed, the reversing journal's ID.
    /// </summary>
    public Guid? ReversedByJournalId { get; private set; }
    
    /// <summary>
    /// If this journal is a reversal, the original journal's ID.
    /// </summary>
    public Guid? ReversesJournalId { get; private set; }
    
    /// <summary>
    /// Reason for reversal (required when reversing).
    /// </summary>
    [MaxLength(500)]
    public string? ReversalReason { get; private set; }
    
    /// <summary>
    /// Marks this journal as reversed.
    /// </summary>
    public void MarkReversed(Guid reversedByJournalId, string reason)
    {
        if (Status == JournalStatus.Reversed)
            throw new InvalidOperationException($"Journal {JournalNumber} is already reversed");
        
        Status = JournalStatus.Reversed;
        ReversedByJournalId = reversedByJournalId;
        ReversalReason = reason;
    }

    public void MarkAsReversal(Guid originalJournalId)
    {
        if (originalJournalId == Guid.Empty)
            throw new ArgumentException("Original journal ID is required", nameof(originalJournalId));

        ReversesJournalId = originalJournalId;
    }
    
    #endregion
    
    #region Audit
    
    /// <summary>
    /// User who created/posted this journal.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }
    
    #endregion
    
    #region Line Items
    
    /// <summary>
    /// Individual debit/credit entries. At least 2 required.
    /// </summary>
    public ICollection<LedgerEntry> LedgerEntries { get; private set; } = new List<LedgerEntry>();
    
    /// <summary>
    /// Total debits in this journal.
    /// </summary>
    [NotMapped]
    public decimal TotalDebits => LedgerEntries.Sum(e => e.DebitAmount);
    
    /// <summary>
    /// Total credits in this journal.
    /// </summary>
    [NotMapped]
    public decimal TotalCredits => LedgerEntries.Sum(e => e.CreditAmount);
    
    /// <summary>
    /// True if journal is balanced (debits == credits).
    /// </summary>
    [NotMapped]
    public bool IsBalanced => TotalDebits == TotalCredits;
    
    #endregion
    
    #region Factory
    
    /// <summary>
    /// Creates a new journal entry. Use PostingService for actual creation.
    /// </summary>
    public static JournalEntry Create(
        Guid tenantId,
        string journalNumber,
        DateTime journalDate,
        string sourceType,
        Guid sourceId,
        string? sourceReference,
        string description,
        string idempotencyKey,
        Guid? userId)
    {
        if (string.IsNullOrWhiteSpace(journalNumber))
            throw new ArgumentException("JournalNumber is required", nameof(journalNumber));
        
        if (string.IsNullOrWhiteSpace(sourceType))
            throw new ArgumentException("SourceType is required", nameof(sourceType));
        
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required", nameof(description));
        
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("IdempotencyKey is required", nameof(idempotencyKey));
        
        return new JournalEntry
        {
            TenantId = tenantId,
            JournalNumber = journalNumber,
            JournalDate = journalDate.Date,
            PostedAtUtc = DateTime.UtcNow,
            SourceType = sourceType,
            SourceId = sourceId,
            SourceReference = sourceReference,
            Description = description,
            IdempotencyKey = idempotencyKey,
            CreatedByUserId = userId,
            Status = JournalStatus.Posted
        };
    }
    
    /// <summary>
    /// Creates a reversal journal entry.
    /// </summary>
    public static JournalEntry CreateReversal(
        Guid tenantId,
        string journalNumber,
        DateTime journalDate,
        Guid originalJournalId,
        string originalJournalNumber,
        string reason,
        string idempotencyKey,
        Guid? userId)
    {
        var journal = Create(
            tenantId,
            journalNumber,
            journalDate,
            "Reversal",
            originalJournalId,
            originalJournalNumber,
            $"Reversal of {originalJournalNumber}: {reason}",
            idempotencyKey,
            userId);
        
        journal.ReversesJournalId = originalJournalId;
        
        return journal;
    }
    
    #endregion
    
    #region EF Core Setters
    
    /// <summary>
    /// Sets the journal number. Only for EF Core materialization.
    /// </summary>
    public void SetJournalNumber(string number) => JournalNumber = number;
    
    /// <summary>
    /// Sets the journal date. Only for EF Core materialization.
    /// </summary>
    public void SetJournalDate(DateTime date) => JournalDate = date;
    
    /// <summary>
    /// Sets the posted timestamp. Only for EF Core materialization.
    /// </summary>
    public void SetPostedAtUtc(DateTime timestamp) => PostedAtUtc = timestamp;
    
    /// <summary>
    /// Sets the source type. Only for EF Core materialization.
    /// </summary>
    public void SetSourceType(string type) => SourceType = type;
    
    /// <summary>
    /// Sets the source ID. Only for EF Core materialization.
    /// </summary>
    public void SetSourceId(Guid id) => SourceId = id;
    
    /// <summary>
    /// Sets the description. Only for EF Core materialization.
    /// </summary>
    public void SetDescription(string desc) => Description = desc;
    
    /// <summary>
    /// Sets the idempotency key. Only for EF Core materialization.
    /// </summary>
    public void SetIdempotencyKey(string key) => IdempotencyKey = key;
    
    #endregion
}
