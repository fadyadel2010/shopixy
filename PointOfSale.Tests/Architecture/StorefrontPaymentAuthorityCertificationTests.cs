namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontPaymentAuthorityCertificationTests
{
    [Fact]
    public void PaymentIntentCreation_MustDeriveAmountCurrencyAndMethodFromSqlAuthority()
    {
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains("OrderLockingHelper.LoadOrderWithLockAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("Amount = order.Total", paymentService, StringComparison.Ordinal);
        Assert.Contains("Currency = validatedCurrency", paymentService, StringComparison.Ordinal);
        Assert.Contains("EnsurePaymentCurrencyConsistencyAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("CheckoutInventoryAuthorityGuard.EnsureStorefrontOrderReservationsActiveAsync", paymentService, StringComparison.Ordinal);

        Assert.Contains("ValidatePaymentMethodAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("_dbContext.TenantPaymentMethods", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodConfigurationValidator.Validate", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidatePaymentMethodOrderAmountLimits", checkoutService, StringComparison.Ordinal);
        Assert.DoesNotContain("Amount = request.Amount", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = request.Currency", paymentService, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderWebhooks_MustRequireStoredTenantSignatureEventIdAndProviderAmounts()
    {
        var pipeline = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentWebhookRequestPipeline.cs");
        var resolver = ReadRepoFile("PointOfSale.API/Services/Payments/IPaymentWebhookTenantResolver.cs");
        var stripe = ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");
        var paymob = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var lifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");

        Assert.Contains("Webhook signature is required.", pipeline, StringComparison.Ordinal);
        Assert.Contains("_tenantResolver.ResolveAsync(provider, rawPayload", pipeline, StringComparison.Ordinal);
        Assert.Contains("claimedTenantId.HasValue && claimedTenantId.Value != resolution.TenantId", pipeline, StringComparison.Ordinal);
        Assert.Contains("IgnoreQueryFilters()", resolver, StringComparison.Ordinal);

        Assert.Contains("EventUtility.ConstructEvent", stripe, StringComparison.Ordinal);
        Assert.Contains("PrepareWebhookLogAsync", stripe, StringComparison.Ordinal);
        Assert.Contains("FindPaymentIntentWithFallbackAsync", stripe, StringComparison.Ordinal);
        Assert.Contains("ProviderAmount = providerAmount", stripe, StringComparison.Ordinal);
        Assert.Contains("WebhookEventId = stripeEvent.Id", stripe, StringComparison.Ordinal);

        Assert.Contains("VerifyHmacSignature", paymob, StringComparison.Ordinal);
        Assert.Contains("PrepareWebhookLogAsync", paymob, StringComparison.Ordinal);
        Assert.Contains("ProviderAmount = providerAmount", paymob, StringComparison.Ordinal);
        Assert.Contains("WebhookEventId = transaction.Id.ToString()", paymob, StringComparison.Ordinal);

        Assert.Contains("TryQuarantineProviderAuthorityMismatchAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("provider_amount_missing", lifecycle, StringComparison.Ordinal);
        Assert.Contains("provider_currency_missing", lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentLifecycle_MustOwnStateMachineTerminalCollapseAndAccountingOutbox()
    {
        var lifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var interceptor = ReadRepoFile("PointOfSale.Infrastructure/Data/PaymentStatusSaveChangesInterceptor.cs");

        Assert.Contains("PaymentLifecycleOutcome.AmountMismatch => Task.FromResult(PaymentTransitionResult.Failed", lifecycle, StringComparison.Ordinal);
        Assert.Contains("IsTerminalSqlPaymentTruth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Stale provider success rejected against terminal SQL truth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Stale provider failure collapsed against terminal SQL truth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext", lifecycle, StringComparison.Ordinal);
        Assert.Contains("EnqueueSuccessOutboxAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PostCommittedCollectionAccountingFromSqlTruthAsync", lifecycle, StringComparison.Ordinal);

        Assert.Contains("PaymentStatus can ONLY be changed via IPaymentLifecycleService", interceptor, StringComparison.Ordinal);
        Assert.Contains("Unauthorized PaymentStatus change blocked", interceptor, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualWalletAndCod_MustRequirePrivilegedOrDeliveryAuthority()
    {
        var adminManual = ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs");
        var lifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var manualStateMachine = ReadRepoFile("PointOfSale.API/Services/Payments/ManualWalletStateMachine.cs");
        var reviewApplication = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");
        var fulfillment = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin,Manager\")]", adminManual, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", adminManual, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentReviewApplicationService.ApproveAsync", adminManual, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentReviewApplicationService.RejectAsync", adminManual, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualWalletStateMachine.EnsureValidTransition", adminManual, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleSource.PaymentReviewApproval", reviewApplication, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleSource.PaymentReviewRejection", reviewApplication, StringComparison.Ordinal);

        Assert.Contains("PaymentIntentStatus.AwaitingVerification", manualStateMachine, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentStatus.Rejected", manualStateMachine, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentStatus.Paid", manualStateMachine, StringComparison.Ordinal);

        Assert.Contains("PaymentLifecycleSource.OfflineCollection when order.PaymentStatus != PaymentStatus.Pending", lifecycle, StringComparison.Ordinal);
        Assert.Contains("MarkOfflineCollectionDeliveredAsync", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("order.PaymentStatus = PaymentStatus.Collected", fulfillment, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementAndReplay_MustRemainTenantScopedAndSqlIdempotent()
    {
        var lifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("LoadOrdersForSettlementMutationAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("orders.Any(order => order.TenantId != tenantId)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("CurrencyEquals(order.Currency, normalizedCurrency)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("SettlementBatchMatches", lifecycle, StringComparison.Ordinal);
        Assert.Contains("EnsureCollectionAccountingPostedForCodSettlementAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("EnsureCollectionAccountingPostedForGatewaySettlementAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("BuildCodSettlementIdempotencyKey", lifecycle, StringComparison.Ordinal);
        Assert.Contains("BuildGatewaySettlementIdempotencyKey", lifecycle, StringComparison.Ordinal);

        Assert.Contains("IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_PaymentIntents_Active_OrderId_Provider_Unique", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordP2DTruth()
    {
        Assert.Contains(
            "PASS P2-D Storefront Payment Authority",
            ReadRepoFile("docs/architecture/storefront-payment-authority-certification-findings.md"),
            StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     ".ai-context/PROJECT_CONTEXT.md",
                     ".ai-context/ARCHITECTURE_TRUTH.md",
                     ".ai-context/KNOWN_RISKS.md",
                     ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
                     ".ai-context/CODEX_RULES.md"
                 })
        {
            Assert.Contains("PASS P2-D", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
