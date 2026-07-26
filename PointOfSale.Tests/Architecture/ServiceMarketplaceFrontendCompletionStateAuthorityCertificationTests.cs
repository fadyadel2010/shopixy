namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendCompletionStateAuthorityCertificationTests
{
    [Fact]
    public void CompletionState_MustRequireCanonicalLifecycleAndBackendEvidence()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");
        var status = ReadRepoFile("PointOfSale.Web/src/marketplace/status.ts");
        var state = ReadRepoFile("PointOfSale.Web/src/marketplace/completionStateAuthority.ts");
        var workflow = ReadRepoFile("PointOfSale.Web/src/marketplace/completionWorkflow.ts");

        Assert.Contains("completionEvidence = BuildCompletionEvidence(r)", controller, StringComparison.Ordinal);
        Assert.Contains("lifecycleStatusByBackendValue", status, StringComparison.Ordinal);
        Assert.DoesNotContain("expired:", status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("replace(/[-_\\s]/g", status, StringComparison.Ordinal);
        Assert.Contains("deriveServiceRequestCompletionStatePresentation", state, StringComparison.Ordinal);
        Assert.Contains("evidence.status !== lifecycle.statusLabelFallback", state, StringComparison.Ordinal);
        Assert.Contains("evidence.providerCompletion === null && evidence.buyerConfirmation === null", state, StringComparison.Ordinal);
        Assert.Contains("deriveCompletionEvidencePresentation", state, StringComparison.Ordinal);
        Assert.Contains("deriveServiceRequestCompletionStatePresentation", workflow, StringComparison.Ordinal);
        Assert.Contains("|| !completionState", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletionStateSurfaces_MustUseCurrentScopedBackendAuthority()
    {
        var sources = new[]
        {
            ReadRepoFile("PointOfSale.Web/src/marketplace/components/RequestCard.tsx"),
            ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx"),
            ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx"),
            ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ChatPage.tsx"),
            ReadRepoFile("PointOfSale.Web/src/marketplace/viewmodels/useRequestDetailsVM.ts")
        };
        var authority = ReadRepoFile("PointOfSale.Web/src/marketplace/authority.ts");
        var boundary = ReadRepoFile("PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");
        var chat = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");

        foreach (var source in sources)
        {
            Assert.Contains("deriveServiceRequestCompletionStatePresentation", source, StringComparison.Ordinal);
        }

        Assert.Contains("staleTime: 0", authority, StringComparison.Ordinal);
        Assert.Contains("retry: false", authority, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", boundary, StringComparison.Ordinal);
        Assert.Contains("isMarketplaceAuthorityInvalidation(event.data, scope)", boundary, StringComparison.Ordinal);
        Assert.Contains("invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })", chat, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", chat, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs4D()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-frontend-completion-state-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S4-D",
            "## State Findings",
            "## Lifecycle Findings",
            "## Presentation Findings",
            "## Action-Consistency Findings",
            "## Cache Findings",
            "## Realtime Findings",
            "## Actor-Transition Findings",
            "## History Findings",
            "## Failure Findings",
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

        Assert.Contains("PASS F-S4-D", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-D", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-D", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-D", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-D", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
