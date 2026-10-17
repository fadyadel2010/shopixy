using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.HealthChecks;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Tests.Shipping;
using StackExchange.Redis;

namespace PointOfSale.Tests.Infrastructure;

public sealed class DependencyReliabilityConfiguredReadOnlyTests
{
    [Phase6ReadOnlyFact]
    public async Task Existing_dependencies_low_rate_minimal_read_and_ping_with_transport_and_queue_observations()
    {
        var root = Path.Combine(TestConfiguration.GetSolutionRoot(), "PointOfSale.API");
        var config = new ConfigurationBuilder().SetBasePath(root).AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", true).AddJsonFile("appsettings.Development.local.json", true)
            .AddUserSecrets<R2Options>(true).AddEnvironmentVariables().Build();
        string Value(string flat, string nested) => (config[flat] ?? config[nested] ?? "").Trim();
        var r2 = new R2Options { AccountId = Value("R2_ACCOUNT_ID", "R2:AccountId"), AccessKeyId = Value("R2_ACCESS_KEY_ID", "R2:AccessKeyId"),
            SecretAccessKey = Value("R2_SECRET_ACCESS_KEY", "R2:SecretAccessKey"), Bucket = Value("R2_BUCKET", "R2:Bucket"), ServiceUrl = Value("R2_SERVICE_URL", "R2:ServiceUrl") };
        Assert.True(r2.IsConfigured);
        var settings = config.GetSection("Redis").Get<RedisSettings>()!;
        var redisOptions = ConfigurationOptions.Parse(settings.ConnectionString);
        using var redis = new RedisConnectionFactory(Options.Create(settings), NullLogger<RedisConnectionFactory>.Instance);
        var check = new R2DependencyHealthCheck(Options.Create(r2), new Host(root), NullLogger<R2DependencyHealthCheck>.Instance);
        var samples = new List<object>();
        for (var i = 0; i < 24; i++)
        {
            var result = await check.CheckHealthAsync(new HealthCheckContext());
            var clock = Stopwatch.StartNew();
            var beforeQueue = redis.Connection?.GetCounters().TotalOutstanding;
            var pause = GC.GetTotalPauseDuration();
            object ping;
            try { var latency = await redis.ProbeLatencyAsync(); ping = new { success = latency.HasValue, latencyMs = latency?.TotalMilliseconds, elapsedMs = clock.Elapsed.TotalMilliseconds }; }
            catch (Exception ex) { ping = new { success = false, category = ex.GetType().Name, elapsedMs = clock.Elapsed.TotalMilliseconds }; }
            samples.Add(new { observedUtc = DateTime.UtcNow, r2 = new { status = result.Status.ToString(), result.Data }, redis = new {
                ping, connected = redis.IsConnected, beforeQueue, afterQueue = redis.Connection?.GetCounters().TotalOutstanding,
                threadPoolThreads = ThreadPool.ThreadCount, pendingWorkItems = ThreadPool.PendingWorkItemCount, gcPauseMs = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds } });
            await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("SHOPIXY_PHASE6_EVIDENCE")!, "dependency-traced-probes.json"),
                JsonSerializer.Serialize(new { readOnly = true, objectNamesRecorded = 0, writes = 0, redisConfiguration = new { redisOptions.Ssl,
                    endpointCount = redisOptions.EndPoints.Count, settings.ConnectTimeoutMs, settings.AsyncTimeoutMs }, samples }, new JsonSerializerOptions { WriteIndented = true }));
            if (i < 23) await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
    private sealed class Host(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "PointOfSale.API";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
