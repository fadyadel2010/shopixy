using System.Text.Json;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub;

public sealed record InventoryReservationRequest(
    Guid TenantId,
    Guid OrderId,
    Guid? SubOrderId,
    Guid OrderItemId,
    Guid ProductId,
    Guid VariantId,
    decimal Quantity,
    Guid? WarehouseId,
    DateTime? ExpiresAtUtc,
    Guid EventId,
    string? CorrelationId,
    string Source);

public sealed record InventoryDeliveryCommitRequest(
    Guid TenantId,
    Guid OrderId,
    Guid? SubOrderId,
    Guid OrderItemId,
    Guid ProductId,
    Guid VariantId,
    decimal Quantity,
    Guid EventId,
    string? CorrelationId,
    string Source);

public sealed record InventoryReturnReceiptRequest(
    Guid TenantId,
    Guid ReturnId,
    Guid RefundId,
    Guid OrderId,
    Guid OrderItemId,
    Guid ProductId,
    Guid VariantId,
    decimal DeliveredQuantity,
    decimal ResellableQuantity,
    decimal DamagedQuantity,
    Guid EventId,
    string? CorrelationId,
    string Source);

public interface IInventoryConsistencyService
{
    Task<bool> ReserveOrderItemAsync(
        InventoryReservationRequest request,
        CancellationToken cancellationToken = default);

    Task ReleaseReservationAsync(
        Guid tenantId,
        Guid reservationId,
        decimal quantity,
        Guid eventId,
        string? correlationId,
        string source,
        CancellationToken cancellationToken = default);

    Task ReleaseOrderItemReservationsAsync(
        Guid tenantId,
        Guid orderItemId,
        decimal? quantity,
        Guid eventId,
        string? correlationId,
        string source,
        CancellationToken cancellationToken = default);

    Task ReleaseMarketplaceOrderReservationsAsync(
        Guid marketplaceOrderId,
        Guid eventId,
        string? correlationId,
        string source,
        CancellationToken cancellationToken = default);

    Task EnsureMarketplaceOrderReservationsActiveAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken = default);

    Task ReacquireMarketplaceOrderReservationsForPaymentAsync(
        Guid marketplaceOrderId,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<int> FinalizeMarketplaceOrderReservationsForPaymentAsync(
        Guid marketplaceOrderId,
        Guid paymentId,
        Guid eventId,
        string? correlationId,
        string source,
        CancellationToken cancellationToken = default);

    Task CommitDeliveryAsync(
        InventoryDeliveryCommitRequest request,
        CancellationToken cancellationToken = default);

    Task ReceiveReturnAsync(
        InventoryReturnReceiptRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class InventoryConsistencyService : IInventoryConsistencyService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<InventoryConsistencyService> _logger;

    private sealed record MarketplaceReservationAuthorityItem(
        Guid TenantId,
        Guid SubOrderId,
        Guid OrderItemId,
        Guid ProductId,
        Guid VariantId,
        decimal RequiredQuantity);

    private static readonly TimeSpan MarketplacePaymentReservationTtl = TimeSpan.FromMinutes(30);

    public InventoryConsistencyService(
        ApplicationDbContext dbContext,
        ILogger<InventoryConsistencyService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> ReserveOrderItemAsync(
        InventoryReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            return false;
        }

        return await ExecuteInTransactionAsync(async () =>
        {
            var start = await TryBeginEventAsync(
                request.TenantId,
                request.EventId,
                InventoryEventType.StockReserved,
                request.OrderId,
                request.OrderItemId,
                BuildPayloadHash(
                    request.TenantId,
                    request.OrderId,
                    request.OrderItemId,
                    request.VariantId,
                    request.Quantity,
                    request.WarehouseId),
                request.CorrelationId,
                request.Source,
                cancellationToken);

            if (start.AlreadyHandled)
            {
                return true;
            }

            var inbox = start.Inbox!;
            var nowUtc = DateTime.UtcNow;
            var movements = new List<StockMovement>();
            var pendingChanges = new List<PendingCommerceHubStockSyncChange>();

            await EnsureReservationRequestMatchesSqlCatalogAuthorityAsync(
                request,
                cancellationToken);

            var stocks = await LoadStocksForUpdateAsync(
                request.TenantId,
                request.VariantId,
                request.WarehouseId,
                cancellationToken);
            var currentStocks = await ProductVariantInventoryLineageAuthority.FilterCurrentInventoryStockOwnersAsync(
                _dbContext,
                request.TenantId,
                stocks,
                cancellationToken);

            var rawTotalAvailable = stocks.Sum(stock => stock.AvailableQuantity);
            var currentTotalAvailable = currentStocks.Sum(stock => stock.AvailableQuantity);

            if (rawTotalAvailable >= request.Quantity &&
                currentTotalAvailable < request.Quantity)
            {
                throw BuildInvariantException(
                    "MARKETPLACE_RESERVATION_STOCK_AUTHORITY_LOST",
                    "Marketplace reservation cannot be created because SQL stock rows are no longer backed by current warehouse, variant, or stock ownership authority.");
            }

            if (currentTotalAvailable < request.Quantity)
            {
                await MarkEventFailedAsync(inbox, cancellationToken);
                return false;
            }

            var remainingToReserve = request.Quantity;
            var reservationStocks = request.WarehouseId.HasValue
                ? currentStocks
                : currentStocks
                    .OrderByDescending(stock => stock.AvailableQuantity)
                    .ThenBy(stock => stock.WarehouseId)
                    .ThenBy(stock => stock.Id)
                    .ToList();

            foreach (var stock in reservationStocks)
            {
                if (remainingToReserve <= 0m)
                {
                    break;
                }

                var toReserve = Math.Min(stock.AvailableQuantity, remainingToReserve);
                if (toReserve <= 0m)
                {
                    continue;
                }

                var oldAvailable = stock.AvailableQuantity;
                stock.ReservedQuantity += toReserve;
                var newAvailable = stock.AvailableQuantity;

                var reservation = new InventoryReservation
                {
                    Id = Guid.NewGuid(),
                    TenantId = request.TenantId,
                    OrderId = request.OrderId,
                    SubOrderId = request.SubOrderId,
                    OrderItemId = request.OrderItemId,
                    ProductId = request.ProductId,
                    VariantId = request.VariantId,
                    WarehouseId = stock.WarehouseId,
                    ReservedQuantity = toReserve,
                    Status = InventoryReservationStatus.Active,
                    ReservationEventId = request.EventId,
                    LastMutationEventId = request.EventId,
                    ExpiresAtUtc = request.ExpiresAtUtc,
                    CreatedAt = nowUtc
                };

                _dbContext.InventoryReservations.Add(reservation);

                movements.Add(CreateMovement(
                    tenantId: request.TenantId,
                    warehouseId: stock.WarehouseId,
                    productId: request.ProductId,
                    variantId: request.VariantId,
                    movementType: StockMovementType.Adjustment,
                    quantityChange: 0m,
                    reservedQuantityChange: toReserve,
                    damagedQuantityChange: 0m,
                    stock: stock,
                    referenceId: request.OrderId,
                    referenceType: "CommerceHubOrder",
                    referenceLineId: request.OrderItemId,
                    reservationId: reservation.Id,
                    inventoryEventId: request.EventId,
                    inventoryEventType: InventoryEventType.StockReserved,
                    correlationId: request.CorrelationId,
                    source: request.Source,
                    notes: "CommerceHub stock reserved for checkout.",
                    nowUtc: nowUtc));

                pendingChanges.Add(new PendingCommerceHubStockSyncChange(
                    request.VariantId,
                    stock.WarehouseId,
                    oldAvailable,
                    newAvailable,
                    "CommerceHub Reservation",
                    request.OrderId,
                    "CommerceHubOrder",
                    request.CorrelationId));

                remainingToReserve -= toReserve;
            }

            if (remainingToReserve > 0m)
            {
                await MarkEventFailedAsync(inbox, cancellationToken);
                return false;
            }

            _dbContext.StockMovements.AddRange(movements);
            inbox.Status = InventoryEventInboxStatus.Applied;
            inbox.AppliedAtUtc = nowUtc;
            inbox.StockMovementId = movements.FirstOrDefault()?.Id;
            inbox.UpdatedAt = nowUtc;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await EnqueueStockSyncOutboxMessagesAsync(
                request.TenantId,
                pendingChanges,
                cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task ReleaseReservationAsync(
        Guid tenantId,
        Guid reservationId,
        decimal quantity,
        Guid eventId,
        string? correlationId,
        string source,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0m)
        {
            return;
        }

        await ExecuteInTransactionAsync(async () =>
        {
            var start = await TryBeginEventAsync(
                tenantId,
                eventId,
                InventoryEventType.StockReleased,
                null,
                reservationId,
                BuildPayloadHash(tenantId, reservationId, quantity),
                correlationId,
                source,
                cancellationToken);

            if (start.AlreadyHandled)
            {
                return 0;
            }

            var inbox = start.Inbox!;
            var reservation = await LoadReservationForUpdateAsync(tenantId, reservationId, cancellationToken)
                ?? throw BuildInvariantException(
                    "INVENTORY_RESERVATION_NOT_FOUND",
                    "Inventory reservation was not found.");

            var releasableQuantity = reservation.RemainingReservedQuantity;
            if (quantity > releasableQuantity)
            {
                throw BuildInvariantException(
                    "INVENTORY_RELEASE_EXCEEDS_RESERVATION",
                    "Inventory release exceeds the remaining reserved quantity.");
            }

            var stock = await LoadStockForUpdateAsync(
                tenantId,
                reservation.VariantId,
                reservation.WarehouseId,
                cancellationToken)
                ?? throw BuildInvariantException(
                    "INVENTORY_STOCK_NOT_FOUND",
                    "Reserved stock row was not found.");

            var nowUtc = DateTime.UtcNow;
            var oldAvailable = stock.AvailableQuantity;
            stock.ReservedQuantity -= quantity;
            reservation.ReleasedQuantity += quantity;
            reservation.LastMutationEventId = eventId;
            reservation.Status = ResolveReservationStatus(reservation, nowUtc);
            reservation.UpdatedAt = nowUtc;
            var newAvailable = stock.AvailableQuantity;

            var movement = CreateMovement(
                tenantId: tenantId,
                warehouseId: stock.WarehouseId,
                productId: reservation.ProductId,
                variantId: reservation.VariantId,
                movementType: StockMovementType.Adjustment,
                quantityChange: 0m,
                reservedQuantityChange: -quantity,
                damagedQuantityChange: 0m,
                stock: stock,
                referenceId: reservation.OrderId,
                referenceType: "CommerceHubOrder",
                referenceLineId: reservation.OrderItemId,
                reservationId: reservation.Id,
                inventoryEventId: eventId,
                inventoryEventType: InventoryEventType.StockReleased,
                correlationId: correlationId,
                source: source,
                notes: "CommerceHub reservation released.",
                nowUtc: nowUtc);

            _dbContext.StockMovements.Add(movement);
            inbox.Status = InventoryEventInboxStatus.Applied;
            inbox.AppliedAtUtc = nowUtc;
            inbox.StockMovementId = movement.Id;
            inbox.UpdatedAt = nowUtc;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await EnqueueStockSyncOutboxMessagesAsync(
                tenantId,
                [
                    new PendingCommerceHubStockSyncChange(
                        reservation.VariantId,
                        stock.WarehouseId,
                        oldAvailable,
                        newAvailable,
                        "CommerceHub Reservation Released",
                        reservation.OrderId,
                        "CommerceHubOrder",
                        correlationId)
                ],
                cancellationToken);
            return 0;
        }, cancellationToken);
    }

    public async Task ReleaseOrderItemReservationsAsync(
        Guid tenantId,
        Guid orderItemId,
        decimal? quantity,
        Guid eventId,
        string? correlationId,
        string source,
        CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync(async () =>
        {
            var start = await TryBeginEventAsync(
                tenantId,
                eventId,
                InventoryEventType.StockReleased,
                null,
                orderItemId,
                BuildPayloadHash(tenantId, orderItemId, quantity),
                correlationId,
                source,
                cancellationToken);

            if (start.AlreadyHandled)
            {
                return 0;
            }

            var inbox = start.Inbox!;
            var nowUtc = DateTime.UtcNow;
            var reservations = await LoadReservationsForUpdateAsync(tenantId, orderItemId, cancellationToken);
            var totalOpenReserved = reservations.Sum(reservation => reservation.RemainingReservedQuantity);
            var remainingToRelease = quantity ?? totalOpenReserved;

            if (remainingToRelease <= 0m)
            {
                inbox.Status = InventoryEventInboxStatus.Applied;
                inbox.AppliedAtUtc = nowUtc;
                inbox.UpdatedAt = nowUtc;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return 0;
            }

            if (remainingToRelease > totalOpenReserved)
            {
                throw BuildInvariantException(
                    "INVENTORY_RELEASE_EXCEEDS_RESERVATION",
                    "Inventory release exceeds the remaining reserved quantity.");
            }

            var stocksByWarehouse = new Dictionary<Guid, ProductVariantStock>();
            var movements = new List<StockMovement>();
            var pendingChanges = new List<PendingCommerceHubStockSyncChange>();

            foreach (var reservation in reservations
                         .OrderByDescending(item => item.RemainingReservedQuantity)
                         .ThenBy(item => item.CreatedAt))
            {
                if (remainingToRelease <= 0m)
                {
                    break;
                }

                var releasable = Math.Min(remainingToRelease, reservation.RemainingReservedQuantity);
                if (releasable <= 0m)
                {
                    continue;
                }

                if (!stocksByWarehouse.TryGetValue(reservation.WarehouseId, out var stock))
                {
                    stock = await LoadStockForUpdateAsync(
                        tenantId,
                        reservation.VariantId,
                        reservation.WarehouseId,
                        cancellationToken)
                        ?? throw BuildInvariantException(
                            "INVENTORY_STOCK_NOT_FOUND",
                            "Reserved stock row was not found.");

                    stocksByWarehouse[reservation.WarehouseId] = stock;
                }

                var oldAvailable = stock.AvailableQuantity;
                stock.ReservedQuantity -= releasable;
                reservation.ReleasedQuantity += releasable;
                reservation.LastMutationEventId = eventId;
                reservation.Status = ResolveReservationStatus(reservation, nowUtc);
                reservation.UpdatedAt = nowUtc;
                var newAvailable = stock.AvailableQuantity;

                movements.Add(CreateMovement(
                    tenantId: tenantId,
                    warehouseId: stock.WarehouseId,
                    productId: reservation.ProductId,
                    variantId: reservation.VariantId,
                    movementType: StockMovementType.Adjustment,
                    quantityChange: 0m,
                    reservedQuantityChange: -releasable,
                    damagedQuantityChange: 0m,
                    stock: stock,
                    referenceId: reservation.OrderId,
                    referenceType: "CommerceHubOrder",
                    referenceLineId: reservation.OrderItemId,
                    reservationId: reservation.Id,
                    inventoryEventId: eventId,
                    inventoryEventType: InventoryEventType.StockReleased,
                    correlationId: correlationId,
                    source: source,
                    notes: "CommerceHub order reservation released.",
                    nowUtc: nowUtc));

                pendingChanges.Add(new PendingCommerceHubStockSyncChange(
                    reservation.VariantId,
                    stock.WarehouseId,
                    oldAvailable,
                    newAvailable,
                    "CommerceHub Reservation Released",
                    reservation.OrderId,
                    "CommerceHubOrder",
                    correlationId));

                remainingToRelease -= releasable;
            }

            _dbContext.StockMovements.AddRange(movements);
            inbox.Status = InventoryEventInboxStatus.Applied;
            inbox.AppliedAtUtc = nowUtc;
            inbox.StockMovementId = movements.FirstOrDefault()?.Id;
            inbox.UpdatedAt = nowUtc;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await EnqueueStockSyncOutboxMessagesAsync(
                tenantId,
                pendingChanges,
                cancellationToken);
            return 0;
        }, cancellationToken);
    }

    public async Task ReleaseMarketplaceOrderReservationsAsync(
        Guid marketplaceOrderId,
        Guid eventId,
        string? correlationId,
        string source,
        CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync(async () =>
        {
            var openReservations = await LoadMarketplaceOrderReservationsForUpdateAsync(
                marketplaceOrderId,
                cancellationToken);

            if (openReservations.Count == 0)
            {
                return 0;
            }

            var tenants = openReservations
                .Select(reservation => reservation.TenantId)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            foreach (var tenantId in tenants)
            {
                var tenantReservations = openReservations
                    .Where(reservation => reservation.TenantId == tenantId)
                    .ToList();

                var start = await TryBeginEventAsync(
                    tenantId,
                    InventoryEventIdFactory.Create($"{eventId:N}:{tenantId:N}", marketplaceOrderId),
                    InventoryEventType.StockReleased,
                    marketplaceOrderId,
                    marketplaceOrderId,
                    BuildPayloadHash(tenantId, marketplaceOrderId, eventId),
                    correlationId,
                    source,
                    cancellationToken);

                if (start.AlreadyHandled)
                {
                    continue;
                }

                var inbox = start.Inbox!;
                var nowUtc = DateTime.UtcNow;
                var stocksByKey = new Dictionary<(Guid VariantId, Guid WarehouseId), ProductVariantStock>();
                var movements = new List<StockMovement>();
                var pendingChanges = new List<PendingCommerceHubStockSyncChange>();

                foreach (var reservation in tenantReservations
                             .OrderBy(item => item.OrderItemId)
                             .ThenBy(item => item.CreatedAt))
                {
                    var releasable = reservation.RemainingReservedQuantity;
                    if (releasable <= 0m)
                    {
                        continue;
                    }

                    var stockKey = (reservation.VariantId, reservation.WarehouseId);
                    if (!stocksByKey.TryGetValue(stockKey, out var stock))
                    {
                        stock = await LoadStockForUpdateAsync(
                            tenantId,
                            reservation.VariantId,
                            reservation.WarehouseId,
                            cancellationToken)
                            ?? throw BuildInvariantException(
                                "INVENTORY_STOCK_NOT_FOUND",
                                "Reserved stock row was not found.");

                        stocksByKey[stockKey] = stock;
                    }

                    var oldAvailable = stock.AvailableQuantity;
                    stock.ReservedQuantity -= releasable;
                    reservation.ReleasedQuantity += releasable;
                    reservation.LastMutationEventId = eventId;
                    reservation.Status = ResolveReservationStatus(reservation, nowUtc);
                    reservation.UpdatedAt = nowUtc;
                    var newAvailable = stock.AvailableQuantity;

                    movements.Add(CreateMovement(
                        tenantId: tenantId,
                        warehouseId: stock.WarehouseId,
                        productId: reservation.ProductId,
                        variantId: reservation.VariantId,
                        movementType: StockMovementType.Adjustment,
                        quantityChange: 0m,
                        reservedQuantityChange: -releasable,
                        damagedQuantityChange: 0m,
                        stock: stock,
                        referenceId: marketplaceOrderId,
                        referenceType: "CommerceHubPayment",
                        referenceLineId: reservation.OrderItemId,
                        reservationId: reservation.Id,
                        inventoryEventId: eventId,
                        inventoryEventType: InventoryEventType.StockReleased,
                        correlationId: correlationId,
                        source: source,
                        notes: "CommerceHub order reservations released after terminal payment failure.",
                        nowUtc: nowUtc));

                    pendingChanges.Add(new PendingCommerceHubStockSyncChange(
                        reservation.VariantId,
                        stock.WarehouseId,
                        oldAvailable,
                        newAvailable,
                        "CommerceHub Payment Failed",
                        marketplaceOrderId,
                        "CommerceHubPayment",
                        correlationId));
                }

                _dbContext.StockMovements.AddRange(movements);
                inbox.Status = InventoryEventInboxStatus.Applied;
                inbox.AppliedAtUtc = nowUtc;
                inbox.StockMovementId = movements.FirstOrDefault()?.Id;
                inbox.UpdatedAt = nowUtc;

                await _dbContext.SaveChangesAsync(cancellationToken);
                await EnqueueStockSyncOutboxMessagesAsync(
                    tenantId,
                    pendingChanges,
                    cancellationToken);
            }

            return 0;
        }, cancellationToken);
    }

    public async Task EnsureMarketplaceOrderReservationsActiveAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync(async () =>
        {
            var nowUtc = DateTime.UtcNow;
            var expectedItems = await _dbContext.CommerceHubSubOrderItems
                .IgnoreQueryFilters()
                .Where(item =>
                    item.SubOrder.MarketplaceOrderId == marketplaceOrderId &&
                    !item.IsDeleted &&
                    !item.SubOrder.IsDeleted &&
                    !item.SubOrder.MarketplaceOrder.IsDeleted &&
                    item.SubOrder.MarketplaceOrder.Status != OrderStatus.Cancelled &&
                    item.SubOrder.MarketplaceOrder.Status != OrderStatus.Refunded &&
                    item.SubOrder.Status != OrderStatus.Cancelled &&
                    item.SubOrder.Status != OrderStatus.Refunded &&
                    item.SubOrder.PaymentMethod == CommerceHubPaymentMethod.Online &&
                    item.Status != CommerceHubOrderItemStatus.Cancelled &&
                    item.Quantity > 0)
                .Select(item => new MarketplaceReservationAuthorityItem(
                    item.SubOrder.StoreId,
                    item.SubOrderId,
                    item.Id,
                    item.ProductId,
                    item.VariantId,
                    (decimal)item.Quantity))
                .ToListAsync(cancellationToken);

            if (expectedItems.Count == 0)
            {
                throw new CommerceHubCheckoutException(
                    "MARKETPLACE_ORDER_HAS_NO_RESERVABLE_ITEMS",
                    "Marketplace payment cannot be accepted because the order has no reservable online items.",
                    StatusCodes.Status409Conflict);
            }

            await EnsureMarketplaceReservationItemsMatchSqlCatalogAuthorityAsync(
                marketplaceOrderId,
                expectedItems,
                cancellationToken);

            await EnsureMarketplaceOrderShippingAuthorityActiveAsync(
                marketplaceOrderId,
                cancellationToken);

            var reservations = await LoadMarketplaceOrderReservationsForUpdateAsync(
                marketplaceOrderId,
                cancellationToken);

            var rawActiveReservations = reservations
                .Where(reservation =>
                    reservation.Status == InventoryReservationStatus.Active &&
                    reservation.RemainingReservedQuantity > 0m &&
                    (reservation.ExpiresAtUtc is null || reservation.ExpiresAtUtc.Value > nowUtc))
                .ToList();

            var activeReservations = await ProductVariantInventoryLineageAuthority.FilterCurrentConsumableReservationsAcrossTenantsAsync(
                _dbContext,
                rawActiveReservations,
                nowUtc,
                cancellationToken);

            var expectedKeys = expectedItems
                .Select(item => (item.TenantId, item.OrderItemId, item.VariantId))
                .ToHashSet();

            var foreignActiveReservation = activeReservations.FirstOrDefault(reservation =>
                !expectedKeys.Contains((reservation.TenantId, reservation.OrderItemId, reservation.VariantId)));
            if (foreignActiveReservation != null)
            {
                throw new CommerceHubCheckoutException(
                    "MARKETPLACE_ORDER_RESERVATION_OWNERSHIP_CONFLICT",
                    "Marketplace payment cannot be accepted because active reservation ownership no longer matches current SQL order item authority.",
                    StatusCodes.Status409Conflict,
                    new
                    {
                        MarketplaceOrderId = marketplaceOrderId,
                        foreignActiveReservation.TenantId,
                        foreignActiveReservation.OrderItemId,
                        foreignActiveReservation.VariantId,
                        foreignActiveReservation.Id
                    });
            }

            var activeReservationQuantities = activeReservations
                .GroupBy(reservation => new
                {
                    reservation.TenantId,
                    reservation.OrderItemId,
                    reservation.VariantId
                })
                .ToDictionary(
                    group => (group.Key.TenantId, group.Key.OrderItemId, group.Key.VariantId),
                    group => group.Sum(reservation => reservation.RemainingReservedQuantity));

            var rawActiveReservationQuantities = rawActiveReservations
                .GroupBy(reservation => new
                {
                    reservation.TenantId,
                    reservation.OrderItemId,
                    reservation.VariantId
                })
                .ToDictionary(
                    group => (group.Key.TenantId, group.Key.OrderItemId, group.Key.VariantId),
                    group => group.Sum(reservation => reservation.RemainingReservedQuantity));

            foreach (var item in expectedItems)
            {
                var key = (item.TenantId, item.OrderItemId, item.VariantId);
                var activeReservedQuantity = activeReservationQuantities.GetValueOrDefault(key);

                if (activeReservedQuantity == item.RequiredQuantity)
                {
                    continue;
                }

                var rawActiveReservedQuantity = rawActiveReservationQuantities.GetValueOrDefault(key);
                if (rawActiveReservedQuantity >= item.RequiredQuantity &&
                    activeReservedQuantity < item.RequiredQuantity)
                {
                    throw new CommerceHubCheckoutException(
                        "MARKETPLACE_ORDER_STOCK_AUTHORITY_LOST",
                        "Marketplace payment cannot be accepted because active reservation rows are no longer backed by current SQL stock, warehouse, and variant lineage authority.",
                        StatusCodes.Status409Conflict,
                        new
                        {
                            MarketplaceOrderId = marketplaceOrderId,
                            item.TenantId,
                            item.OrderItemId,
                            item.VariantId,
                            item.RequiredQuantity,
                            RawActiveReservedQuantity = rawActiveReservedQuantity,
                            CurrentLineageReservedQuantity = activeReservedQuantity
                        });
                }

                throw new CommerceHubCheckoutException(
                    activeReservedQuantity > item.RequiredQuantity
                        ? "MARKETPLACE_ORDER_RESERVATION_OVER_RESERVED"
                        : "MARKETPLACE_ORDER_RESERVATION_MISSING",
                    activeReservedQuantity > item.RequiredQuantity
                        ? "Marketplace payment cannot be accepted because active SQL reservations exceed current order item authority. The order must be reconciled instead of reusing stale reservation visibility."
                        : "Marketplace payment cannot be accepted because one or more order items no longer have active reserved stock. The captured payment must be refunded or the order must be recreated with fresh inventory authority.",
                    StatusCodes.Status409Conflict,
                    new
                    {
                        MarketplaceOrderId = marketplaceOrderId,
                        item.TenantId,
                        item.OrderItemId,
                        item.VariantId,
                        RequiredQuantity = item.RequiredQuantity,
                        ActiveReservedQuantity = activeReservedQuantity
                    });
            }

            await EnsureReservedStockRowsCoverMarketplaceReservationsAsync(
                marketplaceOrderId,
                activeReservations,
                cancellationToken);

            return 0;
        }, cancellationToken);
    }

    public async Task ReacquireMarketplaceOrderReservationsForPaymentAsync(
        Guid marketplaceOrderId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync(async () =>
        {
            var nowUtc = DateTime.UtcNow;
            var reservations = await LoadMarketplaceOrderReservationHistoryForUpdateAsync(
                marketplaceOrderId,
                cancellationToken);
            var marketplaceOrder = await LoadMarketplaceOrderForReservationReacquisitionAsync(
                marketplaceOrderId,
                cancellationToken);

            if (marketplaceOrder == null ||
                marketplaceOrder.Status is not (OrderStatus.Pending or OrderStatus.OnHold) ||
                marketplaceOrder.PaymentStatus != PaymentStatus.Pending ||
                !CommerceHubPaymentMethods.IsOnline(marketplaceOrder.PaymentMethodCode))
            {
                throw new CommerceHubCheckoutException(
                    "MARKETPLACE_RESERVATION_REACQUISITION_NOT_ALLOWED",
                    "Marketplace inventory can only be reacquired for an existing pending online-payment order.",
                    StatusCodes.Status409Conflict);
            }

            var expectedItems = await _dbContext.CommerceHubSubOrderItems
                .IgnoreQueryFilters()
                .Where(item =>
                    item.SubOrder.MarketplaceOrderId == marketplaceOrderId &&
                    !item.IsDeleted &&
                    !item.SubOrder.IsDeleted &&
                    item.SubOrder.Status != OrderStatus.Cancelled &&
                    item.SubOrder.Status != OrderStatus.Refunded &&
                    item.SubOrder.PaymentMethod == CommerceHubPaymentMethod.Online &&
                    item.Status != CommerceHubOrderItemStatus.Cancelled &&
                    item.Quantity > 0)
                .Select(item => new MarketplaceReservationAuthorityItem(
                    item.SubOrder.StoreId,
                    item.SubOrderId,
                    item.Id,
                    item.ProductId,
                    item.VariantId,
                    (decimal)item.Quantity))
                .ToListAsync(cancellationToken);

            if (expectedItems.Count == 0)
            {
                throw new CommerceHubCheckoutException(
                    "MARKETPLACE_ORDER_HAS_NO_RESERVABLE_ITEMS",
                    "Marketplace inventory cannot be reacquired because the order has no reservable online items.",
                    StatusCodes.Status409Conflict);
            }

            await EnsureMarketplaceReservationItemsMatchSqlCatalogAuthorityAsync(
                marketplaceOrderId,
                expectedItems,
                cancellationToken);
            await EnsureMarketplaceOrderShippingAuthorityActiveAsync(
                marketplaceOrderId,
                cancellationToken);
            EnsureHistoricalReservationsMatchCurrentOrderAuthority(
                marketplaceOrderId,
                expectedItems,
                reservations);

            var rawActiveReservations = reservations
                .Where(reservation =>
                    reservation.Status == InventoryReservationStatus.Active &&
                    reservation.RemainingReservedQuantity > 0m &&
                    (reservation.ExpiresAtUtc is null || reservation.ExpiresAtUtc.Value > nowUtc))
                .ToList();
            var activeReservations = await ProductVariantInventoryLineageAuthority.FilterCurrentConsumableReservationsAcrossTenantsAsync(
                _dbContext,
                rawActiveReservations,
                nowUtc,
                cancellationToken);
            var expectedKeys = expectedItems
                .Select(item => (item.TenantId, item.OrderItemId, item.VariantId))
                .ToHashSet();

            if (activeReservations.Any(reservation =>
                    !expectedKeys.Contains((reservation.TenantId, reservation.OrderItemId, reservation.VariantId))))
            {
                throw new CommerceHubCheckoutException(
                    "MARKETPLACE_ORDER_RESERVATION_OWNERSHIP_CONFLICT",
                    "Marketplace inventory cannot be reacquired because active reservation ownership no longer matches the order.",
                    StatusCodes.Status409Conflict);
            }

            var activeByItem = activeReservations
                .GroupBy(reservation => (reservation.TenantId, reservation.OrderItemId, reservation.VariantId))
                .ToDictionary(group => group.Key, group => group.Sum(reservation => reservation.RemainingReservedQuantity));
            var rawActiveByItem = rawActiveReservations
                .GroupBy(reservation => (reservation.TenantId, reservation.OrderItemId, reservation.VariantId))
                .ToDictionary(group => group.Key, group => group.Sum(reservation => reservation.RemainingReservedQuantity));

            foreach (var item in expectedItems)
            {
                var key = (item.TenantId, item.OrderItemId, item.VariantId);
                var activeQuantity = activeByItem.GetValueOrDefault(key);
                if (activeQuantity > item.RequiredQuantity)
                {
                    throw new CommerceHubCheckoutException(
                        "MARKETPLACE_ORDER_RESERVATION_OVER_RESERVED",
                        "Marketplace inventory cannot be reacquired because active reservations exceed current order quantity authority.",
                        StatusCodes.Status409Conflict);
                }

                if (rawActiveByItem.GetValueOrDefault(key) > activeQuantity)
                {
                    throw new CommerceHubCheckoutException(
                        "MARKETPLACE_ORDER_STOCK_AUTHORITY_LOST",
                        "Marketplace inventory cannot be reacquired because an active reservation is no longer backed by current stock lineage authority.",
                        StatusCodes.Status409Conflict);
                }
            }

            if (expectedItems.All(item =>
                    activeByItem.GetValueOrDefault((item.TenantId, item.OrderItemId, item.VariantId)) == item.RequiredQuantity))
            {
                await EnsureReservedStockRowsCoverMarketplaceReservationsAsync(
                    marketplaceOrderId,
                    activeReservations,
                    cancellationToken);
                return 0;
            }

            var paymentHistory = await _dbContext.CommerceHubPayments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(payment => payment.Attempts)
                .Include(payment => payment.Logs)
                .AsSplitQuery()
                .Where(payment => payment.OrderId == marketplaceOrderId)
                .ToListAsync(cancellationToken);
            var hasPaymentAuthorityEvidence = paymentHistory.Any(
                    CommerceHubPaymentSessionRecoveryAuthority.HasReservationReacquisitionBlockingEvidence) ||
                await _dbContext.CommerceHubUnresolvedPaymentWebhooks
                    .IgnoreQueryFilters()
                    .AnyAsync(webhook => webhook.OrderId == marketplaceOrderId, cancellationToken);

            if (hasPaymentAuthorityEvidence)
            {
                throw new CommerceHubCheckoutException(
                    "MARKETPLACE_RESERVATION_REACQUISITION_REQUIRES_NO_PAYMENT_EVIDENCE",
                    "Marketplace inventory cannot be reacquired because payment or provider evidence already exists for this order.",
                    StatusCodes.Status409Conflict);
            }

            var staleOpenReservations = reservations
                .Where(reservation =>
                    reservation.RemainingReservedQuantity > 0m &&
                    !activeReservations.Contains(reservation))
                .ToList();

            if (staleOpenReservations.Any(reservation =>
                    reservation.CommittedQuantity > 0m ||
                    (reservation.Status != InventoryReservationStatus.Expired &&
                     (!reservation.ExpiresAtUtc.HasValue || reservation.ExpiresAtUtc.Value > nowUtc))))
            {
                throw new CommerceHubCheckoutException(
                    "MARKETPLACE_RESERVATION_REACQUISITION_STATE_CONFLICT",
                    "Marketplace inventory cannot be reacquired because historical reservation state requires reconciliation.",
                    StatusCodes.Status409Conflict);
            }

            foreach (var reservation in staleOpenReservations)
            {
                await ReleaseReservationAsync(
                    reservation.TenantId,
                    reservation.Id,
                    reservation.RemainingReservedQuantity,
                    Guid.NewGuid(),
                    correlationId,
                    "CommerceHubPaymentReservationReacquisition",
                    cancellationToken);
            }

            var expiresAtUtc = nowUtc.Add(MarketplacePaymentReservationTtl);
            foreach (var item in expectedItems.OrderBy(item => item.TenantId).ThenBy(item => item.OrderItemId))
            {
                var alreadyReserved = activeByItem.GetValueOrDefault((item.TenantId, item.OrderItemId, item.VariantId));
                var quantityToReserve = item.RequiredQuantity - alreadyReserved;
                if (quantityToReserve <= 0m)
                {
                    continue;
                }

                var reserved = await ReserveOrderItemAsync(
                    new InventoryReservationRequest(
                        item.TenantId,
                        marketplaceOrderId,
                        item.SubOrderId,
                        item.OrderItemId,
                        item.ProductId,
                        item.VariantId,
                        quantityToReserve,
                        null,
                        expiresAtUtc,
                        Guid.NewGuid(),
                        correlationId,
                        "CommerceHubPaymentReservationReacquisition"),
                    cancellationToken);

                if (!reserved)
                {
                    throw new CommerceHubCheckoutException(
                        "OUT_OF_STOCK",
                        "Stock changed before payment could resume. Review current availability before trying again.",
                        StatusCodes.Status409Conflict,
                        new
                        {
                            MarketplaceOrderId = marketplaceOrderId,
                            item.OrderItemId,
                            item.VariantId,
                            Requested = quantityToReserve
                        });
                }
            }

            await EnsureMarketplaceOrderReservationsActiveAsync(marketplaceOrderId, cancellationToken);
            _logger.LogInformation(
                "CommerceHub payment reservation authority reacquired | MarketplaceOrderId: {MarketplaceOrderId} | ItemCount: {ItemCount} | ExpiresAtUtc: {ExpiresAtUtc} | CorrelationId: {CorrelationId}",
                marketplaceOrderId,
                expectedItems.Count,
                expiresAtUtc,
                correlationId);
            return 0;
        }, cancellationToken);
    }

    public async Task<int> FinalizeMarketplaceOrderReservationsForPaymentAsync(
        Guid marketplaceOrderId,
        Guid paymentId,
        Guid eventId,
        string? correlationId,
        string source,
        CancellationToken cancellationToken = default)
    {
        if (marketplaceOrderId == Guid.Empty || paymentId == Guid.Empty || eventId == Guid.Empty)
        {
            throw BuildInvariantException(
                "INVENTORY_PAYMENT_FINALIZATION_IDENTITY_INVALID",
                "Marketplace order, payment, and event identity are required before inventory can be finalized for payment.");
        }

        return await ExecuteInTransactionAsync(async () =>
        {
            await EnsureMarketplaceOrderReservationsActiveAsync(marketplaceOrderId, cancellationToken);

            var nowUtc = DateTime.UtcNow;
            var reservations = await LoadMarketplaceOrderReservationsForUpdateAsync(
                marketplaceOrderId,
                cancellationToken);
            var finalizedCount = 0;

            foreach (var reservation in reservations.Where(item => item.Status == InventoryReservationStatus.Active))
            {
                if (reservation.ExpiresAtUtc == null && reservation.LastMutationEventId == eventId)
                {
                    continue;
                }

                reservation.ExpiresAtUtc = null;
                reservation.LastMutationEventId = eventId;
                reservation.Status = ResolveReservationStatus(reservation, nowUtc);
                reservation.UpdatedAt = nowUtc;
                finalizedCount++;
            }

            _logger.LogInformation(
                "CommerceHub payment inventory finalized | MarketplaceOrderId: {MarketplaceOrderId} | PaymentId: {PaymentId} | EventId: {EventId} | FinalizedReservations: {FinalizedReservations} | CorrelationId: {CorrelationId} | Source: {Source}",
                marketplaceOrderId,
                paymentId,
                eventId,
                finalizedCount,
                correlationId,
                source);

            return finalizedCount;
        }, cancellationToken);
    }

    public async Task CommitDeliveryAsync(
        InventoryDeliveryCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0m)
        {
            return;
        }

        await ExecuteInTransactionAsync(async () =>
        {
            var start = await TryBeginEventAsync(
                request.TenantId,
                request.EventId,
                InventoryEventType.StockDecreased,
                request.OrderId,
                request.OrderItemId,
                BuildPayloadHash(
                    request.TenantId,
                    request.OrderId,
                    request.OrderItemId,
                    request.VariantId,
                    request.Quantity),
                request.CorrelationId,
                request.Source,
                cancellationToken);

            if (start.AlreadyHandled)
            {
                return 0;
            }

            var inbox = start.Inbox!;
            var nowUtc = DateTime.UtcNow;
            var movements = new List<StockMovement>();
            var pendingChanges = new List<PendingCommerceHubStockSyncChange>();

            await EnsureDeliveryCommitSqlAuthorityAsync(
                request,
                nowUtc,
                cancellationToken);

            var reservations = await LoadReservationsForUpdateAsync(
                request.TenantId,
                request.OrderItemId,
                cancellationToken);

            var alreadyCommitted = reservations.Sum(reservation => reservation.CommittedQuantity);
            var remainingToCommit = Math.Max(0m, request.Quantity - alreadyCommitted);

            if (remainingToCommit <= 0m)
            {
                inbox.Status = InventoryEventInboxStatus.Applied;
                inbox.AppliedAtUtc = nowUtc;
                inbox.UpdatedAt = nowUtc;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return 0;
            }

            var activeReservations = await ProductVariantInventoryLineageAuthority.FilterCurrentConsumableReservationsAsync(
                _dbContext,
                request.TenantId,
                reservations
                .Where(reservation =>
                    reservation.Status == InventoryReservationStatus.Active &&
                    reservation.RemainingReservedQuantity > 0m &&
                    (reservation.ExpiresAtUtc == null || reservation.ExpiresAtUtc.Value > nowUtc))
                    .ToList(),
                nowUtc,
                cancellationToken);

            var stocks = await LoadStocksForUpdateAsync(
                request.TenantId,
                request.VariantId,
                warehouseId: null,
                cancellationToken);

            var stocksByWarehouse = stocks.ToDictionary(stock => stock.WarehouseId);

            foreach (var reservation in activeReservations
                         .OrderByDescending(item => item.RemainingReservedQuantity)
                         .ThenBy(item => item.CreatedAt))
            {
                if (remainingToCommit <= 0m)
                {
                    break;
                }

                var reservableCommit = Math.Min(remainingToCommit, reservation.RemainingReservedQuantity);
                if (reservableCommit <= 0m)
                {
                    continue;
                }

                if (!stocksByWarehouse.TryGetValue(reservation.WarehouseId, out var stock))
                {
                    stock = await LoadStockForUpdateAsync(
                        request.TenantId,
                        request.VariantId,
                        reservation.WarehouseId,
                        cancellationToken)
                        ?? throw BuildInvariantException(
                            "INVENTORY_STOCK_NOT_FOUND",
                            "Reserved stock row was not found.");

                    stocksByWarehouse[reservation.WarehouseId] = stock;
                }

                if (stock.ReservedQuantity < reservableCommit || stock.Quantity < reservableCommit)
                {
                    throw BuildInvariantException(
                        "INVENTORY_COMMIT_INVARIANT_VIOLATION",
                        "Reserved stock cannot satisfy the requested delivery commit.");
                }

                var oldAvailable = stock.AvailableQuantity;
                stock.Quantity -= reservableCommit;
                stock.ReservedQuantity -= reservableCommit;
                reservation.CommittedQuantity += reservableCommit;
                reservation.LastMutationEventId = request.EventId;
                reservation.Status = ResolveReservationStatus(reservation, nowUtc);
                reservation.UpdatedAt = nowUtc;
                var newAvailable = stock.AvailableQuantity;

                movements.Add(CreateMovement(
                    tenantId: request.TenantId,
                    warehouseId: stock.WarehouseId,
                    productId: reservation.ProductId,
                    variantId: reservation.VariantId,
                    movementType: StockMovementType.Sale,
                    quantityChange: -reservableCommit,
                    reservedQuantityChange: -reservableCommit,
                    damagedQuantityChange: 0m,
                    stock: stock,
                    referenceId: request.OrderId,
                    referenceType: "CommerceHubDelivery",
                    referenceLineId: request.OrderItemId,
                    reservationId: reservation.Id,
                    inventoryEventId: request.EventId,
                    inventoryEventType: InventoryEventType.StockDecreased,
                    correlationId: request.CorrelationId,
                    source: request.Source,
                    notes: "CommerceHub delivery committed reserved stock.",
                    nowUtc: nowUtc));

                pendingChanges.Add(new PendingCommerceHubStockSyncChange(
                    reservation.VariantId,
                    stock.WarehouseId,
                    oldAvailable,
                    newAvailable,
                    "CommerceHub Delivery Committed",
                    request.OrderId,
                    "CommerceHubDelivery",
                    request.CorrelationId));

                remainingToCommit -= reservableCommit;
            }

            if (remainingToCommit > 0m)
            {
                throw BuildInvariantException(
                    "INVENTORY_DELIVERY_REQUIRES_ACTIVE_RESERVATION",
                    "CommerceHub delivery cannot commit stock that was not previously reserved for the order item.");
            }

            _dbContext.StockMovements.AddRange(movements);
            inbox.Status = InventoryEventInboxStatus.Applied;
            inbox.AppliedAtUtc = nowUtc;
            inbox.StockMovementId = movements.FirstOrDefault()?.Id;
            inbox.UpdatedAt = nowUtc;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await EnqueueStockSyncOutboxMessagesAsync(
                request.TenantId,
                pendingChanges,
                cancellationToken);
            return 0;
        }, cancellationToken);
    }

    public async Task ReceiveReturnAsync(
        InventoryReturnReceiptRequest request,
        CancellationToken cancellationToken = default)
    {
        var totalReceived = request.ResellableQuantity + request.DamagedQuantity;
        if (totalReceived <= 0m)
        {
            throw BuildInvariantException(
                "INVENTORY_RETURN_QUANTITY_INVALID",
                "Return receipt quantity must be greater than zero.");
        }

        await ExecuteInTransactionAsync(async () =>
        {
            var start = await TryBeginEventAsync(
                request.TenantId,
                request.EventId,
                request.DamagedQuantity > 0m
                    ? InventoryEventType.StockDamaged
                    : InventoryEventType.StockIncreased,
                request.ReturnId,
                request.OrderItemId,
                BuildPayloadHash(
                    request.TenantId,
                    request.ReturnId,
                    request.OrderItemId,
                    request.ResellableQuantity,
                    request.DamagedQuantity),
                request.CorrelationId,
                request.Source,
                cancellationToken);

            if (start.AlreadyHandled)
            {
                return 0;
            }

            var inbox = start.Inbox!;
            var nowUtc = DateTime.UtcNow;
            var trackedReturn = await LoadReturnRequestForUpdateAsync(request.ReturnId, cancellationToken)
                ?? throw BuildInvariantException(
                    "RETURN_NOT_FOUND",
                    "Return request was not found.");

            if (trackedReturn.Status == CommerceHubReturnStatus.Confirmed)
            {
                var alreadyApplied =
                    trackedReturn.ReceivedResellableQuantity == DecimalToInt(request.ResellableQuantity) &&
                    trackedReturn.ReceivedDamagedQuantity == DecimalToInt(request.DamagedQuantity);

                if (alreadyApplied)
                {
                    inbox.Status = InventoryEventInboxStatus.Applied;
                    inbox.AppliedAtUtc = nowUtc;
                    inbox.UpdatedAt = nowUtc;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return 0;
                }

                throw BuildInvariantException(
                    "RETURN_ALREADY_RECEIVED",
                    "This return request was already confirmed.");
            }

            var priorReceived = await _dbContext.CommerceHubReturnRequests
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item =>
                    item.OrderItemId == request.OrderItemId &&
                    item.Id != request.ReturnId &&
                    !item.IsDeleted &&
                    item.Status == CommerceHubReturnStatus.Confirmed)
                .SumAsync(
                    item => item.ReceivedResellableQuantity + item.ReceivedDamagedQuantity,
                    cancellationToken);

            if (priorReceived + totalReceived > request.DeliveredQuantity)
            {
                throw BuildInvariantException(
                    "RETURN_QUANTITY_EXCEEDS_DELIVERED",
                    "Returned quantity cannot exceed delivered quantity.");
            }

            var reservations = await LoadReservationsForUpdateAsync(
                request.TenantId,
                request.OrderItemId,
                cancellationToken);

            var returnableQuantity = reservations.Sum(reservation => reservation.RemainingDeliveredReturnableQuantity);
            if (returnableQuantity < totalReceived)
            {
                throw BuildInvariantException(
                    "RETURN_QUANTITY_EXCEEDS_DELIVERED",
                    "Returned quantity exceeds the delivered quantity remaining on this order item.");
            }

            var stocks = await LoadStocksForUpdateAsync(
                request.TenantId,
                request.VariantId,
                warehouseId: null,
                cancellationToken);

            await EnsureReturnStockLineageAuthorityAsync(
                request,
                reservations,
                stocks,
                cancellationToken);

            var stocksByWarehouse = stocks.ToDictionary(stock => stock.WarehouseId);
            var movements = new List<StockMovement>();
            var pendingChanges = new List<PendingCommerceHubStockSyncChange>();

            ApplyReturnedQuantity(
                reservations,
                stocksByWarehouse,
                request,
                request.ResellableQuantity,
                isDamaged: false,
                nowUtc,
                movements,
                pendingChanges);

            ApplyReturnedQuantity(
                reservations,
                stocksByWarehouse,
                request,
                request.DamagedQuantity,
                isDamaged: true,
                nowUtc,
                movements,
                pendingChanges);

            _dbContext.StockMovements.AddRange(movements);
            inbox.Status = InventoryEventInboxStatus.Applied;
            inbox.AppliedAtUtc = nowUtc;
            inbox.StockMovementId = movements.FirstOrDefault()?.Id;
            inbox.UpdatedAt = nowUtc;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await EnqueueStockSyncOutboxMessagesAsync(
                request.TenantId,
                pendingChanges,
                cancellationToken);
            return 0;
        }, cancellationToken);
    }

    private async Task EnsureReturnStockLineageAuthorityAsync(
        InventoryReturnReceiptRequest request,
        IReadOnlyCollection<InventoryReservation> reservations,
        IReadOnlyCollection<ProductVariantStock> stocks,
        CancellationToken cancellationToken)
    {
        var returnWarehouseIds = reservations
            .Where(reservation => reservation.RemainingDeliveredReturnableQuantity > 0m)
            .Select(reservation => reservation.WarehouseId)
            .Distinct()
            .ToHashSet();

        if (returnWarehouseIds.Count == 0)
        {
            return;
        }

        var relevantStocks = stocks
            .Where(stock => returnWarehouseIds.Contains(stock.WarehouseId))
            .ToList();

        var currentStocks = await ProductVariantInventoryLineageAuthority.FilterCurrentInventoryStockOwnersAsync(
            _dbContext,
            request.TenantId,
            relevantStocks,
            cancellationToken);

        var currentWarehouseIds = currentStocks
            .Select(stock => stock.WarehouseId)
            .ToHashSet();

        var staleReservation = reservations
            .Where(reservation => reservation.RemainingDeliveredReturnableQuantity > 0m)
            .OrderBy(reservation => reservation.CreatedAt)
            .FirstOrDefault(reservation => !currentWarehouseIds.Contains(reservation.WarehouseId));

        if (staleReservation != null)
        {
            throw BuildInvariantException(
                "INVENTORY_RETURN_STOCK_AUTHORITY_LOST",
                "CommerceHub return receipt cannot restore stock because SQL warehouse, variant, or stock ownership no longer proves current inventory authority.");
        }
    }

    private void ApplyReturnedQuantity(
        IReadOnlyList<InventoryReservation> reservations,
        IDictionary<Guid, ProductVariantStock> stocksByWarehouse,
        InventoryReturnReceiptRequest request,
        decimal quantity,
        bool isDamaged,
        DateTime nowUtc,
        IList<StockMovement> movements,
        IList<PendingCommerceHubStockSyncChange> pendingChanges)
    {
        if (quantity <= 0m)
        {
            return;
        }

        var remaining = quantity;

        foreach (var reservation in reservations
                     .OrderByDescending(item => item.RemainingDeliveredReturnableQuantity)
                     .ThenBy(item => item.CreatedAt))
        {
            if (remaining <= 0m)
            {
                break;
            }

            var allocatable = Math.Min(remaining, reservation.RemainingDeliveredReturnableQuantity);
            if (allocatable <= 0m)
            {
                continue;
            }

            if (!stocksByWarehouse.TryGetValue(reservation.WarehouseId, out var stock))
            {
                throw BuildInvariantException(
                    "INVENTORY_STOCK_NOT_FOUND",
                    "Delivered stock row was not found.");
            }

            var oldAvailable = stock.AvailableQuantity;
            if (isDamaged)
            {
                stock.DamagedQuantity += allocatable;
                reservation.DamagedReturnedQuantity += allocatable;
            }
            else
            {
                stock.Quantity += allocatable;
                reservation.ReturnedQuantity += allocatable;
            }
            var newAvailable = stock.AvailableQuantity;

            reservation.LastMutationEventId = request.EventId;
            reservation.Status = ResolveReservationStatus(reservation, nowUtc);
            reservation.UpdatedAt = nowUtc;

            movements.Add(CreateMovement(
                tenantId: request.TenantId,
                warehouseId: stock.WarehouseId,
                productId: request.ProductId,
                variantId: request.VariantId,
                movementType: isDamaged ? StockMovementType.Adjustment : StockMovementType.SalesReturn,
                quantityChange: isDamaged ? 0m : allocatable,
                reservedQuantityChange: 0m,
                damagedQuantityChange: isDamaged ? allocatable : 0m,
                stock: stock,
                referenceId: request.ReturnId,
                referenceType: "CommerceHubReturn",
                referenceLineId: request.OrderItemId,
                reservationId: reservation.Id,
                inventoryEventId: request.EventId,
                inventoryEventType: isDamaged ? InventoryEventType.StockDamaged : InventoryEventType.StockIncreased,
                correlationId: request.CorrelationId,
                source: request.Source,
                notes: isDamaged
                    ? "CommerceHub damaged return received."
                    : "CommerceHub resellable return received.",
                nowUtc: nowUtc));

            pendingChanges.Add(new PendingCommerceHubStockSyncChange(
                reservation.VariantId,
                stock.WarehouseId,
                oldAvailable,
                newAvailable,
                isDamaged ? "CommerceHub Damaged Return Received" : "CommerceHub Return Received",
                request.ReturnId,
                "CommerceHubReturn",
                request.CorrelationId));

            remaining -= allocatable;
        }

        if (remaining > 0m)
        {
            throw BuildInvariantException(
                "RETURN_QUANTITY_EXCEEDS_DELIVERED",
                "Returned quantity exceeds the delivered quantity remaining on this order item.");
        }
    }

    private async Task EnsureReservationRequestMatchesSqlCatalogAuthorityAsync(
        InventoryReservationRequest request,
        CancellationToken cancellationToken)
    {
        var authority = await (
            from product in _dbContext.Products.IgnoreQueryFilters()
            join variant in _dbContext.ProductVariants.IgnoreQueryFilters()
                on product.Id equals variant.ProductId
            where product.TenantId == request.TenantId &&
                  variant.TenantId == request.TenantId &&
                  product.Id == request.ProductId &&
                  variant.Id == request.VariantId &&
                  !product.IsDeleted &&
                  !variant.IsDeleted &&
                  variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                  variant.SemanticSupersededAtUtc == null &&
                  variant.SemanticReplacementVariantId == null
            select new
            {
                ProductActive = product.IsActive,
                ProductOnlineVisible = product.IsOnlineVisible,
                product.PublishStatus,
                VariantActive = variant.IsActive,
                product.TrackStock,
                product.AllowBackorder
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (authority == null ||
            !authority.ProductActive ||
            !authority.ProductOnlineVisible ||
            authority.PublishStatus != PublishStatus.Published ||
            !authority.VariantActive)
        {
            throw BuildInvariantException(
                "MARKETPLACE_RESERVATION_SQL_AUTHORITY_LOST",
                "Marketplace reservation cannot be created because SQL product or variant authority is no longer available.");
        }

        if (!authority.TrackStock || authority.AllowBackorder)
        {
            return;
        }
    }

    private async Task EnsureDeliveryCommitInventoryLineageAuthorityAsync(
        InventoryDeliveryCommitRequest request,
        CancellationToken cancellationToken)
    {
        var authorityFound = await (
            from product in _dbContext.Products.IgnoreQueryFilters()
            join variant in _dbContext.ProductVariants.IgnoreQueryFilters()
                on product.Id equals variant.ProductId
            where product.TenantId == request.TenantId &&
                  variant.TenantId == request.TenantId &&
                  product.Id == request.ProductId &&
                  variant.Id == request.VariantId &&
                  !product.IsDeleted &&
                  !variant.IsDeleted &&
                  product.IsActive &&
                  variant.IsActive &&
                  variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                  variant.SemanticSupersededAtUtc == null &&
                  variant.SemanticReplacementVariantId == null
            select 1)
            .AnyAsync(cancellationToken);

        if (!authorityFound)
        {
            throw BuildInvariantException(
                "MARKETPLACE_DELIVERY_INVENTORY_LINEAGE_LOST",
                "CommerceHub delivery cannot commit stock because current SQL variant or product inventory lineage no longer matches the order item.");
        }
    }

    private async Task EnsureMarketplaceReservationItemsMatchSqlCatalogAuthorityAsync(
        Guid marketplaceOrderId,
        IReadOnlyCollection<MarketplaceReservationAuthorityItem> expectedItems,
        CancellationToken cancellationToken)
    {
        var productIds = expectedItems.Select(item => item.ProductId).Distinct().ToList();
        var variantIds = expectedItems.Select(item => item.VariantId).Distinct().ToList();

        var catalogAuthority = await (
            from product in _dbContext.Products.IgnoreQueryFilters()
            join variant in _dbContext.ProductVariants.IgnoreQueryFilters()
                on product.Id equals variant.ProductId
            where productIds.Contains(product.Id) &&
                  variantIds.Contains(variant.Id) &&
                  !product.IsDeleted &&
                  !variant.IsDeleted &&
                  variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                  variant.SemanticSupersededAtUtc == null &&
                  variant.SemanticReplacementVariantId == null
            select new
            {
                ProductId = product.Id,
                VariantId = variant.Id,
                product.TenantId,
                ProductActive = product.IsActive,
                ProductOnlineVisible = product.IsOnlineVisible,
                product.PublishStatus,
                VariantActive = variant.IsActive
            })
            .ToListAsync(cancellationToken);

        var catalogByVariant = catalogAuthority.ToDictionary(
            item => item.VariantId,
            item => item);

        foreach (var item in expectedItems)
        {
            if (!catalogByVariant.TryGetValue(item.VariantId, out var authority) ||
                authority.ProductId != item.ProductId ||
                authority.TenantId != item.TenantId ||
                !authority.ProductActive ||
                !authority.ProductOnlineVisible ||
                authority.PublishStatus != PublishStatus.Published ||
                !authority.VariantActive)
            {
                throw new CommerceHubCheckoutException(
                    "MARKETPLACE_ORDER_SQL_AUTHORITY_LOST",
                    "Marketplace payment cannot be accepted because SQL product or variant authority no longer matches the order.",
                    StatusCodes.Status409Conflict,
                    new
                    {
                        MarketplaceOrderId = marketplaceOrderId,
                        item.TenantId,
                        item.OrderItemId,
                        item.ProductId,
                        item.VariantId
                    });
            }
        }
    }

    private static void EnsureHistoricalReservationsMatchCurrentOrderAuthority(
        Guid marketplaceOrderId,
        IReadOnlyCollection<MarketplaceReservationAuthorityItem> expectedItems,
        IReadOnlyCollection<InventoryReservation> reservations)
    {
        var expectedByOrderItem = expectedItems.ToDictionary(item => item.OrderItemId);
        if (reservations.Count == 0 || expectedItems.Any(item => reservations.All(reservation => reservation.OrderItemId != item.OrderItemId)))
        {
            throw new CommerceHubCheckoutException(
                "MARKETPLACE_RESERVATION_HISTORY_MISSING",
                "Marketplace inventory cannot be reacquired because the original SQL reservation evidence is unavailable.",
                StatusCodes.Status409Conflict,
                new { MarketplaceOrderId = marketplaceOrderId });
        }

        foreach (var reservation in reservations)
        {
            if (!expectedByOrderItem.TryGetValue(reservation.OrderItemId, out var item) ||
                reservation.TenantId != item.TenantId ||
                reservation.SubOrderId != item.SubOrderId ||
                reservation.ProductId != item.ProductId ||
                reservation.VariantId != item.VariantId)
            {
                throw new CommerceHubCheckoutException(
                    "MARKETPLACE_RESERVATION_HISTORY_CHANGED",
                    "Marketplace inventory cannot be reacquired because current order product or variant identity differs from original reservation authority.",
                    StatusCodes.Status409Conflict,
                    new
                    {
                        MarketplaceOrderId = marketplaceOrderId,
                        reservation.OrderItemId,
                        reservation.ProductId,
                        reservation.VariantId
                    });
            }
        }

        foreach (var item in expectedItems)
        {
            var itemReservations = reservations
                .Where(reservation => reservation.OrderItemId == item.OrderItemId)
                .OrderBy(reservation => reservation.CreatedAt)
                .ThenBy(reservation => reservation.Id)
                .ToList();
            var originalEventId = itemReservations[0].ReservationEventId;
            var originalQuantity = itemReservations
                .Where(reservation => reservation.ReservationEventId == originalEventId)
                .Sum(reservation => reservation.ReservedQuantity);

            if (originalEventId == Guid.Empty || originalQuantity != item.RequiredQuantity)
            {
                throw new CommerceHubCheckoutException(
                    "MARKETPLACE_RESERVATION_QUANTITY_CHANGED",
                    "Marketplace inventory cannot be reacquired because current order quantity differs from original reservation authority.",
                    StatusCodes.Status409Conflict,
                    new
                    {
                        MarketplaceOrderId = marketplaceOrderId,
                        item.OrderItemId,
                        OriginalQuantity = originalQuantity,
                        CurrentQuantity = item.RequiredQuantity
                    });
            }
        }
    }

    private async Task EnsureMarketplaceOrderShippingAuthorityActiveAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken)
    {
        var shippingSelections = await _dbContext.CommerceHubSubOrders
            .IgnoreQueryFilters()
            .Where(subOrder =>
                subOrder.MarketplaceOrderId == marketplaceOrderId &&
                !subOrder.IsDeleted &&
                subOrder.Status != OrderStatus.Cancelled &&
                subOrder.Status != OrderStatus.Refunded &&
                subOrder.ShippingMethodId.HasValue)
            .Select(subOrder => new
            {
                subOrder.Id,
                subOrder.StoreId,
                ShippingMethodId = subOrder.ShippingMethodId!.Value
            })
            .ToListAsync(cancellationToken);

        if (shippingSelections.Count == 0)
        {
            return;
        }

        var shippingIds = shippingSelections.Select(selection => selection.ShippingMethodId).Distinct().ToList();
        var storeIds = shippingSelections.Select(selection => selection.StoreId).Distinct().ToList();
        var activeMethods = await _dbContext.ShippingMethods
            .IgnoreQueryFilters()
            .Where(method =>
                shippingIds.Contains(method.Id) &&
                storeIds.Contains(method.TenantId) &&
                !method.IsDeleted &&
                method.IsActive)
            .Select(method => new { method.Id, method.TenantId })
            .ToListAsync(cancellationToken);

        var activeKeys = activeMethods
            .Select(method => (method.TenantId, method.Id))
            .ToHashSet();

        foreach (var selection in shippingSelections)
        {
            if (activeKeys.Contains((selection.StoreId, selection.ShippingMethodId)))
            {
                continue;
            }

            throw new CommerceHubCheckoutException(
                "MARKETPLACE_SHIPPING_AUTHORITY_LOST",
                "Marketplace payment cannot be accepted because the selected shipping method is no longer available.",
                StatusCodes.Status409Conflict,
                new
                {
                    MarketplaceOrderId = marketplaceOrderId,
                    SubOrderId = selection.Id,
                    selection.StoreId,
                    selection.ShippingMethodId
                });
        }
    }

    private async Task EnsureReservedStockRowsCoverMarketplaceReservationsAsync(
        Guid marketplaceOrderId,
        IReadOnlyCollection<InventoryReservation> activeReservations,
        CancellationToken cancellationToken)
    {
        var requiredByStock = activeReservations
            .GroupBy(reservation => new
            {
                reservation.TenantId,
                reservation.VariantId,
                reservation.WarehouseId
            })
            .ToList();

        foreach (var group in requiredByStock)
        {
            var requiredReservedQuantity = group.Sum(reservation => reservation.RemainingReservedQuantity);
            var stock = await LoadStockForUpdateAsync(
                group.Key.TenantId,
                group.Key.VariantId,
                group.Key.WarehouseId,
                cancellationToken);

            if (stock == null || stock.ReservedQuantity < requiredReservedQuantity)
            {
                throw new CommerceHubCheckoutException(
                    "MARKETPLACE_ORDER_STOCK_AUTHORITY_LOST",
                    "Marketplace payment cannot be accepted because SQL stock no longer backs the active reservation.",
                    StatusCodes.Status409Conflict,
                    new
                    {
                        MarketplaceOrderId = marketplaceOrderId,
                        group.Key.TenantId,
                        group.Key.VariantId,
                        group.Key.WarehouseId,
                        RequiredReservedQuantity = requiredReservedQuantity,
                        StockReservedQuantity = stock?.ReservedQuantity ?? 0m
                    });
            }
        }
    }

    private async Task EnsureDeliveryCommitSqlAuthorityAsync(
        InventoryDeliveryCommitRequest request,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var authority = await _dbContext.CommerceHubSubOrderItems
            .IgnoreQueryFilters()
            .Where(item => item.Id == request.OrderItemId)
            .Select(item => new
            {
                ItemDeleted = item.IsDeleted,
                item.SubOrderId,
                item.ProductId,
                item.VariantId,
                item.Quantity,
                ItemStatus = item.Status,
                SubOrderDeleted = item.SubOrder.IsDeleted,
                item.SubOrder.StoreId,
                item.SubOrder.MarketplaceOrderId,
                SubOrderStatus = item.SubOrder.Status,
                item.SubOrder.PaymentMethod,
                item.SubOrder.PaymentStatus,
                OrderDeleted = item.SubOrder.MarketplaceOrder.IsDeleted,
                OrderStatus = item.SubOrder.MarketplaceOrder.Status,
                ShippingMethodId = item.SubOrder.ShippingMethodId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (authority == null ||
            authority.ItemDeleted ||
            authority.SubOrderDeleted ||
            authority.OrderDeleted ||
            authority.StoreId != request.TenantId ||
            authority.MarketplaceOrderId != request.OrderId ||
            (request.SubOrderId.HasValue && authority.SubOrderId != request.SubOrderId.Value) ||
            authority.ProductId != request.ProductId ||
            authority.VariantId != request.VariantId ||
            authority.Quantity < request.Quantity ||
            authority.ItemStatus == CommerceHubOrderItemStatus.Cancelled ||
            authority.SubOrderStatus is OrderStatus.Cancelled or OrderStatus.Refunded ||
            authority.OrderStatus is OrderStatus.Cancelled or OrderStatus.Refunded)
        {
            throw BuildInvariantException(
                "MARKETPLACE_DELIVERY_SQL_AUTHORITY_LOST",
                "CommerceHub delivery cannot commit stock because SQL order, item, or fulfillment authority no longer matches the request.");
        }

        if (authority.PaymentMethod != CommerceHubPaymentMethod.CashOnDelivery &&
            authority.PaymentStatus is not (PaymentStatus.Collected or PaymentStatus.Settled))
        {
            throw BuildInvariantException(
                "MARKETPLACE_DELIVERY_PAYMENT_AUTHORITY_LOST",
                "CommerceHub delivery cannot commit stock before SQL payment authority allows fulfillment.");
        }

        await EnsureDeliveryCommitInventoryLineageAuthorityAsync(
            request,
            cancellationToken);

        if (authority.ShippingMethodId.HasValue)
        {
            var shippingActive = await _dbContext.ShippingMethods
                .IgnoreQueryFilters()
                .AnyAsync(method =>
                    method.Id == authority.ShippingMethodId.Value &&
                    method.TenantId == request.TenantId &&
                    !method.IsDeleted &&
                    method.IsActive,
                    cancellationToken);

            if (!shippingActive)
            {
                throw BuildInvariantException(
                    "MARKETPLACE_DELIVERY_SHIPPING_AUTHORITY_LOST",
                    "CommerceHub delivery cannot commit stock because SQL shipping eligibility no longer matches the order.");
            }
        }
    }

    private async Task<List<ProductVariantStock>> LoadStocksForUpdateAsync(
        Guid tenantId,
        Guid variantId,
        Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        IQueryable<ProductVariantStock> query;

        if (_dbContext.Database.IsRelational())
        {
            query = warehouseId.HasValue
                ? _dbContext.ProductVariantStocks.FromSqlInterpolated($@"
                    SELECT * FROM ""ProductVariantStocks""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""VariantId"" = {variantId}
                      AND ""WarehouseId"" = {warehouseId.Value}
                      AND ""IsDeleted"" = FALSE
                    ORDER BY ""WarehouseId"", ""Id""
                    FOR UPDATE").IgnoreQueryFilters()
                : _dbContext.ProductVariantStocks.FromSqlInterpolated($@"
                    SELECT * FROM ""ProductVariantStocks""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""VariantId"" = {variantId}
                      AND ""IsDeleted"" = FALSE
                    ORDER BY ""WarehouseId"", ""Id""
                    FOR UPDATE").IgnoreQueryFilters();
        }
        else
        {
            query = _dbContext.ProductVariantStocks
                .IgnoreQueryFilters()
                .Where(stock =>
                stock.TenantId == tenantId &&
                stock.VariantId == variantId &&
                !stock.IsDeleted &&
                (!warehouseId.HasValue || stock.WarehouseId == warehouseId.Value));
        }

        return await query
            .OrderBy(stock => stock.WarehouseId)
            .ThenBy(stock => stock.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<ProductVariantStock?> LoadStockForUpdateAsync(
        Guid tenantId,
        Guid variantId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        return (await LoadStocksForUpdateAsync(
                tenantId,
                variantId,
                warehouseId,
                cancellationToken))
            .FirstOrDefault();
    }

    private async Task<List<InventoryReservation>> LoadReservationsForUpdateAsync(
        Guid tenantId,
        Guid orderItemId,
        CancellationToken cancellationToken)
    {
        IQueryable<InventoryReservation> query = _dbContext.Database.IsRelational()
            ? _dbContext.InventoryReservations.FromSqlInterpolated($@"
                SELECT * FROM ""InventoryReservations""
                WHERE ""TenantId"" = {tenantId}
                  AND ""OrderItemId"" = {orderItemId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE").IgnoreQueryFilters()
            : _dbContext.InventoryReservations
                .IgnoreQueryFilters()
                .Where(item =>
                item.TenantId == tenantId &&
                item.OrderItemId == orderItemId &&
                !item.IsDeleted);

        return await query
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private async Task<InventoryReservation?> LoadReservationForUpdateAsync(
        Guid tenantId,
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        IQueryable<InventoryReservation> query = _dbContext.Database.IsRelational()
            ? _dbContext.InventoryReservations.FromSqlInterpolated($@"
                SELECT * FROM ""InventoryReservations""
                WHERE ""TenantId"" = {tenantId}
                  AND ""Id"" = {reservationId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE").IgnoreQueryFilters()
            : _dbContext.InventoryReservations
                .IgnoreQueryFilters()
                .Where(item =>
                    item.TenantId == tenantId &&
                    item.Id == reservationId &&
                    !item.IsDeleted);

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<List<InventoryReservation>> LoadMarketplaceOrderReservationsForUpdateAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken)
    {
        IQueryable<InventoryReservation> query;

        if (_dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            query = _dbContext.InventoryReservations.FromSqlInterpolated($@"
                SELECT * FROM ""InventoryReservations""
                WHERE ""OrderId"" = {marketplaceOrderId}
                  AND ""IsDeleted"" = FALSE
                  AND ""ReservedQuantity"" > ""CommittedQuantity"" + ""ReleasedQuantity""
                FOR UPDATE").IgnoreQueryFilters();
        }
        else if (_dbContext.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
        {
            query = _dbContext.InventoryReservations.FromSqlInterpolated($@"
                SELECT * FROM [InventoryReservations] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [OrderId] = {marketplaceOrderId}
                  AND [IsDeleted] = 0
                  AND [ReservedQuantity] > [CommittedQuantity] + [ReleasedQuantity]").IgnoreQueryFilters();
        }
        else
        {
            query = _dbContext.InventoryReservations
                .IgnoreQueryFilters()
                .Where(item =>
                    item.OrderId == marketplaceOrderId &&
                    !item.IsDeleted &&
                    item.ReservedQuantity > item.CommittedQuantity + item.ReleasedQuantity);
        }

        return await query
            .OrderBy(item => item.TenantId)
            .ThenBy(item => item.OrderItemId)
            .ThenBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<InventoryReservation>> LoadMarketplaceOrderReservationHistoryForUpdateAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken)
    {
        IQueryable<InventoryReservation> query;

        if (_dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            query = _dbContext.InventoryReservations.FromSqlInterpolated($@"
                SELECT * FROM ""InventoryReservations""
                WHERE ""OrderId"" = {marketplaceOrderId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE").IgnoreQueryFilters();
        }
        else if (_dbContext.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
        {
            query = _dbContext.InventoryReservations.FromSqlInterpolated($@"
                SELECT * FROM [InventoryReservations] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [OrderId] = {marketplaceOrderId}
                  AND [IsDeleted] = 0").IgnoreQueryFilters();
        }
        else
        {
            query = _dbContext.InventoryReservations
                .IgnoreQueryFilters()
                .Where(item => item.OrderId == marketplaceOrderId && !item.IsDeleted);
        }

        return await query
            .OrderBy(item => item.TenantId)
            .ThenBy(item => item.OrderItemId)
            .ThenBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<CommerceHubMarketplaceOrder?> LoadMarketplaceOrderForReservationReacquisitionAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken)
    {
        IQueryable<CommerceHubMarketplaceOrder> query;

        if (_dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            query = _dbContext.CommerceHubMarketplaceOrders.FromSqlInterpolated($@"
                SELECT * FROM ""CommerceHubMarketplaceOrders""
                WHERE ""Id"" = {marketplaceOrderId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE").IgnoreQueryFilters();
        }
        else if (_dbContext.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
        {
            query = _dbContext.CommerceHubMarketplaceOrders.FromSqlInterpolated($@"
                SELECT * FROM [CommerceHubMarketplaceOrders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [Id] = {marketplaceOrderId}
                  AND [IsDeleted] = 0").IgnoreQueryFilters();
        }
        else
        {
            query = _dbContext.CommerceHubMarketplaceOrders
                .IgnoreQueryFilters()
                .Where(order => order.Id == marketplaceOrderId && !order.IsDeleted);
        }

        return await query.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<CommerceHubReturnRequest?> LoadReturnRequestForUpdateAsync(
        Guid returnId,
        CancellationToken cancellationToken)
    {
        IQueryable<CommerceHubReturnRequest> query = _dbContext.Database.IsRelational()
            ? _dbContext.CommerceHubReturnRequests.FromSqlInterpolated($@"
                SELECT * FROM ""CommerceHubReturnRequests""
                WHERE ""Id"" = {returnId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE").IgnoreQueryFilters()
            : _dbContext.CommerceHubReturnRequests
                .IgnoreQueryFilters()
                .Where(item => item.Id == returnId && !item.IsDeleted);

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task EnqueueStockSyncOutboxMessagesAsync(
        Guid tenantId,
        IReadOnlyCollection<PendingCommerceHubStockSyncChange> pendingChanges,
        CancellationToken cancellationToken)
    {
        if (pendingChanges.Count == 0)
        {
            return;
        }

        await TouchVariantStockTimestampsAsync(
            tenantId,
            pendingChanges.Select(change => change.VariantId),
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var variantIds = pendingChanges
            .Select(change => change.VariantId)
            .Distinct()
            .ToList();
        var warehouseIds = pendingChanges
            .Select(change => change.WarehouseId)
            .Distinct()
            .ToList();

        var totalStockByVariant = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .WhereCurrentInventoryStockOwner(tenantId)
            .Where(stock => variantIds.Contains(stock.VariantId))
            .GroupBy(stock => stock.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                TotalAvailable = Math.Max(0, group.Sum(stock => stock.Quantity - stock.ReservedQuantity))
            })
            .ToDictionaryAsync(item => item.VariantId, item => item.TotalAvailable, cancellationToken);

        var variantMetadata = await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .Where(variant => variant.TenantId == tenantId && variantIds.Contains(variant.Id))
            .Select(variant => new
            {
                variant.Id,
                variant.ProductId,
                variant.SKU,
                variant.StockUpdatedAtUtc,
                variant.ProjectionMutationVersion
            })
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var warehouseMetadata = await _dbContext.Warehouses
            .IgnoreQueryFilters()
            .Where(warehouse => warehouse.TenantId == tenantId && warehouseIds.Contains(warehouse.Id))
            .Select(warehouse => new
            {
                warehouse.Id,
                warehouse.Name
            })
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);

        foreach (var change in pendingChanges)
        {
            if (!variantMetadata.TryGetValue(change.VariantId, out var variant))
            {
                throw BuildInvariantException(
                    "INVENTORY_VARIANT_NOT_FOUND",
                    $"Variant {change.VariantId} metadata was not found for stock sync.");
            }

            var stockUpdatedAtUtc = NormalizeUtcTimestamp(
                variant.StockUpdatedAtUtc
                    ?? throw BuildInvariantException(
                        "INVENTORY_STOCK_SYNC_VERSION_MISSING",
                        $"Variant {change.VariantId} is missing StockUpdatedAtUtc."));

            var stockEvent = new ProductVariantStockChangedEvent
            {
                TenantId = tenantId,
                VariantId = change.VariantId,
                ProductId = variant.ProductId,
                SKU = variant.SKU,
                WarehouseId = change.WarehouseId,
                WarehouseName = warehouseMetadata.GetValueOrDefault(change.WarehouseId),
                OldQuantity = change.OldQuantity,
                NewQuantity = change.NewQuantity,
                Reason = change.Reason,
                ReferenceId = change.ReferenceId,
                ReferenceType = change.ReferenceType,
                MutationSource = MutationSources.CommerceHub,
                TotalStockQuantity = totalStockByVariant.GetValueOrDefault(change.VariantId),
                CorrelationId = change.CorrelationId ?? change.ReferenceId?.ToString("N") ?? Guid.NewGuid().ToString("N"),
                StockUpdatedAtUtc = stockUpdatedAtUtc,
                EventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion)
            };

            var payload = JsonSerializer.Serialize(stockEvent, typeof(ProductVariantStockChangedEvent));
            _dbContext.StockSyncOutboxMessages.Add(StockSyncOutboxMessage.Create(stockEvent, payload));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task TouchVariantStockTimestampsAsync(
        Guid tenantId,
        IEnumerable<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        foreach (var variantId in variantIds.Distinct().OrderBy(id => id))
        {
            var variant = await LoadVariantForStockSyncAsync(tenantId, variantId, cancellationToken)
                ?? throw BuildInvariantException(
                    "INVENTORY_VARIANT_NOT_FOUND",
                    $"Variant {variantId} was not found for stock sync.");

            variant.StockUpdatedAtUtc = StockProjectionTimestamp.Next(variant.StockUpdatedAtUtc);
            variant.UpdatedAt = variant.StockUpdatedAtUtc;
        }
    }

    private static DateTime NormalizeUtcTimestamp(DateTime value)
    {
        return StockProjectionTimestamp.NormalizeUtc(value);
    }

    private async Task<ProductVariant?> LoadVariantForStockSyncAsync(
        Guid tenantId,
        Guid variantId,
        CancellationToken cancellationToken)
    {
        IQueryable<ProductVariant> query;

        if (_dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            query = _dbContext.ProductVariants.FromSqlInterpolated($@"
                SELECT *, xmin FROM ""ProductVariants""
                WHERE ""TenantId"" = {tenantId}
                  AND ""Id"" = {variantId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE").IgnoreQueryFilters();
        }
        else if (_dbContext.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
        {
            query = _dbContext.ProductVariants.FromSqlInterpolated($@"
                SELECT * FROM [ProductVariants] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [TenantId] = {tenantId}
                  AND [Id] = {variantId}
                  AND [IsDeleted] = 0").IgnoreQueryFilters();
        }
        else
        {
            query = _dbContext.ProductVariants
                .IgnoreQueryFilters()
                .Where(item => item.TenantId == tenantId && item.Id == variantId && !item.IsDeleted);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<EventStartResult> TryBeginEventAsync(
        Guid tenantId,
        Guid eventId,
        InventoryEventType eventType,
        Guid? referenceId,
        Guid? referenceLineId,
        string? payloadHash,
        string? correlationId,
        string source,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var existing = await _dbContext.InventoryEventInbox
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                inbox => inbox.TenantId == tenantId && inbox.EventId == eventId,
                cancellationToken);

        if (existing != null)
        {
            EnsureInboxReplayMatchesOriginal(
                existing,
                eventType,
                referenceId,
                referenceLineId,
                payloadHash);

            existing.ReplayCount += 1;
            existing.LastReceivedAtUtc = nowUtc;
            existing.UpdatedAt = nowUtc;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new EventStartResult(existing, AlreadyHandled: existing.Status == InventoryEventInboxStatus.Applied);
        }

        var inbox = new InventoryEventInbox
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EventId = eventId,
            EventType = eventType,
            ReferenceId = referenceId,
            ReferenceLineId = referenceLineId,
            PayloadHash = payloadHash,
            FirstReceivedAtUtc = nowUtc,
            LastReceivedAtUtc = nowUtc,
            Status = InventoryEventInboxStatus.Pending,
            CorrelationId = correlationId,
            IdempotencyKey = eventId.ToString("N"),
            Source = source,
            CreatedAt = nowUtc
        };

        _dbContext.InventoryEventInbox.Add(inbox);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new EventStartResult(inbox, AlreadyHandled: false);
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(inbox).State = EntityState.Detached;

            existing = await _dbContext.InventoryEventInbox
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    item => item.TenantId == tenantId && item.EventId == eventId,
                    cancellationToken);

            if (existing == null)
            {
                throw;
            }

            EnsureInboxReplayMatchesOriginal(
                existing,
                eventType,
                referenceId,
                referenceLineId,
                payloadHash);

            existing.ReplayCount += 1;
            existing.LastReceivedAtUtc = nowUtc;
            existing.UpdatedAt = nowUtc;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new EventStartResult(existing, AlreadyHandled: existing.Status == InventoryEventInboxStatus.Applied);
        }
    }

    private static void EnsureInboxReplayMatchesOriginal(
        InventoryEventInbox existing,
        InventoryEventType incomingEventType,
        Guid? incomingReferenceId,
        Guid? incomingReferenceLineId,
        string? incomingPayloadHash)
    {
        if (string.IsNullOrWhiteSpace(existing.PayloadHash) &&
            !string.IsNullOrWhiteSpace(incomingPayloadHash))
        {
            existing.PayloadHash = incomingPayloadHash;
        }

        var payloadMatches = string.Equals(
            existing.PayloadHash,
            incomingPayloadHash,
            StringComparison.Ordinal);

        if (existing.EventType == incomingEventType &&
            existing.ReferenceId == incomingReferenceId &&
            existing.ReferenceLineId == incomingReferenceLineId &&
            payloadMatches)
        {
            return;
        }

        throw new InvalidOperationException(
            $"INVENTORY_EVENT_ID_PAYLOAD_CONFLICT: EventId {existing.EventId} was replayed with a different event type, reference, or payload hash. " +
            $"Original=({existing.EventType}, {existing.ReferenceId}, {existing.ReferenceLineId}, {existing.PayloadHash}); " +
            $"Incoming=({incomingEventType}, {incomingReferenceId}, {incomingReferenceLineId}, {incomingPayloadHash}).");
    }

    private async Task MarkEventFailedAsync(
        InventoryEventInbox inbox,
        CancellationToken cancellationToken)
    {
        inbox.Status = InventoryEventInboxStatus.Failed;
        inbox.AppliedAtUtc = null;
        inbox.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static StockMovement CreateMovement(
        Guid tenantId,
        Guid warehouseId,
        Guid productId,
        Guid variantId,
        StockMovementType movementType,
        decimal quantityChange,
        decimal reservedQuantityChange,
        decimal damagedQuantityChange,
        ProductVariantStock stock,
        Guid? referenceId,
        string referenceType,
        Guid? referenceLineId,
        Guid? reservationId,
        Guid inventoryEventId,
        InventoryEventType inventoryEventType,
        string? correlationId,
        string source,
        string notes,
        DateTime nowUtc)
    {
        return new StockMovement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarehouseId = warehouseId,
            ProductId = productId,
            ProductVariantId = variantId,
            MovementType = movementType,
            QuantityChange = quantityChange,
            ReservedQuantityChange = reservedQuantityChange,
            DamagedQuantityChange = damagedQuantityChange,
            QuantityAfter = stock.Quantity,
            AvailableAfter = stock.AvailableQuantity,
            ReservedAfter = stock.ReservedQuantity,
            DamagedAfter = stock.DamagedQuantity,
            ReferenceId = referenceId,
            ReferenceType = referenceType,
            ReferenceLineId = referenceLineId,
            ReservationId = reservationId,
            InventoryEventId = inventoryEventId,
            InventoryEventType = inventoryEventType,
            CorrelationId = correlationId,
            IdempotencyKey = inventoryEventId.ToString("N"),
            Source = source,
            Notes = notes,
            CreatedAt = nowUtc
        };
    }

    private async Task<T> ExecuteInTransactionAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        var ownsTransaction = _dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction == null;

        if (!ownsTransaction)
        {
            return await action();
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static InventoryReservationStatus ResolveReservationStatus(
        InventoryReservation reservation,
        DateTime nowUtc)
    {
        if (reservation.ExpiresAtUtc.HasValue &&
            reservation.ExpiresAtUtc.Value <= nowUtc &&
            reservation.RemainingReservedQuantity > 0m)
        {
            return InventoryReservationStatus.Expired;
        }

        var totalReturned = reservation.ReturnedQuantity + reservation.DamagedReturnedQuantity;
        if (reservation.CommittedQuantity > 0m &&
            totalReturned >= reservation.CommittedQuantity)
        {
            return InventoryReservationStatus.Returned;
        }

        if (reservation.CommittedQuantity > 0m && totalReturned > 0m)
        {
            return InventoryReservationStatus.PartiallyReturned;
        }

        if (reservation.ReservedQuantity > 0m &&
            reservation.ReleasedQuantity >= reservation.ReservedQuantity &&
            reservation.CommittedQuantity <= 0m)
        {
            return InventoryReservationStatus.Released;
        }

        if (reservation.ReleasedQuantity > 0m && reservation.CommittedQuantity <= 0m)
        {
            return InventoryReservationStatus.PartiallyReleased;
        }

        if (reservation.CommittedQuantity >= reservation.ReservedQuantity && reservation.CommittedQuantity > 0m)
        {
            return InventoryReservationStatus.Committed;
        }

        if (reservation.CommittedQuantity > 0m)
        {
            return InventoryReservationStatus.PartiallyCommitted;
        }

        return InventoryReservationStatus.Active;
    }

    private static CommerceHubCheckoutException BuildInvariantException(string code, string message)
    {
        return new CommerceHubCheckoutException(
            code,
            message,
            StatusCodes.Status409Conflict);
    }

    private static string BuildPayloadHash(params object?[] parts)
    {
        var payload = string.Join("|", parts.Select(part => part?.ToString() ?? string.Empty));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private static int DecimalToInt(decimal value) => decimal.ToInt32(decimal.Truncate(value));

    private sealed record EventStartResult(InventoryEventInbox? Inbox, bool AlreadyHandled);
    private sealed record PendingCommerceHubStockSyncChange(
        Guid VariantId,
        Guid WarehouseId,
        decimal OldQuantity,
        decimal NewQuantity,
        string Reason,
        Guid? ReferenceId,
        string? ReferenceType,
        string? CorrelationId);
}
