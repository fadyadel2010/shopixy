namespace PointOfSale.Tests.Architecture;

public sealed class PaymentEndToEndAuthorityCertificationTests
{
    [Fact]
    public void CompleteStorefrontPaymentRuntime_MustKeepFrontendPresentationSelectionSdkRedirectWalletCandidateAndRefreshOnly()
    {
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var paymentSessionAuthority = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts");
        var paymentFlow = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentFlow.ts");
        var statusApi = ReadRepoFile("PointOfSale.Storefront/src/services/api/paymentStatusApi.ts");
        var stripeForm = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/StripePaymentForm.tsx");
        var paymobForm = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/PaymobPaymentForm.tsx");
        var manualFields = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ManualWalletFields.tsx");
        var checkoutSuccess = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/CheckoutSuccess.tsx");
        var checkoutTypes = ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");
        var checkoutPage = ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPage.tsx");

        Assert.Contains("function admitStorefrontPaymentMethods(payload: unknown): { status: CheckoutDependencyStatus; methods: PaymentMethodDto[] }", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const admittedResponse = admitStorefrontPaymentMethods(response.data);", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("requireStorefrontPaymentSessionAuthority(response.data, latestState.paymentMethod, cart.cartId)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("clearSensitiveCheckoutSessionStorage();", checkoutStore, StringComparison.Ordinal);

        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout store must keep an explicit localStorage partialize boundary.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout store partialize boundary must remain inspectable.");
        var localStoragePartialize = checkoutStore[partializeStart..partializeEnd];

        Assert.Contains("paymentMethod: state.paymentMethod", localStoragePartialize, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethodCode: state.selectedPaymentMethodCode", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("orderResult", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("paymentMethods", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("paymentIntentId", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checkoutToken", localStoragePartialize, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Backend checkout response mixed provider payment-session artifacts.", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("Terminal payment response unexpectedly included reusable provider payment-session artifacts.", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("complete Stripe handoff", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("complete Paymob handoff", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("complete Fawry payment-session handoff", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("pending ManualWallet payment-session evidence", paymentSessionAuthority, StringComparison.Ordinal);

        Assert.Contains("Frontend never treats Stripe.js status as final success.", stripeForm, StringComparison.Ordinal);
        Assert.Contains("Provider redirect values are evidence only", paymobForm, StringComparison.Ordinal);
        Assert.Contains("X-Manual-Wallet-Upload-Token", manualFields, StringComparison.Ordinal);
        Assert.Contains("order.paymentMethod?.toUpperCase() === 'FAWRY' && order.referenceCode", checkoutSuccess, StringComparison.Ordinal);
        Assert.Contains("'FAWRY'", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("window.location.href = buildAffiliateUrl(buildPendingCheckoutUrl(orderResult));", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("const pendingUrl = buildAffiliateUrl(buildPendingCheckoutUrl(orderResult));", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("window.location.replace(pendingUrl);", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("Only the backend webhook confirmation is authoritative", paymentFlow, StringComparison.Ordinal);
        Assert.Contains("return 'pending';", paymentFlow, StringComparison.Ordinal);
        Assert.Contains("requireCanonicalPaymentStatusForOrder(orderId, response.data)", statusApi, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendReadCheckoutSessionAndFinancialRuntime_MustOwnPaymentMethodSessionIntentPricingAndSqlPersistence()
    {
        var paymentMethodsController = ReadRepoFile("PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs");
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var checkoutDtos = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("[Route(\"api/storefront/payment-methods\")]", paymentMethodsController, StringComparison.Ordinal);
        Assert.Contains("SetNoStoreHeaders();", paymentMethodsController, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.TenantId is not Guid tenantId", paymentMethodsController, StringComparison.Ordinal);
        Assert.Contains("GetEnabledPaymentMethodsAsync", paymentMethodsController, StringComparison.Ordinal);
        Assert.Contains("IsSupportedStorefrontPaymentMethod(document.Code)", paymentMethodsController, StringComparison.Ordinal);

        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", checkoutController, StringComparison.Ordinal);
        Assert.Contains("ProcessCheckoutAsync", checkoutController, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"pricing\")]", checkoutController, StringComparison.Ordinal);
        Assert.Contains("public CheckoutExpectedPricingDto ExpectedPricing", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public Guid? PaymentIntentId", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public string PaymentStatus", checkoutDtos, StringComparison.Ordinal);

        Assert.Contains("CheckoutRequestIdempotencyCoordinator", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidatePaymentMethodAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LoadPaymentMethodAuthorityAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("lockAuthorityRow: true", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidatePaymentMethodOrderAmountLimits(paymentMethod, pricing.FinalTotal", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureCheckoutCurrencyAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentId = paymentIntentId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus = order.PaymentStatus.ToString()", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ResolvePaymentIntentExpirationAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PersistPaymentIntentCreatedOutboxAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("case \"FAWRY\":", checkoutService, StringComparison.Ordinal);
        Assert.Contains("\"FAWRY\" => PaymentProvider.Fawry", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ReferenceCode = paymentIntent?.ReferenceCode", checkoutService, StringComparison.Ordinal);

        Assert.Contains("OrderLockingHelper.LoadOrderWithLockAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("EnsurePaymentCurrencyConsistencyAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("Amount = order.Total", paymentService, StringComparison.Ordinal);
        Assert.Contains("Currency = validatedCurrency", paymentService, StringComparison.Ordinal);
        Assert.Contains("Provider = provider", paymentService, StringComparison.Ordinal);
        Assert.Contains("PaymentReference = paymentReference", paymentService, StringComparison.Ordinal);
        Assert.Contains("DUPLICATE_PAYMENT_INTENT", paymentService, StringComparison.Ordinal);
        Assert.Contains("IFawryRuntimeApplicationService", paymentService, StringComparison.Ordinal);
        Assert.Contains("ReferenceCode = result.InstructionIdentifier", paymentService, StringComparison.Ordinal);

        Assert.Contains("IX_PaymentIntents_Active_OrderId_Provider_Unique", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderVerificationAndLifecycleRuntime_MustOwnSuccessFailureStatusAuditAndReplayCollapse()
    {
        var stripeService = ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");
        var paymobService = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var stripeWebhooks = ReadRepoFile("PointOfSale.API/Controllers/StripeWebhooksController.cs");
        var paymobWebhooks = ReadRepoFile("PointOfSale.API/Controllers/PaymobWebhooksController.cs");
        var fawryWebhooks = ReadRepoFile("PointOfSale.API/Controllers/FawryWebhookController.cs");
        var fawryAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/Adapters/FawryPaymentAdapter.cs");
        var tenantResolver = ReadRepoFile("PointOfSale.API/Services/Payments/IPaymentWebhookTenantResolver.cs");
        var adminManual = ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs");
        var manualRuntime = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");
        var manualAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/Adapters/ManualWalletPaymentAdapter.cs");
        var routingMetadata = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentIntentRoutingMetadata.cs");
        var lifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var interceptor = ReadRepoFile("PointOfSale.Infrastructure/Data/PaymentStatusSaveChangesInterceptor.cs");

        Assert.Contains("Stripe-Signature", stripeWebhooks, StringComparison.Ordinal);
        Assert.Contains("EventUtility.ConstructEvent", stripeService, StringComparison.Ordinal);
        Assert.Contains("Source = PaymentLifecycleSource.GatewayWebhook", stripeService, StringComparison.Ordinal);
        Assert.Contains("ProviderAmount = providerAmount", stripeService, StringComparison.Ordinal);
        Assert.Contains("ProviderCurrency = providerCurrency", stripeService, StringComparison.Ordinal);
        Assert.Contains("WebhookEventId = stripeEvent.Id", stripeService, StringComparison.Ordinal);

        Assert.Contains("ValidateCallbackAsync", paymobWebhooks, StringComparison.Ordinal);
        Assert.Contains("Final success/failure is still decided by backend payment state polling.", paymobWebhooks, StringComparison.Ordinal);
        Assert.Contains("VerifyHmacSignature(canonicalHmacPayload, paymobConfig.HmacSecret, hmacSignature)", paymobService, StringComparison.Ordinal);
        Assert.Contains("Source = PaymentLifecycleSource.GatewayWebhook", paymobService, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentRoutingMetadataSerializer.MatchesStoredIntent", paymobService, StringComparison.Ordinal);
        Assert.Contains("Duplicate event ignored", stripeService, StringComparison.Ordinal);
        Assert.Contains("Duplicate event ignored", paymobService, StringComparison.Ordinal);

        Assert.Contains("[Route(\"api/webhooks/fawry\")]", fawryWebhooks, StringComparison.Ordinal);
        Assert.Contains("IFawryWebhookRuntimeIntegrationService", fawryWebhooks, StringComparison.Ordinal);
        Assert.Contains("EvaluateAndExecuteAsync", fawryWebhooks, StringComparison.Ordinal);
        Assert.Contains("FawryWebhookLifecycleAdmission.CreateRequest", fawryWebhooks, StringComparison.Ordinal);
        Assert.Contains("Duplicate event ignored", fawryWebhooks, StringComparison.Ordinal);
        Assert.Contains("FawryMerchantReference.Build(action)", fawryAdapter, StringComparison.Ordinal);
        Assert.Contains("\"fawry\" => await ResolveFawryAsync(root", tenantResolver, StringComparison.Ordinal);

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", adminManual, StringComparison.Ordinal);
        Assert.Contains("pi.Provider == PaymentProvider.ManualWallet", adminManual, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentReviewApplicationService.ApproveAsync(", adminManual, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentReviewApplicationService.RejectAsync(", adminManual, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleSource.PaymentReviewApproval", manualRuntime, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleSource.PaymentReviewRejection", manualRuntime, StringComparison.Ordinal);
        Assert.Contains("intent.Status = PaymentIntentStatus.AwaitingVerification", manualAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentIntentStatus.Paid", manualAdapter, StringComparison.Ordinal);

        Assert.Contains("MatchesStoredIntent", routingMetadata, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleOutcome.AmountMismatch", lifecycle, StringComparison.Ordinal);
        Assert.Contains("TryQuarantineProviderAuthorityMismatchAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("IsTerminalSqlPaymentTruth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Stale provider success rejected against terminal SQL truth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Stale provider failure collapsed against terminal SQL truth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("BuildOrderPaidIdempotencyKey", lifecycle, StringComparison.Ordinal);
        Assert.Contains("BuildGatewayCaptureIdempotencyKey", lifecycle, StringComparison.Ordinal);
        Assert.Contains("BuildWalletApprovedIdempotencyKey", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus can ONLY be changed via IPaymentLifecycleService", interceptor, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryCacheProjectionConcurrencyAndCrossSystemRuntime_MustReenterSqlPaymentAuthority()
    {
        var recoverPage = ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var activeContext = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/activeCheckoutOrderContext.ts");
        var checkoutSessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");
        var recoveryService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs");
        var paymentMethodRepository = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/PaymentMethodReadRepository.cs");
        var paymentMethodProjection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/PaymentMethodProjectionConsumer.cs");
        var orderProjection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/OrderProjectionConsumer.cs");
        var projectionSnapshotLoader = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Projection/StorefrontOrderProjectionAuthoritySnapshotLoader.cs");

        Assert.Contains("checkoutStore.reset();", recoverPage, StringComparison.Ordinal);
        Assert.Contains("requiresManualWalletReentry", recoverPage, StringComparison.Ordinal);
        Assert.Contains("manualWalletProofFileKey: null", recoverPage, StringComparison.Ordinal);
        Assert.Contains("can never be restored from", activeContext, StringComparison.Ordinal);
        Assert.Contains("return null;", activeContext, StringComparison.Ordinal);

        Assert.Contains("RecoverByToken", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("TryRevalidateRecoveredCartAsync", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("CheckoutToken = null", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("case \"PAYMOB\":", recoveryService, StringComparison.Ordinal);
        Assert.Contains("case \"STRIPE\":", recoveryService, StringComparison.Ordinal);
        Assert.Contains("case \"FAWRY\":", recoveryService, StringComparison.Ordinal);
        Assert.Contains("ExactlyOneDurableManualWalletIntent", recoveryService, StringComparison.Ordinal);
        Assert.Contains("PaymentProvider.ManualWallet", recoveryService, StringComparison.Ordinal);

        Assert.Contains("Payment method projection miss or stale; falling back to SQL", paymentMethodRepository, StringComparison.Ordinal);
        Assert.Contains("Payment method read model unavailable; falling back to SQL", paymentMethodRepository, StringComparison.Ordinal);
        Assert.Contains("NO SECRETS", paymentMethodRepository, StringComparison.Ordinal);
        Assert.Contains("Payment method projection rejected by SQL authority", paymentMethodProjection, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator.ValidateSqlVersion", paymentMethodProjection, StringComparison.Ordinal);

        Assert.Contains("Ignored payment projection replay without current SQL order payment lineage", orderProjection, StringComparison.Ordinal);
        Assert.Contains("Collapsed replayed payment facet payload against current SQL lineage", orderProjection, StringComparison.Ordinal);
        Assert.Contains("rebuilt missing projection from SQL source of truth", orderProjection, StringComparison.Ordinal);
        Assert.Contains("Deleted stale payment projection without current SQL order payment lineage", orderProjection, StringComparison.Ordinal);
        Assert.Contains("StorefrontOrderProjectionAuthoritySnapshot", projectionSnapshotLoader, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordCompletePaymentEndToEndTruth()
    {
        var findings = ReadRepoFile("docs/architecture/payment-end-to-end-authority-certification-findings.md");

        Assert.Contains(
            "Complete Payment End-to-End Authority Certification (PASS SF-PAY-END)",
            findings,
            StringComparison.Ordinal);
        Assert.Contains("Manual Payment, Vodafone Cash, Stripe, Paymob, and Fawry all operate inside one backend authority model", findings, StringComparison.Ordinal);
        Assert.Contains("| Fawry | Backend SQL `PaymentIntents`", findings, StringComparison.Ordinal);
        Assert.Contains("Final production provider list: Manual Payment, Vodafone Cash, Stripe, Paymob, Fawry.", findings, StringComparison.Ordinal);

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
