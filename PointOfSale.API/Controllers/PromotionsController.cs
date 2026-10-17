using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using PointOfSale.API.Services.ReadModels;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Pagination;
using PointOfSale.Core.Validation;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

/// <summary>
/// API controller for managing Promotions (Offers) - auto-applied discounts.
/// 
/// ARCHITECTURE (PROMOTIONS ENGINE):
/// - Promotions (Offers) are auto-applied discounts (no code required)
/// - Coupons remain separate (code-based) - managed by CouponsController
/// - PromotionEngine reads both at checkout to calculate discounts
/// 
/// CQRS ARCHITECTURE:
/// - PostgreSQL is the source of truth (write-side)
/// - Events are published to MassTransit on create/update/delete
/// - PromotionProjectionConsumer projects events to MongoDB
/// - Storefront reads from MongoDB for badges/display
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PromotionsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<PromotionsController> _logger;
    private readonly IDiscountReadModelRepairService? _readModelRepairService;

    public PromotionsController(
        ApplicationDbContext dbContext,
        ITenantContext tenantContext,
        IEventPublisher eventPublisher,
        ILogger<PromotionsController> logger,
        IServiceScopeFactory? serviceScopeFactory = null,
        IDiscountReadModelRepairService? readModelRepairService = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _readModelRepairService = readModelRepairService;
    }

    /// <summary>
    /// Get all promotions (offers) for the tenant
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Owner,Admin,Manager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<List<PromotionDto>>> GetPromotions(
        [FromQuery] bool? activeOnly = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? search = null,
        [FromQuery] string sortBy = "priority",
        [FromQuery] string sortDirection = "desc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var now = DateTime.UtcNow;
        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize);

        var currency = await ResolveTenantCurrencyAsync(tenantId);
        var query = _dbContext.Promotions
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && !p.IsDeleted);

        if (activeOnly == true)
        {
            query = query.Where(p => p.IsActive && p.StartDate <= now && p.EndDate >= now);
        }

        if (!string.IsNullOrEmpty(scope))
        {
            if (Enum.TryParse<PromotionScope>(scope, true, out var scopeEnum))
            {
                query = query.Where(p => p.Scope == scopeEnum);
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            if (normalizedSearch.Length > 100)
            {
                return BadRequest(new { message = "Search must be 100 characters or fewer." });
            }

            var searchLower = normalizedSearch.ToLower();
            query = query.Where(p => 
                p.Name.ToLower().Contains(searchLower) ||
                (p.Description != null && p.Description.ToLower().Contains(searchLower)));
        }

        var promotions = await query
            .ApplyPromotionReadSort(sortBy, sortDirection)
            .Skip(PaginationGuard.GetSkip(page, pageSize))
            .Take(pageSize)
            .ToListAsync();

        return promotions.Select(promotion => MapToDto(promotion, currency)).ToList();
    }

    /// <summary>
    /// Get a promotion by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<PromotionDto>> GetPromotion(Guid id)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var promotion = await _dbContext.Promotions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId && !p.IsDeleted);
        
        if (promotion == null)
            return NotFound(new { message = "Promotion not found" });

        var currency = await ResolveTenantCurrencyAsync(tenantId);
        return MapToDto(promotion, currency);
    }

    /// <summary>
    /// Create a new promotion (offer)
    /// CQRS: Publishes PromotionCreatedEvent for MongoDB projection
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<PromotionDto>> CreatePromotion([FromBody] CreatePromotionDto dto)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        var discountValidationError = DiscountValidation.GetValidationError(dto.DiscountType, dto.DiscountValue);
        if (discountValidationError != null)
        {
            return BadRequest(new { message = discountValidationError });
        }

        // Validate dates
        if (dto.EndDate < dto.StartDate)
        {
            return BadRequest(new { message = "End date cannot be before start date" });
        }

        // Check for duplicate name
        var existingName = await _dbContext.Promotions
            .AnyAsync(p => p.TenantId == tenantId && !p.IsDeleted && p.Name.ToLower() == dto.Name.Trim().ToLower());

        if (existingName)
        {
            return BadRequest(new { message = "A promotion with this name already exists" });
        }

        var ruleAdmissionError = await ValidatePromotionRuleAdmissionAsync(tenantId, dto, cancellationToken: default);
        if (ruleAdmissionError != null)
        {
            return BadRequest(new { message = ruleAdmissionError });
        }

        var promotion = new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            DiscountType = dto.DiscountType,
            DiscountValue = dto.DiscountValue,
            MaxDiscountAmount = dto.MaxDiscountAmount,
            Scope = dto.Scope,
            AppliesToId = dto.AppliesToId,
            // Ensure dates are UTC - PostgreSQL timestamp with time zone requires UTC
            StartDate = DateTime.SpecifyKind(dto.StartDate, DateTimeKind.Utc),
            EndDate = DateTime.SpecifyKind(dto.EndDate, DateTimeKind.Utc),
            Priority = dto.Priority,
            IsActive = dto.IsActive,
            IsExclusive = dto.IsExclusive,
            MinOrderAmount = dto.MinOrderAmount,
            MaxUsage = dto.MaxUsage,
            CurrentUsage = 0,
            BannerStyle = dto.BannerStyle,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            promotion.Validate();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        _dbContext.Promotions.Add(promotion);
        
        // CRITICAL: Publish event BEFORE SaveChangesAsync for MassTransit Outbox
        // The outbox stores events in the same transaction as entity changes
        await PublishPromotionCreatedEventAsync(promotion);
        
        try { await _dbContext.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { message = "A promotion with this name already exists. Refresh before retrying." });
        }

        _logger.LogInformation(
            "[Promotions] Created promotion | Id: {Id} | Name: {Name} | Scope: {Scope} | TenantId: {TenantId}",
            promotion.Id, promotion.Name, promotion.Scope, tenantId);

        var currency = await ResolveTenantCurrencyAsync(tenantId);
        return CreatedAtAction(nameof(GetPromotion), new { id = promotion.Id }, MapToDto(promotion, currency));
    }

    /// <summary>
    /// Update a promotion
    /// CQRS: Publishes PromotionUpdatedEvent for MongoDB projection
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<PromotionDto>> UpdatePromotion(
        Guid id,
        [FromBody] UpdatePromotionDto dto,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var promotion = await LoadPromotionForLifecycleMutationAsync(tenantId, id, cancellationToken);
        
        if (promotion == null)
            return NotFound(new { message = "Promotion not found" });

        var expectedRowVersion = ParseRequiredPromotionIfMatch();
        if (expectedRowVersion.Error != null)
        {
            return expectedRowVersion.Error;
        }

        var currentRowVersion = GetPromotionRowVersion(promotion);
        if (currentRowVersion != expectedRowVersion.RowVersion!.Value)
        {
            return Conflict(new { message = "Promotion has changed. Refresh before retrying." });
        }

        var discountValidationError = DiscountValidation.GetValidationError(dto.DiscountType, dto.DiscountValue);
        if (discountValidationError != null)
        {
            return BadRequest(new { message = discountValidationError });
        }

        // Validate dates
        if (dto.EndDate < dto.StartDate)
        {
            return BadRequest(new { message = "End date cannot be before start date" });
        }

        // Check for duplicate name (excluding current)
        var existingName = await _dbContext.Promotions
            .AnyAsync(p => p.TenantId == tenantId && !p.IsDeleted && p.Id != id && p.Name.ToLower() == dto.Name.Trim().ToLower(), cancellationToken);

        if (existingName)
        {
            return BadRequest(new { message = "A promotion with this name already exists" });
        }

        var ruleAdmissionError = await ValidatePromotionRuleAdmissionAsync(tenantId, dto, cancellationToken);
        if (ruleAdmissionError != null)
        {
            return BadRequest(new { message = ruleAdmissionError });
        }

        promotion.Name = dto.Name.Trim();
        promotion.Description = dto.Description?.Trim();
        promotion.DiscountType = dto.DiscountType;
        promotion.DiscountValue = dto.DiscountValue;
        promotion.MaxDiscountAmount = dto.MaxDiscountAmount;
        promotion.Scope = dto.Scope;
        promotion.AppliesToId = dto.AppliesToId;
        // Ensure dates are UTC - PostgreSQL timestamp with time zone requires UTC
        promotion.StartDate = DateTime.SpecifyKind(dto.StartDate, DateTimeKind.Utc);
        promotion.EndDate = DateTime.SpecifyKind(dto.EndDate, DateTimeKind.Utc);
        promotion.Priority = dto.Priority;
        promotion.IsActive = dto.IsActive;
        promotion.IsExclusive = dto.IsExclusive;
        promotion.MinOrderAmount = dto.MinOrderAmount;
        promotion.MaxUsage = dto.MaxUsage;
        promotion.BannerStyle = dto.BannerStyle;
        promotion.UpdatedAt = DateTime.UtcNow;

        try
        {
            promotion.Validate();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        // CRITICAL: Publish event BEFORE SaveChangesAsync for MassTransit Outbox
        await PublishPromotionUpdatedEventAsync(promotion);

        _dbContext.Entry(promotion).Property(p => p.RowVersion).OriginalValue = expectedRowVersion.RowVersion.Value;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Promotion has changed. Refresh before retrying." });
        }

        _logger.LogInformation(
            "[Promotions] Updated promotion | Id: {Id} | Name: {Name}",
            promotion.Id, promotion.Name);

        var currency = await ResolveTenantCurrencyAsync(promotion.TenantId);
        return MapToDto(promotion, currency);
    }

    /// <summary>
    /// Delete a promotion
    /// CQRS: Publishes PromotionDeletedEvent for MongoDB projection
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> DeletePromotion(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var promotion = await LoadPromotionForLifecycleMutationAsync(tenantId, id, cancellationToken);
        
        if (promotion == null)
            return NotFound(new { message = "Promotion not found" });

        var expectedRowVersion = ParseRequiredPromotionIfMatch();
        if (expectedRowVersion.Error != null)
        {
            return expectedRowVersion.Error;
        }

        var currentRowVersion = GetPromotionRowVersion(promotion);
        if (currentRowVersion != expectedRowVersion.RowVersion!.Value)
        {
            return Conflict(new { message = "Promotion has changed. Refresh before retrying." });
        }

        // Check if promotion has been used
        var hasUsages = await _dbContext.PromotionUsages.AnyAsync(
            pu => pu.TenantId == tenantId && pu.PromotionId == id && !pu.IsDeleted,
            cancellationToken);
        
        if (hasUsages)
        {
            // Soft delete - promotion has history
            promotion.IsDeleted = true;
            promotion.IsActive = false;
            promotion.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // Hard delete - no usage history
            _dbContext.Promotions.Remove(promotion);
        }

        // CRITICAL: Publish event BEFORE SaveChangesAsync for MassTransit Outbox
        await PublishPromotionDeletedEventAsync(promotion);

        _dbContext.Entry(promotion).Property(p => p.RowVersion).OriginalValue = expectedRowVersion.RowVersion.Value;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Promotion has changed. Refresh before retrying." });
        }

        _logger.LogInformation(
            "[Promotions] Deleted promotion | Id: {Id} | Name: {Name} | HardDelete: {HardDelete}",
            id, promotion.Name, !hasUsages);

        return NoContent();
    }

    /// <summary>
    /// Toggle promotion active status
    /// </summary>
    [HttpPatch("{id:guid}/toggle")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<PromotionDto>> TogglePromotionStatus(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var promotion = await LoadPromotionForLifecycleMutationAsync(tenantId, id, cancellationToken);
        
        if (promotion == null)
            return NotFound(new { message = "Promotion not found" });

        var expectedRowVersion = ParseRequiredPromotionIfMatch();
        if (expectedRowVersion.Error != null)
        {
            return expectedRowVersion.Error;
        }

        var currentRowVersion = GetPromotionRowVersion(promotion);
        if (currentRowVersion != expectedRowVersion.RowVersion!.Value)
        {
            return Conflict(new { message = "Promotion has changed. Refresh before retrying." });
        }

        promotion.IsActive = !promotion.IsActive;
        promotion.UpdatedAt = DateTime.UtcNow;

        // CRITICAL: Publish event BEFORE SaveChangesAsync for MassTransit Outbox
        await PublishPromotionUpdatedEventAsync(promotion);

        _dbContext.Entry(promotion).Property(p => p.RowVersion).OriginalValue = expectedRowVersion.RowVersion.Value;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Promotion has changed. Refresh before retrying." });
        }

        _logger.LogInformation(
            "[Promotions] Toggled promotion status | Id: {Id} | IsActive: {IsActive}",
            id, promotion.IsActive);

        var currency = await ResolveTenantCurrencyAsync(promotion.TenantId);
        return MapToDto(promotion, currency);
    }

    /// <summary>
    /// Get promotion statistics
    /// </summary>
    [HttpGet("stats")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<PromotionStatsDto>> GetStats()
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var now = DateTime.UtcNow;
        var tenantPromotions = _dbContext.Promotions
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && !p.IsDeleted);
        var tenantPromotionUsages = _dbContext.PromotionUsages
            .AsNoTracking()
            .Where(pu => tenantPromotions.Any(p => p.Id == pu.PromotionId));

        var stats = new PromotionStatsDto
        {
            TenantId = tenantId,
            Currency = await ResolveTenantCurrencyAsync(tenantId),
            TotalPromotions = await tenantPromotions.CountAsync(),
            ActivePromotions = await tenantPromotions
                .CountAsync(p => p.IsActive && p.StartDate <= now && p.EndDate >= now),
            ScheduledPromotions = await tenantPromotions
                .CountAsync(p => p.IsActive && p.StartDate > now),
            ExpiredPromotions = await tenantPromotions
                .CountAsync(p => p.EndDate < now),
            TotalUsages = await tenantPromotionUsages.CountAsync(),
            TotalDiscountGiven = await tenantPromotionUsages.SumAsync(pu => (decimal?)pu.DiscountAmount) ?? 0m
        };

        return stats;
    }

    /// <summary>
    /// Rebuild promotion MongoDB and Elasticsearch read models directly from SQL authority.
    /// </summary>
    [HttpPost("sync-to-mongo")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> SyncPromotionsToMongo(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var repairService = _readModelRepairService
            ?? throw new InvalidOperationException("Promotion read-model repair service is not configured.");
        var synced = await repairService.RebuildPromotionsAsync(tenantId, cancellationToken);

        _logger.LogInformation(
            "Promotions rebuilt from SQL authority into read models | TenantId: {TenantId} | Total: {Total} | Synced: {Synced}",
            tenantId, synced, synced);

        return Ok(new { total = synced, synced });
    }

    #region Private Methods

    private async Task PublishPromotionCreatedEventAsync(Promotion promotion)
    {
        await _eventPublisher.PublishAsync(BuildPromotionCreatedEvent(promotion));
    }

    private PromotionCreatedEvent BuildPromotionCreatedEvent(Promotion promotion)
    {
        return new PromotionCreatedEvent
        {
            TenantId = promotion.TenantId,
            PromotionId = promotion.Id,
            Name = promotion.Name,
            Description = promotion.Description,
            DiscountType = promotion.DiscountType == DiscountType.Percentage ? "percentage" : "fixed",
            DiscountValue = promotion.DiscountValue,
            MaxDiscountAmount = promotion.MaxDiscountAmount,
            Scope = promotion.Scope.ToString().ToLower(),
            AppliesToId = promotion.AppliesToId,
            StartDate = promotion.StartDate,
            EndDate = promotion.EndDate,
            Priority = promotion.Priority,
            IsActive = promotion.IsActive,
            IsExclusive = promotion.IsExclusive,
            MinOrderAmount = promotion.MinOrderAmount,
            MaxUsage = promotion.MaxUsage,
            CurrentUsage = promotion.CurrentUsage,
            BannerStyle = promotion.BannerStyle,
            SearchBoostOverride = promotion.SearchBoostOverride,
            OccurredAtUtc = DateTime.UtcNow
        };
    }

    private async Task PublishPromotionUpdatedEventAsync(Promotion promotion)
    {
        var evt = new PromotionUpdatedEvent
        {
            TenantId = promotion.TenantId,
            PromotionId = promotion.Id,
            Name = promotion.Name,
            Description = promotion.Description,
            DiscountType = promotion.DiscountType == DiscountType.Percentage ? "percentage" : "fixed",
            DiscountValue = promotion.DiscountValue,
            MaxDiscountAmount = promotion.MaxDiscountAmount,
            Scope = promotion.Scope.ToString().ToLower(),
            AppliesToId = promotion.AppliesToId,
            StartDate = promotion.StartDate,
            EndDate = promotion.EndDate,
            Priority = promotion.Priority,
            IsActive = promotion.IsActive,
            IsExclusive = promotion.IsExclusive,
            MinOrderAmount = promotion.MinOrderAmount,
            MaxUsage = promotion.MaxUsage,
            CurrentUsage = promotion.CurrentUsage,
            BannerStyle = promotion.BannerStyle,
            SearchBoostOverride = promotion.SearchBoostOverride,
            OccurredAtUtc = DateTime.UtcNow
        };

        await _eventPublisher.PublishAsync(evt);
    }

    private async Task PublishPromotionDeletedEventAsync(Promotion promotion)
    {
        var evt = new PromotionDeletedEvent
        {
            TenantId = promotion.TenantId,
            PromotionId = promotion.Id,
            Name = promotion.Name,
            Scope = promotion.Scope.ToString().ToLowerInvariant(),
            AppliesToId = promotion.AppliesToId,
            OccurredAtUtc = DateTime.UtcNow
        };

        await _eventPublisher.PublishAsync(evt);
    }

    private async Task<string> ResolveTenantCurrencyAsync(Guid tenantId)
    {
        var currency = await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Currency)
            .SingleAsync();

        return FinancialCurrencyGuard.RequireCurrency(
            currency,
            "AdminPromotionRead",
            $"TenantId: {tenantId}");
    }

    private static PromotionDto MapToDto(Promotion promotion, string currency)
    {
        var now = DateTime.UtcNow;
        var isActive = promotion.IsActive && promotion.StartDate <= now && promotion.EndDate >= now;
        
        return new PromotionDto
        {
            Id = promotion.Id,
            TenantId = promotion.TenantId,
            Name = promotion.Name,
            Description = promotion.Description,
            DiscountType = promotion.DiscountType.ToString(),
            DiscountValue = promotion.DiscountValue,
            Currency = currency,
            MaxDiscountAmount = promotion.MaxDiscountAmount,
            Scope = promotion.Scope.ToString(),
            AppliesToId = promotion.AppliesToId,
            StartDate = promotion.StartDate,
            EndDate = promotion.EndDate,
            Priority = promotion.Priority,
            IsActive = promotion.IsActive,
            IsExclusive = promotion.IsExclusive,
            MinOrderAmount = promotion.MinOrderAmount,
            MaxUsage = promotion.MaxUsage,
            CurrentUsage = promotion.CurrentUsage,
            RowVersion = GetPromotionRowVersion(promotion).ToString(CultureInfo.InvariantCulture),
            IsCurrentlyActive = isActive,
            IsExpired = promotion.EndDate < now,
            IsScheduled = promotion.StartDate > now,
            Status = ResolvePromotionStatus(promotion, now),
            EligibilitySummary = ResolveEligibilitySummary(promotion),
            CreatedAt = promotion.CreatedAt,
            UpdatedAt = promotion.UpdatedAt,
            BannerStyle = promotion.BannerStyle
        };
    }

    private static string ResolvePromotionStatus(Promotion promotion, DateTime now)
    {
        if (!promotion.IsActive)
        {
            return "Inactive";
        }

        if (promotion.StartDate > now)
        {
            return "Scheduled";
        }

        if (promotion.EndDate < now)
        {
            return "Expired";
        }

        return "Active";
    }

    private static string ResolveEligibilitySummary(Promotion promotion)
    {
        var scope = promotion.Scope.ToString();
        var target = promotion.AppliesToId.HasValue ? "specific target" : "all eligible targets";
        var minimum = promotion.MinOrderAmount.HasValue ? $", minimum order {promotion.MinOrderAmount.Value:0.##}" : string.Empty;

        return $"{scope}: {target}{minimum}";
    }

    private async Task<string?> ValidatePromotionRuleAdmissionAsync(
        Guid tenantId,
        CreatePromotionDto dto,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(dto.Scope))
        {
            return "Promotion scope is not supported.";
        }

        if (dto.Priority < 0)
        {
            return "Priority cannot be negative.";
        }

        if (dto.AppliesToId == Guid.Empty)
        {
            return "Promotion rule target is invalid.";
        }

        if (dto.Scope == PromotionScope.Cart)
        {
            return dto.AppliesToId.HasValue
                ? "Cart promotions cannot target a product or category."
                : null;
        }

        if (!dto.AppliesToId.HasValue)
        {
            return null;
        }

        return dto.Scope switch
        {
            PromotionScope.Product => await _dbContext.Products
                .AsNoTracking()
                .AnyAsync(p =>
                    p.Id == dto.AppliesToId.Value &&
                    p.TenantId == tenantId &&
                    !p.IsDeleted &&
                    p.IsActive,
                    cancellationToken)
                ? null
                : "Promotion product target must be an active product in the current tenant.",

            PromotionScope.Category => await _dbContext.Categories
                .AsNoTracking()
                .AnyAsync(c =>
                    c.Id == dto.AppliesToId.Value &&
                    c.TenantId == tenantId &&
                    !c.IsDeleted,
                    cancellationToken)
                ? null
                : "Promotion category target must be a category in the current tenant.",

            _ => "Promotion scope is not supported."
        };
    }

    private async Task<Promotion?> LoadPromotionForLifecycleMutationAsync(
        Guid tenantId,
        Guid promotionId,
        CancellationToken cancellationToken)
    {
        var providerName = _dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.Promotions
                .FromSqlInterpolated($@"
                    SELECT xmin, *
                    FROM ""Promotions""
                    WHERE ""Id"" = {promotionId}
                      AND ""TenantId"" = {tenantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.Promotions
            .FirstOrDefaultAsync(p => p.Id == promotionId && p.TenantId == tenantId && !p.IsDeleted, cancellationToken);
    }

    private (uint? RowVersion, ActionResult? Error) ParseRequiredPromotionIfMatch()
    {
        if (!Request.Headers.TryGetValue("If-Match", out var headerValues))
        {
            return (null, StatusCode(
                StatusCodes.Status428PreconditionRequired,
                new { message = "Promotion lifecycle mutations require current RowVersion evidence." }));
        }

        var header = headerValues.FirstOrDefault()?.Trim().Trim('"');
        if (!uint.TryParse(header, out var rowVersion) || rowVersion == 0)
        {
            return (null, BadRequest(new { message = "If-Match must contain the current promotion RowVersion." }));
        }

        return (rowVersion, null);
    }

    private static uint GetPromotionRowVersion(Promotion promotion)
    {
        return promotion.RowVersion;
    }

    #endregion
}

#region DTOs

public class PromotionDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DiscountType { get; set; } = "Percentage";
    public decimal DiscountValue { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? MaxDiscountAmount { get; set; }
    public string Scope { get; set; } = "Product";
    public Guid? AppliesToId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; }
    public bool IsExclusive { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public int? MaxUsage { get; set; }
    public int CurrentUsage { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public bool IsCurrentlyActive { get; set; }
    public bool IsExpired { get; set; }
    public bool IsScheduled { get; set; }
    public string Status { get; set; } = string.Empty;
    public string EligibilitySummary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string BannerStyle { get; set; } = "gradient";
}

public class CreatePromotionDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;
    public decimal DiscountValue { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public PromotionScope Scope { get; set; } = PromotionScope.Product;
    public Guid? AppliesToId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int Priority { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public bool IsExclusive { get; set; } = false;
    public decimal? MinOrderAmount { get; set; }
    public int? MaxUsage { get; set; }
    /// <summary>
    /// Banner style for category promotions: gradient, solid, minimal, festive
    /// </summary>
    public string BannerStyle { get; set; } = "gradient";
}

public class UpdatePromotionDto : CreatePromotionDto { }

public class PromotionStatsDto
{
    public Guid TenantId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int TotalPromotions { get; set; }
    public int ActivePromotions { get; set; }
    public int ScheduledPromotions { get; set; }
    public int ExpiredPromotions { get; set; }
    public int TotalUsages { get; set; }
    public decimal TotalDiscountGiven { get; set; }
}

#endregion

internal static class PromotionReadQueryExtensions
{
    public static IQueryable<Promotion> ApplyPromotionReadSort(
        this IQueryable<Promotion> query,
        string? sortBy,
        string? sortDirection)
    {
        var descending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var normalizedSort = (sortBy ?? string.Empty).Trim().ToLowerInvariant();

        return normalizedSort switch
        {
            "name" => descending
                ? query.OrderByDescending(p => p.Name).ThenByDescending(p => p.CreatedAt)
                : query.OrderBy(p => p.Name).ThenByDescending(p => p.CreatedAt),
            "startdate" => descending
                ? query.OrderByDescending(p => p.StartDate).ThenByDescending(p => p.CreatedAt)
                : query.OrderBy(p => p.StartDate).ThenByDescending(p => p.CreatedAt),
            "enddate" => descending
                ? query.OrderByDescending(p => p.EndDate).ThenByDescending(p => p.CreatedAt)
                : query.OrderBy(p => p.EndDate).ThenByDescending(p => p.CreatedAt),
            "createdat" => descending
                ? query.OrderByDescending(p => p.CreatedAt)
                : query.OrderBy(p => p.CreatedAt),
            _ => descending
                ? query.OrderByDescending(p => p.Priority).ThenByDescending(p => p.CreatedAt)
                : query.OrderBy(p => p.Priority).ThenByDescending(p => p.CreatedAt),
        };
    }
}
