using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services.ReadModels;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests;

public sealed class DiscountReadModelRepairControllerTests
{
    [Fact]
    public async Task SyncCouponsToMongo_UsesDirectRepairService_NotAuthoritativeReplay()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var repairService = new Mock<IDiscountReadModelRepairService>();
        repairService
            .Setup(service => service.RebuildCouponsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        var publisher = new Mock<IEventPublisher>(MockBehavior.Strict);

        var controller = new CouponsController(
            context,
            new TestTenantContext(tenantId),
            publisher.Object,
            Mock.Of<ICouponValidationService>(),
            NullLogger<CouponsController>.Instance,
            serviceScopeFactory: null,
            reliabilityTelemetry: null,
            readModelRepairService: repairService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.SyncCouponsToMongo(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(7, ReadIntProperty(ok.Value!, "total"));
        Assert.Equal(7, ReadIntProperty(ok.Value!, "synced"));
        repairService.Verify(service => service.RebuildCouponsAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
        publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncPromotionsToMongo_UsesDirectRepairService_NotAuthoritativeReplay()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var repairService = new Mock<IDiscountReadModelRepairService>();
        repairService
            .Setup(service => service.RebuildPromotionsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var publisher = new Mock<IEventPublisher>(MockBehavior.Strict);

        var controller = new PromotionsController(
            context,
            new TestTenantContext(tenantId),
            publisher.Object,
            NullLogger<PromotionsController>.Instance,
            serviceScopeFactory: null,
            readModelRepairService: repairService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.SyncPromotionsToMongo(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(5, ReadIntProperty(ok.Value!, "total"));
        Assert.Equal(5, ReadIntProperty(ok.Value!, "synced"));
        repairService.Verify(service => service.RebuildPromotionsAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
        publisher.VerifyNoOtherCalls();
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static int ReadIntProperty(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<int>(property!.GetValue(value));
    }
}
