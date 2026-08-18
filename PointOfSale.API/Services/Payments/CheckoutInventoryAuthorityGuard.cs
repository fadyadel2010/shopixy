using System.Data;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.Payments;

/// <summary>
/// Revalidates storefront checkout inventory authority before payment intent work.
/// Storefront/cart/session visibility is advisory only; active SQL reservations
/// and reserved stock rows must still cover the order before provider side effects.
/// </summary>
public static class CheckoutInventoryAuthorityGuard
{
    public static async Task EnsureStorefrontOrderReservationsActiveAsync(
        ApplicationDbContext dbContext,
        Order order,
        CancellationToken cancellationToken)
    {
        if (!order.CartId.HasValue)
        {
            return;
        }

        await EnsureOrderReservationsActiveAsync(dbContext, order, cancellationToken);
    }

    public static async Task EnsureOrderReservationsActiveAsync(
        ApplicationDbContext dbContext,
        Order order,
        CancellationToken cancellationToken)
    {

        var ownsTransaction = dbContext.Database.IsRelational() &&
                              dbContext.Database.CurrentTransaction == null;

        if (!ownsTransaction)
        {
            await EnsureStorefrontOrderReservationsActiveCoreAsync(
                dbContext,
                order,
                cancellationToken);
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var lockedOrder = await OrderLockingHelper.LoadOrderWithLockAsync(
            dbContext,
            order.TenantId,
            order.Id,
            cancellationToken);

        if (lockedOrder == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new PaymentOrderNotFoundException(order.Id);
        }

        await EnsureStorefrontOrderReservationsActiveCoreAsync(
            dbContext,
            lockedOrder,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task EnsureStorefrontOrderReservationsActiveCoreAsync(
        ApplicationDbContext dbContext,
        Order order,
        CancellationToken cancellationToken)
    {
        var orderItems = await dbContext.OrderItems
            .IgnoreQueryFilters()
            .Where(item =>
                item.OrderId == order.Id &&
                !item.IsDeleted &&
                item.Quantity > 0 &&
                item.VariantId.HasValue)
            .Select(item => new
            {
                item.Id,
                item.ProductId,
                VariantId = item.VariantId!.Value,
                item.WarehouseId,
                RequiredQuantity = (decimal)item.Quantity
            })
            .ToListAsync(cancellationToken);

        if (orderItems.Count == 0)
        {
            throw BuildInventoryAuthorityLost(
                order.Id,
                "NoVariantOrderItems",
                "SqlVariantOrderItems");
        }

        var productIds = orderItems.Select(item => item.ProductId).Distinct().ToList();
        var variantIds = orderItems.Select(item => item.VariantId).Distinct().ToList();

        var productAuthority = await dbContext.Products
            .IgnoreQueryFilters()
            .Where(product =>
                product.TenantId == order.TenantId &&
                productIds.Contains(product.Id) &&
                !product.IsDeleted)
            .Select(product => new
            {
                product.Id,
                product.IsActive,
                product.TrackStock,
                product.AllowBackorder
            })
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        var variantAuthority = await dbContext.ProductVariants
            .IgnoreQueryFilters()
            .Where(variant =>
                variant.TenantId == order.TenantId &&
                variantIds.Contains(variant.Id) &&
                !variant.IsDeleted)
            .Select(variant => new
            {
                variant.Id,
                variant.ProductId,
                variant.IsActive,
                variant.SemanticLifecycleState,
                variant.SemanticSupersededAtUtc,
                variant.SemanticReplacementVariantId
            })
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);

        foreach (var item in orderItems)
        {
            if (!productAuthority.ContainsKey(item.ProductId) ||
                !variantAuthority.TryGetValue(item.VariantId, out var variant) ||
                variant.ProductId != item.ProductId ||
                !productAuthority[item.ProductId].IsActive ||
                !variant.IsActive ||
                variant.SemanticLifecycleState != ProductVariantSemanticLifecycleState.Current ||
                variant.SemanticSupersededAtUtc != null ||
                variant.SemanticReplacementVariantId != null)
            {
                throw BuildInventoryAuthorityLost(
                    order.Id,
                    "ProductVariantOwnershipMismatch",
                    "TenantOwnedSqlProductVariant");
            }
        }

        var reservableItems = orderItems
            .Where(item =>
                productAuthority[item.ProductId].TrackStock &&
                !productAuthority[item.ProductId].AllowBackorder)
            .ToList();

        if (reservableItems.Count == 0)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var reservations = await LoadOrderReservationsForUpdateAsync(
            dbContext,
            order.TenantId,
            order.Id,
            cancellationToken);

        var activeReservations = await ProductVariantInventoryLineageAuthority.FilterCurrentConsumableReservationsAsync(
            dbContext,
            order.TenantId,
            reservations
            .Where(reservation =>
                reservation.Status == InventoryReservationStatus.Active &&
                reservation.RemainingReservedQuantity > 0m &&
                (reservation.ExpiresAtUtc == null || reservation.ExpiresAtUtc.Value > nowUtc))
                .ToList(),
            nowUtc,
            cancellationToken);

        var activeReservedByLine = activeReservations
            .GroupBy(reservation => new
            {
                reservation.OrderItemId,
                reservation.VariantId,
                reservation.WarehouseId
            })
            .ToDictionary(
                group => (group.Key.OrderItemId, group.Key.VariantId, group.Key.WarehouseId),
                group => group.Sum(reservation => reservation.RemainingReservedQuantity));

        var validatedReservationKeys = new HashSet<(Guid OrderItemId, Guid VariantId, Guid WarehouseId)>();
        foreach (var item in reservableItems)
        {
            var warehouseId = item.WarehouseId ?? order.WarehouseId;
            var matchingReservationLines = warehouseId.HasValue
                ? activeReservedByLine
                    .Where(line => line.Key == (item.Id, item.VariantId, warehouseId.Value))
                    .ToList()
                : activeReservedByLine
                    .Where(line =>
                        line.Key.OrderItemId == item.Id &&
                        line.Key.VariantId == item.VariantId)
                    .ToList();
            var reservedQuantity = matchingReservationLines.Count == 1
                ? matchingReservationLines[0].Value
                : 0m;
            if (reservedQuantity != item.RequiredQuantity)
            {
                throw BuildInventoryAuthorityLost(
                    order.Id,
                    $"Required:{item.RequiredQuantity};ActiveReserved:{reservedQuantity};OrderItem:{item.Id};Variant:{item.VariantId}",
                    "ActiveSqlInventoryReservation");
            }

            validatedReservationKeys.Add(matchingReservationLines[0].Key);
        }

        if (activeReservedByLine.Keys.Any(key => !validatedReservationKeys.Contains(key)))
        {
            throw BuildInventoryAuthorityLost(
                order.Id,
                "Active reservation set contains rows that do not map exactly to an authoritative order line.",
                "ExactSqlInventoryReservationSet");
        }

        await EnsureReservedStockRowsStillCoverReservationsAsync(
            dbContext,
            order,
            activeReservations,
            cancellationToken);
    }

    private static async Task<List<InventoryReservation>> LoadOrderReservationsForUpdateAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var providerName = dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await dbContext.InventoryReservations
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""InventoryReservations""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""OrderId"" = {orderId}
                      AND ""IsDeleted"" = FALSE
                    ORDER BY ""OrderItemId"", ""VariantId"", ""WarehouseId"", ""CreatedAt"", ""Id""
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return await dbContext.InventoryReservations
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [InventoryReservations] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [OrderId] = {orderId}
                      AND [IsDeleted] = 0
                    ORDER BY [OrderItemId], [VariantId], [WarehouseId], [CreatedAt], [Id]")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
        }

        return await dbContext.InventoryReservations
            .IgnoreQueryFilters()
            .Where(reservation =>
                reservation.TenantId == tenantId &&
                reservation.OrderId == orderId &&
                !reservation.IsDeleted)
            .OrderBy(reservation => reservation.OrderItemId)
            .ThenBy(reservation => reservation.VariantId)
            .ThenBy(reservation => reservation.WarehouseId)
            .ThenBy(reservation => reservation.CreatedAt)
            .ThenBy(reservation => reservation.Id)
            .ToListAsync(cancellationToken);
    }

    private static async Task EnsureReservedStockRowsStillCoverReservationsAsync(
        ApplicationDbContext dbContext,
        Order order,
        IReadOnlyCollection<InventoryReservation> activeReservations,
        CancellationToken cancellationToken)
    {
        var reservationQuantitiesByStock = activeReservations
            .GroupBy(reservation => new
            {
                reservation.VariantId,
                reservation.WarehouseId
            })
            .ToDictionary(
                group => (group.Key.VariantId, group.Key.WarehouseId),
                group => group.Sum(reservation => reservation.RemainingReservedQuantity));

        if (reservationQuantitiesByStock.Count == 0)
        {
            return;
        }

        var variantIds = reservationQuantitiesByStock.Keys.Select(key => key.VariantId).Distinct().ToList();
        var warehouseIds = reservationQuantitiesByStock.Keys.Select(key => key.WarehouseId).Distinct().ToList();
        var stocks = await LoadReservedStocksForUpdateAsync(
            dbContext,
            order.TenantId,
            variantIds,
            warehouseIds,
            cancellationToken);

        foreach (var (key, requiredReservedQuantity) in reservationQuantitiesByStock)
        {
            var stock = stocks.SingleOrDefault(candidate =>
                candidate.VariantId == key.VariantId &&
                candidate.WarehouseId == key.WarehouseId);

            if (stock == null || stock.ReservedQuantity < requiredReservedQuantity)
            {
                throw BuildInventoryAuthorityLost(
                    order.Id,
                    $"StockReserved:{stock?.ReservedQuantity ?? 0m};ReservationReserved:{requiredReservedQuantity};Variant:{key.VariantId};Warehouse:{key.WarehouseId}",
                    "ProductVariantStocksReservedQuantity");
            }
        }
    }

    private static async Task<List<ProductVariantStock>> LoadReservedStocksForUpdateAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        IReadOnlyCollection<Guid> variantIds,
        IReadOnlyCollection<Guid> warehouseIds,
        CancellationToken cancellationToken)
    {
        var providerName = dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await dbContext.ProductVariantStocks
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""ProductVariantStocks""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""VariantId"" = ANY({variantIds.ToArray()})
                      AND ""WarehouseId"" = ANY({warehouseIds.ToArray()})
                      AND ""IsDeleted"" = FALSE
                    ORDER BY ""VariantId"", ""WarehouseId"", ""Id""
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return await dbContext.ProductVariantStocks
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [ProductVariantStocks] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .Where(stock => variantIds.Contains(stock.VariantId) && warehouseIds.Contains(stock.WarehouseId))
                .OrderBy(stock => stock.VariantId)
                .ThenBy(stock => stock.WarehouseId)
                .ThenBy(stock => stock.Id)
                .ToListAsync(cancellationToken);
        }

        return await dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .Where(stock =>
                stock.TenantId == tenantId &&
                variantIds.Contains(stock.VariantId) &&
                warehouseIds.Contains(stock.WarehouseId) &&
                !stock.IsDeleted)
            .OrderBy(stock => stock.VariantId)
            .ThenBy(stock => stock.WarehouseId)
            .ThenBy(stock => stock.Id)
            .ToListAsync(cancellationToken);
    }

    private static PaymentOrderInvalidStateException BuildInventoryAuthorityLost(
        Guid orderId,
        string current,
        string expected)
    {
        return new PaymentOrderInvalidStateException(
            orderId,
            $"InventoryAuthority:{current}",
            expected,
            "CHECKOUT_INVENTORY_AUTHORITY_LOST");
    }
}
