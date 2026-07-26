namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendCompletionWorkflowAuthorityCertificationTests
{
    [Fact]
    public void CompletionExecution_MustBeScopedDeduplicatedAndBackendRevalidated()
    {
        var workflow = ReadRepoFile("PointOfSale.Web/src/marketplace/completionWorkflow.ts");
        var markCompleted = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useMarkCompleted.ts");
        var confirmCompletion = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useConfirmCompletion.ts");
        var incoming = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx");
        var myRequests = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx");

        Assert.Contains("completionExecutions", workflow, StringComparison.Ordinal);
        Assert.Contains("Completion workflow already in progress", workflow, StringComparison.Ordinal);
        Assert.Contains("authorityScope !== getCurrentAuthorityScope()", workflow, StringComparison.Ordinal);
        Assert.Contains("deriveServiceRequestLifecycleMutationAuthority(response, kind)", workflow, StringComparison.Ordinal);
        Assert.Contains("await revalidate()", workflow, StringComparison.Ordinal);
        Assert.Contains("deriveServiceRequestCompletionOutcome", workflow, StringComparison.Ordinal);
        Assert.Contains("authoritativeActorId !== currentActorId", workflow, StringComparison.Ordinal);
        Assert.Contains("authoritativeRequestId !== expectedRequestId", workflow, StringComparison.Ordinal);

        foreach (var source in new[] { markCompleted, confirmCompletion, incoming, myRequests })
        {
            Assert.Contains("executeServiceRequestCompletionWorkflow", source, StringComparison.Ordinal);
            Assert.Contains("marketplaceApi.getRequestDetails(requestId)", source, StringComparison.Ordinal);
            Assert.Contains("publishMarketplaceAuthorityInvalidation(authorityScope)", source, StringComparison.Ordinal);
            Assert.Contains("marketplaceKeys.scope(authorityScope)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("setQueryData", source, StringComparison.Ordinal);
            Assert.DoesNotContain("onMutate", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CompletionWorkflow_MustKeepCacheRealtimeHistoryAndActorStateDerivative()
    {
        var authority = ReadRepoFile("PointOfSale.Web/src/marketplace/authority.ts");
        var boundary = ReadRepoFile("PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");
        var queryKeys = ReadRepoFile("PointOfSale.Web/src/marketplace/queryKeys.ts");
        var chat = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");

        Assert.Contains("tenantId", authority, StringComparison.Ordinal);
        Assert.Contains("userId", authority, StringComparison.Ordinal);
        Assert.Contains("role !== 'superadmin'", authority, StringComparison.Ordinal);
        Assert.Contains("scope: (scope = getMarketplaceAuthorityScope())", queryKeys, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", boundary, StringComparison.Ordinal);
        Assert.Contains("isMarketplaceAuthorityInvalidation(event.data, scope)", boundary, StringComparison.Ordinal);
        Assert.Contains("queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })", chat, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", chat, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs4B()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-frontend-completion-workflow-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S4-B",
            "## Workflow Findings",
            "## Provider Findings",
            "## Buyer Findings",
            "## Mutation Findings",
            "## Success Findings",
            "## Failure Findings",
            "## Cache Findings",
            "## Realtime Findings",
            "## Actor-Transition Findings",
            "## History Findings",
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

        Assert.Contains("PASS F-S4-B", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-B", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-B", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-B", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-B", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
