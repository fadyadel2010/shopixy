using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Nest;
using PointOfSale.Core.DTOs;
using Npgsql;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Configuration;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

var root = FindRepoRoot(AppContext.BaseDirectory);
var configPath = Path.Combine(root, "PointOfSale.API", "appsettings.Development.local.json");
using var configJson = JsonDocument.Parse(File.ReadAllText(configPath));

var sqlConnectionString = GetConfig("ConnectionStrings", "DefaultConnection");
var mongoConnectionString = GetConfig("MongoDB", "ConnectionString");
var mongoDatabaseName = GetConfig("MongoDB", "DatabaseName");
var mongoProductsCollection = GetConfig("MongoDB", "ProductsCollectionName");
var elasticsearchUrl = GetConfig("Elasticsearch", "Url").TrimEnd('/');
var productsIndex = GetConfig("Elasticsearch", "ProductsIndexName");

await using var sql = new NpgsqlConnection(sqlConnectionString);
await sql.OpenAsync();

var products = await LoadSqlVisibleProductsAsync(sql);
Console.WriteLine($"SQL_BASE_VISIBLE_COUNT={products.Count}");

foreach (var group in products.GroupBy(item => item.TenantId))
{
    Console.WriteLine($"TENANT={group.Key}");
    Console.WriteLine($"SQL_BASE_VISIBLE_FOR_TENANT={group.Count()}");

    var tenant = await LoadTenantAsync(sql, group.Key);
    Console.WriteLine($"TENANT_ACTIVE_AFTER_LINEAGE={Bool01(tenant is { IsActive: true, Status: 1, DeletedAtUtc: null })}");
    Console.WriteLine($"TENANT_NAME={tenant?.Name ?? "<missing>"}");
    Console.WriteLine($"TENANT_SLUG={tenant?.Slug ?? "<null>"}");
    Console.WriteLine($"TENANT_STATUS={tenant?.Status.ToString() ?? "<missing>"}");
    Console.WriteLine($"TENANT_IS_ACTIVE={Bool01(tenant?.IsActive == true)}");
    Console.WriteLine($"TENANT_DELETED_AT={(tenant?.DeletedAtUtc?.ToString("O") ?? "<null>")}");

    var store = await LoadStoreConfigAsync(sql, group.Key);
    Console.WriteLine($"STORE_CONFIG_EXISTS={Bool01(store != null)}");
    Console.WriteLine($"STOREFRONT_STATUS={store?.StorefrontStatus ?? "<missing>"}");

    var categoryAccepted = 0;
    var variantPriceAccepted = 0;
    foreach (var product in group)
    {
        var category = await ResolveCategoryAsync(sql, product.TenantId, product.CategoryId);
        var variants = await LoadVariantSummaryAsync(sql, product.TenantId, product.ProductId);
        var sqlOverlayAccepted = tenant is { IsActive: true, Status: 1, DeletedAtUtc: null } &&
            category.Live &&
            (!product.HasVariants || variants.ActivePricedCurrentVariants > 0);

        if (category.Live)
        {
            categoryAccepted++;
        }

        if (!product.HasVariants || variants.ActivePricedCurrentVariants > 0)
        {
            variantPriceAccepted++;
        }

        Console.WriteLine($"PRODUCT={product.ProductId}");
        Console.WriteLine($"PRODUCT_NAME={product.Name}");
        Console.WriteLine($"PRODUCT_SLUG={product.Slug ?? "<null>"}");
        Console.WriteLine($"PRODUCT_CATEGORY={product.CategoryId}");
        Console.WriteLine($"PRODUCT_HAS_VARIANTS={Bool01(product.HasVariants)}");
        Console.WriteLine($"CATEGORY_LIVE={Bool01(category.Live)}");
        Console.WriteLine($"CATEGORY_REASON={category.Reason}");
        Console.WriteLine($"CURRENT_VARIANTS={variants.CurrentVariants}");
        Console.WriteLine($"ACTIVE_CURRENT_VARIANTS={variants.ActiveCurrentVariants}");
        Console.WriteLine($"ACTIVE_PRICED_CURRENT_VARIANTS={variants.ActivePricedCurrentVariants}");
        Console.WriteLine($"SQL_OVERLAY_ACCEPTED={Bool01(sqlOverlayAccepted)}");
    }

    Console.WriteLine($"AFTER_TENANT_FILTER={group.Count(item => tenant is { IsActive: true, Status: 1, DeletedAtUtc: null })}");
    Console.WriteLine($"AFTER_CATEGORY_LINEAGE={categoryAccepted}");
    Console.WriteLine($"AFTER_VARIANT_PRICE_RULE={variantPriceAccepted}");

    if (args.Contains("--repair", StringComparer.OrdinalIgnoreCase))
    {
        await RepairTenantProjectionAsync(group.Key, group.Select(item => item.ProductId).ToList());
    }

    await TraceMongoAsync(group.Key, group.Select(item => item.ProductId).ToList());
    await TraceElasticsearchAsync(group.Key, group.Select(item => item.ProductId).ToList());
    await TraceDiscoveryServiceAsync(group.Key);
    await TraceProjectionEventsAsync(sql, group.Key, group.Select(item => item.ProductId).ToList());
    await TraceApiAsync(group.Key);
}

string GetConfig(params string[] path)
{
    JsonElement current = configJson.RootElement;
    foreach (var segment in path)
    {
        current = current.GetProperty(segment);
    }

    return current.GetString() ?? string.Empty;
}

async Task<List<SqlProduct>> LoadSqlVisibleProductsAsync(NpgsqlConnection connection)
{
    const string commandText = """
        SELECT "TenantId", "Id", "Name", "Slug", "CategoryId", "HasVariants"
        FROM "Products"
        WHERE "IsActive" = TRUE
          AND "PublishStatus" = 1
          AND "IsOnlineVisible" = TRUE
          AND "IsSearchable" = TRUE
          AND "IsDeleted" = FALSE
        ORDER BY "CreatedAt" DESC, "Id"
        """;

    await using var command = new NpgsqlCommand(commandText, connection);
    await using var reader = await command.ExecuteReaderAsync();

    var rows = new List<SqlProduct>();
    while (await reader.ReadAsync())
    {
        rows.Add(new SqlProduct(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetGuid(4),
            reader.GetBoolean(5)));
    }

    return rows;
}

async Task<TenantRow?> LoadTenantAsync(NpgsqlConnection connection, Guid tenantId)
{
    const string commandText = """
        SELECT "Name", "Slug", "IsActive", "Status", "DeletedAtUtc"
        FROM "Tenants"
        WHERE "Id" = @tenantId
        """;

    await using var command = new NpgsqlCommand(commandText, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return new TenantRow(
        reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetString(1),
        reader.GetBoolean(2),
        reader.GetInt32(3),
        reader.IsDBNull(4) ? null : reader.GetDateTime(4));
}

async Task<StoreConfigRow?> LoadStoreConfigAsync(NpgsqlConnection connection, Guid tenantId)
{
    const string commandText = """
        SELECT "StorefrontStatus"
        FROM "StoreConfigs"
        WHERE "TenantId" = @tenantId
        """;

    await using var command = new NpgsqlCommand(commandText, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);
    await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync()
        ? new StoreConfigRow(reader.GetString(0))
        : null;
}

async Task<CategoryTrace> ResolveCategoryAsync(NpgsqlConnection connection, Guid tenantId, Guid categoryId)
{
    if (categoryId == Guid.Empty)
    {
        return new CategoryTrace(false, "CategoryId is empty");
    }

    var visited = new HashSet<Guid>();
    var current = categoryId;

    while (current != Guid.Empty)
    {
        if (!visited.Add(current))
        {
            return new CategoryTrace(false, "Category lineage cycle");
        }

        const string commandText = """
            SELECT "Id", "ParentCategoryId", "IsDeleted"
            FROM "Categories"
            WHERE "TenantId" = @tenantId
              AND "Id" = @categoryId
            """;

        await using var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("categoryId", current);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return new CategoryTrace(false, $"Category {current} missing for tenant");
        }

        var parent = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1);
        var isDeleted = reader.GetBoolean(2);
        if (isDeleted)
        {
            return new CategoryTrace(false, $"Category {current} deleted");
        }

        if (!parent.HasValue)
        {
            return new CategoryTrace(true, "Live");
        }

        if (parent.Value == Guid.Empty || parent.Value == current)
        {
            return new CategoryTrace(false, $"Invalid parent {parent.Value}");
        }

        current = parent.Value;
    }

    return new CategoryTrace(false, "Category lineage fell through");
}

async Task<VariantSummary> LoadVariantSummaryAsync(NpgsqlConnection connection, Guid tenantId, Guid productId)
{
    const string commandText = """
        SELECT
          COUNT(*) FILTER (WHERE "IsDeleted" = FALSE AND "SemanticLifecycleState" = 0) AS current_count,
          COUNT(*) FILTER (WHERE "IsDeleted" = FALSE AND "SemanticLifecycleState" = 0 AND "IsActive" = TRUE) AS active_count,
          COUNT(*) FILTER (
            WHERE "IsDeleted" = FALSE
              AND "SemanticLifecycleState" = 0
              AND "IsActive" = TRUE
              AND COALESCE("OnlinePrice", "Price") > 0
          ) AS active_priced_count
        FROM "ProductVariants"
        WHERE "TenantId" = @tenantId
          AND "ProductId" = @productId
        """;

    await using var command = new NpgsqlCommand(commandText, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);
    command.Parameters.AddWithValue("productId", productId);
    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();
    return new VariantSummary(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
}

async Task TraceMongoAsync(Guid tenantId, IReadOnlyCollection<Guid> productIds)
{
    var client = new MongoClient(mongoConnectionString);
    var collection = client
        .GetDatabase(mongoDatabaseName)
        .GetCollection<BsonDocument>(mongoProductsCollection);

    var tenantFilter = Builders<BsonDocument>.Filter.Eq("TenantId", tenantId.ToString());
    var visibleFilter = tenantFilter &
        Builders<BsonDocument>.Filter.Eq("IsActive", true) &
        Builders<BsonDocument>.Filter.Eq("IsOnlineVisible", true);

    Console.WriteLine($"MONGO_TENANT_DOCS={await collection.CountDocumentsAsync(tenantFilter)}");
    Console.WriteLine($"MONGO_VISIBLE_DOCS={await collection.CountDocumentsAsync(visibleFilter)}");

    foreach (var productId in productIds)
    {
        var productFilter = tenantFilter & Builders<BsonDocument>.Filter.Eq("ProductId", productId.ToString());
        var document = await collection.Find(productFilter).FirstOrDefaultAsync();
        Console.WriteLine($"MONGO_PRODUCT_{productId}_EXISTS={Bool01(document != null)}");
        if (document != null)
        {
            Console.WriteLine($"MONGO_PRODUCT_{productId}_IS_ACTIVE={Bool01(document.GetValue("IsActive", false).ToBoolean())}");
            Console.WriteLine($"MONGO_PRODUCT_{productId}_IS_ONLINE_VISIBLE={Bool01(document.GetValue("IsOnlineVisible", false).ToBoolean())}");
        }
    }
}

async Task TraceElasticsearchAsync(Guid tenantId, IReadOnlyCollection<Guid> productIds)
{
    using var client = new HttpClient { BaseAddress = new Uri(elasticsearchUrl) };

    var sample = await PostJsonAsync(client, $"/{productsIndex}/_search", """
        {"size":1,"query":{"match_all":{}}}
        """);
    var sampleSource = sample.RootElement
        .GetProperty("hits")
        .GetProperty("hits")
        .EnumerateArray()
        .FirstOrDefault()
        .TryGetProperty("_source", out var source)
        ? source
        : default;
    var tenantField = sampleSource.ValueKind == JsonValueKind.Object && sampleSource.TryGetProperty("tenantId", out _)
        ? "tenantId"
        : "TenantId";
    var productField = sampleSource.ValueKind == JsonValueKind.Object && sampleSource.TryGetProperty("productId", out _)
        ? "productId"
        : "ProductId";
    var activeField = sampleSource.ValueKind == JsonValueKind.Object && sampleSource.TryGetProperty("isActive", out _)
        ? "isActive"
        : "IsActive";
    var visibleField = sampleSource.ValueKind == JsonValueKind.Object && sampleSource.TryGetProperty("isOnlineVisible", out _)
        ? "isOnlineVisible"
        : "IsOnlineVisible";

    var tenantDocs = await EsCountAsync(client, new
    {
        term = new Dictionary<string, object> { [tenantField] = tenantId.ToString() }
    });
    var activeDocs = await EsCountAsync(client, BoolFilter(
        Term(tenantField, tenantId.ToString()),
        Term(activeField, true)));
    var visibleDocs = await EsCountAsync(client, BoolFilter(
        Term(tenantField, tenantId.ToString()),
        Term(activeField, true),
        Term(visibleField, true)));

    Console.WriteLine($"ES_FIELD_STYLE_TENANT={tenantField}");
    Console.WriteLine($"ES_TENANT_DOCS={tenantDocs}");
    Console.WriteLine($"ES_AFTER_ACTIVE_FILTER={activeDocs}");
    Console.WriteLine($"ES_AFTER_ONLINE_VISIBLE_FILTER={visibleDocs}");

    foreach (var productId in productIds)
    {
        var exists = await EsCountAsync(client, BoolFilter(
            Term(tenantField, tenantId.ToString()),
            Term(productField, productId.ToString())));
        var visible = await EsCountAsync(client, BoolFilter(
            Term(tenantField, tenantId.ToString()),
            Term(productField, productId.ToString()),
            Term(activeField, true),
            Term(visibleField, true)));
        Console.WriteLine($"ES_PRODUCT_{productId}_DOCS={exists}");
        Console.WriteLine($"ES_PRODUCT_{productId}_VISIBLE_DOCS={visible}");
    }

    var discoverCandidates = await EsSearchTotalAsync(client, BoolFilter(
        Term(tenantField, tenantId.ToString()),
        Term(activeField, true),
        Term(visibleField, true)));
    Console.WriteLine($"ES_DISCOVER_CANDIDATES_TOTAL={discoverCandidates.Total}");
    Console.WriteLine($"ES_DISCOVER_CANDIDATES_FIRST_PAGE={discoverCandidates.Hits}");
}

async Task TraceApiAsync(Guid tenantId)
{
    using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5010") };
    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        "/api/storefront/products/discover?page=1&pageSize=12&lang=en");
    request.Headers.Add("X-Tenant-Id", tenantId.ToString());

    try
    {
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"API_STATUS={(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"API_BODY={Trim(text, 500)}");
            return;
        }

        using var document = JsonDocument.Parse(text);
        var items = document.RootElement.GetProperty("items");
        Console.WriteLine($"API_ITEMS_COUNT={items.GetArrayLength()}");
        Console.WriteLine($"API_TOTAL_COUNT={document.RootElement.GetProperty("totalCount").GetInt32()}");
        Console.WriteLine("API_ITEM_IDS=" + string.Join(",", items.EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"API_ERROR={ex.GetType().Name}:{ex.Message}");
    }
}

async Task TraceProjectionEventsAsync(NpgsqlConnection connection, Guid tenantId, IReadOnlyCollection<Guid> productIds)
{
    var outboxTables = await LoadMatchingTableNamesAsync(connection, "%Outbox%");
    Console.WriteLine("OUTBOX_TABLES=" + string.Join(",", outboxTables));

    foreach (var table in outboxTables.Where(name => name.Contains("Outbox", StringComparison.OrdinalIgnoreCase)))
    {
        var columns = await LoadColumnNamesAsync(connection, table);
        if (string.Equals(table, "OutboxMessage", StringComparison.Ordinal) ||
            string.Equals(table, "OutboxMessages", StringComparison.Ordinal))
        {
            Console.WriteLine($"OUTBOX_{table}_COLUMNS={string.Join(",", columns.Order(StringComparer.OrdinalIgnoreCase))}");
            Console.WriteLine($"OUTBOX_{table}_TOTAL_ROWS={await CountAllRowsAsync(connection, table)}");
        }

        if (columns.Contains("MessageType") && columns.Contains("Content"))
        {
            var count = await CountJsonTextMatchesAsync(
                connection,
                table,
                "Content",
                productIds.Select(id => id.ToString()).ToList());
            Console.WriteLine($"OUTBOX_{table}_PRODUCT_ID_REFERENCES={count}");
        }
        else if (columns.Contains("Payload"))
        {
            var count = await CountJsonTextMatchesAsync(
                connection,
                table,
                "Payload",
                productIds.Select(id => id.ToString()).ToList());
            Console.WriteLine($"OUTBOX_{table}_PRODUCT_ID_REFERENCES={count}");
        }
        else if (columns.Contains("Body"))
        {
            var count = await CountJsonTextMatchesAsync(
                connection,
                table,
                "Body",
                productIds.Select(id => id.ToString()).ToList());
            Console.WriteLine($"OUTBOX_{table}_PRODUCT_ID_REFERENCES={count}");
        }

        if (columns.Contains("TenantId"))
        {
            var tenantCount = await CountRowsForTenantAsync(connection, table, tenantId);
            Console.WriteLine($"OUTBOX_{table}_TENANT_ROWS={tenantCount}");
        }
    }
}

async Task TraceDiscoveryServiceAsync(Guid tenantId)
{
    var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(sqlConnectionString)
        .Options;
    await using var dbContext = new ApplicationDbContext(dbOptions);

    var elasticConnection = new ConnectionSettings(new Uri(elasticsearchUrl))
        .DefaultIndex(productsIndex)
        .DefaultMappingFor<ProductSearchDocument>(mapping => mapping.IndexName(productsIndex));
    var elasticClient = new ElasticClient(elasticConnection);
    var discovery = new ProductDiscoveryService(
        elasticClient,
        dbContext,
        Options.Create(new ElasticsearchSettings
        {
            Url = elasticsearchUrl,
            Enabled = true,
            ProductsIndexName = productsIndex,
            RequestTimeoutSeconds = 30
        }),
        NullLogger<ProductDiscoveryService>.Instance);

    var result = await discovery.DiscoverAsync(
        tenantId,
        new ProductDiscoveryQuery
        {
            Page = 1,
            PageSize = 12,
            Sort = ProductSortMode.Relevance,
            Lang = "en"
        });

    Console.WriteLine($"SERVICE_DISCOVER_ITEMS={result.Items.Count}");
    Console.WriteLine($"SERVICE_DISCOVER_TOTAL={result.TotalCount}");
    Console.WriteLine("SERVICE_DISCOVER_ITEM_IDS=" + string.Join(",", result.Items.Select(item => item.Id)));
}

async Task RepairTenantProjectionAsync(Guid tenantId, IReadOnlyCollection<Guid> productIds)
{
    var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(sqlConnectionString)
        .Options;
    await using var dbContext = new ApplicationDbContext(dbOptions);

    var mongoSettings = Options.Create(new MongoDbSettings
    {
        ConnectionString = mongoConnectionString,
        DatabaseName = mongoDatabaseName,
        ProductsCollectionName = mongoProductsCollection
    });
    var mongoContext = new MongoDbContext(mongoSettings, NullLogger<MongoDbContext>.Instance);
    var repository = new ProductReadRepository(mongoContext);
    var mutationBoundary = new StorefrontProductProjectionMutationBoundary(repository);
    var projectionWriter = new ProductSqlAuthorityProjectionWriter(dbContext, mutationBoundary);

    var elasticSettings = Options.Create(new ElasticsearchSettings
    {
        Url = elasticsearchUrl,
        Enabled = true,
        ProductsIndexName = productsIndex,
        RequestTimeoutSeconds = 30,
        NumberOfShards = 1,
        NumberOfReplicas = 0
    });
    var elasticConnection = new ConnectionSettings(new Uri(elasticsearchUrl))
        .DefaultIndex(productsIndex)
        .DefaultMappingFor<ProductSearchDocument>(mapping => mapping.IndexName(productsIndex));
    var elasticClient = new ElasticClient(elasticConnection);
    var searchIndex = new ProductSearchIndexService(
        elasticClient,
        elasticSettings,
        new SearchTelemetry(),
        NullLogger<ProductSearchIndexService>.Instance);

    await searchIndex.EnsureIndexExistsAsync();

    var mongoUpserted = 0;
    var indexed = 0;
    var tombstoned = 0;
    var errors = 0;

    foreach (var productId in productIds)
    {
        try
        {
            var productVersion = await dbContext.Products
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(product => product.TenantId == tenantId && product.Id == productId)
                .Select(product => new
                {
                    product.Id,
                    product.CreatedAt,
                    product.UpdatedAt,
                    product.ProjectionMutationVersion
                })
                .SingleAsync();

            var eventVersion = ProductEventVersionHelper.ToProductEventVersion(productVersion.ProjectionMutationVersion);
            var eventOccurredAtUtc = productVersion.UpdatedAt ?? productVersion.CreatedAt;
            var eventId = Guid.NewGuid();

            var writeResult = await projectionWriter.ApplyAsync(
                new ProductSqlAuthorityProjectionWriteRequest(
                    tenantId,
                    productId,
                    new ProductDocument
                    {
                        TenantId = tenantId,
                        ProductId = productId
                    },
                    eventId,
                    eventOccurredAtUtc,
                    eventVersion));

            if (writeResult.Tombstoned)
            {
                tombstoned++;
                await searchIndex.DeleteAsync(tenantId, productId);
                continue;
            }

            if (writeResult.Applied)
            {
                mongoUpserted++;
            }

            var sqlProjection = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                dbContext,
                new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = productId
                },
                tenantId,
                CancellationToken.None);

            if (sqlProjection is { IsActive: true, IsOnlineVisible: true })
            {
                var searchDocument = ProductSearchDocumentMapper.MapFromProductDocument(sqlProjection, eventId);
                if (await searchIndex.IndexAsync(searchDocument))
                {
                    indexed++;
                }
            }
            else
            {
                await searchIndex.DeleteAsync(tenantId, productId);
            }
        }
        catch (Exception ex)
        {
            errors++;
            Console.WriteLine($"REPAIR_PRODUCT_{productId}_ERROR={ex.GetType().Name}:{ex.Message}");
        }
    }

    Console.WriteLine($"REPAIR_MONGO_UPSERTED={mongoUpserted}");
    Console.WriteLine($"REPAIR_ES_INDEXED={indexed}");
    Console.WriteLine($"REPAIR_TOMBSTONED={tombstoned}");
    Console.WriteLine($"REPAIR_ERRORS={errors}");
}

async Task<List<string>> LoadMatchingTableNamesAsync(NpgsqlConnection connection, string like)
{
    const string commandText = """
        SELECT table_name
        FROM information_schema.tables
        WHERE table_schema = 'public'
          AND table_name ILIKE @like
        ORDER BY table_name
        """;

    await using var command = new NpgsqlCommand(commandText, connection);
    command.Parameters.AddWithValue("like", like);
    await using var reader = await command.ExecuteReaderAsync();
    var result = new List<string>();
    while (await reader.ReadAsync())
    {
        result.Add(reader.GetString(0));
    }

    return result;
}

async Task<HashSet<string>> LoadColumnNamesAsync(NpgsqlConnection connection, string table)
{
    const string commandText = """
        SELECT column_name
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = @table
        """;

    await using var command = new NpgsqlCommand(commandText, connection);
    command.Parameters.AddWithValue("table", table);
    await using var reader = await command.ExecuteReaderAsync();
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    while (await reader.ReadAsync())
    {
        result.Add(reader.GetString(0));
    }

    return result;
}

async Task<long> CountJsonTextMatchesAsync(NpgsqlConnection connection, string table, string column, IReadOnlyCollection<string> needles)
{
    if (needles.Count == 0)
    {
        return 0;
    }

    var predicates = needles.Select((_, index) => $""" "{column}"::text ILIKE @needle{index} """);
    var commandText = $"""SELECT COUNT(*) FROM "{table}" WHERE {string.Join(" OR ", predicates)}""";
    await using var command = new NpgsqlCommand(commandText, connection);
    var i = 0;
    foreach (var needle in needles)
    {
        command.Parameters.AddWithValue($"needle{i}", $"%{needle}%");
        i++;
    }

    return (long)(await command.ExecuteScalarAsync() ?? 0L);
}

async Task<long> CountAllRowsAsync(NpgsqlConnection connection, string table)
{
    var commandText = $"""SELECT COUNT(*) FROM "{table}" """;
    await using var command = new NpgsqlCommand(commandText, connection);
    return (long)(await command.ExecuteScalarAsync() ?? 0L);
}

async Task<long> CountRowsForTenantAsync(NpgsqlConnection connection, string table, Guid tenantId)
{
    var commandText = $"""SELECT COUNT(*) FROM "{table}" WHERE "TenantId" = @tenantId""";
    await using var command = new NpgsqlCommand(commandText, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);
    return (long)(await command.ExecuteScalarAsync() ?? 0L);
}

static Dictionary<string, object> Term(string field, object value) =>
    new() { ["term"] = new Dictionary<string, object> { [field] = value } };

static Dictionary<string, object> BoolFilter(params object[] filters) =>
    new() { ["bool"] = new Dictionary<string, object> { ["filter"] = filters } };

async Task<long> EsCountAsync(HttpClient client, object query)
{
    var payload = JsonSerializer.Serialize(new { query });
    using var document = await PostJsonAsync(client, $"/{productsIndex}/_count", payload);
    return document.RootElement.GetProperty("count").GetInt64();
}

async Task<(long Total, int Hits)> EsSearchTotalAsync(HttpClient client, object query)
{
    var payload = JsonSerializer.Serialize(new
    {
        from = 0,
        size = 12,
        track_total_hits = true,
        query
    });
    using var document = await PostJsonAsync(client, $"/{productsIndex}/_search", payload);
    var hitsElement = document.RootElement.GetProperty("hits");
    var total = hitsElement.GetProperty("total");
    var value = total.ValueKind == JsonValueKind.Object
        ? total.GetProperty("value").GetInt64()
        : total.GetInt64();
    return (value, hitsElement.GetProperty("hits").GetArrayLength());
}

async Task<JsonDocument> PostJsonAsync(HttpClient client, string path, string json)
{
    using var content = new StringContent(json, Encoding.UTF8, "application/json");
    using var response = await client.PostAsync(path, content);
    var responseText = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"{path} failed: {(int)response.StatusCode} {Trim(responseText, 500)}");
    }

    return JsonDocument.Parse(responseText);
}

static string FindRepoRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Repo root not found.");
}

static string Bool01(bool value) => value ? "1" : "0";

static string Trim(string value, int max) =>
    value.Length <= max ? value : value[..max];

sealed record SqlProduct(
    Guid TenantId,
    Guid ProductId,
    string Name,
    string? Slug,
    Guid CategoryId,
    bool HasVariants);

sealed record TenantRow(string Name, string? Slug, bool IsActive, int Status, DateTime? DeletedAtUtc);
sealed record StoreConfigRow(string StorefrontStatus);
sealed record CategoryTrace(bool Live, string Reason);
sealed record VariantSummary(long CurrentVariants, long ActiveCurrentVariants, long ActivePricedCurrentVariants);
