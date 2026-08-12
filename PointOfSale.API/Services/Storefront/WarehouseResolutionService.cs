using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services.Cart;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Services.Storefront;

public static class WarehouseResolutionErrorCodes
{
    public const string NoEligibleWarehouse = "NO_ELIGIBLE_WAREHOUSE";
    public const string WarehouseSelectionPolicyRequired = "WAREHOUSE_SELECTION_POLICY_REQUIRED";
    public const string WarehouseInactive = "WAREHOUSE_INACTIVE";
    public const string InsufficientStockAtResolvedWarehouse = "INSUFFICIENT_STOCK_AT_RESOLVED_WAREHOUSE";
    public const string WarehouseChanged = "WAREHOUSE_CHANGED";
    public const string CarrierAssignmentUnavailable = "CARRIER_ASSIGNMENT_UNAVAILABLE";
    public const string ShippingMethodIncompatibleWithWarehouse = "SHIPPING_METHOD_INCOMPATIBLE_WITH_WAREHOUSE";
    public const string CheckoutSessionStale = "CHECKOUT_SESSION_STALE";
    public const string WarehouseResolutionExpired = "WAREHOUSE_RESOLUTION_EXPIRED";
    public const string CartChangedReResolutionRequired = "CART_CHANGED_RE_RESOLUTION_REQUIRED";
    public const string TenantMismatch = "TENANT_MISMATCH";
    public const string WarehouseResolutionServiceUnavailable = "WAREHOUSE_RESOLUTION_SERVICE_UNAVAILABLE";
}

public sealed class WarehouseResolutionException : Exception
{
    public WarehouseResolutionException(string errorCode, string message, int statusCode)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }
    public int StatusCode { get; }
}

public sealed record WarehouseResolutionPreviewResult(
    WarehouseResolution Resolution,
    CartDto Cart);

public sealed record WarehouseResolutionCheckoutValidation(
    WarehouseResolution Resolution,
    Guid WarehouseId);

public interface IWarehouseResolutionService
{
    Task<CartDto> LoadPreviewCartAsync(
        Guid tenantId,
        CartOwner owner,
        Guid checkoutSessionId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WarehouseResolutionPreviewResult> ResolveForPreviewAsync(
        Guid tenantId,
        CartOwner owner,
        Guid checkoutSessionId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WarehouseResolutionPreviewResult> ResolveForSelectedShippingMethodAsync(
        Guid tenantId,
        CartOwner owner,
        Guid checkoutSessionId,
        Guid shippingMethodId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WarehouseResolutionCheckoutValidation> ValidateSubmittedResolutionAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        Guid? customerId,
        string? sessionId,
        CartSnapshot snapshot,
        CheckoutRequestDto request,
        string correlationId,
        bool lockForCheckout,
        CancellationToken cancellationToken = default);

    Task MarkOrderedAsync(
        Guid tenantId,
        Guid warehouseResolutionId,
        Guid orderId,
        CancellationToken cancellationToken = default);
}

public sealed class WarehouseResolutionService : IWarehouseResolutionService
{
    public const string AlgorithmCode = "STOREFRONT_SHIPPING_METHOD_FULFILLMENT_WAREHOUSE_V1";
    private static readonly TimeSpan ResolutionTtl = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _dbContext;
    private readonly ICartService _cartService;
    private readonly ICarrierIntegrationAuthority _carrierIntegrationAuthority;
    private readonly ILogger<WarehouseResolutionService> _logger;

    public WarehouseResolutionService(
        ApplicationDbContext dbContext,
        ICartService cartService,
        ILogger<WarehouseResolutionService> logger,
        ICarrierIntegrationAuthority? carrierIntegrationAuthority = null)
    {
        _dbContext = dbContext;
        _cartService = cartService;
        _carrierIntegrationAuthority = carrierIntegrationAuthority ?? NullCarrierIntegrationAuthority.Instance;
        _logger = logger;
    }

    public async Task<CartDto> LoadPreviewCartAsync(
        Guid tenantId,
        CartOwner owner,
        Guid checkoutSessionId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadOwnedActiveSessionAsync(
            tenantId,
            checkoutSessionId,
            owner.OwnerId,
            owner.OwnerType,
            customerId: TryParseCustomerId(owner),
            sessionId: string.Equals(owner.OwnerType, "session", StringComparison.OrdinalIgnoreCase) ? owner.OwnerId : null,
            cancellationToken);

        var cart = await _cartService.GetCartAsync(tenantId, owner, correlationId, cancellationToken);
        ValidateCartForSession(tenantId, owner.OwnerId, owner.OwnerType, session, cart);
        return cart;
    }

    public async Task<WarehouseResolutionPreviewResult> ResolveForPreviewAsync(
        Guid tenantId,
        CartOwner owner,
        Guid checkoutSessionId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadOwnedActiveSessionAsync(
            tenantId,
            checkoutSessionId,
            owner.OwnerId,
            owner.OwnerType,
            customerId: TryParseCustomerId(owner),
            sessionId: string.Equals(owner.OwnerType, "session", StringComparison.OrdinalIgnoreCase) ? owner.OwnerId : null,
            cancellationToken);

        var cart = await _cartService.GetCartAsync(tenantId, owner, correlationId, cancellationToken);
        ValidateCartForSession(tenantId, owner.OwnerId, owner.OwnerType, session, cart);

        var input = await BuildResolutionInputAsync(tenantId, session.Id, session.CartId, cart.Items, cancellationToken);
        var active = await LoadActiveResolutionAsync(tenantId, session.Id, cancellationToken);
        var nowUtc = DateTime.UtcNow;

        if (active != null)
        {
            if (active.ExpiresAtUtc.HasValue && active.ExpiresAtUtc.Value <= nowUtc)
            {
                Invalidate(active, "expired", nowUtc, WarehouseResolutionStatus.Expired);
            }
            else if (string.Equals(active.InputsSnapshotHash, input.Hash, StringComparison.Ordinal))
            {
                return new WarehouseResolutionPreviewResult(active, cart);
            }
            else
            {
                Invalidate(active, WarehouseResolutionErrorCodes.CartChangedReResolutionRequired, nowUtc);
            }
        }

        var resolution = await CreateResolutionRowAsync(
            tenantId,
            session.Id,
            session.CartId,
            input,
            WarehouseResolutionSource.StorefrontCheckoutRefresh,
            nowUtc,
            cancellationToken);

        return new WarehouseResolutionPreviewResult(resolution, cart);
    }

    public async Task<WarehouseResolutionPreviewResult> ResolveForSelectedShippingMethodAsync(
        Guid tenantId,
        CartOwner owner,
        Guid checkoutSessionId,
        Guid shippingMethodId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadOwnedActiveSessionAsync(
            tenantId,
            checkoutSessionId,
            owner.OwnerId,
            owner.OwnerType,
            customerId: TryParseCustomerId(owner),
            sessionId: string.Equals(owner.OwnerType, "session", StringComparison.OrdinalIgnoreCase) ? owner.OwnerId : null,
            cancellationToken);

        var cart = await _cartService.GetCartAsync(tenantId, owner, correlationId, cancellationToken);
        ValidateCartForSession(tenantId, owner.OwnerId, owner.OwnerType, session, cart);

        var warehouseId = await ValidateSelectedShippingMethodAuthorityAsync(
            tenantId,
            shippingMethodId,
            cancellationToken);
        var input = await BuildResolutionInputForWarehouseAsync(
            tenantId,
            session.Id,
            session.CartId,
            cart.Items,
            shippingMethodId,
            warehouseId,
            cancellationToken);

        var active = await LoadActiveResolutionAsync(tenantId, session.Id, cancellationToken);
        var nowUtc = DateTime.UtcNow;

        if (active != null)
        {
            if (active.ExpiresAtUtc.HasValue && active.ExpiresAtUtc.Value <= nowUtc)
            {
                Invalidate(active, "expired", nowUtc, WarehouseResolutionStatus.Expired);
            }
            else if (active.WarehouseId == warehouseId &&
                     string.Equals(active.InputsSnapshotHash, input.Hash, StringComparison.Ordinal))
            {
                return new WarehouseResolutionPreviewResult(active, cart);
            }
            else
            {
                Invalidate(active, WarehouseResolutionErrorCodes.CartChangedReResolutionRequired, nowUtc);
            }
        }

        var resolution = await CreateResolutionRowAsync(
            tenantId,
            session.Id,
            session.CartId,
            input,
            WarehouseResolutionSource.StorefrontCheckoutRefresh,
            nowUtc,
            cancellationToken);

        return new WarehouseResolutionPreviewResult(resolution, cart);
    }

    public async Task<WarehouseResolutionCheckoutValidation> ValidateSubmittedResolutionAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        Guid? customerId,
        string? sessionId,
        CartSnapshot snapshot,
        CheckoutRequestDto request,
        string correlationId,
        bool lockForCheckout,
        CancellationToken cancellationToken = default)
    {
        if (!request.CheckoutSessionId.HasValue || request.CheckoutSessionId.Value == Guid.Empty ||
            !request.WarehouseResolutionId.HasValue || request.WarehouseResolutionId.Value == Guid.Empty ||
            !request.WarehouseResolutionVersion.HasValue || request.WarehouseResolutionVersion.Value <= 0)
        {
            throw Conflict(
                WarehouseResolutionErrorCodes.CheckoutSessionStale,
                "Checkout shipping methods must be refreshed before placing the order.");
        }

        var session = await LoadOwnedActiveSessionAsync(
            tenantId,
            request.CheckoutSessionId.Value,
            ownerId,
            ownerType,
            customerId,
            sessionId,
            cancellationToken);

        if (session.CartId != snapshot.CartId)
        {
            throw Conflict(
                WarehouseResolutionErrorCodes.CheckoutSessionStale,
                "Checkout session no longer matches this cart.");
        }

        var resolution = await LoadResolutionForCheckoutAsync(
            tenantId,
            request.WarehouseResolutionId.Value,
            request.WarehouseResolutionVersion.Value,
            lockForCheckout,
            cancellationToken);

        if (resolution == null ||
            resolution.CheckoutSessionId != session.Id ||
            resolution.CartId != snapshot.CartId)
        {
            throw Conflict(
                WarehouseResolutionErrorCodes.WarehouseChanged,
                "Warehouse resolution changed. Refresh shipping methods and retry.");
        }

        var nowUtc = DateTime.UtcNow;
        if (resolution.ExpiresAtUtc.HasValue && resolution.ExpiresAtUtc.Value <= nowUtc)
        {
            Invalidate(resolution, "expired", nowUtc, WarehouseResolutionStatus.Expired);
            throw Conflict(
                WarehouseResolutionErrorCodes.WarehouseResolutionExpired,
                "Warehouse resolution expired. Refresh shipping methods and retry.");
        }

        if (resolution.Status != WarehouseResolutionStatus.Resolved &&
            resolution.Status != WarehouseResolutionStatus.Locked)
        {
            throw Conflict(
                WarehouseResolutionErrorCodes.WarehouseChanged,
                "Warehouse resolution is no longer active.");
        }

        if (!resolution.WarehouseId.HasValue)
        {
            throw Conflict(
                WarehouseResolutionErrorCodes.WarehouseChanged,
                "Warehouse resolution is incomplete.");
        }

        var expectedWarehouseId = await ValidateSelectedShippingMethodAuthorityAsync(
            tenantId,
            request.ShippingMethodId,
            cancellationToken);

        if (resolution.WarehouseId.Value != expectedWarehouseId)
        {
            throw Conflict(
                WarehouseResolutionErrorCodes.WarehouseChanged,
                "Warehouse resolution does not match the selected shipping method.");
        }

        var input = await BuildResolutionInputForWarehouseAsync(
            tenantId,
            session.Id,
            snapshot.CartId,
            snapshot.Items,
            request.ShippingMethodId,
            expectedWarehouseId,
            cancellationToken);

        if (!string.Equals(input.Hash, resolution.InputsSnapshotHash, StringComparison.Ordinal))
        {
            if (!input.CandidateWarehouseIds.Contains(resolution.WarehouseId.Value))
            {
                Invalidate(resolution, WarehouseResolutionErrorCodes.InsufficientStockAtResolvedWarehouse, nowUtc);
                throw Conflict(
                    WarehouseResolutionErrorCodes.InsufficientStockAtResolvedWarehouse,
                    "The resolved warehouse no longer has enough stock for this cart.");
            }

            Invalidate(resolution, WarehouseResolutionErrorCodes.CartChangedReResolutionRequired, nowUtc);
            throw Conflict(
                WarehouseResolutionErrorCodes.CartChangedReResolutionRequired,
                "Cart or warehouse inputs changed. Refresh shipping methods and retry.");
        }

        if (!input.CandidateWarehouseIds.Contains(resolution.WarehouseId.Value))
        {
            Invalidate(resolution, WarehouseResolutionErrorCodes.InsufficientStockAtResolvedWarehouse, nowUtc);
            throw Conflict(
                WarehouseResolutionErrorCodes.InsufficientStockAtResolvedWarehouse,
                "The resolved warehouse no longer has enough stock for this cart.");
        }

        if (lockForCheckout && resolution.Status == WarehouseResolutionStatus.Resolved)
        {
            resolution.Status = WarehouseResolutionStatus.Locked;
            resolution.LockedAtUtc = nowUtc;
            resolution.UpdatedAtUtc = nowUtc;
            resolution.UpdatedAt = nowUtc;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Warehouse resolution validated for checkout | TenantId: {TenantId} | CheckoutSessionId: {CheckoutSessionId} | WarehouseResolutionId: {WarehouseResolutionId} | WarehouseId: {WarehouseId} | CorrelationId: {CorrelationId}",
            tenantId,
            session.Id,
            resolution.Id,
            resolution.WarehouseId.Value,
            correlationId);

        return new WarehouseResolutionCheckoutValidation(resolution, resolution.WarehouseId.Value);
    }

    public async Task MarkOrderedAsync(
        Guid tenantId,
        Guid warehouseResolutionId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _dbContext.WarehouseResolutions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId && item.Id == warehouseResolutionId && !item.IsDeleted,
                cancellationToken);

        if (resolution == null)
        {
            throw Conflict(
                WarehouseResolutionErrorCodes.WarehouseChanged,
                "Warehouse resolution disappeared before order conversion.");
        }

        if (resolution.Status == WarehouseResolutionStatus.Ordered && resolution.ConvertedOrderId == orderId)
        {
            return;
        }

        if (resolution.Status != WarehouseResolutionStatus.Locked || resolution.ConvertedOrderId.HasValue)
        {
            throw Conflict(
                WarehouseResolutionErrorCodes.WarehouseChanged,
                "Warehouse resolution is not locked for this order conversion.");
        }

        var nowUtc = DateTime.UtcNow;
        resolution.Status = WarehouseResolutionStatus.Ordered;
        resolution.ConvertedOrderId = orderId;
        resolution.UpdatedAtUtc = nowUtc;
        resolution.UpdatedAt = nowUtc;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<WarehouseResolution> CreateResolutionRowAsync(
        Guid tenantId,
        Guid checkoutSessionId,
        Guid cartId,
        ResolutionInput input,
        WarehouseResolutionSource source,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var nextVersion = await _dbContext.WarehouseResolutions
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.CheckoutSessionId == checkoutSessionId)
            .Select(item => (int?)item.ResolutionVersion)
            .MaxAsync(cancellationToken) ?? 0;
        nextVersion++;

        var resolution = new WarehouseResolution
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CheckoutSessionId = checkoutSessionId,
            CartId = cartId,
            ResolutionSource = source,
            ResolutionVersion = nextVersion,
            AlgorithmCode = AlgorithmCode,
            InputsSnapshotHash = input.Hash,
            InputsSnapshotJson = input.SafeJson,
            CreatedAt = nowUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAt = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        if (input.CandidateWarehouseIds.Count == 1)
        {
            resolution.Status = WarehouseResolutionStatus.Resolved;
            resolution.WarehouseId = input.CandidateWarehouseIds[0];
            resolution.ResolvedAtUtc = nowUtc;
            resolution.ExpiresAtUtc = nowUtc.Add(ResolutionTtl);
        }
        else if (input.CandidateWarehouseIds.Count == 0)
        {
            resolution.Status = WarehouseResolutionStatus.Failed;
            resolution.FailureCode = WarehouseResolutionErrorCodes.NoEligibleWarehouse;
        }
        else
        {
            resolution.Status = WarehouseResolutionStatus.Failed;
            resolution.FailureCode = WarehouseResolutionErrorCodes.WarehouseSelectionPolicyRequired;
        }

        _dbContext.WarehouseResolutions.Add(resolution);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _dbContext.Entry(resolution).State = EntityState.Detached;
            var concurrent = await _dbContext.WarehouseResolutions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.TenantId == tenantId &&
                               item.CheckoutSessionId == checkoutSessionId &&
                               item.InputsSnapshotHash == input.Hash &&
                               !item.IsDeleted)
                .OrderByDescending(item => item.ResolutionVersion)
                .FirstOrDefaultAsync(cancellationToken);
            if (concurrent != null)
            {
                ThrowIfFailed(concurrent);
                if (concurrent.Status is WarehouseResolutionStatus.Resolved or WarehouseResolutionStatus.Locked)
                {
                    return concurrent;
                }
            }

            throw Conflict(
                WarehouseResolutionErrorCodes.WarehouseChanged,
                "Warehouse resolution changed concurrently. Refresh shipping methods and retry.");
        }

        ThrowIfFailed(resolution);

        return resolution;
    }

    private static void ThrowIfFailed(WarehouseResolution resolution)
    {
        if (resolution.Status != WarehouseResolutionStatus.Failed)
        {
            return;
        }

        throw new WarehouseResolutionException(
            resolution.FailureCode!,
            resolution.FailureCode == WarehouseResolutionErrorCodes.NoEligibleWarehouse
                ? "No active same-tenant warehouse can fulfill this cart."
                : "Multiple warehouses can fulfill this cart and no warehouse selection policy is configured.",
            StatusCodes.Status422UnprocessableEntity);
    }

    private async Task<ResolutionInput> BuildResolutionInputAsync(
        Guid tenantId,
        Guid checkoutSessionId,
        Guid cartId,
        IEnumerable<CartItemDto> items,
        CancellationToken cancellationToken)
    {
        var lines = items
            .Where(item => item.Quantity > 0)
            .Select(item => new ResolutionLine(item.ProductId, item.VariantId, item.Quantity))
            .ToList();

        return await BuildResolutionInputAsync(tenantId, checkoutSessionId, cartId, lines, cancellationToken);
    }

    private async Task<ResolutionInput> BuildResolutionInputAsync(
        Guid tenantId,
        Guid checkoutSessionId,
        Guid cartId,
        IEnumerable<CartItemSnapshot> items,
        CancellationToken cancellationToken)
    {
        var lines = items
            .Where(item => item.Quantity > 0)
            .Select(item => new ResolutionLine(item.ProductId, item.VariantId, item.Quantity))
            .ToList();

        return await BuildResolutionInputAsync(tenantId, checkoutSessionId, cartId, lines, cancellationToken);
    }

    private async Task<ResolutionInput> BuildResolutionInputForWarehouseAsync(
        Guid tenantId,
        Guid checkoutSessionId,
        Guid cartId,
        IEnumerable<CartItemDto> items,
        Guid shippingMethodId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        var lines = items
            .Where(item => item.Quantity > 0)
            .Select(item => new ResolutionLine(item.ProductId, item.VariantId, item.Quantity))
            .ToList();

        return await BuildResolutionInputForWarehouseAsync(
            tenantId,
            checkoutSessionId,
            cartId,
            lines,
            shippingMethodId,
            warehouseId,
            cancellationToken);
    }

    private async Task<ResolutionInput> BuildResolutionInputForWarehouseAsync(
        Guid tenantId,
        Guid checkoutSessionId,
        Guid cartId,
        IEnumerable<CartItemSnapshot> items,
        Guid shippingMethodId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        var lines = items
            .Where(item => item.Quantity > 0)
            .Select(item => new ResolutionLine(item.ProductId, item.VariantId, item.Quantity))
            .ToList();

        return await BuildResolutionInputForWarehouseAsync(
            tenantId,
            checkoutSessionId,
            cartId,
            lines,
            shippingMethodId,
            warehouseId,
            cancellationToken);
    }

    private async Task<ResolutionInput> BuildResolutionInputForWarehouseAsync(
        Guid tenantId,
        Guid checkoutSessionId,
        Guid cartId,
        IReadOnlyCollection<ResolutionLine> lines,
        Guid shippingMethodId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        var baseInput = await BuildResolutionInputAsync(
            tenantId,
            checkoutSessionId,
            cartId,
            lines,
            cancellationToken);
        var shippingDecision = await _dbContext.ShippingMethods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.Id == shippingMethodId && !item.IsDeleted)
            .Select(item => new { item.CarrierCode, item.ServiceLevel })
            .SingleOrDefaultAsync(cancellationToken);
        if (shippingDecision == null)
        {
            throw Unprocessable(
                WarehouseResolutionErrorCodes.ShippingMethodIncompatibleWithWarehouse,
                "Selected shipping method is not available.");
        }

        var selectedWarehouseCanFulfill = baseInput.CandidateWarehouseIds.Contains(warehouseId);
        var selectedSnapshot = new
        {
            TenantId = tenantId,
            CheckoutSessionId = checkoutSessionId,
            CartId = cartId,
            ShippingMethodId = shippingMethodId,
            CarrierCode = CarrierCodes.Normalize(shippingDecision.CarrierCode),
            ServiceLevel = (shippingDecision.ServiceLevel ?? string.Empty).Trim().ToLowerInvariant(),
            FulfillmentWarehouseId = warehouseId,
            AlgorithmCode,
            BaseResolutionHash = baseInput.Hash,
            SelectedWarehouseCanFulfill = selectedWarehouseCanFulfill
        };

        return new ResolutionInput(
            DeterministicRequestHasher.ComputeHash(selectedSnapshot),
            JsonSerializer.Serialize(selectedSnapshot, JsonOptions),
            selectedWarehouseCanFulfill ? new List<Guid> { warehouseId } : new List<Guid>());
    }

    private async Task<ResolutionInput> BuildResolutionInputAsync(
        Guid tenantId,
        Guid checkoutSessionId,
        Guid cartId,
        IReadOnlyCollection<ResolutionLine> lines,
        CancellationToken cancellationToken)
    {
        var variantIds = lines
            .Where(line => line.VariantId.HasValue && line.VariantId.Value != Guid.Empty)
            .Select(line => line.VariantId!.Value)
            .Distinct()
            .ToList();

        var variants = await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .WhereInventoryReservableLineage(tenantId)
            .Where(variant => variantIds.Contains(variant.Id))
            .Select(variant => new
            {
                variant.Id,
                variant.ProductId,
                variant.Product.TrackStock,
                variant.Product.AllowBackorder
            })
            .ToListAsync(cancellationToken);

        var variantById = variants.ToDictionary(item => item.Id);
        var hasInvalidLineage = lines.Count == 0 || lines.Any(line =>
            !line.VariantId.HasValue ||
            !variantById.TryGetValue(line.VariantId.Value, out var variant) ||
            variant.ProductId != line.ProductId);
        var requiredQuantities = lines
            .Where(line => line.VariantId.HasValue &&
                           variantById.TryGetValue(line.VariantId.Value, out var variant) &&
                           variant.ProductId == line.ProductId &&
                           variant.TrackStock &&
                           !variant.AllowBackorder)
            .GroupBy(line => line.VariantId!.Value)
            .Select(group => new VariantRequirement(group.Key, group.Sum(item => item.Quantity)))
            .OrderBy(item => item.VariantId)
            .ToList();

        var warehouses = await _dbContext.Warehouses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(warehouse => warehouse.TenantId == tenantId && warehouse.IsActive && !warehouse.IsDeleted)
            .Select(warehouse => new { warehouse.Id, warehouse.IsActive })
            .OrderBy(warehouse => warehouse.Id)
            .ToListAsync(cancellationToken);

        Dictionary<(Guid WarehouseId, Guid VariantId), decimal> stockByWarehouseVariant;
        if (requiredQuantities.Count == 0)
        {
            stockByWarehouseVariant = new Dictionary<(Guid WarehouseId, Guid VariantId), decimal>();
        }
        else
        {
            var requiredVariantIds = requiredQuantities.Select(item => item.VariantId).ToList();
            stockByWarehouseVariant = await _dbContext.ProductVariantStocks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .WhereCurrentInventoryStockOwner(tenantId)
                .Where(stock => requiredVariantIds.Contains(stock.VariantId))
                .GroupBy(stock => new { stock.WarehouseId, stock.VariantId })
                .Select(group => new
                {
                    group.Key.WarehouseId,
                    group.Key.VariantId,
                    Available = group.Sum(stock => stock.Quantity - stock.ReservedQuantity)
                })
                .ToDictionaryAsync(
                    item => (item.WarehouseId, item.VariantId),
                    item => item.Available,
                    cancellationToken);
        }

        var candidates = warehouses
            .Where(warehouse => !hasInvalidLineage &&
                (requiredQuantities.Count == 0 ||
                 requiredQuantities.All(requirement =>
                     stockByWarehouseVariant.TryGetValue((warehouse.Id, requirement.VariantId), out var available) &&
                     available >= requirement.Quantity)))
            .Select(warehouse => warehouse.Id)
            .OrderBy(id => id)
            .ToList();

        var safeSnapshot = new
        {
            TenantId = tenantId,
            CheckoutSessionId = checkoutSessionId,
            CartId = cartId,
            AlgorithmCode,
            HasInvalidLineage = hasInvalidLineage,
            Lines = lines
                .OrderBy(line => line.VariantId ?? Guid.Empty)
                .ThenBy(line => line.ProductId)
                .Select(line => new { line.ProductId, line.VariantId, line.Quantity }),
            RequiredVariants = requiredQuantities,
            ActiveWarehouseIds = warehouses.Select(warehouse => warehouse.Id),
            Stock = stockByWarehouseVariant
                .OrderBy(item => item.Key.WarehouseId)
                .ThenBy(item => item.Key.VariantId)
                .Select(item => new
                {
                    item.Key.WarehouseId,
                    item.Key.VariantId,
                    Available = item.Value
                })
        };

        var safeJson = JsonSerializer.Serialize(safeSnapshot, JsonOptions);
        var hash = DeterministicRequestHasher.ComputeHash(safeSnapshot);
        return new ResolutionInput(hash, safeJson, candidates);
    }

    private async Task<Guid> ValidateSelectedShippingMethodAuthorityAsync(
        Guid tenantId,
        Guid shippingMethodId,
        CancellationToken cancellationToken)
    {
        var method = await _dbContext.ShippingMethods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.Id == shippingMethodId &&
                        item.IsActive &&
                        !item.IsDeleted,
                cancellationToken);

        if (method == null)
        {
            throw Unprocessable(
                WarehouseResolutionErrorCodes.ShippingMethodIncompatibleWithWarehouse,
                "Selected shipping method is not available.");
        }

        if (!method.FulfillmentWarehouseId.HasValue || method.FulfillmentWarehouseId.Value == Guid.Empty)
        {
            throw Unprocessable(
                WarehouseResolutionErrorCodes.ShippingMethodIncompatibleWithWarehouse,
                "Fulfillment warehouse is required.");
        }

        var warehouseId = method.FulfillmentWarehouseId.Value;
        var warehouseActive = await _dbContext.Warehouses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                warehouse => warehouse.TenantId == tenantId &&
                             warehouse.Id == warehouseId &&
                             warehouse.IsActive &&
                             !warehouse.IsDeleted,
                cancellationToken);
        if (!warehouseActive)
        {
            throw Conflict(
                WarehouseResolutionErrorCodes.WarehouseInactive,
                "Selected fulfillment warehouse is inactive.");
        }

        var carrierCode = CarrierCodes.Normalize(method.CarrierCode);
        var serviceLevelReadiness = await _carrierIntegrationAuthority.GetServiceLevelReadinessAsync(
            tenantId,
            carrierCode,
            method.ServiceLevel,
            cancellationToken);
        if (!serviceLevelReadiness.CanExecute)
        {
            throw Conflict(
                WarehouseResolutionErrorCodes.CarrierAssignmentUnavailable,
                "Selected carrier service level is not currently available.");
        }

        if (CarrierCodes.IsManual(carrierCode))
        {
            return warehouseId;
        }

        var readiness = await _carrierIntegrationAuthority.GetIntegrationReadinessAsync(
            tenantId,
            carrierCode,
            method.Id,
            warehouseId,
            CarrierIntegrationPurpose.StorefrontAdmission,
            cancellationToken);
        if (!readiness.CanExecute)
        {
            throw Conflict(
                WarehouseResolutionErrorCodes.CarrierAssignmentUnavailable,
                "Selected carrier is not currently configured.");
        }

        return warehouseId;
    }

    private async Task<CheckoutSession> LoadOwnedActiveSessionAsync(
        Guid tenantId,
        Guid checkoutSessionId,
        string ownerId,
        string ownerType,
        Guid? customerId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var session = await _dbContext.CheckoutSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.Id == checkoutSessionId &&
                        item.Status == CheckoutSessionStatus.Active &&
                        item.InvalidatedAtUtc == null,
                cancellationToken);

        if (session == null)
        {
            throw Conflict(
                WarehouseResolutionErrorCodes.CheckoutSessionStale,
                "Checkout session is not active. Refresh checkout and retry.");
        }

        if (session.OnlineCustomerId.HasValue)
        {
            if (customerId.HasValue && customerId.Value == session.OnlineCustomerId.Value)
            {
                return session;
            }

            if (string.Equals(ownerType, "customer", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(ownerId, out var ownerCustomerId) &&
                ownerCustomerId == session.OnlineCustomerId.Value)
            {
                return session;
            }

            throw Conflict(
                WarehouseResolutionErrorCodes.CheckoutSessionStale,
                "Checkout session is not owned by this customer.");
        }

        if (!string.IsNullOrWhiteSpace(session.SessionId) &&
            string.Equals(session.SessionId.Trim(), sessionId ?? ownerId, StringComparison.OrdinalIgnoreCase))
        {
            return session;
        }

        throw Conflict(
            WarehouseResolutionErrorCodes.CheckoutSessionStale,
            "Checkout session is not owned by this browser session.");
    }

    private static void ValidateCartForSession(
        Guid tenantId,
        string ownerId,
        string ownerType,
        CheckoutSession session,
        CartDto cart)
    {
        if (cart.TenantId != tenantId ||
            cart.CartId != session.CartId ||
            !string.Equals(cart.OwnerType, ownerType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(cart.OwnerId, ownerId, StringComparison.Ordinal))
        {
            throw Conflict(
                WarehouseResolutionErrorCodes.CheckoutSessionStale,
                "Checkout session no longer matches the authoritative cart.");
        }
    }

    private async Task<WarehouseResolution?> LoadActiveResolutionAsync(
        Guid tenantId,
        Guid checkoutSessionId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.WarehouseResolutions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.CheckoutSessionId == checkoutSessionId &&
                        !item.IsDeleted &&
                        (item.Status == WarehouseResolutionStatus.Resolved ||
                         item.Status == WarehouseResolutionStatus.Locked),
                cancellationToken);
    }

    private async Task<WarehouseResolution?> LoadResolutionForCheckoutAsync(
        Guid tenantId,
        Guid warehouseResolutionId,
        int resolutionVersion,
        bool lockForCheckout,
        CancellationToken cancellationToken)
    {
        if (lockForCheckout && IsPostgresProvider())
        {
            return await _dbContext.WarehouseResolutions
                .FromSqlInterpolated($@"
                    SELECT * FROM ""WarehouseResolutions""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {warehouseResolutionId}
                      AND ""ResolutionVersion"" = {resolutionVersion}
                      AND ""IsDeleted"" = false
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.WarehouseResolutions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.Id == warehouseResolutionId &&
                        item.ResolutionVersion == resolutionVersion &&
                        !item.IsDeleted,
                cancellationToken);
    }

    private void Invalidate(
        WarehouseResolution resolution,
        string reason,
        DateTime nowUtc,
        WarehouseResolutionStatus status = WarehouseResolutionStatus.Invalidated)
    {
        resolution.Status = status;
        resolution.InvalidationReason = reason;
        resolution.InvalidatedAtUtc = nowUtc;
        resolution.UpdatedAtUtc = nowUtc;
        resolution.UpdatedAt = nowUtc;
    }

    private bool IsPostgresProvider() =>
        _dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private static Guid? TryParseCustomerId(CartOwner owner)
    {
        return string.Equals(owner.OwnerType, "customer", StringComparison.OrdinalIgnoreCase) &&
               Guid.TryParse(owner.OwnerId, out var customerId)
            ? customerId
            : null;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static WarehouseResolutionException Conflict(string errorCode, string message) =>
        new(errorCode, message, StatusCodes.Status409Conflict);

    private static WarehouseResolutionException Unprocessable(string errorCode, string message) =>
        new(errorCode, message, StatusCodes.Status422UnprocessableEntity);

    private sealed record ResolutionLine(Guid ProductId, Guid? VariantId, int Quantity);

    private sealed record VariantRequirement(Guid VariantId, int Quantity);

    private sealed record ResolutionInput(string Hash, string SafeJson, List<Guid> CandidateWarehouseIds);
}
