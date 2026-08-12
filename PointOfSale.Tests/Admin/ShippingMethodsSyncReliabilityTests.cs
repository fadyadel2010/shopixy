using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services.Pricing;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Admin;

public sealed class ShippingMethodsSyncReliabilityTests
{
    [Fact]
    public async Task SyncShippingMethods_PersistsOutboxBeforeReturningSuccess()
    {
        var tenantId = Guid.NewGuid();
        var callOrder = new List<string>();
        await using var dbContext = CreateDbContext(tenantId, callOrder);
        var warehouseId = AddWarehouse(dbContext, tenantId);
        dbContext.ShippingMethods.Add(CreateShippingMethod(tenantId, "Express", warehouseId));
        await dbContext.SaveChangesAsync();
        dbContext.ResetTracking();

        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<ShippingMethodUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("publish"))
            .Returns(Task.CompletedTask);

        var controller = CreateController(dbContext, publishEndpoint.Object, tenantId);

        var result = await controller.SyncShippingMethods();

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));

        Assert.Equal(1, payload.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(1, payload.RootElement.GetProperty("committed").GetInt32());
        Assert.Equal(new[] { "publish", "save" }, callOrder);
        Assert.Equal(1, dbContext.TrackedSaveChangesCalls);
    }

    [Fact]
    public async Task SyncShippingMethods_WhenCommitFails_DoesNotReturnSuccess()
    {
        var tenantId = Guid.NewGuid();
        var callOrder = new List<string>();
        await using var dbContext = CreateDbContext(tenantId, callOrder);
        var warehouseId = AddWarehouse(dbContext, tenantId);
        dbContext.ShippingMethods.Add(CreateShippingMethod(tenantId, "Express", warehouseId));
        await dbContext.SaveChangesAsync();
        dbContext.ResetTracking();
        dbContext.ThrowOnSave = true;

        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<ShippingMethodUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("publish"))
            .Returns(Task.CompletedTask);

        var controller = CreateController(dbContext, publishEndpoint.Object, tenantId);

        var result = await controller.SyncShippingMethods();

        var serviceUnavailable = Assert.IsType<ObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(serviceUnavailable.Value));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, serviceUnavailable.StatusCode);
        Assert.Equal("SYNC_PERSISTENCE_FAILED", payload.RootElement.GetProperty("error").GetString());
        Assert.Equal(new[] { "publish", "save" }, callOrder);
        Assert.Equal(1, dbContext.TrackedSaveChangesCalls);
    }

    private static ShippingMethodsController CreateController(
        TrackingApplicationDbContext dbContext,
        IEventPublisher publishEndpoint,
        Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        return new ShippingMethodsController(
            dbContext,
            tenantContext,
            publishEndpoint,
            Mock.Of<IShippingCarrierRegistry>(),
            Mock.Of<IShippingCalculationService>(),
            new CommerceReliabilityTelemetry(),
            NullLogger<ShippingMethodsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static TrackingApplicationDbContext CreateDbContext(Guid tenantId, List<string> callOrder)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return new TrackingApplicationDbContext(options, tenantContext, callOrder);
    }

    private static Guid AddWarehouse(ApplicationDbContext dbContext, Guid tenantId)
    {
        var warehouseId = Guid.NewGuid();
        dbContext.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Origin",
            IsActive = true
        });
        return warehouseId;
    }

    private static ShippingMethod CreateShippingMethod(Guid tenantId, string name, Guid warehouseId)
    {
        return new ShippingMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            BaseRate = 50,
            PerKgRate = 10,
            EstimatedDeliveryDaysMin = 1,
            EstimatedDeliveryDaysMax = 2,
            IsActive = true,
            FulfillmentWarehouseId = warehouseId,
            SortOrder = 1,
            CreatedAt = DateTime.UtcNow
        };
    }

    private sealed class TrackingApplicationDbContext : ApplicationDbContext
    {
        private readonly List<string> _callOrder;

        public TrackingApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext tenantContext,
            List<string> callOrder)
            : base(options, tenantContext)
        {
            _callOrder = callOrder;
        }

        public bool ThrowOnSave { get; set; }
        public int TrackedSaveChangesCalls { get; private set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            TrackedSaveChangesCalls++;
            _callOrder.Add("save");

            if (ThrowOnSave)
            {
                throw new DbUpdateException("Simulated commit failure.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }

        public void ResetTracking()
        {
            TrackedSaveChangesCalls = 0;
            _callOrder.Clear();
        }
    }
}
