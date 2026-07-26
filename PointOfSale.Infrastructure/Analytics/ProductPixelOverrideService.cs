using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Analytics;

/// <summary>
/// Service for managing Per-Product Pixel Overrides (Phase M2).
/// Uses Read-Through caching with Redis.
/// </summary>
public class ProductPixelOverrideService : IProductPixelOverrideService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ProductPixelOverrideService> _logger;

    private const string CacheKeyPrefix = "pixel:product-overrides:tenant:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1); // Long TTL, invalidates on write

    public ProductPixelOverrideService(
        IServiceScopeFactory scopeFactory,
        IConnectionMultiplexer redis,
        ILogger<ProductPixelOverrideService> logger)
    {
        _scopeFactory = scopeFactory;
        _redis = redis;
        _logger = logger;
    }

    public async Task<ProductPixelOverride?> GetOverrideAsync(Guid tenantId, Guid productId, CancellationToken ct = default)
    {
        var overrides = await GetOverridesForTenantAsync(tenantId, ct);
        return overrides.TryGetValue(productId, out var overrideEntity) ? overrideEntity : null;
    }

    public async Task<Dictionary<Guid, ProductPixelOverride>> GetOverridesForTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var key = $"{CacheKeyPrefix}{tenantId:N}";
        
        // 1. Try Cache
        try
        {
            var db = _redis.GetDatabase();
            var cachedJson = await db.StringGetAsync(key);

            if (!cachedJson.IsNullOrEmpty)
            {
                var cachedDict = JsonSerializer.Deserialize<Dictionary<Guid, ProductPixelOverride>>(cachedJson!);
                if (cachedDict != null)
                {
                    _logger.LogDebug("Cache HIT for ProductPixelOverrides | Tenant: {TenantId} | Count: {Count}", tenantId, cachedDict.Count);
                    return cachedDict;
                }
            }
            _logger.LogDebug("Cache MISS for ProductPixelOverrides | Tenant: {TenantId}", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache read failed for ProductPixelOverrides Tenant {TenantId}", tenantId);
        }

        // 2. Fetch from DB
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var overrides = await dbContext.ProductPixelOverrides
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId)
            // Include product details if needed for UI, but here we just need the rules
            .ToListAsync(ct);

        var overridesDict = overrides.ToDictionary(o => o.ProductId);

        // 3. Update Cache
        try
        {
            var db = _redis.GetDatabase();
            // Reuse existing key variable
            await db.StringSetAsync(key, JsonSerializer.Serialize(overridesDict), CacheTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache write failed for ProductPixelOverrides Tenant {TenantId}", tenantId);
        }

        return overridesDict;
    }

    public async Task SaveOverrideAsync(ProductPixelOverride overrideEntity, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        overrideEntity.UpdatedAtUtc = DateTime.UtcNow;
        overrideEntity.Id = Guid.NewGuid(); // Will be ignored on conflict

        // Use atomic upsert to handle race conditions
        var sql = @"
            INSERT INTO ""ProductPixelOverrides"" 
            (""Id"", ""TenantId"", ""ProductId"", ""IsEnabled"", ""Strategy"", ""Value"", 
             ""ApplyToMeta"", ""ApplyToGoogle"", ""ApplyToTikTok"", ""UpdatedAtUtc"", ""UpdatedBy"")
            VALUES 
            ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10})
            ON CONFLICT (""TenantId"", ""ProductId"") 
            DO UPDATE SET 
                ""IsEnabled"" = EXCLUDED.""IsEnabled"",
                ""Strategy"" = EXCLUDED.""Strategy"",
                ""Value"" = EXCLUDED.""Value"",
                ""ApplyToMeta"" = EXCLUDED.""ApplyToMeta"",
                ""ApplyToGoogle"" = EXCLUDED.""ApplyToGoogle"",
                ""ApplyToTikTok"" = EXCLUDED.""ApplyToTikTok"",
                ""UpdatedAtUtc"" = EXCLUDED.""UpdatedAtUtc"",
                ""UpdatedBy"" = EXCLUDED.""UpdatedBy""";

        await dbContext.Database.ExecuteSqlRawAsync(sql,
            overrideEntity.Id,
            overrideEntity.TenantId,
            overrideEntity.ProductId,
            overrideEntity.IsEnabled,
            (int)overrideEntity.Strategy,
            overrideEntity.Value,
            overrideEntity.ApplyToMeta,
            overrideEntity.ApplyToGoogle,
            overrideEntity.ApplyToTikTok,
            overrideEntity.UpdatedAtUtc,
            overrideEntity.UpdatedBy);
        
        _logger.LogInformation(
            "Product Override Saved | Tenant: {TenantId} | Product: {ProductId} | Enabled: {Enabled}",
            overrideEntity.TenantId, overrideEntity.ProductId, overrideEntity.IsEnabled);

        // Invalidate Cache
        await InvalidateCacheAsync(overrideEntity.TenantId);
    }

    public async Task DeleteOverrideAsync(Guid tenantId, Guid productId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var existing = await dbContext.ProductPixelOverrides
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.ProductId == productId, ct);

        if (existing != null)
        {
            dbContext.ProductPixelOverrides.Remove(existing);
            await dbContext.SaveChangesAsync(ct);
            
            _logger.LogInformation(
                "Product Override Deleted | Tenant: {TenantId} | Product: {ProductId}",
                tenantId, productId);

            // Invalidate Cache
            await InvalidateCacheAsync(tenantId);
        }
    }

    private async Task InvalidateCacheAsync(Guid tenantId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"{CacheKeyPrefix}{tenantId:N}";
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache delete failed for ProductPixelOverrides Tenant {TenantId}", tenantId);
        }
    }
}
