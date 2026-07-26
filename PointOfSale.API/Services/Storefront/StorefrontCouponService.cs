using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Discounts;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Thin storefront adapter over the SQL-authoritative coupon validation service.
/// All coupon decisions now route through ICouponValidationService.
/// </summary>
public class StorefrontCouponService : IStorefrontCouponService
{
    private readonly ICouponValidationService _couponValidationService;
    private readonly ApplicationDbContext _dbContext;

    public StorefrontCouponService(
        ICouponValidationService couponValidationService,
        ApplicationDbContext dbContext)
    {
        _couponValidationService = couponValidationService;
        _dbContext = dbContext;
    }

    public async Task<CouponValidationResult> ValidateCouponAsync(
        Guid tenantId,
        string code,
        CartDto cart,
        Guid customerId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var productIds = cart.Items
            .Select(item => item.ProductId)
            .Where(productId => productId != Guid.Empty)
            .Distinct()
            .ToList();

        var categoryLookup = productIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await _dbContext.Products
                .AsNoTracking()
                .Where(product => productIds.Contains(product.Id) && !product.IsDeleted)
                .Select(product => new { product.Id, product.CategoryId })
                .ToDictionaryAsync(product => product.Id, product => product.CategoryId, cancellationToken);

        var decision = await _couponValidationService.ValidateCouponByCodeAsync(
            tenantId,
            code,
            customerId,
            new CouponValidationSnapshot
            {
                OrderSubtotal = cart.Subtotal,
                AppliedDiscountAmount = cart.AppliedCoupon?.Amount ?? 0m,
                CartItems = cart.Items
                    .Select(item => new CouponScopeValidationItem(
                        item.ProductId,
                        categoryLookup.GetValueOrDefault(item.ProductId)))
                    .ToList()
            },
            correlationId,
            cancellationToken: cancellationToken);

        return MapDecision(decision);
    }

    public Task<CouponValidationResult> RevalidateCouponAsync(
        Guid tenantId,
        string code,
        CartDto cart,
        Guid customerId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        return ValidateCouponAsync(tenantId, code, cart, customerId, correlationId, cancellationToken);
    }

    public async Task<decimal> CalculateDiscountAsync(
        Guid couponId,
        decimal cartSubtotal,
        CancellationToken cancellationToken = default)
    {
        var coupon = await _dbContext.Coupons
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entity => entity.Id == couponId && !entity.IsDeleted, cancellationToken);

        return coupon?.CalculateDiscount(cartSubtotal) ?? 0m;
    }

    public Task<bool> CustomerHasOrdersAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return _couponValidationService.CustomerHasCompletedOrdersAsync(tenantId, customerId, cancellationToken);
    }

    public Task<int> GetCustomerCouponUsageAsync(
        Guid tenantId,
        Guid customerId,
        Guid couponId,
        CancellationToken cancellationToken = default)
    {
        return _couponValidationService.GetCustomerConfirmedUsageCountAsync(
            tenantId,
            couponId,
            customerId,
            cancellationToken);
    }

    private static CouponValidationResult MapDecision(CouponValidationDecision decision)
    {
        return decision.IsValid && decision.CouponId.HasValue && decision.CalculatedDiscountAmount.HasValue
            ? CouponValidationResult.Success(
                decision.CouponId.Value,
                decision.CouponCode ?? string.Empty,
                decision.DiscountType ?? "fixed",
                decision.DiscountValue ?? 0m,
                decision.CalculatedDiscountAmount.Value)
            : CouponValidationResult.Failure(
                decision.ErrorCode ?? PointOfSale.API.DTOs.Storefront.CouponErrorCodes.Invalid,
                decision.ErrorMessage ?? "Coupon is invalid.");
    }
}
