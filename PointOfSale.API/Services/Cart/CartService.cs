using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.Events;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Promotions;
using PointOfSale.API.Services.Pricing;
using PointOfSale.API.Services.Checkout;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Pricing;
using PointOfSale.Core.Validation;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Reliability;
using MongoDB.Driver;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.Cart;

/// <summary>
/// Implementation of Cart service.
/// 
/// ARCHITECTURAL BOUNDARY (Phase 3.1 - Cart API):
/// - Redis is the ONLY storage for cart state
/// - Cart is tenant-scoped and ephemeral (TTL-based)
/// - MongoDB is read-only (product validation only)
/// - Every cart operation emits an event via MassTransit
/// - No stock reservation, no checkout, no payments
/// 
/// Phase 3.2 Hardening:
/// - Cart consistency validation with auto-fix/reset
/// - Cart size and abuse protection limits
/// - Currency locking (first item defines currency)
/// - Product re-validation on cart read
/// - Redis failure safety with graceful degradation
/// - Enhanced observability with validation actions
/// 
/// Error Semantics:
/// - Redis unavailable → 503 SERVICE_UNAVAILABLE
/// - Product not found/inactive → 404 PRODUCT_NOT_FOUND
/// - Invalid quantity → 400 INVALID_QUANTITY
/// - Cart limit exceeded → 400 CART_LIMIT_EXCEEDED
/// - Currency mismatch → 400 CURRENCY_MISMATCH
/// 
/// TTL: 24 hours (reset on every write)
/// </summary>
public class CartService : ICartService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly RedisConnectionFactory _redisFactory;
    private readonly RedisSettings _redisSettings;
    private readonly IProductReadRepository _productRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<CartService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IMongoCollection<StorefrontConfigDocument> _storefrontConfigCollection;
    private readonly IPromotionEngine _promotionEngine;
    private readonly ITaxCalculationService _taxCalculationService;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly IDistributedLockService _distributedLockService;
    private readonly AsyncLocal<List<object>?> _deferredCartEvents = new();

    // =============================================================================
    // CART CONFIGURATION CONSTANTS
    // =============================================================================
    
    /// <summary>
    /// Cart TTL in hours (24 hours)
    /// </summary>
    private const int CartTtlHours = 24;

    /// <summary>
    /// Maximum quantity per item
    /// </summary>
    private const int MaxQuantityPerItem = 99;

    /// <summary>
    /// Maximum number of unique items in cart
    /// </summary>
    private const int MaxItems = 50;

    /// <summary>
    /// Maximum total quantity across all items
    /// </summary>
    private const int MaxTotalQuantity = 300;

    /// <summary>
    /// Maximum cart payload size in bytes (~50KB)
    /// </summary>
    private const int MaxCartPayloadBytes = 50 * 1024;

    /// <summary>
    /// Cart mutation lock TTL (3 minutes).
    /// Increased from 15 seconds to handle long-running operations like ApplyDraftAsync/MergeCartAsync
    /// which can iterate over many items with multiple Redis, MongoDB, EF Core, tax, and event-publishing calls.
    /// Combined with automatic renewal mechanism to prevent lock expiration during execution.
    /// </summary>
    private static readonly TimeSpan CartMutationLockTtl = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Lock renewal interval (every 30 seconds).
    /// Heartbeat mechanism to extend the lock while the operation is still running.
    /// </summary>
    private static readonly TimeSpan CartMutationLockRenewalInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _cartMutationLockRenewalInterval;

    public CartService(
        ApplicationDbContext dbContext,
        RedisConnectionFactory redisFactory,
        IOptions<RedisSettings> redisSettings,
        IProductReadRepository productRepository,
        IEventPublisher eventPublisher,
        IMongoDatabase mongoDatabase,
        IPromotionEngine promotionEngine,
        ITaxCalculationService taxCalculationService,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        IDistributedLockService distributedLockService,
        ILogger<CartService> logger,
        TimeSpan? lockRenewalInterval = null)
    {
        _dbContext = dbContext;
        _redisFactory = redisFactory;
        _redisSettings = redisSettings.Value;
        _productRepository = productRepository;
        _eventPublisher = eventPublisher;
        _storefrontConfigCollection = mongoDatabase.GetCollection<StorefrontConfigDocument>("storefront_configs");
        _promotionEngine = promotionEngine;
        _taxCalculationService = taxCalculationService;
        _reliabilityTelemetry = reliabilityTelemetry;
        _distributedLockService = distributedLockService;
        _logger = logger;
        _cartMutationLockRenewalInterval = lockRenewalInterval ?? CartMutationLockRenewalInterval;
        if (_cartMutationLockRenewalInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockRenewalInterval));
        }
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    #region Get Cart

    /// <inheritdoc />
    public async Task<CartDto> GetCartAsync(
        Guid tenantId,
        CartOwner owner,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureOwnerTenantMatchAsync(tenantId, owner, correlationId, cancellationToken);

        var cacheKey = BuildCacheKey(tenantId, owner);

        _logger.LogDebug(
            "GetCart | TenantId: {TenantId} | OwnerType: {OwnerType} | OwnerId: {OwnerId} | CorrelationId: {CorrelationId}",
            tenantId, owner.OwnerType, owner.OwnerId, correlationId);

        var cart = await GetCartFromRedisAsync(tenantId, owner, cacheKey, correlationId);

        // BACKWARD COMPATIBILITY: If cart not found and owner is a customer with "N" format GUID,
        // try legacy hyphenated format to recover existing carts after deployment
        if (cart == null && owner.IsCustomer && Guid.TryParse(owner.OwnerId, out var customerGuid))
        {
            var legacyOwnerId = customerGuid.ToString(); // Default format with hyphens
            if (!string.Equals(legacyOwnerId, owner.OwnerId, StringComparison.OrdinalIgnoreCase))
            {
                var legacyCacheKey = CacheKeyBuilder.CartKey(
                    _redisSettings.InstanceName,
                    tenantId,
                    owner.OwnerType,
                    legacyOwnerId);

                _logger.LogInformation(
                    "Attempting legacy cart key fallback | TenantId: {TenantId} | CustomerId: {CustomerId} | NewKey: {NewKey} | LegacyKey: {LegacyKey} | CorrelationId: {CorrelationId}",
                    tenantId,
                    owner.OwnerId,
                    cacheKey,
                    legacyCacheKey,
                    correlationId);

                cart = await GetCartFromRedisAsync(tenantId, owner, legacyCacheKey, correlationId);

                // If found in legacy format, migrate to new format
                if (cart != null)
                {
                    _logger.LogInformation(
                        "Migrating cart from legacy key format | TenantId: {TenantId} | CustomerId: {CustomerId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                        tenantId,
                        owner.OwnerId,
                        cart.CartId,
                        correlationId);

                    try
                    {
                        // Save to new key format
                        await SaveCartToRedisAsync(cart, cacheKey, correlationId, cancellationToken);

                        // Delete legacy key
                        var database = _redisFactory.GetDatabase();
                        if (database != null)
                        {
                            await database.KeyDeleteAsync(legacyCacheKey);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to migrate cart to new key format - cart will remain accessible | TenantId: {TenantId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
                            tenantId,
                            owner.OwnerId,
                            correlationId);
                    }
                }
            }
        }

        if (cart != null)
        {
            // Phase 3.2: Validate and fix cart consistency
            var (validatedCart, action) = await ValidateAndFixCartAsync(
                cart, tenantId, owner, cacheKey, correlationId, cancellationToken);
            
            cart = validatedCart;

            // Phase 3.2: Re-validate products and remove unavailable items
            cart = await RevalidateCartProductsAsync(cart, cacheKey, correlationId, cancellationToken);

            // CART PROMOTIONS ENGINE: Apply cart-level promotions
            await ApplyCartPromotionAsync(cart, tenantId, correlationId, cancellationToken);
            
            // Refresh pricing preview with tax calculation - degrade gracefully on failure
            try
            {
                await RefreshCartPricingPreviewAsync(cart, correlationId, cancellationToken);
            }
            catch (Exception ex)
            {
                // Don't fail the cart read if tax calculation fails
                _logger.LogWarning(ex,
                    "Failed to refresh cart pricing preview - continuing without tax update | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                    tenantId, cart.CartId, correlationId);
            }

            _logger.LogInformation(
                "GetCart success | TenantId: {TenantId} | CartId: {CartId} | ItemCount: {ItemCount} | TotalQuantity: {TotalQuantity} | CartPromotion: {CartPromotion} | ValidationAction: {ValidationAction} | CorrelationId: {CorrelationId}",
                tenantId, cart.CartId, cart.Items.Count, cart.ItemCount, cart.AppliedCartPromotion?.Name ?? "None", action, correlationId);
            
            return cart;
        }

        // Return empty cart structure (expired/missing cart = empty)
        var emptyCart = await CreateEmptyCartAsync(tenantId, owner, cancellationToken);
        _logger.LogInformation(
            "GetCart - empty cart | TenantId: {TenantId} | OwnerType: {OwnerType} | OwnerId: {OwnerId} | CorrelationId: {CorrelationId}",
            tenantId, owner.OwnerType, owner.OwnerId, correlationId);

        return emptyCart;
    }

    #endregion

    #region Add Item

    /// <inheritdoc />
    /// <remarks>
    /// PHASE 11.4 — VARIANT-CENTRIC CART
    /// 
    /// Every cart item is now a VARIANT (SKU), not a product.
    /// - VariantId is the primary key for cart items
    /// - Price comes from Variant.OnlinePrice or Variant.Price (NEVER Product.Price)
    /// - Stock validation is per-Variant (TotalStock)
    /// - Same product with different variants = different cart items
    /// 
    /// GUARDRAILS:
    /// - NEVER use Product.Price in cart or checkout
    /// - Variant is the sellable SKU
    /// - Fail fast if VariantId missing or invalid
    /// 
    /// AF-PA5.2 — CART LINE ATTRIBUTION:
    /// - Affiliate context is backend evidence only
    /// - Mixed carts are allowed
    /// - Each line receives attribution only after current backend eligibility admission
    /// </remarks>
    public async Task<CartDto> AddItemAsync(
        Guid tenantId,
        CartOwner owner,
        AddCartItemRequest request,
        Guid? affiliateAccountId,
        string? affiliateReferralCode,
        Guid? promotionLinkId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureOwnerTenantMatchAsync(tenantId, owner, correlationId, cancellationToken);
        // Mongo supplies only a candidate. Fetch it before acquiring the cart
        // owner lock; SQL stock, visibility, variant and price are still read
        // fresh under the lock before any cart mutation or Redis persistence.
        var projectedProduct = await _productRepository.FindByProductIdAsync(
            tenantId, request.ProductId, cancellationToken);
        return await ExecuteWithCartMutationLockAsync(
            tenantId,
            owner,
            correlationId,
            cancellationToken,
            ct => AddItemInternalAsync(
                tenantId,
                owner,
                request,
                affiliateAccountId,
                affiliateReferralCode,
                promotionLinkId,
                correlationId,
                ct,
                projectedProduct,
                candidateAlreadyLoaded: true));
    }

    public Task<CartDto> AddItemAsync(
        Guid tenantId,
        CartOwner owner,
        AddCartItemRequest request,
        bool affiliateModeOnly,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        return AddItemAsync(
            tenantId,
            owner,
            request,
            null,
            null,
            null,
            correlationId,
            cancellationToken);
    }

    /// <summary>
    /// Internal helper for adding items to cart without owner validation.
    /// Used by ApplyDraftAsync after it has already validated the owner.
    /// </summary>
    private async Task<CartDto> AddItemInternalAsync(
        Guid tenantId,
        CartOwner owner,
        AddCartItemRequest request,
        Guid? affiliateAccountId,
        string? affiliateReferralCode,
        Guid? promotionLinkId,
        string correlationId,
        CancellationToken cancellationToken = default,
        ProductDocument? projectedProduct = null,
        bool candidateAlreadyLoaded = false)
    {

        var cacheKey = BuildCacheKey(tenantId, owner);

        _logger.LogDebug(
            "AddItem starting | TenantId: {TenantId} | ProductId: {ProductId} | VariantId: {VariantId} | Quantity: {Quantity} | CorrelationId: {CorrelationId}",
            tenantId, request.ProductId, request.VariantId, request.Quantity, correlationId);

        // =============================================================================
        // PHASE 11.4: VARIANT VALIDATION - Variant is the sellable SKU
        // =============================================================================
        
        // Step 1: Load product document from MongoDB
        var product = await ValidateProductAsync(
            tenantId, request.ProductId, correlationId, cancellationToken,
            projectedProduct, candidateAlreadyLoaded);
        if (product == null)
        {
            throw new ProductNotFoundException(request.ProductId);
        }
        var productPurchasability = ProductStorefrontVisibilityAuthority.Evaluate(product);

        var lineAdmission = affiliateAccountId.HasValue
            ? await ResolveAffiliateCartLineAdmissionAsync(
                tenantId,
                request.ProductId,
                affiliateAccountId,
                affiliateReferralCode,
                promotionLinkId,
                correlationId,
                cancellationToken)
            : AffiliateCartLineAdmission.NotEligible;

        // Step 2: Resolve and validate the sellable SKU.
        ProductVariantDocument? variant = null;
        StorefrontPurchasabilityResult? variantPurchasability = null;
        
        if (product.HasVariants)
        {
            // =============================================================================
            // PHASE 11.6: VARIANT REQUIRED ENFORCEMENT
            // =============================================================================
            // If product HasVariants = true (flagged in PostgreSQL), VariantId is MANDATORY.
            // No silent fallback - fail fast to force frontend to send correct data.
            // 
            // HasVariants flag comes from PostgreSQL Product.HasVariants field.
            // This is different from Variants.Count > 0 (which is MongoDB structure).
            // A product can have 1 variant in MongoDB but HasVariants = false (simple product).
            // =============================================================================
            
            if (request.VariantId == null)
            {
                _logger.LogWarning(
                    "AddItem rejected - variant selection required but not provided | TenantId: {TenantId} | ProductId: {ProductId} | ProductName: {ProductName} | CorrelationId: {CorrelationId}",
                    tenantId, request.ProductId, product.Name, correlationId);
                throw new VariantRequiredException(request.ProductId, product.Name);
            }
            else
            {
                // Find the specific variant
                variant = product.Variants.FirstOrDefault(v => v.VariantId == request.VariantId.Value);
                
                if (variant == null)
                {
                    _logger.LogWarning(
                        "AddItem rejected - variant not found | TenantId: {TenantId} | ProductId: {ProductId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                        tenantId, request.ProductId, request.VariantId, correlationId);
                    throw new VariantNotFoundException(request.ProductId, request.VariantId.Value);
                }
                
                // Validate variant is active
                if (!variant.IsActive)
                {
                    _logger.LogWarning(
                        "AddItem rejected - variant inactive | TenantId: {TenantId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                        tenantId, request.VariantId, correlationId);
                    throw new VariantInactiveException(request.VariantId.Value);
                }
            }
            
            variantPurchasability = ProductStorefrontVisibilityAuthority.EvaluateVariant(product, variant);

            if (!variantPurchasability.InStock)
            {
                _logger.LogWarning(
                    "AddItem rejected - variant out of stock by canonical storefront contract | TenantId: {TenantId} | VariantId: {VariantId} | AvailableStock: {AvailableStock} | CorrelationId: {CorrelationId}",
                    tenantId, variant.VariantId, variantPurchasability.StockQuantity, correlationId);
                throw new VariantOutOfStockException(variant.VariantId, variant.SKU ?? "Unknown");
            }
            
            // PHASE 11.6: Validate variant has a valid price > 0
            if (variantPurchasability.Price <= 0 || !variantPurchasability.IsAvailable)
            {
                _logger.LogWarning(
                    "AddItem rejected - variant has no valid price | TenantId: {TenantId} | VariantId: {VariantId} | Price: {Price} | OnlinePrice: {OnlinePrice} | CorrelationId: {CorrelationId}",
                    tenantId, variant.VariantId, variant.Price, variant.OnlinePrice, correlationId);
                throw new ProductNotForSaleException(request.ProductId);
            }
        }
        else
        {
            if (!productPurchasability.IsAvailable || productPurchasability.Price <= 0m)
            {
                _logger.LogWarning(
                    "AddItem rejected - simple product is not purchasable by canonical storefront contract | TenantId: {TenantId} | ProductId: {ProductId} | Reason: {Reason} | CorrelationId: {CorrelationId}",
                    tenantId, request.ProductId, productPurchasability.UnavailableReason, correlationId);
                throw new ProductNotForSaleException(request.ProductId);
            }

            if (!productPurchasability.InStock)
            {
                _logger.LogWarning(
                    "AddItem rejected - out of stock | TenantId: {TenantId} | ProductId: {ProductId} | StockQuantity: {StockQuantity} | CorrelationId: {CorrelationId}",
                    tenantId, request.ProductId, productPurchasability.StockQuantity, correlationId);
                throw new ProductOutOfStockException(request.ProductId, product.Name);
            }

            // Simple products still have exactly one server-authoritative sellable SKU.
            // Resolve it from the tenant/product-scoped authority and never adopt an
            // arbitrary caller-supplied VariantId as cart identity.
            variant = ResolveCanonicalSimpleProductVariant(product);
            if (variant == null)
            {
                _logger.LogWarning(
                    "AddItem rejected - simple product has no active canonical variant | TenantId: {TenantId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                    tenantId, request.ProductId, correlationId);
                throw new VariantNotFoundException(request.ProductId, request.VariantId ?? Guid.Empty);
            }

            if (request.VariantId.HasValue && request.VariantId.Value != variant.VariantId)
            {
                _logger.LogWarning(
                    "AddItem rejected - submitted variant is not the product's canonical simple variant | TenantId: {TenantId} | ProductId: {ProductId} | SubmittedVariantId: {SubmittedVariantId} | CanonicalVariantId: {CanonicalVariantId} | CorrelationId: {CorrelationId}",
                    tenantId, request.ProductId, request.VariantId.Value, variant.VariantId, correlationId);
                throw new VariantNotFoundException(request.ProductId, request.VariantId.Value);
            }

            variantPurchasability = productPurchasability;
        }

        // Step 3: Get or create cart
        var cart = await GetCartFromRedisAsync(tenantId, owner, cacheKey, correlationId);
        var isNewCart = cart == null;
        cart ??= await CreateEmptyCartAsync(tenantId, owner, cancellationToken);

        // Phase 3.2: Validate and fix existing cart
        if (!isNewCart)
        {
            var (validatedCart, _) = await ValidateAndFixCartAsync(
                cart, tenantId, owner, cacheKey, correlationId, cancellationToken);
            cart = validatedCart;
        }

        // PHASE 10.4.X: Currency Authority Lockdown
        // CreateEmptyCart just read the current tenant SQL currency for this
        // new cart. Repeating that same query in this mutation acquires another
        // connection while the owner lock is held. Existing Redis carts still
        // require a fresh SQL currency check to catch drift.
        var storefrontCurrency = isNewCart
            ? cart.Currency
            : await GetStorefrontCurrencyAsync(tenantId, cancellationToken);
        
        // Validate cart currency matches storefront
        if (!string.Equals(cart.Currency, storefrontCurrency, StringComparison.OrdinalIgnoreCase))
        {
            _reliabilityTelemetry.RecordCurrencyProjectionMismatch(
                "cart",
                cart.Items.Count == 0 ? "cart_currency_repaired" : "cart_currency_drift");

            if (cart.Items.Count == 0)
            {
                _logger.LogInformation(
                    "Updating cart currency to match storefront | TenantId: {TenantId} | OldCurrency: {OldCurrency} | NewCurrency: {NewCurrency} | CorrelationId: {CorrelationId}",
                    tenantId, cart.Currency, storefrontCurrency, correlationId);
                cart.Currency = storefrontCurrency;
            }
            else
            {
                _logger.LogError(
                    "Currency drift detected - cart has items with wrong currency | TenantId: {TenantId} | CartCurrency: {CartCurrency} | StorefrontCurrency: {StorefrontCurrency} | ItemCount: {ItemCount} | CorrelationId: {CorrelationId}",
                    tenantId, cart.Currency, storefrontCurrency, cart.Items.Count, correlationId);
                throw new CurrencyDriftException(cart.Currency, storefrontCurrency);
            }
        }

        // =============================================================================
        // PHASE 11.4: VARIANT-BASED CART ITEM MANAGEMENT
        // =============================================================================
        
        // Step 4: Add or update item
        // Canonical lines are keyed by VariantId. ProductId is only a migration
        // fallback for Redis lines written before simple-product SKU persistence.
        var variantId = variant?.VariantId;
        var existingItem = cart.Items.FirstOrDefault(i => i.VariantId == variantId);
        if (existingItem == null && !product.HasVariants && variantId.HasValue)
        {
            existingItem = cart.Items.FirstOrDefault(i =>
                i.ProductId == request.ProductId && !i.VariantId.HasValue);
            if (existingItem != null)
            {
                existingItem.VariantId = variantId;
            }
        }
        
        int previousQuantity = existingItem?.Quantity ?? 0;
        if (variant != null && variantPurchasability != null)
        {
            var requestedTotalQuantity = previousQuantity + request.Quantity;
            // Apply the same capping logic that will be used when persisting
            var finalQuantity = Math.Min(requestedTotalQuantity, MaxQuantityPerItem);
            
            if (finalQuantity > variantPurchasability.StockQuantity)
            {
                _logger.LogWarning(
                    "AddItem rejected - requested quantity exceeds authoritative SQL stock | TenantId: {TenantId} | ProductId: {ProductId} | VariantId: {VariantId} | PreviousQuantity: {PreviousQuantity} | RequestedQuantity: {RequestedQuantity} | FinalQuantity: {FinalQuantity} | AvailableStock: {AvailableStock} | CorrelationId: {CorrelationId}",
                    tenantId,
                    request.ProductId,
                    variant.VariantId,
                    previousQuantity,
                    request.Quantity,
                    finalQuantity,
                    variantPurchasability.StockQuantity,
                    correlationId);
                throw new VariantInsufficientStockException(
                    variant.VariantId,
                    variant.SKU ?? "Unknown",
                    finalQuantity,
                    variantPurchasability.StockQuantity);
            }
        }

        if (existingItem != null)
        {
            // Increment quantity (cap at max) - idempotent
            existingItem.Quantity = Math.Min(existingItem.Quantity + request.Quantity, MaxQuantityPerItem);
            existingItem.ThumbnailImage = ProductMediaImageResolver.Resolve(product, variantId);
            ApplyAffiliateCartLineAdmission(existingItem, lineAdmission);
        }
        else
        {
            // =============================================================================
            // PHASE 11.4: VARIANT PRICING
            // =============================================================================
            // NEVER USE Product.Price - Variant is the source of truth
            decimal effectivePrice;
            string sku;
            string? imageUrl;
            List<CartVariantAttribute> variantAttributes;
            
            if (variant != null)
            {
                effectivePrice = variantPurchasability?.Price ?? ProductStorefrontVisibilityAuthority.EvaluateVariant(product, variant).Price;
                sku = variant.SKU ?? product.SKU ?? string.Empty;
                imageUrl = ProductMediaImageResolver.Resolve(product, variant.VariantId);
                variantAttributes = variant.Attributes
                    .Select(a => new CartVariantAttribute { Name = a.Name, Value = a.Value })
                    .ToList();
            }
            else
            {
                effectivePrice = productPurchasability.Price;
                sku = product.SKU ?? string.Empty;
                imageUrl = ProductMediaImageResolver.Resolve(product);
                variantAttributes = new List<CartVariantAttribute>();
            }
            
            // Add new item with variant snapshot data
            // PHASE: Promotions Engine - Calculate discounted price if promotion is active
            var discountedUnitPrice = product.HasActivePromotion
                ? StorefrontPromotionPriceComposer.TryCompose(
                    effectivePrice,
                    product.PromotionDiscountType,
                    product.PromotionDiscountValue,
                    product.PromotionMaxDiscountAmount)
                : null;
            var hasValidPromotion = discountedUnitPrice.HasValue;
            
            cart.Items.Add(new CartItemDto
            {
                ProductId = product.ProductId,
                VariantId = variantId,
                SKU = sku,
                Name = product.Name,
                NameAr = product.NameAr,
                Quantity = Math.Min(request.Quantity, MaxQuantityPerItem),
                UnitPrice = effectivePrice, // PHASE 11.4: Variant price (NEVER Product.Price)
                ThumbnailImage = imageUrl,
                VariantAttributes = variantAttributes,
                AddedAtUtc = DateTime.UtcNow,
                // Use variant weight if available, otherwise fall back to product weight
                Weight = variant?.Weight ?? product.Weight,
                AvailableStock = variantPurchasability?.StockQuantity ?? productPurchasability.StockQuantity,
                // PHASE: Promotions Engine - Cart item promotion fields
                HasActivePromotion = hasValidPromotion,
                PromotionBadge = hasValidPromotion ? product.PromotionBadge : null,
                PromotionDiscountType = hasValidPromotion ? product.PromotionDiscountType : null,
                PromotionDiscountValue = hasValidPromotion ? product.PromotionDiscountValue : null,
                DiscountedUnitPrice = discountedUnitPrice,
                IsAffiliateEligibleLine = lineAdmission.IsEligible,
                AffiliateAccountId = lineAdmission.AffiliateAccountId,
                AffiliateReferralCode = lineAdmission.ReferralCode,
                PromotionLinkId = lineAdmission.PromotionLinkId
            });
            
            _logger.LogDebug(
                "Cart item added | ProductId: {ProductId} | VariantId: {VariantId} | SKU: {SKU} | UnitPrice: {UnitPrice} | HasPromotion: {HasPromotion} | CorrelationId: {CorrelationId}",
                product.ProductId, variantId, sku, effectivePrice, product.HasActivePromotion, correlationId);
        }

        // Phase 3.2: Validate cart limits BEFORE saving
        ValidateCartLimits(cart, tenantId, correlationId);

        // Step 5: Recalculate totals
        cart.RecalculateTotals();
        cart.ExpiresAtUtc = DateTime.UtcNow.AddHours(CartTtlHours);

        // Step 6: Save to Redis
        await SaveCartToRedisAsync(cart, cacheKey, correlationId, cancellationToken);

        // Step 7: Emit events
        if (isNewCart)
        {
            await EmitCartCreatedEventAsync(cart, correlationId, cancellationToken);
        }

        // PHASE 11.4: Get the actual cart item to emit correct SKU/Price (not product-level values)
        var addedCartItem = cart.Items.FirstOrDefault(i => i.VariantId == variantId);
        
        if (addedCartItem != null)
        {
            await EmitCartItemAddedEventAsync(cart, addedCartItem, request.Quantity, correlationId, cancellationToken);
        }

        _logger.LogInformation(
            "AddItem success | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | VariantId: {VariantId} | Quantity: {Quantity} | NewTotal: {NewTotal} | ItemCount: {ItemCount} | TotalQuantity: {TotalQuantity} | CorrelationId: {CorrelationId}",
            tenantId, cart.CartId, request.ProductId, variantId, request.Quantity, addedCartItem?.Quantity ?? 0, cart.Items.Count, cart.ItemCount, correlationId);

        return cart;
    }

    #endregion

    #region Update Item

    /// <inheritdoc />
    public async Task<CartDto> UpdateItemAsync(
        Guid tenantId,
        CartOwner owner,
        Guid productId,
        UpdateCartItemRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureOwnerTenantMatchAsync(tenantId, owner, correlationId, cancellationToken);

        return await ExecuteWithCartMutationLockAsync(
            tenantId,
            owner,
            correlationId,
            cancellationToken,
            async ct =>
            {

        var cacheKey = BuildCacheKey(tenantId, owner);

        _logger.LogDebug(
            "UpdateItem starting | TenantId: {TenantId} | ProductId: {ProductId} | NewQuantity: {NewQuantity} | CorrelationId: {CorrelationId}",
            tenantId, productId, request.Quantity, correlationId);

        // Step 1: Get cart
        var cart = await GetCartFromRedisAsync(tenantId, owner, cacheKey, correlationId);
        if (cart == null)
        {
            // No cart exists - return empty (expired/missing = empty)
            _logger.LogWarning(
                "UpdateItem - cart not found | TenantId: {TenantId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                tenantId, productId, correlationId);
            return await CreateEmptyCartAsync(tenantId, owner, ct);
        }

        // Phase 3.2: Validate and fix cart
        var (validatedCart, _) = await ValidateAndFixCartAsync(
            cart, tenantId, owner, cacheKey, correlationId, ct);
        cart = validatedCart;

        // Step 2: Find item
        // AUDIT FIX CK-2: Prefer VariantId-bearing item to avoid ambiguity when
        // multiple variants of the same product exist in the cart.
        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == productId && i.VariantId != null)
            ?? cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem == null)
        {
            _logger.LogWarning(
                "UpdateItem - item not found | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                tenantId, cart.CartId, productId, correlationId);
            return cart;
        }

        int previousQuantity = existingItem.Quantity;

        // Step 3: Update or remove
        if (request.Quantity == 0)
        {
            // Remove item
            cart.Items.Remove(existingItem);
            
            // Emit removed event
            var variantName = FormatVariantName(existingItem.VariantAttributes);
            await EmitCartItemRemovedEventAsync(cart, productId, existingItem.VariantId, variantName, previousQuantity, correlationId, ct);
            
            _logger.LogInformation(
                "UpdateItem - removed | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | ItemCount: {ItemCount} | CorrelationId: {CorrelationId}",
                tenantId, cart.CartId, productId, cart.Items.Count, correlationId);
        }
        else
        {
            // Update quantity
            existingItem.Quantity = Math.Min(request.Quantity, MaxQuantityPerItem);

            // Phase 3.2: Validate cart limits
            ValidateCartLimits(cart, tenantId, correlationId);
            
            // Emit updated event
            var variantName = FormatVariantName(existingItem.VariantAttributes);
            await EmitCartItemUpdatedEventAsync(cart, productId, existingItem.VariantId, variantName, previousQuantity, existingItem.Quantity, correlationId, ct);
            
            _logger.LogInformation(
                "UpdateItem success | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | PrevQty: {PrevQuantity} | NewQty: {NewQuantity} | TotalQuantity: {TotalQuantity} | CorrelationId: {CorrelationId}",
                tenantId, cart.CartId, productId, previousQuantity, existingItem.Quantity, cart.ItemCount, correlationId);
        }

        // Step 4: Recalculate and save
        cart.RecalculateTotals();
        cart.ExpiresAtUtc = DateTime.UtcNow.AddHours(CartTtlHours);
        await SaveCartToRedisAsync(cart, cacheKey, correlationId, ct);

        return cart;
            });
    }

    /// <summary>
    /// PHASE 11.7: Variant-Centric Update Item
    /// Updates the quantity of an item located ONLY by VariantId.
    /// Validates stock availability before update.
    /// </summary>
    /// <inheritdoc />
    public async Task<CartDto> UpdateItemByVariantAsync(
        Guid tenantId,
        CartOwner owner,
        Guid variantId,
        UpdateCartItemRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureOwnerTenantMatchAsync(tenantId, owner, correlationId, cancellationToken);

        return await ExecuteWithCartMutationLockAsync(
            tenantId,
            owner,
            correlationId,
            cancellationToken,
            async ct =>
            {

        var cacheKey = BuildCacheKey(tenantId, owner);

        _logger.LogDebug(
            "[PHASE 11.7] UpdateItemByVariant starting | TenantId: {TenantId} | VariantId: {VariantId} | NewQuantity: {NewQuantity} | CorrelationId: {CorrelationId}",
            tenantId, variantId, request.Quantity, correlationId);

        // Step 1: Get cart
        var cart = await GetCartFromRedisAsync(tenantId, owner, cacheKey, correlationId);
        if (cart == null)
        {
            _logger.LogWarning(
                "[PHASE 11.7] UpdateItemByVariant - cart not found | TenantId: {TenantId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                tenantId, variantId, correlationId);
            throw new CartItemNotFoundException(variantId);
        }

        // Phase 3.2: Validate and fix cart
        var (validatedCart, _) = await ValidateAndFixCartAsync(
            cart, tenantId, owner, cacheKey, correlationId, ct);
        cart = validatedCart;

        // Step 2: Find item by VariantId ONLY (no ProductId fallback!)
        var existingItem = cart.Items.FirstOrDefault(i => i.VariantId == variantId);
        if (existingItem == null)
        {
            _logger.LogWarning(
                "[PHASE 11.7] UpdateItemByVariant - item not found by VariantId | TenantId: {TenantId} | CartId: {CartId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                tenantId, cart.CartId, variantId, correlationId);
            throw new CartItemNotFoundException(variantId);
        }

        int previousQuantity = existingItem.Quantity;

        // Step 3: Handle removal case (quantity = 0)
        if (request.Quantity == 0)
        {
            cart.Items.Remove(existingItem);
            
            // Emit removed event
            var variantName = FormatVariantName(existingItem.VariantAttributes);
            await EmitCartItemRemovedEventAsync(cart, existingItem.ProductId, existingItem.VariantId, variantName, previousQuantity, correlationId, ct);
            
            _logger.LogInformation(
                "[PHASE 11.7] UpdateItemByVariant - removed | TenantId: {TenantId} | CartId: {CartId} | VariantId: {VariantId} | ItemCount: {ItemCount} | CorrelationId: {CorrelationId}",
                tenantId, cart.CartId, variantId, cart.Items.Count, correlationId);
        }
        else
        {
            // Step 4: Validate purchasability from the canonical storefront authority.
            var projectedProduct = await _productRepository.FindByProductIdAsync(tenantId, existingItem.ProductId, ct);
            var product = projectedProduct == null
                ? null
                : await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                    _dbContext,
                    projectedProduct,
                    tenantId,
                    ct);
            var variant = product?.Variants?.FirstOrDefault(v => v.VariantId == variantId);
            
            // CRITICAL: Fail closed if current storefront authority is unavailable
            if (product == null || variant == null)
            {
                _logger.LogWarning(
                    "UpdateItemByVariant rejected - product/variant authority unavailable (fail closed) | TenantId: {TenantId} | CartId: {CartId} | VariantId: {VariantId} | RequestedQuantity: {RequestedQuantity} | ExistingQuantity: {ExistingQuantity} | ProductFound: {ProductFound} | VariantFound: {VariantFound} | CorrelationId: {CorrelationId}",
                    tenantId,
                    cart.CartId,
                    variantId,
                    request.Quantity,
                    existingItem.Quantity,
                    product != null,
                    variant != null,
                    correlationId);
                
                // Fail closed: don't allow quantity increases when stock can't be resolved
                // Only allow decreases or keeping the same quantity
                if (request.Quantity > existingItem.Quantity)
                {
                    throw new VariantInsufficientStockException(
                        variantId,
                        existingItem.SKU,
                        request.Quantity,
                        existingItem.Quantity); // Treat existing quantity as the cap
                }
            }
            else
            {
                StorefrontPurchasabilityResult variantPurchasability;
                if (product.HasVariants)
                {
                    variantPurchasability = ProductStorefrontVisibilityAuthority.EvaluateVariant(product, variant);
                    if (!variantPurchasability.IsAvailable)
                    {
                        throw new VariantNotFoundException(existingItem.ProductId, variantId);
                    }
                }
                else
                {
                    var canonicalVariant = ResolveCanonicalSimpleProductVariant(product);
                    if (canonicalVariant == null || canonicalVariant.VariantId != variantId)
                    {
                        throw new VariantNotFoundException(existingItem.ProductId, variantId);
                    }

                    variantPurchasability = ProductStorefrontVisibilityAuthority.Evaluate(product);
                    if (!variantPurchasability.IsAvailable)
                    {
                        throw new VariantNotFoundException(existingItem.ProductId, variantId);
                    }
                }

                var availableStock = variantPurchasability.StockQuantity;

                // Apply the same capping logic that will be used when persisting
                var finalQuantity = Math.Min(request.Quantity, MaxQuantityPerItem);
                
                if (finalQuantity > availableStock)
                {
                    _logger.LogWarning(
                        "Stock exceeded | Tenant={TenantId} Variant={VariantId} Requested={RequestedQty} FinalQuantity={FinalQty} Available={AvailableQty} | CorrelationId: {CorrelationId}",
                        tenantId, variantId, request.Quantity, finalQuantity, availableStock, correlationId);
                    throw new VariantInsufficientStockException(
                        variantId,
                        variant.SKU ?? existingItem.SKU,
                        finalQuantity,
                        availableStock);
                }
            }

            // Step 5: Update quantity (apply max limit)
            existingItem.Quantity = Math.Min(request.Quantity, MaxQuantityPerItem);

            // Phase 3.2: Validate cart limits
            ValidateCartLimits(cart, tenantId, correlationId);
            
            // Emit updated event
            var variantName = FormatVariantName(existingItem.VariantAttributes);
            await EmitCartItemUpdatedEventAsync(cart, existingItem.ProductId, existingItem.VariantId, variantName, previousQuantity, existingItem.Quantity, correlationId, ct);
            
            _logger.LogInformation(
                "[PHASE 11.7] UpdateItemByVariant success | TenantId: {TenantId} | CartId: {CartId} | VariantId: {VariantId} | PrevQty: {PrevQuantity} | NewQty: {NewQuantity} | CorrelationId: {CorrelationId}",
                tenantId, cart.CartId, variantId, previousQuantity, existingItem.Quantity, correlationId);
        }

        // Step 6: Recalculate and save
        cart.RecalculateTotals();
        cart.ExpiresAtUtc = DateTime.UtcNow.AddHours(CartTtlHours);
        await SaveCartToRedisAsync(cart, cacheKey, correlationId, ct);

        return cart;
            });
    }

    #endregion

    #region Remove Item

    /// <inheritdoc />
    public async Task<CartDto> RemoveItemAsync(
        Guid tenantId,
        CartOwner owner,
        Guid productId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureOwnerTenantMatchAsync(tenantId, owner, correlationId, cancellationToken);

        return await ExecuteWithCartMutationLockAsync(
            tenantId,
            owner,
            correlationId,
            cancellationToken,
            async ct =>
            {

        var cacheKey = BuildCacheKey(tenantId, owner);

        _logger.LogDebug(
            "RemoveItem starting | TenantId: {TenantId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
            tenantId, productId, correlationId);

        // Step 1: Get cart
        var cart = await GetCartFromRedisAsync(tenantId, owner, cacheKey, correlationId);
        if (cart == null)
        {
            _logger.LogWarning(
                "RemoveItem - cart not found | TenantId: {TenantId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                tenantId, productId, correlationId);
            return await CreateEmptyCartAsync(tenantId, owner, ct);
        }

        // Phase 3.2: Validate and fix cart
        var (validatedCart, _) = await ValidateAndFixCartAsync(
            cart, tenantId, owner, cacheKey, correlationId, ct);
        cart = validatedCart;

        // Step 2: Find and remove item
        // AUDIT FIX CK-2: Prefer VariantId-bearing item to avoid ambiguity
        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == productId && i.VariantId != null)
            ?? cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem == null)
        {
            _logger.LogWarning(
                "RemoveItem - item not found | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                tenantId, cart.CartId, productId, correlationId);
            return cart;
        }

        int quantityRemoved = existingItem.Quantity;
        cart.Items.Remove(existingItem);

        // Step 3: Recalculate and save
        cart.RecalculateTotals();
        cart.ExpiresAtUtc = DateTime.UtcNow.AddHours(CartTtlHours);
        await SaveCartToRedisAsync(cart, cacheKey, correlationId, ct);

        // Step 4: Emit event
        var variantName = FormatVariantName(existingItem.VariantAttributes);
        await EmitCartItemRemovedEventAsync(cart, productId, existingItem.VariantId, variantName, quantityRemoved, correlationId, ct);

        _logger.LogInformation(
            "RemoveItem success | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | QuantityRemoved: {QuantityRemoved} | ItemCount: {ItemCount} | CorrelationId: {CorrelationId}",
            tenantId, cart.CartId, productId, quantityRemoved, cart.Items.Count, correlationId);

        return cart;
            });
    }

    /// <summary>
    /// PHASE 11.7: Variant-Centric Remove Item
    /// Removes an item from the cart located ONLY by VariantId.
    /// </summary>
    /// <inheritdoc />
    public async Task<CartDto> RemoveItemByVariantAsync(
        Guid tenantId,
        CartOwner owner,
        Guid variantId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureOwnerTenantMatchAsync(tenantId, owner, correlationId, cancellationToken);

        return await ExecuteWithCartMutationLockAsync(
            tenantId,
            owner,
            correlationId,
            cancellationToken,
            async ct =>
            {

        var cacheKey = BuildCacheKey(tenantId, owner);

        _logger.LogDebug(
            "[PHASE 11.7] RemoveItemByVariant starting | TenantId: {TenantId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
            tenantId, variantId, correlationId);

        // Step 1: Get cart
        var cart = await GetCartFromRedisAsync(tenantId, owner, cacheKey, correlationId);
        if (cart == null)
        {
            _logger.LogWarning(
                "[PHASE 11.7] RemoveItemByVariant - cart not found | TenantId: {TenantId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                tenantId, variantId, correlationId);
            throw new CartItemNotFoundException(variantId);
        }

        // Phase 3.2: Validate and fix cart
        var (validatedCart, _) = await ValidateAndFixCartAsync(
            cart, tenantId, owner, cacheKey, correlationId, ct);
        cart = validatedCart;

        // Step 2: Find and remove item by VariantId ONLY (no ProductId fallback!)
        var existingItem = cart.Items.FirstOrDefault(i => i.VariantId == variantId);
        if (existingItem == null)
        {
            _logger.LogWarning(
                "[PHASE 11.7] RemoveItemByVariant - item not found | TenantId: {TenantId} | CartId: {CartId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                tenantId, cart.CartId, variantId, correlationId);
            throw new CartItemNotFoundException(variantId);
        }

        int quantityRemoved = existingItem.Quantity;
        var productId = existingItem.ProductId;
        cart.Items.Remove(existingItem);

        // Step 3: Recalculate and save
        cart.RecalculateTotals();
        cart.ExpiresAtUtc = DateTime.UtcNow.AddHours(CartTtlHours);
        await SaveCartToRedisAsync(cart, cacheKey, correlationId, ct);

        // Step 4: Emit event
        var variantName = FormatVariantName(existingItem.VariantAttributes);
        await EmitCartItemRemovedEventAsync(cart, productId, existingItem.VariantId, variantName, quantityRemoved, correlationId, ct);

        _logger.LogInformation(
            "[PHASE 11.7] RemoveItemByVariant success | TenantId: {TenantId} | CartId: {CartId} | VariantId: {VariantId} | QuantityRemoved: {QuantityRemoved} | ItemCount: {ItemCount} | CorrelationId: {CorrelationId}",
            tenantId, cart.CartId, variantId, quantityRemoved, cart.Items.Count, correlationId);

        return cart;
            });
    }

    #endregion

    #region Clear Cart

    /// <inheritdoc />
    public async Task<CartDto> ClearCartAsync(
        Guid tenantId,
        CartOwner owner,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureOwnerTenantMatchAsync(tenantId, owner, correlationId, cancellationToken);

        return await ExecuteWithCartMutationLockAsync(
            tenantId,
            owner,
            correlationId,
            cancellationToken,
            async ct =>
            {

        var cacheKey = BuildCacheKey(tenantId, owner);

        _logger.LogDebug(
            "ClearCart starting | TenantId: {TenantId} | OwnerType: {OwnerType} | OwnerId: {OwnerId} | CorrelationId: {CorrelationId}",
            tenantId, owner.OwnerType, owner.OwnerId, correlationId);

        // Step 1: Get cart for event data
        var cart = await GetCartFromRedisAsync(tenantId, owner, cacheKey, correlationId);
        if (cart == null || cart.Items.Count == 0)
        {
            _logger.LogInformation(
                "ClearCart - cart already empty | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId, correlationId);
            return await CreateEmptyCartAsync(tenantId, owner, ct);
        }

        int itemsCleared = cart.Items.Count;
        decimal totalValueCleared = cart.GrandTotal;
        var cartId = cart.CartId;

        // Step 2: Delete from Redis
        await DeleteCartFromRedisAsync(cacheKey, correlationId, ct);

        // Step 3: Emit event
        await EmitCartClearedEventAsync(tenantId, cartId, owner, itemsCleared, totalValueCleared, correlationId, ct);

        _logger.LogInformation(
            "ClearCart success | TenantId: {TenantId} | CartId: {CartId} | ItemsCleared: {ItemsCleared} | ValueCleared: {ValueCleared} | CorrelationId: {CorrelationId}",
            tenantId, cartId, itemsCleared, totalValueCleared, correlationId);

        return await CreateEmptyCartAsync(tenantId, owner, ct);
            });
    }

    #endregion

    #region Apply Draft

    /// <inheritdoc />
    public async Task<CartDto> ApplyDraftAsync(
        Guid tenantId,
        CartOwner owner,
        ApplyOrderDraftRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureOwnerTenantMatchAsync(tenantId, owner, correlationId, cancellationToken);

        return await ExecuteWithCartMutationLockAsync(
            tenantId,
            owner,
            correlationId,
            cancellationToken,
            async ct =>
            {

        _logger.LogInformation(
            "ApplyDraft starting | TenantId: {TenantId} | ItemCount: {ItemCount} | ShippingMethodId: {ShippingMethodId} | CorrelationId: {CorrelationId}",
            tenantId, request.Items.Count, request.ShippingMethodId, correlationId);

        CartDto cart = null!;

        // 1. Add all items
        foreach (var item in request.Items)
        {
                try
                {
                    // We reuse AddItemInternalAsync to ensure all validation rules (stock, variants, currency) are applied
                    // Using internal method because we already validated owner at the start of ApplyDraftAsync
                    cart = await AddItemInternalAsync(
                        tenantId, 
                        owner, 
                        new AddCartItemRequest 
                        { 
                            ProductId = item.ProductId, 
                            VariantId = item.VariantId, 
                            Quantity = item.Quantity 
                        }, 
                        null,
                        null,
                        null,
                        correlationId, 
                        ct);
                }
                catch (VariantRequiredException)
                {
                    // AI didn't provide a variant, but the product requires one.
                    // Instead of failing, we attempt to find a default/first available variant.
                    _logger.LogInformation(
                        "ApplyDraft - variant missing for {ProductId}, attempting to auto-resolve default variant | CorrelationId: {CorrelationId}",
                        item.ProductId, correlationId);
                    
                    var projectedProduct = await _productRepository.FindByProductIdAsync(tenantId, item.ProductId, ct);
                    var product = projectedProduct == null
                        ? null
                        : await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                            _dbContext,
                            projectedProduct,
                            tenantId,
                            ct);
                    if (product != null && product.Variants != null && product.Variants.Count > 0)
                    {
                        var defaultVariant = product.Variants.FirstOrDefault(v => v.IsDefault && v.IsActive) 
                                           ?? product.Variants.FirstOrDefault(v => v.IsActive);
                                           
                        if (defaultVariant != null)
                        {
                             // Using internal method because we already validated owner at the start of ApplyDraftAsync
                             cart = await AddItemInternalAsync(
                                tenantId, 
                                owner, 
                                new AddCartItemRequest 
                                { 
                                    ProductId = item.ProductId, 
                                    VariantId = defaultVariant.VariantId, 
                                    Quantity = item.Quantity 
                                }, 
                                null,
                                null,
                                null,
                                correlationId, 
                                ct);
                                
                             _logger.LogInformation(
                                "ApplyDraft - auto-resolved to variant {VariantId} for product {ProductId} | CorrelationId: {CorrelationId}",
                                defaultVariant.VariantId, item.ProductId, correlationId);
                             continue; // Success
                        }
                    }
                    
                    // If we still can't find a variant, we must fail
                    throw;
                }
                catch (Exception ex)
                {
                    // CRITICAL CHANGE: Re-throw exception so the frontend knows the operation failed.
                    // This prevents redirecting to checkout with an empty cart if the product is invalid.
                    _logger.LogError(ex, 
                        "ApplyDraft - failed to add item | ProductId: {ProductId} | VariantId: {VariantId}", 
                        item.ProductId, item.VariantId);
                    throw;
                }
        }

        // If no items were added successfully, get the current cart to return
        if (cart == null)
        {
            cart = await GetCartAsync(tenantId, owner, correlationId, ct);
        }

        // 2. Apply shipping method if provided
        if (request.ShippingMethodId.HasValue && cart.Items.Count > 0)
        {
            // Note: In a real implementation we would validate the shipping method ID here
            // For now we assume the ID is valid as it came from the AI context which sees valid methods
            // We store it in the cart (assuming the CartDto schema supports it, otherwise we'd need to extend it)
            // Since CartDto doesn't strictly have ShippingMethodId (it's in Checkout), we might need to skip this
            // or emit an event. For now, we'll log it as "Selected".
            _logger.LogInformation("ApplyDraft - Shipping method selected: {ShippingMethodId}", request.ShippingMethodId);
        }

        return cart;
            });
    }

    #endregion

    #region Cart Merge (Phase 3.3)

    /// <inheritdoc />
    public async Task<CartDto> MergeCartAsync(
        Guid tenantId,
        CartOwner sessionOwner,
        CartOwner customerOwner,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureOwnerTenantMatchAsync(tenantId, sessionOwner, correlationId, cancellationToken);
        await EnsureOwnerTenantMatchAsync(tenantId, customerOwner, correlationId, cancellationToken);

        return await ExecuteWithCartMergeLocksAsync(
            tenantId,
            sessionOwner,
            customerOwner,
            correlationId,
            cancellationToken,
            async ct =>
            {

        var sessionCacheKey = BuildCacheKey(tenantId, sessionOwner);
        var customerCacheKey = BuildCacheKey(tenantId, customerOwner);

        _logger.LogDebug(
            "MergeCart starting | TenantId: {TenantId} | SessionId: {SessionId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
            tenantId, sessionOwner.OwnerId, customerOwner.OwnerId, correlationId);

        // Step 1: Get guest (session) cart
        var guestCart = await GetCartFromRedisAsync(tenantId, sessionOwner, sessionCacheKey, correlationId);
        
        if (guestCart == null || guestCart.Items.Count == 0)
        {
            // No guest cart or empty - nothing to merge (idempotent)
            _logger.LogInformation(
                "MergeCart - no guest cart to merge | TenantId: {TenantId} | SessionId: {SessionId} | CorrelationId: {CorrelationId}",
                tenantId, sessionOwner.OwnerId, correlationId);
            
            // Return customer cart (or empty if none)
            var existingCustomerCart = await GetCartFromRedisAsync(tenantId, customerOwner, customerCacheKey, correlationId);
            return existingCustomerCart ?? await CreateEmptyCartAsync(tenantId, customerOwner, ct);
        }

        // Step 2: Get or create customer cart
        var customerCart = await GetCartFromRedisAsync(tenantId, customerOwner, customerCacheKey, correlationId);
        var isNewCustomerCart = customerCart == null;
        customerCart ??= await CreateEmptyCartAsync(tenantId, customerOwner, ct);

        // Step 3: Currency check - abort if mismatch
        if (customerCart.Items.Count > 0 && 
            !string.Equals(guestCart.Currency, customerCart.Currency, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "MergeCart ABORTED - currency mismatch | TenantId: {TenantId} | GuestCurrency: {GuestCurrency} | CustomerCurrency: {CustomerCurrency} | CorrelationId: {CorrelationId}",
                tenantId, guestCart.Currency, customerCart.Currency, correlationId);

            // Delete guest cart (as specified)
            await DeleteGuestCartSafelyAsync(sessionCacheKey, correlationId);

            // Emit merge event with currency mismatch flag
            await EmitCartMergedEventAsync(
                tenantId, guestCart, customerCart, 0, 0, 0, true, correlationId, ct);

            // Emit cleared event for discarded guest cart
            await EmitCartClearedEventAsync(
                tenantId, guestCart.CartId, sessionOwner, guestCart.Items.Count, guestCart.GrandTotal, correlationId, ct);

            return customerCart;
        }

        // Step 4: Merge items
        int newItemsAdded = 0;
        int itemsUpdated = 0;
        var guestCartId = guestCart.CartId;
        var guestItemCount = guestCart.Items.Count;

        foreach (var guestItem in guestCart.Items)
        {
            // PHASE 11.3: Variant-aware merge - match by VariantId when present, fallback to ProductId/SKU
            CartItemDto? existingItem = null;
            if (guestItem.VariantId.HasValue)
            {
                existingItem = customerCart.Items.FirstOrDefault(i => i.VariantId == guestItem.VariantId);
            }
            else
            {
                // Legacy fallback: match by ProductId (or SKU if ProductId match has no VariantId)
                existingItem = customerCart.Items.FirstOrDefault(i => 
                    i.ProductId == guestItem.ProductId && !i.VariantId.HasValue);
            }

            if (existingItem != null)
            {
                // Item exists - sum quantities (cap at MaxQuantityPerItem)
                int previousQuantity = existingItem.Quantity;
                existingItem.Quantity = Math.Min(existingItem.Quantity + guestItem.Quantity, MaxQuantityPerItem);
                
                if (existingItem.Quantity != previousQuantity)
                {
                    itemsUpdated++;
                    
                    // Emit update event
                    var variantName = FormatVariantName(existingItem.VariantAttributes);
                    await EmitCartItemUpdatedEventAsync(
                        customerCart, guestItem.ProductId, existingItem.VariantId, variantName, previousQuantity, existingItem.Quantity, correlationId, ct);
                }

                if (!existingItem.IsAffiliateEligibleLine && guestItem.IsAffiliateEligibleLine)
                {
                    existingItem.IsAffiliateEligibleLine = true;
                    existingItem.AffiliateAccountId = guestItem.AffiliateAccountId;
                    existingItem.AffiliateReferralCode = guestItem.AffiliateReferralCode;
                    existingItem.PromotionLinkId = guestItem.PromotionLinkId;
                    itemsUpdated++;
                }
            }
            else
            {
                // Item not in customer cart - add it with full variant snapshot
                var newItem = new CartItemDto
                {
                    ProductId = guestItem.ProductId,
                    VariantId = guestItem.VariantId, // Preserve variant ID
                    SKU = guestItem.SKU,
                    Name = guestItem.Name,
                    NameAr = guestItem.NameAr,
                    Quantity = Math.Min(guestItem.Quantity, MaxQuantityPerItem),
                    UnitPrice = guestItem.UnitPrice, // Keep snapshot price
                    ThumbnailImage = guestItem.ThumbnailImage,
                    AddedAtUtc = guestItem.AddedAtUtc, // Preserve original add time
                    Weight = guestItem.Weight, // Preserve weight snapshot
                    VariantAttributes = guestItem.VariantAttributes, // Preserve variant attributes
                AvailableStock = guestItem.AvailableStock, // Preserve stock snapshot
                    IsAffiliateEligibleLine = guestItem.IsAffiliateEligibleLine,
                    AffiliateAccountId = guestItem.AffiliateAccountId,
                    AffiliateReferralCode = guestItem.AffiliateReferralCode,
                    PromotionLinkId = guestItem.PromotionLinkId,
                    // Preserve promotion snapshot
                    HasActivePromotion = guestItem.HasActivePromotion,
                    PromotionBadge = guestItem.PromotionBadge,
                    PromotionDiscountType = guestItem.PromotionDiscountType,
                    PromotionDiscountValue = guestItem.PromotionDiscountValue,
                    DiscountedUnitPrice = guestItem.DiscountedUnitPrice
                };

                // Check max items limit before adding
                if (customerCart.Items.Count < MaxItems)
                {
                    customerCart.Items.Add(newItem);
                    newItemsAdded++;

                    // Emit add event (using a simplified version since we don't have ProductDocument)
                    await EmitCartItemAddedFromMergeAsync(
                        customerCart, newItem, correlationId, ct);
                }
                else
                {
                    _logger.LogWarning(
                        "MergeCart - max items limit reached, skipping item | TenantId: {TenantId} | ProductId: {ProductId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                        tenantId, guestItem.ProductId, guestItem.VariantId, correlationId);
                }
            }
        }

        // Step 5: Enforce total quantity limit
        EnforceTotalQuantityLimit(customerCart, tenantId, correlationId);

        // Step 6: Validate and fix merged cart (reuse Phase 3.2 validation)
        var (validatedCart, validationAction) = await ValidateAndFixCartAsync(
            customerCart, tenantId, customerOwner, customerCacheKey, correlationId, ct);
        customerCart = validatedCart;

        // Step 7: Recalculate totals and save
        customerCart.RecalculateTotals();
        customerCart.ExpiresAtUtc = DateTime.UtcNow.AddHours(CartTtlHours);
        await SaveCartToRedisAsync(customerCart, customerCacheKey, correlationId, ct);

        // Step 8: Delete guest cart (MUST happen, but failure is warning only)
        await DeleteGuestCartSafelyAsync(sessionCacheKey, correlationId, ct);

        // Step 9: Emit events
        if (isNewCustomerCart && customerCart.Items.Count > 0)
        {
            await EmitCartCreatedEventAsync(customerCart, correlationId, ct);
        }

        await EmitCartMergedEventAsync(
            tenantId, guestCart, customerCart, guestItemCount, newItemsAdded, itemsUpdated, false, correlationId, ct);

        // Emit cleared event for the guest cart that was consumed
        await EmitCartClearedEventAsync(
            tenantId, guestCartId, sessionOwner, guestItemCount, guestCart.GrandTotal, correlationId, ct);

        _logger.LogInformation(
            "MergeCart success | TenantId: {TenantId} | SourceOwnerId: {SourceOwnerId} | TargetOwnerId: {TargetOwnerId} | ItemsMerged: {ItemsMerged} | NewItemsAdded: {NewItemsAdded} | ItemsUpdated: {ItemsUpdated} | ValidationAction: {ValidationAction} | CorrelationId: {CorrelationId}",
            tenantId, sessionOwner.OwnerId, customerOwner.OwnerId, guestItemCount, newItemsAdded, itemsUpdated, validationAction, correlationId);

        return customerCart;
            });
    }

    /// <summary>
    /// Enforces the total quantity limit across all items.
    /// If exceeded, reduces quantities proportionally.
    /// </summary>
    private void EnforceTotalQuantityLimit(CartDto cart, Guid tenantId, string correlationId)
    {
        var totalQuantity = cart.Items.Sum(i => i.Quantity);
        
        if (totalQuantity <= MaxTotalQuantity)
        {
            return;
        }

        _logger.LogWarning(
            "MergeCart - total quantity limit exceeded, capping | TenantId: {TenantId} | TotalQuantity: {TotalQuantity} | MaxAllowed: {MaxAllowed} | CorrelationId: {CorrelationId}",
            tenantId, totalQuantity, MaxTotalQuantity, correlationId);

        // Reduce proportionally
        var ratio = (decimal)MaxTotalQuantity / totalQuantity;
        foreach (var item in cart.Items)
        {
            item.Quantity = Math.Max(1, (int)(item.Quantity * ratio));
        }
    }

    /// <summary>
    /// Safely deletes the guest cart. Failure is logged as warning but doesn't fail the operation.
    /// </summary>
    private async Task DeleteGuestCartSafelyAsync(string cacheKey, string correlationId, CancellationToken cancellationToken = default)
    {
        try
        {
            await DeleteCartFromRedisAsync(cacheKey, correlationId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Failure to delete guest cart is a warning, not a failure
            _logger.LogWarning(ex,
                "MergeCart - failed to delete guest cart (non-fatal) | Key: {Key} | CorrelationId: {CorrelationId}",
                cacheKey, correlationId);
        }
    }

    /// <summary>
    /// Emits CartItemAddedEvent for items added during merge (simplified version without ProductDocument).
    /// </summary>
    private async Task EmitCartItemAddedFromMergeAsync(
        CartDto cart,
        CartItemDto item,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var variantName = FormatVariantName(item.VariantAttributes);
            
            var evt = new CartItemAddedEvent
            {
                TenantId = cart.TenantId,
                CartId = cart.CartId,
                OwnerType = cart.OwnerType,
                OwnerId = cart.OwnerId,
                ProductId = item.ProductId,
                VariantId = item.VariantId,
                VariantName = variantName,
                SKU = item.SKU,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                NewTotalQuantity = item.Quantity,
                CorrelationId = correlationId,
                IdempotencyKey = $"cart-merge-item-added:{cart.CartId}:{item.ProductId}:{item.VariantId}:{item.Quantity}:{correlationId}",
                OccurredAtUtc = DateTime.UtcNow
            };

            await PublishCartEventAsync(evt, cancellationToken);

            _logger.LogDebug(
                "CartItemAddedEvent (merge) emitted | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                cart.TenantId, cart.CartId, item.ProductId, item.VariantId, correlationId);
        }
        catch (OperationCanceledException)
        {
            // Preserve cancellation - rethrow to propagate cancellation signal
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to emit CartItemAddedEvent (merge) | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                cart.TenantId, cart.CartId, item.ProductId, correlationId);
        }
    }

    /// <summary>
    /// Emits CartMergedEvent after successful merge.
    /// </summary>
    private async Task EmitCartMergedEventAsync(
        Guid tenantId,
        CartDto sourceCart,
        CartDto targetCart,
        int mergedItemCount,
        int newItemsAdded,
        int itemsUpdated,
        bool wasCurrencyMismatch,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var evt = new CartMergedEvent
            {
                TenantId = tenantId,
                SourceCartId = sourceCart.CartId,
                SourceOwnerType = sourceCart.OwnerType,
                SourceOwnerId = sourceCart.OwnerId,
                TargetCartId = targetCart.CartId,
                TargetOwnerType = targetCart.OwnerType,
                TargetOwnerId = targetCart.OwnerId,
                MergedItemCount = mergedItemCount,
                NewItemsAdded = newItemsAdded,
                ItemsUpdated = itemsUpdated,
                WasCurrencyMismatch = wasCurrencyMismatch,
                CorrelationId = correlationId,
                IdempotencyKey = $"cart-merged:{sourceCart.CartId}:{targetCart.CartId}:{mergedItemCount}:{newItemsAdded}:{itemsUpdated}:{correlationId}",
                OccurredAtUtc = DateTime.UtcNow
            };

            await PublishCartEventAsync(evt, cancellationToken);

            _logger.LogDebug(
                "CartMergedEvent emitted | TenantId: {TenantId} | SourceCartId: {SourceCartId} | TargetCartId: {TargetCartId} | CorrelationId: {CorrelationId}",
                tenantId, sourceCart.CartId, targetCart.CartId, correlationId);
        }
        catch (OperationCanceledException)
        {
            // CRITICAL: Do not swallow cancellation - lock may have been lost
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to emit CartMergedEvent | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId, correlationId);
        }
    }

    #endregion

    #region Coupon Operations (Phase C2)

    /// <inheritdoc />
    public async Task UpdateCartWithCouponAsync(
        Guid tenantId,
        CartOwner owner,
        string? couponCode,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureOwnerTenantMatchAsync(tenantId, owner, correlationId, cancellationToken);

        await ExecuteWithCartMutationLockAsync(
            tenantId,
            owner,
            correlationId,
            cancellationToken,
            async ct =>
            {

        var cacheKey = BuildCacheKey(tenantId, owner);

        _logger.LogDebug(
            "UpdateCartWithCoupon | TenantId: {TenantId} | OwnerType: {OwnerType} | OwnerId: {OwnerId} | CouponCode: {CouponCode} | CorrelationId: {CorrelationId}",
            tenantId, owner.OwnerType, owner.OwnerId, couponCode ?? "(removing)", correlationId);

        // Get current cart
        var cart = await GetCartFromRedisAsync(tenantId, owner, cacheKey, correlationId);
        
        if (cart == null)
        {
            _logger.LogWarning(
                "UpdateCartWithCoupon - cart not found | TenantId: {TenantId} | OwnerType: {OwnerType} | OwnerId: {OwnerId} | CorrelationId: {CorrelationId}",
                tenantId, owner.OwnerType, owner.OwnerId, correlationId);
            throw new CartNotFoundException(tenantId, owner.OwnerId);
        }

        // Update coupon code only (discount is recalculated on every read)
        cart.AppliedCouponCode = couponCode;
        cart.LastUpdatedUtc = DateTime.UtcNow;
        cart.ExpiresAtUtc = DateTime.UtcNow.AddHours(CartTtlHours); // Reset TTL

        // Save to Redis
        await SaveCartToRedisAsync(cart, cacheKey, correlationId, ct);

        _logger.LogInformation(
            "UpdateCartWithCoupon success | TenantId: {TenantId} | CartId: {CartId} | CouponCode: {CouponCode} | CorrelationId: {CorrelationId}",
            tenantId, cart.CartId, couponCode ?? "(removed)", correlationId);
                return true;
            });
    }

    /// <inheritdoc />
    public async Task SaveCartAsync(
        CartDto cart,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (cart == null) throw new ArgumentNullException(nameof(cart));

        var owner = new CartOwner { OwnerType = cart.OwnerType, OwnerId = cart.OwnerId };
        await EnsureOwnerTenantMatchAsync(cart.TenantId, owner, correlationId, cancellationToken);

        await ExecuteWithCartMutationLockAsync(
            cart.TenantId,
            owner,
            correlationId,
            cancellationToken,
            async ct =>
            {
        var cacheKey = BuildCacheKey(cart.TenantId, owner);

        _logger.LogDebug(
            "[Coupon] SaveCartAsync | TenantId: {TenantId} | CartId: {CartId} | DiscountTotal: {DiscountTotal} | AppliedCouponCode: {CouponCode} | CorrelationId: {CorrelationId}",
            cart.TenantId, cart.CartId, cart.DiscountTotal, cart.AppliedCouponCode ?? "(none)", correlationId);

        // Update timestamps
        cart.LastUpdatedUtc = DateTime.UtcNow;
        cart.ExpiresAtUtc = DateTime.UtcNow.AddHours(CartTtlHours);

        // Save full cart to Redis
        await SaveCartToRedisAsync(cart, cacheKey, correlationId, ct);

        _logger.LogInformation(
            "[Coupon] SaveCartAsync success | TenantId: {TenantId} | CartId: {CartId} | DiscountTotal: {DiscountTotal} | CorrelationId: {CorrelationId}",
            cart.TenantId, cart.CartId, cart.DiscountTotal, correlationId);
                return true;
            });
    }

    /// <inheritdoc />
    public async Task SetActiveCheckoutIdempotencyAsync(
        Guid tenantId,
        CartOwner owner,
        Guid cartId,
        string checkoutIdempotencyKey,
        string snapshotHash,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureOwnerTenantMatchAsync(tenantId, owner, correlationId, cancellationToken);

        await ExecuteWithCartMutationLockAsync(
            tenantId,
            owner,
            correlationId,
            cancellationToken,
            async ct =>
            {
                var cacheKey = BuildCacheKey(tenantId, owner);
                var cart = await GetCartFromRedisAsync(tenantId, owner, cacheKey, correlationId);
                if (cart == null)
                {
                    throw new CheckoutCartNotFoundException(cartId);
                }

                if (cart.CartId != cartId ||
                    cart.TenantId != tenantId ||
                    !string.Equals(cart.OwnerType, owner.OwnerType, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(cart.OwnerId, owner.OwnerId, StringComparison.Ordinal))
                {
                    throw new CheckoutCartOwnershipException(cartId);
                }

                cart.ActiveCheckoutIdempotencyKey = checkoutIdempotencyKey;
                cart.ActiveCheckoutSnapshotHash = snapshotHash;

                var ttl = cart.ExpiresAtUtc > DateTime.UtcNow
                    ? cart.ExpiresAtUtc - DateTime.UtcNow
                    : TimeSpan.FromHours(CartTtlHours);

                var database = _redisFactory.GetDatabase();
                if (database == null)
                {
                    throw new RedisUnavailableException();
                }

                ct.ThrowIfCancellationRequested();
                await database.StringSetAsync(
                    cacheKey,
                    JsonSerializer.Serialize(cart, _jsonOptions),
                    ttl);

                _logger.LogDebug(
                    "Persisted checkout idempotency through Cart owner | TenantId: {TenantId} | CartId: {CartId} | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                    tenantId,
                    cartId,
                    checkoutIdempotencyKey,
                    correlationId);

                return true;
            });
    }

    #endregion

    #region Cart Validation (Phase 3.2)


    /// <summary>
    /// Validation action taken on cart
    /// </summary>
    private enum ValidationAction
    {
        None,
        Fixed,
        Reset
    }

    /// <summary>
    /// Validates and fixes cart consistency issues.
    /// - Items list not null
    /// - Quantity >= 1 and <= 99
    /// - UnitPrice >= 0
    /// - Single currency across cart
    /// - Subtotal = Σ(quantity × unitPrice)
    /// - GrandTotal = Subtotal (no tax/discount yet)
    /// </summary>
    private async Task<(CartDto Cart, ValidationAction Action)> ValidateAndFixCartAsync(
        CartDto cart,
        Guid tenantId,
        CartOwner owner,
        string cacheKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        bool needsFix = false;
        bool needsReset = false;

        try
        {
            // Check for null items list
            if (cart.Items == null)
            {
                _logger.LogWarning(
                    "Cart validation: null items list | TenantId: {TenantId} | CartId: {CartId} | ValidationAction: RESET | CorrelationId: {CorrelationId}",
                    tenantId, cart.CartId, correlationId);
                needsReset = true;
            }

            if (!needsReset)
            {
                // Validate each item
                var itemsToRemove = new List<CartItemDto>();
                foreach (var item in cart.Items)
                {
                    // Quantity validation
                    if (item.Quantity < 1)
                    {
                        _logger.LogInformation(
                            "Cart validation: invalid quantity < 1 | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | Quantity: {Quantity} | ValidationAction: FIXED | CorrelationId: {CorrelationId}",
                            tenantId, cart.CartId, item.ProductId, item.Quantity, correlationId);
                        itemsToRemove.Add(item);
                        needsFix = true;
                    }
                    else if (item.Quantity > MaxQuantityPerItem)
                    {
                        _logger.LogInformation(
                            "Cart validation: quantity > max | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | Quantity: {Quantity} | ValidationAction: FIXED | CorrelationId: {CorrelationId}",
                            tenantId, cart.CartId, item.ProductId, item.Quantity, correlationId);
                        item.Quantity = MaxQuantityPerItem;
                        needsFix = true;
                    }

                    // UnitPrice validation
                    if (item.UnitPrice < 0)
                    {
                        _logger.LogWarning(
                            "Cart validation: negative unit price | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | UnitPrice: {UnitPrice} | ValidationAction: FIXED | CorrelationId: {CorrelationId}",
                            tenantId, cart.CartId, item.ProductId, item.UnitPrice, correlationId);
                        item.UnitPrice = 0;
                        needsFix = true;
                    }
                }

                // Remove invalid items
                foreach (var item in itemsToRemove)
                {
                    cart.Items.Remove(item);
                }

                // Validate totals
                var expectedSubtotal = cart.Items.Sum(i => i.UnitPrice * i.Quantity);
                if (Math.Abs(cart.Subtotal - expectedSubtotal) > 0.01m)
                {
                    _logger.LogInformation(
                        "Cart validation: subtotal mismatch | TenantId: {TenantId} | CartId: {CartId} | Expected: {Expected} | Actual: {Actual} | ValidationAction: FIXED | CorrelationId: {CorrelationId}",
                        tenantId, cart.CartId, expectedSubtotal, cart.Subtotal, correlationId);
                    needsFix = true;
                }

                // Recalculate if needed
                if (needsFix)
                {
                    cart.RecalculateTotals();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cart validation failed - resetting cart | TenantId: {TenantId} | CartId: {CartId} | ValidationAction: RESET | CorrelationId: {CorrelationId}",
                tenantId, cart.CartId, correlationId);
            needsReset = true;
        }

        if (needsReset)
        {
            // Reset cart and emit event
            var oldCartId = cart.CartId;
            cart = await CreateEmptyCartAsync(tenantId, owner, cancellationToken);
            
            await EmitCartClearedEventAsync(tenantId, oldCartId, owner, 0, 0, correlationId, cancellationToken);
            await DeleteCartFromRedisAsync(cacheKey, correlationId, cancellationToken);
            
            return (cart, ValidationAction.Reset);
        }

        if (needsFix)
        {
            // Save fixed cart
            await SaveCartToRedisAsync(cart, cacheKey, correlationId, cancellationToken);
            return (cart, ValidationAction.Fixed);
        }

        return (cart, ValidationAction.None);
    }

    /// <summary>
    /// Validates cart size limits.
    /// Throws CartLimitExceededException if limits are exceeded.
    /// </summary>
    private void ValidateCartLimits(CartDto cart, Guid tenantId, string correlationId)
    {
        // Check max items
        if (cart.Items.Count > MaxItems)
        {
            _logger.LogWarning(
                "Cart limit exceeded: max items | TenantId: {TenantId} | CartId: {CartId} | ItemCount: {ItemCount} | MaxItems: {MaxItems} | CorrelationId: {CorrelationId}",
                tenantId, cart.CartId, cart.Items.Count, MaxItems, correlationId);
            throw new CartLimitExceededException($"Maximum {MaxItems} unique items allowed in cart");
        }

        // Check max total quantity
        var totalQuantity = cart.Items.Sum(i => i.Quantity);
        if (totalQuantity > MaxTotalQuantity)
        {
            _logger.LogWarning(
                "Cart limit exceeded: max total quantity | TenantId: {TenantId} | CartId: {CartId} | TotalQuantity: {TotalQuantity} | MaxTotalQuantity: {MaxTotalQuantity} | CorrelationId: {CorrelationId}",
                tenantId, cart.CartId, totalQuantity, MaxTotalQuantity, correlationId);
            throw new CartLimitExceededException($"Maximum {MaxTotalQuantity} total items allowed in cart");
        }

        // Check payload size (estimate)
        var estimatedSize = EstimateCartPayloadSize(cart);
        if (estimatedSize > MaxCartPayloadBytes)
        {
            _logger.LogWarning(
                "Cart limit exceeded: max payload size | TenantId: {TenantId} | CartId: {CartId} | EstimatedSize: {EstimatedSize} | MaxSize: {MaxSize} | CorrelationId: {CorrelationId}",
                tenantId, cart.CartId, estimatedSize, MaxCartPayloadBytes, correlationId);
            throw new CartLimitExceededException("Cart data exceeds maximum allowed size");
        }
    }

    /// <summary>
    /// Estimates the cart payload size in bytes.
    /// </summary>
    private int EstimateCartPayloadSize(CartDto cart)
    {
        try
        {
            var json = JsonSerializer.Serialize(cart, _jsonOptions);
            return Encoding.UTF8.GetByteCount(json);
        }
        catch
        {
            // Conservative estimate if serialization fails
            return cart.Items.Count * 500;
        }
    }

    /// <summary>
    /// Re-validates products in cart on read.
    /// Removes items where product is no longer active or visible.
    /// </summary>
    private async Task<CartDto> RevalidateCartProductsAsync(
        CartDto cart,
        string cacheKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (cart.Items.Count == 0)
        {
            return cart;
        }

        var productIds = cart.Items
            .Select(item => item.ProductId)
            .Where(productId => productId != Guid.Empty)
            .Distinct()
            .ToList();
        var itemsToRemove = new List<CartItemDto>();
        var cartWasUpdated = false;
        try
        {
            var projectedProducts = await _productRepository.GetByProductIdsAsync(
                cart.TenantId,
                productIds,
                affiliateModeOnly: false,
                cancellationToken) ?? [];
            var tenantCandidates = projectedProducts
                .Where(product =>
                    product.TenantId == cart.TenantId &&
                    productIds.Contains(product.ProductId))
                .ToList();
            var authoritativeProducts = await ProductSearchSqlAuthorityOverlay.ApplyBatchAsync(
                _dbContext,
                tenantCandidates,
                cart.TenantId,
                cancellationToken);
            var productsById = authoritativeProducts
                .Where(product => product != null)
                .ToDictionary(product => product!.ProductId, product => product!);
            var affiliateAdmissions = new Dictionary<
                (Guid ProductId, Guid? AccountId, string? ReferralCode, Guid? PromotionLinkId),
                AffiliateCartLineAdmission>();

            // Product and SQL authority are loaded once for the complete cart. Per-line
            // checks below remain in-memory except attributed affiliate admission.
            foreach (var item in cart.Items)
            {
                productsById.TryGetValue(item.ProductId, out var product);
                
                if (product == null || !product.IsActive || !product.IsOnlineVisible)
                {
                    _logger.LogWarning(
                        "Product no longer available - removing from cart | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                        cart.TenantId, cart.CartId, item.ProductId, correlationId);
                    itemsToRemove.Add(item);
                }
                else
                {
                    var productPurchasability = ProductStorefrontVisibilityAuthority.Evaluate(product);
                    if (!product.HasVariants)
                    {
                        if (!productPurchasability.CanPurchase)
                        {
                            _logger.LogWarning(
                                "Simple product no longer satisfies canonical purchasability - removing from cart | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | Reason: {Reason} | CorrelationId: {CorrelationId}",
                                cart.TenantId,
                                cart.CartId,
                                item.ProductId,
                                productPurchasability.UnavailableReason,
                                correlationId);
                            itemsToRemove.Add(item);
                            continue;
                        }

                        var canonicalVariant = ResolveCanonicalSimpleProductVariant(product);
                        if (canonicalVariant == null)
                        {
                            _logger.LogWarning(
                                "Simple product has no active canonical variant - removing from cart | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                                cart.TenantId,
                                cart.CartId,
                                item.ProductId,
                                correlationId);
                            itemsToRemove.Add(item);
                            continue;
                        }

                        if (!item.VariantId.HasValue)
                        {
                            // Redis carts can outlive a deployment. Upgrade legacy simple
                            // lines in place to the canonical SKU identity.
                            item.VariantId = canonicalVariant.VariantId;
                            cartWasUpdated = true;
                        }
                        else if (item.VariantId.Value != canonicalVariant.VariantId)
                        {
                            _logger.LogWarning(
                                "Simple cart item variant is not canonical - removing from cart | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | VariantId: {VariantId} | CanonicalVariantId: {CanonicalVariantId} | CorrelationId: {CorrelationId}",
                                cart.TenantId,
                                cart.CartId,
                                item.ProductId,
                                item.VariantId,
                                canonicalVariant.VariantId,
                                correlationId);
                            itemsToRemove.Add(item);
                            continue;
                        }

                        item.AvailableStock = productPurchasability.StockQuantity;
                        if (item.UnitPrice != productPurchasability.Price)
                        {
                            item.UnitPrice = productPurchasability.Price;
                            cartWasUpdated = true;
                        }
                    }

                    // PHASE 11.7.1: Populate AvailableStock for UX hardening
                    // Find the variant in the product
                    var variant = item.VariantId.HasValue && product.HasVariants
                        ? product.Variants?.FirstOrDefault(v => v.VariantId == item.VariantId.Value)
                        : null;

                    if (item.VariantId.HasValue && product.HasVariants && variant == null)
                    {
                        _logger.LogWarning(
                            "Variant no longer exists in canonical product shape - removing from cart | TenantId: {TenantId} | CartId: {CartId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                            cart.TenantId,
                            cart.CartId,
                            item.VariantId,
                            correlationId);
                        itemsToRemove.Add(item);
                        continue;
                    }

                    if (variant != null)
                    {
                        var variantPurchasability = ProductStorefrontVisibilityAuthority.EvaluateVariant(product, variant);
                        if (!variantPurchasability.CanPurchase)
                        {
                            _logger.LogWarning(
                                "Variant no longer satisfies canonical purchasability - removing from cart | TenantId: {TenantId} | CartId: {CartId} | VariantId: {VariantId} | Reason: {Reason} | CorrelationId: {CorrelationId}",
                                cart.TenantId,
                                cart.CartId,
                                item.VariantId,
                                variantPurchasability.UnavailableReason,
                                correlationId);
                            itemsToRemove.Add(item);
                            continue;
                        }

                        var availableStock = variantPurchasability.StockQuantity;
                        item.AvailableStock = availableStock;

                        if (product.TrackStock && !product.AllowBackorder && availableStock <= 0)
                        {
                            _logger.LogWarning(
                                "Variant no longer has authoritative stock - removing from cart | TenantId: {TenantId} | CartId: {CartId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                                cart.TenantId,
                                cart.CartId,
                                item.VariantId,
                                correlationId);
                            itemsToRemove.Add(item);
                            continue;
                        }

                        if (product.TrackStock && !product.AllowBackorder && item.Quantity > availableStock)
                        {
                            _logger.LogInformation(
                                "Cart item quantity reduced to authoritative SQL stock | TenantId: {TenantId} | CartId: {CartId} | VariantId: {VariantId} | RequestedQuantity: {RequestedQuantity} | AvailableStock: {AvailableStock} | CorrelationId: {CorrelationId}",
                                cart.TenantId,
                                cart.CartId,
                                item.VariantId,
                                item.Quantity,
                                availableStock,
                                correlationId);
                            item.Quantity = availableStock;
                            cartWasUpdated = true;
                        }
                    }

                    // Redis owns line identity/quantity, never current media selection.
                    // Repair missing, changed and removed image snapshots on every read,
                    // before cart, checkout and order snapshot consumers receive the line.
                    var currentImage = ProductMediaImageResolver.Resolve(product, item.VariantId);
                    if (!string.Equals(item.ThumbnailImage, currentImage, StringComparison.Ordinal))
                    {
                        item.ThumbnailImage = currentImage;
                        cartWasUpdated = true;
                    }

                    var admissionKey = (
                        item.ProductId,
                        item.AffiliateAccountId,
                        item.AffiliateReferralCode,
                        item.PromotionLinkId);
                    if (!affiliateAdmissions.TryGetValue(admissionKey, out var refreshedLineAdmission))
                    {
                        refreshedLineAdmission = await ResolveAffiliateCartLineAdmissionAsync(
                            cart.TenantId,
                            item.ProductId,
                            item.AffiliateAccountId,
                            item.AffiliateReferralCode,
                            item.PromotionLinkId,
                            correlationId,
                            cancellationToken);
                        affiliateAdmissions[admissionKey] = refreshedLineAdmission;
                    }

                    if (ApplyAffiliateCartLineAdmission(item, refreshedLineAdmission))
                    {
                        cartWasUpdated = true;
                    }

                    if (RefreshCartItemPromotionSnapshot(item, product))
                    {
                        cartWasUpdated = true;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Product re-validation failed - clearing unverified affiliate attribution | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                cart.TenantId, cart.CartId, correlationId);

            var affiliateAdmissionCleared = false;
            foreach (var item in cart.Items)
            {
                affiliateAdmissionCleared |= ApplyAffiliateCartLineAdmission(
                    item,
                    AffiliateCartLineAdmission.NotEligible);
            }

            if (affiliateAdmissionCleared)
            {
                cart.RecalculateTotals();
                await SaveCartToRedisAsync(cart, cacheKey, correlationId, cancellationToken);
            }

            return cart;
        }

        if (itemsToRemove.Count > 0)
        {
            // Remove unavailable items
            foreach (var item in itemsToRemove)
            {
                cart.Items.Remove(item);
                
                // Emit removal event
                var variantName = FormatVariantName(item.VariantAttributes);
                await EmitCartItemRemovedEventAsync(cart, item.ProductId, item.VariantId, variantName, item.Quantity, correlationId, cancellationToken);
            }

            // Recalculate and save
            cart.RecalculateTotals();
            await SaveCartToRedisAsync(cart, cacheKey, correlationId, cancellationToken);

            _logger.LogInformation(
                "Removed unavailable products from cart | TenantId: {TenantId} | CartId: {CartId} | RemovedCount: {RemovedCount} | RemainingItems: {RemainingItems} | CorrelationId: {CorrelationId}",
                cart.TenantId, cart.CartId, itemsToRemove.Count, cart.Items.Count, correlationId);
        }
        else if (cartWasUpdated)
        {
            cart.RecalculateTotals();
            await SaveCartToRedisAsync(cart, cacheKey, correlationId, cancellationToken);
        }

        return cart;
    }

    internal static ProductVariantDocument? ResolveCanonicalSimpleProductVariant(ProductDocument product)
    {
        if (product.HasVariants)
        {
            return null;
        }

        return product.Variants.FirstOrDefault(variant => variant.IsDefault && variant.IsActive)
            ?? product.Variants.FirstOrDefault(variant => variant.IsActive);
    }

    private async Task<AffiliateCartLineAdmission> ResolveAffiliateCartLineAdmissionAsync(
        Guid tenantId,
        Guid productId,
        Guid? affiliateAccountId,
        string? affiliateReferralCode,
        Guid? promotionLinkId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!affiliateAccountId.HasValue || string.IsNullOrWhiteSpace(affiliateReferralCode))
        {
            return AffiliateCartLineAdmission.NotEligible;
        }

        var normalizedReferralCode = affiliateReferralCode.Trim().ToUpperInvariant();
        var affiliateAccountActive = await _dbContext.AffiliateAccounts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(account =>
                account.Id == affiliateAccountId.Value &&
                !account.IsDeleted &&
                account.Status == AffiliateStatus.Active,
                cancellationToken);

        if (!affiliateAccountActive)
        {
            return AffiliateCartLineAdmission.NotEligible;
        }

        Guid? admittedPromotionLinkId = null;
        if (promotionLinkId.HasValue)
        {
            var link = await _dbContext.AffiliatePromotionLinks
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(item =>
                    item.Id == promotionLinkId.Value &&
                    item.TenantId == tenantId &&
                    !item.IsDeleted &&
                    item.IsActive &&
                    item.AffiliateAccountId == affiliateAccountId.Value &&
                    item.ReferralCode == normalizedReferralCode)
                .Select(item => new { item.Id, item.ProductId })
                .SingleOrDefaultAsync(cancellationToken);

            if (link == null)
            {
                return AffiliateCartLineAdmission.NotEligible;
            }

            if (link.ProductId == productId)
            {
                admittedPromotionLinkId = link.Id;
            }
        }

        var product = await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item =>
                item.Id == productId &&
                item.TenantId == tenantId,
                cancellationToken);

        if (product == null ||
            product.IsDeleted ||
            !product.IsActive ||
            product.PublishStatus != PublishStatus.Published ||
            !product.IsOnlineVisible)
        {
            return AffiliateCartLineAdmission.NotEligible;
        }

        if (!await HasCurrentAffiliatePolicyAndCategoryAsync(tenantId, productId, cancellationToken) ||
            !await HasPositiveAffiliateLinePriceAsync(product, cancellationToken) ||
            !await HasAvailableAffiliateLineInventoryAsync(product, cancellationToken))
        {
            return AffiliateCartLineAdmission.NotEligible;
        }

        _logger.LogDebug(
            "Affiliate cart line admitted | TenantId: {TenantId} | ProductId: {ProductId} | AffiliateAccountId: {AffiliateAccountId} | PromotionLinkId: {PromotionLinkId} | CorrelationId: {CorrelationId}",
            tenantId,
            productId,
            affiliateAccountId.Value,
            admittedPromotionLinkId,
            correlationId);

        return new AffiliateCartLineAdmission(
            true,
            affiliateAccountId.Value,
            normalizedReferralCode,
            admittedPromotionLinkId);
    }

    private async Task<bool> HasCurrentAffiliatePolicyAndCategoryAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AffiliateProductPolicies
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(policy =>
                policy.TenantId == tenantId &&
                policy.ProductId == productId &&
                !policy.IsDeleted &&
                policy.IsEnabled &&
                policy.AffiliateCategoryId.HasValue)
            .Join(
                _dbContext.AffiliateCategories.AsNoTracking().IgnoreQueryFilters(),
                policy => policy.AffiliateCategoryId!.Value,
                category => category.Id,
                (policy, category) => category)
            .AnyAsync(category =>
                !category.IsDeleted &&
                category.IsActive &&
                ((category.Level == 1 && category.ParentId == null) ||
                 (category.Level == 2 &&
                  category.ParentId != null &&
                  _dbContext.AffiliateCategories
                      .IgnoreQueryFilters()
                      .Any(parent =>
                          parent.Id == category.ParentId.Value &&
                          parent.Level == 1 &&
                          parent.IsActive &&
                          !parent.IsDeleted))),
                cancellationToken);
    }

    private async Task<bool> HasPositiveAffiliateLinePriceAsync(Product product, CancellationToken cancellationToken)
    {
        if (product.HasVariants)
        {
            return await _dbContext.ProductVariants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .AnyAsync(variant =>
                    variant.TenantId == product.TenantId &&
                    variant.ProductId == product.Id &&
                    !variant.IsDeleted &&
                    variant.IsActive &&
                    variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                    variant.SemanticSupersededAtUtc == null &&
                    variant.SemanticReplacementVariantId == null &&
                    ((variant.OnlinePrice.HasValue && variant.OnlinePrice.Value > 0m) ||
                     (!variant.OnlinePrice.HasValue && variant.Price > 0m) ||
                     (variant.OnlinePrice.HasValue && variant.OnlinePrice.Value == 0m && variant.Price > 0m)),
                    cancellationToken);
        }

        return await _dbContext.ProductUnits
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(unit =>
                unit.ProductId == product.Id &&
                !unit.IsDeleted &&
                unit.IsDefault &&
                unit.SellingPrice.HasValue &&
                unit.SellingPrice.Value > 0m,
                cancellationToken);
    }

    private async Task<bool> HasAvailableAffiliateLineInventoryAsync(Product product, CancellationToken cancellationToken)
    {
        if (!product.TrackStock || product.AllowBackorder)
        {
            return true;
        }

        return await _dbContext.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(stock =>
                stock.TenantId == product.TenantId &&
                !stock.IsDeleted &&
                stock.Variant != null &&
                stock.Variant.TenantId == product.TenantId &&
                stock.Variant.ProductId == product.Id &&
                !stock.Variant.IsDeleted &&
                stock.Variant.IsActive &&
                stock.Variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                stock.Variant.SemanticSupersededAtUtc == null &&
                stock.Variant.SemanticReplacementVariantId == null &&
                (product.HasVariants || stock.Variant.IsDefault) &&
                stock.Warehouse != null &&
                stock.Warehouse.TenantId == product.TenantId &&
                !stock.Warehouse.IsDeleted &&
                stock.Warehouse.IsActive &&
                stock.Quantity - stock.ReservedQuantity > 0m,
                cancellationToken);
    }

    private static bool ApplyAffiliateCartLineAdmission(CartItemDto item, AffiliateCartLineAdmission admission)
    {
        if (item.IsAffiliateEligibleLine == admission.IsEligible &&
            item.AffiliateAccountId == admission.AffiliateAccountId &&
            string.Equals(item.AffiliateReferralCode, admission.ReferralCode, StringComparison.Ordinal) &&
            item.PromotionLinkId == admission.PromotionLinkId)
        {
            return false;
        }

        item.IsAffiliateEligibleLine = admission.IsEligible;
        item.AffiliateAccountId = admission.AffiliateAccountId;
        item.AffiliateReferralCode = admission.ReferralCode;
        item.PromotionLinkId = admission.PromotionLinkId;
        return true;
    }

    private sealed record AffiliateCartLineAdmission(
        bool IsEligible,
        Guid? AffiliateAccountId,
        string? ReferralCode,
        Guid? PromotionLinkId)
    {
        public static readonly AffiliateCartLineAdmission NotEligible = new(false, null, null, null);
    }

    private static bool RefreshCartItemPromotionSnapshot(CartItemDto item, ProductDocument product)
    {
        var discountedUnitPrice = product.HasActivePromotion
            ? StorefrontPromotionPriceComposer.TryCompose(
                item.UnitPrice,
                product.PromotionDiscountType,
                product.PromotionDiscountValue,
                product.PromotionMaxDiscountAmount)
            : null;
        var hasActivePromotion = discountedUnitPrice.HasValue;
        var nextBadge = hasActivePromotion ? product.PromotionBadge : null;
        var nextDiscountType = hasActivePromotion ? product.PromotionDiscountType : null;
        var nextDiscountValue = hasActivePromotion ? product.PromotionDiscountValue : null;

        if (item.HasActivePromotion == hasActivePromotion &&
            string.Equals(item.PromotionBadge, nextBadge, StringComparison.Ordinal) &&
            string.Equals(item.PromotionDiscountType, nextDiscountType, StringComparison.Ordinal) &&
            item.PromotionDiscountValue == nextDiscountValue &&
            item.DiscountedUnitPrice == discountedUnitPrice)
        {
            return false;
        }

        item.HasActivePromotion = hasActivePromotion;
        item.PromotionBadge = nextBadge;
        item.PromotionDiscountType = nextDiscountType;
        item.PromotionDiscountValue = nextDiscountValue;
        item.DiscountedUnitPrice = discountedUnitPrice;
        return true;
    }

    private async Task<AuthoritativeVariantStockSnapshot?> GetAuthoritativeVariantStockSnapshotAsync(
        Guid tenantId,
        Guid variantId,
        CancellationToken cancellationToken)
    {
        var snapshots = await GetAuthoritativeVariantStockSnapshotsAsync(
            tenantId,
            new List<Guid> { variantId },
            cancellationToken);

        return snapshots.GetValueOrDefault(variantId);
    }

    private async Task<Dictionary<Guid, AuthoritativeVariantStockSnapshot>> GetAuthoritativeVariantStockSnapshotsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        if (variantIds.Count == 0)
        {
            return new Dictionary<Guid, AuthoritativeVariantStockSnapshot>();
        }

        var variants = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WherePubliclyVisibleLineage(tenantId)
            .Where(v =>
                variantIds.Contains(v.Id))
            .Select(v => new
            {
                v.Id,
                v.ProductId,
                v.SKU,
                v.StockUpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        if (variants.Count == 0)
        {
            return new Dictionary<Guid, AuthoritativeVariantStockSnapshot>();
        }

        var currentVariantIds = variants
            .Select(variant => variant.Id)
            .Distinct()
            .ToList();

        var availableByVariant = await _dbContext.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WhereCurrentInventoryStockOwner(tenantId)
            .Where(stock => currentVariantIds.Contains(stock.VariantId))
            .GroupBy(stock => stock.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                AvailableQuantity = group.Sum(stock => stock.Quantity - stock.ReservedQuantity)
            })
            .ToDictionaryAsync(item => item.VariantId, item => item.AvailableQuantity, cancellationToken);

        return variants.ToDictionary(
            variant => variant.Id,
            variant => new AuthoritativeVariantStockSnapshot(
                variant.Id,
                variant.ProductId,
                variant.SKU,
                ToNonNegativeInt(availableByVariant.GetValueOrDefault(variant.Id)),
                variant.StockUpdatedAtUtc));
    }

    private int ResolveAuthoritativeAvailableStock(
        Guid tenantId,
        Guid productId,
        ProductVariantDocument? mongoVariant,
        AuthoritativeVariantStockSnapshot? sqlSnapshot,
        string surface,
        string correlationId)
    {
        var mongoAvailableStock = mongoVariant == null
            ? (int?)null
            : ToNonNegativeInt(mongoVariant.TotalStock);

        if (sqlSnapshot == null)
        {
            _reliabilityTelemetry.RecordStockDivergenceDetected(surface, "sql_variant_lifecycle_missing");
            _logger.LogWarning(
                "Variant stock failed closed because SQL no longer proves public variant lifecycle | TenantId: {TenantId} | ProductId: {ProductId} | VariantId: {VariantId} | Surface: {Surface} | MongoAvailableStock: {MongoAvailableStock} | CorrelationId: {CorrelationId}",
                tenantId,
                productId,
                mongoVariant?.VariantId,
                surface,
                mongoAvailableStock,
                correlationId);
            return 0;
        }

        var divergenceReason = GetStockDivergenceReason(mongoVariant, mongoAvailableStock, sqlSnapshot);
        if (divergenceReason != null)
        {
            _reliabilityTelemetry.RecordStockDivergenceDetected(surface, divergenceReason);
            _logger.LogWarning(
                "Stock divergence detected; using SQL source of truth | TenantId: {TenantId} | ProductId: {ProductId} | VariantId: {VariantId} | Surface: {Surface} | Reason: {Reason} | MongoAvailableStock: {MongoAvailableStock} | SqlAvailableStock: {SqlAvailableStock} | MongoStockUpdatedAtUtc: {MongoStockUpdatedAtUtc} | SqlStockUpdatedAtUtc: {SqlStockUpdatedAtUtc} | CorrelationId: {CorrelationId}",
                tenantId,
                productId,
                sqlSnapshot.VariantId,
                surface,
                divergenceReason,
                mongoAvailableStock,
                sqlSnapshot.AvailableStock,
                mongoVariant?.StockUpdatedAtUtc,
                sqlSnapshot.StockUpdatedAtUtc,
                correlationId);
            return sqlSnapshot.AvailableStock;
        }

        return mongoAvailableStock ?? sqlSnapshot.AvailableStock;
    }

    private static string? GetStockDivergenceReason(
        ProductVariantDocument? mongoVariant,
        int? mongoAvailableStock,
        AuthoritativeVariantStockSnapshot sqlSnapshot)
    {
        if (mongoVariant == null)
        {
            return "mongo_variant_missing";
        }

        if (!mongoVariant.StockUpdatedAtUtc.HasValue)
        {
            return "mongo_stock_timestamp_missing";
        }

        if (sqlSnapshot.StockUpdatedAtUtc.HasValue &&
            mongoVariant.StockUpdatedAtUtc.Value < sqlSnapshot.StockUpdatedAtUtc.Value)
        {
            return "mongo_stock_stale";
        }

        if (mongoAvailableStock.HasValue &&
            mongoAvailableStock.Value != sqlSnapshot.AvailableStock)
        {
            return "mongo_sql_stock_mismatch";
        }

        return null;
    }

    private static int ToNonNegativeInt(decimal value)
    {
        var normalized = Math.Max(0m, decimal.Truncate(value));
        return normalized > int.MaxValue ? int.MaxValue : (int)normalized;
    }

    /// <summary>
    /// Applies the best cart-level promotion to the cart.
    /// Cart promotions are auto-applied based on subtotal and tenant settings.
    /// </summary>
    private async Task ApplyCartPromotionAsync(
        CartDto cart,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Skip if cart is empty
        if (cart.Items.Count == 0 || cart.Subtotal <= 0)
        {
            cart.ApplyCartPromotion(null);
            return;
        }

        try
        {
            // Get the best active cart promotion for this tenant
            var cartPromotion = await _promotionEngine.GetActiveCartPromotionAsync(
                tenantId,
                cart.Subtotal - cart.PromotionDiscountTotal, // Use discounted subtotal (after item promotions)
                DateTime.UtcNow,
                cancellationToken);

            if (cartPromotion != null)
            {
                var appliedPromo = new AppliedCartPromotionInfo
                {
                    PromotionId = cartPromotion.PromotionId,
                    Name = cartPromotion.Name,
                    Description = cartPromotion.Description,
                    DiscountType = cartPromotion.DiscountType,
                    DiscountValue = cartPromotion.DiscountValue,
                    DiscountAmount = cartPromotion.CalculatedDiscount,
                    Badge = cartPromotion.Badge
                };

                cart.ApplyCartPromotion(appliedPromo);

                _logger.LogInformation(
                    "[CartPromotion] Applied | TenantId: {TenantId} | CartId: {CartId} | PromotionId: {PromotionId} | Name: {Name} | Discount: {Discount} | CorrelationId: {CorrelationId}",
                    tenantId, cart.CartId, cartPromotion.PromotionId, cartPromotion.Name, cartPromotion.CalculatedDiscount, correlationId);
            }
            else
            {
                cart.ApplyCartPromotion(null);
            }
        }
        catch (Exception ex)
        {
            // Don't fail the cart read if promotion lookup fails
            _logger.LogWarning(ex,
                "[CartPromotion] Failed to load cart promotions - continuing without | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                tenantId, cart.CartId, correlationId);
            cart.ApplyCartPromotion(null);
        }
    }

    #endregion

    #region Redis Operations

    private string BuildCacheKey(Guid tenantId, CartOwner owner)
    {
        return CacheKeyBuilder.CartKey(_redisSettings.InstanceName, tenantId, owner.OwnerType, owner.OwnerId);
    }

    private async Task<T> ExecuteWithCartMutationLockAsync<T>(
        Guid tenantId,
        CartOwner owner,
        string correlationId,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<T>> operation)
    {
        // CRITICAL FIX: Always use owner lock for cart mutations to prevent race conditions
        // during cart creation. Cart-id locks are only used by CheckoutExecutionLockManager
        // which acquires both checkout and cart locks together.
        //
        // Previous behavior: Switched from owner lock to cart-id lock after cart creation,
        // which allowed concurrent mutations to acquire different locks for the same cart.
        //
        // New behavior: Owner lock is stable for all cart mutations. This ensures that
        // create-then-save flows (like ApplyDraftAsync) maintain exclusive access throughout.
        var lockKey = BuildOwnerMutationLockKey(tenantId, owner);

        _logger.LogDebug(
            "Cart mutation lock acquired | LockKey: {LockKey} | TenantId: {TenantId} | Owner: {OwnerType}:{OwnerId} | CorrelationId: {CorrelationId}",
            lockKey, tenantId, owner.OwnerType, owner.OwnerId, correlationId);

        return await ExecuteWithDeferredCartEventsAsync(
            () => ExecuteWithRedisLockAsync(
                lockKey,
                "cart_mutation",
                correlationId,
                cancellationToken,
                operation),
            correlationId,
            cancellationToken);
    }

    private async Task<T> ExecuteWithCartMergeLocksAsync<T>(
        Guid tenantId,
        CartOwner sessionOwner,
        CartOwner customerOwner,
        string correlationId,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<T>> operation)
    {
        var sessionKey = await ResolveCartMutationLockKeyAsync(tenantId, sessionOwner, correlationId);
        var customerKey = await ResolveCartMutationLockKeyAsync(tenantId, customerOwner, correlationId);
        var lockKeys = new[] { sessionKey, customerKey }
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        return await ExecuteWithDeferredCartEventsAsync(
            () => ExecuteWithRedisLocksAsync(
                lockKeys,
                "cart_merge",
                correlationId,
                cancellationToken,
                operation),
            correlationId,
            cancellationToken);
    }

    private async Task<T> ExecuteWithDeferredCartEventsAsync<T>(
        Func<Task<T>> lockedOperation,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (_deferredCartEvents.Value != null)
        {
            return await lockedOperation();
        }

        var pendingEvents = new List<object>();
        _deferredCartEvents.Value = pendingEvents;

        T result;
        try
        {
            result = await lockedOperation();
        }
        finally
        {
            _deferredCartEvents.Value = null;
        }

        if (pendingEvents.Count == 0)
        {
            return result;
        }

        try
        {
            await _eventPublisher.PublishBatchAsync(pendingEvents, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to emit deferred cart events (best-effort, continuing) | EventCount: {EventCount} | CorrelationId: {CorrelationId}",
                pendingEvents.Count,
                correlationId);
        }

        return result;
    }

    private async Task<string> ResolveCartMutationLockKeyAsync(
        Guid tenantId,
        CartOwner owner,
        string correlationId)
    {
        // CRITICAL FIX: Always use owner lock for cart mutations to prevent race conditions.
        // This ensures consistency with ExecuteWithCartMutationLockAsync behavior.
        return BuildOwnerMutationLockKey(tenantId, owner);
    }

    private async Task<T> ExecuteWithRedisLockAsync<T>(
        string lockKey,
        string operationName,
        string correlationId,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<T>> operation)
    {
        return await ExecuteWithRedisLocksAsync(
            new[] { lockKey },
            operationName,
            correlationId,
            cancellationToken,
            operation);
    }

    private async Task<T> ExecuteWithRedisLocksAsync<T>(
        IReadOnlyList<string> lockKeys,
        string operationName,
        string correlationId,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<T>> operation)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lockToken = Guid.NewGuid().ToString("N");
        var acquiredLocks = new List<string>(lockKeys.Count);
        long firstAcquiredAt = 0;
        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? renewalTask = null;

        try
        {
            // Acquire all locks
            foreach (var lockKey in lockKeys)
            {
                var lockResult = await _distributedLockService.TryAcquireLockAsync(
                    lockKey,
                    lockToken,
                    CartMutationLockTtl);

                if (lockResult.IsInfrastructureFailure)
                {
                    _logger.LogError(
                        "Cart mutation lock infrastructure failure | Operation: {Operation} | LockKey: {LockKey} | CorrelationId: {CorrelationId}",
                        operationName,
                        lockKey,
                        correlationId);
                    throw new RedisUnavailableException();
                }

                if (!lockResult.IsAcquired)
                {
                    _reliabilityTelemetry.RecordCartConcurrencyConflict(operationName);
                    _logger.LogInformation(
                        "Cart mutation conflict prevented | Operation: {Operation} | LockKey: {LockKey} | CorrelationId: {CorrelationId}",
                        operationName,
                        lockKey,
                        correlationId);
                    throw new CartConcurrencyConflictException();
                }

                acquiredLocks.Add(lockKey);
                if (firstAcquiredAt == 0)
                {
                    firstAcquiredAt = Stopwatch.GetTimestamp();
                }
            }

            // Start background lock renewal task
            renewalTask = RenewLocksPeriodicAsync(
                acquiredLocks,
                lockToken,
                operationName,
                correlationId,
                operationCts,
                renewalCts.Token);

            // Execute the protected operation with operationCts.Token so it observes cancellation
            var result = await operation(operationCts.Token);
            
            // CRITICAL: Check if lock was lost during operation before returning result
            if (renewalTask.IsFaulted)
            {
                try
                {
                    await renewalTask; // This will throw the exception
                }
                catch (CartConcurrencyConflictException)
                {
                    throw; // Re-throw to prevent returning stale result
                }
            }
            
            _reliabilityTelemetry.RecordCartUpdateLostPrevented(operationName);
            return result;
        }
        finally
        {
            // Stop renewal task
            renewalCts.Cancel();
            CartConcurrencyConflictException? renewalConflict = null;
            if (renewalTask != null)
            {
                try
                {
                    await renewalTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelling renewal
                }
                catch (CartConcurrencyConflictException ex)
                {
                    // Keep the conflict, but always attempt token-owned release
                    // for *all* acquired locks before propagating it. Throwing
                    // here used to bypass the release loop on renewal failure.
                    renewalConflict = ex;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Lock renewal task failed | Operation: {Operation} | CorrelationId: {CorrelationId}",
                        operationName,
                        correlationId);
                }
            }

            // Release all locks in reverse order
            for (var i = acquiredLocks.Count - 1; i >= 0; i--)
            {
                try
                {
                    await _distributedLockService.ReleaseLockAsync(acquiredLocks[i], lockToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Cart mutation lock release failed | Operation: {Operation} | LockKey: {LockKey} | CorrelationId: {CorrelationId}",
                        operationName,
                        acquiredLocks[i],
                        correlationId);
                }
            }

            if (firstAcquiredAt != 0)
            {
                _reliabilityTelemetry.RecordCartMutationLockDuration(
                    operationName, Stopwatch.GetElapsedTime(firstAcquiredAt));
            }

            if (renewalConflict != null)
            {
                throw renewalConflict;
            }
        }
    }

    /// <summary>
    /// Background task that periodically renews locks to prevent expiration during long-running operations.
    /// </summary>
    private async Task RenewLocksPeriodicAsync(
        IReadOnlyList<string> lockKeys,
        string lockToken,
        string operationName,
        string correlationId,
        CancellationTokenSource operationCts,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_cartMutationLockRenewalInterval, cancellationToken);

                foreach (var lockKey in lockKeys)
                {
                    var refreshResult = await _distributedLockService.RefreshLockAsync(
                        lockKey,
                        lockToken,
                        CartMutationLockTtl);

                    if (refreshResult.IsInfrastructureFailure)
                    {
                        _logger.LogWarning(
                            "Lock renewal infrastructure failure | Operation: {Operation} | LockKey: {LockKey} | CorrelationId: {CorrelationId}",
                            operationName,
                            lockKey,
                            correlationId);
                    }
                    else if (!refreshResult.IsAcquired)
                    {
                        _logger.LogError(
                            "Lock renewal failed - lock lost during operation | Operation: {Operation} | CorrelationId: {CorrelationId}",
                            operationName,
                            correlationId);
                        
                        // CRITICAL: Lock lost - abort the protected operation immediately
                        operationCts.Cancel();
                        // Security: Do not include lock key or correlation ID in exception message
                        // as it may be exposed externally through the controller
                        throw new CartConcurrencyConflictException("Lock lost during cart operation");
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Lock renewed | Operation: {Operation} | LockKey: {LockKey} | TTL: {TTL}s | CorrelationId: {CorrelationId}",
                            operationName,
                            lockKey,
                            CartMutationLockTtl.TotalSeconds,
                            correlationId);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the operation completes
        }
        catch (CartConcurrencyConflictException)
        {
            // Let CartConcurrencyConflictException propagate to fault the renewal task
            // so ExecuteWithRedisLocksAsync can detect lock loss before returning results
            throw;
        }
        // Removed broad catch (Exception ex) to ensure CartConcurrencyConflictException
        // is not swallowed and can properly fault the renewal task
    }

    private static string BuildOwnerMutationLockKey(Guid tenantId, CartOwner owner) =>
        $"lock:cart:owner:{tenantId:N}:{owner.OwnerType.Trim().ToLowerInvariant()}:{owner.OwnerId}";

    private async Task EnsureOwnerTenantMatchAsync(
        Guid tenantId,
        CartOwner owner,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (owner == null)
        {
            throw new UnauthorizedAccessException(TenantErrorCodes.TenantMismatch);
        }

        if (string.Equals(owner.OwnerType, "session", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(owner.OwnerId))
            {
                _logger.LogWarning(
                    "Cart session ownership validation failed | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    tenantId,
                    correlationId);
                throw new UnauthorizedAccessException(TenantErrorCodes.TenantMismatch);
            }

            return;
        }

        if (!string.Equals(owner.OwnerType, "customer", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(owner.OwnerId, out var customerId) ||
            customerId == Guid.Empty)
        {
            _logger.LogWarning(
                "Cart owner validation failed | TenantId: {TenantId} | OwnerType: {OwnerType} | OwnerId: {OwnerId} | CorrelationId: {CorrelationId}",
                tenantId,
                owner.OwnerType,
                owner.OwnerId,
                correlationId);
            throw new UnauthorizedAccessException(TenantErrorCodes.TenantMismatch);
        }

        var customer = await _dbContext.OnlineCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);

        if (customer == null || customer.TenantId != tenantId)
        {
            _logger.LogWarning(
                "Cart tenant mismatch detected for customer owner | TenantId: {TenantId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
                tenantId,
                customerId,
                correlationId);
            throw new UnauthorizedAccessException(TenantErrorCodes.TenantMismatch);
        }
    }

    private async Task<CartDto?> GetCartFromRedisAsync(
        Guid tenantId,
        CartOwner owner,
        string cacheKey,
        string correlationId)
    {
        try
        {
            var database = _redisFactory.GetDatabase();
            if (database == null)
            {
                _logger.LogWarning(
                    "Redis unavailable | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    tenantId, correlationId);
                throw new RedisUnavailableException();
            }

            var value = await database.StringGetAsync(cacheKey);
            if (value.IsNullOrEmpty)
            {
                // Missing key = expired cart (Phase 3.2: return null, not error)
                return null;
            }

            try
            {
                var cart = JsonSerializer.Deserialize<CartDto>(value!, _jsonOptions);
                return cart;
            }
            catch (JsonException ex)
            {
                // Phase 3.2: Deserialization error = corrupted payload = reset cart
                _logger.LogWarning(ex,
                    "Cart deserialization failed - treating as empty cart | TenantId: {TenantId} | Key: {Key} | CorrelationId: {CorrelationId}",
                    tenantId, cacheKey, correlationId);
                
                // Delete corrupted data
                await database.KeyDeleteAsync(cacheKey);
                return null;
            }
        }
        catch (RedisUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Redis GET failed | TenantId: {TenantId} | Key: {Key} | CorrelationId: {CorrelationId}",
                tenantId, cacheKey, correlationId);
            throw new RedisUnavailableException();
        }
    }

    private async Task SaveCartToRedisAsync(
        CartDto cart,
        string cacheKey,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // CRITICAL: Check cancellation before any persistent operations
        cancellationToken.ThrowIfCancellationRequested();
        
        // Refresh pricing/tax BEFORE Redis operations so exceptions bubble up correctly
        await RefreshCartPricingPreviewAsync(cart, correlationId, cancellationToken);

        // CRITICAL: Check cancellation again after async operation
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var database = _redisFactory.GetDatabase();
            if (database == null)
            {
                _logger.LogWarning(
                    "Redis unavailable for save | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    cart.TenantId, correlationId);
                throw new RedisUnavailableException();
            }

            cart.ActiveCheckoutIdempotencyKey = null;
            cart.ActiveCheckoutSnapshotHash = null;

            var json = JsonSerializer.Serialize(cart, _jsonOptions);
            
            // Phase 3.2: Check payload size before saving
            var payloadSize = Encoding.UTF8.GetByteCount(json);
            if (payloadSize > MaxCartPayloadBytes)
            {
                _logger.LogWarning(
                    "Cart payload too large | TenantId: {TenantId} | CartId: {CartId} | Size: {Size} | MaxSize: {MaxSize} | CorrelationId: {CorrelationId}",
                    cart.TenantId, cart.CartId, payloadSize, MaxCartPayloadBytes, correlationId);
                throw new CartLimitExceededException("Cart data exceeds maximum allowed size");
            }
            
            // CRITICAL: Final check before Redis write
            cancellationToken.ThrowIfCancellationRequested();
            
            var ttl = TimeSpan.FromHours(CartTtlHours);
            
            await database.StringSetAsync(cacheKey, json, ttl);

            _logger.LogDebug(
                "Cart saved to Redis | TenantId: {TenantId} | CartId: {CartId} | ItemCount: {ItemCount} | TotalQuantity: {TotalQuantity} | PayloadBytes: {PayloadBytes} | TTL: {TtlHours}h | CorrelationId: {CorrelationId}",
                cart.TenantId, cart.CartId, cart.Items.Count, cart.ItemCount, payloadSize, CartTtlHours, correlationId);
        }
        catch (RedisUnavailableException)
        {
            throw;
        }
        catch (CartLimitExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Redis SET failed | TenantId: {TenantId} | Key: {Key} | CorrelationId: {CorrelationId}",
                cart.TenantId, cacheKey, correlationId);
            throw new RedisUnavailableException();
        }
    }

    private async Task RefreshCartPricingPreviewAsync(
        CartDto cart,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cart);

        cart.Subtotal = Math.Round(cart.Items.Sum(item => item.UnitPrice * item.Quantity), 2, MidpointRounding.AwayFromZero);

        var promotionDiscount = Math.Round(cart.PromotionDiscountTotal, 2, MidpointRounding.AwayFromZero);
        var cartPromotionDiscount = Math.Round(cart.CartPromotionDiscountTotal, 2, MidpointRounding.AwayFromZero);
        var couponDiscount = Math.Round(cart.DiscountTotal, 2, MidpointRounding.AwayFromZero);

        var taxComputation = await _taxCalculationService.CalculateAsync(
            cart.TenantId,
            cart.Subtotal,
            countryCode: null,
            cancellationToken);

        cart.TaxTotal = taxComputation.TaxAmount;
        cart.GrandTotal = Math.Round(
            Math.Max(0m, cart.Subtotal - promotionDiscount - cartPromotionDiscount - couponDiscount + cart.TaxTotal),
            2,
            MidpointRounding.AwayFromZero);

        _logger.LogDebug(
            "Cart pricing preview refreshed server-side | TenantId: {TenantId} | CartId: {CartId} | Subtotal: {Subtotal} | Tax: {Tax} | GrandTotal: {GrandTotal} | CorrelationId: {CorrelationId}",
            cart.TenantId,
            cart.CartId,
            cart.Subtotal,
            cart.TaxTotal,
            cart.GrandTotal,
            correlationId);
    }

    private async Task DeleteCartFromRedisAsync(string cacheKey, string correlationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        try
        {
            var database = _redisFactory.GetDatabase();
            if (database == null)
            {
                throw new RedisUnavailableException();
            }

            await database.KeyDeleteAsync(cacheKey);

            _logger.LogDebug(
                "Cart deleted from Redis | Key: {Key} | CorrelationId: {CorrelationId}",
                cacheKey, correlationId);
        }
        catch (RedisUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Redis DELETE failed | Key: {Key} | CorrelationId: {CorrelationId}",
                cacheKey, correlationId);
            throw new RedisUnavailableException();
        }
    }

    #endregion

    #region Product Validation

    /// <summary>
    /// Validates a product for cart addition.
    /// Checks: exists, active, online visible, and HAS VALID PRICE.
    /// </summary>
    private async Task<ProductDocument?> ValidateProductAsync(
        Guid tenantId,
        Guid productId,
        string correlationId,
        CancellationToken cancellationToken,
        ProductDocument? projectedCandidate = null,
        bool candidateAlreadyLoaded = false)
    {
        try
        {
            var projectedProduct = candidateAlreadyLoaded
                ? projectedCandidate
                : await _productRepository.FindByProductIdAsync(tenantId, productId, cancellationToken);
            if (projectedProduct != null &&
                (projectedProduct.TenantId != tenantId || projectedProduct.ProductId != productId))
                return null;
            var product = projectedProduct == null
                ? null
                : await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                    _dbContext,
                    projectedProduct,
                    tenantId,
                    cancellationToken);
            
            if (product == null)
            {
                _logger.LogWarning(
                    "Product not found | TenantId: {TenantId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                    tenantId, productId, correlationId);
                return null;
            }

            if (!product.IsActive)
            {
                _logger.LogWarning(
                    "Product not active | TenantId: {TenantId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                    tenantId, productId, correlationId);
                return null;
            }

            if (!product.IsOnlineVisible)
            {
                _logger.LogWarning(
                    "Product not visible online | TenantId: {TenantId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                    tenantId, productId, correlationId);
                return null;
            }
            
            // =============================================================================
            // PHASE 11.6: VARIANT-AWARE PRICE VALIDATION
            // =============================================================================
            // For products with HasVariants = true:
            // - Product-level price is intentionally 0/null
            // - Real price comes from the selected variant
            // - Price validation happens later at variant level
            //
            // For products with HasVariants = false (simple products):
            // - Product-level price must be valid > 0
            // =============================================================================
            
            if (!product.HasVariants)
            {
                var purchasability = ProductStorefrontVisibilityAuthority.Evaluate(product);
                var effectivePrice = purchasability.Price;
                
                if (!purchasability.IsAvailable || effectivePrice <= 0)
                {
                    _logger.LogWarning(
                        "Simple product has no valid canonical storefront price (PRODUCT_NOT_FOR_SALE) | TenantId: {TenantId} | ProductId: {ProductId} | EffectivePrice: {EffectivePrice} | Reason: {Reason} | CorrelationId: {CorrelationId}",
                        tenantId, productId, effectivePrice, purchasability.UnavailableReason, correlationId);
                    throw new ProductNotForSaleException(productId);
                }
            }
            else
            {
                _logger.LogDebug(
                    "Variant product - skipping product-level price validation | TenantId: {TenantId} | ProductId: {ProductId} | HasVariants: true | CorrelationId: {CorrelationId}",
                    tenantId, productId, correlationId);
            }

            return product;
        }
        catch (ProductNotForSaleException)
        {
            throw; // Re-throw specific exception
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Product validation failed | TenantId: {TenantId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                tenantId, productId, correlationId);
            return null;
        }
    }

    #endregion

    #region Event Emission

    private Task PublishCartEventAsync(object @event, CancellationToken cancellationToken)
    {
        var pendingEvents = _deferredCartEvents.Value;
        if (pendingEvents != null)
        {
            pendingEvents.Add(@event);
            return Task.CompletedTask;
        }

        return _eventPublisher.PublishAsync(@event, cancellationToken);
    }

    private async Task EmitCartCreatedEventAsync(CartDto cart, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            var evt = new CartCreatedEvent
            {
                TenantId = cart.TenantId,
                CartId = cart.CartId,
                OwnerType = cart.OwnerType,
                OwnerId = cart.OwnerId,
                Currency = cart.Currency,
                CorrelationId = correlationId,
                IdempotencyKey = $"cart-created:{cart.CartId}",
                OccurredAtUtc = DateTime.UtcNow
            };

            await PublishCartEventAsync(evt, cancellationToken);

            _logger.LogDebug(
                "CartCreatedEvent emitted | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                cart.TenantId, cart.CartId, correlationId);
        }
        catch (OperationCanceledException)
        {
            // CRITICAL: Do not swallow cancellation - lock may have been lost
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to emit CartCreatedEvent (best-effort, continuing) | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                cart.TenantId, cart.CartId, correlationId);
            
            // Record event publishing failure for monitoring/alerting
            try
            {
                _reliabilityTelemetry.RecordCheckoutFinalizeFailure("cart_created_event_publish_failed");
            }
            catch (Exception telemetryEx)
            {
                _logger.LogDebug(telemetryEx,
                    "Telemetry recording failed (non-fatal) | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                    cart.TenantId, cart.CartId, correlationId);
            }
            
            // Best-effort: Swallow exception because cart was already saved to Redis
            // Throwing would cause inconsistent state where cart exists but client sees error
        }
    }

    private async Task EmitCartItemAddedEventAsync(
        CartDto cart,
        CartItemDto cartItem,
        int quantityAdded,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var variantName = FormatVariantName(cartItem.VariantAttributes);

            var evt = new CartItemAddedEvent
            {
                TenantId = cart.TenantId,
                CartId = cart.CartId,
                OwnerType = cart.OwnerType,
                OwnerId = cart.OwnerId,
                ProductId = cartItem.ProductId,
                VariantId = cartItem.VariantId,
                VariantName = variantName,
                SKU = cartItem.SKU,
                Quantity = quantityAdded,
                UnitPrice = cartItem.UnitPrice,
                NewTotalQuantity = cartItem.Quantity,
                CorrelationId = correlationId,
                IdempotencyKey = $"cart-item-added:{cart.CartId}:{cartItem.ProductId}:{cartItem.VariantId}:{quantityAdded}:{cartItem.Quantity}:{correlationId}",
                OccurredAtUtc = DateTime.UtcNow
            };

            await PublishCartEventAsync(evt, cancellationToken);

            _logger.LogDebug(
                "CartItemAddedEvent emitted | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | VariantId: {VariantId} | SKU: {SKU} | UnitPrice: {UnitPrice} | CorrelationId: {CorrelationId}",
                cart.TenantId, cart.CartId, cartItem.ProductId, cartItem.VariantId, cartItem.SKU, cartItem.UnitPrice, correlationId);
        }
        catch (OperationCanceledException)
        {
            // CRITICAL: Do not swallow cancellation - lock may have been lost
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to emit CartItemAddedEvent (best-effort, continuing) | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                cart.TenantId, cart.CartId, cartItem.ProductId, cartItem.VariantId, correlationId);
            // Best-effort: Swallow exception because cart was already saved to Redis
            // Throwing would cause inconsistent state where cart is mutated but client sees error
        }
    }

    private async Task EmitCartItemUpdatedEventAsync(
        CartDto cart,
        Guid productId,
        Guid? variantId,
        string? variantName,
        int previousQuantity,
        int newQuantity,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var evt = new CartItemUpdatedEvent
            {
                TenantId = cart.TenantId,
                CartId = cart.CartId,
                OwnerType = cart.OwnerType,
                OwnerId = cart.OwnerId,
                ProductId = productId,
                VariantId = variantId,
                VariantName = variantName,
                PreviousQuantity = previousQuantity,
                NewQuantity = newQuantity,
                CorrelationId = correlationId,
                IdempotencyKey = $"cart-item-updated:{cart.CartId}:{productId}:{variantId}:{previousQuantity}:{newQuantity}:{correlationId}",
                OccurredAtUtc = DateTime.UtcNow
            };

            await PublishCartEventAsync(evt, cancellationToken);

            _logger.LogDebug(
                "CartItemUpdatedEvent emitted | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                cart.TenantId, cart.CartId, productId, variantId, correlationId);
        }
        catch (OperationCanceledException)
        {
            // CRITICAL: Do not swallow cancellation - lock may have been lost
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to emit CartItemUpdatedEvent (best-effort, continuing) | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                cart.TenantId, cart.CartId, productId, variantId, correlationId);
            // Best-effort: Swallow exception because cart was already saved to Redis
            // Throwing would cause inconsistent state where cart is mutated but client sees error
        }
    }

    private async Task EmitCartItemRemovedEventAsync(
        CartDto cart,
        Guid productId,
        Guid? variantId,
        string? variantName,
        int quantityRemoved,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var evt = new CartItemRemovedEvent
            {
                TenantId = cart.TenantId,
                CartId = cart.CartId,
                OwnerType = cart.OwnerType,
                OwnerId = cart.OwnerId,
                ProductId = productId,
                VariantId = variantId,
                VariantName = variantName,
                QuantityRemoved = quantityRemoved,
                CorrelationId = correlationId,
                IdempotencyKey = $"cart-item-removed:{cart.CartId}:{productId}:{variantId}:{quantityRemoved}:{correlationId}",
                OccurredAtUtc = DateTime.UtcNow
            };

            await PublishCartEventAsync(evt, cancellationToken);

            _logger.LogDebug(
                "CartItemRemovedEvent emitted | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                cart.TenantId, cart.CartId, productId, variantId, correlationId);
        }
        catch (OperationCanceledException)
        {
            // CRITICAL: Do not swallow cancellation - lock may have been lost
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to emit CartItemRemovedEvent (best-effort, continuing) | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                cart.TenantId, cart.CartId, productId, variantId, correlationId);
            // Best-effort: Swallow exception because cart was already saved to Redis
            // Throwing would cause inconsistent state where cart is mutated but client sees error
        }
    }

    private async Task EmitCartClearedEventAsync(
        Guid tenantId,
        Guid cartId,
        CartOwner owner,
        int itemsCleared,
        decimal totalValueCleared,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var evt = new CartClearedEvent
            {
                TenantId = tenantId,
                CartId = cartId,
                OwnerType = owner.OwnerType,
                OwnerId = owner.OwnerId,
                ItemsCleared = itemsCleared,
                TotalValueCleared = totalValueCleared,
                CorrelationId = correlationId,
                IdempotencyKey = $"cart-cleared:{cartId}:{itemsCleared}:{totalValueCleared:F2}:{correlationId}",
                OccurredAtUtc = DateTime.UtcNow
            };

            await PublishCartEventAsync(evt, cancellationToken);

            _logger.LogDebug(
                "CartClearedEvent emitted | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                tenantId, cartId, correlationId);
        }
        catch (OperationCanceledException)
        {
            // CRITICAL: Do not swallow cancellation - lock may have been lost
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to emit CartClearedEvent (best-effort, continuing) | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                tenantId, cartId, correlationId);
            // Best-effort: Swallow exception because cart was already deleted from Redis
            // Throwing would cause inconsistent state where cart is cleared but client sees error
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// PHASE 10.4.X: Creates an empty cart with currency from StorefrontConfig.
    /// Throws CurrencyNotConfiguredException if currency is not configured.
    /// </summary>
    private async Task<CartDto> CreateEmptyCartAsync(
        Guid tenantId,
        CartOwner owner,
        CancellationToken cancellationToken)
    {
        // Fetch tenant currency from StorefrontConfig
        var currency = await GetStorefrontCurrencyAsync(tenantId, cancellationToken);

        return new CartDto
        {
            CartId = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerType = owner.OwnerType,
            OwnerId = owner.OwnerId,
            Items = new List<CartItemDto>(),
            Subtotal = 0,
            DiscountTotal = 0,
            TaxTotal = 0,
            GrandTotal = 0,
            Currency = currency,
            LastUpdatedUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(CartTtlHours)
        };
    }

    /// <summary>
    /// Gets storefront currency from current tenant SQL authority.
    /// Throws CurrencyNotConfiguredException if not configured - NO FALLBACKS.
    /// </summary>
    internal async Task<string> GetStorefrontCurrencyAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        try
        {
            var tenant = await PointOfSale.Infrastructure.Tenancy.TenantAuthorityMemo
                .GetTenantByIdAsync(_dbContext, tenantId, cancellationToken);
            var currency = tenant?.Currency;

            if (!string.IsNullOrWhiteSpace(currency))
            {
                return currency.Trim().ToUpperInvariant();
            }
            
            _reliabilityTelemetry.RecordCurrencyProjectionMismatch("tenant_sql", "missing_currency");
            _logger.LogError(
                "Currency not configured in tenant SQL authority | TenantId: {TenantId}",
                tenantId);
            throw new CurrencyNotConfiguredException(tenantId);
        }
        catch (CurrencyNotConfiguredException)
        {
            throw; // Re-throw our own exception
        }
        catch (Exception ex)
        {
            _reliabilityTelemetry.RecordCurrencyProjectionMismatch("tenant_sql", "currency_lookup_failed");
            _logger.LogError(ex, 
                "Failed to fetch storefront currency from tenant SQL authority | TenantId: {TenantId}",
                tenantId);
            throw new CurrencyNotConfiguredException(tenantId);
        }
    }

    /// <summary>
    /// Formats variant name from VariantAttributes for event emission.
    /// Returns null if no attributes exist.
    /// </summary>
    private static string? FormatVariantName(List<CartVariantAttribute>? variantAttributes)
    {
        return variantAttributes?.Count > 0
            ? string.Join(" / ", variantAttributes.Select(a => a.Value))
            : null;
    }

    private sealed record AuthoritativeVariantStockSnapshot(
        Guid VariantId,
        Guid ProductId,
        string? SKU,
        int AvailableStock,
        DateTime? StockUpdatedAtUtc);

    #endregion
}

#region Custom Exceptions

/// <summary>
/// Exception thrown when a product is not found or not available.
/// </summary>
public class ProductNotFoundException : Exception
{
    public Guid ProductId { get; }

    public ProductNotFoundException(Guid productId) 
        : base($"Product with ID '{productId}' not found or not available")
    {
        ProductId = productId;
    }
}

/// <summary>
/// Exception thrown when Redis is unavailable.
/// </summary>
public class RedisUnavailableException : Exception
{
    public RedisUnavailableException() 
        : base("Redis service is temporarily unavailable")
    {
    }
}

/// <summary>
/// Exception thrown when a cart write cannot acquire the cart mutation lock.
/// HTTP callers should return 409 and refetch the cart before retrying.
/// </summary>
public class CartConcurrencyConflictException : Exception
{
    public CartConcurrencyConflictException()
        : base("Cart is being updated by another request. Refetch the cart and retry the action.")
    {
    }

    public CartConcurrencyConflictException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Exception thrown when cart limits are exceeded (Phase 3.2).
/// </summary>
public class CartLimitExceededException : Exception
{
    public CartLimitExceededException(string message) 
        : base(message)
    {
    }
}

/// <summary>
/// Exception thrown when currency mismatch occurs (Phase 3.2).
/// </summary>
public class CurrencyMismatchException : Exception
{
    public string CartCurrency { get; }
    public string ProductCurrency { get; }

    public CurrencyMismatchException(string cartCurrency, string productCurrency) 
        : base($"Currency mismatch: cart uses '{cartCurrency}' but product uses '{productCurrency}'")
    {
        CartCurrency = cartCurrency;
        ProductCurrency = productCurrency;
    }
}

/// <summary>
/// Exception thrown when cart is not found (Phase C2).
/// </summary>
public class CartNotFoundException : Exception
{
    public Guid TenantId { get; }
    public string OwnerId { get; }

    public CartNotFoundException(Guid tenantId, string ownerId) 
        : base($"Cart not found for tenant '{tenantId}' and owner '{ownerId}'")
    {
        TenantId = tenantId;
        OwnerId = ownerId;
    }
}

/// <summary>
/// Exception thrown when a product has no valid selling price.
/// This occurs when a product has no DefaultUnit configuration or price = 0.
/// </summary>
public class ProductNotForSaleException : Exception
{
    public Guid ProductId { get; }

    public ProductNotForSaleException(Guid productId) 
        : base($"Product '{productId}' is not configured for sale (no valid price)")
    {
        ProductId = productId;
    }
}

/// <summary>
/// Exception thrown when a product is out of stock.
/// Used when TrackStock = true and AllowBackorder = false and StockQuantity = 0.
/// </summary>
public class ProductOutOfStockException : Exception
{
    public Guid ProductId { get; }
    public string ProductName { get; }

    public ProductOutOfStockException(Guid productId, string productName) 
        : base($"Product '{productName}' is currently out of stock")
    {
        ProductId = productId;
        ProductName = productName;
    }
}

// =============================================================================
// PHASE 11.4 — VARIANT-SPECIFIC EXCEPTIONS
// =============================================================================

/// <summary>
/// Exception thrown when a variant is not found within a product.
/// PHASE 11.4: Variant is the sellable SKU.
/// </summary>
public class VariantNotFoundException : Exception
{
    public Guid ProductId { get; }
    public Guid VariantId { get; }

    public VariantNotFoundException(Guid productId, Guid variantId) 
        : base($"Variant '{variantId}' not found for product '{productId}'")
    {
        ProductId = productId;
        VariantId = variantId;
    }
}

/// <summary>
/// Exception thrown when a variant exists but is inactive.
/// PHASE 11.4: Inactive variants cannot be added to cart.
/// </summary>
public class VariantInactiveException : Exception
{
    public Guid VariantId { get; }

    public VariantInactiveException(Guid variantId) 
        : base($"Variant '{variantId}' is inactive and cannot be purchased")
    {
        VariantId = variantId;
    }
}

/// <summary>
/// Exception thrown when a variant is out of stock.
/// PHASE 11.4: Stock is tracked per-Variant (TotalStock).
/// </summary>
public class VariantOutOfStockException : Exception
{
    public Guid VariantId { get; }
    public string SKU { get; }

    public VariantOutOfStockException(Guid variantId, string sku) 
        : base($"Variant '{sku}' is currently out of stock")
    {
        VariantId = variantId;
        SKU = sku;
    }
}

/// <summary>
/// Exception thrown when a variant is required but not provided.
/// PHASE 11.6: Products with HasVariants = true MUST have variantId in cart requests.
/// </summary>
public class VariantRequiredException : Exception
{
    public Guid ProductId { get; }
    public string ProductName { get; }

    public VariantRequiredException(Guid productId, string productName) 
        : base($"Product '{productName}' requires variant selection. Please select product options before adding to cart.")
    {
        ProductId = productId;
        ProductName = productName;
    }
}

/// <summary>
/// Exception thrown when a cart item is not found by VariantId.
/// PHASE 11.7: Cart items are identified ONLY by VariantId.
/// </summary>
public class CartItemNotFoundException : Exception
{
    public Guid VariantId { get; }

    public CartItemNotFoundException(Guid variantId) 
        : base($"Cart item with variant '{variantId}' not found")
    {
        VariantId = variantId;
    }
}

/// <summary>
/// Exception thrown when requested quantity exceeds available variant stock.
/// PHASE 11.7: Stock validation before quantity update.
/// </summary>
public class VariantInsufficientStockException : Exception
{
    public Guid VariantId { get; }
    public string SKU { get; }
    public int RequestedQuantity { get; }
    public int AvailableStock { get; }

    public VariantInsufficientStockException(Guid variantId, string sku, int requestedQuantity, int availableStock) 
        : base($"Only {availableStock} items available in stock for '{sku}'")
    {
        VariantId = variantId;
        SKU = sku;
        RequestedQuantity = requestedQuantity;
        AvailableStock = availableStock;
    }
}

#endregion

