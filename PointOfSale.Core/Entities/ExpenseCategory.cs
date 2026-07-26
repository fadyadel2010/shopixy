using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class ExpenseCategory : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this expense category.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    public string? Description { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    // Navigation
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}
