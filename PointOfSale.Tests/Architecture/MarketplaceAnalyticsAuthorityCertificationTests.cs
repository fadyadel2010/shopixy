namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceAnalyticsAuthorityCertificationTests
{
    [Fact]
    public void SearchAnalyticsIngress_MustStampSqlVisibleTenantAuthority()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubController.cs");
        var background = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubSearchAnalyticsBackgroundService.cs");

        Assert.Contains("ResolveVisibleSearchAnalyticsAuthorityAsync", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", controller, StringComparison.Ordinal);
        Assert.Contains("visibleProductsByTenant", controller, StringComparison.Ordinal);
        Assert.Contains("tenantGroup.Key", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("request.TenantId));", controller, StringComparison.Ordinal);

        Assert.Contains("ResolveVisibleSearchAnalyticsAuthorityAsync", background, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", background, StringComparison.Ordinal);
        Assert.Contains("TenantId = authority.TenantId", background, StringComparison.Ordinal);
        Assert.Contains("ProductId = authority.ProductId", background, StringComparison.Ordinal);
        Assert.Contains("return null;", background, StringComparison.Ordinal);
    }

    [Fact]
    public void MerchantListingAnalytics_MustDeriveConversionsFromTenantOwnedSqlOrderWindow()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");

        Assert.Contains("[HttpGet(\"listings/analytics\")]", controller, StringComparison.Ordinal);
        Assert.Contains("days = Math.Clamp(days, 1, 90);", controller, StringComparison.Ordinal);
        Assert.Contains("item.SubOrder.StoreId == tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("!item.SubOrder.IsDeleted", controller, StringComparison.Ordinal);
        Assert.Contains("!item.SubOrder.MarketplaceOrder.IsDeleted", controller, StringComparison.Ordinal);
        Assert.Contains("item.SubOrder.MarketplaceOrder.CreatedAt >= since", controller, StringComparison.Ordinal);
        Assert.Contains("productIds.Contains(item.ProductId)", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceRevenueAnalytics_MustConsumeLedgerAndSqlBusinessTruthOnly()
    {
        var aggregateService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubDailyFinancialAggregateService.cs");
        var dashboardService = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminFinancialDashboardService.cs");
        var aggregateDashboard = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminFinancialDashboardService.Aggregates.cs");
        var vendorFinance = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs");

        Assert.Contains("CommerceHubFinancialTransactions.AsNoTracking()", aggregateService, StringComparison.Ordinal);
        Assert.Contains("join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking()", aggregateService, StringComparison.Ordinal);
        Assert.Contains("join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking()", aggregateService, StringComparison.Ordinal);
        Assert.Contains("transaction.ReferenceType == CommerceHubFinancialReferenceType.Order", aggregateService, StringComparison.Ordinal);
        Assert.Contains("transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund", aggregateService, StringComparison.Ordinal);
        Assert.Contains("refund.Status == CommerceHubRefundStatus.Completed", aggregateService, StringComparison.Ordinal);
        Assert.Contains("row.NetRevenue = RoundCurrency(row.GrossCommission - row.PromoCost - row.RefundImpactOnRevenue)", aggregateService, StringComparison.Ordinal);

        Assert.Contains("DataSource = \"LIVE_LEDGER\"", dashboardService, StringComparison.Ordinal);
        Assert.Contains("TryGetAggregateWindowAsync", dashboardService, StringComparison.Ordinal);
        Assert.Contains("!aggregateWindow.IsFresh", aggregateDashboard, StringComparison.Ordinal);
        Assert.Contains("return null;", aggregateDashboard, StringComparison.Ordinal);

        Assert.Contains("BuildLedgerQuery(CommerceHubFinancialEntityType.Vendor, vendorId)", vendorFinance, StringComparison.Ordinal);
        Assert.Contains("ExportVendorStatementCsvAsync", vendorFinance, StringComparison.Ordinal);
        Assert.Contains("ApplyDateRangeFilters", vendorFinance, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceAnalyticsCertificationDocumentation_MustRecordM13AuthorityFreeze()
    {
        var findings = ReadRepoFile("docs/architecture/marketplace-analytics-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS M13 Marketplace Analytics Authority",
            "Revenue findings",
            "Conversion findings",
            "Attribution findings",
            "Vendor findings",
            "Customer findings",
            "Trending findings",
            "Reporting findings",
            "Projection findings",
            "Isolation findings",
            "Concurrency findings",
            "Historical findings",
            "Adversarial findings",
            "Runtime Hardening Performed",
            "Exact Runtime Files Modified",
            "Regression Freezes Added",
            "Architecture Updates"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS M13 Marketplace Analytics Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS M13 Marketplace Analytics Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS M13", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS M13 Marketplace Analytics Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS M13 Marketplace Analytics Authority Freeze", codexRules, StringComparison.Ordinal);
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
