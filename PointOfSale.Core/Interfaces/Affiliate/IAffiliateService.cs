using PointOfSale.Core.DTOs.Affiliate;
using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Core.Interfaces.Affiliate;

/// <summary>
/// Domain service for the Affiliate System write-side.
/// All methods enforce domain invariants at the service layer.
/// </summary>
public interface IAffiliateService
{
    // ─── Registration ───────────────────────────────────────────────

    /// <summary>
    /// Registers a new affiliate account for a user.
    /// Fails if user already has an account or referral code is taken.
    /// </summary>
    Task<AffiliateAccount> RegisterAffiliateAccountAsync(
        Guid userId, string referralCode, string displayName,
        CancellationToken ct = default);

    /// <summary>
    /// Activates an affiliate account. Idempotent if already active.
    /// </summary>
    Task ActivateAffiliateAsync(Guid affiliateAccountId, CancellationToken ct = default);

    /// <summary>
    /// Suspends an affiliate account. Idempotent if already suspended.
    /// </summary>
    Task SuspendAffiliateAsync(Guid affiliateAccountId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Closes an affiliate account. Closed accounts are terminal and cannot be reactivated.
    /// </summary>
    Task CloseAffiliateAsync(Guid affiliateAccountId, string reason, CancellationToken ct = default);

    // ─── Product Policy ─────────────────────────────────────────────

    /// <summary>
    /// Enables or updates affiliate commission for a product. Upsert semantics.
    /// AF-30A: Requires category to be assigned first. Use EnableAffiliateWithCategoryAsync() instead.
    /// </summary>
    Task<AffiliateProductPolicy> EnableAffiliateForProductAsync(
        Guid productId, decimal commissionRate, decimal? maxPerItem = null,
        CancellationToken ct = default);

    /// <summary>
    /// AF-30A: Atomically enables affiliate with category assignment.
    /// Preferred method for enabling affiliate marketing.
    /// </summary>
    Task<AffiliatePolicyWithCategoryDetails> EnableAffiliateWithCategoryAsync(
        Guid productId, decimal commissionRate, Guid categoryId, decimal? maxPerItem = null,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the structural template assigned to this product's affiliate landing page.
    /// </summary>
    Task<AffiliateProductPolicy> UpdateTemplateForProductAsync(
        Guid productId, string templateType, CancellationToken ct = default);

    /// <summary>
    /// AF-30: Updates the marketplace category assigned to this product's affiliate policy.
    /// Validates that category exists and is active.
    /// Returns enriched policy with category details for event snapshot.
    /// </summary>
    Task<AffiliatePolicyWithCategoryDetails> UpdateCategoryForProductAsync(
        Guid productId, Guid? categoryId, CancellationToken ct = default);

    /// <summary>
    /// Disables affiliate commission for a product (soft disable — sets IsEnabled = false).
    /// Returns null if no policy exists.
    /// </summary>
    Task<AffiliateProductPolicy?> DisableAffiliateForProductAsync(
        Guid productId, CancellationToken ct = default);

    // ─── Attribution ────────────────────────────────────────────────

    /// <summary>
    /// Attributes an order to an affiliate via referral code.
    /// Fails if order is already attributed to a different affiliate.
    /// Idempotent if same affiliate.
    /// </summary>
    Task<AffiliateAttribution> AttributeOrderToAffiliateAsync(
        Guid orderId, string referralCode, AttributionSource source,
        Guid? expectedAffiliateAccountId = null,
        Guid? promotionLinkId = null,
        CancellationToken ct = default);

    // ─── Commission Recording ───────────────────────────────────────

    /// <summary>
    /// Records commission for eligible items in an attributed order.
    /// Only processes items where OrderItem.PaidAmount > 0.
    /// Called when payment is captured/allocated — NOT on OrderPlaced.
    /// Idempotent per OrderItem via unique constraint.
    /// </summary>
    Task RecordCommissionForOrderAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// Reverses commission for a refunded order item.
    /// Idempotent — skips if reversal already exists.
    /// </summary>
    Task ReverseCommissionForOrderItemAsync(
        Guid tenantId,
        Guid orderId,
        Guid orderItemId,
        CancellationToken ct = default,
        decimal? refundedBaseAmount = null,
        string? refundIdempotencyKey = null);

    // ─── Settlement (AF-7) ──────────────────────────────────────────

    /// <summary>
    /// Approves all earned commissions for an order (typically on delivery).
    /// Creates Approved ledger entries referencing the Earned entries.
    /// Skips items that have been reversed.
    /// Idempotent per OrderItem via unique constraint.
    /// </summary>
    Task ApproveCommissionForOrderAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// Approves earned commissions from a delivery authority event after validating
    /// the event tenant against the current order authority state.
    /// </summary>
    Task ApproveCommissionForDeliveredOrderAsync(
        Guid orderId,
        Guid? eventTenantId,
        DateTime deliveredAtUtc,
        string sourceEvent,
        int eventVersion,
        CancellationToken ct = default,
        string? deliveryReference = null,
        string? proofOfDeliveryUrl = null);

    /// <summary>
    /// Records payment for all approved commissions for an order.
    /// Creates Paid ledger entries referencing the Approved entries.
    /// Idempotent per OrderItem via unique constraint.
    /// </summary>
    Task RecordCommissionPaidAsync(
        Guid orderId,
        string paymentReference,
        string? adminNotes = null,
        decimal? externalTransferAmount = null,
        CancellationToken ct = default);
}
