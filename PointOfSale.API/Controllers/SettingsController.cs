using System.Text.Json;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Redis.Services;
using StackExchange.Redis;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SettingsController : ControllerBase
{
    private static readonly IReadOnlyDictionary<string, string> CurrencyAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["$"] = "USD",
        ["US$"] = "USD",
        ["USD"] = "USD",
        ["\u20AC"] = "EUR",
        ["EUR"] = "EUR",
        ["\u00A3"] = "GBP",
        ["GBP"] = "GBP",
        ["E\u00A3"] = "EGP",
        ["LE"] = "EGP",
        ["\u062C.\u0645"] = "EGP",
        ["EGP"] = "EGP"
    };

    private static readonly IReadOnlyDictionary<string, string> CurrencySymbols = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["USD"] = "$",
        ["EUR"] = "\u20AC",
        ["GBP"] = "\u00A3",
        ["EGP"] = "E\u00A3"
    };

    private readonly ApplicationDbContext _context;
    private readonly ILogger<SettingsController> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly IConnectionMultiplexer? _redis;
    private readonly IStoreConfigCacheService? _storeConfigCache;

    // Redis cache key prefix for tenant country
    private const string TENANT_COUNTRY_CACHE_PREFIX = "tenant:country:";

    public SettingsController(
        ApplicationDbContext context, 
        ILogger<SettingsController> logger,
        ITenantContext tenantContext,
        IEventPublisher eventPublisher,
        IStoreConfigCacheService? storeConfigCache = null,
        IConnectionMultiplexer? redis = null)
    {
        _context = context;
        _logger = logger;
        _tenantContext = tenantContext;
        _eventPublisher = eventPublisher;
        _storeConfigCache = storeConfigCache;
        _redis = redis;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<SettingsDto>> GetSettings()
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;

            var tenant = await _context.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => new
                {
                    t.Name,
                    t.ContactEmail,
                    t.Currency
                })
                .FirstOrDefaultAsync();

            if (tenant == null)
            {
                return NotFound(new { message = "Tenant not found" });
            }

            var dict = await LoadActiveTenantSettingsDictionaryAsync(tenantId);
            
            // Tenant.Currency is the authority; settings are derived display state.
            var fallbackCurrency = FinancialCurrencyGuard.RequireCurrency(
                tenant.Currency,
                "tenant.currency",
                $"tenantId:{tenantId:D};controller:SettingsController.GetSettings");

            var currencyCode = fallbackCurrency;
            try
            {
                currencyCode = ResolveStoredSettingsCurrencyCode(
                    GetValue(dict, "Tax.CurrencyCode", fallbackCurrency),
                    fallbackCurrency);
            }
            catch (Exception ex) when (ex is FinancialCurrencyMissingException or FinancialCurrencyInvalidException)
            {
                // Legacy settings value failed validation; fall back to Tenant.Currency authority.
                _logger.LogWarning(
                    ex,
                    "Stored currency code failed validation, using fallback | TenantId: {TenantId} | Fallback: {Fallback}",
                    tenantId,
                    fallbackCurrency);
            }

            var dto = new SettingsDto
            {
                CompanyName = GetValue(dict, "Company.Name", tenant.Name),
                CompanyAddress = GetValue(dict, "Company.Address", ""),
                CompanyPhone = GetValue(dict, "Company.Phone", ""),
                CompanyEmail = GetValue(dict, "Company.Email", tenant.ContactEmail ?? string.Empty),
                CompanyLogo = GetValue(dict, "Company.Logo", null),

                CurrencyCode = currencyCode,
                CurrencySymbol = ResolveDisplayCurrencySymbol(GetValue(dict, "Tax.CurrencySymbol", string.Empty), currencyCode),
                EnableTax = ParseStoredBoolean(GetValue(dict, "Tax.Enable", "true"), defaultValue: true),
                TaxRate = ParseStoredDecimal(GetValue(dict, "Tax.Rate", "0"), defaultValue: 0m),
                
                ReceiptHeader = GetValue(dict, "Receipt.Header", "Welcome"),
                ReceiptFooter = GetValue(dict, "Receipt.Footer", "Thank you for your visit"),
                ShowLogoOnReceipt = ParseStoredBoolean(GetValue(dict, "Receipt.ShowLogo", "true"), defaultValue: true)
            };

            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving settings");
            return StatusCode(500, new { message = "Error retrieving settings" });
        }
    }

    [HttpPut]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<SettingsDto>> UpdateSettings(SettingsDto dto)
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;

            var correlationId = HttpContext.TraceIdentifier;
            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
            {
                return NotFound(new { message = "Tenant not found" });
            }

            var normalizedCurrencyCode = ResolveRequestedSettingsCurrencyCode(
                !string.IsNullOrWhiteSpace(dto.CurrencyCode) ? dto.CurrencyCode : dto.CurrencySymbol,
                tenant.Currency);
            var displayCurrencySymbol = ResolveDisplayCurrencySymbol(dto.CurrencySymbol, normalizedCurrencyCode);
            
            await UpdateSettingAsync(tenant.Id, "Company.Name", dto.CompanyName, "Company Profile");
            await UpdateSettingAsync(tenant.Id, "Company.Address", dto.CompanyAddress, "Company Profile");
            await UpdateSettingAsync(tenant.Id, "Company.Phone", dto.CompanyPhone, "Company Profile");
            await UpdateSettingAsync(tenant.Id, "Company.Email", dto.CompanyEmail, "Company Profile");
            if (dto.CompanyLogo != null) await UpdateSettingAsync(tenant.Id, "Company.Logo", dto.CompanyLogo, "Company Profile");

            await UpdateSettingAsync(tenant.Id, "Tax.CurrencyCode", normalizedCurrencyCode, "Tax & Currency");
            await UpdateSettingAsync(tenant.Id, "Tax.CurrencySymbol", displayCurrencySymbol, "Tax & Currency");
            await UpdateSettingAsync(tenant.Id, "Tax.Enable", dto.EnableTax.ToString(), "Tax & Currency");
            await UpdateSettingAsync(tenant.Id, "Tax.Rate", dto.TaxRate.ToString(CultureInfo.InvariantCulture), "Tax & Currency");

            await UpdateSettingAsync(tenant.Id, "Receipt.Header", dto.ReceiptHeader, "Receipt Settings");
            await UpdateSettingAsync(tenant.Id, "Receipt.Footer", dto.ReceiptFooter, "Receipt Settings");
            await UpdateSettingAsync(tenant.Id, "Receipt.ShowLogo", dto.ShowLogoOnReceipt.ToString(), "Receipt Settings");

            // =================================================================
            // PHASE 10.4.X: Currency Authority Lockdown
            // Also update Tenant.Currency and publish TenantCurrencyChangedEvent
            // This ensures Storefront currency stays in sync
            // Compare against Tenant.Currency (not SystemSettings) to ensure
            // MongoDB gets updated even if settings were saved before new code
            // S40: Event is now enqueued to outbox BEFORE SaveChanges for atomicity
            // =================================================================
            string? previousCurrency = null;
            Guid? eventAggregateId = null;
            
            if (tenant != null)
            {
                previousCurrency = tenant.Currency;
                    
                // Only sync if tenant currency differs from the requested currency
                if (!string.Equals(previousCurrency, normalizedCurrencyCode, StringComparison.OrdinalIgnoreCase))
                {
                    var currencyChangedAtUtc = DateTime.UtcNow;
                    tenant.Currency = normalizedCurrencyCode;
                    tenant.UpdatedAtUtc = currencyChangedAtUtc;
                    eventAggregateId = tenant.Id;
                        
                    _logger.LogInformation(
                        "Syncing currency to Tenant | TenantId: {TenantId} | OldCurrency: {OldCurrency} | NewCurrency: {NewCurrency} | CorrelationId: {CorrelationId}",
                        tenantId, previousCurrency, normalizedCurrencyCode, correlationId);
                        
                    // S40: Enqueue TenantCurrencyChangedEvent to outbox for atomic publish
                    // Event will be published by background processor after commit succeeds
                    var currencyChangedEvent = new TenantCurrencyChangedEvent
                    {
                        TenantId = tenant.Id,
                        Currency = normalizedCurrencyCode,
                        PreviousCurrency = previousCurrency,
                        TenantSlug = tenant.Slug,
                        CorrelationId = correlationId,
                        OccurredAtUtc = currencyChangedAtUtc,
                        EventVersion = Math.Max(1, (int)Math.Min(int.MaxValue, new DateTimeOffset(currencyChangedAtUtc).ToUnixTimeSeconds()))
                    };

                    var outboxMessage = new PointOfSale.Core.Entities.OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        EventType = typeof(TenantCurrencyChangedEvent).FullName ?? nameof(TenantCurrencyChangedEvent),
                        EventPayload = JsonSerializer.Serialize(currencyChangedEvent),
                        TenantId = tenant.Id,
                        AggregateId = tenant.Id,
                        EventVersion = currencyChangedEvent.EventVersion,
                        CreatedAtUtc = currencyChangedAtUtc
                    };

                    _context.OutboxMessages.Add(outboxMessage);

                    _logger.LogInformation(
                        "TenantCurrencyChangedEvent enqueued to outbox | TenantId: {TenantId} | Currency: {Currency} | OutboxMessageId: {OutboxMessageId} | CorrelationId: {CorrelationId}",
                        tenant.Id, normalizedCurrencyCode, outboxMessage.Id, correlationId);
                }
                else
                {
                    _logger.LogDebug(
                        "Currency unchanged, skipping Tenant sync | TenantId: {TenantId} | Currency: {Currency}",
                        tenant.Id, normalizedCurrencyCode);
                }
            }

            // S40: Commit all changes atomically (tenant update + outbox message)
            await _context.SaveChangesAsync();

            // S40: Log successful commit with event details
            if (eventAggregateId.HasValue)
            {
                _logger.LogInformation(
                    "Currency change committed successfully | TenantId: {TenantId} | OldCurrency: {OldCurrency} | NewCurrency: {NewCurrency} | CorrelationId: {CorrelationId}",
                    eventAggregateId.Value, previousCurrency, normalizedCurrencyCode, correlationId);
            }

            dto.CurrencyCode = normalizedCurrencyCode;
            dto.CurrencySymbol = displayCurrencySymbol;
            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating settings");
            return StatusCode(500, new { message = "Error updating settings" });
        }
    }

    // ==========================================================================
    // PHONE NUMBER NORMALIZATION: Default Country Configuration
    // ==========================================================================

    /// <summary>
    /// Get the tenant's default country for phone number normalization.
    /// Used by WhatsApp for converting local phone numbers to E.164 format.
    /// </summary>
    [HttpGet("default-country")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<TenantCountryDto>> GetDefaultCountry()
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;

            var tenant = await _context.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => new { t.Country, t.Name })
                .FirstOrDefaultAsync();

            if (tenant == null)
            {
                return NotFound(new { message = "Tenant not found" });
            }

            return Ok(new TenantCountryDto
            {
                CountryCode = tenant.Country,
                TenantName = tenant.Name,
                IsConfigured = !string.IsNullOrEmpty(tenant.Country)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving default country");
            return StatusCode(500, new { message = "Error retrieving default country" });
        }
    }

    /// <summary>
    /// Set the tenant's default country for phone number normalization.
    /// This country is used to parse local phone numbers for WhatsApp messaging.
    /// </summary>
    [HttpPut("default-country")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<TenantCountryDto>> SetDefaultCountry([FromBody] SetDefaultCountryRequest request)
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;

            // Validate country code format (ISO 3166-1 alpha-2)
            if (!CountryCodeHelper.TryNormalizeCountryCode(request.CountryCode, out var normalizedCountryCode))
            {
                return BadRequest(new { 
                    message = "Invalid country code. Please use ISO 3166-1 alpha-2 format (e.g., 'EG', 'SA', 'US')." 
                });
            }

            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
            {
                return NotFound(new { message = "Tenant not found" });
            }

            var previousCountry = tenant.Country;
            tenant.Country = normalizedCountryCode;
            tenant.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Invalidate Redis cache for phone normalizer
            await InvalidateTenantCountryCacheAsync(tenantId);
            await InvalidateStorefrontConfigCacheAsync(tenantId);

            _logger.LogInformation(
                "[Settings] Default country updated | TenantId: {TenantId} | OldCountry: {OldCountry} | NewCountry: {NewCountry}",
                tenantId, previousCountry, normalizedCountryCode);

            return Ok(new TenantCountryDto
            {
                CountryCode = normalizedCountryCode,
                TenantName = tenant.Name,
                IsConfigured = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting default country");
            return StatusCode(500, new { message = "Error setting default country" });
        }
    }

    /// <summary>
    /// Invalidate Redis cache for tenant country when updated.
    /// </summary>
    private async Task InvalidateTenantCountryCacheAsync(Guid tenantId)
    {
        if (_redis == null) return;

        try
        {
            var db = _redis.GetDatabase();
            var key = $"{TENANT_COUNTRY_CACHE_PREFIX}{tenantId}";
            await db.KeyDeleteAsync(key);
            _logger.LogDebug("[Settings] Invalidated tenant country cache | TenantId: {TenantId}", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Settings] Failed to invalidate tenant country cache");
        }
    }

    private async Task InvalidateStorefrontConfigCacheAsync(Guid tenantId)
    {
        if (_storeConfigCache == null)
        {
            return;
        }

        try
        {
            await _storeConfigCache.InvalidateStoreConfigAsync(tenantId);
            _logger.LogDebug("[Settings] Invalidated storefront config cache after country update | TenantId: {TenantId}", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Settings] Failed to invalidate storefront config cache after country update");
        }
    }

    private static string? GetValue(IReadOnlyDictionary<string, string?> dict, string key, string? defaultValue)
    {
        return dict.ContainsKey(key) ? dict[key] : defaultValue;
    }

    private static string ResolveStoredSettingsCurrencyCode(string? currency, string fallbackCurrency)
    {
        var candidate = string.IsNullOrWhiteSpace(currency) ? fallbackCurrency : currency.Trim();
        if (CurrencyAliases.TryGetValue(candidate, out var mappedCurrency))
        {
            candidate = mappedCurrency;
        }

        return FinancialCurrencyGuard.RequireCurrency(
            candidate,
            "settings.currencyCode",
            "settings.read");
    }

    private static string ResolveRequestedSettingsCurrencyCode(string? currency, string? fallbackCurrency)
    {
        var candidate = string.IsNullOrWhiteSpace(currency) ? fallbackCurrency : currency.Trim();
        if (!string.IsNullOrWhiteSpace(candidate) && CurrencyAliases.TryGetValue(candidate, out var mappedCurrency))
        {
            candidate = mappedCurrency;
        }

        return FinancialCurrencyGuard.RequireCurrency(
            candidate,
            "settings.currencyCode",
            "settings.update");
    }

    private static string ResolveDisplayCurrencySymbol(string? requestedSymbol, string currencyCode)
    {
        if (!string.IsNullOrWhiteSpace(requestedSymbol)
            && !CurrencyAliases.ContainsKey(requestedSymbol.Trim().ToUpperInvariant()))
        {
            return requestedSymbol.Trim();
        }

        return CurrencySymbols.TryGetValue(currencyCode, out var symbol)
            ? symbol
            : currencyCode;
    }

    private static bool ParseStoredBoolean(string? rawValue, bool defaultValue)
    {
        return bool.TryParse(rawValue, out var parsed) ? parsed : defaultValue;
    }

    private static decimal ParseStoredDecimal(string? rawValue, decimal defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        var trimmed = rawValue.Trim();
        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed))
        {
            return parsed;
        }

        var normalized = trimmed.Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed)
            ? parsed
            : defaultValue;
    }

    private async Task UpdateSettingAsync(Guid tenantId, string key, string value, string category)
    {
        var settings = await _context.SystemSettings
            .IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.TenantId == tenantId && s.Key == key)
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .ThenByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .ToListAsync();

        if (settings.Count > 1)
        {
            _logger.LogWarning(
                "Detected duplicate active tenant settings rows while updating setting; newest row will be used | TenantId: {TenantId} | Key: {Key} | DuplicateCount: {DuplicateCount}",
                tenantId,
                key,
                settings.Count);
        }

        var setting = settings.FirstOrDefault();

        if (setting == null)
        {
            setting = new SystemSetting
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Key = key,
                Value = value,
                Category = category,
                CreatedAt = DateTime.UtcNow
            };
            _context.SystemSettings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
    }

    private async Task<Dictionary<string, string?>> LoadActiveTenantSettingsDictionaryAsync(Guid tenantId)
    {
        var settings = await _context.SystemSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(setting => !setting.IsDeleted && setting.TenantId == tenantId)
            .OrderBy(setting => setting.Key)
            .ThenByDescending(setting => setting.UpdatedAt ?? setting.CreatedAt)
            .ThenByDescending(setting => setting.CreatedAt)
            .ThenByDescending(setting => setting.Id)
            .ToListAsync();

        var malformedKeyRows = settings
            .Where(setting => string.IsNullOrWhiteSpace(setting.Key))
            .ToList();

        if (malformedKeyRows.Count > 0)
        {
            _logger.LogWarning(
                "Ignoring malformed tenant settings rows with null or whitespace keys | TenantId: {TenantId} | RowCount: {RowCount}",
                tenantId,
                malformedKeyRows.Count);
        }

        var normalizedSettings = settings
            .Where(setting => !string.IsNullOrWhiteSpace(setting.Key))
            .Select(setting => new
            {
                Key = setting.Key.Trim(),
                setting.Value,
                setting.Id,
                setting.CreatedAt,
                setting.UpdatedAt
            })
            .ToList();

        var duplicateGroups = normalizedSettings
            .GroupBy(setting => setting.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToList();

        foreach (var duplicateGroup in duplicateGroups)
        {
            _logger.LogWarning(
                "Detected duplicate active tenant settings rows while reading settings; newest row will be used | TenantId: {TenantId} | Key: {Key} | DuplicateCount: {DuplicateCount}",
                tenantId,
                duplicateGroup.Key,
                duplicateGroup.Count());
        }

        return normalizedSettings
            .GroupBy(setting => setting.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value,
                StringComparer.Ordinal);
    }
}

/// <summary>
/// DTO for tenant default country configuration.
/// </summary>
public class TenantCountryDto
{
    /// <summary>
    /// ISO 3166-1 alpha-2 country code (e.g., "EG", "SA", "US").
    /// </summary>
    public string? CountryCode { get; set; }

    /// <summary>
    /// Tenant name for display purposes.
    /// </summary>
    public string? TenantName { get; set; }

    /// <summary>
    /// Whether a default country is configured.
    /// </summary>
    public bool IsConfigured { get; set; }
}

/// <summary>
/// Request to set the tenant's default country.
/// </summary>
public class SetDefaultCountryRequest
{
    /// <summary>
    /// ISO 3166-1 alpha-2 country code (e.g., "EG", "SA", "US").
    /// </summary>
    public string CountryCode { get; set; } = string.Empty;
}
