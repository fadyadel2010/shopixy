namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PASS A3 — Affiliate Commission Authority Architecture Certification
/// Regression-freeze tests for the end-to-end authority chain:
/// checkout snapshot → ledger recording → delivery approval → settlement payout.
/// DO NOT DELETE OR RELAX THESE TESTS — they are production financial authority guards.
/// </summary>
public sealed class AffiliateCommissionAuthorityCertificationTests
{
    [Fact]
    public void CommissionRecording_MustUseImmutableCheckoutSnapshots()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var orderItem = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/OrderItem.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        // Checkout locks and loads policy snapshots before commit
        Assert.Contains("LoadAffiliateCommissionPolicySnapshotsAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("lockAuthorityRows: true", checkout, StringComparison.Ordinal);
        // Snapshot captured-at timestamp anchors the snapshot point
        Assert.Contains("AffiliateCommissionSnapshotCapturedAtUtc = shouldPersistAffiliateCommissionSnapshot", checkout, StringComparison.Ordinal);

        // OrderItem holds all fields required for downstream recording
        Assert.Contains("AffiliateCommissionEligible", orderItem, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionPolicyIdSnapshot", orderItem, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionRateSnapshot", orderItem, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionBaseAmountSnapshot", orderItem, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionAmountSnapshot", orderItem, StringComparison.Ordinal);

        // Commission recording uses snapshot exclusively
        Assert.Contains("CreateEarnedFromSnapshot", service, StringComparison.Ordinal);
        Assert.Contains("item.AffiliateCommissionRateSnapshot.Value", service, StringComparison.Ordinal);
        Assert.Contains("item.AffiliateCommissionAmountSnapshot.Value", service, StringComparison.Ordinal);

        // Recording must NOT re-query live policy during commission execution
        Assert.DoesNotContain(
            "AffiliateProductPolicies.FirstOrDefaultAsync",
            Slice(service, "public async Task RecordCommissionForOrderAsync", "private async Task<bool> TryRepairMissingAttributionAsync"),
            StringComparison.Ordinal);

        // DB-level constraint enforces OrderItem snapshot consistency
        Assert.Contains("CK_OrderItems_AffiliateCommissionSnapshot_Valid", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void CommissionRecording_MustRunWithIgnoreQueryFilters_ForConsumerContext()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        // Consumers run without HTTP tenant context — IgnoreQueryFilters is mandatory
        Assert.Contains("CRITICAL: Use IgnoreQueryFilters — called from MassTransit consumer", service, StringComparison.Ordinal);
        Assert.Contains("Derive TenantId from the Order entity itself", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CommissionLifecycle_MustEarnBeforeDeliveryAndApproveOnlyAfterDeliveredAuthority()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var settlementConsumer = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateSettlementConsumers.cs");

        // Earning happens first (before delivery)
        Assert.Contains("RecordCommissionForOrderAsync", service, StringComparison.Ordinal);
        Assert.Contains("EntryType = CommissionEntryType.Earned", CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateCommissionLedger.cs"), StringComparison.Ordinal);

        // Approval is triggered by delivery events
        Assert.Contains("IConsumer<OrderDeliveredEvent>", settlementConsumer, StringComparison.Ordinal);
        Assert.Contains("IConsumer<FulfillmentDeliveredEvent>", settlementConsumer, StringComparison.Ordinal);

        // Delivery gate enforces all three conditions before approval
        Assert.Contains("EnsureAffiliateDeliveryApprovalAuthority", service, StringComparison.Ordinal);
        Assert.Contains("!order.IsPhysicallyDelivered", service, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_DELIVERY_AUTHORITY_NOT_CONFIRMED", service, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_DELIVERY_PAYMENT_AUTHORITY_NOT_CONFIRMED", service, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_DELIVERY_ACCOUNTING_AUTHORITY_NOT_CONFIRMED", service, StringComparison.Ordinal);

        // Approval creates a new ledger entry from the earned entry — never modifies it
        Assert.Contains("AffiliateCommissionLedger.CreateApproved(earned)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CommissionLedger_MustBeAppendOnlyAndIdempotentPerOrderItemLifecycle()
    {
        var ledger = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateCommissionLedger.cs");
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateCommissionLedgerConfiguration.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        // Ledger is documented as append-only
        Assert.Contains("Append-only commission ledger entry", ledger, StringComparison.Ordinal);

        // All idempotency keys present per lifecycle state
        Assert.Contains("IdempotencyKey = $\"AFFILIATE_COMM:EARNED:{orderItemId}\"", ledger, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey = $\"AFFILIATE_COMM:APPROVED:{earnedEntry.OrderItemId}\"", ledger, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey = $\"AFFILIATE_COMM:PAID:{approvedEntry.OrderItemId}\"", ledger, StringComparison.Ordinal);

        // Unique index on IdempotencyKey alone (TenantId is NULL for global affiliates)
        Assert.Contains("builder.HasIndex(e => e.IdempotencyKey)", config, StringComparison.Ordinal);
        Assert.Contains("IsUnique()", config, StringComparison.Ordinal);

        // All FK deletes are Restrict — no cascade deletion
        Assert.Contains("OnDelete(DeleteBehavior.Restrict)", config, StringComparison.Ordinal);

        // Unique violations are caught and recorded as idempotent skips
        Assert.Contains("IsUniqueViolation(ex)", service, StringComparison.Ordinal);
        Assert.Contains("RecordIdempotentSkip", service, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementAuthority_BalanceFormula_MustUseNetApprovedMinusWithdrawals()
    {
        var payoutService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");

        // Balance = NetApproved + Adjustments + Withdrawals(negative) - PendingRequests
        // CommissionLedger.Paid does NOT reduce available balance (only BalanceLedger.Withdrawal does)
        Assert.Contains("CORRECT FORMULA", payoutService, StringComparison.Ordinal);
        Assert.Contains("Double Accounting Correction", payoutService, StringComparison.Ordinal);
        Assert.Contains("CommissionLedger.Paid does NOT reduce available balance", payoutService, StringComparison.Ordinal);

        // Partial reversal support: net per OrderItemId, not excluded on any reversal
        Assert.Contains("AF-38D PARTIAL REVERSAL FIX", payoutService, StringComparison.Ordinal);
        Assert.Contains("Math.Max(item.ApprovedSum - item.ReversedSum, 0m)", payoutService, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementAuthority_ExternalEvidence_MustBeIdempotentAndUniquePerReference()
    {
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        // External payment reference idempotency — same reference cannot close two different payout requests
        Assert.Contains("AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_IDEMPOTENCY_CONFLICT", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AllocateExternalTransferEvidenceAsync", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementAuthority_IdempotencyConflict_MustDetectFinancialInconsistency()
    {
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        // If existing withdrawal has different amounts/tenant/account → financial inconsistency
        Assert.Contains("IDEMPOTENCY CONFLICT", orchestrator, StringComparison.Ordinal);
        Assert.Contains("Mismatches:", orchestrator, StringComparison.Ordinal);
        Assert.Contains("financial data inconsistency", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementAuthority_CommissionPaidRace_MustThrowNotSilentlySkip()
    {
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        // If another payout concurrently marks same OrderItem as Paid, the current transaction must fail loud
        Assert.Contains("AFFILIATE_PAYOUT_COMMISSION_AUTHORITY_CHANGED", orchestrator, StringComparison.Ordinal);
        Assert.Contains("Retry with refreshed commission authority", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementAuthority_PayoutRequestMustExist_FailFast()
    {
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        // If PayoutRequest is missing, abort — do not orphan withdrawal/commission updates
        Assert.Contains("AFFILIATE_PAYOUT_REQUEST_NOT_FOUND", orchestrator, StringComparison.Ordinal);
        Assert.Contains("Cannot complete settlement without a valid payout request", orchestrator, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }
}
