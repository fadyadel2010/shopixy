using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubOrderProjectionRepairService
{
    Task EnsureOrderProjectedAsync(Guid marketplaceOrderId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repairs customer-critical CommerceHub order reads on demand from SQL authority.
/// The controller still serves MongoDB documents, but it now forces a targeted
/// rebuild before returning not-found or stale order state.
/// </summary>
public sealed class CommerceHubOrderProjectionRepairService : ICommerceHubOrderProjectionRepairService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoCollection<CommerceHubOrderDocument> _collection;
    private readonly ILogger<CommerceHubOrderProjectionRepairService> _logger;

    public CommerceHubOrderProjectionRepairService(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        ILogger<CommerceHubOrderProjectionRepairService> logger)
    {
        _dbContext = dbContext;
        _collection = mongoDatabase.GetCollection<CommerceHubOrderDocument>("commercehub_orders");
        _logger = logger;
    }

    public async Task EnsureOrderProjectedAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken = default)
    {
        if (marketplaceOrderId == Guid.Empty)
        {
            return;
        }

        var order = await _dbContext.CommerceHubMarketplaceOrders
            .AsNoTracking()
            .Include(item => item.SubOrders)
            .ThenInclude(item => item.Items)
            .FirstOrDefaultAsync(
                item => item.Id == marketplaceOrderId && !item.IsDeleted,
                cancellationToken);

        if (order == null)
        {
            return;
        }

        order.SubOrders = order.SubOrders.GetOrderedSubOrders().ToList();
        var document = CommerceHubOrderProjectionRepairSupport.BuildProjectionDocument(order);

        try
        {
            var result = await _collection.ReplaceOneAsync(
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
                var existing = await LoadProjectionRecoveryStateAsync(order.Id, cancellationToken);
                if (CommerceHubOrderProjectionRepairSupport.ProjectionProvesSqlAuthority(
                    existing,
                    order.CustomerId,
                    document.LastSourceMutationTicks,
                    document.ProjectionChecksum))
                {
                    await MarkProjectedAsync(order.Id, cancellationToken);
                }
                else
                {
                    // SECURITY: Fail closed - do not allow callers to proceed with untrusted projection
                    throw new CommerceHubProjectionRepairException(
                        "Projection repair failed: existing projection does not prove SQL authority",
                        order.Id,
                        order.CustomerId,
                        document.LastSourceMutationTicks,
                        document.ProjectionChecksum);
                }

                return;
            }

            _logger.LogWarning(
                "[CommerceHubProjectionRepair] Repaired marketplace order projection on demand | OrderId: {OrderId} | OrderNumber: {OrderNumber} | SourceVersion: {SourceVersion}",
                order.Id,
                order.OrderNumber,
                ProductEventVersionHelper.ToProductEventVersion(document.LastSourceMutationTicks));

            await MarkProjectedAsync(order.Id, cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogInformation(
                ex,
                "[CommerceHubProjectionRepair] Collapsed concurrent projection repair race | OrderId: {OrderId}",
                order.Id);

            var existing = await LoadProjectionRecoveryStateAsync(order.Id, cancellationToken);
            if (CommerceHubOrderProjectionRepairSupport.ProjectionProvesSqlAuthority(
                existing,
                order.CustomerId,
                document.LastSourceMutationTicks,
                document.ProjectionChecksum))
            {
                await MarkProjectedAsync(order.Id, cancellationToken);
            }
            else
            {
                // SECURITY: Fail closed - do not allow callers to proceed with untrusted projection
                throw new CommerceHubProjectionRepairException(
                    "Projection repair failed after race: existing projection does not prove SQL authority",
                    order.Id,
                    order.CustomerId,
                    document.LastSourceMutationTicks,
                    document.ProjectionChecksum);
            }
        }
    }

    private async Task<CommerceHubOrderProjectionRepairSupport.CommerceHubProjectionRecoveryState?> LoadProjectionRecoveryStateAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken)
    {
        return await _collection
            .Find(doc => doc.MarketplaceOrderId == marketplaceOrderId)
            .SortByDescending(doc => doc.LastSourceMutationTicks)
            .ThenByDescending(doc => doc.LastEventVersion)
            .ThenByDescending(doc => doc.LastEventOccurredAtUtc)
            .ThenByDescending(doc => doc.UpdatedAt)
            .Project(doc => new CommerceHubOrderProjectionRepairSupport.CommerceHubProjectionRecoveryState
            {
                CustomerId = doc.CustomerId,
                LastEventOccurredAtUtc = doc.LastEventOccurredAtUtc,
                LastEventVersion = doc.LastEventVersion,
                LastSourceMutationTicks = doc.LastSourceMutationTicks,
                ProjectionChecksum = doc.ProjectionChecksum
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task MarkProjectedAsync(Guid marketplaceOrderId, CancellationToken cancellationToken)
    {
        var tracked = await _dbContext.CommerceHubMarketplaceOrders
            .FirstOrDefaultAsync(item => item.Id == marketplaceOrderId, cancellationToken);

        if (tracked == null || tracked.IsProjected)
        {
            return;
        }

        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginProjection();
        tracked.IsProjected = true;
        tracked.IsProjectionTerminalFailure = false;
        tracked.ProjectedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Exception thrown when CommerceHub order projection repair fails to establish SQL authority.
/// This is a fail-closed security measure to prevent callers from proceeding with untrusted projections.
/// </summary>
public sealed class CommerceHubProjectionRepairException : InvalidOperationException
{
    public Guid OrderId { get; }
    public Guid CustomerId { get; }
    public long LastSourceMutationTicks { get; }
    public string ProjectionChecksum { get; }

    public CommerceHubProjectionRepairException(
        string message,
        Guid orderId,
        Guid customerId,
        long lastSourceMutationTicks,
        string projectionChecksum)
        : base(message)
    {
        OrderId = orderId;
        CustomerId = customerId;
        LastSourceMutationTicks = lastSourceMutationTicks;
        ProjectionChecksum = projectionChecksum;
    }
}
