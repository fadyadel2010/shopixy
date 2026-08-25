using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class AdminSearchSyncControllerTests
{
    [Fact]
    public async Task SyncProducts_RebuildsSearchDocumentsFromCurrentSqlAuthority()
    {
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var capturedDocuments = new List<ProductSearchDocument>();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Authority Category",
            CreatedAt = DateTime.UtcNow
        });
        dbContext.Units.Add(new PointOfSale.Core.Entities.Unit
        {
            Id = unitId,
            TenantId = tenantId,
            Name = "Box",
            Symbol = "bx",
            CreatedAt = DateTime.UtcNow
        });
        dbContext.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "SQL First Product",
            CategoryId = categoryId,
            SalePrice = 29m,
            OnlinePrice = 31m,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            HasVariants = false,
            CreatedAt = DateTime.UtcNow
        });
        dbContext.ProductUnits.Add(new ProductUnit
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            UnitId = unitId,
            SellingPrice = 145m,
            ConversionFactor = 1m,
            IsDefault = true,
            IsBaseUnit = true,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var searchIndexService = new Mock<IUnifiedSearchIndexService>(MockBehavior.Strict);
        searchIndexService
            .Setup(service => service.BulkIndexProductsAsync(
                It.IsAny<IEnumerable<ProductSearchDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ProductSearchDocument> documents, CancellationToken _) =>
            {
                var batch = documents.ToList();
                capturedDocuments.AddRange(batch);
                return new BulkIndexResult
                {
                    SuccessCount = batch.Count
                };
            });

        var controller = CreateController(dbContext, searchIndexService.Object, tenantId);

        var result = await controller.SyncProducts(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var indexed = Assert.Single(capturedDocuments);
        Assert.Equal(145d, indexed.Price);
        Assert.Equal("Authority Category", indexed.CategoryName);
        Assert.Equal(productId, indexed.ProductId);

        searchIndexService.Verify(
            service => service.TombstoneProductAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<long>(),
                It.IsAny<bool>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncProducts_WhenSqlCategoryLineageIsInvalid_DeletesStaleSearchVisibility()
    {
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var unitId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Cyclic Category",
            ParentCategoryId = categoryId,
            CreatedAt = DateTime.UtcNow
        });
        dbContext.Units.Add(new PointOfSale.Core.Entities.Unit
        {
            Id = unitId,
            TenantId = tenantId,
            Name = "Piece",
            Symbol = "pc",
            CreatedAt = DateTime.UtcNow
        });
        dbContext.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Stale Search Product",
            CategoryId = categoryId,
            SalePrice = 19m,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            HasVariants = false,
            CreatedAt = DateTime.UtcNow
        });
        dbContext.ProductUnits.Add(new ProductUnit
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            UnitId = unitId,
            SellingPrice = 19m,
            ConversionFactor = 1m,
            IsDefault = true,
            IsBaseUnit = true,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var searchIndexService = new Mock<IUnifiedSearchIndexService>(MockBehavior.Strict);
        searchIndexService
            .Setup(service => service.TombstoneProductAsync(
                tenantId,
                productId,
                It.IsAny<long>(),
                false,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = CreateController(dbContext, searchIndexService.Object, tenantId);

        var result = await controller.SyncProducts(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        searchIndexService.Verify(
            service => service.TombstoneProductAsync(
                tenantId,
                productId,
                It.IsAny<long>(),
                false,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
        searchIndexService.Verify(
            service => service.BulkIndexProductsAsync(
                It.IsAny<IEnumerable<ProductSearchDocument>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncCategories_DeletesTenantScopedSearchDocumentsMissingFromCurrentSqlAuthority()
    {
        var tenantId = Guid.NewGuid();
        var liveCategoryId = Guid.NewGuid();
        var capturedDocuments = new List<CategorySearchDocument>();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Categories.Add(new Category
        {
            Id = liveCategoryId,
            TenantId = tenantId,
            Name = "Live Category",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var searchIndexService = new Mock<IUnifiedSearchIndexService>(MockBehavior.Strict);
        searchIndexService
            .Setup(service => service.BulkIndexCategoriesAsync(
                It.IsAny<IEnumerable<CategorySearchDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<CategorySearchDocument> documents, CancellationToken _) =>
            {
                var batch = documents.ToList();
                capturedDocuments.AddRange(batch);
                return new BulkIndexResult
                {
                    SuccessCount = batch.Count
                };
            });
        searchIndexService
            .Setup(service => service.DeleteStaleCategoriesAsync(
                tenantId,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(liveCategoryId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var controller = CreateController(dbContext, searchIndexService.Object, tenantId);

        var result = await controller.SyncCategories(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Single(capturedDocuments);
        Assert.Equal(liveCategoryId, capturedDocuments[0].CategoryId);

        var payload = ok.Value;
        Assert.NotNull(payload);
        var deletedFromSearch = (int?)payload!.GetType().GetProperty("deletedFromSearch")?.GetValue(payload);
        var staleDeletedFromSearch = (int?)payload.GetType().GetProperty("staleDeletedFromSearch")?.GetValue(payload);
        Assert.Equal(2, deletedFromSearch);
        Assert.Equal(2, staleDeletedFromSearch);

        searchIndexService.Verify(
            service => service.DeleteCategoryAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        searchIndexService.Verify(
            service => service.DeleteStaleCategoriesAsync(
                tenantId,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(liveCategoryId)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Search sync tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        return dbContext;
    }

    private static AdminSearchSyncController CreateController(
        ApplicationDbContext dbContext,
        IUnifiedSearchIndexService searchIndexService,
        Guid tenantId)
    {
        return new AdminSearchSyncController(
            dbContext,
            searchIndexService,
            new TestTenantContext(tenantId),
            NullLogger<AdminSearchSyncController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }
}
