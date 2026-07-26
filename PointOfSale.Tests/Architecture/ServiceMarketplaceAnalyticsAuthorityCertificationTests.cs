namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceAnalyticsAuthorityCertificationTests
{
    [Fact]
    public void ServiceMarketplaceAnalyticsRuntime_MustDeriveDashboardKpisFromSqlAuthorities()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");
        var trustService = ReadRepoFile("PointOfSale.Infrastructure/Services/ProviderTrustService.cs");
        var requestService = ReadRepoFile("PointOfSale.Infrastructure/Services/ServiceRequestService.cs");
        var reviewService = ReadRepoFile("PointOfSale.Infrastructure/Services/ServiceReviewService.cs");

        Assert.Contains("Analytics cards are derivative only; bind them to current SQL tenant/profile authority.", controller, StringComparison.Ordinal);
        Assert.Contains("var tenantId = GetTenantId();", controller, StringComparison.Ordinal);
        Assert.Contains("s.TenantId == tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("r.TenantId == tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("r.ProviderProfileId == profile.Id", controller, StringComparison.Ordinal);
        Assert.Contains("r.Status == Core.Enums.ServiceRequestStatus.CompletedByBuyer", controller, StringComparison.Ordinal);
        Assert.Contains("_trustService.GetTrustAggregateAsync(profile.Id)", controller, StringComparison.Ordinal);

        Assert.Contains("Public trust authority is recomposed from SQL ServiceReview truth", trustService, StringComparison.Ordinal);
        Assert.Contains("ComposeSqlTrustAggregateAsync", trustService, StringComparison.Ordinal);
        Assert.Contains("row.Request.Status == ServiceRequestStatus.CompletedByBuyer", trustService, StringComparison.Ordinal);
        Assert.Contains("ProviderTrustAggregate.FromSqlReviewTruth", trustService, StringComparison.Ordinal);

        Assert.Contains("r.TenantId == tenantId && r.ProviderUserId == providerUserId && !r.IsDeleted", requestService, StringComparison.Ordinal);
        Assert.Contains("r.TenantId == tenantId && r.BuyerUserId == buyerUserId && !r.IsDeleted", requestService, StringComparison.Ordinal);
        Assert.Contains("r.ServiceRequest.Status == ServiceRequestStatus.CompletedByBuyer", reviewService, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceMarketplaceAnalyticsRuntime_MustNotUseProjectionCacheOrExportAuthority()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");
        var searchService = ReadRepoFile("PointOfSale.Infrastructure/Services/MarketplaceSearchService.cs");
        var searchDocument = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Documents/MarketplaceServiceDocument.cs");

        Assert.Contains("Elasticsearch may return candidates; returned listing fields are recomposed from SQL authority.", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("NEVER queries PostgreSQL - only Elasticsearch.", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpGet(\"analytics/export", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[HttpGet(\"reports/export", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[HttpGet(\"dashboard/export", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[HttpGet(\"export", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("text/csv", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", controller, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("_context.Set<Service>()", searchService, StringComparison.Ordinal);
        Assert.Contains("_providerTrustService.GetTrustAggregateAsync", searchService, StringComparison.Ordinal);
        Assert.DoesNotContain("AverageRating = document.Trust?.AverageRating", searchService, StringComparison.Ordinal);

        Assert.Contains("TotalRequests", searchDocument, StringComparison.Ordinal);
        Assert.Contains("CompletedRequests", searchDocument, StringComparison.Ordinal);
        Assert.Contains("CompletionRate", searchDocument, StringComparison.Ordinal);
        Assert.Contains("ServiceStats = new MarketplaceServiceDocument.ServiceStatistics", controller, StringComparison.Ordinal);
        Assert.Contains("TotalRequests = 0", controller, StringComparison.Ordinal);
        Assert.Contains("CompletedRequests = 0", controller, StringComparison.Ordinal);
        Assert.Contains("CompletionRate = 0", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceMarketplaceAnalyticsCertificationDocumentation_MustRecordS1FAuthorityFreeze()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-analytics-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS S1-F Service Marketplace Analytics Authority",
            "Performance findings",
            "Revenue findings",
            "Trust findings",
            "Funnel findings",
            "Dashboard findings",
            "Export findings",
            "Projection findings",
            "Isolation findings",
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

        Assert.Contains("PASS S1-F Service Marketplace Analytics Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS S1-F Service Marketplace Analytics Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS S1-F", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS S1-F Service Marketplace Analytics Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS S1-F Service Marketplace Analytics Authority Freeze", codexRules, StringComparison.Ordinal);
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

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
