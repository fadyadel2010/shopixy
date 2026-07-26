namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendRequestCreationWorkflowAuthorityCertificationTests
{
    [Fact]
    public void SubmissionAndSuccess_MustDeriveFromBackendCreatedRequestAuthority()
    {
        var page = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx");
        var hook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useRequestService.ts");
        var adapter = ReadRepoFile("PointOfSale.Web/src/marketplace/requestCreationAuthority.ts");
        var api = ReadRepoFile("PointOfSale.Web/src/marketplace/api/marketplaceApi.ts");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");

        Assert.Contains("MAX_REQUEST_MESSAGE_LENGTH", page, StringComparison.Ordinal);
        Assert.Contains("normalizedMessage.length > MAX_REQUEST_MESSAGE_LENGTH", page, StringComparison.Ordinal);
        Assert.Contains("deriveCreatedServiceRequestAuthority", hook, StringComparison.Ordinal);
        Assert.Contains("throw new Error('Request creation response did not contain authoritative created-request identity')", hook, StringComparison.Ordinal);
        Assert.Contains("requestId", adapter, StringComparison.Ordinal);
        Assert.Contains("responseServiceId.toLowerCase() !== submittedId.toLowerCase()", adapter, StringComparison.Ordinal);
        Assert.Contains("status !== 'Pending'", adapter, StringComparison.Ordinal);
        Assert.Contains("Promise<CreatedServiceRequestAuthority>", api, StringComparison.Ordinal);
        Assert.Contains("requestId = request.Id", controller, StringComparison.Ordinal);
        Assert.Contains("serviceId = request.ServiceId", controller, StringComparison.Ordinal);
        Assert.Contains("status = request.Status.ToString()", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void MutationNavigationCacheAndReplay_MustRemainDerivative()
    {
        var page = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx");
        var hook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useRequestService.ts");
        var authority = ReadRepoFile("PointOfSale.Web/src/marketplace/authority.ts");
        var boundary = ReadRepoFile("PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");

        Assert.Contains("submissionRef.current", page, StringComparison.Ordinal);
        Assert.Contains("getMarketplaceAuthorityScope() !== submittedScope", page, StringComparison.Ordinal);
        Assert.Contains("requestContextRef.current.authorityScope !== submittedScope", page, StringComparison.Ordinal);
        Assert.Contains("requestContextRef.current.serviceId !== submittedServiceId", page, StringComparison.Ordinal);
        Assert.Contains("navigate('/marketplace?tab=my-requests')", page, StringComparison.Ordinal);
        Assert.Contains("retry: false", hook, StringComparison.Ordinal);
        Assert.Contains("networkMode: 'always'", hook, StringComparison.Ordinal);
        Assert.Contains("onSettled:", hook, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.scope(authorityScope)", hook, StringComparison.Ordinal);
        Assert.Contains("publishMarketplaceAuthorityInvalidation(authorityScope, variables.serviceId)", hook, StringComparison.Ordinal);
        Assert.Contains("BroadcastChannel", authority, StringComparison.Ordinal);
        Assert.Contains("isMarketplaceAuthorityInvalidation(event.data, scope)", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("onMutate", hook, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendRequestCreationAuthority_MustRemainSqlOwned()
    {
        var service = ReadRepoFile("PointOfSale.Infrastructure/Services/ServiceRequestService.cs");
        var configuration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/ServiceRequestConfiguration.cs");
        var dto = ReadRepoFile("PointOfSale.Core/DTOs/Marketplace/RequestServiceDto.cs");

        Assert.Contains("IsolationLevel.Serializable", service, StringComparison.Ordinal);
        Assert.Contains("s.TenantId == tenantId", service, StringComparison.Ordinal);
        Assert.Contains("s.Profile.TenantId == tenantId", service, StringComparison.Ordinal);
        Assert.Contains("duplicateActiveRequest", service, StringComparison.Ordinal);
        Assert.Contains("SelfServiceRequestForbidden", service, StringComparison.Ordinal);
        Assert.Contains("ServiceRequest.Create", service, StringComparison.Ordinal);
        Assert.Contains("PostgresErrorCodes.UniqueViolation", service, StringComparison.Ordinal);
        Assert.Contains("UX_ServiceRequests_Active_Service_Buyer", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderUserId", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("Status", dto, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs2A()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-frontend-request-creation-workflow-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S2-A",
            "## Submission Findings",
            "## Success Findings",
            "## Failure Findings",
            "## Mutation Findings",
            "## Navigation Findings",
            "## Cache Findings",
            "## Realtime Findings",
            "## Actor-Transition Findings",
            "## History Findings",
            "## Offline Findings",
            "## Recovery Findings",
            "## Concurrency Findings",
            "## Adversarial Findings",
            "## Runtime Hardening Performed",
            "## Exact Runtime Files Modified",
            "## Regression Freezes Added",
            "## Architecture Updates",
            "## Explicit Answers"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS F-S2-A", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S2-A", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S2-A", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S2-A", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S2-A", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
