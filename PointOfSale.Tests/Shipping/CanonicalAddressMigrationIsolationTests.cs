using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using System.Text.Json.Nodes;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Migrations;

namespace PointOfSale.Tests.Shipping;

public sealed class CanonicalAddressMigrationIsolationTests
{
    [Fact]
    public async Task AdditiveMigration_ExecutesOnProvenRestrictedDisposableCluster_AndEnforcesInsertOnlyRetention()
    {
        var configured = Environment.GetEnvironmentVariable("SHIPPING_FOUNDATION_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Explicit isolated foundation connection required; no merchant configuration fallback.");
        var cs = new NpgsqlConnectionStringBuilder(configured);
        Assert.Equal("127.0.0.1", cs.Host); Assert.Equal(55483, cs.Port);
        Assert.Equal("shipping_fixture", cs.Database); Assert.Equal("shipping_fixture", cs.Username);
        await using var connection = new NpgsqlConnection(cs.ConnectionString);
        await connection.OpenAsync();
        await using (var identity = new NpgsqlCommand("SELECT inet_server_port(), current_database(), current_user, rolsuper, rolcreatedb, rolcreaterole FROM pg_roles WHERE rolname = current_user", connection))
        await using (var result = await identity.ExecuteReaderAsync())
        {
            Assert.True(await result.ReadAsync()); Assert.Equal(55483, result.GetInt32(0));
            Assert.Equal("shipping_fixture", result.GetString(1)); Assert.Equal("shipping_fixture", result.GetString(2));
            Assert.False(result.GetBoolean(3)); Assert.False(result.GetBoolean(4)); Assert.False(result.GetBoolean(5));
        }
        // New schema only. No reset, truncation, drop or migration against any existing merchant table.
        var schema = "canonical_" + Guid.NewGuid().ToString("N");
        await Run($"CREATE SCHEMA {schema}; SET search_path TO {schema};");
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs.ConnectionString).Options);
        // Real EF column names/provider types, no merchant FK dependencies or unrelated financial fixtures.
        await CreateTable<AddressAdmissionEvidence>("AddressAdmissionEvidence");
        await CreateTable<Order>("Orders");
        await CreateTable<Shipment>("Shipments");
        var legacy = Guid.NewGuid();
        await Run($"INSERT INTO \"AddressAdmissionEvidence\" (\"Id\") VALUES ('{legacy}');");
        var migration = new CanonicalAddressEvidenceFoundation();
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations);
        foreach (var command in commands) await Run(command.CommandText);
        await using (var value = new NpgsqlCommand("SELECT \"CanonicalAddressEvidenceJson\" IS NULL FROM \"AddressAdmissionEvidence\" WHERE \"Id\"=@id", connection))
        {
            value.Parameters.AddWithValue("id", legacy); Assert.Equal(true, await value.ExecuteScalarAsync());
        }
        var pair = await new WarehouseAddressAdmissionTests().CreateCanonicalPairAsync();
        var id = pair.Destination.Id; var json = pair.Destination.CanonicalAddressEvidenceJson!; var order = pair.Order.Id;
        await Insert("AddressAdmissionEvidence", Row(pair.Origin));
        await Insert("AddressAdmissionEvidence", Row(pair.Destination));
        await Insert("AddressAdmissionEvidence", Row(pair.Billing));
        await Reject($"UPDATE \"AddressAdmissionEvidence\" SET \"PostalCode\"='99999' WHERE \"Id\"='{id}';");
        await Reject($"DELETE FROM \"AddressAdmissionEvidence\" WHERE \"Id\"='{id}';");
        await using (var update = new NpgsqlCommand("UPDATE \"AddressAdmissionEvidence\" SET \"CanonicalAddressEvidenceJson\"=@json WHERE \"Id\"=@id", connection))
        {
            update.Parameters.AddWithValue("json", json); update.Parameters.AddWithValue("id", legacy);
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync())).SqlState);
        }
        await Insert("Orders", Row(pair.Order));
        await Reject($"UPDATE \"Orders\" SET \"CartId\"='{Guid.NewGuid()}' WHERE \"Id\"='{order}';");
        await Reject($"DELETE FROM \"Orders\" WHERE \"Id\"='{order}';");
        foreach (var field in new[] { "TenantId", "CartId", "ShippingCountry", "ShippingZipCode", "ShippingStreet", "ShippingBuilding", "GuestEmail", "BillingStreet" })
        {
            var row = Row(pair.Order); row["Id"] = Guid.NewGuid().ToString();
            row[field] = field.EndsWith("Id", StringComparison.Ordinal) ? Guid.NewGuid().ToString() : "substitution";
            await RejectInsert("Orders", row);
        }
        var swappedRole = Row(pair.Order); swappedRole["Id"] = Guid.NewGuid().ToString();
        swappedRole["BillingAddressAdmissionEvidenceId"] = id.ToString(); await RejectInsert("Orders", swappedRole);
        await Run($"UPDATE \"Orders\" SET \"ShippingCost\"=26 WHERE \"Id\"='{order}';");

        await Insert("Shipments", Row(pair.Shipment));
        foreach (var field in new[] { "RecipientEmail", "ShippingZipCode", "DestinationStreet", "OriginStreet", "ServiceLevel", "OriginSnapshotJson" })
            await Reject($"UPDATE \"Shipments\" SET \"{field}\"='replacement' WHERE \"Id\"='{pair.Shipment.Id}';");
        await Reject($"DELETE FROM \"Shipments\" WHERE \"Id\"='{pair.Shipment.Id}';");
        await Reject($"UPDATE \"Shipments\" SET \"IsDeleted\"=true WHERE \"Id\"='{pair.Shipment.Id}';");
        var invalidOrigin = Row(pair.Shipment); invalidOrigin["Id"] = Guid.NewGuid().ToString(); await RejectInsert("Shipments", invalidOrigin);
        var invalidDestination = Row(pair.Shipment); invalidDestination["Id"] = Guid.NewGuid().ToString();
        invalidDestination["OriginGeographicSnapshotVersion"] = null; invalidDestination["OriginSnapshotJson"] = null;
        invalidDestination["RecipientEmail"] = "different@example.invalid"; await RejectInsert("Shipments", invalidDestination);

        // A local request timestamp alone is not an attempted call. A durable attempt is.
        var attempted = Row(pair.Shipment); var attemptedId = Guid.NewGuid(); attempted["Id"] = attemptedId.ToString();
        attempted["OriginGeographicSnapshotVersion"] = null; attempted["OriginSnapshotJson"] = null;
        attempted["ExternalRegistrationAttemptCount"] = 1;
        await Insert("Shipments", attempted);
        var snapshot = JsonNode.Parse(pair.Shipment.OriginSnapshotJson!)!.AsObject(); snapshot["ShipmentId"] = attemptedId.ToString();
        await using (var capture = new NpgsqlCommand("UPDATE \"Shipments\" SET \"OriginGeographicSnapshotVersion\"=2, \"OriginSnapshotJson\"=@json WHERE \"Id\"=@id", connection))
        {
            capture.Parameters.AddWithValue("json", snapshot.ToJsonString()); capture.Parameters.AddWithValue("id", attemptedId);
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => capture.ExecuteNonQueryAsync())).SqlState);
        }

        var concurrent = Row(pair.Shipment); var concurrentId = Guid.NewGuid(); concurrent["Id"] = concurrentId.ToString();
        concurrent["OriginGeographicSnapshotVersion"] = null; concurrent["OriginSnapshotJson"] = null;
        await Insert("Shipments", concurrent);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var arrivals = 0;
        var outcomes = await Task.WhenAll(CaptureConcurrently(0), CaptureConcurrently(1));
        Assert.Equal(1, outcomes.Count(code => code == "accepted")); Assert.Equal(1, outcomes.Count(code => code == "23514"));

        async Task<string> CaptureConcurrently(int variant)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var competing = new NpgsqlConnection(cs.ConnectionString); await competing.OpenAsync(deadline.Token);
            await using (var scope = new NpgsqlCommand($"SET search_path TO {schema}", competing)) await scope.ExecuteNonQueryAsync(deadline.Token);
            var payload = JsonNode.Parse(pair.Shipment.OriginSnapshotJson!)!.AsObject(); payload["ShipmentId"] = concurrentId.ToString();
            var time = pair.Shipment.OriginGeographicSnapshotCreatedAtUtc!.Value.AddSeconds(variant); payload["SnapshottedAtUtc"] = JsonValue.Create(time);
            if (Interlocked.Increment(ref arrivals) == 2) ready.SetResult();
            await ready.Task.WaitAsync(deadline.Token);
            await using var command = new NpgsqlCommand("UPDATE \"Shipments\" SET \"OriginGeographicSnapshotVersion\"=2, \"OriginSnapshotJson\"=@json, \"OriginGeographicSnapshotCreatedAtUtc\"=@time WHERE \"Id\"=@id", competing);
            command.Parameters.AddWithValue("json", payload.ToJsonString()); command.Parameters.AddWithValue("time", time); command.Parameters.AddWithValue("id", concurrentId);
            try { Assert.Equal(1, await command.ExecuteNonQueryAsync(deadline.Token)); return "accepted"; }
            catch (PostgresException error) { return error.SqlState; }
        }

        async Task CreateTable<T>(string table)
        {
            var properties = db.Model.FindEntityType(typeof(T))!.GetProperties()
                .Where(p => p.Name != nameof(AddressAdmissionEvidence.CanonicalAddressEvidenceJson) && p.GetColumnName() != "xmin");
            var columns = properties.Select(p => $"\"{p.GetColumnName()}\" {p.GetRelationalTypeMapping().StoreType}{(p.Name == "Id" ? " PRIMARY KEY" : "")}");
            await Run($"CREATE TABLE \"{table}\" ({string.Join(",", columns)});");
        }
        JsonObject Row<T>(T value) where T : class
        {
            var values = db.Model.FindEntityType(typeof(T))!.GetProperties().Where(p => p.GetColumnName() != "xmin").ToDictionary(p => p.GetColumnName()!, p =>
            {
                var field = p.PropertyInfo?.GetValue(value);
                return p.GetTypeMapping().Converter?.ConvertToProvider(field) ?? field;
            });
            return JsonSerializer.SerializeToNode(values)!.AsObject();
        }
        async Task Insert(string table, JsonObject row)
        {
            await using var command = new NpgsqlCommand($"INSERT INTO \"{table}\" SELECT * FROM jsonb_populate_record(NULL::\"{table}\", @row)", connection);
            command.Parameters.AddWithValue("row", NpgsqlDbType.Jsonb, row.ToJsonString());
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        async Task RejectInsert(string table, JsonObject row) => Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => Insert(table, row))).SqlState);

        async Task Run(string sql) { await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        async Task Reject(string sql) => Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => Run(sql))).SqlState);
    }
}
