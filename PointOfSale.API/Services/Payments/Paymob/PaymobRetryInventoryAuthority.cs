using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;

namespace PointOfSale.API.Services.Payments.Paymob;

public sealed class PaymobRetryInventoryAuthority : IPaymobRetryInventoryAuthority
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IProductVariantStockRepository _stockRepository;

    public PaymobRetryInventoryAuthority(
        ApplicationDbContext dbContext,
        IProductVariantStockRepository stockRepository)
    {
        _dbContext = dbContext;
        _stockRepository = stockRepository;
    }

    public async Task EnsureRetryReservationAuthorityAsync(
        Order order,
        DateTime reservationExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var reservableRows = await _dbContext.OrderItems
            .IgnoreQueryFilters()
            .Where(item =>
                item.OrderId == order.Id &&
                !item.IsDeleted &&
                item.Quantity > 0 &&
                item.VariantId.HasValue)
            .Join(
                _dbContext.Products.IgnoreQueryFilters(),
                item => item.ProductId,
                product => product.Id,
                (item, product) => new
                {
                    item.Id,
                    item.ProductId,
                    VariantId = item.VariantId!.Value,
                    WarehouseId = item.WarehouseId ?? order.WarehouseId,
                    RequiredQuantity = (decimal)item.Quantity,
                    product.TenantId,
                    product.TrackStock,
                    product.AllowBackorder,
                    ProductDeleted = product.IsDeleted
                })
            .Where(item =>
                item.TenantId == order.TenantId &&
                !item.ProductDeleted &&
                item.TrackStock &&
                !item.AllowBackorder)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);

        var unresolvedOrderItemIds = reservableRows
            .Where(item => !item.WarehouseId.HasValue)
            .Select(item => item.Id)
            .ToList();
        var reservationWarehouseRows = unresolvedOrderItemIds.Count == 0
            ? []
            : await _dbContext.InventoryReservations
                .IgnoreQueryFilters()
                .Where(reservation =>
                    reservation.TenantId == order.TenantId &&
                    reservation.OrderId == order.Id &&
                    unresolvedOrderItemIds.Contains(reservation.OrderItemId) &&
                    !reservation.IsDeleted)
                .Select(reservation => new
                {
                    reservation.OrderItemId,
                    reservation.VariantId,
                    reservation.WarehouseId
                })
                .ToListAsync(cancellationToken);
        var reservationWarehouseAuthority = reservationWarehouseRows
            .GroupBy(row => (row.OrderItemId, row.VariantId))
            .Select(group => new
            {
                group.Key,
                WarehouseIds = group.Select(row => row.WarehouseId).Distinct().ToList()
            })
            .Where(group => group.WarehouseIds.Count == 1)
            .ToDictionary(group => group.Key, group => group.WarehouseIds[0]);

        var reservableItems = reservableRows
            .Select(item => new PaymobRetryReservationItem(
                item.Id,
                item.ProductId,
                item.VariantId,
                item.WarehouseId ?? reservationWarehouseAuthority.GetValueOrDefault((item.Id, item.VariantId)),
                item.RequiredQuantity))
            .ToList();

        if (reservableItems.Any(item => !item.WarehouseId.HasValue))
        {
            throw new PaymentOrderInvalidStateException(
                order.Id,
                "Inventory:WarehouseAuthorityMissingForPaymobRetry",
                "OrderItemOrOrderWarehouseId");
        }

        if (reservableItems.Count == 0 ||
            await HasActiveRetryReservationAuthorityAsync(order, reservableItems, reservationExpiresAtUtc, cancellationToken))
        {
            return;
        }

        // Replace partial or stale reservation sets as one authority unit. Keeping
        // a partial set and reserving full quantities would over-reserve inventory.
        await _stockRepository.ReleaseOrderReservationsAsync(
            order.TenantId,
            order.Id,
            cancellationToken);

        foreach (var item in reservableItems)
        {
            var reserved = await _stockRepository.ReserveAsync(
                order.TenantId,
                item.VariantId,
                item.WarehouseId,
                item.RequiredQuantity,
                order.Id,
                reservationExpiresAtUtc,
                cancellationToken);

            if (!reserved)
            {
                throw new PaymentOrderInvalidStateException(
                    order.Id,
                    "Inventory:UnavailableForPaymobRetry",
                    "ActiveSqlInventoryReservation");
            }
        }

        await BindRetryReservationsToOrderItemsAsync(order, reservableItems, cancellationToken);
    }

    private async Task BindRetryReservationsToOrderItemsAsync(
        Order order,
        IReadOnlyCollection<PaymobRetryReservationItem> reservableItems,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var placeholders = (await _dbContext.InventoryReservations
            .IgnoreQueryFilters()
            .Where(reservation =>
                reservation.TenantId == order.TenantId &&
                reservation.OrderId == order.Id &&
                reservation.OrderItemId == Guid.Empty &&
                !reservation.IsDeleted &&
                reservation.Status == InventoryReservationStatus.Active &&
                reservation.ReservedQuantity > reservation.CommittedQuantity + reservation.ReleasedQuantity &&
                (reservation.ExpiresAtUtc == null || reservation.ExpiresAtUtc > nowUtc))
            .ToListAsync(cancellationToken))
            .OrderByDescending(reservation => reservation.ReservedQuantity - reservation.CommittedQuantity - reservation.ReleasedQuantity)
            .ThenBy(reservation => reservation.CreatedAt)
            .ThenBy(reservation => reservation.Id)
            .ToList();

        foreach (var item in reservableItems.OrderBy(item => item.Id))
        {
            var alreadyAssigned = await _dbContext.InventoryReservations
                .IgnoreQueryFilters()
                .Where(reservation =>
                    reservation.TenantId == order.TenantId &&
                    reservation.OrderId == order.Id &&
                    reservation.OrderItemId == item.Id &&
                    reservation.VariantId == item.VariantId &&
                    reservation.WarehouseId == item.WarehouseId &&
                    !reservation.IsDeleted &&
                    reservation.Status == InventoryReservationStatus.Active &&
                    reservation.ReservedQuantity > reservation.CommittedQuantity + reservation.ReleasedQuantity &&
                    (reservation.ExpiresAtUtc == null || reservation.ExpiresAtUtc > nowUtc))
                .SumAsync(
                    reservation => reservation.ReservedQuantity - reservation.CommittedQuantity - reservation.ReleasedQuantity,
                    cancellationToken);

            var remainingToAssign = Math.Max(0m, item.RequiredQuantity - alreadyAssigned);
            if (remainingToAssign <= 0m)
            {
                continue;
            }

            foreach (var placeholder in placeholders
                         .Where(reservation => reservation.VariantId == item.VariantId &&
                            reservation.WarehouseId == item.WarehouseId &&
                            reservation.ReservedQuantity > reservation.CommittedQuantity + reservation.ReleasedQuantity)
                         .ToList())
            {
                if (remainingToAssign <= 0m)
                {
                    break;
                }

                var remainingPlaceholderQuantity = placeholder.ReservedQuantity -
                    placeholder.CommittedQuantity -
                    placeholder.ReleasedQuantity;
                var assignable = Math.Min(remainingToAssign, remainingPlaceholderQuantity);
                if (assignable <= 0m)
                {
                    continue;
                }

                if (assignable == remainingPlaceholderQuantity)
                {
                    placeholder.OrderItemId = item.Id;
                    placeholder.ProductId = item.ProductId;
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
                        OrderItemId = item.Id,
                        ProductId = item.ProductId,
                        VariantId = placeholder.VariantId,
                        WarehouseId = placeholder.WarehouseId,
                        ReservedQuantity = assignable,
                        Status = InventoryReservationStatus.Active,
                        ReservationEventId = placeholder.ReservationEventId,
                        LastMutationEventId = placeholder.LastMutationEventId,
                        ExpiresAtUtc = placeholder.ExpiresAtUtc,
                        CreatedAt = placeholder.CreatedAt,
                        UpdatedAt = nowUtc
                    });
                }

                remainingToAssign -= assignable;
            }

            if (remainingToAssign > 0m)
            {
                throw new PaymentOrderInvalidStateException(
                    order.Id,
                    "Inventory:RetryReservationBindingIncomplete",
                    "ActiveSqlInventoryReservation");
            }
        }

        if (placeholders.Any(reservation =>
                reservation.ReservedQuantity > reservation.CommittedQuantity + reservation.ReleasedQuantity))
        {
            throw new PaymentOrderInvalidStateException(
                order.Id,
                "Inventory:RetryReservationBindingHasUnassignedRows",
                "ActiveSqlInventoryReservation");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> HasActiveRetryReservationAuthorityAsync(
        Order order,
        IEnumerable<PaymobRetryReservationItem> reservableItems,
        DateTime reservationExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var requiredReservationKeys = reservableItems
            .Select(item => (OrderItemId: item.Id, item.VariantId, item.WarehouseId))
            .ToHashSet();
        var activeReservations = await _dbContext.InventoryReservations
            .IgnoreQueryFilters()
            .Where(reservation =>
                reservation.TenantId == order.TenantId &&
                reservation.OrderId == order.Id &&
                !reservation.IsDeleted &&
                reservation.Status == InventoryReservationStatus.Active &&
                reservation.ReservedQuantity > reservation.CommittedQuantity + reservation.ReleasedQuantity &&
                (reservation.ExpiresAtUtc == null || reservation.ExpiresAtUtc > nowUtc))
            .ToListAsync(cancellationToken);

        if (activeReservations.Any(reservation =>
                !requiredReservationKeys.Contains((
                    reservation.OrderItemId,
                    reservation.VariantId,
                    (Guid?)reservation.WarehouseId))))
        {
            return false;
        }

        foreach (var item in reservableItems)
        {
            var reserved = activeReservations
                .Where(reservation =>
                    reservation.OrderItemId == item.Id &&
                    reservation.VariantId == item.VariantId &&
                    reservation.WarehouseId == item.WarehouseId)
                .Sum(reservation => reservation.ReservedQuantity - reservation.CommittedQuantity - reservation.ReleasedQuantity);

            if (reserved != item.RequiredQuantity)
            {
                return false;
            }
        }

        var reservationExtended = false;
        foreach (var reservation in activeReservations.Where(reservation =>
                     requiredReservationKeys.Contains((reservation.OrderItemId, reservation.VariantId, reservation.WarehouseId)) &&
                     reservation.ExpiresAtUtc.HasValue &&
                     reservation.ExpiresAtUtc.Value < reservationExpiresAtUtc))
        {
            reservation.ExpiresAtUtc = reservationExpiresAtUtc;
            reservation.UpdatedAt = nowUtc;
            reservationExtended = true;
        }

        if (reservationExtended)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private sealed record PaymobRetryReservationItem(
        Guid Id,
        Guid ProductId,
        Guid VariantId,
        Guid? WarehouseId,
        decimal RequiredQuantity);
}
