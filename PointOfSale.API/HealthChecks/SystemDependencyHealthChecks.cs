using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Elasticsearch.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Nest;
using Npgsql;
using PointOfSale.API.Configuration;
using PointOfSale.Infrastructure.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Services;
using RabbitMQ.Client;

namespace PointOfSale.API.HealthChecks;

public sealed class PostgresDependencyHealthCheck : IHealthCheck
{
    private static readonly TimeSpan DependencyTimeout = TimeSpan.FromSeconds(5);

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PostgresDependencyHealthCheck> _logger;

    public PostgresDependencyHealthCheck(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<PostgresDependencyHealthCheck> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = PostgresConnectionStringPolicy.NormalizeForHealthChecks(
            _configuration.GetConnectionString("DefaultConnection"),
            _environment.IsDevelopment(),
            timeoutSeconds: (int)DependencyTimeout.TotalSeconds);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy("PostgreSQL connection string is not configured.");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DependencyTimeout);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(timeoutCts.Token);

            await using var command = new NpgsqlCommand("SELECT 1;", connection);
            await command.ExecuteScalarAsync(timeoutCts.Token);

            return HealthCheckResult.Healthy(
                "PostgreSQL is reachable.",
                new Dictionary<string, object>
                {
                    ["host"] = connection.Host ?? string.Empty,
                    ["port"] = connection.Port,
                    ["database"] = connection.Database,
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL dependency health check failed");
            return HealthCheckResult.Unhealthy(
                "PostgreSQL is unavailable.",
                exception: ex);
        }
    }
}

public sealed class RedisDependencyHealthCheck : IHealthCheck
{
    private static readonly TimeSpan DegradedLatencyThreshold = TimeSpan.FromMilliseconds(100);

    private readonly RedisConnectionFactory _connectionFactory;
    private readonly ILogger<RedisDependencyHealthCheck> _logger;

    public RedisDependencyHealthCheck(
        RedisConnectionFactory connectionFactory,
        ILogger<RedisDependencyHealthCheck> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_connectionFactory.IsEnabled)
        {
            return HealthCheckResult.Unhealthy(
                "Redis is disabled by configuration.",
                data: new Dictionary<string, object> { ["enabled"] = false });
        }

        try
        {
            var database = _connectionFactory.GetDatabase();
            if (database == null)
            {
                return HealthCheckResult.Unhealthy(
                    "Redis connection is unavailable.",
                    data: new Dictionary<string, object>
                    {
                        ["enabled"] = true,
                        ["connected"] = _connectionFactory.IsConnected,
                    });
            }

            var latency = await database.PingAsync();
            var data = new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["connected"] = _connectionFactory.IsConnected,
                ["latencyMs"] = latency.TotalMilliseconds,
            };

            return latency >= DegradedLatencyThreshold
                ? HealthCheckResult.Unhealthy("Redis is reachable but responding slowly.", data: data)
                : HealthCheckResult.Healthy("Redis is reachable.", data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis dependency health check failed");
            return HealthCheckResult.Unhealthy(
                "Redis is unavailable.",
                exception: ex);
        }
    }
}

public sealed class MongoDependencyHealthCheck : IHealthCheck
{
    private static readonly TimeSpan DependencyTimeout = TimeSpan.FromSeconds(5);

    private readonly MongoDbContext _mongoDbContext;
    private readonly ILogger<MongoDependencyHealthCheck> _logger;

    public MongoDependencyHealthCheck(
        MongoDbContext mongoDbContext,
        ILogger<MongoDependencyHealthCheck> logger)
    {
        _mongoDbContext = mongoDbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DependencyTimeout);

            var pingResult = await _mongoDbContext.Database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: timeoutCts.Token);

            return HealthCheckResult.Healthy(
                "MongoDB is reachable.",
                new Dictionary<string, object>
                {
                    ["database"] = _mongoDbContext.Database.DatabaseNamespace.DatabaseName,
                    ["ok"] = pingResult.GetValue("ok", BsonNull.Value).ToString() ?? string.Empty,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MongoDB dependency health check failed");
            return HealthCheckResult.Unhealthy(
                "MongoDB is unavailable.",
                exception: ex);
        }
    }
}

public sealed class ElasticsearchDependencyHealthCheck : IHealthCheck
{
    private static readonly TimeSpan DependencyTimeout = TimeSpan.FromSeconds(5);

    private readonly IElasticClient _elasticClient;
    private readonly ElasticsearchSettings _settings;
    private readonly ILogger<ElasticsearchDependencyHealthCheck> _logger;

    public ElasticsearchDependencyHealthCheck(
        IElasticClient elasticClient,
        IOptions<ElasticsearchSettings> settings,
        ILogger<ElasticsearchDependencyHealthCheck> logger)
    {
        _elasticClient = elasticClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return HealthCheckResult.Unhealthy(
                "Elasticsearch is disabled by configuration.",
                data: new Dictionary<string, object> { ["enabled"] = false });
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DependencyTimeout);

            var ping = await _elasticClient.PingAsync(c => c, timeoutCts.Token);
            if (!ping.IsValid)
            {
                return HealthCheckResult.Unhealthy(
                    "Elasticsearch is unavailable.",
                    data: new Dictionary<string, object>
                    {
                        ["url"] = _settings.Url ?? string.Empty,
                        ["error"] = ping.DebugInformation ?? string.Empty,
                    });
            }

            var clusterHealth = await _elasticClient.Cluster.HealthAsync(ct: timeoutCts.Token);
            var data = new Dictionary<string, object>
            {
                ["url"] = _settings.Url ?? string.Empty,
                ["status"] = clusterHealth.Status.ToString(),
                ["activeShards"] = clusterHealth.ActiveShards,
                ["unassignedShards"] = clusterHealth.UnassignedShards,
            };

            return clusterHealth.Status == global::Elasticsearch.Net.Health.Red
                ? HealthCheckResult.Unhealthy("Elasticsearch cluster status is RED.", data: data)
                : HealthCheckResult.Healthy("Elasticsearch is reachable.", data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Elasticsearch dependency health check failed");
            return HealthCheckResult.Unhealthy(
                "Elasticsearch is unavailable.",
                exception: ex);
        }
    }
}

public sealed class RabbitMqDependencyHealthCheck : IHealthCheck
{
    private const int DefaultPort = 5672;
    private static readonly TimeSpan DependencyTimeout = TimeSpan.FromSeconds(3);

    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMqDependencyHealthCheck> _logger;

    public RabbitMqDependencyHealthCheck(
        IConfiguration configuration,
        ILogger<RabbitMqDependencyHealthCheck> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var configuredHost = _configuration["RabbitMQ:Host"];
        if (string.IsNullOrWhiteSpace(configuredHost))
        {
            return HealthCheckResult.Unhealthy("RabbitMQ host is not configured.");
        }

        var (host, port) = ParseHostAndPort(configuredHost);
        if (string.IsNullOrWhiteSpace(host))
        {
            return HealthCheckResult.Unhealthy("RabbitMQ host configuration is invalid.");
        }

        var virtualHost = _configuration["RabbitMQ:VirtualHost"] ?? "/";
        var username = _configuration["RabbitMQ:Username"] ?? "guest";
        var password = _configuration["RabbitMQ:Password"] ?? "guest";
        var data = new Dictionary<string, object>
        {
            ["host"] = host,
            ["port"] = port,
            ["vhost"] = virtualHost,
        };

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DependencyTimeout);

            var rabbitMqConnectionFactory = new ConnectionFactory
            {
                HostName = host,
                Port = port,
                UserName = username,
                Password = password,
                VirtualHost = virtualHost,
                AutomaticRecoveryEnabled = false,
                RequestedConnectionTimeout = DependencyTimeout,
                ClientProvidedName = "PointOfSale.RabbitMqDependencyHealthCheck"
            };

            await using (var connection = await rabbitMqConnectionFactory.CreateConnectionAsync(timeoutCts.Token))
            {
            }

            return HealthCheckResult.Healthy(
                "RabbitMQ is reachable.",
                data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ dependency health check failed");
            return HealthCheckResult.Degraded(
                "RabbitMQ is unavailable.",
                exception: ex,
                data: data);
        }
    }

    private static (string Host, int Port) ParseHostAndPort(string configuredHost)
    {
        var normalized = configuredHost.Trim();
        if (Uri.TryCreate(
                normalized.Contains("://", StringComparison.Ordinal)
                    ? normalized
                    : $"amqp://{normalized}",
                UriKind.Absolute,
                out var uri))
        {
            return (uri.Host, uri.Port > 0 ? uri.Port : DefaultPort);
        }

        return (normalized, DefaultPort);
    }
}

public sealed class R2DependencyHealthCheck : IHealthCheck
{
    private static readonly TimeSpan DependencyTimeout = TimeSpan.FromSeconds(5);

    private readonly R2Options _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<R2DependencyHealthCheck> _logger;

    public R2DependencyHealthCheck(
        IOptions<R2Options> options,
        IHostEnvironment environment,
        ILogger<R2DependencyHealthCheck> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            if (_environment.IsDevelopment())
            {
                return await CheckDevelopmentFileStorageAsync(cancellationToken);
            }

            return HealthCheckResult.Unhealthy("Cloudflare R2 is not fully configured.");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DependencyTimeout);

            using var client = CreateClient();
            var response = await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _options.Bucket,
                MaxKeys = 1
            }, timeoutCts.Token);

            return response.HttpStatusCode == HttpStatusCode.OK
                ? HealthCheckResult.Healthy(
                    "Cloudflare R2 is reachable.",
                    new Dictionary<string, object>
                    {
                        ["bucket"] = _options.Bucket,
                        ["serviceUrl"] = _options.ResolvedServiceUrl ?? string.Empty,
                    })
                : HealthCheckResult.Unhealthy(
                    $"Cloudflare R2 returned HTTP {(int)response.HttpStatusCode}.",
                    data: new Dictionary<string, object>
                    {
                        ["bucket"] = _options.Bucket,
                        ["serviceUrl"] = _options.ResolvedServiceUrl ?? string.Empty,
                        ["statusCode"] = (int)response.HttpStatusCode,
                    });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudflare R2 dependency health check failed");
            return HealthCheckResult.Unhealthy(
                "Cloudflare R2 is unavailable.",
                exception: ex);
        }
    }

    private async Task<HealthCheckResult> CheckDevelopmentFileStorageAsync(
        CancellationToken cancellationToken)
    {
        var storageRoot = Path.GetFullPath(Path.Combine(
            _environment.ContentRootPath,
            "secure-uploads",
            "development-object-storage"));
        var healthRoot = Path.Combine(storageRoot, "_health");
        var probePath = Path.Combine(healthRoot, $"{Guid.NewGuid():N}.probe");

        try
        {
            Directory.CreateDirectory(healthRoot);
            await File.WriteAllBytesAsync(probePath, new byte[] { 0x52, 0x34 }, cancellationToken);
            File.Delete(probePath);

            return HealthCheckResult.Healthy(
                "Development file storage is active and writable; Cloudflare R2 is optional in Development.",
                new Dictionary<string, object>
                {
                    ["storageMode"] = "development-file",
                    ["r2Required"] = false,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Development file-storage dependency health check failed");
            return HealthCheckResult.Unhealthy(
                "Development file storage is unavailable.",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    ["storageMode"] = "development-file",
                    ["r2Required"] = false,
                });
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            catch
            {
                // The primary result already captures storage availability.
            }
        }
    }

    private IAmazonS3 CreateClient()
    {
        var credentials = new BasicAWSCredentials(
            _options.AccessKeyId.Trim(),
            _options.SecretAccessKey.Trim());

        var config = new AmazonS3Config
        {
            ServiceURL = _options.ResolvedServiceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
            Timeout = DependencyTimeout,
            MaxErrorRetry = 0
        };

        return new AmazonS3Client(credentials, config);
    }
}
