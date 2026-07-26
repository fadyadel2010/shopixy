using Microsoft.Extensions.Caching.Memory;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubGovernanceCacheInvalidationService
{
    ValueTask<string> GetVisibilityVersionTokenAsync(CancellationToken cancellationToken = default);

    Task<CommerceHubGovernanceCacheInvalidationResult> InvalidateVisibilityAsync(
        Guid? vendorId,
        string reason,
        CancellationToken cancellationToken = default);
}

public sealed class CommerceHubGovernanceCacheInvalidationResult
{
    public Guid? VendorId { get; init; }
    public long Version { get; init; }
    public bool UsedRedis { get; init; }
    public bool UsedFallback { get; init; }
    public string? FailureReason { get; init; }
}

public sealed class CommerceHubGovernanceCacheInvalidationService : ICommerceHubGovernanceCacheInvalidationService
{
    private const string VisibilityVersionRedisKey = "commercehub:governance:visibility:version";
    private const string VisibilityVersionMemoryKey = "commercehub:governance:visibility:version:memory";
    private readonly RedisConnectionFactory _redisConnectionFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<CommerceHubGovernanceCacheInvalidationService> _logger;

    public CommerceHubGovernanceCacheInvalidationService(
        RedisConnectionFactory redisConnectionFactory,
        IMemoryCache memoryCache,
        ILogger<CommerceHubGovernanceCacheInvalidationService> logger)
    {
        _redisConnectionFactory = redisConnectionFactory;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async ValueTask<string> GetVisibilityVersionTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var database = _redisConnectionFactory.GetDatabase();
        if (database != null)
        {
            try
            {
                var value = await database.StringGetAsync(VisibilityVersionRedisKey);
                if (value.HasValue && long.TryParse(value.ToString(), out var redisVersion))
                {
                    return redisVersion.ToString();
                }

                return "0";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "CommerceHub governance cache version lookup fell back to local memory because Redis could not be read.");
            }
        }

        return GetLocalVersion().ToString();
    }

    public async Task<CommerceHubGovernanceCacheInvalidationResult> InvalidateVisibilityAsync(
        Guid? vendorId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "Governance visibility update" : reason.Trim();
        var database = _redisConnectionFactory.GetDatabase();
        if (database != null)
        {
            try
            {
                var newVersion = await database.StringIncrementAsync(VisibilityVersionRedisKey);
                SetLocalVersion((long)newVersion);

                _logger.LogInformation(
                    "CommerceHub governance cache invalidated via Redis | VendorId: {VendorId} | Version: {Version} | Reason: {Reason}",
                    vendorId,
                    newVersion,
                    normalizedReason);

                return new CommerceHubGovernanceCacheInvalidationResult
                {
                    VendorId = vendorId,
                    Version = (long)newVersion,
                    UsedRedis = true,
                    UsedFallback = false
                };
            }
            catch (Exception ex)
            {
                var fallbackVersion = IncrementLocalVersion();
                _logger.LogWarning(
                    ex,
                    "CommerceHub governance cache invalidation fell back to local memory | VendorId: {VendorId} | Version: {Version} | Reason: {Reason}",
                    vendorId,
                    fallbackVersion,
                    normalizedReason);

                return new CommerceHubGovernanceCacheInvalidationResult
                {
                    VendorId = vendorId,
                    Version = fallbackVersion,
                    UsedRedis = false,
                    UsedFallback = true,
                    FailureReason = ex.Message
                };
            }
        }

        var localVersion = IncrementLocalVersion();
        _logger.LogInformation(
            "CommerceHub governance cache invalidated via local memory fallback | VendorId: {VendorId} | Version: {Version} | Reason: {Reason}",
            vendorId,
            localVersion,
            normalizedReason);

        return new CommerceHubGovernanceCacheInvalidationResult
        {
            VendorId = vendorId,
            Version = localVersion,
            UsedRedis = false,
            UsedFallback = true,
            FailureReason = _redisConnectionFactory.IsEnabled
                ? "Redis database was unavailable during invalidation."
                : "Redis caching is disabled."
        };
    }

    private long GetLocalVersion()
    {
        return _memoryCache.TryGetValue<long>(VisibilityVersionMemoryKey, out var version)
            ? version
            : 0L;
    }

    private long IncrementLocalVersion()
    {
        var version = GetLocalVersion() + 1;
        SetLocalVersion(version);
        return version;
    }

    private void SetLocalVersion(long version)
    {
        _memoryCache.Set(VisibilityVersionMemoryKey, version);
    }
}
