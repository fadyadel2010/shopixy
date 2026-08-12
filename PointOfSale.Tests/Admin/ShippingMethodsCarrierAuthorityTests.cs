using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services.Pricing;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Admin;

public sealed class ShippingMethodsCarrierAuthorityTests
{
    [Fact]
    public async Task CreateShippingMethod_MissingCarrierCodeAndSpoofedCarrierName_RemainsCanonicalManual()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        var warehouseId = SeedWarehouse(db, tenantId);
        var controller = CreateController(db, tenantId);

        var result = await controller.CreateShippingMethod(new CreateShippingMethodDto
        {
            Name = "Local delivery",
            CarrierName = "Bosta",
            BaseRate = 10,
            PerKgRate = 1,
            FulfillmentWarehouseId = warehouseId,
            EstimatedDeliveryDaysMin = 1,
            EstimatedDeliveryDaysMax = 2
        });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<ShippingMethodDto>(created.Value);
        Assert.Equal(CarrierCodes.Manual, dto.CarrierCode);
        Assert.Equal("Manual Fulfillment", dto.CarrierName);
        Assert.Equal("standard", dto.ServiceLevel);

        var persisted = await db.ShippingMethods.SingleAsync();
        Assert.Equal(CarrierCodes.Manual, persisted.CarrierCode);
        Assert.Equal("Manual Fulfillment", persisted.CarrierName);
        Assert.Equal("standard", persisted.ServiceLevel);
        Assert.Empty(await db.CarrierAccounts.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task CreateShippingMethod_BostaCarrierCode_DerivesDisplayNameFromRegistry()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        var warehouseId = SeedWarehouse(db, tenantId);
        var controller = CreateController(db, tenantId);

        var result = await controller.CreateShippingMethod(new CreateShippingMethodDto
        {
            Name = "Carrier delivery",
            CarrierCode = CarrierCodes.Bosta,
            CarrierName = "Manual Fulfillment",
            ServiceLevel = "standard",
            BaseRate = 25,
            PerKgRate = 2,
            FulfillmentWarehouseId = warehouseId,
            EstimatedDeliveryDaysMin = 1,
            EstimatedDeliveryDaysMax = 3
        });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<ShippingMethodDto>(created.Value);
        Assert.Equal(CarrierCodes.Bosta, dto.CarrierCode);
        Assert.Equal("Bosta", dto.CarrierName);
        Assert.Equal("standard", dto.ServiceLevel);

        var persisted = await db.ShippingMethods.SingleAsync();
        Assert.Equal(CarrierCodes.Bosta, persisted.CarrierCode);
        Assert.Equal("Bosta", persisted.CarrierName);
    }

    [Fact]
    public async Task CreateShippingMethod_ActiveWithoutFulfillmentWarehouse_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        var controller = CreateController(db, tenantId);

        var result = await controller.CreateShippingMethod(new CreateShippingMethodDto
        {
            Name = "Missing origin",
            CarrierCode = CarrierCodes.Manual,
            BaseRate = 10,
            PerKgRate = 1,
            IsActive = true,
            EstimatedDeliveryDaysMin = 1,
            EstimatedDeliveryDaysMax = 2
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.ShippingMethods.ToListAsync());
    }

    [Fact]
    public async Task CreateShippingMethod_UnknownCarrierCode_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        var controller = CreateController(db, tenantId);

        var result = await controller.CreateShippingMethod(new CreateShippingMethodDto
        {
            Name = "Unknown delivery",
            CarrierCode = "unknown-carrier",
            BaseRate = 10,
            PerKgRate = 1,
            EstimatedDeliveryDaysMin = 1,
            EstimatedDeliveryDaysMax = 2
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.ShippingMethods.ToListAsync());
    }

    [Fact]
    public async Task CreateShippingMethod_UnsupportedServiceLevel_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        var controller = CreateController(db, tenantId);

        var result = await controller.CreateShippingMethod(new CreateShippingMethodDto
        {
            Name = "Manual express",
            CarrierCode = CarrierCodes.Manual,
            ServiceLevel = "express",
            BaseRate = 10,
            PerKgRate = 1,
            EstimatedDeliveryDaysMin = 1,
            EstimatedDeliveryDaysMax = 2
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.ShippingMethods.ToListAsync());
    }

    private static ShippingMethodsController CreateController(ApplicationDbContext db, Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var publisher = new Mock<IEventPublisher>();
        publisher
            .Setup(publisher => publisher.PublishAsync(It.IsAny<ShippingMethodUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new ShippingMethodsController(
            db,
            tenantContext,
            publisher.Object,
            CreateRegistry(),
            Mock.Of<IShippingCalculationService>(),
            new CommerceReliabilityTelemetry(),
            NullLogger<ShippingMethodsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return new ApplicationDbContext(options, tenantContext);
    }

    private static Guid SeedWarehouse(ApplicationDbContext db, Guid tenantId)
    {
        var warehouseId = Guid.NewGuid();
        db.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Main",
            IsActive = true
        });
        db.SaveChanges();
        return warehouseId;
    }

    private static IShippingCarrierRegistry CreateRegistry()
    {
        var manual = CreateAdapter(CarrierCodes.Manual, "Manual Fulfillment", "standard");
        var bosta = CreateAdapter(CarrierCodes.Bosta, "Bosta", "standard");
        var registry = new Mock<IShippingCarrierRegistry>();
        registry.Setup(registry => registry.GetAdapter(CarrierCodes.Manual)).Returns(manual);
        registry.Setup(registry => registry.GetAdapter(CarrierCodes.Bosta)).Returns(bosta);
        registry.Setup(registry => registry.IsCarrierSupported(CarrierCodes.Manual)).Returns(true);
        registry.Setup(registry => registry.IsCarrierSupported(CarrierCodes.Bosta)).Returns(true);
        registry.Setup(registry => registry.GetSupportedCarrierCodes()).Returns(new[] { CarrierCodes.Manual, CarrierCodes.Bosta });
        return registry.Object;
    }

    private static IShippingCarrierAdapter CreateAdapter(string carrierCode, string displayName, params string[] serviceLevels)
    {
        var adapter = new Mock<IShippingCarrierAdapter>();
        adapter.SetupGet(adapter => adapter.CarrierCode).Returns(carrierCode);
        adapter.SetupGet(adapter => adapter.DisplayName).Returns(displayName);
        adapter.Setup(adapter => adapter.GetSupportedServiceLevels()).Returns(serviceLevels
            .Select(level => new ServiceLevelInfo
            {
                Code = level,
                Name = level,
                TypicalDaysMin = 1,
                TypicalDaysMax = 3
            })
            .ToList());
        return adapter.Object;
    }
}
