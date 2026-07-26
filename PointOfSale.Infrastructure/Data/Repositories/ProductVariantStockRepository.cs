using System.Text.Json;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;

namespace PointOfSale.Infrastructure.Data.Repositories;

public interface IProductVariantStockRepository
{
    Task<bool> ReserveAsync(
        Guid tenantId,
        Guid variantId,
        Guid? warehouseId,
        decimal quantity,
        Guid orderId,
        DateTime? expiresAtUtc = null,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(
        Guid tenantId,
        Guid variantId,
        Guid? warehouseId,
        decimal quantity,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseOrderReservationsAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<decimal> GetAvailableQuantityAsync(
        Guid tenantId,
        Guid variantId,
        CancellationToken cancellationToken = default);

    Task<Dictionary<Guid, decimal>> GetAvailableQuantitiesAsync(
        Guid tenantId,
        List<Guid> variantIds,
        CancellationToken cancellationToken = default);

    Task CommitReservationAsync(
        Guid tenantId,
        Guid variantId,
        Guid? warehouseId,
        decimal quantity,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task CommitOrderReservationsAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task BindOrderReservationsAsync(
        Guid tenantId,
        Guid orderId,
        IReadOnlyCollection<OrderItem> orderItems,
        CancellationToken cancellationToken = default);
}

public class ProductVariantStockRepository : IProductVariantStockRepository
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ProductVariantStockRepository> _logger;

    public ProductVariantStockRepository(
        ApplicationDbContext dbContext,
        ILogger<ProductVariantStockRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> ReserveAsync(
        Guid tenantId,
        Guid variantId,
        Guid? warehouseId,
        decimal quantity,
        Guid orderId,
        DateTime? expiresAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0m)
        {
            _logger.LogWarning(
                "ReserveStock rejected - invalid quantity | TenantId: {TenantId} | VariantId: {VariantId} | Quantity: {Quantity} | OrderId: {OrderId}",
                tenantId, variantId, quantity, orderId);
            return false;
        }

        if (!await ProductVariantInventoryLineageAuthority.IsVariantReservableForInventoryAsync(
                _dbContext,
                tenantId,
                variantId,
                cancellationToken))
        {
            _logger.LogWarning(
                "ReserveStock rejected - variant is not current inventory lineage owner | TenantId: {TenantId} | VariantId: {VariantId} | Quantity: {Quantity} | OrderId: {OrderId}",
                tenantId, variantId, quantity, orderId);
            return false;
        }

        var reserved = false;

        await ExecuteWithinMutationTransactionAsync(async () =>
        {
            var nowUtc = DateTime.UtcNow;
            var pendingChanges = new List<PendingRepositoryStockSyncChange>();
            var variantProductId = await LoadVariantProductIdAsync(tenantId, variantId, cancellationToken);
            var stocks = await LoadStocksForMutationAsync(
                tenantId,
                variantId,
                warehouseId,
                cancellationToken);
            stocks = await ProductVariantInventoryLineageAuthority.FilterCurrentInventoryStockOwnersAsync(
                _dbContext,
                tenantId,
                stocks,
                cancellationToken);

            if (!warehouseId.HasValue)
            {
                // PRODUCTION HARDENING: Ensure deterministic lock order by WarehouseId
                // to prevent deadlocks across concurrent reservations.
                // LoadStocksForMutationAsync already acquires locks in deterministic order,
                // so we must NOT re-order by AvailableQuantity which would break lock ordering.
                // Instead, keep the deterministic WarehouseId order from the query.
                stocks = stocks
                    .OrderBy(stock => stock.WarehouseId)
                    .ThenBy(stock => stock.Id)
                    .ToList();
            }

            var totalAvailable = stocks.Sum(stock => stock.AvailableQuantity);
            if (totalAvailable < quantity)
            {
                reserved = false;
                return;
            }

            var remainingToReserve = quantity;

            foreach (var stock in stocks)
            {
                if (remainingToReserve <= 0m)
                {
                    break;
                }

                var reservable = Math.Min(stock.AvailableQuantity, remainingToReserve);
                if (reservable <= 0m)
                {
                    continue;
                }

                var oldAvailable = stock.AvailableQuantity;
                var reservationEventId = Guid.NewGuid();
                stock.ReservedQuantity += reservable;
                stock.UpdatedAt = nowUtc;
                var newAvailable = stock.AvailableQuantity;

                var reservation = new InventoryReservation
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    OrderId = orderId,
                    OrderItemId = Guid.Empty,
                    ProductId = variantProductId,
                    VariantId = variantId,
                    WarehouseId = stock.WarehouseId,
                    ReservedQuantity = reservable,
                    Status = InventoryReservationStatus.Active,
                    ReservationEventId = reservationEventId,
                    LastMutationEventId = reservationEventId,
                    ExpiresAtUtc = expiresAtUtc,
                    CreatedAt = nowUtc
                };

                _dbContext.InventoryReservations.Add(reservation);
                AddReservationMovement(
                    tenantId,
                    stock,
                    reservation,
                    quantityChange: 0m,
                    reservedQuantityChange: reservable,
                    InventoryEventType.StockReserved,
                    orderId,
                    "Order",
                    "Checkout stock reserved.",
                    nowUtc);

                pendingChanges.Add(new PendingRepositoryStockSyncChange(
                    stock.VariantId,
                    stock.WarehouseId,
                    oldAvailable,
                    newAvailable,
                    "Checkout Reservation",
                    orderId,
                    "Order"));

                remainingToReserve -= reservable;
            }

            if (remainingToReserve > 0m)
            {
                throw new InvalidOperationException(
                    $"Unable to reserve full quantity. Requested: {quantity}, Reserved: {quantity - remainingToReserve}, Remaining: {remainingToReserve}. " +
                    $"TenantId: {tenantId}, VariantId: {variantId}, OrderId: {orderId}");
            }

            var invalidStock = stocks.FirstOrDefault(stock => stock.ReservedQuantity < 0m || stock.ReservedQuantity > stock.Quantity);
            if (invalidStock != null)
            {
                throw new InvalidOperationException(
                    $"Reservation invariant violated before save. TenantId: {tenantId}, VariantId: {variantId}, OrderId: {orderId}, " +
                    $"WarehouseId: {invalidStock.WarehouseId}, Quantity: {invalidStock.Quantity}, ReservedQuantity: {invalidStock.ReservedQuantity}");
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await EnqueueStockSyncOutboxMessagesAsync(tenantId, pendingChanges, cancellationToken);
            reserved = true;
        }, cancellationToken);

        if (reserved)
        {
            _logger.LogInformation(
                "ReserveStock success | TenantId: {TenantId} | VariantId: {VariantId} | Quantity: {Quantity} | OrderId: {OrderId}",
                tenantId, variantId, quantity, orderId);
        }
        else
        {
            _logger.LogWarning(
                "ReserveStock failed - insufficient stock | TenantId: {TenantId} | VariantId: {VariantId} | Quantity: {Quantity} | OrderId: {OrderId}",
                tenantId, variantId, quantity, orderId);
        }

        return reserved;
    }

    public async Task ReleaseAsync(
        Guid tenantId,
        Guid variantId,
        Guid? warehouseId,
        decimal quantity,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0m)
        {
            return;
        }

        await ExecuteWithinMutationTransactionAsync(async () =>
        {
            var reservations = await LoadReservationsForMutationAsync(
                tenantId,
                orderId,
                variantId,
                warehouseId,
                cancellationToken);
            var pendingChanges = new List<PendingRepositoryStockSyncChange>();

            var alreadyReleased = reservations.Sum(reservation => reservation.ReleasedQuantity);
            var remainingToRelease = Math.Max(0m, quantity - alreadyReleased);
            if (remainingToRelease <= 0m)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var stockCache = new Dictionary<(Guid VariantId, Guid WarehouseId), ProductVariantStock>();

            foreach (var reservation in reservations
                         .Where(item => item.RemainingReservedQuantity > 0m)
                         .OrderByDescending(item => item.RemainingReservedQuantity)
                         .ThenBy(item => item.CreatedAt)
                         .ThenBy(item => item.Id))
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

                var stock = await LoadReservationStockAsync(
                    tenantId,
                    reservation,
                    stockCache,
                    cancellationToken);

                if (stock.ReservedQuantity < releasable)
                {
                    throw new InvalidOperationException(
                        $"Reservation release invariant violated for order {orderId}. " +
                        $"VariantId: {variantId}, WarehouseId: {reservation.WarehouseId}, Requested: {releasable}, ReservedOnStock: {stock.ReservedQuantity}");
                }

                var oldAvailable = stock.AvailableQuantity;
                var mutationEventId = Guid.NewGuid();
                stock.ReservedQuantity -= releasable;
                stock.UpdatedAt = nowUtc;
                var newAvailable = stock.AvailableQuantity;

                if (reservation.ProductId == Guid.Empty)
                {
                    reservation.ProductId = await LoadVariantProductIdAsync(tenantId, reservation.VariantId, cancellationToken);
                }

                reservation.ReleasedQuantity += releasable;
                reservation.LastMutationEventId = mutationEventId;
                reservation.Status = ResolveReservationStatus(reservation, nowUtc);
                reservation.UpdatedAt = nowUtc;

                AddReservationMovement(
                    tenantId,
                    stock,
                    reservation,
                    quantityChange: 0m,
                    reservedQuantityChange: -releasable,
                    InventoryEventType.StockReleased,
                    orderId,
                    "Order",
                    "Checkout stock reservation released.",
                    nowUtc);

                pendingChanges.Add(new PendingRepositoryStockSyncChange(
                    stock.VariantId,
                    stock.WarehouseId,
                    oldAvailable,
                    newAvailable,
                    "Checkout Reservation Released",
                    orderId,
                    "Order"));

                remainingToRelease -= releasable;
            }

            if (remainingToRelease > 0m)
            {
                throw new InvalidOperationException(
                    $"Unable to release full quantity. Requested: {quantity}, Released: {quantity - remainingToRelease}, Remaining: {remainingToRelease}. " +
                    $"TenantId: {tenantId}, VariantId: {variantId}, OrderId: {orderId}");
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await EnqueueStockSyncOutboxMessagesAsync(tenantId, pendingChanges, cancellationToken);
        }, cancellationToken);

        _logger.LogInformation(
            "ReleaseStock success | TenantId: {TenantId} | VariantId: {VariantId} | Quantity: {Quantity} | OrderId: {OrderId}",
            tenantId, variantId, quantity, orderId);
    }

    public async Task<bool> ReleaseOrderReservationsAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var anyReleased = false;
        
        await ExecuteWithinMutationTransactionAsync(async () =>
        {
            var reservations = await LoadOrderReservationsForMutationAsync(
                tenantId,
                orderId,
                cancellationToken);
            var pendingChanges = new List<PendingRepositoryStockSyncChange>();

            var openReservations = reservations
                .Where(reservation => reservation.RemainingReservedQuantity > 0m)
                .OrderByDescending(reservation => reservation.RemainingReservedQuantity)
                .ThenBy(reservation => reservation.CreatedAt)
                .ThenBy(reservation => reservation.Id)
                .ToList();

            if (openReservations.Count == 0)
            {
                return;
            }

            anyReleased = true;
            var nowUtc = DateTime.UtcNow;
            var stockCache = new Dictionary<(Guid VariantId, Guid WarehouseId), ProductVariantStock>();

            foreach (var reservation in openReservations)
            {
                var releasable = reservation.RemainingReservedQuantity;
                if (releasable <= 0m)
                {
                    continue;
                }

                var stock = await LoadReservationStockAsync(
                    tenantId,
                    reservation,
                    stockCache,
                    cancellationToken);

                if (stock.ReservedQuantity < releasable)
                {
                    throw new InvalidOperationException(
                        $"Reservation release invariant violated for order {orderId}. " +
                        $"VariantId: {reservation.VariantId}, WarehouseId: {reservation.WarehouseId}, Requested: {releasable}, ReservedOnStock: {stock.ReservedQuantity}");
                }

                var oldAvailable = stock.AvailableQuantity;
                var mutationEventId = Guid.NewGuid();
                stock.ReservedQuantity -= releasable;
                stock.UpdatedAt = nowUtc;
                var newAvailable = stock.AvailableQuantity;

                if (reservation.ProductId == Guid.Empty)
                {
                    reservation.ProductId = await LoadVariantProductIdAsync(tenantId, reservation.VariantId, cancellationToken);
                }

                reservation.ReleasedQuantity += releasable;
                reservation.LastMutationEventId = mutationEventId;
                reservation.Status = ResolveReservationStatus(reservation, nowUtc);
                reservation.UpdatedAt = nowUtc;

                AddReservationMovement(
                    tenantId,
                    stock,
                    reservation,
                    quantityChange: 0m,
                    reservedQuantityChange: -releasable,
                    InventoryEventType.StockReleased,
                    orderId,
                    "Order",
                    "Checkout order reservation released.",
                    nowUtc);

                pendingChanges.Add(new PendingRepositoryStockSyncChange(
                    stock.VariantId,
                    stock.WarehouseId,
                    oldAvailable,
                    newAvailable,
                    "Checkout Reservation Released",
                    orderId,
                    "Order"));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await EnqueueStockSyncOutboxMessagesAsync(tenantId, pendingChanges, cancellationToken);
        }, cancellationToken);

        _logger.LogInformation(
            "ReleaseOrderReservations completed | TenantId: {TenantId} | OrderId: {OrderId} | AnyReleased: {AnyReleased}",
            tenantId,
            orderId,
            anyReleased);
        
        return anyReleased;
    }

    public async Task<decimal> GetAvailableQuantityAsync(
        Guid tenantId,
        Guid variantId,
        CancellationToken cancellationToken = default)
    {
        if (!await ProductVariantInventoryLineageAuthority.IsVariantReservableForInventoryAsync(
                _dbContext,
                tenantId,
                variantId,
                cancellationToken))
        {
            return 0m;
        }

        return await _dbContext.Set<ProductVariantStock>()
            .IgnoreQueryFilters()
            .WhereCurrentInventoryStockOwner(tenantId)
            .Where(s => s.VariantId == variantId)
            .SumAsync(s => s.Quantity - s.ReservedQuantity, cancellationToken);
    }

    public async Task<Dictionary<Guid, decimal>> GetAvailableQuantitiesAsync(
        Guid tenantId,
        List<Guid> variantIds,
        CancellationToken cancellationToken = default)
    {
        if (variantIds == null || variantIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var currentVariantIds = await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .WhereInventoryReservableLineage(tenantId)
            .Where(variant =>
                variantIds.Contains(variant.Id) &&
                !variant.IsDeleted)
            .Select(variant => variant.Id)
            .ToListAsync(cancellationToken);

        if (currentVariantIds.Count == 0)
        {
            return variantIds.Distinct().ToDictionary(id => id, _ => 0m);
        }

        var stocks = await _dbContext.Set<ProductVariantStock>()
            .IgnoreQueryFilters()
            .WhereCurrentInventoryStockOwner(tenantId)
            .Where(s => currentVariantIds.Contains(s.VariantId))
            .GroupBy(s => s.VariantId)
            .Select(g => new { VariantId = g.Key, Available = g.Sum(s => s.Quantity - s.ReservedQuantity) })
            .ToListAsync(cancellationToken);

        var result = variantIds.Distinct().ToDictionary(id => id, _ => 0m);
        foreach (var stock in stocks)
        {
            result[stock.VariantId] = stock.Available;
        }

        return result;
    }

    public async Task CommitReservationAsync(
        Guid tenantId,
        Guid variantId,
        Guid? warehouseId,
        decimal quantity,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0m)
        {
            return;
        }

        await ExecuteWithinMutationTransactionAsync(async () =>
        {
            var reservations = await LoadReservationsForMutationAsync(
                tenantId,
                orderId,
                variantId,
                warehouseId,
                cancellationToken);
            var pendingChanges = new List<PendingRepositoryStockSyncChange>();

            var alreadyCommitted = reservations.Sum(reservation => reservation.CommittedQuantity);
            var remainingToCommit = Math.Max(0m, quantity - alreadyCommitted);
            if (remainingToCommit <= 0m)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var stockCache = new Dictionary<(Guid VariantId, Guid WarehouseId), ProductVariantStock>();
            var consumableReservations = await ProductVariantInventoryLineageAuthority.FilterCurrentConsumableReservationsAsync(
                _dbContext,
                tenantId,
                reservations
                    .Where(item => item.RemainingReservedQuantity > 0m)
                    .ToList(),
                nowUtc,
                cancellationToken);

            foreach (var reservation in consumableReservations
                         .Where(item => item.RemainingReservedQuantity > 0m)
                         .OrderByDescending(item => item.RemainingReservedQuantity)
                         .ThenBy(item => item.CreatedAt)
                         .ThenBy(item => item.Id))
            {
                if (remainingToCommit <= 0m)
                {
                    break;
                }

                var commitQuantity = Math.Min(remainingToCommit, reservation.RemainingReservedQuantity);
                if (commitQuantity <= 0m)
                {
                    continue;
                }

                var stock = await LoadReservationStockAsync(
                    tenantId,
                    reservation,
                    stockCache,
                    cancellationToken);

                if (stock.ReservedQuantity < commitQuantity || stock.Quantity < commitQuantity)
                {
                    throw new InvalidOperationException(
                        $"Reservation commit invariant violated for order {orderId}. " +
                        $"VariantId: {variantId}, WarehouseId: {reservation.WarehouseId}, Requested: {commitQuantity}, Quantity: {stock.Quantity}, Reserved: {stock.ReservedQuantity}");
                }

                var oldAvailable = stock.AvailableQuantity;
                var mutationEventId = Guid.NewGuid();
                stock.Quantity -= commitQuantity;
                stock.ReservedQuantity -= commitQuantity;
                stock.UpdatedAt = nowUtc;
                var newAvailable = stock.AvailableQuantity;

                if (reservation.ProductId == Guid.Empty)
                {
                    reservation.ProductId = await LoadVariantProductIdAsync(tenantId, reservation.VariantId, cancellationToken);
                }

                reservation.CommittedQuantity += commitQuantity;
                reservation.LastMutationEventId = mutationEventId;
                reservation.Status = ResolveReservationStatus(reservation, nowUtc);
                reservation.UpdatedAt = nowUtc;

                AddReservationMovement(
                    tenantId,
                    stock,
                    reservation,
                    quantityChange: -commitQuantity,
                    reservedQuantityChange: -commitQuantity,
                    InventoryEventType.StockDecreased,
                    orderId,
                    "Order",
                    "Checkout reservation committed.",
                    nowUtc);

                pendingChanges.Add(new PendingRepositoryStockSyncChange(
                    stock.VariantId,
                    stock.WarehouseId,
                    oldAvailable,
                    newAvailable,
                    "Checkout Reservation Committed",
                    orderId,
                    "Order"));

                remainingToCommit -= commitQuantity;
            }

            if (remainingToCommit > 0m)
            {
                throw new InvalidOperationException(
                    $"Unable to commit full quantity. Requested: {quantity}, Committed: {quantity - remainingToCommit}, Remaining: {remainingToCommit}. " +
                    $"TenantId: {tenantId}, VariantId: {variantId}, OrderId: {orderId}");
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await EnqueueStockSyncOutboxMessagesAsync(tenantId, pendingChanges, cancellationToken);
        }, cancellationToken);

        _logger.LogInformation(
            "CommitReservation success | TenantId: {TenantId} | VariantId: {VariantId} | Quantity: {Quantity} | OrderId: {OrderId}",
            tenantId, variantId, quantity, orderId);
    }

    public async Task CommitOrderReservationsAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithinMutationTransactionAsync(async () =>
        {
            var reservations = await LoadOrderReservationsForMutationAsync(
                tenantId,
                orderId,
                cancellationToken);
            var pendingChanges = new List<PendingRepositoryStockSyncChange>();

            var nowUtc = DateTime.UtcNow;
            var openReservations = await ProductVariantInventoryLineageAuthority.FilterCurrentConsumableReservationsAsync(
                _dbContext,
                tenantId,
                reservations
                    .Where(reservation => reservation.RemainingReservedQuantity > 0m)
                    .ToList(),
                nowUtc,
                cancellationToken);
            openReservations = openReservations
                .OrderByDescending(reservation => reservation.RemainingReservedQuantity)
                .ThenBy(reservation => reservation.CreatedAt)
                .ThenBy(reservation => reservation.Id)
                .ToList();

            if (openReservations.Count == 0)
            {
                return;
            }

            var stockCache = new Dictionary<(Guid VariantId, Guid WarehouseId), ProductVariantStock>();

            foreach (var reservation in openReservations)
            {
                var commitQuantity = reservation.RemainingReservedQuantity;
                if (commitQuantity <= 0m)
                {
                    continue;
                }

                var stock = await LoadReservationStockAsync(
                    tenantId,
                    reservation,
                    stockCache,
                    cancellationToken);

                if (stock.ReservedQuantity < commitQuantity || stock.Quantity < commitQuantity)
                {
                    throw new InvalidOperationException(
                        $"Reservation commit invariant violated for order {orderId}. " +
                        $"VariantId: {reservation.VariantId}, WarehouseId: {reservation.WarehouseId}, Requested: {commitQuantity}, Quantity: {stock.Quantity}, Reserved: {stock.ReservedQuantity}");
                }

                var oldAvailable = stock.AvailableQuantity;
                var mutationEventId = Guid.NewGuid();
                stock.Quantity -= commitQuantity;
                stock.ReservedQuantity -= commitQuantity;
                stock.UpdatedAt = nowUtc;
                var newAvailable = stock.AvailableQuantity;

                if (reservation.ProductId == Guid.Empty)
                {
                    reservation.ProductId = await LoadVariantProductIdAsync(tenantId, reservation.VariantId, cancellationToken);
                }

                reservation.CommittedQuantity += commitQuantity;
                reservation.LastMutationEventId = mutationEventId;
                reservation.Status = ResolveReservationStatus(reservation, nowUtc);
                reservation.UpdatedAt = nowUtc;

                AddReservationMovement(
                    tenantId,
                    stock,
                    reservation,
                    quantityChange: -commitQuantity,
                    reservedQuantityChange: -commitQuantity,
                    InventoryEventType.StockDecreased,
                    orderId,
                    "Order",
                    "Checkout order reservation committed.",
                    nowUtc);

                pendingChanges.Add(new PendingRepositoryStockSyncChange(
                    stock.VariantId,
                    stock.WarehouseId,
                    oldAvailable,
                    newAvailable,
                    "Checkout Reservation Committed",
                    orderId,
                    "Order"));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await EnqueueStockSyncOutboxMessagesAsync(tenantId, pendingChanges, cancellationToken);
        }, cancellationToken);

        _logger.LogInformation(
            "CommitOrderReservations success | TenantId: {TenantId} | OrderId: {OrderId}",
            tenantId, orderId);
    }

    public async Task BindOrderReservationsAsync(
        Guid tenantId,
        Guid orderId,
        IReadOnlyCollection<OrderItem> orderItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderItems);

        await ExecuteWithinMutationTransactionAsync(async () =>
        {
            var reservations = await LoadOrderReservationsForMutationAsync(
                tenantId,
                orderId,
                cancellationToken);

            if (reservations.Count == 0)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;

            var placeholders = reservations
                .Where(reservation => reservation.OrderItemId == Guid.Empty 
                    && reservation.RemainingReservedQuantity > 0m
                    && reservation.Status == InventoryReservationStatus.Active
                    && (reservation.ExpiresAtUtc == null || reservation.ExpiresAtUtc > nowUtc))
                .OrderByDescending(reservation => reservation.RemainingReservedQuantity)
                .ThenBy(reservation => reservation.CreatedAt)
                .ThenBy(reservation => reservation.Id)
                .ToList();

            placeholders = await ProductVariantInventoryLineageAuthority.FilterCurrentConsumableReservationsAsync(
                _dbContext,
                tenantId,
                placeholders,
                nowUtc,
                cancellationToken,
                allowUnboundProductId: true);

            foreach (var orderItem in orderItems
                         .Where(item => item.VariantId.HasValue && item.Quantity > 0)
                         .OrderBy(item => item.Id))
            {
                var variantId = orderItem.VariantId!.Value;
                var alreadyAssigned = reservations
                    .Where(reservation => reservation.OrderItemId == orderItem.Id && reservation.VariantId == variantId)
                    .Sum(reservation => reservation.ReservedQuantity);

                var remainingToAssign = Math.Max(0m, orderItem.Quantity - alreadyAssigned);
                if (remainingToAssign <= 0m)
                {
                    continue;
                }

                foreach (var placeholder in placeholders
                             .Where(reservation => reservation.VariantId == variantId && reservation.RemainingReservedQuantity > 0m)
                             .ToList())
                {
                    if (remainingToAssign <= 0m)
                    {
                        break;
                    }

                    var assignable = Math.Min(remainingToAssign, placeholder.RemainingReservedQuantity);
                    if (assignable <= 0m)
                    {
                        continue;
                    }

                    if (assignable == placeholder.RemainingReservedQuantity)
                    {
                        placeholder.OrderItemId = orderItem.Id;
                        placeholder.ProductId = orderItem.ProductId;
                        placeholder.UpdatedAt = nowUtc;
                        placeholders.Remove(placeholder);
                    }
                    else
                    {
                        placeholder.ReservedQuantity -= assignable;
                        placeholder.UpdatedAt = nowUtc;

                        _dbContext.InventoryReservations.Add(new InventoryReservation
                        {
                            Id = Guid.NewGuid(),
                            TenantId = placeholder.TenantId,
                            OrderId = placeholder.OrderId,
                            OrderItemId = orderItem.Id,
                            ProductId = orderItem.ProductId,
                            VariantId = placeholder.VariantId,
                            WarehouseId = placeholder.WarehouseId,
                            ReservedQuantity = assignable,
                            Status = InventoryReservationStatus.Active,
                            ReservationEventId = placeholder.ReservationEventId,
                            ExpiresAtUtc = placeholder.ExpiresAtUtc,
                            CreatedAt = placeholder.CreatedAt,
                            UpdatedAt = nowUtc
                        });
                    }

                    remainingToAssign -= assignable;
                }

                if (remainingToAssign > 0m)
                {
                    throw new InvalidOperationException(
                        $"Unable to bind reserved inventory to order item {orderItem.Id}. " +
                        $"TenantId: {tenantId}, OrderId: {orderId}, VariantId: {variantId}, Remaining: {remainingToAssign}");
                }
            }

            if (placeholders.Any(reservation => reservation.RemainingReservedQuantity > 0m))
            {
                throw new InvalidOperationException(
                    $"Order reservation binding left unassigned inventory rows. TenantId: {tenantId}, OrderId: {orderId}");
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        _logger.LogInformation(
            "BindOrderReservations success | TenantId: {TenantId} | OrderId: {OrderId} | ItemCount: {ItemCount}",
            tenantId, orderId, orderItems.Count);
    }

    private async Task<List<ProductVariantStock>> LoadStocksForMutationAsync(
        Guid tenantId,
        Guid variantId,
        Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        IQueryable<ProductVariantStock> query;

        if (IsPostgresProvider())
        {
            return await (warehouseId.HasValue
                ? _dbContext.ProductVariantStocks.FromSqlInterpolated($@"
                    SELECT * FROM ""ProductVariantStocks""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""VariantId"" = {variantId}
                      AND ""WarehouseId"" = {warehouseId.Value}
                      AND ""IsDeleted"" = false
                    ORDER BY ""WarehouseId"", ""Id""
                    FOR UPDATE").IgnoreQueryFilters()
                : _dbContext.ProductVariantStocks.FromSqlInterpolated($@"
                    SELECT * FROM ""ProductVariantStocks""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""VariantId"" = {variantId}
                      AND ""IsDeleted"" = false
                    ORDER BY ""WarehouseId"", ""Id""
                    FOR UPDATE").IgnoreQueryFilters())
                .ToListAsync(cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await (warehouseId.HasValue
                ? _dbContext.ProductVariantStocks.FromSqlInterpolated($@"
                    SELECT * FROM [ProductVariantStocks] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [VariantId] = {variantId}
                      AND [WarehouseId] = {warehouseId.Value}
                      AND [IsDeleted] = 0
                    ORDER BY [WarehouseId], [Id]").IgnoreQueryFilters()
                : _dbContext.ProductVariantStocks.FromSqlInterpolated($@"
                    SELECT * FROM [ProductVariantStocks] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [VariantId] = {variantId}
                      AND [IsDeleted] = 0
                    ORDER BY [WarehouseId], [Id]").IgnoreQueryFilters())
                .ToListAsync(cancellationToken);
        }

        query = _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .Where(stock =>
                stock.TenantId == tenantId &&
                stock.VariantId == variantId &&
                !stock.IsDeleted &&
                (!warehouseId.HasValue || stock.WarehouseId == warehouseId.Value));

        return await query
            .OrderBy(stock => stock.WarehouseId)
            .ThenBy(stock => stock.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<InventoryReservation>> LoadOrderReservationsForMutationAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        IQueryable<InventoryReservation> query;

        if (IsPostgresProvider())
        {
            return await _dbContext.InventoryReservations.FromSqlInterpolated($@"
                SELECT * FROM ""InventoryReservations""
                WHERE ""TenantId"" = {tenantId}
                  AND ""OrderId"" = {orderId}
                  AND ""IsDeleted"" = false
                ORDER BY ""CreatedAt"", ""Id""
                FOR UPDATE")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _dbContext.InventoryReservations.FromSqlInterpolated($@"
                SELECT * FROM [InventoryReservations] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [TenantId] = {tenantId}
                  AND [OrderId] = {orderId}
                  AND [IsDeleted] = 0
                ORDER BY [CreatedAt], [Id]")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
        }

        query = _dbContext.InventoryReservations
            .IgnoreQueryFilters()
            .Where(reservation =>
                reservation.TenantId == tenantId &&
                reservation.OrderId == orderId &&
                !reservation.IsDeleted);

        return await query
            .OrderBy(reservation => reservation.CreatedAt)
            .ThenBy(reservation => reservation.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<InventoryReservation>> LoadReservationsForMutationAsync(
        Guid tenantId,
        Guid orderId,
        Guid variantId,
        Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        var reservations = await LoadOrderReservationsForMutationAsync(
            tenantId,
            orderId,
            cancellationToken);

        return reservations
            .Where(reservation =>
                reservation.VariantId == variantId &&
                (!warehouseId.HasValue || reservation.WarehouseId == warehouseId.Value))
            .ToList();
    }

    private async Task<ProductVariantStock> LoadReservationStockAsync(
        Guid tenantId,
        InventoryReservation reservation,
        IDictionary<(Guid VariantId, Guid WarehouseId), ProductVariantStock> stockCache,
        CancellationToken cancellationToken)
    {
        var cacheKey = (reservation.VariantId, reservation.WarehouseId);
        if (stockCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var stock = (await LoadStocksForMutationAsync(
                tenantId,
                reservation.VariantId,
                reservation.WarehouseId,
                cancellationToken))
            .SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"Reserved stock row was not found. TenantId: {tenantId}, VariantId: {reservation.VariantId}, WarehouseId: {reservation.WarehouseId}");

        stockCache[cacheKey] = stock;
        return stock;
    }

    private async Task<Guid> LoadVariantProductIdAsync(
        Guid tenantId,
        Guid variantId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .WhereInventoryReservableLineage(tenantId)
            .Where(variant => variant.Id == variantId)
            .Select(variant => variant.ProductId)
            .SingleAsync(cancellationToken);
    }

    private void AddReservationMovement(
        Guid tenantId,
        ProductVariantStock stock,
        InventoryReservation reservation,
        decimal quantityChange,
        decimal reservedQuantityChange,
        InventoryEventType inventoryEventType,
        Guid orderId,
        string referenceType,
        string notes,
        DateTime nowUtc)
    {
        if (reservation.ProductId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Reservation movement requires ProductId. TenantId: {tenantId}, ReservationId: {reservation.Id}, VariantId: {reservation.VariantId}");
        }

        var inventoryEventId = reservation.LastMutationEventId ?? reservation.ReservationEventId;
        var movementKind = quantityChange < 0m
            ? StockMovementType.Sale
            : StockMovementType.Adjustment;

        _dbContext.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarehouseId = stock.WarehouseId,
            ProductId = reservation.ProductId,
            ProductVariantId = stock.VariantId,
            MovementType = movementKind,
            QuantityChange = quantityChange,
            ReservedQuantityChange = reservedQuantityChange,
            DamagedQuantityChange = 0m,
            QuantityAfter = stock.Quantity,
            AvailableAfter = stock.AvailableQuantity,
            ReservedAfter = stock.ReservedQuantity,
            DamagedAfter = stock.DamagedQuantity,
            ReferenceId = orderId,
            ReferenceLineId = reservation.OrderItemId == Guid.Empty ? null : reservation.OrderItemId,
            ReservationId = reservation.Id,
            InventoryEventId = inventoryEventId,
            InventoryEventType = inventoryEventType,
            ReferenceType = referenceType,
            CorrelationId = orderId.ToString("N"),
            IdempotencyKey = $"inventory-reservation:{tenantId:N}:{orderId:N}:{reservation.Id:N}:{inventoryEventType}:{inventoryEventId:N}",
            Source = nameof(ProductVariantStockRepository),
            Notes = notes,
            CreatedAt = nowUtc
        });
    }

    private async Task EnqueueStockSyncOutboxMessagesAsync(
        Guid tenantId,
        IReadOnlyCollection<PendingRepositoryStockSyncChange> pendingChanges,
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
                // Quantity is the sellable stock bucket; damaged units are tracked separately
                // and must not be subtracted again from public availability.
                // Clamped to non-negative to prevent downstream edge cases from data drift
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
                throw new InvalidOperationException($"Variant {change.VariantId} metadata was not found for stock sync.");
            }

            var stockUpdatedAtUtc = NormalizeUtcTimestamp(
                variant.StockUpdatedAtUtc
                    ?? throw new InvalidOperationException($"Variant {change.VariantId} is missing StockUpdatedAtUtc."));

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
                MutationSource = MutationSources.POS,
                TotalStockQuantity = totalStockByVariant.GetValueOrDefault(change.VariantId),
                CorrelationId = change.ReferenceId?.ToString("N") ?? Guid.NewGuid().ToString("N"),
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
                ?? throw new InvalidOperationException($"Variant {variantId} was not found for stock sync.");

            variant.StockUpdatedAtUtc = StockProjectionTimestamp.Next(variant.StockUpdatedAtUtc);
            variant.UpdatedAt = variant.StockUpdatedAtUtc;
        }
    }

    private DateTime NormalizeUtcTimestamp(DateTime value)
    {
        return StockProjectionTimestamp.NormalizeUtc(value.Kind == DateTimeKind.Unspecified
            ? HandleUnspecifiedKind(value)
            : value);
    }

    private DateTime HandleUnspecifiedKind(DateTime value)
    {
        _logger.LogWarning(
            "DateTime with Unspecified kind encountered and assumed to be UTC | Value: {Value} | This may indicate data from external sources or legacy code",
            value);
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private async Task<ProductVariant?> LoadVariantForStockSyncAsync(
        Guid tenantId,
        Guid variantId,
        CancellationToken cancellationToken)
    {
        IQueryable<ProductVariant> query;

        if (IsPostgresProvider())
        {
            query = _dbContext.ProductVariants
                .FromSqlInterpolated($@"
                    SELECT * FROM ""ProductVariants""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {variantId}
                      AND ""IsDeleted"" = false
                    FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else if (IsSqlServerProvider())
        {
            query = _dbContext.ProductVariants
                .FromSqlInterpolated($@"
                    SELECT * FROM [ProductVariants] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {variantId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters();
        }
        else
        {
            query = _dbContext.ProductVariants
                .IgnoreQueryFilters()
                .Where(variant => variant.TenantId == tenantId && variant.Id == variantId && !variant.IsDeleted);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task ExecuteWithinMutationTransactionAsync(
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        var ownsTransaction = _dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction == null;

        if (!ownsTransaction)
        {
            await action();
            return;
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                await action();
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
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

    private bool IsPostgresProvider() =>
        _dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _dbContext.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private sealed record PendingRepositoryStockSyncChange(
        Guid VariantId,
        Guid WarehouseId,
        decimal OldQuantity,
        decimal NewQuantity,
        string Reason,
        Guid? ReferenceId,
        string? ReferenceType);
}
