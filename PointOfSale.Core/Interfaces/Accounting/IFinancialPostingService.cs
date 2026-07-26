using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Models.Accounting;

namespace PointOfSale.Core.Interfaces.Accounting;

/// <summary>
/// Core posting engine interface for double-entry accounting.
/// All financial operations MUST go through this service.
/// </summary>
public interface IFinancialPostingService
{
    /// <summary>
    /// Posts a balanced journal entry atomically.
    /// Returns existing journal if idempotency key matches (idempotent).
    /// 
    /// Algorithm:
    /// 1. Check idempotency (return existing if duplicate)
    /// 2. Validate lines (balanced, valid accounts, etc.)
    /// 3. Begin SERIALIZABLE transaction
    /// 4. Lock all involved accounts (ordered by ID to prevent deadlocks)
    /// 5. Validate PreventNegative constraints
    /// 6. Generate journal number
    /// 7. Create JournalEntry
    /// 8. For each line: ApplyEntry, create LedgerEntry with BalanceAfter
    /// 9. SaveChanges
    /// 10. Commit
    /// </summary>
    Task<JournalEntry> PostJournalAsync(
        PostingRequest request,
        CancellationToken ct = default);
    
    /// <summary>
    /// Creates a reversal journal for an existing journal.
    /// Original journal is marked as Reversed.
    /// 
    /// Creates a new journal with swapped debits/credits.
    /// </summary>
    Task<JournalEntry> ReverseJournalAsync(
        Guid tenantId,
        Guid journalId,
        string reason,
        Guid? userId = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Validates a posting request without persisting.
    /// Throws typed exceptions if validation fails.
    /// 
    /// Validations:
    /// - At least 2 lines
    /// - Debits == Credits
    /// - Exactly one side per line (debit XOR credit)
    /// - All amounts > 0
    /// - All accounts exist and are active
    /// </summary>
    Task ValidateJournalAsync(
        PostingRequest request,
        CancellationToken ct = default);
    
    /// <summary>
    /// Gets a journal entry by ID.
    /// </summary>
    Task<JournalEntry?> GetJournalAsync(
        Guid tenantId,
        Guid journalId,
        CancellationToken ct = default);
    
    /// <summary>
    /// Gets a journal entry by idempotency key.
    /// Returns null if not found.
    /// </summary>
    Task<JournalEntry?> GetJournalByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct = default);
    
    /// <summary>
    /// Checks if a journal with the given idempotency key exists.
    /// Used for quick idempotency checks without loading the full entity.
    /// </summary>
    Task<bool> ExistsByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct = default);
}

