namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubCheckoutCertificationTests
{
    [Fact]
    public void CheckoutCertificationDocumentation_MustContainRequiredPassSixteenFourSections()
    {
        var certification = ReadRepoFile("docs/architecture/commercehub-checkout-certification-findings.md");

        foreach (var marker in new[]
        {
            "Scope: PASS 16.4",
            "## Checkout Inventory",
            "## Authority Findings",
            "## Pricing Findings",
            "## Inventory Reservation Findings",
            "## Payment Findings",
            "## Address And Shipping Findings",
            "## Order Creation Findings",
            "## Idempotency Findings",
            "## Concurrency Findings",
            "## Recovery Findings",
            "## Frontend UX Findings",
            "## Financial Findings",
            "## Auditability Findings",
            "## Chaos Findings",
            "## Monitoring Findings",
            "## Runtime Hardening Performed",
            "## Regression Freeze Added",
            "## Architecture Updates",
            "## Remaining Debt",
            "## Required Answers"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CheckoutEntryPoint_MustRemainAuthenticatedRateLimitedAndIdempotent()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubCheckoutController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var checkoutPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx");
        var checkoutIdempotency = ReadRepoFile("PointOfSale.CommerceHub/src/pages/checkoutIdempotency.ts");

        Assert.Contains("Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme", controller, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", controller, StringComparison.Ordinal);
        Assert.Contains("EnforceCheckoutRateLimitAsync", controller, StringComparison.Ordinal);
        Assert.Contains("EnsureCustomerCheckoutAllowedAsync", service, StringComparison.Ordinal);
        Assert.Contains("BuildCheckoutRequestHash(customerId, request)", service, StringComparison.Ordinal);
        Assert.Contains("EnsureCheckoutIdempotencyRequestMatches", service, StringComparison.Ordinal);
        Assert.Contains("WaitForCheckoutReplayAsync", service, StringComparison.Ordinal);
        Assert.Contains("PersistCheckoutResponseIfMissingAsync", service, StringComparison.Ordinal);
        Assert.Contains("submissionInFlightRef.current", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("createCheckoutAttemptIdempotencyKey", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("buildCheckoutAttemptFingerprint", checkoutIdempotency, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutAuthority_MustReloadCurrentSqlStateBeforeOrderCreation()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("IsolationLevel.Serializable", service, StringComparison.Ordinal);
        Assert.Contains("LockCartForCheckoutAsync", service, StringComparison.Ordinal);
        Assert.Contains("EnsureLockedCartMatchesCheckoutRequest", service, StringComparison.Ordinal);
        Assert.Contains("LoadCommerceHubListingsForCheckoutAuthorityAsync", service, StringComparison.Ordinal);
        Assert.Contains("LoadProductVariantsForCheckoutAuthorityAsync", service, StringComparison.Ordinal);
        Assert.Contains("LoadProductsForCheckoutAuthorityAsync", service, StringComparison.Ordinal);
        Assert.Contains("LoadTenantsForCheckoutAuthorityAsync", service, StringComparison.Ordinal);
        Assert.Contains("LoadTenantPaymentMethodsForCheckoutAuthorityAsync", service, StringComparison.Ordinal);
        Assert.Contains("EnsureCommerceIntakeAllowedAsync", service, StringComparison.Ordinal);
        Assert.Contains("GetVendorsSuspendedFromNewOrdersAsync", service, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorOrderAcceptanceAllowedAsync", service, StringComparison.Ordinal);
        Assert.Contains("PublishStatus.Published", service, StringComparison.Ordinal);
        Assert.Contains("product.IsOnlineVisible", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutFrontend_MustRefreshCartPricingAndPaymentAuthorityBeforeSubmit()
    {
        var checkoutPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx");
        var cartStore = ReadRepoFile("PointOfSale.CommerceHub/src/store/cart-store.ts");

        Assert.Contains("await saveCart({ persistIfAvailableOnly: false })", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("cartService.priceCart", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("buildCommerceHubCheckoutRequest", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("authoritativePaymentMethod?.isAvailable", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("setCheckoutLock(true)", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("paymentInitiationError", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("persistActivePaymentRecoveryContext", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("refreshAfterConflict", cartStore, StringComparison.Ordinal);
        Assert.Contains("refreshAfterLockedCart", cartStore, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutCertificationDocumentation_MustBeRegisteredInArchitectureContext()
    {
        Assert.Contains("PASS 16.4 Checkout Certification", ReadRepoFile(".ai-context/PROJECT_CONTEXT.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.4 Checkout Certification Truth", ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.4", ReadRepoFile(".ai-context/KNOWN_RISKS.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.4 Checkout Certification", ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.4 Checkout Certification Freeze", ReadRepoFile(".ai-context/CODEX_RULES.md"), StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
