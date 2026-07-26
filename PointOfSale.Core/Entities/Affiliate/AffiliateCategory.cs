namespace PointOfSale.Core.Entities.Affiliate;

/// <summary>
/// AF-30: Global Marketplace Taxonomy Layer.
///
/// ARCHITECTURE:
/// - Global, normalized, curated taxonomy (NOT per-tenant)
/// - Hierarchical: Main Category (Level 1) → Subcategory (Level 2)
/// - Max depth = 2 (strictly enforced)
/// - Isolated from POS store categories
/// - No soft deletes (IsActive toggle only)
///
/// INVARIANTS:
/// - Slug globally unique
/// - (ParentId + Name) unique
/// - Level 1 = Main Category (ParentId = null)
/// - Level 2 = Subcategory (ParentId != null)
/// - No orphaned subcategories (FK enforced)
/// - Max depth = 2 (no Level 3+)
///
/// USAGE:
/// - AffiliateProductPolicy.AffiliateCategoryId → FK to this table
/// - Projected to AffiliateProductIntelligenceDocument for read-side
/// - Enables category-based discovery, SEO, analytics
/// </summary>
public class AffiliateCategory : BaseEntity
{
    /// <summary>
    /// Category name (e.g., "Electronics", "Mobile Phones").
    /// </summary>
    public string Name { get; private set; } = "";

    /// <summary>
    /// URL-safe slug (e.g., "electronics", "mobile-phones").
    /// Globally unique.
    /// </summary>
    public string Slug { get; private set; } = "";

    /// <summary>
    /// Parent category ID (null for main categories, non-null for subcategories).
    /// </summary>
    public Guid? ParentId { get; private set; }

    /// <summary>
    /// Hierarchy level: 1 = Main Category, 2 = Subcategory.
    /// Max depth = 2 (strictly enforced).
    /// </summary>
    public int Level { get; private set; }

    /// <summary>
    /// Active status. Inactive categories are hidden from discovery.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Display order for sorting within same level.
    /// </summary>
    public int DisplayOrder { get; private set; }

    private AffiliateCategory() { }

    /// <summary>
    /// Create a main category (Level 1).
    /// </summary>
    public static AffiliateCategory CreateMainCategory(string name, string slug, int displayOrder = 0)
    {
        ValidateName(name);
        ValidateSlug(slug);

        return new AffiliateCategory
        {
            Name = name.Trim(),
            Slug = slug.ToLowerInvariant().Trim(),
            ParentId = null,
            Level = 1,
            IsActive = true,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Create a subcategory (Level 2).
    /// </summary>
    public static AffiliateCategory CreateSubcategory(
        string name, string slug, Guid parentId, int displayOrder = 0)
    {
        if (parentId == Guid.Empty)
            throw new ArgumentException("ParentId required for subcategory", nameof(parentId));

        ValidateName(name);
        ValidateSlug(slug);

        return new AffiliateCategory
        {
            Name = name.Trim(),
            Slug = slug.ToLowerInvariant().Trim(),
            ParentId = parentId,
            Level = 2,
            IsActive = true,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void UpdateName(string name)
    {
        ValidateName(name);
        Name = name.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateSlug(string slug)
    {
        ValidateSlug(slug);
        Slug = slug.ToLowerInvariant().Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDisplayOrder(int displayOrder)
    {
        DisplayOrder = displayOrder;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Category name is required", nameof(name));

        if (name.Length > 100)
            throw new ArgumentException("Category name cannot exceed 100 characters", nameof(name));
    }

    private static void ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Category slug is required", nameof(slug));

        if (slug.Length > 100)
            throw new ArgumentException("Category slug cannot exceed 100 characters", nameof(slug));

        // Slug must be URL-safe: lowercase alphanumeric + hyphens only
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9\-]+$"))
            throw new ArgumentException(
                "Category slug must contain only lowercase letters, numbers, and hyphens", nameof(slug));
    }

    // AF-30A FIX D-1: EF Core setter methods REMOVED.
    // EF Core materializes entities via reflection on `private set` properties.
    // Public setter methods are unnecessary and bypass domain validation
    // (e.g., SetLevel(3) would bypass the Level ∈ {1,2} invariant).
    // DB CHECK constraints provide a backstop, but defense-in-depth
    // requires no bypass path in the domain model.
}
