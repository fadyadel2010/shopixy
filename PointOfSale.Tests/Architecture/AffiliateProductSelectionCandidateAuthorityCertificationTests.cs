namespace PointOfSale.Tests.Architecture;

public sealed class AffiliateProductSelectionCandidateAuthorityCertificationTests
{
    [Fact]
    public void AffiliateProductSelection_MustBeAdmittedByCurrentBackendEligibilityBeforePersistenceOrPresentation()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs");

        Assert.Contains("Selection Admission: current backend SQL eligibility is required", controller, StringComparison.Ordinal);
        Assert.Contains("IsProductCurrentlyAffiliateSelectableAsync(product, ct)", controller, StringComparison.Ordinal);
        Assert.Contains("product.PublishStatus != PublishStatus.Published", controller, StringComparison.Ordinal);
        Assert.Contains("!product.IsOnlineVisible", controller, StringComparison.Ordinal);
        Assert.Contains("policyWithActiveCategory", controller, StringComparison.Ordinal);
        Assert.Contains("item.policy.IsEnabled", controller, StringComparison.Ordinal);
        Assert.Contains("item.category.IsActive", controller, StringComparison.Ordinal);
        Assert.Contains("HasPositiveAffiliatePriceAsync(product, ct)", controller, StringComparison.Ordinal);
        Assert.Contains("HasAvailableAffiliateInventoryAsync(product, ct)", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("var policyEnabled = await _dbContext.AffiliateProductPolicies", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateProductSelection_ReadsMustFilterStaleOrIneligibleRelationships()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs");

        Assert.Contains("MapToResponseAsync(link, requireCurrentSelectionAdmission: true, ct)", controller, StringComparison.Ordinal);
        Assert.Contains("if (admitted != null)", controller, StringComparison.Ordinal);
        Assert.Contains("if (response == null)", controller, StringComparison.Ordinal);
        Assert.Contains("(requireCurrentSelectionAdmission && !await IsProductCurrentlyAffiliateSelectableAsync(product, ct))", controller, StringComparison.Ordinal);
        Assert.Contains("return null;", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateProductSelection_DuplicateRelationshipsMustRemainDatabaseImpossible()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliatePromotionLink.cs");
        var configuration = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliatePromotionLinkConfiguration.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs");

        Assert.Contains("One PromotionLink per (AffiliateUserId, StoreId, ProductId)", entity, StringComparison.Ordinal);
        Assert.Contains("new { e.AffiliateUserId, e.TenantId, e.ProductId }", configuration, StringComparison.Ordinal);
        Assert.Contains(".IsUnique()", configuration, StringComparison.Ordinal);
        Assert.Contains("UQ_AffiliatePromotionLinks_User_Store_Product", configuration, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(l => l.AffiliateUserId == affiliateUserId", controller, StringComparison.Ordinal);
        Assert.Contains("existingLink.Activate()", controller, StringComparison.Ordinal);
    }
}
