using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Services;

public sealed class OrderStatusSyncShipmentLineageTests
{
    [Fact]
    public async Task SyncFromShipmentAsync_CurrentCancelledShipment_DoesNotImpersonateOrderCancellation()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Orders.Add(CreateOrder(tenantId, orderId));
        dbContext.Shipments.Add(CreateShipment(tenantId, orderId, shipmentId, ShipmentStatus.Cancelled));
        await dbContext.SaveChangesAsync();

        var publisher = CreatePublisher();
        var service = new OrderStatusSyncService(
            dbContext,
            publisher.Object,
            NullLogger<OrderStatusSyncService>.Instance);

        var result = await service.SyncFromShipmentAsync(
            tenantId,
            orderId,
            shipmentId,
            ShipmentStatus.Delivered,
            "replayed-provider-callback");

        var order = await dbContext.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == orderId);
        Assert.True(result.Success);
        Assert.False(result.StatusChanged);
        Assert.Equal(OrderStatus.Confirmed, result.NewStatus);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        publisher.Verify(
            candidate => candidate.PublishAsync(
                It.IsAny<OrderStatusChangedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncFromShipmentAsync_ReturnedShipment_DoesNotImpersonatePaymentRefund()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Orders.Add(CreateOrder(tenantId, orderId));
        dbContext.Shipments.Add(CreateShipment(tenantId, orderId, shipmentId, ShipmentStatus.Returned));
        await dbContext.SaveChangesAsync();

        var publisher = CreatePublisher();
        var service = new OrderStatusSyncService(
            dbContext,
            publisher.Object,
            NullLogger<OrderStatusSyncService>.Instance);

        var result = await service.SyncFromShipmentAsync(
            tenantId,
            orderId,
            shipmentId,
            ShipmentStatus.Returned,
            "carrier-return");

        var order = await dbContext.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == orderId);
        Assert.True(result.Success);
        Assert.False(result.StatusChanged);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.NotEqual(OrderStatus.Refunded, order.Status);
        publisher.Verify(
            candidate => candidate.PublishAsync(
                It.IsAny<OrderStatusChangedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncFromShipmentAsync_IgnoresShipmentThatNoLongerOwnsOrderLineage()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var differentOrderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Orders.AddRange(
            CreateOrder(tenantId, orderId),
            CreateOrder(tenantId, differentOrderId));
        dbContext.Shipments.Add(CreateShipment(tenantId, differentOrderId, shipmentId, ShipmentStatus.Delivered));
        await dbContext.SaveChangesAsync();

        var publisher = CreatePublisher();
        var service = new OrderStatusSyncService(
            dbContext,
            publisher.Object,
            NullLogger<OrderStatusSyncService>.Instance);

        var result = await service.SyncFromShipmentAsync(
            tenantId,
            orderId,
            shipmentId,
            ShipmentStatus.Delivered,
            "stale-queue-replay");

        var order = await dbContext.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == orderId);
        Assert.True(result.Success);
        Assert.False(result.StatusChanged);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Contains("no longer current SQL lineage", result.Message, StringComparison.Ordinal);
        publisher.Verify(
            candidate => candidate.PublishAsync(
                It.IsAny<OrderStatusChangedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Mock<IEventPublisher> CreatePublisher()
    {
        var publisher = new Mock<IEventPublisher>(MockBehavior.Strict);
        publisher
            .Setup(candidate => candidate.PublishAsync(
                It.IsAny<OrderStatusChangedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return publisher;
    }

    private static Order CreateOrder(Guid tenantId, Guid orderId)
    {
        return new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = $"ORD-{orderId:N}",
            Status = OrderStatus.Confirmed,
            Currency = "EGP",
            ShippingAddress = "SQL authority street",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static Shipment CreateShipment(
        Guid tenantId,
        Guid orderId,
        Guid shipmentId,
        ShipmentStatus status)
    {
        return new Shipment
        {
            Id = shipmentId,
            TenantId = tenantId,
            OrderId = orderId,
            ShipmentNumber = $"SHP-{shipmentId:N}",
            Status = status,
            Currency = "EGP",
            RecipientName = "SQL recipient",
            ShippingAddress = "SQL authority street",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"OrderStatusSyncShipmentLineage_{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return new ApplicationDbContext(options, tenantContext);
    }
}
