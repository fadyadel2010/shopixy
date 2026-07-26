namespace PointOfSale.Core.Entities;

/// <summary>
/// Dynamic attribute definition for a CommerceHub category.
/// Drives product attribute forms, frontend filters, and Elasticsearch indexing.
/// 
/// Examples:
/// - TV category: Brand (select), Size (number), Resolution (select)
/// - Mobile category: Brand (select), Storage (select), RAM (select)
/// 
/// ES Strategy:
/// - IsFilterable = true  → appears in storefront filter sidebar (keyword/range)
/// - IsSearchable = true  → indexed for full-text search relevance
/// </summary>
public class CommerceHubCategoryAttribute
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// The category this attribute belongs to.
    /// </summary>
    public Guid CategoryId { get; set; }
    
    /// <summary>
    /// Attribute name (English), e.g. "Brand", "Size", "Storage"
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Attribute name (Arabic)
    /// </summary>
    public string? NameAr { get; set; }
    
    /// <summary>
    /// Input type: "select", "number", "text"
    /// Determines how the UI renders the field and ES field type:
    /// - select → keyword (exact match / aggregation)
    /// - number → double (range queries)
    /// - text   → keyword (exact match)
    /// </summary>
    public string Type { get; set; } = "text";
    
    /// <summary>
    /// Whether this attribute must be filled before publishing.
    /// </summary>
    public bool IsRequired { get; set; }
    
    /// <summary>
    /// When true, this attribute appears in the storefront filter sidebar.
    /// Drives ES aggregations for faceted search.
    /// Default: true (backward compatible).
    /// </summary>
    public bool IsFilterable { get; set; } = true;
    
    /// <summary>
    /// When true, this attribute value contributes to search relevance scoring.
    /// Default: true (backward compatible).
    /// </summary>
    public bool IsSearchable { get; set; } = true;
    
    /// <summary>
    /// JSON array of options for "select" type.
    /// Example: ["Samsung","LG","Sony","TCL"]
    /// Null for "number" and "text" types.
    /// </summary>
    public string? OptionsJson { get; set; }
    
    /// <summary>
    /// Display ordering within the attribute form.
    /// </summary>
    public int SortOrder { get; set; }
    
    // Navigation
    public CommerceHubCategory Category { get; set; } = null!;
}
