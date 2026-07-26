using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Infrastructure.Data;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Analytics;

/// <summary>
/// Service for managing Pixel Tracking Configuration.
/// Handles storage in PostgreSQL and caching in Redis.
/// </summary>
public class PixelTrackingConfigService : IPixelTrackingConfigService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PixelTrackingConfigService> _logger;

    private const string CacheKeyPrefix = "pixel_tracking:config:tenant:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10); // Longer TTL as rarely changes

    public PixelTrackingConfigService(
        IServiceScopeFactory scopeFactory,
        IConnectionMultiplexer redis,
        ILogger<PixelTrackingConfigService> logger)
    {
        _scopeFactory = scopeFactory;
        _redis = redis;
        _logger = logger;
    }

    public async Task<PixelTrackingConfig> GetConfigAsync(Guid tenantId, CancellationToken ct = default)
    {
        // 1. Try Cache
        try
        {
            var db = _redis.GetDatabase();
            var key = $"{CacheKeyPrefix}{tenantId:N}";
            var cachedCtx = await db.StringGetAsync(key);
            if (!cachedCtx.IsNullOrEmpty)
            {
                var cachedConfig = JsonSerializer.Deserialize<PixelTrackingConfig>(cachedCtx!);
                if (cachedConfig != null)
                {
                    return cachedConfig;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache read failed for PixelTrackingConfig Tenant {TenantId}", tenantId);
        }

        // 2. Try DB
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Note: Assuming DbSet<PixelTrackingConfig> will be added to DbContext
        // Using Set<PixelTrackingConfig>() to avoid compile error if property not yet in DbContext class
        var config = await dbContext.Set<PixelTrackingConfig>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        if (config == null)
        {
            // Return default disabled config
            config = new PixelTrackingConfig
            {
                TenantId = tenantId,
                IsEnabled = false,
                Strategy = PixelOverrideStrategy.Percentage,
                Value = 100,
                AppliesTo = Array.Empty<PixelProvider>()
            };
        }
        else
        {
            // Hydrate AppliesTo from JSON
            if (!string.IsNullOrEmpty(config.AppliesToJson))
            {
                try 
                {
                    config.AppliesTo = JsonSerializer.Deserialize<PixelProvider[]>(config.AppliesToJson) 
                                       ?? Array.Empty<PixelProvider>();
                }
                catch
                {
                    config.AppliesTo = Array.Empty<PixelProvider>();
                }
            }
        }

        // 3. Cache Result (even if default)
        try
        {
            var db = _redis.GetDatabase();
            var key = $"{CacheKeyPrefix}{tenantId:N}";
            await db.StringSetAsync(key, JsonSerializer.Serialize(config), CacheTtl);
        }
        catch (Exception ex)
        {
             _logger.LogWarning(ex, "Redis cache write failed for PixelTrackingConfig Tenant {TenantId}", tenantId);
        }

        return config;
    }

    public async Task UpdateConfigAsync(PixelTrackingConfig config, CancellationToken ct = default)
    {
        // Sync JSON property
        config.AppliesToJson = JsonSerializer.Serialize(config.AppliesTo ?? Array.Empty<PixelProvider>());
        config.UpdatedAtUtc = DateTime.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var existing = await dbContext.Set<PixelTrackingConfig>()
            .FirstOrDefaultAsync(c => c.TenantId == config.TenantId, ct);

        if (existing == null)
        {
            config.Id = Guid.NewGuid();
            dbContext.Set<PixelTrackingConfig>().Add(config);
        }
        else
        {
            // Update existing
            existing.IsEnabled = config.IsEnabled;
            existing.Strategy = config.Strategy;
            existing.Value = config.Value;
            existing.AppliesTo = config.AppliesTo ?? Array.Empty<PixelProvider>(); // Not mapped but good to keep sync
            existing.AppliesToJson = config.AppliesToJson;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            existing.UpdatedBy = config.UpdatedBy;
        }

        await dbContext.SaveChangesAsync(ct);

        // Invalidate/Update Cache
        try
        {
            var db = _redis.GetDatabase();
            var key = $"{CacheKeyPrefix}{config.TenantId:N}";
            // Update with new value directly to avoid race condition/read-after-write gap
            await db.StringSetAsync(key, JsonSerializer.Serialize(config), CacheTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache update failed for PixelTrackingConfig Tenant {TenantId}", config.TenantId);
        }
        
        _logger.LogInformation(
            "Pixel Tracking Config Updated | Tenant: {TenantId} | Enabled: {Enabled} | Strategy: {Strategy}", 
            config.TenantId, config.IsEnabled, config.Strategy);
    }
}
