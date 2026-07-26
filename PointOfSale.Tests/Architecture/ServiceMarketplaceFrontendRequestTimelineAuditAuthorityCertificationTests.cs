namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendRequestTimelineAuditAuthorityCertificationTests
{
    [Fact]
    public void RequestTimelineAudit_MustUseBackendAuditTimelineBoundaryOnly()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");
        var types = ReadRepoFile("PointOfSale.Web/src/marketplace/types/index.ts");
        var timelineAuthority = ReadRepoFile("PointOfSale.Web/src/marketplace/timelineAuthority.ts");
        var chatPage = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ChatPage.tsx");

        Assert.Contains("auditTimeline = BuildRequestAuditTimeline(request)", controller, StringComparison.Ordinal);
        Assert.Contains("request.AcceptedAt", controller, StringComparison.Ordinal);
        Assert.Contains("request.RejectedAt", controller, StringComparison.Ordinal);
        Assert.Contains("request.CompletedByProviderAt", controller, StringComparison.Ordinal);
        Assert.Contains("request.CompletedByBuyerAt", controller, StringComparison.Ordinal);
        Assert.Contains("request.CancelledAt", controller, StringComparison.Ordinal);

        Assert.Contains("ServiceRequestAuditEvent", types, StringComparison.Ordinal);
        Assert.Contains("deriveRequestAuditTimelinePresentation", timelineAuthority, StringComparison.Ordinal);
        Assert.Contains("seenIds.has(eventId)", timelineAuthority, StringComparison.Ordinal);
        Assert.Contains("timestamp < lastTimestamp", timelineAuthority, StringComparison.Ordinal);
        Assert.Contains("requiredEventsByStatus[currentStatus]", timelineAuthority, StringComparison.Ordinal);
        Assert.Contains("allowedEventsByStatus[currentStatus]", timelineAuthority, StringComparison.Ordinal);

        Assert.Contains("deriveCompletionTimelineAuditPresentation(request, completionState, currentUserId)", chatPage, StringComparison.Ordinal);
        Assert.DoesNotContain("new Date(request.createdAt)", chatPage, StringComparison.Ordinal);
    }

    [Fact]
    public void RealtimeTimelineAudit_MustInvalidateAndRefetchInsteadOfAppendingTimelineTruth()
    {
        var chatHook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");

        Assert.Contains("Realtime is an invalidation trigger only", chatHook, StringComparison.Ordinal);
        Assert.Contains("queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) })", chatHook, StringComparison.Ordinal);
        Assert.Contains("queryClient.resetQueries({ queryKey: marketplaceKeys.chat(requestId, authorityScope)", chatHook, StringComparison.Ordinal);
        Assert.Contains("queryClient.invalidateQueries({ queryKey: marketplaceKeys.request(requestId, authorityScope) })", chatHook, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData<ChatMessage[]>", chatHook, StringComparison.Ordinal);
        Assert.DoesNotContain("(old = []) => [...old, message]", chatHook, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs2E()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-frontend-request-timeline-audit-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S2-E",
            "## Timeline Findings",
            "## Lifecycle-History Findings",
            "## Actor-Attribution Findings",
            "## Timestamp Findings",
            "## Consistency Findings",
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

        Assert.Contains("PASS F-S2-E", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S2-E", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S2-E", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S2-E", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S2-E", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
