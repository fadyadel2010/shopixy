using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Identity;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Configuration;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Payment;
using PointOfSale.Infrastructure.Redis.Services;
using MassTransitOutboxMessage = MassTransit.EntityFrameworkCoreIntegration.OutboxMessage;

namespace PointOfSale.Infrastructure.HealthChecks;

/// <summary>
/// PRODUCTION HARDENING (Phase 12) — OBSERVABILITY HEALTH CHECKS
/// 
/// This module provides production-grade health checks for:
/// - MongoDB projection sync (PostgreSQL vs MongoDB document counts)
/// - Redis connectivity and latency
/// - Event processing lag detection
/// 
/// These health checks are designed to catch SILENT FAILURES before they impact users.
/// </summary>

/// <summary>
/// Health check that detects projection drift between PostgreSQL and MongoDB.
/// If the difference exceeds the threshold, it indicates event processing issues.
/// </summary>
public class ProjectionDriftHealthCheck : IHealthCheck
{
    private static readonly TimeSpan DependencyTimeout = TimeSpan.FromSeconds(5);

    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoDatabase _mongoDatabase;
    private readonly ILogger<ProjectionDriftHealthCheck> _logger;
    private readonly string _productsCollectionName;
    
    // Maximum acceptable difference before degraded status
    private const int DegradedThreshold = 10;
    // Maximum acceptable difference before unhealthy status
    private const int UnhealthyThreshold = 50;

    public ProjectionDriftHealthCheck(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        ILogger<ProjectionDriftHealthCheck> logger,
        IOptions<MongoDbSettings>? mongoSettings = null)
    {
        _dbContext = dbContext;
        _mongoDatabase = mongoDatabase;
        _logger = logger;
        _productsCollectionName = (mongoSettings?.Value ?? new MongoDbSettings()).ProductsCollectionName;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DependencyTimeout);

            // Observe SQL-owned identities globally; never inherit a request tenant
            // or reconstruct full catalog payloads on the five-second health path.
            var snapshot = await ProductProjectionHealthSnapshotReader.ReadAsync(_dbContext, timeoutCts.Token);
            var currentTenantIds = snapshot.SqlTenantIds;
            var pgProductCount = snapshot.VisibleProducts.Count;
            
            // This Mongo database can be shared with historical/development tenants that
            // are intentionally absent from the current SQL database. Compare only the
            // current SQL tenant scope, while reporting foreign documents separately.
            var mongoProducts = _mongoDatabase.GetCollection<ProductDocument>(_productsCollectionName);
            var mongoTotalProductCount = await mongoProducts.CountDocumentsAsync(
                FilterDefinition<ProductDocument>.Empty, 
                cancellationToken: timeoutCts.Token);
            var currentTenantFilter = currentTenantIds.Count == 0
                ? Builders<ProductDocument>.Filter.Where(_ => false)
                : Builders<ProductDocument>.Filter.In(product => product.TenantId, currentTenantIds);
            var mongoCurrentTenantTotalCount = await mongoProducts.CountDocumentsAsync(
                currentTenantFilter,
                cancellationToken: timeoutCts.Token);
            var mongoVisibleFilter = Builders<ProductDocument>.Filter.And(
                currentTenantFilter,
                Builders<ProductDocument>.Filter.Eq(product => product.IsActive, true),
                Builders<ProductDocument>.Filter.Eq(product => product.IsOnlineVisible, true));
            using var cursor = await mongoProducts.FindAsync(mongoVisibleFilter,
                new FindOptions<ProductDocument, ProductDocument>
                {
                    Projection = Builders<ProductDocument>.Projection.Expression(p => new ProductDocument { TenantId = p.TenantId, ProductId = p.ProductId }),
                    BatchSize = 1000
                }, timeoutCts.Token);
            var visibleDocuments = await cursor.ToListAsync(timeoutCts.Token);
            var mongoProductCount = (long)visibleDocuments.Count;
            var foreignTenantDocumentCount = Math.Max(0, mongoTotalProductCount - mongoCurrentTenantTotalCount);

            var observedIdentities = visibleDocuments.Select(p => new ProductProjectionIdentity(p.TenantId, p.ProductId)).ToHashSet();
            var missingProducts = snapshot.VisibleProducts.Except(observedIdentities).Count();
            var unexpectedProducts = observedIdentities.Except(snapshot.VisibleProducts).Count();
            var duplicateIdentities = visibleDocuments.Count - observedIdentities.Count;
            // Equal global counts can hide missing/extra products in the same tenant
            // or cancel opposite drift across tenants. Compare the actual pair key.
            var drift = (long)missingProducts + unexpectedProducts + duplicateIdentities;
            var driftDirection = mongoProductCount > pgProductCount
                ? "MongoDBAhead"
                : pgProductCount > mongoProductCount
                    ? "PostgreSQLAhead"
                    : "Even";

            var data = new Dictionary<string, object>
            {
                ["PostgreSQLCount"] = pgProductCount,
                ["PostgreSQLSourceCount"] = snapshot.SourceProductCount,
                ["MongoDBCount"] = mongoProductCount,
                ["MongoDBCurrentTenantTotalCount"] = mongoCurrentTenantTotalCount,
                ["MongoDBTotalCount"] = mongoTotalProductCount,
                ["ForeignTenantDocumentCount"] = foreignTenantDocumentCount,
                ["Drift"] = drift,
                ["Direction"] = driftDirection,
                ["MissingProductCount"] = missingProducts,
                ["UnexpectedProductCount"] = unexpectedProducts,
                ["DuplicateIdentityCount"] = duplicateIdentities,
                ["SqlTenantCount"] = currentTenantIds.Count
            };

            if (duplicateIdentities > 0)
                return HealthCheckResult.Unhealthy("Duplicate tenant/product projection identities detected.", data: data);

            if (drift >= UnhealthyThreshold)
            {
                OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                    "mongo_product_projection",
                    OperationalConvergenceAuthority.StorefrontProduct,
                    OperationalConvergenceDecision.DriftDetected,
                    OperationalConvergenceReason.DriftDetected,
                    count: drift);

                _logger.LogError(
                    "Projection drift CRITICAL | PostgreSQL: {PgCount} | MongoDB: {MongoCount} | Drift: {Drift}",
                    pgProductCount, mongoProductCount, drift);
                
                return HealthCheckResult.Unhealthy(
                    $"Projection drift exceeds threshold: {drift} documents ({driftDirection})",
                    data: data);
            }

            if (drift >= DegradedThreshold)
            {
                OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                    "mongo_product_projection",
                    OperationalConvergenceAuthority.StorefrontProduct,
                    OperationalConvergenceDecision.DriftDetected,
                    OperationalConvergenceReason.DriftDetected,
                    count: drift);

                _logger.LogWarning(
                    "Projection drift DEGRADED | PostgreSQL: {PgCount} | MongoDB: {MongoCount} | Drift: {Drift}",
                    pgProductCount, mongoProductCount, drift);
                
                return HealthCheckResult.Degraded(
                    $"Minor projection drift detected: {drift} documents",
                    data: data);
            }

            OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                "mongo_product_projection",
                OperationalConvergenceAuthority.StorefrontProduct,
                OperationalConvergenceDecision.DriftWithinTolerance,
                OperationalConvergenceReason.DriftWithinTolerance);

            if (foreignTenantDocumentCount > 0)
            {
                return HealthCheckResult.Degraded(
                    $"Current SQL tenant projection identities are aligned within threshold; {foreignTenantDocumentCount} foreign-tenant MongoDB documents lack current SQL tenant authority and are preserved.",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"Projections synced within tolerance (drift: {drift})",
                data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                "mongo_product_projection",
                OperationalConvergenceAuthority.StorefrontProduct,
                OperationalConvergenceDecision.Retried,
                OperationalConvergenceReason.DriftHealthCheckFailed);

            _logger.LogError(ex, "Projection drift health check failed");
            return HealthCheckResult.Unhealthy(
                "Failed to check projection drift",
                exception: ex,
                data: new Dictionary<string, object> { ["FailureCategory"] = ex is OperationCanceledException or TimeoutException ? "dependency-timeout" : "dependency-failure" });
        }
    }
}

/// <summary>
/// Health check for durable projection outboxes that feed storefront and marketplace read models.
/// Repeated failures are readiness failures because downstream projections may be stale.
/// </summary>
public class ProjectionOutboxHealthCheck : IHealthCheck
{
    private static readonly TimeSpan WarningLag = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CriticalLag = TimeSpan.FromMinutes(15);
    private const int RepeatedFailureAttemptThreshold = 3;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ProjectionOutboxHealthCheck> _logger;

    public ProjectionOutboxHealthCheck(
        ApplicationDbContext dbContext,
        ILogger<ProjectionOutboxHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var warningCutoffUtc = nowUtc.Subtract(WarningLag);
            var criticalCutoffUtc = nowUtc.Subtract(CriticalLag);

            // Aggregate stock sync outbox metrics in a single query
            var stockMetrics = await _dbContext.Set<StockSyncOutboxMessage>()
                .IgnoreQueryFilters()
                .Where(message => message.PublishedAtUtc == null)
                .GroupBy(message => 1) // Group all records together
                .Select(group => new
                {
                    PendingCount = group.Count(),
                    RepeatedFailureCount = group.Count(m =>
                        m.LastError != null &&
                        m.AttemptCount >= RepeatedFailureAttemptThreshold),
                    CriticalLagCount = group.Count(m => m.OccurredAtUtc <= criticalCutoffUtc),
                    WarningLagCount = group.Count(m => m.OccurredAtUtc <= warningCutoffUtc)
                })
                .FirstOrDefaultAsync(cancellationToken);

            // Aggregate marketplace event outbox metrics in a single query
            var marketplaceMetrics = await _dbContext.Set<MarketplaceEventOutboxMessage>()
                .IgnoreQueryFilters()
                .Where(message => message.PublishedAtUtc == null)
                .GroupBy(message => 1) // Group all records together
                .Select(group => new
                {
                    PendingCount = group.Count(),
                    RepeatedFailureCount = group.Count(m =>
                        m.LastError != null &&
                        m.AttemptCount >= RepeatedFailureAttemptThreshold),
                    CriticalLagCount = group.Count(m => m.OccurredAtUtc <= criticalCutoffUtc),
                    WarningLagCount = group.Count(m => m.OccurredAtUtc <= warningCutoffUtc)
                })
                .FirstOrDefaultAsync(cancellationToken);

            var marketplaceProjectionMetrics = await _dbContext.Set<MarketplaceProjectionOutboxMessage>()
                .IgnoreQueryFilters()
                .Where(message => message.ProcessedAtUtc == null)
                .GroupBy(message => 1)
                .Select(group => new
                {
                    PendingCount = group.Count(),
                    RepeatedFailureCount = group.Count(m =>
                        m.LastError != null &&
                        m.AttemptCount >= RepeatedFailureAttemptThreshold),
                    CriticalLagCount = group.Count(m => m.OccurredAtUtc <= criticalCutoffUtc),
                    WarningLagCount = group.Count(m => m.OccurredAtUtc <= warningCutoffUtc)
                })
                .FirstOrDefaultAsync(cancellationToken);

            // Extract metrics with null-safe defaults
            var stockPendingCount = stockMetrics?.PendingCount ?? 0;
            var stockRepeatedFailureCount = stockMetrics?.RepeatedFailureCount ?? 0;
            var stockCriticalLagCount = stockMetrics?.CriticalLagCount ?? 0;
            var stockWarningLagCount = stockMetrics?.WarningLagCount ?? 0;

            var marketplacePendingCount = marketplaceMetrics?.PendingCount ?? 0;
            var marketplaceRepeatedFailureCount = marketplaceMetrics?.RepeatedFailureCount ?? 0;
            var marketplaceCriticalLagCount = marketplaceMetrics?.CriticalLagCount ?? 0;
            var marketplaceWarningLagCount = marketplaceMetrics?.WarningLagCount ?? 0;

            var marketplaceProjectionPendingCount = marketplaceProjectionMetrics?.PendingCount ?? 0;
            var marketplaceProjectionRepeatedFailureCount = marketplaceProjectionMetrics?.RepeatedFailureCount ?? 0;
            var marketplaceProjectionCriticalLagCount = marketplaceProjectionMetrics?.CriticalLagCount ?? 0;
            var marketplaceProjectionWarningLagCount = marketplaceProjectionMetrics?.WarningLagCount ?? 0;

            var repeatedFailureCount = stockRepeatedFailureCount + marketplaceRepeatedFailureCount + marketplaceProjectionRepeatedFailureCount;
            var criticalLagCount = stockCriticalLagCount + marketplaceCriticalLagCount + marketplaceProjectionCriticalLagCount;
            var warningLagCount = stockWarningLagCount + marketplaceWarningLagCount + marketplaceProjectionWarningLagCount;
            var totalPendingCount = stockPendingCount + marketplacePendingCount + marketplaceProjectionPendingCount;

            var data = new Dictionary<string, object>
            {
                ["StockPendingCount"] = stockPendingCount,
                ["StockRepeatedFailureCount"] = stockRepeatedFailureCount,
                ["StockCriticalLagCount"] = stockCriticalLagCount,
                ["MarketplacePendingCount"] = marketplacePendingCount,
                ["MarketplaceRepeatedFailureCount"] = marketplaceRepeatedFailureCount,
                ["MarketplaceCriticalLagCount"] = marketplaceCriticalLagCount,
                ["MarketplaceProjectionPendingCount"] = marketplaceProjectionPendingCount,
                ["MarketplaceProjectionRepeatedFailureCount"] = marketplaceProjectionRepeatedFailureCount,
                ["MarketplaceProjectionCriticalLagCount"] = marketplaceProjectionCriticalLagCount,
                ["TotalPendingCount"] = totalPendingCount,
                ["RepeatedFailureCount"] = repeatedFailureCount,
                ["CriticalLagCount"] = criticalLagCount,
                ["WarningLagCount"] = warningLagCount
            };

            if (repeatedFailureCount > 0 || criticalLagCount > 0)
            {
                OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                    "projection_outbox",
                    OperationalConvergenceAuthority.ProjectionEventFence,
                    OperationalConvergenceDecision.DriftDetected,
                    OperationalConvergenceReason.ProjectionLagDetected,
                    count: Math.Max(1, repeatedFailureCount + criticalLagCount));

                _logger.LogError(
                    "Projection outbox readiness failed | Pending: {Pending} | RepeatedFailures: {RepeatedFailures} | CriticalLag: {CriticalLag}",
                    totalPendingCount,
                    repeatedFailureCount,
                    criticalLagCount);

                return HealthCheckResult.Unhealthy(
                    "Projection outbox delivery is blocked; downstream read models may be stale.",
                    data: data);
            }

            if (warningLagCount > 0)
            {
                OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                    "projection_outbox",
                    OperationalConvergenceAuthority.ProjectionEventFence,
                    OperationalConvergenceDecision.DriftDetected,
                    OperationalConvergenceReason.ProjectionLagDetected,
                    count: warningLagCount);

                _logger.LogWarning(
                    "Projection outbox delivery is lagging | Pending: {Pending} | WarningLag: {WarningLag}",
                    totalPendingCount,
                    warningLagCount);

                return HealthCheckResult.Degraded(
                    "Projection outbox delivery is lagging.",
                    data: data);
            }

            OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                "projection_outbox",
                OperationalConvergenceAuthority.ProjectionEventFence,
                OperationalConvergenceDecision.DriftWithinTolerance,
                OperationalConvergenceReason.ProjectionLagWithinTolerance);

            return HealthCheckResult.Healthy(
                "Projection outboxes are delivering within readiness thresholds.",
                data);
        }
        catch (Exception ex)
        {
            OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                "projection_outbox",
                OperationalConvergenceAuthority.ProjectionEventFence,
                OperationalConvergenceDecision.Retried,
                OperationalConvergenceReason.OutboxReadinessCheckFailed);

            _logger.LogError(ex, "Projection outbox health check failed");
            return HealthCheckResult.Unhealthy(
                "Failed to check projection outbox health",
                exception: ex);
        }
    }
}

/// <summary>
/// Readiness check for the generic transactional integration outbox.
/// A zero-attempt stale row means the publisher is not running, while repeated
/// failures or operator reconciliation indicate blocked authoritative fanout.
/// </summary>
public sealed class IntegrationOutboxReadinessHealthCheck : IHealthCheck
{
    private static readonly TimeSpan WarningLag = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CriticalLag = TimeSpan.FromMinutes(15);
    private const int RepeatedFailureThreshold = 3;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<IntegrationOutboxReadinessHealthCheck> _logger;

    public IntegrationOutboxReadinessHealthCheck(
        ApplicationDbContext dbContext,
        ILogger<IntegrationOutboxReadinessHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var warningCutoffUtc = nowUtc.Subtract(WarningLag);
            var criticalCutoffUtc = nowUtc.Subtract(CriticalLag);

            var metrics = await _dbContext.OutboxMessages
                .AsNoTracking()
                .Where(message => message.ProcessedAtUtc == null)
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    PendingCount = group.Count(),
                    ZeroAttemptCriticalLagCount = group.Count(message =>
                        message.RetryCount == 0 && message.CreatedAtUtc <= criticalCutoffUtc),
                    RepeatedFailureCount = group.Count(message =>
                        message.RetryCount >= RepeatedFailureThreshold),
                    OperatorReconciliationCount = group.Count(message =>
                        message.RequiresOperatorReconciliation),
                    CriticalLagCount = group.Count(message => message.CreatedAtUtc <= criticalCutoffUtc),
                    WarningLagCount = group.Count(message => message.CreatedAtUtc <= warningCutoffUtc)
                })
                .FirstOrDefaultAsync(cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["PendingCount"] = metrics?.PendingCount ?? 0,
                ["ZeroAttemptCriticalLagCount"] = metrics?.ZeroAttemptCriticalLagCount ?? 0,
                ["RepeatedFailureCount"] = metrics?.RepeatedFailureCount ?? 0,
                ["OperatorReconciliationCount"] = metrics?.OperatorReconciliationCount ?? 0,
                ["CriticalLagCount"] = metrics?.CriticalLagCount ?? 0,
                ["WarningLagCount"] = metrics?.WarningLagCount ?? 0
            };

            if (metrics is
                {
                    CriticalLagCount: > 0
                } or
                {
                    RepeatedFailureCount: > 0
                } or
                {
                    OperatorReconciliationCount: > 0
                })
            {
                _logger.LogError(
                    "Integration outbox readiness failed | Pending: {Pending} | ZeroAttemptCriticalLag: {ZeroAttemptCriticalLag} | RepeatedFailures: {RepeatedFailures} | OperatorReconciliation: {OperatorReconciliation} | CriticalLag: {CriticalLag}",
                    metrics?.PendingCount ?? 0,
                    metrics?.ZeroAttemptCriticalLagCount ?? 0,
                    metrics?.RepeatedFailureCount ?? 0,
                    metrics?.OperatorReconciliationCount ?? 0,
                    metrics?.CriticalLagCount ?? 0);

                return HealthCheckResult.Unhealthy(
                    "Integration outbox delivery is blocked; authoritative event fanout may be stale.",
                    data: data);
            }

            if (metrics is { WarningLagCount: > 0 })
            {
                return HealthCheckResult.Degraded(
                    "Integration outbox delivery is lagging.",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                "Integration outbox is delivering within readiness thresholds.",
                data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Integration outbox readiness health check failed");
            return HealthCheckResult.Unhealthy(
                "Failed to check integration outbox readiness.",
                exception: ex);
        }
    }
}

/// <summary>
/// Readiness check for MassTransit EF bus outbox rows. These rows carry normal
/// authoritative integration events published through IEventPublisher, so stale
/// rows mean downstream systems can lag committed SQL authority.
/// </summary>
public class MassTransitBusOutboxReadinessHealthCheck : IHealthCheck
{
    private static readonly TimeSpan WarningLag = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CriticalLag = TimeSpan.FromMinutes(15);

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<MassTransitBusOutboxReadinessHealthCheck> _logger;

    public MassTransitBusOutboxReadinessHealthCheck(
        ApplicationDbContext dbContext,
        ILogger<MassTransitBusOutboxReadinessHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var warningCutoffUtc = nowUtc.Subtract(WarningLag);
            var criticalCutoffUtc = nowUtc.Subtract(CriticalLag);

            // PRODUCTION HARDENING: Use per-message sequence comparison to determine pending status.
            // A message is pending if its SequenceNumber exceeds OutboxState.LastSequenceNumber,
            // not just when Delivered is null (which is an outbox-level flag, not message-level).
            var criticalFilter = BuildCriticalMassTransitOutboxMessageTypeFilter();
            
            var metrics = await (
                from message in _dbContext.Set<MassTransitOutboxMessage>().AsNoTracking()
                join state in _dbContext.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxState>().AsNoTracking()
                    on message.OutboxId equals state.OutboxId into stateGroup
                from state in stateGroup.DefaultIfEmpty()
                where state == null || message.SequenceNumber > state.LastSequenceNumber
                select message)
                .Where(criticalFilter)
                .GroupBy(message => 1)
                .Select(group => new
                {
                    PendingCount = group.Count(),
                    CriticalLagCount = group.Count(message => message.SentTime <= criticalCutoffUtc),
                    WarningLagCount = group.Count(message => message.SentTime <= warningCutoffUtc)
                })
                .FirstOrDefaultAsync(cancellationToken);

            var pendingCount = metrics?.PendingCount ?? 0;
            var criticalLagCount = metrics?.CriticalLagCount ?? 0;
            var warningLagCount = metrics?.WarningLagCount ?? 0;

            var data = new Dictionary<string, object>
            {
                ["PendingCount"] = pendingCount,
                ["CriticalLagCount"] = criticalLagCount,
                ["WarningLagCount"] = warningLagCount
            };

            if (criticalLagCount > 0)
            {
                OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                    "mass_transit_bus_outbox",
                    OperationalConvergenceAuthority.ProjectionEventFence,
                    OperationalConvergenceDecision.DriftDetected,
                    OperationalConvergenceReason.ProjectionLagDetected,
                    count: criticalLagCount);

                _logger.LogError(
                    "MassTransit bus outbox readiness failed | Pending: {Pending} | CriticalLag: {CriticalLag}",
                    pendingCount,
                    criticalLagCount);

                return HealthCheckResult.Unhealthy(
                    "MassTransit bus outbox delivery is blocked; authoritative integration events may be stale.",
                    data: data);
            }

            if (warningLagCount > 0)
            {
                OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                    "mass_transit_bus_outbox",
                    OperationalConvergenceAuthority.ProjectionEventFence,
                    OperationalConvergenceDecision.DriftDetected,
                    OperationalConvergenceReason.ProjectionLagDetected,
                    count: warningLagCount);

                _logger.LogWarning(
                    "MassTransit bus outbox delivery is lagging | Pending: {Pending} | WarningLag: {WarningLag}",
                    pendingCount,
                    warningLagCount);

                return HealthCheckResult.Degraded(
                    "MassTransit bus outbox delivery is lagging.",
                    data: data);
            }

            OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                "mass_transit_bus_outbox",
                OperationalConvergenceAuthority.ProjectionEventFence,
                OperationalConvergenceDecision.DriftWithinTolerance,
                OperationalConvergenceReason.ProjectionLagWithinTolerance);

            return HealthCheckResult.Healthy(
                "MassTransit bus outbox is delivering within readiness thresholds.",
                data);
        }
        catch (Exception ex)
        {
            OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                "mass_transit_bus_outbox",
                OperationalConvergenceAuthority.ProjectionEventFence,
                OperationalConvergenceDecision.Retried,
                OperationalConvergenceReason.OutboxReadinessCheckFailed);

            _logger.LogError(ex, "MassTransit bus outbox readiness health check failed");
            return HealthCheckResult.Unhealthy(
                "Failed to check MassTransit bus outbox readiness.",
                exception: ex);
        }
    }

    private static Expression<Func<MassTransitOutboxMessage, bool>> BuildCriticalMassTransitOutboxMessageTypeFilter()
    {
        var parameter = Expression.Parameter(typeof(MassTransitOutboxMessage), "message");
        var messageType = Expression.Property(parameter, nameof(MassTransitOutboxMessage.MessageType));
        
        // PRODUCTION HARDENING: Use EF.Functions.ILike for case-insensitive pattern matching
        // that translates correctly to PostgreSQL ILIKE operator
        var efFunctionsProperty = Expression.Property(null, typeof(EF), nameof(EF.Functions));
        var iLikeMethod = typeof(NpgsqlDbFunctionsExtensions).GetMethod(
            nameof(NpgsqlDbFunctionsExtensions.ILike),
            [typeof(DbFunctions), typeof(string), typeof(string)])!;

        Expression? includedExpression = null;
        foreach (var fragment in IntegrationEventAuthority.CriticalEventNameFragments)
        {
            var patternConstant = Expression.Constant($"%{fragment}%");
            var iLikeCall = Expression.Call(iLikeMethod, efFunctionsProperty, messageType, patternConstant);
            includedExpression = includedExpression == null
                ? iLikeCall
                : Expression.OrElse(includedExpression, iLikeCall);
        }

        foreach (var eventTypeName in IntegrationEventAuthority.NonAuthoritativeEventTypeNames)
        {
            var excludedPatternConstant = Expression.Constant($"%{eventTypeName}%");
            var excludedILikeCall = Expression.Call(iLikeMethod, efFunctionsProperty, messageType, excludedPatternConstant);
            includedExpression = Expression.AndAlso(includedExpression!, Expression.Not(excludedILikeCall));
        }

        return Expression.Lambda<Func<MassTransitOutboxMessage, bool>>(includedExpression!, parameter);
    }
}

/// <summary>
/// Readiness gate for financial truth events that intentionally remain retryable
/// instead of moving to a terminal dead-letter state.
/// </summary>
public class AccountingOutboxReadinessHealthCheck : IHealthCheck
{
    private static readonly TimeSpan WarningLag = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CriticalLag = TimeSpan.FromMinutes(15);
    private const int RepeatedFailureAttemptThreshold = 3;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AccountingOutboxReadinessHealthCheck> _logger;

    public AccountingOutboxReadinessHealthCheck(
        ApplicationDbContext dbContext,
        ILogger<AccountingOutboxReadinessHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var warningCutoffUtc = nowUtc.Subtract(WarningLag);
            var criticalCutoffUtc = nowUtc.Subtract(CriticalLag);

            var criticalPendingCount = await _dbContext.Set<AccountingOutboxMessage>()
                .IgnoreQueryFilters()
                .CountAsync(message =>
                    message.ProcessedAtUtc == null &&
                    AccountingOutboxProcessor.CriticalFinancialTruthEventTypes.Contains(message.EventType),
                    cancellationToken);

            var repeatedFailureCount = await _dbContext.Set<AccountingOutboxMessage>()
                .IgnoreQueryFilters()
                .CountAsync(message =>
                    message.ProcessedAtUtc == null &&
                    AccountingOutboxProcessor.CriticalFinancialTruthEventTypes.Contains(message.EventType) &&
                    message.LastError != null &&
                    message.AttemptCount >= RepeatedFailureAttemptThreshold,
                    cancellationToken);

            var criticalLagCount = await _dbContext.Set<AccountingOutboxMessage>()
                .IgnoreQueryFilters()
                .CountAsync(message =>
                    message.ProcessedAtUtc == null &&
                    AccountingOutboxProcessor.CriticalFinancialTruthEventTypes.Contains(message.EventType) &&
                    message.OccurredAtUtc <= criticalCutoffUtc,
                    cancellationToken);

            var warningLagCount = await _dbContext.Set<AccountingOutboxMessage>()
                .IgnoreQueryFilters()
                .CountAsync(message =>
                    message.ProcessedAtUtc == null &&
                    AccountingOutboxProcessor.CriticalFinancialTruthEventTypes.Contains(message.EventType) &&
                    message.OccurredAtUtc <= warningCutoffUtc,
                    cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["CriticalPendingCount"] = criticalPendingCount,
                ["RepeatedFailureCount"] = repeatedFailureCount,
                ["CriticalLagCount"] = criticalLagCount,
                ["WarningLagCount"] = warningLagCount
            };

            if (repeatedFailureCount > 0 || criticalLagCount > 0)
            {
                _logger.LogError(
                    "Accounting outbox readiness failed | CriticalPending: {CriticalPending} | RepeatedFailures: {RepeatedFailures} | CriticalLag: {CriticalLag}",
                    criticalPendingCount,
                    repeatedFailureCount,
                    criticalLagCount);

                return HealthCheckResult.Unhealthy(
                    "Critical accounting outbox delivery is blocked; financial truth may lag committed business state.",
                    data: data);
            }

            if (warningLagCount > 0)
            {
                _logger.LogWarning(
                    "Accounting outbox delivery is lagging | CriticalPending: {CriticalPending} | WarningLag: {WarningLag}",
                    criticalPendingCount,
                    warningLagCount);

                return HealthCheckResult.Degraded(
                    "Critical accounting outbox delivery is lagging.",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                "Critical accounting outbox delivery is within readiness thresholds.",
                data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Accounting outbox readiness health check failed");
            return HealthCheckResult.Unhealthy(
                "Failed to check accounting outbox readiness.",
                exception: ex);
        }
    }
}

/// <summary>
/// Readiness gate for replay-safe payment cancellation recovery.
/// This worker is allowed to retry and overlap safely, but operators still need
/// visibility when cancellation backlog or terminal failures threaten convergence.
/// </summary>
public class PaymentCancellationOutboxReadinessHealthCheck : IHealthCheck
{
    private static readonly TimeSpan WarningLag = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CriticalLag = TimeSpan.FromMinutes(15);
    private const int RepeatedFailureAttemptThreshold = 3;

    private readonly ApplicationDbContext _dbContext;
    private readonly PaymentCancellationOutboxOptions _options;
    private readonly ILogger<PaymentCancellationOutboxReadinessHealthCheck> _logger;

    public PaymentCancellationOutboxReadinessHealthCheck(
        ApplicationDbContext dbContext,
        IOptions<PaymentCancellationOutboxOptions> options,
        ILogger<PaymentCancellationOutboxReadinessHealthCheck> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var warningCutoffUtc = nowUtc.Subtract(WarningLag);
            var criticalCutoffUtc = nowUtc.Subtract(CriticalLag);

            var pendingCount = await _dbContext.PaymentCancellationOutboxMessages
                .IgnoreQueryFilters()
                .CountAsync(message =>
                    message.ProcessedAtUtc == null &&
                    !message.IsFailed,
                    cancellationToken);

            var terminalFailureCount = await _dbContext.PaymentCancellationOutboxMessages
                .IgnoreQueryFilters()
                .CountAsync(message =>
                    message.ProcessedAtUtc == null &&
                    message.IsFailed,
                    cancellationToken);

            var repeatedFailureCount = await _dbContext.PaymentCancellationOutboxMessages
                .IgnoreQueryFilters()
                .CountAsync(message =>
                    message.ProcessedAtUtc == null &&
                    !message.IsFailed &&
                    message.LastError != null &&
                    message.AttemptCount >= RepeatedFailureAttemptThreshold,
                    cancellationToken);

            var criticalLagCount = await _dbContext.PaymentCancellationOutboxMessages
                .IgnoreQueryFilters()
                .CountAsync(message =>
                    message.ProcessedAtUtc == null &&
                    !message.IsFailed &&
                    message.CreatedAt <= criticalCutoffUtc,
                    cancellationToken);

            var warningLagCount = await _dbContext.PaymentCancellationOutboxMessages
                .IgnoreQueryFilters()
                .CountAsync(message =>
                    message.ProcessedAtUtc == null &&
                    !message.IsFailed &&
                    message.CreatedAt <= warningCutoffUtc,
                    cancellationToken);

            var activeClaimCount = await _dbContext.PaymentCancellationOutboxMessages
                .IgnoreQueryFilters()
                .CountAsync(message =>
                    message.ProcessedAtUtc == null &&
                    !message.IsFailed &&
                    message.ClaimedUntilUtc != null &&
                    message.ClaimedUntilUtc > nowUtc,
                    cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["PendingCount"] = pendingCount,
                ["TerminalFailureCount"] = terminalFailureCount,
                ["RepeatedFailureCount"] = repeatedFailureCount,
                ["CriticalLagCount"] = criticalLagCount,
                ["WarningLagCount"] = warningLagCount,
                ["ActiveClaimCount"] = activeClaimCount,
                ["ProcessingLeaseSeconds"] = _options.ProcessingLeaseSeconds
            };

            if (terminalFailureCount > 0 || repeatedFailureCount > 0 || criticalLagCount > 0)
            {
                _logger.LogError(
                    "Payment cancellation outbox readiness failed | Pending: {Pending} | TerminalFailures: {TerminalFailures} | RepeatedFailures: {RepeatedFailures} | CriticalLag: {CriticalLag} | ActiveClaims: {ActiveClaims}",
                    pendingCount,
                    terminalFailureCount,
                    repeatedFailureCount,
                    criticalLagCount,
                    activeClaimCount);

                return HealthCheckResult.Unhealthy(
                    "Payment cancellation recovery backlog is blocked or aged beyond readiness thresholds.",
                    data: data);
            }

            if (warningLagCount > 0)
            {
                _logger.LogWarning(
                    "Payment cancellation outbox readiness is lagging | Pending: {Pending} | WarningLag: {WarningLag} | ActiveClaims: {ActiveClaims}",
                    pendingCount,
                    warningLagCount,
                    activeClaimCount);

                return HealthCheckResult.Degraded(
                    "Payment cancellation recovery backlog is lagging.",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                "Payment cancellation recovery backlog is within readiness thresholds.",
                data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment cancellation outbox readiness health check failed");
            return HealthCheckResult.Unhealthy(
                "Failed to check payment cancellation outbox readiness.",
                exception: ex);
        }
    }
}

/// <summary>
/// Readiness gate for cross-system identity authority.
/// Detects direct SQL/bulk writes that bypass DbContext canonical identity guards.
/// </summary>
public class CanonicalIdentityAuthorityHealthCheck : IHealthCheck
{
    private readonly CanonicalIdentityReconciliationService _reconciliationService;
    private readonly ILogger<CanonicalIdentityAuthorityHealthCheck> _logger;

    public CanonicalIdentityAuthorityHealthCheck(
        CanonicalIdentityReconciliationService reconciliationService,
        ILogger<CanonicalIdentityAuthorityHealthCheck> logger)
    {
        _reconciliationService = reconciliationService;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _reconciliationService.InspectAsync(cancellationToken);
            var data = new Dictionary<string, object>
            {
                ["MissingCanonicalIdentityLinks"] = result.RepairedCount,
                ["CanonicalIdentityConflicts"] = result.ConflictCount
            };

            if (result.ConflictCount > 0)
            {
                _logger.LogCritical(
                    "Canonical identity authority check failed with {ConflictCount} cross-system conflicts.",
                    result.ConflictCount);

                return HealthCheckResult.Unhealthy(
                    "Canonical identity authority is split across systems; manual resolution is required.",
                    data: data);
            }

            if (result.RepairedCount > 0)
            {
                _logger.LogWarning(
                    "Canonical identity authority check found {MissingCount} missing canonical links.",
                    result.RepairedCount);

                return HealthCheckResult.Degraded(
                    "Canonical identity links are missing; the reconciliation worker should repair them.",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                "Canonical identity authority links are complete and conflict-free.",
                data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Canonical identity authority health check failed");
            return HealthCheckResult.Unhealthy(
                "Failed to check canonical identity authority.",
                exception: ex);
        }
    }
}

/// <summary>
/// Readiness gate for payout state that depends on external bank/provider truth.
/// </summary>
public class PayoutExternalTruthReadinessHealthCheck : IHealthCheck
{
    private static readonly TimeSpan WarningReconciliationLag = TimeSpan.FromMinutes(75);
    private static readonly TimeSpan CriticalReconciliationLag = TimeSpan.FromHours(2);
    private static readonly TimeSpan ProcessingPayoutCriticalLag = TimeSpan.FromHours(2);

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<PayoutExternalTruthReadinessHealthCheck> _logger;

    public PayoutExternalTruthReadinessHealthCheck(
        ApplicationDbContext dbContext,
        ILogger<PayoutExternalTruthReadinessHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var warningCutoffUtc = nowUtc.Subtract(WarningReconciliationLag);
            var criticalCutoffUtc = nowUtc.Subtract(CriticalReconciliationLag);
            var processingPayoutCutoffUtc = nowUtc.Subtract(ProcessingPayoutCriticalLag);

            var completedPayoutsWithoutExternalEvidence = await _dbContext.CommerceHubPayouts
                .IgnoreQueryFilters()
                .CountAsync(payout =>
                    !payout.IsDeleted &&
                    payout.Status == CommerceHubPayoutStatus.Completed &&
                    (payout.CompletionEvidenceFingerprint == null ||
                     payout.CompletionEvidenceFingerprint == string.Empty ||
                     (payout.ExternalReferenceId == null &&
                      payout.BankTransactionId == null)),
                    cancellationToken);

            var latestReport = await _dbContext.Set<CommerceHubFinancialReconciliationReport>()
                .AsNoTracking()
                .OrderByDescending(report => report.TimestampUtc)
                .Select(report => new { report.TimestampUtc, report.Status, report.MismatchCount })
                .FirstOrDefaultAsync(cancellationToken);

            var completedPayoutCount = await _dbContext.CommerceHubPayouts
                .IgnoreQueryFilters()
                .CountAsync(payout =>
                    !payout.IsDeleted &&
                    payout.Status == CommerceHubPayoutStatus.Completed,
                    cancellationToken);

            var staleProcessingPayouts = await _dbContext.CommerceHubPayouts
                .IgnoreQueryFilters()
                .CountAsync(payout =>
                    !payout.IsDeleted &&
                    payout.Status == CommerceHubPayoutStatus.Processing &&
                    (payout.ProcessedAtUtc ?? payout.CreatedAt) < processingPayoutCutoffUtc,
                    cancellationToken);

            var reconciliationRequiredPayouts = await _dbContext.CommerceHubPayouts
                .IgnoreQueryFilters()
                .CountAsync(payout =>
                    !payout.IsDeleted &&
                    payout.Status == CommerceHubPayoutStatus.ReconciliationRequired,
                    cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["CompletedPayoutsWithoutExternalEvidence"] = completedPayoutsWithoutExternalEvidence,
                ["CompletedPayoutCount"] = completedPayoutCount,
                ["StaleProcessingPayouts"] = staleProcessingPayouts,
                ["ReconciliationRequiredPayouts"] = reconciliationRequiredPayouts,
                ["LatestReconciliationTimestampUtc"] = latestReport?.TimestampUtc.ToString("O") ?? "none",
                ["LatestReconciliationStatus"] = latestReport?.Status ?? "none",
                ["LatestReconciliationMismatchCount"] = latestReport?.MismatchCount ?? 0
            };

            var reconciliationMissing = completedPayoutCount > 0 && latestReport == null;
            var reconciliationCriticalLag =
                completedPayoutCount > 0 &&
                latestReport != null &&
                latestReport.TimestampUtc <= criticalCutoffUtc;
            var reconciliationWarningLag =
                completedPayoutCount > 0 &&
                latestReport != null &&
                latestReport.TimestampUtc <= warningCutoffUtc;

            if (completedPayoutsWithoutExternalEvidence > 0 ||
                staleProcessingPayouts > 0 ||
                reconciliationMissing ||
                reconciliationCriticalLag ||
                string.Equals(latestReport?.Status, "Critical", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "Payout external truth readiness failed | MissingEvidence: {MissingEvidence} | StaleProcessingPayouts: {StaleProcessingPayouts} | CompletedPayouts: {CompletedPayouts} | LatestStatus: {LatestStatus}",
                    completedPayoutsWithoutExternalEvidence,
                    staleProcessingPayouts,
                    completedPayoutCount,
                    latestReport?.Status ?? "none");

                return HealthCheckResult.Unhealthy(
                    "Payout state is not safely reconciled against external bank/provider truth.",
                    data: data);
            }

            if (reconciliationWarningLag ||
                string.Equals(latestReport?.Status, "Warning", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Payout external truth readiness is lagging | CompletedPayouts: {CompletedPayouts} | LatestStatus: {LatestStatus}",
                    completedPayoutCount,
                    latestReport?.Status ?? "none");

                return HealthCheckResult.Degraded(
                    "Payout external truth reconciliation is lagging or has warning findings.",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                "Completed payouts have terminal evidence and recent reconciliation coverage.",
                data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payout external truth readiness health check failed");
            return HealthCheckResult.Unhealthy(
                "Failed to check payout external truth readiness.",
                exception: ex);
        }
    }
}

/// <summary>
/// Readiness gate for external side effects that are intentionally reconciled after the
/// authoritative SQL transition commits.
/// </summary>
public class CommerceHubExternalOutcomeRecoveryHealthCheck : IHealthCheck
{
    private static readonly TimeSpan RefundCriticalLag = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ShipmentWarningLag = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ShipmentCriticalLag = TimeSpan.FromMinutes(15);
    private static readonly string[] BlockingUnresolvedPaymentStatuses =
    [
        "PendingResolution",
        "PendingTenantResolution",
        "RoutedToSuspense",
        "SuspenseMatchedPaymentPendingClearance"
    ];
    private static readonly string[] BlockingUnresolvedRefundStatuses =
    [
        "ProviderRefundPendingReconciliation"
    ];

    private const string ExternalRegistrationStateFailed = "Failed";
    private const string ExternalRegistrationStateReconciliationRequired = "ReconciliationRequired";

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CommerceHubExternalOutcomeRecoveryHealthCheck> _logger;

    public CommerceHubExternalOutcomeRecoveryHealthCheck(
        ApplicationDbContext dbContext,
        ILogger<CommerceHubExternalOutcomeRecoveryHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var refundCriticalCutoffUtc = nowUtc.Subtract(RefundCriticalLag);
            var shipmentWarningCutoffUtc = nowUtc.Subtract(ShipmentWarningLag);
            var shipmentCriticalCutoffUtc = nowUtc.Subtract(ShipmentCriticalLag);

            var unresolvedRefundCount = await _dbContext.CommerceHubRefunds
                .IgnoreQueryFilters()
                .CountAsync(refund =>
                    !refund.IsDeleted &&
                    (refund.Status == CommerceHubRefundStatus.ReconciliationRequired ||
                     (refund.Status == CommerceHubRefundStatus.Processing &&
                      (refund.GatewayStatus == "execution_unknown" ||
                       refund.GatewayStatus == null ||
                       ((refund.ProcessingStartedAtUtc ?? refund.CreatedAt) < refundCriticalCutoffUtc &&
                        (refund.GatewayStatus == "processing" ||
                         refund.GatewayStatus == "pending" ||
                         refund.GatewayStatus == "pending_confirmation"))))),
                    cancellationToken);

            var unresolvedCapturedWebhookCount = await _dbContext.CommerceHubUnresolvedPaymentWebhooks
                .IgnoreQueryFilters()
                .CountAsync(webhook =>
                    !webhook.IsDeleted &&
                    webhook.Amount > 0m &&
                    BlockingUnresolvedPaymentStatuses.Contains(webhook.Status),
                    cancellationToken);

            var unresolvedRefundWebhookCount = await _dbContext.CommerceHubUnresolvedPaymentWebhooks
                .IgnoreQueryFilters()
                .CountAsync(webhook =>
                    !webhook.IsDeleted &&
                    webhook.Amount > 0m &&
                    BlockingUnresolvedRefundStatuses.Contains(webhook.Status),
                    cancellationToken);

            var reconciliationRequiredShipmentCount = await _dbContext.Shipments
                .IgnoreQueryFilters()
                .CountAsync(shipment =>
                    !shipment.IsDeleted &&
                    shipment.CarrierCode != CarrierCodes.Manual &&
                    shipment.ExternalRegistrationCompletedAtUtc == null &&
                    shipment.ExternalRegistrationState == ExternalRegistrationStateReconciliationRequired,
                    cancellationToken);

            var criticalPendingCarrierRegistrations = await _dbContext.Shipments
                .IgnoreQueryFilters()
                .CountAsync(shipment =>
                    !shipment.IsDeleted &&
                    shipment.CarrierCode != CarrierCodes.Manual &&
                    shipment.ExternalRegistrationCompletedAtUtc == null &&
                    shipment.Status == ShipmentStatus.PendingExternalRegistration &&
                    (shipment.ExternalRegistrationState == ExternalRegistrationStateFailed ||
                     shipment.ExternalRegistrationState == ExternalRegistrationStateReconciliationRequired ||
                     (shipment.ExternalRegistrationRequestedAtUtc ?? shipment.CreatedAt) <= shipmentCriticalCutoffUtc ||
                     (shipment.ExternalRegistrationLastAttemptAtUtc != null &&
                      shipment.ExternalRegistrationLastAttemptAtUtc <= shipmentCriticalCutoffUtc)),
                    cancellationToken);

            var warningPendingCarrierRegistrations = await _dbContext.Shipments
                .IgnoreQueryFilters()
                .CountAsync(shipment =>
                    !shipment.IsDeleted &&
                    shipment.CarrierCode != CarrierCodes.Manual &&
                    shipment.ExternalRegistrationCompletedAtUtc == null &&
                    shipment.Status == ShipmentStatus.PendingExternalRegistration &&
                    ((shipment.ExternalRegistrationRequestedAtUtc ?? shipment.CreatedAt) <= shipmentWarningCutoffUtc ||
                     (shipment.ExternalRegistrationLastAttemptAtUtc != null &&
                      shipment.ExternalRegistrationLastAttemptAtUtc <= shipmentWarningCutoffUtc)),
                    cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["UnresolvedRefundCount"] = unresolvedRefundCount,
                ["UnresolvedCapturedWebhookCount"] = unresolvedCapturedWebhookCount,
                ["UnresolvedRefundWebhookCount"] = unresolvedRefundWebhookCount,
                ["ReconciliationRequiredShipmentCount"] = reconciliationRequiredShipmentCount,
                ["CriticalPendingCarrierRegistrations"] = criticalPendingCarrierRegistrations,
                ["WarningPendingCarrierRegistrations"] = warningPendingCarrierRegistrations
            };

            if (unresolvedRefundCount > 0 ||
                unresolvedCapturedWebhookCount > 0 ||
                unresolvedRefundWebhookCount > 0 ||
                reconciliationRequiredShipmentCount > 0 ||
                criticalPendingCarrierRegistrations > 0)
            {
                _logger.LogError(
                    "CommerceHub external outcome recovery readiness failed | UnresolvedRefunds: {UnresolvedRefunds} | UnresolvedCapturedWebhooks: {UnresolvedCapturedWebhooks} | UnresolvedRefundWebhooks: {UnresolvedRefundWebhooks} | ReconciliationRequiredShipments: {ReconciliationRequiredShipments} | CriticalPendingCarrierRegistrations: {CriticalPendingCarrierRegistrations}",
                    unresolvedRefundCount,
                    unresolvedCapturedWebhookCount,
                    unresolvedRefundWebhookCount,
                    reconciliationRequiredShipmentCount,
                    criticalPendingCarrierRegistrations);

                return HealthCheckResult.Unhealthy(
                    "CommerceHub external side effects still require deterministic reconciliation or recovery.",
                    data: data);
            }

            if (warningPendingCarrierRegistrations > 0)
            {
                _logger.LogWarning(
                    "CommerceHub carrier registration recovery is lagging | WarningPendingCarrierRegistrations: {WarningPendingCarrierRegistrations}",
                    warningPendingCarrierRegistrations);

                return HealthCheckResult.Degraded(
                    "CommerceHub carrier registration recovery is lagging.",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                "CommerceHub external side-effect recovery is within readiness thresholds.",
                data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CommerceHub external outcome recovery health check failed");
            return HealthCheckResult.Unhealthy(
                "Failed to check CommerceHub external outcome recovery readiness.",
                exception: ex);
        }
    }
}

/// <summary>
/// Health check for Redis latency.
/// Catches slow Redis that could impact cart and checkout performance.
/// </summary>
public class RedisLatencyHealthCheck : IHealthCheck
{
    private readonly RedisConnectionFactory _connectionFactory;
    private readonly ILogger<RedisLatencyHealthCheck> _logger;
    
    // Latency thresholds in milliseconds
    private const int DegradedThresholdMs = 50;
    private const int UnhealthyThresholdMs = 200;

    public RedisLatencyHealthCheck(
        RedisConnectionFactory connectionFactory,
        ILogger<RedisLatencyHealthCheck> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_connectionFactory.IsEnabled)
        {
            return HealthCheckResult.Degraded(
                "Redis is disabled by configuration.",
                data: new Dictionary<string, object> { ["Enabled"] = false });
        }

        try
        {
            var latency = await _connectionFactory.ProbeLatencyAsync(cancellationToken);
            if (latency == null)
            {
                return HealthCheckResult.Unhealthy(
                    "Redis unavailable",
                    data: new Dictionary<string, object>
                    {
                        ["Enabled"] = true,
                        ["IsConnected"] = _connectionFactory.IsConnected,
                        ["FailureCategory"] = "unavailable"
                    });
            }

            // A latency sample is one round trip, not the sum of three sequential
            // cache mutations. Failed/timed-out PING is never a successful sample.
            var latencyMs = latency.Value.TotalMilliseconds;

            var data = new Dictionary<string, object>
            {
                ["LatencyMs"] = latencyMs,
                ["IsConnected"] = _connectionFactory.IsConnected,
                ["Probe"] = "PING"
            };

            if (latencyMs >= UnhealthyThresholdMs)
            {
                _logger.LogError(
                    "Redis latency CRITICAL | LatencyMs: {LatencyMs}",
                    latencyMs);
                
                return HealthCheckResult.Unhealthy(
                    $"Redis latency too high: {latencyMs}ms",
                    data: data);
            }

            if (latencyMs >= DegradedThresholdMs)
            {
                _logger.LogWarning(
                    "Redis latency DEGRADED | LatencyMs: {LatencyMs}",
                    latencyMs);
                
                return HealthCheckResult.Degraded(
                    $"Redis latency elevated: {latencyMs}ms",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"Redis healthy (latency: {latencyMs}ms)",
                data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis latency health check failed");
            return HealthCheckResult.Unhealthy(
                "Redis unavailable",
                exception: ex,
                data: new Dictionary<string, object> { ["Probe"] = "PING", ["FailureCategory"] = ex is TimeoutException ? "timeout" : "command-failure" });
        }
    }
}

/// <summary>
/// Health check for stock reservation integrity.
/// Detects stuck reservations (ReservedQuantity > 0 for orders that completed/failed).
/// </summary>
public class StockReservationHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IInventoryReservationMetricsService _metricsService;
    private readonly IOptions<InventoryReservationMonitoringOptions> _options;
    private readonly ILogger<StockReservationHealthCheck> _logger;

    public StockReservationHealthCheck(
        ApplicationDbContext dbContext,
        IInventoryReservationMetricsService metricsService,
        IOptions<InventoryReservationMonitoringOptions> options,
        ILogger<StockReservationHealthCheck> logger)
    {
        _dbContext = dbContext;
        _metricsService = metricsService;
        _options = options;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check for negative available stock (which should be impossible)
            var negativeStockCount = await _dbContext.ProductVariantStocks
                .CountAsync(s => s.Quantity < s.ReservedQuantity, cancellationToken);

            var snapshot = await _metricsService.RefreshSnapshotAsync(cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["NegativeStockCount"] = negativeStockCount,
                ["TotalReservedQuantity"] = snapshot.TotalReservedQuantity,
                ["StaleReservationCount"] = snapshot.StaleReservationCount,
                ["StaleReservedQuantity"] = snapshot.StaleReservedQuantity,
                ["OldestStaleReservationAgeMinutes"] = snapshot.OldestStaleReservationAgeMinutes,
                ["CommitReleaseRatio"] = snapshot.CommitReleaseRatio
            };

            if (negativeStockCount > 0)
            {
                _logger.LogError(
                    "Stock integrity CRITICAL - negative available stock detected | Count: {Count}",
                    negativeStockCount);
                
                return HealthCheckResult.Unhealthy(
                    $"CRITICAL: {negativeStockCount} variants have negative available stock",
                    data: data);
            }

            if (snapshot.StaleReservationCount > 0)
            {
                _logger.LogWarning(
                    "Stale stock reservations detected | Count: {Count} | ReservedQuantity: {ReservedQuantity} | OldestAgeMinutes: {OldestAgeMinutes}",
                    snapshot.StaleReservationCount,
                    snapshot.StaleReservedQuantity,
                    snapshot.OldestStaleReservationAgeMinutes);

                return HealthCheckResult.Degraded(
                    $"Detected {snapshot.StaleReservationCount} stale reservations holding {snapshot.StaleReservedQuantity} units",
                    data: data);
            }

            if (snapshot.TotalReservedQuantity > _options.Value.HighReservedQuantityThreshold)
            {
                _logger.LogWarning(
                    "High reservation volume | TotalReserved: {TotalReserved}",
                    snapshot.TotalReservedQuantity);
                
                return HealthCheckResult.Degraded(
                    $"High reservation volume: {snapshot.TotalReservedQuantity} units reserved",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"Stock reservations healthy (total reserved: {snapshot.TotalReservedQuantity})",
                data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stock reservation health check failed");
            return HealthCheckResult.Unhealthy(
                "Failed to check stock reservations",
                exception: ex);
        }
    }
}
