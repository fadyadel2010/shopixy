using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

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

        var totalReservedQuantity = await _dbContext.ProductVariantStocks
            .Where(stock => !stock.IsDeleted)
            .SumAsync(stock => (decimal?)stock.ReservedQuantity, cancellationToken) ?? 0m;

        var staleReservations = await _dbContext.InventoryReservations
            .Where(reservation =>
                !reservation.IsDeleted &&
                reservation.ReservedQuantity > reservation.CommittedQuantity + reservation.ReleasedQuantity &&
                (reservation.ExpiresAtUtc ?? reservation.CreatedAt) <= staleCutoff)
            .Select(reservation => new
            {
                RemainingReservedQuantity = reservation.ReservedQuantity - reservation.CommittedQuantity - reservation.ReleasedQuantity,
                AgeAnchorUtc = reservation.ExpiresAtUtc ?? reservation.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var staleReservationCount = staleReservations.Count;
        var staleReservedQuantity = staleReservations.Sum(item => item.RemainingReservedQuantity);
        var oldestStaleReservationAgeMinutes = staleReservations.Count == 0
            ? 0d
            : (now - staleReservations.Min(item => item.AgeAnchorUtc)).TotalMinutes;
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
