namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendContractPresentationAuthorityCertificationTests
{
    [Fact]
    public void ContractPresentation_MustUseBackendSnapshotBoundaryAndLifecycleGate()
    {
        var contract = ReadRepoFile("PointOfSale.Web/src/marketplace/contractPresentation.ts");
        var types = ReadRepoFile("PointOfSale.Web/src/marketplace/types/index.ts");
        var card = ReadRepoFile("PointOfSale.Web/src/marketplace/components/RequestCard.tsx");
        var myRequests = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx");
        var incoming = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx");
        var chatPage = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ChatPage.tsx");

        Assert.Contains("deriveServiceRequestContractPresentation", contract, StringComparison.Ordinal);
        Assert.Contains("contractVisibleStatuses", contract, StringComparison.Ordinal);
        Assert.Contains("accepted", contract, StringComparison.Ordinal);
        Assert.Contains("completedByProvider", contract, StringComparison.Ordinal);
        Assert.Contains("completedByBuyer", contract, StringComparison.Ordinal);
        Assert.Contains("return null", contract, StringComparison.Ordinal);
        Assert.Contains("validMoney", contract, StringComparison.Ordinal);
        Assert.Contains("derivePackages", contract, StringComparison.Ordinal);
        Assert.Contains("pricingTypes", contract, StringComparison.Ordinal);

        Assert.Contains("startingPrice?: number", types, StringComparison.Ordinal);
        Assert.Contains("packages?: string", types, StringComparison.Ordinal);
        Assert.Contains("requestMessage?: string", types, StringComparison.Ordinal);

        foreach (var source in new[] { card, myRequests, incoming, chatPage })
        {
            Assert.Contains("deriveServiceRequestContractPresentation", source, StringComparison.Ordinal);
            Assert.Contains("contract ?", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ContractPresentation_MustNotUseRouteStateOptimismRealtimeOrCacheWritesAsAuthority()
    {
        var marketplaceSources = string.Join('\n', new[]
        {
            ReadRepoFile("PointOfSale.Web/src/marketplace/contractPresentation.ts"),
            ReadRepoFile("PointOfSale.Web/src/marketplace/components/RequestCard.tsx"),
            ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx"),
            ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx"),
            ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ChatPage.tsx"),
            ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts"),
            ReadRepoFile("PointOfSale.Web/src/marketplace/api/marketplaceApi.ts")
        });
        var chatHook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");
        var api = ReadRepoFile("PointOfSale.Web/src/marketplace/api/marketplaceApi.ts");
        var myRequests = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx");
        var incoming = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx");
        var chatPage = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ChatPage.tsx");

        Assert.DoesNotContain("location.state", marketplaceSources, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", marketplaceSources, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", marketplaceSources, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData<Contract", marketplaceSources, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData<ServiceRequest", marketplaceSources, StringComparison.Ordinal);
        Assert.DoesNotContain("queryClient.setQueryData<ChatMessage[]>", chatHook, StringComparison.Ordinal);
        Assert.Contains("getRequestDetails", api, StringComparison.Ordinal);

        foreach (var source in new[] { myRequests, incoming })
        {
            Assert.Contains("if (isLoading || isFetching)", source, StringComparison.Ordinal);
        }

        Assert.Contains("if (isLoadingRequest || isFetchingRequest)", chatPage, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendRequestDtos_MustExposeCurrentRequestSnapshotContractFields()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");

        Assert.Contains("serviceTitle = r.ServiceTitleSnapshot", controller, StringComparison.Ordinal);
        Assert.Contains("serviceCategory = r.ServiceCategorySnapshot", controller, StringComparison.Ordinal);
        Assert.Contains("startingPrice = r.ServiceStartingPriceSnapshot", controller, StringComparison.Ordinal);
        Assert.Contains("pricingType = r.ServicePricingTypeSnapshot?.ToString()", controller, StringComparison.Ordinal);
        Assert.Contains("currency = r.ServiceCurrencySnapshot", controller, StringComparison.Ordinal);
        Assert.Contains("packages = request.ServicePackagesSnapshot", controller, StringComparison.Ordinal);
        Assert.Contains("requestMessage = r.RequestMessage", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("serviceTitle = r.Service.Title", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("startingPrice = r.Service.StartingPrice", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs2C()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-frontend-contract-presentation-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S2-C",
            "## Contract Identity Findings",
            "## Pricing Findings",
            "## Scope Findings",
            "## Deliverable Findings",
            "## Timeline Findings",
            "## Revision Findings",
            "## Lifecycle Findings",
            "## Cache Findings",
            "## Realtime Findings",
            "## Actor-Transition Findings",
            "## History Findings",
            "## Failure Findings",
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

        Assert.Contains("PASS F-S2-C", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S2-C", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S2-C", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S2-C", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S2-C", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);

}
