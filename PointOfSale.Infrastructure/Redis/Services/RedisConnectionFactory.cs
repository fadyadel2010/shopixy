using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.Redis.Configuration;
using StackExchange.Redis;
using System.Threading;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// Manages the Redis connection as a singleton.
/// 
/// IMPORTANT:
/// - ConnectionMultiplexer is thread-safe and should be reused (singleton)
/// - Connection failures are handled gracefully - cache is optional
/// - Lazy initialization to avoid startup failures if Redis is unavailable
/// </summary>
public class RedisConnectionFactory : IDisposable
{
    private readonly RedisSettings _settings;
    private readonly ILogger<RedisConnectionFactory> _logger;
    private readonly RecoverableRedisConnection _connection;
    private bool _disposed;

    // STARTUP-3: This attribute forces Microsoft.Extensions.DependencyInjection to always select
    // this 2-parameter constructor. Without it, MEDI greedily selects the 3-parameter test-only
    // constructor below (because IConnectionMultiplexer is registered via a factory that resolves
    // RedisConnectionFactory), producing a singleton resolution cycle that hangs Host.StartAsync().
    [ActivatorUtilitiesConstructor]
    public RedisConnectionFactory(
        IOptions<RedisSettings> settings,
        ILogger<RedisConnectionFactory> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        
        // Lazy initialization - connection is created on first use
        _connection = new RecoverableRedisConnection(CreateConnection,
            TimeSpan.FromMilliseconds(Math.Max(_settings.ConnectTimeoutMs + 2000, 10000)), TimeSpan.FromSeconds(30));
    }

    public RedisConnectionFactory(
        IOptions<RedisSettings> settings,
        ILogger<RedisConnectionFactory> logger,
        IConnectionMultiplexer? connectionOverride)
    {
        _settings = settings.Value;
        _logger = logger;
        _connection = new RecoverableRedisConnection(() => connectionOverride, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Gets the Redis connection. May be null if connection failed.
    /// Callers must handle null gracefully.
    /// </summary>
    public IConnectionMultiplexer? Connection => _connection.Value;

    /// <summary>
    /// Gets the Redis database. Returns null if connection is unavailable.
    /// </summary>
    public IDatabase? GetDatabase()
    {
        try
        {
            return Connection?.GetDatabase();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Redis database. Cache operations will be skipped.");
            return null;
        }
    }

    /// <summary>
    /// Gets the Redis server for administrative operations (e.g., KEYS/SCAN).
    /// Returns null if connection is unavailable.
    /// NOTE: KEYS command should be avoided in production. Use SCAN via KeysAsync.
    /// </summary>
    public IServer? GetServer()
    {
        try
        {
            if (Connection == null || !Connection.IsConnected)
            {
                return null;
            }

            var endpoints = Connection.GetEndPoints();
            if (endpoints.Length == 0)
            {
                return null;
            }

            return Connection.GetServer(endpoints[0]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Redis server. Pattern operations will be skipped.");
            return null;
        }
    }

    /// <summary>
    /// Checks if Redis connection is available and healthy
    /// </summary>
    public bool IsConnected => Connection?.IsConnected ?? false;

    /// <summary>
    /// Gets whether Redis caching is enabled in configuration
    /// </summary>
    public bool IsEnabled => _settings.Enabled;

    /// <summary>
    /// Measures one read-only command on the existing shared connection. The
    /// configured asynchronous command budget and caller cancellation both bound
    /// the wait; no retries, cache writes or connection replacement are performed.
    /// </summary>
    public async Task<TimeSpan?> ProbeLatencyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = await _connection.GetAsync(cancellationToken).ConfigureAwait(false);
        var database = connection?.GetDatabase();
        if (database == null) return null;
        return await database.PingAsync().WaitAsync(
            TimeSpan.FromMilliseconds(_settings.AsyncTimeoutMs), cancellationToken).ConfigureAwait(false);
    }

    private IConnectionMultiplexer? CreateConnection()
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Redis caching is disabled in configuration");
            return null;
        }

        if (string.IsNullOrWhiteSpace(_settings.ConnectionString))
        {
            _logger.LogWarning("Redis connection string is not configured. Caching will be disabled.");
            return null;
        }

        try
        {
            var options = ConfigurationOptions.Parse(_settings.ConnectionString);
            options.ConnectTimeout = _settings.ConnectTimeoutMs;
            options.SyncTimeout = _settings.SyncTimeoutMs;
            options.AsyncTimeout = _settings.AsyncTimeoutMs;
            options.AbortOnConnectFail = false; // Don't throw on startup if Redis is down
            options.ConnectRetry = 3;

            _logger.LogInformation(
                "Connecting to Redis | Endpoint: {Endpoint} | ConnectTimeout: {ConnectTimeout}ms",
                MaskConnectionString(_settings.ConnectionString),
                _settings.ConnectTimeoutMs);

            // The recoverable holder owns the dedicated thread, bounded wait and
            // late-result lifetime. AbortOnConnectFail=false preserves SDK reconnect.
            var connection = ConnectionMultiplexer.Connect(options);

            connection.ConnectionFailed += (sender, args) =>
            {
                _logger.LogWarning(
                    "Redis connection failed | Endpoint: {Endpoint} | FailureType: {FailureType}",
                    args.EndPoint,
                    args.FailureType);
            };

            connection.ConnectionRestored += (sender, args) =>
            {
                _logger.LogInformation(
                    "Redis connection restored | Endpoint: {Endpoint}",
                    args.EndPoint);
            };

            if (connection.IsConnected)
            {
                _logger.LogInformation("Redis connection established successfully");
            }
            else
            {
                _logger.LogWarning("Redis connection created but not connected. Will retry on first operation.");
            }

            return connection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to create Redis connection. Caching will be disabled. ConnectionString: {ConnectionString}",
                MaskConnectionString(_settings.ConnectionString));
            return null;
        }
    }

    private static string MaskConnectionString(string connectionString)
    {
        // Mask password if present
        if (connectionString.Contains("password=", StringComparison.OrdinalIgnoreCase))
        {
            var parts = connectionString.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("password=", StringComparison.OrdinalIgnoreCase))
                {
                    parts[i] = "password=***";
                }
            }
            return string.Join(",", parts);
        }
        return connectionString;
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _connection.Dispose();
        
        _disposed = true;
    }
}
