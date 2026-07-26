namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubVendorSuspensionCertificationTests
{
    [Fact]
    public void VendorSuspension_MustBeComputedFromCurrentDebtSnapshot()
    {
        var debt = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorDebtService.cs");
        var control = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorFinancialControlService.cs");

        Assert.Contains("IsSuspendedFromNewOrders", debt, StringComparison.Ordinal);
        Assert.Contains("isSuspended", debt, StringComparison.Ordinal);
        Assert.Contains("GetVendorsSuspendedFromNewOrdersAsync", control, StringComparison.Ordinal);
        Assert.Contains("GetVendorDebtSnapshotsAsync", control, StringComparison.Ordinal);
        Assert.Contains("snapshot.IsSuspendedFromNewOrders", control, StringComparison.Ordinal);
    }

    [Fact]
    public void Checkout_MustRejectFinanciallySuspendedVendorsBeforeOrderCreation()
    {
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("BeginTransactionAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", checkout, StringComparison.Ordinal);
        Assert.Contains("LoadCommerceHubListingsForCheckoutAuthorityAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("LoadTenantsForCheckoutAuthorityAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("EnsureCommerceIntakeAllowedAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("GetVendorsSuspendedFromNewOrdersAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("VENDOR_SUSPENDED_FOR_SETTLEMENT", checkout, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorOrderAcceptanceAllowedAsync", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void Discovery_MustSuppressFinanciallySuspendedVendorsAcrossPublicSurfaces()
    {
        var publicVisibility = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPublicVendorVisibilityService.cs");
        var query = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var marketplaceRead = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceReadController.cs");
        var homepageRead = ReadRepoFile("PointOfSale.API/Services/Homepage/HomepageSectionReadService.cs");

        Assert.Contains("GetVendorsSuspendedFromNewOrdersAsync", publicVisibility, StringComparison.Ordinal);
        Assert.Contains("FilterVisibleListingIdsAsync", publicVisibility, StringComparison.Ordinal);
        Assert.Contains("!listing.IsDeleted", publicVisibility, StringComparison.Ordinal);
        Assert.Contains("listing.IsPublished", publicVisibility, StringComparison.Ordinal);

        Assert.Contains("LoadPubliclySuppressedVendorIdsAsync", query, StringComparison.Ordinal);
        Assert.Contains("LoadSuspendedVendorIdsAsync", query, StringComparison.Ordinal);
        Assert.Contains("IsVendorSuppressedAsync", query, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleListingItemsAsync", query, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleSuggestionProductsAsync", query, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleAutocompleteProductsAsync", query, StringComparison.Ordinal);

        Assert.Contains("GetSuppressedVendorIdsAsync", marketplaceRead, StringComparison.Ordinal);
        Assert.Contains("IsVendorSuppressedAsync", marketplaceRead, StringComparison.Ordinal);
        Assert.Contains("FilterVisibleListingIdsAsync", marketplaceRead, StringComparison.Ordinal);
        Assert.Contains("FilterVisibleListingIdsAsync", homepageRead, StringComparison.Ordinal);
    }

    [Fact]
    public void Reactivation_MustRequireDebtBelowHardLimit()
    {
        var debt = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorDebtService.cs");
        var certification = ReadRepoFile("docs/architecture/commercehub-cod-risk-control-certification-findings.md");

        Assert.Contains("amountOwed >= threshold", debt, StringComparison.Ordinal);
        Assert.Contains("amountOwed - threshold + 0.01m", debt, StringComparison.Ordinal);
        Assert.Contains("requires reducing debt below the threshold", certification, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
