using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.Pricing;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.API.Infrastructure.Http;
using PointOfSale.Core.Helpers;
using System.Text.Json;

namespace PointOfSale.API.Controllers;

/// <summary>
/// API controller for managing shipping methods (POS Admin).
/// 
/// ARCHITECTURE:
/// - PostgreSQL is the source of truth (Write Model)
/// - Events are emitted on Create/Update/Delete
/// - Consumers project to MongoDB for Storefront reads
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ShippingMethodsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly IShippingCarrierRegistry _carrierRegistry;
    private readonly ICarrierIntegrationAuthority _carrierIntegrationAuthority;
    private readonly IShippingCalculationService _shippingCalculationService;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ILogger<ShippingMethodsController> _logger;
    private Guid TenantId => _tenantContext.RequiredTenantId;

    public ShippingMethodsController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IEventPublisher eventPublisher,
        IShippingCarrierRegistry carrierRegistry,
        ICarrierIntegrationAuthority carrierIntegrationAuthority,
        IShippingCalculationService shippingCalculationService,
        CommerceReliabilityTelemetry telemetry,
        ILogger<ShippingMethodsController> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _eventPublisher = eventPublisher;
        _carrierRegistry = carrierRegistry;
        _carrierIntegrationAuthority = carrierIntegrationAuthority;
        _shippingCalculationService = shippingCalculationService;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <summary>
    /// Get all shipping methods
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ShippingMethodDto>>> GetShippingMethods(
        [FromQuery] bool? activeOnly = true,
        [FromQuery] string? carrierCode = null,
        [FromQuery] Guid? warehouseId = null)
    {
        SetNoStoreHeaders();
        var tenantId = TenantId;
        var query = _context.ShippingMethods
            .Where(s => s.TenantId == tenantId && !s.IsDeleted);
        
        if (activeOnly == true)
        {
            query = query.Where(s => s.IsActive);
        }

        if (carrierCode != null)
        {
            var error = ValidateCarrierCode(carrierCode);
            if (error != null) return BadRequest(new { message = error });
            var normalized = CarrierCodes.Normalize(carrierCode);
            query = normalized == CarrierCodes.Manual
                ? query.Where(s => s.CarrierCode == null || s.CarrierCode == "" || s.CarrierCode.ToLower() == normalized)
                : query.Where(s => s.CarrierCode != null && s.CarrierCode.ToLower() == normalized);
        }

        if (warehouseId.HasValue)
        {
            var error = await ValidateFulfillmentWarehouseAsync(tenantId, warehouseId);
            if (error != null) return BadRequest(new { message = error });
            query = query.Where(s => s.FulfillmentWarehouseId == warehouseId.Value);
        }
        
        var methods = await query
            .OrderBy(s => s.SortOrder)
            .ToListAsync();
        
        return Ok(methods.Select(MapToDto).ToList());
    }

    /// <summary>
    /// Get a shipping method by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ShippingMethodDto>> GetShippingMethod(Guid id)
    {
        SetNoStoreHeaders();
        var tenantId = TenantId;
        var method = await _context.ShippingMethods
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId && !s.IsDeleted);
        
        if (method == null)
        {
            return NotFound(new { message = "Shipping method not found" });
        }
        
        return Ok(MapToDto(method));
    }

    /// <summary>
    /// Create a new shipping method
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<ShippingMethodDto>> CreateShippingMethod(CreateShippingMethodDto dto)
    {
        var tenantId = TenantId;

        var dtoValidationError = dto.Validate();
        if (dtoValidationError != null)
        {
            return BadRequest(new { message = dtoValidationError });
        }
        
        // PHASE 1: Validate CarrierCode - only allow registered carriers or manual
        var validationError = ValidateCarrierCode(dto.CarrierCode);
        if (validationError != null)
        {
            return BadRequest(new { message = validationError });
        }
        
        // Normalize CarrierCode: null/empty becomes "manual"
        var normalizedCarrierCode = CarrierCodes.Normalize(dto.CarrierCode);
        var serviceLevelValidationError = ValidateAndNormalizeServiceLevel(normalizedCarrierCode, dto.ServiceLevel, out var normalizedServiceLevel);
        if (serviceLevelValidationError != null)
        {
            return BadRequest(new { message = serviceLevelValidationError });
        }
        var canonicalCarrierName = ResolveCarrierDisplayName(normalizedCarrierCode);
        if (dto.IsActive && (!dto.FulfillmentWarehouseId.HasValue || dto.FulfillmentWarehouseId.Value == Guid.Empty))
        {
            return BadRequest(new { message = "Active shipping methods require a fulfillment warehouse." });
        }

        var warehouseValidationError = await ValidateFulfillmentWarehouseAsync(tenantId, dto.FulfillmentWarehouseId);
        if (warehouseValidationError != null)
        {
            return BadRequest(new { message = warehouseValidationError });
        }

        var carrierReadinessError = await ValidateActiveCarrierReadinessAsync(
            tenantId,
            normalizedCarrierCode,
            dto.FulfillmentWarehouseId,
            dto.IsActive);
        if (carrierReadinessError != null)
        {
            return BadRequest(new { message = carrierReadinessError });
        }
        
        var method = new ShippingMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = dto.Name,
            NameAr = dto.NameAr,
            Description = dto.Description,
            BaseRate = dto.BaseRate,
            PerKgRate = dto.PerKgRate,
            FreeShippingThreshold = dto.FreeShippingThreshold,
            EstimatedDeliveryDaysMin = dto.EstimatedDeliveryDaysMin,
            EstimatedDeliveryDaysMax = dto.EstimatedDeliveryDaysMax,
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder,
            CarrierName = canonicalCarrierName,
            CarrierCode = normalizedCarrierCode,
            ServiceLevel = normalizedServiceLevel,
            FulfillmentWarehouseId = dto.FulfillmentWarehouseId,
            TrackingUrlTemplate = dto.TrackingUrlTemplate,
            AvailableCountries = dto.AvailableCountries,
            CreatedAt = DateTime.UtcNow
        };
        method.UpdatedAt = NextShippingMethodMutationTimestamp(method);
        
        _context.ShippingMethods.Add(method);
        
        // CRITICAL: Publish event BEFORE SaveChangesAsync so it goes through EF Core Outbox
        // This ensures both entity changes and outbox messages are saved atomically
        // Pass BaseRate from tracked entity to avoid AsNoTracking query that would return null
        await PublishShippingMethodUpdatedEventAsync(tenantId, method.Id, method.BaseRate, method.UpdatedAt);
        
        await _context.SaveChangesAsync();
        
        _logger.LogInformation(
            "ShippingMethod created | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId} | Name: {Name}",
            tenantId, method.Id, method.Name);
        
        return CreatedAtAction(nameof(GetShippingMethod), new { id = method.Id }, MapToDto(method));
    }

    /// <summary>
    /// Update a shipping method
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> UpdateShippingMethod(Guid id, UpdateShippingMethodDto dto)
    {
        var tenantId = TenantId;

        var dtoValidationError = dto.Validate();
        if (dtoValidationError != null)
        {
            return BadRequest(new { message = dtoValidationError });
        }

        // PHASE 1: Validate CarrierCode - only allow registered carriers or manual
        var validationError = ValidateCarrierCode(dto.CarrierCode);
        if (validationError != null)
        {
            return BadRequest(new { message = validationError });
        }
        
        // Normalize CarrierCode: null/empty becomes "manual"
        var normalizedCarrierCode = CarrierCodes.Normalize(dto.CarrierCode);
        var serviceLevelValidationError = ValidateAndNormalizeServiceLevel(normalizedCarrierCode, dto.ServiceLevel, out var normalizedServiceLevel);
        if (serviceLevelValidationError != null)
        {
            return BadRequest(new { message = serviceLevelValidationError });
        }
        var canonicalCarrierName = ResolveCarrierDisplayName(normalizedCarrierCode);
        if (dto.IsActive && (!dto.FulfillmentWarehouseId.HasValue || dto.FulfillmentWarehouseId.Value == Guid.Empty))
        {
            return BadRequest(new { message = "Active shipping methods require a fulfillment warehouse." });
        }

        var warehouseValidationError = await ValidateFulfillmentWarehouseAsync(tenantId, dto.FulfillmentWarehouseId);
        if (warehouseValidationError != null)
        {
            return BadRequest(new { message = warehouseValidationError });
        }

        var carrierReadinessError = await ValidateActiveCarrierReadinessAsync(
            tenantId,
            normalizedCarrierCode,
            dto.FulfillmentWarehouseId,
            dto.IsActive);
        if (carrierReadinessError != null)
        {
            return BadRequest(new { message = carrierReadinessError });
        }
        
        var method = await _context.ShippingMethods
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId && !s.IsDeleted);
        
        if (method == null)
        {
            return NotFound(new { message = "Shipping method not found" });
        }

        if (CheckReviewedRevision(method) is { } revisionError) return revisionError;

        method.Name = dto.Name;
        method.NameAr = dto.NameAr;
        method.Description = dto.Description;
        method.BaseRate = dto.BaseRate;
        method.PerKgRate = dto.PerKgRate;
        method.FreeShippingThreshold = dto.FreeShippingThreshold;
        method.EstimatedDeliveryDaysMin = dto.EstimatedDeliveryDaysMin;
        method.EstimatedDeliveryDaysMax = dto.EstimatedDeliveryDaysMax;
        method.IsActive = dto.IsActive;
        method.SortOrder = dto.SortOrder;
        method.CarrierName = canonicalCarrierName;
        method.CarrierCode = normalizedCarrierCode;
        method.ServiceLevel = normalizedServiceLevel;
        method.FulfillmentWarehouseId = dto.FulfillmentWarehouseId;
        method.TrackingUrlTemplate = dto.TrackingUrlTemplate;
        method.AvailableCountries = dto.AvailableCountries;
        method.UpdatedAt = NextShippingMethodMutationTimestamp(method);
        
        // CRITICAL: Publish event BEFORE SaveChangesAsync so it goes through EF Core Outbox
        // Pass BaseRate from tracked entity to avoid AsNoTracking query that would read stale data
        await PublishShippingMethodUpdatedEventAsync(tenantId, method.Id, method.BaseRate, method.UpdatedAt);
        
        await _context.SaveChangesAsync();
        
        _logger.LogInformation(
            "ShippingMethod updated | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId} | Name: {Name}",
            tenantId, method.Id, method.Name);
        
        return NoContent();
    }

    /// <summary>
    /// Delete a shipping method
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteShippingMethod(Guid id)
    {
        var tenantId = TenantId;
        var method = await _context.ShippingMethods
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId && !s.IsDeleted);
        
        if (method == null)
        {
            return NotFound(new { message = "Shipping method not found" });
        }
        
        // Soft delete
        if (CheckReviewedRevision(method) is { } revisionError) return revisionError;
        method.IsDeleted = true;
        method.UpdatedAt = NextShippingMethodMutationTimestamp(method);
        
        // CRITICAL: Publish event BEFORE SaveChangesAsync so it goes through EF Core Outbox
        await PublishShippingMethodDeletedEventAsync(tenantId, id, method.UpdatedAt);
        
        await _context.SaveChangesAsync();
        
        _logger.LogInformation(
            "ShippingMethod deleted | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId}",
            tenantId, id);
        
        return NoContent();
    }

    /// <summary>
    /// Legacy compatibility quote only. The caller-supplied subtotal and weight are
    /// presentation inputs and are never admitted by checkout or persisted to an order.
    /// </summary>
    [HttpPost("calculate")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ShippingCostResult>> CalculateShippingCost(CalculateShippingRequest request)
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers["X-Shipping-Authority"] = "compatibility-quote-only";
        var tenantId = TenantId;
        var dtoValidationError = request.Validate();
        if (dtoValidationError != null)
        {
            return BadRequest(new { message = dtoValidationError });
        }

        var method = await _context.ShippingMethods
            .FirstOrDefaultAsync(s =>
                s.Id == request.ShippingMethodId &&
                s.TenantId == tenantId &&
                !s.IsDeleted);
        
        if (method == null || !method.IsActive)
        {
            return NotFound(new { message = "Shipping method not found or inactive" });
        }

        var warehouseValidationError = await ValidateFulfillmentWarehouseAsync(tenantId, method.FulfillmentWarehouseId);
        if (!method.FulfillmentWarehouseId.HasValue || warehouseValidationError != null)
        {
            return BadRequest(new { message = warehouseValidationError ?? "Shipping method does not have a configured fulfillment warehouse." });
        }
        
        var isFreeShipping = method.FreeShippingThreshold.HasValue &&
                             request.OrderTotal >= method.FreeShippingThreshold.Value;
        var cost = _shippingCalculationService.Calculate(method, request.OrderTotal, request.TotalWeight);
        
        return Ok(new ShippingCostResult
        {
            ShippingCost = cost,
            IsFreeShipping = isFreeShipping,
            EstimatedDeliveryDaysMin = method.EstimatedDeliveryDaysMin,
            EstimatedDeliveryDaysMax = method.EstimatedDeliveryDaysMax
        });
    }

    /// <summary>
    /// Sync all shipping methods to MongoDB by republishing events.
    /// This is an admin-only operation for initial data sync or recovery.
    /// </summary>
    [HttpPost("sync")]
    public async Task<IActionResult> SyncShippingMethods()
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var requestSource = ClientIpAddressResolver.GetTrustedClientIp(HttpContext, "unknown");

        try
        {
            var methods = await _context.ShippingMethods
                .IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId)
                .ToListAsync();

            foreach (var method in methods)
            {
                if (method.IsDeleted)
                {
                    // Republish tombstone event for soft-deleted records
                    await PublishShippingMethodDeletedEventAsync(method.TenantId, method.Id, method.UpdatedAt ?? method.CreatedAt);
                }
                else
                {
                    // Republish update event for non-deleted records
                    // In sync scenario, entity is already loaded so we can pass BaseRate directly
                    await PublishShippingMethodUpdatedEventAsync(method.TenantId, method.Id, method.BaseRate, method.UpdatedAt ?? method.CreatedAt);
                }
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "ShippingMethods sync committed | TenantId: {TenantId} | Count: {Count} | RequestSource: {RequestSource}",
                tenantId,
                methods.Count,
                requestSource);

            return Ok(new
            {
                message = $"Synced {methods.Count} shipping methods",
                count = methods.Count,
                committed = methods.Count
            });
        }
        catch (Exception ex)
        {
            _telemetry.RecordShippingSyncFailed(tenantId, "outbox_commit_failed");
            _logger.LogError(
                ex,
                "ShippingMethods sync failed before commit | TenantId: {TenantId} | RequestSource: {RequestSource} | Reason: {Reason}",
                tenantId,
                requestSource,
                "outbox_commit_failed");

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "SYNC_PERSISTENCE_FAILED",
                message = "Shipping method sync could not be committed. No success was recorded."
            });
        }
    }

    #region Event Publishing

    private async Task PublishShippingMethodUpdatedEventAsync(Guid tenantId, Guid shippingMethodId, decimal baseRate, DateTime? occurredAtUtc = null)
    {
        // CQRS FIX (Audit CQRS-1): Accept BaseRate as parameter from tracked entity
        // to avoid AsNoTracking query that would miss unsaved changes (create path)
        // or read stale data (update path).
        var @event = new ShippingMethodUpdatedEvent
        {
            TenantId = tenantId,
            ShippingMethodId = shippingMethodId,
            BaseRate = baseRate,
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow
        };

        await _eventPublisher.PublishAsync(@event);

        _logger.LogDebug(
            "Published ShippingMethodUpdatedEvent | EventId: {EventId} | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId} | BaseRate: {BaseRate}",
            @event.EventId, tenantId, shippingMethodId, baseRate);
    }

    private async Task PublishShippingMethodDeletedEventAsync(Guid tenantId, Guid shippingMethodId, DateTime? occurredAtUtc = null)
    {
        var @event = new ShippingMethodDeletedEvent
        {
            TenantId = tenantId,
            ShippingMethodId = shippingMethodId,
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow
        };

        await _eventPublisher.PublishAsync(@event);

        _logger.LogDebug(
            "Published ShippingMethodDeletedEvent | EventId: {EventId} | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId}",
            @event.EventId, tenantId, shippingMethodId);
    }

    #endregion

    #region Validation

    private static DateTime NextShippingMethodMutationTimestamp(ShippingMethod method)
    {
        var now = DateTime.UtcNow;
        var current = method.UpdatedAt ?? method.CreatedAt;
        return current >= now ? current.AddTicks(10) : now;
    }

    private IActionResult? CheckReviewedRevision(ShippingMethod method)
    {
        if (!Request.Headers.TryGetValue("If-Match", out var header)) return null;
        var expected = header.ToString().Trim('"');
        return expected == (method.UpdatedAt ?? method.CreatedAt).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ? null
            : Conflict(new { error = "SHIPPING_METHOD_CONFLICT", message = "Shipping method changed. Reload before saving." });
    }

    /// <summary>
    /// Validates that CarrierCode is either null/empty ("manual") or a registered carrier.
    /// Returns error message if invalid, null if valid.
    /// 
    /// ARCHITECTURAL RULE:
    /// - null/empty → "manual" (valid)
    /// - "manual" → valid (first-class carrier)
    /// - Registered carrier (bosta, aramex, mylerz, dhl) → valid
    /// - Anything else → REJECTED with 400 Bad Request
    /// </summary>
    private string? ValidateCarrierCode(string? carrierCode)
    {
        // null or empty is valid (will be normalized to "manual")
        if (string.IsNullOrWhiteSpace(carrierCode))
        {
            return null; // Valid
        }
        
        var normalized = carrierCode.Trim().ToLowerInvariant();
        
        // Check if it's the manual carrier
        if (normalized == CarrierCodes.Manual)
        {
            return null; // Valid
        }
        
        // Check if it's a registered carrier
        if (_carrierRegistry.IsCarrierSupported(normalized))
        {
            return null; // Valid
        }
        
        // Invalid carrier code
        var validCodes = string.Join(", ", _carrierRegistry.GetSupportedCarrierCodes());
        return $"Invalid CarrierCode '{carrierCode}'. Valid options: {validCodes}, or leave empty for manual fulfillment.";
    }

    private string? ValidateAndNormalizeServiceLevel(string carrierCode, string? serviceLevel, out string normalizedServiceLevel)
    {
        normalizedServiceLevel = string.IsNullOrWhiteSpace(serviceLevel)
            ? "standard"
            : serviceLevel.Trim().ToLowerInvariant();

        var adapter = _carrierRegistry.GetAdapter(carrierCode);
        var supportedLevels = adapter?.GetSupportedServiceLevels() ?? Array.Empty<ServiceLevelInfo>();
        var requestedServiceLevel = normalizedServiceLevel;
        if (supportedLevels.Count == 0)
        {
            if (carrierCode == CarrierCodes.Mylerz)
                return "Mylerz has no supported service levels pending an applicable official merchant contract.";

            return requestedServiceLevel == "standard"
                ? null
                : $"ServiceLevel '{serviceLevel}' is not supported for carrier '{carrierCode}'. Valid options: standard.";
        }

        if (supportedLevels.Any(level => string.Equals(level.Code, requestedServiceLevel, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var validLevels = string.Join(", ", supportedLevels.Select(level => level.Code));
        return $"ServiceLevel '{serviceLevel}' is not supported for carrier '{carrierCode}'. Valid options: {validLevels}.";
    }

    private string ResolveCarrierDisplayName(string carrierCode)
    {
        var adapter = _carrierRegistry.GetAdapter(carrierCode);
        if (adapter != null)
        {
            return adapter.DisplayName;
        }

        return CarrierCodes.IsManual(carrierCode) ? "Manual Fulfillment" : carrierCode;
    }

    private ShippingMethodDto MapToDto(ShippingMethod method)
    {
        var normalizedCarrierCode = CarrierCodes.Normalize(method.CarrierCode);
        return new ShippingMethodDto
        {
            Id = method.Id,
            Revision = (method.UpdatedAt ?? method.CreatedAt).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Name = method.Name,
            NameAr = method.NameAr,
            Description = method.Description,
            BaseRate = method.BaseRate,
            PerKgRate = method.PerKgRate,
            FreeShippingThreshold = method.FreeShippingThreshold,
            EstimatedDeliveryDaysMin = method.EstimatedDeliveryDaysMin,
            EstimatedDeliveryDaysMax = method.EstimatedDeliveryDaysMax,
            IsActive = method.IsActive,
            SortOrder = method.SortOrder,
            CarrierName = ResolveCarrierDisplayName(normalizedCarrierCode),
            CarrierCode = normalizedCarrierCode,
            ServiceLevel = string.IsNullOrWhiteSpace(method.ServiceLevel) ? "standard" : method.ServiceLevel.Trim().ToLowerInvariant(),
            FulfillmentWarehouseId = method.FulfillmentWarehouseId,
            TrackingUrlTemplate = method.TrackingUrlTemplate,
            AvailableCountries = method.AvailableCountries
        };
    }

    private async Task<string?> ValidateFulfillmentWarehouseAsync(Guid tenantId, Guid? fulfillmentWarehouseId)
    {
        if (!fulfillmentWarehouseId.HasValue)
        {
            return null;
        }

        if (fulfillmentWarehouseId.Value == Guid.Empty)
        {
            return "Fulfillment warehouse is required.";
        }

        var warehouse = await _context.Warehouses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId && item.Id == fulfillmentWarehouseId.Value,
                HttpContext.RequestAborted);

        if (warehouse == null || warehouse.IsDeleted)
        {
            return "Selected fulfillment warehouse is no longer available.";
        }

        if (!warehouse.IsActive)
        {
            return "Selected fulfillment warehouse is inactive.";
        }

        return null;
    }

    private async Task<string?> ValidateActiveCarrierReadinessAsync(
        Guid tenantId,
        string carrierCode,
        Guid? fulfillmentWarehouseId,
        bool isActive)
    {
        // Drafts and deactivation are allowed. Every external activation must
        // independently pass backend authority, including direct API callers.
        if (!isActive || CarrierCodes.IsManual(carrierCode))
        {
            return null;
        }

        var readiness = await _carrierIntegrationAuthority.GetIntegrationReadinessAsync(
            tenantId,
            carrierCode,
            warehouseId: fulfillmentWarehouseId,
            purpose: CarrierIntegrationPurpose.StorefrontAdmission,
            cancellationToken: HttpContext.RequestAborted);
        return readiness.CanExecute
            ? null
            : $"{readiness.DisplayName} shipping method cannot be activated: {readiness.Blockers.FirstOrDefault() ?? "CARRIER_NOT_READY"}.";
    }

    private void SetNoStoreHeaders()
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";
    }

    #endregion
}

#region DTOs

public class ShippingMethodDto
{
    public string Revision { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public decimal BaseRate { get; set; }
    public decimal PerKgRate { get; set; }
    public decimal? FreeShippingThreshold { get; set; }
    public int EstimatedDeliveryDaysMin { get; set; }
    public int EstimatedDeliveryDaysMax { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public string? CarrierName { get; set; }
    /// <summary>
    /// Carrier code for carrier-agnostic shipping (bosta, aramex, mylerz, dhl).
    /// Null means "manual" (no carrier integration).
    /// </summary>
    public string? CarrierCode { get; set; }
    public string ServiceLevel { get; set; } = "standard";
    public Guid? FulfillmentWarehouseId { get; set; }
    public string? TrackingUrlTemplate { get; set; }
    public string? AvailableCountries { get; set; }
}

public class CreateShippingMethodDto
{
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public decimal BaseRate { get; set; }
    public decimal PerKgRate { get; set; }
    public decimal? FreeShippingThreshold { get; set; }
    public int EstimatedDeliveryDaysMin { get; set; } = 1;
    public int EstimatedDeliveryDaysMax { get; set; } = 5;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? CarrierName { get; set; }
    /// <summary>
    /// Carrier code for carrier-agnostic shipping (bosta, aramex, mylerz, dhl).
    /// Null means "manual" (no carrier integration).
    /// </summary>
    public string? CarrierCode { get; set; }
    public string? ServiceLevel { get; set; }
    public Guid? FulfillmentWarehouseId { get; set; }
    public string? TrackingUrlTemplate { get; set; }
    public string? AvailableCountries { get; set; }

    public virtual string? Validate()
    {
        if (!ShipmentTrackingUrl.IsValidTemplate(TrackingUrlTemplate))
            return "Tracking URL template must be an absolute HTTP(S) URL without credentials or host placeholders.";

        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Shipping method name is required.";
        }

        if (BaseRate < 0m)
        {
            return "Base rate cannot be negative.";
        }

        if (PerKgRate < 0m)
        {
            return "Per kg rate cannot be negative.";
        }

        if (FreeShippingThreshold.HasValue && FreeShippingThreshold.Value < 0m)
        {
            return "Free shipping threshold cannot be negative.";
        }

        if (EstimatedDeliveryDaysMin < 0)
        {
            return "Minimum estimated delivery days cannot be negative.";
        }

        if (EstimatedDeliveryDaysMax < 0)
        {
            return "Maximum estimated delivery days cannot be negative.";
        }

        if (EstimatedDeliveryDaysMax < EstimatedDeliveryDaysMin)
        {
            return "Maximum estimated delivery days must be greater than or equal to the minimum.";
        }

        if (SortOrder < 0)
        {
            return "Sort order cannot be negative.";
        }

        return ValidateAvailableCountries();
    }

    private string? ValidateAvailableCountries()
    {
        // Null is the existing unrestricted scope. Empty/malformed input must
        // never be admitted as all countries, including for direct API callers.
        if (AvailableCountries == null) return null;
        const string error = "AvailableCountries must be null (all countries) or a non-empty JSON array of unique assigned ISO 3166-1 alpha-2 country codes in uppercase.";
        try
        {
            using var document = JsonDocument.Parse(AvailableCountries);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
                return error;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) return error;
                var code = item.GetString();
                if (code == null || code.Length != 2
                    || !CountryCodeHelper.TryNormalizeCountryCode(code, out var normalized)
                    || !string.Equals(code, normalized, StringComparison.Ordinal)
                    || !seen.Add(code))
                    return error;
            }
        }
        catch (JsonException)
        {
            return error;
        }

        return null;
    }
}

public class UpdateShippingMethodDto : CreateShippingMethodDto { }

public class CalculateShippingRequest
{
    public Guid ShippingMethodId { get; set; }
    public decimal OrderTotal { get; set; }
    public decimal TotalWeight { get; set; }

    public string? Validate()
    {
        if (ShippingMethodId == Guid.Empty)
        {
            return "Shipping method is required.";
        }

        if (OrderTotal < 0m)
        {
            return "Order total cannot be negative.";
        }

        if (TotalWeight < 0m)
        {
            return "Total weight cannot be negative.";
        }

        return null;
    }
}

public class ShippingCostResult
{
    public decimal ShippingCost { get; set; }
    public bool IsFreeShipping { get; set; }
    public int EstimatedDeliveryDaysMin { get; set; }
    public int EstimatedDeliveryDaysMax { get; set; }
}

#endregion
