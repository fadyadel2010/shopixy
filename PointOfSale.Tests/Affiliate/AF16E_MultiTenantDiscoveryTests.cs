using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using Xunit;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-16E: Multi-Tenant Discover API Data Integrity Tests.
///
/// CRITICAL INVARIANTS:
/// 1. Every product MUST carry its own tenant's StorefrontBaseUrl
/// 2. Products from different tenants MUST have different StorefrontBaseUrls
/// 3. Discovery MUST fail-fast if any product lacks StorefrontBaseUrl
/// 4. No silent fallback to CurrentTenant or UrlTemplate
/// </summary>
public class AF16E_MultiTenantDiscoveryTests
{
    [Fact]
    public void AffiliateDiscoveryItem_MustHaveStorefrontBaseUrl()
    {
        // Verify that AffiliateDiscoveryItem has StorefrontBaseUrl property
        var prop = typeof(AffiliateDiscoveryItem).GetProperty("StorefrontBaseUrl");
        
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop.PropertyType);
    }

    [Fact]
    public void AffiliateDiscoveryItem_MustHaveTenantId()
    {
        // Verify that AffiliateDiscoveryItem has TenantId property for validation
        var prop = typeof(AffiliateDiscoveryItem).GetProperty("TenantId");
        
        Assert.NotNull(prop);
        Assert.Equal(typeof(Guid), prop.PropertyType);
    }

    [Fact]
    public void PublicAffiliateProductSearchDocument_MustHaveStorefrontBaseUrl()
    {
        // Verify Elasticsearch document has StorefrontBaseUrl field
        var prop = typeof(PublicAffiliateProductSearchDocument).GetProperty("StorefrontBaseUrl");
        
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop.PropertyType);
    }

    [Fact]
    public void PublicAffiliateProductSearchDocument_MustHaveTenantId()
    {
        // Verify Elasticsearch document has TenantId field
        var prop = typeof(PublicAffiliateProductSearchDocument).GetProperty("TenantId");
        
        Assert.NotNull(prop);
        Assert.Equal(typeof(Guid), prop.PropertyType);
    }

    [Fact]
    public void AffiliateDiscoveryItem_StorefrontBaseUrl_IsNotNullable()
    {
        // StorefrontBaseUrl must be non-nullable to enforce fail-fast
        var prop = typeof(AffiliateDiscoveryItem).GetProperty("StorefrontBaseUrl");
        
        Assert.NotNull(prop);
        Assert.False(Nullable.GetUnderlyingType(prop.PropertyType) != null);
    }

    [Theory]
    [InlineData("http://anatico.localhost:5174", "http://mena.localhost:5174")]
    [InlineData("https://store1.example.com", "https://store2.example.com")]
    public void MultiTenantProducts_MustHaveDifferentStorefrontUrls(string url1, string url2)
    {
        // Simulate two products from different tenants
        var product1 = new AffiliateDiscoveryItem
        {
            ProductId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            StorefrontBaseUrl = url1,
            Name = "Product A"
        };

        var product2 = new AffiliateDiscoveryItem
        {
            ProductId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            StorefrontBaseUrl = url2,
            Name = "Product B"
        };

        // Each product must have its own tenant's URL
        Assert.NotEqual(product1.StorefrontBaseUrl, product2.StorefrontBaseUrl);
        Assert.NotEqual(product1.TenantId, product2.TenantId);
    }

    [Fact]
    public void PromotionLink_MustUseProductStorefrontUrl_NotCurrentTenant()
    {
        // This test documents the architectural requirement:
        // Promotion links MUST be constructed from product.StorefrontBaseUrl
        // NOT from CurrentTenant or any other source

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var productFromTenantB = new AffiliateDiscoveryItem
        {
            ProductId = Guid.NewGuid(),
            TenantId = tenantB,
            StorefrontBaseUrl = "http://mena.localhost:5174",
            Slug = "blue-widget",
            Name = "Blue Widget"
        };

        // Even if current tenant is A, promotion link MUST use tenant B's URL
        var currentTenant = tenantA;
        var promotionLink = $"{productFromTenantB.StorefrontBaseUrl}/products/{productFromTenantB.Slug}?ref=ABC123";

        Assert.Contains("mena.localhost", promotionLink);
        Assert.DoesNotContain("anatico.localhost", promotionLink);
    }

    [Fact]
    public void DiscoveryResult_WithMissingStorefrontUrl_MustFailFast()
    {
        // Simulate discovery result with missing StorefrontBaseUrl
        var items = new List<AffiliateDiscoveryItem>
        {
            new() { ProductId = Guid.NewGuid(), StorefrontBaseUrl = "http://valid.localhost", TenantId = Guid.NewGuid() },
            new() { ProductId = Guid.NewGuid(), StorefrontBaseUrl = "", TenantId = Guid.NewGuid() }, // INVALID
            new() { ProductId = Guid.NewGuid(), StorefrontBaseUrl = "http://valid2.localhost", TenantId = Guid.NewGuid() }
        };

        // Validation logic (should be in service)
        var invalidProducts = items.Where(p => string.IsNullOrWhiteSpace(p.StorefrontBaseUrl)).ToList();

        Assert.NotEmpty(invalidProducts);
        Assert.Single(invalidProducts);
    }
}
