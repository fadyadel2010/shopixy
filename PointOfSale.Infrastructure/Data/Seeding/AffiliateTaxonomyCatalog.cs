using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Infrastructure.Data.Seeding;

/// <summary>
/// AF-30B: Immutable, deterministic definition of the source-defined Affiliate
/// taxonomy baseline (canonical provenance: DB/sql/add_affiliate_category_system.sql).
///
/// This catalog is the single startup-seed authority for the global
/// <see cref="AffiliateCategory"/> platform master data. It is intentionally:
/// - Fixed identity: every row uses the exact GUID from the source SQL (no
///   Guid.NewGuid() anywhere) so product policies and projections can bind to
///   stable category identity across environments.
/// - Ordered: roots are defined before their children so a seed pass can always
///   satisfy the self-referencing FK in one forward sweep.
/// - Read-only: entries are immutable records; no mutable global entity instances
///   are exposed (entities are materialized fresh by the seeder at insert time).
///
/// The catalog is DISTINCT from CommerceHubCategory and its 10000000-... seed.
/// </summary>
public static class AffiliateTaxonomyCatalog
{
    private static readonly Guid ElectronicsId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FashionId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid HomeAndLivingId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BeautyAndHealthId = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SportsAndOutdoorsId = new("55555555-5555-5555-5555-555555555555");

    /// <summary>
    /// Full baseline taxonomy, parents strictly before children.
    /// Values mirror DB/sql/add_affiliate_category_system.sql exactly:
    /// 8 main categories (Level 1) and the source-defined 25 subcategories
    /// (Level 2). Roots without children in the source SQL (Books &amp; Media,
    /// Food &amp; Beverages, Toys &amp; Games) intentionally have none here too.
    /// </summary>
    private static readonly IReadOnlyList<AffiliateTaxonomyEntry> BaselineEntries =
        Array.AsReadOnly<AffiliateTaxonomyEntry>(
    [
        // ── Main Categories (Level 1) ──────────────────────────────────────
        new(new("11111111-1111-1111-1111-111111111111"), "Electronics", "electronics", null, 1, 1),
        new(new("22222222-2222-2222-2222-222222222222"), "Fashion", "fashion", null, 1, 2),
        new(new("33333333-3333-3333-3333-333333333333"), "Home & Living", "home-living", null, 1, 3),
        new(new("44444444-4444-4444-4444-444444444444"), "Beauty & Health", "beauty-health", null, 1, 4),
        new(new("55555555-5555-5555-5555-555555555555"), "Sports & Outdoors", "sports-outdoors", null, 1, 5),
        new(new("66666666-6666-6666-6666-666666666666"), "Books & Media", "books-media", null, 1, 6),
        new(new("77777777-7777-7777-7777-777777777777"), "Food & Beverages", "food-beverages", null, 1, 7),
        new(new("88888888-8888-8888-8888-888888888888"), "Toys & Games", "toys-games", null, 1, 8),

        // ── Subcategories: Electronics ─────────────────────────────────────
        new(new("11111111-1111-1111-1111-111111111112"), "Mobile Phones", "mobile-phones", ElectronicsId, 2, 1),
        new(new("11111111-1111-1111-1111-111111111113"), "Laptops & Computers", "laptops-computers", ElectronicsId, 2, 2),
        new(new("11111111-1111-1111-1111-111111111114"), "Tablets", "tablets", ElectronicsId, 2, 3),
        new(new("11111111-1111-1111-1111-111111111115"), "Audio & Headphones", "audio-headphones", ElectronicsId, 2, 4),
        new(new("11111111-1111-1111-1111-111111111116"), "Cameras", "cameras", ElectronicsId, 2, 5),
        new(new("11111111-1111-1111-1111-111111111117"), "Smart Home", "smart-home", ElectronicsId, 2, 6),

        // ── Subcategories: Fashion ─────────────────────────────────────────
        new(new("22222222-2222-2222-2222-222222222223"), "Men's Clothing", "mens-clothing", FashionId, 2, 1),
        new(new("22222222-2222-2222-2222-222222222224"), "Women's Clothing", "womens-clothing", FashionId, 2, 2),
        new(new("22222222-2222-2222-2222-222222222225"), "Shoes", "shoes", FashionId, 2, 3),
        new(new("22222222-2222-2222-2222-222222222226"), "Bags & Accessories", "bags-accessories", FashionId, 2, 4),
        new(new("22222222-2222-2222-2222-222222222227"), "Watches", "watches", FashionId, 2, 5),

        // ── Subcategories: Home & Living ───────────────────────────────────
        new(new("33333333-3333-3333-3333-333333333334"), "Furniture", "furniture", HomeAndLivingId, 2, 1),
        new(new("33333333-3333-3333-3333-333333333335"), "Kitchen & Dining", "kitchen-dining", HomeAndLivingId, 2, 2),
        new(new("33333333-3333-3333-3333-333333333336"), "Bedding", "bedding", HomeAndLivingId, 2, 3),
        new(new("33333333-3333-3333-3333-333333333337"), "Home Decor", "home-decor", HomeAndLivingId, 2, 4),
        new(new("33333333-3333-3333-3333-333333333338"), "Appliances", "appliances", HomeAndLivingId, 2, 5),

        // ── Subcategories: Beauty & Health ─────────────────────────────────
        new(new("44444444-4444-4444-4444-444444444445"), "Skincare", "skincare", BeautyAndHealthId, 2, 1),
        new(new("44444444-4444-4444-4444-444444444446"), "Makeup", "makeup", BeautyAndHealthId, 2, 2),
        new(new("44444444-4444-4444-4444-444444444447"), "Haircare", "haircare", BeautyAndHealthId, 2, 3),
        new(new("44444444-4444-4444-4444-444444444448"), "Fragrances", "fragrances", BeautyAndHealthId, 2, 4),
        new(new("44444444-4444-4444-4444-444444444449"), "Supplements", "supplements", BeautyAndHealthId, 2, 5),

        // ── Subcategories: Sports & Outdoors ───────────────────────────────
        new(new("55555555-5555-5555-5555-555555555556"), "Fitness Equipment", "fitness-equipment", SportsAndOutdoorsId, 2, 1),
        new(new("55555555-5555-5555-5555-555555555557"), "Sportswear", "sportswear", SportsAndOutdoorsId, 2, 2),
        new(new("55555555-5555-5555-5555-555555555558"), "Camping & Hiking", "camping-hiking", SportsAndOutdoorsId, 2, 3),
        new(new("55555555-5555-5555-5555-555555555559"), "Cycling", "cycling", SportsAndOutdoorsId, 2, 4)
    ]);

    public static IReadOnlyList<AffiliateTaxonomyEntry> Entries => BaselineEntries;

    public static IReadOnlyList<AffiliateTaxonomyEntry> Roots { get; } =
        Array.AsReadOnly(Entries.Where(entry => entry.Level == 1).ToArray());
}

/// <summary>
/// One immutable baseline taxonomy row definition (identity + placement only;
/// IsActive/IsDeleted/audit values are decided by the seeder at insert time).
/// </summary>
public sealed record AffiliateTaxonomyEntry(
    Guid Id,
    string Name,
    string Slug,
    Guid? ParentId,
    int Level,
    int DisplayOrder);
