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
    private readonly Lazy<IConnectionMultiplexer?> _connection;
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
        _connection = new Lazy<IConnectionMultiplexer?>(() => CreateConnection());
    }

    public RedisConnectionFactory(
        IOptions<RedisSettings> settings,
        ILogger<RedisConnectionFactory> logger,
        IConnectionMultiplexer? connectionOverride)
    {
        _settings = settings.Value;
        _logger = logger;
        _connection = new Lazy<IConnectionMultiplexer?>(() => connectionOverride);
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
            options.AbortOnConnectFail = false; // Don't throw on startup if Redis is down
            options.ConnectRetry = 3;

            _logger.LogInformation(
                "Connecting to Redis | Endpoint: {Endpoint} | ConnectTimeout: {ConnectTimeout}ms",
                MaskConnectionString(_settings.ConnectionString),
                _settings.ConnectTimeoutMs);

            // Connect on a dedicated thread with a hard, thread-pool-independent timeout so a
            // broken/unresponsive Redis server (port open but no protocol response) can never
            // hang application startup. The timeout uses a System.Threading.Timer (dedicated timer
            // thread) + a kernel WaitHandle.WaitAny, which are NOT subject to thread-pool starvation.
            var connectBudgetMs = Math.Max(_settings.ConnectTimeoutMs + 2000, 10000);
            var connection = ConnectWithTimeout(options, connectBudgetMs);
            if (connection == null)
            {
                _logger.LogWarning(
                    "Redis connect timed out after {Budget}ms (server unreachable or unresponsive). Caching will be disabled. ConnectionString: {ConnectionString}",
                    connectBudgetMs,
                    MaskConnectionString(_settings.ConnectionString));
                return null;
            }

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

    private static IConnectionMultiplexer? ConnectWithTimeout(
        ConfigurationOptions options,
        int budgetMs)
    {
        // IMPORTANT: StackExchange.Redis Connect/ConnectAsync performs a partially synchronous
        // preamble (socket connect / DNS / protocol handshake) that blocks indefinitely against a
        // broken/unresponsive Redis server (port open but no protocol response). A naive timeout
        // built on Task.Wait/Task.Delay/Timer relies on thread-pool timer callbacks, which are
        // starved when those blocked connects consume the thread pool -- causing a deadlock that
        // hangs application startup forever.
        //
        // To make the timeout bulletproof we:
        //   1. Run the connect on a dedicated (non-pool) background Thread, so it never consumes a
        //      thread-pool thread.
        //   2. Enforce the budget with a DEDICATED watchdog Thread that calls Thread.Sleep(budget)
        //      and then sets a "timed out" wait handle. A dedicated thread (not the pool) guarantees
        //      the timeout fires even when the pool is saturated by other blocked Redis connects.
        //   3. Block on a kernel WaitHandle.WaitAny (no thread-pool continuation required) on either
        //      "connect finished" or "timed out".
        // If the budget elapses, we abandon the connect thread (it leaks, but boot proceeds) and
        // return null so caching is gracefully disabled.
        var doneEvent = new ManualResetEvent(false);
        var timeoutEvent = new ManualResetEvent(false);
        IConnectionMultiplexer? result = null;

        var connectThread = new Thread(() =>
        {
            try
            {
                result = ConnectionMultiplexer.Connect(options);
            }
            catch
            {
                result = null;
            }
            finally
            {
                doneEvent.Set();
            }
        })
        {
            IsBackground = true,
            Name = "RedisConnectTimeoutGuard"
        };

        var watchdog = new Thread(() =>
        {
            try
            {
                Thread.Sleep(budgetMs);
            }
            finally
            {
                timeoutEvent.Set();
            }
        })
        {
            IsBackground = true,
            Name = "RedisConnectTimeoutWatchdog"
        };

        connectThread.Start();
        watchdog.Start();

        WaitHandle.WaitAny(new WaitHandle[] { doneEvent, timeoutEvent });

        return result;
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
        
        if (_connection.IsValueCreated && _connection.Value != null)
        {
            _connection.Value.Dispose();
        }
        
        _disposed = true;
    }
}
