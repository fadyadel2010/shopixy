namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceRequestAuthorityCertificationTests
{
    [Fact]
    public void RequestCreation_MustDeriveIdentityOwnershipAndSnapshotsFromCurrentSqlAuthority()
    {
        var service = ReadRepoFile("PointOfSale.Infrastructure/Services/ServiceRequestService.cs");
        var entity = ReadRepoFile("PointOfSale.Core/Entities/ServiceRequest.cs");

        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", service, StringComparison.Ordinal);
        Assert.Contains("s.TenantId == tenantId", service, StringComparison.Ordinal);
        Assert.Contains("s.IsActive", service, StringComparison.Ordinal);
        Assert.Contains("s.Profile.TenantId == tenantId", service, StringComparison.Ordinal);
        Assert.Contains("s.Profile.IsActive", service, StringComparison.Ordinal);
        Assert.Contains("t.Status == TenantStatus.Active", service, StringComparison.Ordinal);
        Assert.Contains("duplicateActiveRequest", service, StringComparison.Ordinal);
        Assert.Contains("ServiceRequest.Create(", service, StringComparison.Ordinal);
        Assert.Contains("service.Title", service, StringComparison.Ordinal);
        Assert.Contains("service.StartingPrice", service, StringComparison.Ordinal);
        Assert.Contains("service.Packages", service, StringComparison.Ordinal);

        Assert.Contains("ProviderProfileId", entity, StringComparison.Ordinal);
        Assert.Contains("ServiceTitleSnapshot", entity, StringComparison.Ordinal);
        Assert.Contains("ServiceStartingPriceSnapshot", entity, StringComparison.Ordinal);
        Assert.Contains("ServicePricingTypeSnapshot", entity, StringComparison.Ordinal);
        Assert.Contains("ServiceCurrencySnapshot", entity, StringComparison.Ordinal);
        Assert.Contains("ServicePackagesSnapshot", entity, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestLifecycle_MustUseSqlTenantOwnershipRowLocksAndTerminalTransitions()
    {
        var service = ReadRepoFile("PointOfSale.Infrastructure/Services/ServiceRequestService.cs");
        var entity = ReadRepoFile("PointOfSale.Core/Entities/ServiceRequest.cs");
        var interceptor = ReadRepoFile("PointOfSale.Infrastructure/Data/MarketplaceInvariantInterceptor.cs");
        var status = ReadRepoFile("PointOfSale.Core/Enums/ServiceRequestStatus.cs");

        Assert.Contains("LoadRequestForMutationAsync", service, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", service, StringComparison.Ordinal);
        Assert.Contains("r.TenantId == tenantId", service, StringComparison.Ordinal);
        Assert.Contains("request.ProviderUserId != providerUserId", service, StringComparison.Ordinal);
        Assert.Contains("request.BuyerUserId != buyerUserId", service, StringComparison.Ordinal);
        Assert.Contains("RejectRequestAsync", service, StringComparison.Ordinal);

        Assert.Contains("public void Reject()", entity, StringComparison.Ordinal);
        Assert.Contains("Status != ServiceRequestStatus.Pending", entity, StringComparison.Ordinal);
        Assert.Contains("Status = ServiceRequestStatus.Rejected", entity, StringComparison.Ordinal);

        Assert.Contains("Rejected = 5", status, StringComparison.Ordinal);
        Assert.Contains("ValidateStatusTransition", interceptor, StringComparison.Ordinal);
        Assert.Contains("ServiceRequest.ProviderProfileId", interceptor, StringComparison.Ordinal);
        Assert.Contains("ServiceRequest.ServiceTitleSnapshot", interceptor, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestPersistence_MustFreezeDuplicateActiveRequestsAndSnapshotColumnsInSql()
    {
        var config = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/ServiceRequestConfiguration.cs");
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260611120000_ServiceMarketplaceRequestAuthorityHardening.cs");

        Assert.Contains("UX_ServiceRequests_Active_Service_Buyer", config, StringComparison.Ordinal);
        Assert.Contains("'Pending', 'Accepted', 'CompletedByProvider'", config, StringComparison.Ordinal);
        Assert.Contains("ServiceTitleSnapshot", config, StringComparison.Ordinal);
        Assert.Contains("ServiceCurrencySnapshot", config, StringComparison.Ordinal);

        Assert.Contains("ProviderProfileId", migration, StringComparison.Ordinal);
        Assert.Contains("ServiceTitleSnapshot", migration, StringComparison.Ordinal);
        Assert.Contains("UPDATE \"\"ServiceRequests\"\" sr", migration, StringComparison.Ordinal);
        Assert.Contains("duplicate active service requests exist", migration, StringComparison.Ordinal);
        Assert.Contains("UX_ServiceRequests_Active_Service_Buyer", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestControllerReads_MustExposeHistoricalSnapshotsAndRejectRoute()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");

        Assert.Contains("serviceTitle = r.ServiceTitleSnapshot", controller, StringComparison.Ordinal);
        Assert.Contains("serviceCategory = r.ServiceCategorySnapshot", controller, StringComparison.Ordinal);
        Assert.Contains("startingPrice = r.ServiceStartingPriceSnapshot", controller, StringComparison.Ordinal);
        Assert.Contains("serviceTitle = request.ServiceTitleSnapshot", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"requests/{requestId}/reject\")]", controller, StringComparison.Ordinal);
        Assert.Contains("RejectRequestAsync(requestId, userId)", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceMarketplaceRequestCertificationDocumentation_MustRecordS1BAuthorityFreeze()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-request-engagement-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS S1-B Service Marketplace Request & Engagement Authority",
            "Identity findings",
            "Ownership findings",
            "Snapshot findings",
            "Acceptance findings",
            "Cancellation findings",
            "Engagement findings",
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

        Assert.Contains("PASS S1-B Service Marketplace Request & Engagement Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS S1-B Service Marketplace Request & Engagement Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS S1-B", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS S1-B Service Marketplace Request & Engagement Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS S1-B Service Marketplace Request & Engagement Authority Freeze", codexRules, StringComparison.Ordinal);
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
