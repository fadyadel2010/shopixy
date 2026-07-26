namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendCompletionTimelineAuditAuthorityCertificationTests
{
    [Fact]
    public void CompletionTimeline_MustBindAuditToCurrentActorLifecycleAndCompletionEvidence()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");
        var authority = ReadRepoFile("PointOfSale.Web/src/marketplace/completionTimelineAuthority.ts");
        var chatPage = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ChatPage.tsx");

        Assert.Contains("auditTimeline = BuildRequestAuditTimeline(request)", controller, StringComparison.Ordinal);
        Assert.Contains("completionEvidence = BuildCompletionEvidence(request)", controller, StringComparison.Ordinal);
        Assert.Contains("deriveCompletionTimelineAuditPresentation", authority, StringComparison.Ordinal);
        Assert.Contains("actorUserId !== buyerUserId && actorUserId !== providerUserId", authority, StringComparison.Ordinal);
        Assert.Contains("hasBackendEventIdentity", authority, StringComparison.Ordinal);
        Assert.Contains("occurredAt !== createdAt", authority, StringComparison.Ordinal);
        Assert.Contains("timestampValue(providerCompletion.occurredAt)", authority, StringComparison.Ordinal);
        Assert.Contains("timestampValue(buyerConfirmation.occurredAt)", authority, StringComparison.Ordinal);
        Assert.Contains("event.actorRole !== null || event.actorUserId !== null", authority, StringComparison.Ordinal);

        Assert.Contains(
            "deriveCompletionTimelineAuditPresentation(request, completionState, currentUserId)",
            chatPage,
            StringComparison.Ordinal);
        Assert.DoesNotContain("deriveRequestAuditTimelinePresentation(request, lifecycle)", chatPage, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletionTimeline_CacheRealtimeHistoryAndTransitionsMustRemainDerivative()
    {
        var requestHook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useRequestDetails.ts");
        var authorityOptions = ReadRepoFile("PointOfSale.Web/src/marketplace/authority.ts");
        var authorityBoundary = ReadRepoFile("PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");
        var chatHook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");
        var chatPage = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ChatPage.tsx");

        Assert.Contains("marketplaceKeys.request(requestId, authorityScope)", requestHook, StringComparison.Ordinal);
        Assert.Contains("refetchInterval: 15_000", requestHook, StringComparison.Ordinal);
        Assert.Contains("staleTime: 0", authorityOptions, StringComparison.Ordinal);
        Assert.Contains("retry: false", authorityOptions, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", authorityBoundary, StringComparison.Ordinal);
        Assert.Contains("isMarketplaceAuthorityInvalidation(event.data, scope)", authorityBoundary, StringComparison.Ordinal);
        Assert.Contains("Realtime is an invalidation trigger only", chatHook, StringComparison.Ordinal);
        Assert.Contains("queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })", chatHook, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", chatHook, StringComparison.Ordinal);
        Assert.Contains("hasCurrentRequestAuthority && request", chatPage, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", chatPage, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", chatPage, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs4E()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-frontend-completion-timeline-audit-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S4-E",
            "## Audit Findings",
            "## Chronology Findings",
            "## Lifecycle-Consistency Findings",
            "## Presentation Findings",
            "## Realtime Findings",
            "## Cache Findings",
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

        Assert.Contains("PASS F-S4-E", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-E", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-E", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-E", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S4-E", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
