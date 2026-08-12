using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Constants;
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
    private readonly ICarrierCapabilityRegistry _carrierCapabilityRegistry;
    private readonly ICarrierIntegrationAuthority _carrierIntegrationAuthority;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminCarrierConfigController> _logger;
    
    public AdminCarrierConfigController(
        ApplicationDbContext dbContext,
        IShippingCarrierRegistry carrierRegistry,
        ICarrierCapabilityRegistry carrierCapabilityRegistry,
        ICarrierIntegrationAuthority carrierIntegrationAuthority,
        ITenantContext tenantContext,
        ILogger<AdminCarrierConfigController> logger)
    {
        _dbContext = dbContext;
        _carrierRegistry = carrierRegistry;
        _carrierCapabilityRegistry = carrierCapabilityRegistry;
        _carrierIntegrationAuthority = carrierIntegrationAuthority;
        _tenantContext = tenantContext;
        _logger = logger;
    }
    
    /// <summary>
    /// Get all supported carriers (registered adapters).
    /// </summary>
    [HttpGet("supported")]
    public ActionResult<List<SupportedCarrierDto>> GetSupportedCarriers()
    {
        var carriers = _carrierCapabilityRegistry.GetAllCapabilities().Select(capability =>
        {
            var adapter = _carrierRegistry.GetAdapter(capability.CarrierCode);
            return new SupportedCarrierDto
            {
                CarrierCode = capability.CarrierCode,
                DisplayName = capability.DisplayName,
                ProductionCapability = capability.ProductionCapability.ToString(),
                AccountRequired = RequiresAccount(capability),
                ConnectionTestSupported = capability.ConnectionTestSupported,
                ConnectionTestMode = capability.ConnectionTestMode.ToString(),
                WebhookSupported = capability.SupportsWebhooks,
                CredentialFields = capability.CredentialFields.Select(MapCredentialField).ToList(),
                ServiceLevels = adapter?.GetSupportedServiceLevels().Select(MapServiceLevel).ToList() ?? new List<ServiceLevelDto>()
            };
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
        var supportedCapabilities = _carrierCapabilityRegistry.GetAllCapabilities();
        
        var result = configs.Select(MapToDto).ToList();
        
        // Add unconfigured carriers as templates
        foreach (var capability in supportedCapabilities)
        {
            if (!configuredCodes.Contains(capability.CarrierCode))
            {
                var adapter = _carrierRegistry.GetAdapter(capability.CarrierCode);
                result.Add(new CarrierConfigurationDto
                {
                    Id = null,
                    CarrierCode = capability.CarrierCode,
                    DisplayName = capability.DisplayName,
                    IsEnabled = false,
                    IsDefault = false,
                    IsConfigured = false,
                    ProductionCapability = capability.ProductionCapability.ToString(),
                    AccountRequired = RequiresAccount(capability),
                    ConnectionTestSupported = capability.ConnectionTestSupported,
                    ConnectionTestMode = capability.ConnectionTestMode.ToString(),
                    WebhookSupported = capability.SupportsWebhooks,
                    CredentialFields = capability.CredentialFields.Select(MapCredentialField).ToList(),
                    ServiceLevels = adapter?.GetSupportedServiceLevels().Select(MapServiceLevel).ToList() ?? new List<ServiceLevelDto>()
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
            var capability = _carrierCapabilityRegistry.GetCapability(carrierCode);
            if (capability == null)
            {
                return NotFound(new { error = "Carrier not found" });
            }
            var adapter = _carrierRegistry.GetAdapter(capability.CarrierCode);
            
            return Ok(new CarrierConfigurationDto
            {
                Id = null,
                CarrierCode = capability.CarrierCode,
                DisplayName = capability.DisplayName,
                IsEnabled = false,
                IsDefault = false,
                IsConfigured = false,
                ProductionCapability = capability.ProductionCapability.ToString(),
                AccountRequired = RequiresAccount(capability),
                ConnectionTestSupported = capability.ConnectionTestSupported,
                ConnectionTestMode = capability.ConnectionTestMode.ToString(),
                WebhookSupported = capability.SupportsWebhooks,
                CredentialFields = capability.CredentialFields.Select(MapCredentialField).ToList(),
                ServiceLevels = adapter?.GetSupportedServiceLevels().Select(MapServiceLevel).ToList() ?? new List<ServiceLevelDto>()
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
        
        var capability = _carrierCapabilityRegistry.GetCapability(carrierCode);
        if (capability == null || _carrierRegistry.GetAdapter(capability.CarrierCode) == null)
        {
            return BadRequest(new { error = $"Carrier '{carrierCode}' is not supported" });
        }

        var validationError = ValidateCarrierConfigurationUpdate(capability, request);
        if (validationError != null)
        {
            return BadRequest(new { error = validationError });
        }
        
        var existing = await _dbContext.Set<CarrierConfiguration>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CarrierCode == capability.CarrierCode, cancellationToken);

        var result = await _carrierIntegrationAuthority.ValidateAndStoreIntegrationAsync(
            tenantId,
            capability.CarrierCode,
            new CarrierIntegrationUpdate(
                request.DisplayName,
                request.IsEnabled,
                request.IsDefault,
                request.ApiKey,
                request.ApiSecret,
                request.AccountId,
                request.ApiUrl,
                request.UseSandbox,
                request.SupportsCod,
                request.SupportsTracking,
                request.SupportsWebhooks,
                request.SupportsLabels,
                request.Configuration,
                request.PickupAddress,
                request.PickupCity,
                request.PickupCountry,
                request.PickupPhone,
                request.PickupContactName,
                request.DefaultServiceLevel,
                request.UseCarrierRates,
                request.FixedShippingCost,
                request.MarkupPercentage,
                request.FreeShippingThreshold,
                request.SortOrder,
                request.Notes,
                request.WebhookSecret),
            cancellationToken);

        if (!result.Succeeded || result.Configuration == null)
        {
            return BadRequest(new { error = result.Blockers.FirstOrDefault() ?? "Carrier integration update failed" });
        }
        
        _logger.LogInformation(
            "{Action} carrier configuration for {CarrierCode} (tenant {TenantId})",
            existing == null ? "Created" : "Updated", capability.CarrierCode, tenantId);

        return Ok(MapToDto(result.Configuration));
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
        var capability = _carrierCapabilityRegistry.GetCapability(carrierCode);
        if (capability == null)
        {
            return NotFound(new { error = "Carrier configuration not found" });
        }

        if (IsProductionBlocked(capability))
        {
            return BadRequest(new { error = "Carrier is not production activatable." });
        }
        
        var config = await _dbContext.Set<CarrierConfiguration>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CarrierCode == capability.CarrierCode, cancellationToken);
        
        if (config == null)
        {
            return NotFound(new { error = "Carrier configuration not found" });
        }
        
        var result = await _carrierIntegrationAuthority.ValidateAndStoreIntegrationAsync(
            tenantId,
            capability.CarrierCode,
            new CarrierIntegrationUpdate(
                DisplayName: null,
                IsEnabled: !config.IsEnabled,
                IsDefault: null,
                ApiKey: null,
                ApiSecret: null,
                AccountId: null,
                ApiUrl: null,
                UseSandbox: null,
                SupportsCod: null,
                SupportsTracking: null,
                SupportsWebhooks: null,
                SupportsLabels: null,
                Configuration: null,
                PickupAddress: null,
                PickupCity: null,
                PickupCountry: null,
                PickupPhone: null,
                PickupContactName: null,
                DefaultServiceLevel: null,
                UseCarrierRates: null,
                FixedShippingCost: null,
                MarkupPercentage: null,
                FreeShippingThreshold: null,
                SortOrder: null,
                Notes: null),
            cancellationToken);
        
        _logger.LogInformation(
            "Toggled carrier {CarrierCode} to {Status} (tenant {TenantId})",
            capability.CarrierCode, !config.IsEnabled ? "enabled" : "disabled", tenantId);

        return result.Configuration == null
            ? NotFound(new { error = "Carrier configuration not found" })
            : Ok(MapToDto(result.Configuration));
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
        var capability = _carrierCapabilityRegistry.GetCapability(carrierCode);
        if (capability == null)
        {
            return NotFound(new { error = "Carrier not found" });
        }

        if (!capability.ConnectionTestSupported || capability.ConnectionTestMode == CarrierConnectionTestMode.Unsupported)
        {
            return BadRequest(new { error = "Connection test is not supported for this carrier." });
        }
        
        var result = await _carrierIntegrationAuthority.ValidateResolvedCredentialsAsync(
            tenantId,
            capability.CarrierCode,
            cancellationToken);
        
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
        var capability = _carrierCapabilityRegistry.GetCapability(carrierCode);
        
        var deleted = await _carrierIntegrationAuthority.DeleteIntegrationAsync(
            tenantId,
            capability?.CarrierCode ?? carrierCode,
            cancellationToken);

        if (!deleted)
        {
            return NotFound(new { error = "Carrier configuration not found" });
        }

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

    private static ServiceLevelDto MapServiceLevel(ServiceLevelInfo serviceLevel) => new()
    {
        Code = serviceLevel.Code,
        Name = serviceLevel.Name,
        Description = serviceLevel.Description,
        TypicalDaysMin = serviceLevel.TypicalDaysMin,
        TypicalDaysMax = serviceLevel.TypicalDaysMax
    };

    private static CarrierCredentialFieldMetadataDto MapCredentialField(CarrierCredentialFieldDescriptor field) => new()
    {
        Key = field.Key,
        Label = field.Label,
        IsSecret = field.IsSecret,
        Required = field.Required,
        Target = field.Target
    };

    private static bool IsProductionBlocked(CarrierCapabilityDescriptor capability) =>
        capability.ProductionCapability is CarrierProductionCapability.Unavailable or CarrierProductionCapability.NotCertified;

    private static bool RequiresAccount(CarrierCapabilityDescriptor capability) =>
        !CarrierCodes.IsManual(capability.CarrierCode) && !IsProductionBlocked(capability);

    private static string? ValidateCarrierConfigurationUpdate(CarrierCapabilityDescriptor capability, UpsertCarrierConfigRequest request)
    {
        if (CarrierCodes.IsManual(capability.CarrierCode))
        {
            return "Manual Fulfillment is managed as a Shipping Method and does not require a carrier configuration.";
        }

        if (IsProductionBlocked(capability) && request.IsEnabled == true)
        {
            return "Carrier is not production activatable.";
        }

        if (!string.IsNullOrWhiteSpace(request.ApiUrl))
        {
            return "ApiUrl is not an advertised runtime credential field for this carrier.";
        }

        var advertisedTargets = capability.CredentialFields
            .Select(field => field.Target)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(request.ApiSecret) && !advertisedTargets.Contains("ApiSecret"))
        {
            return "ApiSecret is not an advertised runtime credential field for this carrier.";
        }

        if (!string.IsNullOrWhiteSpace(request.AccountId) && !advertisedTargets.Contains("AccountId"))
        {
            return "AccountId is not an advertised runtime credential field for this carrier.";
        }

        return null;
    }
    
    private CarrierConfigurationDto MapToDto(CarrierConfiguration config)
    {
        var adapter = _carrierRegistry.GetAdapter(config.CarrierCode);
        var capability = _carrierCapabilityRegistry.GetCapability(config.CarrierCode);
        
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
            HasWebhookSecret = !string.IsNullOrWhiteSpace(config.WebhookSecret),
            ValidationMatchesCurrentConfiguration =
                !string.IsNullOrWhiteSpace(config.CredentialConfigurationFingerprint) &&
                string.Equals(
                    config.CredentialConfigurationFingerprint,
                    config.LastValidatedConfigurationFingerprint,
                    StringComparison.Ordinal),
            ProductionCapability = capability?.ProductionCapability.ToString() ?? "Unavailable",
            AccountRequired = capability != null && RequiresAccount(capability),
            ConnectionTestSupported = capability?.ConnectionTestSupported == true,
            ConnectionTestMode = capability?.ConnectionTestMode.ToString() ?? "Unsupported",
            WebhookSupported = capability?.SupportsWebhooks == true,
            CredentialFields = capability?.CredentialFields.Select(MapCredentialField).ToList() ?? new List<CarrierCredentialFieldMetadataDto>(),
            ServiceLevels = adapter?.GetSupportedServiceLevels().Select(MapServiceLevel).ToList() ?? new List<ServiceLevelDto>()
        };
    }

    private CarrierConfigurationDto MapToDto(CarrierIntegrationConfigurationSnapshot config)
    {
        var adapter = _carrierRegistry.GetAdapter(config.CarrierCode);
        var capability = _carrierCapabilityRegistry.GetCapability(config.CarrierCode);

        return new CarrierConfigurationDto
        {
            Id = config.Id,
            CarrierCode = config.CarrierCode,
            DisplayName = config.DisplayName,
            IsEnabled = config.IsEnabled,
            IsDefault = config.IsDefault,
            IsConfigured = config.HasApiKey,
            HasApiKey = config.HasApiKey,
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
            LastValidatedAtUtc = config.LastValidatedAtUtc,
            LastValidationSucceeded = config.LastValidationSucceeded,
            LastValidationError = config.LastValidationError,
            HasWebhookSecret = config.HasWebhookSecret,
            ValidationMatchesCurrentConfiguration = config.ValidationMatchesCurrentConfiguration,
            ProductionCapability = capability?.ProductionCapability.ToString() ?? "Unavailable",
            AccountRequired = capability != null && RequiresAccount(capability),
            ConnectionTestSupported = capability?.ConnectionTestSupported == true,
            ConnectionTestMode = capability?.ConnectionTestMode.ToString() ?? "Unsupported",
            WebhookSupported = capability?.SupportsWebhooks == true,
            CredentialFields = capability?.CredentialFields.Select(MapCredentialField).ToList() ?? new List<CarrierCredentialFieldMetadataDto>(),
            ServiceLevels = adapter?.GetSupportedServiceLevels().Select(MapServiceLevel).ToList() ?? new List<ServiceLevelDto>()
        };
    }
    
    #endregion
}

#region DTOs

public class SupportedCarrierDto
{
    public required string CarrierCode { get; set; }
    public required string DisplayName { get; set; }
    public string ProductionCapability { get; set; } = "Partial";
    public bool AccountRequired { get; set; } = true;
    public bool ConnectionTestSupported { get; set; } = true;
    public string ConnectionTestMode { get; set; } = "Remote";
    public bool WebhookSupported { get; set; }
    public List<CarrierCredentialFieldMetadataDto> CredentialFields { get; set; } = new();
    public List<ServiceLevelDto> ServiceLevels { get; set; } = new();
}

public class CarrierCredentialFieldMetadataDto
{
    public required string Key { get; set; }
    public required string Label { get; set; }
    public bool IsSecret { get; set; }
    public bool Required { get; set; }
    public required string Target { get; set; }
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
    public bool HasWebhookSecret { get; set; }
    public bool ValidationMatchesCurrentConfiguration { get; set; }
    public string ProductionCapability { get; set; } = "Partial";
    public bool AccountRequired { get; set; } = true;
    public bool ConnectionTestSupported { get; set; } = true;
    public string ConnectionTestMode { get; set; } = "Remote";
    public bool WebhookSupported { get; set; }
    public List<CarrierCredentialFieldMetadataDto> CredentialFields { get; set; } = new();
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
    public string? WebhookSecret { get; set; }
}

public class ValidateCredentialsResponseDto
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? AccountName { get; set; }
}

#endregion
