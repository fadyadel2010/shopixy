namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendAttachmentAuthorityCertificationTests
{
    [Fact]
    public void AttachmentIdentityMetadataAndUploads_MustRemainBackendDerived()
    {
        var attachmentAuthority = ReadRepoFile("PointOfSale.Web/src/marketplace/attachmentAuthority.ts");
        var chatAuthority = ReadRepoFile("PointOfSale.Web/src/marketplace/chatAuthority.ts");
        var chatPage = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ChatPage.tsx");

        Assert.Contains("attachmentId !== undefined && attachmentId !== id", chatAuthority, StringComparison.Ordinal);
        Assert.Contains("normalizeAttachmentFileName(row.attachmentFileName)", chatAuthority, StringComparison.Ordinal);
        Assert.Contains("deriveChatAttachmentUploadAuthority", attachmentAuthority, StringComparison.Ordinal);
        Assert.Contains("normalizeGuid(segments[3]) !== identity.requestId", attachmentAuthority, StringComparison.Ordinal);
        Assert.Contains("normalizeGuid(segments[4]) !== identity.currentUserId", attachmentAuthority, StringComparison.Ordinal);
        Assert.Contains("segments[5] !== fileName", attachmentAuthority, StringComparison.Ordinal);

        Assert.Contains("marketplaceApi.uploadChatAttachment(sendIdentity.requestId, selectedFile)", chatPage, StringComparison.Ordinal);
        Assert.Contains("deriveChatAttachmentUploadAuthority(payload, sendIdentity)", chatPage, StringComparison.Ordinal);
        Assert.DoesNotContain("response.data?.objectKey", chatPage, StringComparison.Ordinal);
    }

    [Fact]
    public void AttachmentVisibilityPreviewAndDownloads_MustReenterCurrentAuthority()
    {
        var chatMessage = ReadRepoFile("PointOfSale.Web/src/marketplace/components/ChatMessage.tsx");
        var chatPage = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ChatPage.tsx");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");

        Assert.Contains("buildAttachmentDownloadPath(message.attachmentId)", chatMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("const isImage =", chatMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", chatMessage, StringComparison.Ordinal);
        Assert.Contains("CanUserViewConversationAsync", controller, StringComparison.Ordinal);
        Assert.Contains("Response.Headers.CacheControl = \"no-store, private, max-age=0\"", controller, StringComparison.Ordinal);

        Assert.Contains("attachmentIntentScopeRef", chatPage, StringComparison.Ordinal);
        Assert.Contains("setSelectedFile(null)", chatPage, StringComparison.Ordinal);
        Assert.Contains("sendOperationIdRef.current += 1", chatPage, StringComparison.Ordinal);
        Assert.Contains("sendOperationIdRef.current === sendOperationId", chatPage, StringComparison.Ordinal);
    }

    [Fact]
    public void AttachmentCachesRealtimeAndActorTransitions_MustNotCreateTruth()
    {
        var chatHook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");
        var queryKeys = ReadRepoFile("PointOfSale.Web/src/marketplace/queryKeys.ts");
        var authorityBoundary = ReadRepoFile("PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");

        Assert.Contains("Realtime is an invalidation trigger only", chatHook, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData<ChatMessage[]>", chatHook, StringComparison.Ordinal);
        Assert.Contains("chat: (requestId: string, scope", queryKeys, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", authorityBoundary, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs3C()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-frontend-attachment-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S3-C",
            "## Attachment Identity Findings",
            "## Metadata Findings",
            "## Visibility Findings",
            "## Download Findings",
            "## Preview Findings",
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

        Assert.Contains("PASS F-S3-C", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-C", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-C", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-C", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S3-C", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
