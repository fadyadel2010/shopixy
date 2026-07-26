using MassTransit;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.API.Services.Governance;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubOrderManagementService
{
    Task<CommerceHubSubOrder> UpdateSubOrderStatusAsync(
        Guid storeId,
        Guid subOrderId,
        OrderStatus status,
        string? reason,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public class CommerceHubOrderManagementService : ICommerceHubOrderManagementService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IInventoryConsistencyService _inventoryConsistencyService;
    private readonly ICommerceHubFinancialWorkflowService _financialWorkflowService;
    private readonly IUserGovernanceService _userGovernanceService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<CommerceHubOrderManagementService> _logger;

    public CommerceHubOrderManagementService(
        ApplicationDbContext dbContext,
        IInventoryConsistencyService inventoryConsistencyService,
        ICommerceHubFinancialWorkflowService financialWorkflowService,
        IUserGovernanceService userGovernanceService,
        IEventPublisher eventPublisher,
        ILogger<CommerceHubOrderManagementService> logger)
    {
        _dbContext = dbContext;
        _inventoryConsistencyService = inventoryConsistencyService;
        _financialWorkflowService = financialWorkflowService;
        _userGovernanceService = userGovernanceService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// SECURITY HARDENING (Audit C-3):
    /// Wrapped in explicit ReadCommitted transaction to ensure atomicity of:
    /// - SubOrder status update
    /// - Status history record
    /// - Financial workflow (earnings, adjustments) for Delivered status
    /// - Marketplace order status aggregation
    /// A failure at any step rolls back ALL mutations.
    /// </summary>
    public async Task<CommerceHubSubOrder> UpdateSubOrderStatusAsync(
        Guid storeId,
        Guid subOrderId,
        OrderStatus status,
        string? reason,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();

        // =================================================================
        // SECURITY HARDENING (Audit C-3): Explicit transaction boundary
        // Ensures status + financial mutations are atomic.
        // =================================================================
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

        CommerceHubSubOrder subOrder;
        CommerceHubMarketplaceOrder marketplaceOrder;
        OrderStatus oldStatus;

        try
        {
            subOrder = await LoadSubOrderForLifecycleUpdateAsync(subOrderId, cancellationToken)
                ?? throw new CommerceHubCheckoutException(
                    "SUBORDER_NOT_FOUND",
                    "Sub-order was not found.",
                    StatusCodes.Status404NotFound);

            if (subOrder.StoreId != storeId)
            {
                throw new CommerceHubCheckoutException(
                    "SUBORDER_FORBIDDEN",
                    "This sub-order does not belong to the current store.",
                    StatusCodes.Status403Forbidden);
            }

            await EnsureVendorOrderOperationAllowedAsync(storeId, cancellationToken);

            oldStatus = subOrder.Status;

            if (oldStatus == status)
            {
                await transaction.CommitAsync(cancellationToken);
                return subOrder;
            }

            if (CommerceHubOrderStateHelper.IsCompletedVendorStatus(oldStatus))
            {
                throw new CommerceHubCheckoutException(
                    "SUBORDER_ALREADY_COMPLETED",
                    "Delivered vendor orders are locked and cannot be modified.",
                    StatusCodes.Status409Conflict);
            }

            if (!CommerceHubOrderStateHelper.CanTransition(subOrder.Status, status))
            {
                throw new CommerceHubCheckoutException(
                    "SUBORDER_STATUS_INVALID",
                    $"Cannot move sub-order from {subOrder.Status} to {status}. Allowed transitions: {CommerceHubOrderStateHelper.DescribeAllowedTransitions(subOrder.Status)}.",
                    StatusCodes.Status409Conflict);
            }

            if (CommerceHubOrderStateHelper.RequiresPaymentReadiness(status) &&
                !CommerceHubOrderStateHelper.IsReadyForFulfillment(subOrder))
            {
                throw new CommerceHubCheckoutException(
                    "SUBORDER_NOT_READY_FOR_FULFILLMENT",
                    "This vendor order can enter fulfillment only after online payment is collected or when it is marked as cash on delivery.",
                    StatusCodes.Status409Conflict);
            }

            marketplaceOrder = subOrder.MarketplaceOrder;
            var previousMarketplaceStatus = marketplaceOrder.Status;
            var previousMarketplacePaymentStatus = marketplaceOrder.PaymentStatus;
            var transitionedAtUtc = DateTime.UtcNow;

            if (status == OrderStatus.Delivered)
            {
                foreach (var item in subOrder.Items)
                {
                    await _inventoryConsistencyService.CommitDeliveryAsync(
                        new InventoryDeliveryCommitRequest(
                            TenantId: subOrder.StoreId,
                            OrderId: marketplaceOrder.Id,
                            SubOrderId: subOrder.Id,
                            OrderItemId: item.Id,
                            ProductId: item.ProductId,
                            VariantId: item.VariantId,
                            Quantity: item.Quantity,
                            EventId: InventoryEventIdFactory.Create("commercehub-delivery-commit", item.Id),
                            CorrelationId: correlationId,
                            Source: "CommerceHubOrderManagement"),
                        cancellationToken);
                }
            }
            else if (status == OrderStatus.Cancelled)
            {
                foreach (var item in subOrder.Items)
                {
                    await _inventoryConsistencyService.ReleaseOrderItemReservationsAsync(
                        subOrder.StoreId,
                        item.Id,
                        quantity: null,
                        eventId: InventoryEventIdFactory.Create("commercehub-order-cancel-release", item.Id),
                        correlationId: correlationId,
                        source: "CommerceHubOrderManagement",
                        cancellationToken: cancellationToken);
                }
            }

            subOrder.Status = status;

            foreach (var item in subOrder.Items)
            {
                item.Status = MapSubOrderStatusToItemStatus(status);
                item.DeliveredAt = item.Status == CommerceHubOrderItemStatus.Delivered
                    ? item.DeliveredAt ?? transitionedAtUtc
                    : null;
            }

            _dbContext.CommerceHubSubOrderStatusHistory.Add(new CommerceHubSubOrderStatusHistory
            {
                Id = Guid.NewGuid(),
                SubOrderId = subOrder.Id,
                PreviousStatus = oldStatus,
                NewStatus = status,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim(),
                ChangedAtUtc = transitionedAtUtc
            });

            if (status == OrderStatus.Delivered)
            {
                // HARDENING: Narrow financial workflow boundary to only the delivered-state financial mutation
                using (var commerceHubFinancialBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow())
                {
                    await _financialWorkflowService.ApplyDeliveredFinancialsAsync(
                        marketplaceOrder,
                        subOrder,
                        transitionedAtUtc,
                        cancellationToken);
                }
            }

            marketplaceOrder.Status = CommerceHubOrderStateHelper.AggregateMarketplaceStatus(
                marketplaceOrder.SubOrders,
                marketplaceOrder.Status);

            await PublishStatusEventsAsync(
                marketplaceOrder,
                subOrder,
                correlationId,
                reason,
                previousMarketplaceStatus != marketplaceOrder.Status ||
                previousMarketplacePaymentStatus != marketplaceOrder.PaymentStatus,
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[FINANCIAL] Transaction rollback during sub-order status update | SubOrderId: {SubOrderId} | StoreId: {StoreId} | TargetStatus: {TargetStatus}",
                subOrderId, storeId, status);
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation(
            "CommerceHub sub-order status updated | SubOrderId: {SubOrderId} | StoreId: {StoreId} | OldStatus: {OldStatus} | NewStatus: {NewStatus} | MarketplaceOrderId: {MarketplaceOrderId}",
            subOrder.Id,
            subOrder.StoreId,
            oldStatus,
            subOrder.Status,
            marketplaceOrder.Id);

        return subOrder;
    }

    private async Task EnsureVendorOrderOperationAllowedAsync(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _userGovernanceService.EnsureVendorOrderOperationAllowedAsync(storeId, cancellationToken);
        }
        catch (UserGovernanceException exception)
        {
            throw new CommerceHubCheckoutException(
                exception.Code,
                exception.Message,
                exception.StatusCode,
                new { vendorId = storeId });
        }
    }

    private async Task<CommerceHubSubOrder?> LoadSubOrderForLifecycleUpdateAsync(
        Guid subOrderId,
        CancellationToken cancellationToken)
    {
        var marketplaceOrderId = await _dbContext.CommerceHubSubOrders
            .AsNoTracking()
            .Where(subOrder => subOrder.Id == subOrderId && !subOrder.IsDeleted)
            .Select(subOrder => (Guid?)subOrder.MarketplaceOrderId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!marketplaceOrderId.HasValue)
        {
            return null;
        }

        var isPostgres = _dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var isSqlServer = _dbContext.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

        if (isPostgres)
        {
            var lockedMarketplaceOrder = await _dbContext.CommerceHubMarketplaceOrders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""CommerceHubMarketplaceOrders""
                    WHERE ""Id"" = {marketplaceOrderId.Value}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);

            if (lockedMarketplaceOrder == null)
            {
                return null;
            }

            await _dbContext.CommerceHubSubOrders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""CommerceHubSubOrders""
                    WHERE ""MarketplaceOrderId"" = {marketplaceOrderId.Value}
                      AND ""IsDeleted"" = FALSE
                    ORDER BY ""Id""
                    FOR UPDATE")
                .LoadAsync(cancellationToken);

            await _dbContext.CommerceHubSubOrderItems
                .FromSqlInterpolated($@"
                    SELECT item.*
                    FROM ""CommerceHubSubOrderItems"" item
                    INNER JOIN ""CommerceHubSubOrders"" sub_order
                        ON sub_order.""Id"" = item.""SubOrderId""
                    WHERE sub_order.""MarketplaceOrderId"" = {marketplaceOrderId.Value}
                      AND sub_order.""IsDeleted"" = FALSE
                      AND item.""IsDeleted"" = FALSE
                    ORDER BY item.""Id""
                    FOR UPDATE")
                .LoadAsync(cancellationToken);
        }
        else if (isSqlServer)
        {
            // SQL Server: Use UPDLOCK, ROWLOCK, HOLDLOCK for atomic write-lock guarantees
            var lockedMarketplaceOrder = await _dbContext.CommerceHubMarketplaceOrders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [CommerceHubMarketplaceOrders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {marketplaceOrderId.Value}
                      AND [IsDeleted] = 0")
                .FirstOrDefaultAsync(cancellationToken);

            if (lockedMarketplaceOrder == null)
            {
                return null;
            }

            await _dbContext.CommerceHubSubOrders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [CommerceHubSubOrders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [MarketplaceOrderId] = {marketplaceOrderId.Value}
                      AND [IsDeleted] = 0
                    ORDER BY [Id]")
                .LoadAsync(cancellationToken);

            await _dbContext.CommerceHubSubOrderItems
                .FromSqlInterpolated($@"
                    SELECT item.*
                    FROM [CommerceHubSubOrderItems] item WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    INNER JOIN [CommerceHubSubOrders] sub_order WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                        ON sub_order.[Id] = item.[SubOrderId]
                    WHERE sub_order.[MarketplaceOrderId] = {marketplaceOrderId.Value}
                      AND sub_order.[IsDeleted] = 0
                      AND item.[IsDeleted] = 0
                    ORDER BY item.[Id]")
                .LoadAsync(cancellationToken);
        }
        else
        {
            // Unsupported provider: fail fast to prevent mutations without proper locks
            throw new NotSupportedException(
                $"CommerceHub order lifecycle updates require row-level locking support. " +
                $"Provider '{_dbContext.Database.ProviderName}' is not supported. " +
                $"Supported providers: PostgreSQL, SQL Server.");
        }

        return await _dbContext.CommerceHubSubOrders
            .Include(subOrder => subOrder.Items)
            .Include(subOrder => subOrder.MarketplaceOrder)
            .ThenInclude(order => order.SubOrders)
            .FirstOrDefaultAsync(
                subOrder =>
                    subOrder.Id == subOrderId &&
                    !subOrder.IsDeleted &&
                    !subOrder.MarketplaceOrder.IsDeleted,
                cancellationToken);
    }

    private async Task PublishStatusEventsAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        string correlationId,
        string? reason,
        bool marketplaceStatusChanged,
        CancellationToken cancellationToken)
    {
        marketplaceOrder.TouchProjectionMutationVersion();
        subOrder.TouchProjectionMutationVersion();
        var affectedStoreIds = marketplaceOrder.GetAffectedStoreIds();
        
        // SECURITY HARDENING: Validate complete store scope before publishing aggregate event
        // GetAffectedStoreIds() filters out Guid.Empty, so we must verify ALL sub-orders have valid StoreIds
        if (subOrder.StoreId == Guid.Empty ||
            affectedStoreIds.Count == 0 ||
            marketplaceOrder.SubOrders.Any(item => item.StoreId == Guid.Empty))
        {
            throw new InvalidOperationException(
                $"Cannot publish CommerceHub status events for marketplace order {marketplaceOrder.Id}: affected store authority is empty. SubOrderId={subOrder.Id}; StoreId={subOrder.StoreId}");
        }

        var eventVersion = CommerceHubOrderEventMetadata.ResolveAggregateEventVersion(marketplaceOrder);
        var statusReason = string.IsNullOrWhiteSpace(reason) ? "lifecycle" : reason;

        await _eventPublisher.PublishAsync(new CommerceHubSubOrderStatusUpdatedEvent
        {
            MarketplaceOrderId = marketplaceOrder.Id,
            SubOrderId = subOrder.Id,
            StoreId = subOrder.StoreId,
            TenantId = subOrder.StoreId,
            CorrelationId = correlationId,
            IdempotencyKey = CommerceHubOrderEventMetadata.BuildSubOrderStatusIdempotencyKey(
                subOrder,
                eventVersion,
                statusReason),
            OccurredAtUtc = DateTime.UtcNow,
            EventVersion = eventVersion
        }, cancellationToken);

        if (marketplaceStatusChanged)
        {
            await _eventPublisher.PublishAsync(new CommerceHubMarketplaceOrderStatusUpdatedEvent
            {
                MarketplaceOrderId = marketplaceOrder.Id,
                SubOrderId = subOrder.Id,
                TenantId = affectedStoreIds.First(),
                AffectedStoreIds = affectedStoreIds,
                CorrelationId = correlationId,
                IdempotencyKey = CommerceHubOrderEventMetadata.BuildMarketplaceStatusIdempotencyKey(
                    marketplaceOrder,
                    eventVersion,
                    statusReason),
                OccurredAtUtc = DateTime.UtcNow,
                EventVersion = eventVersion
            }, cancellationToken);
        }
    }

    private static CommerceHubOrderItemStatus MapSubOrderStatusToItemStatus(OrderStatus status) =>
        status switch
        {
            OrderStatus.Shipped => CommerceHubOrderItemStatus.Shipped,
            OrderStatus.Delivered or OrderStatus.Completed => CommerceHubOrderItemStatus.Delivered,
            OrderStatus.Cancelled or OrderStatus.Refunded => CommerceHubOrderItemStatus.Cancelled,
            _ => CommerceHubOrderItemStatus.Pending
        };
}
