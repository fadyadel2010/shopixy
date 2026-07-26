namespace PointOfSale.Tests.Architecture;

public sealed class StripePaymentAuthorityCertificationTests
{
    [Fact]
    public void StorefrontStripeRuntime_MustKeepFrontendPresentationSdkRedirectAndPendingOnly()
    {
        var checkoutPage = ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPage.tsx");
        var stripeForm = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/StripePaymentForm.tsx");
        var paymentFlow = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentFlow.ts");
        var paymentSessionAuthority = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts");
        var paymentStatusApi = ReadRepoFile("PointOfSale.Storefront/src/services/api/paymentStatusApi.ts");
        var pendingPage = ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx");
        var confirmationPage = ReadRepoFile("PointOfSale.Storefront/src/pages/OrderConfirmationPage.tsx");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");

        Assert.Contains("requireStorefrontPaymentSessionAuthority(response.data, latestState.paymentMethod, cart.cartId)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("provider !== 'STRIPE'", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(paymentIntentId)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(checkoutToken)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isFutureIsoDate(expiresAtUtc)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(response.stripeClientSecret)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(response.stripePublishableKey)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("complete Stripe handoff", paymentSessionAuthority, StringComparison.Ordinal);

        Assert.Contains("orderResult.stripeClientSecret && orderResult.stripePublishableKey", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("returnUrl={`${window.location.origin}${affiliateAwarePendingUrl}`}", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("window.location.href = affiliateAwarePendingUrl", checkoutPage, StringComparison.Ordinal);
        Assert.DoesNotContain("/confirmation", checkoutPage[checkoutPage.IndexOf("STRIPE: Show Stripe payment form", StringComparison.Ordinal)..], StringComparison.OrdinalIgnoreCase);

        Assert.Contains("loadStripe", stripeForm, StringComparison.Ordinal);
        Assert.Contains("PaymentElement", stripeForm, StringComparison.Ordinal);
        Assert.Contains("stripe.confirmPayment", stripeForm, StringComparison.Ordinal);
        Assert.Contains("redirect: 'if_required'", stripeForm, StringComparison.Ordinal);
        Assert.Contains("resolveStripeClientOutcome(paymentIntent?.status)", stripeForm, StringComparison.Ordinal);
        Assert.Contains("Frontend never treats Stripe.js status as final success.", stripeForm, StringComparison.Ordinal);
        Assert.Contains("onPaymentPending();", stripeForm, StringComparison.Ordinal);
        Assert.DoesNotContain("onPaymentSuccess", stripeForm, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paymentIntent?.status === 'succeeded'", stripeForm, StringComparison.Ordinal);

        Assert.Contains("return 'pending';", paymentFlow, StringComparison.Ordinal);
        Assert.Contains("Only the backend webhook confirmation is authoritative", paymentFlow, StringComparison.Ordinal);
        Assert.Contains("getCanonicalPaymentStatus", paymentStatusApi, StringComparison.Ordinal);
        Assert.Contains("requireCanonicalPaymentStatusForOrder(orderId, response.data)", paymentStatusApi, StringComparison.Ordinal);
        Assert.Contains("resolvePendingCheckoutAction(currentOrderId, response)", pendingPage, StringComparison.Ordinal);
        Assert.Contains("nextAction.kind === 'success'", pendingPage, StringComparison.Ordinal);
        Assert.Contains("paymentStatus?.status !== 'PAID'", confirmationPage, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendStripeCreation_MustOwnIntentClientSecretAmountCurrencyCustomerMetadataAndRecovery()
    {
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var stripeService = ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");
        var stripeRuntimeApplication = ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeRuntimeApplicationService.cs");
        var stripeAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentIntentAdapter.cs");
        var stripeController = ReadRepoFile("PointOfSale.API/Controllers/StorefrontStripePaymentsController.cs");
        var recoveryService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs");

        Assert.Contains("_stripePaymentService.CreatePaymentIntentAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("checkoutToken = CreateCheckoutPaymentToken(tenantId, order.Id)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("stripeClientSecret = stripeResponse.ClientSecret", checkoutService, StringComparison.Ordinal);
        Assert.Contains("paymentIntentId = stripeResponse.PaymentIntentId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("expiresAtUtc = await ResolvePaymentIntentExpirationAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("StripeClientSecret = stripeClientSecret", checkoutService, StringComparison.Ordinal);
        Assert.Contains("StripePublishableKey = stripePublishableKey", checkoutService, StringComparison.Ordinal);
        Assert.Contains("StripeClientSecret = null, // Will be regenerated on replay", checkoutService, StringComparison.Ordinal);
        Assert.Contains("GetStripeClientSecret(paymentIntent)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("TryGetStripePublishableKeyAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("response.StripeClientSecret = null", checkoutService, StringComparison.Ordinal);

        Assert.Contains("OrderLockingHelper.LoadOrderWithLockAsync", stripeService, StringComparison.Ordinal);
        Assert.Contains("ValidateCheckoutSessionOwnershipAsync(checkoutToken", stripeService, StringComparison.Ordinal);
        Assert.Contains("ValidateCustomerOwnershipAsync(order", stripeService, StringComparison.Ordinal);
        Assert.Contains("order.PaymentMethodCode.Equals(\"STRIPE\"", stripeService, StringComparison.Ordinal);
        Assert.Contains("Amount = order.Total", stripeService, StringComparison.Ordinal);
        Assert.Contains("Currency = order.Currency", stripeService, StringComparison.Ordinal);
        Assert.Contains("StripeMoneyMapper.ToMinorUnits", stripeAdapter, StringComparison.Ordinal);
        Assert.Contains("[\"orderId\"] = request.OrderId.ToString()", stripeRuntimeApplication, StringComparison.Ordinal);
        Assert.Contains("[\"tenantId\"] = request.TenantId.ToString()", stripeRuntimeApplication, StringComparison.Ordinal);
        Assert.Contains("[\"customerId\"] = request.CustomerId?.ToString() ?? string.Empty", stripeRuntimeApplication, StringComparison.Ordinal);
        Assert.Contains("[\"paymentReference\"] = request.PaymentReference", stripeRuntimeApplication, StringComparison.Ordinal);
        Assert.Contains("SetClientSecret(paymentIntent, stripePaymentIntent.ClientSecret)", stripeService, StringComparison.Ordinal);
        Assert.Contains("_clientSecretProtector.Protect(clientSecret)", stripeService, StringComparison.Ordinal);
        Assert.Contains("_clientSecretProtector.Unprotect(paymentIntent.ClientSecretEncrypted)", stripeService, StringComparison.Ordinal);
        Assert.Contains("StorefrontPaymentSessionTtl", stripeService, StringComparison.Ordinal);
        Assert.Contains("IX_PaymentIntents_Active_OrderId_Provider_Unique", ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs"), StringComparison.Ordinal);

        Assert.Contains("AuthorizeOrderAccessAsync", stripeController, StringComparison.Ordinal);
        Assert.Contains("effectiveCheckoutToken", stripeController, StringComparison.Ordinal);
        Assert.Contains("request.CustomerId.Value != authenticatedCustomerId.Value", stripeController, StringComparison.Ordinal);
        Assert.Contains("CreatePaymentIntentAsync", stripeController, StringComparison.Ordinal);

        Assert.Contains("CreatePaymentToken(order.TenantId, order.Id)", recoveryService, StringComparison.Ordinal);
        Assert.Contains("_stripePaymentService.CreatePaymentIntentAsync", recoveryService, StringComparison.Ordinal);
    }

    [Fact]
    public void StripeWebhooks_MustOwnValidationSuccessFailureStatusAuditReplayAndSqlPersistence()
    {
        var webhookController = ReadRepoFile("PointOfSale.API/Controllers/StripeWebhooksController.cs");
        var stripeService = ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");
        var lifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var interceptor = ReadRepoFile("PointOfSale.Infrastructure/Data/PaymentStatusSaveChangesInterceptor.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("[Route(\"api/webhooks/stripe\")]", webhookController, StringComparison.Ordinal);
        Assert.Contains("[AllowAnonymousTenant]", webhookController, StringComparison.Ordinal);
        Assert.Contains("RequestSizeLimit(WebhookRequestLimits.PaymentProviderMaxBytes)", webhookController, StringComparison.Ordinal);
        Assert.Contains("WebhookRateLimiterPolicy.PolicyName", webhookController, StringComparison.Ordinal);
        Assert.Contains("request.Headers[\"Stripe-Signature\"].FirstOrDefault()", webhookController, StringComparison.Ordinal);
        Assert.Contains("_webhookRequestPipeline.PrepareAsync", webhookController, StringComparison.Ordinal);
        Assert.Contains("StripeWebhookSignatureException", webhookController, StringComparison.Ordinal);
        Assert.Contains("return Unauthorized(new ApiErrorResponse", webhookController, StringComparison.Ordinal);
        Assert.Contains("return StatusCode(StatusCodes.Status500InternalServerError", webhookController, StringComparison.Ordinal);

        Assert.Contains("EventUtility.ConstructEvent", stripeService, StringComparison.Ordinal);
        Assert.Contains("stripeConfig.WebhookSecret", stripeService, StringComparison.Ordinal);
        Assert.Contains("PrepareWebhookLogAsync", stripeService, StringComparison.Ordinal);
        Assert.Contains("ProcessingStatus == WebhookProcessingStatus.Completed", stripeService, StringComparison.Ordinal);
        Assert.Contains("ProcessingStatus == WebhookProcessingStatus.Processing", stripeService, StringComparison.Ordinal);
        Assert.Contains("Duplicate event ignored", stripeService, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", stripeService, StringComparison.Ordinal);
        Assert.Contains("payment_intent.succeeded", stripeService, StringComparison.Ordinal);
        Assert.Contains("payment_intent.payment_failed", stripeService, StringComparison.Ordinal);
        Assert.Contains("payment_intent.canceled", stripeService, StringComparison.Ordinal);
        Assert.Contains("FindPaymentIntentWithFallbackAsync", stripeService, StringComparison.Ordinal);
        Assert.Contains("Outcome = PaymentLifecycleOutcome.Succeeded", stripeService, StringComparison.Ordinal);
        Assert.Contains("Outcome = PaymentLifecycleOutcome.Failed", stripeService, StringComparison.Ordinal);
        Assert.Contains("Outcome = PaymentLifecycleOutcome.Cancelled", stripeService, StringComparison.Ordinal);
        Assert.Contains("Source = PaymentLifecycleSource.GatewayWebhook", stripeService, StringComparison.Ordinal);
        Assert.Contains("ProviderAmount = providerAmount", stripeService, StringComparison.Ordinal);
        Assert.Contains("ProviderCurrency = providerCurrency", stripeService, StringComparison.Ordinal);
        Assert.Contains("WebhookEventId = stripeEvent.Id", stripeService, StringComparison.Ordinal);
        Assert.Contains("webhookLog.PaymentIntentId = intent.Id", stripeService, StringComparison.Ordinal);
        Assert.Contains("webhookLog.OrderId = orderId", stripeService, StringComparison.Ordinal);
        Assert.Contains("PersistWebhookFailureAsync", stripeService, StringComparison.Ordinal);
        Assert.Contains("StoreWebhookIdempotencyAsync", stripeService, StringComparison.Ordinal);

        Assert.Contains("TryQuarantineProviderAuthorityMismatchAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("IsTerminalSqlPaymentTruth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Stale provider success rejected against terminal SQL truth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Stale provider failure collapsed against terminal SQL truth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus can ONLY be changed via IPaymentLifecycleService", interceptor, StringComparison.Ordinal);
        Assert.Contains("IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void CacheRecoveryConcurrencyAndCrossSystemPaths_MustNotBecomeStripeAuthority()
    {
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var checkoutContext = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/activeCheckoutOrderContext.ts");
        var recoverPage = ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var checkoutSessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var stripeService = ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");

        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout store must keep an explicit localStorage partialize boundary.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout store partialize boundary must remain inspectable.");
        var localStoragePartialize = checkoutStore[partializeStart..partializeEnd];

        Assert.DoesNotContain("orderResult", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("stripeClientSecret", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paymentIntentId", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checkoutToken", localStoragePartialize, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("persistActiveCheckoutOrderContext", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("checkoutToken: order.checkoutToken", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("can never be restored from", checkoutContext, StringComparison.Ordinal);
        Assert.Contains("return null;", checkoutContext, StringComparison.Ordinal);
        Assert.Contains("checkoutStore.reset();", recoverPage, StringComparison.Ordinal);
        Assert.Contains("RecoverByToken", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("TryRevalidateRecoveredCartAsync", checkoutSessionController, StringComparison.Ordinal);

        Assert.Contains("BuildReplayResponseFromExistingOrderAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("StripeClientSecret = null, // Will be regenerated on replay", checkoutService, StringComparison.Ordinal);
        Assert.Contains("response.StripeClientSecret = null", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureReplayPaymentIntentAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("StorefrontPaymentSessionTtl", stripeService, StringComparison.Ordinal);
        Assert.Contains("NormalizeCheckoutPaymentIdempotencyKey", stripeService, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_INTENT_IN_PROGRESS", stripeService, StringComparison.Ordinal);
        Assert.Contains("concurrent_claim", stripeService, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordCurrentStripeAuthorityWithoutRewritingPermanentContext()
    {
        Assert.Contains(
            "Stripe Payment Authority Certification (PASS SF-PAY4)",
            ReadRepoFile("docs/architecture/stripe-payment-authority-certification-findings.md"),
            StringComparison.Ordinal);

        var finalAudit = ReadRepoFile("docs/architecture/STRIPE_FINAL.md");
        var runtimeCertification = ReadRepoFile("docs/architecture/PAYMENT_RUNTIME_FINAL_CERTIFICATION.md");

        Assert.Contains("WITHHOLD CERTIFICATION", finalAudit, StringComparison.Ordinal);
        Assert.Contains("Provider-neutral Runtime preserved?", finalAudit, StringComparison.Ordinal);
        Assert.Contains("ARCHITECTURE FROZEN", runtimeCertification, StringComparison.Ordinal);
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
