using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;
using StackExchange.Redis;

if (args.Length != 2 || !Guid.TryParse(args[1], out var tenantId))
{
    Console.Error.WriteLine("Usage: StorefrontConsistencyAudit <workspace-root> <tenant-id>");
    return 2;
}

var workspaceRoot = Path.GetFullPath(args[0]);
var settingsPath = Path.Combine(workspaceRoot, "PointOfSale.API", "appsettings.Development.local.json");
using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
var root = settings.RootElement;
var postgres = root.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!;
var mongoConnection = root.GetProperty("MongoDB").GetProperty("ConnectionString").GetString()!;
var mongoDatabase = root.GetProperty("MongoDB").GetProperty("DatabaseName").GetString()!;
var mongoCollection = root.GetProperty("MongoDB").GetProperty("ProductsCollectionName").GetString()!;
var redisConnection = root.GetProperty("Redis").GetProperty("ConnectionString").GetString()!;
var redisInstance = root.GetProperty("Redis").GetProperty("InstanceName").GetString()!;

await using (var connection = new NpgsqlConnection(postgres))
{
    await connection.OpenAsync();
    const string sql = """
        SELECT jsonb_build_object(
            'tenantId', t."Id",
            'tenantSlug', t."Slug",
            'tenantName', t."Name",
            'tenantActive', t."IsActive",
            'tenantStatus', t."Status",
            'productId', p."Id",
            'name', p."Name",
            'slug', p."Slug",
            'hasVariants', p."HasVariants",
            'isActive', p."IsActive",
            'isOnlineVisible', p."IsOnlineVisible",
            'isSearchable', p."IsSearchable",
            'isDeleted', p."IsDeleted",
            'publishStatus', p."PublishStatus",
            'productType', CASE WHEN p."HasVariants" THEN 'variant' ELSE 'simple' END,
            'categoryId', p."CategoryId",
            'categoryName', c."Name",
            'categoryDeleted', c."IsDeleted",
            'sku', p."SKU",
            'legacyUnit', p."Unit",
            'trackStock', p."TrackStock",
            'allowBackorder', p."AllowBackorder",
            'createdAt', p."CreatedAt",
            'updatedAt', p."UpdatedAt",
            'projectionMutationVersion', p."ProjectionMutationVersion",
            'units', COALESCE((
                SELECT jsonb_agg(jsonb_build_object(
                    'productUnitId', pu."Id",
                    'unitId', pu."UnitId",
                    'unitName', u."Name",
                    'unitSymbol', u."Symbol",
                    'unitTenantId', u."TenantId",
                    'unitActive', u."IsActive",
                    'unitDeleted', u."IsDeleted",
                    'productUnitDeleted', pu."IsDeleted",
                    'isDefault', pu."IsDefault",
                    'isBaseUnit', pu."IsBaseUnit",
                    'conversionFactor', pu."ConversionFactor",
                    'sellingPrice', pu."SellingPrice",
                    'purchasePrice', pu."PurchasePrice"
                ) ORDER BY pu."IsDefault" DESC, pu."Id")
                FROM "ProductUnits" pu
                JOIN "Units" u ON u."Id" = pu."UnitId"
                WHERE pu."ProductId" = p."Id"
            ), '[]'::jsonb),
            'variants', COALESCE((
                SELECT jsonb_agg(jsonb_build_object(
                    'variantId', pv."Id",
                    'tenantId', pv."TenantId",
                    'sku', pv."SKU",
                    'isDefault', pv."IsDefault",
                    'isActive', pv."IsActive",
                    'isDeleted', pv."IsDeleted",
                    'price', pv."Price",
                    'onlinePrice', pv."OnlinePrice",
                    'semanticLifecycleState', pv."SemanticLifecycleState",
                    'createdAt', pv."CreatedAt",
                    'updatedAt', pv."UpdatedAt",
                    'projectionMutationVersion', pv."ProjectionMutationVersion",
                    'attributeCount', (SELECT count(*) FROM "ProductVariantAttributes" pva WHERE pva."VariantId" = pv."Id" AND NOT pva."IsDeleted")
                ) ORDER BY pv."IsDefault" DESC, pv."CreatedAt")
                FROM "ProductVariants" pv
                WHERE pv."ProductId" = p."Id"
            ), '[]'::jsonb)
        )::text
        FROM "Products" p
        JOIN "Tenants" t ON t."Id" = p."TenantId"
        LEFT JOIN "Categories" c ON c."Id" = p."CategoryId"
        WHERE p."TenantId" = @tenantId
        ORDER BY p."CreatedAt" DESC;
        """;
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);
    await using var reader = await command.ExecuteReaderAsync();
    Console.WriteLine("=== POSTGRES_PRODUCTS ===");
    while (await reader.ReadAsync())
    {
        Console.WriteLine(reader.GetString(0));
    }
}

var mongoClient = new MongoClient(mongoConnection);
var database = mongoClient.GetDatabase(mongoDatabase);
var products = database.GetCollection<BsonDocument>(mongoCollection);
var tenantValues = new BsonArray { tenantId.ToString(), new BsonBinaryData(tenantId, GuidRepresentation.Standard) };
var mongoFilter = new BsonDocument("TenantId", new BsonDocument("$in", tenantValues));
var mongoDocuments = await products.Find(mongoFilter)
    .Sort(new BsonDocument("CreatedAtUtc", -1))
    .ToListAsync();
Console.WriteLine("=== MONGO_PRODUCTS ===");
foreach (var document in mongoDocuments)
{
    var selected = new BsonDocument();
    foreach (var field in new[]
    {
        "_id", "TenantId", "ProductId", "Name", "Slug", "HasVariants", "IsActive",
        "IsOnlineVisible", "IsSearchable", "IsDeleted", "CategoryId", "CategoryName",
        "SKU", "DefaultUnit", "Units", "Variants", "StockQuantity", "CreatedAtUtc",
        "SyncedAtUtc", "LastEventOccurredAtUtc", "LastEventVersion", "ProductGeneration",
        "ReadModelSchemaVersion", "ProjectionVersion", "IsTombstone"
    })
    {
        if (document.TryGetValue(field, out var value))
        {
            selected[field] = value;
        }
    }
    Console.WriteLine(selected.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson }));
}

var redis = await ConnectionMultiplexer.ConnectAsync(redisConnection);
var redisServer = redis.GetServer(redis.GetEndPoints().Single());
Console.WriteLine("=== REDIS_STOREFRONT_PRODUCT_KEYS ===");
await foreach (var key in redisServer.KeysAsync(pattern: $"{redisInstance}*{tenantId:N}*product*", pageSize: 1000))
{
    var db = redis.GetDatabase();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        key = key.ToString(),
        type = (await db.KeyTypeAsync(key)).ToString(),
        ttlSeconds = (await db.KeyTimeToLiveAsync(key))?.TotalSeconds
    }));
}

return 0;
