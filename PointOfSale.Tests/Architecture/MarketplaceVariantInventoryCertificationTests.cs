namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceVariantInventoryCertificationTests
{
    [Fact]
    public void CommerceHubVariantAvailability_MustUseSqlVariantAndInventoryLineage()
    {
        var cart = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var stockRepository = ReadRepoFile("PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs");
        var lineage = ReadRepoFile("PointOfSale.Infrastructure/Reliability/ProductVariantInventoryLineageAuthority.cs");

        Assert.Contains("WherePubliclyVisibleLineage()", cart, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage()", checkout, StringComparison.Ordinal);
        Assert.Contains("GetAvailableQuantityAsync", cart, StringComparison.Ordinal);
        Assert.Contains("GetAvailableQuantityAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("WhereInventoryReservableLineage", stockRepository, StringComparison.Ordinal);
        Assert.Contains("WhereCurrentInventoryStockOwner", stockRepository, StringComparison.Ordinal);
        Assert.Contains("FilterCurrentConsumableReservationsAsync", lineage, StringComparison.Ordinal);
        Assert.Contains("FilterCurrentConsumableReservationsAcrossTenantsAsync", lineage, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontAffiliateAdmission_MustNotUseLegacyWarehouseStockAuthority()
    {
        var affiliateReferral = ReadRepoFile("PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs");
        var affiliateLinks = ReadRepoFile("PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs");
        var adminAffiliate = ReadRepoFile("PointOfSale.API/Controllers/AdminProductAffiliateController.cs");
        var cart = ReadRepoFile("PointOfSale.API/Services/Cart/CartService.cs");
        var affiliateService = ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        var combined = string.Join(Environment.NewLine, affiliateReferral, affiliateLinks, adminAffiliate, cart, affiliateService);

        Assert.DoesNotContain(".WarehouseStocks", combined, StringComparison.Ordinal);
        Assert.Contains("ProductVariantStocks", combined, StringComparison.Ordinal);
        Assert.Contains("stock.Quantity - stock.ReservedQuantity > 0m", combined, StringComparison.Ordinal);
        Assert.Contains("SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutStockRepositoryReservations_MustAppendMovementLedgerRows()
    {
        var stockRepository = ReadRepoFile("PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs");

        Assert.Contains("_dbContext.StockMovements.Add", stockRepository, StringComparison.Ordinal);
        Assert.Contains("InventoryEventType.StockReserved", stockRepository, StringComparison.Ordinal);
        Assert.Contains("InventoryEventType.StockReleased", stockRepository, StringComparison.Ordinal);
        Assert.Contains("InventoryEventType.StockDecreased", stockRepository, StringComparison.Ordinal);
        Assert.Contains("ReservationId = reservation.Id", stockRepository, StringComparison.Ordinal);
        Assert.Contains("ReservedQuantityChange = reservedQuantityChange", stockRepository, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCheckoutReservations_MustFailClosedOnStaleStockAuthority()
    {
        var inventory = ReadRepoFile("PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs");
        var paymentRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentRecoveryAuthorityService.cs");

        Assert.Contains("EnsureReservationRequestMatchesSqlCatalogAuthorityAsync", inventory, StringComparison.Ordinal);
        Assert.Contains("FilterCurrentInventoryStockOwnersAsync", inventory, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_RESERVATION_STOCK_AUTHORITY_LOST", inventory, StringComparison.Ordinal);
        Assert.Contains("FilterCurrentConsumableReservationsAcrossTenantsAsync", inventory, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_ORDER_STOCK_AUTHORITY_LOST", inventory, StringComparison.Ordinal);
        Assert.Contains("EnsureReservedStockRowsCoverMarketplaceReservationsAsync", inventory, StringComparison.Ordinal);
        Assert.Contains("WhereCurrentInventoryStockOwner", paymentRecovery, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchProjectionAndRecoveryInventory_MustStayDerivative()
    {
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var query = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");

        Assert.Contains("SqlAuthorityOverlayKind.Stock", overlay, StringComparison.Ordinal);
        Assert.Contains("WhereCurrentInventoryStockOwner", overlay, StringComparison.Ordinal);
        Assert.Contains("document.InStock", overlay, StringComparison.Ordinal);
        Assert.Contains("LoadSqlVariantStockAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("WhereCurrentInventoryStockOwner", projection, StringComparison.Ordinal);
        Assert.Contains("UpdateStockProjectionIfFreshAsync", projection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay", query, StringComparison.Ordinal);
    }

    [Fact]
    public void VariantInventoryCertificationDocumentation_MustRecordPassTwelveFive()
    {
        var certification = ReadRepoFile("docs/architecture/marketplace-variant-inventory-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 12.5",
            "1. Variant Inventory Findings",
            "2. Variant Authority Findings",
            "3. Inventory Authority Findings",
            "4. Marketplace Availability Findings",
            "5. Search And Discovery Findings",
            "6. Cart And Checkout Findings",
            "7. Replay And Recovery Findings",
            "8. Merchant Variant Governance Findings",
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

        Assert.Contains("PASS 12.5 Variant & Inventory Marketplace Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 12.5 Variant & Inventory Marketplace Certification Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 12.5", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 12.5 Variant & Inventory Marketplace Certification", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 12.5 Variant & Inventory Marketplace Certification Freeze", codexRules, StringComparison.Ordinal);
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
