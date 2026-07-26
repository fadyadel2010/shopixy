namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontCheckoutGeographicIntegrationTests
{
    [Fact]
    public void Geo7StorefrontCheckoutDtos_MustExposeCanonicalCandidatesOnly()
    {
        var checkoutDtos = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");

        Assert.Contains("GeographicAddressCandidateDto", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("CountryPlaceId", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("AdministrativeAreaPlaceId", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("LocalityPlaceId", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("DistrictPlaceId", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("ZonePlaceId", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("Canonical Geographic candidate selected by the storefront. Backend admission is the authority.", checkoutDtos, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderPlaceId", checkoutDtos, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderDistrictId", checkoutDtos, StringComparison.Ordinal);
        Assert.DoesNotContain("Bosta", checkoutDtos, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo7CheckoutService_MustAdmitCandidatesBeforeOrderAndPaymentProgression()
    {
        var checkoutService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains("IAddressAdmissionService", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddressAdmissionOwnerDomain.Checkout", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AdmitCheckoutGeographyAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddressGeographicAdmission", checkoutService, StringComparison.Ordinal);
        Assert.Contains("GEOGRAPHIC_ADDRESS_ADMISSION_FAILED", checkoutService, StringComparison.Ordinal);
        Assert.Contains("GeographicAddressSnapshot = admittedGeography", checkoutService, StringComparison.Ordinal);

        Assert.True(
            checkoutService.IndexOf("AddressGeographicAdmission", StringComparison.Ordinal) <
            checkoutService.IndexOf("AffiliateValidation", StringComparison.Ordinal));
        Assert.True(
            checkoutService.IndexOf("AddressGeographicAdmission", StringComparison.Ordinal) <
            checkoutService.IndexOf("ShippingMethodValidation", StringComparison.Ordinal));
        Assert.True(
            checkoutService.IndexOf("activeStep = \"OrderCreation\"", StringComparison.Ordinal) <
            checkoutService.IndexOf("activeStep = \"PaymentIntent\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Geo7IdempotencyAndFrontend_MustCarryCandidatesWithoutAuthority()
    {
        var snapshotBuilder = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutCartIdempotencySnapshotBuilder.cs");
        var storefrontTypes = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");
        var checkoutStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");

        Assert.Contains("v3: Added structured address details and canonical geographic candidates", snapshotBuilder, StringComparison.Ordinal);
        Assert.Contains("address.Geography.CountryPlaceId", snapshotBuilder, StringComparison.Ordinal);
        Assert.Contains("address.Geography.LocalityPlaceId", snapshotBuilder, StringComparison.Ordinal);

        Assert.Contains("GeographicAddressCandidate", storefrontTypes, StringComparison.Ordinal);
        Assert.Contains("geography?: GeographicAddressCandidate", storefrontTypes, StringComparison.Ordinal);
        Assert.Contains("shippingGeography?: GeographicAddressCandidate", storefrontTypes, StringComparison.Ordinal);

        Assert.Contains("shippingGeography: get().address.geography", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("geography: latestState.address.geography", checkoutStore, StringComparison.Ordinal);
        Assert.DoesNotContain("AddressAdmissionEvidenceId", checkoutStore, StringComparison.Ordinal);
        Assert.DoesNotContain("CatalogReleaseId", checkoutStore, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo7Scope_MustNotModifyAffiliateOrCommerceHubCheckoutContracts()
    {
        var commerceHubController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CommerceHubCheckoutController.cs");
        var commerceHubDtos = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/CommerceHub/CommerceHubCheckoutDtos.cs");
        var affiliateTests = CertificationSource.ReadRepoFile("PointOfSale.Tests/Affiliate/AffiliateGuestCheckoutTests.cs");
        var geo7Doc = CertificationSource.ReadRepoFile("docs/architecture/GEO-7_STOREFRONT_CHECKOUT_GEOGRAPHIC_INTEGRATION.md");

        Assert.DoesNotContain("GeographicAddressCandidateDto", commerceHubController, StringComparison.Ordinal);
        Assert.DoesNotContain("IAddressAdmissionService", commerceHubController, StringComparison.Ordinal);
        Assert.DoesNotContain("GeographicAddressCandidateDto", commerceHubDtos, StringComparison.Ordinal);
        Assert.DoesNotContain("AddressAdmissionEvidenceId", affiliateTests, StringComparison.Ordinal);

        Assert.Contains("This implementation applies only to POS Admin Storefront Checkout.", geo7Doc, StringComparison.Ordinal);
        Assert.Contains("CommerceHub first performs Marketplace Split", geo7Doc, StringComparison.Ordinal);
        Assert.Contains("Affiliate Checkout will reuse the same Canonical Vendor Checkout contract", geo7Doc, StringComparison.Ordinal);
    }
}
