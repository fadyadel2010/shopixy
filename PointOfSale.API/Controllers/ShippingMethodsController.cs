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
    private readonly IShippingCalculationService _shippingCalculationService;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ILogger<ShippingMethodsController> _logger;
    private Guid TenantId => _tenantContext.RequiredTenantId;

    public ShippingMethodsController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IEventPublisher eventPublisher,
        IShippingCarrierRegistry carrierRegistry,
        IShippingCalculationService shippingCalculationService,
        CommerceReliabilityTelemetry telemetry,
        ILogger<ShippingMethodsController> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _eventPublisher = eventPublisher;
        _carrierRegistry = carrierRegistry;
        _shippingCalculationService = shippingCalculationService;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <summary>
    /// Get all shipping methods
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ShippingMethodDto>>> GetShippingMethods(
        [FromQuery] bool? activeOnly = true)
    {
        SetNoStoreHeaders();
        var tenantId = TenantId;
        var query = _context.ShippingMethods
            .Where(s => s.TenantId == tenantId && !s.IsDeleted);
        
        if (activeOnly == true)
        {
            query = query.Where(s => s.IsActive);
        }
        
        var methods = await query
            .OrderBy(s => s.SortOrder)
            .Select(s => new ShippingMethodDto
            {
                Id = s.Id,
                Name = s.Name,
                NameAr = s.NameAr,
                Description = s.Description,
                BaseRate = s.BaseRate,
                PerKgRate = s.PerKgRate,
                FreeShippingThreshold = s.FreeShippingThreshold,
                EstimatedDeliveryDaysMin = s.EstimatedDeliveryDaysMin,
                EstimatedDeliveryDaysMax = s.EstimatedDeliveryDaysMax,
                IsActive = s.IsActive,
                SortOrder = s.SortOrder,
                CarrierName = s.CarrierName,
                CarrierCode = s.CarrierCode,
                TrackingUrlTemplate = s.TrackingUrlTemplate,
                AvailableCountries = s.AvailableCountries
            })
            .ToListAsync();
        
        return Ok(methods);
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
        
        return Ok(new ShippingMethodDto
        {
            Id = method.Id,
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
            CarrierName = method.CarrierName,
            CarrierCode = method.CarrierCode,
            TrackingUrlTemplate = method.TrackingUrlTemplate,
            AvailableCountries = method.AvailableCountries
        });
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
            CarrierName = dto.CarrierName,
            CarrierCode = normalizedCarrierCode,
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
        
        return CreatedAtAction(nameof(GetShippingMethod), new { id = method.Id }, new ShippingMethodDto
        {
            Id = method.Id,
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
            CarrierName = method.CarrierName,
            CarrierCode = method.CarrierCode,
            TrackingUrlTemplate = method.TrackingUrlTemplate,
            AvailableCountries = method.AvailableCountries
        });
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
        
        var method = await _context.ShippingMethods
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId && !s.IsDeleted);
        
        if (method == null)
        {
            return NotFound(new { message = "Shipping method not found" });
        }
        
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
        method.CarrierName = dto.CarrierName;
        method.CarrierCode = normalizedCarrierCode;
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
    [Authorize(Roles = "Owner,Admin")]
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
    /// Calculate shipping cost for an order
    /// </summary>
    [HttpPost("calculate")]
    [AllowAnonymous]
    public async Task<ActionResult<ShippingCostResult>> CalculateShippingCost(CalculateShippingRequest request)
    {
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
    [Authorize(Roles = "Owner,Admin")]
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
        return current >= now ? current.AddTicks(1) : now;
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
        
        var normalized = carrierCode.ToLowerInvariant();
        
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
    public string? TrackingUrlTemplate { get; set; }
    public string? AvailableCountries { get; set; }

    public virtual string? Validate()
    {
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
