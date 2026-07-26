using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin carrier configuration API.
/// 
/// CARRIER-AGNOSTIC SHIPPING MODULE
/// 
/// Endpoints for managing carrier configurations:
/// - Enable/disable carriers per tenant
/// - Configure API credentials
/// - Set pricing and service levels
/// - Validate carrier connections
/// </summary>
[ApiController]
[Route("api/admin/carriers")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminCarrierConfigController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IShippingCarrierRegistry _carrierRegistry;
    private readonly ICarrierCredentialEncryptionService _credentialEncryption;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminCarrierConfigController> _logger;
    
    public AdminCarrierConfigController(
        ApplicationDbContext dbContext,
        IShippingCarrierRegistry carrierRegistry,
        ICarrierCredentialEncryptionService credentialEncryption,
        ITenantContext tenantContext,
        ILogger<AdminCarrierConfigController> logger)
    {
        _dbContext = dbContext;
        _carrierRegistry = carrierRegistry;
        _credentialEncryption = credentialEncryption;
        _tenantContext = tenantContext;
        _logger = logger;
    }
    
    /// <summary>
    /// Get all supported carriers (registered adapters).
    /// </summary>
    [HttpGet("supported")]
    public ActionResult<List<SupportedCarrierDto>> GetSupportedCarriers()
    {
        var adapters = _carrierRegistry.GetAllAdapters();
        
        var carriers = adapters.Select(a => new SupportedCarrierDto
        {
            CarrierCode = a.CarrierCode,
            DisplayName = a.DisplayName,
            ServiceLevels = a.GetSupportedServiceLevels().Select(s => new ServiceLevelDto
            {
                Code = s.Code,
                Name = s.Name,
                Description = s.Description,
                TypicalDaysMin = s.TypicalDaysMin,
                TypicalDaysMax = s.TypicalDaysMax
            }).ToList()
        }).ToList();
        
        return Ok(carriers);
    }
    
    /// <summary>
    /// Get carrier configurations for tenant.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<CarrierConfigurationDto>>> GetConfigurations(
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        
        var configs = await _dbContext.Set<CarrierConfiguration>()
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.SortOrder)
            .ToListAsync(cancellationToken);
        
        // Also include supported carriers not yet configured
        var configuredCodes = configs.Select(c => c.CarrierCode).ToHashSet();
        var supportedAdapters = _carrierRegistry.GetAllAdapters();
        
        var result = configs.Select(MapToDto).ToList();
        
        // Add unconfigured carriers as templates
        foreach (var adapter in supportedAdapters)
        {
            if (!configuredCodes.Contains(adapter.CarrierCode))
            {
                result.Add(new CarrierConfigurationDto
                {
                    Id = null,
                    CarrierCode = adapter.CarrierCode,
                    DisplayName = adapter.DisplayName,
                    IsEnabled = false,
                    IsDefault = false,
                    IsConfigured = false,
                    ServiceLevels = adapter.GetSupportedServiceLevels().Select(s => new ServiceLevelDto
                    {
                        Code = s.Code,
                        Name = s.Name,
                        Description = s.Description,
                        TypicalDaysMin = s.TypicalDaysMin,
                        TypicalDaysMax = s.TypicalDaysMax
                    }).ToList()
                });
            }
        }
        
        return Ok(result);
    }
    
    /// <summary>
    /// Get single carrier configuration.
    /// </summary>
    [HttpGet("{carrierCode}")]
    public async Task<ActionResult<CarrierConfigurationDto>> GetConfiguration(
        string carrierCode,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        
        var config = await _dbContext.Set<CarrierConfiguration>()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CarrierCode == carrierCode, cancellationToken);
        
        if (config == null)
        {
            // Return template for unconfigured carrier
            var adapter = _carrierRegistry.GetAdapter(carrierCode);
            if (adapter == null)
            {
                return NotFound(new { error = "Carrier not found" });
            }
            
            return Ok(new CarrierConfigurationDto
            {
                Id = null,
                CarrierCode = adapter.CarrierCode,
                DisplayName = adapter.DisplayName,
                IsEnabled = false,
                IsDefault = false,
                IsConfigured = false,
                ServiceLevels = adapter.GetSupportedServiceLevels().Select(s => new ServiceLevelDto
                {
                    Code = s.Code,
                    Name = s.Name,
                    Description = s.Description,
                    TypicalDaysMin = s.TypicalDaysMin,
                    TypicalDaysMax = s.TypicalDaysMax
                }).ToList()
            });
        }
        
        return Ok(MapToDto(config));
    }
    
    /// <summary>
    /// Create or update carrier configuration.
    /// </summary>
    [HttpPut("{carrierCode}")]
    public async Task<ActionResult<CarrierConfigurationDto>> UpsertConfiguration(
        string carrierCode,
        [FromBody] UpsertCarrierConfigRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        
        // Validate carrier exists
        var adapter = _carrierRegistry.GetAdapter(carrierCode);
        if (adapter == null)
        {
            return BadRequest(new { error = $"Carrier '{carrierCode}' is not supported" });
        }
        
        var config = await _dbContext.Set<CarrierConfiguration>()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CarrierCode == carrierCode, cancellationToken);
        
        var isNew = config == null;
        
        if (isNew)
        {
            config = new CarrierConfiguration
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CarrierCode = carrierCode,
                DisplayName = adapter.DisplayName,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Set<CarrierConfiguration>().Add(config);
        }
        
        // Update fields
        if (request.DisplayName != null)
            config!.DisplayName = request.DisplayName;
        if (request.IsEnabled.HasValue)
            config!.IsEnabled = request.IsEnabled.Value;
        if (request.ApiKey != null)
            config!.ApiKey = _credentialEncryption.Encrypt(request.ApiKey);
        if (request.ApiSecret != null)
            config!.ApiSecret = _credentialEncryption.Encrypt(request.ApiSecret);
        if (request.AccountId != null)
            config!.AccountId = request.AccountId;
        if (request.ApiUrl != null)
            config!.ApiUrl = request.ApiUrl;
        if (request.UseSandbox.HasValue)
            config!.UseSandbox = request.UseSandbox.Value;
        if (request.SupportsCod.HasValue)
            config!.SupportsCod = request.SupportsCod.Value;
        if (request.SupportsTracking.HasValue)
            config!.SupportsTracking = request.SupportsTracking.Value;
        if (request.SupportsWebhooks.HasValue)
            config!.SupportsWebhooks = request.SupportsWebhooks.Value;
        if (request.SupportsLabels.HasValue)
            config!.SupportsLabels = request.SupportsLabels.Value;
        if (request.Configuration != null)
            config!.Configuration = request.Configuration;
        if (request.PickupAddress != null)
            config!.PickupAddress = request.PickupAddress;
        if (request.PickupCity != null)
            config!.PickupCity = request.PickupCity;
        if (request.PickupCountry != null)
            config!.PickupCountry = request.PickupCountry;
        if (request.PickupPhone != null)
            config!.PickupPhone = request.PickupPhone;
        if (request.PickupContactName != null)
            config!.PickupContactName = request.PickupContactName;
        if (request.DefaultServiceLevel != null)
            config!.DefaultServiceLevel = request.DefaultServiceLevel;
        if (request.UseCarrierRates.HasValue)
            config!.UseCarrierRates = request.UseCarrierRates.Value;
        if (request.FixedShippingCost.HasValue)
            config!.FixedShippingCost = request.FixedShippingCost;
        if (request.MarkupPercentage.HasValue)
            config!.MarkupPercentage = request.MarkupPercentage.Value;
        if (request.FreeShippingThreshold.HasValue)
            config!.FreeShippingThreshold = request.FreeShippingThreshold;
        if (request.SortOrder.HasValue)
            config!.SortOrder = request.SortOrder.Value;
        if (request.Notes != null)
            config!.Notes = request.Notes;
        
        // Handle default carrier flag
        if (request.IsDefault.HasValue && request.IsDefault.Value)
        {
            // Unset other defaults
            var others = await _dbContext.Set<CarrierConfiguration>()
                .Where(c => c.TenantId == tenantId && c.CarrierCode != carrierCode && c.IsDefault)
                .ToListAsync(cancellationToken);
            foreach (var other in others)
            {
                other.IsDefault = false;
            }
            config!.IsDefault = true;
        }
        else if (request.IsDefault.HasValue)
        {
            config!.IsDefault = false;
        }
        
        config!.UpdatedAt = DateTime.UtcNow;
        
        await _dbContext.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation(
            "{Action} carrier configuration for {CarrierCode} (tenant {TenantId})",
            isNew ? "Created" : "Updated", carrierCode, tenantId);
        
        return Ok(MapToDto(config));
    }
    
    /// <summary>
    /// Toggle carrier enabled status.
    /// </summary>
    [HttpPost("{carrierCode}/toggle")]
    public async Task<ActionResult<CarrierConfigurationDto>> ToggleEnabled(
        string carrierCode,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        
        var config = await _dbContext.Set<CarrierConfiguration>()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CarrierCode == carrierCode, cancellationToken);
        
        if (config == null)
        {
            return NotFound(new { error = "Carrier configuration not found" });
        }
        
        config.IsEnabled = !config.IsEnabled;
        config.UpdatedAt = DateTime.UtcNow;
        
        await _dbContext.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation(
            "Toggled carrier {CarrierCode} to {Status} (tenant {TenantId})",
            carrierCode, config.IsEnabled ? "enabled" : "disabled", tenantId);
        
        return Ok(MapToDto(config));
    }
    
    /// <summary>
    /// Validate carrier credentials.
    /// </summary>
    [HttpPost("{carrierCode}/validate")]
    public async Task<ActionResult<ValidateCredentialsResponseDto>> ValidateCredentials(
        string carrierCode,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        
        var config = await _dbContext.Set<CarrierConfiguration>()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CarrierCode == carrierCode, cancellationToken);
        
        if (config == null)
        {
            return NotFound(new { error = "Carrier configuration not found" });
        }
        
        var adapter = _carrierRegistry.GetAdapter(carrierCode);
        if (adapter == null)
        {
            return BadRequest(new { error = "Carrier adapter not found" });
        }
        
        var credentials = new CarrierCredentials
        {
            ApiKey = _credentialEncryption.Decrypt(config.ApiKey),
            ApiSecret = _credentialEncryption.Decrypt(config.ApiSecret),
            AccountId = config.AccountId,
            ApiUrl = config.ApiUrl,
            UseSandbox = config.UseSandbox
        };

        if (!string.IsNullOrWhiteSpace(config.Configuration))
        {
            try
            {
                credentials.ExtraConfig =
                    System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(config.Configuration)
                    ?? new Dictionary<string, string>();
            }
            catch (System.Text.Json.JsonException)
            {
                credentials.ExtraConfig = new Dictionary<string, string>();
            }
        }
        
        var result = await adapter.ValidateCredentialsAsync(credentials, cancellationToken);
        
        // Update config with validation result
        config.LastValidatedAtUtc = DateTime.UtcNow;
        config.LastValidationSucceeded = result.Success;
        config.LastValidationError = result.ErrorMessage;
        config.UpdatedAt = DateTime.UtcNow;
        
        await _dbContext.SaveChangesAsync(cancellationToken);
        
        return Ok(new ValidateCredentialsResponseDto
        {
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            AccountName = result.AccountName
        });
    }
    
    /// <summary>
    /// Delete carrier configuration.
    /// </summary>
    [HttpDelete("{carrierCode}")]
    public async Task<IActionResult> DeleteConfiguration(
        string carrierCode,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        
        var config = await _dbContext.Set<CarrierConfiguration>()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CarrierCode == carrierCode, cancellationToken);
        
        if (config == null)
        {
            return NotFound(new { error = "Carrier configuration not found" });
        }
        
        _dbContext.Set<CarrierConfiguration>().Remove(config);
        await _dbContext.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation(
            "Deleted carrier configuration for {CarrierCode} (tenant {TenantId})",
            carrierCode, tenantId);
        
        return NoContent();
    }
    
    #region Helpers
    
    private Guid GetTenantId()
    {
        return _tenantContext.RequiredTenantId;
    }
    
    private CarrierConfigurationDto MapToDto(CarrierConfiguration config)
    {
        var adapter = _carrierRegistry.GetAdapter(config.CarrierCode);
        
        return new CarrierConfigurationDto
        {
            Id = config.Id,
            CarrierCode = config.CarrierCode,
            DisplayName = config.DisplayName,
            IsEnabled = config.IsEnabled,
            IsDefault = config.IsDefault,
            IsConfigured = !string.IsNullOrEmpty(config.ApiKey),
            HasApiKey = !string.IsNullOrEmpty(config.ApiKey),
            UseSandbox = config.UseSandbox,
            SupportsCod = config.SupportsCod,
            SupportsTracking = config.SupportsTracking,
            SupportsWebhooks = config.SupportsWebhooks,
            SupportsLabels = config.SupportsLabels,
            PickupAddress = config.PickupAddress,
            PickupCity = config.PickupCity,
            PickupCountry = config.PickupCountry,
            PickupPhone = config.PickupPhone,
            PickupContactName = config.PickupContactName,
            DefaultServiceLevel = config.DefaultServiceLevel,
            UseCarrierRates = config.UseCarrierRates,
            FixedShippingCost = config.FixedShippingCost,
            MarkupPercentage = config.MarkupPercentage,
            FreeShippingThreshold = config.FreeShippingThreshold,
            SortOrder = config.SortOrder,
            Notes = config.Notes,
            LastValidatedAtUtc = config.LastValidatedAtUtc,
            LastValidationSucceeded = config.LastValidationSucceeded,
            LastValidationError = config.LastValidationError,
            ServiceLevels = adapter?.GetSupportedServiceLevels().Select(s => new ServiceLevelDto
            {
                Code = s.Code,
                Name = s.Name,
                Description = s.Description,
                TypicalDaysMin = s.TypicalDaysMin,
                TypicalDaysMax = s.TypicalDaysMax
            }).ToList() ?? new List<ServiceLevelDto>()
        };
    }
    
    #endregion
}

#region DTOs

public class SupportedCarrierDto
{
    public required string CarrierCode { get; set; }
    public required string DisplayName { get; set; }
    public List<ServiceLevelDto> ServiceLevels { get; set; } = new();
}

public class ServiceLevelDto
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int TypicalDaysMin { get; set; }
    public int TypicalDaysMax { get; set; }
}

public class CarrierConfigurationDto
{
    public Guid? Id { get; set; }
    public required string CarrierCode { get; set; }
    public required string DisplayName { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }
    public bool IsConfigured { get; set; }
    public bool HasApiKey { get; set; }
    public bool UseSandbox { get; set; }
    public bool SupportsCod { get; set; }
    public bool SupportsTracking { get; set; }
    public bool SupportsWebhooks { get; set; }
    public bool SupportsLabels { get; set; }
    public string? PickupAddress { get; set; }
    public string? PickupCity { get; set; }
    public string? PickupCountry { get; set; }
    public string? PickupPhone { get; set; }
    public string? PickupContactName { get; set; }
    public string DefaultServiceLevel { get; set; } = "standard";
    public bool UseCarrierRates { get; set; }
    public decimal? FixedShippingCost { get; set; }
    public decimal MarkupPercentage { get; set; }
    public decimal? FreeShippingThreshold { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
    public DateTime? LastValidatedAtUtc { get; set; }
    public bool? LastValidationSucceeded { get; set; }
    public string? LastValidationError { get; set; }
    public List<ServiceLevelDto> ServiceLevels { get; set; } = new();
}

public class UpsertCarrierConfigRequest
{
    public string? DisplayName { get; set; }
    public bool? IsEnabled { get; set; }
    public bool? IsDefault { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? AccountId { get; set; }
    public string? ApiUrl { get; set; }
    public bool? UseSandbox { get; set; }
    public bool? SupportsCod { get; set; }
    public bool? SupportsTracking { get; set; }
    public bool? SupportsWebhooks { get; set; }
    public bool? SupportsLabels { get; set; }
    public string? Configuration { get; set; }
    public string? PickupAddress { get; set; }
    public string? PickupCity { get; set; }
    public string? PickupCountry { get; set; }
    public string? PickupPhone { get; set; }
    public string? PickupContactName { get; set; }
    public string? DefaultServiceLevel { get; set; }
    public bool? UseCarrierRates { get; set; }
    public decimal? FixedShippingCost { get; set; }
    public decimal? MarkupPercentage { get; set; }
    public decimal? FreeShippingThreshold { get; set; }
    public int? SortOrder { get; set; }
    public string? Notes { get; set; }
}

public class ValidateCredentialsResponseDto
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? AccountName { get; set; }
}

#endregion
