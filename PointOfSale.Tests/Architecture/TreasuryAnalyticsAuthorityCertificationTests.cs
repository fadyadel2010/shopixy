namespace PointOfSale.Tests.Architecture;

public sealed class TreasuryAnalyticsAuthorityCertificationTests
{
    [Fact]
    public void TreasuryDashboardsAndKpis_MustDeriveFromTenantScopedSqlLedgerTruth()
    {
        var treasury = ReadRepoFile("PointOfSale.Infrastructure/Treasury/TreasuryReadService.cs");
        var dashboard = ReadRepoFile("PointOfSale.API/Controllers/DashboardController.cs");
        var financialReports = ReadRepoFile("PointOfSale.API/Controllers/FinancialReportsController.cs");

        Assert.Contains("l.TenantId == tenantId", treasury, StringComparison.Ordinal);
        Assert.Contains("JournalStatus.Posted", treasury, StringComparison.Ordinal);
        Assert.Contains("JournalStatus.Reversed", treasury, StringComparison.Ordinal);
        Assert.Contains("account.TenantId == tenantId", treasury, StringComparison.Ordinal);
        Assert.Contains("account.Account.TenantId == tenantId", treasury, StringComparison.Ordinal);

        Assert.Contains("BuildPostedRevenueQuery(tenantId)", dashboard, StringComparison.Ordinal);
        Assert.Contains("Financial and stock dashboard values are recomputed from SQL authority on every request.", dashboard, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", dashboard, StringComparison.Ordinal);

        Assert.Contains("_reportService.GetProfitAndLossAsync", financialReports, StringComparison.Ordinal);
        Assert.Contains("_reportService.GetBalanceSheetAsync", financialReports, StringComparison.Ordinal);
        Assert.Contains("_reportService.GetClearingAgingAsync", financialReports, StringComparison.Ordinal);
        Assert.Contains("Financial reporting is recomputed from the ledger for every request.", financialReports, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsExportsAndExecutiveSummaries_MustUseAuthoritativeSqlFinancialReaders()
    {
        var reports = ReadRepoFile("PointOfSale.Infrastructure/Accounting/FinancialReportService.cs");
        var commerceHub = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs");
        var superAdmin = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminFinanceOverviewService.cs");

        Assert.Contains("_db.LedgerEntries", reports, StringComparison.Ordinal);
        Assert.Contains("l.TenantId == tenantId", reports, StringComparison.Ordinal);
        Assert.Contains("JournalStatus.Posted", reports, StringComparison.Ordinal);
        Assert.Contains("JournalStatus.Reversed", reports, StringComparison.Ordinal);

        Assert.Contains("BuildLedgerQuery(CommerceHubFinancialEntityType.Vendor, vendorId)", commerceHub, StringComparison.Ordinal);
        Assert.Contains("ExportVendorStatementCsvAsync", commerceHub, StringComparison.Ordinal);
        Assert.Contains("GetVendorStatementAsync", commerceHub, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CommerceHubFinancialTransactions", superAdmin, StringComparison.Ordinal);
        Assert.Contains("_financialDashboardService.GetPlatformOverviewAsync", superAdmin, StringComparison.Ordinal);
    }

    [Fact]
    public void AggregatesAndProjections_MustBeValidatedDerivativesWithLiveSqlFallback()
    {
        var aggregateReader = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminFinancialDashboardService.Aggregates.cs");
        var aggregateService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubDailyFinancialAggregateService.cs");
        var storefront = ReadRepoFile("PointOfSale.Infrastructure/Analytics/StorefrontAnalyticsAuthorityReader.cs");

        Assert.Contains("!aggregateWindow.CoversRequestedWindow", aggregateReader, StringComparison.Ordinal);
        Assert.Contains("!aggregateWindow.IsFresh", aggregateReader, StringComparison.Ordinal);
        Assert.Contains("falling back to live ledger queries", aggregateReader, StringComparison.Ordinal);
        Assert.Contains("!filter.HasVendorFilter && !filter.HasPromoFilter", aggregateReader, StringComparison.Ordinal);

        Assert.Contains("invalidRowCount == 0", aggregateService, StringComparison.Ordinal);
        Assert.Contains("!hasPendingJobs", aggregateService, StringComparison.Ordinal);
        Assert.Contains("AggregateFreshnessThreshold", aggregateService, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CommerceHubFinancialTransactions", aggregateService, StringComparison.Ordinal);

        Assert.Contains("_dbContext.LedgerEntries", storefront, StringComparison.Ordinal);
        Assert.DoesNotContain("IMongo", storefront, StringComparison.Ordinal);
        Assert.DoesNotContain("IDistributedCache", storefront, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayAndTelemetry_MustNotCreateFinancialValue()
    {
        var analyticsConsumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/AnalyticsEventConsumer.cs");
        var purchaseConsumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/AuthoritativePurchaseAnalyticsConsumer.cs");
        var coldStore = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/AnalyticsColdStoreService.cs");
        var posting = ReadRepoFile("PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("StoreEventWithOutcomeAsync", analyticsConsumer, StringComparison.Ordinal);
        Assert.Contains("AnalyticsEventStoreOutcome.AlreadyExists", analyticsConsumer, StringComparison.Ordinal);
        Assert.Contains("BuildDeterministicEventId", purchaseConsumer, StringComparison.Ordinal);
        Assert.Contains("Duplicate", coldStore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EnsureIdempotentRequestMatches", posting, StringComparison.Ordinal);
        Assert.Contains("Journal entries may only be created", dbContext, StringComparison.Ordinal);
        Assert.Contains("Ledger entries may only be created", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void ForecastsAndBehavioralAnalytics_MustRemainNonMonetary()
    {
        var analyticsValidator = ReadRepoFile("PointOfSale.API/Services/Analytics/AnalyticsEventValidator.cs");
        var metricReader = ReadRepoFile("PointOfSale.Infrastructure/Analytics/Alerts/AnalyticsMetricReader.cs");
        var serviceAnalytics = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");

        Assert.Contains("PurchaseCompleted", analyticsValidator, StringComparison.Ordinal);
        Assert.Contains("IStorefrontAnalyticsAuthorityReader", metricReader, StringComparison.Ordinal);
        Assert.Contains("CompletedByBuyer", serviceAnalytics, StringComparison.Ordinal);
        Assert.DoesNotContain("PostJournalAsync", analyticsValidator, StringComparison.Ordinal);
        Assert.DoesNotContain("PostJournalAsync", metricReader, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantIsolationAndHistoricalConsistency_MustComeFromTrustedSqlScope()
    {
        var treasuryController = ReadRepoFile("PointOfSale.API/Controllers/TreasuryController.cs");
        var treasury = ReadRepoFile("PointOfSale.Infrastructure/Treasury/TreasuryReadService.cs");
        var storefront = ReadRepoFile("PointOfSale.Infrastructure/Analytics/StorefrontAnalyticsAuthorityReader.cs");
        var marketplace = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubDailyFinancialAggregateService.cs");

        Assert.Contains("_tenantContext.RequiredTenantId", treasuryController, StringComparison.Ordinal);
        Assert.Contains("IgnoreQueryFilters()", treasury, StringComparison.Ordinal);
        Assert.Contains("entry.TenantId == tenantId", storefront, StringComparison.Ordinal);
        Assert.Contains("row.DateUtc >= fromDate && row.DateUtc <= toDate", marketplace, StringComparison.Ordinal);
        Assert.Contains("BuildAggregateMapAsync", marketplace, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CommerceHubFinancialTransactions", marketplace, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureTruth_MustDeclareAnalyticsDerivativeAndNonMonetary()
    {
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var rules = ReadRepoFile(".ai-context/CODEX_RULES.md");
        var findings = ReadRepoFile("docs/architecture/treasury-analytics-authority-certification-findings.md");

        Assert.Contains("PASS T8 Treasury Analytics Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("Treasury analytics authority is derivative", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("No inspected T8 runtime blocker remains", knownRisks, StringComparison.Ordinal);
        Assert.Contains("Treasury analytics owns derivative", hardening, StringComparison.Ordinal);
        Assert.Contains("Treasury analytics may calculate", rules, StringComparison.Ordinal);
        Assert.Contains("Can analytics create money? No", findings, StringComparison.Ordinal);
        Assert.Contains("Can projections override SQL? No", findings, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
