using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.DTOs.Shipping;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Shipping;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Tests.Architecture;

public sealed class O43FulfillmentShippingRemediationTests
{
    [Fact]
    public async Task GenericStatusApi_RejectsCarrierManagedShipmentWithoutMutation()
    {
        var (db, tenantId, shipmentId) = await CreateCarrierShipmentAsync();
        await using (db)
        using (var redisFactory = CreateDisabledRedisFactory())
        {
            var service = new ShipmentService(
                db,
                redisFactory,
                Options.Create(new RedisSettings { Enabled = false }),
                Mock.Of<IEventPublisher>(),
                Mock.Of<IOrderFulfillmentService>(),
                Mock.Of<IMarketplaceEventOutboxService>(),
                NullLogger<ShipmentService>.Instance);

            var exception = await Assert.ThrowsAsync<ShipmentCarrierStatusAuthorityException>(() =>
                service.UpdateStatusAsync(
                    tenantId,
                    shipmentId,
                    new UpdateShipmentStatusRequestDto { NewStatus = ShipmentStatus.PickedUp },
                    "request-o43",
                    "correlation-o43"));

            Assert.Equal(shipmentId, exception.ShipmentId);
            Assert.Equal(ShipmentStatus.ReadyForPickup, await ReadShipmentStatusAsync(db, shipmentId));
        }
    }

    [Fact]
    public async Task AdminStatusApi_RejectsCarrierManagedShipmentWithoutMutation()
    {
        var (db, tenantId, shipmentId) = await CreateCarrierShipmentAsync();
        await using (db)
        {
            var service = new AdminShipmentService(
                db,
                Mock.Of<IEventPublisher>(),
                Mock.Of<IOrderFulfillmentService>(),
                Mock.Of<IMarketplaceEventOutboxService>(),
                NullLogger<AdminShipmentService>.Instance);

            var exception = await Assert.ThrowsAsync<CarrierManagedStatusAuthorityException>(() =>
                service.UpdateShipmentStatusAsync(
                    tenantId,
                    shipmentId,
                    new AdminUpdateShipmentStatusRequest { NewStatus = ShipmentStatus.PickedUp },
                    "correlation-o43"));

            Assert.Equal(shipmentId, exception.ShipmentId);
            Assert.Equal(ShipmentStatus.ReadyForPickup, await ReadShipmentStatusAsync(db, shipmentId));
        }
    }

    [Fact]
    public void CarrierMutationPaths_EnqueueEventsBeforeTheirFinalSaveAndCommit()
    {
        var source = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        AssertOrderedWithinMethod(
            source,
            "public async Task<CarrierSyncResult> SyncTrackingAsync(",
            "public async Task<WebhookProcessResult> ProcessWebhookAsync(",
            "await EmitStatusChangeEventAsync(",
            "await _dbContext.SaveChangesAsync(cancellationToken);",
            "await mutationTransaction.CommitAsync(cancellationToken);");
        AssertOrderedWithinMethod(
            source,
            "private async Task<WebhookProcessResult> ProcessWebhookInternalAsync(",
            "private async Task<ShippingWebhookLogPreparation> PrepareShippingWebhookLogAsync(",
            "await EmitStatusChangeEventAsync(",
            "await _dbContext.SaveChangesAsync(cancellationToken);",
            "await mutationTransaction.CommitAsync(cancellationToken);");
    }

    private static async Task<(ApplicationDbContext Db, Guid TenantId, Guid ShipmentId)>
        CreateCarrierShipmentAsync()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"O43CarrierAuthority_{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new ApplicationDbContext(options, tenantContext);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = $"ORD-O43-{Guid.NewGuid():N}",
            Status = OrderStatus.Processing,
            FulfillmentStatus = FulfillmentStatus.Packed,
            FulfillmentMode = FulfillmentMode.Carrier,
            FulfillmentCarrierCode = "bosta",
            PaymentMethodCode = "COD",
            PaymentStatus = PaymentStatus.Pending,
            FinancialPostingStatus = FinancialPostingStatus.NotRequired,
            Currency = "EGP",
            ShippingAddress = "42 Test Street",
            ShippingCity = "Cairo",
            ShippingCountry = "EG",
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            Order = order,
            CarrierCode = "bosta",
            ShipmentNumber = $"SHP-O43-{Guid.NewGuid():N}",
            Status = ShipmentStatus.ReadyForPickup,
            Currency = "EGP",
            ShippingAddress = "42 Test Street",
            ShippingCity = "Cairo",
            ShippingCountry = "EG",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        db.Orders.Add(order);
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();
        return (db, tenantId, shipment.Id);
    }

    private static RedisConnectionFactory CreateDisabledRedisFactory()
    {
        return new RedisConnectionFactory(
            Options.Create(new RedisSettings { Enabled = false }),
            NullLogger<RedisConnectionFactory>.Instance,
            connectionOverride: null);
    }

    private static async Task<ShipmentStatus> ReadShipmentStatusAsync(
        ApplicationDbContext db,
        Guid shipmentId)
    {
        db.ChangeTracker.Clear();
        return await db.Shipments
            .IgnoreQueryFilters()
            .Where(shipment => shipment.Id == shipmentId)
            .Select(shipment => shipment.Status)
            .SingleAsync();
    }

    private static void AssertOrderedWithinMethod(
        string source,
        string methodStart,
        string nextMethodStart,
        params string[] expectedInOrder)
    {
        var start = source.IndexOf(methodStart, StringComparison.Ordinal);
        var end = source.IndexOf(nextMethodStart, start + methodStart.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate method beginning with '{methodStart}'.");

        var method = source[start..end];
        var cursor = 0;
        foreach (var expected in expectedInOrder)
        {
            var index = method.IndexOf(expected, cursor, StringComparison.Ordinal);
            Assert.True(index >= cursor, $"Expected '{expected}' after offset {cursor} in '{methodStart}'.");
            cursor = index + expected.Length;
        }
    }
}
