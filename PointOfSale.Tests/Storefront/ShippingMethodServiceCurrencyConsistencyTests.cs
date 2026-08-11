using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Pricing;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Tests.Storefront;

public sealed class ShippingMethodServiceCurrencyConsistencyTests
{
    [Fact]
    public async Task GetShippingMethodsAsync_UsesProjectedTenantCurrencyAcrossAllMethods()
    {
        var tenantId = Guid.NewGuid();
        var repository = new Mock<IShippingMethodReadRepository>();
        repository
            .Setup(service => service.GetActiveShippingMethodsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShippingMethodDocument>
            {
                CreateShippingMethod(tenantId, "sar", 20m),
                CreateShippingMethod(tenantId, "SAR", 35m)
            });

        await using var dbContext = CreateDbContext();
        var service = new ShippingMethodService(
            repository.Object,
            dbContext,
            CreatePassThroughCache(),
            new ShippingCalculationService(),
            new CommerceReliabilityTelemetry(),
            NullLogger<ShippingMethodService>.Instance);

        var result = await service.GetShippingMethodsAsync(
            tenantId,
            cartSubtotal: 100m,
            totalWeight: 0m,
            correlationId: "currency-ok",
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, method => Assert.Equal("SAR", method.Currency));
    }

    [Fact]
    public async Task GetShippingMethodsAsync_WhenProjectedCurrenciesMismatch_ThrowsOutOfSync()
    {
        var tenantId = Guid.NewGuid();
        var repository = new Mock<IShippingMethodReadRepository>();
        repository
            .Setup(service => service.GetActiveShippingMethodsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShippingMethodDocument>
            {
                CreateShippingMethod(tenantId, "SAR", 20m),
                CreateShippingMethod(tenantId, "USD", 35m)
            });

        await using var dbContext = CreateDbContext();
        var service = new ShippingMethodService(
            repository.Object,
            dbContext,
            CreatePassThroughCache(),
            new ShippingCalculationService(),
            new CommerceReliabilityTelemetry(),
            NullLogger<ShippingMethodService>.Instance);

        await Assert.ThrowsAsync<StorefrontConfigOutOfSyncException>(() =>
            service.GetShippingMethodsAsync(
                tenantId,
                cartSubtotal: 100m,
                totalWeight: 0m,
                correlationId: "currency-mismatch",
                cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task GetShippingMethodsAsync_WithWarehouse_FiltersCarrierBackedMethodsByEnabledOriginReadyAssignment()
    {
        var tenantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var bostaAccountId = Guid.NewGuid();
        var fedExAccountId = Guid.NewGuid();
        var repository = new Mock<IShippingMethodReadRepository>();
        repository
            .Setup(service => service.GetActiveShippingMethodsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShippingMethodDocument>
            {
                CreateShippingMethod(tenantId, "SAR", 20m, CarrierCodes.Bosta),
                CreateShippingMethod(tenantId, "SAR", 35m, CarrierCodes.FedEx),
                CreateShippingMethod(tenantId, "SAR", 10m, null)
            });

        await using var dbContext = CreateDbContext();
        dbContext.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Origin",
            IsActive = true
        });
        dbContext.CarrierAccounts.AddRange(
            new CarrierAccount
            {
                Id = bostaAccountId,
                TenantId = tenantId,
                CarrierCode = CarrierCodes.Bosta,
                State = CarrierAccountState.Active,
                OperationalState = CarrierOperationalState.Ready
            },
            new CarrierAccount
            {
                Id = fedExAccountId,
                TenantId = tenantId,
                CarrierCode = CarrierCodes.FedEx,
                State = CarrierAccountState.Active,
                OperationalState = CarrierOperationalState.Ready
            });
        dbContext.CarrierWarehouseAssignments.AddRange(
            new CarrierWarehouseAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CarrierAccountId = bostaAccountId,
                WarehouseId = warehouseId,
                IsEnabled = true,
                OriginReadinessPublished = true
            },
            new CarrierWarehouseAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CarrierAccountId = fedExAccountId,
                WarehouseId = warehouseId,
                IsEnabled = true,
                OriginReadinessPublished = false
            });
        await dbContext.SaveChangesAsync();

        var service = new ShippingMethodService(
            repository.Object,
            dbContext,
            CreatePassThroughCache(),
            new ShippingCalculationService(),
            new CommerceReliabilityTelemetry(),
            NullLogger<ShippingMethodService>.Instance);

        var result = await service.GetShippingMethodsAsync(
            tenantId,
            cartSubtotal: 100m,
            totalWeight: 0m,
            correlationId: "warehouse-filter",
            warehouseId: warehouseId,
            cancellationToken: CancellationToken.None);

        var method = Assert.Single(result);
        Assert.Equal("Bosta", method.CarrierName);
    }

    [Fact]
    public async Task GetShippingMethodsAsync_WithWarehouseAndNoAssignment_FailsClosed()
    {
        var tenantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var repository = new Mock<IShippingMethodReadRepository>();
        repository
            .Setup(service => service.GetActiveShippingMethodsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShippingMethodDocument>
            {
                CreateShippingMethod(tenantId, "SAR", 20m, CarrierCodes.FedEx)
            });

        await using var dbContext = CreateDbContext();
        dbContext.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Origin",
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        var service = new ShippingMethodService(
            repository.Object,
            dbContext,
            CreatePassThroughCache(),
            new ShippingCalculationService(),
            new CommerceReliabilityTelemetry(),
            NullLogger<ShippingMethodService>.Instance);

        var result = await service.GetShippingMethodsAsync(
            tenantId,
            cartSubtotal: 100m,
            totalWeight: 0m,
            correlationId: "warehouse-missing-assignment",
            warehouseId: warehouseId,
            cancellationToken: CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetShippingMethodsAvailabilityAsync_WithActiveMethods_ReturnsAvailable()
    {
        var tenantId = Guid.NewGuid();
        var repository = new Mock<IShippingMethodReadRepository>();
        repository
            .Setup(service => service.GetActiveShippingMethodsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShippingMethodDocument>
            {
                CreateShippingMethod(tenantId, "SAR", 20m)
            });

        await using var dbContext = CreateDbContext();
        dbContext.ShippingMethods.Add(CreateConfiguredShippingMethod(tenantId, true));
        await dbContext.SaveChangesAsync();
        var service = CreateService(repository.Object, dbContext);

        var result = await service.GetShippingMethodsAvailabilityAsync(
            tenantId,
            cartSubtotal: 100m,
            totalWeight: 0m,
            correlationId: "shipping-available",
            cancellationToken: CancellationToken.None);

        Assert.Equal("AVAILABLE", result.Status);
        Assert.Single(result.ShippingMethods);
        Assert.Equal(1, result.ConfiguredCount);
        Assert.Equal(1, result.ActiveCount);
        Assert.Equal(1, result.ApplicableCount);
    }

    [Fact]
    public async Task GetShippingMethodsAvailabilityAsync_WithZeroConfiguredMethods_ReturnsEmpty()
    {
        var tenantId = Guid.NewGuid();
        var repository = new Mock<IShippingMethodReadRepository>();
        repository
            .Setup(service => service.GetActiveShippingMethodsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShippingMethodDocument>());

        await using var dbContext = CreateDbContext();
        var service = CreateService(repository.Object, dbContext);

        var result = await service.GetShippingMethodsAvailabilityAsync(
            tenantId,
            cartSubtotal: 100m,
            totalWeight: 0m,
            correlationId: "shipping-empty",
            cancellationToken: CancellationToken.None);

        Assert.Equal("EMPTY", result.Status);
        Assert.Empty(result.ShippingMethods);
        Assert.Equal(0, result.ConfiguredCount);
        Assert.Equal(0, result.ActiveCount);
    }

    [Fact]
    public async Task GetShippingMethodsAvailabilityAsync_WithOnlyDisabledMethods_ReturnsEmptyWithConfiguredEvidence()
    {
        var tenantId = Guid.NewGuid();
        var repository = new Mock<IShippingMethodReadRepository>();
        repository
            .Setup(service => service.GetActiveShippingMethodsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShippingMethodDocument>());

        await using var dbContext = CreateDbContext();
        dbContext.ShippingMethods.Add(CreateConfiguredShippingMethod(tenantId, false));
        await dbContext.SaveChangesAsync();
        var service = CreateService(repository.Object, dbContext);

        var result = await service.GetShippingMethodsAvailabilityAsync(
            tenantId,
            cartSubtotal: 100m,
            totalWeight: 0m,
            correlationId: "shipping-disabled",
            cancellationToken: CancellationToken.None);

        Assert.Equal("EMPTY", result.Status);
        Assert.Empty(result.ShippingMethods);
        Assert.Equal(1, result.ConfiguredCount);
        Assert.Equal(0, result.ActiveCount);
    }

    [Fact]
    public async Task GetShippingMethodsAvailabilityAsync_WithActiveButNotApplicableMethods_ReturnsNotApplicable()
    {
        var tenantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var repository = new Mock<IShippingMethodReadRepository>();
        repository
            .Setup(service => service.GetActiveShippingMethodsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShippingMethodDocument>
            {
                CreateShippingMethod(tenantId, "SAR", 20m, CarrierCodes.FedEx)
            });

        await using var dbContext = CreateDbContext();
        dbContext.ShippingMethods.Add(CreateConfiguredShippingMethod(tenantId, true));
        dbContext.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Origin",
            IsActive = true
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(repository.Object, dbContext);

        var result = await service.GetShippingMethodsAvailabilityAsync(
            tenantId,
            cartSubtotal: 100m,
            totalWeight: 0m,
            correlationId: "shipping-not-applicable",
            warehouseId: warehouseId,
            cancellationToken: CancellationToken.None);

        Assert.Equal("NOT_APPLICABLE", result.Status);
        Assert.Empty(result.ShippingMethods);
        Assert.Equal(1, result.ConfiguredCount);
        Assert.Equal(1, result.ActiveCount);
        Assert.Equal(0, result.ApplicableCount);
    }

    private static ShippingMethodDocument CreateShippingMethod(
        Guid tenantId,
        string currency,
        decimal baseRate,
        string? carrierCode = null)
    {
        return new ShippingMethodDocument
        {
            TenantId = tenantId,
            ShippingMethodId = Guid.NewGuid(),
            Name = "Standard",
            BaseRate = baseRate,
            PerKgRate = 0m,
            Currency = currency,
            EstimatedDeliveryDaysMin = 1,
            EstimatedDeliveryDaysMax = 3,
            IsActive = true,
            CarrierCode = carrierCode,
            CarrierName = carrierCode switch
            {
                CarrierCodes.Bosta => "Bosta",
                CarrierCodes.FedEx => "FedEx",
                _ => null
            }
        };
    }

    private static ShippingMethod CreateConfiguredShippingMethod(Guid tenantId, bool isActive)
    {
        return new ShippingMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = isActive ? "Standard" : "Disabled Standard",
            BaseRate = 20m,
            PerKgRate = 0m,
            IsActive = isActive,
            SortOrder = 1,
            EstimatedDeliveryDaysMin = 1,
            EstimatedDeliveryDaysMax = 3,
        };
    }

    private static ShippingMethodService CreateService(
        IShippingMethodReadRepository repository,
        ApplicationDbContext dbContext)
    {
        return new ShippingMethodService(
            repository,
            dbContext,
            CreatePassThroughCache(),
            new ShippingCalculationService(),
            new CommerceReliabilityTelemetry(),
            NullLogger<ShippingMethodService>.Instance);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static IShippingMethodCacheService CreatePassThroughCache()
    {
        var cache = new Mock<IShippingMethodCacheService>(MockBehavior.Loose);
        cache
            .Setup(service => service.GetOrCreateAsync(
                It.IsAny<Guid>(),
                It.IsAny<Func<CancellationToken, Task<IReadOnlyCollection<ShippingMethodDocument>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, Func<CancellationToken, Task<IReadOnlyCollection<ShippingMethodDocument>>>, CancellationToken>(
                async (_, factory, cancellationToken) => (await factory(cancellationToken)).ToList());
        cache
            .Setup(service => service.InvalidateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return cache.Object;
    }
}
