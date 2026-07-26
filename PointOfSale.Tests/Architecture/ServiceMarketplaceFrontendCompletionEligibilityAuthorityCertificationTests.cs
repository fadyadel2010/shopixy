namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendCompletionEligibilityAuthorityCertificationTests
{
    [Fact]
    public void CompletionEligibility_MustBindBackendLifecycleCapabilitiesToCurrentActorIdentity()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");
        var eligibility = ReadRepoFile("PointOfSale.Web/src/marketplace/completionEligibility.ts");
        var myRequests = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx");
        var incoming = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx");
        var requestCard = ReadRepoFile("PointOfSale.Web/src/marketplace/components/RequestCard.tsx");
        var requestDetails = ReadRepoFile("PointOfSale.Web/src/marketplace/viewmodels/useRequestDetailsVM.ts");

        Assert.Contains("buyerUserId = r.BuyerUserId", controller, StringComparison.Ordinal);
        Assert.Contains("providerUserId = r.ProviderUserId", controller, StringComparison.Ordinal);
        Assert.Contains("deriveServiceRequestCompletionEligibility", eligibility, StringComparison.Ordinal);
        Assert.Contains("buyerUserId === providerUserId", eligibility, StringComparison.Ordinal);
        Assert.Contains("actorUserId === buyerUserId", eligibility, StringComparison.Ordinal);
        Assert.Contains("actorUserId === providerUserId", eligibility, StringComparison.Ordinal);
        Assert.Contains("capabilities.canMarkCompleted && actorRole !== 'provider'", eligibility, StringComparison.Ordinal);
        Assert.Contains("capabilities.canConfirmCompletion && actorRole !== 'buyer'", eligibility, StringComparison.Ordinal);
        Assert.Contains("lifecycle.statusKey === 'accepted'", eligibility, StringComparison.Ordinal);
        Assert.Contains("lifecycle.statusKey === 'completedByProvider'", eligibility, StringComparison.Ordinal);

        foreach (var source in new[] { myRequests, incoming, requestCard, requestDetails })
        {
            Assert.Contains("deriveServiceRequestCompletionEligibility", source, StringComparison.Ordinal);
        }

        Assert.Contains("'buyer'", myRequests, StringComparison.Ordinal);
        Assert.Contains("'provider'", incoming, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletionMutationsAndRealtime_MustReenterScopedBackendAuthority()
    {
        var markCompleted = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useMarkCompleted.ts");
        var confirmCompletion = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useConfirmCompletion.ts");
        var incoming = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx");
        var myRequests = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx");
        var chat = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");
        var workflow = ReadRepoFile("PointOfSale.Web/src/marketplace/completionWorkflow.ts");

        foreach (var source in new[] { markCompleted, confirmCompletion, incoming, myRequests })
        {
            Assert.Contains("executeServiceRequestCompletionWorkflow", source, StringComparison.Ordinal);
            Assert.Contains("publishMarketplaceAuthorityInvalidation(authorityScope)", source, StringComparison.Ordinal);
            Assert.Contains("marketplaceKeys.scope(authorityScope)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("setQueryData", source, StringComparison.Ordinal);
            Assert.DoesNotContain("onMutate", source, StringComparison.Ordinal);
        }

        Assert.Contains("authorityScope !== getCurrentAuthorityScope()", workflow, StringComparison.Ordinal);
        Assert.Contains("deriveServiceRequestLifecycleMutationAuthority", workflow, StringComparison.Ordinal);
        Assert.Contains("await revalidate()", workflow, StringComparison.Ordinal);
        Assert.Contains("completionExecutions", workflow, StringComparison.Ordinal);
        Assert.Contains("queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })", chat, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", chat, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletionSurfaces_MustWithholdCachedActionsDuringCurrentRefresh()
    {
        var incoming = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx");
        var myRequests = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx");
        var requestDetails = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useRequestDetails.ts");
        var boundary = ReadRepoFile("PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");

        Assert.Contains("if (isLoading || isFetching)", incoming, StringComparison.Ordinal);
        Assert.Contains("if (isLoading || isFetching)", myRequests, StringComparison.Ordinal);
        Assert.Contains("refetchInterval: 15_000", requestDetails, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", boundary, StringComparison.Ordinal);
        Assert.Contains("AUTHORITY_REVALIDATION_INTERVAL_MS", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs4A()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-frontend-completion-eligibility-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S4-A",
            "## Eligibility Findings",
            "## Provider Findings",
            "## Buyer Findings",
            "## Lifecycle Findings",
            "## Visibility Findings",
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

        Assert.Contains("PASS F-S4-A", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-A", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-A", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-A", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-A", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
