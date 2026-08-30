using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Tests.Affiliate;

public sealed class AffiliateProductEligibilityLifecycleCertificationTests
{
    [Fact]
    public void AffiliateService_Enablement_UsesBackendLifecycleEligibilityGate()
    {
        var source = ReadRepositoryFile("PointOfSale.Infrastructure", "Services", "Affiliate", "AffiliateService.cs");

        Assert.Contains("EnsureProductEligibleForAffiliateAsync(product, ct);", source);
        Assert.Contains("Deleted products cannot be affiliate eligible", source);
        Assert.Contains("Inactive products cannot be affiliate eligible", source);
        Assert.Contains("Only published products can be affiliate eligible", source);
        Assert.Contains("Hidden products cannot be affiliate eligible", source);
        Assert.Contains("positive storefront price", source);
        Assert.Contains("in stock or allow backorders", source);
        Assert.Contains("WhereCurrentSemanticOwner()", source);
    }

    [Fact]
    public void AdminSettings_ReadsEffectiveEligibility_NotRawPolicyOnly()
    {
        var source = ReadRepositoryFile("PointOfSale.API", "Controllers", "AdminProductAffiliateController.cs");

        Assert.Contains("IsProductEffectivelyAffiliateEligibleAsync", source);
        Assert.Contains("policy.IsEnabled &&", source);
        Assert.Contains("PublishStatus.Published", source);
        Assert.Contains("category.IsActive", source);
        Assert.Contains("ProductVariantSemanticLifecycleState.Current", source);
        Assert.Contains("stock.Quantity - stock.ReservedQuantity > 0m", source);
    }

    [Fact]
    public void AdminSettings_AlsoExposesPersistedParticipationWithoutInferringFromProductLifecycle()
    {
        var source = ReadRepositoryFile("PointOfSale.API", "Controllers", "AdminProductAffiliateController.cs");

        Assert.Contains("IsPolicyEnabled = policy.IsEnabled", source);
        Assert.Contains("Direct persisted AffiliateProductPolicy.IsEnabled state", source);
    }

    [Fact]
    public void PublicAffiliateProjection_RequiresActiveCategoryAuthority()
    {
        var projectionConsumer = ReadRepositoryFile("PointOfSale.Infrastructure", "Consumers", "Affiliate", "AffiliateProductPolicyProjectionConsumer.cs");
        var publicController = ReadRepositoryFile("PointOfSale.API", "Controllers", "PublicAffiliateProductsController.cs");

        Assert.Contains("AffiliateCategories", projectionConsumer);
        Assert.Contains("category.IsActive", projectionConsumer);
        Assert.Contains("AffiliateCategories", publicController);
        Assert.Contains("item.Category.IsActive", publicController);
    }

    [Fact]
    public void AffiliateDiscovery_ReadAdmission_RequiresActiveCategoryAuthority()
    {
        var discoveryService = ReadRepositoryFile("PointOfSale.Infrastructure", "Elasticsearch", "Services", "AffiliateProductDiscoveryService.cs");
        var affiliatePortalController = ReadRepositoryFile("PointOfSale.API", "Controllers", "AffiliatePortalController.cs");
        var affiliateDiscoveryController = ReadRepositoryFile("PointOfSale.API", "Controllers", "AffiliateDiscoveryController.cs");

        Assert.Contains("RecomposeDiscoveryItemsFromSqlAuthorityAsync", discoveryService);
        Assert.Contains("AffiliateCategories", discoveryService);
        Assert.Contains("item.Category.IsActive", discoveryService);
        Assert.Contains("!item.Category.IsDeleted", discoveryService);

        Assert.Contains("RecomposeAffiliateIntelligenceDocumentBySqlAuthorityAsync", affiliatePortalController);
        Assert.Contains("AffiliateCategories", affiliatePortalController);
        Assert.Contains("item.Category.IsActive", affiliatePortalController);
        Assert.Contains("!item.Category.IsDeleted", affiliatePortalController);

        Assert.Contains("AffiliateCategories", affiliateDiscoveryController);
        Assert.Contains("item.Category.IsActive", affiliateDiscoveryController);
        Assert.Contains("!item.Category.IsDeleted", affiliateDiscoveryController);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.01")]
    [InlineData("0.5001")]
    public void CommissionPolicy_InvalidCommission_CannotRemainEligible(string commissionRateValue)
    {
        var commissionRate = decimal.Parse(commissionRateValue, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentException>(() =>
            AffiliateProductPolicy.Create(Guid.NewGuid(), Guid.NewGuid(), commissionRate, categoryId: Guid.NewGuid()));
    }

    private static string ReadRepositoryFile(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(pathSegments).ToArray()));
    }
}
