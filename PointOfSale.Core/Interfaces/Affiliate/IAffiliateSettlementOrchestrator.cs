namespace PointOfSale.Core.Interfaces.Affiliate;

/// <summary>
/// AF-38H: Unified Affiliate Payout Orchestrator.
/// AF-38Y: Hardened with Serializable isolation, tenant enforcement, and MarkPaid lifecycle.
/// 
/// SINGLE AUTHORITATIVE PAYOUT EXECUTION PIPELINE.
/// 
/// ALL payouts — from ANY source — MUST flow through this orchestrator.
/// This ensures:
/// - No lifecycle drift (CommissionLedger always synced with BalanceLedger)
/// - No monetary drift (BalanceLedger remains single source of truth)
/// - No duplicate payout authority (one execution path)
/// - No UI inconsistency (Settlement tab reflects reality)
/// - No double payment possible (idempotent)
/// - No TOCTOU race (Serializable isolation)
/// - No cross-tenant contamination (StoreTenantId enforced)
/// - Deterministic payout behavior
/// 
/// EXECUTION PIPELINE (MANDATORY ORDER):
/// 1. Begin Transaction (SERIALIZABLE isolation)
/// 2. Validate Available Balance (using RawAvailable, not clamped)
/// 3. Create BalanceLedger.Withdrawal (monetary authority)
/// 4. Mark CommissionLedger entries as Paid (FIFO lifecycle sync)
/// 5. Auto-Reconcile Withdrawal Requests
/// 6. Mark PayoutRequest as Paid (if applicable)
/// 7. Commit Transaction
/// 
/// CONCURRENCY MODEL:
/// Uses IsolationLevel.Serializable — PostgreSQL detects write skew.
/// Callers MUST implement retry for SqlState 40001 (serialization_failure).
/// Built-in retry with MAX_RETRIES = 3 and exponential backoff.
/// </summary>
public interface IAffiliateSettlementOrchestrator
{
    /// <summary>
    /// Execute a payout for an affiliate account.
    /// This is the ONLY method that should ever create a BalanceLedger.Withdrawal
    /// and mark CommissionLedger entries as Paid.
    /// 
    /// AF-38Y CHANGES:
    /// - storeTenantId is now REQUIRED (V-4: cross-tenant enforcement)
    /// - paymentReference is REQUIRED (non-nullable) - external completion evidence before any withdrawal is posted
    /// - adminNotes for PayoutRequest.MarkPaid (V-3: in-transaction lifecycle)
    /// - Serializable isolation with retry (V-2: TOCTOU elimination)
    /// 
    /// INVARIANTS:
    /// - BalanceLedger.Withdrawal is NEVER created without immutable external payout evidence
    /// - BalanceLedger.Withdrawal is ALWAYS created
    /// - CommissionLedger.Paid entries are created only for the commission amount
    ///   covered by the current payout execution (FIFO from Approved)
    /// - PayoutRequests are ALWAYS reconciled
    /// - PayoutRequest.MarkPaid happens inside the same transaction (if payoutRequestId provided)
    /// - All happen atomically in one SERIALIZABLE transaction
    /// 
    /// PARAMETERS:
    /// - paymentReference: REQUIRED (non-nullable) - Bank transfer ID, wallet transaction ID, or other immutable external payment reference
    /// 
    /// Returns: PayoutExecutionResult with the list of OrderIds that had
    /// CommissionLedger.Paid entries created. Callers use this to publish
    /// AffiliateCommissionPaidEvent so MongoDB projections stay in sync.
    /// </summary>
    Task<PayoutExecutionResult> ExecuteAffiliatePayoutAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        decimal amount,
        SettlementSource source,
        string paymentReference,
        Guid? orderId = null,
        Guid? payoutRequestId = null,
        string? reference = null,
        string? adminNotes = null,
        decimal? externalTransferAmount = null,
        Guid? paidByUserId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Executes an atomic batch payout for a collection of specific orders for an affiliate account.
    /// This runs in a single SERIALIZABLE transaction. If any order payout fails, the whole batch rolls back.
    /// </summary>
    Task<PayoutExecutionResult> ExecuteAffiliateBatchPayoutAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        IReadOnlyCollection<Guid> orderIds,
        decimal amount,
        string paymentReference,
        string? reference = null,
        string? adminNotes = null,
        decimal? externalTransferAmount = null,
        CancellationToken ct = default);
}

/// <summary>
/// AF-38Y: Result of a payout execution.
/// Contains information needed by callers to publish projection events.
/// </summary>
public record PayoutExecutionResult
{
    /// <summary>
    /// OrderIds that had CommissionLedger.Paid entries created during this execution.
    /// Callers should publish AffiliateCommissionPaidEvent for each OrderId
    /// to keep MongoDB projections (Settlement tab) in sync.
    /// </summary>
    public List<PayoutOrderInfo> PaidOrders { get; init; } = new();

    /// <summary>
    /// True if the payout was skipped because it was already processed (idempotent).
    /// </summary>
    public bool WasIdempotentSkip { get; init; }
}

/// <summary>
/// Info about an order that had commissions paid during a payout execution.
/// </summary>
public record PayoutOrderInfo(
    Guid OrderId,
    decimal TotalPaid,
    int ItemCount,
    string Currency
);

/// <summary>
/// AF-38H: Source of a payout execution.
/// Used for logging, auditing, and idempotency key generation.
/// </summary>
public enum SettlementSource
{
    /// <summary>
    /// Admin-initiated legacy settlement via /pay endpoint.
    /// Reference is typically an OrderId.
    /// </summary>
    LegacySettlement = 1,

    /// <summary>
    /// Affiliate-initiated withdrawal via the Withdrawal Engine.
    /// Reference is typically a PayoutRequestId.
    /// </summary>
    WithdrawalEngine = 2
}
