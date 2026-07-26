namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubVendorEarningsActionAuthorityCertificationTests
{
    [Fact]
    public void VendorEarningsActions_MustRemainBackendBoundAndFailClosed()
    {
        var earningsPage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubEarnings.jsx");
        var earningsApi = ReadRepoFile("PointOfSale.Web/src/services/commerceHubEarningsApi.js");
        var payoutController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubPayoutsController.cs");
        var payoutMethodsController = ReadRepoFile("PointOfSale.API/Controllers/VendorPayoutMethodsController.cs");

        Assert.Contains("createVendorPayoutMethod", earningsPage, StringComparison.Ordinal);
        Assert.Contains("currentEligibility?.canViewFinance !== true", earningsPage, StringComparison.Ordinal);
        Assert.Contains("!canManagePayoutMethods", earningsPage, StringComparison.Ordinal);
        Assert.Contains("queryClient.invalidateQueries", earningsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("createPayout", earningsPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("retryPayout", earningsPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("completePayout", earningsPage, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("api.post('/api/vendors/payout-methods', payload)", earningsApi, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/commercehub/payouts", earningsApi, StringComparison.Ordinal);

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", payoutMethodsController, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", payoutMethodsController, StringComparison.Ordinal);
        Assert.Contains("ResolveActorId()", payoutMethodsController, StringComparison.Ordinal);

        Assert.Contains("PAYOUT_EXECUTION_WORKFLOW_REQUIRED", payoutController, StringComparison.Ordinal);
        Assert.Contains("Direct CommerceHub payout execution mutation is disabled", payoutController, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorEarningsActionCache_MustBeScopedAndPurgedOnAuthorityTransitions()
    {
        var bootstrap = ReadRepoFile("PointOfSale.Web/src/components/AuthBootstrap.jsx");
        var earningsPage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubEarnings.jsx");

        Assert.Contains("'commercehub-vendor-session', user?.tenantId ?? 'anonymous', user?.id ?? 'anonymous'", earningsPage, StringComparison.Ordinal);
        Assert.Contains("root === 'commercehub-vendor-session'", bootstrap, StringComparison.Ordinal);
        Assert.Contains("queryClient.clear()", bootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('pageshow'", bootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('popstate'", bootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('online'", bootstrap, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
