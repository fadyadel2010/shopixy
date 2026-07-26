using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Infrastructure.Data;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Analytics.Dispatchers;

/// <summary>
/// Implementation of IAnalyticsSettingsService.
/// 
/// STORAGE STRATEGY:
/// - PostgreSQL: Source of truth (TenantAnalyticsConfiguration table)
/// - Redis: Cache for fast reads (5-minute TTL)
/// - Secrets encrypted using Data Protection API
/// 
/// SECURITY:
/// - API secrets and access tokens are encrypted at rest
/// - Cache contains encrypted values (decrypted on read)
/// - Settings changes take effect immediately (cache invalidation)
/// 
/// NOTE: Uses IServiceScopeFactory to create scoped DbContext instances when called
/// from background tasks or MassTransit consumers (fire-and-forget patterns).
/// This avoids ObjectDisposedException that occurs when the original scope is disposed.
/// </summary>
public class AnalyticsSettingsService : IAnalyticsSettingsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly IDataProtector _protector;
    private readonly ILogger<AnalyticsSettingsService> _logger;

    private const string CacheKeyPrefix = "analytics:settings:tenant:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private const string ProtectorPurpose = "PointOfSale.Analytics.Settings";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public AnalyticsSettingsService(
        IServiceScopeFactory scopeFactory,
        IConnectionMultiplexer redis,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<AnalyticsSettingsService> logger)
    {
        _scopeFactory = scopeFactory;
        _redis = redis;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    public async Task<TenantAnalyticsSettings?> GetSettingsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        // Try cache first
        var cached = await GetFromCacheAsync(tenantId);
        if (cached != null)
        {
            return cached;
        }

        // Create a new scope to get a fresh DbContext instance
        // This avoids ObjectDisposedException when called from background tasks
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var config = await dbContext.TenantAnalyticsConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (config == null)
        {
            return null;
        }

        // Decrypt and deserialize
        var settings = DeserializeSettings(config.SettingsJson);
        if (settings != null)
        {
            settings.TenantId = tenantId;
            DecryptSecrets(settings);
            
            // Cache for next time
            await CacheSettingsAsync(tenantId, settings);
        }

        return settings;
    }

    public async Task SaveSettingsAsync(
        TenantAnalyticsSettings settings,
        CancellationToken cancellationToken = default)
    {
        // Encrypt secrets before storing
        var settingsToStore = CloneAndEncryptSecrets(settings);
        var json = JsonSerializer.Serialize(settingsToStore, JsonOptions);

        // Create a new scope to get a fresh DbContext instance
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var existing = await dbContext.TenantAnalyticsConfigurations
            .FirstOrDefaultAsync(c => c.TenantId == settings.TenantId, cancellationToken);

        if (existing != null)
        {
            existing.SettingsJson = json;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            dbContext.TenantAnalyticsConfigurations.Add(new TenantAnalyticsConfiguration
            {
                Id = Guid.NewGuid(),
                TenantId = settings.TenantId,
                SettingsJson = json,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Invalidate cache
        await InvalidateCacheAsync(settings.TenantId, cancellationToken);

        _logger.LogInformation(
            "[Analytics Settings] Saved | TenantId: {TenantId} | GA: {GaEnabled} | Meta: {MetaEnabled} | TikTok: {TikTokEnabled}",
            settings.TenantId,
            settings.Google?.Enabled ?? false,
            settings.Meta?.Enabled ?? false,
            settings.TikTok?.Enabled ?? false);
    }

    public async Task SetProviderEnabledAsync(
        Guid tenantId,
        AnalyticsProvider provider,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(tenantId, cancellationToken) 
            ?? new TenantAnalyticsSettings { TenantId = tenantId };

        switch (provider)
        {
            case AnalyticsProvider.GoogleAnalytics:
                settings.Google ??= new GoogleAnalyticsSettings();
                settings.Google.Enabled = enabled;
                break;
            case AnalyticsProvider.Meta:
                settings.Meta ??= new MetaAnalyticsSettings();
                settings.Meta.Enabled = enabled;
                break;
            case AnalyticsProvider.TikTok:
                settings.TikTok ??= new TikTokAnalyticsSettings();
                settings.TikTok.Enabled = enabled;
                break;
            case AnalyticsProvider.AdminRealtime:
                // Admin realtime is always enabled, ignore
                return;
        }

        await SaveSettingsAsync(settings, cancellationToken);
    }

    public async Task InvalidateCacheAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"{CacheKeyPrefix}{tenantId:N}";
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics Settings] Cache invalidation failed | TenantId: {TenantId}",
                tenantId);
        }
    }

    #region Private Helpers

    private async Task<TenantAnalyticsSettings?> GetFromCacheAsync(Guid tenantId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"{CacheKeyPrefix}{tenantId:N}";
            var cached = await db.StringGetAsync(key);

            if (cached.IsNullOrEmpty)
            {
                return null;
            }

            var settings = DeserializeSettings(cached!);
            if (settings != null)
            {
                settings.TenantId = tenantId;
                DecryptSecrets(settings);
            }

            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics Settings] Cache read failed | TenantId: {TenantId}",
                tenantId);
            return null;
        }
    }

    private async Task CacheSettingsAsync(Guid tenantId, TenantAnalyticsSettings settings)
    {
        try
        {
            // Store encrypted version in cache
            var settingsToCache = CloneAndEncryptSecrets(settings);
            var json = JsonSerializer.Serialize(settingsToCache, JsonOptions);

            var db = _redis.GetDatabase();
            var key = $"{CacheKeyPrefix}{tenantId:N}";
            await db.StringSetAsync(key, json, CacheTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics Settings] Cache write failed | TenantId: {TenantId}",
                tenantId);
        }
    }

    private TenantAnalyticsSettings? DeserializeSettings(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TenantAnalyticsSettings>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Analytics Settings] Deserialization failed");
            return null;
        }
    }

    private TenantAnalyticsSettings CloneAndEncryptSecrets(TenantAnalyticsSettings settings)
    {
        var clone = new TenantAnalyticsSettings
        {
            TenantId = settings.TenantId,
            UpdatedAtUtc = DateTime.UtcNow
        };

        if (settings.Google != null)
        {
            clone.Google = new GoogleAnalyticsSettings
            {
                Enabled = settings.Google.Enabled,
                MeasurementId = settings.Google.MeasurementId,
                ApiSecret = EncryptSecret(settings.Google.ApiSecret),
                DebugMode = settings.Google.DebugMode
            };
        }

        if (settings.Meta != null)
        {
            clone.Meta = new MetaAnalyticsSettings
            {
                Enabled = settings.Meta.Enabled,
                PixelId = settings.Meta.PixelId,
                AccessToken = EncryptSecret(settings.Meta.AccessToken),
                TestEventCode = settings.Meta.TestEventCode
            };
        }

        if (settings.TikTok != null)
        {
            clone.TikTok = new TikTokAnalyticsSettings
            {
                Enabled = settings.TikTok.Enabled,
                PixelId = settings.TikTok.PixelId,
                AccessToken = EncryptSecret(settings.TikTok.AccessToken),
                TestMode = settings.TikTok.TestMode,
                Endpoint = settings.TikTok.Endpoint,
                Timeout = settings.TikTok.Timeout
            };
        }

        return clone;
    }

    private void DecryptSecrets(TenantAnalyticsSettings settings)
    {
        if (settings.Google != null)
        {
            settings.Google.ApiSecret = DecryptSecret(settings.Google.ApiSecret);
        }

        if (settings.Meta != null)
        {
            settings.Meta.AccessToken = DecryptSecret(settings.Meta.AccessToken);
        }

        if (settings.TikTok != null)
        {
            settings.TikTok.AccessToken = DecryptSecret(settings.TikTok.AccessToken);
        }
    }

    private string? EncryptSecret(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return null;
        }

        try
        {
            return _protector.Protect(plaintext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Analytics Settings] Encryption failed");
            throw;
        }
    }

    private string? DecryptSecret(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Analytics Settings] Decryption failed - secret may have been encrypted with different keys");
            return null;
        }
    }

    #endregion
}

/// <summary>
/// Entity for storing tenant analytics configuration in PostgreSQL.
/// </summary>
public class TenantAnalyticsConfiguration
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
