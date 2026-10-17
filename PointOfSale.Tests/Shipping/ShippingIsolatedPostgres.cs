using Npgsql;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

internal static class ShippingIsolatedPostgres
{
    internal static TestDatabaseScope OpenManagedTestScope([System.Runtime.CompilerServices.CallerFilePath] string path = "")
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException("Explicit disposable cluster required before managed shipping fixtures; no configuration fallback.");
        var cs = new NpgsqlConnectionStringBuilder(configured);
        var prefix = Environment.GetEnvironmentVariable("Testing__DatabasePrefix");
        if (cs.Host != "127.0.0.1" || cs.Port != 55483 || cs.Database != "postgres" || cs.Username != "shipping_fixture_admin" ||
            !string.IsNullOrEmpty(cs.SearchPath) || prefix == null || !prefix.StartsWith("shipping_", StringComparison.Ordinal))
            throw new InvalidOperationException("Managed shipping fixtures require the approved disposable cluster and shipping-specific database prefix.");
        using var connection = new NpgsqlConnection(cs.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand("SELECT current_database()='postgres' AND current_user='shipping_fixture_admin' AND inet_server_addr()='127.0.0.1'::inet AND inet_server_port()=55483", connection);
        if (!Equals(command.ExecuteScalar(), true)) throw new InvalidOperationException("Disposable cluster identity failed before fixture migrations.");
        return TestDatabaseScope.Open(path);
    }

    internal static async Task<string> VerifyAsync(string variable, string database, CancellationToken ct = default)
    {
        var configured = Environment.GetEnvironmentVariable(variable)
            ?? throw new InvalidOperationException("Explicit isolated shipping connection required; merchant fallback is forbidden.");
        var cs = new NpgsqlConnectionStringBuilder(configured);
        if (cs.Host != "127.0.0.1" || cs.Port != 55483 || cs.Database != database || cs.Username != "shipping_fixture" ||
            database == "PosEcommerceDb_Baseline" || !string.IsNullOrEmpty(cs.SearchPath))
            throw new InvalidOperationException("Shipping fixture target is not the approved isolated cluster/database/role.");
        await using var connection = new NpgsqlConnection(cs.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT current_database() = @database AND current_user = 'shipping_fixture'
              AND inet_server_addr() = '127.0.0.1'::inet AND inet_server_port() = 55483
              AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole
            FROM pg_roles WHERE rolname = current_user
            """, connection);
        command.Parameters.AddWithValue("database", database);
        if (!Equals(await command.ExecuteScalarAsync(ct), true))
            throw new InvalidOperationException("Isolated shipping PostgreSQL server/role verification failed.");
        return cs.ConnectionString;
    }
}
