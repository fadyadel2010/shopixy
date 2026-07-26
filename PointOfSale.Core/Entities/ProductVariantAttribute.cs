namespace PointOfSale.Core.Entities;

/// <summary>
/// Attribute value for a product variant (e.g., Color=Red, Size=XL).
/// 
/// PHASE 11.1 — VARIANT ATTRIBUTES
/// 
/// Stores the attribute key-value pairs that define a variant.
/// For example, a T-Shirt variant might have:
///   - Name: "Color", Value: "Red"
///   - Name: "Size", Value: "XL"
/// 
/// These attributes are used to:
/// 1. Display variant options on the storefront
/// 2. Generate variant SKUs (optional)
/// 3. Filter and search variants
/// </summary>
public class ProductVariantAttribute : BaseEntity
{
    /// <summary>
    /// The variant this attribute belongs to.
    /// </summary>
    public Guid VariantId { get; set; }
    
    /// <summary>
    /// Attribute name (e.g., "Color", "Size", "Material").
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Attribute value (e.g., "Red", "XL", "Cotton").
    /// </summary>
    public string Value { get; set; } = string.Empty;
    
    /// <summary>
    /// Display order for attribute selection UI.
    /// </summary>
    public int SortOrder { get; set; }
    
    // Navigation property
    public ProductVariant Variant { get; set; } = null!;
}
