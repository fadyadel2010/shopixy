using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class FinancialTransaction : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this transaction.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    public Guid AccountId { get; set; }
    public FinancialAccount Account { get; set; } = null!;
    
    public decimal Amount { get; set; }
    public TransactionType TransactionType { get; set; }
    
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    
    public Guid? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }
}
