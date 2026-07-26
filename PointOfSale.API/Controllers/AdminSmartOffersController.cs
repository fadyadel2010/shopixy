using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using PointOfSale.API.DTOs.SmartOffers;
using PointOfSale.API.Services.ReadModels;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Validation;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/smart-offers")]
[Authorize(Roles = "Owner,Admin,Manager")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminSmartOffersController : ControllerBase
{
    private const int DefaultSmartOfferCouponMaxUsage = 10000;
    private const int DefaultSmartOfferCouponMaxUsagePerCustomer = 1;

    private readonly ApplicationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly IDiscountReadModelRepairService? _readModelRepairService;
    private readonly ILogger<AdminSmartOffersController> _logger;

    public AdminSmartOffersController(
        ApplicationDbContext dbContext,
        ITenantContext tenantContext,
        IEventPublisher eventPublisher,
        ILogger<AdminSmartOffersController> logger,
        IDiscountReadModelRepairService? readModelRepairService = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _eventPublisher = eventPublisher;
        _readModelRepairService = readModelRepairService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<AdminSmartOfferDto>>> GetSmartOffers(
        [FromQuery] string? search,
        [FromQuery] SmartOfferStatus? status,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _dbContext.SmartOffers
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            if (term.Length > 100)
            {
                return BadRequest(new { message = "Search term must not exceed 100 characters." });
            }

            query = query.Where(x =>
                x.Name.ToLower().Contains(term) ||
                x.PopupTitle.ToLower().Contains(term) ||
                (x.CouponCode != null && x.CouponCode.ToLower().Contains(term)));
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        query = ApplySmartOfferReadSort(query, sortBy, sortDirection);

        var offers = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return await MapToAdminDtosAsync(tenantId, offers, cancellationToken);
    }

    [HttpGet("stats")]
    public async Task<ActionResult<AdminSmartOfferStatsDto>> GetSmartOfferStats(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var currency = await ResolveTenantCurrencyAsync(tenantId, cancellationToken);

        var offers = _dbContext.SmartOffers
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);

        return new AdminSmartOfferStatsDto
        {
            TenantId = tenantId,
            Currency = currency,
            TotalOffers = await offers.CountAsync(cancellationToken),
            ActiveOffers = await offers.CountAsync(x => x.Status == SmartOfferStatus.Active, cancellationToken),
            ExitIntentOffers = await offers.CountAsync(x => x.TriggerType == SmartOfferTriggerType.ExitIntent, cancellationToken)
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminSmartOfferDto>> GetSmartOffer(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var offer = await _dbContext.SmartOffers
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (offer == null)
        {
            return NotFound(new { message = "Smart offer not found." });
        }

        var currency = await ResolveTenantCurrencyAsync(tenantId, cancellationToken);
        var coupon = await LoadGeneratedCouponAsync(tenantId, offer.GeneratedCouponId, cancellationToken);
        return MapToAdminDto(offer, currency, coupon);
    }

    [HttpPost]
    public async Task<ActionResult<AdminSmartOfferDto>> CreateSmartOffer(
        [FromBody] UpsertSmartOfferRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateRequest(request);
        if (validationError != null)
        {
            return BadRequest(new { message = validationError });
        }

        var tenantId = _tenantContext.RequiredTenantId;
        var normalizedName = request.Name.Trim();

        var duplicate = await _dbContext.SmartOffers
            .AnyAsync(x => x.TenantId == tenantId && x.Name.ToLower() == normalizedName.ToLower(), cancellationToken);

        if (duplicate)
        {
            return BadRequest(new { message = "A smart offer with this name already exists." });
        }

        var entity = new SmartOffer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = normalizedName,
            Status = request.Status,
            Priority = request.Priority,
            TriggerType = request.TriggerType,
            PagesScope = request.PagesScope,
            DelaySeconds = NormalizeNullable(request.DelaySeconds),
            ScrollPercent = NormalizeNullable(request.ScrollPercent),
            ShowOncePerSession = request.ShowOncePerSession,
            MaxDisplaysPerUser = NormalizeNullable(request.MaxDisplaysPerUser),
            PopupTitle = request.PopupTitle.Trim(),
            PopupDescription = NormalizeText(request.PopupDescription),
            ButtonText = request.ButtonText.Trim(),
            CouponCode = NormalizeText(request.CouponCode)?.ToUpperInvariant(),
            CouponDiscountType = request.CouponDiscountType,
            CouponDiscountValue = request.CouponDiscountValue,
            CouponMinOrderAmount = request.CouponMinOrderAmount,
            CouponMaxDiscountAmount = request.CouponMaxDiscountAmount,
            ImageUrl = NormalizeText(request.ImageUrl),
            BackgroundColor = NormalizeText(request.BackgroundColor),
            CreatedAt = DateTime.UtcNow
        };

        entity.Validate();

        _dbContext.SmartOffers.Add(entity);
        var couponSyncError = await SyncGeneratedCouponAsync(entity, request, cancellationToken);
        if (couponSyncError != null)
        {
            return BadRequest(new { message = couponSyncError });
        }

        await PublishCreatedAsync(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Smart offer created | SmartOfferId: {SmartOfferId} | TenantId: {TenantId} | Name: {Name}",
            entity.Id,
            tenantId,
            entity.Name);

        var currency = await ResolveTenantCurrencyAsync(tenantId, cancellationToken);
        var coupon = await LoadGeneratedCouponAsync(tenantId, entity.GeneratedCouponId, cancellationToken);
        return CreatedAtAction(nameof(GetSmartOffer), new { id = entity.Id }, MapToAdminDto(entity, currency, coupon));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AdminSmartOfferDto>> UpdateSmartOffer(
        Guid id,
        [FromBody] UpsertSmartOfferRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateRequest(request);
        if (validationError != null)
        {
            return BadRequest(new { message = validationError });
        }

        var tenantId = _tenantContext.RequiredTenantId;
        var entity = await LoadSmartOfferForLifecycleMutationAsync(tenantId, id, cancellationToken);
        if (entity == null)
        {
            return NotFound(new { message = "Smart offer not found." });
        }

        var expectedRowVersion = ParseRequiredSmartOfferIfMatch();
        if (expectedRowVersion.Error != null)
        {
            return expectedRowVersion.Error;
        }

        var currentRowVersion = GetSmartOfferRowVersion(entity);
        if (currentRowVersion != expectedRowVersion.RowVersion!.Value)
        {
            return Conflict(new { message = "Smart offer has changed. Refresh before retrying." });
        }

        var normalizedName = request.Name.Trim();
        var duplicate = await _dbContext.SmartOffers
            .AnyAsync(x => x.TenantId == tenantId && x.Id != id && x.Name.ToLower() == normalizedName.ToLower(), cancellationToken);

        if (duplicate)
        {
            return BadRequest(new { message = "A smart offer with this name already exists." });
        }

        entity.Name = normalizedName;
        entity.Status = request.Status;
        entity.Priority = request.Priority;
        entity.TriggerType = request.TriggerType;
        entity.PagesScope = request.PagesScope;
        entity.DelaySeconds = NormalizeNullable(request.DelaySeconds);
        entity.ScrollPercent = NormalizeNullable(request.ScrollPercent);
        entity.ShowOncePerSession = request.ShowOncePerSession;
        entity.MaxDisplaysPerUser = NormalizeNullable(request.MaxDisplaysPerUser);
        entity.PopupTitle = request.PopupTitle.Trim();
        entity.PopupDescription = NormalizeText(request.PopupDescription);
        entity.ButtonText = request.ButtonText.Trim();
        entity.CouponCode = NormalizeText(request.CouponCode)?.ToUpperInvariant();
        entity.CouponDiscountType = request.CouponDiscountType;
        entity.CouponDiscountValue = request.CouponDiscountValue;
        entity.CouponMinOrderAmount = request.CouponMinOrderAmount;
        entity.CouponMaxDiscountAmount = request.CouponMaxDiscountAmount;
        entity.ImageUrl = NormalizeText(request.ImageUrl);
        entity.BackgroundColor = NormalizeText(request.BackgroundColor);
        entity.UpdatedAt = DateTime.UtcNow;
        entity.Validate();

        var couponSyncError = await SyncGeneratedCouponAsync(entity, request, cancellationToken);
        if (couponSyncError != null)
        {
            return BadRequest(new { message = couponSyncError });
        }

        await PublishUpdatedAsync(entity);
        _dbContext.Entry(entity).Property(x => x.RowVersion).OriginalValue = expectedRowVersion.RowVersion.Value;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Smart offer has changed. Refresh before retrying." });
        }

        var currency = await ResolveTenantCurrencyAsync(entity.TenantId, cancellationToken);
        var coupon = await LoadGeneratedCouponAsync(entity.TenantId, entity.GeneratedCouponId, cancellationToken);
        return Ok(MapToAdminDto(entity, currency, coupon));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteSmartOffer(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var entity = await LoadSmartOfferForLifecycleMutationAsync(tenantId, id, cancellationToken);
        if (entity == null)
        {
            return NotFound(new { message = "Smart offer not found." });
        }

        var expectedRowVersion = ParseRequiredSmartOfferIfMatch();
        if (expectedRowVersion.Error != null)
        {
            return expectedRowVersion.Error;
        }

        var currentRowVersion = GetSmartOfferRowVersion(entity);
        if (currentRowVersion != expectedRowVersion.RowVersion!.Value)
        {
            return Conflict(new { message = "Smart offer has changed. Refresh before retrying." });
        }

        await RemoveGeneratedCouponAsync(entity, cancellationToken);
        _dbContext.SmartOffers.Remove(entity);
        await PublishDeletedAsync(entity);
        _dbContext.Entry(entity).Property(x => x.RowVersion).OriginalValue = expectedRowVersion.RowVersion.Value;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Smart offer has changed. Refresh before retrying." });
        }

        return NoContent();
    }

    [HttpPost("sync-generated-coupons")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<object>> SyncGeneratedCouponsToMongo(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        if (_readModelRepairService == null)
        {
            _logger.LogError(
                "Smart offer generated coupon sync unavailable because discount read-model repair is not registered | TenantId: {TenantId}",
                tenantId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Discount read-model repair is unavailable." });
        }

        var generatedCoupons = await _dbContext.Coupons
            .Where(c => c.TenantId == tenantId && !c.IsDeleted)
            .Join(
                _dbContext.SmartOffers.Where(o => o.TenantId == tenantId && o.GeneratedCouponId.HasValue),
                coupon => coupon.Id,
                offer => offer.GeneratedCouponId!.Value,
                (coupon, offer) => coupon)
            .Distinct()
            .ToListAsync(cancellationToken);

        var synced = await _readModelRepairService.RebuildCouponsAsync(
            tenantId,
            generatedCoupons.Select(coupon => coupon.Id).ToList(),
            cancellationToken);

        _logger.LogInformation(
            "Smart offer generated coupons repaired from SQL authority | TenantId: {TenantId} | Total: {Total} | Synced: {Synced}",
            tenantId,
            generatedCoupons.Count,
            synced);

        return Ok(new { total = generatedCoupons.Count, synced });
    }

    private async Task<List<AdminSmartOfferDto>> MapToAdminDtosAsync(
        Guid tenantId,
        IReadOnlyCollection<SmartOffer> offers,
        CancellationToken cancellationToken)
    {
        var currency = await ResolveTenantCurrencyAsync(tenantId, cancellationToken);
        var couponIds = offers
            .Where(x => x.GeneratedCouponId.HasValue)
            .Select(x => x.GeneratedCouponId!.Value)
            .Distinct()
            .ToList();

        var coupons = couponIds.Count == 0
            ? new Dictionary<Guid, Coupon>()
            : await _dbContext.Coupons
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && couponIds.Contains(x.Id) && !x.IsDeleted)
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        return offers
            .Select(offer =>
            {
                var coupon = offer.GeneratedCouponId.HasValue && coupons.TryGetValue(offer.GeneratedCouponId.Value, out var loadedCoupon)
                    ? loadedCoupon
                    : null;
                return MapToAdminDto(offer, currency, coupon);
            })
            .ToList();
    }

    private static AdminSmartOfferDto MapToAdminDto(SmartOffer entity, string currency, Coupon? generatedCoupon)
    {
        return new AdminSmartOfferDto
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            Name = entity.Name,
            Status = entity.Status,
            Priority = entity.Priority,
            TriggerType = entity.TriggerType,
            PagesScope = entity.PagesScope,
            DelaySeconds = entity.DelaySeconds,
            ScrollPercent = entity.ScrollPercent,
            ShowOncePerSession = entity.ShowOncePerSession,
            MaxDisplaysPerUser = entity.MaxDisplaysPerUser,
            PopupTitle = entity.PopupTitle,
            PopupDescription = entity.PopupDescription,
            ButtonText = entity.ButtonText,
            CouponCode = entity.CouponCode,
            GeneratedCouponId = entity.GeneratedCouponId,
            CouponDiscountType = entity.CouponDiscountType,
            CouponDiscountValue = entity.CouponDiscountValue,
            Currency = currency,
            CouponMinOrderAmount = entity.CouponMinOrderAmount,
            CouponMaxDiscountAmount = entity.CouponMaxDiscountAmount,
            UsageLimit = generatedCoupon?.MaxUsage,
            UsageCount = generatedCoupon?.CurrentUsage ?? 0,
            UsageLimitPerCustomer = generatedCoupon?.MaxUsagePerCustomer,
            EligibilitySummary = ResolveEligibilitySummary(entity, generatedCoupon),
            ValidFrom = generatedCoupon?.StartDate,
            ValidTo = generatedCoupon?.EndDate,
            ImageUrl = entity.ImageUrl,
            BackgroundColor = entity.BackgroundColor,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            RowVersion = GetSmartOfferRowVersion(entity).ToString(CultureInfo.InvariantCulture)
        };
    }

    private async Task<SmartOffer?> LoadSmartOfferForLifecycleMutationAsync(
        Guid tenantId,
        Guid smartOfferId,
        CancellationToken cancellationToken)
    {
        var providerName = _dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.SmartOffers
                .FromSqlInterpolated($@"
                    SELECT xmin, *
                    FROM ""SmartOffers""
                    WHERE ""Id"" = {smartOfferId}
                      AND ""TenantId"" = {tenantId}
                    FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.SmartOffers
            .FirstOrDefaultAsync(x => x.Id == smartOfferId && x.TenantId == tenantId, cancellationToken);
    }

    private (uint? RowVersion, ActionResult? Error) ParseRequiredSmartOfferIfMatch()
    {
        if (!Request.Headers.TryGetValue("If-Match", out var headerValues))
        {
            return (null, StatusCode(
                StatusCodes.Status428PreconditionRequired,
                new { message = "Smart Offer lifecycle mutations require current RowVersion evidence." }));
        }

        var header = headerValues.FirstOrDefault()?.Trim().Trim('"');
        if (!uint.TryParse(header, out var rowVersion) || rowVersion == 0)
        {
            return (null, BadRequest(new { message = "If-Match must contain the current smart offer RowVersion." }));
        }

        return (rowVersion, null);
    }

    private static uint GetSmartOfferRowVersion(SmartOffer smartOffer)
    {
        return smartOffer.RowVersion;
    }

    private static IQueryable<SmartOffer> ApplySmartOfferReadSort(
        IQueryable<SmartOffer> query,
        string? sortBy,
        string? sortDirection)
    {
        var descending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        return (sortBy ?? "priority").Trim().ToLowerInvariant() switch
        {
            "name" => descending
                ? query.OrderByDescending(x => x.Name).ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                : query.OrderBy(x => x.Name).ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt),
            "status" => descending
                ? query.OrderByDescending(x => x.Status).ThenByDescending(x => x.Priority)
                : query.OrderBy(x => x.Status).ThenByDescending(x => x.Priority),
            "createdat" => descending
                ? query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Priority)
                : query.OrderBy(x => x.CreatedAt).ThenByDescending(x => x.Priority),
            "updatedat" => descending
                ? query.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).ThenByDescending(x => x.Priority)
                : query.OrderBy(x => x.UpdatedAt ?? x.CreatedAt).ThenByDescending(x => x.Priority),
            _ => descending
                ? query.OrderByDescending(x => x.Priority).ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                : query.OrderBy(x => x.Priority).ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt)
        };
    }

    private async Task<string> ResolveTenantCurrencyAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var currency = await _dbContext.Tenants
            .AsNoTracking()
            .Where(x => x.Id == tenantId)
            .Select(x => x.Currency)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(currency) ? "EGP" : currency.Trim().ToUpperInvariant();
    }

    private async Task<Coupon?> LoadGeneratedCouponAsync(Guid tenantId, Guid? couponId, CancellationToken cancellationToken)
    {
        if (!couponId.HasValue)
        {
            return null;
        }

        return await _dbContext.Coupons
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == couponId.Value && !x.IsDeleted, cancellationToken);
    }

    private static string ResolveEligibilitySummary(SmartOffer entity, Coupon? generatedCoupon)
    {
        if (string.IsNullOrWhiteSpace(entity.CouponCode))
        {
            return "Presentation only; no generated coupon eligibility.";
        }

        if (generatedCoupon == null)
        {
            return "Generated coupon unavailable.";
        }

        return generatedCoupon.MinOrderAmount.HasValue
            ? $"Generated coupon; minimum order {generatedCoupon.MinOrderAmount.Value:0.##}."
            : "Generated coupon; all orders.";
    }

    private static string? ValidateRequest(UpsertSmartOfferRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Name is required.";
        }

        if (string.IsNullOrWhiteSpace(request.PopupTitle))
        {
            return "Popup title is required.";
        }

        if (string.IsNullOrWhiteSpace(request.ButtonText))
        {
            return "Button text is required.";
        }

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            if (!request.CouponDiscountValue.HasValue)
            {
                return "Coupon discount value must be greater than zero when a coupon code is provided.";
            }
        }
        else if (request.CouponDiscountType.HasValue ||
                 request.CouponDiscountValue.HasValue ||
                 request.CouponMinOrderAmount.HasValue ||
                 request.CouponMaxDiscountAmount.HasValue)
        {
            return "Coupon code is required when coupon settings are provided.";
        }

        if (request.CouponDiscountValue.HasValue)
        {
            var discountValidationError = DiscountValidation.GetValidationError(
                request.CouponDiscountType ?? DiscountType.Percentage,
                request.CouponDiscountValue.Value);
            if (discountValidationError != null)
            {
                return discountValidationError;
            }
        }

        if (request.CouponMinOrderAmount.HasValue && request.CouponMinOrderAmount.Value < 0)
        {
            return "Coupon minimum order amount cannot be negative.";
        }

        if (request.CouponMaxDiscountAmount.HasValue && request.CouponMaxDiscountAmount.Value < 0)
        {
            return "Coupon max discount amount cannot be negative.";
        }

        if (request.Priority < 0)
        {
            return "Priority cannot be negative.";
        }

        if (request.MaxDisplaysPerUser.HasValue && request.MaxDisplaysPerUser.Value <= 0)
        {
            return "Max displays per user must be greater than zero.";
        }

        if (request.TriggerType == SmartOfferTriggerType.TimeDelay &&
            (!request.DelaySeconds.HasValue || request.DelaySeconds.Value <= 0))
        {
            return "Delay seconds must be greater than zero for time delay offers.";
        }

        if (request.TriggerType == SmartOfferTriggerType.ScrollDepth &&
            (!request.ScrollPercent.HasValue || request.ScrollPercent.Value <= 0 || request.ScrollPercent.Value > 100))
        {
            return "Scroll percent must be between 1 and 100 for scroll depth offers.";
        }

        if (request.ScrollPercent.HasValue && (request.ScrollPercent.Value <= 0 || request.ScrollPercent.Value > 100))
        {
            return "Scroll percent must be between 1 and 100.";
        }

        if (request.DelaySeconds.HasValue && request.DelaySeconds.Value < 0)
        {
            return "Delay seconds cannot be negative.";
        }

        return null;
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? NormalizeNullable(int? value)
    {
        return value.HasValue && value.Value > 0 ? value : null;
    }

    private async Task<string?> SyncGeneratedCouponAsync(
        SmartOffer entity,
        UpsertSmartOfferRequest request,
        CancellationToken cancellationToken)
    {
        var couponCode = NormalizeText(request.CouponCode)?.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(couponCode))
        {
            await RemoveGeneratedCouponAsync(entity, cancellationToken);
            return null;
        }

        var conflictingCoupon = await _dbContext.Coupons
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x =>
                x.TenantId == entity.TenantId &&
                x.NormalizedCode == couponCode &&
                !x.IsDeleted &&
                x.Id != entity.GeneratedCouponId,
                cancellationToken);

        if (conflictingCoupon != null)
        {
            return $"Coupon code {couponCode} is already used by another coupon.";
        }

        var coupon = entity.GeneratedCouponId.HasValue
            ? await _dbContext.Coupons
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.TenantId == entity.TenantId && x.Id == entity.GeneratedCouponId.Value, cancellationToken)
            : null;

        var isNewCoupon = coupon == null;
        coupon ??= new Coupon
        {
            Id = Guid.NewGuid(),
            TenantId = entity.TenantId,
            CreatedAt = DateTime.UtcNow
        };

        coupon.IsDeleted = false;
        coupon.SetCode(couponCode);
        coupon.Description = $"Generated from Smart Offer: {entity.Name}";
        coupon.DiscountType = request.CouponDiscountType ?? DiscountType.Percentage;
        coupon.DiscountValue = request.CouponDiscountValue ?? 0;
        coupon.MinOrderAmount = request.CouponMinOrderAmount;
        coupon.MaxDiscountAmount = request.CouponMaxDiscountAmount;
        
        // Only set default limits when creating a new coupon or when existing values are unset/invalid
        if (isNewCoupon || coupon.MaxUsage == null || coupon.MaxUsage <= 0)
        {
            coupon.MaxUsage = DefaultSmartOfferCouponMaxUsage;
        }
        
        if (isNewCoupon || coupon.MaxUsagePerCustomer == null || coupon.MaxUsagePerCustomer <= 0)
        {
            coupon.MaxUsagePerCustomer = DefaultSmartOfferCouponMaxUsagePerCustomer;
        }
        
        coupon.StartDate = null;
        coupon.EndDate = null;
        coupon.IsActive = entity.Status == SmartOfferStatus.Active;
        coupon.FirstOrderOnly = false;
        coupon.ApplicableProductIds = null;
        coupon.ApplicableCategoryIds = null;
        coupon.ExcludedProductIds = null;
        coupon.Validate();

        if (isNewCoupon)
        {
            _dbContext.Coupons.Add(coupon);
            entity.GeneratedCouponId = coupon.Id;
            await PublishCouponCreatedEventAsync(coupon);
        }
        else
        {
            entity.GeneratedCouponId = coupon.Id;
            await PublishCouponUpdatedEventAsync(coupon);
        }

        return null;
    }

    private async Task RemoveGeneratedCouponAsync(SmartOffer entity, CancellationToken cancellationToken)
    {
        if (!entity.GeneratedCouponId.HasValue)
        {
            return;
        }

        var coupon = await _dbContext.Coupons
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == entity.TenantId && x.Id == entity.GeneratedCouponId.Value, cancellationToken);

        if (coupon != null && !coupon.IsDeleted)
        {
            coupon.IsDeleted = true;
            await PublishCouponDeletedEventAsync(coupon);
        }

        entity.GeneratedCouponId = null;
    }

    private async Task PublishCreatedAsync(SmartOffer entity)
    {
        await _eventPublisher.PublishAsync(new SmartOfferCreatedEvent
        {
            TenantId = entity.TenantId,
            SmartOfferId = entity.Id,
            Name = entity.Name,
            OccurredAtUtc = DateTime.UtcNow
        });
    }

    private async Task PublishUpdatedAsync(SmartOffer entity)
    {
        await _eventPublisher.PublishAsync(new SmartOfferUpdatedEvent
        {
            TenantId = entity.TenantId,
            SmartOfferId = entity.Id,
            Name = entity.Name,
            OccurredAtUtc = DateTime.UtcNow
        });
    }

    private async Task PublishDeletedAsync(SmartOffer entity)
    {
        await _eventPublisher.PublishAsync(new SmartOfferDeletedEvent
        {
            TenantId = entity.TenantId,
            SmartOfferId = entity.Id,
            Name = entity.Name,
            OccurredAtUtc = DateTime.UtcNow
        });
    }

    private async Task PublishCouponCreatedEventAsync(Coupon coupon)
    {
        await _eventPublisher.PublishAsync(new CouponCreatedEvent
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
            OccurredAtUtc = DateTime.UtcNow
        });
    }

    private async Task PublishCouponUpdatedEventAsync(Coupon coupon)
    {
        await _eventPublisher.PublishAsync(new CouponUpdatedEvent
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
            OccurredAtUtc = DateTime.UtcNow
        });
    }

    private async Task PublishCouponDeletedEventAsync(Coupon coupon)
    {
        await _eventPublisher.PublishAsync(new CouponDeletedEvent
        {
            TenantId = coupon.TenantId,
            CouponId = coupon.Id,
            Code = coupon.Code,
            OccurredAtUtc = DateTime.UtcNow
        });
    }
}
