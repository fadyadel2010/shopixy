namespace PointOfSale.Tests.Architecture;

public sealed class StoreAdministrationEntryNavigationAuthorityCertificationTests
{
    [Fact]
    public void StoreAdministrationRoutes_MustUseOwningBackendAuthorization()
    {
        var app = ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var guard = "CommerceHubVendorRouteGuard capability=";

        Assert.Contains($"{guard}\"commercehub.orders.view\"", app, StringComparison.Ordinal);
        Assert.Contains($"{guard}\"commercehub.payments.view\"", app, StringComparison.Ordinal);
        Assert.Contains($"{guard}\"commercehub.analytics.view\"", app, StringComparison.Ordinal);
        Assert.Contains("<Route element={<PosRoleRouteGuard roles={['Owner', 'Admin']} />}>", app, StringComparison.Ordinal);

        foreach (var route in new[]
                 {
                     "ecommerce/shipping-methods",
                     "ecommerce/coupons",
                     "ecommerce/promotions",
                     "ecommerce/smart-offers",
                     "ecommerce/orders",
                     "ecommerce/blocked-phones",
                     "ecommerce/online-customers",
                     "ecommerce/commerce-ai",
                     "ecommerce/payment-methods",
                     "ecommerce/manual-payments",
                     "storefront",
                     "storefront/builder",
                     "storefront/themes",
                     "storefront/theme",
                     "storefront/versions",
                     "storefront/cms/pages",
                     "storefront/cms/navigation",
                     "storefront/cms/footer",
                     "ecommerce/auth-providers",
                     "analytics/funnels",
                     "analytics/attribution",
                     "settings/analytics",
                     "admin/shipping/setup",
                     "admin/shipping/shipments",
                     "admin/shipping/health",
                     "ecommerce/whatsapp",
                     "ecommerce/cart-recovery",
                     "ecommerce/abandoned-checkouts",
                 })
        {
            Assert.Contains($"path=\"{route}\"", app, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void StoreAdministrationSidebar_MustBePresentationOnlyFromBackendCapabilities()
    {
        var sidebar = ReadRepoFile("PointOfSale.Web/src/components/Sidebar.jsx");

        Assert.Contains("STORE_ADMIN_NAVIGATION_CAPABILITY_BY_PATH", sidebar, StringComparison.Ordinal);
        Assert.Contains("canAccessCommerceHubVendorNavigationCapability(user, capability)", sidebar, StringComparison.Ordinal);
        Assert.Contains("['/ecommerce/coupons', 'commercehub.storefront.manage']", sidebar, StringComparison.Ordinal);
        Assert.Contains("['/ecommerce/promotions', 'commercehub.storefront.manage']", sidebar, StringComparison.Ordinal);
        Assert.Contains("['/ecommerce/commerce-ai', 'commercehub.storefront.manage']", sidebar, StringComparison.Ordinal);
        Assert.Contains("['/ecommerce/whatsapp', 'commercehub.orders.view']", sidebar, StringComparison.Ordinal);
        Assert.Contains("['/ecommerce/abandoned-checkouts', 'commercehub.orders.view']", sidebar, StringComparison.Ordinal);
        Assert.Contains("/storefront/builder?page=home", sidebar, StringComparison.Ordinal);
        Assert.Contains("/storefront/cms/navigation", sidebar, StringComparison.Ordinal);
        Assert.Contains("/storefront/cms/pages", sidebar, StringComparison.Ordinal);
        Assert.Contains("/storefront/cms/footer", sidebar, StringComparison.Ordinal);
        Assert.Contains("requiredRoles: ['Owner', 'Admin']", sidebar, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage.getItem('tenantId')", sidebar, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendSession_MustOwnStoreAdministrationNavigationAuthority()
    {
        var authController = ReadRepoFile("PointOfSale.API/Controllers/AuthController.cs");
        var authority = ReadRepoFile("PointOfSale.Web/src/auth/posVendorSessionAuthority.js");

        Assert.Contains("BuildCommerceHubNavigationCapabilitiesAsync", authController, StringComparison.Ordinal);
        Assert.Contains("capabilities.Add(\"commercehub.storefront.manage\")", authController, StringComparison.Ordinal);
        Assert.Contains("capabilities.Add(\"commercehub.storefront.cms.manage\")", authController, StringComparison.Ordinal);
        Assert.Contains("capabilities.Add(\"commercehub.orders.view\")", authController, StringComparison.Ordinal);
        Assert.Contains("capabilities.Add(\"commercehub.payments.view\")", authController, StringComparison.Ordinal);
        Assert.Contains("capabilities.Add(\"commercehub.analytics.view\")", authController, StringComparison.Ordinal);

        Assert.Contains("derivePosVendorSessionUserAuthority", authority, StringComparison.Ordinal);
        Assert.Contains("navigationCapabilities.some((capability) => !COMMERCEHUB_VENDOR_NAVIGATION_CAPABILITIES.has(capability))", authority, StringComparison.Ordinal);
        Assert.Contains("new Set(navigationCapabilities).size !== navigationCapabilities.length", authority, StringComparison.Ordinal);
        Assert.Contains("capability === 'commercehub.storefront.manage'", authority, StringComparison.Ordinal);
        Assert.Contains("return lifecycle.blocksStorefront !== true", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreAdministrationAuthorityLossEndpoints_MustTriggerBackendSessionRevalidation()
    {
        var authority = ReadRepoFile("PointOfSale.Web/src/auth/posVendorSessionAuthority.js");
        var api = ReadRepoFile("PointOfSale.Web/src/services/api.js");

        foreach (var endpoint in new[]
                 {
                     "/admin/store-config",
                     "/admin/commerce-ai",
                     "/admin/analytics",
                     "/admin/payment-methods",
                     "/admin/phones",
                     "/admin/online-customers",
                     "/admin/abandoned-checkouts",
                     "/admin/whatsapp",
                     "/admin/shipping",
                     "/admin/carriers",
                     "/admin/pages",
                     "/admin/cms",
                     "/admin/footer",
                     "/coupons",
                     "/promotions",
                     "/shippingmethods",
                 })
        {
            Assert.Contains(endpoint, authority, StringComparison.Ordinal);
        }

        Assert.Contains("dispatchPosVendorSessionAuthorityLost()", api, StringComparison.Ordinal);
        Assert.Contains("error.response?.status === 401 || error.response?.status === 403", api, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
