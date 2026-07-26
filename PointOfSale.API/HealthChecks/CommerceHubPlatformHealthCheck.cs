using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.HealthChecks;

public class CommerceHubPlatformHealthCheck : IHealthCheck
{
    private static readonly TimeSpan DependencyTimeout = TimeSpan.FromSeconds(5);

    private static readonly string[] RequiredPostgresTables =
    [
        "CommerceHubListings",
        "CommerceHubVendorSettlements",
        "SearchClicks",
        "SearchQueries",
        "SearchSynonyms",
        "SystemSchemaVersions"
    ];

    private static readonly (string Table, string Column)[] RequiredPostgresColumns =
    [
        ("CommerceHubSubOrders", "ShippingMethodId"),
        ("CommerceHubSubOrders", "ShippingMethodName"),
        ("CommerceHubSubOrders", "ShippingSnapshotJson"),
        ("CommerceHubSubOrders", "PaymentMethod"),
        ("CommerceHubSubOrders", "PaymentMethodCode"),
        ("CommerceHubSubOrders", "PaymentMethodName"),
        ("CommerceHubSubOrders", "PaymentSnapshotJson"),
        ("CommerceHubSubOrders", "IsEarningCreated"),
        ("CommerceHubSubOrders", "EarningCreatedAtUtc"),
        ("CommerceHubSubOrders", "PlatformCommissionAmount"),
        ("CommerceHubSubOrders", "VendorEarningAmount"),
        ("CommerceHubVendorEarnings", "PaymentId")
    ];

    private static readonly string[] RequiredMongoIndexes =
    [
        "_id_",
        "idx_commercehub_tenant_variant_unique",
        "idx_commercehub_published_category_stock",
        "idx_commercehub_product_variant"
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MongoDbContext _mongoDbContext;
    private readonly ICommerceHubSearchIndexService _searchIndexService;
    private readonly ILogger<CommerceHubPlatformHealthCheck> _logger;

    public CommerceHubPlatformHealthCheck(
        IServiceScopeFactory scopeFactory,
        MongoDbContext mongoDbContext,
        ICommerceHubSearchIndexService searchIndexService,
        ILogger<CommerceHubPlatformHealthCheck> logger)
    {
        _scopeFactory = scopeFactory;
        _mongoDbContext = mongoDbContext;
        _searchIndexService = searchIndexService;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        var warnings = new List<string>();
        var issues = new List<string>();

        await CheckPostgresAsync(data, warnings, issues, cancellationToken);
        await CheckMongoAsync(data, warnings, issues, cancellationToken);
        await CheckElasticsearchAsync(data, warnings, issues, cancellationToken);

        if (issues.Count > 0)
        {
            _logger.LogError(
                "CommerceHub platform health is unhealthy | Issues: {Issues} | Warnings: {Warnings}",
                string.Join("; ", issues),
                string.Join("; ", warnings));

            return HealthCheckResult.Unhealthy(
                description: string.Join(" | ", issues),
                data: data);
        }

        if (warnings.Count > 0)
        {
            _logger.LogWarning(
                "CommerceHub platform health is degraded | Warnings: {Warnings}",
                string.Join("; ", warnings));

            return HealthCheckResult.Degraded(
                description: string.Join(" | ", warnings),
                data: data);
        }

        return HealthCheckResult.Healthy(
            description: "CommerceHub platform dependencies are healthy.",
            data: data);
    }

    private async Task CheckPostgresAsync(
        IDictionary<string, object> data,
        ICollection<string> warnings,
        ICollection<string> issues,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            var missingTables = await GetMissingPostgresTablesAsync(dbContext, cancellationToken);
            var missingColumns = await GetMissingPostgresColumnsAsync(dbContext, cancellationToken);

            data["postgres"] = new Dictionary<string, object>
            {
                ["missingTables"] = missingTables,
                ["missingColumns"] = missingColumns,
                ["pendingMigrations"] = pendingMigrations
            };

            if (missingTables.Count > 0)
            {
                issues.Add($"PostgreSQL missing required tables: {string.Join(", ", missingTables)}");
            }
            else if (missingColumns.Count > 0)
            {
                issues.Add($"PostgreSQL missing required CommerceHub columns: {string.Join(", ", missingColumns)}");
            }
            else if (pendingMigrations.Length > 0)
            {
                warnings.Add($"PostgreSQL has pending migrations: {string.Join(", ", pendingMigrations)}");
            }
        }
        catch (Exception ex)
        {
            issues.Add("PostgreSQL health check failed.");
            _logger.LogError(ex, "CommerceHub PostgreSQL health check failed");
            data["postgres"] = new Dictionary<string, object>
            {
                ["error"] = ex.Message
            };
        }
    }

    private async Task CheckMongoAsync(
        IDictionary<string, object> data,
        ICollection<string> warnings,
        ICollection<string> issues,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DependencyTimeout);

            var collectionName = _mongoDbContext.CommerceHubProducts.CollectionNamespace.CollectionName;
            var collectionNamesCursor = await _mongoDbContext.Database.ListCollectionNamesAsync(cancellationToken: timeoutCts.Token);
            var collectionNames = await collectionNamesCursor.ToListAsync(timeoutCts.Token);
            var collectionExists = collectionNames.Contains(collectionName);

            var indexesCursor = await _mongoDbContext.CommerceHubProducts.Indexes.ListAsync(timeoutCts.Token);
            var indexDocuments = await indexesCursor.ToListAsync(timeoutCts.Token);
            var existingIndexes = indexDocuments
                .Select(x => x.GetValue("name", BsonNull.Value))
                .Where(x => x != BsonNull.Value)
                .Select(x => x.AsString)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missingIndexes = RequiredMongoIndexes
                .Where(indexName => !existingIndexes.Contains(indexName))
                .ToArray();

            data["mongo"] = new Dictionary<string, object>
            {
                ["collection"] = collectionName,
                ["collectionExists"] = collectionExists,
                ["missingIndexes"] = missingIndexes
            };

            if (!collectionExists)
            {
                issues.Add($"MongoDB collection '{collectionName}' does not exist.");
            }
            else if (missingIndexes.Length > 0)
            {
                warnings.Add($"MongoDB collection '{collectionName}' is missing indexes: {string.Join(", ", missingIndexes)}");
            }
        }
        catch (Exception ex)
        {
            issues.Add("MongoDB health check failed.");
            _logger.LogError(ex, "CommerceHub MongoDB health check failed");
            data["mongo"] = new Dictionary<string, object>
            {
                ["error"] = ex.Message
            };
        }
    }

    private async Task CheckElasticsearchAsync(
        IDictionary<string, object> data,
        ICollection<string> warnings,
        ICollection<string> issues,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DependencyTimeout);

            var verification = await _searchIndexService.VerifyMigrationAsync(cancellationToken: timeoutCts.Token);
            data["elasticsearch"] = new Dictionary<string, object>
            {
                ["aliasName"] = verification.AliasName,
                ["aliasExists"] = verification.AliasExists,
                ["aliasIndices"] = verification.AliasIndices.ToArray(),
                ["activeIndex"] = verification.ActiveIndexName ?? string.Empty,
                ["writeIndex"] = verification.WriteIndexName ?? string.Empty,
                ["desiredIndex"] = verification.DesiredIndexName,
                ["desiredIndexExists"] = verification.DesiredIndexExists,
                ["desiredDocumentCount"] = verification.DesiredDocumentCount,
                ["desiredIndexMissingFields"] = verification.DesiredIndexMissingFields.ToArray(),
                ["desiredIndexHasAttributeTemplates"] = verification.DesiredIndexHasAttributeTemplates,
                ["legacyConcreteIndexExists"] = verification.LegacyConcreteIndexExists,
                ["aliasPointsToDesiredIndex"] = verification.AliasPointsToDesiredIndex,
                ["smokeTestPassed"] = verification.SmokeTestPassed,
                ["smokeTestHitCount"] = verification.SmokeTestHitCount
            };

            if (string.IsNullOrWhiteSpace(verification.ActiveIndexName))
            {
                issues.Add("Elasticsearch CommerceHub index is missing.");
                return;
            }

            if (!verification.DesiredIndexReady)
            {
                warnings.Add("Elasticsearch desired CommerceHub index is not fully ready for promotion.");
            }

            if (!verification.AliasExists || !verification.AliasPointsToDesiredIndex)
            {
                warnings.Add("Elasticsearch alias is missing or not pointing to the desired CommerceHub versioned index.");
            }

            if (verification.LegacyConcreteIndexExists && !verification.AliasExists)
            {
                warnings.Add(
                    $"Elasticsearch is running in legacy concrete-index mode. Desired index '{verification.DesiredIndexName}' is ready state: {verification.DesiredIndexReady}.");
            }

            if (!verification.SmokeTestPassed)
            {
                warnings.Add("Elasticsearch smoke search through the CommerceHub alias did not validate successfully.");
            }
        }
        catch (Exception ex)
        {
            issues.Add("Elasticsearch health check failed.");
            _logger.LogError(ex, "CommerceHub Elasticsearch health check failed");
            data["elasticsearch"] = new Dictionary<string, object>
            {
                ["error"] = ex.Message
            };
        }
    }

    private static async Task<IReadOnlyCollection<string>> GetMissingPostgresTablesAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    to_regclass('public."CommerceHubListings"') AS "CommerceHubListings",
                    to_regclass('public."CommerceHubVendorSettlements"') AS "CommerceHubVendorSettlements",
                    to_regclass('public."SearchClicks"') AS "SearchClicks",
                    to_regclass('public."SearchQueries"') AS "SearchQueries",
                    to_regclass('public."SearchSynonyms"') AS "SearchSynonyms",
                    to_regclass('public."SystemSchemaVersions"') AS "SystemSchemaVersions";
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                foreach (var tableName in RequiredPostgresTables)
                {
                    var ordinal = reader.GetOrdinal(tableName);
                    if (reader.IsDBNull(ordinal))
                    {
                        missing.Add(tableName);
                    }
                }
            }

            return missing;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<IReadOnlyCollection<string>> GetMissingPostgresColumnsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            foreach (var (table, column) in RequiredPostgresColumns)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = @tableName
                          AND column_name = @columnName
                    );
                    """;

                var tableParameter = command.CreateParameter();
                tableParameter.ParameterName = "@tableName";
                tableParameter.Value = table;
                command.Parameters.Add(tableParameter);

                var columnParameter = command.CreateParameter();
                columnParameter.ParameterName = "@columnName";
                columnParameter.Value = column;
                command.Parameters.Add(columnParameter);

                var exists = await command.ExecuteScalarAsync(cancellationToken);
                if (exists is not true)
                {
                    missing.Add($"{table}.{column}");
                }
            }

            return missing;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
