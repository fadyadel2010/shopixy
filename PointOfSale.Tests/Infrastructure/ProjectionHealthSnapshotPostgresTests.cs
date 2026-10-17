using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.HealthChecks;
using PointOfSale.Infrastructure.MongoDB.Configuration;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ProjectionHealthSnapshotPostgresTests : IDisposable
{
    private readonly TestDatabaseScope scope = TestDatabaseScope.Open();

    [Theory]
    [InlineData("visible", true)]
    [InlineData("inactive-product", false)]
    [InlineData("offline", false)]
    [InlineData("draft", false)]
    [InlineData("deleted-product", false)]
    [InlineData("inactive-tenant", false)]
    [InlineData("suspended-tenant", false)]
    [InlineData("deleted-tenant", false)]
    [InlineData("foreign-category", false)]
    [InlineData("deleted-category", false)]
    [InlineData("foreign-parent", false)]
    [InlineData("deleted-parent", false)]
    [InlineData("category-cycle", false)]
    [InlineData("no-variant", false)]
    [InlineData("unpriced-variant", false)]
    [InlineData("inactive-variant", false)]
    [InlineData("deleted-variant", false)]
    [InlineData("foreign-variant", false)]
    [InlineData("superseded-variant", false)]
    [InlineData("priced-variant", true)]
    [InlineData("zero-online-fallback", true)]
    public async Task LightweightInventory_MatchesCanonicalSqlVisibility_WithoutHydratingCatalog(string defect, bool visible)
    {
        await using var db = scope.CreateDbContext();
        var a = Tenant(); var b = Tenant();
        var category = new Category { Id = Guid.NewGuid(), TenantId = a.Id, Name = "Synthetic category" };
        var parent = new Category { Id = Guid.NewGuid(), TenantId = a.Id, Name = "Synthetic parent" };
        var product = Product(a.Id, category.Id);
        db.Tenants.AddRange(a, b); db.Categories.AddRange(category, parent); db.Products.Add(product);
        await db.SaveChangesAsync();
        if (defect == "inactive-product") product.IsActive = false;
        if (defect == "offline") product.IsOnlineVisible = false;
        if (defect == "draft") product.PublishStatus = PublishStatus.Draft;
        if (defect == "deleted-product") product.IsDeleted = true;
        if (defect == "inactive-tenant") a.IsActive = false;
        if (defect == "suspended-tenant") a.Status = TenantStatus.Suspended;
        if (defect == "deleted-tenant") a.DeletedAtUtc = DateTime.UtcNow;
        if (defect == "foreign-category") category.TenantId = b.Id;
        if (defect == "deleted-category") category.IsDeleted = true;
        if (defect is "foreign-parent" or "deleted-parent" or "category-cycle") category.ParentCategoryId = parent.Id;
        if (defect == "foreign-parent") parent.TenantId = b.Id;
        if (defect == "deleted-parent") parent.IsDeleted = true;
        if (defect == "category-cycle") parent.ParentCategoryId = category.Id;
        if (defect.Contains("variant") || defect == "zero-online-fallback")
        {
            product.HasVariants = true;
            if (defect != "no-variant") db.ProductVariants.Add(new ProductVariant
            {
                Id = Guid.NewGuid(), ProductId = product.Id, TenantId = defect == "foreign-variant" ? b.Id : a.Id,
                SKU = "synthetic-" + Guid.NewGuid().ToString("N"), Price = defect == "unpriced-variant" ? 0 : 10,
                OnlinePrice = defect == "zero-online-fallback" ? 0 : null,
                SemanticSupersededAtUtc = defect == "superseded-variant" ? DateTime.UtcNow : null,
                IsActive = defect != "inactive-variant", IsDeleted = defect == "deleted-variant"
            });
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        var interceptor = new ReaderCounter();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(db.Database.GetConnectionString()).AddInterceptors(interceptor).Options;
        await using var observed = new ApplicationDbContext(options);
        var result = await ProductProjectionHealthSnapshotReader.ReadAsync(observed);
        Assert.Equal(3, interceptor.Commands);
        Assert.Equal(visible, result.VisibleProducts.Contains(new ProductProjectionIdentity(a.Id, product.Id)));
        Assert.Contains(a.Id, result.SqlTenantIds); Assert.Contains(b.Id, result.SqlTenantIds);
        Assert.Equal(defect == "deleted-product" ? 0 : 1, result.SourceProductCount);
        Assert.Empty(observed.ChangeTracker.Entries());

        var canonical = await ProductSearchSqlAuthorityOverlay.ApplyAsync(db,
            new ProductDocument { TenantId = a.Id, ProductId = product.Id }, a.Id, CancellationToken.None);
        Assert.Equal(visible, canonical is { IsActive: true, IsOnlineVisible: true });
        Assert.Null(await ProductSearchSqlAuthorityOverlay.ApplyAsync(db,
            new ProductDocument { TenantId = a.Id, ProductId = product.Id }, b.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EqualCounts_CannotHideWrongProductOrCrossTenantIdentity(bool crossTenant)
    {
        await using var db = scope.CreateDbContext();
        var a = Tenant(); var b = Tenant();
        var category = new Category { Id = Guid.NewGuid(), TenantId = a.Id, Name = "Synthetic" };
        var products = Enumerable.Range(0, 50).Select(_ => Product(a.Id, category.Id)).ToArray();
        db.Tenants.AddRange(a, b); db.Categories.Add(category); db.Products.AddRange(products); await db.SaveChangesAsync();
        var docs = products.Select(p => new ProductDocument { TenantId = crossTenant ? b.Id : a.Id, ProductId = crossTenant ? p.Id : Guid.NewGuid() }).ToArray();
        var check = Check(db, total: 50, current: 50, docs);
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(50, result.Data["PostgreSQLCount"]); Assert.Equal(50L, result.Data["MongoDBCount"]);
        Assert.Equal(50, result.Data["MissingProductCount"]); Assert.Equal(50, result.Data["UnexpectedProductCount"]);
        Assert.Equal(100L, result.Data["Drift"]); Assert.Equal(0L, result.Data["ForeignTenantDocumentCount"]);
    }

    [Fact]
    public async Task SharedCollection_PreservesForeignAlert_AndRecognizesKnownTenantWithoutProducts()
    {
        await using var db = scope.CreateDbContext();
        var a = Tenant(); var b = Tenant();
        var category = new Category { Id = Guid.NewGuid(), TenantId = a.Id, Name = "Synthetic" };
        var product = Product(a.Id, category.Id);
        db.Tenants.AddRange(a, b); db.Categories.Add(category); db.Products.Add(product); await db.SaveChangesAsync();
        var result = await Check(db, total: 3, current: 2,
            new ProductDocument { TenantId = a.Id, ProductId = product.Id },
            new ProductDocument { TenantId = b.Id, ProductId = Guid.NewGuid() }).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(1L, result.Data["ForeignTenantDocumentCount"]);
        Assert.Equal(1, result.Data["UnexpectedProductCount"]);
        Assert.Equal(2, result.Data["SqlTenantCount"]);
        Assert.Contains("preserved", result.Description);
    }

    [Fact]
    public async Task DuplicatePairIdentity_IsUnhealthyEvenWhenCountsAreWithinTolerance()
    {
        await using var db = scope.CreateDbContext();
        var tenant = Tenant(); var category = new Category { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Synthetic" };
        var product = Product(tenant.Id, category.Id);
        db.Tenants.Add(tenant); db.Categories.Add(category); db.Products.Add(product); await db.SaveChangesAsync();
        var doc = new ProductDocument { TenantId = tenant.Id, ProductId = product.Id };
        var result = await Check(db, 2, 2, doc, doc).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(1, result.Data["DuplicateIdentityCount"]);
    }

    [Fact]
    public async Task CallerCancellation_AndMongoFailure_CannotBecomeHealthy()
    {
        await using var db = scope.CreateDbContext();
        var collection = new Mock<IMongoCollection<ProductDocument>>(MockBehavior.Strict);
        collection.Setup(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<ProductDocument>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Synthetic Mongo timeout"));
        var mongo = new Mock<IMongoDatabase>();
        mongo.Setup(m => m.GetCollection<ProductDocument>("products", It.IsAny<MongoCollectionSettings>())).Returns(collection.Object);
        var check = new ProjectionDriftHealthCheck(db, mongo.Object, NullLogger<ProjectionDriftHealthCheck>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.CheckHealthAsync(new HealthCheckContext(), new CancellationToken(true)));
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status); Assert.Equal("dependency-timeout", result.Data["FailureCategory"]);
        collection.Verify(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<ProductDocument>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublicSqlAndProjectionWriter_RetainSameTenantAuthority_RejectForeignMutationSeed()
    {
        var a = Tenant(); var b = Tenant();
        await using (var seed = scope.CreateDbContext())
        {
            seed.Tenants.AddRange(a, b);
            var ca = new Category { Id = Guid.NewGuid(), TenantId = a.Id, Name = "Synthetic A" };
            var cb = new Category { Id = Guid.NewGuid(), TenantId = b.Id, Name = "Synthetic B" };
            seed.Categories.AddRange(ca, cb); seed.Products.AddRange(Product(a.Id, ca.Id), Product(b.Id, cb.Id)); await seed.SaveChangesAsync();
        }
        await using var db = scope.CreateDbContext(a.Id);
        var own = Assert.Single(await db.Products.AsNoTracking().ToListAsync()); Assert.Equal(a.Id, own.TenantId);
        var global = await ProductProjectionHealthSnapshotReader.ReadAsync(db); Assert.Equal(2, global.VisibleProducts.Count);
        var mutation = new Mock<IStorefrontProductProjectionMutationBoundary>(MockBehavior.Strict);
        var writer = new ProductSqlAuthorityProjectionWriter(db, mutation.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ApplyAsync(new ProductSqlAuthorityProjectionWriteRequest(
            b.Id, own.Id, new ProductDocument { TenantId = a.Id, ProductId = own.Id }, Guid.NewGuid(), DateTime.UtcNow, 1)));
        mutation.VerifyNoOtherCalls();
        Assert.Equal(a.Id, Assert.Single(await db.Products.AsNoTracking().ToListAsync()).TenantId);
    }

    [Fact]
    public async Task SingleAndBatchOverlay_ExcludeForeignChildMediaAndVariants()
    {
        await using var db = scope.CreateDbContext();
        var a = Tenant(); var b = Tenant();
        var category = new Category { Id = Guid.NewGuid(), TenantId = a.Id, Name = "Synthetic" };
        var product = Product(a.Id, category.Id); product.HasVariants = true;
        var ownVariant = new ProductVariant { Id = Guid.NewGuid(), TenantId = a.Id, ProductId = product.Id,
            SKU = "own-" + Guid.NewGuid().ToString("N"), Price = 10, IsActive = true };
        var foreignVariant = new ProductVariant { Id = Guid.NewGuid(), TenantId = b.Id, ProductId = product.Id,
            SKU = "foreign-" + Guid.NewGuid().ToString("N"), Price = 99, IsActive = true };
        var ownMedia = new ProductMedia { Id = Guid.NewGuid(), TenantId = a.Id, ProductId = product.Id,
            MediaType = "image", Url = "https://example.invalid/own", Provider = "synthetic" };
        var foreignMedia = new ProductMedia { Id = Guid.NewGuid(), TenantId = b.Id, ProductId = product.Id,
            MediaType = "image", Url = "https://example.invalid/foreign", Provider = "synthetic" };
        db.Tenants.AddRange(a, b); db.Categories.Add(category); db.Products.Add(product);
        db.ProductVariants.AddRange(ownVariant, foreignVariant); db.ProductMedia.AddRange(ownMedia, foreignMedia);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var single = await ProductSearchSqlAuthorityOverlay.ApplyAsync(db,
            new ProductDocument { TenantId = a.Id, ProductId = product.Id }, a.Id, CancellationToken.None);
        var batch = Assert.Single(await ProductSearchSqlAuthorityOverlay.ApplyBatchAsync(db,
            [new ProductDocument { TenantId = a.Id, ProductId = product.Id }], a.Id, CancellationToken.None));
        foreach (var result in new[] { single, batch })
        {
            Assert.NotNull(result);
            Assert.Equal(ownVariant.Id, Assert.Single(result.Variants).VariantId);
            Assert.Equal(ownMedia.Id, Assert.Single(result.Media).Id);
        }
    }

    private static Tenant Tenant() => new() { Id = Guid.NewGuid(), Name = "Synthetic health tenant", Slug = "health-" + Guid.NewGuid().ToString("N"), IsActive = true, Status = TenantStatus.Active };
    private static Product Product(Guid tenant, Guid category) => new() { Id = Guid.NewGuid(), TenantId = tenant, CategoryId = category,
        Name = "Synthetic health product", IsActive = true, IsOnlineVisible = true, PublishStatus = PublishStatus.Published };

    private static ProjectionDriftHealthCheck Check(ApplicationDbContext db, long total, long current, params ProductDocument[] docs)
    {
        var collection = new Mock<IMongoCollection<ProductDocument>>(MockBehavior.Strict);
        collection.SetupSequence(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<ProductDocument>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(total).ReturnsAsync(current);
        var cursor = new Mock<IAsyncCursor<ProductDocument>>(); cursor.SetupGet(c => c.Current).Returns(docs);
        cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
        collection.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<ProductDocument>>(), It.IsAny<FindOptions<ProductDocument, ProductDocument>>(), It.IsAny<CancellationToken>())).ReturnsAsync(cursor.Object);
        var mongo = new Mock<IMongoDatabase>(MockBehavior.Strict);
        mongo.Setup(m => m.GetCollection<ProductDocument>("isolated-products", It.IsAny<MongoCollectionSettings>())).Returns(collection.Object);
        return new ProjectionDriftHealthCheck(db, mongo.Object, NullLogger<ProjectionDriftHealthCheck>.Instance,
            Options.Create(new MongoDbSettings { ProductsCollectionName = "isolated-products" }));
    }

    private sealed class ReaderCounter : DbCommandInterceptor
    {
        public int Commands { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands++; return new ValueTask<InterceptionResult<DbDataReader>>(result); }
    }
    public void Dispose() => scope.Dispose();
}
