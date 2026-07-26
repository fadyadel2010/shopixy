namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendRealtimeAuthorityCertificationTests
{
    [Fact]
    public void SignalR_MustRemainCurrentRequestInvalidationTransportOnly()
    {
        var chatHook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");

        Assert.Contains("if (!identity?.canSendChat || historyAuthorityFailed) return", chatHook, StringComparison.Ordinal);
        Assert.Contains("isCurrentChatRealtimeEvent(message, identity)", chatHook, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.chat(requestId, authorityScope)", chatHook, StringComparison.Ordinal);
        Assert.Contains("acceptsRealtimeEvents = false", chatHook, StringComparison.Ordinal);
        Assert.Contains("connectionAuthorityRef.current = null", chatHook, StringComparison.Ordinal);
        Assert.Contains("connectionAuthority.connection.state !== signalR.HubConnectionState.Connected", chatHook, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData<ChatMessage[]>", chatHook, StringComparison.Ordinal);
        Assert.DoesNotContain("(old = []) => [...old, message]", chatHook, StringComparison.Ordinal);
    }

    [Fact]
    public void Reconnect_MustRetireRecoveredTransportAndReenterBackendAuthority()
    {
        var chatHook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");
        var reconnectStart = chatHook.IndexOf("newConnection.onreconnected", StringComparison.Ordinal);
        var reconnectEnd = chatHook.IndexOf("newConnection.onclose", reconnectStart, StringComparison.Ordinal);
        var reconnectBlock = chatHook[reconnectStart..reconnectEnd];

        Assert.Contains("acceptsRealtimeEvents = false", reconnectBlock, StringComparison.Ordinal);
        Assert.Contains("newConnection.stop()", reconnectBlock, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.request(requestId, authorityScope)", reconnectBlock, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.chat(requestId, authorityScope)", reconnectBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("JoinConversation", reconnectBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("setIsConnected(true)", reconnectBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionStartup_MustNotInvalidateRequestAuthorityAndLoopTeardown()
    {
        var chatHook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");
        var startupStart = chatHook.IndexOf("void (async () =>", StringComparison.Ordinal);
        var startupEnd = chatHook.IndexOf("return () =>", startupStart, StringComparison.Ordinal);
        var startupBlock = chatHook[startupStart..startupEnd];

        Assert.Contains("await newConnection.invoke('JoinConversation', requestId)", startupBlock, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.chat(requestId, authorityScope)", startupBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("marketplaceKeys.request(requestId, authorityScope)", startupBlock, StringComparison.Ordinal);
        Assert.Contains("if (!acceptsRealtimeEvents)", startupBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void BroadcastChannel_MustBeScopedValidatedAndDerivative()
    {
        var authority = ReadRepoFile("PointOfSale.Web/src/marketplace/authority.ts");
        var boundary = ReadRepoFile("PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");

        Assert.Contains("isMarketplaceAuthorityInvalidation", authority, StringComparison.Ordinal);
        Assert.Contains("event.authorityScope === authorityScope", authority, StringComparison.Ordinal);
        Assert.Contains("authorityScope !== getMarketplaceAuthorityScope()", authority, StringComparison.Ordinal);
        Assert.Contains("channel.postMessage({ type: 'invalidate', authorityScope, serviceId })", authority, StringComparison.Ordinal);
        Assert.Contains("try {", authority, StringComparison.Ordinal);
        Assert.Contains("isMarketplaceAuthorityInvalidation(event.data, scope)", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void LifecycleCrossTabSignals_MustPublishOnlyScopedInvalidation()
    {
        foreach (var relativePath in new[]
        {
            "PointOfSale.Web/src/marketplace/hooks/useAcceptRequest.ts",
            "PointOfSale.Web/src/marketplace/hooks/useCancelRequest.ts",
            "PointOfSale.Web/src/marketplace/hooks/useConfirmCompletion.ts",
            "PointOfSale.Web/src/marketplace/hooks/useMarkCompleted.ts",
            "PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx",
            "PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx"
        })
        {
            var source = ReadRepoFile(relativePath);
            Assert.Contains("publishMarketplaceAuthorityInvalidation(authorityScope)", source, StringComparison.Ordinal);
            Assert.Contains("marketplaceKeys.scope(authorityScope)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("setQueryData", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs3D()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-frontend-realtime-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S3-D",
            "## Connection Findings",
            "## Subscription Findings",
            "## Reconnect Findings",
            "## Event Findings",
            "## BroadcastChannel Findings",
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

        Assert.Contains("PASS F-S3-D", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-D", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-D", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-D", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-D", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
