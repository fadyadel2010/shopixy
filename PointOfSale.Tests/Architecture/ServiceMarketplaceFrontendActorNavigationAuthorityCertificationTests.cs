namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendActorNavigationAuthorityCertificationTests
{
    [Fact]
    public void MarketplaceCacheAndHttp_MustRemainBoundToCurrentBackendSessionAuthority()
    {
        var apiClient = ReadRepoFile("PointOfSale.Web/src/core/apiClient.ts");
        var authority = ReadRepoFile("PointOfSale.Web/src/marketplace/authority.ts");
        var queryKeys = ReadRepoFile("PointOfSale.Web/src/marketplace/queryKeys.ts");
        var boundary = ReadRepoFile("PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");

        Assert.Contains("from '../services/api'", apiClient, StringComparison.Ordinal);
        Assert.DoesNotContain("axios.create", apiClient, StringComparison.Ordinal);
        Assert.Contains("user?.tenantId", authority, StringComparison.Ordinal);
        Assert.Contains("user?.id ?? user?.userId", authority, StringComparison.Ordinal);
        Assert.Contains("staleTime: 0", authority, StringComparison.Ordinal);
        Assert.Contains("refetchOnWindowFocus: 'always'", authority, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.scope(scope)", queryKeys, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", boundary, StringComparison.Ordinal);
        Assert.Contains("predicate: (query)", boundary, StringComparison.Ordinal);
        Assert.Contains("useLayoutEffect", boundary, StringComparison.Ordinal);
        Assert.Contains("visibilitychange", boundary, StringComparison.Ordinal);
        Assert.Contains("AUTHORITY_REVALIDATION_INTERVAL_MS", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceActorGate_MustExcludeSuperAdminAndIncompleteSessionIdentity()
    {
        var authority = ReadRepoFile("PointOfSale.Web/src/marketplace/authority.ts");
        var boundary = ReadRepoFile("PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");
        var sidebar = ReadRepoFile("PointOfSale.Web/src/components/Sidebar.jsx");
        var notificationBell = ReadRepoFile("PointOfSale.Web/src/components/MarketplaceNotificationBell.jsx");

        Assert.Contains("role !== 'superadmin'", authority, StringComparison.Ordinal);
        Assert.Contains("tenantId !== null", authority, StringComparison.Ordinal);
        Assert.Contains("userId !== null", authority, StringComparison.Ordinal);
        Assert.Contains("useIsServiceMarketplaceWorkflowActor", boundary, StringComparison.Ordinal);
        Assert.Contains("<Navigate to=\"/\" replace />", boundary, StringComparison.Ordinal);
        Assert.Contains("requiresMarketplaceActor: true", sidebar, StringComparison.Ordinal);
        Assert.Contains("isServiceMarketplaceWorkflowActor(user, true)", sidebar, StringComparison.Ordinal);
        Assert.Contains("if (!isMarketplaceActor)", notificationBell, StringComparison.Ordinal);
        Assert.Contains("return null", notificationBell, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceTabsAndRoutes_MustDeriveVisibilityFromCurrentProfileAuthority()
    {
        var app = ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var shell = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/MarketplaceShell.tsx");
        var serviceDetails = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx");
        var profileTab = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/ProfileTab.tsx");

        Assert.Contains("<MarketplaceAuthorityBoundary><MarketplaceShell /></MarketplaceAuthorityBoundary>", app, StringComparison.Ordinal);
        Assert.Contains("<MarketplaceAuthorityBoundary><ServiceDetailsPage /></MarketplaceAuthorityBoundary>", app, StringComparison.Ordinal);
        Assert.Contains("<MarketplaceAuthorityBoundary><ChatPage /></MarketplaceAuthorityBoundary>", app, StringComparison.Ordinal);
        Assert.Contains("profile?.hasMarketplaceProfile === true", shell, StringComparison.Ordinal);
        Assert.Contains("profile.isActive === true", shell, StringComparison.Ordinal);
        Assert.Contains("normalizeTab(requestedTab", shell, StringComparison.Ordinal);
        Assert.Contains("profileAuthorityFailed", shell, StringComparison.Ordinal);
        Assert.Contains("const canRequestService = service.requestEntry.canRequest", serviceDetails, StringComparison.Ordinal);
        Assert.DoesNotContain("profile.profileId === service.providerProfileId", serviceDetails, StringComparison.Ordinal);
        Assert.Contains("Marketplace profile unavailable", profileTab, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceChat_MustSeparateHistoryVisibilityFromSendAuthority()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");
        var chatHook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");
        var chatPage = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ChatPage.tsx");

        Assert.Contains("canViewChatHistory = request.CanViewChatHistory(userId)", controller, StringComparison.Ordinal);
        Assert.Contains("canSendChat = request.AllowsInteraction()", controller, StringComparison.Ordinal);
        Assert.Contains("isCurrentChatRealtimeEvent(message, identity)", chatHook, StringComparison.Ordinal);
        Assert.Contains("acceptsRealtimeEvents = false", chatHook, StringComparison.Ordinal);
        Assert.Contains("await newConnection.invoke('JoinConversation', requestId)", chatHook, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.request(requestId, authorityScope)", chatHook, StringComparison.Ordinal);
        Assert.Contains("enabled: !!identity?.canViewChatHistory", chatHook, StringComparison.Ordinal);
        Assert.Contains("canViewChatHistory", chatPage, StringComparison.Ordinal);
        Assert.Contains("canSendChat ? <form", chatPage, StringComparison.Ordinal);
        Assert.DoesNotContain("optimistic", chatPage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarketplaceMutations_MustRemainBoundToTheInitiatingActorScope()
    {
        foreach (var relativePath in new[]
        {
            "PointOfSale.Web/src/marketplace/hooks/useAcceptRequest.ts",
            "PointOfSale.Web/src/marketplace/hooks/useActivateService.ts",
            "PointOfSale.Web/src/marketplace/hooks/useCancelRequest.ts",
            "PointOfSale.Web/src/marketplace/hooks/useConfirmCompletion.ts",
            "PointOfSale.Web/src/marketplace/hooks/useCreateReview.ts",
            "PointOfSale.Web/src/marketplace/hooks/useCreateService.ts",
            "PointOfSale.Web/src/marketplace/hooks/useDeactivateService.ts",
            "PointOfSale.Web/src/marketplace/hooks/useMarkCompleted.ts",
            "PointOfSale.Web/src/marketplace/hooks/useRequestService.ts"
        })
        {
            var source = ReadRepoFile(relativePath);
            Assert.Contains("const authorityScope = useMarketplaceAuthorityScope()", source, StringComparison.Ordinal);
            Assert.Contains("marketplaceKeys.scope(authorityScope)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("queryKey: marketplaceKeys.all", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MarketplaceListsAndRealtime_MustFailClosedOnStaleOrForeignAuthority()
    {
        var myRequests = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx");
        var incoming = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx");
        var myServices = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyServicesTab.tsx");
        var notificationBell = ReadRepoFile("PointOfSale.Web/src/components/MarketplaceNotificationBell.jsx");

        Assert.Contains("const requests = isError ? [] : requestData", myRequests, StringComparison.Ordinal);
        Assert.Contains("const requests = isError ? [] : requestData", incoming, StringComparison.Ordinal);
        Assert.Contains("deriveProviderServicePresentation", myServices, StringComparison.Ordinal);
        Assert.Contains("isError || isFetching", myServices, StringComparison.Ordinal);
        Assert.Contains("deriveServiceRequestLifecyclePresentation", myRequests, StringComparison.Ordinal);
        Assert.Contains("lifecycle.canViewChatHistory", myRequests, StringComparison.Ordinal);
        Assert.Contains("deriveServiceRequestLifecyclePresentation", incoming, StringComparison.Ordinal);
        Assert.Contains("lifecycle.canViewChatHistory", incoming, StringComparison.Ordinal);
        Assert.Contains("actorScope", notificationBell, StringComparison.Ordinal);
        Assert.Contains("setUnseenRequests([])", notificationBell, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs0()
    {
        var certification = ReadRepoFile("docs/architecture/service-marketplace-frontend-actor-navigation-authority-certification-findings.md");

        foreach (var marker in new[]
        {
            "PASS F-S0",
            "## Actor Findings",
            "## Navigation Findings",
            "## Route Findings",
            "## Menu Findings",
            "## Tab Findings",
            "## Screen Visibility Findings",
            "## Cache Findings",
            "## Realtime Findings",
            "## Tenant Isolation Findings",
            "## History Findings",
            "## Failure-Handling Findings",
            "## Adversarial Findings",
            "## Runtime Hardening Performed",
            "## Exact Runtime Files Modified",
            "## Regression Freezes Added",
            "## Architecture Updates",
            "## Explicit Answers"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
