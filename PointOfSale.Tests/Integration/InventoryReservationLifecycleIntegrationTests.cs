using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Discounts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Jobs;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed partial class InventoryReservationLifecycleIntegrationTests
{
    [Fact]
    public async Task CheckoutFailureAfterReserve_ShouldReleaseReservation_AndClearReservedQuantity()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var seed = await SeedInventoryAsync(context, tenantId, initialQuantity: 9m);
        var plannedOrderId = Guid.NewGuid();

        var reserved = await repository.ReserveAsync(
            tenantId,
            seed.VariantId,
            seed.WarehouseId,
            quantity: 3m,
            orderId: plannedOrderId,
            expiresAtUtc: DateTime.UtcNow.AddMinutes(15));

        Assert.True(reserved);

        var released = await repository.ReleaseOrderReservationsAsync(tenantId, plannedOrderId);
        Assert.True(released);

        // Verify idempotent behavior - second release should return false (no-op)
        var releasedSecond = await repository.ReleaseOrderReservationsAsync(tenantId, plannedOrderId);
        Assert.False(releasedSecond);

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == seed.VariantId);
        var reservation = await verifyContext.InventoryReservations
            .SingleAsync(item => item.TenantId == tenantId && item.OrderId == plannedOrderId);
        var movements = await verifyContext.StockMovements
            .Where(item => item.TenantId == tenantId && item.ReferenceId == plannedOrderId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();

        Assert.Equal(9m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(3m, reservation.ReservedQuantity);
        Assert.Equal(3m, reservation.ReleasedQuantity);
        Assert.Equal(0m, reservation.CommittedQuantity);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);

        Assert.Equal(2, movements.Count);
        Assert.Equal(InventoryEventType.StockReserved, movements[0].InventoryEventType);
        Assert.Equal(3m, movements[0].ReservedQuantityChange);
        Assert.Equal(InventoryEventType.StockReleased, movements[1].InventoryEventType);
        Assert.Equal(-3m, movements[1].ReservedQuantityChange);
        Assert.All(movements, movement => Assert.Equal(reservation.Id, movement.ReservationId));
    }

    [Fact]
    public async Task PaymentSuccess_ShouldCommitReservation_AndDecreaseStock()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var outbox = CreateAccountingOutboxMock();
        var service = CreateLifecycleService(context, repository, outbox);
        var scenario = await SeedReservedOrderAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentMethodName: "Stripe",
            paymentMethod: PaymentMethod.Card,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 3);

        var result = await service.CaptureOnlinePaymentAsync(
            scenario.OrderId,
            gateway: "stripe",
            paymentIntentId: "pi_inventory_success",
            providerAmount: scenario.Total,
            providerCurrency: "EGP");

        Assert.True(result.Success, result.ErrorMessage);

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);
        var reservation = await verifyContext.InventoryReservations
            .SingleAsync(item => item.TenantId == tenantId && item.OrderId == scenario.OrderId);
        var order = await verifyContext.Orders
            .SingleAsync(item => item.Id == scenario.OrderId);
        var movements = await verifyContext.StockMovements
            .Where(item => item.TenantId == tenantId && item.ReferenceId == scenario.OrderId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();

        Assert.Equal(6m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(3m, reservation.CommittedQuantity);
        Assert.Equal(0m, reservation.ReleasedQuantity);
        Assert.Equal(InventoryReservationStatus.Committed, reservation.Status);
        Assert.Equal(PaymentStatus.Collected, order.PaymentStatus);
        Assert.Equal(CheckoutState.Paid, order.CheckoutState);

        Assert.Contains(movements, movement =>
            movement.InventoryEventType == InventoryEventType.StockReserved &&
            movement.QuantityChange == 0m &&
            movement.ReservedQuantityChange == 3m);
        Assert.Contains(movements, movement =>
            movement.InventoryEventType == InventoryEventType.StockDecreased &&
            movement.QuantityChange == -3m &&
            movement.ReservedQuantityChange == -3m);
    }

    [Fact]
    public async Task PaymentSuccess_AfterCatalogAndWarehouseDeactivation_CommitsHistoricalReservation()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var outbox = CreateAccountingOutboxMock();
        var service = CreateLifecycleService(context, repository, outbox);
        var scenario = await SeedReservedOrderAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentMethodName: "Stripe",
            paymentMethod: PaymentMethod.Card,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 2);

        var variant = await context.ProductVariants
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == tenantId && item.Id == scenario.VariantId);
        var product = await context.Products
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == tenantId && item.Id == variant.ProductId);
        var stock = await context.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);
        var warehouse = await context.Warehouses
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == tenantId && item.Id == stock.WarehouseId);
        product.IsActive = false;
        variant.IsActive = false;
        warehouse.IsActive = false;
        await context.SaveChangesAsync();

        var result = await service.CaptureOnlinePaymentAsync(
            scenario.OrderId,
            gateway: "stripe",
            paymentIntentId: "pi_inventory_historical_commit",
            providerAmount: scenario.Total,
            providerCurrency: "EGP");

        Assert.True(result.Success, result.ErrorMessage);

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var persistedStock = await verifyContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);
        var reservation = await verifyContext.InventoryReservations
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == tenantId && item.OrderId == scenario.OrderId);
        var order = await verifyContext.Orders
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == tenantId && item.Id == scenario.OrderId);

        Assert.Equal(7m, persistedStock.Quantity);
        Assert.Equal(0m, persistedStock.ReservedQuantity);
        Assert.Equal(2m, reservation.CommittedQuantity);
        Assert.Equal(InventoryReservationStatus.Committed, reservation.Status);
        Assert.Equal(PaymentStatus.Collected, order.PaymentStatus);
    }

    [Fact]
    public async Task PaymentSuccess_WhenCallerAlreadyOwnsTransaction_ShouldJoinAmbientTransaction()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var outbox = CreateAccountingOutboxMock();
        var service = CreateLifecycleService(context, repository, outbox);
        var scenario = await SeedReservedOrderAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentMethodName: "Stripe",
            paymentMethod: PaymentMethod.Card,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 3);

        await using var callerTransaction = await context.Database.BeginTransactionAsync();

        var result = await service.CaptureOnlinePaymentAsync(
            scenario.OrderId,
            gateway: "stripe",
            paymentIntentId: "pi_inventory_ambient_tx",
            providerAmount: scenario.Total,
            providerCurrency: "EGP");

        Assert.True(result.Success, result.ErrorMessage);

        await callerTransaction.CommitAsync();

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);
        var reservation = await verifyContext.InventoryReservations
            .SingleAsync(item => item.TenantId == tenantId && item.OrderId == scenario.OrderId);
        var order = await verifyContext.Orders
            .SingleAsync(item => item.Id == scenario.OrderId);

        Assert.Equal(6m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(3m, reservation.CommittedQuantity);
        Assert.Equal(InventoryReservationStatus.Committed, reservation.Status);
        Assert.Equal(PaymentStatus.Collected, order.PaymentStatus);
    }

    [Fact]
    public async Task PaymentSuccess_WhenCallerRollsBackTransaction_ShouldNotPersistChanges()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var outbox = CreateAccountingOutboxMock();
        var service = CreateLifecycleService(context, repository, outbox);
        var scenario = await SeedReservedOrderAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentMethodName: "Stripe",
            paymentMethod: PaymentMethod.Card,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 3);

        // Capture initial state before transaction
        var initialStock = await context.ProductVariantStocks
            .AsNoTracking()
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);
        var initialReservation = await context.InventoryReservations
            .AsNoTracking()
            .SingleAsync(item => item.TenantId == tenantId && item.OrderId == scenario.OrderId);
        var initialOrder = await context.Orders
            .AsNoTracking()
            .SingleAsync(item => item.Id == scenario.OrderId);

        await using var callerTransaction = await context.Database.BeginTransactionAsync();

        var result = await service.CaptureOnlinePaymentAsync(
            scenario.OrderId,
            gateway: "stripe",
            paymentIntentId: "pi_inventory_rollback_tx",
            providerAmount: scenario.Total,
            providerCurrency: "EGP");

        Assert.True(result.Success, result.ErrorMessage);

        // Rollback instead of commit
        await callerTransaction.RollbackAsync();

        // Verify with fresh context that no changes were persisted
        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);
        var reservation = await verifyContext.InventoryReservations
            .SingleAsync(item => item.TenantId == tenantId && item.OrderId == scenario.OrderId);
        var order = await verifyContext.Orders
            .SingleAsync(item => item.Id == scenario.OrderId);

        // Assert: All state remains unchanged after rollback
        Assert.Equal(initialStock.Quantity, stock.Quantity); // Still 9m
        Assert.Equal(initialStock.ReservedQuantity, stock.ReservedQuantity); // Still 3m
        Assert.Equal(initialReservation.CommittedQuantity, reservation.CommittedQuantity); // Still 0m
        Assert.Equal(initialReservation.Status, reservation.Status); // Still Reserved
        Assert.Equal(initialOrder.PaymentStatus, order.PaymentStatus); // Still Pending
    }

    [Fact]
    public async Task PaymentFailure_ShouldReleaseReservation_AndClearReservedQuantity()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var outbox = CreateAccountingOutboxMock();
        var service = CreateLifecycleService(context, repository, outbox);
        var scenario = await SeedReservedOrderAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentMethodName: "Stripe",
            paymentMethod: PaymentMethod.Card,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 2);

        var result = await service.MarkPaymentFailedAsync(
            scenario.OrderId,
            reason: "gateway timeout");

        Assert.True(result.Success, result.ErrorMessage);

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);
        var reservation = await verifyContext.InventoryReservations
            .SingleAsync(item => item.TenantId == tenantId && item.OrderId == scenario.OrderId);
        var order = await verifyContext.Orders
            .SingleAsync(item => item.Id == scenario.OrderId);

        Assert.Equal(9m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(0m, reservation.CommittedQuantity);
        Assert.Equal(2m, reservation.ReleasedQuantity);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
        Assert.Equal(PaymentStatus.Failed, order.PaymentStatus);
        Assert.Equal(CheckoutState.Failed, order.CheckoutState);
    }

    [Fact]
    public async Task PaymentSuccessAfterReservationExpiry_ShouldQuarantine_AndNotMarkPaid()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var outbox = CreateAccountingOutboxMock();
        var service = CreateLifecycleService(context, repository, outbox);
        var scenario = await SeedReservedOrderWithPaymentIntentAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentMethodName: "Stripe",
            paymentMethod: PaymentMethod.Card,
            provider: PaymentProvider.Stripe,
            paymentIntentStatus: PaymentIntentStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 2);

        await MarkReservationsStaleAsync(context, scenario.OrderId, minutesAgo: 5);

        var result = await service.HandlePaymentResultAsync(
            CreatePaymentResultRequest(
                scenario,
                PaymentLifecycleOutcome.Succeeded,
                PaymentLifecycleSource.GatewayWebhook,
                gateway: "stripe",
                webhookEventId: "evt-late-success"));

        var replayResult = await service.HandlePaymentResultAsync(
            CreatePaymentResultRequest(
                scenario,
                PaymentLifecycleOutcome.Succeeded,
                PaymentLifecycleSource.GatewayWebhook,
                gateway: "stripe",
                webhookEventId: "evt-late-success"));

        Assert.False(result.Success);
        Assert.True(result.RequiresManualReview);
        Assert.Equal("RESERVATION_EXPIRED", result.FailureCode);
        Assert.Equal(PaymentStatus.Expired, result.NewStatus);
        Assert.False(replayResult.Success);
        Assert.True(replayResult.RequiresManualReview);
        Assert.Equal(result.FailureCode, replayResult.FailureCode);
        Assert.Equal(result.ErrorMessage, replayResult.ErrorMessage);
        Assert.Equal(result.NewStatus, replayResult.NewStatus);

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var order = await verifyContext.Orders.SingleAsync(item => item.Id == scenario.OrderId);
        var paymentIntent = await verifyContext.PaymentIntents.SingleAsync(item => item.Id == scenario.PaymentIntentId);
        var reservation = await verifyContext.InventoryReservations.SingleAsync(item => item.OrderId == scenario.OrderId);
        var stock = await verifyContext.ProductVariantStocks.SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);

        Assert.Equal(PaymentStatus.Expired, order.PaymentStatus);
        Assert.Equal(CheckoutState.Expired, order.CheckoutState);
        Assert.False(order.IsPaid);
        Assert.Null(order.PaidAt);
        Assert.Equal(OrderStatus.OnHold, order.Status);
        Assert.Equal(PaymentIntentStatus.Suspicious, paymentIntent.Status);
        Assert.Equal("RESERVATION_EXPIRED", paymentIntent.FailureCode);
        Assert.False(paymentIntent.InventoryCommitted);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
        Assert.Equal(2m, reservation.ReleasedQuantity);
        Assert.Equal(9m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);

        outbox.Verify(
            item => item.EnqueueOnlinePaymentAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        outbox.Verify(
            item => item.EnqueueUnresolvedOnlinePaymentAsync(
                tenantId,
                scenario.Total,
                "EGP",
                "stripe",
                "evt-late-success",
                scenario.OrderId,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnlineWebhook_WithLowerProviderAmount_ShouldQuarantine_AndNotCommitReservation()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var outbox = CreateAccountingOutboxMock();
        var service = CreateLifecycleService(context, repository, outbox);
        var scenario = await SeedReservedOrderWithPaymentIntentAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentMethodName: "Stripe",
            paymentMethod: PaymentMethod.Card,
            provider: PaymentProvider.Stripe,
            paymentIntentStatus: PaymentIntentStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 2);

        var result = await service.HandlePaymentResultAsync(
            CreatePaymentResultRequest(
                scenario,
                PaymentLifecycleOutcome.Succeeded,
                PaymentLifecycleSource.GatewayWebhook,
                gateway: "stripe",
                webhookEventId: "evt-underpaid",
                providerAmount: scenario.Total - 1m,
                providerCurrency: "EGP"));

        Assert.False(result.Success);
        Assert.True(result.RequiresManualReview);
        Assert.Equal("PAYMENT_AMOUNT_MISMATCH", result.FailureCode);
        Assert.Equal(PaymentStatus.AmountMismatch, result.NewStatus);

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var order = await verifyContext.Orders.SingleAsync(item => item.Id == scenario.OrderId);
        var paymentIntent = await verifyContext.PaymentIntents.SingleAsync(item => item.Id == scenario.PaymentIntentId);
        var reservation = await verifyContext.InventoryReservations.SingleAsync(item => item.OrderId == scenario.OrderId);
        var stock = await verifyContext.ProductVariantStocks.SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);

        Assert.Equal(PaymentStatus.AmountMismatch, order.PaymentStatus);
        Assert.False(order.IsPaid);
        Assert.Null(order.PaidAt);
        Assert.Equal(PaymentIntentStatus.AmountMismatch, paymentIntent.Status);
        Assert.False(paymentIntent.InventoryCommitted);
        Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
        Assert.Equal(2m, stock.ReservedQuantity);

        outbox.Verify(
            item => item.EnqueueOnlinePaymentAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        outbox.Verify(
            item => item.EnqueueUnresolvedOnlinePaymentAsync(
                tenantId,
                scenario.Total - 1m,
                "EGP",
                "stripe",
                "evt-underpaid",
                scenario.OrderId,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnlineWebhook_WithProviderCurrencyMismatch_ShouldQuarantine_AndNotMarkPaid()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var outbox = CreateAccountingOutboxMock();
        var service = CreateLifecycleService(context, repository, outbox);
        var scenario = await SeedReservedOrderWithPaymentIntentAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "PAYMOB",
            paymentMethodName: "Paymob",
            paymentMethod: PaymentMethod.Card,
            provider: PaymentProvider.Paymob,
            paymentIntentStatus: PaymentIntentStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 1);

        var result = await service.HandlePaymentResultAsync(
            CreatePaymentResultRequest(
                scenario,
                PaymentLifecycleOutcome.Succeeded,
                PaymentLifecycleSource.GatewayWebhook,
                gateway: "paymob",
                webhookEventId: "evt-currency-mismatch",
                providerAmount: scenario.Total,
                providerCurrency: "USD"));

        Assert.False(result.Success);
        Assert.True(result.RequiresManualReview);
        Assert.Equal("PAYMENT_AMOUNT_MISMATCH", result.FailureCode);

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var order = await verifyContext.Orders.SingleAsync(item => item.Id == scenario.OrderId);
        var paymentIntent = await verifyContext.PaymentIntents.SingleAsync(item => item.Id == scenario.PaymentIntentId);

        Assert.Equal(PaymentStatus.AmountMismatch, order.PaymentStatus);
        Assert.False(order.IsPaid);
        Assert.Equal(PaymentIntentStatus.AmountMismatch, paymentIntent.Status);

        outbox.Verify(
            item => item.EnqueueOnlinePaymentAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        outbox.Verify(
            item => item.EnqueueUnresolvedOnlinePaymentAsync(
                tenantId,
                scenario.Total,
                "USD",
                "paymob",
                "evt-currency-mismatch",
                scenario.OrderId,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RetrySameOrder_ShouldNotDuplicateCommit()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var outbox = CreateAccountingOutboxMock();
        var service = CreateLifecycleService(context, repository, outbox);
        var scenario = await SeedReservedOrderAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "COD",
            paymentMethodName: "Cash on Delivery",
            paymentMethod: PaymentMethod.Cash,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Delivered,
            orderStatus: OrderStatus.Shipped,
            quantity: 3);

        var firstResult = await service.MarkOfflineCollectionDeliveredAsync(tenantId, scenario.OrderId, deliveryReference: "COD-1");
        var secondResult = await service.MarkOfflineCollectionDeliveredAsync(tenantId, scenario.OrderId, deliveryReference: "COD-1");

        Assert.True(firstResult.Success, firstResult.ErrorMessage);
        Assert.True(secondResult.Success, secondResult.ErrorMessage);

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);
        var reservation = await verifyContext.InventoryReservations
            .SingleAsync(item => item.TenantId == tenantId && item.OrderId == scenario.OrderId);

        Assert.Equal(6m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(3m, reservation.CommittedQuantity);
        Assert.Equal(0m, reservation.ReleasedQuantity);
        Assert.Equal(InventoryReservationStatus.Committed, reservation.Status);

        outbox.Verify(
            item => item.EnqueueCodDeliveredAsync(tenantId, scenario.OrderId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StripeWebhookReplay_ShouldNotDoubleCommitReservation()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var outbox = CreateAccountingOutboxMock();
        var service = CreateLifecycleService(context, repository, outbox);
        var scenario = await SeedReservedOrderWithPaymentIntentAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentMethodName: "Stripe",
            paymentMethod: PaymentMethod.Card,
            provider: PaymentProvider.Stripe,
            paymentIntentStatus: PaymentIntentStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 2);

        var firstResult = await service.HandlePaymentResultAsync(
            CreatePaymentResultRequest(
                scenario,
                PaymentLifecycleOutcome.Succeeded,
                PaymentLifecycleSource.GatewayWebhook,
                gateway: "stripe",
                webhookEventId: "evt-stripe-success"));
        var secondResult = await service.HandlePaymentResultAsync(
            CreatePaymentResultRequest(
                scenario,
                PaymentLifecycleOutcome.Succeeded,
                PaymentLifecycleSource.GatewayWebhook,
                gateway: "stripe",
                webhookEventId: "evt-stripe-success"));

        Assert.True(firstResult.Success, firstResult.ErrorMessage);
        Assert.True(secondResult.Success, secondResult.ErrorMessage);
        Assert.True(secondResult.WasIdempotent);
        Assert.False(secondResult.StateChanged);
        Assert.Equal(PaymentInventoryAction.Commit, secondResult.InventoryAction);

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);
        var reservation = await verifyContext.InventoryReservations
            .SingleAsync(item => item.TenantId == tenantId && item.OrderId == scenario.OrderId);
        var paymentIntent = await verifyContext.PaymentIntents
            .SingleAsync(item => item.Id == scenario.PaymentIntentId);

        Assert.Equal(7m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(2m, reservation.CommittedQuantity);
        Assert.Equal(0m, reservation.ReleasedQuantity);
        Assert.Equal(InventoryReservationStatus.Committed, reservation.Status);
        Assert.Equal(PaymentIntentStatus.Paid, paymentIntent.Status);
        Assert.True(paymentIntent.InventoryCommitted);

        outbox.Verify(
            item => item.EnqueueOnlinePaymentAsync(
                tenantId,
                scenario.OrderId,
                scenario.PaymentReference,
                "stripe",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DuplicateFailureWebhook_ShouldBeIdempotentAndNotDoubleRelease()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var outbox = CreateAccountingOutboxMock();
        var service = CreateLifecycleService(context, repository, outbox);
        var scenario = await SeedReservedOrderWithPaymentIntentAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentMethodName: "Stripe",
            paymentMethod: PaymentMethod.Card,
            provider: PaymentProvider.Stripe,
            paymentIntentStatus: PaymentIntentStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 2);

        var firstResult = await service.HandlePaymentResultAsync(
            CreatePaymentResultRequest(
                scenario,
                PaymentLifecycleOutcome.Failed,
                PaymentLifecycleSource.GatewayWebhook,
                gateway: "stripe",
                webhookEventId: "evt-stripe-failed",
                reason: "gateway timeout"));
        var secondResult = await service.HandlePaymentResultAsync(
            CreatePaymentResultRequest(
                scenario,
                PaymentLifecycleOutcome.Failed,
                PaymentLifecycleSource.GatewayWebhook,
                gateway: "stripe",
                webhookEventId: "evt-stripe-failed",
                reason: "gateway timeout"));

        Assert.True(firstResult.Success, firstResult.ErrorMessage);
        Assert.True(secondResult.Success, secondResult.ErrorMessage);
        Assert.True(secondResult.WasIdempotent);
        Assert.True(secondResult.StateChanged);
        Assert.Equal(PaymentInventoryAction.Release, secondResult.InventoryAction);

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);
        var reservation = await verifyContext.InventoryReservations
            .SingleAsync(item => item.TenantId == tenantId && item.OrderId == scenario.OrderId);
        var order = await verifyContext.Orders
            .SingleAsync(item => item.Id == scenario.OrderId);
        var paymentIntent = await verifyContext.PaymentIntents
            .SingleAsync(item => item.Id == scenario.PaymentIntentId);

        Assert.Equal(9m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(0m, reservation.CommittedQuantity);
        Assert.Equal(2m, reservation.ReleasedQuantity);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
        Assert.Equal(PaymentStatus.Failed, order.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Failed, paymentIntent.Status);
        Assert.False(paymentIntent.InventoryCommitted);
    }

    [Fact]
    public async Task PaymentSuccessWithUncommittedInventory_ShouldReplayAndCommitInventory()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var outbox = CreateAccountingOutboxMock();
        var service = CreateLifecycleService(context, repository, outbox);
        
        // Seed order with payment intent
        var scenario = await SeedReservedOrderWithPaymentIntentAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentMethodName: "Stripe",
            paymentMethod: PaymentMethod.Card,
            provider: PaymentProvider.Stripe,
            paymentIntentStatus: PaymentIntentStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 2);

        // Manually set payment as successful but with InventoryCommitted = false
        // This simulates a failure after payment succeeded but before inventory was committed
        var order = await context.Orders.SingleAsync(o => o.Id == scenario.OrderId);
        var paymentIntent = await context.PaymentIntents.SingleAsync(pi => pi.Id == scenario.PaymentIntentId);
        
        order.PaymentStatus = PaymentStatus.Collected;
        order.IsPaid = true;
        order.PaidAt = DateTime.UtcNow;
        order.Status = OrderStatus.Confirmed;
        paymentIntent.Status = PaymentIntentStatus.Paid;
        paymentIntent.InventoryCommitted = false; // Key: inventory not committed despite successful payment
        
        await context.SaveChangesAsync();

        // Now replay the payment - should trigger the recovery branch
        var replayResult = await service.HandlePaymentResultAsync(
            CreatePaymentResultRequest(
                scenario,
                PaymentLifecycleOutcome.Succeeded,
                PaymentLifecycleSource.GatewayWebhook,
                gateway: "stripe",
                webhookEventId: "evt-stripe-replay"));

        // Verify the replay was successful and committed inventory
        Assert.True(replayResult.Success, replayResult.ErrorMessage);
        Assert.True(replayResult.WasIdempotent, "Should be marked as idempotent since payment was already successful");
        Assert.True(replayResult.StateChanged, "State should have changed because inventory was committed");
        Assert.Equal(PaymentInventoryAction.Commit, replayResult.InventoryAction);

        // Verify database state
        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var verifyStock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);
        var verifyReservation = await verifyContext.InventoryReservations
            .SingleAsync(item => item.TenantId == tenantId && item.OrderId == scenario.OrderId);
        var verifyPaymentIntent = await verifyContext.PaymentIntents
            .SingleAsync(item => item.Id == scenario.PaymentIntentId);

        // Verify inventory was committed
        Assert.Equal(7m, verifyStock.Quantity);
        Assert.Equal(0m, verifyStock.ReservedQuantity);
        Assert.Equal(2m, verifyReservation.CommittedQuantity);
        Assert.Equal(0m, verifyReservation.ReleasedQuantity);
        Assert.Equal(InventoryReservationStatus.Committed, verifyReservation.Status);
        
        // Verify InventoryCommitted flag is now true
        Assert.True(verifyPaymentIntent.InventoryCommitted, "InventoryCommitted should now be true after replay");
        Assert.Equal(PaymentIntentStatus.Paid, verifyPaymentIntent.Status);

        // Verify outbox was NOT called again (idempotent)
        outbox.Verify(
            item => item.EnqueueOnlinePaymentAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never, 
            "Outbox should not be called during replay since payment was already processed");
    }

    [Theory]
    [InlineData(PaymentLifecycleSource.GatewayWebhook, "STRIPE", "Stripe", PaymentProvider.Stripe, "stripe")]
    [InlineData(PaymentLifecycleSource.GatewayWebhook, "PAYMOB", "Paymob", PaymentProvider.Paymob, "paymob")]
    public async Task StripeAndPaymobWebhooks_ShouldUseSameLifecycleCommitFlow(
        PaymentLifecycleSource source,
        string paymentMethodCode,
        string paymentMethodName,
        PaymentProvider provider,
        string gateway)
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var outbox = CreateAccountingOutboxMock();
        var service = CreateLifecycleService(context, repository, outbox);
        var scenario = await SeedReservedOrderWithPaymentIntentAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode,
            paymentMethodName,
            PaymentMethod.Card,
            provider,
            PaymentIntentStatus.Pending,
            PaymentStatus.Pending,
            FulfillmentStatus.Created,
            OrderStatus.Pending,
            quantity: 1);

        var result = await service.HandlePaymentResultAsync(
            CreatePaymentResultRequest(
                scenario,
                PaymentLifecycleOutcome.Succeeded,
                source,
                gateway,
                $"evt-{gateway}-success"));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(PaymentInventoryAction.Commit, result.InventoryAction);

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);
        var reservation = await verifyContext.InventoryReservations
            .SingleAsync(item => item.TenantId == tenantId && item.OrderId == scenario.OrderId);
        var order = await verifyContext.Orders
            .SingleAsync(item => item.Id == scenario.OrderId);
        var paymentIntent = await verifyContext.PaymentIntents
            .SingleAsync(item => item.Id == scenario.PaymentIntentId);

        Assert.Equal(8m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(1m, reservation.CommittedQuantity);
        Assert.Equal(InventoryReservationStatus.Committed, reservation.Status);
        Assert.Equal(PaymentStatus.Collected, order.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Paid, paymentIntent.Status);
        Assert.True(paymentIntent.InventoryCommitted);
    }

    [Fact]
    public async Task InventoryReservationCleanupJob_ShouldReleaseStaleReservation_AndRefreshMetrics()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var scenario = await SeedReservedOrderAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentMethodName: "Stripe",
            paymentMethod: PaymentMethod.Card,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 2);

        await MarkReservationsStaleAsync(context, scenario.OrderId, minutesAgo: 90);

        var monitoringOptions = Options.Create(new InventoryReservationMonitoringOptions
        {
            StaleReservationThresholdMinutes = 15,
            ActivePaymentGraceMinutes = 5,
            CleanupBatchSize = 10,
            HighReservedQuantityThreshold = 1m
        });
        var metricsService = CreateMetricsService(context, monitoringOptions);
        var couponValidationMock = CreateCouponValidationMock();
        var outbox = CreateAccountingOutboxMock();
        var paymentLifecycleService = CreateLifecycleService(context, repository, outbox, couponValidationMock);
        var cleanupJob = new InventoryReservationCleanupJob(
            context,
            repository,
            metricsService,
            couponValidationMock.Object,
            monitoringOptions,
            NullLogger<InventoryReservationCleanupJob>.Instance,
            paymentLifecycleService);

        await cleanupJob.ExecuteAsync();

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);
        var reservation = await verifyContext.InventoryReservations
            .SingleAsync(item => item.TenantId == tenantId && item.OrderId == scenario.OrderId);
        var snapshot = await CreateMetricsService(verifyContext, monitoringOptions).RefreshSnapshotAsync();

        Assert.Equal(9m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(2m, reservation.ReleasedQuantity);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
        Assert.Equal(0m, snapshot.TotalReservedQuantity);
        Assert.Equal(0, snapshot.StaleReservationCount);
        Assert.Equal(0m, snapshot.StaleReservedQuantity);
        
        // Verify coupon reservation was released with correct parameters
        couponValidationMock.Verify(
            m => m.ReleaseCouponReservationAsync(
                It.Is<Guid>(g => g == tenantId),
                It.Is<Guid>(g => g == scenario.OrderId),
                It.Is<string>(s => s == "inventory-lifecycle-tests"),
                It.Is<string>(s => s == "Inventory reservation expired and was released by cleanup."),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InventoryReservationCleanupJob_ShouldExpireOpenPaymentIntent_WhenReservationIsReleased()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var scenario = await SeedReservedOrderWithPaymentIntentAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentMethodName: "Stripe",
            paymentMethod: PaymentMethod.Card,
            provider: PaymentProvider.Stripe,
            paymentIntentStatus: PaymentIntentStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 2);

        await MarkReservationsStaleAsync(context, scenario.OrderId, minutesAgo: 90);

        var monitoringOptions = Options.Create(new InventoryReservationMonitoringOptions
        {
            StaleReservationThresholdMinutes = 15,
            ActivePaymentGraceMinutes = 5,
            CleanupBatchSize = 10
        });
        var metricsService = CreateMetricsService(context, monitoringOptions);
        var couponValidationMock = CreateCouponValidationMock();
        var outbox = CreateAccountingOutboxMock();
        var paymentLifecycleService = CreateLifecycleService(context, repository, outbox, couponValidationMock);
        var cleanupJob = new InventoryReservationCleanupJob(
            context,
            repository,
            metricsService,
            couponValidationMock.Object,
            monitoringOptions,
            NullLogger<InventoryReservationCleanupJob>.Instance,
            paymentLifecycleService);

        await cleanupJob.ExecuteAsync();

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var order = await verifyContext.Orders.SingleAsync(item => item.Id == scenario.OrderId);
        var paymentIntent = await verifyContext.PaymentIntents.SingleAsync(item => item.Id == scenario.PaymentIntentId);

        Assert.Equal(PaymentStatus.Expired, order.PaymentStatus);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentIntentStatus.Expired, paymentIntent.Status);
        Assert.False(paymentIntent.InventoryCommitted);
        
        // Verify coupon reservation was released with correct parameters
        couponValidationMock.Verify(
            m => m.ReleaseCouponReservationAsync(
                It.Is<Guid>(g => g == tenantId),
                It.Is<Guid>(g => g == scenario.OrderId),
                It.Is<string>(s => s == "inventory-lifecycle-tests"),
                It.Is<string>(s => s == "Inventory reservation expired and was released by cleanup."),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InventoryReservationCleanupJob_ShouldNotReleaseReservation_WhenPaymentStillActive()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var scenario = await SeedReservedOrderWithPaymentIntentAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentMethodName: "Stripe",
            paymentMethod: PaymentMethod.Card,
            provider: PaymentProvider.Stripe,
            paymentIntentStatus: PaymentIntentStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 2);

        await MarkReservationAsLegacyStaleWithoutExpiryAsync(context, scenario.OrderId, minutesAgo: 90);

        var monitoringOptions = Options.Create(new InventoryReservationMonitoringOptions
        {
            StaleReservationThresholdMinutes = 15,
            ActivePaymentGraceMinutes = 120,
            CleanupBatchSize = 10
        });
        var metricsService = CreateMetricsService(context, monitoringOptions);
        var couponValidationMock = CreateCouponValidationMock();
        var outbox = CreateAccountingOutboxMock();
        var paymentLifecycleService = CreateLifecycleService(context, repository, outbox, couponValidationMock);
        var cleanupJob = new InventoryReservationCleanupJob(
            context,
            repository,
            metricsService,
            couponValidationMock.Object,
            monitoringOptions,
            NullLogger<InventoryReservationCleanupJob>.Instance,
            paymentLifecycleService);

        await cleanupJob.ExecuteAsync();

        await using var verifyContext = await CreateDbContextAsync(connection, tenantId);
        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item => item.TenantId == tenantId && item.VariantId == scenario.VariantId);
        var reservation = await verifyContext.InventoryReservations
            .SingleAsync(item => item.TenantId == tenantId && item.OrderId == scenario.OrderId);
        await using var metricsContext = await CreateDbContextAsync(connection, Guid.Empty);
        var snapshot = await CreateMetricsService(metricsContext, monitoringOptions).RefreshSnapshotAsync();

        Assert.Equal(9m, stock.Quantity);
        Assert.Equal(2m, stock.ReservedQuantity);
        Assert.Equal(0m, reservation.ReleasedQuantity);
        Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
        Assert.Equal(2m, snapshot.TotalReservedQuantity);
        Assert.Equal(0, snapshot.StaleReservationCount);
        Assert.Equal(0m, snapshot.StaleReservedQuantity);
        
        // Verify coupon reservation was NOT released (payment still active)
        couponValidationMock.Verify(
            m => m.ReleaseCouponReservationAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StrictLockingMode_ShouldRejectSQLite_AndThrowInvalidOperationException()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var repository = CreateStockRepository(context);
        var outbox = CreateAccountingOutboxMock();
        
        // Create service with RequireRowLevelLocking = true (strict mode)
        var eventPublisher = new Mock<IEventPublisher>();
        eventPublisher.SetReturnsDefault(Task.CompletedTask);

        var commerceAccounting = CreateCommerceAccountingMock();
        var marketplaceOutbox = new Mock<IMarketplaceEventOutboxService>();
        marketplaceOutbox.SetReturnsDefault(Task.CompletedTask);

        var correlationIdAccessor = new Mock<ICorrelationIdAccessor>();
        correlationIdAccessor.Setup(item => item.GetCorrelationId()).Returns("strict-locking-test");
        var metricsService = new Mock<IInventoryReservationMetricsService>();

        var service = new PaymentLifecycleService(
            context,
            outbox.Object,
            commerceAccounting.Object,
            eventPublisher.Object,
            marketplaceOutbox.Object,
            correlationIdAccessor.Object,
            repository,
            metricsService.Object,
            Mock.Of<ICouponValidationService>(),
            NullLogger<PaymentLifecycleService>.Instance,
            Options.Create(new PaymentLifecycleSettings
            {
                RequireRowLevelLocking = true  // Strict mode enabled
            }));

        var scenario = await SeedReservedOrderAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode: "STRIPE",
            paymentMethodName: "Stripe",
            paymentMethod: PaymentMethod.Card,
            paymentStatus: PaymentStatus.Pending,
            fulfillmentStatus: FulfillmentStatus.Created,
            orderStatus: OrderStatus.Pending,
            quantity: 3);

        var result = await service.CaptureOnlinePaymentAsync(
            scenario.OrderId,
            gateway: "stripe",
            paymentIntentId: "pi_strict_locking_test",
            providerAmount: scenario.Total,
            providerCurrency: "EGP");

        Assert.False(result.Success);
        Assert.Contains("Database provider", result.ErrorMessage);
        Assert.Contains("not supported for production payment lifecycle flows", result.ErrorMessage);
        Assert.Contains("row-level locking", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sqlite", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        
        // Verify system state was not mutated after CaptureOnlinePaymentAsync threw
        // Reload order and verify it remains unchanged from SeedReservedOrderAsync
        var reloadedOrder = await context.Orders
            .SingleAsync(o => o.Id == scenario.OrderId && o.TenantId == tenantId);
        
        Assert.Equal(scenario.OrderStatus, reloadedOrder.Status);
        Assert.Equal(scenario.PaymentStatus, reloadedOrder.PaymentStatus);
        
        // Verify payment intent remains unchanged
        if (scenario.PaymentIntentId.HasValue)
        {
            var reloadedIntent = await context.PaymentIntents
                .SingleAsync(pi => pi.Id == scenario.PaymentIntentId.Value && pi.TenantId == tenantId);
            Assert.Equal(PaymentIntentStatus.Pending, reloadedIntent.Status);
        }
        
        // Verify reservation remains unchanged from SeedReservedOrderAsync
        var reloadedReservation = await context.InventoryReservations
            .SingleAsync(r => r.OrderId == scenario.OrderId && r.TenantId == tenantId);
        
        Assert.Equal(scenario.ReservationId, reloadedReservation.Id);
        Assert.Equal(InventoryReservationStatus.Active, reloadedReservation.Status);
        Assert.Equal(0m, reloadedReservation.ReleasedQuantity);
        
        // Verify stock levels remain unchanged via repository
        var reloadedStock = await context.ProductVariantStocks
            .SingleAsync(s => s.VariantId == scenario.VariantId && s.TenantId == tenantId);
        
        Assert.Equal(scenario.InitialStockQuantity, reloadedStock.Quantity);
        Assert.Equal(scenario.Quantity, reloadedStock.ReservedQuantity);
        
        // Verify no outbox/marketplace/event publisher calls occurred
        outbox.VerifyNoOtherCalls();
        marketplaceOutbox.VerifyNoOtherCalls();
        eventPublisher.VerifyNoOtherCalls();
        
        // Verify no commerce accounting calls occurred during the strict-locking failure path
        commerceAccounting.VerifyNoOtherCalls();
    }

    private static ProductVariantStockRepository CreateStockRepository(ApplicationDbContext context)
    {
        return new ProductVariantStockRepository(context, NullLogger<ProductVariantStockRepository>.Instance);
    }

    private static async Task<ApplicationDbContext> CreateDbContextAsync(SqliteConnection connection, Guid tenantId)
    {
        RegisterSqliteFunctions(connection);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new SqliteLifecycleTestDbContext(options, new TestTenantContext(tenantId));
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private sealed class SqliteLifecycleTestDbContext : ApplicationDbContext
    {
        public SqliteLifecycleTestDbContext(
            DbContextOptions<ApplicationDbContext> options,
            TestTenantContext tenantContext)
            : base(options, tenantContext)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var constraint in entityType.GetCheckConstraints().ToArray())
                {
                    entityType.RemoveCheckConstraint(constraint.Name);
                }

                foreach (var property in entityType.GetProperties()
                             .Where(property => string.Equals(
                                 property.GetColumnName(),
                                 "xmin",
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    property.SetColumnType("INTEGER");
                    property.ValueGenerated = ValueGenerated.Never;
                    property.IsConcurrencyToken = false;
                    property.SetDefaultValue(0u);
                }
            }
        }
    }

    private static PaymentLifecycleService CreateLifecycleService(
        ApplicationDbContext context,
        IProductVariantStockRepository repository,
        Mock<IAccountingOutboxService> outbox,
        Mock<ICouponValidationService>? couponValidationService = null,
        bool requireRowLevelLocking = false)
    {
        var eventPublisher = new Mock<IEventPublisher>();
        eventPublisher.SetReturnsDefault(Task.CompletedTask);

        var commerceAccounting = CreateCommerceAccountingMock();
        var marketplaceOutbox = new Mock<IMarketplaceEventOutboxService>();
        marketplaceOutbox.SetReturnsDefault(Task.CompletedTask);

        var correlationIdAccessor = new Mock<ICorrelationIdAccessor>();
        correlationIdAccessor.Setup(item => item.GetCorrelationId()).Returns("inventory-lifecycle-tests");
        var metricsService = new Mock<IInventoryReservationMetricsService>();
        couponValidationService ??= new Mock<ICouponValidationService>();
        couponValidationService
            .Setup(service => service.ConfirmCouponUsageAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CouponConfirmationDecision.None());
        couponValidationService
            .Setup(service => service.ReleaseCouponReservationAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CouponReleaseDecision.None());

        outbox
            .Setup(service => service.EnqueueOnlinePaymentAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                Guid tenantId,
                Guid orderId,
                string paymentIntentId,
                string gateway,
                CancellationToken cancellationToken) =>
            {
                var idempotencyKey = $"ONLINE_PAYMENT:{tenantId}:{orderId}:{paymentIntentId}";
                var alreadyStaged = await context.AccountingOutboxMessages
                    .IgnoreQueryFilters()
                    .AnyAsync(message =>
                        message.TenantId == tenantId &&
                        message.IdempotencyKey == idempotencyKey,
                        cancellationToken);

                if (alreadyStaged)
                {
                    return;
                }

                context.AccountingOutboxMessages.Add(PointOfSale.Core.Entities.Accounting.AccountingOutboxMessage.Create(
                    tenantId,
                    "OnlinePayment",
                    orderId,
                    "OnlinePayment",
                    $"{{\"OrderId\":\"{orderId:D}\",\"PaymentIntentId\":\"{paymentIntentId}\",\"Gateway\":\"{gateway}\"}}",
                    idempotencyKey,
                    "inventory-lifecycle-tests"));
                await context.SaveChangesAsync(cancellationToken);
            });

        outbox
            .Setup(service => service.EnqueueCodDeliveredAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Guid tenantId, Guid orderId, CancellationToken cancellationToken) =>
            {
                var idempotencyKey = $"COD_DELIVERED:{tenantId}:{orderId}";
                var existing = await context.AccountingOutboxMessages
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(message =>
                        message.TenantId == tenantId &&
                        message.IdempotencyKey == idempotencyKey,
                        cancellationToken);

                if (existing != null)
                {
                    return OutboxEnqueueResult.Exists(existing.Id, "PENDING", idempotencyKey);
                }

                var message = PointOfSale.Core.Entities.Accounting.AccountingOutboxMessage.Create(
                    tenantId,
                    "CodDelivered",
                    orderId,
                    "CodDelivered",
                    $"{{\"OrderId\":\"{orderId:D}\"}}",
                    idempotencyKey,
                    "inventory-lifecycle-tests");
                context.AccountingOutboxMessages.Add(message);
                await context.SaveChangesAsync(cancellationToken);
                return OutboxEnqueueResult.Created(message.Id, idempotencyKey);
            });

        return new PaymentLifecycleService(
            context,
            outbox.Object,
            commerceAccounting.Object,
            eventPublisher.Object,
            marketplaceOutbox.Object,
            correlationIdAccessor.Object,
            repository,
            metricsService.Object,
            couponValidationService.Object,
            NullLogger<PaymentLifecycleService>.Instance,
            Options.Create(new PaymentLifecycleSettings
            {
                RequireRowLevelLocking = requireRowLevelLocking
            }));
    }

    private static Mock<IAccountingOutboxService> CreateAccountingOutboxMock()
    {
        var outbox = new Mock<IAccountingOutboxService>();
        outbox.SetReturnsDefault(Task.CompletedTask);
        outbox.Setup(item => item.EnqueueCodDeliveredAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, CancellationToken _) =>
                OutboxEnqueueResult.Created(Guid.NewGuid(), $"cod-{Guid.NewGuid():N}"));
        return outbox;
    }

    private static Mock<ICommerceAccountingService> CreateCommerceAccountingMock()
    {
        var commerceAccounting = new Mock<ICommerceAccountingService>();
        commerceAccounting
            .Setup(service => service.PostOnlinePaymentCapturedAsync(
                It.IsAny<Order>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "ONLINE-JOURNAL-TEST"));
        commerceAccounting
            .Setup(service => service.PostCodDeliveredAsync(
                It.IsAny<Order>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "COD-JOURNAL-TEST"));
        commerceAccounting
            .Setup(service => service.PostWalletApprovedAsync(
                It.IsAny<Order>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "WALLET-JOURNAL-TEST"));
        commerceAccounting
            .Setup(service => service.PostResolvedUnresolvedPaymentAsync(
                It.IsAny<Order>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "SUSPENSE-JOURNAL-TEST"));
        commerceAccounting
            .Setup(service => service.PostResolvedUnresolvedCommerceHubPaymentAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "CH-SUSPENSE-CLEAR-TEST"));
        return commerceAccounting;
    }

    private static async Task<InventorySeed> SeedInventoryAsync(
        ApplicationDbContext context,
        Guid tenantId,
        decimal initialQuantity)
    {
        var warehouseId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var utcNow = DateTime.UtcNow;

        if (!await context.Currencies.AnyAsync(currency => currency.Code == "EGP"))
        {
            context.Currencies.Add(new Currency
            {
                Code = "EGP",
                Name = "Egyptian Pound",
                Symbol = "EGP",
                DecimalPlaces = 2
            });
            await context.SaveChangesAsync();
        }

        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Inventory Lifecycle Tenant",
            Slug = $"inventory-lifecycle-{tenantId:N}",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        await context.SaveChangesAsync();

        context.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Main Warehouse",
            IsActive = true,
            CreatedAt = utcNow
        });
        await context.SaveChangesAsync();

        context.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Inventory Lifecycle Category",
            CreatedAt = utcNow
        });
        await context.SaveChangesAsync();

        context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Inventory Lifecycle Product",
            CategoryId = categoryId,
            SalePrice = 100m,
            OnlinePrice = 100m,
            PurchasePrice = 60m,
            IsActive = true,
            CreatedAt = utcNow
        });
        await context.SaveChangesAsync();

        context.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = "INV-LIFECYCLE-001",
            Price = 100m,
            OnlinePrice = 100m,
            CostPrice = 60m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = utcNow
        });
        await context.SaveChangesAsync();

        context.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarehouseId = warehouseId,
            VariantId = variantId,
            Quantity = initialQuantity,
            ReservedQuantity = 0m,
            DamagedQuantity = 0m,
            CreatedAt = utcNow
        });

        await context.SaveChangesAsync();

        return new InventorySeed(warehouseId, productId, variantId);
    }

    private static async Task<OrderReservationScenario> SeedReservedOrderAsync(
        ApplicationDbContext context,
        ProductVariantStockRepository repository,
        Guid tenantId,
        string paymentMethodCode,
        string paymentMethodName,
        PaymentMethod paymentMethod,
        PaymentStatus paymentStatus,
        FulfillmentStatus fulfillmentStatus,
        OrderStatus orderStatus,
        int quantity)
    {
        // SQLite stores decimals as text by default, so keep quantities single-digit
        // to avoid lexical CHECK constraint comparisons in the in-memory test provider.
        var seed = await SeedInventoryAsync(context, tenantId, initialQuantity: 9m);
        var orderId = Guid.NewGuid();
        var orderItemId = Guid.NewGuid();
        var utcNow = DateTime.UtcNow;
        var unitPrice = 100m;
        var total = unitPrice * quantity;

        var reserved = await repository.ReserveAsync(
            tenantId,
            seed.VariantId,
            seed.WarehouseId,
            quantity,
            orderId,
            expiresAtUtc: utcNow.AddMinutes(15));

        Assert.True(reserved);

        var order = new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..12],
            Status = orderStatus,
            PaymentStatus = paymentStatus,
            FulfillmentStatus = fulfillmentStatus,
            FulfillmentMode = FulfillmentMode.Manual,
            ShippingAddress = "123 Integrity Street",
            Subtotal = total,
            Total = total,
            Currency = "EGP",
            PaymentMethod = paymentMethod,
            PaymentMethodCode = paymentMethodCode,
            PaymentMethodName = paymentMethodName,
            CreatedAt = utcNow
        };

        order.Items.Add(new OrderItem
        {
            Id = orderItemId,
            OrderId = orderId,
            ProductId = seed.ProductId,
            VariantId = seed.VariantId,
            WarehouseId = seed.WarehouseId,
            SKU = "INV-LIFECYCLE-001",
            ProductName = "Inventory Lifecycle Product",
            Quantity = quantity,
            UnitPrice = unitPrice,
            DiscountAmount = 0m,
            TaxAmount = 0m,
            Total = total,
            CostPrice = 60m,
            CreatedAt = utcNow
        });

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        await repository.BindOrderReservationsAsync(
            tenantId,
            orderId,
            order.Items.ToList());

        // Retrieve the reservation ID after binding
        var reservation = await context.InventoryReservations
            .SingleAsync(r => r.OrderId == orderId && r.TenantId == tenantId);

        return new OrderReservationScenario(
            orderId,
            orderItemId,
            seed.VariantId,
            total,
            orderStatus,
            paymentStatus,
            PaymentIntentId: null, // No payment intent for basic reserved orders
            reservation.Id,
            InitialStockQuantity: 9m, // From initialQuantity parameter in SeedInventoryAsync
            Quantity: quantity);
    }

    private static async Task<OnlinePaymentReservationScenario> SeedReservedOrderWithPaymentIntentAsync(
        ApplicationDbContext context,
        ProductVariantStockRepository repository,
        Guid tenantId,
        string paymentMethodCode,
        string paymentMethodName,
        PaymentMethod paymentMethod,
        PaymentProvider provider,
        PaymentIntentStatus paymentIntentStatus,
        PaymentStatus paymentStatus,
        FulfillmentStatus fulfillmentStatus,
        OrderStatus orderStatus,
        int quantity)
    {
        var orderScenario = await SeedReservedOrderAsync(
            context,
            repository,
            tenantId,
            paymentMethodCode,
            paymentMethodName,
            paymentMethod,
            paymentStatus,
            fulfillmentStatus,
            orderStatus,
            quantity);

        var paymentIntentId = Guid.NewGuid();
        var paymentReference = $"PAY-{Guid.NewGuid():N}";

        var providerIntentId = $"{provider.ToString().ToLowerInvariant()}-intent-{paymentIntentId:N}";
        var createdAtUtc = DateTime.UtcNow;

        await context.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO ""PaymentIntents""
    (""Id"", ""OrderId"", ""TenantId"", ""Amount"", ""Currency"", ""PaymentMethod"", ""Status"", ""Provider"",
     ""PaymentReference"", ""ProviderIntentId"", ""IdempotencyKey"", ""ProofImageDeleted"", ""WebhookEventCount"",
     ""InventoryCommitted"", ""CreatedAt"", ""IsDeleted"", ""xmin"")
VALUES
    ({paymentIntentId}, {orderScenario.OrderId}, {tenantId}, {orderScenario.Total}, {"EGP"}, {(int)paymentMethod}, {(int)paymentIntentStatus}, {provider.ToString()},
     {paymentReference}, {providerIntentId}, {$"payment-intent:{paymentIntentId:N}"}, {false}, {0},
     {false}, {createdAtUtc}, {false}, 1);
");

        return new OnlinePaymentReservationScenario(
            orderScenario.OrderId,
            orderScenario.OrderItemId,
            orderScenario.VariantId,
            orderScenario.Total,
            paymentIntentId,
            paymentReference);
    }

    private static HandlePaymentResultRequest CreatePaymentResultRequest(
        OnlinePaymentReservationScenario scenario,
        PaymentLifecycleOutcome outcome,
        PaymentLifecycleSource source,
        string gateway,
        string webhookEventId,
        string? reason = null,
        decimal? providerAmount = null,
        string? providerCurrency = "EGP")
    {
        return new HandlePaymentResultRequest
        {
            OrderId = scenario.OrderId,
            PaymentIntentId = scenario.PaymentIntentId,
            Outcome = outcome,
            Source = source,
            Gateway = gateway,
            PaymentReference = scenario.PaymentReference,
            ProviderPaymentId = $"{gateway}-payment-{scenario.PaymentIntentId:N}",
            ProviderAmount = providerAmount ?? scenario.Total,
            ProviderCurrency = providerCurrency,
            Reason = reason,
            WebhookEventId = webhookEventId,
            ProcessedAtUtc = DateTime.UtcNow
        };
    }

    private static async Task MarkReservationsStaleAsync(
        ApplicationDbContext context,
        Guid orderId,
        int minutesAgo)
    {
        var staleUtc = DateTime.UtcNow.AddMinutes(-minutesAgo);
        var reservations = await context.InventoryReservations
            .Where(item => item.OrderId == orderId)
            .ToListAsync();

        foreach (var reservation in reservations)
        {
            reservation.ExpiresAtUtc = staleUtc;
        }

        await context.SaveChangesAsync();
    }

    private static async Task MarkReservationAsLegacyStaleWithoutExpiryAsync(
        ApplicationDbContext context,
        Guid orderId,
        int minutesAgo)
    {
        var staleUtc = DateTime.UtcNow.AddMinutes(-minutesAgo);
        var reservations = await context.InventoryReservations
            .Where(item => item.OrderId == orderId)
            .ToListAsync();

        foreach (var reservation in reservations)
        {
            reservation.ExpiresAtUtc = null;
            reservation.CreatedAt = staleUtc;
        }

        await context.SaveChangesAsync();
    }

    private static InventoryReservationMetricsService CreateMetricsService(
        ApplicationDbContext context,
        IOptions<InventoryReservationMonitoringOptions> options)
    {
        return new InventoryReservationMetricsService(
            context,
            new InventoryReservationTelemetry(),
            options,
            NullLogger<InventoryReservationMetricsService>.Instance);
    }

    private static Mock<ICouponValidationService> CreateCouponValidationMock()
    {
        var mock = new Mock<ICouponValidationService>();
        mock.Setup(service => service.ReleaseCouponReservationAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CouponReleaseDecision.None());

        return mock;
    }

    private sealed record InventorySeed(
        Guid WarehouseId,
        Guid ProductId,
        Guid VariantId);

    private sealed record OrderReservationScenario(
        Guid OrderId,
        Guid OrderItemId,
        Guid VariantId,
        decimal Total,
        OrderStatus OrderStatus,
        PaymentStatus PaymentStatus,
        Guid? PaymentIntentId,
        Guid ReservationId,
        decimal InitialStockQuantity,
        decimal Quantity);

    private sealed record OnlinePaymentReservationScenario(
        Guid OrderId,
        Guid OrderItemId,
        Guid VariantId,
        decimal Total,
        Guid PaymentIntentId,
        string PaymentReference);

    private static void RegisterSqliteFunctions(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?, string?, string?, string?>(
            "REGEXP_REPLACE",
            (input, pattern, replacement, options) =>
            {
                if (input == null)
                {
                    return null;
                }

                var regexOptions = RegexOptions.None;
                if (!string.IsNullOrWhiteSpace(options) &&
                    options.Contains('i', StringComparison.OrdinalIgnoreCase))
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }

                return Regex.Replace(
                    input,
                    pattern ?? string.Empty,
                    replacement ?? string.Empty,
                    regexOptions);
            });
    }
}
