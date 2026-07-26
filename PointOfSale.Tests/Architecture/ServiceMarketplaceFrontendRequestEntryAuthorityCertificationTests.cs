namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendRequestEntryAuthorityCertificationTests
{
    [Fact]
    public void EligibilityAndSelfService_MustDeriveFromCurrentBackendAuthority()
    {
        var service = ReadRepoFile(
            "PointOfSale.Infrastructure/Services/ServiceRequestService.cs");
        var controller = ReadRepoFile(
            "PointOfSale.API/Controllers/MarketplaceController.cs");
        var page = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx");
        var adapter = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/serviceDetailAuthority.ts");

        Assert.Contains("GetRequestEntryAuthorityAsync", service, StringComparison.Ordinal);
        Assert.Contains("s.TenantId == tenantId.Value", service, StringComparison.Ordinal);
        Assert.Contains("s.Profile.TenantId == tenantId.Value", service, StringComparison.Ordinal);
        Assert.Contains("ProviderUserId = s.Profile!.UserId", service, StringComparison.Ordinal);
        Assert.Contains("return new(false, \"OwnService\")", service, StringComparison.Ordinal);
        Assert.Contains("? new(false, \"ActiveRequestExists\", activeRequestId)", service, StringComparison.Ordinal);
        Assert.Contains(": new(true, \"Eligible\")", service, StringComparison.Ordinal);
        Assert.Contains("requestEntry = new", controller, StringComparison.Ordinal);
        Assert.Contains("const canRequestService = service.requestEntry.canRequest", page, StringComparison.Ordinal);
        Assert.DoesNotContain("profile.profileId === service.providerProfileId", page, StringComparison.Ordinal);
        Assert.Contains("parseRequestEntry", adapter, StringComparison.Ordinal);
        Assert.Contains("reason: 'Unavailable'", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveRequestAndConcurrentReplay_MustFailClosed()
    {
        var service = ReadRepoFile(
            "PointOfSale.Infrastructure/Services/ServiceRequestService.cs");
        var configuration = ReadRepoFile(
            "PointOfSale.Infrastructure/Data/Configurations/ServiceRequestConfiguration.cs");
        var controller = ReadRepoFile(
            "PointOfSale.API/Controllers/MarketplaceController.cs");

        Assert.Contains("IsolationLevel.Serializable", service, StringComparison.Ordinal);
        Assert.Contains("duplicateActiveRequest", service, StringComparison.Ordinal);
        Assert.Contains("ActiveServiceRequestExists()", service, StringComparison.Ordinal);
        Assert.Contains("PostgresErrorCodes.UniqueViolation", service, StringComparison.Ordinal);
        Assert.Contains("ActiveRequestConstraintName", service, StringComparison.Ordinal);
        Assert.Contains("UX_ServiceRequests_Active_Service_Buyer", configuration, StringComparison.Ordinal);
        Assert.Contains("\"ACTIVE_REQUEST_EXISTS\" => Conflict", controller, StringComparison.Ordinal);
        Assert.Contains("\"SELF_REQUEST_FORBIDDEN\"", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void FormMutationCacheAndNavigation_MustRejectStaleReplay()
    {
        var page = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx");
        var hook = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/hooks/useRequestService.ts");
        var authority = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/authority.ts");
        var boundary = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");

        Assert.Contains("submissionRef.current", page, StringComparison.Ordinal);
        Assert.Contains("getMarketplaceAuthorityScope() !== submittedScope", page, StringComparison.Ordinal);
        Assert.Contains("requestContextRef.current.serviceId !== submittedServiceId", page, StringComparison.Ordinal);
        Assert.Contains("mountedRef.current", page, StringComparison.Ordinal);
        Assert.Contains("maxLength={MAX_REQUEST_MESSAGE_LENGTH}", page, StringComparison.Ordinal);
        Assert.Contains("requestMessage.trim()", page, StringComparison.Ordinal);
        Assert.Contains("networkMode: 'always'", hook, StringComparison.Ordinal);
        Assert.Contains("retry: false", hook, StringComparison.Ordinal);
        Assert.Contains("onSettled:", hook, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.scope(authorityScope)", hook, StringComparison.Ordinal);
        Assert.Contains("publishMarketplaceAuthorityInvalidation", hook, StringComparison.Ordinal);
        Assert.Contains("BroadcastChannel", authority, StringComparison.Ordinal);
        Assert.Contains("isMarketplaceAuthorityInvalidation(event.data, scope)", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", hook, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs1E()
    {
        var findings = ReadRepoFile(
            "docs/architecture/service-marketplace-frontend-request-entry-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S1-E",
            "Eligibility Findings",
            "Self-Service Findings",
            "Active-Request Findings",
            "Form Findings",
            "Capability Findings",
            "Mutation Findings",
            "Cache Findings",
            "Navigation Findings",
            "Realtime Findings",
            "Tenant Isolation Findings",
            "Failure-Handling Findings",
            "Concurrency Findings",
            "Adversarial Findings",
            "Runtime Hardening Performed",
            "Exact Runtime Files Modified",
            "Regression Freezes Added",
            "Architecture Updates",
            "Explicit Answers"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS F-S1-E", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-E", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-E", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-E", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-E", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) =>
        CertificationSource.ReadRepoFile(relativePath);
}
