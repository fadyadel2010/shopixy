using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Npgsql;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Tests.Infrastructure;
using Xunit.Abstractions;

namespace PointOfSale.Tests.Shipping;

/// <summary>Aggregate-only reconciliation. No save, dispatch, repair, provisioning or history replay.</summary>
public sealed class CrossDomainConfiguredReadOnlyTests(ITestOutputHelper output)
{
    [CrossDomainReadOnlyFact]
    public async Task Integrity_ConfiguredSqlAndMongo_ObserveRelationshipsWithoutChangingHistory()
    {
        var root = Environment.GetEnvironmentVariable("SHIPPING_CROSS_DOMAIN_EVIDENCE")!;
        var phase = Environment.GetEnvironmentVariable("SHIPPING_CROSS_DOMAIN_PHASE") ?? "initial";
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var cs = new NpgsqlConnectionStringBuilder(configured.Database.GetConnectionString());
        Assert.Equal("192.168.1.5", cs.Host); Assert.Equal("PosEcommerceDb_Baseline", cs.Database);
        await using var c = new NpgsqlConnection(cs.ConnectionString); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", c, tx)) await ro.ExecuteNonQueryAsync();
        async Task<string[][]> Query(string sql)
        {
            await using var q = new NpgsqlCommand(sql, c, tx) { CommandTimeout = 90 };
            await using var r = await q.ExecuteReaderAsync(); var rows = new List<string[]>();
            while (await r.ReadAsync()) rows.Add(Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "" : r.GetValue(i).ToString()!).ToArray());
            return rows.ToArray();
        }
        Assert.Equal("on", (await Query("SELECT current_setting('transaction_read_only')"))[0][0]);
        var history = await Query("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1");
        Assert.Equal(configured.Database.GetMigrations(), history.Select(r => r[0])); Assert.Equal(53, history.Length);
        var constraints = await Query("SELECT conrelid::regclass::text,conname,contype::text,pg_get_constraintdef(oid) FROM pg_constraint WHERE connamespace='public'::regnamespace AND conrelid::regclass::text ~ '(Orders|Shipments|Stock|Reservation|Carrier|Ledger|Treasury|Cashbox|Settlement|AddressAdmission)' ORDER BY 1,2");
        var triggers = await Query("SELECT c.relname,t.tgname,t.tgenabled::text,p.proname FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_proc p ON p.oid=t.tgfoid WHERE NOT t.tgisinternal ORDER BY 1,2");
        var checks = new Dictionary<string, string>
        {
            ["shipment_missing_or_foreign_order"] = "SELECT count(*) FROM \"Shipments\" s LEFT JOIN \"Orders\" o ON o.\"Id\"=s.\"OrderId\" AND o.\"TenantId\"=s.\"TenantId\" WHERE o.\"Id\" IS NULL",
            ["duplicate_active_shipment"] = "SELECT count(*) FROM (SELECT 1 FROM \"Shipments\" WHERE NOT \"IsDeleted\" GROUP BY \"TenantId\",\"OrderId\" HAVING count(*)>1) x",
            ["shipment_origin_warehouse_mismatch"] = "SELECT count(*) FROM \"Shipments\" s JOIN \"Orders\" o ON o.\"Id\"=s.\"OrderId\" WHERE s.\"OriginWarehouseId\" IS DISTINCT FROM o.\"WarehouseId\"",
            ["shipment_foreign_origin_warehouse"] = "SELECT count(*) FROM \"Shipments\" s LEFT JOIN \"Warehouses\" w ON w.\"Id\"=s.\"OriginWarehouseId\" AND w.\"TenantId\"=s.\"TenantId\" WHERE s.\"OriginWarehouseId\" IS NOT NULL AND w.\"Id\" IS NULL",
            ["shipment_shipping_money_mismatch"] = "SELECT count(*) FROM \"Shipments\" s JOIN \"Orders\" o ON o.\"Id\"=s.\"OrderId\" WHERE s.\"ShippingCost\"<>o.\"ShippingCost\" OR s.\"Currency\"<>o.\"Currency\" OR s.\"IsCashOnDelivery\" AND s.\"CodAmount\" IS DISTINCT FROM o.\"Total\"",
            ["stock_invalid_buckets"] = "SELECT count(*) FROM \"ProductVariantStocks\" WHERE \"Quantity\"<0 OR \"ReservedQuantity\"<0 OR \"ReservedQuantity\">\"Quantity\" OR \"DamagedQuantity\"<0",
            ["stock_foreign_variant_or_warehouse"] = "SELECT count(*) FROM \"ProductVariantStocks\" s LEFT JOIN \"ProductVariants\" v ON v.\"Id\"=s.\"VariantId\" AND v.\"TenantId\"=s.\"TenantId\" LEFT JOIN \"Warehouses\" w ON w.\"Id\"=s.\"WarehouseId\" AND w.\"TenantId\"=s.\"TenantId\" WHERE v.\"Id\" IS NULL OR w.\"Id\" IS NULL",
            ["reservation_missing_order_or_line"] = "SELECT count(*) FROM \"InventoryReservations\" r LEFT JOIN \"Orders\" o ON o.\"Id\"=r.\"OrderId\" AND o.\"TenantId\"=r.\"TenantId\" LEFT JOIN \"OrderItems\" i ON i.\"Id\"=r.\"OrderItemId\" AND i.\"OrderId\"=r.\"OrderId\" WHERE o.\"Id\" IS NULL OR i.\"Id\" IS NULL",
            ["reservation_bound_lineage_mismatch"] = "SELECT count(*) FROM \"InventoryReservations\" r JOIN \"OrderItems\" i ON i.\"Id\"=r.\"OrderItemId\" WHERE r.\"OrderId\"<>i.\"OrderId\" OR r.\"ProductId\"<>i.\"ProductId\" OR r.\"VariantId\" IS DISTINCT FROM i.\"VariantId\" OR r.\"WarehouseId\" IS DISTINCT FROM i.\"WarehouseId\"",
            ["stock_reservation_open_balance_mismatch"] = "SELECT count(*) FROM \"ProductVariantStocks\" s LEFT JOIN (SELECT \"TenantId\",\"VariantId\",\"WarehouseId\",sum(\"ReservedQuantity\"-\"CommittedQuantity\"-\"ReleasedQuantity\") q FROM \"InventoryReservations\" WHERE NOT \"IsDeleted\" GROUP BY 1,2,3) r USING (\"TenantId\",\"VariantId\",\"WarehouseId\") WHERE NOT s.\"IsDeleted\" AND s.\"ReservedQuantity\"<>coalesce(r.q,0)",
            ["expired_open_reservations"] = "SELECT count(*) FROM \"InventoryReservations\" WHERE NOT \"IsDeleted\" AND \"ExpiresAtUtc\"<now() AND \"ReservedQuantity\">\"CommittedQuantity\"+\"ReleasedQuantity\"",
            ["settlement_duplicate_order_membership"] = "SELECT count(*) FROM (SELECT 1 FROM \"PaymentSettlementOrders\" GROUP BY \"TenantId\",\"OrderId\" HAVING count(*)>1) x",
            ["settlement_foreign_order_or_batch"] = "SELECT count(*) FROM \"PaymentSettlementOrders\" m LEFT JOIN \"Orders\" o ON o.\"Id\"=m.\"OrderId\" AND o.\"TenantId\"=m.\"TenantId\" LEFT JOIN \"PaymentSettlementBatches\" b ON b.\"Id\"=m.\"SettlementBatchId\" AND b.\"TenantId\"=m.\"TenantId\" WHERE o.\"Id\" IS NULL OR b.\"Id\" IS NULL",
            ["ledger_cash_clearing_balance_mismatch"] = "SELECT count(*) FROM \"LedgerAccounts\" a LEFT JOIN (SELECT \"TenantId\",\"AccountId\",sum(\"DebitAmount\"-\"CreditAmount\") balance FROM \"LedgerLines\" WHERE NOT \"IsDeleted\" GROUP BY 1,2) l ON l.\"AccountId\"=a.\"Id\" AND l.\"TenantId\"=a.\"TenantId\" WHERE NOT a.\"IsDeleted\" AND a.\"AccountCode\" IN ('1010','1130') AND a.\"CurrentBalance\"<>coalesce(l.balance,0)",
            ["journal_unbalanced"] = "SELECT count(*) FROM (SELECT 1 FROM \"LedgerLines\" WHERE NOT \"IsDeleted\" GROUP BY \"TenantId\",\"JournalEntryId\" HAVING sum(\"DebitAmount\")<>sum(\"CreditAmount\")) x",
            ["treasury_foreign_ledger_account"] = "SELECT count(*) FROM \"TreasuryAccounts\" t LEFT JOIN \"LedgerAccounts\" a ON a.\"Id\"=t.\"AccountId\" AND a.\"TenantId\"=t.\"TenantId\" WHERE a.\"Id\" IS NULL"
        };
        var observations = new Dictionary<string, string[][]>();
        // A bad observer query is retained as evidence, never converted to zero.
        foreach (var (name, sql) in checks) observations[name] = await Query(sql);
        var shipmentStates = await Query("SELECT s.\"CarrierCode\",s.\"Status\"::text,o.\"Status\"::text,o.\"FulfillmentStatus\"::text,o.\"PaymentStatus\"::text,s.\"CodCollected\"::text,count(*)::text FROM \"Shipments\" s JOIN \"Orders\" o ON o.\"Id\"=s.\"OrderId\" AND o.\"TenantId\"=s.\"TenantId\" GROUP BY 1,2,3,4,5,6 ORDER BY 1,2,3");
        var carrierStates = await Query("SELECT \"CarrierCode\",\"State\"::text,\"OperationalState\"::text,count(*)::text FROM \"CarrierAccounts\" WHERE \"CarrierCode\" IN ('bosta','aramex','dhl','mylerz') GROUP BY 1,2,3 ORDER BY 1,2");
        var connectionStates = await Query("SELECT a.\"CarrierCode\",r.\"Status\"::text,r.\"TestedAtUtc\"::text FROM \"CarrierConnectionTestRecords\" r JOIN \"CarrierAccounts\" a ON a.\"Id\"=r.\"CarrierAccountId\" AND a.\"TenantId\"=r.\"TenantId\" WHERE a.\"CarrierCode\" IN ('bosta','aramex','dhl','mylerz') ORDER BY 1,3");
        var outbox = await Query("SELECT \"EventType\",count(*)::text,count(*) FILTER(WHERE \"ProcessedAtUtc\" IS NULL)::text,count(*) FILTER(WHERE \"ProcessedAtUtc\" IS NULL AND \"RetryCount\">=5)::text,max(\"RetryCount\")::text FROM \"OutboxMessages\" GROUP BY 1 ORDER BY 1");
        var financialCounts = await Query("SELECT (SELECT count(*) FROM \"CarrierCollectionEvidence\")::text,(SELECT count(*) FROM \"CarrierCodShipmentBindings\")::text,(SELECT count(*) FROM \"CarrierRemittances\")::text,(SELECT count(*) FROM \"PaymentSettlementBatches\")::text,(SELECT count(*) FROM \"LedgerJournalEntries\")::text");
        var commerceOrderTable = configured.Model.FindEntityType(typeof(PointOfSale.Core.Entities.CommerceHubMarketplaceOrder))!.GetTableName()!;
        var commerceLineTable = configured.Model.FindEntityType(typeof(PointOfSale.Core.Entities.CommerceHubSubOrderItem))!.GetTableName()!;
        var commerceSubOrderTable = configured.Model.FindEntityType(typeof(PointOfSale.Core.Entities.CommerceHubSubOrder))!.GetTableName()!;
        var reservationClassification = await Query($"SELECT (o.\"Id\" IS NULL)::text,(i.\"Id\" IS NULL)::text,(ch.\"Id\" IS NOT NULL)::text,r.\"Status\"::text,r.\"IsDeleted\"::text,count(*)::text,(ci.\"Id\" IS NOT NULL AND cv.\"Id\" IS NOT NULL)::text FROM \"InventoryReservations\" r LEFT JOIN \"Orders\" o ON o.\"Id\"=r.\"OrderId\" AND o.\"TenantId\"=r.\"TenantId\" LEFT JOIN \"OrderItems\" i ON i.\"Id\"=r.\"OrderItemId\" AND i.\"OrderId\"=r.\"OrderId\" LEFT JOIN \"{commerceOrderTable}\" ch ON ch.\"Id\"=r.\"OrderId\" LEFT JOIN \"{commerceSubOrderTable}\" co ON co.\"Id\"=r.\"SubOrderId\" AND co.\"MarketplaceOrderId\"=ch.\"Id\" LEFT JOIN \"{commerceLineTable}\" ci ON ci.\"Id\"=r.\"OrderItemId\" AND ci.\"SubOrderId\"=co.\"Id\" AND ci.\"ProductId\"=r.\"ProductId\" AND ci.\"VariantId\"=r.\"VariantId\" LEFT JOIN \"ProductVariants\" cv ON cv.\"Id\"=ci.\"VariantId\" AND cv.\"TenantId\"=r.\"TenantId\" GROUP BY 1,2,3,4,5,7 ORDER BY 1,2,3,4");
        var nullableEvidence = await Query("SELECT count(*)::text,count(*) FILTER(WHERE s.\"OriginWarehouseId\" IS NULL)::text,count(*) FILTER(WHERE o.\"WarehouseId\" IS NULL)::text,count(*) FILTER(WHERE s.\"DestinationAddressAdmissionEvidenceId\" IS NULL)::text,count(*) FILTER(WHERE s.\"OriginSnapshotJson\" IS NOT NULL)::text,count(*) FILTER(WHERE s.\"CarrierRequestSnapshotJson\" IS NOT NULL)::text FROM \"Shipments\" s JOIN \"Orders\" o ON o.\"Id\"=s.\"OrderId\" AND o.\"TenantId\"=s.\"TenantId\"");
        var configurations = await Query("SELECT \"CarrierCode\",\"IsEnabled\"::text,\"SupportsCod\"::text,\"SupportsWebhooks\"::text,count(*)::text FROM \"CarrierConfigurations\" WHERE \"CarrierCode\" IN ('bosta','aramex','dhl','mylerz') GROUP BY 1,2,3,4 ORDER BY 1,2");
        var projections = await Query("SELECT o.\"Id\"::text,o.\"TenantId\"::text,o.\"Status\"::text,o.\"PaymentStatus\"::text,s.\"Status\"::text,o.\"Total\"::text,o.\"ShippingCost\"::text,o.\"FulfillmentStatus\"::text FROM \"Orders\" o JOIN \"Shipments\" s ON s.\"OrderId\"=o.\"Id\" AND s.\"TenantId\"=o.\"TenantId\" WHERE NOT o.\"IsDeleted\" AND NOT s.\"IsDeleted\"");
        await File.WriteAllTextAsync(Path.Combine(root, $"current-sql-reconciliation-{phase}.json"), JsonSerializer.Serialize(new {
            ObservedUtc = DateTime.UtcNow, ReadOnly = true, MigrationCount = history.Length, Observations = observations,
            Constraints = constraints, Triggers = triggers, ShipmentStates = shipmentStates, CarrierStates = carrierStates,
            ConnectionStates = connectionStates, Outbox = outbox, FinancialCounts = financialCounts }, new JsonSerializerOptions { WriteIndented = true }));
        var configuration = new ConfigurationBuilder().SetBasePath(Path.Combine(TestConfiguration.GetSolutionRoot(), "PointOfSale.API"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true)
            .AddJsonFile("appsettings.Development.local.json", true)
            .AddUserSecrets<PointOfSale.Infrastructure.Data.ApplicationDbContextFactory>(true).AddEnvironmentVariables().Build();
        var mongoConnection = configuration["MongoDB:ConnectionString"]!;
        var mongoUrl = new MongoUrl(mongoConnection);
        Assert.Equal("192.168.109.128", mongoUrl.Server.Host);
        var mongo = new MongoClient(mongoConnection).GetDatabase(configuration["MongoDB:DatabaseName"] ?? "ecommerce_read_db");
        var mismatches = new Dictionary<string, int>();
        var mismatchFields = new Dictionary<string, int>();
        foreach (var row in projections)
        {
            var docs = await mongo.GetCollection<OrderDocument>("orders").Find(o => o.OrderId == Guid.Parse(row[0]) && o.TenantId == Guid.Parse(row[1])).ToListAsync();
            // Production projection deliberately gives canonical terminal fulfillment
            // precedence over historical shipment visibility. Comparing raw labels
            // would falsely classify those authorized overrides as projection drift.
            var expectedShipping = Enum.Parse<FulfillmentStatus>(row[7]) switch {
                FulfillmentStatus.Returned => "Returned", FulfillmentStatus.FailedDelivery => "Failed",
                FulfillmentStatus.Cancelled => "Cancelled", FulfillmentStatus.Delivered or FulfillmentStatus.Completed => "Delivered",
                _ => PointOfSale.Infrastructure.Reliability.OrderFulfillmentLineageAuthority.ToProjectionShippingStatus(Enum.Parse<ShipmentStatus>(row[4])) };
            var code = docs.Count != 1 ? "missing_or_duplicate" :
                docs[0].Status != Enum.Parse<OrderStatus>(row[2]).ToString() || docs[0].PaymentStatus != Enum.Parse<PaymentStatus>(row[3]).ToString() ||
                docs[0].ShippingStatus != expectedShipping ||
                docs[0].Total != decimal.Parse(row[5], System.Globalization.CultureInfo.InvariantCulture) ||
                docs[0].ShippingCost != decimal.Parse(row[6], System.Globalization.CultureInfo.InvariantCulture) ? "state_or_totals" : "matched";
            mismatches[code] = mismatches.GetValueOrDefault(code) + 1;
            if (docs.Count == 1)
            {
                var fields = new[] { ("orderStatus", docs[0].Status != Enum.Parse<OrderStatus>(row[2]).ToString()),
                    ("paymentStatus", docs[0].PaymentStatus != Enum.Parse<PaymentStatus>(row[3]).ToString()),
                    ("shippingStatus", docs[0].ShippingStatus != expectedShipping),
                    ("total", docs[0].Total != decimal.Parse(row[5], System.Globalization.CultureInfo.InvariantCulture)),
                    ("shippingCost", docs[0].ShippingCost != decimal.Parse(row[6], System.Globalization.CultureInfo.InvariantCulture)) };
                foreach (var (name, different) in fields) if (different) mismatchFields[name] = mismatchFields.GetValueOrDefault(name) + 1;
            }
        }
        await File.WriteAllTextAsync(Path.Combine(root, $"current-reconciliation-{phase}.json"), JsonSerializer.Serialize(new {
            ObservedUtc = DateTime.UtcNow, ReadOnly = true, MigrationCount = history.Length, PendingMigrations = 0,
            Observations = observations, Constraints = constraints, Triggers = triggers, ShipmentStates = shipmentStates,
            CarrierStates = carrierStates, ConnectionStates = connectionStates, Outbox = outbox, FinancialCounts = financialCounts,
            ReservationClassification = reservationClassification, NullableEvidence = nullableEvidence, Configurations = configurations,
            MongoComparedShipments = projections.Length, MongoResults = mismatches, MongoMismatchFields = mismatchFields }, new JsonSerializerOptions { WriteIndented = true }));
        await tx.RollbackAsync();
        output.WriteLine($"Read-only reconciliation completed: {checks.Count} aggregate checks, {projections.Length} shipment/order projections. Exceptions are observations, not repaired or silently asserted clean.");
    }
}

public sealed class CrossDomainReadOnlyFactAttribute : FactAttribute
{
    public CrossDomainReadOnlyFactAttribute()
    { if (Environment.GetEnvironmentVariable("SHIPPING_CROSS_DOMAIN_READONLY") != "1") Skip = "Explicit configured read-only observation required; no fallback."; }
}
