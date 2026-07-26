using Hangfire;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PointOfSale.Core.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Observability;

namespace PointOfSale.Infrastructure.Jobs;

/// <summary>
/// Releases stale inventory reservations that no longer have an active payment flow.
/// Safe to replay because reservation release is idempotent at the repository level.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 300)]
public sealed class InventoryReservationCleanupJob
{
    private static readonly string[] BlockingUnresolvedPaymentStatuses =
    [
        "PendingResolution",
        "PendingTenantResolution",
        "RoutedToSuspense",
        "SuspenseMatchedPaymentPendingClearance"
    ];

    private readonly ApplicationDbContext _dbContext;
    private readonly IProductVariantStockRepository _stockRepository;
    private readonly IInventoryReservationMetricsService _metricsService;
    private readonly ICouponValidationService _couponValidationService;
    private readonly IOptions<InventoryReservationMonitoringOptions> _options;
    private readonly ILogger<InventoryReservationCleanupJob> _logger;
    private readonly IPaymentLifecycleService _paymentLifecycleService;

    public InventoryReservationCleanupJob(
        ApplicationDbContext dbContext,
        IProductVariantStockRepository stockRepository,
        IInventoryReservationMetricsService metricsService,
        ICouponValidationService couponValidationService,
        IOptions<InventoryReservationMonitoringOptions> options,
        ILogger<InventoryReservationCleanupJob> logger,
        IPaymentLifecycleService paymentLifecycleService)
    {
        _dbContext = dbContext;
        _stockRepository = stockRepository;
        _metricsService = metricsService;
        _couponValidationService = couponValidationService;
        _options = options;
        _logger = logger;
        _paymentLifecycleService = paymentLifecycleService;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var staleCutoff = now.AddMinutes(-_options.Value.StaleReservationThresholdMinutes);
        var activePaymentCutoff = now.AddMinutes(-_options.Value.ActivePaymentGraceMinutes);
        var providerReconciliationCutoff = now.AddMinutes(-_options.Value.ProviderReconciliationGraceMinutes);
        var orphanReservationCutoff = now.AddMinutes(-_options.Value.OrphanReservationGraceMinutes);

        var candidates = await _dbContext.InventoryReservations
            .Where(reservation =>
                !reservation.IsDeleted &&
                reservation.ReservedQuantity > reservation.CommittedQuantity + reservation.ReleasedQuantity &&
                ((reservation.ExpiresAtUtc.HasValue && reservation.ExpiresAtUtc.Value <= now) ||
                 (!reservation.ExpiresAtUtc.HasValue && reservation.CreatedAt <= staleCutoff) ||
                 (reservation.OrderItemId == Guid.Empty && reservation.CreatedAt <= orphanReservationCutoff)))
            .GroupBy(reservation => new { reservation.TenantId, reservation.OrderId })
            .Select(group => new ReservationCleanupCandidate
            {
                TenantId = group.Key.TenantId,
                OrderId = group.Key.OrderId,
                RemainingReservedQuantity = group.Sum(item => item.ReservedQuantity - item.CommittedQuantity - item.ReleasedQuantity),
                OldestReservationUtc = group.Min(item => item.ExpiresAtUtc ?? item.CreatedAt),
                HasExpiredReservation = group.Any(item => item.ExpiresAtUtc.HasValue && item.ExpiresAtUtc.Value <= now)
            })
            .OrderBy(candidate => candidate.OldestReservationUtc)
            .Take(_options.Value.CleanupBatchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            await LogSnapshotAsync(cancellationToken);
            return;
        }

        var orderIds = candidates.Select(candidate => candidate.OrderId).Distinct().ToList();
        var orderStates = await _dbContext.Orders
            .Where(order => orderIds.Contains(order.Id))
            .Select(order => new OrderCleanupState
            {
                OrderId = order.Id,
                OrderStatus = order.Status,
                PaymentStatus = order.PaymentStatus,
                LatestPaymentIntentStatus = _dbContext.PaymentIntents
                    .Where(intent => intent.OrderId == order.Id && !intent.IsDeleted)
                    .OrderByDescending(intent => intent.CreatedAt)
                    .Select(intent => (PaymentIntentStatus?)intent.Status)
                    .FirstOrDefault(),
                LatestPaymentIntentCreatedAtUtc = _dbContext.PaymentIntents
                    .Where(intent => intent.OrderId == order.Id && !intent.IsDeleted)
                    .OrderByDescending(intent => intent.CreatedAt)
                    .Select(intent => (DateTime?)intent.CreatedAt)
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(state => state.OrderId, cancellationToken);

        var commerceHubOrderStates = await _dbContext.CommerceHubMarketplaceOrders
            .Where(order => orderIds.Contains(order.Id))
            .Select(order => new CommerceHubOrderCleanupState
            {
                OrderId = order.Id,
                OrderStatus = order.Status,
                PaymentStatus = order.PaymentStatus,
                LatestPaymentStatus = _dbContext.CommerceHubPayments
                    .Where(payment => payment.OrderId == order.Id && !payment.IsDeleted)
                    .OrderByDescending(payment => payment.CreatedAt)
                    .Select(payment => (CommerceHubPaymentRecordStatus?)payment.Status)
                    .FirstOrDefault(),
                LatestPaymentCreatedAtUtc = _dbContext.CommerceHubPayments
                    .Where(payment => payment.OrderId == order.Id && !payment.IsDeleted)
                    .OrderByDescending(payment => payment.CreatedAt)
                    .Select(payment => (DateTime?)payment.CreatedAt)
                    .FirstOrDefault(),
                LatestPaymentProviderIntentId = _dbContext.CommerceHubPayments
                    .Where(payment => payment.OrderId == order.Id && !payment.IsDeleted)
                    .OrderByDescending(payment => payment.CreatedAt)
                    .Select(payment => payment.ProviderIntentId)
                    .FirstOrDefault(),
                LatestPaymentProviderTransactionId = _dbContext.CommerceHubPayments
                    .Where(payment => payment.OrderId == order.Id && !payment.IsDeleted)
                    .OrderByDescending(payment => payment.CreatedAt)
                    .Select(payment => payment.ProviderTransactionId)
                    .FirstOrDefault(),
                LatestPaymentFailureReason = _dbContext.CommerceHubPayments
                    .Where(payment => payment.OrderId == order.Id && !payment.IsDeleted)
                    .OrderByDescending(payment => payment.CreatedAt)
                    .Select(payment => payment.FailureReason)
                    .FirstOrDefault(),
                HasBlockingUnresolvedWebhook = _dbContext.CommerceHubUnresolvedPaymentWebhooks
                    .Where(webhook =>
                        !webhook.IsDeleted &&
                        webhook.OrderId == order.Id &&
                        BlockingUnresolvedPaymentStatuses.Contains(webhook.Status))
                    .Any()
            })
            .ToDictionaryAsync(state => state.OrderId, cancellationToken);

        var releasedOrders = 0;
        decimal releasedQuantity = 0m;
        var skippedActiveOrders = 0;

        foreach (var candidate in candidates)
        {
            orderStates.TryGetValue(candidate.OrderId, out var orderState);
            commerceHubOrderStates.TryGetValue(candidate.OrderId, out var commerceHubOrderState);

            if (!ShouldReleaseReservation(orderState, activePaymentCutoff, candidate.HasExpiredReservation) ||
                !ShouldReleaseCommerceHubReservation(
                    commerceHubOrderState,
                    activePaymentCutoff,
                    providerReconciliationCutoff,
                    candidate.HasExpiredReservation))
            {
                skippedActiveOrders++;
                continue;
            }

            try
            {
                var currentAuthority = await ReloadCleanupAuthorityAsync(
                    candidate,
                    lockRows: false,
                    cancellationToken);

                if (currentAuthority.RemainingReservedQuantity <= 0m)
                {
                    continue;
                }

                if (!ShouldReleaseReservation(currentAuthority.OrderState, activePaymentCutoff, currentAuthority.HasExpiredReservation) ||
                    !ShouldReleaseCommerceHubReservation(
                        currentAuthority.CommerceHubOrderState,
                        activePaymentCutoff,
                        providerReconciliationCutoff,
                        currentAuthority.HasExpiredReservation))
                {
                    skippedActiveOrders++;
                    continue;
                }

                if (currentAuthority.OrderState?.PaymentStatus is PaymentStatus.Pending or PaymentStatus.Authorized or PaymentStatus.AwaitingVerification)
                {
                    var lifecycleResult = await _paymentLifecycleService.MarkPaymentExpiredAsync(
                        candidate.OrderId,
                        "Inventory reservation expired and was released by cleanup.",
                        cancellationToken);

                    if (!lifecycleResult.IsSuccess)
                    {
                        _logger.LogError(
                            "Failed to expire payment state via lifecycle during reservation cleanup | TenantId: {TenantId} | OrderId: {OrderId} | Error: {ErrorMessage}",
                            candidate.TenantId,
                            candidate.OrderId,
                            lifecycleResult.ErrorMessage);
                        continue;
                    }

                    // MarkPaymentExpiredAsync already handled stock release and metrics recording
                    // Do not double-count metrics, but update local summary counters
                    releasedOrders++;
                    releasedQuantity += currentAuthority.RemainingReservedQuantity;
                    
                    _logger.LogWarning(
                        "Released stale inventory reservations via payment lifecycle | TenantId: {TenantId} | OrderId: {OrderId} | RemainingReservedQuantity: {RemainingReservedQuantity} | OldestReservationUtc: {OldestReservationUtc}",
                        candidate.TenantId, candidate.OrderId, currentAuthority.RemainingReservedQuantity, currentAuthority.OldestReservationUtc);
                }
                else
                {
                    await using var transaction = await BeginCleanupTransactionAsync(cancellationToken);

                    _dbContext.ChangeTracker.Clear();
                    var lockedAuthority = await ReloadCleanupAuthorityAsync(
                        candidate,
                        lockRows: true,
                        cancellationToken);

                    if (lockedAuthority.RemainingReservedQuantity <= 0m)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        continue;
                    }

                    if (!ShouldReleaseReservation(lockedAuthority.OrderState, activePaymentCutoff, lockedAuthority.HasExpiredReservation) ||
                        !ShouldReleaseCommerceHubReservation(
                            lockedAuthority.CommerceHubOrderState,
                            activePaymentCutoff,
                            providerReconciliationCutoff,
                            lockedAuthority.HasExpiredReservation))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        skippedActiveOrders++;
                        _logger.LogInformation(
                            "Skipped stale reservation cleanup after locked SQL authority changed | TenantId: {TenantId} | OrderId: {OrderId}",
                            candidate.TenantId,
                            candidate.OrderId);
                        continue;
                    }

                    if (lockedAuthority.OrderState?.PaymentStatus is PaymentStatus.Pending or PaymentStatus.Authorized or PaymentStatus.AwaitingVerification)
                    {
                        await transaction.RollbackAsync(cancellationToken);

                        var lifecycleResult = await _paymentLifecycleService.MarkPaymentExpiredAsync(
                            candidate.OrderId,
                            "Inventory reservation expired and was released by cleanup.",
                            cancellationToken);

                        if (!lifecycleResult.IsSuccess)
                        {
                            _logger.LogError(
                                "Failed to expire payment state via lifecycle after locked cleanup recomposition | TenantId: {TenantId} | OrderId: {OrderId} | Error: {ErrorMessage}",
                                candidate.TenantId,
                                candidate.OrderId,
                                lifecycleResult.ErrorMessage);
                            continue;
                        }

                        releasedOrders++;
                        releasedQuantity += lockedAuthority.RemainingReservedQuantity;
                        continue;
                    }

                    var inventoryReleased = await _stockRepository.ReleaseOrderReservationsAsync(
                        candidate.TenantId,
                        candidate.OrderId,
                        cancellationToken);

                    if (inventoryReleased)
                    {
                        await _couponValidationService.ReleaseCouponReservationAsync(
                            candidate.TenantId,
                            candidate.OrderId,
                            "inventory_reservation_cleanup_job",
                            "inventory_reservation_expired",
                            cancellationToken);

                        await transaction.CommitAsync(cancellationToken);

                        // Only record metrics when local release path runs
                        _metricsService.RecordRelease(lockedAuthority.RemainingReservedQuantity, "cleanup_job");
                        releasedOrders++;
                        releasedQuantity += lockedAuthority.RemainingReservedQuantity;

                        _logger.LogWarning(
                            "Released stale inventory reservations | TenantId: {TenantId} | OrderId: {OrderId} | RemainingReservedQuantity: {RemainingReservedQuantity} | OldestReservationUtc: {OldestReservationUtc}",
                            candidate.TenantId, candidate.OrderId, lockedAuthority.RemainingReservedQuantity, lockedAuthority.OldestReservationUtc);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to release stale inventory reservations | TenantId: {TenantId} | OrderId: {OrderId}",
                    candidate.TenantId,
                    candidate.OrderId);
            }
        }

        _logger.LogInformation(
            "Inventory reservation cleanup completed | ReleasedOrders: {ReleasedOrders} | ReleasedQuantity: {ReleasedQuantity} | SkippedActiveOrders: {SkippedActiveOrders}",
            releasedOrders, releasedQuantity, skippedActiveOrders);

        await LogSnapshotAsync(cancellationToken);
    }

    private async Task<IDbContextTransaction> BeginCleanupTransactionAsync(CancellationToken cancellationToken)
    {
        return _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : await _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    private async Task<ReservationCleanupAuthority> ReloadCleanupAuthorityAsync(
        ReservationCleanupCandidate candidate,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var reservationState = await LoadReservationAuthorityStateAsync(candidate, lockRows, cancellationToken);
        var orderState = await LoadOrderCleanupStateAsync(candidate, lockRows, cancellationToken);
        var commerceHubOrderState = await LoadCommerceHubOrderCleanupStateAsync(candidate, lockRows, cancellationToken);

        return new ReservationCleanupAuthority(
            orderState,
            commerceHubOrderState,
            reservationState.RemainingReservedQuantity,
            reservationState.OldestReservationUtc,
            reservationState.HasExpiredReservation);
    }

    private async Task<ReservationAuthorityState> LoadReservationAuthorityStateAsync(
        ReservationCleanupCandidate candidate,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        List<InventoryReservationAuthorityRow> rows;

        if (lockRows && IsPostgresProvider())
        {
            var reservations = await _dbContext.InventoryReservations
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""InventoryReservations""
                    WHERE ""TenantId"" = {candidate.TenantId}
                      AND ""OrderId"" = {candidate.OrderId}
                      AND ""IsDeleted"" = false
                    ORDER BY ""CreatedAt"", ""Id""
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            rows = reservations
                .Select(reservation => new InventoryReservationAuthorityRow(
                    reservation.ReservedQuantity,
                    reservation.CommittedQuantity,
                    reservation.ReleasedQuantity,
                    reservation.ExpiresAtUtc,
                    reservation.CreatedAt))
                .ToList();
        }
        else if (lockRows && IsSqlServerProvider())
        {
            var reservations = await _dbContext.InventoryReservations
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [InventoryReservations] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {candidate.TenantId}
                      AND [OrderId] = {candidate.OrderId}
                      AND [IsDeleted] = 0
                    ORDER BY [CreatedAt], [Id]")
                .IgnoreQueryFilters()
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            rows = reservations
                .Select(reservation => new InventoryReservationAuthorityRow(
                    reservation.ReservedQuantity,
                    reservation.CommittedQuantity,
                    reservation.ReleasedQuantity,
                    reservation.ExpiresAtUtc,
                    reservation.CreatedAt))
                .ToList();
        }
        else
        {
            rows = await _dbContext.InventoryReservations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(reservation =>
                    reservation.TenantId == candidate.TenantId &&
                    reservation.OrderId == candidate.OrderId &&
                    !reservation.IsDeleted)
                .OrderBy(reservation => reservation.CreatedAt)
                .ThenBy(reservation => reservation.Id)
                .Select(reservation => new InventoryReservationAuthorityRow(
                    reservation.ReservedQuantity,
                    reservation.CommittedQuantity,
                    reservation.ReleasedQuantity,
                    reservation.ExpiresAtUtc,
                    reservation.CreatedAt))
                .ToListAsync(cancellationToken);
        }

        if (rows.Count == 0)
        {
            return new ReservationAuthorityState(0m, candidate.OldestReservationUtc, false);
        }

        var remaining = rows.Sum(row => row.ReservedQuantity - row.CommittedQuantity - row.ReleasedQuantity);
        var oldest = rows.Min(row => row.ExpiresAtUtc ?? row.CreatedAt);
        var now = DateTime.UtcNow;
        var hasExpired = rows.Any(row => row.ExpiresAtUtc.HasValue && row.ExpiresAtUtc.Value <= now);

        return new ReservationAuthorityState(remaining, oldest, hasExpired);
    }

    private async Task<OrderCleanupState?> LoadOrderCleanupStateAsync(
        ReservationCleanupCandidate candidate,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var order = await LoadOrderForCleanupAuthorityAsync(candidate, lockRows, cancellationToken);
        if (order == null)
        {
            return null;
        }

        var latestPaymentIntent = await _dbContext.PaymentIntents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(intent => intent.OrderId == order.Id && !intent.IsDeleted)
            .OrderByDescending(intent => intent.CreatedAt)
            .Select(intent => new
            {
                Status = (PaymentIntentStatus?)intent.Status,
                CreatedAtUtc = (DateTime?)intent.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new OrderCleanupState
        {
            OrderId = order.Id,
            OrderStatus = order.Status,
            PaymentStatus = order.PaymentStatus,
            LatestPaymentIntentStatus = latestPaymentIntent?.Status,
            LatestPaymentIntentCreatedAtUtc = latestPaymentIntent?.CreatedAtUtc
        };
    }

    private async Task<Order?> LoadOrderForCleanupAuthorityAsync(
        ReservationCleanupCandidate candidate,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        if (lockRows && IsPostgresProvider())
        {
            return await _dbContext.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""Orders""
                    WHERE ""TenantId"" = {candidate.TenantId}
                      AND ""Id"" = {candidate.OrderId}
                      AND ""IsDeleted"" = false
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (lockRows && IsSqlServerProvider())
        {
            return await _dbContext.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {candidate.TenantId}
                      AND [Id] = {candidate.OrderId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                order => order.TenantId == candidate.TenantId &&
                         order.Id == candidate.OrderId &&
                         !order.IsDeleted,
                cancellationToken);
    }

    private async Task<CommerceHubOrderCleanupState?> LoadCommerceHubOrderCleanupStateAsync(
        ReservationCleanupCandidate candidate,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var order = await LoadCommerceHubOrderForCleanupAuthorityAsync(candidate, lockRows, cancellationToken);
        if (order == null)
        {
            return null;
        }

        var latestPayment = await _dbContext.CommerceHubPayments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(payment => payment.OrderId == order.Id && !payment.IsDeleted)
            .OrderByDescending(payment => payment.CreatedAt)
            .Select(payment => new
            {
                Status = (CommerceHubPaymentRecordStatus?)payment.Status,
                CreatedAtUtc = (DateTime?)payment.CreatedAt,
                payment.ProviderIntentId,
                payment.ProviderTransactionId,
                payment.FailureReason
            })
            .FirstOrDefaultAsync(cancellationToken);

        var hasBlockingUnresolvedWebhook = await _dbContext.CommerceHubUnresolvedPaymentWebhooks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                webhook =>
                    !webhook.IsDeleted &&
                    webhook.OrderId == order.Id &&
                    BlockingUnresolvedPaymentStatuses.Contains(webhook.Status),
                cancellationToken);

        return new CommerceHubOrderCleanupState
        {
            OrderId = order.Id,
            OrderStatus = order.Status,
            PaymentStatus = order.PaymentStatus,
            LatestPaymentStatus = latestPayment?.Status,
            LatestPaymentCreatedAtUtc = latestPayment?.CreatedAtUtc,
            LatestPaymentProviderIntentId = latestPayment?.ProviderIntentId,
            LatestPaymentProviderTransactionId = latestPayment?.ProviderTransactionId,
            LatestPaymentFailureReason = latestPayment?.FailureReason,
            HasBlockingUnresolvedWebhook = hasBlockingUnresolvedWebhook
        };
    }

    private async Task<CommerceHubMarketplaceOrder?> LoadCommerceHubOrderForCleanupAuthorityAsync(
        ReservationCleanupCandidate candidate,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        if (lockRows && IsPostgresProvider())
        {
            return await _dbContext.CommerceHubMarketplaceOrders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""CommerceHubMarketplaceOrders""
                    WHERE ""Id"" = {candidate.OrderId}
                      AND ""IsDeleted"" = false
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (lockRows && IsSqlServerProvider())
        {
            return await _dbContext.CommerceHubMarketplaceOrders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [CommerceHubMarketplaceOrders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {candidate.OrderId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.CommerceHubMarketplaceOrders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                order => order.Id == candidate.OrderId &&
                         !order.IsDeleted,
                cancellationToken);
    }

    private async Task LogSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _metricsService.RefreshSnapshotAsync(cancellationToken);

        if (snapshot.StaleReservedQuantity > 0m)
        {
            _logger.LogWarning(
                "ReservedQuantity remains > 0 beyond threshold | TotalReservedQuantity: {TotalReservedQuantity} | StaleReservationCount: {StaleReservationCount} | StaleReservedQuantity: {StaleReservedQuantity} | OldestStaleReservationAgeMinutes: {OldestStaleReservationAgeMinutes} | CommitReleaseRatio: {CommitReleaseRatio}",
                snapshot.TotalReservedQuantity,
                snapshot.StaleReservationCount,
                snapshot.StaleReservedQuantity,
                snapshot.OldestStaleReservationAgeMinutes,
                snapshot.CommitReleaseRatio);
        }
        else
        {
            _logger.LogInformation(
                "Inventory reservation health snapshot | TotalReservedQuantity: {TotalReservedQuantity} | CommitReleaseRatio: {CommitReleaseRatio}",
                snapshot.TotalReservedQuantity,
                snapshot.CommitReleaseRatio);
        }
    }

    private static bool ShouldReleaseReservation(
        OrderCleanupState? orderState,
        DateTime activePaymentCutoff,
        bool hasExpiredReservation)
    {
        if (orderState == null)
        {
            return true;
        }

        // CRITICAL: Never release reservations for active/confirmed orders
        // Active orders include: Confirmed, Processing, Shipped, Delivered, Completed, OnHold
        // Only release for: Pending (with failed payment) or Cancelled
        if (orderState.OrderStatus is OrderStatus.Confirmed 
            or OrderStatus.Processing 
            or OrderStatus.Shipped 
            or OrderStatus.Delivered 
            or OrderStatus.Completed 
            or OrderStatus.OnHold)
        {
            return false;
        }

        // Don't release if payment is already collected/settled
        if (orderState.PaymentStatus is PaymentStatus.Collected or PaymentStatus.Paid or PaymentStatus.Settled)
        {
            return false;
        }

        // Don't release if latest payment intent shows successful payment
        if (orderState.LatestPaymentIntentStatus == PaymentIntentStatus.Paid)
        {
            return false;
        }

        // Don't release if there's a recently active payment intent (within grace period)
        var hasRecentlyActivePaymentIntent =
            orderState.LatestPaymentIntentStatus is PaymentIntentStatus.Pending or PaymentIntentStatus.Authorized or PaymentIntentStatus.AwaitingVerification &&
            orderState.LatestPaymentIntentCreatedAtUtc.HasValue &&
            orderState.LatestPaymentIntentCreatedAtUtc.Value >= activePaymentCutoff;

        if (hasRecentlyActivePaymentIntent)
        {
            return hasExpiredReservation;
        }

        // Safe to release: Order is Pending/Cancelled with no active payment
        return true;
    }

    private static bool ShouldReleaseCommerceHubReservation(
        CommerceHubOrderCleanupState? orderState,
        DateTime activePaymentCutoff,
        DateTime providerReconciliationCutoff,
        bool hasExpiredReservation)
    {
        if (orderState == null)
        {
            return true;
        }

        if (orderState.OrderStatus is OrderStatus.Confirmed
            or OrderStatus.Processing
            or OrderStatus.Shipped
            or OrderStatus.Delivered
            or OrderStatus.Completed
            or OrderStatus.OnHold)
        {
            return false;
        }

        // Reservation TTL must not override the order lifecycle, but terminal
        // cancelled/refunded orders are safe to repair if their explicit release failed.
        if (orderState.OrderStatus is OrderStatus.Cancelled or OrderStatus.Refunded)
        {
            return true;
        }

        // Don't release if payment is already collected/settled (active online payment)
        if (orderState.PaymentStatus is PaymentStatus.Collected
            or PaymentStatus.Paid
            or PaymentStatus.Settled)
        {
            return false;
        }

        // Live CommerceHub COD orders should never have their reservations released by TTL.
        // COD payment lifecycle is independent of reservation cleanup until the order is terminal.
        if (orderState.PaymentStatus is PaymentStatus.CashOnDelivery)
        {
            return false;
        }

        if (orderState.LatestPaymentStatus is CommerceHubPaymentRecordStatus.Paid)
        {
            return false;
        }

        if (orderState.HasBlockingUnresolvedWebhook)
        {
            return false;
        }

        // Check provider authority gap FIRST before any early returns.
        // Recent pending CommerceHub payments with provider intent/transaction IDs
        // should be protected from expiration to allow provider reconciliation.
        var hasProviderCashAuthorityGap =
            orderState.LatestPaymentStatus is CommerceHubPaymentRecordStatus.Pending &&
            orderState.LatestPaymentCreatedAtUtc.HasValue &&
            orderState.LatestPaymentCreatedAtUtc.Value >= providerReconciliationCutoff &&
            (!string.IsNullOrWhiteSpace(orderState.LatestPaymentProviderIntentId) ||
             !string.IsNullOrWhiteSpace(orderState.LatestPaymentProviderTransactionId) ||
             IsProviderSessionAuthorityUncertain(orderState.LatestPaymentFailureReason));

        if (hasProviderCashAuthorityGap)
        {
            return false;
        }

        var hasRecentlyActivePayment =
            orderState.LatestPaymentStatus is CommerceHubPaymentRecordStatus.Pending &&
            orderState.LatestPaymentCreatedAtUtc.HasValue &&
            orderState.LatestPaymentCreatedAtUtc.Value >= activePaymentCutoff;

        if (hasRecentlyActivePayment)
        {
            return hasExpiredReservation;
        }

        return true;
    }

    private static bool IsProviderSessionAuthorityUncertain(string? failureReason) =>
        !string.IsNullOrWhiteSpace(failureReason) &&
        (failureReason.StartsWith("PROVIDER_SESSION_OUTCOME_UNKNOWN:", StringComparison.Ordinal) ||
         failureReason.StartsWith("PROVIDER_SESSION_REINIT_FENCE:", StringComparison.Ordinal));

    private bool IsPostgresProvider() =>
        _dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _dbContext.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private sealed class ReservationCleanupCandidate
    {
        public Guid TenantId { get; init; }
        public Guid OrderId { get; init; }
        public decimal RemainingReservedQuantity { get; init; }
        public DateTime OldestReservationUtc { get; init; }
        public bool HasExpiredReservation { get; init; }
    }

    private sealed record ReservationCleanupAuthority(
        OrderCleanupState? OrderState,
        CommerceHubOrderCleanupState? CommerceHubOrderState,
        decimal RemainingReservedQuantity,
        DateTime OldestReservationUtc,
        bool HasExpiredReservation);

    private sealed record ReservationAuthorityState(
        decimal RemainingReservedQuantity,
        DateTime OldestReservationUtc,
        bool HasExpiredReservation);

    private sealed record InventoryReservationAuthorityRow(
        decimal ReservedQuantity,
        decimal CommittedQuantity,
        decimal ReleasedQuantity,
        DateTime? ExpiresAtUtc,
        DateTime CreatedAt);

    private sealed class OrderCleanupState
    {
        public Guid OrderId { get; init; }
        public OrderStatus OrderStatus { get; init; }
        public PaymentStatus PaymentStatus { get; init; }
        public PaymentIntentStatus? LatestPaymentIntentStatus { get; init; }
        public DateTime? LatestPaymentIntentCreatedAtUtc { get; init; }
    }

    private sealed class CommerceHubOrderCleanupState
    {
        public Guid OrderId { get; init; }
        public OrderStatus OrderStatus { get; init; }
        public PaymentStatus PaymentStatus { get; init; }
        public CommerceHubPaymentRecordStatus? LatestPaymentStatus { get; init; }
        public DateTime? LatestPaymentCreatedAtUtc { get; init; }
        public string? LatestPaymentProviderIntentId { get; init; }
        public string? LatestPaymentProviderTransactionId { get; init; }
        public string? LatestPaymentFailureReason { get; init; }
        public bool HasBlockingUnresolvedWebhook { get; init; }
    }
}
