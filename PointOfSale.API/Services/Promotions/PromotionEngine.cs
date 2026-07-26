using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Exceptions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Validation;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Promotions;

/// <summary>
/// Implementation of the unified Promotion Engine.
/// 
/// ARCHITECTURE (SHOPIFY-GRADE PROMOTIONS ENGINE):
/// - Reads Promotions (offers) from PostgreSQL for write-side calculations
/// - Consumes prevalidated coupon inputs from the SQL authority service
/// - ONE engine for ALL discount calculations
/// - Highest priority wins when multiple promotions apply
/// - NO STACKING for Phase 1 (future enhancement)
/// 
/// CRITICAL FLOW:
/// 1. Load all active offers for tenant
/// 2. Evaluate the already validated coupon if one was supplied
/// 3. Filter offers by cart items (product/category/cart scope)
/// 4. Calculate discount for each applicable promotion
/// 5. Return the highest value promotion (priority as tiebreaker)
/// </summary>
public class PromotionEngine : IPromotionEngine
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<PromotionEngine> _logger;

    public PromotionEngine(
        ApplicationDbContext dbContext,
        ILogger<PromotionEngine> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PromotionResult> CalculateAsync(
        CartSnapshotForPromotion cart,
        DateTime now,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (cart.Items.Count == 0 || cart.Subtotal <= 0)
        {
            _logger.LogDebug(
                "[PromotionEngine] Empty cart, no promotion | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                cart.TenantId, correlationId);
            return PromotionResult.None();
        }

        _logger.LogInformation(
            "[PromotionEngine] Calculate | TenantId: {TenantId} | Items: {ItemCount} | Subtotal: {Subtotal} | CouponCode: {CouponCode} | CorrelationId: {CorrelationId}",
            cart.TenantId, cart.Items.Count, cart.Subtotal, cart.AppliedCouponCode, correlationId);

        // 1. Load active offers for tenant
        var activeOffers = await LoadActiveOffersAsync(cart.TenantId, now, cancellationToken);
        
        _logger.LogDebug(
            "[PromotionEngine] Found {OfferCount} active offers | TenantId: {TenantId}",
            activeOffers.Count, cart.TenantId);

        // 2. Calculate best offer
        var bestOffer = await CalculateBestOfferAsync(activeOffers, cart, now, cancellationToken);

        // 3. Reuse the already-validated coupon discount (if any)
        PromotionResult? couponResult = null;
        if (!string.IsNullOrEmpty(cart.AppliedCouponCode) && cart.AppliedCouponId.HasValue)
        {
            couponResult = CalculatePrevalidatedCouponDiscount(cart);
        }

        // 4. Determine winner (highest discount, priority as tiebreaker)
        var result = DetermineWinner(bestOffer, couponResult);

        if (result.HasPromotion)
        {
            _logger.LogInformation(
                "[PromotionEngine] Applied {Type}: {Name} | Discount: {Discount} | CorrelationId: {CorrelationId}",
                result.PromotionType, result.PromotionName, result.DiscountAmount, correlationId);
        }
        else
        {
            _logger.LogDebug(
                "[PromotionEngine] No promotion applied | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                cart.TenantId, correlationId);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Dictionary<Guid, ActiveOfferInfo>> GetActiveOffersForProductsAsync(
        Guid tenantId,
        IEnumerable<Guid> productIds,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var productIdList = productIds.ToList();
        if (productIdList.Count == 0)
            return new Dictionary<Guid, ActiveOfferInfo>();

        // Get offers that apply to these products (scope = Product)
        var productOffers = await _dbContext.Promotions
            .Where(p => p.TenantId == tenantId
                && p.IsActive
                && p.StartDate <= now
                && p.EndDate >= now
                && p.Scope == PromotionScope.Product
                && (p.AppliesToId == null || productIdList.Contains(p.AppliesToId.Value)))
            .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.DiscountValue)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        // Filter out invalid promotions instead of throwing
        var validProductOffers = new List<Promotion>();
        foreach (var offer in productOffers)
        {
            var validationError = DiscountValidation.GetPromotionValidationError(
                offer.DiscountType,
                offer.DiscountValue,
                offer.MinOrderAmount,
                offer.MaxDiscountAmount,
                offer.MaxUsage,
                offer.StartDate,
                offer.EndDate);

            if (validationError != null)
            {
                _logger.LogWarning(
                    "[PromotionEngine] Skipping invalid product promotion | PromotionId: {PromotionId} | TenantId: {TenantId} | ValidationError: {ValidationError}",
                    offer.Id, tenantId, validationError);
                continue;
            }

            validProductOffers.Add(offer);
        }
        
        productOffers = validProductOffers;

        // Map to product → best offer
        var result = new Dictionary<Guid, ActiveOfferInfo>();
        
        foreach (var productId in productIdList)
        {
            // Find best offer for this product
            var offer = productOffers.FirstOrDefault(o => o.AppliesToId == productId)
                ?? productOffers.FirstOrDefault(o => o.AppliesToId == null);
            
            if (offer != null)
            {
                result[productId] = MapToActiveOfferInfo(offer);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Dictionary<Guid, ActiveOfferInfo>> GetActiveOffersForCategoriesAsync(
        Guid tenantId,
        IEnumerable<Guid> categoryIds,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var categoryIdList = categoryIds.ToList();
        if (categoryIdList.Count == 0)
            return new Dictionary<Guid, ActiveOfferInfo>();

        // Get offers that apply to these categories (scope = Category)
        var categoryOffers = await _dbContext.Promotions
            .Where(p => p.TenantId == tenantId
                && p.IsActive
                && p.StartDate <= now
                && p.EndDate >= now
                && p.Scope == PromotionScope.Category
                && (p.AppliesToId == null || categoryIdList.Contains(p.AppliesToId.Value)))
            .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.DiscountValue)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        // Filter out invalid promotions instead of throwing
        var validCategoryOffers = new List<Promotion>();
        foreach (var offer in categoryOffers)
        {
            var validationError = DiscountValidation.GetPromotionValidationError(
                offer.DiscountType,
                offer.DiscountValue,
                offer.MinOrderAmount,
                offer.MaxDiscountAmount,
                offer.MaxUsage,
                offer.StartDate,
                offer.EndDate);

            if (validationError != null)
            {
                _logger.LogWarning(
                    "[PromotionEngine] Skipping invalid category promotion | PromotionId: {PromotionId} | TenantId: {TenantId} | ValidationError: {ValidationError}",
                    offer.Id, tenantId, validationError);
                continue;
            }

            validCategoryOffers.Add(offer);
        }
        
        categoryOffers = validCategoryOffers;

        // Map to category → best offer
        var result = new Dictionary<Guid, ActiveOfferInfo>();
        
        foreach (var categoryId in categoryIdList)
        {
            var offer = categoryOffers.FirstOrDefault(o => o.AppliesToId == categoryId)
                ?? categoryOffers.FirstOrDefault(o => o.AppliesToId == null);
            
            if (offer != null)
            {
                result[categoryId] = MapToActiveOfferInfo(offer);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public Task<bool> CanCombineCouponWithOffersAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken = default)
    {
        // Phase 1: No stacking allowed
        // Future: Check coupon.IsExclusive flag
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public async Task<CartPromotionResult?> GetActiveCartPromotionAsync(
        Guid tenantId,
        decimal subtotal,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        if (subtotal <= 0)
        {
            return null;
        }

        // Load active cart-scoped promotions
        var cartPromotions = await _dbContext.Promotions
            .Where(p => p.TenantId == tenantId
                && p.IsActive
                && p.StartDate <= now
                && p.EndDate >= now
                && p.Scope == PromotionScope.Cart)
            .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.DiscountValue)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        if (cartPromotions.Count == 0)
        {
            return null;
        }

        // Precompute usage counts for all cart promotions to avoid N+1 queries
        var promotionIds = cartPromotions.Select(p => p.Id).ToList();
        var (confirmedUsageCounts, activeReservationCounts) = await PrecomputeOfferUsageCountsAsync(
            tenantId,
            promotionIds,
            cancellationToken);

        // Find best eligible promotion (first one that meets MinOrderAmount and has capacity)
        foreach (var promo in cartPromotions)
        {
            var validationError = DiscountValidation.GetPromotionValidationError(
                promo.DiscountType,
                promo.DiscountValue,
                promo.MinOrderAmount,
                promo.MaxDiscountAmount,
                promo.MaxUsage,
                promo.StartDate,
                promo.EndDate);

            if (validationError != null)
            {
                _logger.LogWarning(
                    "[PromotionEngine] Skipping invalid cart promotion | PromotionId: {PromotionId} | TenantId: {TenantId} | ValidationError: {ValidationError}",
                    promo.Id, tenantId, validationError);
                continue;
            }

            if (promo.MinOrderAmount.HasValue && subtotal < promo.MinOrderAmount.Value)
            {
                continue; // Skip - doesn't meet minimum order
            }

            // Check MaxUsage capacity using precomputed counts (avoids N+1 queries)
            if (!HasOfferCapacity(promo.Id, promo.MaxUsage, confirmedUsageCounts, activeReservationCounts))
            {
                _logger.LogDebug(
                    "[PromotionEngine] Skipping cart promotion - MaxUsage capacity exceeded | PromotionId: {PromotionId} | TenantId: {TenantId}",
                    promo.Id, tenantId);
                continue;
            }

            var discount = promo.CalculateDiscount(subtotal);

            if (discount <= 0)
            {
                continue;
            }

            _logger.LogInformation(
                "[PromotionEngine] Cart promotion found | TenantId: {TenantId} | PromotionId: {PromotionId} | Name: {Name} | Discount: {Discount} | Subtotal: {Subtotal}",
                tenantId, promo.Id, promo.Name, discount, subtotal);

            return new CartPromotionResult
            {
                PromotionId = promo.Id,
                Name = promo.Name,
                Description = promo.Description,
                DiscountType = promo.DiscountType == DiscountType.Percentage ? "percentage" : "fixed",
                DiscountValue = promo.DiscountValue,
                MaxDiscountAmount = promo.MaxDiscountAmount,
                MinOrderAmount = promo.MinOrderAmount,
                EndDate = promo.EndDate,
                Priority = promo.Priority,
                CalculatedDiscount = discount
            };
        }

        return null;
    }

    #region Private Methods

    private async Task<List<Promotion>> LoadActiveOffersAsync(
        Guid tenantId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var activeOffers = await _dbContext.Promotions
            .Where(p => p.TenantId == tenantId
                && p.IsActive
                && p.StartDate <= now
                && p.EndDate >= now)
            .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.DiscountValue)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        // Filter out invalid promotions instead of throwing
        var validOffers = new List<Promotion>();
        foreach (var offer in activeOffers)
        {
            var validationError = DiscountValidation.GetPromotionValidationError(
                offer.DiscountType,
                offer.DiscountValue,
                offer.MinOrderAmount,
                offer.MaxDiscountAmount,
                offer.MaxUsage,
                offer.StartDate,
                offer.EndDate);

            if (validationError != null)
            {
                _logger.LogWarning(
                    "[PromotionEngine] Skipping invalid promotion | PromotionId: {PromotionId} | TenantId: {TenantId} | ValidationError: {ValidationError}",
                    offer.Id, tenantId, validationError);
                continue;
            }

            validOffers.Add(offer);
        }

        return validOffers;
    }

    private async Task<PromotionResult?> CalculateBestOfferAsync(
        List<Promotion> offers,
        CartSnapshotForPromotion cart,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (offers.Count == 0)
        {
            return null;
        }

        // Precompute usage counts for all offers to avoid N+1 queries
        var promotionIds = offers.Select(o => o.Id).ToList();
        var (confirmedUsageCounts, activeReservationCounts) = await PrecomputeOfferUsageCountsAsync(
            cart.TenantId,
            promotionIds,
            cancellationToken);

        decimal bestDiscount = 0;
        Promotion? bestOffer = null;
        var bestAppliedItems = new List<PromotionAppliedToItem>();

        foreach (var offer in offers)
        {
            // Check MinOrderAmount
            if (offer.MinOrderAmount.HasValue && cart.Subtotal < offer.MinOrderAmount.Value)
            {
                continue;
            }

            if (!HasOfferCapacity(offer.Id, offer.MaxUsage, confirmedUsageCounts, activeReservationCounts))
            {
                continue;
            }

            // Calculate discount based on scope
            var (discount, appliedItems) = CalculateOfferDiscount(offer, cart);

            if (discount > bestDiscount)
            {
                bestDiscount = discount;
                bestOffer = offer;
                bestAppliedItems = appliedItems;
            }
            else if (discount == bestDiscount && offer.Priority > (bestOffer?.Priority ?? 0))
            {
                // Same discount but higher priority
                bestOffer = offer;
                bestAppliedItems = appliedItems;
            }
        }

        if (bestOffer == null || bestDiscount <= 0)
            return null;

        return PromotionResult.FromOffer(
            bestOffer.Id,
            bestOffer.Name,
            bestDiscount,
            FormatOfferDescription(bestOffer),
            bestAppliedItems);
    }

    private (decimal discount, List<PromotionAppliedToItem> appliedItems) CalculateOfferDiscount(
        Promotion offer,
        CartSnapshotForPromotion cart)
    {
        var orderedItems = cart.Items
            .OrderBy(item => item.ProductId)
            .ThenBy(item => item.VariantId ?? Guid.Empty)
            .ThenBy(item => item.CategoryId ?? Guid.Empty)
            .ThenBy(item => item.ProductName, StringComparer.Ordinal)
            .ToList();
        var appliedItems = new List<PromotionAppliedToItem>();
        decimal totalDiscount = 0;

        switch (offer.Scope)
        {
            case PromotionScope.Cart:
                // Apply to entire cart
                totalDiscount = CalculateDiscountAmount(offer, cart.Subtotal);
                var cartAllocations = AllocateDiscountAcrossItems(orderedItems, totalDiscount);
                for (var index = 0; index < orderedItems.Count; index++)
                {
                    var item = orderedItems[index];
                    var itemDiscount = cartAllocations[index];
                    
                    // Calculate per-unit discount for display (may have rounding drift)
                    // LineDiscountTotal preserves the exact allocated amount
                    decimal perUnitDiscount = item.Quantity > 0
                        ? RoundCurrency(itemDiscount / item.Quantity)
                        : 0m;
                    
                    appliedItems.Add(new PromotionAppliedToItem
                    {
                        ProductId = item.ProductId,
                        VariantId = item.VariantId,
                        ProductName = item.ProductName,
                        Quantity = item.Quantity,
                        OriginalPrice = item.UnitPrice,
                        DiscountAmount = perUnitDiscount,
                        LineDiscountTotal = itemDiscount, // Exact line discount
                        FinalPrice = RoundCurrency(item.UnitPrice - perUnitDiscount)
                    });
                }
                break;

            case PromotionScope.Product:
                // Apply to matching products
                foreach (var item in orderedItems)
                {
                    if (offer.AppliesToId == null || offer.AppliesToId == item.ProductId)
                    {
                        var itemDiscount = CalculateDiscountAmount(offer, item.LineTotal);
                        totalDiscount = RoundCurrency(totalDiscount + itemDiscount);
                        
                        // Calculate per-unit discount for display (may have rounding drift)
                        // LineDiscountTotal preserves the exact allocated amount
                        decimal perUnitDiscount = item.Quantity > 0
                            ? RoundCurrency(itemDiscount / item.Quantity)
                            : 0m;
                        
                        appliedItems.Add(new PromotionAppliedToItem
                        {
                            ProductId = item.ProductId,
                            VariantId = item.VariantId,
                            ProductName = item.ProductName,
                            Quantity = item.Quantity,
                            OriginalPrice = item.UnitPrice,
                            DiscountAmount = perUnitDiscount,
                            LineDiscountTotal = itemDiscount, // Exact line discount
                            FinalPrice = RoundCurrency(item.UnitPrice - perUnitDiscount)
                        });
                    }
                }
                break;

            case PromotionScope.Category:
                // Apply to matching categories
                foreach (var item in orderedItems)
                {
                    if (offer.AppliesToId == null || (item.CategoryId.HasValue && offer.AppliesToId == item.CategoryId))
                    {
                        var itemDiscount = CalculateDiscountAmount(offer, item.LineTotal);
                        totalDiscount = RoundCurrency(totalDiscount + itemDiscount);
                        
                        // Calculate per-unit discount for display (may have rounding drift)
                        // LineDiscountTotal preserves the exact allocated amount
                        decimal perUnitDiscount = item.Quantity > 0
                            ? RoundCurrency(itemDiscount / item.Quantity)
                            : 0m;
                        
                        appliedItems.Add(new PromotionAppliedToItem
                        {
                            ProductId = item.ProductId,
                            VariantId = item.VariantId,
                            ProductName = item.ProductName,
                            Quantity = item.Quantity,
                            OriginalPrice = item.UnitPrice,
                            DiscountAmount = perUnitDiscount,
                            LineDiscountTotal = itemDiscount, // Exact line discount
                            FinalPrice = RoundCurrency(item.UnitPrice - perUnitDiscount)
                        });
                    }
                }
                break;
        }

        return (RoundCurrency(totalDiscount), appliedItems);
    }

    private decimal CalculateDiscountAmount(Promotion offer, decimal amount)
    {
        return RoundCurrency(offer.CalculateDiscount(amount));
    }

    private PromotionResult? CalculatePrevalidatedCouponDiscount(CartSnapshotForPromotion cart)
    {
        if (!cart.AppliedCouponId.HasValue ||
            string.IsNullOrWhiteSpace(cart.AppliedCouponCode) ||
            !cart.PrevalidatedCouponDiscountAmount.HasValue ||
            cart.PrevalidatedCouponDiscountAmount.Value <= 0m)
        {
            return null;
        }

        var discountType = cart.PrevalidatedCouponDiscountType ?? "fixed";
        var discountValue = cart.PrevalidatedCouponDiscountValue ?? cart.PrevalidatedCouponDiscountAmount.Value;
        var description = discountType.Equals("percentage", StringComparison.OrdinalIgnoreCase)
            ? $"Coupon {cart.AppliedCouponCode}: {discountValue}% off"
            : $"Coupon {cart.AppliedCouponCode}: ${discountValue} off";

        return PromotionResult.FromCoupon(
            cart.AppliedCouponId.Value,
            cart.AppliedCouponCode,
            RoundCurrency(cart.PrevalidatedCouponDiscountAmount.Value),
            description);
    }

    /// <summary>
    /// Precomputes usage counts for multiple promotions in a single query per table.
    /// This avoids N+1 queries when checking capacity for multiple offers.
    /// </summary>
    private async Task<(Dictionary<Guid, int> confirmedUsage, Dictionary<Guid, int> activeReservations)> 
        PrecomputeOfferUsageCountsAsync(
            Guid tenantId,
            List<Guid> promotionIds,
            CancellationToken cancellationToken)
    {
        if (promotionIds.Count == 0)
        {
            return (new Dictionary<Guid, int>(), new Dictionary<Guid, int>());
        }

        // Query 1: Confirmed usage from PromotionUsages
        var confirmedUsage = await _dbContext.PromotionUsages
            .Where(usage => usage.TenantId == tenantId &&
                           promotionIds.Contains(usage.PromotionId) &&
                           !usage.IsDeleted)
            .GroupBy(usage => usage.PromotionId)
            .Select(g => new { PromotionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PromotionId, x => x.Count, cancellationToken);

        // Query 2: Active reservations from Orders (pending checkouts)
        var activeReservations = await _dbContext.Orders
            .Where(order => order.TenantId == tenantId &&
                           order.AppliedPromotionId.HasValue &&
                           promotionIds.Contains(order.AppliedPromotionId.Value) &&
                           order.AppliedPromotionType == "offer" &&
                           !order.IsDeleted &&
                           order.CheckoutState != CheckoutState.Cancelled &&
                           order.CheckoutState != CheckoutState.Failed &&
                           order.CheckoutState != CheckoutState.Expired &&
                           order.PaymentStatus != PaymentStatus.Collected &&
                           order.PaymentStatus != PaymentStatus.Settled &&
                           order.PaymentStatus != PaymentStatus.Refunded &&
                           order.PaymentStatus != PaymentStatus.PartiallyRefunded &&
                           order.PaymentStatus != PaymentStatus.Failed &&
                           order.PaymentStatus != PaymentStatus.Rejected &&
                           order.PaymentStatus != PaymentStatus.Expired)
            .GroupBy(order => order.AppliedPromotionId!.Value)
            .Select(g => new { PromotionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PromotionId, x => x.Count, cancellationToken);

        return (confirmedUsage, activeReservations);
    }

    /// <summary>
    /// Checks if an offer has capacity based on precomputed usage counts.
    /// </summary>
    private static bool HasOfferCapacity(
        Guid promotionId,
        int? maxUsage,
        Dictionary<Guid, int> confirmedUsageCounts,
        Dictionary<Guid, int> activeReservationCounts)
    {
        if (!maxUsage.HasValue)
        {
            return true;
        }

        var confirmedUsage = confirmedUsageCounts.GetValueOrDefault(promotionId, 0);
        var activeReservations = activeReservationCounts.GetValueOrDefault(promotionId, 0);

        return confirmedUsage + activeReservations < maxUsage.Value;
    }

    /// <summary>
    /// Legacy async method for checking offer capacity (used by GetActiveCartPromotionAsync).
    /// Executes two separate queries per promotion - consider using PrecomputeOfferUsageCountsAsync
    /// for batch operations to avoid N+1 queries.
    /// </summary>

    private async Task<bool> HasOfferCapacityAsync(
        Guid tenantId,
        Guid promotionId,
        int? maxUsage,
        CancellationToken cancellationToken)
    {
        if (!maxUsage.HasValue)
        {
            return true;
        }

        var confirmedUsage = await _dbContext.PromotionUsages
            .CountAsync(
                usage => usage.TenantId == tenantId &&
                         usage.PromotionId == promotionId &&
                         !usage.IsDeleted,
                cancellationToken);

        var activeReservations = await _dbContext.Orders
            .CountAsync(
                order => order.TenantId == tenantId &&
                         order.AppliedPromotionId == promotionId &&
                         order.AppliedPromotionType == "offer" &&
                         !order.IsDeleted &&
                         order.CheckoutState != CheckoutState.Cancelled &&
                         order.CheckoutState != CheckoutState.Failed &&
                         order.CheckoutState != CheckoutState.Expired &&
                         order.PaymentStatus != PaymentStatus.Collected &&
                         order.PaymentStatus != PaymentStatus.Settled &&
                         order.PaymentStatus != PaymentStatus.Refunded &&
                         order.PaymentStatus != PaymentStatus.PartiallyRefunded &&
                         order.PaymentStatus != PaymentStatus.Failed &&
                         order.PaymentStatus != PaymentStatus.Rejected &&
                         order.PaymentStatus != PaymentStatus.Expired,
                cancellationToken);

        return confirmedUsage + activeReservations < maxUsage.Value;
    }

    private PromotionResult DetermineWinner(PromotionResult? offer, PromotionResult? coupon)
    {
        // Phase 1: No stacking - pick the best one
        if (offer == null && coupon == null)
            return PromotionResult.None();

        if (offer == null)
            return coupon!;

        if (coupon == null)
            return offer;

        // Both exist - pick highest discount
        // In case of tie, prefer coupon (user explicitly applied it)
        if (coupon.DiscountAmount >= offer.DiscountAmount)
        {
            _logger.LogDebug(
                "[PromotionEngine] Coupon wins | CouponDiscount: {CouponDiscount} | OfferDiscount: {OfferDiscount}",
                coupon.DiscountAmount, offer.DiscountAmount);
            return coupon;
        }
        else
        {
            _logger.LogDebug(
                "[PromotionEngine] Offer wins | OfferDiscount: {OfferDiscount} | CouponDiscount: {CouponDiscount}",
                offer.DiscountAmount, coupon.DiscountAmount);
            return offer;
        }
    }

    private static string FormatOfferDescription(Promotion offer)
    {
        var discountText = offer.DiscountType == DiscountType.Percentage
            ? $"{offer.DiscountValue}% off"
            : $"${offer.DiscountValue} off";

        return $"{offer.Name}: {discountText}";
    }

    private static ActiveOfferInfo MapToActiveOfferInfo(Promotion offer)
    {
        return new ActiveOfferInfo
        {
            PromotionId = offer.Id,
            Name = offer.Name,
            DiscountType = offer.DiscountType == DiscountType.Percentage ? "percentage" : "fixed",
            DiscountValue = offer.DiscountValue,
            MaxDiscountAmount = offer.MaxDiscountAmount,
            EndDate = offer.EndDate,
            Priority = offer.Priority
        };
    }

    private static IReadOnlyList<decimal> AllocateDiscountAcrossItems(
        IReadOnlyList<CartItemForPromotion> items,
        decimal totalDiscount)
    {
        if (items.Count == 0 || totalDiscount <= 0m)
        {
            return Enumerable.Repeat(0m, items.Count).ToArray();
        }

        var lineSubtotals = items
            .Select(item => RoundCurrency(item.LineTotal))
            .ToArray();
        var totalLineSubtotal = RoundCurrency(lineSubtotals.Sum());
        if (totalLineSubtotal <= 0m)
        {
            return Enumerable.Repeat(0m, items.Count).ToArray();
        }

        var allocations = new decimal[items.Count];
        var allocatedAmount = 0m;
        var remainders = new List<(int Index, decimal FractionalRemainder)>(items.Count);

        for (var index = 0; index < items.Count; index++)
        {
            var lineSubtotal = lineSubtotals[index];
            if (lineSubtotal <= 0m)
            {
                remainders.Add((index, 0m));
                continue;
            }

            var exactShare = totalDiscount * (lineSubtotal / totalLineSubtotal);
            var roundedDownShare = Math.Floor(exactShare * 100m) / 100m;
            allocations[index] = RoundCurrency(roundedDownShare);
            allocatedAmount = RoundCurrency(allocatedAmount + allocations[index]);
            remainders.Add((index, exactShare - roundedDownShare));
        }

        var remainingAmount = RoundCurrency(totalDiscount - allocatedAmount);
        foreach (var remainder in remainders
                     .OrderByDescending(entry => entry.FractionalRemainder)
                     .ThenBy(entry => entry.Index))
        {
            if (remainingAmount <= 0m)
            {
                break;
            }

            allocations[remainder.Index] = RoundCurrency(allocations[remainder.Index] + 0.01m);
            remainingAmount = RoundCurrency(remainingAmount - 0.01m);
        }

        return allocations;
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    #endregion
}
