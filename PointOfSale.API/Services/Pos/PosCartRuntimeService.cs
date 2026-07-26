using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.PosCart;
using PointOfSale.API.Services.Inventory;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Pos;

public sealed class PosCartRuntimeService : IPosCartRuntimeService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<PosCartRuntimeService> _logger;

    public PosCartRuntimeService(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<PosCartRuntimeService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public Task<PosCartServiceResult<PosCartResponse>> CreateAsync(
        CreatePosCartRequest request,
        Guid cashierId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteIdempotentAsync(
            "CREATE",
            idempotencyKey,
            new { request.WarehouseId, request.CustomerId, request.ExpiresInMinutes, CashierId = cashierId },
            async () =>
            {
                var tenantId = _tenantContext.RequiredTenantId;
                var now = DateTime.UtcNow;

                var activeCartExists = await _context.PosCarts.AnyAsync(cart =>
                    cart.TenantId == tenantId &&
                    cart.CashierId == cashierId &&
                    (cart.Status == PosCartStatus.Active ||
                     cart.Status == PosCartStatus.Held ||
                     cart.Status == PosCartStatus.Recovered),
                    cancellationToken);
                if (activeCartExists)
                {
                    return PosCartServiceResult<PosCartResponse>.Conflict("Cashier already has an open POS cart.");
                }

                var warehouse = await LoadActiveWarehouseAsync(request.WarehouseId, cancellationToken);
                if (warehouse == null)
                {
                    return PosCartServiceResult<PosCartResponse>.BadRequest("Warehouse not found.");
                }

                if (!await CashierExistsAsync(cashierId, cancellationToken))
                {
                    return PosCartServiceResult<PosCartResponse>.BadRequest("Cashier not found.");
                }

                if (request.CustomerId.HasValue &&
                    !await CustomerExistsAsync(request.CustomerId.Value, cancellationToken))
                {
                    return PosCartServiceResult<PosCartResponse>.BadRequest("Customer not found.");
                }

                var cart = new PosCart
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    CashierId = cashierId,
                    WarehouseId = request.WarehouseId,
                    CustomerId = request.CustomerId,
                    Status = PosCartStatus.Active,
                    CreatedAt = now,
                    UpdatedAt = now,
                    LastActivityAtUtc = now,
                    ExpiresAtUtc = now.AddMinutes(request.ExpiresInMinutes)
                };

                _context.PosCarts.Add(cart);
                StageOutbox(cart, "PosCartCreated", idempotencyKey, now);
                return PosCartServiceResult<PosCartResponse>.Created(ToResponse(cart));
            },
            cancellationToken);
    }

    public async Task<PosCartServiceResult<PosCartResponse>> GetAsync(
        Guid cartId,
        Guid cashierId,
        CancellationToken cancellationToken = default)
    {
        var cart = await LoadCartAsync(cartId, cancellationToken);
        if (cart == null || cart.CashierId != cashierId)
        {
            return PosCartServiceResult<PosCartResponse>.NotFound("POS cart not found.");
        }

        return PosCartServiceResult<PosCartResponse>.Ok(ToResponse(cart));
    }

    public Task<PosCartServiceResult<PosCartResponse>> AddItemAsync(
        Guid cartId,
        UpsertPosCartItemRequest request,
        Guid cashierId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteIdempotentAsync(
            "ADD_ITEM",
            idempotencyKey,
            new { cartId, request.ProductId, request.ProductVariantId, request.UnitId, request.Quantity, request.DiscountAmount, request.ExpectedVersion, cashierId },
            async () =>
            {
                var now = DateTime.UtcNow;
                var cart = await LoadCartForMutationAsync(cartId, cancellationToken);
                var guard = EnsureMutableCart(cart, cashierId, request.ExpectedVersion);
                if (guard != null)
                {
                    return guard;
                }

                var snapshot = await BuildItemSnapshotAsync(cart!, request, cancellationToken);
                if (!snapshot.Succeeded)
                {
                    return PosCartServiceResult<PosCartResponse>.BadRequest(snapshot.Message!);
                }

                var existing = cart!.Items.FirstOrDefault(item =>
                    item.ProductVariantId == snapshot.Item!.ProductVariantId &&
                    item.UnitId == snapshot.Item.UnitId &&
                    !item.IsDeleted);

                if (existing == null)
                {
                    snapshot.Item.LineNumber = cart.Items.Count(item => !item.IsDeleted) + 1;
                    cart.Items.Add(snapshot.Item);
                    _context.PosCartItems.Add(snapshot.Item);
                }
                else
                {
                    ApplyItemSnapshot(existing, snapshot.Item);
                }

                TouchCart(cart, now);
                RecalculateTotals(cart);
                StageOutbox(cart, "PosCartItemAdded", idempotencyKey, now);
                return PosCartServiceResult<PosCartResponse>.Ok(ToResponse(cart));
            },
            cancellationToken);
    }

    public Task<PosCartServiceResult<PosCartResponse>> UpdateItemAsync(
        Guid cartId,
        Guid itemId,
        UpdatePosCartItemRequest request,
        Guid cashierId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteIdempotentAsync(
            "UPDATE_ITEM",
            idempotencyKey,
            new { cartId, itemId, request.Quantity, request.DiscountAmount, request.ExpectedVersion, cashierId },
            async () =>
            {
                var now = DateTime.UtcNow;
                var cart = await LoadCartForMutationAsync(cartId, cancellationToken);
                var guard = EnsureMutableCart(cart, cashierId, request.ExpectedVersion);
                if (guard != null)
                {
                    return guard;
                }

                var item = cart!.Items.FirstOrDefault(candidate => candidate.Id == itemId && !candidate.IsDeleted);
                if (item == null)
                {
                    return PosCartServiceResult<PosCartResponse>.NotFound("POS cart item not found.");
                }

                var snapshot = await BuildItemSnapshotAsync(cart, new UpsertPosCartItemRequest
                {
                    ProductId = item.ProductId,
                    ProductVariantId = item.ProductVariantId,
                    UnitId = item.UnitId,
                    Quantity = request.Quantity,
                    DiscountAmount = request.DiscountAmount,
                    ExpectedVersion = request.ExpectedVersion
                }, cancellationToken);
                if (!snapshot.Succeeded)
                {
                    return PosCartServiceResult<PosCartResponse>.BadRequest(snapshot.Message!);
                }

                ApplyItemSnapshot(item, snapshot.Item!);
                TouchCart(cart, now);
                RecalculateTotals(cart);
                StageOutbox(cart, "PosCartItemUpdated", idempotencyKey, now);
                return PosCartServiceResult<PosCartResponse>.Ok(ToResponse(cart));
            },
            cancellationToken);
    }

    public Task<PosCartServiceResult<PosCartResponse>> RemoveItemAsync(
        Guid cartId,
        Guid itemId,
        PosCartLifecycleRequest request,
        Guid cashierId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteIdempotentAsync(
            "REMOVE_ITEM",
            idempotencyKey,
            new { cartId, itemId, request.ExpectedVersion, cashierId },
            async () =>
            {
                var now = DateTime.UtcNow;
                var cart = await LoadCartForMutationAsync(cartId, cancellationToken);
                var guard = EnsureMutableCart(cart, cashierId, request.ExpectedVersion);
                if (guard != null)
                {
                    return guard;
                }

                var item = cart!.Items.FirstOrDefault(candidate => candidate.Id == itemId && !candidate.IsDeleted);
                if (item == null)
                {
                    return PosCartServiceResult<PosCartResponse>.NotFound("POS cart item not found.");
                }

                item.IsDeleted = true;
                item.UpdatedAt = now;
                TouchCart(cart, now);
                RecalculateTotals(cart);
                StageOutbox(cart, "PosCartItemRemoved", idempotencyKey, now);
                return PosCartServiceResult<PosCartResponse>.Ok(ToResponse(cart));
            },
            cancellationToken);
    }

    public Task<PosCartServiceResult<PosCartResponse>> TransitionAsync(
        Guid cartId,
        string transition,
        PosCartLifecycleRequest request,
        Guid cashierId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteIdempotentAsync(
            transition.ToUpperInvariant(),
            idempotencyKey,
            new { cartId, transition, request.ExpectedVersion, Reason = request.Reason?.Trim(), cashierId },
            async () =>
            {
                var now = DateTime.UtcNow;
                var cart = await LoadCartForMutationAsync(cartId, cancellationToken);
                if (cart == null || cart.CashierId != cashierId)
                {
                    return PosCartServiceResult<PosCartResponse>.NotFound("POS cart not found.");
                }

                if (cart.Version != request.ExpectedVersion)
                {
                    return PosCartServiceResult<PosCartResponse>.Conflict("POS cart version conflict.");
                }

                var error = ApplyTransition(cart, transition, request.Reason, now);
                if (error != null)
                {
                    return PosCartServiceResult<PosCartResponse>.Conflict(error);
                }

                StageOutbox(cart, $"PosCart{transition}", idempotencyKey, now);
                return PosCartServiceResult<PosCartResponse>.Ok(ToResponse(cart));
            },
            cancellationToken);
    }

    public async Task<PosCartCheckoutPreparation> PrepareCheckoutAsync(
        Guid cartId,
        CreateSaleRequest request,
        Guid cashierId,
        CancellationToken cancellationToken = default)
    {
        var cart = await LoadCartForMutationAsync(cartId, cancellationToken);
        if (cart == null || cart.CashierId != cashierId)
        {
            return PosCartCheckoutPreparation.Fail(StatusCodes.Status404NotFound, "POS cart not found.", request);
        }

        if (cart.Status is not (PosCartStatus.Active or PosCartStatus.Recovered or PosCartStatus.Completed))
        {
            return PosCartCheckoutPreparation.Fail(StatusCodes.Status409Conflict, "POS cart is not checkout-ready.", request);
        }

        if (cart.Status == PosCartStatus.Completed && !cart.CompletedSaleId.HasValue)
        {
            return PosCartCheckoutPreparation.Fail(StatusCodes.Status409Conflict, "POS cart completion is missing sale authority.", request);
        }

        if (cart.Status != PosCartStatus.Completed && cart.ExpiresAtUtc <= DateTime.UtcNow)
        {
            cart.Status = PosCartStatus.Expired;
            cart.ExpiredAtUtc = DateTime.UtcNow;
            cart.Version++;
            cart.UpdatedAt = DateTime.UtcNow;
            return PosCartCheckoutPreparation.Fail(StatusCodes.Status409Conflict, "POS cart has expired.", request);
        }

        if (cart.Items.All(item => item.IsDeleted))
        {
            return PosCartCheckoutPreparation.Fail(StatusCodes.Status400BadRequest, "POS cart is empty.", request);
        }

        if (request.WarehouseId != Guid.Empty && request.WarehouseId != cart.WarehouseId)
        {
            return PosCartCheckoutPreparation.Fail(StatusCodes.Status409Conflict, "POS checkout warehouse does not match cart warehouse.", request);
        }

        if (request.CustomerId.HasValue && request.CustomerId != cart.CustomerId)
        {
            return PosCartCheckoutPreparation.Fail(StatusCodes.Status409Conflict, "POS checkout customer does not match cart customer.", request);
        }

        foreach (var item in cart.Items.Where(item => !item.IsDeleted))
        {
            var stock = await _context.ProductVariantStocks
                .FirstOrDefaultAsync(stock =>
                    stock.TenantId == cart.TenantId &&
                    stock.WarehouseId == cart.WarehouseId &&
                    stock.VariantId == item.ProductVariantId &&
                    !stock.IsDeleted,
                    cancellationToken);
            if (stock == null || stock.AvailableQuantity < item.BaseQuantity)
            {
                return PosCartCheckoutPreparation.Fail(StatusCodes.Status409Conflict, "POS cart inventory is no longer available.", request);
            }
        }

        var checkoutRequest = request with
        {
            CustomerId = cart.CustomerId,
            WarehouseId = cart.WarehouseId,
            Items = cart.Items
                .Where(item => !item.IsDeleted)
                .OrderBy(item => item.LineNumber)
                .Select(item => new SaleItemRequest
                {
                    ProductId = item.ProductId,
                    ProductVariantId = item.ProductVariantId,
                    UnitId = item.UnitId,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPriceSnapshot,
                    DiscountAmount = item.DiscountAmount,
                    TaxRate = item.TaxRateSnapshot
                })
                .ToList()
        };

        return PosCartCheckoutPreparation.Success(checkoutRequest);
    }

    public async Task CompleteFromSaleAsync(
        Guid cartId,
        Guid saleId,
        Guid cashierId,
        CancellationToken cancellationToken = default)
    {
        var cart = await LoadCartForMutationAsync(cartId, cancellationToken);
        if (cart == null || cart.CashierId != cashierId)
        {
            throw new InvalidOperationException("POS cart not found for sale completion.");
        }

        if (cart.Status == PosCartStatus.Completed)
        {
            if (cart.CompletedSaleId == saleId)
            {
                return;
            }

            throw new InvalidOperationException("POS cart was already completed by another sale.");
        }

        if (cart.Status is not (PosCartStatus.Active or PosCartStatus.Recovered))
        {
            throw new InvalidOperationException("POS cart cannot be completed from its current state.");
        }

        var now = DateTime.UtcNow;
        cart.Status = PosCartStatus.Completed;
        cart.CompletedAtUtc = now;
        cart.CompletedSaleId = saleId;
        TouchCart(cart, now);
        StageOutbox(cart, "PosCartCompleted", $"POS_CART_COMPLETE:{cart.TenantId:N}:{cart.Id:N}:{saleId:N}", now);
    }

    private async Task<PosCartServiceResult<PosCartResponse>> ExecuteIdempotentAsync(
        string operation,
        string idempotencyKey,
        object fingerprintPayload,
        Func<Task<PosCartServiceResult<PosCartResponse>>> execute,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            return PosCartServiceResult<PosCartResponse>.BadRequest("Idempotency-Key is required and must not exceed 200 characters.");
        }

        var tenantId = _tenantContext.RequiredTenantId;
        var requestHash = Hash(fingerprintPayload);
        var existing = await _context.PosCartRequestIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(record =>
                record.TenantId == tenantId &&
                record.Key == idempotencyKey &&
                record.Operation == operation,
                cancellationToken);

        if (existing != null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return PosCartServiceResult<PosCartResponse>.Conflict("Idempotency key was already used for a different POS cart request.");
            }

            if (existing.ResponseStatusCode.HasValue && !string.IsNullOrWhiteSpace(existing.ResponseJson))
            {
                var replay = JsonSerializer.Deserialize<PosCartResponse>(existing.ResponseJson, JsonOptions);
                return new PosCartServiceResult<PosCartResponse>(existing.ResponseStatusCode.Value, replay);
            }

            return PosCartServiceResult<PosCartResponse>.Conflict("A matching POS cart request is already in progress.");
        }

        var record = new PosCartRequestIdempotency
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Key = idempotencyKey.Trim(),
            Operation = operation,
            RequestHash = requestHash,
            CreatedAt = DateTime.UtcNow
        };
        _context.PosCartRequestIdempotencyRecords.Add(record);

        await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
        var result = await execute();
        if (result.Value != null)
        {
            record.ResponseJson = JsonSerializer.Serialize(result.Value, JsonOptions);
        }

        record.ResponseStatusCode = result.StatusCode;
        record.CompletedAtUtc = DateTime.UtcNow;
        record.UpdatedAt = record.CompletedAtUtc;
        await _context.SaveChangesAsync(cancellationToken);
        await CommitIfNeededAsync(transaction, cancellationToken);
        return result;
    }

    private async Task<ItemSnapshotResult> BuildItemSnapshotAsync(
        PosCart cart,
        UpsertPosCartItemRequest request,
        CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(candidate =>
                candidate.Id == request.ProductId &&
                candidate.TenantId == cart.TenantId &&
                candidate.IsActive &&
                !candidate.IsDeleted,
                cancellationToken);
        if (product == null)
        {
            return ItemSnapshotResult.Fail("Product not found.");
        }

        var variant = await ProductVariantStockMutationSupport.ResolveVariantAsync(
            _context,
            request.ProductId,
            request.ProductVariantId,
            cancellationToken);
        if (variant == null || variant.TenantId != cart.TenantId)
        {
            return ItemSnapshotResult.Fail("Product variant not found.");
        }

        ProductUnit? productUnit = null;
        var unitId = request.UnitId;
        decimal conversionFactor;
        if (unitId.HasValue)
        {
            productUnit = await _context.ProductUnits
                .FirstOrDefaultAsync(unit =>
                    unit.ProductId == request.ProductId &&
                    unit.UnitId == unitId.Value,
                    cancellationToken);
            if (productUnit == null)
            {
                return ItemSnapshotResult.Fail("Unit is not configured for product.");
            }

            conversionFactor = productUnit.ConversionFactor;
        }
        else
        {
            productUnit = await _context.ProductUnits
                .FirstOrDefaultAsync(unit =>
                    unit.ProductId == request.ProductId &&
                    unit.IsDefault,
                    cancellationToken);
            if (productUnit == null)
            {
                return ItemSnapshotResult.Fail("Default unit is not configured for product.");
            }

            unitId = productUnit.UnitId;
            conversionFactor = productUnit.ConversionFactor;
        }

        if (conversionFactor <= 0)
        {
            return ItemSnapshotResult.Fail("Unit conversion is invalid for product.");
        }

        var baseQuantity = request.Quantity * conversionFactor;
        var stock = await _context.ProductVariantStocks
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == cart.TenantId &&
                candidate.WarehouseId == cart.WarehouseId &&
                candidate.VariantId == variant.Id &&
                !candidate.IsDeleted,
                cancellationToken);
        if (stock == null || stock.AvailableQuantity < baseQuantity)
        {
            return ItemSnapshotResult.Fail("Insufficient stock for POS cart item.");
        }

        var unitPrice = RoundCurrency(productUnit.SellingPrice ?? variant.Price * conversionFactor);
        var lineSubtotal = RoundCurrency(request.Quantity * unitPrice);
        var discount = RoundCurrency(request.DiscountAmount);
        if (discount > lineSubtotal)
        {
            return ItemSnapshotResult.Fail("Discount exceeds line subtotal.");
        }

        var taxRate = RoundCurrency(product.TaxRate);
        var taxableAmount = RoundCurrency(lineSubtotal - discount);
        var taxAmount = product.PriceIncludesTax ? 0m : RoundCurrency(taxableAmount * taxRate / 100m);
        var totalAmount = RoundCurrency(taxableAmount + taxAmount);
        var costPrice = Math.Round(variant.CostPrice, 6, MidpointRounding.AwayFromZero);

        return ItemSnapshotResult.Success(new PosCartItem
        {
            Id = Guid.NewGuid(),
            PosCartId = cart.Id,
            ProductId = product.Id,
            ProductVariantId = variant.Id,
            UnitId = unitId,
            Quantity = request.Quantity,
            ConversionFactor = conversionFactor,
            BaseQuantity = baseQuantity,
            UnitPriceSnapshot = unitPrice,
            DiscountAmount = discount,
            TaxRateSnapshot = taxRate,
            TaxAmount = taxAmount,
            TotalAmount = totalAmount,
            CostPriceSnapshot = costPrice,
            CostAmountSnapshot = RoundCurrency(costPrice * baseQuantity),
            ProductNameSnapshot = product.Name,
            VariantSkuSnapshot = variant.SKU,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    private static void ApplyItemSnapshot(PosCartItem target, PosCartItem source)
    {
        target.Quantity = source.Quantity;
        target.ConversionFactor = source.ConversionFactor;
        target.BaseQuantity = source.BaseQuantity;
        target.UnitPriceSnapshot = source.UnitPriceSnapshot;
        target.DiscountAmount = source.DiscountAmount;
        target.TaxRateSnapshot = source.TaxRateSnapshot;
        target.TaxAmount = source.TaxAmount;
        target.TotalAmount = source.TotalAmount;
        target.CostPriceSnapshot = source.CostPriceSnapshot;
        target.CostAmountSnapshot = source.CostAmountSnapshot;
        target.ProductNameSnapshot = source.ProductNameSnapshot;
        target.VariantSkuSnapshot = source.VariantSkuSnapshot;
        target.UpdatedAt = DateTime.UtcNow;
    }

    private PosCartServiceResult<PosCartResponse>? EnsureMutableCart(PosCart? cart, Guid cashierId, int expectedVersion)
    {
        if (cart == null || cart.CashierId != cashierId)
        {
            return PosCartServiceResult<PosCartResponse>.NotFound("POS cart not found.");
        }

        if (cart.Version != expectedVersion)
        {
            return PosCartServiceResult<PosCartResponse>.Conflict("POS cart version conflict.");
        }

        if (cart.Status is not (PosCartStatus.Active or PosCartStatus.Recovered))
        {
            return PosCartServiceResult<PosCartResponse>.Conflict("POS cart is not mutable.");
        }

        if (cart.ExpiresAtUtc <= DateTime.UtcNow)
        {
            cart.Status = PosCartStatus.Expired;
            cart.ExpiredAtUtc = DateTime.UtcNow;
            cart.Version++;
            cart.UpdatedAt = DateTime.UtcNow;
            return PosCartServiceResult<PosCartResponse>.Conflict("POS cart has expired.");
        }

        return null;
    }

    private static string? ApplyTransition(PosCart cart, string transition, string? reason, DateTime now)
    {
        switch (transition.ToUpperInvariant())
        {
            case "HOLD":
                if (cart.Status is not (PosCartStatus.Active or PosCartStatus.Recovered)) return "POS cart cannot be held.";
                cart.Status = PosCartStatus.Held;
                cart.HeldAtUtc = now;
                break;
            case "RESUME":
                if (cart.Status != PosCartStatus.Held) return "POS cart cannot be resumed.";
                cart.Status = PosCartStatus.Active;
                break;
            case "CANCEL":
                if (cart.Status == PosCartStatus.Completed) return "Completed POS cart cannot be cancelled.";
                cart.Status = PosCartStatus.Cancelled;
                cart.CancelledAtUtc = now;
                cart.CancellationReason = reason;
                break;
            case "EXPIRE":
                if (cart.Status == PosCartStatus.Completed) return "Completed POS cart cannot expire.";
                cart.Status = PosCartStatus.Expired;
                cart.ExpiredAtUtc = now;
                break;
            case "RECOVER":
                if (cart.Status is not (PosCartStatus.Expired or PosCartStatus.Abandoned or PosCartStatus.Cancelled)) return "POS cart cannot be recovered.";
                cart.Status = PosCartStatus.Recovered;
                cart.RecoveredAtUtc = now;
                cart.RecoveryReason = reason;
                cart.ExpiresAtUtc = now.AddHours(8);
                break;
            case "ABANDON":
                if (cart.Status == PosCartStatus.Completed) return "Completed POS cart cannot be abandoned.";
                cart.Status = PosCartStatus.Abandoned;
                cart.AbandonedAtUtc = now;
                break;
            default:
                return "Unsupported POS cart transition.";
        }

        TouchCart(cart, now);
        return null;
    }

    private async Task<PosCart?> LoadCartAsync(Guid cartId, CancellationToken cancellationToken) =>
        await _context.PosCarts
            .Include(cart => cart.Items.OrderBy(item => item.LineNumber))
            .FirstOrDefaultAsync(cart => cart.Id == cartId, cancellationToken);

    private async Task<PosCart?> LoadCartForMutationAsync(Guid cartId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        IQueryable<PosCart> query;
        if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            query = _context.PosCarts
                .FromSqlInterpolated($@"SELECT *, xmin FROM ""PosCarts"" WHERE ""Id"" = {cartId} AND ""TenantId"" = {tenantId} AND ""IsDeleted"" = FALSE FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else
        {
            query = _context.PosCarts
                .IgnoreQueryFilters()
                .Where(cart => cart.Id == cartId && cart.TenantId == tenantId && !cart.IsDeleted);
        }

        return await query
            .Include(cart => cart.Items.OrderBy(item => item.LineNumber))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Warehouse?> LoadActiveWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        await _context.Warehouses.FirstOrDefaultAsync(warehouse =>
            warehouse.Id == warehouseId &&
            warehouse.TenantId == _tenantContext.RequiredTenantId &&
            warehouse.IsActive &&
            !warehouse.IsDeleted,
            cancellationToken);

    private async Task<bool> CustomerExistsAsync(Guid customerId, CancellationToken cancellationToken) =>
        await _context.Customers.AnyAsync(customer =>
            customer.Id == customerId &&
            customer.TenantId == _tenantContext.RequiredTenantId &&
            !customer.IsDeleted,
            cancellationToken);

    private async Task<bool> CashierExistsAsync(Guid cashierId, CancellationToken cancellationToken) =>
        await _context.Users.AnyAsync(user =>
            user.Id == cashierId &&
            user.TenantId == _tenantContext.RequiredTenantId &&
            user.IsActive &&
            !user.IsDeleted,
            cancellationToken);

    private void StageOutbox(PosCart cart, string eventType, string idempotencyKey, DateTime now)
    {
        _context.PosCartOutboxMessages.Add(new PosCartOutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = cart.TenantId,
            CartId = cart.Id,
            EventType = eventType,
            CartVersion = cart.Version,
            OccurredAtUtc = now,
            IdempotencyKey = $"{eventType}:{idempotencyKey}",
            PayloadJson = JsonSerializer.Serialize(new
            {
                cartId = cart.Id,
                cart.TenantId,
                cart.CashierId,
                cart.WarehouseId,
                cart.CustomerId,
                cart.Status,
                cart.Version,
                cart.TotalAmount,
                occurredAtUtc = now
            }, JsonOptions),
            CreatedAt = now
        });
    }

    private static void TouchCart(PosCart cart, DateTime now)
    {
        cart.Version++;
        cart.UpdatedAt = now;
        cart.LastActivityAtUtc = now;
    }

    private static void RecalculateTotals(PosCart cart)
    {
        var activeItems = cart.Items.Where(item => !item.IsDeleted).ToList();
        cart.Subtotal = RoundCurrency(activeItems.Sum(item => item.Quantity * item.UnitPriceSnapshot));
        cart.DiscountAmount = RoundCurrency(activeItems.Sum(item => item.DiscountAmount));
        cart.TaxAmount = RoundCurrency(activeItems.Sum(item => item.TaxAmount));
        cart.TotalAmount = RoundCurrency(activeItems.Sum(item => item.TotalAmount));
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfNeededAsync(CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null)
        {
            return null;
        }

        return await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    private static async Task CommitIfNeededAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken)
    {
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static PosCartResponse ToResponse(PosCart cart) => new()
    {
        CartId = cart.Id,
        TenantId = cart.TenantId,
        CashierId = cart.CashierId,
        WarehouseId = cart.WarehouseId,
        CustomerId = cart.CustomerId,
        Status = cart.Status,
        Version = cart.Version,
        Subtotal = RoundCurrency(cart.Subtotal),
        DiscountAmount = RoundCurrency(cart.DiscountAmount),
        TaxAmount = RoundCurrency(cart.TaxAmount),
        TotalAmount = RoundCurrency(cart.TotalAmount),
        LastActivityAtUtc = cart.LastActivityAtUtc,
        ExpiresAtUtc = cart.ExpiresAtUtc,
        CompletedSaleId = cart.CompletedSaleId,
        Items = cart.Items
            .Where(item => !item.IsDeleted)
            .OrderBy(item => item.LineNumber)
            .Select(item => new PosCartItemResponse
            {
                ItemId = item.Id,
                ProductId = item.ProductId,
                ProductVariantId = item.ProductVariantId,
                UnitId = item.UnitId,
                Quantity = item.Quantity,
                ConversionFactor = item.ConversionFactor,
                BaseQuantity = item.BaseQuantity,
                UnitPriceSnapshot = item.UnitPriceSnapshot,
                DiscountAmount = item.DiscountAmount,
                TaxRateSnapshot = item.TaxRateSnapshot,
                TaxAmount = item.TaxAmount,
                TotalAmount = item.TotalAmount,
                CostPriceSnapshot = item.CostPriceSnapshot,
                CostAmountSnapshot = item.CostAmountSnapshot,
                ProductNameSnapshot = item.ProductNameSnapshot,
                VariantSkuSnapshot = item.VariantSkuSnapshot,
                LineNumber = item.LineNumber
            })
            .ToList()
    };

    private static string Hash(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record ItemSnapshotResult(bool Succeeded, PosCartItem? Item, string? Message)
    {
        public static ItemSnapshotResult Success(PosCartItem item) => new(true, item, null);
        public static ItemSnapshotResult Fail(string message) => new(false, null, message);
    }
}
