namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontCheckoutAddressAuthorityCertificationTests
{
    [Fact]
    public void StorefrontCheckoutAddressFrontend_MustSubmitAddressCandidatesOnly()
    {
        var checkoutTypes = ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var shippingStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx");

        Assert.Contains("export interface ShippingAddress", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("export interface AddressDto", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("shippingAddress: AddressDto", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("const shippingAddress = {", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("name: dynamicSystemValue('full_name', latestState.address.fullName)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("phone: dynamicSystemValue('phone', latestState.address.phone)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("street: dynamicSystemValue('street_address', latestState.address.addressLine)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("city: dynamicSystemValue('city', latestState.address.city)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("state: dynamicSystemValue('area', latestState.address.area)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("onChange={(value) => setAddressField('fullName', value)}", shippingStep, StringComparison.Ordinal);
        Assert.Contains("onChange={(value) => setAddressField('phone', value)}", shippingStep, StringComparison.Ordinal);

        Assert.DoesNotContain("addressId", checkoutTypes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("savedAddress", checkoutTypes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("defaultAddress", checkoutTypes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("addressId", checkoutStore, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("savedAddress", checkoutStore, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("defaultAddress", checkoutStore, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StorefrontCheckoutAddressStorage_MustNotPromoteBrowserStateToAuthority()
    {
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var recoverPage = ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");

        Assert.Contains("const SENSITIVE_STORAGE_KEY = 'checkout-sensitive-session'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("return window.sessionStorage", checkoutStore, StringComparison.Ordinal);
        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout store must keep an explicit localStorage partialize boundary.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout store partialize boundary must remain inspectable.");
        var localStoragePartialize = checkoutStore[partializeStart..partializeEnd];

        Assert.Contains("shippingMethodId: state.shippingMethodId", localStoragePartialize, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: state.paymentMethod", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("address", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("window.localStorage.setItem(SENSITIVE_STORAGE_KEY", checkoutStore, StringComparison.Ordinal);

        Assert.Contains("const session = await recoverCheckout(token);", recoverPage, StringComparison.Ordinal);
        Assert.Contains("checkoutStore.reset();", recoverPage, StringComparison.Ordinal);
        Assert.Contains("checkoutSessionId: null", recoverPage, StringComparison.Ordinal);
        Assert.Contains("address: {", recoverPage, StringComparison.Ordinal);
        Assert.Contains("fullName: session.address.fullName || ''", recoverPage, StringComparison.Ordinal);
        Assert.Contains("session.requiresReview || session.cartChangedDuringRecovery", recoverPage, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendCheckoutAddressAdmission_MustValidateCandidateAndPersistServerSnapshot()
    {
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var checkoutDtos = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains("var (errorCode, errorMessage) = request.Validate();", checkoutController, StringComparison.Ordinal);
        Assert.Contains("var shippingValidation = ShippingAddress.Validate();", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("if (string.IsNullOrWhiteSpace(Country))", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("CountryCodeHelper.TryNormalizeCountryCode(Country, out var normalizedCountry)", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("Country = normalizedCountry;", checkoutDtos, StringComparison.Ordinal);

        Assert.Contains("request.ShippingAddress == null", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidateCheckoutFieldDefinitionsAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("CheckoutFieldRuleEngine.Evaluate(definitions, submittedValues)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("CHECKOUT_SHIPPING_PHONE_REQUIRED", checkoutService, StringComparison.Ordinal);
        Assert.Contains("CHECKOUT_SHIPPING_COUNTRY_REQUIRED", checkoutService, StringComparison.Ordinal);
        Assert.Contains("request.ShippingAddress.Phone", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidateAndReserveInventoryAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("RevalidateCheckoutAuthorityInsideTransactionAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("GuestPhone = request.ShippingAddress.Phone", checkoutService, StringComparison.Ordinal);
        Assert.Contains("GuestName = request.ShippingAddress.Name", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShippingAddress = FormatAddress(request.ShippingAddress)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShippingCity = request.ShippingAddress.City", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShippingState = request.ShippingAddress.State", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShippingZipCode = request.ShippingAddress.ZipCode", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShippingCountry = request.ShippingAddress.Country", checkoutService, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendCheckoutAddressIdentity_MustBeCustomerSessionCartBound()
    {
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var checkoutSessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");

        Assert.Contains("ResolveCallerIdentityAsync", checkoutController, StringComparison.Ordinal);
        Assert.Contains("ResolveAuthorizedCustomerHeader", checkoutController, StringComparison.Ordinal);
        Assert.Contains("ownerType = \"customer\"", checkoutController, StringComparison.Ordinal);
        Assert.Contains("ownerType = \"session\"", checkoutController, StringComparison.Ordinal);
        Assert.Contains("SESSION_HEADER_MISSING", checkoutController, StringComparison.Ordinal);

        Assert.Contains("customer == null || customer.TenantId != tenantId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Checkout tenant mismatch detected for customer ownership", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Checkout owner mismatch detected for authenticated customer", checkoutService, StringComparison.Ordinal);

        Assert.Contains("var recoveredLiveCart = await TryRevalidateRecoveredCartAsync(", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("if (recoveredLiveCart.Cart == null)", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("RECOVERY_CART_REVALIDATION_FAILED", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("FullName = session.CustomerName ?? string.Empty", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("Phone = session.Phone ?? string.Empty", checkoutSessionController, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfPa52Truth()
    {
        Assert.Contains(
            "PASS SF-PA5.2 Checkout Address Authority Certification",
            ReadRepoFile("docs/architecture/storefront-checkout-address-authority-certification-findings.md"),
            StringComparison.Ordinal);

    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
