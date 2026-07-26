using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// Repository implementation for Order read operations from MongoDB.
/// 
/// PHASE 10.5.A: Order Lifecycle State Machine
/// 
/// ARCHITECTURE:
/// - MongoDB is the READ-ONLY source for Storefront orders
/// - All queries MUST filter by TenantId
/// - NO SQL reads allowed in Storefront for orders
/// </summary>
public class OrderReadRepository : IOrderReadRepository
{
    private readonly IMongoCollection<OrderDocument> _collection;
    private readonly ILogger<OrderReadRepository> _logger;

    public OrderReadRepository(
        IMongoDatabase mongoDatabase,
        ILogger<OrderReadRepository> logger)
    {
        _collection = mongoDatabase.GetCollection<OrderDocument>("orders");
        _logger = logger;
    }

    public async Task<OrderDocument?> GetByIdAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "[OrderRead] GetByIdAsync | TenantId: {TenantId} | OrderId: {OrderId}",
            tenantId, orderId);

        var filter = Builders<OrderDocument>.Filter.And(
            Builders<OrderDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<OrderDocument>.Filter.Eq(x => x.OrderId, orderId));

        var documents = await _collection.Find(filter).ToListAsync(cancellationToken);
        return SelectFreshestProjection(documents);
    }

    public async Task<OrderDocument?> GetByOrderNumberAsync(
        Guid tenantId,
        string orderNumber,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "[OrderRead] GetByOrderNumberAsync | TenantId: {TenantId} | OrderNumber: {OrderNumber}",
            tenantId, orderNumber);

        var filter = Builders<OrderDocument>.Filter.And(
            Builders<OrderDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<OrderDocument>.Filter.Eq(x => x.OrderNumber, orderNumber));

        var documents = await _collection.Find(filter).ToListAsync(cancellationToken);
        return SelectFreshestProjection(documents);
    }

    public async Task<(List<OrderDocument> Orders, int TotalCount)> GetCustomerOrdersAsync(
        Guid tenantId,
        Guid customerId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "[OrderRead] GetCustomerOrdersAsync | TenantId: {TenantId} | OnlineCustomerId: {OnlineCustomerId} | Page: {Page}",
            tenantId, customerId, page);

        var filterBuilder = Builders<OrderDocument>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.Eq(x => x.TenantId, tenantId),
            filterBuilder.Eq(x => x.OnlineCustomerId, customerId));

        var totalCount = await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        var orders = await _collection.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return (orders, (int)totalCount);
    }

    public async Task<List<OrderDocument>> GetByIdsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> orderIds,
        CancellationToken cancellationToken = default)
    {
        if (orderIds == null || orderIds.Count == 0)
        {
            return [];
        }

        _logger.LogDebug(
            "[OrderRead] GetByIdsAsync | TenantId: {TenantId} | OrderCount: {OrderCount}",
            tenantId,
            orderIds.Count);

        var requestedIds = orderIds
            .Where(orderId => orderId != Guid.Empty)
            .Distinct()
            .ToList();

        if (requestedIds.Count == 0)
        {
            return [];
        }

        var filterBuilder = Builders<OrderDocument>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.Eq(x => x.TenantId, tenantId),
            filterBuilder.In(x => x.OrderId, requestedIds));

        var documents = await _collection.Find(filter).ToListAsync(cancellationToken);

        // Deduplicate by OrderId, keeping the document with the highest LastEventVersion
        // (most up-to-date projection). This defends against race conditions or index issues.
        var deduplicatedDocuments = documents
            .GroupBy(doc => doc.OrderId)
            .Select(group => SelectFreshestProjection(group)!);

        var documentLookup = deduplicatedDocuments.ToDictionary(document => document.OrderId);

        return requestedIds
            .Where(documentLookup.ContainsKey)
            .Select(orderId => documentLookup[orderId])
            .ToList();
    }

    public async Task<OrderDocument?> GetOrderTrackingAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "[OrderRead] GetOrderTrackingAsync | TenantId: {TenantId} | OrderId: {OrderId}",
            tenantId, orderId);

        // For tracking, we return the full document including timeline
        return await GetByIdAsync(tenantId, orderId, cancellationToken);
    }

    public async Task<List<OrderDocument>> GetOrdersByEmailAsync(
        Guid tenantId,
        string email,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "[OrderRead] GetOrdersByEmailAsync | TenantId: {TenantId} | Email: {Email}",
            tenantId, email);

        var filter = Builders<OrderDocument>.Filter.And(
            Builders<OrderDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<OrderDocument>.Filter.Eq(x => x.CustomerEmail, email.ToLowerInvariant()));

        return await _collection.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Limit(20) // Limit for security
            .ToListAsync(cancellationToken);
    }

    private static OrderDocument? SelectFreshestProjection(IEnumerable<OrderDocument> documents)
    {
        return documents
            .OrderByDescending(document => document.LastEventVersion)
            .ThenByDescending(document => document.LastEventOccurredAtUtc ?? DateTime.MinValue)
            .ThenByDescending(document => document.UpdatedAt)
            .ThenByDescending(document => document.CreatedAt)
            .FirstOrDefault();
    }
}
