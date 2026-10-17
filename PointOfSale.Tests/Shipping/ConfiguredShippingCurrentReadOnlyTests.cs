using MongoDB.Driver;
using Npgsql;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.MongoDB.Documents;
using Xunit.Abstractions;

namespace PointOfSale.Tests.Shipping;

/// <summary>Observation only. Never provisions, migrates, saves, repairs or retries configured history.</summary>
public sealed class ConfiguredShippingCurrentReadOnlyTests(ITestOutputHelper output)
{
    [CurrentShippingReadOnlyFact]
    public async Task ConfiguredStores_ExposeCurrentSchemaAndRetainedHistoricalShipmentWithoutWrites()
    {
        var cs = new NpgsqlConnectionStringBuilder(Required("SHIPPING_CONFIGURED_READONLY_CONNECTION"));
        Assert.Equal("192.168.1.5", cs.Host); Assert.Equal(5432, cs.Port);
        Assert.Equal("PosEcommerceDb_Baseline", cs.Database); Assert.True(string.IsNullOrEmpty(cs.SearchPath));
        var tenant = Guid.Parse(Required("SHIPPING_CURRENT_READONLY_TENANT_ID"));
        var shipmentId = Guid.Parse(Required("SHIPPING_CURRENT_READONLY_SHIPMENT_ID"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var connection = new NpgsqlConnection(cs.ConnectionString);
        await connection.OpenAsync(deadline.Token);
        await using var tx = await connection.BeginTransactionAsync(deadline.Token);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, tx))
            await readOnly.ExecuteNonQueryAsync(deadline.Token);
        await using (var setting = new NpgsqlCommand("SELECT current_setting('transaction_read_only')", connection, tx))
            Assert.Equal("on", await setting.ExecuteScalarAsync(deadline.Token));
        await using (var migrations = new NpgsqlCommand("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"", connection, tx))
        await using (var reader = await migrations.ExecuteReaderAsync(deadline.Token))
        {
            var ids = new List<string>();
            while (await reader.ReadAsync(deadline.Token)) ids.Add(reader.GetString(0));
            output.WriteLine($"Applied migration count={ids.Count}; latest={ids.LastOrDefault()}");
        }
        await using (var schema = new NpgsqlCommand("""
            SELECT count(*) FROM information_schema.columns WHERE table_schema='public'
                AND table_name='AddressAdmissionEvidence' AND column_name='CanonicalAddressEvidenceJson'
            """, connection, tx))
            output.WriteLine($"Current-source canonical address column present={Convert.ToInt64(await schema.ExecuteScalarAsync(deadline.Token)) == 1}");
        await using (var observations = new NpgsqlCommand("""
            SELECT count(*), count(*) FILTER (WHERE "OriginSnapshotJson" IS NOT NULL),
                count(*) FILTER (WHERE "ParcelEvidenceJson" IS NOT NULL),
                count(*) FILTER (WHERE "CarrierRequestSnapshotJson" IS NOT NULL),
                count(*) FILTER (WHERE "ExternalRegistrationState"='ReconciliationRequired'),
                count(*) FILTER (WHERE "OriginGeographicSnapshotVersion"=2)
            FROM "Shipments" WHERE "TenantId"=@tenant
            """, connection, tx))
        {
            observations.Parameters.AddWithValue("tenant", tenant);
            await using var reader = await observations.ExecuteReaderAsync(deadline.Token);
            Assert.True(await reader.ReadAsync(deadline.Token));
            output.WriteLine($"Historical rows={reader.GetInt64(0)}; origin payloads={reader.GetInt64(1)}; parcels={reader.GetInt64(2)}; request payloads={reader.GetInt64(3)}; reconciliation states={reader.GetInt64(4)}; canonical-v2 origins={reader.GetInt64(5)}");
        }
        Guid orderId; string shipmentStatus; string orderStatus; string paymentStatus;
        await using (var order = new NpgsqlCommand("""
            SELECT s."OrderId", s."Status"::text, o."Status"::text, o."PaymentStatus"::text,
                s."CarrierCode", s."ExternalRegistrationAttemptCount", s."CodCollected"
            FROM "Shipments" s JOIN "Orders" o ON o."Id"=s."OrderId" AND o."TenantId"=s."TenantId"
            WHERE s."Id"=@shipment AND s."TenantId"=@tenant AND NOT s."IsDeleted" AND NOT o."IsDeleted"
            """, connection, tx))
        {
            order.Parameters.AddWithValue("shipment", shipmentId); order.Parameters.AddWithValue("tenant", tenant);
            await using var reader = await order.ExecuteReaderAsync(deadline.Token);
            Assert.True(await reader.ReadAsync(deadline.Token));
            orderId = reader.GetGuid(0); shipmentStatus = Enum.Parse<ShipmentStatus>(reader.GetString(1)).ToString();
            orderStatus = Enum.Parse<OrderStatus>(reader.GetString(2)).ToString();
            paymentStatus = Enum.Parse<PaymentStatus>(reader.GetString(3)).ToString();
            Assert.Equal("manual", reader.GetString(4)); Assert.Equal(0, reader.GetInt32(5)); Assert.False(reader.GetBoolean(6));
        }
        var mongoCs = Required("SHIPPING_CONFIGURED_READONLY_MONGO_CONNECTION");
        var mongoUrl = new MongoUrl(mongoCs);
        Assert.Single(mongoUrl.Servers); Assert.Equal("192.168.109.128", mongoUrl.Server.Host); Assert.Equal(27017, mongoUrl.Server.Port);
        var projection = await new MongoClient(mongoCs).GetDatabase("ecommerce_read_db").GetCollection<OrderDocument>("orders")
            .Find(o => o.OrderId == orderId && o.TenantId == tenant).SingleAsync(deadline.Token);
        Assert.Equal(orderStatus, projection.Status); Assert.Equal(shipmentStatus, projection.ShippingStatus);
        Assert.Equal(paymentStatus, projection.PaymentStatus);
        output.WriteLine($"Historical Manual SQL/Mongo status={orderStatus}/{shipmentStatus}/{paymentStatus}; no external attempt or COD collection. This is not a fresh lifecycle or PostgreSQL write/concurrency certificate.");
        await tx.RollbackAsync(deadline.Token);
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Explicit configured read-only observation required: {name}");
}

public sealed class CurrentShippingReadOnlyFactAttribute : FactAttribute
{
    public CurrentShippingReadOnlyFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SHIPPING_CURRENT_READONLY_SHIPMENT_ID") == null)
            Skip = "Opt-in read-only configured-store observation; no fallback fixture.";
    }
}
