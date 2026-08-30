using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data.Seeding;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-30B: Deterministic-identity guarantees of the affiliate taxonomy baseline
/// catalog (provenance: DB/sql/add_affiliate_category_system.sql).
/// </summary>
public sealed class AffiliateTaxonomyCatalogTests
{
    private static readonly (string Id, string Name, string Slug, string? ParentId, int Level, int DisplayOrder)[] SourceBaseline =
    {
        // Main Categories (Level 1)
        ("11111111-1111-1111-1111-111111111111", "Electronics", "electronics", null, 1, 1),
        ("22222222-2222-2222-2222-222222222222", "Fashion", "fashion", null, 1, 2),
        ("33333333-3333-3333-3333-333333333333", "Home & Living", "home-living", null, 1, 3),
        ("44444444-4444-4444-4444-444444444444", "Beauty & Health", "beauty-health", null, 1, 4),
        ("55555555-5555-5555-5555-555555555555", "Sports & Outdoors", "sports-outdoors", null, 1, 5),
        ("66666666-6666-6666-6666-666666666666", "Books & Media", "books-media", null, 1, 6),
        ("77777777-7777-7777-7777-777777777777", "Food & Beverages", "food-beverages", null, 1, 7),
        ("88888888-8888-8888-8888-888888888888", "Toys & Games", "toys-games", null, 1, 8),
        // Electronics
        ("11111111-1111-1111-1111-111111111112", "Mobile Phones", "mobile-phones", "11111111-1111-1111-1111-111111111111", 2, 1),
        ("11111111-1111-1111-1111-111111111113", "Laptops & Computers", "laptops-computers", "11111111-1111-1111-1111-111111111111", 2, 2),
        ("11111111-1111-1111-1111-111111111114", "Tablets", "tablets", "11111111-1111-1111-1111-111111111111", 2, 3),
        ("11111111-1111-1111-1111-111111111115", "Audio & Headphones", "audio-headphones", "11111111-1111-1111-1111-111111111111", 2, 4),
        ("11111111-1111-1111-1111-111111111116", "Cameras", "cameras", "11111111-1111-1111-1111-111111111111", 2, 5),
        ("11111111-1111-1111-1111-111111111117", "Smart Home", "smart-home", "11111111-1111-1111-1111-111111111111", 2, 6),
        // Fashion
        ("22222222-2222-2222-2222-222222222223", "Men's Clothing", "mens-clothing", "22222222-2222-2222-2222-222222222222", 2, 1),
        ("22222222-2222-2222-2222-222222222224", "Women's Clothing", "womens-clothing", "22222222-2222-2222-2222-222222222222", 2, 2),
        ("22222222-2222-2222-2222-222222222225", "Shoes", "shoes", "22222222-2222-2222-2222-222222222222", 2, 3),
        ("22222222-2222-2222-2222-222222222226", "Bags & Accessories", "bags-accessories", "22222222-2222-2222-2222-222222222222", 2, 4),
        ("22222222-2222-2222-2222-222222222227", "Watches", "watches", "22222222-2222-2222-2222-222222222222", 2, 5),
        // Home & Living
        ("33333333-3333-3333-3333-333333333334", "Furniture", "furniture", "33333333-3333-3333-3333-333333333333", 2, 1),
        ("33333333-3333-3333-3333-333333333335", "Kitchen & Dining", "kitchen-dining", "33333333-3333-3333-3333-333333333333", 2, 2),
        ("33333333-3333-3333-3333-333333333336", "Bedding", "bedding", "33333333-3333-3333-3333-333333333333", 2, 3),
        ("33333333-3333-3333-3333-333333333337", "Home Decor", "home-decor", "33333333-3333-3333-3333-333333333333", 2, 4),
        ("33333333-3333-3333-3333-333333333338", "Appliances", "appliances", "33333333-3333-3333-3333-333333333333", 2, 5),
        // Beauty & Health
        ("44444444-4444-4444-4444-444444444445", "Skincare", "skincare", "44444444-4444-4444-4444-444444444444", 2, 1),
        ("44444444-4444-4444-4444-444444444446", "Makeup", "makeup", "44444444-4444-4444-4444-444444444444", 2, 2),
        ("44444444-4444-4444-4444-444444444447", "Haircare", "haircare", "44444444-4444-4444-4444-444444444444", 2, 3),
        ("44444444-4444-4444-4444-444444444448", "Fragrances", "fragrances", "44444444-4444-4444-4444-444444444444", 2, 4),
        ("44444444-4444-4444-4444-444444444449", "Supplements", "supplements", "44444444-4444-4444-4444-444444444444", 2, 5),
        // Sports & Outdoors
        ("55555555-5555-5555-5555-555555555556", "Fitness Equipment", "fitness-equipment", "55555555-5555-5555-5555-555555555555", 2, 1),
        ("55555555-5555-5555-5555-555555555557", "Sportswear", "sportswear", "55555555-5555-5555-5555-555555555555", 2, 2),
        ("55555555-5555-5555-5555-555555555558", "Camping & Hiking", "camping-hiking", "55555555-5555-5555-5555-555555555555", 2, 3),
        ("55555555-5555-5555-5555-555555555559", "Cycling", "cycling", "55555555-5555-5555-5555-555555555555", 2, 4)
    };

    [Fact]
    public void Catalog_MatchesSourceDefinedBaselineExactly()
    {
        var entries = AffiliateTaxonomyCatalog.Entries;

        Assert.Equal(SourceBaseline.Length, entries.Count);

        for (var index = 0; index < SourceBaseline.Length; index++)
        {
            var expected = SourceBaseline[index];
            var actual = entries[index];

            Assert.Equal(Guid.Parse(expected.Id), actual.Id);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Slug, actual.Slug);
            Assert.Equal(
                expected.ParentId is null ? null : Guid.Parse(expected.ParentId),
                actual.ParentId);
            Assert.Equal(expected.Level, actual.Level);
            Assert.Equal(expected.DisplayOrder, actual.DisplayOrder);
        }
    }

    [Fact]
    public void Catalog_FixedIdsAreUniqueStableAndNeverEmpty()
    {
        var entries = AffiliateTaxonomyCatalog.Entries;

        Assert.All(entries, entry => Assert.NotEqual(Guid.Empty, entry.Id));
        Assert.Equal(entries.Count, entries.Select(entry => entry.Id).Distinct().Count());
        Assert.Equal(entries.Count, entries.Select(entry => entry.Slug).Distinct(StringComparer.Ordinal).Count());

        // Stable: repeated access returns the identical immutable instance.
        Assert.Same(entries, AffiliateTaxonomyCatalog.Entries);

        // IReadOnlyList alone does not guarantee runtime immutability: arrays and
        // List<T> can be cast back and changed. The catalog uses a read-only wrapper.
        var listView = Assert.IsAssignableFrom<IList<AffiliateTaxonomyEntry>>(entries);
        Assert.Throws<NotSupportedException>(() => listView[0] = entries[0] with { Name = "Mutated" });
    }

    [Fact]
    public void Catalog_HierarchyIsWellFormedAndDeterministicallyOrdered()
    {
        var entries = AffiliateTaxonomyCatalog.Entries;
        var seenIds = new HashSet<Guid>();
        var byId = entries.ToDictionary(entry => entry.Id);

        foreach (var entry in entries)
        {
            if (entry.ParentId is null)
            {
                Assert.Equal(1, entry.Level);
            }
            else
            {
                // Parents are defined before children (no orphans, no forward refs).
                Assert.True(
                    seenIds.Contains(entry.ParentId.Value),
                    $"Entry '{entry.Slug}' references parent {entry.ParentId} that is not defined before it.");
                Assert.Equal(2, entry.Level);
                Assert.Equal(1, byId[entry.ParentId.Value].Level);
            }

            seenIds.Add(entry.Id);
        }

        // No cycles possible beyond depth 2; every root/child pair validated above.

        // Deterministic sibling order: strictly increasing DisplayOrder per parent.
        foreach (var parentGroup in entries.Where(entry => entry.ParentId is not null).GroupBy(entry => entry.ParentId))
        {
            var siblingOrders = parentGroup.Select(entry => entry.DisplayOrder).ToArray();
            Assert.Equal(siblingOrders.OrderBy(order => order), siblingOrders);
            Assert.Equal(siblingOrders.Length, siblingOrders.Distinct().Count());
        }

        // Root display order is strictly increasing in definition order too.
        var rootOrders = AffiliateTaxonomyCatalog.Roots.Select(root => root.DisplayOrder).ToArray();
        Assert.Equal(rootOrders.OrderBy(order => order), rootOrders);
    }

    [Fact]
    public void Catalog_ValuesSatisfyAffiliateCategoryDomainInvariants()
    {
        foreach (var entry in AffiliateTaxonomyCatalog.Entries)
        {
            // Constructing through the entity factories proves the baseline values
            // pass name/slug validation and level/parent invariants.
            if (entry.Level == 1)
            {
                var category = AffiliateCategory.CreateMainCategory(entry.Name, entry.Slug, entry.DisplayOrder);
                Assert.Null(category.ParentId);
            }
            else
            {
                var category = AffiliateCategory.CreateSubcategory(entry.Name, entry.Slug, entry.ParentId!.Value, entry.DisplayOrder);
                Assert.Equal(entry.ParentId, category.ParentId);
            }
        }
    }

    [Fact]
    public void AffiliateCategory_RemainsGlobalPlatformMasterData_WithoutTenantOwnership()
    {
        Assert.False(typeof(ITenantOwned).IsAssignableFrom(typeof(AffiliateCategory)));
        Assert.Null(typeof(AffiliateCategory).GetProperty("TenantId"));
    }
}
