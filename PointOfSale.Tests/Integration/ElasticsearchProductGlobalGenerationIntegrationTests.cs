using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nest;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.ReadModels.Evolution;
using Xunit.Sdk;

namespace PointOfSale.Tests.Integration;

public sealed class ElasticsearchProductGlobalGenerationIntegrationTests
{
    [Fact]
    public async Task ExternalProductGeneration_FencesReorderingDuplicatesAndDeleteResurrection()
    {
        var url = Environment.GetEnvironmentVariable("SHOPIXY_ELASTICSEARCH_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            throw SkipException.ForSkip("Requires SHOPIXY_ELASTICSEARCH_URL pointing to a disposable Elasticsearch test node.");
        }

        var indexName = $"shopixy-product-generation-{Guid.NewGuid():N}";
        var connectionSettings = new ConnectionSettings(new Uri(url))
            .DefaultIndex(indexName)
            .EnableApiVersioningHeader()
            .ThrowExceptions(false);
        var client = new ElasticClient(connectionSettings);

        try
        {
            var settings = Options.Create(new ElasticsearchSettings
            {
                Enabled = true,
                Url = url,
                ProductsIndexName = indexName
            });
            var service = new ProductSearchIndexService(
                client,
                settings,
                new SearchTelemetry(),
                Mock.Of<IReadModelEvolutionConvergenceService>(),
                NullLogger<ProductSearchIndexService>.Instance);
            var tenantId = Guid.NewGuid();
            var otherTenantId = Guid.NewGuid();
            var productId = Guid.NewGuid();

            Assert.True(await service.EnsureIndexExistsAsync());

            // Simulate a deployed document written repeatedly with ES internal versioning.
            // The first canonical generation must supersede it without waiting for its SQL
            // generation to catch the legacy internal _version.
            for (var legacyWrite = 0; legacyWrite < 5; legacyWrite++)
            {
                var legacy = await client.IndexAsync(
                    Visible(tenantId, productId, 0, $"legacy-{legacyWrite}"),
                    descriptor => descriptor.Index(indexName).Id($"{tenantId}_{productId}"));
                Assert.True(legacy.IsValid, legacy.DebugInformation);
            }

            Assert.True(await service.IndexAsync(Visible(tenantId, productId, 0, "canonical-generation-0")));
            Assert.Equal("canonical-generation-0", (await GetAsync(client, indexName, tenantId, productId)).Name);

            Assert.True(await service.IndexAsync(Visible(tenantId, productId, 2, "generation-2")));
            Assert.True(await service.IndexAsync(Visible(tenantId, productId, 1, "stale-generation-1")));
            Assert.Equal("generation-2", (await GetAsync(client, indexName, tenantId, productId)).Name);

            Assert.True(await service.IndexAsync(Visible(tenantId, productId, 2, "duplicate-generation-2")));
            Assert.Equal("generation-2", (await GetAsync(client, indexName, tenantId, productId)).Name);

            Assert.True(await service.TombstoneAsync(tenantId, productId, 3, isDeleted: true, Guid.NewGuid()));
            var tombstone = await GetAsync(client, indexName, tenantId, productId);
            Assert.True(tombstone.IsTombstone);
            Assert.True(tombstone.IsDeleted);
            Assert.Equal(3, tombstone.ProductGeneration);

            Assert.True(await service.IndexAsync(Visible(tenantId, productId, 2, "resurrection-attempt")));
            Assert.True((await GetAsync(client, indexName, tenantId, productId)).IsTombstone);

            Assert.True(await service.TombstoneAsync(tenantId, productId, 3, isDeleted: true, Guid.NewGuid()));
            Assert.True((await GetAsync(client, indexName, tenantId, productId)).IsTombstone);

            Assert.True(await service.IndexAsync(Visible(tenantId, productId, 4, "legitimate-generation-4")));
            Assert.Equal("legitimate-generation-4", (await GetAsync(client, indexName, tenantId, productId)).Name);

            Assert.True(await service.IndexAsync(Visible(otherTenantId, productId, 1, "other-tenant")));
            Assert.Equal("legitimate-generation-4", (await GetAsync(client, indexName, tenantId, productId)).Name);
            Assert.Equal("other-tenant", (await GetAsync(client, indexName, otherTenantId, productId)).Name);
        }
        finally
        {
            await client.Indices.DeleteAsync(indexName);
        }
    }

    private static ProductSearchDocument Visible(Guid tenantId, Guid productId, long generation, string name) => new()
    {
        TenantId = tenantId,
        ProductId = productId,
        ProductGeneration = generation,
        Name = name,
        IsActive = true,
        IsOnlineVisible = true,
        IsSearchable = true,
        IndexedAtUtc = DateTime.UtcNow
    };

    private static async Task<ProductSearchDocument> GetAsync(
        IElasticClient client,
        string indexName,
        Guid tenantId,
        Guid productId)
    {
        var response = await client.GetAsync<ProductSearchDocument>(
            $"{tenantId}_{productId}",
            descriptor => descriptor.Index(indexName));
        Assert.True(response.Found, response.DebugInformation);
        return response.Source;
    }
}
