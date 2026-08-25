using PointOfSale.API.Controllers;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;

namespace PointOfSale.Tests.Affiliate;

public sealed class AffiliateAuthorityFirstPagingTests
{
    [Fact]
    public void SparseAdmission_PagesTheExactAdmittedSet_AndKeepsLaterItemsReachable()
    {
        var tenantId = Guid.NewGuid();
        var documents = new[]
        {
            Document(tenantId, commission: 0.40m, price: 40m),
            Document(tenantId, commission: 0.30m, price: 30m),
            Document(tenantId, commission: 0.20m, price: 20m)
        };

        var admitted = AffiliatePortalController.FilterAffiliateDocuments(
                documents,
                search: null,
                minCommissionRate: null,
                storeTenantId: null,
                categoryProductKeys: null)
            .ToList();
        var firstPage = AffiliatePortalController.OrderAffiliateDocuments(admitted, "commission_desc")
            .Skip(0)
            .Take(2)
            .ToList();
        var lastPage = AffiliatePortalController.OrderAffiliateDocuments(admitted, "commission_desc")
            .Skip(2)
            .Take(2)
            .ToList();

        Assert.Equal(3, admitted.Count);
        Assert.Equal(2, firstPage.Count);
        Assert.Single(lastPage);
        Assert.Equal(0.20m, lastPage[0].CommissionRate);
    }

    [Fact]
    public void MissingOrRejectedProjection_DoesNotInflateTotalsOrConsumePageSlots()
    {
        var tenantId = Guid.NewGuid();
        var documents = new[]
        {
            Document(tenantId, commission: 0.30m, price: 30m),
            Document(tenantId, commission: 0.25m, price: 0m),
            Document(tenantId, commission: 0.20m, price: 20m, isOutOfStock: true),
            Document(tenantId, commission: 0.10m, price: 10m)
        };

        var admitted = AffiliatePortalController.FilterAffiliateDocuments(
                documents,
                search: null,
                minCommissionRate: null,
                storeTenantId: null,
                categoryProductKeys: null)
            .ToList();
        var page = AffiliatePortalController.OrderAffiliateDocuments(admitted, "commission_desc")
            .Take(2)
            .ToList();

        Assert.Equal(2, admitted.Count);
        Assert.Equal(2, page.Count);
        Assert.DoesNotContain(page, item => item.BasePrice <= 0m || item.IsOutOfStock);
    }

    [Fact]
    public void FiltersAndSort_AreAppliedBeforeTheFinalPageBoundary()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var categoryProductId = Guid.NewGuid();
        var expected = Document(tenantA, commission: 0.25m, price: 15m, name: "Current Authority Name", productId: categoryProductId);
        var documents = new[]
        {
            expected,
            Document(tenantA, commission: 0.10m, price: 5m, name: "Current Authority Name"),
            Document(tenantB, commission: 0.40m, price: 40m, name: "Current Authority Name"),
            Document(tenantA, commission: 0.40m, price: 40m, name: "Different")
        };

        var filtered = AffiliatePortalController.FilterAffiliateDocuments(
                documents,
                search: "authority",
                minCommissionRate: 0.20m,
                storeTenantId: tenantA,
                categoryProductKeys: new HashSet<AffiliatePortalController.AffiliateAuthorityKey>
                {
                    new(tenantA, categoryProductId)
                })
            .ToList();
        var page = AffiliatePortalController.OrderAffiliateDocuments(filtered, "price_asc")
            .Take(1)
            .ToList();

        Assert.Single(filtered);
        Assert.Same(expected, page[0]);
    }

    [Fact]
    public void EqualSortValues_HaveDeterministicTenantAndProductTieBreakers()
    {
        var tenantA = Guid.Parse("10000000-0000-0000-0000-000000000000");
        var tenantB = Guid.Parse("20000000-0000-0000-0000-000000000000");
        var productA = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var productB = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var documents = new[]
        {
            Document(tenantB, 0.2m, 10m, productId: productA),
            Document(tenantA, 0.2m, 10m, productId: productB),
            Document(tenantA, 0.2m, 10m, productId: productA)
        };

        var ordered = AffiliatePortalController.OrderAffiliateDocuments(documents, "price_asc").ToList();

        Assert.Equal(new[] { productA, productB, productA }, ordered.Select(item => item.ProductId));
        Assert.Equal(new[] { tenantA, tenantA, tenantB }, ordered.Select(item => item.TenantId));
    }

    private static AffiliateProductIntelligenceDocument Document(
        Guid tenantId,
        decimal commission,
        decimal price,
        string name = "Product",
        bool isOutOfStock = false,
        Guid? productId = null) => new()
        {
            TenantId = tenantId,
            ProductId = productId ?? Guid.NewGuid(),
            ProductName = name,
            CommissionRate = commission,
            BasePrice = price,
            IsOutOfStock = isOutOfStock,
            LastUpdatedUtc = DateTime.UtcNow
        };
}
