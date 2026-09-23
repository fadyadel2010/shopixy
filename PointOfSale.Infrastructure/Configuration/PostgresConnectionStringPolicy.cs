using Npgsql;

namespace PointOfSale.Infrastructure.Configuration;

/// <summary>
/// Centralizes PostgreSQL transport defaults so runtime services, health checks,
/// and design-time tooling all behave consistently.
/// </summary>
public static class PostgresConnectionStringPolicy
{
    private const int DefaultTimeoutSeconds = 15;
    private const int DefaultCommandTimeoutSeconds = 30;

    public static string? Normalize(string? connectionString, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        if (builder.Timeout <= 0)
        {
            builder.Timeout = DefaultTimeoutSeconds;
        }

        if (builder.CommandTimeout <= 0)
        {
            builder.CommandTimeout = DefaultCommandTimeoutSeconds;
        }

        if (isDevelopment && !HasKeyword(connectionString, "SSL Mode") && !HasKeyword(connectionString, "SslMode"))
        {
            builder.SslMode = SslMode.Disable;
        }

        return builder.ConnectionString;
    }

    public static string? NormalizeForHealthChecks(
        string? connectionString,
        bool isDevelopment,
        int timeoutSeconds = 5)
    {
        var normalized = Normalize(connectionString, isDevelopment);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        var builder = new NpgsqlConnectionStringBuilder(normalized)
        {
            Timeout = timeoutSeconds,
            CommandTimeout = timeoutSeconds,
            Pooling = false
        };

        return builder.ConnectionString;
    }

    private static bool HasKeyword(string connectionString, string keyword)
    {
        return connectionString.IndexOf($"{keyword}=", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
