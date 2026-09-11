namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPaymentAuthorityCertificationTests
{
    [Fact]
    public void PaymentIdentityAndCollection_MustRemainProviderVerifiedSqlOwnedAndIdempotent()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubPaymentsController.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayment()", service, StringComparison.Ordinal);
        Assert.Contains("ResolveMarketplaceOrderAsync", service, StringComparison.Ordinal);
        Assert.Contains("EnsureInitiationAccessAllowed", service, StringComparison.Ordinal);
        Assert.Contains("EnsureInitiationAllowedAsync", service, StringComparison.Ordinal);
        Assert.Contains("EnsureMarketplaceOrderReservationsActiveAsync", service, StringComparison.Ordinal);
        Assert.Contains("BuildPaymentLockKey", service, StringComparison.Ordinal);
        Assert.Contains("EnsureNoUnresolvedPaymentAsync", service, StringComparison.Ordinal);
        Assert.Contains("CreatePendingPayment", service, StringComparison.Ordinal);
        Assert.Contains("EnsurePaymentItems", service, StringComparison.Ordinal);
        Assert.Contains("EnsureProviderIdentityNotLinkedToAnotherPaymentAsync", service, StringComparison.Ordinal);
        Assert.Contains("GetProviderAdapter(CommerceHubPaymentMethods.Stripe).ParseWebhookAsync", service, StringComparison.Ordinal);
        Assert.Contains("GetProviderAdapter(CommerceHubPaymentMethods.Paymob).ParseWebhookAsync", service, StringComparison.Ordinal);
        Assert.Contains("ParseCallbackAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("VerifyPaymobCallbackHmac", service, StringComparison.Ordinal);
        Assert.Contains("EnsureAmountMatches(processedPayment, providerAmount, providerCurrency", service, StringComparison.Ordinal);
        Assert.Contains("EnsureIncomingProviderIdentityMatchesPayment", service, StringComparison.Ordinal);
        Assert.Contains("Payment is already being processed by another worker.", service, StringComparison.Ordinal);
        Assert.Contains("Marketplace order is already paid. Captured duplicate payment queued for refund compensation.", service, StringComparison.Ordinal);
        Assert.Contains("PersistUnresolvedPaymentWebhookAsync", service, StringComparison.Ordinal);

        Assert.Contains("[AllowAnonymousTenant]", controller, StringComparison.Ordinal);
        Assert.Contains("Webhook signature is required.", controller, StringComparison.Ordinal);
        Assert.Contains("WebhookContentTypeValidationFilter", controller, StringComparison.Ordinal);
        Assert.Contains("WebhookRateLimiterPolicy.PolicyName", controller, StringComparison.Ordinal);

        Assert.Contains("IX_CommerceHubPayments_OrderId_SinglePaid", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubPayments_Provider_ProviderIntentId", dbContext, StringComparison.Ordinal);
        Assert.Contains("entity.HasIndex(payment => payment.ProviderReference).IsUnique()", dbContext, StringComparison.Ordinal);
        Assert.Contains("entity.HasIndex(payment => payment.ProviderTransactionId)", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubPayments_Paid_State_Complete", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentLifecycleEarningsFeesEscrowAndCurrency_MustDeriveFromPaymentAndSqlSnapshots()
    {
        var lifecycle = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentLifecycleAuthority.cs");
        var workflow = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinancialWorkflowService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var payout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var readiness = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialReadinessGate.cs");

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPaymentLifecycle()", lifecycle, StringComparison.Ordinal);
        Assert.Contains("EnsureCanCollect", lifecycle, StringComparison.Ordinal);
        Assert.Contains("EnsureCanRefund", lifecycle, StringComparison.Ordinal);
        Assert.Contains("AggregateMarketplacePaymentStatus", lifecycle, StringComparison.Ordinal);

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginFinancialWorkflow()", workflow, StringComparison.Ordinal);
        Assert.Contains("ApplyOnlinePaymentSuccessAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("PaymentId = paymentId", workflow, StringComparison.Ordinal);
        Assert.Contains("subOrder.VendorEarningAmount", workflow, StringComparison.Ordinal);
        Assert.Contains("subOrder.PlatformCommissionAmount", workflow, StringComparison.Ordinal);
        Assert.Contains("ValidateCommissionSnapshotJsonMatchesFields", workflow, StringComparison.Ordinal);
        Assert.Contains("EnsureFinancialSnapshotIntegrity", workflow, StringComparison.Ordinal);
        Assert.Contains("RecordOrderAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_EARNING_PAYMENT_MISMATCH", workflow, StringComparison.Ordinal);
        Assert.Contains("Refunded payments cannot create or unlock vendor earnings.", workflow, StringComparison.Ordinal);

        Assert.Contains("entity.HasIndex(earning => earning.SubOrderId)", dbContext, StringComparison.Ordinal);
        Assert.Contains("OriginalEarningId\\\" IS NULL AND \\\"IsDeleted\\\" = false", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubVendorEarnings_PayoutAmounts_Within_Positive_Earning", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedEntityCreation<CommerceHubVendorEarning>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubVendorEarning>", dbContext, StringComparison.Ordinal);

        Assert.Contains("settlement delay", payout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AvailableAtUtc", payout, StringComparison.Ordinal);
        Assert.Contains("PayoutReservedAmount", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureCommerceIntakeAllowedAsync", readiness, StringComparison.Ordinal);
    }

    [Fact]
    public void RefundReconciliationTenantAndHistoricalConsistency_MustRemainDerivativeAndFailClosed()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var refund = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs");
        var reconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialReconciliationService.cs");
        var unresolvedRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubUnresolvedPaymentReconciliationHostedService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("ApplyProviderRefundWebhookAsync", service, StringComparison.Ordinal);
        Assert.Contains("providerAmountIsCumulativeRefunded", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentCompensationOutboxMessages", service, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_REFUND_AMOUNT_EXCEEDS_REMAINING", service, StringComparison.Ordinal);
        Assert.Contains("lacks immutable payment-item authority", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NeedsManualCurrencyReview", service, StringComparison.Ordinal);
        Assert.Contains("LoadCommerceHubPaymentForProviderCallbackMutationAsync", service, StringComparison.Ordinal);
        Assert.Contains("LoadMarketplaceOrderForProviderCallbackMutationAsync", service, StringComparison.Ordinal);
        Assert.Contains("EnsurePaymentMatchesMarketplaceOrder", service, StringComparison.Ordinal);

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginRefund()", refund, StringComparison.Ordinal);
        Assert.Contains("ReconciliationRequired", refund, StringComparison.Ordinal);
        Assert.Contains("VendorEarningReversalAmount", refund, StringComparison.Ordinal);

        Assert.Contains("ValidateExternalMoneyLifecycle", reconciliation, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPayments", reconciliation, StringComparison.Ordinal);
        Assert.Contains("CompletedRefundsByPaymentId", reconciliation, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialTransactions", reconciliation, StringComparison.Ordinal);
        Assert.Contains("ResolveLinkedUnresolvedCapturedPaymentsAsync", unresolvedRecovery, StringComparison.Ordinal);

        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubPayment>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubPaymentItem>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubPaymentLog>", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentCurrency_MustRemainEgpAcrossGatewayCallbacksRecoveryAndPersistence()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var refundGateway = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundGatewayService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("RequireCommerceHubPaymentCurrency", service, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_PAYMENT_CURRENCY_INVALID", service, StringComparison.Ordinal);
        Assert.Contains("unresolved Paymob webhook", service, StringComparison.Ordinal);
        Assert.Contains("unresolved Stripe webhook", service, StringComparison.Ordinal);
        Assert.Contains("gateway callback", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentMethods.Stripe", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentMethods.Paymob", service, StringComparison.Ordinal);
        Assert.DoesNotContain("UsesZeroDecimalCurrency", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveUnresolvedProviderCurrency", service, StringComparison.Ordinal);

        Assert.Contains("CommerceHub gateway refunds require", refundGateway, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", refundGateway, StringComparison.Ordinal);

        Assert.Contains("GuardCommerceHubCurrencySovereignty", dbContext, StringComparison.Ordinal);
        Assert.Contains("CommerceHub currency sovereignty violation blocked", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubPayments_Currency_EGP", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubUnresolvedPaymentWebhooks_Currency_EGP", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubRefunds_Currency_EGP", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordM3CommerceHubPaymentAuthorityTruth()
    {
        var findings = ReadRepoFile("docs/architecture/commercehub-payment-authority-certification-findings.md");

        Assert.Contains("PASS M3 CommerceHub Payment Authority", findings, StringComparison.Ordinal);
        Assert.Contains("Payment identity findings", findings, StringComparison.Ordinal);
        Assert.Contains("Vendor earnings findings", findings, StringComparison.Ordinal);
        Assert.Contains("Marketplace fee findings", findings, StringComparison.Ordinal);
        Assert.Contains("Escrow findings", findings, StringComparison.Ordinal);
        Assert.Contains("Is CommerceHub Payment Authority production-certifiable? Yes", findings, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
