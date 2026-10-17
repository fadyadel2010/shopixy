using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Consumers;
using PointOfSale.API.DTOs.Shipping;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Checkout;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.API.Services.Shipping;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Fulfillment;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed class RuntimeShippingWorkerBoundaryTests
{
    [Fact]
    public async Task PosCanonicalConfirmation_UsesVerifiedShipmentOwner_NotOnlyLegacyConfirmation()
    {
        var tenant = Guid.NewGuid(); var scope = new TenantContext();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, scope);
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = "SYN-CANONICAL-CONFIRMED", Status = OrderStatus.Confirmed,
            FulfillmentStatus = FulfillmentStatus.Confirmed, FulfillmentMode = FulfillmentMode.Manual,
            ShippingMethodId = Guid.NewGuid(), PaymentMethodCode = "COD", PaymentStatus = PaymentStatus.Pending };
        db.Orders.Add(order); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var shipments = new Mock<IShipmentService>(MockBehavior.Strict);
        shipments.Setup(s => s.CreateShipmentAsync(tenant, order.Id, order.ShippingMethodId.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { Assert.Equal(tenant, scope.TenantId); return new ShipmentDto(); });
        var consumer = new OrderShipmentOrchestrationConsumer(db, shipments.Object, Mock.Of<ICarrierShipmentService>(MockBehavior.Strict),
            NullLogger<OrderShipmentOrchestrationConsumer>.Instance, scope);
        var canonical = Assert.IsAssignableFrom<IConsumer<FulfillmentConfirmedEvent>>(consumer);
        var message = new Mock<ConsumeContext<FulfillmentConfirmedEvent>>();
        message.SetupGet(c => c.Message).Returns(new FulfillmentConfirmedEvent { TenantId = tenant, OrderId = order.Id });
        await canonical.Consume(message.Object);
        shipments.VerifyAll(); Assert.Null(scope.TenantId);
    }

    [Fact]
    public async Task ManualShippingOrder_CannotOutrunPostConfirmationShipmentCreation()
    {
        var tenant = Guid.NewGuid(); var scope = new TenantContext(); scope.SetTenantId(tenant);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, scope);
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = $"SYN-{Guid.NewGuid():N}", Status = OrderStatus.Processing,
            FulfillmentStatus = FulfillmentStatus.Packed, FulfillmentMode = FulfillmentMode.Manual, ShippingMethodId = Guid.NewGuid(), PaymentMethodCode = "COD", PaymentStatus = PaymentStatus.Pending };
        db.Orders.Add(order); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var events = new Mock<IEventPublisher>(); events.SetReturnsDefault(Task.CompletedTask);
        var service = new OrderFulfillmentService(db, events.Object, NullLogger<OrderFulfillmentService>.Instance);
        var result = await service.MarkOutForDeliveryAsync(order.Id, FulfillmentRequestSource.Manual);
        Assert.False(result.Success);
        Assert.Contains("SHIPMENT_CREATION_PENDING", result.ErrorMessage);
        Assert.Equal(FulfillmentStatus.Packed, (await db.Orders.SingleAsync()).FulfillmentStatus);
        events.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("COD", 5)][InlineData("cod", 30)][InlineData(" COD ", 30)]
    public async Task CodAwaitingMerchantConfirmation_IsNotGatewayIntentRecoveryOrExpiry(string method, int minutes)
    {
        var tenant = Guid.NewGuid(); var scope = new TenantContext();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, scope);
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = $"SYN-{Guid.NewGuid():N}", PaymentMethodCode = method,
            PaymentStatus = PaymentStatus.Pending, Status = OrderStatus.Pending, Currency = "EGP", Total = 35 };
        order.TransitionCheckoutState(CheckoutState.PendingPaymentIntent); db.Orders.Add(order); await db.SaveChangesAsync();
        order.CreatedAt = DateTime.UtcNow.AddMinutes(-minutes); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var payment = new Mock<IPaymentService>(MockBehavior.Strict); var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        var events = new Mock<IEventPublisher>(MockBehavior.Strict); var stock = new Mock<IProductVariantStockRepository>(MockBehavior.Strict);
        var recovery = new CheckoutPaymentIntentRecoveryService(db, payment.Object, gateway.Object, Mock.Of<ICheckoutSessionTokenService>(),
            stock.Object, Mock.Of<ICouponValidationService>(MockBehavior.Strict), events.Object, Mock.Of<IManualPaymentDurableEvidenceAuthority>(),
            NullLogger<CheckoutPaymentIntentRecoveryService>.Instance);
        Assert.Equal(0, await recovery.RepairPendingPaymentIntentOrdersAsync());
        payment.VerifyNoOtherCalls(); gateway.VerifyNoOtherCalls(); events.VerifyNoOtherCalls(); stock.VerifyNoOtherCalls();
        var persisted = await db.Orders.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(OrderStatus.Pending, persisted.Status); Assert.Equal(PaymentStatus.Pending, persisted.PaymentStatus);
        Assert.Equal(CheckoutState.PendingPaymentIntent, persisted.CheckoutState); Assert.Empty(await db.PaymentIntents.IgnoreQueryFilters().ToListAsync());
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task CancelledOrderCompensation_BindsVerifiedTenantAndRestoresPreviousScope(bool foreignEvent)
    {
        var tenant = Guid.NewGuid(); var scope = new TenantContext();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, scope);
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = $"SYN-{Guid.NewGuid():N}", Status = OrderStatus.Cancelled };
        db.Orders.Add(order); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var reader = new Mock<IShipmentService>(MockBehavior.Strict); var carrier = new Mock<ICarrierShipmentService>(MockBehavior.Strict);
        var shipmentId = Guid.NewGuid();
        reader.Setup(r => r.GetShipmentByOrderIdAsync(tenant, order.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => scope.TenantId == tenant ? new StorefrontShipmentDto { ShipmentId = shipmentId, OrderId = order.Id } : null);
        carrier.Setup(c => c.CancelShipmentAsync(tenant, shipmentId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { Assert.Equal(tenant, scope.TenantId); return new CarrierCancelResult { Success = true }; });
        var consumer = new OrderShipmentOrchestrationConsumer(db, reader.Object, carrier.Object, NullLogger<OrderShipmentOrchestrationConsumer>.Instance, scope);
        var message = new Mock<ConsumeContext<OrderCancelledEvent>>();
        message.SetupGet(c => c.Message).Returns(new OrderCancelledEvent { TenantId = foreignEvent ? Guid.NewGuid() : tenant, OrderId = order.Id });
        await consumer.Consume(message.Object);
        Assert.Null(scope.TenantId);
        carrier.Verify(c => c.CancelShipmentAsync(tenant, shipmentId, It.IsAny<string>(), It.IsAny<CancellationToken>()), foreignEvent ? Times.Never() : Times.Once());
        if (foreignEvent) reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConfirmedOrderShipmentCreation_BindsVerifiedTenantAndRestoresScope()
    {
        var tenant = Guid.NewGuid(); var scope = new TenantContext();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, scope);
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = $"SYN-{Guid.NewGuid():N}", Status = OrderStatus.Confirmed,
            ShippingMethodId = Guid.NewGuid(), PaymentMethodCode = "COD", PaymentStatus = PaymentStatus.Pending };
        db.Orders.Add(order); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var shipments = new Mock<IShipmentService>(MockBehavior.Strict);
        shipments.Setup(s => s.CreateShipmentAsync(tenant, order.Id, order.ShippingMethodId.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { Assert.Equal(tenant, scope.TenantId); return new ShipmentDto(); });
        var carrier = new Mock<ICarrierShipmentService>(MockBehavior.Strict);
        var consumer = new OrderShipmentOrchestrationConsumer(db, shipments.Object, carrier.Object, NullLogger<OrderShipmentOrchestrationConsumer>.Instance, scope);
        var message = new Mock<ConsumeContext<OrderConfirmedEvent>>(); message.SetupGet(c => c.Message).Returns(new OrderConfirmedEvent { TenantId = tenant, OrderId = order.Id });
        await consumer.Consume(message.Object);
        Assert.Null(scope.TenantId);
        shipments.Verify(s => s.CreateShipmentAsync(tenant, order.Id, order.ShippingMethodId.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        carrier.VerifyNoOtherCalls();
    }
}
