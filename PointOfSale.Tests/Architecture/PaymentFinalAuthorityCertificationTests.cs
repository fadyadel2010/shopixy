namespace PointOfSale.Tests.Architecture;

public sealed class PaymentFinalAuthorityCertificationTests
{
    [Fact]
    public void FrontendPaymentAuthority_MustRemainDerivativeAcrossFinalCertificationDomains()
    {
        var storefrontPaymentFlow = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentFlow.ts");
        var storefrontSessionAuthority = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts");
        var storefrontActiveCheckout = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/activeCheckoutOrderContext.ts");
        var commerceHubRecovery = ReadRepoFile("PointOfSale.CommerceHub/src/features/checkout/activePaymentRecoveryContext.ts");
        var commerceHubOrderActions = ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/orderActionAuthority.ts");

        Assert.Contains("Backend payment status did not prove the requested payment identity.", storefrontPaymentFlow, StringComparison.Ordinal);
        Assert.Contains("Cannot navigate using unverified payment identity.", storefrontPaymentFlow, StringComparison.Ordinal);
        Assert.Contains("return 'pending';", storefrontPaymentFlow, StringComparison.Ordinal);
        Assert.Contains("requireStorefrontPaymentSessionAuthority", storefrontSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("mixed provider payment-session artifacts", storefrontSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("return null;", storefrontActiveCheckout, StringComparison.Ordinal);
        Assert.Contains("return null;", commerceHubRecovery, StringComparison.Ordinal);
        Assert.Contains("Payment retry is no longer authorized for this order.", ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx"), StringComparison.Ordinal);
        Assert.Contains("Payment retry is no longer authorized for this order.", ReadRepoFile("PointOfSale.CommerceHub/src/pages/OrderPaymentRecoveryPage.tsx"), StringComparison.Ordinal);
        Assert.Contains("rejected retry-payment action contradiction", commerceHubOrderActions, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendPaymentAuthority_MustRemainStatusSessionOwnershipAndProviderBound()
    {
        var paymentsController = ReadRepoFile("PointOfSale.API/Controllers/PaymentsController.cs");
        var paymentAuthorization = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentRequestAuthorizationService.cs");
        var commerceHubPayments = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var commerceHubLifecycle = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentLifecycleAuthority.cs");

        Assert.Contains("AuthorizeOrderAccessAsync", paymentsController, StringComparison.Ordinal);
        Assert.Contains("GetCanonicalPaymentStatusAsync", paymentsController, StringComparison.Ordinal);
        Assert.Contains("RequiresGuestSessionValidation", paymentAuthorization, StringComparison.Ordinal);
        Assert.Contains("ValidatePaymentToken", paymentAuthorization, StringComparison.Ordinal);
        Assert.Contains("MatchesCustomer", paymentAuthorization, StringComparison.Ordinal);
        Assert.Contains("GetProviderAdapter(CommerceHubPaymentMethods.Stripe).ParseWebhookAsync", commerceHubPayments, StringComparison.Ordinal);
        Assert.Contains("GetProviderAdapter(CommerceHubPaymentMethods.Paymob).ParseWebhookAsync", commerceHubPayments, StringComparison.Ordinal);
        Assert.Contains("ParseCallbackAsync", commerceHubPayments, StringComparison.Ordinal);
        Assert.DoesNotContain("VerifyPaymobCallbackHmac", commerceHubPayments, StringComparison.Ordinal);
        Assert.Contains("EnsureAmountMatches", commerceHubPayments, StringComparison.Ordinal);
        Assert.Contains("EnsureIncomingProviderIdentityMatchesPayment", commerceHubPayments, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPaymentLifecycle()", commerceHubLifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordM3IFinalPaymentAuthorityTruth()
    {
        var findings = ReadRepoFile("docs/architecture/payment-final-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS M3-I Payment Final Certification", findings, StringComparison.Ordinal);
        Assert.Contains("No frontend payment authority", findings, StringComparison.Ordinal);
        Assert.Contains("Is Payment Authority production-certifiable? Yes", findings, StringComparison.Ordinal);
        Assert.Contains("PASS M3-I Payment Final Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS M3-I Payment Final Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS M3-I payment final runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS M3-I Payment Final Certification", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS M3-I Payment Final Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
