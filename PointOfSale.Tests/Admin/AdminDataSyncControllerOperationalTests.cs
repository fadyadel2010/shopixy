using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Admin;

public sealed class AdminDataSyncControllerOperationalTests
{
    [Fact]
    public async Task SyncCategories_DeletesTenantScopedMongoDocumentsMissingFromCurrentSqlAuthority()
    {
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Electronics",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var categoryService = new Mock<IStorefrontCategoryService>();
        categoryService
            .Setup(service => service.InvalidateCategoryCacheAsync(tenantId, "sync-categories"))
            .ReturnsAsync(2);

        var categoryCollection = new Mock<IMongoCollection<CategoryDocument>>();
        FilterDefinition<CategoryDocument>? capturedDeleteFilter = null;
        categoryCollection
            .Setup(collection => collection.BulkWriteAsync(
                It.IsAny<IEnumerable<WriteModel<CategoryDocument>>>(),
                It.IsAny<BulkWriteOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBulkWriteResult<CategoryDocument>(processedCount: 1));
        categoryCollection
            .Setup(collection => collection.DeleteManyAsync(
                It.IsAny<FilterDefinition<CategoryDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback((FilterDefinition<CategoryDocument> filter, CancellationToken _) =>
                capturedDeleteFilter = filter)
            .ReturnsAsync(CreateDeleteResult(1));

        var database = new Mock<IMongoDatabase>();
        database
            .Setup(db => db.GetCollection<CategoryDocument>("categories", It.IsAny<MongoCollectionSettings>()))
            .Returns(categoryCollection.Object);

        var logger = new CapturingLogger<AdminDataSyncController>();
        var controller = CreateController(dbContext, database.Object, categoryService.Object, tenantId, logger);

        var result = await controller.SyncCategories(CancellationToken.None);

        Assert.True(result is OkObjectResult, logger.LastException?.ToString() ?? $"Expected OkObjectResult but got {result.GetType().Name}.");
        var ok = (OkObjectResult)result;
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));

        Assert.Equal(1, payload.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(1, payload.RootElement.GetProperty("staleDeleted").GetInt32());
        Assert.Equal(2, payload.RootElement.GetProperty("cacheInvalidated").GetInt32());
        categoryService.Verify(service => service.InvalidateCategoryCacheAsync(tenantId, "sync-categories"), Times.Once);

        var renderedFilter = RenderFilter(capturedDeleteFilter!);
        Assert.Contains(tenantId.ToString(), renderedFilter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(categoryId.ToString(), renderedFilter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$nin", renderedFilter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncShippingMethods_DeletesTenantScopedMongoDocumentsMissingFromCurrentSqlAuthority()
    {
        var tenantId = Guid.NewGuid();
        var shippingMethodId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.ShippingMethods.Add(new ShippingMethod
        {
            Id = shippingMethodId,
            TenantId = tenantId,
            Name = "Express",
            BaseRate = 25m,
            PerKgRate = 5m,
            EstimatedDeliveryDaysMin = 1,
            EstimatedDeliveryDaysMax = 2,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var categoryService = Mock.Of<IStorefrontCategoryService>();
        var shippingCollection = new Mock<IMongoCollection<ShippingMethodDocument>>();
        FilterDefinition<ShippingMethodDocument>? capturedDeleteFilter = null;
        shippingCollection
            .Setup(collection => collection.BulkWriteAsync(
                It.IsAny<IEnumerable<WriteModel<ShippingMethodDocument>>>(),
                It.IsAny<BulkWriteOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBulkWriteResult<ShippingMethodDocument>(processedCount: 1));
        shippingCollection
            .Setup(collection => collection.DeleteManyAsync(
                It.IsAny<FilterDefinition<ShippingMethodDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback((FilterDefinition<ShippingMethodDocument> filter, CancellationToken _) =>
                capturedDeleteFilter = filter)
            .ReturnsAsync(CreateDeleteResult(1));

        var database = new Mock<IMongoDatabase>();
        database
            .Setup(db => db.GetCollection<ShippingMethodDocument>("shipping_methods", It.IsAny<MongoCollectionSettings>()))
            .Returns(shippingCollection.Object);

        var logger = new CapturingLogger<AdminDataSyncController>();
        var controller = CreateController(dbContext, database.Object, categoryService, tenantId, logger);

        var result = await controller.SyncShippingMethods(CancellationToken.None);

        Assert.True(result is OkObjectResult, logger.LastException?.ToString() ?? $"Expected OkObjectResult but got {result.GetType().Name}.");
        var ok = (OkObjectResult)result;
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));

        Assert.Equal(1, payload.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(1, payload.RootElement.GetProperty("staleDeleted").GetInt32());

        var renderedFilter = RenderFilter(capturedDeleteFilter!);
        Assert.Contains(tenantId.ToString(), renderedFilter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(shippingMethodId.ToString(), renderedFilter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$nin", renderedFilter, StringComparison.Ordinal);
    }

    private static AdminDataSyncController CreateController(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        IStorefrontCategoryService categoryService,
        Guid tenantId,
        ILogger<AdminDataSyncController>? logger = null)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        return new AdminDataSyncController(
            dbContext,
            mongoDatabase,
            categoryService,
            tenantContext,
            logger ?? NullLogger<AdminDataSyncController>.Instance);
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

    private static BulkWriteResult<TDocument> CreateBulkWriteResult<TDocument>(int processedCount) =>
        new BulkWriteResult<TDocument>.Acknowledged(
            requestCount: processedCount,
            matchedCount: processedCount,
            deletedCount: 0,
            insertedCount: 0,
            modifiedCount: 0,
            processedRequests: Enumerable.Range(0, processedCount)
                .Select(_ => (WriteModel<TDocument>)new UpdateOneModel<TDocument>(
                    Builders<TDocument>.Filter.Empty,
                    Builders<TDocument>.Update.Set("_certification", 1)))
                .ToArray(),
            upserts: Array.Empty<BulkWriteUpsert>());

    private static DeleteResult CreateDeleteResult(long deletedCount) =>
        new DeleteResult.Acknowledged(deletedCount);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public Exception? LastException { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LastException ??= exception;
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private static string RenderFilter<TDocument>(FilterDefinition<TDocument> filter)
    {
        var serializer = BsonSerializer.LookupSerializer<TDocument>();
        return filter.Render(new RenderArgs<TDocument>(serializer, BsonSerializer.SerializerRegistry)).ToString();
    }
}
