using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.MongoDB.Documents;
using StackExchange.Redis;

namespace PointOfSale.Tests.Shipping;

/// <summary>Opt-in observer/precise cleanup of the normal browser-created, unpaid Manual order manifest.</summary>
public sealed class ConfiguredManualFinalLifecycleTests
{
    [ConfiguredSyntheticCleanupFact]
    public async Task CleanupOnlyNewManifestedCartRecoverySideEffects()
    {
        using var noApi = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        Assert.NotNull(await Record.ExceptionAsync(() => noApi.GetAsync("http://localhost:5010/health/live")));
        var evidence = Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!;
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var c = new NpgsqlConnection(db.Database.GetConnectionString()); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        var deleted = new List<object>();
        foreach (var path in Directory.GetFiles(evidence, "manual-*-manifest.json"))
        {
            using var m = JsonDocument.Parse(await File.ReadAllTextAsync(path)); var r = m.RootElement;
            if (r.GetProperty("checkoutSessionPreexisting").GetBoolean()) continue;
            await using var q = new NpgsqlCommand("DELETE FROM \"AbandonedCarts\" WHERE \"TenantId\"=@tenant AND \"CartId\"=@cart AND \"RecoveredOrderId\"=@order RETURNING \"Id\"", c, tx);
            q.Parameters.AddWithValue("tenant", r.GetProperty("tenantId").GetGuid()); q.Parameters.AddWithValue("cart", r.GetProperty("cartId").GetGuid()); q.Parameters.AddWithValue("order", r.GetProperty("orderId").GetGuid());
            await using var reader = await q.ExecuteReaderAsync(); while (await reader.ReadAsync()) deleted.Add(new { Id = reader.GetGuid(0), Order = r.GetProperty("orderId").GetGuid() });
        }
        await tx.CommitAsync(); await File.WriteAllTextAsync(Path.Combine(evidence, "new-manifested-cart-cleanup.json"), JsonSerializer.Serialize(deleted));
    }

    [ConfiguredManualCleanupObserverFact]
    public async Task AllManifestedManualOrders_IndependentSqlMongoAndRedisKeyObservation()
    {
        var evidence = Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!;
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var c = new NpgsqlConnection(db.Database.GetConnectionString()); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", c, tx)) await ro.ExecuteNonQueryAsync();
        var mongo = await ConfiguredMongo(); var observed = new List<object>(); var allIds = new List<Guid>();
        foreach (var path in Directory.GetFiles(evidence, "manual-*-manifest.json"))
        {
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(path)); var m = manifest.RootElement;
            var order = m.GetProperty("orderId").GetGuid(); var shipment = m.GetProperty("shipmentId").GetGuid();
            var related = await RelatedRows(c, tx, order, shipment);
            if (m.GetProperty("checkoutSessionPreexisting").GetBoolean())
            {
                Assert.All(related.Keys, table => Assert.Contains(table, new[] { "AbandonedCarts", "CheckoutSessions" }));
                Assert.Equal(m.GetProperty("checkoutSessionId").GetGuid().ToString(), Assert.Single(related["CheckoutSessions"]));
                await using var preserved = new NpgsqlCommand("SELECT \"Id\"::text FROM \"AbandonedCarts\" WHERE \"TenantId\"=@tenant AND \"CartId\"=@cart AND \"RecoveredOrderId\"=@order", c, tx);
                preserved.Parameters.AddWithValue("tenant", m.GetProperty("tenantId").GetGuid()); preserved.Parameters.AddWithValue("cart", m.GetProperty("cartId").GetGuid()); preserved.Parameters.AddWithValue("order", order);
                if (related.TryGetValue("AbandonedCarts", out var carts)) Assert.Equal((string)(await preserved.ExecuteScalarAsync())!, Assert.Single(carts));
            }
            else Assert.Empty(related);
            Assert.Empty(await RelatedMongo(mongo.Database, order));
            allIds.Add(order); if (shipment != Guid.Empty) allIds.Add(shipment);
            observed.Add(new { Order = order, Shipment = shipment, RemainingSql = related, MongoRelatedRows = 0 });
        }
        await tx.RollbackAsync();
        var config = NormalConfiguration();
        using var redis = await ConnectionMultiplexer.ConnectAsync(config["Redis:ConnectionString"] ?? throw new InvalidOperationException("Normal Redis configuration absent"));
        var keys = new List<string>(); using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        foreach (var endpoint in redis.GetEndPoints())
        foreach (var id in allIds.Distinct())
        foreach (var text in new[] { id.ToString("D"), id.ToString("N") })
            await foreach (var key in redis.GetServer(endpoint).KeysAsync(redis.GetDatabase().Database, $"*{text}*", pageSize: 100).WithCancellation(budget.Token)) keys.Add(key.ToString());
        await File.WriteAllTextAsync(Path.Combine(evidence, "all-manual-cleanup-independent.json"), JsonSerializer.Serialize(new { Orders = observed, RedisKeysContainingManifestedOrderShipmentIds = keys.Distinct().ToArray(), RedisValuesNotScanned = true, BrokerQueueBodiesNotInspected = true, ObservedUtc = DateTime.UtcNow }));
        Assert.Empty(keys);
    }

    [ConfiguredCodPendingCutoffFact]
    public async Task NormalCodOrder_RemainsUnpaidPendingBeyondWorkerGrace_ReadOnly()
    {
        var evidence = Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!;
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, "manual-runtime-manifest.json")));
        var order = manifest.RootElement.GetProperty("orderId").GetGuid(); var tenant = manifest.RootElement.GetProperty("tenantId").GetGuid();
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var c = new NpgsqlConnection(db.Database.GetConnectionString()); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", c, tx)) await ro.ExecuteNonQueryAsync();
        await using var q = new NpgsqlCommand("SELECT \"Status\",\"PaymentStatus\",\"IsPaid\",\"CreatedAt\",(SELECT count(*) FROM \"PaymentIntents\" p WHERE p.\"TenantId\"=@tenant AND p.\"OrderId\"=@order),(SELECT count(*) FROM \"Shipments\" s WHERE s.\"TenantId\"=@tenant AND s.\"OrderId\"=@order) FROM \"Orders\" WHERE \"Id\"=@order AND \"TenantId\"=@tenant AND \"PaymentMethodCode\"='COD' AND \"GuestName\" LIKE 'SYNTHETIC SHIPPING FINAL%'", c, tx);
        q.Parameters.AddWithValue("order", order); q.Parameters.AddWithValue("tenant", tenant);
        double age;
        await using (var r = await q.ExecuteReaderAsync())
        {
            Assert.True(await r.ReadAsync()); Assert.Equal((int)OrderStatus.Pending, r.GetInt32(0)); Assert.Equal((int)PaymentStatus.Pending, r.GetInt32(1));
            Assert.False(r.GetBoolean(2)); age = (DateTime.UtcNow - r.GetDateTime(3)).TotalSeconds; Assert.True(age >= 150, $"Worker grace observation requires >=150s; actual={age}");
            Assert.Equal(0L, r.GetInt64(4)); Assert.Equal(0L, r.GetInt64(5));
        }
        await tx.RollbackAsync();
        await File.WriteAllTextAsync(Path.Combine(evidence, "manual-cod-worker-cutoff.json"), JsonSerializer.Serialize(new { Order = order, AgeSeconds = age, Status = "Pending", PaymentStatus = "Pending", IsPaid = false, PaymentIntents = 0, Shipments = 0, ReadOnly = true, ObservedUtc = DateTime.UtcNow }));
    }

    [ConfiguredManualFinalFact]
    public async Task InspectOnlyExactManifestedRuntimeOrderAndDependentRows_ReadOnly()
    {
        var evidence = Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!;
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, "manual-runtime-manifest.json")));
        var order = manifest.RootElement.GetProperty("orderId").GetGuid();
        var tenant = manifest.RootElement.GetProperty("tenantId").GetGuid();
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var c = new NpgsqlConnection(db.Database.GetConnectionString()); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", c, tx)) await ro.ExecuteNonQueryAsync();
        var rows = new Dictionary<string, string[]>();
        foreach (var (table, column) in new[] { ("Orders", "Id"), ("OrderItems", "OrderId"), ("Shipments", "OrderId"), ("InventoryReservations", "OrderId"),
            ("CheckoutSessions", "ConvertedOrderId"), ("WarehouseResolutions", "ConvertedOrderId"), ("PaymentIntents", "OrderId"),
            ("PaymentCancellationOutboxMessages", "OrderId"), ("AccountingOutboxMessages", "BusinessEntityId"), ("LedgerJournalEntries", "SourceId") })
        {
            var scope = table == "OrderItems" ? "EXISTS (SELECT 1 FROM \"Orders\" o WHERE o.\"Id\"=t.\"OrderId\" AND o.\"TenantId\"=@tenant)" : "\"TenantId\"=@tenant";
            await using var q = new NpgsqlCommand($"SELECT to_jsonb(t)::text FROM \"{table}\" t WHERE {scope} AND \"{column}\"=@order", c, tx);
            q.Parameters.AddWithValue("tenant", tenant); q.Parameters.AddWithValue("order", order);
            await using var r = await q.ExecuteReaderAsync(); var items = new List<string>();
            while (await r.ReadAsync()) items.Add(r.GetString(0)); rows[table] = items.ToArray();
        }
        await File.WriteAllTextAsync(Path.Combine(evidence, "manual-runtime-sql-private.json"), JsonSerializer.Serialize(rows));
        var related = await RelatedRows(c, tx, order, manifest.RootElement.GetProperty("shipmentId").GetGuid());
        await File.WriteAllTextAsync(Path.Combine(evidence, "manual-related-rows.json"), JsonSerializer.Serialize(related));
        await tx.RollbackAsync();
        var mongo = await ConfiguredMongo();
        await File.WriteAllTextAsync(Path.Combine(evidence, "manual-related-mongo.json"), JsonSerializer.Serialize(await RelatedMongo(mongo.Database, order)));
        Assert.Single(rows["Orders"]);
    }

    [ConfiguredManualFinalFact]
    public async Task FreshNormalBrowserManualOrder_VerifyRelationshipsAndUnpaidFinancialBoundary_OrExactCleanup()
    {
        var evidence = Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!;
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, "manual-runtime-manifest.json")));
        var m = manifest.RootElement; var tenant = m.GetProperty("tenantId").GetGuid(); var order = m.GetProperty("orderId").GetGuid();
        var shipment = m.GetProperty("shipmentId").GetGuid();
        var cleanup = Environment.GetEnvironmentVariable("SHIPPING_FINAL_MANUAL_CLEANUP") == "1";
        if (cleanup)
        {
            using var noApi = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            Assert.NotNull(await Record.ExceptionAsync(() => noApi.GetAsync("http://localhost:5010/health/live")));
        }
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var c = new NpgsqlConnection(configured.Database.GetConnectionString()); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.Serializable);
        if (!cleanup) { await using var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", c, tx); await ro.ExecuteNonQueryAsync(); }
        async Task<long> Count(string table, string column, Guid id)
        {
            await using var q = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\" WHERE \"TenantId\"=@tenant AND \"{column}\"=@id", c, tx);
            q.Parameters.AddWithValue("tenant", tenant); q.Parameters.AddWithValue("id", id);
            return (long)(await q.ExecuteScalarAsync())!;
        }
        await using (var q = new NpgsqlCommand("SELECT o.\"Total\",o.\"ShippingCost\",o.\"PaymentStatus\",o.\"IsPaid\",o.\"OnlineCustomerId\",s.\"Status\",s.\"CarrierCode\",s.\"ExternalRegistrationAttemptCount\",s.\"CodCollected\",o.\"GuestName\",s.\"OrderId\",o.\"ShippingAddressAdmissionEvidenceId\",s.\"DestinationAddressAdmissionEvidenceId\" FROM \"Orders\" o LEFT JOIN \"Shipments\" s ON s.\"OrderId\"=o.\"Id\" AND s.\"TenantId\"=o.\"TenantId\" WHERE o.\"Id\"=@order AND o.\"TenantId\"=@tenant AND (s.\"Id\"=@shipment OR (@shipment='00000000-0000-0000-0000-000000000000'::uuid AND s.\"Id\" IS NULL))", c, tx))
        {
            q.Parameters.AddWithValue("tenant", tenant); q.Parameters.AddWithValue("order", order); q.Parameters.AddWithValue("shipment", shipment);
            await using var r = await q.ExecuteReaderAsync(); Assert.True(await r.ReadAsync());
            Assert.Equal(35m, r.GetDecimal(0)); Assert.Equal(10m, r.GetDecimal(1)); Assert.Equal((int)Enum.Parse<PaymentStatus>(m.TryGetProperty("paymentStatus", out var payment) ? payment.GetString()! : "Pending"), r.GetInt32(2)); Assert.False(r.GetBoolean(3));
            Assert.Equal(m.GetProperty("onlineCustomerId").GetGuid(), r.GetGuid(4));
            if (shipment == Guid.Empty)
            {
                Assert.True(cleanup); Assert.Equal("NotCreated", m.GetProperty("shipmentStatus").GetString());
                Assert.True(r.IsDBNull(5)); Assert.True(r.IsDBNull(6)); Assert.True(r.IsDBNull(7)); Assert.True(r.IsDBNull(8)); Assert.True(r.IsDBNull(10));
            }
            else
            {
                Assert.Equal(m.GetProperty("shipmentStatus").GetString(), ((ShipmentStatus)r.GetInt32(5)).ToString());
                Assert.Equal("manual", r.GetString(6)); Assert.Equal(0, r.GetInt32(7)); Assert.False(r.GetBoolean(8)); Assert.Equal(order, r.GetGuid(10));
            }
            Assert.StartsWith("SYNTHETIC SHIPPING FINAL", r.GetString(9));
            Assert.True(r.IsDBNull(11)); Assert.True(r.IsDBNull(12)); // No genuine publication exists: do not claim canonical admission.
        }
        foreach (var table in new[] { "PaymentIntents", "PaymentSettlementOrders", "CarrierRemittanceOrders", "CarrierCollectionEvidence", "CarrierCodShipmentBindings", "OrderRefunds",
            "AffiliateAttributions", "AffiliateCommissionLedger", "CouponReservations", "CouponUsages", "PromotionUsages", "CheckoutAttachments" })
            Assert.Equal(0L, await Count(table, "OrderId", order));
        Assert.Equal(0L, await Count("AccountingOutboxMessages", "BusinessEntityId", order));
        Assert.Equal(0L, await Count("LedgerJournalEntries", "SourceId", order));
        Assert.Equal(0L, await Count("InventoryReservations", "OrderId", order)); // This normal synthetic SKU is backorderable; physical-stock qualification is separate.
        if (m.TryGetProperty("paymentStatus", out var expectedPayment) && expectedPayment.GetString() == "Failed")
        {
            Assert.Equal(1L, await Count("PaymentCancellationOutboxMessages", "OrderId", order));
            await using var completed = new NpgsqlCommand("SELECT count(*) FROM \"PaymentCancellationOutboxMessages\" WHERE \"TenantId\"=@tenant AND \"OrderId\"=@order AND \"ProcessedAtUtc\" IS NOT NULL AND NOT \"IsFailed\" AND \"SkipPaymentIntentResolution\"", c, tx);
            completed.Parameters.AddWithValue("tenant", tenant); completed.Parameters.AddWithValue("order", order);
            Assert.Equal(1L, (long)(await completed.ExecuteScalarAsync())!);
        }
        if (m.GetProperty("shipmentStatus").GetString() == "Cancelled")
        {
            await using var local = new NpgsqlCommand("SELECT \"Outcome\",\"ConfirmationBasis\",\"ConfirmedAtUtc\" FROM \"ShipmentCancellationOperations\" WHERE \"TenantId\"=@tenant AND \"OrderId\"=@order AND \"ShipmentId\"=@shipment", c, tx);
            local.Parameters.AddWithValue("tenant", tenant); local.Parameters.AddWithValue("order", order); local.Parameters.AddWithValue("shipment", shipment);
            await using var r = await local.ExecuteReaderAsync(); Assert.True(await r.ReadAsync());
            Assert.Equal("Confirmed", r.GetString(0)); Assert.Equal("LocalOnly", r.GetString(1)); Assert.False(r.IsDBNull(2)); Assert.False(await r.ReadAsync());
        }
        if (cleanup)
        {
            // This branch cannot run without the exact verified runtime manifest.
            // Delete only nonfinancial rows belonging to this new test order. Never
            // change mappings, providers, stock balances, canonical retention or history.
            foreach (var (table, column, id) in new[] {
                ("ShipmentCancellationOperations", "ShipmentId", shipment),
                ("Shipments", "Id", shipment),
                 ("WarehouseResolutions", "ConvertedOrderId", order),
                 ("PaymentCancellationOutboxMessages", "OrderId", order),
                 ("OrderRequestIdempotency", "OrderId", order),
                 ("ProcessedEvents", "EntityId", order),
                 ("ProcessedEvents", "EntityId", shipment),
                ("MarketplaceEventOutboxMessages", "EntityId", order),
                ("MarketplaceProjectionOutboxMessages", "EntityId", order),
                ("OutboxMessages", "AggregateId", order),
                ("OutboxMessages", "AggregateId", shipment),
                ("OrderCheckoutFieldSnapshots", "OrderId", order),
                ("OrderItems", "OrderId", order),
                 ("Orders", "Id", order) })
            {
                var scope = table == "OrderItems" ? "EXISTS (SELECT 1 FROM \"Orders\" o WHERE o.\"Id\"=\"OrderItems\".\"OrderId\" AND o.\"TenantId\"=@tenant)" : "\"TenantId\"=@tenant";
                await using var cmd = new NpgsqlCommand($"DELETE FROM \"{table}\" WHERE {scope} AND \"{column}\"=@id", c, tx);
                cmd.Parameters.AddWithValue("tenant", tenant); cmd.Parameters.AddWithValue("id", id); await cmd.ExecuteNonQueryAsync();
            }
            // Never delete the preexisting synthetic draft or customer/cart history.
            var session = m.GetProperty("checkoutSessionId").GetGuid();
            var preserveSession = m.GetProperty("checkoutSessionPreexisting").GetBoolean();
            if (!preserveSession)
            {
                await using var sessionDelete = new NpgsqlCommand("DELETE FROM \"CheckoutSessions\" WHERE \"Id\"=@session AND \"TenantId\"=@tenant AND \"ConvertedOrderId\"=@order", c, tx);
                sessionDelete.Parameters.AddWithValue("session", session); sessionDelete.Parameters.AddWithValue("tenant", tenant); sessionDelete.Parameters.AddWithValue("order", order);
                Assert.Equal(1, await sessionDelete.ExecuteNonQueryAsync());
                await using var cartDelete = new NpgsqlCommand("DELETE FROM \"AbandonedCarts\" WHERE \"TenantId\"=@tenant AND \"CartId\"=@cart AND \"RecoveredOrderId\"=@order", c, tx);
                cartDelete.Parameters.AddWithValue("tenant", tenant); cartDelete.Parameters.AddWithValue("cart", m.GetProperty("cartId").GetGuid()); cartDelete.Parameters.AddWithValue("order", order);
                await cartDelete.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            // Mongo cleanup is opt-in using the unchanged configured endpoint, never a substitute DB.
            var mongo = await ConfiguredMongo();
            await mongo.DeleteOneAsync(o => o.OrderId == order && o.TenantId == tenant);
            var relatedMongo = await RelatedMongo(mongo.Database, order);
            foreach (var collection in relatedMongo.Keys)
            {
                Assert.Contains(collection, new[] { "marketplace_order_read_models", "processed_events" });
                var owned = Builders<BsonDocument>.Filter.And(MongoOrderFilter(order), Builders<BsonDocument>.Filter.Eq("TenantId", tenant.ToString()));
                await mongo.Database.GetCollection<BsonDocument>(collection).DeleteManyAsync(owned);
            }
            Assert.Null(await mongo.Find(o => o.OrderId == order && o.TenantId == tenant).FirstOrDefaultAsync());
            Assert.Empty(await RelatedMongo(mongo.Database, order));
            await using var verify = new NpgsqlConnection(configured.Database.GetConnectionString()); await verify.OpenAsync();
            await using var zero = new NpgsqlCommand("SELECT (SELECT count(*) FROM \"Orders\" WHERE \"Id\"=@order)+(SELECT count(*) FROM \"Shipments\" WHERE \"Id\"=@shipment)+(SELECT count(*) FROM \"WarehouseResolutions\" WHERE \"ConvertedOrderId\"=@order)", verify);
            zero.Parameters.AddWithValue("order", order); zero.Parameters.AddWithValue("shipment", shipment); zero.Parameters.AddWithValue("session", session);
            Assert.Equal(0L, (long)(await zero.ExecuteScalarAsync())!);
            var remaining = await RelatedRows(verify, null, order, shipment);
            if (preserveSession)
            {
                Assert.All(remaining.Keys, table => Assert.Contains(table, new[] { "AbandonedCarts", "CheckoutSessions" }));
                Assert.Equal(session.ToString(), Assert.Single(remaining["CheckoutSessions"]));
            }
            else Assert.Empty(remaining);
            await using var kept = new NpgsqlCommand("SELECT count(*) FROM \"CheckoutSessions\" WHERE \"Id\"=@session AND \"TenantId\"=@tenant", verify);
            kept.Parameters.AddWithValue("session", session); kept.Parameters.AddWithValue("tenant", tenant); Assert.Equal(preserveSession ? 1L : 0L, (long)(await kept.ExecuteScalarAsync())!);
            await File.WriteAllTextAsync(Path.Combine(evidence, "manual-cleanup-verified.json"), JsonSerializer.Serialize(new { Order = order, Shipment = shipment, PostgreSqlZeroOrderShipmentRows = true, RelatedRemaining = remaining, PreservedPreexistingSession = preserveSession, MongoZeroRows = true, VerifiedUtc = DateTime.UtcNow }));
        }
        else
        {
            await tx.RollbackAsync();
            var mongo = await ConfiguredMongo();
            var projection = await mongo.Find(o => o.OrderId == order && o.TenantId == tenant).SingleAsync();
            Assert.Equal(m.TryGetProperty("paymentStatus", out var payment) ? payment.GetString() : "Pending", projection.PaymentStatus); Assert.Equal(35m, projection.Total); Assert.Equal(10m, projection.ShippingCost);
            Assert.Equal(m.GetProperty("shipmentStatus").GetString(), projection.ShippingStatus);
            await File.WriteAllTextAsync(Path.Combine(evidence, $"manual-{m.GetProperty("shipmentStatus").GetString()}-observed.json"), JsonSerializer.Serialize(new { Order = order, Shipment = shipment, projection.Status, projection.ShippingStatus, projection.PaymentStatus, projection.Total, projection.ShippingCost, NormalAuthenticatedCustomer = true, NoFinancialRows = true, ReadOnly = true, ObservedUtc = DateTime.UtcNow }));
        }
    }

    private static async Task<Dictionary<string, string[]>> RelatedRows(NpgsqlConnection c, NpgsqlTransaction? tx, Guid order, Guid shipment)
    {
        if (shipment == Guid.Empty) shipment = order;
        var columns = new List<(string Table, string Column)>();
        await using (var schema = new NpgsqlCommand("SELECT table_name,column_name FROM information_schema.columns WHERE table_schema='public' AND udt_name='uuid' ORDER BY table_name,column_name", c, tx))
        await using (var reader = await schema.ExecuteReaderAsync())
            while (await reader.ReadAsync()) columns.Add((reader.GetString(0), reader.GetString(1)));
        var related = new Dictionary<string, string[]>();
        foreach (var table in columns.GroupBy(x => x.Table))
        {
            var name = table.Key.Replace("\"", "\"\"");
            var predicate = string.Join(" OR ", table.Select(x => $"\"{x.Column}\" IN (@order,@shipment)"));
            await using var q = new NpgsqlCommand($"SELECT to_jsonb(t)->>'Id' FROM public.\"{name}\" t WHERE {predicate}", c, tx);
            q.Parameters.AddWithValue("order", order); q.Parameters.AddWithValue("shipment", shipment);
            await using var r = await q.ExecuteReaderAsync(); var ids = new List<string>(); while (await r.ReadAsync()) ids.Add(r.IsDBNull(0) ? "(no Id)" : r.GetString(0));
            if (ids.Count > 0) related[table.Key] = ids.ToArray();
        }
        return related;
    }

    private static FilterDefinition<BsonDocument> MongoOrderFilter(Guid order) => Builders<BsonDocument>.Filter.Or(
        new[] { "OrderId", "orderId", "EntityId", "entityId", "AggregateId", "aggregateId" }.Select(field => Builders<BsonDocument>.Filter.Eq(field, order.ToString())));

    private static async Task<Dictionary<string, string[]>> RelatedMongo(IMongoDatabase database, Guid order)
    {
        var result = new Dictionary<string, string[]>();
        using var names = await database.ListCollectionNamesAsync();
        foreach (var name in await names.ToListAsync())
        {
            var docs = await database.GetCollection<BsonDocument>(name).Find(MongoOrderFilter(order)).Project(new BsonDocument("_id", 1)).ToListAsync();
            if (docs.Count > 0) result[name] = docs.Select(d => d["_id"].ToString()!).ToArray();
        }
        return result;
    }

    private static async Task<IMongoCollection<OrderDocument>> ConfiguredMongo()
    {
        // No connection-string output, override or alternative infrastructure.
        var config = NormalConfiguration();
        var cs = config["MongoDB:ConnectionString"] ?? throw new InvalidOperationException("Normal configured Mongo endpoint unavailable");
        var url = new MongoUrl(cs); Assert.Equal("192.168.109.128", url.Server.Host);
        await Task.CompletedTask;
        var database = config["MongoDB:DatabaseName"] ?? "ecommerce_read_db"; Assert.Equal("ecommerce_read_db", database);
        return new MongoClient(cs).GetDatabase(database).GetCollection<OrderDocument>("orders");
    }

    private static IConfigurationRoot NormalConfiguration()
    {
        var root = Path.Combine(Infrastructure.TestConfiguration.GetSolutionRoot(), "PointOfSale.API");
        var builder = new Microsoft.Extensions.Configuration.ConfigurationBuilder().SetBasePath(root)
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true).AddJsonFile("appsettings.Development.local.json", true)
            .AddUserSecrets<PointOfSale.Infrastructure.Data.ApplicationDbContextFactory>(true).AddEnvironmentVariables();
        return builder.Build();
    }
}

public sealed class ConfiguredManualCleanupObserverFactAttribute : FactAttribute
{
    public ConfiguredManualCleanupObserverFactAttribute()
    { if (Environment.GetEnvironmentVariable("SHIPPING_FINAL_MANUAL_CLEANUP_OBSERVER") != "1") Skip = "Read-only independent observation after exact manifested cleanup."; }
}

public sealed class ConfiguredCodPendingCutoffFactAttribute : FactAttribute
{
    public ConfiguredCodPendingCutoffFactAttribute()
    { if (Environment.GetEnvironmentVariable("SHIPPING_FINAL_COD_PENDING_CUTOFF") != "1") Skip = "Observe the normal synthetic COD order before merchant confirmation, after the worker grace period."; }
}

public sealed class ConfiguredManualFinalFactAttribute : FactAttribute
{
    public ConfiguredManualFinalFactAttribute()
    { if (Environment.GetEnvironmentVariable("SHIPPING_FINAL_MANUAL_VERIFY") != "1") Skip = "Normal authenticated browser-created synthetic manifest required."; }
}
