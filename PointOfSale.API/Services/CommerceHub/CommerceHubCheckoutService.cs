using System.Security.Cryptography;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services.Governance;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Redis.Services;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Pricing;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubCheckoutService
{
    Task<CommerceHubCheckoutResponseDto> CheckoutAsync(
        CommerceHubCheckoutRequestDto request,
        Guid customerId,
        string correlationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public class CommerceHubCheckoutException : Exception
{
    public CommerceHubCheckoutException(
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

public class CommerceHubCheckoutService : ICommerceHubCheckoutService
{
    private const int CurrencyScale = 2;
    private static readonly TimeSpan CartLockTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan InventoryReservationTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IdempotencyReplayWaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdempotencyReplayPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly JsonSerializerOptions CartJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubCartService _cartService;
    private readonly IProductVariantStockRepository _variantStockRepository;
    private readonly IInventoryConsistencyService _inventoryConsistencyService;
    private readonly ICommerceHubShippingService _shippingService;
    private readonly ICommissionService _commissionService;
    private readonly IPromoService _promoService;
    private readonly ICommerceHubVendorFinancialControlService _vendorFinancialControlService;
    private readonly ICommerceHubOrderAccessService _orderAccessService;
    private readonly IUserGovernanceService _userGovernanceService;
    private readonly ICommerceHubFinancialReadinessGate _financialReadinessGate;
    private readonly IDistributedLockService _lockService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ICommerceHubPriceAuthority _priceAuthority;
    private readonly IReadOnlyCollection<IPaymentProviderAdapter> _paymentProviderAdapters;
    private readonly TenantContext _tenantContext;
    private readonly ILogger<CommerceHubCheckoutService> _logger;

    public CommerceHubCheckoutService(
        ApplicationDbContext dbContext,
        ICommerceHubCartService cartService,
        IProductVariantStockRepository variantStockRepository,
        IInventoryConsistencyService inventoryConsistencyService,
        ICommerceHubShippingService shippingService,
        ICommissionService commissionService,
        IPromoService promoService,
        ICommerceHubVendorFinancialControlService vendorFinancialControlService,
        ICommerceHubOrderAccessService orderAccessService,
        IUserGovernanceService userGovernanceService,
        ICommerceHubFinancialReadinessGate financialReadinessGate,
        IDistributedLockService lockService,
        IEventPublisher eventPublisher,
        ICommerceHubPriceAuthority priceAuthority,
        IEnumerable<IPaymentProviderAdapter> paymentProviderAdapters,
        TenantContext tenantContext,
        ILogger<CommerceHubCheckoutService> logger)
    {
        _dbContext = dbContext;
        _cartService = cartService;
        _variantStockRepository = variantStockRepository;
        _inventoryConsistencyService = inventoryConsistencyService;
        _shippingService = shippingService;
        _commissionService = commissionService;
        _promoService = promoService;
        _vendorFinancialControlService = vendorFinancialControlService;
        _orderAccessService = orderAccessService;
        _userGovernanceService = userGovernanceService;
        _financialReadinessGate = financialReadinessGate;
        _lockService = lockService;
        _eventPublisher = eventPublisher;
        _priceAuthority = priceAuthority;
        _paymentProviderAdapters = paymentProviderAdapters.ToList();
        _tenantContext = tenantContext;
        _logger = logger;
    }

    private bool IsPostgresProvider() =>
        _dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _dbContext.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    public async Task<CommerceHubCheckoutResponseDto> CheckoutAsync(
        CommerceHubCheckoutRequestDto request,
        Guid customerId,
        string correlationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        using var globalTenantScope = _tenantContext.BeginGlobalScope();
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var shippingAssignmentBoundary = CommerceHubBoundaryMutationScope.BeginShippingAssignment();
        using var financialBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();
        using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();

        if (customerId == Guid.Empty)
        {
            throw new CommerceHubCheckoutException(
                "CUSTOMER_REQUIRED",
                "A valid customer account is required to place a CommerceHub order.",
                StatusCodes.Status401Unauthorized);
        }

        var normalizedIdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
        if (normalizedIdempotencyKey == null)
        {
            throw new CommerceHubCheckoutException(
                "IDEMPOTENCY_KEY_REQUIRED",
                "Idempotency key is required for CommerceHub checkout.",
                StatusCodes.Status400BadRequest);
        }

        try
        {
            await _userGovernanceService.EnsureCustomerCheckoutAllowedAsync(customerId, cancellationToken);
        }
        catch (UserGovernanceException exception)
        {
            throw new CommerceHubCheckoutException(
                exception.Code,
                exception.Message,
                exception.StatusCode);
        }

        var requestValidation = request.Validate();
        if (requestValidation.Code != null)
        {
            throw new CommerceHubCheckoutException(
                requestValidation.Code,
                requestValidation.Message ?? "CommerceHub checkout request is invalid.",
                StatusCodes.Status400BadRequest);
        }

        var requestHash = BuildCheckoutRequestHash(customerId, request);

        var checkoutStores = request.Stores;

        var requestedLines = checkoutStores
            .SelectMany(
                store => store.Items.Select(item => new RequestedCheckoutLine(
                    store.StoreId,
                    item.ListingId,
                    item.ProductId,
                    item.VariantId,
                    item.Quantity,
                    RoundCurrency(item.ExpectedUnitPrice!.Value))))
            .GroupBy(line => new { line.StoreId, line.ListingId, line.ProductId, line.VariantId })
            .Select(group =>
            {
                var expectedPrices = group
                    .Select(item => item.ExpectedUnitPrice)
                    .Distinct()
                    .ToList();

                if (expectedPrices.Count != 1)
                {
                    throw new CommerceHubCheckoutException(
                        "EXPECTED_PRICE_CONFLICT",
                        "Cart contains conflicting price snapshots for the same item. Please refresh cart.",
                        StatusCodes.Status409Conflict,
                        new
                        {
                            group.Key.ListingId,
                            group.Key.ProductId,
                            group.Key.VariantId
                        });
                }

                return new RequestedCheckoutLine(
                    group.Key.StoreId,
                    group.Key.ListingId,
                    group.Key.ProductId,
                    group.Key.VariantId,
                    group.Sum(item => item.Quantity),
                    expectedPrices[0]);
            })
            .ToList();

        if (requestedLines.Count == 0)
        {
            throw new CommerceHubCheckoutException("CART_EMPTY", "Your cart is empty.", StatusCodes.Status400BadRequest);
        }

        var customerCheckoutLockKey = BuildCustomerCheckoutLockKey(customerId);
        var checkoutLockKey = BuildCheckoutLockKey(customerId, normalizedIdempotencyKey);
        var checkoutLockToken = Guid.NewGuid().ToString("N");
        var customerCheckoutLockAcquired = false;
        var idempotencyCheckoutLockAcquired = false;

        var customerCheckoutLockResult = await _lockService.TryAcquireLockAsync(
            customerCheckoutLockKey,
            checkoutLockToken,
            CartLockTimeout);

        if (customerCheckoutLockResult.IsInfrastructureFailure)
        {
            throw new CommerceHubCheckoutException(
                "LOCK_INFRASTRUCTURE_FAILURE",
                "CommerceHub checkout is temporarily unavailable because the lock infrastructure could not be reached.",
                StatusCodes.Status503ServiceUnavailable);
        }

        if (!customerCheckoutLockResult.IsAcquired)
        {
            var replay = await TryGetPersistedCheckoutResponseAsync(normalizedIdempotencyKey, requestHash, cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            throw CreateCheckoutInProgressException();
        }

        customerCheckoutLockAcquired = true;

        var checkoutLockResult = await _lockService.TryAcquireLockAsync(
            checkoutLockKey,
            checkoutLockToken,
            CartLockTimeout);

        if (checkoutLockResult.IsInfrastructureFailure)
        {
            await _lockService.ReleaseLockAsync(customerCheckoutLockKey, checkoutLockToken);
            customerCheckoutLockAcquired = false;

            throw new CommerceHubCheckoutException(
                "LOCK_INFRASTRUCTURE_FAILURE",
                "CommerceHub checkout is temporarily unavailable because the lock infrastructure could not be reached.",
                StatusCodes.Status503ServiceUnavailable);
        }

        if (!checkoutLockResult.IsAcquired)
        {
            var replay = await TryGetPersistedCheckoutResponseAsync(normalizedIdempotencyKey, requestHash, cancellationToken);
            if (replay != null)
            {
                await _lockService.ReleaseLockAsync(customerCheckoutLockKey, checkoutLockToken);
                customerCheckoutLockAcquired = false;
                return replay;
            }

            await _lockService.ReleaseLockAsync(customerCheckoutLockKey, checkoutLockToken);
            customerCheckoutLockAcquired = false;
            throw CreateCheckoutInProgressException();
        }

        idempotencyCheckoutLockAcquired = true;

        try
        {
            var persistedReplay = await TryGetPersistedCheckoutResponseAsync(normalizedIdempotencyKey, requestHash, cancellationToken);
            if (persistedReplay != null)
            {
                _logger.LogInformation(
                    "CommerceHub checkout replay served from persisted idempotency response | IdempotencyKey: {IdempotencyKey} | CorrelationId: {CorrelationId}",
                    normalizedIdempotencyKey,
                    correlationId);
                return persistedReplay;
            }

            var existingOrder = await LoadMarketplaceOrderByIdempotencyKeyAsync(normalizedIdempotencyKey, cancellationToken);
            if (existingOrder != null)
            {
                await EnsureExistingCheckoutOrderReplayIsRequestHashBoundAsync(
                    normalizedIdempotencyKey,
                    requestHash,
                    cancellationToken);

                var replayResponse = BuildResponse(existingOrder, true);
                await PersistCheckoutResponseIfMissingAsync(
                    normalizedIdempotencyKey,
                    requestHash,
                    replayResponse,
                    customerId,
                    cancellationToken);

                _logger.LogInformation(
                    "CommerceHub checkout replay hit from existing order | OrderNumber: {OrderNumber} | IdempotencyKey: {IdempotencyKey} | CorrelationId: {CorrelationId}",
                    existingOrder.OrderNumber,
                    normalizedIdempotencyKey,
                    correlationId);

                return replayResponse;
            }

            CommerceHubCheckoutIdempotency? idempotencyRecord = null;
            CommerceHubCart? lockedCart = null;

            for (var attempt = 0; attempt < 2 && idempotencyRecord == null; attempt++)
            {
                idempotencyRecord = new CommerceHubCheckoutIdempotency
                {
                    Id = Guid.NewGuid(),
                    Key = normalizedIdempotencyKey,
                    RequestHash = requestHash,
                    TenantId = CommerceHubCartOwnerContext.CommerceHubCartTenantScopeId,
                    CommerceHubCustomerId = customerId,
                    OnlineCustomerId = null,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _dbContext.CommerceHubCheckoutIdempotency.Add(idempotencyRecord);

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (IsCheckoutIdempotencyUniqueConstraintViolation(ex))
                {
                    _dbContext.ChangeTracker.Clear();

                    var replayResponse = await WaitForCheckoutReplayAsync(normalizedIdempotencyKey, requestHash, cancellationToken);
                    if (replayResponse != null)
                    {
                        _logger.LogInformation(
                            "CommerceHub checkout replay returned while waiting on active idempotency claim | IdempotencyKey: {IdempotencyKey} | CorrelationId: {CorrelationId}",
                            normalizedIdempotencyKey,
                            correlationId);
                        return replayResponse;
                    }

                    idempotencyRecord = null;
                }
            }

            if (idempotencyRecord == null)
            {
                throw CreateCheckoutInProgressException();
            }

            var cartOwnerContext = await ResolveAuthenticatedCartOwnerContextAsync(customerId, cancellationToken);
            try
            {
                lockedCart = await LockCartForCheckoutAsync(
                    cartOwnerContext,
                    request.CartVersion!.Value,
                    cancellationToken);
                EnsureLockedCartMatchesCheckoutRequest(lockedCart, requestedLines);
            }
            catch
            {
                await CleanupAfterCheckoutFailureAsync(
                    idempotencyRecord.Id,
                    lockedCart?.Id,
                    CancellationToken.None);
                throw;
            }

            IDbContextTransaction? transaction = null;
            try
            {
                transaction = await _dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

                var listingIds = requestedLines.Select(line => line.ListingId).Distinct().ToList();
                var listings = await LoadCommerceHubListingsForCheckoutAuthorityAsync(
                    listingIds,
                    cancellationToken);

            if (listings.Count != listingIds.Count)
            {
                throw new CommerceHubCheckoutException(
                    "LISTING_NOT_FOUND",
                    "One or more cart items are no longer available.",
                    StatusCodes.Status409Conflict);
            }

            var liveCategoryIds = await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
                _dbContext,
                listings.Values.Select(listing => listing.CommerceHubCategoryId),
                cancellationToken);
            if (listings.Values.Any(listing => !liveCategoryIds.Contains(listing.CommerceHubCategoryId)))
            {
                throw new CommerceHubCheckoutException(
                    "CATEGORY_UNAVAILABLE",
                    "One or more cart items no longer belong to a live CommerceHub category.",
                    StatusCodes.Status409Conflict);
            }

            var variantIds = requestedLines.Select(line => line.VariantId).Distinct().ToList();
            var variants = await LoadProductVariantsForCheckoutAuthorityAsync(
                variantIds,
                cancellationToken);

            if (variants.Count != variantIds.Count)
            {
                throw new CommerceHubCheckoutException(
                    "VARIANT_NOT_FOUND",
                    "One or more cart variants are no longer available.",
                    StatusCodes.Status409Conflict);
            }

            var productIds = requestedLines.Select(line => line.ProductId).Distinct().ToList();
            var products = await LoadProductsForCheckoutAuthorityAsync(
                productIds,
                cancellationToken);

            if (products.Count != productIds.Count)
            {
                throw new CommerceHubCheckoutException(
                    "PRODUCT_NOT_FOUND",
                    "One or more cart products are no longer available.",
                    StatusCodes.Status409Conflict);
            }

            var tenantIds = listings.Values.Select(listing => listing.TenantId).Distinct().ToList();
            var tenants = await LoadTenantsForCheckoutAuthorityAsync(
                tenantIds,
                cancellationToken);

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

            await _financialReadinessGate.EnsureCommerceIntakeAllowedAsync(
                tenantIds,
                "CommerceHubCheckout.Create",
                cancellationToken);

            var suspendedVendorIds = await _vendorFinancialControlService.GetVendorsSuspendedFromNewOrdersAsync(
                tenantIds,
                cancellationToken);

            if (suspendedVendorIds.Count > 0)
            {
                var suspendedStores = suspendedVendorIds
                    .Select(vendorId => new
                    {
                        vendorId,
                        vendorName = tenants.TryGetValue(vendorId, out var tenant) ? tenant.Name : vendorId.ToString()
                    })
                    .ToList();

                throw new CommerceHubCheckoutException(
                    "VENDOR_SUSPENDED_FOR_SETTLEMENT",
                    "One or more stores are temporarily suspended from new CommerceHub orders due to outstanding settlement debt.",
                    StatusCodes.Status409Conflict,
                    new { suspendedStores });
            }

            foreach (var tenantId in tenantIds)
            {
                try
                {
                    await _userGovernanceService.EnsureVendorOrderAcceptanceAllowedAsync(tenantId, cancellationToken);
                }
                catch (UserGovernanceException exception)
                {
                    throw new CommerceHubCheckoutException(
                        exception.Code,
                        exception.Message,
                        exception.StatusCode,
                        new { vendorId = tenantId });
                }
            }

            ValidateRequestedPaymentMethod(request.PaymentMethod, tenantIds, _paymentProviderAdapters);

            var currency = ResolveCommerceHubAuthorityCurrency();
            var lineSnapshots = new List<ValidatedCheckoutLine>();

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
                var roundedUnitPrice = RoundCurrency(priceResolution.FinalPrice);
                if (requestedLine.ExpectedUnitPrice != roundedUnitPrice)
                {
                    throw new CommerceHubCheckoutException(
                        "PRICE_CHANGED",
                        $"{product.Name} price changed. Please refresh cart before checkout.",
                        StatusCodes.Status409Conflict,
                        new
                        {
                            requestedLine.ListingId,
                            requestedLine.ProductId,
                            requestedLine.VariantId,
                            expectedUnitPrice = requestedLine.ExpectedUnitPrice,
                            currentUnitPrice = roundedUnitPrice
                        });
                }

                var variantName = BuildVariantName(variant);
                var variantAttributesJson = variant.Attributes.Count == 0
                    ? null
                    : System.Text.Json.JsonSerializer.Serialize(
                        variant.Attributes
                            .OrderBy(attribute => attribute.SortOrder)
                            .Select(attribute => new { attribute.Name, attribute.Value }));

                // T-08: freeze the tenant-scoped moving-weighted-average merchandise
                // cost at checkout commit. ValidateLineIntegrity above already proved
                // variant.TenantId == listing.TenantId == requested store.
                var unitCostSnapshot = variant.CostPrice;
                if (unitCostSnapshot < 0m)
                {
                    throw new CommerceHubCheckoutException(
                        "INVALID_VARIANT_COST",
                        $"{product.Name} has an invalid merchandise cost and cannot be checked out.",
                        StatusCodes.Status409Conflict);
                }

                lineSnapshots.Add(new ValidatedCheckoutLine(
                    listing.TenantId,
                    requestedLine.ListingId,
                    requestedLine.ProductId,
                    requestedLine.VariantId,
                    requestedLine.Quantity,
                    roundedUnitPrice,
                    unitCostSnapshot,
                    listing.CommerceHubCategoryId,
                    product.Name,
                    variantName,
                    variantAttributesJson,
                    ResolveImageUrl(variant, product),
                    product.Weight));
            }

            var requestedSelectionsByStore = request.ShippingSelections
                .Where(selection => selection.StoreId != Guid.Empty && selection.ShippingOptionId != Guid.Empty)
                .GroupBy(selection => selection.StoreId)
                .ToDictionary(group => group.Key, group => group.Last());

            var promoValidation = await _promoService.ValidatePromoForCheckoutAsync(
                request.PromoCode,
                lineSnapshots
                    .Select(line =>
                    {
                        var commissionRate = RoundCommissionRate(
                            ResolveCommissionForCheckout(line.StoreId, line.CategoryId).Rate);
                        return new PromoCommissionLine(
                            line.VariantId,
                            line.StoreId,
                            line.CategoryId,
                            CommissionCalculator.RoundAmount(line.Total * commissionRate));
                    })
                    .ToList(),
                customerId,
                cancellationToken);

            var requestedSelectionIds = requestedSelectionsByStore
                .ToDictionary(selection => selection.Key, selection => selection.Value.ShippingOptionId);

            var shippingResult = await _shippingService.CalculateAsync(
                lineSnapshots
                    .GroupBy(line => line.StoreId)
                    .Select(group => new CommerceHubShippingStoreRequest
                    {
                        StoreId = group.Key,
                        Subtotal = group.Sum(line => line.Total),
                        TotalWeightKg = group.Sum(line => line.TotalWeight)
                    })
                    .ToList(),
                requestedSelectionIds,
                cancellationToken,
                bypassCache: true);

            var requestedShippingTotal = RoundCurrency(request.ShippingTotal ?? 0m);
            if (requestedShippingTotal != shippingResult.TotalShipping)
            {
                throw new CommerceHubCheckoutException(
                    "SHIPPING_CHANGED",
                    "Shipping price has changed, please refresh cart.",
                    StatusCodes.Status409Conflict,
                    new
                    {
                        requestedShippingTotal,
                        recalculatedShippingTotal = shippingResult.TotalShipping
                    });
            }

            foreach (var storeSelection in shippingResult.StoreSelections)
            {
                if (!requestedSelectionsByStore.TryGetValue(storeSelection.StoreId, out var requestedStoreSelection))
                {
                    throw new CommerceHubCheckoutException(
                        "SHIPPING_INVALID",
                        $"Missing shipping selection for store {storeSelection.StoreId}.",
                        StatusCodes.Status400BadRequest,
                        new { storeId = storeSelection.StoreId });
                }

                var requestedShippingPrice = RoundCurrency(requestedStoreSelection.ShippingPrice);
                if (requestedStoreSelection.ShippingOptionId != storeSelection.ShippingOptionId ||
                    requestedShippingPrice != storeSelection.ShippingPrice)
                {
                    throw new CommerceHubCheckoutException(
                        "SHIPPING_CHANGED",
                        $"Shipping changed for store {storeSelection.StoreId}, please refresh cart.",
                        StatusCodes.Status409Conflict,
                        new
                        {
                            storeId = storeSelection.StoreId,
                            requestedShippingOptionId = requestedStoreSelection.ShippingOptionId,
                            recalculatedShippingOptionId = storeSelection.ShippingOptionId,
                            requestedShippingPrice,
                            recalculatedShippingPrice = storeSelection.ShippingPrice
                        });
                }
            }

            var marketplaceOrderNumber = await GenerateMarketplaceOrderNumberAsync(cancellationToken);
            var nowUtc = DateTime.UtcNow;

            var marketplaceOrder = BuildMarketplaceOrder(
                marketplaceOrderNumber,
                request,
                customerId,
                normalizedIdempotencyKey,
                currency,
                promoValidation,
                lineSnapshots,
                tenants,
                shippingResult,
                nowUtc);

            if (promoValidation != null && marketplaceOrder.PromoDiscountTotal > 0m)
            {
                var promoConsumption = await _promoService.ConsumePromoAsync(
                    promoValidation.Promo,
                    marketplaceOrder.Id,
                    customerId,
                    marketplaceOrder.PromoDiscountTotal,
                    promoValidation.EligibleBaseCommissionAmount,
                    correlationId,
                    cancellationToken);

                if (!promoConsumption.IsApplied)
                {
                    _logger.LogWarning(
                        "CommerceHub promo claim failed during checkout finalization; aborting checkout to prevent silent repricing | OrderNumber: {OrderNumber} | PromoCode: {PromoCode} | SkipCode: {SkipCode} | CorrelationId: {CorrelationId}",
                        marketplaceOrder.OrderNumber,
                        promoValidation.Promo.Code,
                        promoConsumption.SkipCode,
                        correlationId);

                    throw new CommerceHubCheckoutException(
                        promoConsumption.SkipCode ?? "PROMO_CONSUMPTION_FAILED",
                        "The promo could not be applied anymore. Please refresh checkout before placing the order.",
                        StatusCodes.Status409Conflict);
                }
            }

            foreach (var subOrder in marketplaceOrder.SubOrders)
            {
                foreach (var item in subOrder.Items)
                {
                    DateTime? reservationExpiresAtUtc = subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery
                        ? null
                        : DateTime.UtcNow.Add(InventoryReservationTtl);
                    var reserved = await _inventoryConsistencyService.ReserveOrderItemAsync(
                        new InventoryReservationRequest(
                            TenantId: subOrder.StoreId,
                            OrderId: marketplaceOrder.Id,
                            SubOrderId: subOrder.Id,
                            OrderItemId: item.Id,
                            ProductId: item.ProductId,
                            VariantId: item.VariantId,
                            Quantity: item.Quantity,
                            WarehouseId: null,
                            ExpiresAtUtc: reservationExpiresAtUtc,
                            EventId: InventoryEventIdFactory.Create("commercehub-checkout-reserve", item.Id),
                            CorrelationId: correlationId,
                            Source: "CommerceHubCheckout"),
                        cancellationToken);

                    if (!reserved)
                    {
                        throw new CommerceHubCheckoutException(
                            "OUT_OF_STOCK",
                            $"Stock changed while checking out {item.ProductName}. Please review your cart and try again.",
                            StatusCodes.Status409Conflict,
                            new
                            {
                                item.ListingId,
                                item.VariantId,
                                requested = item.Quantity
                            });
                    }
                }
            }

                _dbContext.CommerceHubMarketplaceOrders.Add(marketplaceOrder);

                if (lockedCart != null)
                {
                    _dbContext.CommerceHubCarts.Remove(lockedCart);
                }

                var response = BuildResponse(marketplaceOrder, false);
                idempotencyRecord.ResponseJson = SerializeCheckoutResponse(response);

                await StageMarketplaceOrderCreatedAsync(
                    marketplaceOrder,
                    correlationId,
                    normalizedIdempotencyKey,
                    cancellationToken);

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "CommerceHub checkout created | OrderNumber: {OrderNumber} | Stores: {StoreCount} | Total: {Total} | PaymentMethod: {PaymentMethod} | IdempotencyKey: {IdempotencyKey} | CorrelationId: {CorrelationId}",
                    marketplaceOrder.OrderNumber,
                    marketplaceOrder.SubOrders.Count,
                    marketplaceOrder.TotalAmount,
                    marketplaceOrder.PaymentMethodCode,
                    normalizedIdempotencyKey,
                    correlationId);

                return response;
            }
            catch
            {
                if (transaction != null)
                {
                    try
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch (Exception rollbackException)
                    {
                        _logger.LogWarning(
                            rollbackException,
                            "CommerceHub checkout transaction rollback failed | IdempotencyRecordId: {IdempotencyRecordId} | CorrelationId: {CorrelationId}",
                            idempotencyRecord.Id,
                            correlationId);
                    }

                    await transaction.DisposeAsync();
                    transaction = null;
                }

                await CleanupAfterCheckoutFailureAsync(idempotencyRecord.Id, lockedCart?.Id, CancellationToken.None);
                throw;
            }
            finally
            {
                if (transaction != null)
                {
                    await transaction.DisposeAsync();
                }
            }
        }
        finally
        {
            if (idempotencyCheckoutLockAcquired)
            {
                await _lockService.ReleaseLockAsync(checkoutLockKey, checkoutLockToken);
            }

            if (customerCheckoutLockAcquired)
            {
                await _lockService.ReleaseLockAsync(customerCheckoutLockKey, checkoutLockToken);
            }
        }
    }

    private async Task StageMarketplaceOrderCreatedAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        string correlationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var affectedStoreIds = marketplaceOrder.GetAffectedStoreIds();

        // CRITICAL: Ensure at least one valid store ID exists before publishing
        // Empty collection would cause TenantId = Guid.Empty and fail publisher validation
        if (affectedStoreIds == null || affectedStoreIds.Count == 0)
        {
            _logger.LogError(
                "Cannot publish CommerceHubMarketplaceOrderCreatedEvent - no affected store IDs found | MarketplaceOrderId: {MarketplaceOrderId} | SubOrderCount: {SubOrderCount} | CorrelationId: {CorrelationId}",
                marketplaceOrder.Id,
                marketplaceOrder.SubOrders?.Count ?? 0,
                correlationId);

            throw new InvalidOperationException(
                $"Cannot publish marketplace order created event: marketplace order {marketplaceOrder.Id} has no affected store IDs. " +
                "This indicates a data integrity issue where all sub-orders have empty StoreId values.");
        }

        await _eventPublisher.PublishAsync(new CommerceHubMarketplaceOrderCreatedEvent
        {
            MarketplaceOrderId = marketplaceOrder.Id,
            TenantId = affectedStoreIds.First(),
            AffectedStoreIds = affectedStoreIds,
            CorrelationId = correlationId,
            IdempotencyKey = idempotencyKey,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

#pragma warning disable EF1002 // SQL fragments contain normalized Guid literals only; needed for deterministic multi-row FOR UPDATE locks.
    private async Task<Dictionary<Guid, CommerceHubListing>> LoadCommerceHubListingsForCheckoutAuthorityAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken)
    {
        var orderedIds = NormalizeGuidAuthoritySet(listingIds);
        if (orderedIds.Count == 0)
        {
            return new Dictionary<Guid, CommerceHubListing>();
        }

        IQueryable<CommerceHubListing> query;
        if (IsPostgresProvider())
        {
            query = _dbContext.CommerceHubListings
                .FromSqlRaw($@"
                    SELECT *
                    FROM ""CommerceHubListings""
                    WHERE ""Id"" IN ({FormatSqlGuidList(orderedIds)})
                      AND ""IsDeleted"" = false
                    ORDER BY ""Id""
                    FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else if (IsSqlServerProvider())
        {
            query = _dbContext.CommerceHubListings
                .FromSqlRaw($@"
                    SELECT *
                    FROM [CommerceHubListings] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] IN ({FormatSqlGuidList(orderedIds)})
                      AND [IsDeleted] = CAST(0 AS bit)
                    ORDER BY [Id]")
                .IgnoreQueryFilters();
        }
        else
        {
            query = _dbContext.CommerceHubListings
                .IgnoreQueryFilters()
                .Where(listing => orderedIds.Contains(listing.Id) && !listing.IsDeleted)
                .OrderBy(listing => listing.Id);
        }

        return await query.ToDictionaryAsync(listing => listing.Id, cancellationToken);
    }

    private async Task<Dictionary<Guid, ProductVariant>> LoadProductVariantsForCheckoutAuthorityAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        var orderedIds = NormalizeGuidAuthoritySet(variantIds);
        if (orderedIds.Count == 0)
        {
            return new Dictionary<Guid, ProductVariant>();
        }

        await LockProductVariantAttributesForCheckoutAuthorityAsync(orderedIds, cancellationToken);

        IQueryable<ProductVariant> query;
        if (IsPostgresProvider())
        {
            query = _dbContext.ProductVariants
                .FromSqlRaw($@"
                    SELECT *, xmin
                    FROM ""ProductVariants""
                    WHERE ""Id"" IN ({FormatSqlGuidList(orderedIds)})
                      AND ""IsDeleted"" = false
                    ORDER BY ""Id""
                    FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else if (IsSqlServerProvider())
        {
            query = _dbContext.ProductVariants
                .FromSqlRaw($@"
                    SELECT *
                    FROM [ProductVariants] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] IN ({FormatSqlGuidList(orderedIds)})
                      AND [IsDeleted] = CAST(0 AS bit)
                    ORDER BY [Id]")
                .IgnoreQueryFilters();
        }
        else
        {
            query = _dbContext.ProductVariants
                .IgnoreQueryFilters()
                .Where(variant => orderedIds.Contains(variant.Id) && !variant.IsDeleted)
                .OrderBy(variant => variant.Id);
        }

        return await query
            .Include(variant => variant.Attributes.Where(attribute => !attribute.IsDeleted))
            .WherePubliclyVisibleLineage()
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);
    }

    private async Task LockProductVariantAttributesForCheckoutAuthorityAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        var orderedIds = NormalizeGuidAuthoritySet(variantIds);
        if (orderedIds.Count == 0)
        {
            return;
        }

        if (IsPostgresProvider())
        {
            await _dbContext.ProductVariantAttributes
                .FromSqlRaw($@"
                    SELECT *
                    FROM ""ProductVariantAttributes""
                    WHERE ""VariantId"" IN ({FormatSqlGuidList(orderedIds)})
                      AND ""IsDeleted"" = false
                    ORDER BY ""VariantId"", ""SortOrder"", ""Id""
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
            return;
        }

        if (IsSqlServerProvider())
        {
            await _dbContext.ProductVariantAttributes
                .FromSqlRaw($@"
                    SELECT *
                    FROM [ProductVariantAttributes] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [VariantId] IN ({FormatSqlGuidList(orderedIds)})
                      AND [IsDeleted] = CAST(0 AS bit)
                    ORDER BY [VariantId], [SortOrder], [Id]")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
        }
    }

    private async Task<Dictionary<Guid, Product>> LoadProductsForCheckoutAuthorityAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var orderedIds = NormalizeGuidAuthoritySet(productIds);
        if (orderedIds.Count == 0)
        {
            return new Dictionary<Guid, Product>();
        }

        IQueryable<Product> query;
        if (IsPostgresProvider())
        {
            query = _dbContext.Products
                .FromSqlRaw($@"
                    SELECT *, xmin
                    FROM ""Products""
                    WHERE ""Id"" IN ({FormatSqlGuidList(orderedIds)})
                      AND ""IsDeleted"" = false
                    ORDER BY ""Id""
                    FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else if (IsSqlServerProvider())
        {
            query = _dbContext.Products
                .FromSqlRaw($@"
                    SELECT *
                    FROM [Products] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] IN ({FormatSqlGuidList(orderedIds)})
                      AND [IsDeleted] = CAST(0 AS bit)
                    ORDER BY [Id]")
                .IgnoreQueryFilters();
        }
        else
        {
            query = _dbContext.Products
                .IgnoreQueryFilters()
                .Where(product => orderedIds.Contains(product.Id) && !product.IsDeleted)
                .OrderBy(product => product.Id);
        }

        return await query
            .Where(product =>
                product.IsActive &&
                product.IsOnlineVisible &&
                product.PublishStatus == PublishStatus.Published)
            .ToDictionaryAsync(product => product.Id, cancellationToken);
    }

    private async Task<Dictionary<Guid, Tenant>> LoadTenantsForCheckoutAuthorityAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken cancellationToken)
    {
        var orderedIds = NormalizeGuidAuthoritySet(tenantIds);
        if (orderedIds.Count == 0)
        {
            return new Dictionary<Guid, Tenant>();
        }

        IQueryable<Tenant> query;
        if (IsPostgresProvider())
        {
            query = _dbContext.Tenants
                .FromSqlRaw($@"
                    SELECT *
                    FROM ""Tenants""
                    WHERE ""Id"" IN ({FormatSqlGuidList(orderedIds)})
                      AND ""DeletedAtUtc"" IS NULL
                      AND ""IsActive"" = true
                    ORDER BY ""Id""
                    FOR UPDATE");
        }
        else if (IsSqlServerProvider())
        {
            query = _dbContext.Tenants
                .FromSqlRaw($@"
                    SELECT *
                    FROM [Tenants] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] IN ({FormatSqlGuidList(orderedIds)})
                      AND [DeletedAtUtc] IS NULL
                      AND [Status] = 1
                    ORDER BY [Id]");
        }
        else
        {
            query = _dbContext.Tenants
                .Where(tenant =>
                    orderedIds.Contains(tenant.Id) &&
                    tenant.DeletedAtUtc == null &&
                    tenant.Status == TenantStatus.Active)
                .OrderBy(tenant => tenant.Id);
        }

        return await query.ToDictionaryAsync(tenant => tenant.Id, cancellationToken);
    }

    private static List<Guid> NormalizeGuidAuthoritySet(IReadOnlyCollection<Guid> ids) =>
        ids
            .Where(id => id != Guid.Empty)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

    private static string FormatSqlGuidList(IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0)
        {
            throw new ArgumentException("At least one identifier is required.", nameof(ids));
        }

        return string.Join(", ", ids.Select(id => $"'{id:D}'"));
    }
#pragma warning restore EF1002

    private async Task<CommerceHubMarketplaceOrder?> LoadMarketplaceOrderByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubMarketplaceOrders
            .AsNoTracking()
            .Include(order => order.SubOrders.OrderBy(subOrder => subOrder.OrderNumber))
            .ThenInclude(subOrder => subOrder.Items)
            .FirstOrDefaultAsync(order => order.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    private async Task<CommerceHubCheckoutResponseDto?> TryGetPersistedCheckoutResponseAsync(
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await _dbContext.CommerceHubCheckoutIdempotency
            .FirstOrDefaultAsync(item => item.Key == idempotencyKey, cancellationToken);

        if (record == null)
        {
            return null;
        }

        EnsureCheckoutIdempotencyRequestMatches(record, requestHash);

        if (!string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            var response = DeserializeCheckoutResponse(record.ResponseJson);
            if (response != null)
            {
                response.IsIdempotentReplay = true;
                response.Message = "This checkout request was already processed, so the existing marketplace order was returned.";
                return response;
            }

            _logger.LogWarning(
                "CommerceHub checkout idempotency response JSON could not be deserialized | IdempotencyKey: {IdempotencyKey}",
                idempotencyKey);
        }

        if (IsCheckoutIdempotencyClaimExpired(record, DateTime.UtcNow))
        {
            await RemoveCheckoutIdempotencyClaimIfPendingAsync(record.Id, cancellationToken);
        }

        return null;
    }

    private async Task<CommerceHubCheckoutResponseDto?> WaitForCheckoutReplayAsync(
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var deadlineUtc = DateTime.UtcNow.Add(IdempotencyReplayWaitTimeout);

        while (DateTime.UtcNow < deadlineUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await TryGetPersistedCheckoutResponseAsync(idempotencyKey, requestHash, cancellationToken);
            if (response != null)
            {
                return response;
            }

            var hasActiveClaim = await _dbContext.CommerceHubCheckoutIdempotency
                .AsNoTracking()
                .AnyAsync(item => item.Key == idempotencyKey, cancellationToken);

            if (!hasActiveClaim)
            {
                return null;
            }

            await Task.Delay(IdempotencyReplayPollInterval, cancellationToken);
        }

        return null;
    }

    private async Task PersistCheckoutResponseIfMissingAsync(
        string idempotencyKey,
        string requestHash,
        CommerceHubCheckoutResponseDto response,
        Guid commerceHubCustomerId,
        CancellationToken cancellationToken)
    {
        var record = await _dbContext.CommerceHubCheckoutIdempotency
            .FirstOrDefaultAsync(item => item.Key == idempotencyKey, cancellationToken);

        if (record == null)
        {
            record = new CommerceHubCheckoutIdempotency
            {
                Id = Guid.NewGuid(),
                Key = idempotencyKey,
                RequestHash = requestHash,
                TenantId = CommerceHubCartOwnerContext.CommerceHubCartTenantScopeId,
                CommerceHubCustomerId = commerceHubCustomerId,
                OnlineCustomerId = null,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.CommerceHubCheckoutIdempotency.Add(record);
        }

        EnsureCheckoutIdempotencyRequestMatches(record, requestHash);

        record.TenantId ??= CommerceHubCartOwnerContext.CommerceHubCartTenantScopeId;
        record.CommerceHubCustomerId ??= commerceHubCustomerId;

        if (!string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            // Response already persisted; save RequestHash binding if it was empty (legacy row)
            if (_dbContext.Entry(record).State == EntityState.Modified)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        record.ResponseJson = SerializeCheckoutResponse(response);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureExistingCheckoutOrderReplayIsRequestHashBoundAsync(
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await _dbContext.CommerceHubCheckoutIdempotency
            .FirstOrDefaultAsync(item => item.Key == idempotencyKey, cancellationToken);

        if (record == null)
        {
            throw new CommerceHubCheckoutException(
                "IDEMPOTENCY_REPLAY_UNBOUND",
                "This CommerceHub checkout idempotency key is tied to an existing order but has no request hash. Replay is blocked to prevent returning an order for a different cart.",
                StatusCodes.Status409Conflict);
        }

        EnsureCheckoutIdempotencyRequestMatches(record, requestHash);
    }

    private async Task<CommerceHubCart> LockCartForCheckoutAsync(
        CommerceHubCartOwnerContext ownerContext,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var (tenantId, commerceHubCustomerId) = RequireAuthenticatedOwner(ownerContext);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var cart = await LoadCommerceHubCartForCheckoutAuthorityAsync(
            tenantId,
            commerceHubCustomerId,
            cancellationToken);

        if (cart == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CommerceHubCheckoutException(
                "CART_SYNC_REQUIRED",
                "Your saved cart is empty or out of date. Please refresh the cart before checkout.",
                StatusCodes.Status409Conflict);
        }

        var nowUtc = DateTime.UtcNow;
        if (IsCartLockExpired(cart, nowUtc))
        {
            cart.IsLocked = false;
            cart.LockedAtUtc = null;
            cart.UpdatedAt = nowUtc;
        }

        if (cart.IsLocked)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw CreateCartLockedException(cart.LockedAtUtc);
        }

        if (cart.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw CreateCartConflictException(cart.Version);
        }

        cart.IsLocked = true;
        cart.LockedAtUtc = nowUtc;
        cart.UpdatedAt = nowUtc;
        cart.Version += 1;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return cart;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();
            var currentVersion = await _dbContext.CommerceHubCarts
                .AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.CommerceHubCustomerId == commerceHubCustomerId)
                .Select(item => (int?)item.Version)
                .FirstOrDefaultAsync(cancellationToken);

            throw CreateCartConflictException(currentVersion ?? expectedVersion);
        }
    }

    private async Task<CommerceHubCart?> LoadCommerceHubCartForCheckoutAuthorityAsync(
        Guid tenantId,
        Guid commerceHubCustomerId,
        CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _dbContext.CommerceHubCarts
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""CommerceHubCarts""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""CommerceHubCustomerId"" = {commerceHubCustomerId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _dbContext.CommerceHubCarts
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [CommerceHubCarts] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [CommerceHubCustomerId] = {commerceHubCustomerId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.CommerceHubCarts
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId && item.CommerceHubCustomerId == commerceHubCustomerId,
                cancellationToken);
    }

    private async Task<CommerceHubCartOwnerContext> ResolveAuthenticatedCartOwnerContextAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var customer = await _dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.Id == customerId)
            .Select(item => new
            {
                item.Id,
                item.IsDeleted
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer == null || customer.IsDeleted)
        {
            throw new CommerceHubCheckoutException(
                "AUTH_REQUIRED",
                "Sign in again before continuing checkout.",
                StatusCodes.Status401Unauthorized);
        }

        return new CommerceHubCartOwnerContext
        {
            IsAuthenticated = true,
            TenantId = CommerceHubCartOwnerContext.CommerceHubCartTenantScopeId,
            CommerceHubCustomerId = customer.Id
        };
    }

    private void EnsureLockedCartMatchesCheckoutRequest(
        CommerceHubCart lockedCart,
        IReadOnlyCollection<RequestedCheckoutLine> requestedLines)
    {
        var storedLines = DeserializeStoredCheckoutCartItems(lockedCart.ItemsJson)
            .Where(item =>
                item.StoreId != Guid.Empty &&
                item.ListingId != Guid.Empty &&
                item.ProductId != Guid.Empty &&
                item.VariantId != Guid.Empty &&
                item.Quantity > 0)
            .GroupBy(item => new { item.StoreId, item.ListingId, item.ProductId, item.VariantId })
            .Select(group => new CheckoutCartLineFingerprint(
                group.Key.StoreId,
                group.Key.ListingId,
                group.Key.ProductId,
                group.Key.VariantId,
                group.Sum(item => item.Quantity),
                RoundCurrency(group.Last().PriceSnapshot)))
            .OrderBy(item => item.StoreId)
            .ThenBy(item => item.ListingId)
            .ThenBy(item => item.ProductId)
            .ThenBy(item => item.VariantId)
            .ToList();

        var requestedFingerprints = requestedLines
            .Select(line => new CheckoutCartLineFingerprint(
                line.StoreId,
                line.ListingId,
                line.ProductId,
                line.VariantId,
                line.Quantity,
                RoundCurrency(line.ExpectedUnitPrice)))
            .OrderBy(item => item.StoreId)
            .ThenBy(item => item.ListingId)
            .ThenBy(item => item.ProductId)
            .ThenBy(item => item.VariantId)
            .ToList();

        if (storedLines.Count == 0)
        {
            throw new CommerceHubCheckoutException(
                "CART_SYNC_REQUIRED",
                "Your saved cart is empty or out of date. Please refresh the cart before checkout.",
                StatusCodes.Status409Conflict);
        }

        if (!storedLines.SequenceEqual(requestedFingerprints))
        {
            throw new CommerceHubCheckoutException(
                "CART_CHANGED",
                "Cart changed before checkout. Review the latest cart and try again.",
                StatusCodes.Status409Conflict,
                new { currentVersion = lockedCart.Version });
        }
    }

    private List<StoredCheckoutCartItem> DeserializeStoredCheckoutCartItems(string? itemsJson)
    {
        if (string.IsNullOrWhiteSpace(itemsJson))
        {
            return new List<StoredCheckoutCartItem>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<StoredCheckoutCartItem>>(itemsJson, CartJsonOptions)
                ?? new List<StoredCheckoutCartItem>();
        }
        catch (JsonException ex)
        {
            // SECURITY: Sanitize the FULL itemsJson first to redact sensitive data,
            // then truncate the sanitized result to avoid logging unsanitized sensitive data
            // in the first 500 characters.
            string? jsonSnippet = null;
            if (!string.IsNullOrWhiteSpace(itemsJson) && itemsJson.Length > 0)
            {
                var sanitized = SanitizeJsonForLogging(itemsJson);
                jsonSnippet = sanitized.Length > 500 ? sanitized.Substring(0, 500) : sanitized;
            }

            _logger.LogWarning(
                ex,
                "CommerceHub checkout blocked because stored cart JSON could not be deserialized. JSON snippet (sanitized): {JsonSnippet}",
                jsonSnippet ?? "(empty)");
            return new List<StoredCheckoutCartItem>();
        }
    }

    private async Task CleanupAfterCheckoutFailureAsync(
        Guid idempotencyRecordId,
        Guid? lockedCartId,
        CancellationToken cancellationToken)
    {
        try
        {
            _dbContext.ChangeTracker.Clear();

            if (lockedCartId.HasValue)
            {
                var cart = await _dbContext.CommerceHubCarts
                    .FirstOrDefaultAsync(item => item.Id == lockedCartId.Value, cancellationToken);

                if (cart != null && cart.IsLocked)
                {
                    cart.IsLocked = false;
                    cart.LockedAtUtc = null;
                    cart.UpdatedAt = DateTime.UtcNow;
                    cart.Version += 1;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    _dbContext.ChangeTracker.Clear();
                }
            }

            await RemoveCheckoutIdempotencyClaimIfPendingAsync(idempotencyRecordId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "CommerceHub checkout failure cleanup did not complete fully | IdempotencyRecordId: {IdempotencyRecordId} | LockedCartId: {LockedCartId}",
                idempotencyRecordId,
                lockedCartId);
        }
    }

    private async Task RemoveCheckoutIdempotencyClaimIfPendingAsync(Guid idempotencyRecordId, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var record = await _dbContext.CommerceHubCheckoutIdempotency
            .FirstOrDefaultAsync(item => item.Id == idempotencyRecordId, cancellationToken);

        if (record == null || !string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            return;
        }

        _dbContext.CommerceHubCheckoutIdempotency.Remove(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static CommerceHubCheckoutResponseDto? DeserializeCheckoutResponse(string? responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<CommerceHubCheckoutResponseDto>(responseJson);
    }

    private static string SerializeCheckoutResponse(CommerceHubCheckoutResponseDto response)
    {
        return JsonSerializer.Serialize(response);
    }

    private static bool IsCartLockExpired(CommerceHubCart cart, DateTime nowUtc)
    {
        return cart.IsLocked &&
               (!cart.LockedAtUtc.HasValue || cart.LockedAtUtc.Value <= nowUtc.Subtract(CartLockTimeout));
    }

    private static (Guid TenantId, Guid CommerceHubCustomerId) RequireAuthenticatedOwner(CommerceHubCartOwnerContext ownerContext)
    {
        if (!ownerContext.IsAuthenticated ||
            !ownerContext.TenantId.HasValue ||
            ownerContext.TenantId.Value == Guid.Empty ||
            !ownerContext.CommerceHubCustomerId.HasValue ||
            ownerContext.CommerceHubCustomerId.Value == Guid.Empty)
        {
            throw new CommerceHubCheckoutException(
                "AUTH_REQUIRED",
                "Sign in again before continuing checkout.",
                StatusCodes.Status401Unauthorized);
        }

        return (ownerContext.TenantId.Value, ownerContext.CommerceHubCustomerId.Value);
    }

    private static bool IsCheckoutIdempotencyClaimExpired(CommerceHubCheckoutIdempotency record, DateTime nowUtc)
    {
        return string.IsNullOrWhiteSpace(record.ResponseJson) &&
               record.CreatedAtUtc <= nowUtc.Subtract(CartLockTimeout);
    }

    private static bool IsCheckoutIdempotencyUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException postgresException &&
               postgresException.SqlState == "23505" &&
               string.Equals(
                   postgresException.ConstraintName,
                   "IX_CommerceHubCheckoutIdempotency_Key",
                   StringComparison.Ordinal);
    }

    private static CommerceHubCheckoutException CreateCartLockedException(DateTime? lockedAtUtc)
    {
        return new CommerceHubCheckoutException(
            "CART_LOCKED",
            "Cart is locked during checkout. Refresh the cart after checkout finishes.",
            StatusCodes.Status409Conflict,
            new
            {
                lockedAtUtc,
                lockExpiresAtUtc = lockedAtUtc?.Add(CartLockTimeout)
            });
    }

    private static CommerceHubCheckoutException CreateCartConflictException(int currentVersion)
    {
        return new CommerceHubCheckoutException(
            "CART_CONFLICT",
            "Cart was modified in another session. Review the latest cart and try again.",
            StatusCodes.Status409Conflict,
            new { currentVersion });
    }

    private static CommerceHubCheckoutException CreateCheckoutInProgressException()
    {
        return new CommerceHubCheckoutException(
            "CHECKOUT_IN_PROGRESS",
            "This checkout request is still being processed. Please wait a moment and try again.",
            StatusCodes.Status409Conflict);
    }

    private CommissionResolutionResult ResolveCommissionForCheckout(Guid? vendorId, Guid? categoryId)
    {
        try
        {
            return _commissionService.ResolveCommission(vendorId, categoryId);
        }
        catch (CommissionAuthorityException ex)
        {
            throw new CommerceHubCheckoutException(
                ex.Code,
                ex.Message,
                StatusCodes.Status409Conflict,
                ex.Details);
        }
    }

    private static void ValidateRequestedPaymentMethod(
        string paymentMethod,
        IReadOnlyCollection<Guid> tenantIds,
        IEnumerable<IPaymentProviderAdapter> paymentProviderAdapters)
    {
        if (!CommerceHubPaymentMethodAuthority.IsRequestedMethodAvailable(
                paymentMethod,
                tenantIds,
                paymentProviderAdapters))
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_METHOD_NOT_AVAILABLE",
                "The selected payment method is not available from the CommerceHub platform.",
                StatusCodes.Status400BadRequest);
        }
    }

    private static void ValidateLineIntegrity(
        RequestedCheckoutLine requestedLine,
        CommerceHubListing listing,
        ProductVariant variant,
        Product product)
    {
        if (listing.TenantId != requestedLine.StoreId ||
            listing.ProductId != requestedLine.ProductId ||
            listing.VariantId != requestedLine.VariantId ||
            variant.ProductId != requestedLine.ProductId ||
            product.TenantId != requestedLine.StoreId ||
            variant.TenantId != requestedLine.StoreId)
        {
            throw new CommerceHubCheckoutException(
                "CART_MISMATCH",
                "One or more cart items no longer match the current CommerceHub listing data.",
                StatusCodes.Status409Conflict);
        }
    }

    private async Task<string> GenerateMarketplaceOrderNumberAsync(CancellationToken cancellationToken)
    {
        var nextSequence = await GetNextMarketplaceOrderSequenceAsync(cancellationToken);
        return $"ORD-CH-{nextSequence:D4}";
    }

    private async Task<long> GetNextMarketplaceOrderSequenceAsync(CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await _dbContext.CommerceHubMarketplaceOrders.CountAsync(cancellationToken) + 1L;
        }

        var connection = _dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT nextval('\"CommerceHubMarketplaceOrderNumberSeq\"')";

            var currentTransaction = _dbContext.Database.CurrentTransaction;
            if (currentTransaction != null)
            {
                command.Transaction = currentTransaction.GetDbTransaction();
            }

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(result);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private CommerceHubMarketplaceOrder BuildMarketplaceOrder(
        string orderNumber,
        CommerceHubCheckoutRequestDto request,
        Guid customerId,
        string idempotencyKey,
        string currency,
        PromoValidationResult? promoValidation,
        IReadOnlyCollection<ValidatedCheckoutLine> lineSnapshots,
        IReadOnlyDictionary<Guid, Tenant> tenants,
        CommerceHubShippingCalculationResult shippingResult,
        DateTime nowUtc)
    {
        var normalizedPaymentMethod = request.PaymentMethod.Trim().ToUpperInvariant();
        var isCod = CommerceHubPaymentMethods.IsCod(normalizedPaymentMethod);
        var subOrderPaymentMethod = isCod
            ? CommerceHubPaymentMethod.CashOnDelivery
            : CommerceHubPaymentMethod.Online;
        var orderStatus = OrderStatus.Pending;
        var paymentStatus = ResolveInitialPaymentStatus(subOrderPaymentMethod);
        var paymentMethodName = ResolvePaymentMethodName(normalizedPaymentMethod, subOrderPaymentMethod);
        var shippingSelections = shippingResult.StoreSelections.ToDictionary(selection => selection.StoreId);

        var groupedByStore = lineSnapshots
            .GroupBy(line => line.StoreId)
            .OrderBy(group => tenants[group.Key].Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var marketplaceOrder = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            OrderNumber = orderNumber,
            IdempotencyKey = idempotencyKey,
            FullName = request.Customer.FullName.Trim(),
            PhoneNumber = request.Customer.PhoneNumber.Trim(),
            AddressLine = request.Customer.AddressLine.Trim(),
            City = request.Customer.City.Trim(),
            Area = string.IsNullOrWhiteSpace(request.Customer.Area) ? null : request.Customer.Area.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Customer.Notes) ? null : request.Customer.Notes.Trim(),
            Currency = currency,
            PaymentMethodCode = normalizedPaymentMethod,
            PaymentMethodName = paymentMethodName,
            PaymentGateway = ResolvePaymentGateway(normalizedPaymentMethod),
            PaymentReference = isCod ? null : $"CHPAY-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}",
            PromoCode = promoValidation?.Promo.Code,
            Status = orderStatus,
            PaymentStatus = paymentStatus,
            OnlineCustomerId = null
        };

        marketplaceOrder.OrderAccessToken = _orderAccessService.GenerateAccessToken(
            marketplaceOrder.Id, marketplaceOrder.OrderNumber);

        foreach (var (group, index) in groupedByStore.Select((group, index) => (group, index)))
        {
            var shippingSelection = shippingSelections[group.Key];
            var mergedStoreLines = MergeSubOrderLines(group);
            var subOrder = new CommerceHubSubOrder
            {
                Id = Guid.NewGuid(),
                MarketplaceOrderId = marketplaceOrder.Id,
                MarketplaceOrder = marketplaceOrder,
                OrderNumber = $"{orderNumber}-{BuildSubOrderSuffix(index)}",
                StoreId = group.Key,
                StoreName = tenants[group.Key].Name,
                Currency = currency,
                Subtotal = RoundCurrency(mergedStoreLines.Sum(line => line.Total)),
                Shipping = shippingSelection.ShippingPrice,
                ShippingMethodId = shippingSelection.ShippingOptionId,
                ShippingMethodName = shippingSelection.ShippingName,
                ShippingSnapshotJson = BuildShippingSnapshotJson(shippingSelection),
                PaymentMethod = subOrderPaymentMethod,
                PaymentMethodCode = normalizedPaymentMethod,
                PaymentMethodName = paymentMethodName,
                PaymentSnapshotJson = BuildPaymentSnapshotJson(
                    subOrderPaymentMethod,
                    normalizedPaymentMethod,
                    paymentMethodName,
                    marketplaceOrder.PaymentGateway ?? ResolvePaymentGateway(normalizedPaymentMethod),
                    paymentStatus),
                Total = RoundCurrency(mergedStoreLines.Sum(line => line.Total) + shippingSelection.ShippingPrice),
                Status = orderStatus,
                PaymentStatus = paymentStatus
            };

            var itemCommissionResolutionContexts = new List<CommissionItemResolutionContext>();

            foreach (var line in mergedStoreLines)
            {
                var commissionResolution = ResolveCommissionForCheckout(line.StoreId, line.CategoryId);
                var itemCommissionRate = RoundCommissionRate(commissionResolution.Rate);
                // Commission applies only to product subtotal. Shipping remains pass-through.
                var itemCommissionAmount = CommissionCalculator.RoundAmount(line.Total * itemCommissionRate);
                var subOrderItem = new CommerceHubSubOrderItem
                {
                    Id = Guid.NewGuid(),
                    SubOrderId = subOrder.Id,
                    ListingId = line.ListingId,
                    ProductId = line.ProductId,
                    VariantId = line.VariantId,
                    CategoryId = line.CategoryId,
                    ProductName = line.ProductName,
                    VariantName = line.VariantName,
                    VariantAttributesJson = line.VariantAttributesJson,
                    ImageUrl = line.ImageUrl,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    // T-08: immutable merchandise-cost authority, frozen here.
                    UnitCostSnapshot = line.UnitCostSnapshot,
                    CostAmountSnapshot = line.CostAmountSnapshot,
                    CostSnapshotCapturedAtUtc = nowUtc,
                    PlatformCommissionRate = itemCommissionRate,
                    PlatformCommissionAmount = itemCommissionAmount,
                    FinalPlatformCommissionAmount = itemCommissionAmount,
                    PromoDiscountAmount = 0m,
                    Total = line.Total,
                    Status = MapSubOrderStatusToItemStatus(orderStatus),
                    DeliveredAt = orderStatus is OrderStatus.Delivered or OrderStatus.Completed
                        ? nowUtc
                        : null
                };

                subOrder.Items.Add(subOrderItem);
                itemCommissionResolutionContexts.Add(new CommissionItemResolutionContext(subOrderItem, commissionResolution));
            }

            var promoApplication = promoValidation == null
                ? PromoApplicationResult.Empty
                : PromoCalculationEngine.Apply(
                    promoValidation.Promo,
                    subOrder.StoreId,
                    subOrder.Items
                        .Select(item => new PromoCommissionLine(
                            item.Id,
                            subOrder.StoreId,
                            item.CategoryId,
                            item.PlatformCommissionAmount))
                        .ToList(),
                    _promoService.CalculateDiscount);

            foreach (var item in subOrder.Items)
            {
                var itemPromoDiscount = promoApplication.DiscountByLine.GetValueOrDefault(item.Id);
                item.PromoDiscountAmount = CommissionCalculator.RoundAmount(itemPromoDiscount);
                item.FinalPlatformCommissionAmount = CommissionCalculator.RoundAmount(
                    Math.Max(0m, item.PlatformCommissionAmount - item.PromoDiscountAmount));
            }

            var itemCommissionSnapshots = itemCommissionResolutionContexts
                .Select(context => new CommissionItemSnapshot(
                    context.Item.ListingId,
                    context.Item.ProductId,
                    context.Item.VariantId,
                    context.Item.Quantity,
                    context.Item.Total,
                    context.Item.PlatformCommissionRate,
                    context.Item.PlatformCommissionAmount,
                    context.Item.FinalPlatformCommissionAmount,
                    context.Item.PromoDiscountAmount,
                    context.Resolution.RuleId,
                    context.Resolution.ScopeType,
                    context.Resolution.ScopeId,
                    context.Resolution.Priority,
                    context.Resolution.Source))
                .ToList();

            var snapshotSummary = BuildCommissionSnapshotSummary(itemCommissionSnapshots);

            subOrder.PlatformCommissionAmount = CommissionCalculator.RoundAmount(subOrder.Items.Sum(item => item.PlatformCommissionAmount));
            subOrder.FinalPlatformCommissionAmount = CommissionCalculator.RoundAmount(subOrder.Items.Sum(item => item.FinalPlatformCommissionAmount));
            subOrder.PromoDiscountAmount = CommissionCalculator.RoundAmount(subOrder.Items.Sum(item => item.PromoDiscountAmount));
            subOrder.PlatformCommissionRate = subOrder.Subtotal <= 0m
                ? 0m
                : RoundCommissionRate(subOrder.PlatformCommissionAmount / subOrder.Subtotal);
            subOrder.VendorEarningAmount = CommissionCalculator.RoundAmount(Math.Max(0m, subOrder.Total - subOrder.PlatformCommissionAmount));
            subOrder.CommissionSnapshotJson = BuildCommissionSnapshotJson(subOrder, itemCommissionSnapshots, snapshotSummary, nowUtc);

            _logger.LogInformation(
                "CommerceHub commission snapshot created | OrderNumber: {OrderNumber} | SubOrderId: {SubOrderId} | Rate: {Rate} | GrossCommission: {GrossCommission} | FinalCommission: {FinalCommission} | PromoDiscount: {PromoDiscount} | Source: {Source} | RuleId: {RuleId} | Priority: {Priority} | CommissionBase: {CommissionBase} | ShippingExcluded: true",
                subOrder.OrderNumber,
                subOrder.Id,
                subOrder.PlatformCommissionRate,
                subOrder.PlatformCommissionAmount,
                subOrder.FinalPlatformCommissionAmount,
                subOrder.PromoDiscountAmount,
                snapshotSummary.Source,
                snapshotSummary.RuleId,
                snapshotSummary.Priority,
                CommissionCalculator.CommissionBase);

            marketplaceOrder.SubOrders.Add(subOrder);
        }

        marketplaceOrder.Subtotal = RoundCurrency(marketplaceOrder.SubOrders.Sum(subOrder => subOrder.Subtotal));
        marketplaceOrder.ShippingTotal = RoundCurrency(marketplaceOrder.SubOrders.Sum(subOrder => subOrder.Shipping));
        marketplaceOrder.TotalAmount = RoundCurrency(marketplaceOrder.Subtotal + marketplaceOrder.ShippingTotal);
        marketplaceOrder.PromoDiscountTotal = RoundCurrency(marketplaceOrder.SubOrders.Sum(subOrder => subOrder.PromoDiscountAmount));

        if (marketplaceOrder.PromoDiscountTotal <= 0m)
        {
            marketplaceOrder.PromoCode = null;
        }

        return marketplaceOrder;
    }

    private static IReadOnlyCollection<ValidatedCheckoutLine> MergeSubOrderLines(IEnumerable<ValidatedCheckoutLine> lines)
    {
        var materializedLines = lines.ToList();

        var conflictingVariantIds = materializedLines
            .GroupBy(line => line.VariantId)
            .Where(group =>
                group
                    .Select(line => new
                    {
                        line.ListingId,
                        line.ProductId,
                        line.UnitPrice,
                        // T-08: merged lines must share one frozen cost basis.
                        line.UnitCostSnapshot,
                        line.CategoryId,
                        line.ProductName,
                        line.VariantName,
                        line.VariantAttributesJson,
                        line.ImageUrl
                    })
                    .Distinct()
                    .Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (conflictingVariantIds.Count > 0)
        {
            throw new CommerceHubCheckoutException(
                "DUPLICATE_VARIANT_CONFLICT",
                "One or more vendor order lines could not be merged safely because their pricing or catalog snapshots differ.",
                StatusCodes.Status409Conflict,
                new { variantIds = conflictingVariantIds });
        }

        return materializedLines
            .GroupBy(line => line.VariantId)
            .Select(group =>
            {
                var firstLine = group.First();
                return new ValidatedCheckoutLine(
                    firstLine.StoreId,
                    firstLine.ListingId,
                    firstLine.ProductId,
                    firstLine.VariantId,
                    group.Sum(line => line.Quantity),
                    firstLine.UnitPrice,
                    firstLine.UnitCostSnapshot,
                    firstLine.CategoryId,
                    firstLine.ProductName,
                    firstLine.VariantName,
                    firstLine.VariantAttributesJson,
                    firstLine.ImageUrl,
                    firstLine.WeightKg);
            })
            .OrderBy(line => line.ProductName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(line => line.VariantName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildSubOrderSuffix(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var suffix = string.Empty;
        var value = index;

        do
        {
            suffix = (char)('A' + (value % 26)) + suffix;
            value = (value / 26) - 1;
        }
        while (value >= 0);

        return suffix;
    }

    private static string BuildCommissionSnapshotJson(
        CommerceHubSubOrder subOrder,
        IReadOnlyCollection<CommissionItemSnapshot> itemSnapshots,
        CommissionSnapshotSummary snapshotSummary,
        DateTime calculatedAtUtc)
    {
        return JsonSerializer.Serialize(new
        {
            rate = subOrder.PlatformCommissionRate,
            amount = subOrder.PlatformCommissionAmount,
            grossCommissionAmount = subOrder.PlatformCommissionAmount,
            finalCommissionAmount = subOrder.FinalPlatformCommissionAmount,
            promoDiscountAmount = subOrder.PromoDiscountAmount,
            promoCode = subOrder.MarketplaceOrder.PromoCode,
            source = snapshotSummary.Source,
            ruleId = snapshotSummary.RuleId,
            priority = snapshotSummary.Priority,
            scopeType = snapshotSummary.ScopeType?.ToString(),
            scopeId = snapshotSummary.ScopeId,
            vendorEarning = subOrder.VendorEarningAmount,
            commissionBase = CommissionCalculator.CommissionBase,
            commissionBaseAmount = subOrder.Subtotal,
            shippingExcluded = true,
            subtotal = subOrder.Subtotal,
            shipping = subOrder.Shipping,
            total = subOrder.Total,
            currency = subOrder.Currency,
            calculatedAtUtc,
            resolutionBreakdown = itemSnapshots
                .GroupBy(snapshot => new { snapshot.Source, snapshot.RuleId, snapshot.Priority })
                .Select(group => new
                {
                    group.Key.Source,
                    group.Key.RuleId,
                    group.Key.Priority,
                    lineCount = group.Count(),
                    lineTotal = CommissionCalculator.RoundAmount(group.Sum(item => item.LineTotal)),
                    commissionAmount = CommissionCalculator.RoundAmount(group.Sum(item => item.GrossAmount)),
                    grossCommissionAmount = CommissionCalculator.RoundAmount(group.Sum(item => item.GrossAmount)),
                    finalCommissionAmount = CommissionCalculator.RoundAmount(group.Sum(item => item.FinalAmount)),
                    promoDiscountAmount = CommissionCalculator.RoundAmount(group.Sum(item => item.PromoDiscountAmount))
                }),
            items = itemSnapshots.Select(snapshot => new
            {
                snapshot.ListingId,
                snapshot.ProductId,
                snapshot.VariantId,
                snapshot.Quantity,
                snapshot.LineTotal,
                rate = snapshot.Rate,
                amount = snapshot.GrossAmount,
                grossAmount = snapshot.GrossAmount,
                finalAmount = snapshot.FinalAmount,
                snapshot.PromoDiscountAmount,
                snapshot.RuleId,
                scopeType = snapshot.ScopeType?.ToString(),
                snapshot.ScopeId,
                snapshot.Priority,
                snapshot.Source
            })
        });
    }

    private static CommissionSnapshotSummary BuildCommissionSnapshotSummary(
        IReadOnlyCollection<CommissionItemSnapshot> itemSnapshots)
    {
        var distinctResolutions = itemSnapshots
            .Select(snapshot => new
            {
                snapshot.Source,
                snapshot.RuleId,
                snapshot.Priority,
                snapshot.ScopeType,
                snapshot.ScopeId
            })
            .Distinct()
            .ToList();

        if (distinctResolutions.Count == 1)
        {
            var resolution = distinctResolutions[0];
            return new CommissionSnapshotSummary(
                resolution.Source,
                resolution.RuleId,
                resolution.Priority,
                resolution.ScopeType,
                resolution.ScopeId);
        }

        return new CommissionSnapshotSummary(
            CommissionCalculator.MixedSource,
            null,
            null,
            null,
            null);
    }

    private static void ResetPromoFinancials(CommerceHubMarketplaceOrder marketplaceOrder)
    {
        marketplaceOrder.PromoCode = null;
        marketplaceOrder.PromoDiscountTotal = 0m;

        foreach (var subOrder in marketplaceOrder.SubOrders)
        {
            foreach (var item in subOrder.Items)
            {
                item.PromoDiscountAmount = 0m;
                item.FinalPlatformCommissionAmount = CommissionCalculator.RoundAmount(item.PlatformCommissionAmount);
            }

            subOrder.PromoDiscountAmount = 0m;
            subOrder.FinalPlatformCommissionAmount = CommissionCalculator.RoundAmount(subOrder.PlatformCommissionAmount);
            subOrder.CommissionSnapshotJson = NormalizeCommissionSnapshotWithoutPromo(
                subOrder.CommissionSnapshotJson,
                subOrder);
        }
    }

    private static string? NormalizeCommissionSnapshotWithoutPromo(
        string? existingSnapshotJson,
        CommerceHubSubOrder subOrder)
    {
        if (string.IsNullOrWhiteSpace(existingSnapshotJson))
        {
            return existingSnapshotJson;
        }

        try
        {
            var rootNode = JsonNode.Parse(existingSnapshotJson) as JsonObject;
            if (rootNode == null)
            {
                return existingSnapshotJson;
            }

            rootNode["finalCommissionAmount"] = subOrder.PlatformCommissionAmount;
            rootNode["promoDiscountAmount"] = 0m;
            rootNode["promoCode"] = null;

            if (rootNode["resolutionBreakdown"] is JsonArray resolutionBreakdown)
            {
                foreach (var entryNode in resolutionBreakdown.OfType<JsonObject>())
                {
                    if (TryGetDecimalValue(entryNode["grossCommissionAmount"], out var grossAmount))
                    {
                        entryNode["finalCommissionAmount"] = grossAmount;
                    }
                    else if (TryGetDecimalValue(entryNode["commissionAmount"], out var commissionAmount))
                    {
                        entryNode["finalCommissionAmount"] = commissionAmount;
                    }

                    entryNode["promoDiscountAmount"] = 0m;
                }
            }

            if (rootNode["items"] is JsonArray items)
            {
                foreach (var itemNode in items.OfType<JsonObject>())
                {
                    if (TryGetDecimalValue(itemNode["grossAmount"], out var grossAmount))
                    {
                        itemNode["finalAmount"] = grossAmount;
                    }
                    else if (TryGetDecimalValue(itemNode["amount"], out var amount))
                    {
                        itemNode["finalAmount"] = amount;
                    }

                    itemNode["PromoDiscountAmount"] = 0m;
                }
            }

            return rootNode.ToJsonString();
        }
        catch (JsonException)
        {
            return existingSnapshotJson;
        }
    }

    internal static CommerceHubCheckoutResponseDto BuildResponse(CommerceHubMarketplaceOrder order, bool isIdempotentReplay)
    {
        var isCod = CommerceHubPaymentMethods.IsCod(order.PaymentMethodCode);
        var provider = order.PaymentGateway ?? ResolvePaymentGateway(order.PaymentMethodCode);

        return new CommerceHubCheckoutResponseDto
        {
            MarketplaceOrderId = order.Id,
            OrderNumber = order.OrderNumber,
            IdempotencyKey = order.IdempotencyKey,
            IsIdempotentReplay = isIdempotentReplay,
            Currency = order.Currency,
            PaymentMethod = order.PaymentMethodCode,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            PaymentReference = order.PaymentReference,
            PaymentReferenceId = order.PaymentReferenceId,
            PaidAt = order.PaidAt,
            Subtotal = order.Subtotal,
            Shipping = order.ShippingTotal,
            ShippingTotal = order.ShippingTotal,
            Total = order.TotalAmount,
            PromoCode = order.PromoCode,
            PromoDiscountTotal = order.PromoDiscountTotal,
            Message = isIdempotentReplay
                ? "This checkout request was already processed, so the existing marketplace order was returned."
                : isCod
                    ? "Order placed successfully. Each store now has its own sub-order for fulfillment."
                    : "Order created successfully. Initiate the payment session next; webhook verification remains server-authoritative.",
            OrderAccessToken = order.OrderAccessToken,
            SubOrders = order.SubOrders
                .OrderBy(subOrder => subOrder.OrderNumber, StringComparer.OrdinalIgnoreCase)
                .Select(subOrder => new CommerceHubCheckoutSubOrderDto
                {
                    SubOrderId = subOrder.Id,
                    OrderNumber = subOrder.OrderNumber,
                    StoreId = subOrder.StoreId,
                    StoreName = subOrder.StoreName,
                    Currency = subOrder.Currency,
                    Subtotal = subOrder.Subtotal,
                    Shipping = subOrder.Shipping,
                    ShippingMethodId = subOrder.ShippingMethodId,
                    ShippingMethodName = subOrder.ShippingMethodName,
                    ShippingSnapshotJson = subOrder.ShippingSnapshotJson,
                    PaymentMethod = subOrder.PaymentMethod,
                    PaymentMethodCode = subOrder.PaymentMethodCode,
                    PaymentMethodName = subOrder.PaymentMethodName,
                    PaymentSnapshotJson = subOrder.PaymentSnapshotJson,
                    Total = subOrder.Total,
                    Status = subOrder.Status,
                    PaymentStatus = subOrder.PaymentStatus,
                    PaymentReferenceId = subOrder.PaymentReferenceId,
                    PaidAt = subOrder.PaidAt,
                    Items = subOrder.Items
                        .Select(item => new CommerceHubCheckoutSubOrderItemDto
                        {
                            ListingId = item.ListingId,
                            ProductId = item.ProductId,
                            VariantId = item.VariantId,
                            ProductName = item.ProductName,
                            VariantName = item.VariantName,
                            VariantAttributesJson = item.VariantAttributesJson,
                            ImageUrl = item.ImageUrl,
                            Quantity = item.Quantity,
                            UnitPrice = item.UnitPrice,
                            Total = item.Total
                        })
                        .ToList()
                })
                .ToList(),
            PaymentSession = isCod
                ? null
                : new CommerceHubCheckoutPaymentSessionDto
                {
                    Provider = provider,
                    Status = "requires_initiation",
                    IsSimulated = false,
                    MerchantReference = order.PaymentReference ?? order.OrderNumber,
                    Amount = order.TotalAmount,
                    Currency = order.Currency,
                    Metadata = new Dictionary<string, string>
                    {
                        ["marketplaceOrderId"] = order.Id.ToString(),
                        ["orderNumber"] = order.OrderNumber,
                        ["paymentReference"] = order.PaymentReference ?? string.Empty,
                        ["provider"] = provider,
                        ["paymentInitiationRequired"] = bool.TrueString
                    }
                }
        };
    }

    private static string? ResolveImageUrl(ProductVariant variant, Product product)
    {
        if (!string.IsNullOrWhiteSpace(variant.ImageUrl))
        {
            return variant.ImageUrl;
        }

        if (string.IsNullOrWhiteSpace(product.Images))
        {
            return null;
        }

        return product.Images
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
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

    private static string? NormalizeIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var normalized = idempotencyKey.Trim();
        if (normalized.Length > 100)
        {
            return normalized[..100];
        }

        return normalized;
    }

    private static string BuildCheckoutRequestHash(Guid customerId, CommerceHubCheckoutRequestDto request)
    {
        return DeterministicRequestHasher.ComputeHash(new
        {
            CustomerId = customerId,
            PaymentMethod = request.PaymentMethod.Trim().ToUpperInvariant(),
            PromoCode = string.IsNullOrWhiteSpace(request.PromoCode) ? null : request.PromoCode.Trim().ToUpperInvariant(),
            Customer = new
            {
                FullName = request.Customer.FullName.Trim(),
                PhoneNumber = request.Customer.PhoneNumber.Trim(),
                AddressLine = request.Customer.AddressLine.Trim(),
                City = request.Customer.City.Trim(),
                Area = string.IsNullOrWhiteSpace(request.Customer.Area) ? null : request.Customer.Area.Trim(),
                Notes = string.IsNullOrWhiteSpace(request.Customer.Notes) ? null : request.Customer.Notes.Trim()
            },
            ShippingTotal = RoundCurrency(request.ShippingTotal ?? 0m),
            Stores = request.Stores
                .OrderBy(store => store.StoreId)
                .Select(store => new
                {
                    store.StoreId,
                    Items = store.Items
                        .OrderBy(item => item.ListingId)
                        .ThenBy(item => item.ProductId)
                        .ThenBy(item => item.VariantId)
                        .Select(item => new
                        {
                            item.ListingId,
                            item.ProductId,
                            item.VariantId,
                            item.Quantity,
                            ExpectedUnitPrice = RoundCurrency(item.ExpectedUnitPrice ?? 0m)
                        })
                        .ToList()
                })
                .ToList(),
            ShippingSelections = request.ShippingSelections
                .OrderBy(selection => selection.StoreId)
                .ThenBy(selection => selection.ShippingOptionId)
                .Select(selection => new
                {
                    selection.StoreId,
                    selection.ShippingOptionId,
                    ShippingPrice = RoundCurrency(selection.ShippingPrice)
                })
                .ToList()
        });
    }

    private static void EnsureCheckoutIdempotencyRequestMatches(
        CommerceHubCheckoutIdempotency record,
        string requestHash)
    {
        if (string.IsNullOrWhiteSpace(record.RequestHash))
        {
            throw new CommerceHubCheckoutException(
                "IDEMPOTENCY_REPLAY_UNBOUND",
                "This CommerceHub checkout idempotency key is not bound to a request hash. Replay is blocked to prevent returning stale checkout state.",
                StatusCodes.Status409Conflict);
        }

        // S37C: Handle legacy migrated records with sentinel hash
        if (record.RequestHash == "LEGACY_MIGRATED_NO_HASH")
        {
            throw new CommerceHubCheckoutException(
                "IDEMPOTENCY_REPLAY_LEGACY",
                "This CommerceHub checkout idempotency key was created before request hash tracking was enabled. Replay is blocked to prevent returning stale checkout state. Please retry with a new idempotency key.",
                StatusCodes.Status409Conflict);
        }

        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new CommerceHubCheckoutException(
                "IDEMPOTENCY_PAYLOAD_CONFLICT",
                "This idempotency key was already used for a different CommerceHub checkout request.",
                StatusCodes.Status409Conflict);
        }
    }

    private static string BuildCheckoutLockKey(Guid customerId, string normalizedIdempotencyKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedIdempotencyKey)))[..24].ToLowerInvariant();
        return $"lock:checkout:{customerId:N}:{hash}";
    }

    private static string BuildCustomerCheckoutLockKey(Guid customerId) =>
        $"lock:commercehub:checkout:customer:{customerId:N}";

    private static Guid DeterministicGuid(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        var hash = SHA256.HashData(bytes);
        Array.Resize(ref hash, 16);
        return new Guid(hash);
    }

    private static CommerceHubOrderItemStatus MapSubOrderStatusToItemStatus(OrderStatus status) =>
        status switch
        {
            OrderStatus.Shipped => CommerceHubOrderItemStatus.Shipped,
            OrderStatus.Delivered or OrderStatus.Completed => CommerceHubOrderItemStatus.Delivered,
            OrderStatus.Cancelled or OrderStatus.Refunded => CommerceHubOrderItemStatus.Cancelled,
            _ => CommerceHubOrderItemStatus.Pending
        };

    private static string BuildShippingSnapshotJson(CommerceHubShippingStoreSelection selection)
    {
        return JsonSerializer.Serialize(new
        {
            shippingOptionId = selection.ShippingOptionId,
            shippingName = selection.ShippingName,
            shippingPrice = selection.ShippingPrice,
            freeShippingThreshold = selection.FreeShippingThreshold
        });
    }

    private static PaymentStatus ResolveInitialPaymentStatus(CommerceHubPaymentMethod paymentMethod)
    {
        return paymentMethod == CommerceHubPaymentMethod.CashOnDelivery
            ? PaymentStatus.CashOnDelivery
            : PaymentStatus.Pending;
    }

    private static string ResolvePaymentMethodName(
        string paymentMethodCode,
        CommerceHubPaymentMethod paymentMethod)
    {
        if (paymentMethod == CommerceHubPaymentMethod.CashOnDelivery)
        {
            return "Cash on Delivery";
        }

        return paymentMethodCode.Trim().ToUpperInvariant() switch
        {
            CommerceHubPaymentMethods.Stripe => "Stripe",
            _ => "Paymob"
        };
    }

    private static string ResolvePaymentGateway(string paymentMethodCode)
    {
        if (CommerceHubPaymentMethods.IsCod(paymentMethodCode))
        {
            return "cod";
        }

        return paymentMethodCode.Trim().ToLowerInvariant();
    }

    private static string BuildPaymentSnapshotJson(
        CommerceHubPaymentMethod paymentMethod,
        string paymentMethodCode,
        string paymentMethodName,
        string paymentGateway,
        PaymentStatus paymentStatus)
    {
        return JsonSerializer.Serialize(new
        {
            method = paymentMethod.ToString(),
            paymentMethodCode,
            paymentMethodName,
            paymentGateway,
            status = paymentStatus.ToString()
        });
    }

    private static decimal RoundCurrency(decimal value)
    {
        return CommissionCalculator.RoundAmount(value);
    }

    private static string ResolveCommerceHubAuthorityCurrency() =>
        FinancialCurrencyGuard.PlatformBaseCurrencyCode;

    private static decimal RoundCommissionRate(decimal value)
    {
        return CommissionCalculator.RoundRate(value);
    }

    private sealed record RequestedCheckoutLine(
        Guid StoreId,
        Guid ListingId,
        Guid ProductId,
        Guid VariantId,
        int Quantity,
        decimal ExpectedUnitPrice);

    private sealed record CheckoutCartLineFingerprint(
        Guid StoreId,
        Guid ListingId,
        Guid ProductId,
        Guid VariantId,
        int Quantity,
        decimal ExpectedUnitPrice);

    private sealed class StoredCheckoutCartItem
    {
        public Guid StoreId { get; set; }
        public Guid ListingId { get; set; }
        public Guid ProductId { get; set; }
        public Guid VariantId { get; set; }
        public int Quantity { get; set; }
        public decimal PriceSnapshot { get; set; }
    }

    private sealed record ValidatedCheckoutLine(
        Guid StoreId,
        Guid ListingId,
        Guid ProductId,
        Guid VariantId,
        int Quantity,
        decimal UnitPrice,
        decimal UnitCostSnapshot,
        Guid CategoryId,
        string ProductName,
        string? VariantName,
        string? VariantAttributesJson,
        string? ImageUrl,
        decimal WeightKg)
    {
        public decimal Total => UnitPrice * Quantity;
        public decimal TotalWeight => WeightKg * Quantity;
        // T-08: frozen merchandise-cost basis for this line.
        public decimal CostAmountSnapshot => Math.Round(UnitCostSnapshot * Quantity, 2, MidpointRounding.AwayFromZero);
    }

    private sealed record CommissionItemSnapshot(
        Guid ListingId,
        Guid ProductId,
        Guid VariantId,
        int Quantity,
        decimal LineTotal,
        decimal Rate,
        decimal GrossAmount,
        decimal FinalAmount,
        decimal PromoDiscountAmount,
        Guid? RuleId,
        CommissionRuleScopeType? ScopeType,
        Guid? ScopeId,
        int? Priority,
        string Source);

    private sealed record CommissionItemResolutionContext(
        CommerceHubSubOrderItem Item,
        CommissionResolutionResult Resolution);

    private sealed record CommissionSnapshotSummary(
        string Source,
        Guid? RuleId,
        int? Priority,
        CommissionRuleScopeType? ScopeType,
        Guid? ScopeId);

    private static bool TryGetDecimalValue(JsonNode? node, out decimal value)
    {
        if (node == null)
        {
            value = 0m;
            return false;
        }

        try
        {
            value = node.Deserialize<decimal>();
            return true;
        }
        catch (JsonException)
        {
            value = 0m;
            return false;
        }
        catch (InvalidOperationException)
        {
            value = 0m;
            return false;
        }
        catch (FormatException)
        {
            value = 0m;
            return false;
        }
    }

    // S40: Hoist sensitive keys set to avoid recreation per node
    private static readonly HashSet<string> SensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "email", "emailaddress", "customeremail",
        "phone", "phonenumber", "customerphone",
        "fullname", "name", "customername",
        "address", "addressline", "address1", "address2", "addressline1", "addressline2",
        "city", "area", "region", "state", "province",
        "cardNumber", "cardnumber", "cardnum", "card",
        "cvv", "cvvcode", "cvc", "securitycode",
        "password", "passwordhash",
        "notes"
    };

    /// <summary>
    /// Sanitizes JSON for logging by redacting sensitive fields.
    /// Removes or masks fields like email, phone, address, payment details.
    /// S40: Uses structured JSON parsing to handle escaped characters and nested objects correctly.
    /// Reuses parsed JsonElement to avoid reparsing.
    /// </summary>
    private static string SanitizeJsonForLogging(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;

        try
        {
            // Parse JSON once and reuse the root element
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // S40: Reuse already-parsed root element instead of reparsing
            var sanitized = RedactSensitiveProperties(root, SensitiveKeys);

            // Serialize back to string
            return JsonSerializer.Serialize(sanitized, new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch
        {
            // If JSON parsing fails, return a safe placeholder
            return "{\"error\":\"[INVALID_JSON_REDACTED]\"}";
        }
    }

    /// <summary>
    /// Recursively redacts sensitive properties from a JSON element.
    /// S40: Accepts prebuilt sensitive keys set to avoid recreation per node.
    /// </summary>
    private static object? RedactSensitiveProperties(JsonElement element, HashSet<string> sensitiveKeys)
    {

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var property in element.EnumerateObject())
                {
                    if (sensitiveKeys.Contains(property.Name))
                    {
                        dict[property.Name] = "[REDACTED]";
                    }
                    else
                    {
                        dict[property.Name] = RedactSensitiveProperties(property.Value, sensitiveKeys);
                    }
                }
                return dict;

            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(RedactSensitiveProperties(item, sensitiveKeys));
                }
                return list;

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                if (element.TryGetInt32(out var intValue))
                    return intValue;
                if (element.TryGetInt64(out var longValue))
                    return longValue;
                if (element.TryGetDecimal(out var decimalValue))
                    return decimalValue;
                return element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
                return null;

            default:
                return null;
        }
    }

}
