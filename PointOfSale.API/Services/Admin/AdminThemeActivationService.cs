using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Services.Admin;

public sealed class AdminThemeActivationService : IAdminThemeActivationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly IPublishLockService _publishLockService;
    private readonly IAdminStoreConfigService _storeConfigReadService;
    private readonly StorefrontPublishTelemetry _publishTelemetry;
    private readonly ILogger<AdminThemeActivationService> _logger;

    public AdminThemeActivationService(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        IPublishLockService publishLockService,
        IAdminStoreConfigService storeConfigReadService,
        StorefrontPublishTelemetry publishTelemetry,
        ILogger<AdminThemeActivationService> logger)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _publishLockService = publishLockService;
        _storeConfigReadService = storeConfigReadService;
        _publishTelemetry = publishTelemetry;
        _logger = logger;
    }

    public async Task<AdminStoreConfigResponse> ActivateThemeAsync(
        Guid tenantId,
        AdminThemeActivationRequest request,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null)
    {
        if (!StorefrontThemes.IsSupported(request.ThemeId))
        {
            throw new ArgumentException("ThemeId is not supported");
        }

        var themeId = StorefrontThemes.NormalizeOrDefault(request.ThemeId);

        _logger.LogInformation(
            "ActivateTheme (Live) | TenantId: {TenantId} | ThemeId: {ThemeId} | TraceId: {TraceId}",
            tenantId,
            themeId,
            traceId);

        var lockResult = await _publishLockService.TryAcquireAsync(tenantId, "theme");
        if (!lockResult.Acquired)
        {
            _logger.LogWarning(
                "ActivateTheme rejected - lock not acquired | TenantId: {TenantId} | ThemeId: {ThemeId}",
                tenantId,
                themeId);
            throw new ConcurrencyConflictException("StoreConfigThemeActivation", tenantId);
        }

        try
        {
            var config = await _dbContext.StoreConfigs
                .FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);

            if (config == null)
            {
                throw new InvalidOperationException("Store config not found");
            }

            ApplyExpectedRowVersion(config, expectedRowVersion);

            var currentThemeId = ExtractStorefrontTheme(config.CustomizationJson, config.ThemeId);
            if (!string.Equals(currentThemeId, themeId, StringComparison.OrdinalIgnoreCase))
            {
                config.CustomizationJson = SetStorefrontThemeInCustomizationJson(
                    config.CustomizationJson,
                    themeId,
                    fallbackThemeId: config.ThemeId);
                config.UpdatedAtUtc = DateTime.UtcNow;

                await PersistActivationAsync(config, currentThemeId, themeId, traceId, cancellationToken);
            }

            return await _storeConfigReadService.GetStoreConfigAsync(tenantId, traceId, cancellationToken);
        }
        finally
        {
            if (lockResult.LockToken != null)
            {
                await _publishLockService.ReleaseAsync(tenantId, lockResult.LockToken);
            }
        }
    }

    private void ApplyExpectedRowVersion(StoreConfig config, uint? expectedRowVersion)
    {
        if (!expectedRowVersion.HasValue)
        {
            return;
        }

        _dbContext.Entry(config)
            .Property(item => item.RowVersion)
            .OriginalValue = expectedRowVersion.Value;
    }

    private async Task PersistActivationAsync(
        StoreConfig config,
        string previousThemeId,
        string activatedThemeId,
        string traceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await PublishEventAsync(new StorefrontThemeActivatedEvent
            {
                TenantId = config.TenantId,
                StoreConfigId = config.Id,
                PreviousThemeId = previousThemeId,
                ThemeId = activatedThemeId,
                StoreName = config.StoreName,
                ActivatedAtUtc = config.UpdatedAtUtc
            }, traceId, cancellationToken);

            await PublishEventAsync(new StoreConfigUpdatedEvent
            {
                TenantId = config.TenantId,
                StoreConfigId = config.Id,
                ThemeId = config.ThemeId,
                StoreName = config.StoreName,
                UpdatedAtUtc = config.UpdatedAtUtc
            }, traceId, cancellationToken);

            await PublishEventAsync(new TenantUpdatedEvent
            {
                TenantId = config.TenantId,
                StoreName = config.StoreName
            }, traceId, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            _publishTelemetry.RecordPublishSuccess("theme_activation", "outbox_commit");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _publishTelemetry.RecordPublishFailure("theme_activation", "outbox_commit", nameof(DbUpdateConcurrencyException));
            _logger.LogWarning(
                ex,
                "Theme activation concurrency conflict | TenantId: {TenantId} | ConfigId: {ConfigId} | TraceId: {TraceId}",
                config.TenantId,
                config.Id,
                traceId);
            throw new ConcurrencyConflictException("StoreConfigThemeActivation", config.Id, ex);
        }
        catch (Exception ex)
        {
            _publishTelemetry.RecordPublishFailure("theme_activation", "outbox_commit", ex.GetType().Name);
            _logger.LogError(
                ex,
                "Theme activation staging failed | TenantId: {TenantId} | ConfigId: {ConfigId} | TraceId: {TraceId}",
                config.TenantId,
                config.Id,
                traceId);
            throw;
        }
    }

    private async Task PublishEventAsync<T>(
        T @event,
        string traceId,
        CancellationToken cancellationToken)
        where T : IntegrationEvent
    {
        @event.CorrelationId = string.IsNullOrWhiteSpace(@event.CorrelationId) ? traceId : @event.CorrelationId;
        @event.IdempotencyKey = string.IsNullOrWhiteSpace(@event.IdempotencyKey)
            ? @event.EventId.ToString("N")
            : @event.IdempotencyKey;

        await _eventPublisher.PublishAsync(@event, cancellationToken);
    }

    private static string ExtractStorefrontTheme(string? customizationJson, string? fallbackThemeId = null)
    {
        if (!string.IsNullOrWhiteSpace(customizationJson))
        {
            try
            {
                var root = JsonSerializer.Deserialize<Dictionary<string, object>>(customizationJson);
                if (root != null &&
                    root.TryGetValue("storefrontTheme", out var storefrontThemeObj))
                {
                    var storefrontTheme = storefrontThemeObj switch
                    {
                        string value => value,
                        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                        _ => null
                    };

                    if (!string.IsNullOrWhiteSpace(storefrontTheme))
                    {
                        return StorefrontThemes.NormalizeOrDefault(storefrontTheme);
                    }
                }
            }
            catch
            {
                // Malformed customization JSON falls back to the legacy theme id/default.
            }
        }

        return StorefrontThemes.NormalizeOrDefault(fallbackThemeId);
    }

    private static string SetStorefrontThemeInCustomizationJson(
        string? customizationJson,
        string? requestedStorefrontTheme,
        string? fallbackThemeId = null)
    {
        var resolvedStorefrontTheme = !string.IsNullOrWhiteSpace(requestedStorefrontTheme)
            ? StorefrontThemes.NormalizeOrDefault(requestedStorefrontTheme)
            : ExtractStorefrontTheme(customizationJson, fallbackThemeId);

        Dictionary<string, object> root;
        try
        {
            root = JsonSerializer.Deserialize<Dictionary<string, object>>(customizationJson ?? "{}")
                ?? new Dictionary<string, object>();
        }
        catch
        {
            root = new Dictionary<string, object>();
        }

        root["storefrontTheme"] = resolvedStorefrontTheme;
        return JsonSerializer.Serialize(root);
    }
}
