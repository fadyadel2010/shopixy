using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// Repository interface for Order read operations from MongoDB.
/// 
/// PHASE 10.5.A: Order Lifecycle State Machine
/// 
/// ARCHITECTURE:
/// - MongoDB is the READ-ONLY source for Storefront orders
/// - All queries MUST filter by TenantId
/// - NO SQL reads allowed in Storefront for orders
/// </summary>
public interface IOrderReadRepository
{
    /// <summary>
    /// Gets an order by ID for a tenant.
    /// </summary>
    Task<OrderDocument?> GetByIdAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an order by order number for a tenant.
    /// </summary>
    Task<OrderDocument?> GetByOrderNumberAsync(
        Guid tenantId,
        string orderNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets orders for a customer with pagination.
    /// </summary>
    Task<(List<OrderDocument> Orders, int TotalCount)> GetCustomerOrdersAsync(
        Guid tenantId,
        Guid customerId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific ordered set of customer order documents by authoritative order ids.
    /// The caller owns the page shape and count; Mongo only supplies the read-model documents.
    /// </summary>
    Task<List<OrderDocument>> GetByIdsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> orderIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets order tracking information (timeline, status, shipping info).
    /// </summary>
    Task<OrderDocument?> GetOrderTrackingAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets orders by email (for guest order tracking).
    /// </summary>
    Task<List<OrderDocument>> GetOrdersByEmailAsync(
        Guid tenantId,
        string email,
        CancellationToken cancellationToken = default);
}
