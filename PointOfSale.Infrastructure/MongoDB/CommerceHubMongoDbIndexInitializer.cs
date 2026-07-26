using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Operations;

namespace PointOfSale.Infrastructure.MongoDB;

/// <summary>
/// Creates MongoDB indexes for CommerceHub read models.
/// Kept separate from the main initializer to minimize blast radius in existing startup logic.
/// </summary>
public class CommerceHubMongoDbIndexInitializer : IHostedService
{
    private readonly MongoDbContext _mongoDbContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CommerceHubMongoDbIndexInitializer> _logger;

    public CommerceHubMongoDbIndexInitializer(
        MongoDbContext mongoDbContext,
        IServiceScopeFactory scopeFactory,
        ILogger<CommerceHubMongoDbIndexInitializer> logger)
    {
        _mongoDbContext = mongoDbContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => RunInitializationAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunInitializationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var commerceHubProducts = _mongoDbContext.CommerceHubProducts;
            var productIndexes = new[]
            {
                new CreateIndexModel<CommerceHubProductDocument>(
                    Builders<CommerceHubProductDocument>.IndexKeys
                        .Ascending(x => x.TenantId)
                        .Ascending(x => x.VariantId),
                    new CreateIndexOptions
                    {
                        Unique = true,
                        Name = "idx_commercehub_tenant_variant_unique",
                        Background = true
                    }),
                new CreateIndexModel<CommerceHubProductDocument>(
                    Builders<CommerceHubProductDocument>.IndexKeys
                        .Ascending(x => x.IsPublished)
                        .Ascending(x => x.CategoryId)
                        .Ascending(x => x.InStock),
                    new CreateIndexOptions
                    {
                        Name = "idx_commercehub_published_category_stock",
                        Background = true
                    }),
                new CreateIndexModel<CommerceHubProductDocument>(
                    Builders<CommerceHubProductDocument>.IndexKeys
                        .Ascending(x => x.ProductId)
                        .Ascending(x => x.VariantId),
                    new CreateIndexOptions
                    {
                        Name = "idx_commercehub_product_variant",
                        Background = true
                    })
            };

            await commerceHubProducts.Indexes.CreateManyAsync(productIndexes, cancellationToken);

            var reviewCollection = _mongoDbContext.MarketplaceProductReviews;
            var reviewIndexes = new[]
            {
                new CreateIndexModel<MarketplaceProductReviewDocument>(
                    Builders<MarketplaceProductReviewDocument>.IndexKeys
                        .Ascending(x => x.ProductId)
                        .Descending(x => x.CreatedAtUtc),
                    new CreateIndexOptions
                    {
                        Name = "idx_marketplace_product_reviews_product_created",
                        Background = true
                    }),
                new CreateIndexModel<MarketplaceProductReviewDocument>(
                    Builders<MarketplaceProductReviewDocument>.IndexKeys
                        .Ascending(x => x.ProductId)
                        .Ascending(x => x.IsVerifiedPurchase)
                        .Descending(x => x.CreatedAtUtc),
                    new CreateIndexOptions
                    {
                        Name = "idx_marketplace_product_reviews_product_verified_created",
                        Background = true
                    })
            };

            await reviewCollection.Indexes.CreateManyAsync(reviewIndexes, cancellationToken);

            // ProductReviewSummaryDocument uses ProductId as [BsonId] (_id),
            // which is inherently unique and indexed — no additional index needed.

            var indexNames = new[]
            {
                "idx_commercehub_tenant_variant_unique",
                "idx_commercehub_published_category_stock",
                "idx_commercehub_product_variant",
                "idx_marketplace_product_reviews_product_created",
                "idx_marketplace_product_reviews_product_verified_created"
            };

            await RecordSchemaVersionAsync(
                version: "v1",
                details: new
                {
                    collections = new[]
                    {
                        commerceHubProducts.CollectionNamespace.CollectionName,
                        reviewCollection.CollectionNamespace.CollectionName
                    },
                    indexes = indexNames
                },
                cancellationToken);

            _logger.LogInformation(
                "MongoDB indexes created successfully | Collections: {Collections} | Indexes: {Indexes}",
                string.Join(", ", new[]
                {
                    commerceHubProducts.CollectionNamespace.CollectionName,
                    reviewCollection.CollectionNamespace.CollectionName
                }),
                string.Join(", ", indexNames));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create CommerceHub MongoDB indexes");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RecordSchemaVersionAsync(
        string version,
        object details,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var schemaVersionService = scope.ServiceProvider.GetService<ISystemSchemaVersionService>();
            if (schemaVersionService == null)
            {
                return;
            }

            await schemaVersionService.RecordAsync(
                component: "mongo:commercehub_products",
                version: version,
                details: details,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record CommerceHub MongoDB schema version");
        }
    }
}
