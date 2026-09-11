using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class InventoryReservationConcurrencyIntegrationTests
{
    [Fact]
    public async Task ReserveAsync_TenConcurrentSingleUnitOrders_ReservesExactlyAvailableStock()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;
        var seed = await SeedInventoryAsync(scope, tenantContext, tenantId, quantity: 10m);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, 10)
            .Select(_ => ReserveAsync(
                scope,
                tenantContext,
                tenantId,
                seed.VariantId,
                seed.WarehouseId,
                quantity: 1m,
                orderId: Guid.NewGuid(),
                startGate.Task))
            .ToArray();

        startGate.SetResult();
        var results = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.All(results, result => Assert.True(result.Succeeded, result.Error));

        var eleventh = await ReserveAsync(
            scope,
            tenantContext,
            tenantId,
            seed.VariantId,
            seed.WarehouseId,
            quantity: 1m,
            orderId: Guid.NewGuid(),
            Task.CompletedTask);

        Assert.False(eleventh.Succeeded);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.VariantId == seed.VariantId && item.WarehouseId == seed.WarehouseId);

        Assert.Equal(10m, stock.Quantity);
        Assert.Equal(10m, stock.ReservedQuantity);
        Assert.Equal(0m, stock.AvailableQuantity);
        Assert.Equal(10, await verifyContext.InventoryReservations.CountAsync(item => item.VariantId == seed.VariantId));
    }

    [Fact]
    public async Task ReserveAsync_TwoConcurrentSevenUnitOrders_OnlyOneSucceeds()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;
        var seed = await SeedInventoryAsync(scope, tenantContext, tenantId, quantity: 10m);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = new[]
        {
            ReserveAsync(scope, tenantContext, tenantId, seed.VariantId, seed.WarehouseId, 7m, Guid.NewGuid(), startGate.Task),
            ReserveAsync(scope, tenantContext, tenantId, seed.VariantId, seed.WarehouseId, 7m, Guid.NewGuid(), startGate.Task)
        };

        startGate.SetResult();
        var results = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Single(results, result => result.Succeeded);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.VariantId == seed.VariantId && item.WarehouseId == seed.WarehouseId);

        Assert.Equal(10m, stock.Quantity);
        Assert.Equal(7m, stock.ReservedQuantity);
        Assert.Equal(3m, stock.AvailableQuantity);
        Assert.Single(await verifyContext.InventoryReservations
            .Where(item => item.VariantId == seed.VariantId)
            .ToListAsync());
    }

    [Fact]
    public async Task ReserveAndRelease_WhenConcurrent_PreservesExactReservationMath()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;
        var seed = await SeedInventoryAsync(scope, tenantContext, tenantId, quantity: 10m);
        var existingOrderId = Guid.NewGuid();
        var newOrderId = Guid.NewGuid();

        var initial = await ReserveAsync(
            scope,
            tenantContext,
            tenantId,
            seed.VariantId,
            seed.WarehouseId,
            5m,
            existingOrderId,
            Task.CompletedTask);
        Assert.True(initial.Succeeded, initial.Error);

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reserveTask = ReserveAsync(
            scope,
            tenantContext,
            tenantId,
            seed.VariantId,
            seed.WarehouseId,
            5m,
            newOrderId,
            startGate.Task);
        var releaseTask = ReleaseOrderAsync(
            scope,
            tenantContext,
            tenantId,
            existingOrderId,
            startGate.Task);

        startGate.SetResult();
        var reserve = await reserveTask.WaitAsync(TimeSpan.FromSeconds(30));
        var released = await releaseTask.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(reserve.Succeeded, reserve.Error);
        Assert.True(released.Succeeded, released.Error);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.VariantId == seed.VariantId && item.WarehouseId == seed.WarehouseId);
        var reservations = await verifyContext.InventoryReservations
            .Where(item => item.VariantId == seed.VariantId)
            .ToListAsync();

        Assert.Equal(10m, stock.Quantity);
        Assert.Equal(5m, stock.ReservedQuantity);
        Assert.Equal(5m, stock.AvailableQuantity);
        Assert.Equal(5m, reservations.Sum(item => item.RemainingReservedQuantity));
    }

    [Fact]
    public async Task CommitAndRelease_WhenConcurrent_ProducesOneValidTerminalOutcome()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;
        var seed = await SeedInventoryAsync(scope, tenantContext, tenantId, quantity: 10m);
        var orderId = Guid.NewGuid();

        var initial = await ReserveAsync(
            scope,
            tenantContext,
            tenantId,
            seed.VariantId,
            seed.WarehouseId,
            5m,
            orderId,
            Task.CompletedTask);
        Assert.True(initial.Succeeded, initial.Error);

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commitTask = CommitOrderAsync(scope, tenantContext, tenantId, orderId, startGate.Task);
        var releaseTask = ReleaseOrderAsync(scope, tenantContext, tenantId, orderId, startGate.Task);

        startGate.SetResult();
        var outcomes = await Task.WhenAll(commitTask, releaseTask).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.All(outcomes, outcome => Assert.True(outcome.Succeeded, outcome.Error));

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.VariantId == seed.VariantId && item.WarehouseId == seed.WarehouseId);
        var reservation = await verifyContext.InventoryReservations
            .SingleAsync(item => item.OrderId == orderId);

        var committed = reservation.CommittedQuantity == 5m && reservation.ReleasedQuantity == 0m;
        var released = reservation.ReleasedQuantity == 5m && reservation.CommittedQuantity == 0m;

        Assert.True(committed || released);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(committed ? 5m : 10m, stock.Quantity);
        Assert.Equal(stock.Quantity, stock.AvailableQuantity);
    }

    [Fact]
    public async Task ReserveAsync_ConcurrentMultiWarehouseAllocation_DoesNotCrossOrOverAllocateRows()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;
        var seed = await SeedInventoryAsync(scope, tenantContext, tenantId, quantity: 5m);
        var secondWarehouseId = Guid.NewGuid();

        await using (var context = scope.CreateDbContext(tenantContext))
        {
            context.Warehouses.Add(new Warehouse
            {
                Id = secondWarehouseId,
                TenantId = tenantId,
                Name = "C-2 Disposable Warehouse B",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            context.ProductVariantStocks.Add(new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                WarehouseId = secondWarehouseId,
                VariantId = seed.VariantId,
                Quantity = 5m,
                ReservedQuantity = 0m,
                DamagedQuantity = 0m,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new[]
        {
            ReserveAsync(scope, tenantContext, tenantId, seed.VariantId, null, 5m, Guid.NewGuid(), startGate.Task),
            ReserveAsync(scope, tenantContext, tenantId, seed.VariantId, null, 5m, Guid.NewGuid(), startGate.Task)
        };

        startGate.SetResult();
        var results = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.All(results, result => Assert.True(result.Succeeded, result.Error));

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var stocks = await verifyContext.ProductVariantStocks
            .Where(item => item.VariantId == seed.VariantId)
            .OrderBy(item => item.WarehouseId)
            .ToListAsync();
        var reservations = await verifyContext.InventoryReservations
            .Where(item => item.VariantId == seed.VariantId)
            .ToListAsync();

        Assert.Equal(2, stocks.Count);
        Assert.All(stocks, stock =>
        {
            Assert.Equal(5m, stock.Quantity);
            Assert.Equal(5m, stock.ReservedQuantity);
            Assert.Equal(0m, stock.AvailableQuantity);
        });
        Assert.Equal(2, reservations.Count);
        Assert.Equal(2, reservations.Select(item => item.WarehouseId).Distinct().Count());
        Assert.All(reservations, reservation => Assert.Equal(tenantId, reservation.TenantId));
    }

    [Fact]
    public async Task ReacquireMarketplaceReservations_WhenConcurrent_DoesNotDuplicateAuthority()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;
        var seed = await SeedReleasedMarketplaceReservationAsync(scope, tenantContext, tenantId, quantity: 2m);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new[]
        {
            ReacquireAsync(scope, tenantContext, seed.OrderId, "reacquire-a", startGate.Task),
            ReacquireAsync(scope, tenantContext, seed.OrderId, "reacquire-b", startGate.Task)
        };

        startGate.SetResult();
        var outcomes = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Contains(outcomes, outcome => outcome.Succeeded);
        Assert.All(
            outcomes.Where(outcome => !outcome.Succeeded),
            outcome => Assert.Contains("could not serialize access", outcome.Error, StringComparison.OrdinalIgnoreCase));

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var stock = await verifyContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(item => item.VariantId == seed.VariantId && item.WarehouseId == seed.WarehouseId);
        var activeReservations = await verifyContext.InventoryReservations
            .IgnoreQueryFilters()
            .Where(item => item.OrderId == seed.OrderId && item.Status == InventoryReservationStatus.Active)
            .ToListAsync();

        Assert.Single(activeReservations);
        Assert.Equal(2m, activeReservations.Single().RemainingReservedQuantity);
        Assert.Equal(2m, stock.ReservedQuantity);
        Assert.Equal(3m, stock.AvailableQuantity);
    }

    private static async Task<AttemptResult> ReserveAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        Guid tenantId,
        Guid variantId,
        Guid? warehouseId,
        decimal quantity,
        Guid orderId,
        Task startGate)
    {
        await startGate;
        await using var context = scope.CreateDbContext(tenantContext);
        var repository = CreateRepository(context);

        try
        {
            var reserved = await repository.ReserveAsync(
                tenantId,
                variantId,
                warehouseId,
                quantity,
                orderId,
                DateTime.UtcNow.AddMinutes(15));
            return new AttemptResult(reserved, reserved ? null : "Insufficient stock");
        }
        catch (Exception ex)
        {
            return new AttemptResult(false, ex.ToString());
        }
    }

    private static async Task<AttemptResult> ReleaseOrderAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        Guid tenantId,
        Guid orderId,
        Task startGate)
    {
        await startGate;
        await using var context = scope.CreateDbContext(tenantContext);
        var repository = CreateRepository(context);

        try
        {
            await repository.ReleaseOrderReservationsAsync(tenantId, orderId);
            return new AttemptResult(true, null);
        }
        catch (Exception ex)
        {
            return new AttemptResult(false, ex.ToString());
        }
    }

    private static async Task<AttemptResult> CommitOrderAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        Guid tenantId,
        Guid orderId,
        Task startGate)
    {
        await startGate;
        await using var context = scope.CreateDbContext(tenantContext);
        var repository = CreateRepository(context);

        try
        {
            await repository.CommitOrderReservationsAsync(tenantId, orderId);
            return new AttemptResult(true, null);
        }
        catch (Exception ex)
        {
            return new AttemptResult(false, ex.ToString());
        }
    }

    private static async Task<AttemptResult> ReacquireAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        Guid orderId,
        string correlationId,
        Task startGate)
    {
        await startGate;
        await using var context = scope.CreateDbContext(tenantContext);
        var service = new InventoryConsistencyService(
            context,
            NullLogger<InventoryConsistencyService>.Instance);

        try
        {
            await service.ReacquireMarketplaceOrderReservationsForPaymentAsync(orderId, correlationId);
            return new AttemptResult(true, null);
        }
        catch (Exception ex)
        {
            return new AttemptResult(false, ex.ToString());
        }
    }

    private static ProductVariantStockRepository CreateRepository(ApplicationDbContext context) =>
        new(context, NullLogger<ProductVariantStockRepository>.Instance);

    private static async Task<InventorySeed> SeedInventoryAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        Guid tenantId,
        decimal quantity)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        var warehouseId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "C-2 Inventory Concurrency Tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        context.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "C-2 Disposable Warehouse",
            IsActive = true,
            CreatedAt = nowUtc
        });
        context.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "C-2 Disposable Category",
            CreatedAt = nowUtc
        });
        context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = categoryId,
            Name = "C-2 Disposable Product",
            SalePrice = 100m,
            OnlinePrice = 100m,
            PurchasePrice = 60m,
            TrackStock = true,
            AllowBackorder = false,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            CreatedAt = nowUtc
        });
        context.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = $"C2-{variantId:N}",
            Price = 100m,
            OnlinePrice = 100m,
            CostPrice = 60m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = nowUtc
        });
        context.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarehouseId = warehouseId,
            VariantId = variantId,
            Quantity = quantity,
            ReservedQuantity = 0m,
            DamagedQuantity = 0m,
            CreatedAt = nowUtc
        });

        await context.SaveChangesAsync();
        return new InventorySeed(warehouseId, productId, variantId);
    }

    private static async Task<MarketplaceReservationSeed> SeedReleasedMarketplaceReservationAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        Guid tenantId,
        decimal quantity)
    {
        var inventory = await SeedInventoryAsync(scope, tenantContext, tenantId, quantity: 5m);
        await using var context = scope.CreateDbContext(tenantContext);
        var customer = CommerceHubCustomer.Create(
            $"reacquire-{Guid.NewGuid():N}@example.com",
            "Concurrent Reacquisition Customer",
            "01000000000",
            "hash");
        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Customer = customer,
            OrderNumber = $"CH-RQ-{Guid.NewGuid():N}"[..18],
            FullName = "Concurrent Reacquisition Customer",
            PhoneNumber = "01000000000",
            AddressLine = "1 Concurrency Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "PAYMOB",
            PaymentMethodName = "Paymob",
            Subtotal = quantity * 100m,
            TotalAmount = quantity * 100m,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        };
        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = $"SO-RQ-{Guid.NewGuid():N}"[..18],
            StoreId = tenantId,
            StoreName = "Concurrent Reacquisition Store",
            Currency = "EGP",
            Subtotal = quantity * 100m,
            Total = quantity * 100m,
            PaymentMethod = CommerceHubPaymentMethod.Online,
            PaymentMethodCode = "PAYMOB",
            PaymentMethodName = "Paymob",
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        };
        var item = new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = inventory.ProductId,
            VariantId = inventory.VariantId,
            ProductName = "Concurrent Reacquisition Product",
            Quantity = decimal.ToInt32(quantity),
            UnitPrice = 100m,
            Total = quantity * 100m,
            Status = CommerceHubOrderItemStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        subOrder.Items.Add(item);
        order.SubOrders.Add(subOrder);
        context.CommerceHubCustomers.Add(customer);
        context.CommerceHubMarketplaceOrders.Add(order);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        {
            await context.SaveChangesAsync();
        }

        var service = new InventoryConsistencyService(
            context,
            NullLogger<InventoryConsistencyService>.Instance);
        var reserved = await service.ReserveOrderItemAsync(new InventoryReservationRequest(
            tenantId,
            order.Id,
            subOrder.Id,
            item.Id,
            inventory.ProductId,
            inventory.VariantId,
            quantity,
            inventory.WarehouseId,
            DateTime.UtcNow.AddMinutes(10),
            Guid.NewGuid(),
            "reacquire-original",
            "CommerceHubCheckout"));
        Assert.True(reserved);
        await service.ReleaseOrderItemReservationsAsync(
            tenantId,
            item.Id,
            null,
            Guid.NewGuid(),
            "reacquire-release",
            "InventoryReservationCleanup",
            CancellationToken.None);

        return new MarketplaceReservationSeed(
            order.Id,
            inventory.WarehouseId,
            inventory.VariantId);
    }

    private readonly record struct InventorySeed(Guid WarehouseId, Guid ProductId, Guid VariantId);
    private readonly record struct MarketplaceReservationSeed(Guid OrderId, Guid WarehouseId, Guid VariantId);
    private readonly record struct AttemptResult(bool Succeeded, string? Error);
}
