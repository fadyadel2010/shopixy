using PointOfSale.Core.DTOs.AbandonedCart;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Service for managing abandoned cart lifecycle.
/// Phase: Smart Abandoned Cart Recovery Engine - Hardening
/// 
/// RESPONSIBILITIES:
/// - Upsert cart activity records
/// - Find carts eligible for abandonment detection
/// - Manage state transitions safely
/// - Generate recovery tokens
/// 
/// HARDENING:
/// - Atomic upsert (no race conditions)
/// - Safe state transitions via Pending state
/// - Retry tracking for failed n8n calls
/// 
/// DOES NOT:
/// - Send messages (that's n8n)
/// - Trigger workflows directly (that's the job)
/// - Make eligibility decisions (that's IAbandonedCartEligibilityService)
/// </summary>
public interface IAbandonedCartService
{
    /// <summary>
    /// Records cart activity (atomic upsert).
    /// Creates new record or updates existing.
    /// Resets RecoveryStatus to Active if cart was previously abandoned.
    /// IDEMPOTENT: Safe to call multiple times for same cart.
    /// HARDENING: Uses PostgreSQL ON CONFLICT for atomicity.
    /// </summary>
    Task UpsertCartActivityAsync(
        UpsertCartActivityRequest request,
        CancellationToken ct = default);
    
    /// <summary>
    /// Finds carts that are idle beyond the threshold and ready to be marked as abandoned.
    /// Returns carts where:
    /// - RecoveryStatus = Active
    /// - LastActivityAtUtc + FirstMessageDelayMinutes <= now
    /// - Tenant has automation enabled
    /// HARDENING: Also recovers stuck Pending carts (timeout).
    /// </summary>
    IAsyncEnumerable<AbandonedCart> FindCartsReadyForAbandonmentAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Marks a cart as abandoned.
    /// Sets: RecoveryStatus = Abandoned, AbandonedAtUtc = now
    /// IDEMPOTENT: Safe to call multiple times.
    /// </summary>
    Task MarkAsAbandonedAsync(Guid abandonedCartId, CancellationToken ct = default);
    
    /// <summary>
    /// HARDENING: Transition to Pending state before n8n call.
    /// Returns false if cart is not in Abandoned state (preventing duplicate triggers).
    /// Use this + ConfirmTriggerSuccessAsync for safe triggering.
    /// </summary>
    Task<bool> TryMarkAsPendingAsync(Guid abandonedCartId, CancellationToken ct = default);
    
    /// <summary>
    /// HARDENING: Confirm successful n8n webhook call.
    /// Transitions from Pending to Triggered.
    /// Call this after successful n8n webhook response.
    /// </summary>
    Task<bool> ConfirmTriggerSuccessAsync(Guid abandonedCartId, CancellationToken ct = default);
    
    /// <summary>
    /// HARDENING: Handle n8n webhook failure.
    /// Reverts to Abandoned for retry or marks as Failed if exhausted.
    /// </summary>
    Task RecordTriggerFailureAsync(Guid abandonedCartId, string errorMessage, CancellationToken ct = default);
    
    /// <summary>
    /// Marks a cart as triggered (recovery workflow initiated).
    /// Sets: RecoveryStatus = Triggered, TriggeredAtUtc = now
    /// IDEMPOTENT: Returns false if already triggered.
    /// DEPRECATED: Use TryMarkAsPendingAsync + ConfirmTriggerSuccessAsync instead.
    /// </summary>
    Task<bool> MarkAsTriggeredAsync(Guid abandonedCartId, CancellationToken ct = default);
    
    /// <summary>
    /// Marks a cart as recovered (order completed).
    /// Sets: RecoveryStatus = Recovered, RecoveredAtUtc = now, RecoveredOrderId
    /// Called when customer completes order after receiving recovery message.
    /// HARDENING: Atomic update, idempotent.
    /// </summary>
    Task MarkAsRecoveredAsync(Guid tenantId, Guid cartId, Guid orderId, CancellationToken ct = default);
    
    /// <summary>
    /// HARDENING: Mark expired carts.
    /// Called by cleanup job to expire carts past recovery window.
    /// </summary>
    Task<int> ExpireOldTriggeredCartsAsync(int expirationDays, CancellationToken ct = default);
    
    /// <summary>
    /// Generates a signed recovery token for cart restoration.
    /// Token contains tenant/cart ownership claims and an expiration.
    /// URL-safe and non-forgeable.
    /// </summary>
    string GenerateRecoveryToken(AbandonedCart cart);
    
    /// <summary>
    /// Validates and decodes a recovery token.
    /// Returns a structured validation result for fail-closed enforcement.
    /// </summary>
    AbandonedCartRecoveryTokenValidationResult ValidateRecoveryToken(string token);
    
    /// <summary>
    /// Gets abandoned cart by ID.
    /// </summary>
    Task<AbandonedCart?> GetByIdAsync(Guid id, CancellationToken ct = default);
    
    /// <summary>
    /// Gets abandoned cart by CartId.
    /// </summary>
    Task<AbandonedCart?> GetByCartIdAsync(Guid tenantId, Guid cartId, CancellationToken ct = default);
}

public sealed record AbandonedCartRecoveryTokenValidationResult(
    bool IsValid,
    Guid? TenantId,
    Guid? CartId,
    string? SessionId,
    Guid? CustomerId,
    DateTimeOffset? ExpiresAtUtc,
    string FailureReason,
    bool SignatureFailed = false)
{
    public static AbandonedCartRecoveryTokenValidationResult Valid(
        Guid tenantId,
        Guid cartId,
        string? sessionId,
        Guid? customerId,
        DateTimeOffset expiresAtUtc) =>
        new(
            true,
            tenantId,
            cartId,
            sessionId,
            customerId,
            expiresAtUtc,
            string.Empty);

    public static AbandonedCartRecoveryTokenValidationResult Invalid(
        string reason,
        Guid? tenantId = null,
        Guid? cartId = null,
        string? sessionId = null,
        Guid? customerId = null,
        DateTimeOffset? expiresAtUtc = null) =>
        new(
            false,
            tenantId,
            cartId,
            sessionId,
            customerId,
            expiresAtUtc,
            reason);

    public static AbandonedCartRecoveryTokenValidationResult SignatureFailure(string reason) =>
        new(
            false,
            null,
            null,
            null,
            null,
            null,
            reason,
            SignatureFailed: true);
}
