using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Core.Interfaces.Affiliate;

/// <summary>
/// AF-38: Affiliate Payout Service interface.
/// Orchestrates the withdrawal lifecycle on top of the commission ledger.
/// 
/// AF-38Y: MarkPayoutPaidAsync is DEPRECATED.
/// All payout execution MUST go through IAffiliateSettlementOrchestrator.
/// </summary>
public interface IAffiliatePayoutService
{
    /// <summary>
    /// Calculate available balance for withdrawal.
    /// AF-38Y: Returns both RawAvailable (for guards) and DisplayAvailable (for UI).
    /// </summary>
    Task<AffiliateAvailableBalanceResult> GetAvailableBalanceAsync(
        Guid affiliateAccountId,
        CancellationToken ct = default);

    /// <summary>
    /// Calculate available balance for one store's affiliate wallet.
    /// Store scoping is required for any payout mutation.
    /// </summary>
    Task<AffiliateAvailableBalanceResult> GetAvailableBalanceAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Affiliate-initiated: Request a withdrawal.
    /// Validates threshold and available balance inside a transaction.
    /// </summary>
    Task<AffiliatePayoutRequest> RequestPayoutAsync(
        Guid affiliateAccountId,
        decimal amount,
        CancellationToken ct = default);

    /// <summary>
    /// Affiliate-initiated: Request a withdrawal from a specific store balance.
    /// </summary>
    Task<AffiliatePayoutRequest> RequestPayoutAsync(
        Guid affiliateAccountId,
        decimal amount,
        Guid storeTenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Get payout history for an affiliate (portal view).
    /// </summary>
    Task<List<AffiliatePayoutRequest>> GetPayoutHistoryAsync(
        Guid affiliateAccountId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Get payout history for an affiliate scoped to a specific store (portal view).
    /// </summary>
    Task<List<AffiliatePayoutRequest>> GetPayoutHistoryAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    // ── Admin operations ─────────────────────────────────────────────

    /// <summary>
    /// Admin: List payout requests with optional status filter.
    /// </summary>
    Task<(List<AffiliatePayoutRequest> Items, int TotalCount)> ListPayoutRequestsAsync(
        Guid storeTenantId,
        PayoutRequestStatus? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    /// <summary>
    /// AF-38C: Admin: List payout requests for a SPECIFIC affiliate.
    /// DATA ISOLATION: Filters by BOTH storeTenantId AND affiliateAccountId.
    /// </summary>
    Task<(List<AffiliatePayoutRequest> Items, int TotalCount)> ListPayoutRequestsForAffiliateAsync(
        Guid storeTenantId,
        Guid affiliateAccountId,
        PayoutRequestStatus? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Admin: Approve a pending payout request.
    /// </summary>
    Task<AffiliatePayoutRequest> ApprovePayoutAsync(
        Guid payoutRequestId,
        Guid storeTenantId,
        Guid approvedByUserId,
        string? adminNotes = null,
        CancellationToken ct = default);

    /// <summary>
    /// Admin: Reject a pending payout request.
    /// Returns funds to Available balance (no ledger entry needed).
    /// </summary>
    Task<AffiliatePayoutRequest> RejectPayoutAsync(
        Guid payoutRequestId,
        Guid storeTenantId,
        Guid rejectedByUserId,
        string? adminNotes = null,
        CancellationToken ct = default);

    /// <summary>
    /// AF-38Y V-5: DEPRECATED — Use IAffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync instead.
    /// This method bypasses the unified orchestrator and has inconsistent lock ordering.
    /// Retained only for backward compatibility — throws NotSupportedException.
    /// </summary>
    [Obsolete("AF-38H: Use IAffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync. This method is no longer supported.")]
    Task<AffiliatePayoutRequest> MarkPayoutPaidAsync(
        Guid payoutRequestId,
        Guid storeTenantId,
        string? paymentReference = null,
        string? adminNotes = null,
        CancellationToken ct = default);

    /// <summary>
    /// AF-38F: Auto-Synchronization Layer.
    /// Automatically reconciles open withdrawal requests when money leaves the wallet.
    /// FIFO-based, idempotent, transaction-safe.
    /// 
    /// CRITICAL RULES:
    /// - NEVER modifies CommissionLedger
    /// - NEVER creates BalanceLedger entries
    /// - ONLY updates PayoutRequest state
    /// - NEVER affects Available balance calculation
    /// - Must run inside same transaction as BalanceLedger insert
    /// </summary>
    Task AutoReconcileWithdrawalRequestsAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        decimal amountPaid,
        string paymentReference,
        CancellationToken ct = default);
}

/// <summary>
/// AF-38Y: Available balance breakdown for an affiliate.
/// 
/// CRITICAL DISTINCTION:
/// - RawAvailable: Unclamped real balance — used by guards and withdrawal validation.
///   CAN BE NEGATIVE (after penalties). Guards MUST use this value.
/// - AvailableForWithdrawal: Clamped to 0 — used ONLY for UI display.
///   NEVER use this for withdrawal validation.
/// </summary>
public record AffiliateAvailableBalanceResult(
    decimal TotalApproved,
    decimal TotalPaid,
    decimal TotalPendingRequests,
    /// <summary>
    /// AF-38Y V-1 FIX: Unclamped real balance — CAN BE NEGATIVE.
    /// All withdrawal guards MUST use this value, not AvailableForWithdrawal.
    /// Formula: Approved + Adjustments + Withdrawals(negative) - PendingRequests
    /// </summary>
    decimal RawAvailable,
    /// <summary>
    /// AF-38Y V-1 FIX: Display-only clamped value = Max(0, RawAvailable).
    /// Used ONLY in UI responses. NEVER for guards.
    /// </summary>
    decimal AvailableForWithdrawal,
    decimal MinimumThreshold,
    bool CanWithdraw,
    string Currency
);
