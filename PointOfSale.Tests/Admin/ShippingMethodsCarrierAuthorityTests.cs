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
    public async Task SharedCountryScopeAndWarehousePersistAcrossRateUpdateAndIndependentReadback()
    {
        var tenant = Guid.NewGuid(); await using var db = CreateDbContext(tenant);
        var warehouse = SeedWarehouse(db, tenant); var controller = CreateController(db, tenant);
        const string countries = "[ \"EG\", \"SA\" ]";
        var created = Assert.IsType<CreatedAtActionResult>((await controller.CreateShippingMethod(new()
        {
            Name = "SYNTHETIC-country-rate-roundtrip", CarrierCode = "manual", IsActive = false,
            FulfillmentWarehouseId = warehouse, AvailableCountries = countries, BaseRate = 25m, PerKgRate = 2m,
        })).Result);
        var id = Assert.IsType<ShippingMethodDto>(created.Value).Id;
        db.ChangeTracker.Clear();
        var updated = await controller.UpdateShippingMethod(id, new()
        {
            Name = "SYNTHETIC-country-rate-roundtrip", CarrierCode = "manual", IsActive = false,
            FulfillmentWarehouseId = warehouse, AvailableCountries = countries, BaseRate = 30m, PerKgRate = 3m,
        });
        Assert.IsType<NoContentResult>(updated); db.ChangeTracker.Clear();
        var read = Assert.IsType<ShippingMethodDto>(Assert.IsType<OkObjectResult>((await controller.GetShippingMethod(id)).Result).Value);
        Assert.Equal(countries, read.AvailableCountries); Assert.Equal(warehouse, read.FulfillmentWarehouseId);
        Assert.Equal(30m, read.BaseRate); Assert.Equal(3m, read.PerKgRate); Assert.False(read.IsActive);
        var persisted = await db.ShippingMethods.AsNoTracking().SingleAsync();
        Assert.Equal(countries, persisted.AvailableCountries); Assert.Equal(warehouse, persisted.FulfillmentWarehouseId);
        Assert.Empty(await db.CarrierAccounts.IgnoreQueryFilters().ToListAsync());
        Assert.IsType<NotFoundObjectResult>((await CreateController(db, Guid.NewGuid()).GetShippingMethod(id)).Result);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"EG\",\"EG\"]")]
    [InlineData("[\"ZZ\"]")]
    [InlineData("null")]
    public async Task InvalidCountryUpdateCannotBroadenPersistedScopeOrChangeRates(string invalidScope)
    {
        var tenant = Guid.NewGuid(); await using var db = CreateDbContext(tenant);
        var warehouse = SeedWarehouse(db, tenant);
        var method = new ShippingMethod { Id = Guid.NewGuid(), TenantId = tenant, Name = "SYNTHETIC-reject-invalid-scope", IsActive = false,
            CarrierCode = "manual", AvailableCountries = "[\"EG\"]", FulfillmentWarehouseId = warehouse, BaseRate = 25m };
        db.ShippingMethods.Add(method); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.IsType<BadRequestObjectResult>(await CreateController(db, tenant).UpdateShippingMethod(method.Id, new()
        {
            Name = method.Name, CarrierCode = "manual", IsActive = false, FulfillmentWarehouseId = warehouse,
            AvailableCountries = invalidScope, BaseRate = 99m,
        }));
        var retained = await db.ShippingMethods.AsNoTracking().SingleAsync();
        Assert.Equal("[\"EG\"]", retained.AvailableCountries); Assert.Equal(25m, retained.BaseRate);
        Assert.Equal(warehouse, retained.FulfillmentWarehouseId);
    }

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
        var controller = CreateController(db, tenantId,
            ReadyCarrierAuthority(tenantId, warehouseId, CarrierCodes.Bosta, true, "").Object);

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

    [Fact]
    public async Task CreateShippingMethod_ActiveAramexWithoutAuthoritativeReadiness_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        var warehouseId = SeedWarehouse(db, tenantId);
        var authority = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        authority.Setup(service => service.GetIntegrationReadinessAsync(
                tenantId,
                CarrierCodes.Aramex,
                null,
                warehouseId,
                CarrierIntegrationPurpose.StorefrontAdmission,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierIntegrationReadiness(
                CarrierCodes.Aramex,
                "Aramex",
                IsManual: false,
                Enabled: false,
                CanExecute: false,
                Environment: null,
                CarrierProductionCapability.Partial,
                new[] { "CARRIER_CONFIGURATION_MISSING", "CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED" },
                OriginReadinessRequired: false,
                OriginReadinessSatisfied: true,
                CredentialAvailability: "Missing"));
        var controller = CreateController(db, tenantId, authority.Object);

        var result = await controller.CreateShippingMethod(new CreateShippingMethodDto
        {
            Name = "Unavailable Aramex",
            CarrierCode = CarrierCodes.Aramex,
            ServiceLevel = "standard",
            FulfillmentWarehouseId = warehouseId,
            IsActive = true,
            EstimatedDeliveryDaysMin = 1,
            EstimatedDeliveryDaysMax = 3
        });

        var rejected = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("CARRIER_CONFIGURATION_MISSING", System.Text.Json.JsonSerializer.Serialize(rejected.Value));
        Assert.Empty(await db.ShippingMethods.ToListAsync());
        authority.VerifyAll();
    }

    [Fact]
    public async Task CreateShippingMethod_ActiveDhlWithoutRemoteAccountReadiness_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        var warehouseId = SeedWarehouse(db, tenantId);
        var authority = ReadyCarrierAuthority(tenantId, warehouseId, CarrierCodes.Dhl, canExecute: false,
            "DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED");
        var controller = CreateController(db, tenantId, authority.Object);

        var result = await controller.CreateShippingMethod(new CreateShippingMethodDto
        {
            Name = "Unavailable DHL",
            CarrierCode = CarrierCodes.Dhl,
            ServiceLevel = "express",
            FulfillmentWarehouseId = warehouseId,
            IsActive = true,
            EstimatedDeliveryDaysMin = 1,
            EstimatedDeliveryDaysMax = 3
        });

        var rejected = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED", System.Text.Json.JsonSerializer.Serialize(rejected.Value));
        Assert.Empty(await db.ShippingMethods.ToListAsync());
        authority.VerifyAll();
    }

    [Fact]
    public async Task CreateShippingMethod_UnreadyDhlStillAllowsInactiveDraft()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        var warehouseId = SeedWarehouse(db, tenantId);
        var authority = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        var controller = CreateController(db, tenantId, authority.Object);

        var result = await controller.CreateShippingMethod(new CreateShippingMethodDto
        {
            Name = "DHL draft", CarrierCode = CarrierCodes.Dhl, ServiceLevel = "express",
            FulfillmentWarehouseId = warehouseId, IsActive = false,
            EstimatedDeliveryDaysMin = 1, EstimatedDeliveryDaysMax = 3
        });

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.False((await db.ShippingMethods.SingleAsync()).IsActive);
        authority.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateShippingMethod_AramexRequiresReadinessOnlyForActiveMethods(bool isActive)
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        var warehouseId = SeedWarehouse(db, tenantId);
        var authority = ReadyAramexAuthority(tenantId, warehouseId);
        var controller = CreateController(db, tenantId, authority.Object);

        var result = await controller.CreateShippingMethod(new CreateShippingMethodDto
        {
            Name = "Synthetic Aramex method",
            CarrierCode = CarrierCodes.Aramex,
            ServiceLevel = "standard",
            FulfillmentWarehouseId = warehouseId,
            IsActive = isActive,
            EstimatedDeliveryDaysMin = 1,
            EstimatedDeliveryDaysMax = 3
        });

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(isActive, (await db.ShippingMethods.SingleAsync()).IsActive);
        authority.Verify(service => service.GetIntegrationReadinessAsync(
            tenantId, CarrierCodes.Aramex, null, warehouseId,
            CarrierIntegrationPurpose.StorefrontAdmission, It.IsAny<CancellationToken>()),
            isActive ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateShippingMethod_UnreadyAramexBlocksActivationButAllowsDeactivation(bool isActive)
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        var warehouseId = SeedWarehouse(db, tenantId);
        var method = new ShippingMethod
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Existing synthetic method",
            CarrierCode = CarrierCodes.Aramex, ServiceLevel = "standard", IsActive = false,
            FulfillmentWarehouseId = warehouseId
        };
        db.ShippingMethods.Add(method);
        await db.SaveChangesAsync();
        var authority = ReadyAramexAuthority(tenantId, warehouseId, canExecute: false);
        var controller = CreateController(db, tenantId, authority.Object);

        var result = await controller.UpdateShippingMethod(method.Id, new UpdateShippingMethodDto
        {
            Name = "Updated synthetic method", CarrierCode = CarrierCodes.Aramex,
            ServiceLevel = "standard", FulfillmentWarehouseId = warehouseId,
            IsActive = isActive, EstimatedDeliveryDaysMin = 1, EstimatedDeliveryDaysMax = 3
        });

        if (isActive)
        {
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Existing synthetic method", method.Name);
        }
        else
        {
            Assert.IsType<NoContentResult>(result);
            Assert.Equal("Updated synthetic method", method.Name);
        }
        Assert.False((await db.ShippingMethods.SingleAsync()).IsActive);
        authority.Verify(service => service.GetIntegrationReadinessAsync(
            tenantId, CarrierCodes.Aramex, null, warehouseId,
            CarrierIntegrationPurpose.StorefrontAdmission, It.IsAny<CancellationToken>()),
            isActive ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateShippingMethod_UnreadyDhlBlocksActivationButAllowsDeactivation(bool isActive)
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        var warehouseId = SeedWarehouse(db, tenantId);
        var method = new ShippingMethod
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Existing DHL method",
            CarrierCode = CarrierCodes.Dhl, ServiceLevel = "express", IsActive = !isActive,
            FulfillmentWarehouseId = warehouseId
        };
        db.ShippingMethods.Add(method);
        await db.SaveChangesAsync();
        var authority = ReadyCarrierAuthority(tenantId, warehouseId, CarrierCodes.Dhl, false,
            "DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED");
        var controller = CreateController(db, tenantId, authority.Object);

        var result = await controller.UpdateShippingMethod(method.Id, new UpdateShippingMethodDto
        {
            Name = "Updated DHL method", CarrierCode = CarrierCodes.Dhl, ServiceLevel = "express",
            FulfillmentWarehouseId = warehouseId, IsActive = isActive,
            EstimatedDeliveryDaysMin = 1, EstimatedDeliveryDaysMax = 3
        });

        if (isActive)
        {
            var rejected = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED", System.Text.Json.JsonSerializer.Serialize(rejected.Value));
            Assert.Equal("Existing DHL method", method.Name);
        }
        else
        {
            Assert.IsType<NoContentResult>(result);
            Assert.Equal("Updated DHL method", method.Name);
        }
        Assert.False((await db.ShippingMethods.SingleAsync()).IsActive);
        authority.Verify(service => service.GetIntegrationReadinessAsync(
            tenantId, CarrierCodes.Dhl, null, warehouseId,
            CarrierIntegrationPurpose.StorefrontAdmission, It.IsAny<CancellationToken>()),
            isActive ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task MethodRead_AppliesCarrierAndWarehouseFilters_AndRejectsUnavailableWarehouse()
    {
        var tenant = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        var first = SeedWarehouse(db, tenant);
        var second = SeedWarehouse(db, tenant);
        db.ShippingMethods.AddRange(
            new ShippingMethod { Id = Guid.NewGuid(), TenantId = tenant, Name = "Manual first", IsActive = true, FulfillmentWarehouseId = first },
            new ShippingMethod { Id = Guid.NewGuid(), TenantId = tenant, Name = "Manual second", CarrierCode = "manual", IsActive = true, FulfillmentWarehouseId = second },
            new ShippingMethod { Id = Guid.NewGuid(), TenantId = tenant, Name = "Bosta", CarrierCode = "bosta", IsActive = true, FulfillmentWarehouseId = first });
        await db.SaveChangesAsync();
        var controller = CreateController(db, tenant);
        var result = await controller.GetShippingMethods(true, "manual", first);
        var methods = Assert.IsAssignableFrom<IEnumerable<ShippingMethodDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Manual first", Assert.Single(methods).Name);
        Assert.IsType<BadRequestObjectResult>((await controller.GetShippingMethods(true, "manual", Guid.NewGuid())).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.GetShippingMethods(true, "unknown", first)).Result);
    }

    [Fact]
    public async Task BostaActivation_IsServerSideRejectedWhenUnready_ButDraftIsAllowed()
    {
        var tenant = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        var warehouse = SeedWarehouse(db, tenant);
        var authority = ReadyCarrierAuthority(tenant, warehouse, CarrierCodes.Bosta, false, "CARRIER_CONFIGURATION_MISSING");
        var controller = CreateController(db, tenant, authority.Object);
        var dto = new CreateShippingMethodDto { Name = "Bosta draft", CarrierCode = "bosta", FulfillmentWarehouseId = warehouse };
        Assert.IsType<BadRequestObjectResult>((await controller.CreateShippingMethod(dto)).Result);
        Assert.Empty(db.ShippingMethods);
        dto.IsActive = false;
        Assert.IsType<CreatedAtActionResult>((await controller.CreateShippingMethod(dto)).Result);
        Assert.False((await db.ShippingMethods.SingleAsync()).IsActive);
    }

    private static Mock<ICarrierIntegrationAuthority> ReadyAramexAuthority(
        Guid tenantId, Guid warehouseId, bool canExecute = true)
    {
        var authority = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        authority.Setup(service => service.GetIntegrationReadinessAsync(
                tenantId, CarrierCodes.Aramex, null, warehouseId,
                CarrierIntegrationPurpose.StorefrontAdmission, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierIntegrationReadiness(
                CarrierCodes.Aramex, "Aramex", false, canExecute, canExecute, "Sandbox",
                CarrierProductionCapability.Partial,
                canExecute ? Array.Empty<string>() : new[] { "CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED" },
                false, true, "Available"));
        return authority;
    }

    private static Mock<ICarrierIntegrationAuthority> ReadyCarrierAuthority(
        Guid tenantId, Guid warehouseId, string carrierCode, bool canExecute, string blocker)
    {
        var authority = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        authority.Setup(service => service.GetIntegrationReadinessAsync(
                tenantId, carrierCode, null, warehouseId,
                CarrierIntegrationPurpose.StorefrontAdmission, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierIntegrationReadiness(
                carrierCode, carrierCode == CarrierCodes.Dhl ? "DHL Express" : carrierCode,
                false, canExecute, canExecute, "Sandbox", CarrierProductionCapability.Partial,
                canExecute ? Array.Empty<string>() : new[] { blocker }, false, true, "Available"));
        return authority;
    }

    private static ShippingMethodsController CreateController(
        ApplicationDbContext db,
        Guid tenantId,
        ICarrierIntegrationAuthority? authority = null)
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
            authority ?? Mock.Of<ICarrierIntegrationAuthority>(),
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
        var aramex = CreateAdapter(CarrierCodes.Aramex, "Aramex", "standard", "international", "economy");
        var dhl = CreateAdapter(CarrierCodes.Dhl, "DHL Express", "express", "economy", "domestic");
        var registry = new Mock<IShippingCarrierRegistry>();
        registry.Setup(registry => registry.GetAdapter(CarrierCodes.Manual)).Returns(manual);
        registry.Setup(registry => registry.GetAdapter(CarrierCodes.Bosta)).Returns(bosta);
        registry.Setup(registry => registry.GetAdapter(CarrierCodes.Aramex)).Returns(aramex);
        registry.Setup(registry => registry.GetAdapter(CarrierCodes.Dhl)).Returns(dhl);
        registry.Setup(registry => registry.IsCarrierSupported(CarrierCodes.Manual)).Returns(true);
        registry.Setup(registry => registry.IsCarrierSupported(CarrierCodes.Bosta)).Returns(true);
        registry.Setup(registry => registry.IsCarrierSupported(CarrierCodes.Aramex)).Returns(true);
        registry.Setup(registry => registry.IsCarrierSupported(CarrierCodes.Dhl)).Returns(true);
        registry.Setup(registry => registry.GetSupportedCarrierCodes()).Returns(new[] { CarrierCodes.Manual, CarrierCodes.Bosta, CarrierCodes.Aramex, CarrierCodes.Dhl });
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
