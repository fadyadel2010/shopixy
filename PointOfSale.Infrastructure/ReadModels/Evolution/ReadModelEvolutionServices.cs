using System.Collections.Concurrent;
using System.Text.Json;
using Elasticsearch.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Nest;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Infrastructure.ReadModels.Evolution;

public sealed class ReadModelEvolutionConvergenceService : IReadModelEvolutionConvergenceService
{
    private static readonly TimeSpan StatusCacheDuration = TimeSpan.FromSeconds(30);

    private readonly IReadModelEvolutionSnapshotProvider _snapshotProvider;
    private readonly ILogger<ReadModelEvolutionConvergenceService> _logger;
    private readonly ConcurrentDictionary<string, CachedStatus> _statusCache = new(StringComparer.Ordinal);

    public ReadModelEvolutionConvergenceService(
        IReadModelEvolutionSnapshotProvider snapshotProvider,
        ILogger<ReadModelEvolutionConvergenceService> logger)
    {
        _snapshotProvider = snapshotProvider;
        _logger = logger;
    }

    public async Task<ReadModelCompatibilityDecision> GetCompatibilityDecisionAsync(
        string readModelName,
        ReadModelStore store,
        CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(readModelName, store, cancellationToken);
        return status.Decision;
    }

    public async Task<ReadModelEvolutionStatus> GetStatusAsync(
        string readModelName,
        ReadModelStore store,
        CancellationToken cancellationToken = default)
    {
        var definition = ReadModelEvolutionRegistry.Get(readModelName, store);
        var cacheKey = BuildCacheKey(definition);
        var nowUtc = DateTime.UtcNow;

        if (_statusCache.TryGetValue(cacheKey, out var cached) &&
            nowUtc - cached.CachedAtUtc <= StatusCacheDuration)
        {
            return cached.Status;
        }

        var status = await BuildStatusAsync(definition, cancellationToken);
        _statusCache[cacheKey] = new CachedStatus(status, nowUtc);
        return status;
    }

    public async Task<IReadOnlyCollection<ReadModelEvolutionStatus>> GetAllStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        var statuses = new List<ReadModelEvolutionStatus>();
        foreach (var definition in ReadModelEvolutionRegistry.All)
        {
            statuses.Add(await GetStatusAsync(definition.Name, definition.Store, cancellationToken));
        }

        return statuses;
    }

    private async Task<ReadModelEvolutionStatus> BuildStatusAsync(
        ReadModelDefinition definition,
        CancellationToken cancellationToken)
    {
        ReadModelConvergenceSnapshot? snapshot = null;
        try
        {
            snapshot = await _snapshotProvider.GetSnapshotAsync(definition, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Read model convergence snapshot failed; compatibility mode remains active | ReadModel: {ReadModelName} | Store: {Store}",
                definition.Name,
                definition.Store);
        }

        var decision = ReadModelEvolutionPolicy.Evaluate(definition, snapshot);
        var awaitingSchema = snapshot == null
            ? 0
            : Math.Max(0, snapshot.TotalDocuments - snapshot.DocumentsAtCurrentSchema);
        var awaitingProjection = snapshot == null
            ? 0
            : Math.Max(0, snapshot.TotalDocuments - snapshot.DocumentsAtCurrentProjection);

        if (decision.Converged && snapshot != null)
        {
            try
            {
                await _snapshotProvider.MarkConvergedAsync(definition, snapshot.ObservedAtUtc, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Read model convergence timestamp update failed | ReadModel: {ReadModelName} | Store: {Store}",
                    definition.Name,
                    definition.Store);
            }
        }

        return new ReadModelEvolutionStatus(
            definition,
            snapshot,
            decision,
            awaitingSchema,
            awaitingProjection);
    }

    private static string BuildCacheKey(ReadModelDefinition definition) =>
        $"{definition.Name}:{definition.Store}";

    private sealed record CachedStatus(ReadModelEvolutionStatus Status, DateTime CachedAtUtc);
}

public sealed class ReadModelEvolutionSnapshotProvider : IReadModelEvolutionSnapshotProvider
{
    private const string CategorySearchIndexName = "categories";

    private readonly MongoDbContext _mongoDbContext;
    private readonly IElasticClient _elasticClient;
    private readonly ElasticsearchSettings _elasticsearchSettings;
    private readonly ILogger<ReadModelEvolutionSnapshotProvider> _logger;

    public ReadModelEvolutionSnapshotProvider(
        MongoDbContext mongoDbContext,
        IElasticClient elasticClient,
        IOptions<ElasticsearchSettings> elasticsearchSettings,
        ILogger<ReadModelEvolutionSnapshotProvider> logger)
    {
        _mongoDbContext = mongoDbContext;
        _elasticClient = elasticClient;
        _elasticsearchSettings = elasticsearchSettings.Value;
        _logger = logger;
    }

    public async Task<ReadModelConvergenceSnapshot?> GetSnapshotAsync(
        ReadModelDefinition definition,
        CancellationToken cancellationToken = default)
    {
        return definition.Store switch
        {
            ReadModelStore.MongoDb => await GetMongoSnapshotAsync(definition, cancellationToken),
            ReadModelStore.Elasticsearch => await GetElasticsearchSnapshotAsync(definition, cancellationToken),
            _ => null
        };
    }

    public async Task MarkConvergedAsync(
        ReadModelDefinition definition,
        DateTime convergedAtUtc,
        CancellationToken cancellationToken = default)
    {
        switch (definition.Store)
        {
            case ReadModelStore.MongoDb:
                await MarkMongoConvergedAsync(definition, convergedAtUtc, cancellationToken);
                break;
            case ReadModelStore.Elasticsearch:
                await MarkElasticsearchConvergedAsync(definition, convergedAtUtc, cancellationToken);
                break;
        }
    }

    private async Task<ReadModelConvergenceSnapshot?> GetMongoSnapshotAsync(
        ReadModelDefinition definition,
        CancellationToken cancellationToken)
    {
        var collectionName = ResolveMongoCollectionName(definition);
        if (collectionName == null)
        {
            return null;
        }

        var collection = _mongoDbContext.Database.GetCollection<BsonDocument>(collectionName);
        var filter = Builders<BsonDocument>.Filter.Empty;
        var total = await collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var documentsAtCurrentSchema = await collection.CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Gte(
                nameof(IReadModelEvolutionDocument.ReadModelSchemaVersion),
                definition.CurrentSchemaVersion),
            cancellationToken: cancellationToken);
        var documentsAtCurrentProjection = await collection.CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Gte(
                nameof(IReadModelEvolutionDocument.ProjectionVersion),
                definition.CurrentProjectionVersion),
            cancellationToken: cancellationToken);

        return BuildSnapshot(
            total,
            documentsAtCurrentSchema,
            documentsAtCurrentProjection,
            definition,
            DateTime.UtcNow);
    }

    private async Task<ReadModelConvergenceSnapshot?> GetElasticsearchSnapshotAsync(
        ReadModelDefinition definition,
        CancellationToken cancellationToken)
    {
        if (!_elasticsearchSettings.Enabled)
        {
            return null;
        }

        var indexName = ResolveElasticsearchIndexName(definition);
        if (indexName == null)
        {
            return null;
        }

        var total = await CountElasticsearchAsync(indexName, new { match_all = new { } }, cancellationToken);
        if (!total.HasValue)
        {
            return null;
        }

        var documentsAtCurrentSchema = await CountElasticsearchAsync(
            indexName,
            new
            {
                range = new Dictionary<string, object>
                {
                    ["readModelSchemaVersion"] = new { gte = definition.CurrentSchemaVersion }
                }
            },
            cancellationToken);
        var documentsAtCurrentProjection = await CountElasticsearchAsync(
            indexName,
            new
            {
                range = new Dictionary<string, object>
                {
                    ["projectionVersion"] = new { gte = definition.CurrentProjectionVersion }
                }
            },
            cancellationToken);

        if (!documentsAtCurrentSchema.HasValue || !documentsAtCurrentProjection.HasValue)
        {
            return null;
        }

        return BuildSnapshot(
            total.Value,
            documentsAtCurrentSchema.Value,
            documentsAtCurrentProjection.Value,
            definition,
            DateTime.UtcNow);
    }

    private async Task MarkMongoConvergedAsync(
        ReadModelDefinition definition,
        DateTime convergedAtUtc,
        CancellationToken cancellationToken)
    {
        var collectionName = ResolveMongoCollectionName(definition);
        if (collectionName == null)
        {
            return;
        }

        var collection = _mongoDbContext.Database.GetCollection<BsonDocument>(collectionName);
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Gte(
                nameof(IReadModelEvolutionDocument.ReadModelSchemaVersion),
                definition.CurrentSchemaVersion),
            Builders<BsonDocument>.Filter.Gte(
                nameof(IReadModelEvolutionDocument.ProjectionVersion),
                definition.CurrentProjectionVersion),
            Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Exists(
                    nameof(IReadModelEvolutionDocument.ProjectionConvergedAtUtc),
                    false),
                Builders<BsonDocument>.Filter.Eq(
                    nameof(IReadModelEvolutionDocument.ProjectionConvergedAtUtc),
                    BsonNull.Value)));

        var update = Builders<BsonDocument>.Update
            .Set(nameof(IReadModelEvolutionDocument.ReadModelName), definition.Name)
            .Set(nameof(IReadModelEvolutionDocument.ReadModelSchemaVersion), definition.CurrentSchemaVersion)
            .Set(nameof(IReadModelEvolutionDocument.ProjectionVersion), definition.CurrentProjectionVersion)
            .Set(nameof(IReadModelEvolutionDocument.ProjectionConvergedAtUtc), convergedAtUtc);

        await collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
    }

    private async Task MarkElasticsearchConvergedAsync(
        ReadModelDefinition definition,
        DateTime convergedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!_elasticsearchSettings.Enabled)
        {
            return;
        }

        var indexName = ResolveElasticsearchIndexName(definition);
        if (indexName == null)
        {
            return;
        }

        var response = await _elasticClient.LowLevel.DoRequestAsync<StringResponse>(
            global::Elasticsearch.Net.HttpMethod.POST,
            $"{indexName}/_update_by_query?conflicts=proceed&refresh=false",
            cancellationToken,
            PostData.Serializable(new
            {
                script = new
                {
                    source = "ctx._source.projectionConvergedAtUtc = params.convergedAtUtc",
                    lang = "painless",
                    @params = new
                    {
                        convergedAtUtc = convergedAtUtc.ToString("O")
                    }
                },
                query = new
                {
                    @bool = new
                    {
                        must = new object[]
                        {
                            new
                            {
                                range = new Dictionary<string, object>
                                {
                                    ["readModelSchemaVersion"] = new { gte = definition.CurrentSchemaVersion }
                                }
                            },
                            new
                            {
                                range = new Dictionary<string, object>
                                {
                                    ["projectionVersion"] = new { gte = definition.CurrentProjectionVersion }
                                }
                            }
                        },
                        must_not = new object[]
                        {
                            new
                            {
                                exists = new { field = "projectionConvergedAtUtc" }
                            }
                        }
                    }
                }
            }));

        if (!response.Success)
        {
            _logger.LogWarning(
                "Elasticsearch read model convergence timestamp update failed | ReadModel: {ReadModelName} | Index: {IndexName} | Error: {Error}",
                definition.Name,
                indexName,
                response.Body);
        }
    }

    private async Task<long?> CountElasticsearchAsync(
        string indexName,
        object query,
        CancellationToken cancellationToken)
    {
        var response = await _elasticClient.LowLevel.DoRequestAsync<StringResponse>(
            global::Elasticsearch.Net.HttpMethod.POST,
            $"{indexName}/_count",
            cancellationToken,
            PostData.Serializable(new { query }));

        if (!response.Success || string.IsNullOrWhiteSpace(response.Body))
        {
            return null;
        }

        using var document = JsonDocument.Parse(response.Body);
        if (document.RootElement.TryGetProperty("count", out var countElement) &&
            countElement.TryGetInt64(out var count))
        {
            return count;
        }

        return null;
    }

    private static ReadModelConvergenceSnapshot BuildSnapshot(
        long total,
        long documentsAtCurrentSchema,
        long documentsAtCurrentProjection,
        ReadModelDefinition definition,
        DateTime observedAtUtc)
    {
        var minimumSchemaVersion = total == 0 || documentsAtCurrentSchema >= total
            ? definition.CurrentSchemaVersion
            : 0;
        var minimumProjectionVersion = total == 0 || documentsAtCurrentProjection >= total
            ? definition.CurrentProjectionVersion
            : 0;

        return new ReadModelConvergenceSnapshot(
            total,
            documentsAtCurrentSchema,
            documentsAtCurrentProjection,
            minimumSchemaVersion,
            minimumProjectionVersion,
            observedAtUtc);
    }

    private string? ResolveMongoCollectionName(ReadModelDefinition definition)
    {
        if (definition.Store != ReadModelStore.MongoDb)
        {
            return null;
        }

        return definition.Name switch
        {
            ReadModelEvolutionRegistry.StorefrontProduct => _mongoDbContext.Products.CollectionNamespace.CollectionName,
            ReadModelEvolutionRegistry.StorefrontCategory => _mongoDbContext.Categories.CollectionNamespace.CollectionName,
            ReadModelEvolutionRegistry.StorefrontPromotion => _mongoDbContext.Promotions.CollectionNamespace.CollectionName,
            ReadModelEvolutionRegistry.CommerceHubProduct => _mongoDbContext.CommerceHubProducts.CollectionNamespace.CollectionName,
            _ => null
        };
    }

    private string? ResolveElasticsearchIndexName(ReadModelDefinition definition)
    {
        if (definition.Store != ReadModelStore.Elasticsearch)
        {
            return null;
        }

        return definition.Name switch
        {
            ReadModelEvolutionRegistry.StorefrontProduct => _elasticsearchSettings.ProductsIndexName,
            ReadModelEvolutionRegistry.StorefrontCategory => CategorySearchIndexName,
            ReadModelEvolutionRegistry.CommerceHubProduct => _elasticsearchSettings.CommerceHubProductsIndexName,
            _ => null
        };
    }
}
