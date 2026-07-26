namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceOrderCreationAuthorityCertificationTests
{
    [Fact]
    public void FrontendCheckoutCreation_MustValidateMutationResponsesBeforeSuccessPresentation()
    {
        var checkoutService = ReadRepoFile("PointOfSale.CommerceHub/src/services/checkout-service.ts");
        var creationAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/features/checkout/orderCreationAuthority.ts");
        var checkoutPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx");

        Assert.Contains("requireCheckoutOrderCreationAuthority(response.data)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("requirePaymentInitiationAuthority(response.data, payload.orderId, payload.provider)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("marketplaceOrderId", creationAuthority, StringComparison.Ordinal);
        Assert.Contains("orderNumber", creationAuthority, StringComparison.Ordinal);
        Assert.Contains("response.subOrders.length === 0", creationAuthority, StringComparison.Ordinal);
        Assert.Contains("seenSubOrderIds.has(subOrder.subOrderId)", creationAuthority, StringComparison.Ordinal);
        Assert.Contains("response.marketplaceOrderId !== expectedOrderId", creationAuthority, StringComparison.Ordinal);
        Assert.Contains("response.provider !== expectedProvider", creationAuthority, StringComparison.Ordinal);
        Assert.Contains("setOrderResult(response)", checkoutPage, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendCheckoutCreation_MustKeepStorageRecoveryAndPaymentRedirectsDerivative()
    {
        var recoveryContext = ReadRepoFile("PointOfSale.CommerceHub/src/features/checkout/activePaymentRecoveryContext.ts");
        var checkoutPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx");
        var recoveryPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/OrderPaymentRecoveryPage.tsx");
        var recoveryService = ReadRepoFile("PointOfSale.CommerceHub/src/services/order-recovery-service.ts");
        var stripeForm = ReadRepoFile("PointOfSale.CommerceHub/src/payments/EmbeddedStripePaymentForm.tsx");

        Assert.Contains("sessionStorage", recoveryContext, StringComparison.Ordinal);
        Assert.Contains("storage.removeItem(ACTIVE_PAYMENT_RECOVERY_STORAGE_KEY)", recoveryContext, StringComparison.Ordinal);
        Assert.Contains("readActivePaymentRecoveryContext", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("Your cart was already converted into an authoritative order", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("orderRecoveryService.getOrder", recoveryPage, StringComparison.Ordinal);
        Assert.Contains("requireRecoverableOrderAuthority(response.data, orderId)", recoveryService, StringComparison.Ordinal);
        Assert.Contains("order.canRetryPayment", recoveryPage, StringComparison.Ordinal);
        Assert.Contains("return_url: window.location.href", stripeForm, StringComparison.Ordinal);
        Assert.Contains("Backend authority will confirm the final order state", stripeForm, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceOrderCreationAuthorityDocumentation_MustBeFrozen()
    {
        var findings = ReadRepoFile("docs/architecture/commercehub-marketplace-order-creation-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS M2-B Marketplace Order Creation Authority", findings, StringComparison.Ordinal);
        Assert.Contains("Creation findings", findings, StringComparison.Ordinal);
        Assert.Contains("Runtime hardening performed", findings, StringComparison.Ordinal);
        Assert.Contains("Marketplace Order Creation Authority is production-certifiable", findings, StringComparison.Ordinal);
        Assert.Contains("PASS M2-B Marketplace Order Creation Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS M2-B Marketplace Order Creation Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS M2-B", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS M2-B Marketplace Order Creation Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS M2-B Marketplace Order Creation Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
