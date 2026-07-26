using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Core.DTOs.Affiliate;

/// <summary>
/// AF-30: Enriched policy response with category details for event snapshot.
/// Prevents SQL reads in controller layer during event publishing.
/// </summary>
public class AffiliatePolicyWithCategoryDetails
{
    public AffiliateProductPolicy Policy { get; set; } = null!;
    public CategoryDetails? CategoryDetails { get; set; }
}

/// <summary>
/// AF-30: Category details snapshot for event publishing.
/// Contains denormalized category data to avoid SQL joins in projection layer.
/// </summary>
public class CategoryDetails
{
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public string CategorySlug { get; set; } = "";
    public string? ParentCategoryName { get; set; }
    public string? ParentCategorySlug { get; set; }
}
