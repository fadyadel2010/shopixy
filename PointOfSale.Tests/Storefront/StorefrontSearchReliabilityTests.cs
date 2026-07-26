using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Storefront;

public sealed class StorefrontSearchReliabilityTests
{
    [Fact]
    public async Task UnifiedSearch_WhenSearchFails_Returns503_And_IncrementsMetric()
    {
        var tenantId = Guid.NewGuid();
        var searchService = new Mock<IUnifiedSearchIndexService>();
        searchService
            .Setup(service => service.SearchAsync(
                tenantId,
                "chair",
                null,
                1,
                20,
                "en",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnifiedSearchResult.Failure("cluster unavailable"));

        using var telemetry = new CommerceReliabilityTelemetry();
        long searchFailureCount = 0;
        using var listener = CreateCounterListener(
            "search_failure_total",
            measurement => Interlocked.Add(ref searchFailureCount, measurement));

        var controller = new StorefrontSearchController(
            searchService.Object,
            CreateDbContext(),
            telemetry,
            NullLogger<StorefrontSearchController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateTenantHttpContext(tenantId)
            }
        };

        var result = await controller.Search("chair", cancellationToken: CancellationToken.None);

        var unavailable = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(unavailable.Value);
        Assert.Equal("SEARCH_UNAVAILABLE", error.Code);
        Assert.Equal("Search is temporarily unavailable", error.Message);
        Assert.Equal(1, searchFailureCount);
    }

    [Fact]
    public async Task ProductSearch_WhenSearchInfrastructureThrows_Returns503_And_IncrementsMetric()
    {
        var tenantId = Guid.NewGuid();
        var searchService = new Mock<IProductSearchService>();
        searchService
            .Setup(service => service.SearchProductsAsync(
                tenantId,
                It.IsAny<ProductSearchQuery>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StorefrontSearchUnavailableException("product_search", "cluster unavailable"));

        using var telemetry = new CommerceReliabilityTelemetry();
        long searchFailureCount = 0;
        using var listener = CreateCounterListener(
            "search_failure_total",
            measurement => Interlocked.Add(ref searchFailureCount, measurement));

        var controller = new StorefrontProductsController(
            Mock.Of<IStorefrontProductService>(),
            searchService.Object,
            Mock.Of<IProductDiscoveryService>(),
            Mock.Of<ICategoryReadRepository>(),
            CreateDbContext(),
            telemetry,
            NullLogger<StorefrontProductsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateTenantHttpContext(tenantId)
            }
        };

        var result = await controller.SearchProducts("chair", cancellationToken: CancellationToken.None);

        var unavailable = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(unavailable.Value);
        Assert.Equal("SEARCH_UNAVAILABLE", error.Code);
        Assert.Equal("Search is temporarily unavailable", error.Message);
        Assert.Equal(1, searchFailureCount);
    }

    private static MeterListener CreateCounterListener(string instrumentName, Action<long> onMeasurement)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == CommerceReliabilityTelemetry.MeterName &&
                    instrument.Name == instrumentName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            onMeasurement(measurement);
        });
        listener.Start();
        return listener;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static DefaultHttpContext CreateTenantHttpContext(Guid tenantId)
    {
        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(new TestTenantContext(tenantId))
            .BuildServiceProvider();

        return new DefaultHttpContext
        {
            RequestServices = services
        };
    }
}
