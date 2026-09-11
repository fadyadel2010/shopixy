using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.API.Services.CommerceHub;

/// <summary>
/// Background service that detects marketplace orders that were persisted to SQL 
/// but never projected to MongoDB (e.g. because the event publish failed after checkout).
/// Runs every 60 seconds and re-projects missing or checksum-divergent documents.
/// </summary>
public class CommerceHubProjectionReconciliationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TerminalProjectionRetryInterval = TimeSpan.FromMinutes(15);
    private const int MaxPassesPerCycle = 5;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CommerceHubProjectionReconciliationService> _logger;

    public CommerceHubProjectionReconciliationService(
        IServiceScopeFactory scopeFactory,
        ILogger<CommerceHubProjectionReconciliationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ProjectionRecovery] CommerceHub projection reconciliation service started");

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
                _logger.LogError(ex, "[ProjectionRecovery] Reconciliation cycle failed unexpectedly");
            }
        }

        _logger.LogInformation("[ProjectionRecovery] CommerceHub projection reconciliation service stopped");
    }

    private async Task RunRecoveryCycleAsync(CancellationToken cancellationToken)
    {
        for (var pass = 0; pass < MaxPassesPerCycle; pass++)
        {
            var processed = await ReconcileBatchAsync(cancellationToken);
            if (processed < 50)
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
        var collection = mongoDatabase.GetCollection<CommerceHubOrderDocument>("commercehub_orders");
        var terminalRetryCutoffUtc = DateTime.UtcNow.Subtract(TerminalProjectionRetryInterval);

        // PRODUCTION HARDENING: terminal projection failures stay retryable on a slower cadence.
        // SQL remains the authority; the terminal flag is an operations severity marker, not a dead-letter.
        var unprojectedOrders = await dbContext.CommerceHubMarketplaceOrders
            .AsNoTracking()
            .Include(order => order.SubOrders)
            .ThenInclude(subOrder => subOrder.Items)
            .Where(order =>
                !order.IsDeleted &&
                !order.IsProjected &&
                (!order.IsProjectionTerminalFailure ||
                 order.LastProjectionAttemptAt == null ||
                 order.LastProjectionAttemptAt <= terminalRetryCutoffUtc))
            .OrderBy(order => order.IsProjectionTerminalFailure)
            .ThenBy(order => order.LastProjectionAttemptAt == null ? 0 : 1)
            .ThenBy(order => order.LastProjectionAttemptAt)
            .ThenBy(order => order.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        // Apply SubOrders ordering client-side after materialization
        foreach (var order in unprojectedOrders)
        {
            order.SubOrders = order.SubOrders.GetOrderedSubOrders().ToList();
        }

        if (unprojectedOrders.Count == 0)
        {
            return 0;
        }

        _logger.LogInformation(
            "[ProjectionRecovery] Found {Count} unprojected marketplace orders to reconcile",
            unprojectedOrders.Count);

        var recoveredCount = 0;

        foreach (var order in unprojectedOrders)
        {
            try
            {
                // PRODUCTION HARDENING: Track projection attempt before processing
                if (!await RecordProjectionAttemptAsync(dbContext, order.Id, cancellationToken))
                {
                    continue;
                }

                var document = CommerceHubOrderProjectionRepairSupport.BuildProjectionDocument(order);

                var result = await collection.ReplaceOneAsync(
                    CommerceHubOrderProjectionRepairSupport.BuildProjectionRecoveryFilter(
                        order.Id,
                        order.CustomerId,
                        document.LastSourceMutationTicks,
                        document.ProjectionChecksum),
                    document,
                    new ReplaceOptions { IsUpsert = true },
                    cancellationToken);

                if (result.MatchedCount == 0 && result.UpsertedId == null)
                {
                    var existing = await collection
                        .Find(doc => doc.MarketplaceOrderId == order.Id)
                        .Project(doc => new CommerceHubOrderProjectionRepairSupport.CommerceHubProjectionRecoveryState
                        {
                            CustomerId = doc.CustomerId,
                            LastEventOccurredAtUtc = doc.LastEventOccurredAtUtc,
                            LastEventVersion = doc.LastEventVersion,
                            LastSourceMutationTicks = doc.LastSourceMutationTicks,
                            ProjectionChecksum = doc.ProjectionChecksum
                        })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (CommerceHubOrderProjectionRepairSupport.ProjectionProvesSqlAuthority(
                        existing,
                        order.CustomerId,
                        document.LastSourceMutationTicks,
                        document.ProjectionChecksum))
                    {
                        await MarkProjectedAsync(dbContext, order.Id, cancellationToken);
                        continue;
                    }

                    _logger.LogWarning(
                        "[ProjectionRecovery] Existing marketplace order projection did not prove SQL authority and was not overwritten because it may be newer | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                        order.Id,
                        order.OrderNumber);
                    continue;
                }

                _logger.LogWarning(
                    "[ProjectionRecovery] Re-projected or repaired marketplace order | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                    order.Id,
                    order.OrderNumber);

                await MarkProjectedAsync(dbContext, order.Id, cancellationToken);
                recoveredCount++;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                var document = CommerceHubOrderProjectionRepairSupport.BuildProjectionDocument(order);
                var replacedOrphan = await CommerceHubOrderProjectionRepairSupport.TryReplaceOrphanedOrderNumberCollisionAsync(
                    dbContext,
                    collection,
                    order,
                    document,
                    cancellationToken);

                if (replacedOrphan)
                {
                    _logger.LogWarning(
                        "[ProjectionRecovery] Replaced orphaned order-number projection from SQL authority | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                        order.Id,
                        order.OrderNumber);
                    await MarkProjectedAsync(dbContext, order.Id, cancellationToken);
                    recoveredCount++;
                    continue;
                }

                _logger.LogInformation(
                    ex,
                    "[ProjectionRecovery] Marketplace order projection repair collapsed a concurrent upsert race | OrderId: {OrderId}",
                    order.Id);

                // Re-run SQL-authority check against the existing Mongo document
                var existing = await collection
                        .Find(doc => doc.MarketplaceOrderId == order.Id)
                        .Project(doc => new CommerceHubOrderProjectionRepairSupport.CommerceHubProjectionRecoveryState
                        {
                            CustomerId = doc.CustomerId,
                            LastEventOccurredAtUtc = doc.LastEventOccurredAtUtc,
                        LastEventVersion = doc.LastEventVersion,
                        LastSourceMutationTicks = doc.LastSourceMutationTicks,
                        ProjectionChecksum = doc.ProjectionChecksum
                        })
                        .FirstOrDefaultAsync(cancellationToken);

                if (CommerceHubOrderProjectionRepairSupport.ProjectionProvesSqlAuthority(
                    existing,
                    order.CustomerId,
                    document.LastSourceMutationTicks,
                    document.ProjectionChecksum))
                {
                    await MarkProjectedAsync(dbContext, order.Id, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[ProjectionRecovery] Failed to reconcile marketplace order | OrderId: {OrderId}",
                    order.Id);
            }
        }

        if (recoveredCount > 0)
        {
            _logger.LogInformation(
                "[ProjectionRecovery] Reconciliation cycle complete | Recovered: {Recovered} | Total: {Total}",
                recoveredCount,
                unprojectedOrders.Count);
        }

        return unprojectedOrders.Count;
    }

    private static async Task MarkProjectedAsync(
        ApplicationDbContext dbContext,
        Guid marketplaceOrderId,
        CancellationToken cancellationToken)
    {
        var tracked = await dbContext.CommerceHubMarketplaceOrders
            .FirstOrDefaultAsync(o => o.Id == marketplaceOrderId, cancellationToken);

        if (tracked != null && !tracked.IsProjected)
        {
            using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginProjection();
            tracked.IsProjected = true;
            tracked.IsProjectionTerminalFailure = false;
            tracked.ProjectedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<bool> RecordProjectionAttemptAsync(
        ApplicationDbContext dbContext,
        Guid marketplaceOrderId,
        CancellationToken cancellationToken)
    {
        const int TerminalFailureThreshold = 10;

        var nowUtc = DateTime.UtcNow;
        
        var updatedRows = await dbContext.CommerceHubMarketplaceOrders
            .Where(o => o.Id == marketplaceOrderId && !o.IsDeleted && !o.IsProjected)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(o => o.LastProjectionAttemptAt, nowUtc)
                .SetProperty(o => o.ProjectionAttemptCount, o => o.ProjectionAttemptCount + 1)
                .SetProperty(o => o.IsProjectionTerminalFailure, o => o.ProjectionAttemptCount + 1 >= TerminalFailureThreshold),
                cancellationToken);

        if (updatedRows == 0)
        {
            // Order was already projected or deleted by another process
            return false;
        }

        return true;
    }
}
