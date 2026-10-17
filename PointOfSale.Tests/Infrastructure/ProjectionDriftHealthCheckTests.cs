using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.HealthChecks;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ProjectionDriftHealthCheckTests
{
    [Fact]
    public async Task Global_product_count_ignores_missing_request_tenant_but_excludes_soft_deleted_rows()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var tenantContext = new TenantContext();

        await using var db = new ApplicationDbContext(options, tenantContext);
        var liveTenantId = Guid.NewGuid();
        var liveProductId = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = liveTenantId,
            Name = "Live tenant",
            IsActive = true,
            Status = TenantStatus.Active
        });
        db.Products.AddRange(
            new Product
            {
                Id = liveProductId,
                TenantId = liveTenantId,
                Name = "Live product",
                CategoryId = Guid.Empty,
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                IsDeleted = false
            },
            new Product
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                Name = "Deleted product",
                CategoryId = Guid.NewGuid(),
                IsDeleted = true
            });
        await db.SaveChangesAsync();

        var collection = new Mock<IMongoCollection<ProductDocument>>();
        collection
            .SetupSequence(value => value.CountDocumentsAsync(
                It.IsAny<FilterDefinition<ProductDocument>>(),
                It.IsAny<CountOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1)
            .ReturnsAsync(1);
        SetupVisibleIdentities(collection, new ProductDocument { TenantId = liveTenantId, ProductId = liveProductId });

        var mongo = new Mock<IMongoDatabase>();
        mongo
            .Setup(value => value.GetCollection<ProductDocument>(
                "products",
                It.IsAny<MongoCollectionSettings>()))
            .Returns(collection.Object);

        var sut = new ProjectionDriftHealthCheck(
            db,
            mongo.Object,
            NullLogger<ProjectionDriftHealthCheck>.Instance);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, result.Data["PostgreSQLCount"]);
        Assert.Equal(1L, result.Data["MongoDBCount"]);
        Assert.Equal("Even", result.Data["Direction"]);
    }

    [Fact]
    public async Task Foreign_tenant_documents_are_reported_as_degraded_without_corrupting_current_scope_drift()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var tenantContext = new TenantContext();

        await using var db = new ApplicationDbContext(options, tenantContext);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Current tenant",
            IsActive = true,
            Status = TenantStatus.Active
        });
        db.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Current product",
            CategoryId = Guid.Empty,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            IsDeleted = false
        });
        await db.SaveChangesAsync();

        var collection = new Mock<IMongoCollection<ProductDocument>>();
        collection
            .SetupSequence(value => value.CountDocumentsAsync(
                It.IsAny<FilterDefinition<ProductDocument>>(),
                It.IsAny<CountOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(2)
            .ReturnsAsync(1);
        SetupVisibleIdentities(collection, new ProductDocument { TenantId = tenantId, ProductId = productId });

        var mongo = new Mock<IMongoDatabase>();
        mongo
            .Setup(value => value.GetCollection<ProductDocument>(
                "products",
                It.IsAny<MongoCollectionSettings>()))
            .Returns(collection.Object);

        var sut = new ProjectionDriftHealthCheck(
            db,
            mongo.Object,
            NullLogger<ProjectionDriftHealthCheck>.Instance);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(0L, result.Data["Drift"]);
        Assert.Equal(1L, result.Data["ForeignTenantDocumentCount"]);
        Assert.Contains("foreign-tenant", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetupVisibleIdentities(Mock<IMongoCollection<ProductDocument>> collection, params ProductDocument[] documents)
    {
        var cursor = new Mock<IAsyncCursor<ProductDocument>>();
        cursor.SetupGet(c => c.Current).Returns(documents);
        cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
        collection.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<ProductDocument>>(),
            It.IsAny<FindOptions<ProductDocument, ProductDocument>>(), It.IsAny<CancellationToken>())).ReturnsAsync(cursor.Object);
    }
}
