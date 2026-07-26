namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendChatHistoryAuthorityCertificationTests
{
    [Fact]
    public void HistoryPagination_MustUseBackendOwnedCompositeCursorWindows()
    {
        var chatInterface = ReadRepoFile("PointOfSale.Core/Interfaces/IChatService.cs");
        var chatService = ReadRepoFile("PointOfSale.Infrastructure/Services/ChatService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");
        var api = ReadRepoFile("PointOfSale.Web/src/marketplace/api/marketplaceApi.ts");

        Assert.Contains("Guid? beforeMessageId = null", chatInterface, StringComparison.Ordinal);
        Assert.Contains("m.Id == beforeMessageId.Value", chatService, StringComparison.Ordinal);
        Assert.Contains("m.CreatedAt == cursor.CreatedAt && m.Id.CompareTo(cursor.Id) < 0", chatService, StringComparison.Ordinal);
        Assert.DoesNotContain("m.CreatedAt < beforeTimestamp.Value", chatService, StringComparison.Ordinal);

        Assert.Contains("historyPageSize + 1", controller, StringComparison.Ordinal);
        Assert.Contains("nextBeforeMessageId = hasMore ? messages[0].Id", controller, StringComparison.Ordinal);
        Assert.Contains("beforeMessageId", api, StringComparison.Ordinal);
        Assert.DoesNotContain("beforeTimestamp", api, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryWindows_MustValidateEveryPageAndCompleteChronology()
    {
        var authority = ReadRepoFile("PointOfSale.Web/src/marketplace/chatAuthority.ts");
        var chatHook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");

        Assert.Contains("deriveChatHistoryPageAuthority", authority, StringComparison.Ordinal);
        Assert.Contains("beforeMessageId !== expectedBeforeMessageId", authority, StringComparison.Ordinal);
        Assert.Contains("nextBeforeMessageId !== items[0]?.id", authority, StringComparison.Ordinal);
        Assert.Contains("mergeChatHistoryPagesAuthority", authority, StringComparison.Ordinal);
        Assert.Contains("[...pages].reverse().flatMap", authority, StringComparison.Ordinal);
        Assert.Contains("deriveChatMessagesAuthority(", authority, StringComparison.Ordinal);

        Assert.Contains("useInfiniteQuery", chatHook, StringComparison.Ordinal);
        Assert.Contains("initialPageParam: null", chatHook, StringComparison.Ordinal);
        Assert.Contains("getNextPageParam", chatHook, StringComparison.Ordinal);
        Assert.Contains("fetchNextPage({ cancelRefetch: false })", chatHook, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", chatHook, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshRealtimeAndFailures_MustNotPreserveOrAppendHistoryTruth()
    {
        var chatHook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");
        var chatPage = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ChatPage.tsx");
        var queryKeys = ReadRepoFile("PointOfSale.Web/src/marketplace/queryKeys.ts");

        Assert.Contains("queryClient.resetQueries", chatHook, StringComparison.Ordinal);
        Assert.Contains("historyAuthorityFailed = chatQuery.isError || chatQuery.isFetchNextPageError", chatHook, StringComparison.Ordinal);
        Assert.Contains("authorityFailed: historyAuthorityFailed", chatHook, StringComparison.Ordinal);
        Assert.Contains("hasOlderMessages", chatPage, StringComparison.Ordinal);
        Assert.Contains("previousScrollHeight", chatPage, StringComparison.Ordinal);
        Assert.Contains("currentContainer.scrollTop", chatPage, StringComparison.Ordinal);
        Assert.Contains("chat: (requestId: string, scope", queryKeys, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", chatHook, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs3E()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-frontend-chat-history-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S3-E",
            "## History Identity Findings",
            "## Pagination Findings",
            "## Ordering Findings",
            "## Visibility Findings",
            "## Realtime Findings",
            "## Cache Findings",
            "## Actor-Transition Findings",
            "## History-Restoration Findings",
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

        Assert.Contains("PASS F-S3-E", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-E", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-E", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-E", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-E", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
