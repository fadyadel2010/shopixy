using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Projection;

namespace PointOfSale.API.Services.Storefront;

public interface IStorefrontOrderProjectionRepairService
{
    Task EnsureOrderProjectedAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken = default);
    Task EnsureOrderByNumberProjectedAsync(Guid tenantId, string orderNumber, CancellationToken cancellationToken = default);
    Task EnsureCustomerOrdersProjectedAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default);
    Task EnsureCustomerOrdersProjectedPageAsync(Guid tenantId, Guid customerId, int page, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>
    /// Repairs critical storefront order read projections on-demand from SQL authority.
    /// Controllers still read from MongoDB, but they can force a targeted projection rebuild
    /// before serving order detail, tracking, and the exact customer-history page being requested.
/// </summary>
public sealed class StorefrontOrderProjectionRepairService : IStorefrontOrderProjectionRepairService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoCollection<OrderDocument> _collection;
    private readonly ILogger<StorefrontOrderProjectionRepairService> _logger;

    public StorefrontOrderProjectionRepairService(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        ILogger<StorefrontOrderProjectionRepairService> logger)
    {
        _dbContext = dbContext;
        _collection = mongoDatabase.GetCollection<OrderDocument>("orders");
        _logger = logger;
    }

    public async Task EnsureOrderProjectedAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await BuildAuthorityQuery()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.Id == orderId &&
                        item.Source == OrderSource.Ecommerce &&
                        !item.IsDeleted,
                cancellationToken);

        if (order != null)
        {
            await UpsertProjectionAsync(order, cancellationToken);
        }
    }

    public async Task EnsureOrderByNumberProjectedAsync(
        Guid tenantId,
        string orderNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            return;
        }

        var normalizedOrderNumber = orderNumber.Trim();
        var order = await BuildAuthorityQuery()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.OrderNumber == normalizedOrderNumber &&
                        item.Source == OrderSource.Ecommerce &&
                        !item.IsDeleted,
                cancellationToken);

        if (order != null)
        {
            await UpsertProjectionAsync(order, cancellationToken);
        }
    }

    public async Task EnsureCustomerOrdersProjectedAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCustomerOrdersProjectedPageAsync(
            tenantId,
            customerId,
            page: 1,
            pageSize: 50,
            cancellationToken);
    }

    public async Task EnsureCustomerOrdersProjectedPageAsync(
        Guid tenantId,
        Guid customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty)
        {
            return;
        }

        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var skipLong = (long)(normalizedPage - 1) * normalizedPageSize;
        var skip = (int)Math.Min(skipLong, int.MaxValue);

        var orders = await BuildAuthorityQuery()
            .Where(item =>
                item.TenantId == tenantId &&
                item.OnlineCustomerId == customerId &&
                item.Source == OrderSource.Ecommerce &&
                !item.IsDeleted)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Skip(skip)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            await UpsertProjectionAsync(order, cancellationToken);
        }
    }

    private IQueryable<Order> BuildAuthorityQuery() =>
        _dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(order => order.Customer)
            .Include(order => order.OnlineCustomer)
            .Include(order => order.Items);

    private async Task UpsertProjectionAsync(Order order, CancellationToken cancellationToken)
    {
        var sourceTimestamp = StorefrontOrderProjectionBuilder.ResolveSourceTimestamp(
            order,
            order.UpdatedAt ?? order.CreatedAt);
        var sourceVersion = order.ProjectionMutationVersion;
        var existing = await _collection
            .Find(document => document.TenantId == order.TenantId && document.OrderId == order.Id)
            .SortByDescending(document => document.LastEventVersion)
            .ThenByDescending(document => document.LastEventOccurredAtUtc)
            .ThenByDescending(document => document.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var authoritySnapshot = await StorefrontOrderProjectionAuthoritySnapshotLoader.LoadAsync(
            _dbContext,
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
            return;
        }

        var filter = BuildFreshnessGuard(order.TenantId, order.Id, sourceTimestamp, sourceVersion);

        try
        {
            var result = await _collection.ReplaceOneAsync(
                filter,
                replacement,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);

            if (result.MatchedCount > 0 || result.UpsertedId != null)
            {
                _logger.LogWarning(
                    "[StorefrontProjectionRepair] Repaired storefront order projection on demand | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber} | SourceVersion: {SourceVersion}",
                    order.TenantId,
                    order.Id,
                    order.OrderNumber,
                    sourceVersion);
            }
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            // A newer projection already won the race.
        }
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
}
