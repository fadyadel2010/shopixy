using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using PointOfSale.Core.Entities;
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
        db.Products.AddRange(
            new Product
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                Name = "Live product",
                CategoryId = Guid.NewGuid(),
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
            .Setup(value => value.CountDocumentsAsync(
                It.IsAny<FilterDefinition<ProductDocument>>(),
                It.IsAny<CountOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

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
}
