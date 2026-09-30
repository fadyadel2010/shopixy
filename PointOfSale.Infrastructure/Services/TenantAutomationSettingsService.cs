using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Services;

/// <summary>
/// Implementation of tenant automation settings service.
/// Phase: Smart Abandoned Cart Recovery Engine - Foundation
/// </summary>
public class TenantAutomationSettingsService : ITenantAutomationSettingsService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TenantAutomationSettingsService> _logger;

    public TenantAutomationSettingsService(
        ApplicationDbContext context,
        ILogger<TenantAutomationSettingsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<TenantAutomationSettings> GetSettingsAsync(Guid tenantId, CancellationToken ct = default)
    {
        // CRITICAL: IgnoreQueryFilters() because this method is called from:
        // 1. Background jobs (AbandonedCartScannerJob) without TenantContext
        // 2. MassTransit consumers without HTTP context
        // We explicitly filter by tenantId parameter instead of relying on global filter.
        var settings = await _context.Set<TenantAutomationSettings>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (settings == null)
        {
            var country = await _context.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(tenant => tenant.Id == tenantId)
                .Select(tenant => tenant.Country)
                .SingleOrDefaultAsync(ct);
            _logger.LogDebug(
                "[Automation Settings] No settings found for tenant, returning defaults | TenantId: {TenantId}",
                tenantId);

            // Return default settings (not persisted yet)
            return new TenantAutomationSettings
            {
                TenantId = tenantId,
                EnableAbandonedRecovery = false,
                FirstMessageDelayMinutes = 60,
                SecondMessageDelayMinutes = 1440,
                WorkingHoursFrom = new TimeSpan(9, 0, 0),
                WorkingHoursTo = new TimeSpan(21, 0, 0),
                TimeZoneId = AbandonedCartScheduleTime.ResolveTimeZoneId(country)
            };
        }

        return settings;
    }

    public async Task<TenantAutomationSettings> UpdateSettingsAsync(
        Guid tenantId, 
        TenantAutomationSettings settings, 
        CancellationToken ct = default)
    {
        // Validate settings
        ValidateSettings(settings);

        var existing = await _context.Set<TenantAutomationSettings>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (existing == null)
        {
            var country = await _context.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(tenant => tenant.Id == tenantId)
                .Select(tenant => tenant.Country)
                .SingleOrDefaultAsync(ct);
            // Create new
            settings.Id = Guid.NewGuid();
            settings.TenantId = tenantId;
            settings.CreatedAtUtc = DateTime.UtcNow;
            settings.UpdatedAtUtc = DateTime.UtcNow;
            settings.TimeZoneId = AbandonedCartScheduleTime.ResolveTimeZoneId(country);

            _context.Set<TenantAutomationSettings>().Add(settings);

            _logger.LogInformation(
                "[Automation Settings] Created new settings | TenantId: {TenantId} | EnabledRecovery: {Enabled}",
                tenantId, settings.EnableAbandonedRecovery);
        }
        else
        {
            // Update existing
            existing.EnableAbandonedRecovery = settings.EnableAbandonedRecovery;
            existing.FirstMessageDelayMinutes = settings.FirstMessageDelayMinutes;
            existing.SecondMessageDelayMinutes = settings.SecondMessageDelayMinutes;
            existing.WorkingHoursFrom = settings.WorkingHoursFrom;
            existing.WorkingHoursTo = settings.WorkingHoursTo;
            existing.UpdatedAtUtc = DateTime.UtcNow;

            _logger.LogInformation(
                "[Automation Settings] Updated settings | TenantId: {TenantId} | EnabledRecovery: {Enabled}",
                tenantId, existing.EnableAbandonedRecovery);

            settings = existing;
        }

        await _context.SaveChangesAsync(ct);

        return settings;
    }

    public async Task<bool> ExistsAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _context.Set<TenantAutomationSettings>()
            .IgnoreQueryFilters()
            .AnyAsync(s => s.TenantId == tenantId, ct);
    }

    private void ValidateSettings(TenantAutomationSettings settings)
    {
        // Validate first message delay
        if (settings.FirstMessageDelayMinutes < 0)
        {
            throw new ArgumentException("First message delay cannot be negative", nameof(settings.FirstMessageDelayMinutes));
        }

        if (settings.FirstMessageDelayMinutes > 10080) // 7 days
        {
            throw new ArgumentException("First message delay cannot exceed 7 days (10080 minutes)", nameof(settings.FirstMessageDelayMinutes));
        }

        // Validate second message delay
        if (settings.SecondMessageDelayMinutes < 0)
        {
            throw new ArgumentException("Second message delay cannot be negative", nameof(settings.SecondMessageDelayMinutes));
        }

        if (settings.SecondMessageDelayMinutes > 20160) // 14 days
        {
            throw new ArgumentException("Second message delay cannot exceed 14 days (20160 minutes)", nameof(settings.SecondMessageDelayMinutes));
        }

        // Validate working hours
        if (settings.WorkingHoursFrom == settings.WorkingHoursTo)
        {
            throw new ArgumentException("Working hours must define a non-empty sending window", nameof(settings.WorkingHoursFrom));
        }

        if (settings.WorkingHoursFrom.TotalHours < 0 || settings.WorkingHoursFrom.TotalHours >= 24)
        {
            throw new ArgumentException("Working hours 'from' must be between 00:00 and 23:59", nameof(settings.WorkingHoursFrom));
        }

        if (settings.WorkingHoursTo.TotalHours <= 0 || settings.WorkingHoursTo.TotalHours > 24)
        {
            throw new ArgumentException("Working hours 'to' must be between 00:01 and 24:00", nameof(settings.WorkingHoursTo));
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException("Recovery time zone is invalid", nameof(settings.TimeZoneId));
        }
    }
}
