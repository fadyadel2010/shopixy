using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Projection;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Tests.Infrastructure;

public sealed class OrderFulfillmentLineageAuthorityTests
{
    [Fact]
    public async Task LoadCurrentShipmentAsync_ReturnsCurrentSqlShipmentTruth()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        SeedOrderAndShipment(dbContext, tenantId, orderId, shipmentId, ShipmentStatus.Cancelled);
        await dbContext.SaveChangesAsync();

        var snapshot = await OrderFulfillmentLineageAuthority.LoadCurrentShipmentAsync(
            dbContext,
            tenantId,
            orderId,
            shipmentId);

        Assert.NotNull(snapshot);
        Assert.Equal(shipmentId, snapshot!.ShipmentId);
        Assert.Equal(ShipmentStatus.Cancelled, snapshot.ShipmentStatus);
        Assert.Equal("TRACK-SQL", snapshot.TrackingNumber);
        Assert.Equal("sql-carrier", snapshot.CarrierCode);
    }

    [Fact]
    public async Task LoadCurrentShipmentAsync_FailsClosed_ForDetachedOrDeletedShipmentLineage()
    {
        var tenantId = Guid.NewGuid();
        var currentOrderId = Guid.NewGuid();
        var staleOrderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        SeedOrderAndShipment(dbContext, tenantId, currentOrderId, shipmentId, ShipmentStatus.Delivered);
        dbContext.Orders.Add(CreateOrder(tenantId, staleOrderId));
        await dbContext.SaveChangesAsync();

        var detachedSnapshot = await OrderFulfillmentLineageAuthority.LoadCurrentShipmentAsync(
            dbContext,
            tenantId,
            staleOrderId,
            shipmentId);

        Assert.Null(detachedSnapshot);

        var shipment = await dbContext.Shipments.SingleAsync(candidate => candidate.Id == shipmentId);
        shipment.IsDeleted = true;
        await dbContext.SaveChangesAsync();

        var deletedSnapshot = await OrderFulfillmentLineageAuthority.LoadCurrentShipmentAsync(
            dbContext,
            tenantId,
            currentOrderId,
            shipmentId);

        Assert.Null(deletedSnapshot);
    }

    [Fact]
    public async Task ProjectionSnapshotLoader_UsesLiveShipmentVisibility_ForRebuildConvergence()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = CreateOrder(tenantId, orderId);
        order.ShippedAt = DateTime.UtcNow.AddHours(-1);
        dbContext.Orders.Add(order);
        dbContext.Shipments.Add(CreateShipment(tenantId, orderId, shipmentId, ShipmentStatus.Cancelled));
        await dbContext.SaveChangesAsync();

        var authoritySnapshot = await StorefrontOrderProjectionAuthoritySnapshotLoader.LoadAsync(
            dbContext,
            tenantId,
            orderId,
            CancellationToken.None);
        var document = StorefrontOrderProjectionBuilder.Build(
            order,
            Guid.NewGuid(),
            DateTime.UtcNow,
            aggregateVersion: 1,
            authoritySnapshot: authoritySnapshot);

        Assert.Equal("Cancelled", authoritySnapshot.ShipmentShippingStatus);
        Assert.Equal("Cancelled", document.ShippingStatus);
        Assert.Equal("TRACK-SQL", document.TrackingNumber);
        Assert.Equal("sql-carrier", document.CarrierName);
    }

    [Theory]
    [InlineData(FulfillmentStatus.FailedDelivery, "Failed")]
    [InlineData(FulfillmentStatus.Returned, "Returned")]
    public void ProjectionBuilder_TerminalFulfillmentOverridesStaleShipmentVisibility(
        FulfillmentStatus fulfillmentStatus,
        string expectedShippingStatus)
    {
        var order = CreateOrder(Guid.NewGuid(), Guid.NewGuid());
        order.FulfillmentStatus = fulfillmentStatus;
        order.AcceptFulfillmentStatusChange();
        var shipmentSnapshot = new StorefrontOrderProjectionAuthoritySnapshot(
            ShipmentShippingStatus: "Delivered",
            ShipmentTrackingNumber: null,
            ShipmentCarrierCode: null,
            ShipmentDeliveredAtUtc: null,
            AttributedAtUtc: null,
            AffiliateCommissionAmount: null);

        var document = StorefrontOrderProjectionBuilder.Build(
            order,
            Guid.NewGuid(),
            DateTime.UtcNow,
            aggregateVersion: 1,
            authoritySnapshot: shipmentSnapshot);

        Assert.Equal(expectedShippingStatus, document.ShippingStatus);
    }

    private static void SeedOrderAndShipment(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid orderId,
        Guid shipmentId,
        ShipmentStatus status)
    {
        dbContext.Orders.Add(CreateOrder(tenantId, orderId));
        dbContext.Shipments.Add(CreateShipment(tenantId, orderId, shipmentId, status));
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
            TrackingNumber = "TRACK-SQL",
            CarrierCode = "sql-carrier",
            Currency = "EGP",
            RecipientName = "SQL recipient",
            ShippingAddress = "SQL authority street",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"OrderFulfillmentLineageAuthority_{Guid.NewGuid():N}")
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return new ApplicationDbContext(options, tenantContext);
    }
}
