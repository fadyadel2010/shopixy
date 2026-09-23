using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Observability;

public sealed class InventoryReservationMonitoringOptions
{
    public const string SectionName = "InventoryReservationMonitoring";

    public int StaleReservationThresholdMinutes { get; set; } = 30;
    public int ActivePaymentGraceMinutes { get; set; } = 15;
    public int ProviderReconciliationGraceMinutes { get; set; } = 120;
    public int OrphanReservationGraceMinutes { get; set; } = 2;
    public int CleanupBatchSize { get; set; } = 100;
    public decimal HighReservedQuantityThreshold { get; set; } = 1000m;
}

public sealed class InventoryReservationMetricsSnapshot
{
    public decimal TotalReservedQuantity { get; init; }
    public int StaleReservationCount { get; init; }
    public decimal StaleReservedQuantity { get; init; }
    public double OldestStaleReservationAgeMinutes { get; init; }
    public double CommitReleaseRatio { get; init; }
}

public interface IInventoryReservationMetricsService
{
    void RecordCommit(decimal quantity, string source);
    void RecordRelease(decimal quantity, string source);
    Task<InventoryReservationMetricsSnapshot> RefreshSnapshotAsync(CancellationToken cancellationToken = default);
}

public sealed class InventoryReservationMetricsService : IInventoryReservationMetricsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly InventoryReservationTelemetry _telemetry;
    private readonly IOptions<InventoryReservationMonitoringOptions> _options;
    private readonly ILogger<InventoryReservationMetricsService> _logger;

    public InventoryReservationMetricsService(
        ApplicationDbContext dbContext,
        InventoryReservationTelemetry telemetry,
        IOptions<InventoryReservationMonitoringOptions> options,
        ILogger<InventoryReservationMetricsService> logger)
    {
        _dbContext = dbContext;
        _telemetry = telemetry;
        _options = options;
        _logger = logger;
    }

    public void RecordCommit(decimal quantity, string source)
    {
        _telemetry.RecordCommit(quantity, source);
    }

    public void RecordRelease(decimal quantity, string source)
    {
        _telemetry.RecordRelease(quantity, source);
    }

    public async Task<InventoryReservationMetricsSnapshot> RefreshSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var staleCutoff = now.AddMinutes(-_options.Value.StaleReservationThresholdMinutes);
        var activePaymentCutoff = now.AddMinutes(-_options.Value.ActivePaymentGraceMinutes);
        var providerReconciliationCutoff = now.AddMinutes(-_options.Value.ProviderReconciliationGraceMinutes);

        var totalReservedQuantity = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .Where(stock => !stock.IsDeleted)
            .SumAsync(stock => (decimal?)stock.ReservedQuantity, cancellationToken) ?? 0m;

        var staleReservations = await _dbContext.InventoryReservations
            .IgnoreQueryFilters()
            .Where(reservation =>
                !reservation.IsDeleted &&
                reservation.ReservedQuantity > reservation.CommittedQuantity + reservation.ReleasedQuantity &&
                (reservation.ExpiresAtUtc ?? reservation.CreatedAt) <= staleCutoff)
            .Select(reservation => new
            {
                RemainingReservedQuantity = reservation.ReservedQuantity - reservation.CommittedQuantity - reservation.ReleasedQuantity,
                AgeAnchorUtc = reservation.ExpiresAtUtc ?? reservation.CreatedAt,
                reservation.OrderId,
                HasExpiredReservation = reservation.ExpiresAtUtc.HasValue && reservation.ExpiresAtUtc.Value <= now
            })
            .ToListAsync(cancellationToken);

        var orderIds = staleReservations
            .Select(reservation => reservation.OrderId)
            .Distinct()
            .ToList();
        var orderStates = await _dbContext.Orders
            .IgnoreQueryFilters()
            .Where(order => orderIds.Contains(order.Id))
            .Select(order => new
            {
                order.Id,
                order.Status,
                order.PaymentStatus,
                LatestPaymentIntentStatus = _dbContext.PaymentIntents
                    .IgnoreQueryFilters()
                    .Where(intent => intent.OrderId == order.Id && !intent.IsDeleted)
                    .OrderByDescending(intent => intent.CreatedAt)
                    .Select(intent => (PaymentIntentStatus?)intent.Status)
                    .FirstOrDefault(),
                LatestPaymentIntentCreatedAtUtc = _dbContext.PaymentIntents
                    .IgnoreQueryFilters()
                    .Where(intent => intent.OrderId == order.Id && !intent.IsDeleted)
                    .OrderByDescending(intent => intent.CreatedAt)
                    .Select(intent => (DateTime?)intent.CreatedAt)
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(order => order.Id, cancellationToken);
        var commerceHubOrderStates = await _dbContext.CommerceHubMarketplaceOrders
            .IgnoreQueryFilters()
            .Where(order => orderIds.Contains(order.Id))
            .Select(order => new
            {
                order.Id,
                order.Status,
                order.PaymentStatus,
                LatestPaymentStatus = _dbContext.CommerceHubPayments
                    .IgnoreQueryFilters()
                    .Where(payment => payment.OrderId == order.Id && !payment.IsDeleted)
                    .OrderByDescending(payment => payment.CreatedAt)
                    .Select(payment => (CommerceHubPaymentRecordStatus?)payment.Status)
                    .FirstOrDefault(),
                LatestPaymentCreatedAtUtc = _dbContext.CommerceHubPayments
                    .IgnoreQueryFilters()
                    .Where(payment => payment.OrderId == order.Id && !payment.IsDeleted)
                    .OrderByDescending(payment => payment.CreatedAt)
                    .Select(payment => (DateTime?)payment.CreatedAt)
                    .FirstOrDefault(),
                LatestPaymentProviderIntentId = _dbContext.CommerceHubPayments
                    .IgnoreQueryFilters()
                    .Where(payment => payment.OrderId == order.Id && !payment.IsDeleted)
                    .OrderByDescending(payment => payment.CreatedAt)
                    .Select(payment => payment.ProviderIntentId)
                    .FirstOrDefault(),
                LatestPaymentProviderTransactionId = _dbContext.CommerceHubPayments
                    .IgnoreQueryFilters()
                    .Where(payment => payment.OrderId == order.Id && !payment.IsDeleted)
                    .OrderByDescending(payment => payment.CreatedAt)
                    .Select(payment => payment.ProviderTransactionId)
                    .FirstOrDefault(),
                LatestPaymentFailureReason = _dbContext.CommerceHubPayments
                    .IgnoreQueryFilters()
                    .Where(payment => payment.OrderId == order.Id && !payment.IsDeleted)
                    .OrderByDescending(payment => payment.CreatedAt)
                    .Select(payment => payment.FailureReason)
                    .FirstOrDefault(),
                HasBlockingUnresolvedWebhook = _dbContext.CommerceHubUnresolvedPaymentWebhooks
                    .IgnoreQueryFilters()
                    .Any(webhook =>
                        !webhook.IsDeleted &&
                        webhook.OrderId == order.Id &&
                        InventoryReservationReleaseAuthority.BlockingUnresolvedPaymentStatuses.Contains(webhook.Status))
            })
            .ToDictionaryAsync(order => order.Id, cancellationToken);

        var actionableStaleReservations = staleReservations
            .Where(reservation =>
            {
                orderStates.TryGetValue(reservation.OrderId, out var order);
                commerceHubOrderStates.TryGetValue(reservation.OrderId, out var commerceHubOrder);
                return InventoryReservationReleaseAuthority.CanReleaseOrderReservation(
                           order?.Status,
                           order?.PaymentStatus,
                           order?.LatestPaymentIntentStatus,
                           order?.LatestPaymentIntentCreatedAtUtc,
                           activePaymentCutoff,
                           reservation.HasExpiredReservation) &&
                       InventoryReservationReleaseAuthority.CanReleaseCommerceHubReservation(
                           commerceHubOrder?.Status,
                           commerceHubOrder?.PaymentStatus,
                           commerceHubOrder?.LatestPaymentStatus,
                           commerceHubOrder?.LatestPaymentCreatedAtUtc,
                           commerceHubOrder?.LatestPaymentProviderIntentId,
                           commerceHubOrder?.LatestPaymentProviderTransactionId,
                           commerceHubOrder?.LatestPaymentFailureReason,
                           commerceHubOrder?.HasBlockingUnresolvedWebhook ?? false,
                           activePaymentCutoff,
                           providerReconciliationCutoff,
                           reservation.HasExpiredReservation);
            })
            .ToList();

        var staleReservationCount = actionableStaleReservations.Count;
        var staleReservedQuantity = actionableStaleReservations.Sum(item => item.RemainingReservedQuantity);
        var oldestStaleReservationAgeMinutes = actionableStaleReservations.Count == 0
            ? 0d
            : (now - actionableStaleReservations.Min(item => item.AgeAnchorUtc)).TotalMinutes;
        var commitReleaseRatio = _telemetry.UpdateSnapshot(
            totalReservedQuantity,
            staleReservationCount,
            staleReservedQuantity);

        _logger.LogDebug(
            "Inventory reservation snapshot refreshed | TotalReservedQuantity: {TotalReservedQuantity} | StaleReservationCount: {StaleReservationCount} | StaleReservedQuantity: {StaleReservedQuantity}",
            totalReservedQuantity, staleReservationCount, staleReservedQuantity);

        return new InventoryReservationMetricsSnapshot
        {
            TotalReservedQuantity = totalReservedQuantity,
            StaleReservationCount = staleReservationCount,
            StaleReservedQuantity = staleReservedQuantity,
            OldestStaleReservationAgeMinutes = oldestStaleReservationAgeMinutes,
            CommitReleaseRatio = commitReleaseRatio
        };
    }
}
