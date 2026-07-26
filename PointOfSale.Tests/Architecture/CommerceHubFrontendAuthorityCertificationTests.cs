namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubFrontendAuthorityCertificationTests
{
    [Fact]
    public void CommerceHubFoundationFinalAuthorityGraph_MustRemainBackendOwnedFailClosedAndRefetchOnly()
    {
        var posSessionController = ReadRepoFile("PointOfSale.API/Controllers/AuthController.cs");
        var posAuthStore = ReadRepoFile("PointOfSale.Web/src/store/authStore.js");
        var adminOrdersApi = ReadRepoFile("PointOfSale.Web/src/services/adminOrdersApi.js");
        var ordersPage = ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");
        var commerceHubSettings = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubSettings.jsx");
        var storefrontBuilder = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");
        var themeMarketplace = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/ThemeMarketplacePage.jsx");
        var telemetry = ReadRepoFile("PointOfSale.Web/src/services/telemetry.js");
        var findings = ReadRepoFile("docs/architecture/commercehub-foundation-final-certification-findings.md");

        Assert.True(
            CountOccurrences(
                posSessionController,
                "NavigationCapabilities = await BuildCommerceHubNavigationCapabilitiesAsync(user, cancellationToken)") >= 4,
            "Registration, login, current-session, and refresh responses must all derive CommerceHub vendor capabilities from current backend authority.");

        Assert.Contains("removeStoredValue(STORAGE_KEYS.TENANT_ID)", posAuthStore, StringComparison.Ordinal);
        Assert.DoesNotContain("setStoredValue(STORAGE_KEYS.TENANT_ID", posAuthStore, StringComparison.Ordinal);
        Assert.Contains("useAuthStore.getState().user?.tenantId", adminOrdersApi, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage.getItem('tenantId')", adminOrdersApi, StringComparison.Ordinal);
        Assert.Contains("const authorityScope = ['pos-admin-orders', user?.tenantId ?? 'anonymous', user?.id ?? 'anonymous']", ordersPage, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholderData: (previousData) => previousData", ordersPage, StringComparison.Ordinal);
        Assert.Contains("{ordersQuery.isError ? (", ordersPage, StringComparison.Ordinal);

        Assert.Contains("setProducts([])", commerceHubSettings, StringComparison.Ordinal);
        Assert.Contains("setListingRows([])", commerceHubSettings, StringComparison.Ordinal);
        Assert.Contains("await api.put('/admin/commercehub/update', payload)", commerceHubSettings, StringComparison.Ordinal);
        Assert.Contains("await api.post('/admin/commercehub/publish', payload)", commerceHubSettings, StringComparison.Ordinal);
        Assert.Contains("await api.delete('/admin/commercehub/unpublish'", commerceHubSettings, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(commerceHubSettings, "await fetchData()") >= 3,
            "CommerceHub listing mutations must converge through current backend reads.");
        Assert.DoesNotContain("updateVariantListingInState", commerceHubSettings, StringComparison.Ordinal);

        Assert.Contains("const tenantId = currentUser?.tenantId || ''", storefrontBuilder, StringComparison.Ordinal);
        Assert.Contains("const tenantId = user?.tenantId || ''", themeMarketplace, StringComparison.Ordinal);
        Assert.Contains("useAuthStore.getState().user", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage.getItem('tenantId')", storefrontBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage.getItem('tenantId')", themeMarketplace, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage.getItem(STORAGE_KEYS.TENANT_ID)", telemetry, StringComparison.Ordinal);

        foreach (var marker in new[]
                 {
                     "## Session Integration Findings",
                     "## Actor Findings",
                     "## Route And Navigation Findings",
                     "## Scope-Isolation Findings",
                     "## Cache Findings",
                     "## Realtime Findings",
                     "## Failure Findings",
                     "## Adversarial Findings",
                     "## Explicit Answers"
                 })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CommerceHubNavigationAuthority_MustDeriveMenusRoutesAndActionsFromCurrentBackendCapabilities()
    {
        var posSessionController = ReadRepoFile("PointOfSale.API/Controllers/AuthController.cs");
        var customerProfileDto = ReadRepoFile("PointOfSale.API/DTOs/Customers/CustomerAuthDtos.cs");
        var customerAuthService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var superAdminSessionDto = ReadRepoFile("PointOfSale.API/SuperAdmin/DTOs/SuperAdminLoginResponse.cs");
        var superAdminAuthController = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminAuthController.cs");
        var customerAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/auth/customer-session-authority.ts");
        var customerHeader = ReadRepoFile("PointOfSale.CommerceHub/src/components/layout/Header.tsx");
        var customerDashboard = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerDashboardPage.tsx");
        var customerRoutes = ReadRepoFile("PointOfSale.CommerceHub/src/router/index.tsx");
        var customerGuard = ReadRepoFile("PointOfSale.CommerceHub/src/router/CustomerRouteGuards.tsx");
        var vendorAuthority = ReadRepoFile("PointOfSale.Web/src/auth/posVendorSessionAuthority.js");
        var vendorSidebar = ReadRepoFile("PointOfSale.Web/src/components/Sidebar.jsx");
        var vendorGuard = ReadRepoFile("PointOfSale.Web/src/components/CommerceHubVendorRouteGuard.jsx");
        var superAdminAuthority = ReadRepoFile("PointOfSale.Web/src/super-admin/auth/superAdminAuth.types.ts");
        var superAdminLayout = ReadRepoFile("PointOfSale.Web/src/super-admin/layout/SuperAdminLayout.tsx");
        var superAdminGuard = ReadRepoFile("PointOfSale.Web/src/super-admin/auth/SuperAdminCapabilityRoute.tsx");
        var appRoutes = ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var findings = ReadRepoFile("docs/architecture/commercehub-navigation-authority-certification-findings.md");

        Assert.Contains("BuildCommerceHubNavigationCapabilitiesAsync", posSessionController, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorListingAllowedAsync", posSessionController, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorOrderOperationAllowedAsync", posSessionController, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorRepaymentAllowedAsync", posSessionController, StringComparison.Ordinal);
        Assert.Contains("\"commercehub.analytics.view\"", posSessionController, StringComparison.Ordinal);
        Assert.Contains("\"commercehub.storefront.manage\"", posSessionController, StringComparison.Ordinal);
        Assert.Contains("\"commercehub.storefront.cms.manage\"", posSessionController, StringComparison.Ordinal);
        Assert.Contains("NavigationCapabilities", customerProfileDto, StringComparison.Ordinal);
        Assert.Contains("\"customer.orders.view\"", customerAuthService, StringComparison.Ordinal);
        Assert.Contains("NavigationCapabilities", superAdminSessionDto, StringComparison.Ordinal);
        Assert.Contains("\"super-admin.marketplace.govern\"", superAdminAuthController, StringComparison.Ordinal);

        Assert.Contains("CUSTOMER_NAVIGATION_CAPABILITIES", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("customerHasNavigationCapability", customerHeader, StringComparison.Ordinal);
        Assert.Contains("enabled: canViewOrders", customerDashboard, StringComparison.Ordinal);
        Assert.Contains("{canViewRefunds ?", customerDashboard, StringComparison.Ordinal);
        Assert.Contains("RequireCustomerNavigationCapability", customerRoutes, StringComparison.Ordinal);
        Assert.Contains("return <Navigate to=\"/\" replace />", customerGuard, StringComparison.Ordinal);

        Assert.Contains("COMMERCEHUB_VENDOR_NAVIGATION_CAPABILITIES", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("hasCommerceHubVendorNavigationCapability", vendorSidebar, StringComparison.Ordinal);
        Assert.DoesNotContain("requiredRoles: ['Owner', 'Admin', 'Accountant']", vendorSidebar, StringComparison.Ordinal);
        Assert.Contains("return <Navigate to=\"/\" replace />", vendorGuard, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorRouteGuard capability=\"commercehub.catalog.manage\"", appRoutes, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorRouteGuard capability=\"commercehub.orders.view\"", appRoutes, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorRouteGuard capability=\"commercehub.finance.view\"", appRoutes, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorRouteGuard capability=\"commercehub.payments.view\"", appRoutes, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorRouteGuard capability=\"commercehub.analytics.view\"", appRoutes, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorRouteGuard capability=\"commercehub.storefront.manage\"", appRoutes, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorRouteGuard capability=\"commercehub.storefront.cms.manage\"", appRoutes, StringComparison.Ordinal);

        Assert.Contains("SUPER_ADMIN_NAVIGATION_CAPABILITIES", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("session.navigationCapabilities.length === SUPER_ADMIN_NAVIGATION_CAPABILITIES.size", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("hasSuperAdminNavigationCapability", superAdminLayout, StringComparison.Ordinal);
        Assert.Contains("return <Navigate to=\"/super-admin/finance\" replace />", superAdminGuard, StringComparison.Ordinal);
        Assert.Contains("SuperAdminCapabilityRoute capability=\"super-admin.users.govern\"", appRoutes, StringComparison.Ordinal);
        Assert.Contains("SuperAdminCapabilityRoute capability=\"super-admin.marketplace.operate\"", appRoutes, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "PASS F-M0-F",
            "## Customer Navigation Findings",
            "## Vendor Navigation Findings",
            "## Super Admin Navigation Findings",
            "## Capability Findings",
            "## Refresh Findings",
            "## Cache Findings",
            "## Actor-Transition Findings",
            "## Cross-Tab Findings",
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
    }

    [Fact]
    public void CommerceHubActorResolutionPresentation_MustDeriveFromCurrentBackendAuthorityOnly()
    {
        var customerAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/auth/customer-session-authority.ts");
        var customerStore = ReadRepoFile("PointOfSale.CommerceHub/src/store/customer-auth-store.ts");
        var customerBootstrap = ReadRepoFile("PointOfSale.CommerceHub/src/providers/CustomerSessionBootstrap.tsx");
        var customerLogin = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerLoginPage.tsx");
        var posAuthority = ReadRepoFile("PointOfSale.Web/src/auth/posVendorSessionAuthority.js");
        var posBootstrap = ReadRepoFile("PointOfSale.Web/src/components/AuthBootstrap.jsx");
        var superAdminTypes = ReadRepoFile("PointOfSale.Web/src/super-admin/auth/superAdminAuth.types.ts");
        var superAdminStore = ReadRepoFile("PointOfSale.Web/src/super-admin/auth/superAdminAuthStore.ts");
        var superAdminBootstrap = ReadRepoFile("PointOfSale.Web/src/super-admin/auth/useSuperAdminSessionBootstrap.ts");
        var superAdminRoute = ReadRepoFile("PointOfSale.Web/src/super-admin/auth/SuperAdminProtectedRoute.tsx");
        var superAdminClient = ReadRepoFile("PointOfSale.Web/src/super-admin/core/superAdminApiClient.ts");
        var findings = ReadRepoFile("docs/architecture/commercehub-actor-resolution-authority-certification-findings.md");

        Assert.Contains("isValidGuid(id)", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("isValidEmail(email)", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("isSameCustomerActorAuthority", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("customerAuthorityRevision", customerStore, StringComparison.Ordinal);
        Assert.Contains("latestHydrationRequestId", customerStore, StringComparison.Ordinal);
        Assert.Contains("previousCustomerId !== customer.id", customerBootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"online\", handleHistoryNavigation)", customerBootstrap, StringComparison.Ordinal);
        Assert.Contains("isSameCustomerActorAuthority(response.customer, customer)", customerLogin, StringComparison.Ordinal);

        Assert.Contains("POS_ACTOR_ROLES", posAuthority, StringComparison.Ordinal);
        Assert.Contains("GUID_PATTERN.test(id)", posAuthority, StringComparison.Ordinal);
        Assert.Contains("!POS_ACTOR_ROLES.has(role)", posAuthority, StringComparison.Ordinal);
        Assert.Contains("sessionRevisionChanged", posBootstrap, StringComparison.Ordinal);
        Assert.Contains("queryClient.clear()", posBootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('focus', handleRouteLifecycle)", posBootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('online', handleRouteLifecycle)", posBootstrap, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('visibilitychange', handleVisibility)", posBootstrap, StringComparison.Ordinal);

        Assert.Contains("isValidGuid(session.superAdminId)", superAdminTypes, StringComparison.Ordinal);
        Assert.Contains("isValidEmail(session.email)", superAdminTypes, StringComparison.Ordinal);
        Assert.Contains("superAdminAuthorityRevision", superAdminStore, StringComparison.Ordinal);
        Assert.Contains("getSuperAdminAuthorityRevision", superAdminBootstrap, StringComparison.Ordinal);
        Assert.Contains("authorityRevisionAtStart === getSuperAdminAuthorityRevision()", superAdminRoute, StringComparison.Ordinal);
        Assert.Contains("Super Admin actor changed during refresh.", superAdminClient, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "PASS F-M0-D",
            "## Actor Identity Findings",
            "## Actor-Type Findings",
            "## Scope Findings",
            "## Bootstrap Findings",
            "## Transition Findings",
            "## Cache Findings",
            "## Cross-Tab Findings",
            "## Route Findings",
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
    }

    [Fact]
    public void CommerceHubRouteAuthority_MustRevalidateBackendTruthAndFailClosedAcrossNavigationLifecycle()
    {
        var customerStore = ReadRepoFile("PointOfSale.CommerceHub/src/store/customer-auth-store.ts");
        var customerBootstrap = ReadRepoFile("PointOfSale.CommerceHub/src/providers/CustomerSessionBootstrap.tsx");
        var customerGuard = ReadRepoFile("PointOfSale.CommerceHub/src/router/CustomerRouteGuards.tsx");
        var customerLogin = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerLoginPage.tsx");
        var posStore = ReadRepoFile("PointOfSale.Web/src/store/authStore.js");
        var posBootstrap = ReadRepoFile("PointOfSale.Web/src/components/AuthBootstrap.jsx");
        var posAuthority = ReadRepoFile("PointOfSale.Web/src/auth/posVendorSessionAuthority.js");
        var posLogin = ReadRepoFile("PointOfSale.Web/src/pages/Login.jsx");
        var vendorSettings = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubSettings.jsx");
        var superAdminRoute = ReadRepoFile("PointOfSale.Web/src/super-admin/auth/SuperAdminProtectedRoute.tsx");
        var superAdminLogin = ReadRepoFile("PointOfSale.Web/src/super-admin/auth/SuperAdminLoginPage.tsx");
        var findings = ReadRepoFile("docs/architecture/commercehub-route-authority-certification-findings.md");

        Assert.Contains("beginRouteAuthorityValidation", customerStore, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"popstate\", handleHistoryNavigation)", customerBootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"pageshow\", handlePageShow)", customerBootstrap, StringComparison.Ordinal);
        Assert.Contains("void revalidateSession(true)", customerBootstrap, StringComparison.Ordinal);
        Assert.Contains("resolveCustomerRouteRedirect", customerGuard, StringComparison.Ordinal);
        Assert.Contains("resolveCustomerRouteRedirect(redirectState?.redirectTo)", customerLogin, StringComparison.Ordinal);

        Assert.Contains("beginRouteAuthorityValidation", posStore, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('popstate', handleRouteLifecycle)", posBootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('pageshow', handlePageShow)", posBootstrap, StringComparison.Ordinal);
        Assert.Contains("withholdRoutePresentation: true", posBootstrap, StringComparison.Ordinal);
        Assert.Contains("resolvePosRouteRedirect", posAuthority, StringComparison.Ordinal);
        Assert.Contains("resolvePosRouteRedirect(", posLogin, StringComparison.Ordinal);
        Assert.Contains("const currentVendorId = currentUser?.tenantId || ''", vendorSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage.getItem('tenantId')", vendorSettings, StringComparison.Ordinal);

        Assert.Contains("isRouteAuthorityValidating", superAdminRoute, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('popstate', handleRouteLifecycle)", superAdminRoute, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('pageshow', handlePageShow)", superAdminRoute, StringComparison.Ordinal);
        Assert.Contains("url.origin !== window.location.origin", superAdminLogin, StringComparison.Ordinal);
        Assert.Contains("url.pathname.startsWith('/super-admin/login')", superAdminLogin, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "PASS F-M0-E",
            "## Protected-Route Findings",
            "## Deep-Link Findings",
            "## Refresh Findings",
            "## History Findings",
            "## Redirect Findings",
            "## Actor-Transition Findings",
            "## Authority-Loss Findings",
            "## Route-State Findings",
            "## Cross-Tab Findings",
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
    }

    [Fact]
    public void CustomerCommerceHubFrontend_MustFailClosedForNonEgpCachedCurrency()
    {
        var currencyAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/utils/commerceHubCurrencyAuthority.ts");
        var formatter = ReadRepoFile("PointOfSale.CommerceHub/src/utils/formatCurrency.ts");
        var cartTypes = ReadRepoFile("PointOfSale.CommerceHub/src/types/cart.ts");
        var cartStore = ReadRepoFile("PointOfSale.CommerceHub/src/store/cart-store.ts");

        Assert.Contains("COMMERCEHUB_AUTHORITY_CURRENCY = \"EGP\"", currencyAuthority, StringComparison.Ordinal);
        Assert.Contains("resolveCommerceHubDisplayCurrency", formatter, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_UNSUPPORTED_CURRENCY_LABEL", formatter, StringComparison.Ordinal);
        Assert.Contains("return COMMERCEHUB_UNSUPPORTED_CURRENCY_LABEL;", formatter, StringComparison.Ordinal);

        Assert.Contains("const DEFAULT_CURRENCY = COMMERCEHUB_AUTHORITY_CURRENCY;", cartTypes, StringComparison.Ordinal);
        Assert.Contains("currency: DEFAULT_CURRENCY", cartTypes, StringComparison.Ordinal);
        Assert.Contains("currency: COMMERCEHUB_AUTHORITY_CURRENCY", cartStore, StringComparison.Ordinal);
        Assert.Contains("const storeCurrency = COMMERCEHUB_AUTHORITY_CURRENCY;", cartStore, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerCommerceHubCheckout_MustRefreshBackendAuthorityBeforeCreatingOrders()
    {
        var checkoutPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx");
        var checkoutTypes = ReadRepoFile("PointOfSale.CommerceHub/src/types/checkout.ts");
        var cartPricing = ReadRepoFile("PointOfSale.CommerceHub/src/cart/useCommerceHubCartPricing.ts");

        Assert.Contains("await saveCart({ persistIfAvailableOnly: false })", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("cartService.priceCart(buildCommerceHubCartPricingRequest(authoritativePayload", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("authoritativePaymentMethod?.isAvailable", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("buildCommerceHubCheckoutRequest", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("expectedUnitPrice", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("cartVersion", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("apiError.code !== \"CART_MISMATCH\"", cartPricing, StringComparison.Ordinal);
        Assert.Contains("await useCartStore.getState().saveCart({ persistIfAvailableOnly: false })", cartPricing, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerCommerceHubSessionPresentation_MustDeriveFromCurrentBackendAuthorityOnly()
    {
        var sessionAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/auth/customer-session-authority.ts");
        var authStore = ReadRepoFile("PointOfSale.CommerceHub/src/store/customer-auth-store.ts");
        var authService = ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-auth-service.ts");
        var loginPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerLoginPage.tsx");
        var bootstrap = ReadRepoFile("PointOfSale.CommerceHub/src/providers/CustomerSessionBootstrap.tsx");
        var httpClient = ReadRepoFile("PointOfSale.CommerceHub/src/api/http-client.ts");
        var settingsPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerSettingsPage.tsx");
        var resetPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerResetPasswordPage.tsx");

        Assert.Contains("deriveCustomerSessionProfileAuthority", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("value.isEmailVerified !== true", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("deriveCustomerAuthResponseAuthority", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("Date.parse(value.expiresAt) <= Date.now()", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER_SESSION_AUTHORITY_LOST_EVENT", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER_SESSION_LOGOUT_STORAGE_KEY", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("/api/commercehub/checkout", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("/api/marketplace/reviews", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("/review-context", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("Storage is an optional revocation signal; backend revalidation remains authoritative", sessionAuthority, StringComparison.Ordinal);

        Assert.Contains("deriveCustomerAuthResponseAuthority(response)", authStore, StringComparison.Ordinal);
        Assert.Contains("set(clearedSessionState)", authStore, StringComparison.Ordinal);
        Assert.Contains("customerAuthService.getProfile(signal)", authStore, StringComparison.Ordinal);
        Assert.DoesNotContain("persist(", authStore, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", authStore, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", authStore, StringComparison.Ordinal);

        Assert.Contains("httpClient.post<unknown>(customerAuthEndpoints.login", authService, StringComparison.Ordinal);
        Assert.Contains("requireCustomerAuthResponseAuthority(response.data)", authService, StringComparison.Ordinal);
        Assert.Contains("httpClient.get<unknown>(customerAuthEndpoints.me", authService, StringComparison.Ordinal);
        Assert.Contains("requireCustomerProfileAuthority(response.data)", authService, StringComparison.Ordinal);
        Assert.Contains("httpClient.put<unknown>(customerAuthEndpoints.updateProfile", authService, StringComparison.Ordinal);

        Assert.Contains("const customer = await customerAuthService.getProfile()", loginPage, StringComparison.Ordinal);
        Assert.Contains("setSession({ expiresAt: response.expiresAt, customer })", loginPage, StringComparison.Ordinal);
        Assert.Contains("clearSession()", loginPage, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", loginPage, StringComparison.Ordinal);

        Assert.Contains("CUSTOMER_SESSION_AUTHORITY_LOST_EVENT", bootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"focus\", handleHistoryNavigation)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener(\"visibilitychange\", handleVisibility)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("window.setInterval(() => void revalidateSession()", bootstrap, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData(customerQueryKeys.profile(), event", bootstrap, StringComparison.Ordinal);

        Assert.Contains("status === 401 || status === 403", httpClient, StringComparison.Ordinal);
        Assert.Contains("isCustomerSessionAuthorityEndpoint(url)", httpClient, StringComparison.Ordinal);
        Assert.Contains("dispatchCustomerSessionAuthorityLost()", httpClient, StringComparison.Ordinal);

        Assert.Contains("broadcastCustomerLogout()", settingsPage, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", settingsPage, StringComparison.Ordinal);
        Assert.Contains("broadcastCustomerLogout()", resetPage, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", resetPage, StringComparison.Ordinal);
    }

    [Fact]
    public void PosAdminCommerceHubVendorSessionPresentation_MustDeriveFromCurrentBackendAuthorityOnly()
    {
        var sessionAuthority = ReadRepoFile("PointOfSale.Web/src/auth/posVendorSessionAuthority.js");
        var authStore = ReadRepoFile("PointOfSale.Web/src/store/authStore.js");
        var loginPage = ReadRepoFile("PointOfSale.Web/src/pages/Login.jsx");
        var bootstrap = ReadRepoFile("PointOfSale.Web/src/components/AuthBootstrap.jsx");
        var apiClient = ReadRepoFile("PointOfSale.Web/src/services/api.js");
        var storageScope = ReadRepoFile("PointOfSale.Web/src/utils/storageScope.js");
        var financePage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubFinance.jsx");
        var earningsPage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubEarnings.jsx");
        var paymentsPage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubPayments.jsx");
        var settingsPage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubSettings.jsx");
        var layout = ReadRepoFile("PointOfSale.Web/src/components/Layout.jsx");
        var app = ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var queryClient = ReadRepoFile("PointOfSale.Web/src/queryClient.js");

        Assert.Contains("derivePosVendorSessionUserAuthority", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("deriveCommerceHubVendorEligibilityPresentation", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("canSubmitPaymentEvidence === true", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("requirePosVendorSessionUserAuthority", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("POS_VENDOR_SESSION_AUTHORITY_LOST_EVENT", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("POS_VENDOR_SESSION_LOGOUT_STORAGE_KEY", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("isCommerceHubVendorSessionAuthorityEndpoint", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("/admin/commercehub", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("/commercehub/finance", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("/commercehub/earnings", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("/commercehub/payments", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("Storage is an optional terminal signal; backend revalidation remains authoritative", sessionAuthority, StringComparison.Ordinal);

        Assert.Contains("derivePosVendorSessionUserAuthority(user)", authStore, StringComparison.Ordinal);
        Assert.DoesNotContain("persist(", authStore, StringComparison.Ordinal);
        Assert.DoesNotContain("partialize", authStore, StringComparison.Ordinal);
        Assert.Contains("removeStoredValue(STORAGE_KEYS.AUTH_STORAGE)", authStore, StringComparison.Ordinal);

        Assert.Contains("await api.post('/auth/login'", loginPage, StringComparison.Ordinal);
        Assert.Contains("const sessionResponse = await api.get('/auth/session')", loginPage, StringComparison.Ordinal);
        Assert.Contains("requirePosVendorSessionUserAuthority(sessionResponse.data?.user ?? null)", loginPage, StringComparison.Ordinal);
        Assert.DoesNotContain("const { user } = response.data", loginPage, StringComparison.Ordinal);

        Assert.Contains("requirePosVendorSessionUserAuthority(response.data?.user ?? null)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("POS_VENDOR_SESSION_AUTHORITY_LOST_EVENT", bootstrap, StringComparison.Ordinal);
        Assert.Contains("POS_VENDOR_SESSION_LOGOUT_STORAGE_KEY", bootstrap, StringComparison.Ordinal);
        Assert.Contains("clearAuthority", bootstrap, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries({ queryKey: ['commercehub-vendor-session'] })", bootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('storage', handleStorage)", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("void bootstrapSession()", bootstrap.Substring(bootstrap.IndexOf("const handleStorage", StringComparison.Ordinal)), StringComparison.Ordinal);

        Assert.Contains("'/api/auth/refresh'", apiClient, StringComparison.Ordinal);
        Assert.Contains("const sessionResponse = await axios.get('/api/auth/session'", apiClient, StringComparison.Ordinal);
        Assert.Contains("requirePosVendorSessionUserAuthority(sessionResponse.data?.user ?? null)", apiClient, StringComparison.Ordinal);
        Assert.Contains("error.response?.status === 401 || error.response?.status === 403", apiClient, StringComparison.Ordinal);
        Assert.Contains("isCommerceHubVendorSessionAuthorityEndpoint(originalRequest.url)", apiClient, StringComparison.Ordinal);
        Assert.Contains("dispatchPosVendorSessionAuthorityLost()", apiClient, StringComparison.Ordinal);

        Assert.Contains("return useAuthStore.getState().user || null", storageScope, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage.getItem(STORAGE_KEYS.AUTH_STORAGE)", storageScope, StringComparison.Ordinal);

        Assert.Contains("commercehub-vendor-session", financePage, StringComparison.Ordinal);
        Assert.Contains("...vendorSessionScope, 'commercehub-finance'", financePage, StringComparison.Ordinal);
        Assert.Contains("commercehub-vendor-session", earningsPage, StringComparison.Ordinal);
        Assert.Contains("...vendorSessionScope, 'commercehub-earnings-dashboard'", earningsPage, StringComparison.Ordinal);
        Assert.Contains("commercehub-vendor-session", paymentsPage, StringComparison.Ordinal);
        Assert.Contains("...vendorSessionScope, 'commercehub-payments-dashboard'", paymentsPage, StringComparison.Ordinal);
        Assert.Contains("deriveCommerceHubVendorEligibilityPresentation(currentUser)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("!canManageCatalog", settingsPage, StringComparison.Ordinal);
        Assert.Contains("deriveCommerceHubVendorEligibilityPresentation(user)", earningsPage, StringComparison.Ordinal);
        Assert.Contains("!canManagePayoutMethods", earningsPage, StringComparison.Ordinal);
        Assert.Contains("deriveCommerceHubVendorEligibilityPresentation(user, {", paymentsPage, StringComparison.Ordinal);
        Assert.Contains("!canSubmitPayment", paymentsPage, StringComparison.Ordinal);

        Assert.Contains("broadcastPosVendorLogout()", layout, StringComparison.Ordinal);
        Assert.Contains("export const queryClient", queryClient, StringComparison.Ordinal);
        Assert.Contains("import { queryClient } from './queryClient'", app, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubVendorIdentityAuthority_MustDeriveProfileStoreAndOwnershipPresentationFromCurrentBackendScope()
    {
        var vendorIdentityAuthority = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js");
        var vendorIdentityTests = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.test.ts");
        var storefrontStatus = ReadRepoFile("PointOfSale.Web/src/components/StorefrontStatusCard.jsx");
        var commerceHubSettings = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubSettings.jsx");
        var storefrontBuilder = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");
        var cmsPagesList = ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx");
        var cmsPageEditor = ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx");
        var financePage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubFinance.jsx");
        var earningsPage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubEarnings.jsx");
        var paymentsPage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubPayments.jsx");
        var findings = ReadRepoFile("docs/architecture/commercehub-vendor-identity-authority-certification-findings.md");

        Assert.Contains("resolveCurrentVendorIdentityAuthority", vendorIdentityAuthority, StringComparison.Ordinal);
        Assert.Contains("isCurrentVendorTenantAuthority", vendorIdentityAuthority, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentVendorScopedDto", vendorIdentityAuthority, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentVendorScopedArray", vendorIdentityAuthority, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentStoreIdentityPresentation", vendorIdentityAuthority, StringComparison.Ordinal);
        Assert.Contains("return []", vendorIdentityAuthority, StringComparison.Ordinal);

        Assert.Contains("rejects foreign vendor, store, ownership, finance, and analytics DTO scope", vendorIdentityTests, StringComparison.Ordinal);
        Assert.Contains("fails closed for mixed ownership arrays", vendorIdentityTests, StringComparison.Ordinal);
        Assert.Contains("validates store identity presentation", vendorIdentityTests, StringComparison.Ordinal);

        Assert.Contains("deriveCurrentStoreIdentityPresentation(response.data, user)", storefrontStatus, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentVendorScopedArray(productsRes || [], currentUser)", commerceHubSettings, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentVendorScopedArray(listingsRes.data || [], currentUser)", commerceHubSettings, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentVendorScopedDto(kpisRes.data || null, currentUser)", commerceHubSettings, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentStoreIdentityPresentation(adminConfig, currentUser)", storefrontBuilder, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentVendorScopedDto(", storefrontBuilder, StringComparison.Ordinal);

        Assert.Contains("useAuthStore((state) => state.user?.tenantId || '')", cmsPagesList, StringComparison.Ordinal);
        Assert.Contains("useAuthStore((state) => state.user?.tenantId || '')", cmsPageEditor, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage.getItem('tenantSlug')", cmsPagesList, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage.getItem('tenantSlug')", cmsPageEditor, StringComparison.Ordinal);

        Assert.Contains("const dashboard = deriveCurrentVendorScopedDto(dashboardQuery.data, user)", financePage, StringComparison.Ordinal);
        Assert.Contains("const statementMeta = deriveCurrentVendorScopedDto(statementQuery.data, user)", financePage, StringComparison.Ordinal);
        Assert.Contains("const methods = deriveCurrentVendorScopedArray(methodsQuery.data ?? [], user)", earningsPage, StringComparison.Ordinal);
        Assert.Contains("paymentMethods: deriveCurrentVendorScopedArray", paymentsPage, StringComparison.Ordinal);
        Assert.Contains("paymentHistory: deriveCurrentVendorScopedArray", paymentsPage, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "PASS M1-A",
            "## Vendor Identity Findings",
            "## Store Identity Findings",
            "## Ownership Findings",
            "## Bootstrap Findings",
            "## Mutation Findings",
            "## Cache Findings",
            "## Transition Findings",
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
    }

    [Fact]
    public void SuperAdminCommerceHubSessionPresentation_MustDeriveFromCurrentBackendAuthorityOnly()
    {
        var sessionTypes = ReadRepoFile("PointOfSale.Web/src/super-admin/auth/superAdminAuth.types.ts");
        var authStore = ReadRepoFile("PointOfSale.Web/src/super-admin/auth/superAdminAuthStore.ts");
        var loginPage = ReadRepoFile("PointOfSale.Web/src/super-admin/auth/SuperAdminLoginPage.tsx");
        var protectedRoute = ReadRepoFile("PointOfSale.Web/src/super-admin/auth/SuperAdminProtectedRoute.tsx");
        var bootstrap = ReadRepoFile("PointOfSale.Web/src/super-admin/auth/useSuperAdminSessionBootstrap.ts");
        var apiClient = ReadRepoFile("PointOfSale.Web/src/super-admin/core/superAdminApiClient.ts");
        var layout = ReadRepoFile("PointOfSale.Web/src/super-admin/layout/SuperAdminLayout.tsx");

        Assert.Contains("deriveSuperAdminSessionAuthority", sessionTypes, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminSessionAuthority", sessionTypes, StringComparison.Ordinal);
        Assert.Contains("session.role === 'SuperAdmin'", sessionTypes, StringComparison.Ordinal);
        Assert.Contains("session.tokenType === 'Bearer'", sessionTypes, StringComparison.Ordinal);
        Assert.Contains("Number.isInteger(session.tokenVersion)", sessionTypes, StringComparison.Ordinal);
        Assert.Contains("parsed > Date.now()", sessionTypes, StringComparison.Ordinal);

        Assert.Contains("SUPER_ADMIN_SESSION_LOGOUT_STORAGE_KEY", authStore, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminSessionAuthority(session)", authStore, StringComparison.Ordinal);
        Assert.Contains("purgeSuperAdminRuntimeAuthority()", authStore, StringComparison.Ordinal);
        Assert.Contains("superAdminQueryClient.clear()", authStore, StringComparison.Ordinal);
        Assert.DoesNotContain("persist(", authStore, StringComparison.Ordinal);
        Assert.DoesNotContain("partialize", authStore, StringComparison.Ordinal);

        Assert.Contains("await loginSuperAdmin", loginPage, StringComparison.Ordinal);
        Assert.Contains("const session = await getSuperAdminSession()", loginPage, StringComparison.Ordinal);
        Assert.DoesNotContain("setSession(await loginSuperAdmin", loginPage, StringComparison.Ordinal);

        Assert.Contains("SUPER_ADMIN_SESSION_LOGOUT_STORAGE_KEY", protectedRoute, StringComparison.Ordinal);
        Assert.Contains("logout({ broadcast: false })", protectedRoute, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('focus', handleRouteLifecycle)", protectedRoute, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('online', handleRouteLifecycle)", protectedRoute, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('visibilitychange', handleVisibility)", protectedRoute, StringComparison.Ordinal);

        Assert.Contains("localStorage.removeItem(SUPER_ADMIN_AUTH_STORAGE_KEY)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("getSuperAdminSession()", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage.getItem(SUPER_ADMIN_AUTH_STORAGE_KEY)", bootstrap, StringComparison.Ordinal);

        Assert.Contains("refreshSuperAdminSession({ tokenVersion })", apiClient, StringComparison.Ordinal);
        Assert.Contains(".then(() => getSuperAdminSession())", apiClient, StringComparison.Ordinal);
        Assert.Contains("failing closed", apiClient, StringComparison.Ordinal);
        Assert.Contains("statusCode === 401 || statusCode === 403", apiClient, StringComparison.Ordinal);
        Assert.Contains("redirectToSuperAdminLogin()", apiClient, StringComparison.Ordinal);

        Assert.Contains("logoutSuperAdmin()", layout, StringComparison.Ordinal);
        Assert.Contains("logout()", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubVendorGovernanceAuthority_MustDerivePresentationAndActionsFromCurrentBackendAuthorityOnly()
    {
        var governanceAuthority = ReadRepoFile("PointOfSale.Web/src/super-admin/marketplace-governance/marketplaceGovernanceAuthority.ts");
        var governanceService = ReadRepoFile("PointOfSale.Web/src/super-admin/marketplace-governance/marketplaceGovernanceService.ts");
        var governancePage = ReadRepoFile("PointOfSale.Web/src/super-admin/marketplace-governance/MarketplaceGovernancePage.tsx");
        var governanceTests = ReadRepoFile("PointOfSale.Web/src/super-admin/marketplace-governance/marketplaceGovernanceAuthority.test.ts");
        var superAdminClient = ReadRepoFile("PointOfSale.Web/src/super-admin/core/superAdminApiClient.ts");
        var posAuthority = ReadRepoFile("PointOfSale.Web/src/auth/posVendorSessionAuthority.js");
        var findings = ReadRepoFile("docs/architecture/commercehub-vendor-governance-authority-certification-findings.md");

        Assert.Contains("AUTHORITY_OWNER = 'SQL marketplace governance authority'", governanceAuthority, StringComparison.Ordinal);
        Assert.Contains("deriveMarketplaceGovernanceDashboardAuthority", governanceAuthority, StringComparison.Ordinal);
        Assert.Contains("deriveMarketplaceGovernanceListingAuthority", governanceAuthority, StringComparison.Ordinal);
        Assert.Contains("deriveMarketplaceGovernanceActionAuthority", governanceAuthority, StringComparison.Ordinal);
        Assert.Contains("VENDOR_GOVERNANCE_STATUSES", governanceAuthority, StringComparison.Ordinal);
        Assert.Contains("TENANT_STATUSES", governanceAuthority, StringComparison.Ordinal);
        Assert.Contains("VISIBILITY_STATES", governanceAuthority, StringComparison.Ordinal);
        Assert.Contains("value.entityId !== expected.entityId", governanceAuthority, StringComparison.Ordinal);
        Assert.Contains("value.action !== expected.action", governanceAuthority, StringComparison.Ordinal);
        Assert.Contains("vendorGovernanceStatus === 'Suspended'", governanceAuthority, StringComparison.Ordinal);
        Assert.Contains("tenantStatus === 'Suspended'", governanceAuthority, StringComparison.Ordinal);

        Assert.Contains("deriveMarketplaceGovernanceDashboardAuthority(response.data)", governanceService, StringComparison.Ordinal);
        Assert.Contains("deriveMarketplaceGovernanceListingsAuthority(response.data.listings)", governanceService, StringComparison.Ordinal);
        Assert.Contains("deriveMarketplaceGovernanceActionAuthority(response.data, { entityId: listingId, action: 'suppress' })", governanceService, StringComparison.Ordinal);
        Assert.Contains("deriveMarketplaceGovernanceActionAuthority(response.data, { entityId: listingId, action: 'restore' })", governanceService, StringComparison.Ordinal);
        Assert.Contains("deriveMarketplaceGovernanceActionAuthority(response.data, { entityId: tenantId, action: 'suspend' })", governanceService, StringComparison.Ordinal);
        Assert.Contains("deriveMarketplaceGovernanceActionAuthority(response.data, { entityId: tenantId, action: 'restore' })", governanceService, StringComparison.Ordinal);

        Assert.DoesNotContain("placeholderData: (previousData) => previousData", governancePage, StringComparison.Ordinal);
        Assert.Contains("const [selectedListingId, setSelectedListingId]", governancePage, StringComparison.Ordinal);
        Assert.Contains("listings.find((listing) => listing.listingId === selectedListingId) ?? null", governancePage, StringComparison.Ordinal);
        Assert.Contains("const dashboard = dashboardQuery.isFetching ? undefined : dashboardQuery.data", governancePage, StringComparison.Ordinal);
        Assert.Contains("const actionDisabled = listingsQuery.isFetching || isActionRunning", governancePage, StringComparison.Ordinal);
        Assert.Contains("disabled={actionDisabled}", governancePage, StringComparison.Ordinal);

        Assert.Contains("statusCode === 401 || statusCode === 403", superAdminClient, StringComparison.Ordinal);
        Assert.Contains("redirectToSuperAdminLogin()", superAdminClient, StringComparison.Ordinal);
        Assert.Contains("deriveCommerceHubVendorLifecyclePresentation", posAuthority, StringComparison.Ordinal);
        Assert.Contains("lifecyclePermitsCapabilities", posAuthority, StringComparison.Ordinal);

        Assert.Contains("requires backend SQL authority owner", governanceTests, StringComparison.Ordinal);
        Assert.Contains("rejects fabricated, contradictory, and alias governance listing presentation", governanceTests, StringComparison.Ordinal);
        Assert.Contains("requires action responses to match the initiated backend action and entity", governanceTests, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "PASS M1-F",
            "## Governance Findings",
            "## Action Findings",
            "## Presentation Findings",
            "## Transition Findings",
            "## Consistency Findings",
            "## Bootstrap Findings",
            "## Cache Findings",
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
    }

    [Fact]
    public void CommerceHubAuthorityScopeIsolation_MustKeepRuntimeDomainsAndActorsDisjoint()
    {
        var app = ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var queryClients = ReadRepoFile("PointOfSale.Web/src/queryClient.js");
        var queryBoundaries = ReadRepoFile("PointOfSale.Web/src/components/RuntimeQueryBoundaries.jsx");
        var posBootstrap = ReadRepoFile("PointOfSale.Web/src/components/AuthBootstrap.jsx");
        var storageScope = ReadRepoFile("PointOfSale.Web/src/utils/storageScope.js");
        var superAdminStore = ReadRepoFile("PointOfSale.Web/src/super-admin/auth/superAdminAuthStore.ts");
        var marketplaceBoundary = ReadRepoFile("PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");
        var marketplaceKeys = ReadRepoFile("PointOfSale.Web/src/marketplace/queryKeys.ts");
        var findings = ReadRepoFile("docs/architecture/commercehub-authority-scope-isolation-certification-findings.md");

        Assert.Contains("export const queryClient = createRuntimeQueryClient()", queryClients, StringComparison.Ordinal);
        Assert.Contains("export const marketplaceQueryClient = createRuntimeQueryClient()", queryClients, StringComparison.Ordinal);
        Assert.Contains("export const superAdminQueryClient = createRuntimeQueryClient()", queryClients, StringComparison.Ordinal);
        Assert.Contains("QueryClientProvider client={marketplaceQueryClient}", queryBoundaries, StringComparison.Ordinal);
        Assert.Contains("QueryClientProvider client={superAdminQueryClient}", queryBoundaries, StringComparison.Ordinal);
        Assert.Contains("<MarketplaceQueryBoundary><MarketplaceAuthorityBoundary>", app, StringComparison.Ordinal);
        Assert.Contains("<SuperAdminQueryBoundary><SuperAdminProtectedRoute", app, StringComparison.Ordinal);

        Assert.Contains("useAuthStore.subscribe((state, previousState)", posBootstrap, StringComparison.Ordinal);
        Assert.Contains("queryClient.clear()", posBootstrap, StringComparison.Ordinal);
        Assert.Contains("invalidatePosRuntimeCache({ reason: 'actor-scope-changed'", posBootstrap, StringComparison.Ordinal);
        Assert.Contains("revalidateCommerceHubVendorAuthority", posBootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(POS_VENDOR_SESSION_AUTHORITY_LOST_EVENT, revalidateCommerceHubVendorAuthority)", posBootstrap, StringComparison.Ordinal);
        Assert.Contains("event.key && watchedKeys.has(event.key)", posBootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("!event.key || watchedKeys.has(event.key)", posBootstrap, StringComparison.Ordinal);

        Assert.Contains("return useAuthStore.getState().user || null", storageScope, StringComparison.Ordinal);
        Assert.Contains("persistedUser?.tenantId", storageScope, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage.getItem(STORAGE_KEYS.TENANT_ID)", storageScope, StringComparison.Ordinal);

        Assert.Contains("superAdminQueryClient.clear()", superAdminStore, StringComparison.Ordinal);
        Assert.Contains("query.queryKey[1] !== scope", marketplaceBoundary, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.scope(scope)", marketplaceBoundary, StringComparison.Ordinal);
        Assert.Contains("...marketplaceKeys.scope(scope)", marketplaceKeys, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "PASS F-M0-G",
            "## Customer/Vendor Isolation Findings",
            "## Vendor/Super Admin Isolation Findings",
            "## Tenant Isolation Findings",
            "## CommerceHub/POS Isolation Findings",
            "## CommerceHub/Service Marketplace Isolation Findings",
            "## Cache Findings",
            "## Realtime Findings",
            "## Actor-Transition Findings",
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
    }

    [Fact]
    public void PosAdminCommerceHubFrontend_MustNotRenderOrSubmitNonEgpCommerceHubCurrency()
    {
        var webCurrencyAuthority = ReadRepoFile("PointOfSale.Web/src/utils/commerceHubCurrencyAuthority.js");
        var financePage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubFinance.jsx");
        var paymentsPage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubPayments.jsx");
        var paymentsApi = ReadRepoFile("PointOfSale.Web/src/services/commerceHubPaymentsApi.js");
        var settingsPage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubSettings.jsx");

        Assert.Contains("COMMERCEHUB_AUTHORITY_CURRENCY = 'EGP'", webCurrencyAuthority, StringComparison.Ordinal);
        Assert.Contains("resolveCommerceHubDisplayCurrency", financePage, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_UNSUPPORTED_CURRENCY_LABEL", financePage, StringComparison.Ordinal);
        Assert.Contains("resolveCommerceHubDisplayCurrency", paymentsPage, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_UNSUPPORTED_CURRENCY_LABEL", paymentsPage, StringComparison.Ordinal);
        Assert.Contains("formData.append('currency', COMMERCEHUB_AUTHORITY_CURRENCY)", paymentsApi, StringComparison.Ordinal);
        Assert.DoesNotContain("formData.append('currency', currency)", paymentsApi, StringComparison.Ordinal);

        Assert.Contains("variant.isCommerceHubCurrencyEligible !== true", settingsPage, StringComparison.Ordinal);
        Assert.Contains("disabled={isSaving || !state.categoryId || isCurrencyBlocked || !canManageCatalog}", settingsPage, StringComparison.Ordinal);
        Assert.Contains("CommerceHub currently supports EGP products only.", settingsPage, StringComparison.Ordinal);
        Assert.Contains("currencyCode=\"EGP\"", settingsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassF2FrontendAuthority()
    {
        var certification = ReadRepoFile("docs/architecture/frontend-commercehub-authority-certification-findings.md");
        var customerSessionCertification = ReadRepoFile("docs/architecture/commercehub-customer-session-authority-certification-findings.md");

        foreach (var marker in new[]
        {
            "PASS F2",
            "## Discovery Findings",
            "## Cart Findings",
            "## Checkout Findings",
            "## Order Findings",
            "## Refund Findings",
            "## Publication Findings",
            "## Financial Findings",
            "## Currency Findings",
            "## Cache Findings",
            "## DevTools Findings",
            "## Runtime Hardening Performed",
            "## Regression Freezes Added",
            "## Architecture Updates",
            "## Remaining Debt",
            "## Required Answers"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }

        var vendorSessionCertification = ReadRepoFile("docs/architecture/commercehub-vendor-session-authority-certification-findings.md");
        var superAdminSessionCertification = ReadRepoFile("docs/architecture/commercehub-super-admin-session-authority-certification-findings.md");

        Assert.Contains("PASS F-M0-A CommerceHub Customer Session Authority", customerSessionCertification, StringComparison.Ordinal);
        Assert.Contains("## Session Identity Findings", customerSessionCertification, StringComparison.Ordinal);
        Assert.Contains("## Adversarial Findings", customerSessionCertification, StringComparison.Ordinal);
        Assert.Contains("## Explicit Answers", customerSessionCertification, StringComparison.Ordinal);
        Assert.Contains("PASS F-M0-B CommerceHub Vendor Session Authority", vendorSessionCertification, StringComparison.Ordinal);
        Assert.Contains("## Vendor Identity Findings", vendorSessionCertification, StringComparison.Ordinal);
        Assert.Contains("## Runtime Hardening Performed", vendorSessionCertification, StringComparison.Ordinal);
        Assert.Contains("## Explicit Answers", vendorSessionCertification, StringComparison.Ordinal);
        Assert.Contains("PASS F-M0-C CommerceHub Super Admin Session Authority", superAdminSessionCertification, StringComparison.Ordinal);
        Assert.Contains("## Admin Identity Findings", superAdminSessionCertification, StringComparison.Ordinal);
        Assert.Contains("## Runtime Hardening Performed", superAdminSessionCertification, StringComparison.Ordinal);
        Assert.Contains("## Explicit Answers", superAdminSessionCertification, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string marker) =>
        source.Split(marker, StringSplitOptions.None).Length - 1;

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
