using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Pricing;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Reliability;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub;

public sealed class CommerceHubCartOwnerContext
{
    public static CommerceHubCartOwnerContext Guest { get; } = new();
    public static readonly Guid CommerceHubCartTenantScopeId = Guid.Parse("8a74c4f4-5c4a-492c-8f56-d1bbddcf4c11");

    public bool IsAuthenticated { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? CommerceHubCustomerId { get; init; }
    public Guid? OnlineCustomerId { get; init; }
}

public class CommerceHubCartException : Exception
{
    public CommerceHubCartException(
        string code,
        string message,
        int statusCode,
        object? data = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        DataPayload = data;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public object? DataPayload { get; }
}

public interface ICommerceHubCartService
{
    Task<CommerceHubCartStateDto> GetCartAsync(
        CommerceHubCartOwnerContext ownerContext,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubCartStateDto> SaveCartAsync(
        CommerceHubCartUpsertRequestDto request,
        CommerceHubCartOwnerContext ownerContext,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubCartStateDto> ClearCartAsync(
        CommerceHubCartOwnerContext ownerContext,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubCartPricingResponseDto> CalculateAsync(
        CommerceHubCartPricingRequestDto request,
        CommerceHubCartOwnerContext ownerContext,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public class CommerceHubCartService : ICommerceHubCartService
{
    private const int CurrencyScale = 2;
    private const int DefaultCartVersion = 0;
    private const int MaxCartLineQuantity = CommerceHubCartLimits.MaxLineQuantity;
    private const int MaxCartDistinctItems = CommerceHubCartLimits.MaxDistinctItems;
    private static readonly TimeSpan CartLockTimeout = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IProductVariantStockRepository _variantStockRepository;
    private readonly ICommerceHubShippingService _shippingService;
    private readonly ICommissionService _commissionService;
    private readonly IPromoService _promoService;
    private readonly ICommerceHubPriceAuthority _priceAuthority;
    private readonly ILogger<CommerceHubCartService> _logger;

    public CommerceHubCartService(
        ApplicationDbContext dbContext,
        IProductVariantStockRepository variantStockRepository,
        ICommerceHubShippingService shippingService,
        ICommissionService commissionService,
        IPromoService promoService,
        ICommerceHubPriceAuthority priceAuthority,
        ILogger<CommerceHubCartService> logger)
    {
        _dbContext = dbContext;
        _variantStockRepository = variantStockRepository;
        _shippingService = shippingService;
        _commissionService = commissionService;
        _promoService = promoService;
        _priceAuthority = priceAuthority;
        _logger = logger;
    }

    public async Task<CommerceHubCartStateDto> GetCartAsync(
        CommerceHubCartOwnerContext ownerContext,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!ownerContext.IsAuthenticated)
        {
            return BuildCartStateDto(
                cartId: null,
                ownerContext,
                DateTime.UtcNow,
                DefaultCartVersion,
                Array.Empty<StoredCartItem>(),
                Array.Empty<CommerceHubCartNoticeDto>());
        }

        var (tenantId, commerceHubCustomerId) = RequireAuthenticatedOwner(ownerContext);
        return await GetAuthenticatedCartAsync(
            ownerContext,
            tenantId,
            commerceHubCustomerId,
            correlationId,
            cancellationToken,
            allowRetryOnConflict: true);
    }

    public async Task<CommerceHubCartStateDto> SaveCartAsync(
        CommerceHubCartUpsertRequestDto request,
        CommerceHubCartOwnerContext ownerContext,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var requestedItems = (request.Items ?? new List<CommerceHubCartSnapshotItemRequestDto>())
            .Select(item => new RequestedStoredCartItem
            {
                StoreId = item.StoreId,
                ListingId = item.ListingId,
                ProductId = item.ProductId,
                VariantId = item.VariantId,
                Quantity = item.Quantity,
                PriceSnapshot = item.PriceSnapshot,
                Name = item.Name,
                ImageUrl = item.ImageUrl
            })
            .ToList();

        var sanitized = requestedItems.Count == 0
            ? new CartSanitizationResult()
            : await SanitizeAsync(
                requestedItems,
                correlationId,
                cancellationToken,
                strictValidation: true);
        var updatedAtUtc = DateTime.UtcNow;

        if (!ownerContext.IsAuthenticated)
        {
            return BuildCartStateDto(
                cartId: null,
                ownerContext,
                updatedAtUtc,
                DefaultCartVersion,
                sanitized.Items,
                sanitized.Notices);
        }

        var (tenantId, commerceHubCustomerId) = RequireAuthenticatedOwner(ownerContext);
        var existingCart = await _dbContext.CommerceHubCarts
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId && item.CommerceHubCustomerId == commerceHubCustomerId,
                cancellationToken);

        if (existingCart != null)
        {
            ReleaseExpiredCartLock(existingCart, updatedAtUtc);
            EnsureCartIsMutable(existingCart);
        }

        if (sanitized.Items.Count == 0)
        {
            if (existingCart == null)
            {
                if (request.Version > DefaultCartVersion)
                {
                    throw CreateCartConflictException(DefaultCartVersion);
                }

                return BuildCartStateDto(
                    cartId: null,
                    ownerContext,
                    updatedAtUtc,
                    DefaultCartVersion,
                    Array.Empty<StoredCartItem>(),
                    sanitized.Notices);
            }

            EnsureMatchingVersion(request.Version, existingCart.Version);
            _dbContext.CommerceHubCarts.Remove(existingCart);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.ChangeTracker.Clear();
                var currentVersion = await GetPersistedCartVersionAsync(tenantId, commerceHubCustomerId, cancellationToken);

                if (!currentVersion.HasValue)
                {
                    return BuildCartStateDto(
                        cartId: null,
                        ownerContext,
                        updatedAtUtc,
                        DefaultCartVersion,
                        Array.Empty<StoredCartItem>(),
                        sanitized.Notices);
                }

                throw CreateCartConflictException(currentVersion.Value);
            }

            _logger.LogInformation(
                "CommerceHub persisted cart cleared during save | TenantId: {TenantId} | CommerceHubCustomerId: {CommerceHubCustomerId} | PreviousVersion: {PreviousVersion} | CorrelationId: {CorrelationId}",
                tenantId,
                commerceHubCustomerId,
                existingCart.Version,
                correlationId);

            return BuildCartStateDto(
                cartId: null,
                ownerContext,
                updatedAtUtc,
                DefaultCartVersion,
                Array.Empty<StoredCartItem>(),
                sanitized.Notices);
        }

        var serializedItems = SerializeStoredItems(sanitized.Items);

        if (existingCart == null)
        {
            if (request.Version > DefaultCartVersion)
            {
                throw CreateCartConflictException(DefaultCartVersion);
            }

            existingCart = new CommerceHubCart
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CommerceHubCustomerId = commerceHubCustomerId,
                // PRODUCTION HARDENING: Use commerceHubCustomerId for OnlineCustomerId to avoid
                // unique index collision on (TenantId, OnlineCustomerId). The OnlineCustomerId
                // column is retained for backward compatibility, but new flows bind ownership
                // through CommerceHubCustomerId. Setting it to the same value ensures uniqueness.
                OnlineCustomerId = commerceHubCustomerId,
                ItemsJson = serializedItems,
                CreatedAt = updatedAtUtc,
                Version = 1
            };

            _dbContext.CommerceHubCarts.Add(existingCart);
        }
        else
        {
            EnsureMatchingVersion(request.Version, existingCart.Version);
            SynchronizeCartOwner(existingCart, commerceHubCustomerId);
            existingCart.ItemsJson = serializedItems;
            existingCart.UpdatedAt = updatedAtUtc;
            existingCart.Version += 1;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            var currentVersion = await GetPersistedCartVersionAsync(tenantId, commerceHubCustomerId, cancellationToken);
            throw CreateCartConflictException(currentVersion ?? DefaultCartVersion);
        }
        catch (DbUpdateException ex) when (IsCartOwnerUniqueConstraintViolation(ex))
        {
            _dbContext.ChangeTracker.Clear();
            var currentVersion = await GetPersistedCartVersionAsync(tenantId, commerceHubCustomerId, cancellationToken);
            throw CreateCartConflictException(currentVersion ?? DefaultCartVersion);
        }

        _logger.LogInformation(
            "CommerceHub persisted cart saved | CartId: {CartId} | TenantId: {TenantId} | CommerceHubCustomerId: {CommerceHubCustomerId} | ItemCount: {ItemCount} | Version: {Version} | CorrelationId: {CorrelationId}",
            existingCart.Id,
            tenantId,
            commerceHubCustomerId,
            sanitized.Items.Count,
            existingCart.Version,
            correlationId);

        return BuildCartStateDto(existingCart.Id, ownerContext, updatedAtUtc, existingCart.Version, sanitized.Items, sanitized.Notices);
    }

    public async Task<CommerceHubCartStateDto> ClearCartAsync(
        CommerceHubCartOwnerContext ownerContext,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var updatedAtUtc = DateTime.UtcNow;

        if (!ownerContext.IsAuthenticated)
        {
            return BuildCartStateDto(
                cartId: null,
                ownerContext,
                updatedAtUtc,
                DefaultCartVersion,
                Array.Empty<StoredCartItem>(),
                Array.Empty<CommerceHubCartNoticeDto>());
        }

        var (tenantId, commerceHubCustomerId) = RequireAuthenticatedOwner(ownerContext);
        var existingCart = await _dbContext.CommerceHubCarts
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId && item.CommerceHubCustomerId == commerceHubCustomerId,
                cancellationToken);

        if (existingCart != null)
        {
            ReleaseExpiredCartLock(existingCart, updatedAtUtc);
            EnsureCartIsMutable(existingCart);
            _dbContext.CommerceHubCarts.Remove(existingCart);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.ChangeTracker.Clear();
                var currentVersion = await GetPersistedCartVersionAsync(tenantId, commerceHubCustomerId, cancellationToken);
                if (!currentVersion.HasValue)
                {
                    return BuildCartStateDto(
                        cartId: null,
                        ownerContext,
                        updatedAtUtc,
                        DefaultCartVersion,
                        Array.Empty<StoredCartItem>(),
                        Array.Empty<CommerceHubCartNoticeDto>());
                }

                throw CreateCartConflictException(currentVersion.Value);
            }
        }

        _logger.LogInformation(
            "CommerceHub persisted cart cleared | TenantId: {TenantId} | CommerceHubCustomerId: {CommerceHubCustomerId} | CorrelationId: {CorrelationId}",
            tenantId,
            commerceHubCustomerId,
            correlationId);

        return BuildCartStateDto(
            cartId: null,
            ownerContext,
            updatedAtUtc,
            DefaultCartVersion,
            Array.Empty<StoredCartItem>(),
            Array.Empty<CommerceHubCartNoticeDto>());
    }

    public async Task<CommerceHubCartPricingResponseDto> CalculateAsync(
        CommerceHubCartPricingRequestDto request,
        CommerceHubCartOwnerContext ownerContext,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var requestedLines = request.Stores
            .SelectMany(
                store => store.Items.Select(item => new RequestedPricingLine(
                    store.StoreId,
                    item.ListingId,
                    item.ProductId,
                    item.VariantId,
                    item.Quantity)))
            .GroupBy(line => new { line.StoreId, line.ListingId, line.ProductId, line.VariantId })
            .Select(group => new RequestedPricingLine(
                group.Key.StoreId,
                group.Key.ListingId,
                group.Key.ProductId,
                group.Key.VariantId,
                group.Sum(item => item.Quantity)))
            .ToList();

        if (requestedLines.Count == 0)
        {
            throw new CommerceHubCheckoutException("CART_EMPTY", "Your cart is empty.", StatusCodes.Status400BadRequest);
        }

        if (requestedLines.Count > MaxCartDistinctItems)
        {
            throw new CommerceHubCheckoutException(
                "CART_TOO_LARGE",
                $"Your cart can contain up to {MaxCartDistinctItems} items.",
                StatusCodes.Status400BadRequest,
                new { maxItems = MaxCartDistinctItems, requestedItems = requestedLines.Count });
        }

        var invalidQuantityLine = requestedLines
            .FirstOrDefault(line => line.Quantity < 1 || line.Quantity > MaxCartLineQuantity);
        if (invalidQuantityLine != null)
        {
            throw new CommerceHubCheckoutException(
                "INVALID_QUANTITY",
                $"Quantity must be between 1 and {MaxCartLineQuantity}.",
                StatusCodes.Status400BadRequest,
                new
                {
                    invalidQuantityLine.StoreId,
                    invalidQuantityLine.ProductId,
                    invalidQuantityLine.VariantId,
                    requested = invalidQuantityLine.Quantity,
                    maxAllowed = MaxCartLineQuantity
                });
        }

        var listingIds = requestedLines.Select(line => line.ListingId).Distinct().ToList();
        var listings = await _dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(listing => !listing.IsDeleted && listingIds.Contains(listing.Id))
            .ToDictionaryAsync(listing => listing.Id, cancellationToken);

        if (listings.Count != listingIds.Count)
        {
            throw new CommerceHubCheckoutException(
                "LISTING_NOT_FOUND",
                "One or more cart items are no longer available.",
                StatusCodes.Status409Conflict);
        }

        var listingTenantIds = listings.Values.Select(listing => listing.TenantId).Distinct().ToList();
        var variantIds = requestedLines.Select(line => line.VariantId).Distinct().ToList();
        var variants = await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(variant => variant.Attributes.Where(attribute => !attribute.IsDeleted))
            .WherePubliclyVisibleLineage()
            .Where(variant =>
                variantIds.Contains(variant.Id) &&
                listingTenantIds.Contains(variant.TenantId))
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);

        if (variants.Count != variantIds.Count)
        {
            throw new CommerceHubCheckoutException(
                "VARIANT_NOT_FOUND",
                "One or more cart variants are no longer available.",
                StatusCodes.Status409Conflict);
        }

        var productIds = requestedLines.Select(line => line.ProductId).Distinct().ToList();
        var products = await _dbContext.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(product =>
                !product.IsDeleted &&
                product.IsActive &&
                product.IsOnlineVisible &&
                product.PublishStatus == PublishStatus.Published &&
                productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        if (products.Count != productIds.Count)
        {
            throw new CommerceHubCheckoutException(
                "PRODUCT_NOT_FOUND",
                "One or more cart products are no longer available.",
                StatusCodes.Status409Conflict);
        }

        var tenantIds = listingTenantIds;
        var tenants = await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant =>
                tenantIds.Contains(tenant.Id) &&
                tenant.DeletedAtUtc == null &&
                tenant.Status == TenantStatus.Active)
            .ToDictionaryAsync(tenant => tenant.Id, cancellationToken);

        if (tenants.Count != tenantIds.Count)
        {
            throw new CommerceHubCheckoutException(
                "STORE_UNAVAILABLE",
                "One or more stores are no longer available for checkout.",
                StatusCodes.Status409Conflict);
        }

        foreach (var tenant in tenants.Values)
        {
            if (tenant.Currency != PointOfSale.Core.Financial.FinancialCurrencyGuard.PlatformBaseCurrencyCode)
            {
                throw new CommerceHubCheckoutException(
                    "STORE_CURRENCY_INVALID",
                    $"Store {tenant.Name} operates in {tenant.Currency}. CommerceHub only supports EGP-denominated stores.",
                    StatusCodes.Status409Conflict);
            }
        }

        var paymentMethods = await _dbContext.TenantPaymentMethods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(method => tenantIds.Contains(method.TenantId))
            .ToListAsync(cancellationToken);
        var paymentMethodAvailability = CommerceHubPaymentMethodAuthority.BuildAvailability(tenantIds, paymentMethods);

        var currency = ResolveCommerceHubAuthorityCurrency();
        var validatedLines = new List<ValidatedPricingLine>();

        foreach (var requestedLine in requestedLines)
        {
            var listing = listings[requestedLine.ListingId];
            var variant = variants[requestedLine.VariantId];
            var product = products[requestedLine.ProductId];

            ValidateLineIntegrity(requestedLine, listing, variant, product);

            if (!listing.IsPublished)
            {
                throw new CommerceHubCheckoutException(
                    "LISTING_UNPUBLISHED",
                    $"{product.Name} is no longer published in CommerceHub.",
                    StatusCodes.Status409Conflict);
            }

            if (!variant.IsActive || !product.IsActive)
            {
                throw new CommerceHubCheckoutException(
                    "ITEM_UNAVAILABLE",
                    $"{product.Name} is no longer available for checkout.",
                    StatusCodes.Status409Conflict);
            }

            var availableQuantity = await _variantStockRepository.GetAvailableQuantityAsync(
                listing.TenantId,
                variant.Id,
                cancellationToken);

            if (availableQuantity < requestedLine.Quantity)
            {
                throw new CommerceHubCheckoutException(
                    "OUT_OF_STOCK",
                    $"Only {decimal.ToInt32(decimal.Truncate(Math.Max(0, availableQuantity)))} item(s) available for {product.Name}.",
                    StatusCodes.Status409Conflict,
                    new
                    {
                        requestedLine.ListingId,
                        requestedLine.VariantId,
                        requested = requestedLine.Quantity,
                        available = availableQuantity
                    });
            }

            var priceResolution = await _priceAuthority.ResolveAsync(variant, listing, cancellationToken);
            var unitPrice = RoundCurrency(priceResolution.FinalPrice);
            CommissionResolutionResult commissionResolution;
            try
            {
                commissionResolution = _commissionService.ResolveCommission(
                    requestedLine.StoreId,
                    listing.CommerceHubCategoryId);
            }
            catch (CommissionAuthorityException ex)
            {
                throw new CommerceHubCheckoutException(
                    ex.Code,
                    ex.Message,
                    StatusCodes.Status409Conflict,
                    ex.Details);
            }

            var baseCommissionAmount = CommissionCalculator.RoundAmount(
                unitPrice * requestedLine.Quantity * CommissionCalculator.RoundRate(commissionResolution.Rate));

            // Use variant-specific weight if available, otherwise fall back to product weight
            var weightKg = variant.Weight ?? product.Weight;

            validatedLines.Add(new ValidatedPricingLine(
                requestedLine.StoreId,
                requestedLine.ListingId,
                requestedLine.ProductId,
                requestedLine.VariantId,
                listing.CommerceHubCategoryId,
                requestedLine.Quantity,
                unitPrice,
                baseCommissionAmount,
                weightKg));
        }

        var requestedSelectionsByStore = request.ShippingSelections
            .Where(selection => selection.StoreId != Guid.Empty && selection.ShippingOptionId != Guid.Empty)
            .GroupBy(selection => selection.StoreId)
            .ToDictionary(group => group.Key, group => group.Last().ShippingOptionId);

        var promoLines = validatedLines
            .Select(line => new PromoCommissionLine(
                line.VariantId,
                line.StoreId,
                line.CategoryId,
                line.BaseCommissionAmount))
            .ToList();
        var promoValidation = await _promoService.ValidatePromoAsync(
            request.PromoCode,
            promoLines,
            ownerContext.IsAuthenticated ? ownerContext.CommerceHubCustomerId : null,
            cancellationToken);

        var shippingResult = await _shippingService.CalculateAsync(
            validatedLines
                .GroupBy(line => line.StoreId)
                .Select(group => new CommerceHubShippingStoreRequest
                {
                    StoreId = group.Key,
                    Subtotal = RoundCurrency(group.Sum(line => line.Total)),
                    TotalWeightKg = group.Sum(line => line.TotalWeight)
                })
                .ToList(),
            requestedSelectionsByStore,
            cancellationToken,
            bypassCache: true);

        var subtotal = RoundCurrency(validatedLines.Sum(line => line.Total));
        var shippingTotal = RoundCurrency(shippingResult.TotalShipping);
        var promoDiscountTotal = promoValidation == null
            ? 0m
            : RoundCurrency(
                validatedLines
                    .GroupBy(line => line.StoreId)
                    .Select(group => PromoCalculationEngine.Apply(
                        promoValidation.Promo,
                        group.Key,
                        group.Select(line => new PromoCommissionLine(
                                line.VariantId,
                                line.StoreId,
                                line.CategoryId,
                                line.BaseCommissionAmount))
                            .ToList(),
                        _promoService.CalculateDiscount).DiscountAmount)
                    .Sum());

        _logger.LogInformation(
            "CommerceHub cart priced | Stores: {StoreCount} | ItemCount: {ItemCount} | Subtotal: {Subtotal} | Shipping: {Shipping} | PromoDiscount: {PromoDiscount} | CorrelationId: {CorrelationId}",
            validatedLines.Select(line => line.StoreId).Distinct().Count(),
            validatedLines.Count,
            subtotal,
            shippingTotal,
            promoDiscountTotal,
            correlationId);

        return new CommerceHubCartPricingResponseDto
        {
            Currency = currency,
            Subtotal = subtotal,
            ShippingTotal = shippingTotal,
            Total = RoundCurrency(subtotal + shippingTotal),
            Promo = promoValidation == null
                ? null
                : new CommerceHubCartPricingPromoDto
                {
                    Code = promoValidation.Promo.Code,
                    DiscountTotal = promoDiscountTotal,
                    IsApplied = promoDiscountTotal > 0m
                },
            StoreSelections = shippingResult.StoreSelections
                .Select(selection => new CommerceHubStoreShippingSelectionDto
                {
                    StoreId = selection.StoreId,
                    ShippingOptionId = selection.ShippingOptionId,
                    ShippingPrice = selection.ShippingPrice,
                    ShippingName = selection.ShippingName
                })
                .ToList(),
            PaymentMethods = paymentMethodAvailability
                .Select(method => new CommerceHubCartPaymentMethodAvailabilityDto
                {
                    Code = method.Code,
                    IsAvailable = method.IsAvailable,
                    UnavailableReasonCode = method.UnavailableReasonCode,
                    UnavailableReason = method.UnavailableReason
                })
                .ToList()
        };
    }

    private async Task<CartSanitizationResult> SanitizeAsync(
        IReadOnlyCollection<RequestedStoredCartItem> requestedItems,
        string correlationId,
        CancellationToken cancellationToken,
        bool strictValidation = false)
    {
        var notices = new List<CommerceHubCartNoticeDto>();
        var normalizedLines = new List<NormalizedStoredCartLine>();

        foreach (var group in requestedItems
                     .Select(item => NormalizeRequestedItem(item, notices, strictValidation))
                     .Where(item => item != null)
                     .Cast<RequestedStoredCartItem>()
                     .GroupBy(item => new { item.StoreId, item.ListingId, item.ProductId, item.VariantId }))
        {
            var requestedQuantity = group.Sum(item => item.Quantity);
            if (strictValidation && requestedQuantity > MaxCartLineQuantity)
            {
                throw CreateInvalidQuantityException(
                    group.Key.StoreId,
                    group.Key.ProductId,
                    group.Key.VariantId,
                    requestedQuantity);
            }

            var appliedQuantity = strictValidation
                ? requestedQuantity
                : Math.Min(requestedQuantity, MaxCartLineQuantity);
            var preferredSnapshot = group
                .Where(item => item.PriceSnapshot.HasValue && item.PriceSnapshot.Value > 0m)
                .Select(item => item.PriceSnapshot)
                .LastOrDefault();
            var preferredName = group.Select(item => item.Name).LastOrDefault(name => !string.IsNullOrWhiteSpace(name));
            var preferredImageUrl = group.Select(item => item.ImageUrl).LastOrDefault(url => !string.IsNullOrWhiteSpace(url));

            if (!strictValidation && requestedQuantity > MaxCartLineQuantity)
            {
                notices.Add(CreateNotice(
                    "QUANTITY_CLAMPED",
                    "Cart quantity was limited to the maximum allowed per item.",
                    group.Key.StoreId,
                    group.Key.ProductId,
                    group.Key.VariantId,
                    requestedQuantity,
                    appliedQuantity));
            }

            normalizedLines.Add(new NormalizedStoredCartLine(
                group.Key.StoreId!.Value,
                group.Key.ListingId!.Value,
                group.Key.ProductId!.Value,
                group.Key.VariantId!.Value,
                appliedQuantity,
                preferredSnapshot,
                preferredName,
                preferredImageUrl));
        }

        if (normalizedLines.Count > MaxCartDistinctItems)
        {
            if (strictValidation)
            {
                throw new CommerceHubCartException(
                    "CART_TOO_LARGE",
                    $"Your cart can contain up to {MaxCartDistinctItems} items.",
                    StatusCodes.Status400BadRequest,
                    new { maxItems = MaxCartDistinctItems, requestedItems = normalizedLines.Count });
            }

            notices.Add(CreateNotice(
                "CART_TRIMMED",
                $"Only the first {MaxCartDistinctItems} cart items were kept.",
                null,
                null,
                null,
                normalizedLines.Count,
                MaxCartDistinctItems));

            normalizedLines = normalizedLines
                .OrderBy(item => item.StoreId)
                .ThenBy(item => item.ListingId)
                .ThenBy(item => item.ProductId)
                .ThenBy(item => item.VariantId)
                .Take(MaxCartDistinctItems)
                .ToList();
        }

        if (normalizedLines.Count == 0)
        {
            return new CartSanitizationResult
            {
                Items = new List<StoredCartItem>(),
                Notices = notices
            };
        }

        var storeIds = normalizedLines.Select(line => line.StoreId).Distinct().ToList();
        var listingIds = normalizedLines.Select(line => line.ListingId).Distinct().ToList();
        var productIds = normalizedLines.Select(line => line.ProductId).Distinct().ToList();
        var variantIds = normalizedLines.Select(line => line.VariantId).Distinct().ToList();

        var tenants = await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant =>
                storeIds.Contains(tenant.Id) &&
                tenant.DeletedAtUtc == null &&
                tenant.Status == TenantStatus.Active)
            .ToDictionaryAsync(tenant => tenant.Id, cancellationToken);

        var listings = await _dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(listing => !listing.IsDeleted && listingIds.Contains(listing.Id))
            .ToDictionaryAsync(listing => listing.Id, cancellationToken);

        var products = await _dbContext.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(product => !product.IsDeleted && productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        var variants = await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(variant => variant.Attributes.Where(attribute => !attribute.IsDeleted))
            .WherePubliclyVisibleLineage()
            .Where(variant =>
                variantIds.Contains(variant.Id) &&
                storeIds.Contains(variant.TenantId))
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);

        var availableQuantities = new Dictionary<(Guid StoreId, Guid VariantId), int>();
        foreach (var storeGroup in normalizedLines.GroupBy(line => line.StoreId))
        {
            var quantities = await _variantStockRepository.GetAvailableQuantitiesAsync(
                storeGroup.Key,
                storeGroup.Select(line => line.VariantId).Distinct().ToList(),
                cancellationToken);

            foreach (var quantity in quantities)
            {
                availableQuantities[(storeGroup.Key, quantity.Key)] = NormalizeAvailableQuantity(quantity.Value);
            }
        }

        var sanitizedItems = new List<StoredCartItem>();

        foreach (var line in normalizedLines
                     .OrderBy(item => item.StoreId)
                     .ThenBy(item => item.ListingId)
                     .ThenBy(item => item.ProductId)
                     .ThenBy(item => item.VariantId))
        {
            if (!tenants.TryGetValue(line.StoreId, out var store))
            {
                notices.Add(CreateNotice(
                    "STORE_REMOVED",
                    "A store in your cart is no longer available.",
                    line.StoreId,
                    line.ProductId,
                    line.VariantId,
                    line.Quantity,
                    null));
                continue;
            }

            if (store.Currency != PointOfSale.Core.Financial.FinancialCurrencyGuard.PlatformBaseCurrencyCode)
            {
                if (strictValidation)
                {
                    throw new CommerceHubCartException(
                        "STORE_CURRENCY_INVALID",
                        $"Store {store.Name} operates in {store.Currency}. CommerceHub only supports EGP-denominated stores.",
                        StatusCodes.Status409Conflict);
                }

                notices.Add(CreateNotice(
                    "STORE_CURRENCY_INVALID",
                    $"Store {store.Name} operates in {store.Currency}. CommerceHub only supports EGP-denominated stores.",
                    line.StoreId,
                    line.ProductId,
                    line.VariantId,
                    line.Quantity,
                    null));
                continue;
            }

            if (!listings.TryGetValue(line.ListingId, out var listing))
            {
                notices.Add(CreateNotice(
                    "LISTING_REMOVED",
                    "A listing in your cart is no longer available.",
                    line.StoreId,
                    line.ProductId,
                    line.VariantId,
                    line.Quantity,
                    null));
                continue;
            }

            if (!products.TryGetValue(line.ProductId, out var product))
            {
                notices.Add(CreateNotice(
                    "PRODUCT_REMOVED",
                    "A product in your cart no longer exists.",
                    line.StoreId,
                    line.ProductId,
                    line.VariantId,
                    line.Quantity,
                    null));
                continue;
            }

            if (!variants.TryGetValue(line.VariantId, out var variant))
            {
                notices.Add(CreateNotice(
                    "VARIANT_REMOVED",
                    $"{product.Name} is no longer available in the selected variant.",
                    line.StoreId,
                    line.ProductId,
                    line.VariantId,
                    line.Quantity,
                    null));
                continue;
            }

            if (!ValidateLineIntegrity(line, listing, variant, product))
            {
                notices.Add(CreateNotice(
                    "ITEM_REMOVED_MISMATCH",
                    "A cart item no longer matches the current CommerceHub listing data.",
                    line.StoreId,
                    line.ProductId,
                    line.VariantId,
                    line.Quantity,
                    null));
                continue;
            }

            if (!listing.IsPublished || !product.IsActive || !variant.IsActive ||
                !product.IsOnlineVisible || product.PublishStatus != PublishStatus.Published)
            {
                notices.Add(CreateNotice(
                    "ITEM_REMOVED_UNAVAILABLE",
                    $"{product.Name} is no longer available.",
                    line.StoreId,
                    line.ProductId,
                    line.VariantId,
                    line.Quantity,
                    null));
                continue;
            }

            availableQuantities.TryGetValue((line.StoreId, line.VariantId), out var availableQuantity);
            if (availableQuantity <= 0)
            {
                notices.Add(CreateNotice(
                    "ITEM_REMOVED_OUT_OF_STOCK",
                    $"{product.Name} is out of stock and was removed from your cart.",
                    line.StoreId,
                    line.ProductId,
                    line.VariantId,
                    line.Quantity,
                    0));
                continue;
            }

            var appliedQuantity = line.Quantity;
            string? inventoryNotice = null;

            if (appliedQuantity > availableQuantity)
            {
                notices.Add(CreateNotice(
                    "QUANTITY_ADJUSTED",
                    $"Only {availableQuantity} item(s) are currently available for {product.Name}.",
                    line.StoreId,
                    line.ProductId,
                    line.VariantId,
                    line.Quantity,
                    availableQuantity));

                appliedQuantity = availableQuantity;
                inventoryNotice = $"Only {availableQuantity} item(s) available.";
            }

            var priceResolution = await _priceAuthority.ResolveAsync(variant, listing, cancellationToken);
            var unitPrice = RoundCurrency(priceResolution.FinalPrice);
            var requestedPriceSnapshot = line.PriceSnapshot.HasValue && line.PriceSnapshot.Value > 0m
                ? RoundCurrency(line.PriceSnapshot.Value)
                : (decimal?)null;

            if (requestedPriceSnapshot.HasValue && requestedPriceSnapshot.Value != unitPrice)
            {
                notices.Add(CreateNotice(
                    "PRICE_CHANGED",
                    $"Price changed for {product.Name}. Cart totals were refreshed.",
                    line.StoreId,
                    line.ProductId,
                    line.VariantId,
                    line.Quantity,
                    appliedQuantity));
            }

            var variantAttributes = variant.Attributes
                .OrderBy(attribute => attribute.SortOrder)
                .Select(attribute => new CommerceHubCartVariantAttributeDto
                {
                    Name = attribute.Name,
                    Value = attribute.Value
                })
                .ToList();

            sanitizedItems.Add(new StoredCartItem
            {
                StoreId = store.Id,
                StoreName = string.IsNullOrWhiteSpace(store.Name) ? "Store" : store.Name.Trim(),
                Currency = ResolveCommerceHubAuthorityCurrency(),
                ListingId = listing.Id,
                ProductId = product.Id,
                VariantId = variant.Id,
                Quantity = appliedQuantity,
                PriceSnapshot = unitPrice,
                Name = string.IsNullOrWhiteSpace(product.Name) ? (line.Name ?? "Item") : product.Name.Trim(),
                ImageUrl = ResolveImageUrl(variant, product, line.ImageUrl),
                VariantLabel = BuildVariantName(variant),
                VariantAttributes = variantAttributes,
                MaxQuantity = Math.Min(MaxCartLineQuantity, availableQuantity),
                IsInStock = true,
                InventoryNotice = inventoryNotice
            });
        }

        _logger.LogInformation(
            "CommerceHub cart sanitized | RequestedItems: {RequestedItems} | SanitizedItems: {SanitizedItems} | Notices: {Notices} | CorrelationId: {CorrelationId}",
            requestedItems.Count,
            sanitizedItems.Count,
            notices.Count,
            correlationId);

        return new CartSanitizationResult
        {
            Items = sanitizedItems,
            Notices = notices
        };
    }

    private static RequestedStoredCartItem? NormalizeRequestedItem(
        RequestedStoredCartItem item,
        ICollection<CommerceHubCartNoticeDto> notices,
        bool strictValidation)
    {
        if (!item.StoreId.HasValue || item.StoreId == Guid.Empty ||
            !item.ListingId.HasValue || item.ListingId == Guid.Empty ||
            !item.ProductId.HasValue || item.ProductId == Guid.Empty ||
            !item.VariantId.HasValue || item.VariantId == Guid.Empty)
        {
            if (strictValidation)
            {
                throw new CommerceHubCartException(
                    "ITEM_INVALID",
                    "Each cart item must include valid store, listing, product, and variant identifiers.",
                    StatusCodes.Status400BadRequest);
            }

            notices.Add(CreateNotice(
                "ITEM_REMOVED_INVALID",
                "An invalid cart item was removed.",
                item.StoreId,
                item.ProductId,
                item.VariantId,
                item.Quantity,
                null));
            return null;
        }

        if (item.Quantity < 1 || (strictValidation && item.Quantity > MaxCartLineQuantity))
        {
            if (strictValidation)
            {
                throw CreateInvalidQuantityException(item.StoreId, item.ProductId, item.VariantId, item.Quantity);
            }

            notices.Add(CreateNotice(
                "ITEM_REMOVED_INVALID_QUANTITY",
                "A cart item with an invalid quantity was removed.",
                item.StoreId,
                item.ProductId,
                item.VariantId,
                item.Quantity,
                null));
            return null;
        }

        return new RequestedStoredCartItem
        {
            StoreId = item.StoreId.Value,
            ListingId = item.ListingId.Value,
            ProductId = item.ProductId.Value,
            VariantId = item.VariantId.Value,
            Quantity = item.Quantity,
            PriceSnapshot = item.PriceSnapshot,
            Name = item.Name,
            ImageUrl = item.ImageUrl
        };
    }

    private static (Guid TenantId, Guid CommerceHubCustomerId) RequireAuthenticatedOwner(CommerceHubCartOwnerContext ownerContext)
    {
        if (!ownerContext.IsAuthenticated ||
            !ownerContext.TenantId.HasValue ||
            ownerContext.TenantId.Value == Guid.Empty ||
            !ownerContext.CommerceHubCustomerId.HasValue ||
            ownerContext.CommerceHubCustomerId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("Authenticated CommerceHub cart owner is missing required identifiers.");
        }

        return (ownerContext.TenantId.Value, ownerContext.CommerceHubCustomerId.Value);
    }

    private static string ResolveCommerceHubAuthorityCurrency() =>
        FinancialCurrencyGuard.PlatformBaseCurrencyCode;

    private static int NormalizeAvailableQuantity(decimal quantity)
    {
        if (quantity <= 0)
        {
            return 0;
        }

        var truncated = decimal.Truncate(quantity);
        if (truncated >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return decimal.ToInt32(truncated);
    }

    private static decimal RoundCurrency(decimal value)
    {
        return Math.Round(value, CurrencyScale, MidpointRounding.AwayFromZero);
    }

    private static string? ResolveImageUrl(ProductVariant variant, Product product, string? fallbackImageUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(variant.ImageUrl))
        {
            return variant.ImageUrl;
        }

        if (!string.IsNullOrWhiteSpace(product.Images))
        {
            var firstImage = product.Images
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(firstImage))
            {
                return firstImage;
            }
        }

        return string.IsNullOrWhiteSpace(fallbackImageUrl) ? null : fallbackImageUrl;
    }

    private static string? BuildVariantName(ProductVariant variant)
    {
        if (variant.Attributes.Count == 0)
        {
            return null;
        }

        return string.Join(
            " / ",
            variant.Attributes
                .OrderBy(attribute => attribute.SortOrder)
                .Select(attribute => attribute.Value));
    }

    private static string SerializeStoredItems(IReadOnlyCollection<StoredCartItem> items)
    {
        var normalized = items
            .OrderBy(item => item.StoreId)
            .ThenBy(item => item.ListingId)
            .ThenBy(item => item.ProductId)
            .ThenBy(item => item.VariantId)
            .Select(item => new StoredCartItem
            {
                StoreId = item.StoreId,
                StoreName = item.StoreName,
                Currency = ResolveCommerceHubAuthorityCurrency(),
                ListingId = item.ListingId,
                ProductId = item.ProductId,
                VariantId = item.VariantId,
                Quantity = item.Quantity,
                PriceSnapshot = RoundCurrency(item.PriceSnapshot),
                Name = item.Name,
                ImageUrl = item.ImageUrl,
                VariantLabel = item.VariantLabel,
                VariantAttributes = item.VariantAttributes
                    .Select(attribute => new CommerceHubCartVariantAttributeDto
                    {
                        Name = attribute.Name,
                        Value = attribute.Value
                    })
                    .ToList(),
                MaxQuantity = item.MaxQuantity,
                IsInStock = item.IsInStock,
                InventoryNotice = item.InventoryNotice
            })
            .ToList();

        return JsonSerializer.Serialize(normalized, JsonOptions);
    }

    private List<StoredCartItem> DeserializeStoredItems(string? itemsJson)
    {
        if (string.IsNullOrWhiteSpace(itemsJson))
        {
            return new List<StoredCartItem>();
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<StoredCartItem>>(itemsJson, JsonOptions) ?? new List<StoredCartItem>();
            foreach (var item in items)
            {
                item.Currency = ResolveCommerceHubAuthorityCurrency();
                item.StoreName ??= string.Empty;
                item.Name ??= string.Empty;
                item.VariantAttributes ??= new List<CommerceHubCartVariantAttributeDto>();
            }

            return items;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "CommerceHub cart JSON could not be deserialized and will be reset.");
            return new List<StoredCartItem>();
        }
    }

    private static CommerceHubCartStateDto BuildCartStateDto(
        Guid? cartId,
        CommerceHubCartOwnerContext ownerContext,
        DateTime updatedAtUtc,
        int version,
        IReadOnlyCollection<StoredCartItem> items,
        IReadOnlyCollection<CommerceHubCartNoticeDto> notices,
        bool isLocked = false,
        DateTime? lockedAtUtc = null)
    {
        var orderedNotices = notices.ToList();

        return new CommerceHubCartStateDto
        {
            CartId = cartId,
            IsServerBacked = ownerContext.IsAuthenticated,
            IsAuthenticatedOwner = ownerContext.IsAuthenticated,
            IsLocked = isLocked,
            LockedAtUtc = lockedAtUtc,
            Version = version,
            UpdatedAtUtc = updatedAtUtc,
            Items = items
                .OrderBy(item => item.StoreName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.VariantLabel, StringComparer.OrdinalIgnoreCase)
                .Select(item => new CommerceHubCartStateItemDto
                {
                    StoreId = item.StoreId,
                    StoreName = item.StoreName,
                    Currency = item.Currency,
                    ListingId = item.ListingId,
                    ProductId = item.ProductId,
                    VariantId = item.VariantId,
                    Quantity = item.Quantity,
                    PriceSnapshot = item.PriceSnapshot,
                    Name = item.Name,
                    ImageUrl = item.ImageUrl,
                    VariantLabel = item.VariantLabel,
                    VariantAttributes = item.VariantAttributes
                        .Select(attribute => new CommerceHubCartVariantAttributeDto
                        {
                            Name = attribute.Name,
                            Value = attribute.Value
                        })
                        .ToList(),
                    MaxQuantity = item.MaxQuantity,
                    IsInStock = item.IsInStock,
                    InventoryNotice = item.InventoryNotice
                })
                .ToList(),
            Notices = orderedNotices,
            Warnings = orderedNotices
                .Select(notice => notice.Message)
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };
    }

    private static RequestedStoredCartItem ToRequestedStoredCartItem(StoredCartItem item)
    {
        return new RequestedStoredCartItem
        {
            StoreId = item.StoreId,
            ListingId = item.ListingId,
            ProductId = item.ProductId,
            VariantId = item.VariantId,
            Quantity = item.Quantity,
            PriceSnapshot = item.PriceSnapshot,
            Name = item.Name,
            ImageUrl = item.ImageUrl
        };
    }

    private static bool ValidateLineIntegrity(
        NormalizedStoredCartLine requestedLine,
        CommerceHubListing listing,
        ProductVariant variant,
        Product product)
    {
        return listing.TenantId == requestedLine.StoreId &&
               listing.ProductId == requestedLine.ProductId &&
               listing.VariantId == requestedLine.VariantId &&
               variant.ProductId == requestedLine.ProductId &&
               variant.TenantId == requestedLine.StoreId &&
               product.TenantId == requestedLine.StoreId;
    }

    private static void ValidateLineIntegrity(
        RequestedPricingLine requestedLine,
        CommerceHubListing listing,
        ProductVariant variant,
        Product product)
    {
        if (listing.TenantId != requestedLine.StoreId ||
            listing.ProductId != requestedLine.ProductId ||
            listing.VariantId != requestedLine.VariantId ||
            variant.ProductId != requestedLine.ProductId ||
            variant.TenantId != requestedLine.StoreId ||
            product.TenantId != requestedLine.StoreId)
        {
            throw new CommerceHubCheckoutException(
                "CART_MISMATCH",
                "One or more cart items no longer match the current CommerceHub listing data.",
                StatusCodes.Status409Conflict);
        }
    }

    private static CommerceHubCartNoticeDto CreateNotice(
        string code,
        string message,
        Guid? storeId,
        Guid? productId,
        Guid? variantId,
        int? requestedQuantity,
        int? appliedQuantity)
    {
        return new CommerceHubCartNoticeDto
        {
            Code = code,
            Message = message,
            StoreId = storeId,
            ProductId = productId,
            VariantId = variantId,
            RequestedQuantity = requestedQuantity,
            AppliedQuantity = appliedQuantity
        };
    }

    private async Task<CommerceHubCartStateDto> GetAuthenticatedCartAsync(
        CommerceHubCartOwnerContext ownerContext,
        Guid tenantId,
        Guid commerceHubCustomerId,
        string correlationId,
        CancellationToken cancellationToken,
        bool allowRetryOnConflict)
    {
        var cart = await _dbContext.CommerceHubCarts
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId && item.CommerceHubCustomerId == commerceHubCustomerId,
                cancellationToken);

        if (cart == null)
        {
            return BuildCartStateDto(
                cartId: null,
                ownerContext,
                DateTime.UtcNow,
                DefaultCartVersion,
                Array.Empty<StoredCartItem>(),
                Array.Empty<CommerceHubCartNoticeDto>());
        }

        var nowUtc = DateTime.UtcNow;
        var lockExpired = IsCartLockExpired(cart, nowUtc);
        if (lockExpired)
        {
            cart.IsLocked = false;
            cart.LockedAtUtc = null;
            cart.UpdatedAt = nowUtc;
        }

        var isLocked = cart.IsLocked;
        var storedItems = DeserializeStoredItems(cart.ItemsJson);
        var sanitized = await SanitizeAsync(
            storedItems.Select(ToRequestedStoredCartItem).ToList(),
            correlationId,
            cancellationToken);
        var updatedAtUtc = cart.UpdatedAt ?? cart.CreatedAt;

        if (sanitized.Items.Count == 0)
        {
            if (isLocked)
            {
                return BuildCartStateDto(
                    cart.Id,
                    ownerContext,
                    updatedAtUtc,
                    cart.Version,
                    Array.Empty<StoredCartItem>(),
                    sanitized.Notices,
                    isLocked: true,
                    lockedAtUtc: cart.LockedAtUtc);
            }

            _dbContext.CommerceHubCarts.Remove(cart);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (allowRetryOnConflict)
            {
                _logger.LogInformation(
                    "CommerceHub cart changed while sanitation cleanup was being persisted, retrying load | TenantId: {TenantId} | CommerceHubCustomerId: {CommerceHubCustomerId} | CorrelationId: {CorrelationId}",
                    tenantId,
                    commerceHubCustomerId,
                    correlationId);

                _dbContext.ChangeTracker.Clear();
                return await GetAuthenticatedCartAsync(
                    ownerContext,
                    tenantId,
                    commerceHubCustomerId,
                    correlationId,
                    cancellationToken,
                    allowRetryOnConflict: false);
            }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.ChangeTracker.Clear();
                var currentVersion = await GetPersistedCartVersionAsync(tenantId, commerceHubCustomerId, cancellationToken);
                if (!currentVersion.HasValue)
                {
                    return BuildCartStateDto(
                        cartId: null,
                        ownerContext,
                        DateTime.UtcNow,
                        DefaultCartVersion,
                        Array.Empty<StoredCartItem>(),
                        sanitized.Notices);
                }

                throw CreateCartConflictException(currentVersion.Value);
            }

            _logger.LogInformation(
                "CommerceHub persisted cart removed after sanitation | CartId: {CartId} | TenantId: {TenantId} | CommerceHubCustomerId: {CommerceHubCustomerId} | CorrelationId: {CorrelationId}",
                cart.Id,
                tenantId,
                commerceHubCustomerId,
                correlationId);

            return BuildCartStateDto(
                cartId: null,
                ownerContext,
                DateTime.UtcNow,
                DefaultCartVersion,
                Array.Empty<StoredCartItem>(),
                sanitized.Notices);
        }

        var originalJson = SerializeStoredItems(storedItems);
        var sanitizedJson = SerializeStoredItems(sanitized.Items);
        var shouldPersistSanitizedItems = !isLocked && !string.Equals(originalJson, sanitizedJson, StringComparison.Ordinal);
        var shouldPersistLockRelease = lockExpired;

        if (shouldPersistSanitizedItems || shouldPersistLockRelease)
        {
            if (shouldPersistSanitizedItems)
            {
                cart.ItemsJson = sanitizedJson;
            }

            SynchronizeCartOwner(cart, commerceHubCustomerId);
            cart.UpdatedAt = nowUtc;
            cart.Version += 1;

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                updatedAtUtc = cart.UpdatedAt.Value;
            }
            catch (DbUpdateConcurrencyException) when (allowRetryOnConflict)
            {
                _logger.LogInformation(
                    "CommerceHub cart changed while normalization was being persisted, retrying load | TenantId: {TenantId} | CommerceHubCustomerId: {CommerceHubCustomerId} | CorrelationId: {CorrelationId}",
                    tenantId,
                    commerceHubCustomerId,
                    correlationId);

                _dbContext.ChangeTracker.Clear();
                return await GetAuthenticatedCartAsync(
                    ownerContext,
                    tenantId,
                    commerceHubCustomerId,
                    correlationId,
                    cancellationToken,
                    allowRetryOnConflict: false);
            }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.ChangeTracker.Clear();
                var currentVersion = await GetPersistedCartVersionAsync(tenantId, commerceHubCustomerId, cancellationToken);
                if (!currentVersion.HasValue)
                {
                    return BuildCartStateDto(
                        cartId: null,
                        ownerContext,
                        DateTime.UtcNow,
                        DefaultCartVersion,
                        Array.Empty<StoredCartItem>(),
                        sanitized.Notices);
                }

                throw CreateCartConflictException(currentVersion.Value);
            }

            _logger.LogInformation(
                "CommerceHub persisted cart normalized on load | CartId: {CartId} | TenantId: {TenantId} | CommerceHubCustomerId: {CommerceHubCustomerId} | ItemCount: {ItemCount} | Version: {Version} | LockReleased: {LockReleased} | CorrelationId: {CorrelationId}",
                cart.Id,
                tenantId,
                commerceHubCustomerId,
                sanitized.Items.Count,
                cart.Version,
                shouldPersistLockRelease,
                correlationId);
        }

        return BuildCartStateDto(
            cart.Id,
            ownerContext,
            updatedAtUtc,
            cart.Version,
            sanitized.Items,
            sanitized.Notices,
            isLocked: cart.IsLocked,
            lockedAtUtc: cart.LockedAtUtc);
    }

    private async Task<int?> GetPersistedCartVersionAsync(
        Guid tenantId,
        Guid commerceHubCustomerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubCarts
            .AsNoTracking()
            .Where(cart => cart.TenantId == tenantId && cart.CommerceHubCustomerId == commerceHubCustomerId)
            .Select(cart => (int?)cart.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static void EnsureMatchingVersion(int requestedVersion, int currentVersion)
    {
        if (requestedVersion != currentVersion)
        {
            throw CreateCartConflictException(currentVersion);
        }
    }

    private static void EnsureCartIsMutable(CommerceHubCart cart)
    {
        if (cart.IsLocked)
        {
            throw CreateCartLockedException(cart.LockedAtUtc);
        }
    }

    /// <summary>
    /// Synchronizes cart owner columns to satisfy CK_CommerceHubCarts_CustomerAuthorityBoundary.
    /// Both CommerceHubCustomerId and OnlineCustomerId must be set to the same value before
    /// persisting to ensure the constraint (OnlineCustomerId = CommerceHubCustomerId) is satisfied.
    /// </summary>
    private static void SynchronizeCartOwner(CommerceHubCart cart, Guid commerceHubCustomerId)
    {
        cart.CommerceHubCustomerId = commerceHubCustomerId;
        cart.OnlineCustomerId = commerceHubCustomerId;
    }

    private static void ReleaseExpiredCartLock(CommerceHubCart cart, DateTime nowUtc)
    {
        if (!IsCartLockExpired(cart, nowUtc))
        {
            return;
        }

        cart.IsLocked = false;
        cart.LockedAtUtc = null;
        cart.UpdatedAt = nowUtc;
    }

    private static bool IsCartLockExpired(CommerceHubCart cart, DateTime nowUtc)
    {
        return cart.IsLocked &&
               (!cart.LockedAtUtc.HasValue || cart.LockedAtUtc.Value <= nowUtc.Subtract(CartLockTimeout));
    }

    private static CommerceHubCartException CreateCartConflictException(int currentVersion)
    {
        return new CommerceHubCartException(
            "CART_CONFLICT",
            "Cart was modified in another session. Review the latest cart and try again.",
            StatusCodes.Status409Conflict,
            new { currentVersion });
    }

    private static CommerceHubCartException CreateCartLockedException(DateTime? lockedAtUtc)
    {
        return new CommerceHubCartException(
            "CART_LOCKED",
            "Cart is locked during checkout. Refresh the cart after checkout finishes.",
            StatusCodes.Status409Conflict,
            new
            {
                lockedAtUtc,
                lockExpiresAtUtc = lockedAtUtc?.Add(CartLockTimeout)
            });
    }

    private static CommerceHubCartException CreateInvalidQuantityException(
        Guid? storeId,
        Guid? productId,
        Guid? variantId,
        int requestedQuantity)
    {
        return new CommerceHubCartException(
            "INVALID_QUANTITY",
            $"Quantity must be between 1 and {MaxCartLineQuantity}.",
            StatusCodes.Status400BadRequest,
            new
            {
                storeId,
                productId,
                variantId,
                requested = requestedQuantity,
                maxAllowed = MaxCartLineQuantity
            });
    }

    private static bool IsCartOwnerUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException postgresException &&
               postgresException.SqlState == "23505" &&
               (string.Equals(
                    postgresException.ConstraintName,
                    "UX_CommerceHubCarts_TenantId_CommerceHubCustomerId_Active",
                    StringComparison.Ordinal) ||
                string.Equals(
                    postgresException.ConstraintName,
                    "UX_CommerceHubCarts_CommerceHubCustomerId_Active",
                    StringComparison.Ordinal) ||
                string.Equals(
                    postgresException.ConstraintName,
                    "IX_CommerceHubCarts_TenantId_OnlineCustomerId",
                    StringComparison.Ordinal));
    }

    private sealed class RequestedStoredCartItem
    {
        public Guid? StoreId { get; set; }
        public Guid? ListingId { get; set; }
        public Guid? ProductId { get; set; }
        public Guid? VariantId { get; set; }
        public int Quantity { get; set; }
        public decimal? PriceSnapshot { get; set; }
        public string? Name { get; set; }
        public string? ImageUrl { get; set; }
    }

    private sealed record NormalizedStoredCartLine(
        Guid StoreId,
        Guid ListingId,
        Guid ProductId,
        Guid VariantId,
        int Quantity,
        decimal? PriceSnapshot,
        string? Name,
        string? ImageUrl);

    private sealed class CartSanitizationResult
    {
        public List<StoredCartItem> Items { get; set; } = new();
        public List<CommerceHubCartNoticeDto> Notices { get; set; } = new();
    }

    private sealed record RequestedPricingLine(
        Guid StoreId,
        Guid ListingId,
        Guid ProductId,
        Guid VariantId,
        int Quantity);

    private sealed record ValidatedPricingLine(
        Guid StoreId,
        Guid ListingId,
        Guid ProductId,
        Guid VariantId,
        Guid CategoryId,
        int Quantity,
        decimal UnitPrice,
        decimal BaseCommissionAmount,
        decimal WeightKg)
    {
        public decimal Total => UnitPrice * Quantity;
        public decimal TotalWeight => WeightKg * Quantity;
    }

    private sealed class StoredCartItem
    {
        public Guid StoreId { get; set; }
        public string? StoreName { get; set; } = string.Empty;
        public string Currency { get; set; } = "EGP";
        public Guid ListingId { get; set; }
        public Guid ProductId { get; set; }
        public Guid VariantId { get; set; }
        public int Quantity { get; set; }
        public decimal PriceSnapshot { get; set; }
        public string? Name { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string? VariantLabel { get; set; }
        public List<CommerceHubCartVariantAttributeDto>? VariantAttributes { get; set; } = new();
        public int? MaxQuantity { get; set; }
        public bool IsInStock { get; set; }
        public string? InventoryNotice { get; set; }
    }
}
