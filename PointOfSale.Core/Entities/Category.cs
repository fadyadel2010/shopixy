using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Product category with hierarchical support
/// </summary>
public class Category : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this category.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    /// <summary>
    /// Parent category ID for hierarchical structure
    /// </summary>
    public Guid? ParentCategoryId { get; set; }
    
    // Navigation properties
    public Category? ParentCategory { get; set; }
    public ICollection<Category> SubCategories { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
