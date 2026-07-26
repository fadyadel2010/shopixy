namespace PointOfSale.Tests.Architecture;

public sealed class MarketplacePricingAuthorityCertificationTests
{
    [Fact]
    public void FinalMarketplacePrice_MustUseCentralMerchantOverrideResolver()
    {
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var cart = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");

        Assert.Contains("CommerceHubPriceOverrideRuntimePolicy.IsEnabledForTenant", handlers, StringComparison.Ordinal);
        Assert.Contains("ICommerceHubPriceAuthority", handlers, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync", cart, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceAuthority", overlay, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Pricing", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplacePriceOverrides_MustStayGuardedAcrossSqlAndProjections()
    {
        var entity = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubListing.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var config = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubListingConfiguration.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var admin = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");

        Assert.Contains("ValidateCommerceHubPriceOverride", entity, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPrice = commerceHubPrice", entity, StringComparison.Ordinal);
        Assert.Contains("GuardCommerceHubPriceAuthority", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubListings_CommerceHubPrice_Positive", config, StringComparison.Ordinal);
        Assert.Contains("priceResolution.OverridePrice", projection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPrice = l.CommerceHubPrice", admin, StringComparison.Ordinal);
    }

    [Fact]
    public void VariantPricingAndReplay_MustConvergeThroughSqlAuthorityAndCompletedFences()
    {
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var variantEvents = ReadRepoFile("PointOfSale.Core/Events/ProductVariantEvents.cs");
        var testAuth = ReadRepoFile("PointOfSale.API/Controllers/TestAuthController.cs");

        Assert.Contains("IConsumer<VariantPriceUpdatedEvent>", projection, StringComparison.Ordinal);
        Assert.Contains("LoadSqlVariantPriceAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage(tenantId)", projection, StringComparison.Ordinal);
        Assert.Contains("ProductEventVersionHelper.ToProductEventVersion", projection, StringComparison.Ordinal);
        Assert.Contains("UpdatePriceProjectionIfFreshAsync", projection, StringComparison.Ordinal);
        Assert.Contains("LastPriceEventVersion", projection, StringComparison.Ordinal);
        Assert.Contains("VariantPriceConsumerName,\n                    eventType,\n                    message.VariantId", projection, StringComparison.Ordinal);
        Assert.Contains("VariantPriceUpdatedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent", variantEvents, StringComparison.Ordinal);
        Assert.Contains("PRICE_CON_04_Out_Of_Order_Marketplace_Price_Propagation_Rejected_By_Version_Aware_Downstream_Consumers", testAuth, StringComparison.Ordinal);
    }

    [Fact]
    public void Promotions_MustNotBecomeCommerceHubNumericPriceAuthority()
    {
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var composer = ReadRepoFile("PointOfSale.Core/Pricing/StorefrontPromotionPriceComposer.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var cart = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");

        Assert.Contains("Storefront-only promotion price composition", composer, StringComparison.Ordinal);
        Assert.Contains("Promotions are not global pricing authority and must not be used by affiliate or CommerceHub pricing.", composer, StringComparison.Ordinal);
        Assert.Contains("Storefront promotions are storefront-only overlays. CommerceHub", projection, StringComparison.Ordinal);
        Assert.Contains("listing.Price = basePrice;", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("StorefrontPromotionPriceComposer.Compose", projection, StringComparison.Ordinal);
        Assert.Contains("ValidatePromoForCheckoutAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("ValidatePromoAsync", cart, StringComparison.Ordinal);
        Assert.Contains("BaseCommissionAmount", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchDiscoveryAndAffiliatePrices_MustStayDerivative()
    {
        var queryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var readRepository = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/CommerceHubReadRepository.cs");
        var searchReadService = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchReadService.cs");
        var affiliatePolicy = ReadRepoFile("PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductPolicyProjectionConsumer.cs");
        var storefrontAuthority = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Documents/ProductStorefrontVisibilityAuthority.cs");

        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchIntentType.Price", readRepository, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchIntentType.Price", searchReadService, StringComparison.Ordinal);
        Assert.Contains("TryResolveAffiliatePublicPricing", affiliatePolicy, StringComparison.Ordinal);
        Assert.Contains("TryResolveAffiliatePublicPricing", storefrontAuthority, StringComparison.Ordinal);
        Assert.Contains("EffectivePrice", storefrontAuthority, StringComparison.Ordinal);
    }

    [Fact]
    public void PricingAuthorityCertificationDocumentation_MustRecordPassTwelveFour()
    {
        var certification = ReadRepoFile("docs/architecture/marketplace-pricing-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 12.4",
            "1. Pricing Inventory Findings",
            "2. Final Price Authority Findings",
            "3. Marketplace Override Findings",
            "4. Variant Pricing Findings",
            "5. Promotion Findings",
            "6. Merchant Governance Findings",
            "7. Search And Discovery Pricing Findings",
            "8. Replay And Recovery Findings",
            "9. Auditability Findings",
            "10. Chaos Findings",
            "11. Runtime Hardening Performed",
            "12. Monitoring Improvements",
            "13. Governance Updates",
            "14. Honest Remaining Debt"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }

        Assert.Contains("PASS 12.4 Marketplace Pricing Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 12.4 Marketplace Pricing Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 12.4", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 12.4 Marketplace Pricing Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 12.4 Marketplace Pricing Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
