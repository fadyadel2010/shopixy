using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class AdminSyncControllerOperationalTests
{
    [Fact]
    public async Task SyncProductVariants_WhenNoRowsExist_StillUsesTenantScopedCacheBoundary()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var projectionWriter = new Mock<IProductSqlAuthorityProjectionWriter>(MockBehavior.Strict);
        var cacheService = new Mock<IStorefrontProductCacheInvalidator>(MockBehavior.Strict);
        cacheService
            .Setup(service => service.InvalidateTenantAsync(
                tenantId,
                true,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(dbContext, projectionWriter.Object, cacheService.Object, tenantId);

        var result = await controller.SyncProductVariants(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        cacheService.Verify(
            service => service.InvalidateTenantAsync(
                tenantId,
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncSingleProductVariants_AfterSqlProjectionWrite_UsesProductCacheBoundary()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Repair Product",
            Slug = "repair-product",
            CategoryId = Guid.NewGuid(),
            SalePrice = 15m,
            PurchasePrice = 10m,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var projectionWriter = new Mock<IProductSqlAuthorityProjectionWriter>(MockBehavior.Strict);
        projectionWriter
            .Setup(writer => writer.ApplyAsync(
                It.Is<ProductSqlAuthorityProjectionWriteRequest>(request =>
                    request.TenantId == tenantId &&
                    request.ProductId == productId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSqlAuthorityProjectionWriteResult(
                ProductSqlAuthorityProjectionWriteDisposition.Upserted));

        var cacheService = new Mock<IStorefrontProductCacheInvalidator>(MockBehavior.Strict);
        cacheService
            .Setup(service => service.InvalidateProductAsync(
                tenantId,
                productId,
                "repair-product",
                true,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(dbContext, projectionWriter.Object, cacheService.Object, tenantId);

        var result = await controller.SyncSingleProductVariants(productId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        projectionWriter.VerifyAll();
        cacheService.VerifyAll();
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static AdminSyncController CreateController(
        ApplicationDbContext dbContext,
        IProductSqlAuthorityProjectionWriter projectionWriter,
        IStorefrontProductCacheInvalidator cacheService,
        Guid tenantId) =>
        new(
            dbContext,
            projectionWriter,
            cacheService,
            new TestTenantContext(tenantId),
            NullLogger<AdminSyncController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
}
