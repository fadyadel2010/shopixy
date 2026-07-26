namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PASS A3 — Affiliate Commission Recovery Authority Certification
/// Regression-freeze tests for idempotency, reconciliation, and serializable isolation recovery.
/// DO NOT DELETE OR RELAX THESE TESTS — they are production financial authority guards.
/// </summary>
public sealed class AffiliateCommissionRecoveryCertificationTests
{
    [Fact]
    public void Idempotency_MustCatchUniqueConstraintAndRecordSkip()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        // Service catches unique constraint violation (Postgres 23505) and records idempotent skip
        Assert.Contains("IsUniqueViolation(ex)", service, StringComparison.Ordinal);
        Assert.Contains("RecordIdempotentSkip", service, StringComparison.Ordinal);

        // Detaches failed entity so EF change tracker stays clean
        Assert.Contains("EntityState.Detached", service, StringComparison.Ordinal);
    }

    [Fact]
    public void Idempotency_UniqueViolationDetector_MustCheckPostgresErrorCode()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        // Both must detect PostgreSQL unique violation (23505) specifically — not all DbUpdateExceptions
        Assert.Contains("pg.SqlState == \"23505\"", service, StringComparison.Ordinal);
        Assert.Contains("pg.SqlState == \"23505\"", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void Idempotency_ApprovalRace_MustHandleConcurrentConsumers()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        // Pre-save concurrency guard: re-check DB for concurrently approved items before SaveChanges
        Assert.Contains("existingApprovedItemIds", service, StringComparison.Ordinal);
        Assert.Contains("concurrent consumer already approved", service, StringComparison.Ordinal);

        // Post-save unique constraint: if pre-check fails, catch 23505 and handle gracefully
        Assert.Contains("concurrent consumer already approved these items", service, StringComparison.Ordinal);

        // Staged outbox entries must also be detached to prevent duplicate event publication
        Assert.Contains("stagedOutboxEntries", service, StringComparison.Ordinal);
        Assert.Contains("OutboxMessage", service, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryAndReconciliation_MustRestoreConvergence()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        // Inline reconciliation: refunded items get reversed commissions during recording
        Assert.Contains("ReconcileRefundedCommissionEntriesAsync", service, StringComparison.Ordinal);
        // Inline reconciliation: delivered orders get approved commissions during recording
        Assert.Contains("ReconcileDeliveredCommissionApprovalsAsync", service, StringComparison.Ordinal);
        // Retry gate: refund reversal for item with no earned commission defers until earning converges
        Assert.Contains("ShouldRetryMissingEarnedCommissionForRefundAsync", service, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_AttributionRepair_MustRestoreMissingAttributionRows()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        // Attribution reconciliation background loop exists
        Assert.Contains("ReconcileMissingAttributionsAsync", service, StringComparison.Ordinal);
        Assert.Contains("TryRepairMissingAttributionAsync", service, StringComparison.Ordinal);
        Assert.Contains("AttributeOrderToAffiliateFromOrderSnapshotAsync", service, StringComparison.Ordinal);

        // AttributionRecoveryBatchSize limits concurrent repair to prevent runaway DB load
        Assert.Contains("AttributionRecoveryBatchSize", service, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_CommissionDeferral_MustRetryUntilAccountingPosted()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        // If accounting not posted, throw AffiliateCommissionDeferredException → MassTransit retry
        Assert.Contains("AffiliateCommissionDeferredException", service, StringComparison.Ordinal);
        Assert.Contains("accounting_not_posted", service, StringComparison.Ordinal);
        Assert.Contains("FinancialPostingStatus", service, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_MissingEarnedBeforeDeliveryApproval_MustRetry()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        // If delivered order has no earned commission yet, throw to trigger retry
        Assert.Contains("EnsureCommissionReadyForDeliveredAffiliateOrderAsync", service, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_COMMISSION_NOT_READY_FOR_DELIVERY_APPROVAL", service, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_MissingEarnedBeforeRefundReversal_MustRetry()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        // If refund arrives before commission is earned, must retry not silently skip
        Assert.Contains("AFFILIATE_COMMISSION_NOT_READY_FOR_REFUND_REVERSAL", service, StringComparison.Ordinal);
        Assert.Contains("ShouldRetryMissingEarnedCommissionForRefundAsync", service, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializableIsolation_MustRetryOnPgConcurrencyConflict()
    {
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        // Orchestrator has retry logic for SSI serialization conflicts (SQLState 40001)
        Assert.Contains("IsSerializationFailure", orchestrator, StringComparison.Ordinal);
        Assert.Contains("SqlState == \"40001\"", orchestrator, StringComparison.Ordinal);
        Assert.Contains("MAX_RETRIES = 3", orchestrator, StringComparison.Ordinal);
        Assert.Contains("ExecutePayoutCoreAsync", orchestrator, StringComparison.Ordinal);

        // Exponential backoff between retries
        Assert.Contains("50 * attempt", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializableIsolation_MustDetectWrappedDbUpdateException()
    {
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        // Must detect 40001 both direct AND wrapped in DbUpdateException
        Assert.Contains("ex is DbUpdateException dbEx && dbEx.InnerException is PostgresException innerPg && innerPg.SqlState == \"40001\"", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void BalanceFormula_MustUsRawAvailable_NotClampedDisplayValue()
    {
        var payoutService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        // AffiliatePayoutService: RawAvailable is the true financial balance for the affiliate portal
        Assert.Contains("RawAvailable", payoutService, StringComparison.Ordinal);
        Assert.Contains("var rawAvailable =", payoutService, StringComparison.Ordinal);

        // AffiliateSettlementOrchestrator: payout balance check un-reserves pending requests
        // using RawAvailable + TotalPendingRequests (the actual net monetary balance at execution time)
        Assert.Contains("var netActualBalance = balance.RawAvailable + balance.TotalPendingRequests;", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AF-38Y V-1 FIX", payoutService, StringComparison.Ordinal);
        Assert.Contains("RawAvailable is the REAL balance", payoutService, StringComparison.Ordinal);
    }

    [Fact]
    public void BalanceFormula_MultiCurrency_MustBlockPayouts()
    {
        var payoutService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");

        // If multiple currencies exist in the ledger, payouts must be blocked
        Assert.Contains("AffiliatePayoutAuthorityException", payoutService, StringComparison.Ordinal);
        Assert.Contains("multiple currencies exist", payoutService, StringComparison.Ordinal);
        Assert.Contains("Payouts are blocked until the ledger is reconciled", payoutService, StringComparison.Ordinal);
    }
}
