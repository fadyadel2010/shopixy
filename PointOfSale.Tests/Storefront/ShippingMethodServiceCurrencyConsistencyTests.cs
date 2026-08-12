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
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Storefront;

public sealed class ShippingMethodServiceCurrencyConsistencyTests
{
    [Fact]
    public async Task GetShippingMethodsAsync_UsesProjectedTenantCurrencyAcrossAllMethods()
    {
        var tenantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var repository = new Mock<IShippingMethodReadRepository>();
        repository
            .Setup(service => service.GetActiveShippingMethodsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShippingMethodDocument>
            {
                CreateShippingMethod(tenantId, "sar", 20m, fulfillmentWarehouseId: warehouseId),
                CreateShippingMethod(tenantId, "SAR", 35m, fulfillmentWarehouseId: warehouseId)
            });

        await using var dbContext = CreateDbContext();
        SeedWarehouse(dbContext, tenantId, warehouseId);
        var service = new ShippingMethodService(
            repository.Object,
            dbContext,
            CreatePassThroughCache(),
            new ShippingCalculationService(),
            new CommerceReliabilityTelemetry(),
            NullLogger<ShippingMethodService>.Instance,
            new FakeCarrierIntegrationAuthority(new Dictionary<string, bool>
            {
                [CarrierCodes.Bosta] = true,
                [CarrierCodes.FedEx] = false
            }));

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
        var warehouseId = Guid.NewGuid();
        var repository = new Mock<IShippingMethodReadRepository>();
        repository
            .Setup(service => service.GetActiveShippingMethodsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShippingMethodDocument>
            {
                CreateShippingMethod(tenantId, "SAR", 20m, fulfillmentWarehouseId: warehouseId),
                CreateShippingMethod(tenantId, "USD", 35m, fulfillmentWarehouseId: warehouseId)
            });

        await using var dbContext = CreateDbContext();
        SeedWarehouse(dbContext, tenantId, warehouseId);
        var service = new ShippingMethodService(
            repository.Object,
            dbContext,
            CreatePassThroughCache(),
            new ShippingCalculationService(),
            new CommerceReliabilityTelemetry(),
            NullLogger<ShippingMethodService>.Instance,
            new FakeCarrierIntegrationAuthority(new Dictionary<string, bool>
            {
                [CarrierCodes.Bosta] = true,
                [CarrierCodes.FedEx] = false
            }));

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
                CreateShippingMethod(tenantId, "SAR", 20m, CarrierCodes.Bosta, warehouseId),
                CreateShippingMethod(tenantId, "SAR", 35m, CarrierCodes.FedEx, warehouseId),
                CreateShippingMethod(tenantId, "SAR", 10m, null, warehouseId)
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
        dbContext.CarrierConfigurations.AddRange(
            new CarrierConfiguration
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CarrierCode = CarrierCodes.Bosta,
                DisplayName = "Bosta",
                IsEnabled = true
            },
            new CarrierConfiguration
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CarrierCode = CarrierCodes.FedEx,
                DisplayName = "FedEx",
                IsEnabled = true
            });
        await dbContext.SaveChangesAsync();

        var service = new ShippingMethodService(
            repository.Object,
            dbContext,
            CreatePassThroughCache(),
            new ShippingCalculationService(),
            new CommerceReliabilityTelemetry(),
            NullLogger<ShippingMethodService>.Instance,
            new FakeCarrierIntegrationAuthority(new Dictionary<string, bool>
            {
                [CarrierCodes.Bosta] = true,
                [CarrierCodes.FedEx] = false
            }));

        var result = await service.GetShippingMethodsAsync(
            tenantId,
            cartSubtotal: 100m,
            totalWeight: 0m,
            correlationId: "warehouse-filter",
            warehouseId: warehouseId,
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, method => method.CarrierName == "Bosta");
        Assert.Contains(result, method => method.CarrierName == null);
        Assert.DoesNotContain(result, method => method.CarrierName == "FedEx");
    }

    [Fact]
    public async Task GetShippingMethodsAsync_WithReadyAssignmentButNoEnabledCarrierConfiguration_FailsClosed()
    {
        var tenantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var repository = new Mock<IShippingMethodReadRepository>();
        repository
            .Setup(service => service.GetActiveShippingMethodsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShippingMethodDocument>
            {
                CreateShippingMethod(tenantId, "SAR", 20m, CarrierCodes.Bosta, warehouseId)
            });

        await using var dbContext = CreateDbContext();
        dbContext.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Origin",
            IsActive = true
        });
        dbContext.CarrierAccounts.Add(new CarrierAccount
        {
            Id = accountId,
            TenantId = tenantId,
            CarrierCode = CarrierCodes.Bosta,
            State = CarrierAccountState.Active,
            OperationalState = CarrierOperationalState.Ready
        });
        dbContext.CarrierWarehouseAssignments.Add(new CarrierWarehouseAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CarrierAccountId = accountId,
            WarehouseId = warehouseId,
            IsEnabled = true,
            OriginReadinessPublished = true
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
            correlationId: "warehouse-missing-carrier-config",
            warehouseId: warehouseId,
            cancellationToken: CancellationToken.None);

        Assert.Empty(result);
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
                CreateShippingMethod(tenantId, "SAR", 20m, CarrierCodes.FedEx, warehouseId)
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
        var warehouseId = Guid.NewGuid();
        var repository = new Mock<IShippingMethodReadRepository>();
        repository
            .Setup(service => service.GetActiveShippingMethodsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShippingMethodDocument>
            {
                CreateShippingMethod(tenantId, "SAR", 20m, fulfillmentWarehouseId: warehouseId)
            });

        await using var dbContext = CreateDbContext();
        SeedWarehouse(dbContext, tenantId, warehouseId);
        dbContext.ShippingMethods.Add(CreateConfiguredShippingMethod(tenantId, true, warehouseId));
        await dbContext.SaveChangesAsync();
        var service = CreateService(repository.Object, dbContext);

        var result = await service.GetShippingMethodsAvailabilityAsync(
            tenantId,
            cartSubtotal: 100m,
            totalWeight: 0m,
            correlationId: "shipping-available",
            cancellationToken: CancellationToken.None);

        Assert.Equal("AVAILABLE", result.Status);
        Assert.Equal(tenantId, result.TenantId);
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
        Assert.Equal(tenantId, result.TenantId);
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
        Assert.Equal(tenantId, result.TenantId);
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
                CreateShippingMethod(tenantId, "SAR", 20m, CarrierCodes.FedEx, warehouseId)
            });

        await using var dbContext = CreateDbContext();
        dbContext.ShippingMethods.Add(CreateConfiguredShippingMethod(tenantId, true, warehouseId));
        SeedWarehouse(dbContext, tenantId, warehouseId);
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
        Assert.Equal(tenantId, result.TenantId);
        Assert.Empty(result.ShippingMethods);
        Assert.Equal(1, result.ConfiguredCount);
        Assert.Equal(1, result.ActiveCount);
        Assert.Equal(0, result.ApplicableCount);
    }

    private static ShippingMethodDocument CreateShippingMethod(
        Guid tenantId,
        string currency,
        decimal baseRate,
        string? carrierCode = null,
        Guid? fulfillmentWarehouseId = null)
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
            ServiceLevel = "standard",
            FulfillmentWarehouseId = fulfillmentWarehouseId,
            CarrierName = carrierCode switch
            {
                CarrierCodes.Bosta => "Bosta",
                CarrierCodes.FedEx => "FedEx",
                _ => null
            }
        };
    }

    private static ShippingMethod CreateConfiguredShippingMethod(Guid tenantId, bool isActive, Guid? fulfillmentWarehouseId = null)
    {
        return new ShippingMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = isActive ? "Standard" : "Disabled Standard",
            BaseRate = 20m,
            PerKgRate = 0m,
            IsActive = isActive,
            FulfillmentWarehouseId = fulfillmentWarehouseId,
            SortOrder = 1,
            EstimatedDeliveryDaysMin = 1,
            EstimatedDeliveryDaysMax = 3,
        };
    }

    private static void SeedWarehouse(ApplicationDbContext dbContext, Guid tenantId, Guid warehouseId)
    {
        dbContext.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Origin",
            IsActive = true
        });
        dbContext.SaveChanges();
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

    private sealed class FakeCarrierIntegrationAuthority : ICarrierIntegrationAuthority
    {
        private readonly IReadOnlyDictionary<string, bool> _readiness;

        public FakeCarrierIntegrationAuthority(IReadOnlyDictionary<string, bool> readiness)
        {
            _readiness = readiness;
        }

        public Task<IReadOnlyList<CarrierIntegrationConfigurationSnapshot>> ListRuntimeConfigurationsAsync(
            Guid tenantId,
            CarrierIntegrationPurpose purpose,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CarrierIntegrationConfigurationSnapshot>>(Array.Empty<CarrierIntegrationConfigurationSnapshot>());

        public Task<CarrierCredentialsResolution> ResolveCredentialsAsync(
            Guid tenantId,
            string carrierCode,
            CarrierIntegrationPurpose purpose,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CarrierCredentialsResolution(false, carrierCode, null, null, Array.Empty<string>()));

        public Task<CarrierIntegrationReadiness> GetIntegrationReadinessAsync(
            Guid tenantId,
            string carrierCode,
            Guid? shippingMethodId = null,
            Guid? warehouseId = null,
            CarrierIntegrationPurpose purpose = CarrierIntegrationPurpose.StorefrontAdmission,
            CancellationToken cancellationToken = default)
        {
            var ready = _readiness.TryGetValue(CarrierCodes.Normalize(carrierCode), out var configuredReady) && configuredReady;
            return Task.FromResult(new CarrierIntegrationReadiness(
                CarrierCodes.Normalize(carrierCode),
                carrierCode,
                IsManual: CarrierCodes.IsManual(carrierCode),
                Enabled: ready,
                CanExecute: ready,
                Environment: null,
                CarrierProductionCapability.Partial,
                ready ? Array.Empty<string>() : new[] { "TEST_PROVIDER_NOT_READY" },
                OriginReadinessRequired: false,
                OriginReadinessSatisfied: true,
                CredentialAvailability: ready ? "Available" : "Missing"));
        }

        public Task<CarrierServiceLevelReadiness> GetServiceLevelReadinessAsync(
            Guid tenantId,
            string carrierCode,
            string? serviceLevel,
            CancellationToken cancellationToken = default)
        {
            var exists = !string.IsNullOrWhiteSpace(serviceLevel);
            return Task.FromResult(new CarrierServiceLevelReadiness(
                CarrierCodes.Normalize(carrierCode),
                serviceLevel?.Trim().ToLowerInvariant() ?? string.Empty,
                exists,
                exists,
                exists,
                exists,
                exists ? Array.Empty<string>() : new[] { "SERVICE_LEVEL_MISSING" }));
        }

        public Task<CarrierIntegrationStoreResult> ValidateAndStoreIntegrationAsync(
            Guid tenantId,
            string carrierCode,
            CarrierIntegrationUpdate update,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteIntegrationAsync(
            Guid tenantId,
            string carrierCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<CarrierValidationResult> ValidateResolvedCredentialsAsync(
            Guid tenantId,
            string carrierCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CarrierValidationResult { Success = false });

        public Task<CarrierWebhookTrustResolution> ResolveWebhookTrustAsync(
            Guid tenantId,
            string carrierCode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
