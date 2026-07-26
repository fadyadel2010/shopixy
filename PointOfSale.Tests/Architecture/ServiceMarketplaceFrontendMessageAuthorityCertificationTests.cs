namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendMessageAuthorityCertificationTests
{
    [Fact]
    public void MessageHistory_MustRemainBackendOrderedAndPresentationOnly()
    {
        var chatService = ReadRepoFile("PointOfSale.Infrastructure/Services/ChatService.cs");
        var chatAuthority = ReadRepoFile("PointOfSale.Web/src/marketplace/chatAuthority.ts");
        var chatPage = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ChatPage.tsx");
        var chatMessage = ReadRepoFile("PointOfSale.Web/src/marketplace/components/ChatMessage.tsx");

        Assert.Contains(".OrderByDescending(m => m.CreatedAt)", chatService, StringComparison.Ordinal);
        Assert.Contains(".ThenByDescending(m => m.Id)", chatService, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(m => m.CreatedAt)", chatService, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(m => m.Id)", chatService, StringComparison.Ordinal);

        Assert.Contains("messageIds.has(id)", chatAuthority, StringComparison.Ordinal);
        Assert.Contains("serviceRequestId !== identity.requestId", chatAuthority, StringComparison.Ordinal);
        Assert.Contains("senderRole !== expectedSenderRole", chatAuthority, StringComparison.Ordinal);
        Assert.Contains("createdTimestamp < previousTimestamp", chatAuthority, StringComparison.Ordinal);
        Assert.Contains("createdTimestamp === previousTimestamp && id <= previousMessageId", chatAuthority, StringComparison.Ordinal);
        Assert.Contains("isoTimestampPattern", chatAuthority, StringComparison.Ordinal);
        Assert.Contains("normalizeAttachmentFileName(row.attachmentFileName)", chatAuthority, StringComparison.Ordinal);

        Assert.Contains("message.senderUserId === chatIdentity.currentUserId", chatPage, StringComparison.Ordinal);
        Assert.Contains("formatTime(message.createdAt", chatMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Date.now()", chatPage, StringComparison.Ordinal);
        Assert.DoesNotContain("new Date().toISOString()", chatPage, StringComparison.Ordinal);
    }

    [Fact]
    public void RealtimeAndSendRaces_MustRefetchInsteadOfCreatingMessageTruth()
    {
        var chatHook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");
        var chatPage = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ChatPage.tsx");

        Assert.Contains("Realtime is an invalidation trigger only", chatHook, StringComparison.Ordinal);
        Assert.Contains("deriveChatHistoryPageAuthority(payload, identity, pageParam)", chatHook, StringComparison.Ordinal);
        Assert.Contains("mergeChatHistoryPagesAuthority", chatHook, StringComparison.Ordinal);
        Assert.Contains("await queryClient.resetQueries", chatHook, StringComparison.Ordinal);
        Assert.Contains("finally", chatHook, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData<ChatMessage[]>", chatHook, StringComparison.Ordinal);
        Assert.DoesNotContain("(old = []) => [...old, message]", chatHook, StringComparison.Ordinal);

        Assert.Contains("sendInFlightRef.current", chatPage, StringComparison.Ordinal);
        Assert.Contains("sendInFlightRef.current = true", chatPage, StringComparison.Ordinal);
        Assert.Contains("sendInFlightRef.current = false", chatPage, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageCaches_MustRemainActorTenantRequestScopedAndFailClosed()
    {
        var queryKeys = ReadRepoFile("PointOfSale.Web/src/marketplace/queryKeys.ts");
        var authorityBoundary = ReadRepoFile("PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");
        var chatHook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");

        Assert.Contains("chat: (requestId: string, scope", queryKeys, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.scope(scope)", queryKeys, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", authorityBoundary, StringComparison.Ordinal);
        Assert.Contains("historyAuthorityFailed = chatQuery.isError || chatQuery.isFetchNextPageError", chatHook, StringComparison.Ordinal);
        Assert.Contains("authorityFailed: historyAuthorityFailed", chatHook, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs3B()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-frontend-message-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S3-B",
            "## Message Identity Findings",
            "## Sender Findings",
            "## Ordering Findings",
            "## Timestamp Findings",
            "## Visibility Findings",
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

        Assert.Contains("PASS F-S3-B", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-B", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-B", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-B", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-B", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
