namespace PointOfSale.Core.Models.Accounting;

/// <summary>
/// Request model for posting a journal entry.
/// </summary>
public class PostingRequest
{
    /// <summary>
    /// Tenant ID.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Business date of the transaction.
    /// </summary>
    public DateTime JournalDate { get; set; }
    
    /// <summary>
    /// Type of originating document.
    /// Examples: "SalesInvoice", "Order", "Settlement", "Transfer".
    /// </summary>
    public string SourceType { get; set; } = string.Empty;
    
    /// <summary>
    /// ID of the originating document.
    /// </summary>
    public Guid SourceId { get; set; }
    
    /// <summary>
    /// Human-readable reference number.
    /// </summary>
    public string? SourceReference { get; set; }
    
    /// <summary>
    /// Description of the transaction.
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Unique key for idempotency.
    /// Format: {OPERATION}:{TenantId}:{UniqueIds}
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    
    /// <summary>
    /// User performing the action (optional).
    /// </summary>
    public Guid? UserId { get; set; }
    
    /// <summary>
    /// Debit/credit lines. At least 2 required.
    /// </summary>
    public List<PostingLine> Lines { get; set; } = new();
}

/// <summary>
/// A single debit or credit line in a posting request.
/// </summary>
public class PostingLine
{
    /// <summary>
    /// Account to debit or credit.
    /// </summary>
    public Guid AccountId { get; set; }
    
    /// <summary>
    /// Debit amount (0 if credit).
    /// </summary>
    public decimal DebitAmount { get; set; }
    
    /// <summary>
    /// Credit amount (0 if debit).
    /// </summary>
    public decimal CreditAmount { get; set; }
    
    /// <summary>
    /// Optional line description.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Creates a debit line.
    /// </summary>
    public static PostingLine Debit(Guid accountId, decimal amount, string? description = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive", nameof(amount));
        
        return new PostingLine
        {
            AccountId = accountId,
            DebitAmount = amount,
            CreditAmount = 0,
            Description = description
        };
    }
    
    /// <summary>
    /// Creates a credit line.
    /// </summary>
    public static PostingLine Credit(Guid accountId, decimal amount, string? description = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive", nameof(amount));
        
        return new PostingLine
        {
            AccountId = accountId,
            DebitAmount = 0,
            CreditAmount = amount,
            Description = description
        };
    }
}
