using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Inventory;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.API.DTOs;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Inventory;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Infrastructure.DocumentNumbers;
using PointOfSale.API.Mappers;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PurchaseReturnsController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string RequestIdHeader = "X-Request-Id";
    private const string PurchaseAttemptIdHeader = "X-Payment-Attempt-Id";
    private const int MaxPurchaseReturnPageSize = 100;
    private static readonly JsonSerializerOptions PurchaseReturnIdempotencyJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<PurchaseReturnsController> _logger;
    private readonly ILedgerService _ledgerService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IAccountingOutboxService _accountingOutbox;
    private readonly ICommerceAccountingService _commerceAccounting;

    public PurchaseReturnsController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<PurchaseReturnsController> logger,
        ILedgerService ledgerService,
        IEventPublisher eventPublisher,
        IAccountingOutboxService accountingOutbox,
        ICommerceAccountingService commerceAccounting)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
        _ledgerService = ledgerService;
        _eventPublisher = eventPublisher;
        _accountingOutbox = accountingOutbox;
        _commerceAccounting = commerceAccounting;
    }

    /// <summary>
    /// Get all purchase returns with pagination and filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ReturnResponseDto>>> GetPurchaseReturns(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? supplierId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPurchaseReturnPageSize);

        var query = _context.Returns
            .AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.Warehouse)
            .Include(r => r.Items)
                .ThenInclude(i => i.Product)
            .Include(r => r.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v!.Attributes)
            .Include(r => r.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v!.Stocks)
            .Include(r => r.Items)
                .ThenInclude(i => i.Unit)
            .AsSplitQuery()
            .Where(r => r.ReturnType == ReturnType.PurchaseReturn)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.ToLower();
            query = query.Where(r =>
                r.ReturnNumber.ToLower().Contains(search) ||
                (r.Supplier != null && r.Supplier.Name.ToLower().Contains(search)) ||
                (r.OriginalInvoiceNumber != null && r.OriginalInvoiceNumber.ToLower().Contains(search)));
        }

        if (supplierId.HasValue)
            query = query.Where(r => r.SupplierId == supplierId.Value);

        if (startDate.HasValue)
            query = query.Where(r => r.ReturnDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(r => r.ReturnDate <= endDate.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.ReturnDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<ReturnResponseDto>
        {
            Items = items.Select(item => item.ToResponseDto()).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>
    /// Get purchase return by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ReturnResponseDto>> GetPurchaseReturn(Guid id, CancellationToken cancellationToken)
    {
        var purchaseReturn = await _context.Returns
            .AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.Warehouse)
            .Include(r => r.Items)
                .ThenInclude(i => i.Product)
            .Include(r => r.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v!.Attributes)
            .Include(r => r.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v!.Stocks)
            .Include(r => r.Items)
                .ThenInclude(i => i.Unit)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id && r.ReturnType == ReturnType.PurchaseReturn, cancellationToken);

        if (purchaseReturn == null)
            return NotFound();

        return Ok(purchaseReturn.ToResponseDto());
    }

    /// <summary>
    /// Get purchase invoice details for creating a return
    /// </summary>
    [HttpGet("invoice/{invoiceId}")]
    public async Task<ActionResult<PurchaseInvoiceResponseDto>> GetPurchaseInvoiceForReturn(Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await _context.PurchaseInvoices
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Warehouse)
            .Include(p => p.Items)
                .ThenInclude(i => i.Product)
            .Include(p => p.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v!.Attributes)
            .Include(p => p.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v!.Stocks)
            .Include(p => p.Items)
                .ThenInclude(i => i.Unit)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == invoiceId, cancellationToken);

        if (invoice == null)
            return NotFound();

        return Ok(invoice.ToResponseDto());
    }

    /// <summary>
    /// Create a new purchase return
    /// 
    /// VARIANT-CENTRIC IMPLEMENTATION:
    /// - Stock deducted from ProductVariantStocks (source of truth)
    /// - VariantId is MANDATORY (resolved from original invoice or default variant)
    /// - Available stock validated: (Quantity - ReservedQuantity) >= returnQuantity
    /// - ProductVariantStockChangedEvent published for MongoDB sync
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<PurchaseReturnResponse>> CreatePurchaseReturn(CreatePurchaseReturnRequest request)
    {
        var resolution = ResolvePurchaseReturnIdempotencyContext();
        if (resolution.ErrorResult != null)
        {
            return resolution.ErrorResult;
        }

        var fingerprint = BuildPurchaseReturnRequestFingerprint(request);
        var scopedIdempotencyKey = BuildSupplierMutationIdempotencyKey(resolution.IdempotencyKey!);
        var existingRecord = await FindPurchaseReturnIdempotencyAsync(scopedIdempotencyKey);
        if (existingRecord != null)
        {
            if (!string.Equals(existingRecord.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return Conflict(new
                {
                    message = "Idempotency-Key was already used for a different purchase return request."
                });
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                return BuildStoredPurchaseReturnResponse(existingRecord);
            }

            return Conflict(new
            {
                message = "A purchase return request with the same idempotency key is already being processed."
            });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var transactionCommitted = false;
        var claim = await TryClaimPurchaseReturnIdempotencyAsync(
            scopedIdempotencyKey,
            resolution.PurchaseAttemptId,
            fingerprint);

        if (claim.ShortCircuitResult != null)
        {
            await transaction.RollbackAsync();
            return claim.ShortCircuitResult;
        }

        var idempotencyRecord = claim.Record!;

        // Track stock changes for event publishing after commit
        var stockChanges = new List<PendingProductVariantStockChange>();

        try
        {
            if (!request.OriginalInvoiceId.HasValue)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Original purchase invoice is required." });
            }

            var originalInvoice = await LoadOriginalPurchaseInvoiceForReturnMutationAsync(request.OriginalInvoiceId.Value);
            if (originalInvoice == null)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Original purchase invoice not found" });
            }

            if (originalInvoice.Status is InvoiceStatus.Cancelled or InvoiceStatus.FullyReturned)
            {
                await transaction.RollbackAsync();
                return Conflict(new { message = "This purchase invoice cannot accept another return." });
            }

            if (request.SupplierId != originalInvoice.SupplierId ||
                request.WarehouseId != originalInvoice.WarehouseId)
            {
                await transaction.RollbackAsync();
                return BadRequest(new
                {
                    message = "Supplier and warehouse must match the original purchase invoice."
                });
            }

            var supplier = await _context.Suppliers.FindAsync(originalInvoice.SupplierId);
            if (supplier == null)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Original purchase supplier is no longer authoritative." });
            }

            var warehouse = await _context.Warehouses.FindAsync(originalInvoice.WarehouseId);
            if (warehouse == null || !warehouse.IsActive)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Original purchase warehouse is not active." });
            }

            var returnTimestamp = DateTime.UtcNow;
            var returnNumber = await DocumentNumberingSupport.AllocateReturnNumberAsync(
                _context,
                ReturnType.PurchaseReturn,
                returnTimestamp);

            // Create purchase return
            var purchaseReturn = new Return
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,
                ReturnNumber = returnNumber,
                ReturnDate = request.ReturnDate.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(request.ReturnDate, DateTimeKind.Utc)
                    : request.ReturnDate.ToUniversalTime(),
                ReturnType = ReturnType.PurchaseReturn,
                OriginalPurchaseInvoiceId = originalInvoice.Id,
                OriginalInvoiceNumber = originalInvoice.InvoiceNumber,
                SupplierId = originalInvoice.SupplierId,
                WarehouseId = originalInvoice.WarehouseId,
                Reason = request.Reason,
                Notes = request.Notes,
                CreatedAt = returnTimestamp
            };

            var pendingLines = new List<PendingPurchaseReturnLine>();
            var requestedAuthorityKeys = new HashSet<(Guid ProductId, Guid VariantId)>();

            // Resolve and validate return items before taking stock locks.
            foreach (var itemRequest in request.Items)
            {
                var product = await _context.Products.FindAsync(itemRequest.ProductId);
                if (product == null)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = $"Product not found: {itemRequest.ProductId}" });
                }

                var candidateOriginalItems = originalInvoice.Items
                    .Where(item => item.ProductId == itemRequest.ProductId)
                    .ToList();
                if (candidateOriginalItems.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = $"Product {product.Name} was not received on the original invoice." });
                }

                Guid effectiveVariantId;
                if (itemRequest.ProductVariantId.HasValue)
                {
                    effectiveVariantId = itemRequest.ProductVariantId.Value;
                }
                else
                {
                    var distinctVariantIds = candidateOriginalItems
                        .Where(item => item.ProductVariantId.HasValue)
                        .Select(item => item.ProductVariantId!.Value)
                        .Distinct()
                        .ToList();
                    if (distinctVariantIds.Count != 1)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = $"VariantId is required for product {product.Name}." });
                    }

                    effectiveVariantId = distinctVariantIds[0];
                }

                var originalItems = candidateOriginalItems
                    .Where(item => item.ProductVariantId == effectiveVariantId)
                    .OrderBy(item => item.CreatedAt)
                    .ThenBy(item => item.Id)
                    .ToList();
                if (originalItems.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = "Product variant was not received on the original invoice." });
                }

                if (!requestedAuthorityKeys.Add((itemRequest.ProductId, effectiveVariantId)))
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = "Duplicate product variant lines are not allowed in one purchase return." });
                }

                var unitAuthorityItem = itemRequest.UnitId.HasValue
                    ? originalItems.FirstOrDefault(item => item.UnitId == itemRequest.UnitId)
                    : originalItems[0];
                if (unitAuthorityItem == null)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = "Return unit must match a unit received on the original invoice." });
                }

                var unitId = unitAuthorityItem.UnitId;
                var conversionFactor = unitAuthorityItem.ConversionFactor > 0m
                    ? unitAuthorityItem.ConversionFactor
                    : 1m;
                var baseQuantity = itemRequest.Quantity * conversionFactor;

                var totalReturnedBaseQuantity = await _context.ReturnItems
                    .Where(returnItem =>
                        returnItem.Return.OriginalPurchaseInvoiceId == originalInvoice.Id &&
                        returnItem.ProductId == itemRequest.ProductId &&
                        returnItem.ProductVariantId == effectiveVariantId)
                    .SumAsync(returnItem => (decimal?)returnItem.BaseQuantity) ?? 0m;

                var originalReceivedBaseQuantity = originalItems.Sum(ResolveOriginalInvoiceBaseQuantity);
                if (totalReturnedBaseQuantity + baseQuantity > originalReceivedBaseQuantity)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new
                    {
                        message = $"Cannot return more than received quantity for {product.Name}"
                    });
                }

                var originalSubtotal = originalItems.Sum(item => item.TotalAmount - item.TaxAmount);
                var originalTax = originalItems.Sum(item => item.TaxAmount);
                var subtotalPerBaseUnit = originalSubtotal / originalReceivedBaseQuantity;
                var taxPerBaseUnit = originalTax / originalReceivedBaseQuantity;
                var authoritativeSubtotal = RoundCurrency(baseQuantity * subtotalPerBaseUnit);
                var authoritativeTax = RoundCurrency(baseQuantity * taxPerBaseUnit);
                var authoritativeUnitPrice = RoundCurrency(authoritativeSubtotal / itemRequest.Quantity);
                var authoritativeTaxRate = authoritativeSubtotal > 0m
                    ? RoundRate(authoritativeTax * 100m / authoritativeSubtotal)
                    : 0m;

                pendingLines.Add(new PendingPurchaseReturnLine(
                    itemRequest.ProductId,
                    product.Name,
                    effectiveVariantId,
                    unitId,
                    itemRequest.Quantity,
                    conversionFactor,
                    baseQuantity,
                    authoritativeUnitPrice,
                    authoritativeTaxRate,
                    authoritativeSubtotal,
                    authoritativeTax,
                    subtotalPerBaseUnit,
                    itemRequest.Reason));
            }

            var lockTargets = pendingLines
                .Select(line => new StockLockTarget(
                    request.WarehouseId,
                    line.EffectiveVariantId,
                    CreateIfMissing: true))
                .DistinctBy(target => (target.WarehouseId, target.VariantId))
                .ToList();

            await LockPurchaseCostProductsAsync(pendingLines.Select(line => line.ProductId));

            var lockedStocks = await ProductVariantStockMutationSupport.LockMultipleStocksAsync(
                _context,
                _tenantContext.RequiredTenantId,
                lockTargets);

            var stockMap = lockedStocks.ToDictionary(
                stock => (stock.Value.WarehouseId, stock.Value.VariantId),
                stock => stock.Value);

            var variantIds = pendingLines
                .Select(line => line.EffectiveVariantId)
                .Distinct()
                .ToList();

            var variantsById = await _context.ProductVariants
                .Where(variant => variantIds.Contains(variant.Id))
                .ToDictionaryAsync(variant => variant.Id);
            var productIds = pendingLines
                .Select(line => line.ProductId)
                .Distinct()
                .ToList();
            var productsById = await _context.Products
                .Where(product => productIds.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id);

            foreach (var product in productsById.Values.OrderBy(product => product.Id))
            {
                await _context.Entry(product).ReloadAsync();
            }

            foreach (var variant in variantsById.Values.OrderBy(variant => variant.Id))
            {
                await _context.Entry(variant).ReloadAsync();
            }

            decimal subtotal = 0;
            decimal totalTax = 0;

            foreach (var line in pendingLines)
            {
                var variantStock = stockMap[(request.WarehouseId, line.EffectiveVariantId)];

                if (variantStock.AvailableQuantity < line.BaseQuantity)
                {
                    variantsById.TryGetValue(line.EffectiveVariantId, out var variant);
                    await transaction.RollbackAsync();
                    return BadRequest(new
                    {
                        message =
                            $"Insufficient stock for {line.ProductName} ({variant?.SKU ?? "default"}). " +
                            $"Available: {variantStock.AvailableQuantity} (base units), Requested: {line.BaseQuantity} (base units)"
                    });
                }

                var itemSubtotal = line.SubtotalAmount;
                var itemTax = line.TaxAmount;
                var itemTotal = RoundCurrency(itemSubtotal + itemTax);

                var returnItem = new ReturnItem
                {
                    Id = Guid.NewGuid(),
                    ReturnId = purchaseReturn.Id,
                    ProductId = line.ProductId,
                    ProductVariantId = line.EffectiveVariantId,
                    UnitId = line.UnitId,
                    Quantity = line.Quantity,
                    ConversionFactor = line.ConversionFactor,
                    BaseQuantity = line.BaseQuantity,
                    UnitPrice = line.UnitPrice,
                    TaxRate = line.TaxRate,
                    TaxAmount = itemTax,
                    TotalAmount = itemTotal,
                    CostPriceSnapshot = line.BaseCost,
                    CostAmountSnapshot = RoundCurrency(line.BaseQuantity * line.BaseCost),
                    Reason = line.Reason,
                    CreatedAt = returnTimestamp
                };

                purchaseReturn.Items.Add(returnItem);
                subtotal += itemSubtotal;
                totalTax += itemTax;

                var oldQuantity = variantStock.Quantity;

                if (!variantsById.TryGetValue(line.EffectiveVariantId, out var variantEntity))
                {
                    throw new InvalidOperationException($"Purchase return variant {line.EffectiveVariantId} is no longer authoritative.");
                }

                variantEntity.CostPrice = VariantCostAuthority.ApplyPurchaseReturn(
                    variantStock.Quantity,
                    variantEntity.CostPrice,
                    line.BaseQuantity,
                    line.BaseCost);
                if (productsById.TryGetValue(line.ProductId, out var productEntity) && !productEntity.HasVariants)
                {
                    productEntity.PurchasePrice = variantEntity.CostPrice;
                }

                variantStock.Quantity -= line.BaseQuantity;
                variantStock.UpdatedAt = returnTimestamp;

                stockChanges.Add(ProductVariantStockMutationSupport.BuildPendingStockChange(
                    line.EffectiveVariantId,
                    line.ProductId,
                    request.WarehouseId,
                    oldQuantity,
                    variantStock.Quantity,
                    "PurchaseReturn",
                    referenceId: purchaseReturn.Id,
                    referenceType: "PurchaseReturn",
                    sku: variantEntity?.SKU));

                var movement = new StockMovement
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.RequiredTenantId,
                    WarehouseId = request.WarehouseId,
                    ProductId = line.ProductId,
                    ProductVariantId = line.EffectiveVariantId,
                    MovementType = StockMovementType.PurchaseReturn,
                    UnitId = line.UnitId,
                    ConversionFactor = line.ConversionFactor,
                    QuantityChange = -line.BaseQuantity,
                    QuantityAfter = variantStock.Quantity,
                    ReferenceId = purchaseReturn.Id,
                    ReferenceNumber = returnNumber,
                    Price = line.ConversionFactor > 0 ? line.UnitPrice / line.ConversionFactor : line.UnitPrice,
                    Notes = $"Purchase Return: {request.Reason}",
                    CreatedAt = returnTimestamp
                };
                _context.StockMovements.Add(movement);
            }

            purchaseReturn.Subtotal = RoundCurrency(subtotal);
            purchaseReturn.TaxAmount = RoundCurrency(totalTax);
            purchaseReturn.TotalAmount = RoundCurrency(purchaseReturn.Subtotal + purchaseReturn.TaxAmount);

            originalInvoice.AccountingMutationVersion++;
            var purchaseReturnMutationVersion = originalInvoice.AccountingMutationVersion;

            var totalReceivedBaseQuantity = originalInvoice.Items.Sum(ResolveOriginalInvoiceBaseQuantity);
            var totalReturnedAfterThisRequest = await _context.ReturnItems
                .Where(returnItem => returnItem.Return.OriginalPurchaseInvoiceId == originalInvoice.Id)
                .SumAsync(returnItem => (decimal?)returnItem.BaseQuantity) ?? 0m;
            totalReturnedAfterThisRequest += pendingLines.Sum(line => line.BaseQuantity);
            originalInvoice.Status = totalReturnedAfterThisRequest >= totalReceivedBaseQuantity
                ? InvoiceStatus.FullyReturned
                : InvoiceStatus.PartiallyReturned;

            _context.Returns.Add(purchaseReturn);

            // Create ledger entry - Debit supplier (reduce what we owe them)
            var balanceAfter = await _ledgerService.ApplySupplierTransaction(
                supplier.Id,
                returnTimestamp,
                TransactionType.PurchaseReturn,
                returnNumber,
                purchaseReturn.TotalAmount,
                0,
                purchaseReturn.Id,
                $"Purchase Return {returnNumber}: {request.Reason}");

            var supplierTransaction = new SupplierTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,
                SupplierId = supplier.Id,
                TransactionDate = returnTimestamp,
                TransactionType = TransactionType.PurchaseReturn,
                DebitAmount = purchaseReturn.TotalAmount,
                CreditAmount = 0,
                BalanceAfter = balanceAfter,
                ReferenceId = purchaseReturn.Id,
                ReferenceNumber = returnNumber,
                Notes = $"Purchase Return: {request.Reason}",
                CreatedAt = returnTimestamp
            };
            _context.SupplierTransactions.Add(supplierTransaction);

            // ════════════════════════════════════════════════════════════════════════════
            // PHASE LCR: Enqueue to accounting outbox for ledger posting
            // ════════════════════════════════════════════════════════════════════════════
            await _accountingOutbox.EnqueuePurchaseReturnAsync(
                purchaseReturn.TenantId,
                purchaseReturn.Id,
                originalInvoice.Id,
                purchaseReturnMutationVersion);
            
            // DO NOT mutate balances directly - purchase return posting must stay on the outbox/ledger path.

            await DocumentNumberingSupport.SaveReturnChangesWithRetryAsync(
                _context,
                purchaseReturn,
                returnTimestamp,
                _logger);
            returnNumber = purchaseReturn.ReturnNumber;

            // PUBLISH EVENTS AFTER COMMIT (ensures data consistency)
            var response = new PurchaseReturnResponse
            {
                ReturnId = purchaseReturn.Id,
                ReturnNumber = returnNumber,
                TotalAmount = purchaseReturn.TotalAmount,
                RefundAmount = purchaseReturn.TotalAmount,
                Message = "Purchase return created successfully"
            };

            idempotencyRecord.ReturnId = purchaseReturn.Id;
            idempotencyRecord.ResponseStatusCode = StatusCodes.Status200OK;
            idempotencyRecord.ResponseJson = SerializePurchaseReturnPayload(response);
            idempotencyRecord.CompletedAtUtc = DateTime.UtcNow;
            idempotencyRecord.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            if (stockChanges.Count > 0)
            {
                await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                    _context,
                    _tenantContext.RequiredTenantId,
                    stockChanges);
            }

            await transaction.CommitAsync();
            transactionCommitted = true;

            // Post to external accounting system AFTER local state is durable
            var purchaseReturnPosting = await _commerceAccounting.PostPurchaseReturnAsync(
                purchaseReturn,
                HttpContext.RequestAborted);
            if (!purchaseReturnPosting.Success)
            {
                _logger.LogError(
                    "Purchase return accounting post failed for return {ReturnNumber} after commit: {ErrorMessage}. Manual reconciliation required.",
                    returnNumber,
                    purchaseReturnPosting.ErrorMessage);
                // Note: Return is already committed, so we log the error but don't fail the request
                // The outbox worker will retry the accounting post
            }
            else
            {
                // Mark outbox as processed and persist - don't fail the request if this fails
                try
                {
                    await MarkSynchronousPurchaseReturnAccountingOutboxProcessedAsync(
                        purchaseReturn.TenantId,
                        purchaseReturn.Id,
                        purchaseReturnMutationVersion,
                        purchaseReturnPosting.JournalId,
                        HttpContext.RequestAborted);
                    await _context.SaveChangesAsync(HttpContext.RequestAborted);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to mark purchase return accounting outbox as processed for return {ReturnNumber}. Outbox worker may retry.",
                        returnNumber);
                    // Don't rethrow - the purchase return is already committed successfully
                }
            }

            _logger.LogInformation("Purchase return {ReturnNumber} created successfully with {ItemCount} items. Stock deducted from ProductVariantStocks.",
                returnNumber, purchaseReturn.Items.Count);

            return Ok(response);
        }
        catch (Exception ex)
        {
            if (!transactionCommitted)
            {
                await transaction.RollbackAsync();
                _context.ChangeTracker.Clear();
            }

            _logger.LogError(ex, "Error creating purchase return");
            return StatusCode(500, new { message = "An unexpected error occurred while processing the purchase return." });
        }
    }

    private async Task<(PurchaseReturnRequestIdempotency? Record, ActionResult<PurchaseReturnResponse>? ShortCircuitResult)> TryClaimPurchaseReturnIdempotencyAsync(
        string idempotencyKey,
        string? purchaseAttemptId,
        string fingerprint)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claimId = Guid.NewGuid();
            var nowUtc = DateTime.UtcNow;

            var rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""PurchaseReturnRequestIdempotency""
                (""Id"", ""TenantId"", ""Key"", ""PurchaseAttemptId"", ""RequestFingerprint"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES
                ({claimId}, {tenantId}, {idempotencyKey}, {purchaseAttemptId}, {fingerprint}, {nowUtc}, {nowUtc}, {false})
                ON CONFLICT (""TenantId"", ""Key"") DO NOTHING");

            if (rowsInserted > 0)
            {
                var claimedRecord = await _context.PurchaseReturnRequestIdempotencyRecords
                    .FirstAsync(record => record.TenantId == tenantId && record.Key == idempotencyKey);

                return (claimedRecord, null);
            }

            var existingRecord = await _context.PurchaseReturnRequestIdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(record => record.TenantId == tenantId && record.Key == idempotencyKey);

            if (existingRecord == null)
            {
                continue;
            }

            if (!string.Equals(existingRecord.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return (null, Conflict(new
                {
                    message = "Idempotency-Key was already used for a different purchase return request."
                }));
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                return (null, BuildStoredPurchaseReturnResponse(existingRecord));
            }

            return (null, Conflict(new
            {
                message = "A purchase return request with the same idempotency key is already being processed."
            }));
        }

        return (null, Conflict(new
        {
            message = "A purchase return request with the same idempotency key is already being processed."
        }));
    }

    private async Task<PurchaseReturnRequestIdempotency?> FindPurchaseReturnIdempotencyAsync(string idempotencyKey)
    {
        return await _context.PurchaseReturnRequestIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.TenantId == _tenantContext.RequiredTenantId &&
                item.Key == idempotencyKey);
    }

    private (string? IdempotencyKey, string? PurchaseAttemptId, ActionResult<PurchaseReturnResponse>? ErrorResult) ResolvePurchaseReturnIdempotencyContext()
    {
        var idempotencyKey = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();
        var requestId = Request.Headers[RequestIdHeader].FirstOrDefault()?.Trim();
        var purchaseAttemptId = Request.Headers[PurchaseAttemptIdHeader].FirstOrDefault()?.Trim();

        if (!string.IsNullOrWhiteSpace(idempotencyKey) &&
            !string.IsNullOrWhiteSpace(requestId) &&
            !string.Equals(idempotencyKey, requestId, StringComparison.Ordinal))
        {
            return (null, null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} and {RequestIdHeader} must match when both are provided."
            }));
        }

        var normalizedIdempotencyKey = !string.IsNullOrWhiteSpace(idempotencyKey)
            ? idempotencyKey
            : requestId;

        if (string.IsNullOrWhiteSpace(normalizedIdempotencyKey))
        {
            return (null, null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} header is required for purchase return requests."
            }));
        }

        if (normalizedIdempotencyKey.Length > 200)
        {
            return (null, null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} must not exceed 200 characters."
            }));
        }

        if (!string.IsNullOrWhiteSpace(purchaseAttemptId) && purchaseAttemptId.Length > 200)
        {
            return (null, null, BadRequest(new
            {
                message = $"{PurchaseAttemptIdHeader} must not exceed 200 characters."
            }));
        }

        return (normalizedIdempotencyKey, purchaseAttemptId, null);
    }

    private string BuildSupplierMutationIdempotencyKey(string operationId) =>
        $"SUPPLIER:{_tenantContext.RequiredTenantId}:{operationId}";

    private static string BuildPurchaseReturnRequestFingerprint(CreatePurchaseReturnRequest request)
    {
        return DeterministicRequestHasher.ComputeHash(new
        {
            request.SupplierId,
            request.WarehouseId,
            request.ReturnDate,
            request.OriginalInvoiceId,
            Items = request.Items
                .OrderBy(item => item.ProductId)
                .ThenBy(item => item.ProductVariantId)
                .ThenBy(item => item.UnitId)
                .Select(item => new
                {
                    item.ProductId,
                    item.ProductVariantId,
                    item.UnitId,
                    item.Quantity,
                    Reason = item.Reason?.Trim()
                })
                .ToArray(),
            Reason = request.Reason?.Trim(),
            Notes = request.Notes?.Trim()
        });
    }

    private static ActionResult<PurchaseReturnResponse> BuildStoredPurchaseReturnResponse(PurchaseReturnRequestIdempotency record)
    {
        var statusCode = record.ResponseStatusCode ?? StatusCodes.Status200OK;

        if (string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            return new StatusCodeResult(statusCode);
        }

        using var document = JsonDocument.Parse(record.ResponseJson);
        var payload = document.RootElement.Clone();
        return new ObjectResult(payload)
        {
            StatusCode = statusCode,
        };
    }

    private static string SerializePurchaseReturnPayload(PurchaseReturnResponse response) =>
        JsonSerializer.Serialize(response, PurchaseReturnIdempotencyJsonOptions);

    private async Task MarkSynchronousPurchaseReturnAccountingOutboxProcessedAsync(
        Guid tenantId,
        Guid returnId,
        int? mutationVersion,
        Guid? journalId,
        CancellationToken cancellationToken)
    {
        if (!journalId.HasValue || journalId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Synchronous purchase return accounting post for return {returnId} did not return a journal id.");
        }

        var message = await _context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate =>
                    candidate.TenantId == tenantId &&
                    candidate.BusinessEntityId == returnId &&
                    candidate.EventType == "PurchaseReturn" &&
                    candidate.IdempotencyKey == BuildPurchaseReturnAccountingOutboxIdempotencyKey(tenantId, returnId) &&
                    candidate.AggregateVersion == mutationVersion,
                cancellationToken);

        if (message == null)
        {
            throw new InvalidOperationException(
                $"Synchronous purchase return accounting post for return {returnId} has no matching outbox row.");
        }

        if (message.ProcessedAtUtc.HasValue)
        {
            if (message.JournalId != journalId.Value)
            {
                throw new InvalidOperationException(
                    $"Synchronous purchase return accounting outbox row for return {returnId} was already processed with a different journal.");
            }

            return;
        }

        if (message.IsFailed ||
            (message.ClaimedUntilUtc.HasValue && message.ClaimedUntilUtc.Value > DateTime.UtcNow))
        {
            throw new InvalidOperationException(
                $"Synchronous purchase return accounting outbox row for return {returnId} is failed or claimed and cannot be completed by the request executor.");
        }

        message.MarkProcessed(journalId.Value);
    }

    private async Task<PurchaseInvoice?> LoadOriginalPurchaseInvoiceForReturnMutationAsync(Guid originalInvoiceId)
    {
        IQueryable<PurchaseInvoice> query;

        if (IsPostgresProvider())
        {
            query = _context.PurchaseInvoices
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""PurchaseInvoices""
                    WHERE ""TenantId"" = {_tenantContext.RequiredTenantId}
                      AND ""Id"" = {originalInvoiceId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else if (IsSqlServerProvider())
        {
            query = _context.PurchaseInvoices
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [PurchaseInvoices] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {_tenantContext.RequiredTenantId}
                      AND [Id] = {originalInvoiceId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters();
        }
        else
        {
            query = _context.PurchaseInvoices
                .IgnoreQueryFilters()
                .Where(invoice =>
                    invoice.TenantId == _tenantContext.RequiredTenantId &&
                    invoice.Id == originalInvoiceId &&
                    !invoice.IsDeleted);
        }

        var originalInvoice = await query.FirstOrDefaultAsync();
        if (originalInvoice == null)
        {
            return null;
        }

        await _context.Entry(originalInvoice)
            .Collection(invoice => invoice.Items)
            .Query()
            .Include(item => item.ProductVariant)
            .LoadAsync();

        return originalInvoice;
    }

    private static decimal ResolveOriginalInvoiceBaseQuantity(PurchaseInvoiceItem originalItem)
    {
        if (originalItem.BaseQuantity > 0)
        {
            return originalItem.BaseQuantity;
        }

        var conversionFactor = originalItem.ConversionFactor > 0
            ? originalItem.ConversionFactor
            : 1m;

        return originalItem.Quantity * conversionFactor;
    }

    private async Task LockPurchaseCostProductsAsync(IEnumerable<Guid> productIds)
    {
        foreach (var productId in productIds.Distinct().OrderBy(id => id))
        {
            IQueryable<Product> query;

            if (IsPostgresProvider())
            {
                query = _context.Products
                    .FromSqlInterpolated($@"
                        SELECT *
                        FROM ""Products""
                        WHERE ""TenantId"" = {_tenantContext.RequiredTenantId}
                          AND ""Id"" = {productId}
                          AND ""IsDeleted"" = FALSE
                        FOR UPDATE")
                    .IgnoreQueryFilters();
            }
            else if (IsSqlServerProvider())
            {
                query = _context.Products
                    .FromSqlInterpolated($@"
                        SELECT *
                        FROM [Products] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                        WHERE [TenantId] = {_tenantContext.RequiredTenantId}
                          AND [Id] = {productId}
                          AND [IsDeleted] = 0")
                    .IgnoreQueryFilters();
            }
            else
            {
                query = _context.Products
                    .IgnoreQueryFilters()
                    .Where(product =>
                        product.TenantId == _tenantContext.RequiredTenantId &&
                        product.Id == productId &&
                        !product.IsDeleted);
            }

            if (await query.FirstOrDefaultAsync() == null)
            {
                throw new InvalidOperationException($"Purchase return cost product {productId} is no longer authoritative.");
            }
        }
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundRate(decimal value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero);

    private static string BuildPurchaseReturnAccountingOutboxIdempotencyKey(Guid tenantId, Guid returnId) =>
        $"PURCHASE_RETURN:{tenantId}:{returnId}";

    private bool IsPostgresProvider() =>
        _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

}

file sealed record PendingPurchaseReturnLine(
    Guid ProductId,
    string ProductName,
    Guid EffectiveVariantId,
    Guid? UnitId,
    decimal Quantity,
    decimal ConversionFactor,
    decimal BaseQuantity,
    decimal UnitPrice,
    decimal TaxRate,
    decimal SubtotalAmount,
    decimal TaxAmount,
    decimal BaseCost,
    string? Reason);

// DTOs
public record CreatePurchaseReturnRequest
{
    public Guid SupplierId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateTime ReturnDate { get; set; }
    public Guid? OriginalInvoiceId { get; set; }
    [Required]
    [MinLength(1)]
    public List<PurchaseReturnItemRequest> Items { get; set; } = new();
    [MaxLength(500)]
    public string? Reason { get; set; }
    [MaxLength(1000)]
    public string? Notes { get; set; }
    public bool RefundToCash { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal RefundAmount { get; set; }
}

public record PurchaseReturnItemRequest
{
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public Guid? UnitId { get; set; } // Unit for the return
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal Quantity { get; set; } // Quantity in the selected unit
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal UnitPrice { get; set; }
    [Range(typeof(decimal), "0", "100")]
    public decimal TaxRate { get; set; }
    [MaxLength(500)]
    public string? Reason { get; set; }
}

public record PurchaseReturnResponse
{
    public Guid ReturnId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public string Message { get; set; } = string.Empty;
}
