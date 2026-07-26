using PointOfSale.Core.Entities;
using PointOfSale.Core.DTOs;

namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Service for managing checkout sessions (abandoned checkout recovery).
/// 
/// DESIGN RULES:
/// - CheckoutSessions are NOT orders
/// - Never reserves inventory
/// - Never processes payments
/// - Write operations go to PostgreSQL
/// - Read operations for admin go to MongoDB (via controller/read repo)
/// </summary>
public interface ICheckoutSessionService
{
    /// <summary>
    /// Create or resume a checkout session when customer enters checkout.
    /// If an active session exists for this cart, return it (upsert).
    /// </summary>
    Task<CheckoutSession> CreateOrResumeSessionAsync(
        Guid tenantId,
        Guid cartId,
        string? sessionId,
        Guid? onlineCustomerId,
        string? itemsSnapshotJson,
        decimal? cartTotal,
        string? currency,
        string? deviceType = null,
        string? source = null,
        CancellationToken ct = default);

    /// <summary>
    /// Create or resume a checkout session and surface whether the durable
    /// write succeeded with deferred or failed event dispatch.
    /// </summary>
    Task<CheckoutSessionOperationResult> CreateOrResumeSessionWithOutcomeAsync(
        Guid tenantId,
        Guid cartId,
        string? sessionId,
        Guid? onlineCustomerId,
        string? itemsSnapshotJson,
        decimal? cartTotal,
        string? currency,
        string? deviceType = null,
        string? source = null,
        CancellationToken ct = default);

    /// <summary>
    /// Update a checkout session with partial data from autosave.
    /// Only updates non-null fields. Refreshes LastActivityAtUtc.
    /// </summary>
    Task<CheckoutSession?> UpdateSessionAsync(
        Core.DTOs.UpdateCheckoutSessionRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Update a checkout session while reporting whether dispatch degraded
    /// after the session state was durably persisted.
    /// </summary>
    Task<CheckoutSessionOperationResult> UpdateSessionWithOutcomeAsync(
        Core.DTOs.UpdateCheckoutSessionRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Associates a private manual wallet proof object key with a checkout session.
    /// Replaces any existing proof reference for the same session.
    /// </summary>
    Task<CheckoutSession?> SetManualWalletProofAsync(
        Guid sessionId,
        Guid tenantId,
        string proofObjectKey,
        CancellationToken ct = default);

    /// <summary>
    /// Associates a proof object key and reports whether downstream
    /// projection dispatch degraded after the write succeeded.
    /// </summary>
    Task<CheckoutSessionOperationResult> SetManualWalletProofWithOutcomeAsync(
        Guid sessionId,
        Guid tenantId,
        string proofObjectKey,
        CancellationToken ct = default);

    /// <summary>
    /// Update a checkout session with partial data from autosave.
    /// Only updates non-null fields. Refreshes LastActivityAtUtc.
    /// </summary>
    /// <remarks>
    /// OBSOLETE: Use the overload accepting UpdateCheckoutSessionRequest instead.
    /// This method will be removed in a future version.
    /// </remarks>
    [Obsolete("Use UpdateSessionAsync(UpdateCheckoutSessionRequest, CancellationToken) instead. This overload will be removed in a future version.")]
    Task<CheckoutSession?> UpdateSessionAsync(
        Guid sessionId,
        Guid tenantId,
        string? customerName,
        string? phone,
        string? email,
        string? city,
        string? area,
        string? address,
        string? country,
        string? notes,
        Guid? shippingMethodId,
        string? paymentMethodCode,
        string? walletType,
        string? walletPhoneNumber,
        string? itemsSnapshotJson,
        decimal? cartTotal,
        string? currency,
        CancellationToken ct = default);

    /// <summary>
    /// Find active sessions that have been idle beyond the threshold.
    /// Used by the background scanner job.
    /// </summary>
    Task<List<CheckoutSession>> FindSessionsReadyForAbandonmentAsync(
        int idleMinutes = 30,
        CancellationToken ct = default);

    /// <summary>
    /// Mark a session as abandoned.
    /// Called by the background scanner job.
    /// </summary>
    Task MarkAsAbandonedAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Mark a session as converted to an order.
    /// Called when checkout completes successfully.
    /// </summary>
    Task MarkAsConvertedAsync(
        Guid cartId,
        Guid tenantId,
        Guid orderId,
        string orderNumber,
        CancellationToken ct = default);

    /// <summary>
    /// Get a checkout session by ID.
    /// </summary>
    Task<CheckoutSession?> GetByIdAsync(Guid sessionId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Resolve the canonical recovery session for a legacy or duplicated checkout session identifier.
    /// Returns null when the requested session has been superseded by another owner context.
    /// </summary>
    Task<CheckoutSession?> ResolveRecoverySessionAsync(Guid sessionId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Resolve the canonical recovery session while preserving success when
    /// reconciliation side-effects cannot be published synchronously.
    /// </summary>
    Task<CheckoutSessionOperationResult> ResolveRecoverySessionWithOutcomeAsync(Guid sessionId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Resolve the canonical active session for mutation flows that must fail closed when a stale session id/token is presented.
    /// </summary>
    Task<CheckoutSession?> ResolveActiveSessionAsync(Guid sessionId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Resolve the canonical active session while preserving durable state
    /// when dispatch side-effects degrade.
    /// </summary>
    Task<CheckoutSessionOperationResult> ResolveActiveSessionWithOutcomeAsync(Guid sessionId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Get a checkout session by recovery token.
    /// Used by the /recover-checkout/{token} endpoint.
    /// </summary>
    Task<CheckoutSession?> GetByRecoveryTokenAsync(string recoveryToken, CancellationToken ct = default);
}
