using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Sales representative/salesperson
/// </summary>
public class SalesRepresentative : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this sales representative.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Monthly sales target amount (optional)
    /// </summary>
    public decimal? MonthlyTarget { get; set; }
    
    /// <summary>
    /// Commission percentage based on target achievement (optional)
    /// </summary>
    public decimal? CommissionPercentage { get; set; }
    
    public string? Notes { get; set; }
    
    // Navigation properties
    public ICollection<SalesInvoice> SalesInvoices { get; set; } = new List<SalesInvoice>();
}
