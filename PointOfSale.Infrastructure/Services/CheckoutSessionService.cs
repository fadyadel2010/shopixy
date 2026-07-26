using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using PointOfSale.Core.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Services;

/// <summary>
/// Service for managing checkout sessions — write-side operations on PostgreSQL.
///
/// ARCHITECTURAL RULES:
/// - Non-sensitive events are staged into the MassTransit outbox before commit
/// - Sensitive proof events are best-effort after commit and must never turn a durable write into a false failure
/// - Tenant isolation enforced by EF Core query filters
/// - CheckoutSessions are NEVER orders
/// - Background scanner uses IgnoreQueryFilters for cross-tenant scanning
/// </summary>
public class CheckoutSessionService : ICheckoutSessionService
{
    private const string ActiveSessionUniqueIndexName = "IX_CheckoutSessions_TenantId_CartId_Active_Unique";

    private readonly ApplicationDbContext _db;
    private readonly IEventPublisher _eventPublisher;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<CheckoutSessionService> _logger;

    public CheckoutSessionService(
        ApplicationDbContext db,
        IEventPublisher eventPublisher,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<CheckoutSessionService> logger)
    {
        _db = db;
        _eventPublisher = eventPublisher;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CheckoutSession> CreateOrResumeSessionAsync(
        Guid tenantId,
        Guid cartId,
        string? sessionId,
        Guid? onlineCustomerId,
        string? itemsSnapshotJson,
        decimal? cartTotal,
        string? currency,
        string? deviceType = null,
        string? source = null,
        CancellationToken ct = default)
    {
        var outcome = await CreateOrResumeSessionWithOutcomeAsync(
            tenantId,
            cartId,
            sessionId,
            onlineCustomerId,
            itemsSnapshotJson,
            cartTotal,
            currency,
            deviceType,
            source,
            ct);

        return outcome.Session ?? throw new InvalidOperationException("Checkout session could not be created or resumed.");
    }

    /// <inheritdoc />
    public async Task<CheckoutSessionOperationResult> CreateOrResumeSessionWithOutcomeAsync(
        Guid tenantId,
        Guid cartId,
        string? sessionId,
        Guid? onlineCustomerId,
        string? itemsSnapshotJson,
        decimal? cartTotal,
        string? currency,
        string? deviceType = null,
        string? source = null,
        CancellationToken ct = default)
    {
        var normalizedSessionId = NormalizeSessionId(sessionId);

        if (!onlineCustomerId.HasValue && string.IsNullOrWhiteSpace(normalizedSessionId))
        {
            throw new InvalidOperationException("Guest checkout sessions require a session identifier.");
        }

        try
        {
            return await CreateOrResumeSessionCoreAsync(
                tenantId,
                cartId,
                normalizedSessionId,
                onlineCustomerId,
                itemsSnapshotJson,
                cartTotal,
                currency,
                deviceType,
                source,
                ct);
        }
        catch (DbUpdateException ex) when (IsActiveSessionUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();

            _logger.LogWarning(
                ex,
                "Checkout session create/resume collided with the active-session uniqueness guard | TenantId: {TenantId} | CartId: {CartId}",
                tenantId,
                cartId);

            _reliabilityTelemetry.RecordCheckoutSessionRacePrevented(
                "create_or_resume",
                "active_session_unique_index");

            return await CreateOrResumeSessionCoreAsync(
                tenantId,
                cartId,
                normalizedSessionId,
                onlineCustomerId,
                itemsSnapshotJson,
                cartTotal,
                currency,
                deviceType,
                source,
                ct);
        }
    }

    /// <inheritdoc />
    public async Task<CheckoutSession?> UpdateSessionAsync(
        Core.DTOs.UpdateCheckoutSessionRequest request,
        CancellationToken ct = default)
    {
        var outcome = await UpdateSessionWithOutcomeAsync(request, ct);
        return outcome.Session;
    }

    /// <inheritdoc />
    public async Task<CheckoutSessionOperationResult> UpdateSessionWithOutcomeAsync(
        Core.DTOs.UpdateCheckoutSessionRequest request,
        CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        await using var transaction = await BeginConsistencyTransactionAsync(ct);
        var dispatchTracker = new DispatchTracker();

        var requestedSession = await _db.CheckoutSessions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                session => session.Id == request.SessionId && session.TenantId == request.TenantId,
                ct);

        if (requestedSession == null || requestedSession.Status != CheckoutSessionStatus.Active)
        {
            _logger.LogWarning(
                "Checkout session {SessionId} not found or not active for tenant {TenantId}",
                request.SessionId,
                request.TenantId);
            return CheckoutSessionOperationResult.Success(null);
        }

        var lockAcquired = await AcquireCartScopedLockAsync(request.TenantId, requestedSession.CartId, ct);
        if (!lockAcquired)
        {
            _logger.LogDebug(
                "Cart-scoped lock not available (non-Npgsql provider), proceeding without lock for session update | SessionId: {SessionId} | TenantId: {TenantId} | CartId: {CartId}",
                request.SessionId,
                request.TenantId,
                requestedSession.CartId);
        }

        var recoverableSessions = await LoadRecoverableSessionsByCartAsync(
            request.TenantId,
            requestedSession.CartId,
            ct);

        var resolution = ResolveSessionAccess(
            surface: "update",
            requestedSession,
            recoverableSessions,
            nowUtc,
            allowCanonicalRedirect: true,
            requireActive: true);

        if (resolution.CanonicalSession == null)
        {
            await StageResolutionOutboxEventsAsync(
                resolution,
                request.TenantId,
                "update",
                dispatchTracker,
                ct);
            await SaveResolutionChangesAsync(resolution, transaction, ct);
            return CheckoutSessionOperationResult.Success(null);
        }

        var session = resolution.CanonicalSession;

        if (request.CustomerName != null) session.CustomerName = request.CustomerName;
        if (request.Phone != null) session.Phone = request.Phone;
        if (request.Email != null) session.Email = request.Email;
        if (request.City != null) session.City = request.City;
        if (request.Area != null) session.Area = request.Area;
        if (request.Address != null) session.Address = request.Address;

        if (request.Country != null)
        {
            if (Core.Helpers.CountryCodeHelper.TryNormalizeCountryCode(request.Country, out var normalizedCountry))
            {
                session.Country = normalizedCountry;
            }
            else
            {
                _logger.LogWarning(
                    "Invalid country code '{Country}' provided for checkout session {SessionId} tenant {TenantId}. Skipping country update.",
                    request.Country,
                    request.SessionId,
                    request.TenantId);
            }
        }

        if (request.Notes != null) session.Notes = request.Notes;
        if (request.ShippingMethodId.HasValue) session.ShippingMethodId = request.ShippingMethodId;
        if (request.PaymentMethodCode != null) session.PaymentMethodCode = request.PaymentMethodCode;

        if (!IsManualWalletPaymentMethod(session.PaymentMethodCode))
        {
            session.WalletType = null;
            session.WalletPhoneNumber = null;
        }
        else
        {
            if (request.WalletType != null) session.WalletType = request.WalletType;
            if (request.WalletPhoneNumber != null) session.WalletPhoneNumber = request.WalletPhoneNumber;
        }

        if (request.ItemsSnapshotJson != null) session.ItemsSnapshotJson = request.ItemsSnapshotJson;
        if (request.CartTotal.HasValue) session.CartTotal = request.CartTotal;

        var validatedCurrency = await ValidateCheckoutCurrencyAsync(
            request.TenantId,
            request.Currency ?? session.Currency,
            $"checkout_session:update:{request.SessionId:D}",
            ct);
        session.Currency = validatedCurrency;

        session.FirstActivityAtUtc ??= nowUtc;
        session.LastActivityAtUtc = nowUtc;

        resolution.PublishCanonicalUpdate = true;

        await StageResolutionOutboxEventsAsync(
            resolution,
            request.TenantId,
            "update",
            dispatchTracker,
            ct);
        await SaveResolutionChangesAsync(resolution, transaction, ct);
        await PublishResolutionSensitiveEventsAsync(
            resolution,
            request.TenantId,
            "update",
            ct);

        return CreateOperationResult(session, dispatchTracker);
    }

    /// <inheritdoc />
    public async Task<CheckoutSession?> SetManualWalletProofAsync(
        Guid sessionId,
        Guid tenantId,
        string proofObjectKey,
        CancellationToken ct = default)
    {
        var outcome = await SetManualWalletProofWithOutcomeAsync(sessionId, tenantId, proofObjectKey, ct);
        return outcome.Session;
    }

    /// <inheritdoc />
    public async Task<CheckoutSessionOperationResult> SetManualWalletProofWithOutcomeAsync(
        Guid sessionId,
        Guid tenantId,
        string proofObjectKey,
        CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        await using var transaction = await BeginConsistencyTransactionAsync(ct);
        var dispatchTracker = new DispatchTracker();

        var requestedSession = await _db.CheckoutSessions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                session => session.Id == sessionId && session.TenantId == tenantId,
                ct);

        if (requestedSession == null || requestedSession.Status != CheckoutSessionStatus.Active)
        {
            _logger.LogWarning(
                "Checkout session {SessionId} not found or not active for tenant {TenantId} when setting proof",
                sessionId,
                tenantId);
            return CheckoutSessionOperationResult.Success(null);
        }

        var lockAcquired = await AcquireCartScopedLockAsync(tenantId, requestedSession.CartId, ct);
        if (!lockAcquired)
        {
            _logger.LogDebug(
                "Cart-scoped lock not available (non-Npgsql provider), proceeding without lock for manual wallet proof | SessionId: {SessionId} | TenantId: {TenantId} | CartId: {CartId}",
                sessionId,
                tenantId,
                requestedSession.CartId);
        }

        var recoverableSessions = await LoadRecoverableSessionsByCartAsync(
            tenantId,
            requestedSession.CartId,
            ct);

        var resolution = ResolveSessionAccess(
            surface: "manual_wallet_proof",
            requestedSession,
            recoverableSessions,
            nowUtc,
            allowCanonicalRedirect: false,
            requireActive: true);

        if (resolution.CanonicalSession == null)
        {
            await StageResolutionOutboxEventsAsync(
                resolution,
                tenantId,
                "manual_wallet_proof",
                dispatchTracker,
                ct);
            await SaveResolutionChangesAsync(resolution, transaction, ct);
            return CheckoutSessionOperationResult.Success(null);
        }

        var session = resolution.CanonicalSession;

        session.ProofImageUrl = proofObjectKey;
        session.FirstActivityAtUtc ??= nowUtc;
        session.LastActivityAtUtc = nowUtc;

        resolution.PublishCanonicalUpdate = true;

        await StageResolutionOutboxEventsAsync(
            resolution,
            tenantId,
            "manual_wallet_proof",
            dispatchTracker,
            ct);
        await SaveResolutionChangesAsync(resolution, transaction, ct);
        await PublishResolutionSensitiveEventsAsync(
            resolution,
            tenantId,
            "manual_wallet_proof",
            ct);

        return CreateOperationResult(session, dispatchTracker);
    }

    /// <inheritdoc />
    [Obsolete("Use UpdateSessionAsync(UpdateCheckoutSessionRequest, CancellationToken) instead. This overload will be removed in a future version.")]
    public Task<CheckoutSession?> UpdateSessionAsync(
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
        CancellationToken ct = default)
    {
        return UpdateSessionAsync(new Core.DTOs.UpdateCheckoutSessionRequest
        {
            SessionId = sessionId,
            TenantId = tenantId,
            CustomerName = customerName,
            Phone = phone,
            Email = email,
            City = city,
            Area = area,
            Address = address,
            Country = country,
            Notes = notes,
            ShippingMethodId = shippingMethodId,
            PaymentMethodCode = paymentMethodCode,
            WalletType = walletType,
            WalletPhoneNumber = walletPhoneNumber,
            ItemsSnapshotJson = itemsSnapshotJson,
            CartTotal = cartTotal,
            Currency = currency,
        }, ct);
    }

    /// <inheritdoc />
    public async Task<List<CheckoutSession>> FindSessionsReadyForAbandonmentAsync(
        int idleMinutes = 30,
        CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow.AddMinutes(-idleMinutes);

        return await _db.CheckoutSessions
            .IgnoreQueryFilters()
            .Where(session =>
                session.Status == CheckoutSessionStatus.Active &&
                session.LastActivityAtUtc < threshold)
            .Take(200)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task MarkAsAbandonedAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.CheckoutSessions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                existing => existing.Id == sessionId && existing.Status == CheckoutSessionStatus.Active,
                ct);

        if (session == null)
        {
            return;
        }

        session.Status = CheckoutSessionStatus.Abandoned;
        session.AbandonedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _eventPublisher.PublishAsync(new CheckoutSessionAbandonedEvent
        {
            TenantId = session.TenantId,
            CheckoutSessionId = session.Id,
            CartId = session.CartId,
            CustomerName = session.CustomerName,
            Phone = session.Phone,
            City = session.City,
            ItemsSnapshotJson = session.ItemsSnapshotJson,
            CartTotal = session.CartTotal,
            Currency = session.Currency,
            DeviceType = session.DeviceType,
            Source = session.Source,
            RecoveryToken = session.RecoveryToken,
            FirstActivityAtUtc = session.FirstActivityAtUtc,
            AbandonedAtUtc = session.AbandonedAtUtc!.Value,
            LastActivityAtUtc = session.LastActivityAtUtc,
        }, ct);

        _logger.LogInformation(
            "Marked checkout session {SessionId} as abandoned (tenant {TenantId}, cart {CartId})",
            session.Id,
            session.TenantId,
            session.CartId);
    }

    /// <inheritdoc />
    public async Task MarkAsConvertedAsync(
        Guid cartId,
        Guid tenantId,
        Guid orderId,
        string orderNumber,
        CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        await using var transaction = await BeginConsistencyTransactionAsync(ct);
        var dispatchTracker = new DispatchTracker();

        var lockAcquired = await AcquireCartScopedLockAsync(tenantId, cartId, ct);
        if (!lockAcquired)
        {
            _logger.LogDebug(
                "Cart-scoped lock not available (non-Npgsql provider), proceeding without lock for conversion | TenantId: {TenantId} | CartId: {CartId}",
                tenantId,
                cartId);
        }

        var recoverableSessions = await LoadRecoverableSessionsByCartAsync(tenantId, cartId, ct);
        if (recoverableSessions.Count == 0)
        {
            _logger.LogDebug(
                "No checkout session found for cart {CartId} tenant {TenantId} to convert",
                cartId,
                tenantId);
            return;
        }

        TrackDuplicateSessions("convert", tenantId, cartId, recoverableSessions.Count);

        var canonicalSession = SelectCanonicalSession(recoverableSessions);
        if (canonicalSession == null)
        {
            _logger.LogDebug(
                "No canonical checkout session could be resolved for cart {CartId} tenant {TenantId}",
                cartId,
                tenantId);
            return;
        }

        var invalidatedSessions = new List<CheckoutSession>();
        var canonicalOwnerDuplicates = recoverableSessions
            .Where(session => session.Id != canonicalSession.Id && SameOwner(session, canonicalSession))
            .ToList();

        var canonicalMerged = MergeCriticalDataIntoCanonical(canonicalSession, canonicalOwnerDuplicates);

        foreach (var duplicateSession in recoverableSessions.Where(session => session.Id != canonicalSession.Id))
        {
            InvalidateSession(duplicateSession, nowUtc, invalidatedSessions);
        }

        canonicalSession.Status = CheckoutSessionStatus.ConvertedToOrder;
        canonicalSession.ConvertedAtUtc = nowUtc;
        canonicalSession.ConvertedOrderId = orderId;
        canonicalSession.ConvertedOrderNumber = orderNumber;
        canonicalSession.AbandonedAtUtc = null;

        var hasTrackedManualWalletProof = await _db.PaymentIntents
            .IgnoreQueryFilters()
            .AnyAsync(
                paymentIntent => paymentIntent.TenantId == tenantId &&
                                 paymentIntent.OrderId == orderId &&
                                 paymentIntent.Provider == PaymentProvider.ManualWallet &&
                                 !string.IsNullOrWhiteSpace(paymentIntent.ProofImageUrl),
                ct);

        if (hasTrackedManualWalletProof)
        {
            canonicalSession.WalletType = null;
            canonicalSession.WalletPhoneNumber = null;
            canonicalSession.ProofImageUrl = null;
        }

        // Stage all non-sensitive events to outbox BEFORE commit
        if (invalidatedSessions.Count > 0)
        {
            _logger.LogInformation(
                "Reconciled duplicate checkout sessions before conversion | TenantId: {TenantId} | CartId: {CartId} | InvalidatedSessions: {InvalidatedSessions}",
                tenantId,
                cartId,
                invalidatedSessions.Count);

            _reliabilityTelemetry.RecordCheckoutSessionReconciliation(
                "convert",
                "invalidate_duplicates",
                invalidatedSessions.Count);

            foreach (var invalidatedSession in invalidatedSessions)
            {
                await TryStageOutboxEventAsync(
                    BuildSessionInvalidatedEvent(invalidatedSession),
                    "convert",
                    "session_invalidated",
                    dispatchTracker,
                    ct);
            }
        }

        if (canonicalMerged)
        {
            await TryStageOutboxEventAsync(
                BuildSessionUpdatedEvent(canonicalSession, tenantId),
                "convert",
                "session_updated",
                dispatchTracker,
                ct);
        }

        if (canonicalMerged || hasTrackedManualWalletProof)
        {
            await TryStageOutboxEventAsync(
                BuildSessionProofSyncRequestedEvent(canonicalSession, tenantId),
                "convert",
                "session_proof_sync",
                dispatchTracker,
                ct);
        }

        await TryStageOutboxEventAsync(
            new CheckoutSessionConvertedEvent
            {
                TenantId = canonicalSession.TenantId,
                CheckoutSessionId = canonicalSession.Id,
                CartId = canonicalSession.CartId,
                OrderId = orderId,
                OrderNumber = orderNumber,
                ConvertedAtUtc = nowUtc,
            },
            "convert",
            "session_converted",
            dispatchTracker,
            ct);

        await _db.SaveChangesAsync(ct);
        await CommitTransactionIfNeededAsync(transaction, ct);

        // Publish sensitive proof events AFTER commit (best-effort, won't cause false failures)
        if (canonicalMerged)
        {
            await TryPublishSensitiveProofEventAsync(
                BuildSessionProofEvent(canonicalSession, tenantId),
                "convert",
                ct);
        }

        if (hasTrackedManualWalletProof && !canonicalMerged)
        {
            await TryPublishSensitiveProofEventAsync(
                new CheckoutSessionProofEvent
                {
                    TenantId = canonicalSession.TenantId,
                    CheckoutSessionId = canonicalSession.Id,
                    WalletType = null,
                    WalletPhoneNumber = null,
                    ProofImageUrl = null,
                },
                "convert",
                ct);
        }

        if (dispatchTracker.HasFailures)
        {
            _logger.LogWarning(
                "Checkout session converted with partial event publishing failures | SessionId: {SessionId} | OrderNumber: {OrderNumber} | TenantId: {TenantId} | FailedStages: {FailedStages}",
                canonicalSession.Id,
                orderNumber,
                canonicalSession.TenantId,
                string.Join(", ", dispatchTracker.FailureStages));
        }
        else
        {
            _logger.LogInformation(
                "Converted checkout session {SessionId} to order {OrderNumber} (tenant {TenantId})",
                canonicalSession.Id,
                orderNumber,
                canonicalSession.TenantId);
        }
    }

    /// <inheritdoc />
    public async Task<CheckoutSession?> GetByIdAsync(Guid sessionId, Guid tenantId, CancellationToken ct = default)
    {
        return await _db.CheckoutSessions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                session => session.Id == sessionId && session.TenantId == tenantId,
                ct);
    }

    /// <inheritdoc />
    public async Task<CheckoutSession?> ResolveRecoverySessionAsync(
        Guid sessionId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var outcome = await ResolveRecoverySessionWithOutcomeAsync(sessionId, tenantId, ct);
        return outcome.Session;
    }

    /// <inheritdoc />
    public async Task<CheckoutSessionOperationResult> ResolveRecoverySessionWithOutcomeAsync(
        Guid sessionId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        await using var transaction = await BeginConsistencyTransactionAsync(ct);
        var dispatchTracker = new DispatchTracker();

        var requestedSession = await _db.CheckoutSessions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                session => session.Id == sessionId && session.TenantId == tenantId,
                ct);

        if (requestedSession == null)
        {
            return CheckoutSessionOperationResult.Success(null);
        }

        // Acquire lock BEFORE status validation to close TOCTOU window
        var lockAcquired = await AcquireCartScopedLockAsync(tenantId, requestedSession.CartId, ct);
        if (!lockAcquired)
        {
            _logger.LogDebug(
                "Cart-scoped lock not available (non-Npgsql provider), proceeding without lock for recovery session | SessionId: {SessionId} | TenantId: {TenantId} | CartId: {CartId}",
                sessionId,
                tenantId,
                requestedSession.CartId);
        }

        // Validate status AFTER acquiring lock to prevent race conditions
        if (requestedSession.Status != CheckoutSessionStatus.Active &&
            requestedSession.Status != CheckoutSessionStatus.Abandoned)
        {
            return CheckoutSessionOperationResult.Success(null);
        }

        var recoverableSessions = await LoadRecoverableSessionsByCartAsync(
            tenantId,
            requestedSession.CartId,
            ct);

        var resolution = ResolveSessionAccess(
            surface: "recover",
            requestedSession,
            recoverableSessions,
            nowUtc,
            allowCanonicalRedirect: true,
            requireActive: false);

        await StageResolutionOutboxEventsAsync(
            resolution,
            tenantId,
            "recover",
            dispatchTracker,
            ct);
        await SaveResolutionChangesAsync(resolution, transaction, ct);
        await PublishResolutionSensitiveEventsAsync(
            resolution,
            tenantId,
            "recover",
            ct);

        return CreateOperationResult(resolution.CanonicalSession, dispatchTracker);
    }

    /// <inheritdoc />
    public async Task<CheckoutSession?> ResolveActiveSessionAsync(
        Guid sessionId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var outcome = await ResolveActiveSessionWithOutcomeAsync(sessionId, tenantId, ct);
        return outcome.Session;
    }

    /// <inheritdoc />
    public async Task<CheckoutSessionOperationResult> ResolveActiveSessionWithOutcomeAsync(
        Guid sessionId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        await using var transaction = await BeginConsistencyTransactionAsync(ct);
        var dispatchTracker = new DispatchTracker();

        var requestedSession = await _db.CheckoutSessions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                session => session.Id == sessionId && session.TenantId == tenantId,
                ct);

        if (requestedSession == null)
        {
            return CheckoutSessionOperationResult.Success(null);
        }

        var lockAcquired = await AcquireCartScopedLockAsync(tenantId, requestedSession.CartId, ct);
        if (!lockAcquired)
        {
            _logger.LogDebug(
                "Cart-scoped lock not available (non-Npgsql provider), proceeding without lock for active session resolution | SessionId: {SessionId} | TenantId: {TenantId} | CartId: {CartId}",
                sessionId,
                tenantId,
                requestedSession.CartId);
        }

        // Validate status AFTER acquiring lock to prevent race conditions
        if (requestedSession.Status != CheckoutSessionStatus.Active)
        {
            return CheckoutSessionOperationResult.Success(null);
        }

        var recoverableSessions = await LoadRecoverableSessionsByCartAsync(
            tenantId,
            requestedSession.CartId,
            ct);

        var resolution = ResolveSessionAccess(
            surface: "active_resolution",
            requestedSession,
            recoverableSessions,
            nowUtc,
            allowCanonicalRedirect: false,
            requireActive: true);

        await StageResolutionOutboxEventsAsync(
            resolution,
            tenantId,
            "active_resolution",
            dispatchTracker,
            ct);
        await SaveResolutionChangesAsync(resolution, transaction, ct);
        await PublishResolutionSensitiveEventsAsync(
            resolution,
            tenantId,
            "active_resolution",
            ct);

        return CreateOperationResult(resolution.CanonicalSession, dispatchTracker);
    }

    /// <inheritdoc />
    public async Task<CheckoutSession?> GetByRecoveryTokenAsync(string recoveryToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recoveryToken))
        {
            return null;
        }

        return await _db.CheckoutSessions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(session => session.RecoveryToken == recoveryToken, ct);
    }

    private async Task<CheckoutSessionOperationResult> CreateOrResumeSessionCoreAsync(
        Guid tenantId,
        Guid cartId,
        string? normalizedSessionId,
        Guid? onlineCustomerId,
        string? itemsSnapshotJson,
        decimal? cartTotal,
        string? currency,
        string? deviceType,
        string? source,
        CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        await using var transaction = await BeginConsistencyTransactionAsync(ct);
        var dispatchTracker = new DispatchTracker();

        var lockAcquired = await AcquireCartScopedLockAsync(tenantId, cartId, ct);
        if (!lockAcquired)
        {
            _logger.LogDebug(
                "Cart-scoped lock not available (non-Npgsql provider), proceeding without lock | TenantId: {TenantId} | CartId: {CartId}",
                tenantId,
                cartId);
        }

        var recoverableSessions = await LoadRecoverableSessionsByCartAsync(tenantId, cartId, ct);

        TrackDuplicateSessions("create_or_resume", tenantId, cartId, recoverableSessions.Count);

        var ownerMatchedSessions = recoverableSessions
            .Where(session => MatchesOwner(session, onlineCustomerId, normalizedSessionId))
            .ToList();

        var ownerMismatchedSessions = recoverableSessions
            .Where(session => !MatchesOwner(session, onlineCustomerId, normalizedSessionId))
            .ToList();

        var invalidatedSessions = new List<CheckoutSession>();
        var canonicalSession = SelectCanonicalSession(ownerMatchedSessions);
        var publishCanonicalUpdate = false;

        if (canonicalSession != null)
        {
            var validatedCurrency = await ValidateCheckoutCurrencyAsync(
                tenantId,
                currency ?? canonicalSession.Currency,
                $"checkout_session:resume:{canonicalSession.Id:D}",
                ct);

            var duplicateOwnerSessions = ownerMatchedSessions
                .Where(session => session.Id != canonicalSession.Id)
                .ToList();

            publishCanonicalUpdate = MergeCriticalDataIntoCanonical(canonicalSession, duplicateOwnerSessions);

            foreach (var duplicateSession in duplicateOwnerSessions)
            {
                InvalidateSession(duplicateSession, nowUtc, invalidatedSessions);
            }

            foreach (var mismatchedSession in ownerMismatchedSessions)
            {
                InvalidateSession(mismatchedSession, nowUtc, invalidatedSessions);
            }

            canonicalSession.SessionId = normalizedSessionId;
            canonicalSession.OnlineCustomerId = onlineCustomerId;
            canonicalSession.ItemsSnapshotJson = itemsSnapshotJson ?? canonicalSession.ItemsSnapshotJson;
            canonicalSession.CartTotal = cartTotal ?? canonicalSession.CartTotal;
            canonicalSession.Currency = validatedCurrency;
            canonicalSession.DeviceType = string.IsNullOrWhiteSpace(deviceType) ? canonicalSession.DeviceType : deviceType.Trim();
            canonicalSession.Source = string.IsNullOrWhiteSpace(source) ? canonicalSession.Source : source.Trim();
            canonicalSession.LastActivityAtUtc = nowUtc;

            if (canonicalSession.Status != CheckoutSessionStatus.Active)
            {
                canonicalSession.Status = CheckoutSessionStatus.Active;
                canonicalSession.AbandonedAtUtc = null;
                publishCanonicalUpdate = true;
            }

            if (string.IsNullOrWhiteSpace(canonicalSession.RecoveryToken))
            {
                canonicalSession.RecoveryToken = CreateRecoveryNonce();
                publishCanonicalUpdate = true;
            }

            var resolution = new SessionResolution
            {
                Surface = "create_or_resume",
                RequestedSession = canonicalSession,
                CanonicalSession = canonicalSession,
                PublishCanonicalUpdate = publishCanonicalUpdate
            };

            foreach (var invalidatedSession in invalidatedSessions)
            {
                resolution.InvalidatedSessions.Add(invalidatedSession);
            }

            await StageResolutionOutboxEventsAsync(
                resolution,
                tenantId,
                "create_or_resume",
                dispatchTracker,
                ct);
            await _db.SaveChangesAsync(ct);
            await CommitTransactionIfNeededAsync(transaction, ct);
            await PublishResolutionSensitiveEventsAsync(
                resolution,
                tenantId,
                "create_or_resume",
                ct);

            if (invalidatedSessions.Count > 0)
            {
                _logger.LogInformation(
                    "Reconciled checkout session duplicates during create/resume | TenantId: {TenantId} | CartId: {CartId} | InvalidatedSessions: {InvalidatedSessions}",
                    tenantId,
                    cartId,
                    invalidatedSessions.Count);

                _reliabilityTelemetry.RecordCheckoutSessionReconciliation(
                    "create_or_resume",
                    "invalidate_duplicates",
                    invalidatedSessions.Count);
            }

            _logger.LogInformation(
                "Resumed checkout session {SessionId} for cart {CartId} tenant {TenantId}",
                canonicalSession.Id,
                cartId,
                tenantId);

            return CreateOperationResult(canonicalSession, dispatchTracker);
        }

        foreach (var mismatchedSession in ownerMismatchedSessions)
        {
            InvalidateSession(mismatchedSession, nowUtc, invalidatedSessions);
        }

        var newSessionCurrency = await ValidateCheckoutCurrencyAsync(
            tenantId,
            currency,
            $"checkout_session:create:{cartId:D}",
            ct);

        var session = new CheckoutSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CartId = cartId,
            RecoveryToken = CreateRecoveryNonce(),
            SessionId = normalizedSessionId,
            OnlineCustomerId = onlineCustomerId,
            ItemsSnapshotJson = itemsSnapshotJson ?? "[]",
            CartTotal = cartTotal,
            Currency = newSessionCurrency,
            DeviceType = string.IsNullOrWhiteSpace(deviceType) ? null : deviceType.Trim(),
            Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim(),
            Status = CheckoutSessionStatus.Active,
            CreatedAtUtc = nowUtc,
            LastActivityAtUtc = nowUtc,
        };

        _db.CheckoutSessions.Add(session);
        foreach (var invalidatedSession in invalidatedSessions)
        {
            await TryStageOutboxEventAsync(
                BuildSessionInvalidatedEvent(invalidatedSession),
                "create_or_resume",
                "session_invalidated",
                dispatchTracker,
                ct);
        }
        await TryStageOutboxEventAsync(
            BuildSessionStartedEvent(
                session,
                tenantId,
                cartId,
                normalizedSessionId,
                onlineCustomerId,
                cartTotal,
                newSessionCurrency),
            "create_or_resume",
            "session_started",
            dispatchTracker,
            ct);
        await _db.SaveChangesAsync(ct);
        await CommitTransactionIfNeededAsync(transaction, ct);

        if (invalidatedSessions.Count > 0)
        {
            _logger.LogInformation(
                "Invalidated stale checkout sessions before creating a canonical session | TenantId: {TenantId} | CartId: {CartId} | InvalidatedSessions: {InvalidatedSessions}",
                tenantId,
                cartId,
                invalidatedSessions.Count);

            _reliabilityTelemetry.RecordCheckoutSessionReconciliation(
                "create_or_resume",
                "invalidate_stale_sessions",
                invalidatedSessions.Count);
        }

        _logger.LogInformation(
            "Created checkout session {SessionId} for cart {CartId} tenant {TenantId}",
            session.Id,
            cartId,
            tenantId);

        return CreateOperationResult(session, dispatchTracker);
    }

    private async Task<List<CheckoutSession>> LoadRecoverableSessionsByCartAsync(
        Guid tenantId,
        Guid cartId,
        CancellationToken ct)
    {
        return await _db.CheckoutSessions
            .IgnoreQueryFilters()
            .Where(session =>
                session.TenantId == tenantId &&
                session.CartId == cartId &&
                (session.Status == CheckoutSessionStatus.Active ||
                 session.Status == CheckoutSessionStatus.Abandoned) &&
                session.InvalidatedAtUtc == null)
            .OrderBy(session => session.Status == CheckoutSessionStatus.Active ? 0 : 1)
            .ThenByDescending(session => session.LastActivityAtUtc)
            .ThenByDescending(session => session.CreatedAtUtc)
            .ThenByDescending(session => session.Id)
            .ToListAsync(ct);
    }

    private async Task<string> ValidateCheckoutCurrencyAsync(
        Guid tenantId,
        string? sessionCurrency,
        string context,
        CancellationToken ct)
    {
        try
        {
            var tenantCurrency = await ResolveRequiredTenantCurrencyAsync(tenantId, context, ct);
            return FinancialCurrencyGuard.RequireMatch(
                sessionCurrency ?? string.Empty,
                "checkout.session.currency",
                tenantCurrency,
                "tenant.currency",
                context);
        }
        catch (FinancialCurrencyMismatchException ex)
        {
            _reliabilityTelemetry.RecordCurrencyMismatch(tenantId, null, "checkout");
            _logger.LogError(ex, "Checkout session currency mismatch rejected | TenantId: {TenantId} | Context: {Context}", tenantId, context);
            throw;
        }
        catch (FinancialCurrencyMissingException ex)
        {
            _reliabilityTelemetry.RecordCurrencyMissing(tenantId, null, "checkout");
            _logger.LogError(ex, "Checkout session currency missing rejected | TenantId: {TenantId} | Context: {Context}", tenantId, context);
            throw;
        }
        catch (FinancialCurrencyInvalidException ex)
        {
            _reliabilityTelemetry.RecordCurrencyInvalid(tenantId, null, "checkout");
            _logger.LogError(ex, "Checkout session currency invalid rejected | TenantId: {TenantId} | Context: {Context}", tenantId, context);
            throw;
        }
    }

    private async Task<string> ResolveRequiredTenantCurrencyAsync(
        Guid tenantId,
        string context,
        CancellationToken ct)
    {
        var tenantCurrency = await _db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Currency)
            .FirstOrDefaultAsync(ct);

        return FinancialCurrencyGuard.RequireCurrency(
            tenantCurrency,
            "tenant.currency",
            context);
    }

    private SessionResolution ResolveSessionAccess(
        string surface,
        CheckoutSession requestedSession,
        IReadOnlyList<CheckoutSession> recoverableSessions,
        DateTime nowUtc,
        bool allowCanonicalRedirect,
        bool requireActive)
    {
        TrackDuplicateSessions(surface, requestedSession.TenantId, requestedSession.CartId, recoverableSessions.Count);

        var resolution = new SessionResolution
        {
            Surface = surface,
            RequestedSession = requestedSession
        };

        var canonicalSession = SelectCanonicalSession(recoverableSessions);
        if (canonicalSession == null)
        {
            return resolution;
        }

        var ownerMatchesCanonical = SameOwner(requestedSession, canonicalSession);

        if (ownerMatchesCanonical)
        {
            var duplicateOwnerSessions = recoverableSessions
                .Where(session => session.Id != canonicalSession.Id && SameOwner(session, canonicalSession))
                .ToList();

            resolution.PublishCanonicalUpdate = MergeCriticalDataIntoCanonical(canonicalSession, duplicateOwnerSessions);
        }

        foreach (var duplicateSession in recoverableSessions.Where(session => session.Id != canonicalSession.Id))
        {
            InvalidateSession(duplicateSession, nowUtc, resolution.InvalidatedSessions);
        }

        if (!ownerMatchesCanonical)
        {
            _logger.LogWarning(
                "Rejected stale checkout session after deterministic reconciliation | TenantId: {TenantId} | CartId: {CartId} | RequestedSessionId: {RequestedSessionId} | CanonicalSessionId: {CanonicalSessionId}",
                requestedSession.TenantId,
                requestedSession.CartId,
                requestedSession.Id,
                canonicalSession.Id);

            resolution.RejectedByCanonicalMismatch = true;
            return resolution;
        }

        if (requireActive && canonicalSession.Status != CheckoutSessionStatus.Active)
        {
            return resolution;
        }

        if (!allowCanonicalRedirect && canonicalSession.Id != requestedSession.Id)
        {
            _logger.LogWarning(
                "Rejected stale checkout session token after deterministic reconciliation | TenantId: {TenantId} | CartId: {CartId} | RequestedSessionId: {RequestedSessionId} | CanonicalSessionId: {CanonicalSessionId}",
                requestedSession.TenantId,
                requestedSession.CartId,
                requestedSession.Id,
                canonicalSession.Id);
            return resolution;
        }

        resolution.CanonicalSession = canonicalSession;
        return resolution;
    }

    private async Task SaveResolutionChangesAsync(
        SessionResolution resolution,
        IDbContextTransaction? transaction,
        CancellationToken ct)
    {
        if (resolution.InvalidatedSessions.Count > 0 || resolution.PublishCanonicalUpdate)
        {
            await _db.SaveChangesAsync(ct);
        }

        await CommitTransactionIfNeededAsync(transaction, ct);
    }

    private async Task StageResolutionOutboxEventsAsync(
        SessionResolution resolution,
        Guid tenantId,
        string operation,
        DispatchTracker dispatchTracker,
        CancellationToken ct)
    {
        foreach (var invalidatedSession in resolution.InvalidatedSessions)
        {
            await TryStageOutboxEventAsync(
                BuildSessionInvalidatedEvent(invalidatedSession),
                operation,
                "session_invalidated",
                dispatchTracker,
                ct);
        }

        if (resolution.InvalidatedSessions.Count > 0)
        {
            _reliabilityTelemetry.RecordCheckoutSessionReconciliation(
                resolution.Surface,
                resolution.RejectedByCanonicalMismatch ? "canonical_mismatch" : "invalidate_duplicates",
                resolution.InvalidatedSessions.Count);
        }

        if (resolution.PublishCanonicalUpdate && resolution.CanonicalSession != null)
        {
            await TryStageOutboxEventAsync(
                BuildSessionUpdatedEvent(resolution.CanonicalSession, tenantId),
                operation,
                "session_updated",
                dispatchTracker,
                ct);

            await TryStageOutboxEventAsync(
                BuildSessionProofSyncRequestedEvent(resolution.CanonicalSession, tenantId),
                operation,
                "session_proof_sync",
                dispatchTracker,
                ct);
        }
    }

    private async Task PublishResolutionSensitiveEventsAsync(
        SessionResolution resolution,
        Guid tenantId,
        string operation,
        CancellationToken ct)
    {
        if (!resolution.PublishCanonicalUpdate || resolution.CanonicalSession == null)
        {
            return;
        }

        await TryPublishSensitiveProofEventAsync(
            BuildSessionProofEvent(resolution.CanonicalSession, tenantId),
            operation,
            ct);
    }

    private static string NormalizeSessionId(string? sessionId) =>
        string.IsNullOrWhiteSpace(sessionId)
            ? null
            : sessionId.Trim();

    private static CheckoutSession? SelectCanonicalSession(IEnumerable<CheckoutSession> sessions)
    {
        CheckoutSession? canonicalSession = null;

        foreach (var session in sessions)
        {
            canonicalSession = session;
            break;
        }

        return canonicalSession;
    }

    private static bool MatchesOwner(
        CheckoutSession session,
        Guid? onlineCustomerId,
        string? normalizedSessionId)
    {
        if (onlineCustomerId.HasValue)
        {
            return session.OnlineCustomerId == onlineCustomerId.Value;
        }

        return !session.OnlineCustomerId.HasValue &&
               string.Equals(
                   NormalizeSessionId(session.SessionId),
                   normalizedSessionId,
                   StringComparison.Ordinal);
    }

    private static bool SameOwner(CheckoutSession left, CheckoutSession right)
    {
        if (left.OnlineCustomerId.HasValue || right.OnlineCustomerId.HasValue)
        {
            return left.OnlineCustomerId.HasValue &&
                   right.OnlineCustomerId.HasValue &&
                   left.OnlineCustomerId.Value == right.OnlineCustomerId.Value;
        }

        var leftSessionId = NormalizeSessionId(left.SessionId);
        var rightSessionId = NormalizeSessionId(right.SessionId);

        return !string.IsNullOrWhiteSpace(leftSessionId) &&
               string.Equals(leftSessionId, rightSessionId, StringComparison.Ordinal);
    }

    private static bool MergeCriticalDataIntoCanonical(
        CheckoutSession canonicalSession,
        IReadOnlyList<CheckoutSession> duplicateOwnerSessions)
    {
        var changed = false;

        foreach (var duplicateSession in duplicateOwnerSessions)
        {
            changed |= CopyIfMissing(
                duplicateSession.CustomerName,
                canonicalSession.CustomerName,
                value => canonicalSession.CustomerName = value);

            changed |= CopyIfMissing(
                duplicateSession.Phone,
                canonicalSession.Phone,
                value => canonicalSession.Phone = value);

            changed |= CopyIfMissing(
                duplicateSession.Email,
                canonicalSession.Email,
                value => canonicalSession.Email = value);

            changed |= CopyIfMissing(
                duplicateSession.City,
                canonicalSession.City,
                value => canonicalSession.City = value);

            changed |= CopyIfMissing(
                duplicateSession.Area,
                canonicalSession.Area,
                value => canonicalSession.Area = value);

            changed |= CopyIfMissing(
                duplicateSession.Address,
                canonicalSession.Address,
                value => canonicalSession.Address = value);

            changed |= CopyIfMissing(
                duplicateSession.Country,
                canonicalSession.Country,
                value => canonicalSession.Country = value);

            changed |= CopyIfMissing(
                duplicateSession.Notes,
                canonicalSession.Notes,
                value => canonicalSession.Notes = value);

            changed |= CopyIfMissing(
                duplicateSession.PaymentMethodCode,
                canonicalSession.PaymentMethodCode,
                value => canonicalSession.PaymentMethodCode = value);

            changed |= CopyIfMissing(
                duplicateSession.Currency,
                canonicalSession.Currency,
                value => canonicalSession.Currency = value);

            changed |= CopyIfMissing(
                duplicateSession.WalletType,
                canonicalSession.WalletType,
                value => canonicalSession.WalletType = value);

            changed |= CopyIfMissing(
                duplicateSession.WalletPhoneNumber,
                canonicalSession.WalletPhoneNumber,
                value => canonicalSession.WalletPhoneNumber = value);

            changed |= CopyIfMissing(
                duplicateSession.ProofImageUrl,
                canonicalSession.ProofImageUrl,
                value => canonicalSession.ProofImageUrl = value);

            changed |= CopyIfMissing(
                duplicateSession.DeviceType,
                canonicalSession.DeviceType,
                value => canonicalSession.DeviceType = value);

            changed |= CopyIfMissing(
                duplicateSession.Source,
                canonicalSession.Source,
                value => canonicalSession.Source = value);

            if (!canonicalSession.ShippingMethodId.HasValue && duplicateSession.ShippingMethodId.HasValue)
            {
                canonicalSession.ShippingMethodId = duplicateSession.ShippingMethodId;
                changed = true;
            }

            if (!canonicalSession.CartTotal.HasValue && duplicateSession.CartTotal.HasValue)
            {
                canonicalSession.CartTotal = duplicateSession.CartTotal;
                changed = true;
            }

            if (IsEmptyItemsSnapshot(canonicalSession.ItemsSnapshotJson) &&
                !IsEmptyItemsSnapshot(duplicateSession.ItemsSnapshotJson))
            {
                canonicalSession.ItemsSnapshotJson = duplicateSession.ItemsSnapshotJson;
                changed = true;
            }

            var earliestFirstActivity = MinTimestamp(
                canonicalSession.FirstActivityAtUtc,
                duplicateSession.FirstActivityAtUtc);

            if (earliestFirstActivity != canonicalSession.FirstActivityAtUtc)
            {
                canonicalSession.FirstActivityAtUtc = earliestFirstActivity;
                changed = true;
            }

            if (duplicateSession.LastActivityAtUtc > canonicalSession.LastActivityAtUtc)
            {
                canonicalSession.LastActivityAtUtc = duplicateSession.LastActivityAtUtc;
                changed = true;
            }
        }

        return changed;
    }

    private static bool CopyIfMissing(
        string? source,
        string? target,
        Action<string> assign)
    {
        if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(source))
        {
            assign(source.Trim());
            return true;
        }

        return false;
    }

    private static DateTime? MinTimestamp(DateTime? left, DateTime? right)
    {
        if (!left.HasValue)
        {
            return right;
        }

        if (!right.HasValue)
        {
            return left;
        }

        return left.Value <= right.Value ? left : right;
    }

    private static bool IsEmptyItemsSnapshot(string? itemsSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(itemsSnapshotJson))
        {
            return true;
        }

        return string.Equals(itemsSnapshotJson.Trim(), "[]", StringComparison.Ordinal);
    }

    private static void InvalidateSession(
        CheckoutSession session,
        DateTime nowUtc,
        ICollection<CheckoutSession> invalidatedSessions)
    {
        session.RecoveryToken = CreateRecoveryNonce();
        session.LastActivityAtUtc = nowUtc;
        session.Status = CheckoutSessionStatus.Abandoned;
        session.AbandonedAtUtc ??= nowUtc;
        session.InvalidatedAtUtc = nowUtc;

        invalidatedSessions.Add(session);
    }

    private async Task<IDbContextTransaction?> BeginConsistencyTransactionAsync(CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            return null;
        }

        return await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
    }

    private async Task<bool> AcquireCartScopedLockAsync(Guid tenantId, Guid cartId, CancellationToken ct)
    {
        if (!IsNpgsqlProvider())
        {
            _logger.LogDebug(
                "Cart-scoped lock not available for non-Npgsql provider | Provider: {Provider} | TenantId: {TenantId} | CartId: {CartId}",
                _db.Database.ProviderName ?? "Unknown",
                tenantId,
                cartId);
            return false;
        }

        var lockKey = ComputeCartLockKey(tenantId, cartId);
        await _db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0});",
            new object[] { lockKey },
            ct);
        return true;
    }

    private bool IsNpgsqlProvider() =>
        _db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private static long ComputeCartLockKey(Guid tenantId, Guid cartId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId:N}:{cartId:N}"));
        return BitConverter.ToInt64(hash, 0);
    }

    private static async Task CommitTransactionIfNeededAsync(
        IDbContextTransaction? transaction,
        CancellationToken ct)
    {
        if (transaction != null)
        {
            await transaction.CommitAsync(ct);
        }
    }

    private void TrackDuplicateSessions(
        string surface,
        Guid tenantId,
        Guid cartId,
        int recoverableSessionCount)
    {
        if (recoverableSessionCount <= 1)
        {
            return;
        }

        var duplicateCount = recoverableSessionCount - 1;

        _logger.LogWarning(
            "Duplicate checkout sessions detected | Surface: {Surface} | TenantId: {TenantId} | CartId: {CartId} | DuplicateCount: {DuplicateCount}",
            surface,
            tenantId,
            cartId,
            duplicateCount);

        _reliabilityTelemetry.RecordCheckoutSessionDuplicateDetected(surface, duplicateCount);
    }

    private static bool IsActiveSessionUniqueViolation(DbUpdateException exception)
    {
        if (exception.InnerException is PostgresException postgresException)
        {
            return postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
                   string.Equals(
                       postgresException.ConstraintName,
                       ActiveSessionUniqueIndexName,
                       StringComparison.Ordinal);
        }

        var errorMessage = exception.InnerException?.Message ?? exception.Message;
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return false;
        }

        return errorMessage.Contains(ActiveSessionUniqueIndexName, StringComparison.Ordinal) ||
               errorMessage.Contains(
                   "UNIQUE constraint failed: CheckoutSessions.TenantId, CheckoutSessions.CartId, CheckoutSessions.Status",
                   StringComparison.Ordinal);
    }

    private static string CreateRecoveryNonce()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Determines if a payment method code represents a manual wallet payment method that requires proof data.
    /// Returns false for empty/null values or non-wallet payment methods, indicating proof data should be cleared.
    /// Uses an explicit check against the MANUALWALLET payment method code.
    /// </summary>
    private static bool IsManualWalletPaymentMethod(string? paymentMethodCode)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodCode))
        {
            return false;
        }

        return string.Equals(paymentMethodCode, "MANUALWALLET", StringComparison.OrdinalIgnoreCase);
    }

    private CheckoutSessionOperationResult CreateOperationResult(
        CheckoutSession? session,
        DispatchTracker dispatchTracker)
    {
        if (session == null)
        {
            return CheckoutSessionOperationResult.Success(null);
        }

        return dispatchTracker.HasFailures
            ? CheckoutSessionOperationResult.PartialSuccess(session, dispatchTracker.FailureStages)
            : CheckoutSessionOperationResult.Success(session);
    }

    private async Task TryStageOutboxEventAsync(
        IntegrationEvent integrationEvent,
        string operation,
        string stage,
        DispatchTracker dispatchTracker,
        CancellationToken ct)
    {
        try
        {
            await _eventPublisher.PublishAsync(integrationEvent, ct);
        }
        catch (Exception ex)
        {
            dispatchTracker.RecordFailure(stage);
            _reliabilityTelemetry.RecordCheckoutEventPublishFailed(operation, stage, "outbox");
            _logger.LogError(
                ex,
                "Failed to stage checkout session event in outbox | Operation: {Operation} | Stage: {Stage} | TenantId: {TenantId}",
                operation,
                stage,
                integrationEvent.TenantId);
        }
    }

    private async Task TryPublishSensitiveProofEventAsync(
        CheckoutSessionProofEvent integrationEvent,
        string operation,
        CancellationToken ct)
    {
        try
        {
            await _eventPublisher.PublishSensitiveAsync(integrationEvent, ct);
        }
        catch (Exception ex)
        {
            // Best-effort only - do NOT record in DispatchTracker to avoid surfacing as partial_success
            _reliabilityTelemetry.RecordCheckoutEventPublishFailed(operation, "session_proof", "direct");
            _logger.LogError(
                ex,
                "Failed to publish checkout session proof event after commit (best-effort) | Operation: {Operation} | TenantId: {TenantId} | SessionId: {SessionId}",
                operation,
                integrationEvent.TenantId,
                integrationEvent.CheckoutSessionId);
        }
    }

    private static CheckoutSessionStartedEvent BuildSessionStartedEvent(
        CheckoutSession session,
        Guid tenantId,
        Guid cartId,
        string? normalizedSessionId,
        Guid? onlineCustomerId,
        decimal? cartTotal,
        string newSessionCurrency) =>
        new()
        {
            TenantId = tenantId,
            CheckoutSessionId = session.Id,
            CartId = cartId,
            SessionId = normalizedSessionId,
            OnlineCustomerId = onlineCustomerId,
            ItemsSnapshotJson = session.ItemsSnapshotJson,
            CartTotal = cartTotal,
            Currency = newSessionCurrency,
            RecoveryToken = session.RecoveryToken,
            DeviceType = session.DeviceType,
            Source = session.Source,
            CreatedAtUtc = session.CreatedAtUtc,
        };

    private static CheckoutSessionUpdatedEvent BuildSessionUpdatedEvent(CheckoutSession session, Guid tenantId) =>
        new()
        {
            TenantId = tenantId,
            CheckoutSessionId = session.Id,
            CustomerName = session.CustomerName,
            Phone = session.Phone,
            Email = session.Email,
            City = session.City,
            Area = session.Area,
            Address = session.Address,
            Country = session.Country,
            Notes = session.Notes,
            ItemsSnapshotJson = session.ItemsSnapshotJson,
            CartTotal = session.CartTotal,
            Currency = session.Currency,
            ShippingMethodId = session.ShippingMethodId,
            PaymentMethodCode = session.PaymentMethodCode,
            FirstActivityAtUtc = session.FirstActivityAtUtc,
            LastActivityAtUtc = session.LastActivityAtUtc,
        };

    private static CheckoutSessionProofEvent BuildSessionProofEvent(CheckoutSession session, Guid tenantId) =>
        new()
        {
            TenantId = tenantId,
            CheckoutSessionId = session.Id,
            WalletType = session.WalletType,
            WalletPhoneNumber = session.WalletPhoneNumber,
            ProofImageUrl = session.ProofImageUrl,
        };

    private static CheckoutSessionProofSyncRequestedEvent BuildSessionProofSyncRequestedEvent(
        CheckoutSession session,
        Guid tenantId) =>
        new()
        {
            TenantId = tenantId,
            CheckoutSessionId = session.Id,
            // Include ConvertedAtUtc as proof-state discriminator to ensure proof-sync events
            // are not deduped when conversion clears wallet/proof fields
            // Format: CHECKOUT_SESSION_PROOF_SYNC:{tenantId}:{sessionId}:{lastActivity}:{converted}
            IdempotencyKey = $"CHECKOUT_SESSION_PROOF_SYNC:{tenantId}:{session.Id}:{session.LastActivityAtUtc:O}:{session.ConvertedAtUtc:O}"
        };

    private static CheckoutSessionAbandonedEvent BuildSessionInvalidatedEvent(CheckoutSession session) =>
        new()
        {
            TenantId = session.TenantId,
            CheckoutSessionId = session.Id,
            CartId = session.CartId,
            CustomerName = session.CustomerName,
            Phone = session.Phone,
            City = session.City,
            ItemsSnapshotJson = session.ItemsSnapshotJson,
            CartTotal = session.CartTotal,
            Currency = session.Currency,
            DeviceType = session.DeviceType,
            Source = session.Source,
            RecoveryToken = session.RecoveryToken,
            FirstActivityAtUtc = session.FirstActivityAtUtc,
            AbandonedAtUtc = session.AbandonedAtUtc ?? DateTime.UtcNow,
            LastActivityAtUtc = session.LastActivityAtUtc,
        };

    private sealed class SessionResolution
    {
        public string Surface { get; init; } = string.Empty;

        public CheckoutSession RequestedSession { get; init; } = null!;

        public CheckoutSession? CanonicalSession { get; set; }

        public List<CheckoutSession> InvalidatedSessions { get; } = new();

        public bool PublishCanonicalUpdate { get; set; }

        public bool RejectedByCanonicalMismatch { get; set; }
    }

    private sealed class DispatchTracker
    {
        private readonly List<string> _failureStages = new();

        public bool HasFailures => _failureStages.Count > 0;

        public IReadOnlyList<string> FailureStages => _failureStages;

        public void RecordFailure(string stage)
        {
            if (!_failureStages.Contains(stage, StringComparer.Ordinal))
            {
                _failureStages.Add(stage);
            }
        }
    }
}
