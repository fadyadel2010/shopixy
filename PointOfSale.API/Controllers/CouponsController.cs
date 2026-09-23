using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Helpers;
using PointOfSale.API.Services.ReadModels;
using PointOfSale.Core.Discounts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Controllers;

/// <summary>
/// API controller for managing coupons/promo codes
/// 
/// CQRS ARCHITECTURE:
/// - PostgreSQL is the source of truth for coupon identity, lifecycle, eligibility, and usage
/// - Events are published to MassTransit on create/update/delete
/// - CouponProjectionConsumer projects derivative visibility documents to MongoDB
/// - StorefrontCouponService validates accepted coupon state through SQL-backed coupon authority
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CouponsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ICouponValidationService _couponValidationService;
    private readonly ILogger<CouponsController> _logger;
    private readonly CommerceReliabilityTelemetry? _reliabilityTelemetry;
    private readonly IDiscountReadModelRepairService? _readModelRepairService;

    private Guid TenantId => _tenantContext.RequiredTenantId;

    public CouponsController(
        ApplicationDbContext context, 
        ITenantContext tenantContext,
        IEventPublisher eventPublisher,
        ICouponValidationService couponValidationService,
        ILogger<CouponsController> logger,
        IServiceScopeFactory? serviceScopeFactory = null,
        CommerceReliabilityTelemetry? reliabilityTelemetry = null,
        IDiscountReadModelRepairService? readModelRepairService = null)
    {
        _context = context;
        _tenantContext = tenantContext;
        _eventPublisher = eventPublisher;
        _couponValidationService = couponValidationService;
        _logger = logger;
        _reliabilityTelemetry = reliabilityTelemetry;
        _readModelRepairService = readModelRepairService;
    }

    /// <summary>
    /// Get all coupons
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Owner,Admin,Manager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<IEnumerable<CouponDto>>> GetCoupons(
        [FromQuery] bool? activeOnly = null,
        [FromQuery] string? search = null,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] string sortDirection = "desc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize);

        var tenantId = TenantId;
        var currency = await ResolveTenantCurrencyAsync(tenantId);
        var query = _context.Coupons
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && !c.IsDeleted);
        
        if (activeOnly == true)
        {
            query = query.Where(c => c.IsActive);
        }
        
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            if (normalizedSearch.Length > 100)
            {
                return BadRequest(new { message = "Search must be 100 characters or fewer." });
            }

            query = query.Where(c => c.Code.Contains(normalizedSearch) ||
                                    (c.Description != null && c.Description.Contains(normalizedSearch)));
        }
        
        var coupons = await query
            .ApplyCouponReadSort(sortBy, sortDirection)
            .Skip(PaginationGuard.GetSkip(page, pageSize))
            .Take(pageSize)
            .ToListAsync();
        
        return Ok(coupons.Select(coupon => MapToCouponDto(coupon, currency)).ToList());
    }

    /// <summary>
    /// Get a coupon by ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<CouponDto>> GetCoupon(Guid id)
    {
        var tenantId = TenantId;
        var coupon = await _context.Coupons
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId && !c.IsDeleted);
        
        if (coupon == null)
        {
            return NotFound(new { message = "Coupon not found" });
        }
        
        var currency = await ResolveTenantCurrencyAsync(tenantId);
        return Ok(MapToCouponDto(coupon, currency));
    }

    /// <summary>
    /// Validate a coupon code (Legacy endpoint - StorefrontCouponService is preferred)
    /// </summary>
    [HttpPost("validate")]
    [Authorize(AuthenticationSchemes = "StorefrontJwt")]
    [EnableRateLimiting("CouponRateLimit")]
    public async Task<ActionResult<CouponValidationResult>> ValidateCoupon(
        ValidateCouponRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.TenantId.HasValue || _tenantContext.TenantId.Value == Guid.Empty)
        {
            return BadRequest(new { message = "Tenant context is required to validate coupons." });
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Ok(new CouponValidationResult
            {
                IsValid = false,
                ErrorCode = PointOfSale.Core.Discounts.CouponErrorCodes.Invalid,
                ErrorMessage = "Coupon code is required."
            });
        }

        if (request.OrderTotal < 0)
        {
            return Ok(new CouponValidationResult
            {
                IsValid = false,
                ErrorCode = PointOfSale.Core.Discounts.CouponErrorCodes.InvalidOrderTotal,
                ErrorMessage = "Order total must be zero or greater."
            });
        }

        var tenantId = _tenantContext.RequiredTenantId;
        var customerId = User.GetCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(new { message = "You must sign in to use coupons." });
        }

        var validation = await _couponValidationService.ValidateCouponByCodeAsync(
            tenantId,
            request.Code,
            customerId.Value,
            new CouponValidationSnapshot
            {
                OrderSubtotal = request.OrderTotal
            },
            HttpContext.TraceIdentifier,
            cancellationToken: cancellationToken);

        return Ok(new CouponValidationResult
        {
            IsValid = validation.IsValid,
            ErrorCode = validation.ErrorCode,
            ErrorMessage = validation.ErrorMessage,
            CouponId = validation.CouponId,
            Code = validation.CouponCode,
            DiscountType = string.Equals(validation.DiscountType, "percentage", StringComparison.OrdinalIgnoreCase)
                ? DiscountType.Percentage
                : string.Equals(validation.DiscountType, "fixed", StringComparison.OrdinalIgnoreCase)
                    ? DiscountType.FixedAmount
                    : null,
            DiscountValue = validation.DiscountValue,
            DiscountAmount = validation.CalculatedDiscountAmount
        });
    }

    /// <summary>
    /// Create a new coupon
    /// CQRS: Publishes CouponCreatedEvent for MongoDB projection
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<CouponDto>> CreateCoupon(CreateCouponDto dto, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        // Create coupon and set code (which handles normalization)
        var coupon = new Coupon
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Description = dto.Description,
            DiscountType = dto.DiscountType,
            DiscountValue = dto.DiscountValue,
            MinOrderAmount = dto.MinOrderAmount,
            MaxDiscountAmount = dto.MaxDiscountAmount,
            MaxUsage = dto.MaxUsage,
            MaxUsagePerCustomer = dto.MaxUsagePerCustomer,
            StartDate = dto.StartDate.HasValue ? DateTime.SpecifyKind(dto.StartDate.Value, DateTimeKind.Utc) : null,
            EndDate = dto.EndDate.HasValue ? DateTime.SpecifyKind(dto.EndDate.Value, DateTimeKind.Utc) : null,
            IsActive = dto.IsActive,
            FirstOrderOnly = dto.FirstOrderOnly,
            ApplicableProductIds = dto.ApplicableProductIds,
            ApplicableCategoryIds = dto.ApplicableCategoryIds,
            ExcludedProductIds = dto.ExcludedProductIds,
            CreatedAt = DateTime.UtcNow
        };

        coupon.SetCode(dto.Code);

        var createValidationMessage = await ValidateAndCanonicalizeCouponEligibilityAsync(
            coupon,
            "admin_create",
            cancellationToken);
        if (createValidationMessage != null)
        {
            return BadRequest(new { message = createValidationMessage });
        }
        
        // Check if code already exists for THIS TENANT
        // Multi-tenant: Same code can exist for different tenants
        var exists = await _context.Coupons.AnyAsync(c => 
            c.TenantId == tenantId && 
            c.NormalizedCode == coupon.NormalizedCode && 
            !c.IsDeleted,
            cancellationToken);
        if (exists)
        {
            return BadRequest(new { message = "A coupon with this code already exists" });
        }

        _context.Coupons.Add(coupon);
        
        // CRITICAL: Publish event BEFORE SaveChangesAsync for MassTransit Outbox
        // The outbox stores events in the same transaction as entity changes
        await PublishCouponCreatedEventAsync(coupon, cancellationToken);
        
        await _context.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation(
            "Coupon created | CouponId: {CouponId} | Code: {Code} | TenantId: {TenantId}",
            coupon.Id, coupon.Code, tenantId);
        
        var currency = await ResolveTenantCurrencyAsync(tenantId);
        return CreatedAtAction(nameof(GetCoupon), new { id = coupon.Id }, MapToCouponDto(coupon, currency));
    }

    /// <summary>
    /// Update a coupon
    /// CQRS: Publishes CouponUpdatedEvent for MongoDB projection
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> UpdateCoupon(Guid id, UpdateCouponDto dto, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var coupon = await LoadCouponForLifecycleMutationAsync(tenantId, id, cancellationToken);
        
        if (coupon == null)
        {
            return NotFound(new { message = "Coupon not found" });
        }

        var expectedRowVersion = ParseRequiredCouponIfMatch();
        if (expectedRowVersion.Error != null)
        {
            return expectedRowVersion.Error;
        }

        var currentRowVersion = GetCouponRowVersion(coupon);
        if (currentRowVersion != expectedRowVersion.RowVersion!.Value)
        {
            return Conflict(new { message = "Coupon has changed. Refresh before retrying." });
        }

        // Check if new code already exists for THIS TENANT (if code is being changed)
        // Multi-tenant: Same code can exist for different tenants
        if (!coupon.Code.Equals(dto.Code, StringComparison.OrdinalIgnoreCase))
        {
            coupon.SetCode(dto.Code);
            
            var exists = await _context.Coupons.AnyAsync(c => 
                c.TenantId == tenantId &&
                c.NormalizedCode == coupon.NormalizedCode && 
                c.Id != id &&
                !c.IsDeleted,
                cancellationToken);
            if (exists)
            {
                return BadRequest(new { message = "A coupon with this code already exists" });
            }
        }
        else
        {
            // Keep existing code (already normalized)
            // Just update other fields below
        }
        
        coupon.Description = dto.Description;
        coupon.DiscountType = dto.DiscountType;
        coupon.DiscountValue = dto.DiscountValue;
        coupon.MinOrderAmount = dto.MinOrderAmount;
        coupon.MaxDiscountAmount = dto.MaxDiscountAmount;
        coupon.MaxUsage = dto.MaxUsage;
        coupon.MaxUsagePerCustomer = dto.MaxUsagePerCustomer;
        coupon.StartDate = dto.StartDate.HasValue ? DateTime.SpecifyKind(dto.StartDate.Value, DateTimeKind.Utc) : null;
        coupon.EndDate = dto.EndDate.HasValue ? DateTime.SpecifyKind(dto.EndDate.Value, DateTimeKind.Utc) : null;
        coupon.IsActive = dto.IsActive;
        coupon.FirstOrderOnly = dto.FirstOrderOnly;
        coupon.ApplicableProductIds = dto.ApplicableProductIds;
        coupon.ApplicableCategoryIds = dto.ApplicableCategoryIds;
        coupon.ExcludedProductIds = dto.ExcludedProductIds;

        var updateValidationMessage = await ValidateAndCanonicalizeCouponEligibilityAsync(
            coupon,
            "admin_update",
            cancellationToken);
        if (updateValidationMessage != null)
        {
            return BadRequest(new { message = updateValidationMessage });
        }

        _context.Entry(coupon).Property(c => c.RowVersion).OriginalValue = expectedRowVersion.RowVersion.Value;
        
        // CRITICAL: Publish event BEFORE SaveChangesAsync for MassTransit Outbox
        await PublishCouponUpdatedEventAsync(coupon, cancellationToken);
        
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Coupon has changed. Refresh before retrying." });
        }
        
        _logger.LogInformation(
            "Coupon updated | CouponId: {CouponId} | Code: {Code} | TenantId: {TenantId}",
            coupon.Id, coupon.Code, coupon.TenantId);
        
        var currency = await ResolveTenantCurrencyAsync(tenantId);
        return Ok(MapToCouponDto(coupon, currency));
    }

    /// <summary>
    /// Delete a coupon
    /// CQRS: Publishes CouponDeletedEvent for MongoDB projection
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCoupon(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var coupon = await LoadCouponForLifecycleMutationAsync(tenantId, id, cancellationToken);
        
        if (coupon == null)
        {
            return NotFound(new { message = "Coupon not found" });
        }

        var expectedRowVersion = ParseRequiredCouponIfMatch();
        if (expectedRowVersion.Error != null)
        {
            return expectedRowVersion.Error;
        }

        var currentRowVersion = GetCouponRowVersion(coupon);
        if (currentRowVersion != expectedRowVersion.RowVersion!.Value)
        {
            return Conflict(new { message = "Coupon has changed. Refresh before retrying." });
        }
        
        // Soft delete
        coupon.IsDeleted = true;
        _context.Entry(coupon).Property(c => c.RowVersion).OriginalValue = expectedRowVersion.RowVersion.Value;
        
        // CRITICAL: Publish event BEFORE SaveChangesAsync for MassTransit Outbox
        await PublishCouponDeletedEventAsync(coupon, cancellationToken);
        
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Coupon has changed. Refresh before retrying." });
        }
        
        _logger.LogInformation(
            "Coupon deleted | CouponId: {CouponId} | Code: {Code} | TenantId: {TenantId}",
            coupon.Id, coupon.Code, coupon.TenantId);
        
        return NoContent();
    }

    /// <summary>
    /// Toggle coupon active status
    /// CQRS: Publishes CouponUpdatedEvent for MongoDB projection
    /// </summary>
    [HttpPatch("{id}/toggle")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> ToggleCouponStatus(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var coupon = await LoadCouponForLifecycleMutationAsync(tenantId, id, cancellationToken);
        
        if (coupon == null)
        {
            return NotFound(new { message = "Coupon not found" });
        }

        var expectedRowVersion = ParseRequiredCouponIfMatch();
        if (expectedRowVersion.Error != null)
        {
            return expectedRowVersion.Error;
        }

        var currentRowVersion = GetCouponRowVersion(coupon);
        if (currentRowVersion != expectedRowVersion.RowVersion!.Value)
        {
            return Conflict(new { message = "Coupon has changed. Refresh before retrying." });
        }
        
        coupon.IsActive = !coupon.IsActive;
        _context.Entry(coupon).Property(c => c.RowVersion).OriginalValue = expectedRowVersion.RowVersion.Value;
        
        // CRITICAL: Publish event BEFORE SaveChangesAsync for MassTransit Outbox
        await PublishCouponUpdatedEventAsync(coupon, cancellationToken);
        
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Coupon has changed. Refresh before retrying." });
        }
        
        _logger.LogInformation(
            "Coupon toggled | CouponId: {CouponId} | Code: {Code} | IsActive: {IsActive} | TenantId: {TenantId}",
            coupon.Id, coupon.Code, coupon.IsActive, coupon.TenantId);
        
        var currency = await ResolveTenantCurrencyAsync(tenantId);
        return Ok(MapToCouponDto(coupon, currency));
    }

    /// <summary>
    /// Rebuild coupon MongoDB read models directly from SQL authority.
    /// </summary>
    [HttpPost("sync-to-mongo")]
    public async Task<IActionResult> SyncCouponsToMongo(CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var repairService = _readModelRepairService
            ?? throw new InvalidOperationException("Coupon read-model repair service is not configured.");
        var synced = await repairService.RebuildCouponsAsync(tenantId, cancellationToken);

        _logger.LogInformation(
            "Coupons rebuilt from SQL authority into MongoDB | TenantId: {TenantId} | Total: {Total} | Synced: {Synced}",
            tenantId, synced, synced);

        return Ok(new { total = synced, synced });
    }

    #region Event Publishing

    private async Task PublishCouponCreatedEventAsync(Coupon coupon, CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(BuildCouponCreatedEvent(coupon), cancellationToken);
    }

    private CouponCreatedEvent BuildCouponCreatedEvent(Coupon coupon)
    {
        // Validate product IDs parsing - fail-closed on malformed data
        if (!TryParseProductIds(coupon.ApplicableProductIds, out var applicableProductIds, out var applicableError))
        {
            _logger.LogError(
                "Failed to parse ApplicableProductIds for coupon | CouponId: {CouponId} | TenantId: {TenantId} | Error: {Error}",
                coupon.Id,
                coupon.TenantId,
                applicableError);
            throw new InvalidOperationException(
                $"Failed to parse ApplicableProductIds for coupon {coupon.Id}: {applicableError}");
        }

        if (!TryParseCategoryIds(coupon.ApplicableCategoryIds, out var applicableCategoryIds, out var categoryError))
        {
            _logger.LogError(
                "Failed to parse ApplicableCategoryIds for coupon | CouponId: {CouponId} | TenantId: {TenantId} | Error: {Error}",
                coupon.Id,
                coupon.TenantId,
                categoryError);
            throw new InvalidOperationException(
                $"Failed to parse ApplicableCategoryIds for coupon {coupon.Id}: {categoryError}");
        }

        if (!TryParseProductIds(coupon.ExcludedProductIds, out var excludedProductIds, out var excludedError))
        {
            _logger.LogError(
                "Failed to parse ExcludedProductIds for coupon | CouponId: {CouponId} | TenantId: {TenantId} | Error: {Error}",
                coupon.Id,
                coupon.TenantId,
                excludedError);
            throw new InvalidOperationException(
                $"Failed to parse ExcludedProductIds for coupon {coupon.Id}: {excludedError}");
        }

        return new CouponCreatedEvent
        {
            TenantId = coupon.TenantId,
            CouponId = coupon.Id,
            Code = coupon.Code,
            DiscountType = coupon.DiscountType == DiscountType.Percentage ? "percentage" : "fixed",
            DiscountValue = coupon.DiscountValue,
            MaxDiscountAmount = coupon.MaxDiscountAmount,
            MinOrderAmount = coupon.MinOrderAmount,
            MaxUsage = coupon.MaxUsage,
            CurrentUsage = coupon.CurrentUsage,
            MaxUsagePerCustomer = coupon.MaxUsagePerCustomer,
            FirstOrderOnly = coupon.FirstOrderOnly,
            StartDate = coupon.StartDate,
            EndDate = coupon.EndDate,
            IsActive = coupon.IsActive,
            Description = coupon.Description,
            ApplicableProductIds = applicableProductIds,
            ApplicableCategoryIds = applicableCategoryIds,
            ExcludedProductIds = excludedProductIds,
            OccurredAtUtc = DateTime.UtcNow
        };
    }

    private async Task PublishCouponUpdatedEventAsync(Coupon coupon, CancellationToken cancellationToken)
    {
        // Validate product IDs parsing - fail-closed on malformed data
        if (!TryParseProductIds(coupon.ApplicableProductIds, out var applicableProductIds, out var applicableError))
        {
            _logger.LogError(
                "Failed to parse ApplicableProductIds for coupon update | CouponId: {CouponId} | TenantId: {TenantId} | Error: {Error}",
                coupon.Id,
                coupon.TenantId,
                applicableError);
            throw new InvalidOperationException(
                $"Failed to parse ApplicableProductIds for coupon {coupon.Id}: {applicableError}");
        }

        if (!TryParseCategoryIds(coupon.ApplicableCategoryIds, out var applicableCategoryIds, out var categoryError))
        {
            _logger.LogError(
                "Failed to parse ApplicableCategoryIds for coupon update | CouponId: {CouponId} | TenantId: {TenantId} | Error: {Error}",
                coupon.Id,
                coupon.TenantId,
                categoryError);
            throw new InvalidOperationException(
                $"Failed to parse ApplicableCategoryIds for coupon {coupon.Id}: {categoryError}");
        }

        if (!TryParseProductIds(coupon.ExcludedProductIds, out var excludedProductIds, out var excludedError))
        {
            _logger.LogError(
                "Failed to parse ExcludedProductIds for coupon update | CouponId: {CouponId} | TenantId: {TenantId} | Error: {Error}",
                coupon.Id,
                coupon.TenantId,
                excludedError);
            throw new InvalidOperationException(
                $"Failed to parse ExcludedProductIds for coupon {coupon.Id}: {excludedError}");
        }

        var evt = new CouponUpdatedEvent
        {
            TenantId = coupon.TenantId,
            CouponId = coupon.Id,
            Code = coupon.Code,
            DiscountType = coupon.DiscountType == DiscountType.Percentage ? "percentage" : "fixed",
            DiscountValue = coupon.DiscountValue,
            MaxDiscountAmount = coupon.MaxDiscountAmount,
            MinOrderAmount = coupon.MinOrderAmount,
            MaxUsage = coupon.MaxUsage,
            CurrentUsage = coupon.CurrentUsage,
            MaxUsagePerCustomer = coupon.MaxUsagePerCustomer,
            FirstOrderOnly = coupon.FirstOrderOnly,
            StartDate = coupon.StartDate,
            EndDate = coupon.EndDate,
            IsActive = coupon.IsActive,
            Description = coupon.Description,
            ApplicableProductIds = applicableProductIds,
            ApplicableCategoryIds = applicableCategoryIds,
            ExcludedProductIds = excludedProductIds,
            OccurredAtUtc = DateTime.UtcNow
        };

        await _eventPublisher.PublishAsync(evt, cancellationToken);
    }

    private async Task PublishCouponDeletedEventAsync(Coupon coupon, CancellationToken cancellationToken)
    {
        var evt = new CouponDeletedEvent
        {
            TenantId = coupon.TenantId,
            CouponId = coupon.Id,
            Code = coupon.Code,
            OccurredAtUtc = DateTime.UtcNow
        };

        await _eventPublisher.PublishAsync(evt, cancellationToken);
    }

    private static List<Guid>? ParseProductIds(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(json);
        }
        catch
        {
            // Return null on error - caller must check for malformed data
            return null;
        }
    }

    private static bool TryParseProductIds(string? json, out List<Guid>? result, out string? errorMessage)
    {
        result = null;
        errorMessage = null;

        if (string.IsNullOrEmpty(json))
        {
            return true; // Empty is valid
        }

        try
        {
            result = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(json);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static bool TryParseCategoryIds(string? json, out List<Guid>? result, out string? errorMessage)
    {
        result = null;
        errorMessage = null;

        if (string.IsNullOrEmpty(json))
        {
            return true; // Empty is valid
        }

        try
        {
            result = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(json);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static List<Guid>? ParseCategoryIds(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(json);
        }
        catch
        {
            return null;
        }
    }

    #endregion

    private async Task<string?> ValidateAndCanonicalizeCouponEligibilityAsync(
        Coupon coupon,
        string surface,
        CancellationToken cancellationToken)
    {
        try
        {
            coupon.Validate();
        }
        catch (InvalidOperationException ex)
        {
            _reliabilityTelemetry?.RecordCouponInvalidConfiguration(coupon.TenantId, surface, ex.Message);
            return ex.Message;
        }

        if (!TryParseCanonicalGuidList(coupon.ApplicableProductIds, out var applicableProductIds, out var applicableProductJson, out var applicableProductError))
        {
            _reliabilityTelemetry?.RecordCouponInvalidConfiguration(coupon.TenantId, surface, applicableProductError ?? "invalid_applicable_product_ids");
            return applicableProductError;
        }

        if (!TryParseCanonicalGuidList(coupon.ApplicableCategoryIds, out var applicableCategoryIds, out var applicableCategoryJson, out var applicableCategoryError))
        {
            _reliabilityTelemetry?.RecordCouponInvalidConfiguration(coupon.TenantId, surface, applicableCategoryError ?? "invalid_applicable_category_ids");
            return applicableCategoryError;
        }

        if (!TryParseCanonicalGuidList(coupon.ExcludedProductIds, out var excludedProductIds, out var excludedProductJson, out var excludedProductError))
        {
            _reliabilityTelemetry?.RecordCouponInvalidConfiguration(coupon.TenantId, surface, excludedProductError ?? "invalid_excluded_product_ids");
            return excludedProductError;
        }

        if (applicableProductIds.Count > 0 && excludedProductIds.Count > 0)
        {
            var excludedSet = excludedProductIds.ToHashSet();
            if (applicableProductIds.Any(excludedSet.Contains))
            {
                const string message = "A product cannot be both applicable and excluded for the same coupon.";
                _reliabilityTelemetry?.RecordCouponInvalidConfiguration(coupon.TenantId, surface, message);
                return message;
            }
        }

        var allProductIds = applicableProductIds.Concat(excludedProductIds).Distinct().ToList();
        if (allProductIds.Count > 0)
        {
            var existingProductCount = await _context.Products
                .AsNoTracking()
                .IgnoreQueryFilters()
                .CountAsync(
                    product => product.TenantId == coupon.TenantId &&
                               !product.IsDeleted &&
                               allProductIds.Contains(product.Id),
                    cancellationToken);

            if (existingProductCount != allProductIds.Count)
            {
                const string message = "Coupon product eligibility must reference existing products owned by the current tenant.";
                _reliabilityTelemetry?.RecordCouponInvalidConfiguration(coupon.TenantId, surface, message);
                return message;
            }
        }

        if (applicableCategoryIds.Count > 0)
        {
            var existingCategoryCount = await _context.Categories
                .AsNoTracking()
                .IgnoreQueryFilters()
                .CountAsync(
                    category => category.TenantId == coupon.TenantId &&
                                !category.IsDeleted &&
                                applicableCategoryIds.Contains(category.Id),
                    cancellationToken);

            if (existingCategoryCount != applicableCategoryIds.Count)
            {
                const string message = "Coupon category eligibility must reference existing categories owned by the current tenant.";
                _reliabilityTelemetry?.RecordCouponInvalidConfiguration(coupon.TenantId, surface, message);
                return message;
            }
        }

        coupon.ApplicableProductIds = applicableProductJson;
        coupon.ApplicableCategoryIds = applicableCategoryJson;
        coupon.ExcludedProductIds = excludedProductJson;

        return null;
    }

    private static bool TryParseCanonicalGuidList(
        string? json,
        out List<Guid> ids,
        out string? canonicalJson,
        out string? errorMessage)
    {
        ids = [];
        canonicalJson = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(json);
            if (parsed == null)
            {
                errorMessage = "Coupon eligibility IDs must be a JSON array.";
                return false;
            }

            if (parsed.Any(id => id == Guid.Empty))
            {
                errorMessage = "Coupon eligibility IDs cannot contain empty GUID values.";
                return false;
            }

            ids = parsed
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            canonicalJson = ids.Count == 0
                ? null
                : System.Text.Json.JsonSerializer.Serialize(ids);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private async Task<string> ResolveTenantCurrencyAsync(Guid tenantId)
    {
        var currency = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Currency)
            .SingleAsync();

        return FinancialCurrencyGuard.RequireCurrency(
            currency,
            "AdminCouponRead",
            $"TenantId: {tenantId}");
    }

    private CouponDto MapToCouponDto(Coupon coupon, string currency)
    {
        return new CouponDto
        {
            Id = coupon.Id,
            TenantId = coupon.TenantId,
            RowVersion = GetCouponRowVersion(coupon).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Code = coupon.Code,
            Name = string.IsNullOrWhiteSpace(coupon.Description) ? coupon.Code : coupon.Description.Trim(),
            Description = coupon.Description,
            DiscountType = coupon.DiscountType,
            DiscountValue = coupon.DiscountValue,
            Currency = currency,
            MinOrderAmount = coupon.MinOrderAmount,
            MaxDiscountAmount = coupon.MaxDiscountAmount,
            MaxUsage = coupon.MaxUsage,
            CurrentUsage = coupon.CurrentUsage,
            MaxUsagePerCustomer = coupon.MaxUsagePerCustomer,
            StartDate = coupon.StartDate,
            EndDate = coupon.EndDate,
            IsActive = coupon.IsActive,
            FirstOrderOnly = coupon.FirstOrderOnly,
            ApplicableProductIds = coupon.ApplicableProductIds,
            ApplicableCategoryIds = coupon.ApplicableCategoryIds,
            ExcludedProductIds = coupon.ExcludedProductIds,
            IsValid = coupon.IsValid(),
            Status = ResolveCouponStatus(coupon),
            EligibilitySummary = ResolveEligibilitySummary(coupon)
        };
    }

    private async Task<Coupon?> LoadCouponForLifecycleMutationAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken)
    {
        var providerName = _context.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await _context.Coupons
                .FromSqlInterpolated($@"
                    SELECT xmin, *
                    FROM ""Coupons""
                    WHERE ""Id"" = {couponId}
                      AND ""TenantId"" = {tenantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return await _context.Coupons
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [Coupons] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {couponId}
                      AND [TenantId] = {tenantId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _context.Coupons
            .FirstOrDefaultAsync(c => c.Id == couponId && c.TenantId == tenantId && !c.IsDeleted, cancellationToken);
    }

    private (uint? RowVersion, IActionResult? Error) ParseRequiredCouponIfMatch()
    {
        if (!Request.Headers.TryGetValue("If-Match", out var headerValues))
        {
            return (null, StatusCode(
                StatusCodes.Status428PreconditionRequired,
                new { message = "Coupon lifecycle mutations require current RowVersion evidence." }));
        }

        var header = headerValues.FirstOrDefault()?.Trim().Trim('"');
        if (!uint.TryParse(header, out var rowVersion) || rowVersion == 0)
        {
            return (null, BadRequest(new { message = "If-Match must contain the current coupon RowVersion." }));
        }

        return (rowVersion, null);
    }

    private uint GetCouponRowVersion(Coupon coupon)
    {
        return coupon.RowVersion;
    }

    private static string ResolveCouponStatus(Coupon coupon)
    {
        if (!coupon.IsActive)
        {
            return "Inactive";
        }

        return coupon.IsValid() ? "Active" : "Expired";
    }

    private static string ResolveEligibilitySummary(Coupon coupon)
    {
        var parts = new List<string>();

        if (coupon.FirstOrderOnly)
        {
            parts.Add("First order only");
        }

        if (coupon.MinOrderAmount.HasValue)
        {
            parts.Add($"Minimum order {coupon.MinOrderAmount.Value:0.##}");
        }

        if (!string.IsNullOrWhiteSpace(coupon.ApplicableProductIds))
        {
            parts.Add("Product scoped");
        }

        if (!string.IsNullOrWhiteSpace(coupon.ApplicableCategoryIds))
        {
            parts.Add("Category scoped");
        }

        if (!string.IsNullOrWhiteSpace(coupon.ExcludedProductIds))
        {
            parts.Add("Exclusions configured");
        }

        return parts.Count == 0 ? "All eligible orders" : string.Join("; ", parts);
    }
}

internal static class CouponReadQueryExtensions
{
    private static readonly HashSet<string> SupportedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "createdAt",
        "code",
        "discountValue",
        "currentUsage",
        "startDate",
        "endDate",
        "status"
    };

    public static IQueryable<Coupon> ApplyCouponReadSort(
        this IQueryable<Coupon> query,
        string? sortBy,
        string? sortDirection)
    {
        var field = string.IsNullOrWhiteSpace(sortBy) ? "createdAt" : sortBy.Trim();
        if (!SupportedSortFields.Contains(field))
        {
            field = "createdAt";
        }

        var descending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        return field.ToLowerInvariant() switch
        {
            "code" => descending
                ? query.OrderByDescending(c => c.Code)
                : query.OrderBy(c => c.Code),
            "discountvalue" => descending
                ? query.OrderByDescending(c => c.DiscountValue).ThenByDescending(c => c.CreatedAt)
                : query.OrderBy(c => c.DiscountValue).ThenByDescending(c => c.CreatedAt),
            "currentusage" => descending
                ? query.OrderByDescending(c => c.CurrentUsage).ThenByDescending(c => c.CreatedAt)
                : query.OrderBy(c => c.CurrentUsage).ThenByDescending(c => c.CreatedAt),
            "startdate" => descending
                ? query.OrderByDescending(c => c.StartDate).ThenByDescending(c => c.CreatedAt)
                : query.OrderBy(c => c.StartDate).ThenByDescending(c => c.CreatedAt),
            "enddate" => descending
                ? query.OrderByDescending(c => c.EndDate).ThenByDescending(c => c.CreatedAt)
                : query.OrderBy(c => c.EndDate).ThenByDescending(c => c.CreatedAt),
            "status" => descending
                ? query.OrderByDescending(c => c.IsActive).ThenByDescending(c => c.CreatedAt)
                : query.OrderBy(c => c.IsActive).ThenByDescending(c => c.CreatedAt),
            _ => descending
                ? query.OrderByDescending(c => c.CreatedAt)
                : query.OrderBy(c => c.CreatedAt),
        };
    }
}

#region DTOs

public class CouponDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? MinOrderAmount { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public int? MaxUsage { get; set; }
    public int CurrentUsage { get; set; }
    public int? MaxUsagePerCustomer { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    public bool FirstOrderOnly { get; set; }
    public string? ApplicableProductIds { get; set; }
    public string? ApplicableCategoryIds { get; set; }
    public string? ExcludedProductIds { get; set; }
    public bool IsValid { get; set; }
    public string Status { get; set; } = string.Empty;
    public string EligibilitySummary { get; set; } = string.Empty;
}

public class CreateCouponDto
{
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;
    public decimal DiscountValue { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public int? MaxUsage { get; set; }
    public int? MaxUsagePerCustomer { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public bool FirstOrderOnly { get; set; } = false;
    public string? ApplicableProductIds { get; set; }
    public string? ApplicableCategoryIds { get; set; }
    public string? ExcludedProductIds { get; set; }
}

public class UpdateCouponDto : CreateCouponDto { }

public class ValidateCouponRequest
{
    public string Code { get; set; } = string.Empty;
    public decimal OrderTotal { get; set; }
}

public class CouponValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? CouponId { get; set; }
    public string? Code { get; set; }
    public DiscountType? DiscountType { get; set; }
    public decimal? DiscountValue { get; set; }
    public decimal? DiscountAmount { get; set; }
}

#endregion
