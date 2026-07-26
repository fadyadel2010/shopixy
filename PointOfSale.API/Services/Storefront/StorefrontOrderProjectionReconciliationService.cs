using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Projection;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Sweeps storefront SQL orders and repairs missing or stale Mongo order projections.
/// This closes the gap where a lost early event could leave the storefront with no
/// readable order even though SQL, payments, and accounting already have it.
/// </summary>
public class StorefrontOrderProjectionReconciliationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 50;
    private const int MaxPassesPerCycle = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StorefrontOrderProjectionReconciliationService> _logger;

    private DateTime _cursorUtc = DateTime.MinValue;
    private readonly HashSet<Guid> _cursorOrderIds = [];

    public StorefrontOrderProjectionReconciliationService(
        IServiceScopeFactory scopeFactory,
        ILogger<StorefrontOrderProjectionReconciliationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[StorefrontProjectionRecovery] Storefront order projection reconciliation service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunRecoveryCycleAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[StorefrontProjectionRecovery] Reconciliation cycle failed unexpectedly");
            }
        }

        _logger.LogInformation("[StorefrontProjectionRecovery] Storefront order projection reconciliation service stopped");
    }

    private async Task RunRecoveryCycleAsync(CancellationToken cancellationToken)
    {
        for (var pass = 0; pass < MaxPassesPerCycle; pass++)
        {
            var processed = await ReconcileBatchAsync(cancellationToken);
            if (processed < BatchSize)
            {
                break;
            }
        }
    }

    private async Task<int> ReconcileBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mongoDatabase = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
        var collection = mongoDatabase.GetCollection<OrderDocument>("orders");

        var candidates = await dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(order => order.Customer)
            .Include(order => order.OnlineCustomer)
            .Include(order => order.Items)
            .Where(order =>
                !order.IsDeleted &&
                order.Source == OrderSource.Ecommerce &&
                ((order.UpdatedAt ?? order.CreatedAt) > _cursorUtc ||
                 ((order.UpdatedAt ?? order.CreatedAt) == _cursorUtc && !_cursorOrderIds.Contains(order.Id))))
            .OrderBy(order => order.UpdatedAt ?? order.CreatedAt)
            .ThenBy(order => order.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            if (_cursorUtc != DateTime.MinValue || _cursorOrderIds.Count > 0)
            {
                _cursorUtc = DateTime.MinValue;
                _cursorOrderIds.Clear();
            }

            return 0;
        }

        var repaired = 0;

        foreach (var order in candidates)
        {
            var sourceTimestamp = StorefrontOrderProjectionBuilder.ResolveSourceTimestamp(order, order.UpdatedAt ?? order.CreatedAt);
            var sourceVersion = order.ProjectionMutationVersion;
            var existing = await collection
                .Find(doc => doc.TenantId == order.TenantId && doc.OrderId == order.Id)
                .SortByDescending(doc => doc.LastEventVersion)
                .ThenByDescending(doc => doc.LastEventOccurredAtUtc)
                .ThenByDescending(doc => doc.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            var authoritySnapshot = await StorefrontOrderProjectionAuthoritySnapshotLoader.LoadAsync(
                dbContext,
                order.TenantId,
                order.Id,
                cancellationToken);

            var replacement = StorefrontOrderProjectionBuilder.Build(
                order,
                Guid.NewGuid(),
                sourceTimestamp,
                sourceVersion,
                existing?.Timeline,
                authoritySnapshot);

            if (ProjectionAlreadyMatches(existing, replacement, sourceTimestamp, sourceVersion))
            {
                AdvanceCursor(order);
                continue;
            }

            var filter = BuildFreshnessGuard(order.TenantId, order.Id, sourceTimestamp, sourceVersion);
            
            try
            {
                var result = await collection.ReplaceOneAsync(
                    filter,
                    replacement,
                    new ReplaceOptions { IsUpsert = true },
                    cancellationToken);

                if (result.MatchedCount == 0 && result.UpsertedId == null)
                {
                    AdvanceCursor(order);
                    continue;
                }

                repaired++;
                AdvanceCursor(order);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
            {
                // Duplicate key error: freshness guard rejected the update but IsUpsert=true
                // attempted an insert that violated the unique index on (TenantId, OrderId).
                // This means a newer projection already exists, so treat it as a no-op.
                AdvanceCursor(order);
                continue;
            }
            catch (MongoWriteException ex)
            {
                _logger.LogError(
                    ex,
                    "[StorefrontProjectionRecovery] Unexpected MongoDB write error | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber} | ErrorCode: {ErrorCode}",
                    order.TenantId,
                    order.Id,
                    order.OrderNumber,
                    ex.WriteError?.Code);
                AdvanceCursor(order);
                continue;
            }

            _logger.LogWarning(
                "[StorefrontProjectionRecovery] Repaired storefront order projection | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber} | SourceVersion: {SourceVersion}",
                order.TenantId,
                order.Id,
                order.OrderNumber,
                sourceVersion);
        }

        if (repaired > 0)
        {
            _logger.LogInformation(
                "[StorefrontProjectionRecovery] Reconciliation cycle complete | Repaired: {Repaired} | BatchSize: {BatchSize}",
                repaired,
                candidates.Count);
        }

        return candidates.Count;
    }

    private static bool ProjectionAlreadyMatches(
        OrderDocument? existing,
        OrderDocument replacement,
        DateTime sourceTimestamp,
        long sourceVersion)
    {
        if (existing == null)
        {
            return false;
        }

        var clampedVersion = sourceVersion > int.MaxValue ? int.MaxValue : (int)sourceVersion;
        if (existing.LastEventVersion < clampedVersion)
        {
            return false;
        }

        if ((existing.LastEventOccurredAtUtc ?? DateTime.MinValue) < sourceTimestamp)
        {
            return false;
        }

        return string.Equals(
            StorefrontOrderProjectionBuilder.ComputeAuthorityFingerprint(existing),
            StorefrontOrderProjectionBuilder.ComputeAuthorityFingerprint(replacement),
            StringComparison.Ordinal);
    }

    private static FilterDefinition<OrderDocument> BuildFreshnessGuard(
        Guid tenantId,
        Guid orderId,
        DateTime sourceTimestamp,
        long sourceVersion)
    {
        var filter = Builders<OrderDocument>.Filter;
        var clampedVersion = sourceVersion > int.MaxValue ? int.MaxValue : (int)sourceVersion;

        return filter.And(
            filter.Eq(document => document.TenantId, tenantId),
            filter.Eq(document => document.OrderId, orderId),
            filter.Or(
                filter.Exists(document => document.LastEventVersion, false),
                filter.Lt(document => document.LastEventVersion, clampedVersion),
                filter.And(
                    filter.Eq(document => document.LastEventVersion, clampedVersion),
                    filter.Lte(document => document.LastEventOccurredAtUtc, sourceTimestamp))));
    }

    private void AdvanceCursor(Order order)
    {
        var cursorValue = order.UpdatedAt ?? order.CreatedAt;

        if (cursorValue > _cursorUtc)
        {
            _cursorUtc = cursorValue;
            _cursorOrderIds.Clear();
        }

        if (cursorValue == _cursorUtc)
        {
            _cursorOrderIds.Add(order.Id);
        }
    }
}
