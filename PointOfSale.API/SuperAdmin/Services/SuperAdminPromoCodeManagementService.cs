using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminPromoCodeManagementService
{
    Task<IReadOnlyList<SuperAdminPromoCodeDto>> GetPromoCodesAsync(CancellationToken cancellationToken = default);

    Task<SuperAdminPromoCodeDto> CreatePromoCodeAsync(
        SuperAdminUpsertPromoCodeRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminPromoCodeDto> UpdatePromoCodeAsync(
        Guid id,
        SuperAdminUpsertPromoCodeRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminPromoCodeDto> ActivatePromoCodeAsync(
        Guid id,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminPromoCodeDto> DeactivatePromoCodeAsync(
        Guid id,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminPromoCodePreviewResponse> PreviewAsync(
        SuperAdminPromoCodePreviewRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SuperAdminPromoCodeValidationException : Exception
{
    public SuperAdminPromoCodeValidationException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}

public sealed class SuperAdminPromoCodeManagementService : ISuperAdminPromoCodeManagementService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPromoService _promoService;

    public SuperAdminPromoCodeManagementService(
        ApplicationDbContext dbContext,
        IPromoService promoService)
    {
        _dbContext = dbContext;
        _promoService = promoService;
    }

    public async Task<IReadOnlyList<SuperAdminPromoCodeDto>> GetPromoCodesAsync(CancellationToken cancellationToken = default)
    {
        var promoCodes = await _dbContext.PromoCodes
            .AsNoTracking()
            .Where(promo => !promo.IsDeleted)
            .OrderByDescending(promo => promo.IsActive)
            .ThenByDescending(promo => promo.CreatedAt)
            .ToListAsync(cancellationToken);

        var usageCounts = await _dbContext.PromoCodeUsages
            .AsNoTracking()
            .GroupBy(usage => usage.PromoCodeId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken);

        return promoCodes
            .Select(promo => Map(promo, usageCounts.GetValueOrDefault(promo.Id, promo.UsedCount)))
            .ToList();
    }

    public async Task<SuperAdminPromoCodeDto> CreatePromoCodeAsync(
        SuperAdminUpsertPromoCodeRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = RequireCode(request.Code);
        var discountType = RequireDiscountType(request.DiscountType);
        var discountValue = RequireDiscountValue(discountType, request.DiscountValue);
        var maxDiscountAmount = NormalizeMaxDiscountAmount(request.MaxDiscountAmount);
        var usageLimit = NormalizeUsageLimit(request.UsageLimit);
        var maxUsagePerCustomer = NormalizeMaxUsagePerCustomer(request.MaxUsagePerCustomer);
        var vendorId = NormalizeScopeTarget(request.VendorId);
        var categoryId = NormalizeScopeTarget(request.CategoryId);
        NormalizeScope(vendorId, categoryId);

        if (maxUsagePerCustomer.HasValue &&
            usageLimit.HasValue &&
            maxUsagePerCustomer.Value > usageLimit.Value)
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status400BadRequest,
                "PROMO_CUSTOMER_USAGE_LIMIT_INVALID",
                "MaxUsagePerCustomer cannot exceed UsageLimit.");
        }

        await ValidateScopeTargetsAsync(vendorId, categoryId, cancellationToken);
        await EnsureCodeAvailableAsync(normalizedCode, promoIdToIgnore: null, cancellationToken);

        var promoCode = new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = normalizedCode,
            DiscountType = discountType,
            DiscountValue = discountValue,
            MaxDiscountAmount = maxDiscountAmount,
            UsageLimit = usageLimit,
            MaxUsagePerCustomer = maxUsagePerCustomer,
            UsedCount = 0,
            ExpiryDate = NormalizeExpiryDate(request.ExpiryDate),
            IsActive = request.IsActive ?? true,
            VendorId = vendorId,
            CategoryId = categoryId,
            CreatedBy = actorId
        };

        _dbContext.PromoCodes.Add(promoCode);
        await SaveChangesAsync(cancellationToken);

        return Map(promoCode, promoCode.UsedCount);
    }

    public async Task<SuperAdminPromoCodeDto> UpdatePromoCodeAsync(
        Guid id,
        SuperAdminUpsertPromoCodeRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        var promoCode = await LoadPromoCodeAsync(id, cancellationToken);
        var normalizedCode = RequireCode(request.Code);
        var discountType = RequireDiscountType(request.DiscountType);
        var discountValue = RequireDiscountValue(discountType, request.DiscountValue);
        var maxDiscountAmount = NormalizeMaxDiscountAmount(request.MaxDiscountAmount);
        var usageLimit = NormalizeUsageLimit(request.UsageLimit);
        var maxUsagePerCustomer = NormalizeMaxUsagePerCustomer(request.MaxUsagePerCustomer);
        var vendorId = NormalizeScopeTarget(request.VendorId);
        var categoryId = NormalizeScopeTarget(request.CategoryId);
        NormalizeScope(vendorId, categoryId);

        var actualUsedCount = await _dbContext.PromoCodeUsages
            .CountAsync(usage => usage.PromoCodeId == promoCode.Id, cancellationToken);
        promoCode.UsedCount = actualUsedCount;

        if (usageLimit.HasValue && usageLimit.Value < actualUsedCount)
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status400BadRequest,
                "PROMO_USAGE_LIMIT_INVALID",
                "UsageLimit cannot be lower than the current UsedCount.");
        }

        if (maxUsagePerCustomer.HasValue &&
            usageLimit.HasValue &&
            maxUsagePerCustomer.Value > usageLimit.Value)
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status400BadRequest,
                "PROMO_CUSTOMER_USAGE_LIMIT_INVALID",
                "MaxUsagePerCustomer cannot exceed UsageLimit.");
        }

        await ValidateScopeTargetsAsync(vendorId, categoryId, cancellationToken);
        await EnsureCodeAvailableAsync(normalizedCode, id, cancellationToken);

        promoCode.Code = normalizedCode;
        promoCode.DiscountType = discountType;
        promoCode.DiscountValue = discountValue;
        promoCode.MaxDiscountAmount = maxDiscountAmount;
        promoCode.UsageLimit = usageLimit;
        promoCode.MaxUsagePerCustomer = maxUsagePerCustomer;
        promoCode.ExpiryDate = NormalizeExpiryDate(request.ExpiryDate);
        promoCode.IsActive = request.IsActive ?? promoCode.IsActive;
        promoCode.VendorId = vendorId;
        promoCode.CategoryId = categoryId;
        promoCode.UpdatedBy = actorId;

        await SaveChangesAsync(cancellationToken);
        return Map(promoCode, promoCode.UsedCount);
    }

    public async Task<SuperAdminPromoCodeDto> ActivatePromoCodeAsync(
        Guid id,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        var promoCode = await LoadPromoCodeAsync(id, cancellationToken);
        await ValidateScopeTargetsAsync(promoCode.VendorId, promoCode.CategoryId, cancellationToken);
        promoCode.IsActive = true;
        promoCode.UpdatedBy = actorId;
        await SaveChangesAsync(cancellationToken);
        return Map(promoCode, promoCode.UsedCount);
    }

    public async Task<SuperAdminPromoCodeDto> DeactivatePromoCodeAsync(
        Guid id,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        var promoCode = await LoadPromoCodeAsync(id, cancellationToken);
        promoCode.IsActive = false;
        promoCode.UpdatedBy = actorId;
        await SaveChangesAsync(cancellationToken);
        return Map(promoCode, promoCode.UsedCount);
    }

    public async Task<SuperAdminPromoCodePreviewResponse> PreviewAsync(
        SuperAdminPromoCodePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status400BadRequest,
                "PROMO_PREVIEW_INVALID",
                "Preview request is required.");
        }

        var baseCommissionAmount = CommissionCalculator.RoundAmount(request.BaseCommissionAmount);
        if (baseCommissionAmount < 0m)
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status400BadRequest,
                "PROMO_PREVIEW_INVALID",
                "BaseCommissionAmount must be zero or greater.");
        }

        var promoCode = request.PromoId.HasValue
            ? await LoadPromoCodeAsync(request.PromoId.Value, cancellationToken)
            : BuildPreviewPromoCode(request);

        var previewApplication = PromoCalculationEngine.Apply(
            promoCode,
            request.PreviewVendorId ?? Guid.Empty,
            new[]
            {
                new PromoCommissionLine(
                    Guid.NewGuid(),
                    request.PreviewVendorId ?? Guid.Empty,
                    request.PreviewCategoryId ?? Guid.Empty,
                    baseCommissionAmount)
            },
            _promoService.CalculateDiscount);

        return new SuperAdminPromoCodePreviewResponse
        {
            BaseCommissionAmount = baseCommissionAmount,
            DiscountAmount = previewApplication.DiscountAmount,
            FinalCommissionAmount = CommissionCalculator.RoundAmount(
                Math.Max(0m, baseCommissionAmount - previewApplication.DiscountAmount)),
            IsApplicable = previewApplication.DiscountAmount > 0m,
            PreviewVendorId = request.PreviewVendorId,
            PreviewCategoryId = request.PreviewCategoryId
        };
    }

    private PromoCode BuildPreviewPromoCode(SuperAdminPromoCodePreviewRequest request)
    {
        var discountType = RequireDiscountType(request.DiscountType);
        var discountValue = RequireDiscountValue(discountType, request.DiscountValue);
        var maxDiscountAmount = NormalizeMaxDiscountAmount(request.MaxDiscountAmount);
        var maxUsagePerCustomer = NormalizeMaxUsagePerCustomer(request.MaxUsagePerCustomer);
        var vendorId = NormalizeScopeTarget(request.ScopeVendorId);
        var categoryId = NormalizeScopeTarget(request.ScopeCategoryId);
        NormalizeScope(vendorId, categoryId);

        return new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = "PREVIEW",
            DiscountType = discountType,
            DiscountValue = discountValue,
            MaxDiscountAmount = maxDiscountAmount,
            MaxUsagePerCustomer = maxUsagePerCustomer,
            VendorId = vendorId,
            CategoryId = categoryId,
            IsActive = true
        };
    }

    private async Task<PromoCode> LoadPromoCodeAsync(Guid id, CancellationToken cancellationToken)
    {
        var promoCode = await _dbContext.PromoCodes
            .FirstOrDefaultAsync(item => item.Id == id && !item.IsDeleted, cancellationToken);

        if (promoCode == null)
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status404NotFound,
                "PROMO_NOT_FOUND",
                "Promo code was not found.");
        }

        return promoCode;
    }

    private async Task ValidateScopeTargetsAsync(
        Guid? vendorId,
        Guid? categoryId,
        CancellationToken cancellationToken)
    {
        if (vendorId.HasValue)
        {
            var vendorAuthority = await CommerceHubVendorAuthority.LoadAsync(
                _dbContext,
                vendorId.Value,
                cancellationToken);

            if (!vendorAuthority.IsMarketplaceEligible)
            {
                throw new SuperAdminPromoCodeValidationException(
                    StatusCodes.Status400BadRequest,
                    "PROMO_SCOPE_VENDOR_INELIGIBLE",
                    "The selected vendor is not currently eligible for CommerceHub promotions.");
            }
        }

        if (categoryId.HasValue)
        {
            if (!await CommerceHubCategoryLineageAuthority.IsLiveAsync(
                    _dbContext,
                    categoryId.Value,
                    cancellationToken))
            {
                throw new SuperAdminPromoCodeValidationException(
                    StatusCodes.Status400BadRequest,
                    "PROMO_SCOPE_CATEGORY_INACTIVE",
                    "The selected CommerceHub category is not live.");
            }
        }
    }

    private async Task EnsureCodeAvailableAsync(
        string normalizedCode,
        Guid? promoIdToIgnore,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.PromoCodes
            .AsNoTracking()
            .AnyAsync(
                promo => !promo.IsDeleted &&
                         promo.Code == normalizedCode &&
                         promo.Id != promoIdToIgnore,
                cancellationToken);

        if (exists)
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status409Conflict,
                "PROMO_CODE_CONFLICT",
                $"Promo code '{normalizedCode}' already exists.");
        }
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryTranslateConstraintViolation(exception, out var validationException))
        {
            throw validationException;
        }
    }

    private static bool TryTranslateConstraintViolation(
        DbUpdateException exception,
        out SuperAdminPromoCodeValidationException validationException)
    {
        if (exception.InnerException is not PostgresException postgresException)
        {
            validationException = null!;
            return false;
        }

        validationException = postgresException.SqlState switch
        {
            "23505" => new SuperAdminPromoCodeValidationException(
                StatusCodes.Status409Conflict,
                "PROMO_CODE_CONFLICT",
                "Promo code write conflicted with an existing promo code."),
            "23514" => new SuperAdminPromoCodeValidationException(
                StatusCodes.Status400BadRequest,
                "PROMO_CODE_INVALID",
                "Promo code failed a database validation constraint."),
            _ => null!
        };

        return validationException != null;
    }

    private string RequireCode(string? code)
    {
        var normalizedCode = _promoService.NormalizeCode(code);
        if (normalizedCode == null)
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status400BadRequest,
                "PROMO_CODE_INVALID",
                "Promo code is required.");
        }

        return normalizedCode;
    }

    private static PromoDiscountType RequireDiscountType(PromoDiscountType? discountType)
    {
        if (!discountType.HasValue || !Enum.IsDefined(discountType.Value))
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status400BadRequest,
                "PROMO_DISCOUNT_TYPE_INVALID",
                "A valid discount type is required.");
        }

        return discountType.Value;
    }

    private static decimal RequireDiscountValue(
        PromoDiscountType discountType,
        decimal? discountValue)
    {
        if (!discountValue.HasValue || discountValue.Value <= 0m)
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status400BadRequest,
                "PROMO_DISCOUNT_VALUE_INVALID",
                "Discount value must be greater than zero.");
        }

        if (discountType == PromoDiscountType.Percentage && discountValue.Value > 100m)
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status400BadRequest,
                "PROMO_DISCOUNT_VALUE_INVALID",
                "Percentage discount value must not exceed 100.");
        }

        return CommissionCalculator.RoundAmount(discountValue.Value);
    }

    private static decimal? NormalizeMaxDiscountAmount(decimal? maxDiscountAmount)
    {
        if (!maxDiscountAmount.HasValue)
        {
            return null;
        }

        if (maxDiscountAmount.Value <= 0m)
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status400BadRequest,
                "PROMO_MAX_DISCOUNT_INVALID",
                "MaxDiscountAmount must be greater than zero when provided.");
        }

        return CommissionCalculator.RoundAmount(maxDiscountAmount.Value);
    }

    private static int? NormalizeUsageLimit(int? usageLimit)
    {
        if (!usageLimit.HasValue)
        {
            return null;
        }

        if (usageLimit.Value <= 0)
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status400BadRequest,
                "PROMO_USAGE_LIMIT_INVALID",
                "UsageLimit must be greater than zero when provided.");
        }

        return usageLimit.Value;
    }

    private static int? NormalizeMaxUsagePerCustomer(int? maxUsagePerCustomer)
    {
        if (!maxUsagePerCustomer.HasValue)
        {
            return null;
        }

        if (maxUsagePerCustomer.Value <= 0)
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status400BadRequest,
                "PROMO_CUSTOMER_USAGE_LIMIT_INVALID",
                "MaxUsagePerCustomer must be greater than zero when provided.");
        }

        return maxUsagePerCustomer.Value;
    }

    private static DateTime? NormalizeExpiryDate(DateTime? expiryDate)
    {
        if (!expiryDate.HasValue)
        {
            return null;
        }

        return expiryDate.Value.Kind switch
        {
            DateTimeKind.Utc => expiryDate.Value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(expiryDate.Value, DateTimeKind.Utc),
            _ => expiryDate.Value.ToUniversalTime()
        };
    }

    private static void NormalizeScope(Guid? vendorId, Guid? categoryId)
    {
        var scopeCount = 0;
        if (vendorId.HasValue && vendorId.Value != Guid.Empty)
        {
            scopeCount += 1;
        }

        if (categoryId.HasValue && categoryId.Value != Guid.Empty)
        {
            scopeCount += 1;
        }

        if (scopeCount > 1)
        {
            throw new SuperAdminPromoCodeValidationException(
                StatusCodes.Status400BadRequest,
                "PROMO_SCOPE_INVALID",
                "Promo codes can be global, vendor-scoped, or category-scoped, but not multiple scopes at once.");
        }
    }

    private static Guid? NormalizeScopeTarget(Guid? scopeTargetId) =>
        scopeTargetId.HasValue && scopeTargetId.Value != Guid.Empty
            ? scopeTargetId.Value
            : null;

    private static SuperAdminPromoCodeDto Map(PromoCode promoCode, int usedCount)
    {
        return new SuperAdminPromoCodeDto
        {
            Id = promoCode.Id,
            Code = promoCode.Code,
            DiscountType = promoCode.DiscountType,
            DiscountValue = promoCode.DiscountValue,
            MaxDiscountAmount = promoCode.MaxDiscountAmount,
            UsageLimit = promoCode.UsageLimit,
            MaxUsagePerCustomer = promoCode.MaxUsagePerCustomer,
            UsedCount = usedCount,
            ExpiryDate = promoCode.ExpiryDate,
            IsActive = promoCode.IsActive,
            VendorId = promoCode.VendorId,
            CategoryId = promoCode.CategoryId,
            CreatedAt = promoCode.CreatedAt,
            UpdatedAt = promoCode.UpdatedAt
        };
    }
}
