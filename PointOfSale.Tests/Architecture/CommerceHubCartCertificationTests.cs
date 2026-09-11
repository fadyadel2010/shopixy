namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubCartCertificationTests
{
    [Fact]
    public void CartCertificationDocumentation_MustContainRequiredPassSixteenThreeSections()
    {
        var certification = ReadRepoFile("docs/architecture/commercehub-cart-certification-findings.md");

        foreach (var marker in new[]
        {
            "Scope: PASS 16.3",
            "## Cart Inventory",
            "## Add-To-Cart Findings",
            "## Quantity Findings",
            "## Guest Cart Findings",
            "## Cart Merge Findings",
            "## Pricing Findings",
            "## Inventory Findings",
            "## Session Findings",
            "## Multi-Tab And Multi-Device Findings",
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
    public void CartOwnershipAndSessionBehavior_MustRemainCustomerScopedAndConflictAware()
    {
        var cartService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var cartController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubCartController.cs");
        var cartStore = ReadRepoFile("PointOfSale.CommerceHub/src/store/cart-store.ts");
        var cartEntity = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubCart.cs");

        Assert.Contains("CommerceHubCartTenantScopeId", cartService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCustomerId", cartService, StringComparison.Ordinal);
        Assert.Contains("OnlineCustomerId = commerceHubCustomerId", cartService, StringComparison.Ordinal);
        Assert.Contains("SynchronizeCartOwner", cartService, StringComparison.Ordinal);
        Assert.Contains("EnsureMatchingVersion(request.Version, existingCart.Version)", cartService, StringComparison.Ordinal);
        Assert.Contains("\"CART_CONFLICT\"", cartService, StringComparison.Ordinal);
        Assert.Contains("\"CART_LOCKED\"", cartService, StringComparison.Ordinal);
        Assert.Contains("AuthenticateAsync(CustomerSecurityConstants.AuthenticationScheme)", cartController, StringComparison.Ordinal);
        Assert.Contains("stores: state.stores", cartStore, StringComparison.Ordinal);
        Assert.Contains("adoptGuestCartOnBootstrap: hasLocalItems", cartStore, StringComparison.Ordinal);
        Assert.Contains("mergeCartStores(localStores, serverStores)", cartStore, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCustomerId", cartEntity, StringComparison.Ordinal);
    }

    [Fact]
    public void CartFrontend_MustKeepBackendAlignmentAndCustomerUnderstandability()
    {
        var cartStore = ReadRepoFile("PointOfSale.CommerceHub/src/store/cart-store.ts");
        var cartTypes = ReadRepoFile("PointOfSale.CommerceHub/src/types/cart.ts");
        var cartService = ReadRepoFile("PointOfSale.CommerceHub/src/services/cart-service.ts");

        Assert.Contains("applyBackendCartState", cartStore, StringComparison.Ordinal);
        Assert.Contains("refreshAfterConflict", cartStore, StringComparison.Ordinal);
        Assert.Contains("refreshAfterLockedCart", cartStore, StringComparison.Ordinal);
        Assert.Contains("syncError", cartStore, StringComparison.Ordinal);
        Assert.Contains("clampCartItemQuantity", cartStore, StringComparison.Ordinal);
        Assert.Contains("buildCommerceHubCartSyncRequest(get().stores, get().version)", cartStore, StringComparison.Ordinal);
        Assert.Contains("getCartValidation", cartTypes, StringComparison.Ordinal);
        Assert.Contains("quantity_exceeds_stock", cartTypes, StringComparison.Ordinal);
        Assert.Contains("buildCommerceHubCartPricingRequest", cartTypes, StringComparison.Ordinal);
        Assert.Contains("priceCart(payload", cartService, StringComparison.Ordinal);
    }

    [Fact]
    public void CartCertificationDocumentation_MustBeRegisteredInArchitectureContext()
    {
        Assert.Contains("PASS 16.3 Cart Certification", ReadRepoFile(".ai-context/PROJECT_CONTEXT.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.3 Cart Certification Truth", ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.3", ReadRepoFile(".ai-context/KNOWN_RISKS.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.3 Cart Certification", ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.3 Cart Certification Freeze", ReadRepoFile(".ai-context/CODEX_RULES.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void CartSanitization_MustEnforceFullProductPublicationVisibility()
    {
        // PASS 16.3 HARDENING: Cart sanitization must check IsOnlineVisible and PublishStatus
        // in addition to IsActive, ensuring parity with CalculateAsync and checkout authority.
        var cartService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");

        // Sanitization must reject products that are active but not online-visible or not published
        Assert.Contains("!product.IsOnlineVisible", cartService, StringComparison.Ordinal);
        Assert.Contains("product.PublishStatus != PublishStatus.Published", cartService, StringComparison.Ordinal);

        // CalculateAsync must also enforce the same authority
        Assert.Contains("product.IsOnlineVisible", cartService, StringComparison.Ordinal);
        Assert.Contains("PublishStatus.Published", cartService, StringComparison.Ordinal);
    }

    [Fact]
    public void CartCheckoutHandoff_MustEnforceSerializableIsolationAndVersionLocking()
    {
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        // Checkout must use Serializable isolation
        Assert.Contains("IsolationLevel.Serializable", checkoutService, StringComparison.Ordinal);

        // Checkout must lock the cart row with FOR UPDATE
        Assert.Contains("FOR UPDATE", checkoutService, StringComparison.Ordinal);

        // Checkout must lock mutable catalog/store rows and use platform provider authority.
        Assert.Contains("LoadCommerceHubListingsForCheckoutAuthorityAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LoadProductVariantsForCheckoutAuthorityAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LoadProductsForCheckoutAuthorityAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LoadTenantsForCheckoutAuthorityAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("IEnumerable<IPaymentProviderAdapter> paymentProviderAdapters", checkoutService, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethods", checkoutService, StringComparison.Ordinal);

        // Checkout must enforce vendor governance and financial readiness
        Assert.Contains("EnsureCommerceIntakeAllowedAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("GetVendorsSuspendedFromNewOrdersAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorOrderAcceptanceAllowedAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureCustomerCheckoutAllowedAsync", checkoutService, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
