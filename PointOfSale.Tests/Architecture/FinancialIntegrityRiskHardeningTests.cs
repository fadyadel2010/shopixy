using System.Text.RegularExpressions;

namespace PointOfSale.Tests.Architecture;

public sealed class FinancialIntegrityRiskHardeningTests
{
    [Fact]
    public void RefundMutation_MustLoadPostgresConcurrencyTokenAndReturnSafeFailures()
    {
        var lifecycle = File.ReadAllText(InfrastructurePath("Payments", "PaymentLifecycleService.cs"));
        var refundMethod = ExtractMethodBody(lifecycle, "RecordRefundAsync");
        var refundLock = ExtractMethodBody(lifecycle, "LoadOrderForRefundMutationAsync");
        var controller = File.ReadAllText(ApiPath("Controllers", "PaymentLifecycleController.cs"));
        var refundEndpoint = ExtractMethodBody(controller, "RecordRefund");

        Assert.NotNull(refundMethod);
        Assert.NotNull(refundLock);
        Assert.NotNull(refundEndpoint);
        Assert.Contains("SELECT xmin, * FROM \"\"Orders\"\"", refundLock!, StringComparison.Ordinal);
        Assert.Contains("REFUND_PROCESSING_FAILED", refundMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("Internal error: {ex.Message}", refundMethod!, StringComparison.Ordinal);
        Assert.Contains("return SettlementFailure(result);", refundEndpoint!, StringComparison.Ordinal);
        Assert.Contains("REFUND_AMOUNT_INVALID", refundEndpoint!, StringComparison.Ordinal);
        Assert.Contains("REFUND_ID_REQUIRED", refundEndpoint!, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPaymentWebhook_MustRevalidateProviderMoneyInsideSerializableCommit()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var method = ExtractMethodBody(source, "ApplyWebhookOutcomeAsync");
        Assert.NotNull(method);

        Assert.Contains("decimal providerAmount", method!, StringComparison.Ordinal);
        Assert.Contains("string providerCurrency", method!, StringComparison.Ordinal);
        AssertContainsInOrder(
            method!,
            "BeginTransactionAsync(IsolationLevel.Serializable",
            "processedPayment = await LoadCommerceHubPaymentForProviderCallbackMutationAsync",
            "var marketplaceOrder = await LoadMarketplaceOrderForProviderCallbackMutationAsync",
            "EnsurePaymentMatchesMarketplaceOrder(processedPayment, marketplaceOrder)",
            "EnsureAmountMatches(processedPayment, providerAmount, providerCurrency, providerCode)");

        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", source, StringComparison.Ordinal);
        Assert.Contains("LoadCommerceHubPaymentForProviderCallbackMutationAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadMarketplaceOrderForProviderCallbackMutationAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentLifecycle_MustCollapseDelayedProviderCallbacksAgainstTerminalSqlTruth()
    {
        var lifecycle = File.ReadAllText(InfrastructurePath("Payments", "PaymentLifecycleService.cs"));
        var successMethod = ExtractMethodBody(lifecycle, "HandleSuccessAsync");
        var failureMethod = ExtractMethodBody(lifecycle, "HandleFailureAsync");
        var staleSuccessGuard = ExtractMethodBody(lifecycle, "TryRejectStaleProviderSuccessAgainstTerminalSqlTruth");
        var staleFailureGuard = ExtractMethodBody(lifecycle, "TryCollapseStaleProviderFailureAgainstTerminalSqlTruth");

        Assert.NotNull(successMethod);
        Assert.NotNull(failureMethod);
        Assert.NotNull(staleSuccessGuard);
        Assert.NotNull(staleFailureGuard);

        AssertContainsInOrder(
            successMethod!,
            "var aggregate = await LoadAggregateAsync(request, ct)",
            "var staleSuccessResult = TryRejectStaleProviderSuccessAgainstTerminalSqlTruth",
            "if (staleSuccessResult != null)");

        AssertContainsInOrder(
            failureMethod!,
            "var aggregate = await LoadAggregateAsync(request, ct)",
            "var staleFailureResult = TryCollapseStaleProviderFailureAgainstTerminalSqlTruth",
            "if (staleFailureResult != null)");

        Assert.Contains("STALE_PROVIDER_SUCCESS_REJECTED", staleSuccessGuard!, StringComparison.Ordinal);
        Assert.Contains("PaymentTransitionResult.ManualReviewRequired", staleSuccessGuard!, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Refunded", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.PartiallyRefunded", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Failed", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Rejected", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Expired", lifecycle, StringComparison.Ordinal);

        Assert.Contains("PaymentTransitionResult.Succeeded", staleFailureGuard!, StringComparison.Ordinal);
        Assert.Contains("wasIdempotent: true", staleFailureGuard!, StringComparison.Ordinal);
        Assert.Contains("stateChanged: false", staleFailureGuard!, StringComparison.Ordinal);
        Assert.Contains("PaymentInventoryAction.None", staleFailureGuard!, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentLifecycle_PostCommitAccountingFastPath_MustReloadSqlTruthBeforeLedgerMutation()
    {
        var lifecycle = File.ReadAllText(InfrastructurePath("Payments", "PaymentLifecycleService.cs"));
        var successMethod = ExtractMethodBody(lifecycle, "HandleSuccessAsync");
        var refundMethod = ExtractMethodBody(lifecycle, "RecordRefundAsync");
        var gatewaySettlementMethod = ExtractMethodBody(lifecycle, "RecordGatewaySettlementAsync");
        var codSettlementMethod = ExtractMethodBody(lifecycle, "RecordOfflineSettlementAsync");
        var collectionHelper = ExtractMethodBody(lifecycle, "PostCommittedCollectionAccountingFromSqlTruthAsync");
        var refundHelper = ExtractMethodBody(lifecycle, "PostCommittedRefundAccountingFromSqlTruthAsync");
        var gatewaySettlementHelper = ExtractMethodBody(lifecycle, "PostCommittedGatewaySettlementAccountingFromSqlTruthAsync");
        var codSettlementHelper = ExtractMethodBody(lifecycle, "PostCommittedCodSettlementAccountingFromSqlTruthAsync");
        var settlementAuthorityHelper = ExtractMethodBody(lifecycle, "LoadRequiredSettlementAuthorityForAccountingPostAsync");

        Assert.NotNull(successMethod);
        Assert.NotNull(refundMethod);
        Assert.NotNull(gatewaySettlementMethod);
        Assert.NotNull(codSettlementMethod);
        Assert.NotNull(collectionHelper);
        Assert.NotNull(refundHelper);
        Assert.NotNull(gatewaySettlementHelper);
        Assert.NotNull(codSettlementHelper);
        Assert.NotNull(settlementAuthorityHelper);

        Assert.Contains("PostCommittedCollectionAccountingFromSqlTruthAsync", successMethod!, StringComparison.Ordinal);
        Assert.Contains("PostCommittedRefundAccountingFromSqlTruthAsync", refundMethod!, StringComparison.Ordinal);
        Assert.Contains("PostCommittedGatewaySettlementAccountingFromSqlTruthAsync", gatewaySettlementMethod!, StringComparison.Ordinal);
        Assert.Contains("PostCommittedCodSettlementAccountingFromSqlTruthAsync", codSettlementMethod!, StringComparison.Ordinal);

        AssertContainsInOrder(
            collectionHelper!,
            "_db.ChangeTracker.Clear()",
            "BeginLifecycleTransactionAsync",
            "LoadOrderForPaymentLifecycleMutationAsync",
            "LoadRequiredAccountingOutboxMessageByIdAsync",
            "BuildCollectionAccountingRequestFromSqlTruth",
            "ResolveCollectionAccountingAmountFromSqlTruth",
            "PostSuccessfulCollectionAccountingAsync",
            "accountingMessage.MarkProcessed",
            "await _db.SaveChangesAsync(ct)",
            "await accountingTransaction.CommitAsync(ct)");

        AssertContainsInOrder(
            refundHelper!,
            "_db.ChangeTracker.Clear()",
            "BeginLifecycleTransactionAsync",
            "LoadOrderForRefundMutationAsync",
            "AccountingLedgerAuthority.LoadCurrentRefundAsync",
            "AccountingLedgerAuthority.EvaluateRefundPosting",
            "BuildRefundAccountingContextFromSqlTruth",
            "_commerceAccounting.PostEcommerceRefundAsync",
            "accountingMessage.MarkProcessed",
            "await _db.SaveChangesAsync(ct)",
            "await accountingTransaction.CommitAsync(ct)");

        AssertContainsInOrder(
            gatewaySettlementHelper!,
            "_db.ChangeTracker.Clear()",
            "BeginLifecycleTransactionAsync",
            "LoadSettlementBatchForAccountingPostAsync",
            "LoadRequiredSettlementAuthorityForAccountingPostAsync",
            "_commerceAccounting.PostGatewaySettlementAsync",
            "accountingMessage.MarkProcessed",
            "await _db.SaveChangesAsync(ct)",
            "await accountingTransaction.CommitAsync(ct)");

        AssertContainsInOrder(
            codSettlementHelper!,
            "_db.ChangeTracker.Clear()",
            "BeginLifecycleTransactionAsync",
            "LoadSettlementBatchForAccountingPostAsync",
            "LoadRequiredSettlementAuthorityForAccountingPostAsync",
            "_commerceAccounting.PostCodSettlementAsync",
            "accountingMessage.MarkProcessed",
            "await _db.SaveChangesAsync(ct)",
            "await accountingTransaction.CommitAsync(ct)");

        AssertContainsInOrder(
            settlementAuthorityHelper!,
            "AccountingLedgerAuthority.LoadCurrentSettlementAsync",
            "AccountingLedgerAuthority.EvaluateSettlementPosting");

        Assert.Contains("FOR UPDATE", lifecycle, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", lifecycle, StringComparison.Ordinal);
        Assert.Contains("IsCollectionAccountingEligibleFromSqlTruth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("BuildCollectionAccountingRequestFromSqlTruth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("TryGetPayloadString(message.PayloadJson", lifecycle, StringComparison.Ordinal);
        Assert.Contains("TryGetPayloadDecimal(message.PayloadJson", lifecycle, StringComparison.Ordinal);
        Assert.Contains("BuildRefundAccountingContextFromSqlTruth", lifecycle, StringComparison.Ordinal);
        Assert.Contains("EnsureSettlementBatchRetainsCurrentOrderLineageAsync(batch, order.Id, ct)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("hasCurrentPostSettlementRefundHistory", lifecycle, StringComparison.Ordinal);
        Assert.Contains("order.PaymentStatus == PaymentStatus.Settled || hasCurrentPostSettlementRefundHistory", lifecycle, StringComparison.Ordinal);
        Assert.Contains("LoadRequiredSettlementAuthorityForAccountingPostAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("EnsureSettlementBatchRetainsCurrentOrderLineageAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentSettlementOrders", lifecycle, StringComparison.Ordinal);
        Assert.Contains("settlementBatch.OrderCount != currentOrderCount", lifecycle, StringComparison.Ordinal);
        Assert.Contains("fees < 0m || fees > grossAmount", lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontPaymentReplay_MustDeleteMissingSqlLineage_AndSuppressConflictingTimelineNarrative()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "OrderProjectionConsumer.cs"));
        var paymentRegionStart = consumer.IndexOf("#region OrderPaymentStatusChangedEvent", StringComparison.Ordinal);
        var paymentRegionEnd = paymentRegionStart >= 0
            ? consumer.IndexOf("#endregion", paymentRegionStart, StringComparison.Ordinal)
            : -1;
        var consumeMethod = paymentRegionStart >= 0 && paymentRegionEnd > paymentRegionStart
            ? consumer[paymentRegionStart..paymentRegionEnd]
            : null;
        var deleteMethod = ExtractMethodBody(consumer, "DeletePaymentProjectionWithoutSqlAuthorityAsync");

        Assert.NotNull(consumeMethod);
        Assert.NotNull(deleteMethod);
        AssertContainsInOrder(
            consumeMethod!,
            "OrderPaymentLineageAuthority.LoadCurrentVisibilityAsync",
            "if (paymentAuthority == null)",
            "DeletePaymentProjectionWithoutSqlAuthorityAsync",
            "var eventMatchesCurrentPaymentTruth = paymentAuthority.PaymentStatus == @event.NewStatus",
            "if (eventMatchesCurrentPaymentTruth)",
            "Builders<OrderDocument>.Update.Push(x => x.Timeline, timelineEntry)");

        Assert.DoesNotContain(
            "Set(x => x.PaymentStatus, @event.NewStatus.ToString())",
            consumeMethod!,
            StringComparison.Ordinal);
        AssertContainsInOrder(
            deleteMethod!,
            "DeleteOneAsync",
            "OperationalConvergenceDecision.Deleted",
            "OperationalConvergenceReason.MissingSqlAuthority",
            "InvalidateCacheAsync");
    }

    [Fact]
    public void CheckoutCon14_CommerceHubUnresolvedPaymentRecovery_MustRecomposeLockedSqlTruthBeforeClearance()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubFinanceReconciliationJob.cs"));
        var method = ExtractMethodBody(source, "ResolveLinkedUnresolvedCapturedPaymentsAsync");

        Assert.NotNull(method);
        AssertContainsInOrder(
            method!,
            ".AsNoTracking()",
            ".Select(webhook => webhook.Id)",
            "BeginTransactionAsync(",
            "IsolationLevel.Serializable",
            "LoadUnresolvedPaymentWebhookForRecoveryAsync",
            "ResolvePaidPaymentForWebhookAsync",
            "LoadMarketplaceOrderForRecoveryAsync",
            "QueueSuspenseClearanceAsync(webhook, payment, marketplaceOrder",
            "ReleaseMarketplaceHoldAsync(marketplaceOrder",
            "await transaction.CommitAsync");

        Assert.Contains("LoadPaidPaymentForRecoveryAsync", source, StringComparison.Ordinal);
        Assert.Contains("LockCommerceHubPaymentForRecoveryAsync", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", source, StringComparison.Ordinal);
        Assert.Contains("IsUnresolvedPaymentRecoveryCandidate", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutCon14_StorefrontUnresolvedPaymentRecovery_MustRecomposeLockedSqlTruthBeforeLifecycleAndAccounting()
    {
        var source = File.ReadAllText(ApiPath("Services", "Payments", "StorefrontUnresolvedPaymentReconciliationJob.cs"));
        var method = ExtractMethodBody(source, "ExecuteAsync");

        Assert.NotNull(method);
        AssertContainsInOrder(
            method!,
            ".AsNoTracking()",
            ".Select(log => log.Id)",
            "BeginTransactionAsync(",
            "IsolationLevel.Serializable",
            "LoadWebhookLogForRecoveryAsync",
            "LoadPaymentIntentForRecoveryAsync",
            "LoadOrderForRecoveryAsync",
            "_paymentLifecycleService.HandlePaymentResultAsync",
            "_accountingOutbox.EnqueueResolvedUnresolvedOnlinePaymentAsync",
            "await transaction.CommitAsync");

        Assert.Contains("IsRecoverableSuspenseLog", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutCon14_PayoutReconciliationQuarantine_MustLockCurrentPayoutTruthBeforeStatusMutation()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutReconciliationHostedService.cs"));
        var method = ExtractMethodBody(source, "QuarantineStaleProcessingPayoutsAsync");

        Assert.NotNull(method);
        AssertContainsInOrder(
            method!,
            ".AsNoTracking()",
            ".Select(payout => payout.Id)",
            "BeginTransactionAsync(",
            "IsolationLevel.Serializable",
            "LoadPayoutForReconciliationAsync",
            "payout.Status != CommerceHubPayoutStatus.Processing",
            "payout.Status = CommerceHubPayoutStatus.ReconciliationRequired",
            "await transaction.CommitAsync");

        Assert.Contains("LoadPayoutForReconciliationAsync", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubRefundCompletion_MustRejectAggregateRefundOverrunsAtCommitBoundary()
    {
        var refundService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));
        var refundExecution = File.ReadAllText(ApiPath("Services", "CommerceHub", "RefundExecutionService.cs"));
        var refundValidator = File.ReadAllText(ApiPath("Services", "CommerceHub", "RefundCompletionValidator.cs"));

        foreach (var source in new[] { refundService, refundExecution })
        {
            var completeMethod = ExtractMethodBody(source, "CompleteRefundAsync");
            Assert.NotNull(completeMethod);

            AssertContainsInOrder(
                completeMethod!,
                "EnsureRefundCompletionDoesNotOverrunAsync",
                "trackedRefund.Status = CommerceHubRefundStatus.Completed",
                "payment.RefundedAmount = RoundCurrency(payment.RefundedAmount + trackedRefund.Amount)",
                "_financialLedgerService.RecordRefundAsync");
        }

        Assert.Contains("committedOrReservedSubOrderRefunds", refundValidator, StringComparison.Ordinal);
        Assert.Contains("committedOrReservedPaymentRefunds", refundValidator, StringComparison.Ordinal);
        Assert.Contains("REFUND_COMPLETION_LIMIT_EXCEEDED", refundValidator, StringComparison.Ordinal);
        Assert.Contains("REFUND_PAYMENT_COMPLETION_LIMIT_EXCEEDED", refundValidator, StringComparison.Ordinal);
        Assert.Contains("refund.Status != CommerceHubRefundStatus.Failed", refundValidator, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubMoneyTables_MustHaveDatabaseLevelFinancialInvariants()
    {
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260429100000_S49_CrossSystemCriticalRiskClosure.sql"));

        foreach (var source in new[] { dbContext, migration })
        {
            Assert.Contains("CK_CommerceHubPayments_Amount_Positive", source, StringComparison.Ordinal);
            Assert.Contains("CK_CommerceHubPayments_RefundedAmount_Range", source, StringComparison.Ordinal);
            Assert.Contains("CK_CommerceHubPayments_Paid_State_Complete", source, StringComparison.Ordinal);
            Assert.Contains("CK_CommerceHubRefunds_Amount_Positive", source, StringComparison.Ordinal);
            Assert.Contains("CK_CommerceHubRefunds_Reversals_NonNegative", source, StringComparison.Ordinal);
            Assert.Contains("CK_CommerceHubRefunds_Reversals_Total_Amount", source, StringComparison.Ordinal);
            Assert.Contains("CK_CommerceHubRefunds_Completed_State_Complete", source, StringComparison.Ordinal);
            Assert.Contains("CK_CommerceHubPayouts_Amount_Positive", source, StringComparison.Ordinal);
            Assert.Contains("CK_CommerceHubPayouts_Completed_State_Complete", source, StringComparison.Ordinal);
            Assert.Contains("CK_CommerceHubPayouts_Completed_Evidence", source, StringComparison.Ordinal);
        }

        Assert.Contains("NOT VALID", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubLedgerRecorder_MustHealPartialReferenceLedgers()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "FinancialLedger", "CommerceHubFinancialLedgerService.cs"));
        var configuration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "CommerceHubFinancialTransactionConfiguration.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260427170000_S42_CommerceHubLedgerFingerprintIdempotency.sql"));

        foreach (var methodName in new[] { "RecordOrderAsync", "RecordRefundAsync", "RecordPayoutAsync" })
        {
            var method = ExtractMethodBody(source, methodName);
            Assert.NotNull(method);

            Assert.DoesNotContain("HasReferenceEntriesAsync", method!, StringComparison.Ordinal);
            Assert.Contains("AppendTransactionIfMissingAsync", method!, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("private async Task<bool> HasReferenceEntriesAsync", source, StringComparison.Ordinal);
        Assert.Contains("entry.ReferenceType == transaction.ReferenceType", source, StringComparison.Ordinal);
        Assert.Contains("entry.Direction == transaction.Direction", source, StringComparison.Ordinal);
        Assert.Contains("entry.FinancialBucket == transaction.FinancialBucket", source, StringComparison.Ordinal);
        Assert.Contains("entry.Amount == transaction.Amount", source, StringComparison.Ordinal);
        Assert.Contains("entry.Currency == transaction.Currency", source, StringComparison.Ordinal);
        Assert.Contains("EnsureIdempotencyKeyMatchesFinancialFingerprint", source, StringComparison.Ordinal);
        Assert.Contains("HasSameFinancialFingerprint", source, StringComparison.Ordinal);
        Assert.Contains("idempotency key collision", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UX_CHFinancialTx_", source, StringComparison.Ordinal);
        Assert.Contains("UX_CHFinancialTx_Fingerprint_Vendor", configuration, StringComparison.Ordinal);
        Assert.Contains("UX_CHFinancialTx_Fingerprint_Platform", configuration, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS \"UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_EntityId\"", migration, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS \"UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_Platform\"", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"UX_CHFinancialTx_Fingerprint_Vendor\"", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"UX_CHFinancialTx_Fingerprint_Platform\"", migration, StringComparison.Ordinal);
        Assert.Contains("RecordOrderAsync_WhenReferenceHasPartialLedger_ShouldAppendMissingEntries", File.ReadAllText(TestsPath("CommerceHub", "CommerceHubFinancialLedgerServiceTests.cs")), StringComparison.Ordinal);
        Assert.Contains("RecordRefundAsync_WhenReferenceHasPartialLedger_ShouldAppendMissingEntries", File.ReadAllText(TestsPath("CommerceHub", "CommerceHubFinancialLedgerServiceTests.cs")), StringComparison.Ordinal);
        Assert.Contains("RecordPayoutAsync_WhenReferenceHasPartialLedger_ShouldAppendMissingEntries", File.ReadAllText(TestsPath("CommerceHub", "CommerceHubFinancialLedgerServiceTests.cs")), StringComparison.Ordinal);
        Assert.Contains("RecordTransactionAsync_WhenExistingReferenceHasDifferentDirection_ShouldAppendCorrectEntry", File.ReadAllText(TestsPath("CommerceHub", "CommerceHubFinancialLedgerServiceTests.cs")), StringComparison.Ordinal);
        Assert.Contains("RecordTransactionAsync_ReusedIdempotencyKeyWithDifferentFinancialFingerprint_ShouldThrow", File.ReadAllText(TestsPath("CommerceHub", "CommerceHubFinancialLedgerServiceTests.cs")), StringComparison.Ordinal);
        Assert.Contains("RecordTransactionAsync_DefaultIdempotencyKeyWithDifferentAmount_ShouldThrow", File.ReadAllText(TestsPath("CommerceHub", "CommerceHubFinancialLedgerServiceTests.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPaymentProviderIdentity_MustBeUniqueBeforeFinancialCapture()
    {
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260427183000_S43_CommerceHubPaymentProviderIdentityUniqueness.sql"));

        Assert.Contains("UX_CommerceHubPayments_Provider_ProviderIntentId", dbContext, StringComparison.Ordinal);
        Assert.Contains(".IsUnique()", dbContext, StringComparison.Ordinal);
        Assert.Contains("EnsureProviderIdentityNotLinkedToAnotherPaymentAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("EnsureIncomingProviderIdentityMatchesPayment", paymentService, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_PROVIDER_INTENT_ALREADY_LINKED", paymentService, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_PROVIDER_TRANSACTION_ALREADY_LINKED", paymentService, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_PROVIDER_IDENTITY_CONFLICT", paymentService, StringComparison.Ordinal);
        Assert.Contains("IsProviderPaymentIdentityUniqueViolation", paymentService, StringComparison.Ordinal);
        Assert.Contains("PostgresErrorCodes.UniqueViolation", paymentService, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS \"IX_CommerceHubPayments_Provider_ProviderIntentId\"", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"UX_CommerceHubPayments_Provider_ProviderIntentId\"", migration, StringComparison.Ordinal);
        Assert.Contains("GROUP BY \"Provider\", \"ProviderIntentId\"", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementAccountingOutbox_MustUseDurableSettlementBatchAsBusinessEntity()
    {
        var contract = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "PointOfSale.Core", "Interfaces", "Accounting", "IAccountingOutboxService.cs")));
        var outbox = File.ReadAllText(InfrastructurePath("Accounting", "AccountingOutboxService.cs"));
        var lifecycle = File.ReadAllText(InfrastructurePath("Payments", "PaymentLifecycleService.cs"));
        var events = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "PointOfSale.Core", "Events", "PaymentLifecycleEvents.cs")));

        Assert.Contains("Task EnqueueGatewaySettlementAsync(Guid tenantId, Guid settlementBatchId, string gateway, decimal grossAmount, decimal fees, string settlementId, CancellationToken ct = default);", contract, StringComparison.Ordinal);
        Assert.Contains("Task EnqueueCodSettlementAsync(Guid tenantId, Guid settlementBatchId, decimal grossAmount, decimal fees, string settlementId, CancellationToken ct = default);", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("var entityId = Guid.NewGuid();", outbox, StringComparison.Ordinal);
        Assert.Contains("EnqueueMessageAsync(tenantId, \"GatewaySettlement\", settlementBatchId", outbox, StringComparison.Ordinal);
        Assert.Contains("EnqueueMessageAsync(tenantId, \"CodSettlement\", settlementBatchId", outbox, StringComparison.Ordinal);
        Assert.Contains("SettlementBatchId = settlementBatchId", outbox, StringComparison.Ordinal);
        Assert.Contains("tenantId, settlementBatch.Id, actualTotal", lifecycle, StringComparison.Ordinal);
        
        // Normalize line endings for cross-platform compatibility
        var normalizedLifecycle = NormalizeLineEndings(lifecycle);
        Assert.Contains(NormalizeLineEndings("await _accountingOutbox.EnqueueCodSettlementAsync(\n                    tenantId, settlementBatch.Id, actualTotal, request.Fees, request.SettlementId, ct);"), normalizedLifecycle, StringComparison.Ordinal);
        Assert.Contains(NormalizeLineEndings("await _accountingOutbox.EnqueueGatewaySettlementAsync(\n                    tenantId,\n                    settlementBatch.Id,\n                    request.Gateway,\n                    request.GrossAmount,\n                    request.Fees,\n                    request.SettlementId,\n                    ct);"), normalizedLifecycle, StringComparison.Ordinal);
        
        Assert.Contains("OfflineSettlementCompletedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent", events, StringComparison.Ordinal);
        Assert.Contains("GatewaySettlementCompletedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent", events, StringComparison.Ordinal);
        Assert.Contains("GetAuthoritativeAggregateId() => SettlementBatchId", events, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementReplay_MustRecomposeCurrentSettlementOrderMappingsInsteadOfTrustingRecordedOrderCount()
    {
        var authority = File.ReadAllText(InfrastructurePath("Reliability", "AccountingLedgerAuthority.cs"));

        Assert.Contains("dbContext.PaymentSettlementOrders", authority, StringComparison.Ordinal);
        Assert.Contains("CurrentOrderCount", authority, StringComparison.Ordinal);
        Assert.Contains("RecordedOrderCount", authority, StringComparison.Ordinal);
        Assert.Contains("SettlementOrderLineageMismatch", authority, StringComparison.Ordinal);
        Assert.Contains("settlement.RecordedOrderCount != settlement.CurrentOrderCount", authority, StringComparison.Ordinal);
        Assert.Contains("!batch.IsDeleted", authority, StringComparison.Ordinal);
        Assert.Contains("RecordedSettlementOrderCount", authority, StringComparison.Ordinal);
        Assert.Contains("CurrentSettlementOrderCount", authority, StringComparison.Ordinal);
        Assert.Contains("SettlementIncludesOrder", authority, StringComparison.Ordinal);
        Assert.Contains("RefundSettlementAuthorityMissing", authority, StringComparison.Ordinal);
        Assert.Contains("RefundSettlementOrderLineageMissing", authority, StringComparison.Ordinal);
        Assert.Contains("RefundSettlementOrderLineageMismatch", authority, StringComparison.Ordinal);
    }

    private static void AssertContainsInOrder(string source, params string[] markers)
    {
        var previousIndex = -1;

        foreach (var marker in markers)
        {
            var index = source.IndexOf(marker, previousIndex + 1, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Expected marker '{marker}' after index {previousIndex}.");
            previousIndex = index;
        }
    }

    /// <summary>
    /// Normalizes line endings to Unix-style (\n) for cross-platform test compatibility.
    /// Converts both Windows (\r\n) and old Mac (\r) line endings to Unix (\n).
    /// </summary>
    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static string ApiPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.API", Path.Combine(parts)));

    private static string InfrastructurePath(params string[] parts)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var activePath = Path.Combine(repositoryRoot, "PointOfSale.Infrastructure", Path.Combine(parts));
        if (File.Exists(activePath) || parts.Length == 0 || !string.Equals(parts[0], "Migrations", StringComparison.Ordinal))
        {
            return activePath;
        }

        return Path.Combine(
            repositoryRoot,
            "docs",
            "recovery",
            "archive",
            "RECOVERY-4_PHASE_A",
            "PointOfSale.Infrastructure",
            Path.Combine(parts));
    }

    private static string TestsPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.Tests", Path.Combine(parts)));

    private static string? ExtractMethodBody(string source, string methodName)
    {
        var methodPattern = $@"\b{Regex.Escape(methodName)}\s*\(";
        foreach (Match match in Regex.Matches(source, methodPattern))
        {
            var openParenIndex = source.IndexOf('(', match.Index);
            if (openParenIndex == -1)
            {
                continue;
            }

            var closeParenIndex = FindMatching(source, openParenIndex, '(', ')');
            if (closeParenIndex == -1)
            {
                continue;
            }

            var openBraceIndex = FindMethodOpeningBrace(source, closeParenIndex + 1);
            if (openBraceIndex == -1)
            {
                continue;
            }

            var closeBraceIndex = FindMatching(source, openBraceIndex, '{', '}');
            if (closeBraceIndex != -1)
            {
                return source.Substring(match.Index, closeBraceIndex - match.Index + 1);
            }
        }

        return null;
    }

    private static int FindMatching(string source, int openIndex, char open, char close)
    {
        var depth = 1;
        for (var i = openIndex + 1; i < source.Length; i++)
        {
            if (source[i] == open)
            {
                depth++;
            }
            else if (source[i] == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int FindMethodOpeningBrace(string source, int startIndex)
    {
        for (var i = startIndex; i < source.Length; i++)
        {
            if (char.IsWhiteSpace(source[i]))
            {
                continue;
            }

            return source[i] == '{' ? i : -1;
        }

        return -1;
    }
}
