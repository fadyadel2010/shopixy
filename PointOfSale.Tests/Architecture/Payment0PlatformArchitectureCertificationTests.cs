namespace PointOfSale.Tests.Architecture;

public sealed class Payment0PlatformArchitectureCertificationTests
{
    [Fact]
    public void PaymentAuthorityContract_MustOwnPaymentConcernsWithoutOrderCheckoutOrInventoryAuthority()
    {
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/IPaymentService.cs");
        var lifecycle = ReadRepoFile("PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs");
        var paymentIntent = ReadRepoFile("PointOfSale.Core/Entities/PaymentIntent.cs");
        var paymentMethod = ReadRepoFile("PointOfSale.Core/Entities/TenantPaymentMethod.cs");

        Assert.Contains("Payment owns payment intent/session orchestration", paymentService, StringComparison.Ordinal);
        Assert.Contains("Orders provide immutable payable/order reference evidence", paymentService, StringComparison.Ordinal);
        Assert.Contains("Provider webhooks/callbacks are signed evidence", paymentService, StringComparison.Ordinal);
        Assert.Contains("Inventory/order side effects are downstream results", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("Order is Source of Truth", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("No refunds in this phase", paymentService, StringComparison.Ordinal);

        Assert.Contains("This is the ONLY way to change PaymentStatus", lifecycle, StringComparison.Ordinal);
        Assert.Contains("HandlePaymentResultAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("RecordRefundAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("RecordGatewaySettlementAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("AmountMismatch is a SYSTEM-ONLY outcome", lifecycle, StringComparison.Ordinal);

        Assert.Contains("public Guid OrderId", paymentIntent, StringComparison.Ordinal);
        Assert.Contains("public decimal Amount", paymentIntent, StringComparison.Ordinal);
        Assert.Contains("public string Currency", paymentIntent, StringComparison.Ordinal);
        Assert.Contains("public PaymentProvider Provider", paymentIntent, StringComparison.Ordinal);
        Assert.Contains("public string IdempotencyKey", paymentIntent, StringComparison.Ordinal);
        Assert.Contains("public string? ProviderMetadata", paymentIntent, StringComparison.Ordinal);
        Assert.Contains("public int WebhookEventCount", paymentIntent, StringComparison.Ordinal);

        Assert.Contains("Tenant-scoped payment method configuration", paymentMethod, StringComparison.Ordinal);
        Assert.Contains("Secrets are encrypted using Data Protection API", paymentMethod, StringComparison.Ordinal);
        Assert.Contains("NO payment provider integration in this entity", paymentMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderContractsAndWebhooks_MustRemainBackendPaymentOwnedAndFailClosed()
    {
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/Adapters/IPaymentAdapter.cs");
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");
        var webhookController = ReadRepoFile("PointOfSale.API/Controllers/PaymentWebhooksController.cs");
        var webhookLog = ReadRepoFile("PointOfSale.Core/Entities/PaymentWebhookLog.cs");
        var stripe = ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");
        var paymob = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var fawry = ReadRepoFile("PointOfSale.API/Services/Payments/Adapters/FawryPaymentAdapter.cs");
        var manual = ReadRepoFile("PointOfSale.API/Services/Payments/Adapters/ManualWalletPaymentAdapter.cs");

        Assert.Contains("public interface IPaymentAdapter", adapter, StringComparison.Ordinal);
        Assert.Contains("PaymentProvider Provider", adapter, StringComparison.Ordinal);
        Assert.Contains("Task<PaymentInitiationResult> InitiateAsync", adapter, StringComparison.Ordinal);

        Assert.Contains("IEnumerable<IPaymentAdapter> paymentAdapters", paymentService, StringComparison.Ordinal);
        Assert.Contains("adapter.InitiateAsync(paymentIntent", paymentService, StringComparison.Ordinal);
        Assert.Contains("VerifyWebhookSignature(provider, signature, rawPayload)", paymentService, StringComparison.Ordinal);
        Assert.Contains("CheckWebhookIdempotencyAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("new PaymentWebhookLog", paymentService, StringComparison.Ordinal);
        Assert.Contains("Duplicate event ignored", paymentService, StringComparison.Ordinal);
        Assert.Contains("Source = PaymentLifecycleSource.GatewayWebhook", paymentService, StringComparison.Ordinal);

        Assert.Contains("[Route(\"api/payments/webhooks\")]", webhookController, StringComparison.Ordinal);
        Assert.Contains("PrepareAsync", webhookController, StringComparison.Ordinal);
        Assert.Contains("PaymentInvalidSignatureException", webhookController, StringComparison.Ordinal);
        Assert.Contains("Webhook processing failed. Provider should retry.", webhookController, StringComparison.Ordinal);

        Assert.Contains("ProviderEventId", webhookLog, StringComparison.Ordinal);
        Assert.Contains("SignatureVerified", webhookLog, StringComparison.Ordinal);
        Assert.Contains("ProcessingStatus", webhookLog, StringComparison.Ordinal);
        Assert.Contains("SourceIp", webhookLog, StringComparison.Ordinal);

        Assert.Contains("EventUtility.ConstructEvent", stripe, StringComparison.Ordinal);
        Assert.Contains("VerifyHmacSignature", paymob, StringComparison.Ordinal);
        Assert.Contains("FawryMerchantReference.Build(action)", fawry, StringComparison.Ordinal);
        Assert.Contains("PaymentProvider.ManualWallet", manual, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutBoundary_MustSubmitCandidatesAndConsumePaymentSessionEvidenceOnly()
    {
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var checkoutDtos = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");
        var sessionAuthority = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts");
        var paymentFlow = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentFlow.ts");
        var paymentMethodsController = ReadRepoFile("PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs");

        Assert.Contains("ProcessCheckoutAsync", checkoutController, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", checkoutController, StringComparison.Ordinal);
        Assert.DoesNotContain("EventUtility.ConstructEvent", checkoutController, StringComparison.Ordinal);
        Assert.DoesNotContain("VerifyHmacSignature", checkoutController, StringComparison.Ordinal);

        Assert.Contains("ValidatePaymentMethodAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentId = paymentIntentId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ResolveTransactionalCheckoutPaymentProvider(paymentMethod.Code)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus = order.PaymentStatus.ToString()", checkoutService, StringComparison.Ordinal);

        Assert.Contains("public Guid? PaymentIntentId", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public string? CheckoutToken", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public string? StripeClientSecret", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public string? PaymobIframeUrl", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public string? ReferenceCode", checkoutDtos, StringComparison.Ordinal);

        Assert.Contains("Backend checkout response mixed provider payment-session artifacts.", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("Terminal payment response unexpectedly included reusable provider payment-session artifacts.", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("never durable frontend", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("Only the backend webhook confirmation is authoritative", paymentFlow, StringComparison.Ordinal);
        Assert.Contains("return 'pending';", paymentFlow, StringComparison.Ordinal);

        Assert.Contains("GetEnabledPaymentMethodsAsync", paymentMethodsController, StringComparison.Ordinal);
        Assert.Contains("NO SECRETS", paymentMethodsController, StringComparison.Ordinal);
        Assert.Contains("IsSupportedStorefrontPaymentMethod(document.Code)", paymentMethodsController, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityFailureAndPersistenceBoundaries_MustBeTenantScopedIdempotentAndLifecycleGuarded()
    {
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");
        var paymentContract = ReadRepoFile("PointOfSale.API/Services/Payments/IPaymentService.cs");
        var requestAuthorization = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentRequestAuthorizationService.cs");
        var lifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var interceptor = ReadRepoFile("PointOfSale.Infrastructure/Data/PaymentStatusSaveChangesInterceptor.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var paymentMethodValidator = ReadRepoFile("PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs");

        Assert.Contains("EnsurePaymentCurrencyConsistencyAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("DUPLICATE_PAYMENT_INTENT", paymentService, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderNotSupportedException", paymentContract, StringComparison.Ordinal);
        Assert.Contains("PaymentMismatchException", paymentContract, StringComparison.Ordinal);
        Assert.Contains("PaymentInvalidSignatureException", paymentService, StringComparison.Ordinal);

        Assert.Contains("AuthorizeOrderAccessAsync", requestAuthorization, StringComparison.Ordinal);
        Assert.Contains("RequiresGuestSessionValidation", requestAuthorization, StringComparison.Ordinal);
        Assert.Contains("ValidatePaymentToken", requestAuthorization, StringComparison.Ordinal);

        Assert.Contains("TryQuarantineProviderAuthorityMismatchAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("IsTerminalSqlPaymentTruth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus can ONLY be changed via IPaymentLifecycleService", interceptor, StringComparison.Ordinal);

        Assert.Contains("IX_PaymentIntents_Active_OrderId_Provider_Unique", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId", dbContext, StringComparison.Ordinal);
        Assert.Contains("Unsupported payment method", paymentMethodValidator, StringComparison.Ordinal);
        Assert.Contains("RequiresConfiguration", paymentMethodValidator, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifact_MustRecordPayment0PlatformArchitectureDecision()
    {
        var artifact = ReadRepoFile("docs/architecture/PAYMENT_0_PLATFORM_ARCHITECTURE_CERTIFICATION.md");

        Assert.Contains("PAYMENT-0 Payment Platform Architecture Certification", artifact, StringComparison.Ordinal);
        Assert.Contains("Architecture Certified: Yes", artifact, StringComparison.Ordinal);
        Assert.Contains("Provider Neutral: Yes", artifact, StringComparison.Ordinal);
        Assert.Contains("Runtime modified: No", artifact, StringComparison.Ordinal);
        Assert.Contains("One stale architecture comment was corrected", artifact, StringComparison.Ordinal);
        Assert.Contains("Payment consumes final payable amount and immutable order reference evidence only", artifact, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
