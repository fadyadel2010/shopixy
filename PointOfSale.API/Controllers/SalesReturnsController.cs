using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Refunds;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Inventory;
using PointOfSale.API.Infrastructure.DocumentNumbers;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Mappers;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SalesReturnsController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string RequestIdHeader = "X-Request-Id";
    private const string PaymentAttemptIdHeader = "X-Payment-Attempt-Id";
    private const string SalesReturnCreateOperation = "SALES_RETURN_CREATE";
    
    /// <summary>
    /// Maximum number of items allowed in a single sales return request.
    /// This limit prevents unbounded work and ensures reasonable request sizes.
    /// </summary>
    public const int MAX_SALES_RETURN_ITEMS = 50;
    private static readonly JsonSerializerOptions SalesReturnIdempotencyJsonOptions = new(JsonSerializerDefaults.Web);
    
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<SalesReturnsController> _logger;
    private readonly ILedgerService _ledgerService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IAccountingOutboxService _accountingOutbox;
    private readonly ICommerceAccountingService _commerceAccounting;

    public SalesReturnsController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<SalesReturnsController> logger,
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
    /// Get all sales returns with pagination and filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ReturnResponseDto>>> GetSalesReturns(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? customerId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        const int MaxPageSize = PaginationGuard.MaxPageSize;

        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize, MaxPageSize);

        var query = _context.Returns
            .AsNoTracking()
            .Include(r => r.Customer)
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
            .Where(r => r.ReturnType == ReturnType.SalesReturn)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.ToLower();
            query = query.Where(r =>
                r.ReturnNumber.ToLower().Contains(search) ||
                (r.Customer != null && r.Customer.Name.ToLower().Contains(search)) ||
                (r.OriginalInvoiceNumber != null && r.OriginalInvoiceNumber.ToLower().Contains(search)));
        }

        if (customerId.HasValue)
            query = query.Where(r => r.CustomerId == customerId.Value);

        if (startDate.HasValue)
            query = query.Where(r => r.ReturnDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(r => r.ReturnDate <= endDate.Value);

        var totalCount = await query.CountAsync();
        var skip = PaginationGuard.GetSkip(page, pageSize);

        var items = await query
            .OrderByDescending(r => r.ReturnDate)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync();

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
    /// Get sales return by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ReturnResponseDto>> GetSalesReturn(Guid id)
    {
        var salesReturn = await _context.Returns
            .AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.Warehouse)
            .Include(r => r.Items)
                .ThenInclude(i => i.Product)
            .Include(r => r.Items)
                .ThenInclude(i => i.ProductVariant)
            .Include(r => r.Items)
                .ThenInclude(i => i.Unit)
            .FirstOrDefaultAsync(r => r.Id == id && r.ReturnType == ReturnType.SalesReturn);

        if (salesReturn == null)
            return NotFound();

        return Ok(salesReturn.ToResponseDto());
    }

    [HttpGet("eligible-invoices")]
    public async Task<ActionResult<IReadOnlyList<SalesReturnEligibleInvoiceResponseDto>>> GetEligibleSalesInvoices(
        [FromQuery] Guid? customerId = null,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, PaginationGuard.MaxPageSize);

        var query = _context.SalesInvoices
            .AsNoTracking()
            .Where(invoice =>
                (invoice.Status == InvoiceStatus.Completed || invoice.Status == InvoiceStatus.PartiallyReturned) &&
                invoice.Warehouse.IsActive &&
                invoice.Items.Any(item => !item.IsDeleted));

        if (customerId.HasValue)
        {
            query = query.Where(invoice => invoice.CustomerId == customerId.Value);
        }

        var candidates = await query
            .Include(invoice => invoice.Customer)
            .Include(invoice => invoice.Warehouse)
            .Include(invoice => invoice.Items)
                .ThenInclude(item => item.Product)
            .Include(invoice => invoice.Items)
                .ThenInclude(item => item.ProductVariant)
                    .ThenInclude(variant => variant!.Attributes)
            .Include(invoice => invoice.Items)
                .ThenInclude(item => item.Unit)
            .AsSplitQuery()
            .OrderByDescending(invoice => invoice.InvoiceDate)
            .ThenByDescending(invoice => invoice.CreatedAt)
            .ToListAsync(cancellationToken);

        var eligibleInvoices = new List<SalesReturnEligibleInvoiceResponseDto>(pageSize);
        foreach (var invoice in candidates)
        {
            var readModel = await BuildSalesReturnInvoiceReadModelAsync(invoice, cancellationToken);
            if (!readModel.Success || readModel.Response!.Items.Count == 0)
            {
                continue;
            }

            eligibleInvoices.Add(new SalesReturnEligibleInvoiceResponseDto
            {
                Id = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                InvoiceDate = invoice.InvoiceDate,
                TotalAmount = invoice.TotalAmount,
                Status = invoice.Status,
                Customer = ToCustomerReference(invoice.Customer),
                Warehouse = ToWarehouseReference(invoice.Warehouse)
            });

            if (eligibleInvoices.Count == pageSize)
            {
                break;
            }
        }

        return Ok(eligibleInvoices);
    }

    /// <summary>
    /// Get authoritative remaining sales invoice quantities for creating a return.
    /// </summary>
    [HttpGet("invoice/{invoiceId}")]
    public async Task<ActionResult<SalesReturnInvoiceResponseDto>> GetSalesInvoiceForReturn(
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        var invoice = await _context.SalesInvoices
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Warehouse)
            .Include(s => s.SalesRepresentative)
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
            .Include(s => s.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v!.Attributes)
            .Include(s => s.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v!.Stocks)
            .Include(s => s.Items)
                .ThenInclude(i => i.Unit)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == invoiceId, cancellationToken);

        if (invoice == null)
            return NotFound();

        if (invoice.Status is not (InvoiceStatus.Completed or InvoiceStatus.PartiallyReturned))
        {
            return Conflict(new { message = "This sales invoice cannot accept another return." });
        }

        if (!invoice.Warehouse.IsActive)
        {
            return Conflict(new { message = "The original sales warehouse is not active." });
        }

        var readModel = await BuildSalesReturnInvoiceReadModelAsync(invoice, cancellationToken);
        if (!readModel.Success)
        {
            return Conflict(new { message = readModel.ErrorMessage });
        }

        if (readModel.Response!.Items.Count == 0)
        {
            return Conflict(new { message = "This sales invoice has already been fully returned." });
        }

        return Ok(readModel.Response);
    }

    private async Task<SalesReturnInvoiceReadModelResult> BuildSalesReturnInvoiceReadModelAsync(
        SalesInvoice invoice,
        CancellationToken cancellationToken)
    {
        var snapshotBuildResult = await BuildOriginalInvoiceSnapshotsAsync(invoice);
        if (!snapshotBuildResult.Success)
        {
            return SalesReturnInvoiceReadModelResult.Fail(snapshotBuildResult.ErrorMessage!);
        }

        var snapshots = snapshotBuildResult.Snapshots!;
        var defaultVariantIds = snapshotBuildResult.DefaultVariantIds;
        var previousReturnItems = await _context.ReturnItems
            .AsNoTracking()
            .Where(item =>
                !item.IsDeleted &&
                !item.Return.IsDeleted &&
                item.Return.ReturnType == ReturnType.SalesReturn &&
                item.Return.OriginalInvoiceId == invoice.Id)
            .ToListAsync(cancellationToken);
        var previousReturns = await _context.Returns
            .AsNoTracking()
            .Where(previousReturn =>
                previousReturn.ReturnType == ReturnType.SalesReturn &&
                previousReturn.OriginalInvoiceId == invoice.Id)
            .ToListAsync(cancellationToken);
        var tenderCapacity = CalculateRemainingTenderCapacity(invoice, previousReturns);

        var priorBaseQuantitiesByKey = new Dictionary<OriginalInvoiceSnapshotKey, decimal>();
        foreach (var previousReturnItem in previousReturnItems)
        {
            var key = ResolvePriorReturnKey(previousReturnItem, snapshots, defaultVariantIds, out var errorMessage);
            if (errorMessage != null)
            {
                return SalesReturnInvoiceReadModelResult.Fail(errorMessage);
            }

            priorBaseQuantitiesByKey[key] =
                priorBaseQuantitiesByKey.GetValueOrDefault(key) + previousReturnItem.BaseQuantity;
        }

        var variantIds = snapshots.Keys.Select(key => key.EffectiveVariantId).ToList();
        var variantsById = await _context.ProductVariants
            .AsNoTracking()
            .Include(variant => variant.Attributes)
            .Where(variant => variantIds.Contains(variant.Id))
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);

        var items = new List<SalesReturnInvoiceItemResponseDto>();
        foreach (var snapshot in snapshots.Values.OrderBy(value => value.ProductName).ThenBy(value => value.EffectiveVariantId))
        {
            var previouslyReturnedBaseQuantity = priorBaseQuantitiesByKey.GetValueOrDefault(snapshot.Key);
            var remainingBaseQuantity = snapshot.OriginalBaseQuantity - previouslyReturnedBaseQuantity;
            if (remainingBaseQuantity <= 0)
            {
                continue;
            }

            var matchingOriginalItems = invoice.Items
                .Where(item =>
                    item.ProductId == snapshot.ProductId &&
                    ResolveEffectiveVariantId(item, defaultVariantIds) == snapshot.EffectiveVariantId)
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .ToList();
            var originalItem = matchingOriginalItems[0];
            variantsById.TryGetValue(snapshot.EffectiveVariantId, out var variant);

            items.Add(new SalesReturnInvoiceItemResponseDto
            {
                OriginalInvoiceItemIds = matchingOriginalItems.Select(item => item.Id).ToList(),
                ProductId = snapshot.ProductId,
                ProductName = snapshot.ProductName,
                ProductVariantId = snapshot.EffectiveVariantId,
                VariantSku = variant?.SKU ?? string.Empty,
                VariantAttributes = variant?.Attributes
                    .Where(attribute => !attribute.IsDeleted)
                    .OrderBy(attribute => attribute.SortOrder)
                    .Select(attribute => new ProductVariantAttributeResponseDto
                    {
                        Name = attribute.Name,
                        Value = attribute.Value
                    })
                    .ToList() ?? [],
                UnitId = snapshot.UnitId,
                UnitName = originalItem.Unit?.Name,
                UnitSymbol = originalItem.Unit?.Symbol,
                ConversionFactor = snapshot.ConversionFactor,
                SoldQuantity = snapshot.OriginalQuantity,
                PreviouslyReturnedQuantity = previouslyReturnedBaseQuantity / snapshot.ConversionFactor,
                RemainingQuantity = remainingBaseQuantity / snapshot.ConversionFactor,
                SoldBaseQuantity = snapshot.OriginalBaseQuantity,
                PreviouslyReturnedBaseQuantity = previouslyReturnedBaseQuantity,
                RemainingBaseQuantity = remainingBaseQuantity,
                UnitPrice = snapshot.UnitPrice,
                DiscountAmount = snapshot.LineDiscountAmount,
                TaxRate = snapshot.TaxRate,
                TaxAmount = snapshot.LineTaxAmount,
                TotalAmount = snapshot.OriginalLineTotalAmount
            });
        }

        return SalesReturnInvoiceReadModelResult.Ok(new SalesReturnInvoiceResponseDto
        {
            Id = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceDate = invoice.InvoiceDate,
            Status = invoice.Status,
            Subtotal = invoice.Subtotal,
            DiscountAmount = invoice.DiscountAmount,
            TaxAmount = invoice.TaxAmount,
            TotalAmount = invoice.TotalAmount,
            PaymentMethod = invoice.PaymentMethod,
            PaidAmount = invoice.PaidAmount,
            CashAmount = invoice.CashAmount,
            CardAmount = invoice.CardAmount,
            RemainingCashRefundableAmount = tenderCapacity.RemainingCashAmount,
            RemainingCardRefundableAmount = tenderCapacity.RemainingCardAmount,
            HasLegacySettlementGap = tenderCapacity.HasLegacySettlementGap,
            MutationVersion = invoice.AccountingMutationVersion,
            Customer = ToCustomerReference(invoice.Customer),
            Warehouse = ToWarehouseReference(invoice.Warehouse),
            Items = items
        });
    }

    private static Guid? ResolveEffectiveVariantId(
        SalesInvoiceItem item,
        IReadOnlyDictionary<Guid, Guid> defaultVariantIds) =>
        item.ProductVariantId ?? defaultVariantIds.GetValueOrDefault(item.ProductId);

    private static ContactReferenceDto? ToCustomerReference(Customer? customer) => customer == null
        ? null
        : new ContactReferenceDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Phone = customer.Phone,
            Email = customer.Email
        };

    private static WarehouseReferenceDto ToWarehouseReference(Warehouse warehouse) => new()
    {
        Id = warehouse.Id,
        Name = warehouse.Name,
        Code = warehouse.Code,
        Address = warehouse.Address
    };

    /// <summary>
    /// Create a new sales return
    /// 
    /// VARIANT-CENTRIC IMPLEMENTATION:
    /// - Stock restored to ProductVariantStocks (source of truth)
    /// - VariantId is MANDATORY (resolved from original invoice or default variant)
    /// - ProductVariantStockChangedEvent published for MongoDB sync
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SalesReturnResponse>> CreateSalesReturn(CreateSalesReturnRequest request)
    {
        var legacyRefundContractError = GetLegacyRefundContractError(request);
        if (legacyRefundContractError != null)
        {
            return BadRequest(new { message = legacyRefundContractError });
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            return BadRequest(new { message = "At least one return item is required" });
        }

        var paymentAttemptId = Request.Headers[PaymentAttemptIdHeader].FirstOrDefault();
        AddPaymentAttemptHeader(paymentAttemptId);

        var resolution = ResolveSalesReturnIdempotencyContext(request, paymentAttemptId);
        if (resolution.ErrorResult != null)
        {
            return resolution.ErrorResult;
        }

        var existingRecord = await FindSalesReturnIdempotencyAsync(resolution.IdempotencyKey!);
        if (existingRecord != null)
        {
            if (!string.Equals(existingRecord.RequestHash, resolution.RequestHash, StringComparison.Ordinal))
            {
                return Conflict(new
                {
                    message = "Idempotency-Key was already used for a different sales return request."
                });
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                return BuildStoredSalesReturnResponse(existingRecord);
            }

            return Conflict(new
            {
                message = "A sales return request with the same idempotency key is already being processed."
            });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var claim = await TryClaimSalesReturnIdempotencyAsync(
            resolution.IdempotencyKey!,
            resolution.RequestHash!);

        if (claim.ShortCircuitResult != null)
        {
            await transaction.RollbackAsync();
            return claim.ShortCircuitResult;
        }

        return await CreateSalesReturnInternal(
            request,
            paymentAttemptId,
            claim.Record!,
            transaction);
    }

    private void AddPaymentAttemptHeader(string? paymentAttemptId)
    {
        if (!string.IsNullOrWhiteSpace(paymentAttemptId))
        {
            Response.Headers[PaymentAttemptIdHeader] = paymentAttemptId;
        }
    }

    private (
        string? IdempotencyKey,
        string? RequestHash,
        ActionResult<SalesReturnResponse>? ErrorResult) ResolveSalesReturnIdempotencyContext(
        CreateSalesReturnRequest request,
        string? paymentAttemptId)
    {
        var idempotencyKey = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();
        var requestId = Request.Headers[RequestIdHeader].FirstOrDefault()?.Trim();

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
            var autoRequestHash = BuildSalesReturnRequestFingerprint(
                request);
            return ($"AUTO:{autoRequestHash}", autoRequestHash, null);
        }

        if (normalizedIdempotencyKey.Length > 200)
        {
            return (null, null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} must not exceed 200 characters."
            }));
        }

        return (
            normalizedIdempotencyKey,
            BuildSalesReturnRequestFingerprint(
                request),
            null);
    }

    private Guid? ResolveCurrentUserId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? User.FindFirstValue("sub")
                        ?? User.FindFirstValue("userId");

        return Guid.TryParse(rawUserId, out var parsedUserId)
            ? parsedUserId
            : null;
    }

    private string ResolveCurrentUserName()
    {
        return User.Identity?.Name
               ?? User.FindFirstValue(ClaimTypes.Name)
               ?? User.FindFirstValue("email")
               ?? "Unknown";
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? GetLegacyRefundContractError(CreateSalesReturnRequest request)
    {
        if (request.ExtensionData == null || request.ExtensionData.Count == 0)
        {
            return null;
        }

        if (request.ExtensionData.Keys.Any(key =>
                key.Equals("refundFromCash", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("refundAmount", StringComparison.OrdinalIgnoreCase)))
        {
            return "Legacy refund payload fields refundFromCash/refundAmount are no longer supported. Use refundMethod, cashAmount, and cardAmount.";
        }

        return null;
    }

    private static RefundAllocation ResolveRefundAllocation(CreateSalesReturnRequest request, decimal authoritativeReturnTotal)
    {
        decimal? cashAmount = request.CashAmount;
        decimal? cardAmount = request.CardAmount;

        if (request.RefundMethod == RefundMethod.Cash && !cashAmount.HasValue && !cardAmount.HasValue)
        {
            cashAmount = authoritativeReturnTotal;
            cardAmount = 0m;
        }
        else if (request.RefundMethod == RefundMethod.Card && !cashAmount.HasValue && !cardAmount.HasValue)
        {
            cashAmount = 0m;
            cardAmount = authoritativeReturnTotal;
        }

        var resolution = RefundAllocationSupport.Resolve(
            request.RefundMethod,
            cashAmount,
            cardAmount);

        if (!resolution.Success)
        {
            throw new ValidationException(resolution.ErrorMessage);
        }

        return resolution.Allocation!;
    }

    private static DateTime NormalizeToUtc(DateTime value)
    {
        if (value == default)
        {
            return DateTime.UtcNow;
        }

        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }

    private async Task<ActionResult<SalesReturnResponse>> CreateSalesReturnInternal(
        CreateSalesReturnRequest request,
        string? paymentAttemptId,
        SalesRequestIdempotency idempotencyRecord,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        if (!request.OriginalInvoiceId.HasValue)
        {
            return BadRequest(new { message = "Original sales invoice is required" });
        }

        var stockChanges = new List<PendingProductVariantStockChange>();

        try
        {
            // SR-CON-01 authoritative fence: keep the original invoice row write-locked for the
            // full return transaction so competing returns against the same invoice revalidate
            // against committed history instead of the same stale pre-return snapshot.
            var originalInvoice = await LoadOriginalInvoiceForReturnMutationAsync(request.OriginalInvoiceId.Value);

            if (originalInvoice == null)
            {
                return BadRequest(new { message = "Original sales invoice not found" });
            }

            if (originalInvoice.Items.Count == 0)
            {
                return BadRequest(new { message = "Original sales invoice has no items to return" });
            }

            if (originalInvoice.Status is not (InvoiceStatus.Completed or InvoiceStatus.PartiallyReturned))
            {
                return Conflict(new { message = "This sales invoice cannot accept another return." });
            }

            if (request.CustomerId != originalInvoice.CustomerId ||
                request.WarehouseId != originalInvoice.WarehouseId)
            {
                return BadRequest(new { message = "Customer and warehouse must match the original sales invoice." });
            }

            if (originalInvoice.Warehouse == null || !originalInvoice.Warehouse.IsActive || originalInvoice.Warehouse.IsDeleted)
            {
                return BadRequest(new { message = "The original sales warehouse is not active." });
            }

            var customer = originalInvoice.CustomerId.HasValue
                ? await _context.Customers.FindAsync(originalInvoice.CustomerId.Value)
                : null;

            if (originalInvoice.CustomerId.HasValue && customer == null)
            {
                return BadRequest(new { message = "Customer not found" });
            }

            var originalInvoiceTotal = RoundCurrency(originalInvoice.TotalAmount);
            if (originalInvoiceTotal <= 0)
            {
                return BadRequest(new { message = "Original sales invoice total must be greater than zero" });
            }

            var snapshotBuildResult = await BuildOriginalInvoiceSnapshotsAsync(originalInvoice);
            if (!snapshotBuildResult.Success)
            {
                return BadRequest(new { message = snapshotBuildResult.ErrorMessage });
            }

            var snapshots = snapshotBuildResult.Snapshots!;
            var defaultVariantIds = snapshotBuildResult.DefaultVariantIds;

            var previousReturnItems = await _context.ReturnItems
                .Where(ri =>
                    !ri.IsDeleted &&
                    !ri.Return.IsDeleted &&
                    ri.Return.ReturnType == ReturnType.SalesReturn &&
                    ri.Return.OriginalInvoiceId == originalInvoice.Id)
                .ToListAsync();

            var previousReturns = await _context.Returns
                .AsNoTracking()
                .Where(r => r.ReturnType == ReturnType.SalesReturn && r.OriginalInvoiceId == originalInvoice.Id)
                .ToListAsync();
            var priorReturnedTotal = RoundCurrency(previousReturns.Sum(previousReturn => previousReturn.TotalAmount));

            var priorBaseQuantitiesByKey = new Dictionary<OriginalInvoiceSnapshotKey, decimal>();
            foreach (var previousReturnItem in previousReturnItems)
            {
                var previousKey = ResolvePriorReturnKey(previousReturnItem, snapshots, defaultVariantIds, out var resolveError);
                if (resolveError != null)
                {
                    return BadRequest(new { message = resolveError });
                }

                priorBaseQuantitiesByKey[previousKey] =
                    priorBaseQuantitiesByKey.GetValueOrDefault(previousKey) + previousReturnItem.BaseQuantity;
            }

            var pendingLines = new List<PendingReturnLine>();
            var requestedBaseQuantitiesByKey = new Dictionary<OriginalInvoiceSnapshotKey, decimal>();
            var requestedKeys = new HashSet<OriginalInvoiceSnapshotKey>();
            var ignoredClientOverrides = new List<object>();

            foreach (var itemRequest in request.Items)
            {
                if (itemRequest.Quantity <= 0)
                {
                    return BadRequest(new { message = "Return quantity must be greater than zero" });
                }

                var snapshot = ResolveSnapshot(itemRequest, snapshots, defaultVariantIds, out var snapshotError);
                if (snapshot == null)
                {
                    return BadRequest(new { message = snapshotError });
                }

                if (!requestedKeys.Add(snapshot.Key))
                {
                    return BadRequest(new { message = "Duplicate product variant lines are not allowed in one sales return." });
                }

                if (itemRequest.UnitId.HasValue && itemRequest.UnitId != snapshot.UnitId)
                {
                    return BadRequest(new
                    {
                        message = $"Returned unit must match the original sale unit for {snapshot.ProductName}"
                    });
                }

                var currentBaseQuantity = itemRequest.Quantity * snapshot.ConversionFactor;
                if (currentBaseQuantity <= 0)
                {
                    return BadRequest(new { message = $"Return quantity must be greater than zero for {snapshot.ProductName}" });
                }

                var priorBaseQuantity = priorBaseQuantitiesByKey.GetValueOrDefault(snapshot.Key);
                var alreadyRequestedBaseQuantity = requestedBaseQuantitiesByKey.GetValueOrDefault(snapshot.Key);
                if (priorBaseQuantity + alreadyRequestedBaseQuantity + currentBaseQuantity > snapshot.OriginalBaseQuantity)
                {
                    return BadRequest(new
                    {
                        message = $"Cannot return more than sold quantity for {snapshot.ProductName}"
                    });
                }

                var normalizedClientUnitPrice = itemRequest.UnitPrice.HasValue
                    ? RoundCurrency(itemRequest.UnitPrice.Value)
                    : (decimal?)null;
                var normalizedClientTaxRate = itemRequest.TaxRate.HasValue
                    ? RoundCurrency(itemRequest.TaxRate.Value)
                    : (decimal?)null;
                if ((normalizedClientUnitPrice.HasValue && normalizedClientUnitPrice.Value != snapshot.UnitPrice) ||
                    (normalizedClientTaxRate.HasValue && normalizedClientTaxRate.Value != snapshot.TaxRate))
                {
                    ignoredClientOverrides.Add(new
                    {
                        snapshot.ProductId,
                        snapshot.EffectiveVariantId,
                        clientUnitPrice = normalizedClientUnitPrice,
                        clientTaxRate = normalizedClientTaxRate,
                        authoritativeUnitPrice = snapshot.UnitPrice,
                        authoritativeTaxRate = snapshot.TaxRate
                    });

                    _logger.LogWarning(
                        "Ignored client refund pricing override for invoice {InvoiceId}, product {ProductId}, variant {VariantId}. Client UnitPrice={ClientUnitPrice}, Client TaxRate={ClientTaxRate}, Server UnitPrice={ServerUnitPrice}, Server TaxRate={ServerTaxRate} | TenantId: {TenantId} | TraceId: {TraceId}",
                        originalInvoice.Id,
                        snapshot.ProductId,
                        snapshot.EffectiveVariantId,
                        normalizedClientUnitPrice,
                        normalizedClientTaxRate,
                        snapshot.UnitPrice,
                        snapshot.TaxRate,
                        _tenantContext.RequiredTenantId,
                        GetTraceId());
                }

                var effectivePriorBaseQuantity = priorBaseQuantity + alreadyRequestedBaseQuantity;
                pendingLines.Add(new PendingReturnLine(
                    snapshot,
                    itemRequest.Quantity,
                    currentBaseQuantity,
                    RoundCurrency(snapshot.UnitPrice * itemRequest.Quantity),
                    CalculateCumulativeProrated(snapshot.LineDiscountAmount, snapshot.OriginalBaseQuantity, effectivePriorBaseQuantity, currentBaseQuantity),
                    CalculateCumulativeProrated(snapshot.LineTaxAmount, snapshot.OriginalBaseQuantity, effectivePriorBaseQuantity, currentBaseQuantity),
                    itemRequest.Reason));

                requestedBaseQuantitiesByKey[snapshot.Key] = alreadyRequestedBaseQuantity + currentBaseQuantity;
            }

            if (pendingLines.Count == 0)
            {
                return BadRequest(new { message = "At least one return item is required" });
            }

            var originalInvoiceDiscountBasis = RoundCurrency(
                originalInvoice.Subtotal - snapshots.Values.Sum(snapshot => snapshot.LineDiscountAmount));
            var priorReturnedDiscountBasis = RoundCurrency(snapshots.Values.Sum(snapshot =>
            {
                var priorBaseQuantity = priorBaseQuantitiesByKey.GetValueOrDefault(snapshot.Key);
                if (priorBaseQuantity <= 0 || snapshot.ConversionFactor <= 0)
                {
                    return 0m;
                }

                var priorGrossSubtotal = RoundCurrency(
                    snapshot.UnitPrice * (priorBaseQuantity / snapshot.ConversionFactor));
                var priorLineDiscount = CalculateCumulativeProrated(
                    snapshot.LineDiscountAmount,
                    snapshot.OriginalBaseQuantity,
                    0m,
                    priorBaseQuantity);
                return RoundCurrency(priorGrossSubtotal - priorLineDiscount);
            }));

            var invoiceDiscountShares = AllocateAmountAcrossLines(
                pendingLines,
                CalculateCumulativeProrated(
                    RoundCurrency(originalInvoice.DiscountAmount),
                    originalInvoiceDiscountBasis,
                    priorReturnedDiscountBasis,
                    RoundCurrency(pendingLines.Sum(line => line.GrossSubtotal - line.LineDiscountAmount))),
                line => RoundCurrency(line.GrossSubtotal - line.LineDiscountAmount));
            var statusAfterReturn = priorBaseQuantitiesByKey.Values.Sum() + pendingLines.Sum(line => line.BaseQuantity) >=
                                    snapshots.Values.Sum(snapshot => snapshot.OriginalBaseQuantity)
                ? InvoiceStatus.FullyReturned
                : InvoiceStatus.PartiallyReturned;

            return await FinalizeSalesReturnAsync(
                request,
                paymentAttemptId,
                idempotencyRecord,
                originalInvoice,
                customer,
                originalInvoiceTotal,
                priorReturnedTotal,
                previousReturns,
                pendingLines,
                invoiceDiscountShares,
                statusAfterReturn,
                ignoredClientOverrides,
                stockChanges,
                transaction);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(
                ex,
                "Error creating sales return | TenantId: {TenantId} | OriginalInvoiceId: {OriginalInvoiceId} | TraceId: {TraceId}",
                _tenantContext.RequiredTenantId,
                request.OriginalInvoiceId,
                GetTraceId());
            return StatusCode(500, new
            {
                message = "An unexpected error occurred while processing the sales return."
            });
        }
    }

    private async Task<ActionResult<SalesReturnResponse>> FinalizeSalesReturnAsync(
        CreateSalesReturnRequest request,
        string? paymentAttemptId,
        SalesRequestIdempotency idempotencyRecord,
        SalesInvoice originalInvoice,
        Customer? customer,
        decimal originalInvoiceTotal,
        decimal priorReturnedTotal,
        IReadOnlyCollection<Return> previousReturns,
        List<PendingReturnLine> pendingLines,
        decimal[] invoiceDiscountShares,
        InvoiceStatus statusAfterReturn,
        List<object> ignoredClientOverrides,
        List<PendingProductVariantStockChange> stockChanges,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        var currentUserId = ResolveCurrentUserId();
        var returnTimestamp = DateTime.UtcNow;
        var returnNumber = await DocumentNumberingSupport.AllocateReturnNumberAsync(
            _context,
            ReturnType.SalesReturn,
            returnTimestamp);

        var salesReturn = new Return
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.RequiredTenantId,
            ReturnNumber = returnNumber,
            ReturnDate = NormalizeToUtc(request.ReturnDate),
            ReturnType = ReturnType.SalesReturn,
            OriginalInvoiceId = originalInvoice.Id,
            OriginalInvoiceNumber = originalInvoice.InvoiceNumber,
            CustomerId = originalInvoice.CustomerId,
            WarehouseId = originalInvoice.WarehouseId,
            Reason = request.Reason,
            Notes = request.Notes,
            CreatedAt = returnTimestamp,
            CreatedBy = currentUserId
        };

        await LockReturnProjectionProductsAsync(
            pendingLines.Select(line => line.Snapshot.ProductId));

        var variantIds = pendingLines
            .Select(line => line.Snapshot.EffectiveVariantId)
            .Distinct()
            .ToList();

        var variantsById = await _context.ProductVariants
            .Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id);
        var lockTargets = pendingLines
            .Select(line => new StockLockTarget(
                originalInvoice.WarehouseId,
                line.Snapshot.EffectiveVariantId,
                CreateIfMissing: true))
            .DistinctBy(target => (target.WarehouseId, target.VariantId))
            .ToList();
        var stockMap = await ProductVariantStockMutationSupport.LockMultipleStocksAsync(
            _context,
            _tenantContext.RequiredTenantId,
            lockTargets);

        decimal subtotal = 0;
        decimal totalTax = 0;
        var lineAuditEntries = new List<object>();

        for (var index = 0; index < pendingLines.Count; index++)
        {
            var line = pendingLines[index];
            var invoiceDiscountShare = invoiceDiscountShares[index];
            var netSubtotal = RoundCurrency(line.GrossSubtotal - line.LineDiscountAmount - invoiceDiscountShare);
            if (netSubtotal < 0)
            {
                return BadRequest(new
                {
                    message = $"Computed refund subtotal became negative for {line.Snapshot.ProductName}. Review the original invoice before refunding."
                });
            }

            var totalAmount = RoundCurrency(netSubtotal + line.LineTaxAmount);
            var returnItem = new ReturnItem
            {
                Id = Guid.NewGuid(),
                ReturnId = salesReturn.Id,
                ProductId = line.Snapshot.ProductId,
                ProductVariantId = line.Snapshot.EffectiveVariantId,
                UnitId = line.Snapshot.UnitId,
                Quantity = line.Quantity,
                ConversionFactor = line.Snapshot.ConversionFactor,
                BaseQuantity = line.BaseQuantity,
                UnitPrice = line.Snapshot.UnitPrice,
                TaxRate = line.Snapshot.TaxRate,
                TaxAmount = line.LineTaxAmount,
                TotalAmount = totalAmount,
                CostPriceSnapshot = line.Snapshot.CostPriceSnapshot,
                CostAmountSnapshot = ResolveReturnCostAmountSnapshot(
                    line.Snapshot.CostPriceSnapshot,
                    line.BaseQuantity),
                Reason = line.Reason,
                CreatedAt = returnTimestamp,
                CreatedBy = currentUserId
            };

            salesReturn.Items.Add(returnItem);
            subtotal = RoundCurrency(subtotal + netSubtotal);
            totalTax = RoundCurrency(totalTax + line.LineTaxAmount);

            lineAuditEntries.Add(new
            {
                line.Snapshot.ProductId,
                line.Snapshot.EffectiveVariantId,
                line.Snapshot.ProductName,
                line.Quantity,
                baseQuantity = line.BaseQuantity,
                unitId = line.Snapshot.UnitId,
                unitPrice = line.Snapshot.UnitPrice,
                taxRate = line.Snapshot.TaxRate,
                grossSubtotal = line.GrossSubtotal,
                lineDiscount = line.LineDiscountAmount,
                invoiceDiscount = invoiceDiscountShare,
                taxAmount = line.LineTaxAmount,
                totalAmount
            });

            if (!stockMap.TryGetValue((originalInvoice.WarehouseId, line.Snapshot.EffectiveVariantId), out var variantStock))
            {
                throw new InvalidOperationException(
                    $"Stock record not found in locked stock map for WarehouseId={originalInvoice.WarehouseId}, " +
                    $"VariantId={line.Snapshot.EffectiveVariantId}. This may indicate a silent row-creation failure " +
                    $"(e.g., CreateIfMissing: true with ON CONFLICT DO NOTHING).");
            }

            var oldQuantity = variantStock.Quantity;
            variantStock.Quantity += line.BaseQuantity;
            variantStock.UpdatedAt = returnTimestamp;
            variantStock.UpdatedBy = currentUserId;
            variantsById.TryGetValue(line.Snapshot.EffectiveVariantId, out var variantEntity);
            stockChanges.Add(ProductVariantStockMutationSupport.BuildPendingStockChange(
                line.Snapshot.EffectiveVariantId,
                line.Snapshot.ProductId,
                originalInvoice.WarehouseId,
                oldQuantity,
                variantStock.Quantity,
                "SalesReturn",
                referenceId: salesReturn.Id,
                referenceType: "SalesReturn",
                sku: variantEntity?.SKU));

            _context.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,
                WarehouseId = originalInvoice.WarehouseId,
                ProductId = line.Snapshot.ProductId,
                ProductVariantId = line.Snapshot.EffectiveVariantId,
                MovementType = StockMovementType.SalesReturn,
                UnitId = line.Snapshot.UnitId,
                ConversionFactor = line.Snapshot.ConversionFactor,
                QuantityChange = line.BaseQuantity,
                QuantityAfter = variantStock.Quantity,
                ReferenceId = salesReturn.Id,
                ReferenceNumber = returnNumber,
                Price = line.Snapshot.CostPriceSnapshot,
                Notes = $"Sales Return: {request.Reason}",
                CreatedAt = returnTimestamp,
                CreatedBy = currentUserId
            });
        }

        salesReturn.Subtotal = RoundCurrency(subtotal);
        salesReturn.TaxAmount = RoundCurrency(totalTax);
        salesReturn.TotalAmount = RoundCurrency(salesReturn.Subtotal + salesReturn.TaxAmount);

        if (salesReturn.TotalAmount <= 0)
        {
            return BadRequest(new { message = "Calculated refund total must be greater than zero" });
        }

        if (RoundCurrency(priorReturnedTotal + salesReturn.TotalAmount) > originalInvoiceTotal)
        {
            return BadRequest(new
            {
                message = "Calculated refund total exceeds the original invoice total after prior returns"
            });
        }

        RefundAllocation refundAllocation;
        try
        {
            refundAllocation = ResolveRefundAllocation(request, salesReturn.TotalAmount);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var approvedPayoutAmount = refundAllocation.TotalRefundAmount;

        if (approvedPayoutAmount < 0)
        {
            return BadRequest(new { message = "Refund amount cannot be negative" });
        }

        if (approvedPayoutAmount > 0 && approvedPayoutAmount > originalInvoiceTotal)
        {
            return BadRequest(new { message = "Refund amount cannot exceed the original invoice total" });
        }

        if (approvedPayoutAmount > salesReturn.TotalAmount)
        {
            return BadRequest(new { message = "Refund amount cannot exceed the calculated return total" });
        }

        var creditToCustomerAmount = RoundCurrency(salesReturn.TotalAmount - approvedPayoutAmount);
        if (refundAllocation.Method == RefundMethod.AccountCredit)
        {
            creditToCustomerAmount = salesReturn.TotalAmount;
        }
        else if (approvedPayoutAmount != salesReturn.TotalAmount)
        {
            return BadRequest(new
            {
                message = "Cash, card, and mixed refunds must allocate the full calculated return total. Use AccountCredit for non-payout returns."
            });
        }

        if (creditToCustomerAmount > 0 && customer == null)
        {
            return BadRequest(new
            {
                message = "A customer account is required when the refund is not fully paid out immediately"
            });
        }

        salesReturn.RefundMethod = refundAllocation.Method;
        salesReturn.CashRefundAmount = refundAllocation.CashAmount;
        salesReturn.CardRefundAmount = refundAllocation.CardAmount;
        salesReturn.CustomerCreditAmount = creditToCustomerAmount;

        var tenderValidationError = ValidateRefundAgainstOriginalTender(
            originalInvoice,
            refundAllocation,
            previousReturns);
        if (tenderValidationError != null)
        {
            return BadRequest(new { message = tenderValidationError });
        }

        // The sales invoice remains the bounded historical authority for all return mutations.
        // Advance its mutation version exactly once per committed return so downstream intent is
        // auditable against a deterministic, monotonic invoice version stream.
        originalInvoice.AccountingMutationVersion++;
        var refundMutationVersion = originalInvoice.AccountingMutationVersion;
        originalInvoice.Status = statusAfterReturn;

        _context.Returns.Add(salesReturn);

        if (customer != null && creditToCustomerAmount > 0)
        {
            await _ledgerService.CreateLedgerEntry(
                PartyType.Customer,
                customer.Id,
                returnTimestamp,
                TransactionType.SalesReturn,
                returnNumber,
                0,
                creditToCustomerAmount,
                salesReturn.Id,
                $"Sales Return {returnNumber}: {request.Reason}");

            _context.CustomerTransactions.Add(new CustomerTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,
                CustomerId = customer.Id,
                TransactionDate = returnTimestamp,
                TransactionType = TransactionType.SalesReturn,
                DebitAmount = 0,
                CreditAmount = creditToCustomerAmount,
                BalanceAfter = customer.Balance,
                ReferenceId = salesReturn.Id,
                ReferenceNumber = returnNumber,
                Notes = $"Sales Return: {request.Reason}",
                CreatedAt = returnTimestamp,
                CreatedBy = currentUserId
            });
        }

        await _accountingOutbox.EnqueuePosRefundAsync(
            salesReturn.TenantId,
            salesReturn.Id,
            originalInvoice.Id,
            refundAllocation.Method,
            refundAllocation.CashAmount,
            refundAllocation.CardAmount,
            refundMutationVersion);

        _context.AuditLogs.Add(new AuditLog
        {
            TenantId = _tenantContext.RequiredTenantId,
            TimestampUtc = returnTimestamp,
            UserId = currentUserId,
            UserNameSnapshot = ResolveCurrentUserName(),
            ActionType = "Create",
            EntityType = "SalesReturn",
            EntityId = salesReturn.Id.ToString(),
            Description = $"System-calculated sales return created for invoice {originalInvoice.InvoiceNumber}",
            AfterDataJson = JsonSerializer.Serialize(new
            {
                invoiceId = originalInvoice.Id,
                userId = currentUserId,
                originalTotal = originalInvoiceTotal,
                refundTotal = salesReturn.TotalAmount,
                payoutAmount = approvedPayoutAmount,
                refundMethod = refundAllocation.Method.ToString(),
                cashAmount = refundAllocation.CashAmount,
                cardAmount = refundAllocation.CardAmount,
                creditedToCustomer = creditToCustomerAmount,
                items = lineAuditEntries,
                paymentAttemptId,
                ignoredClientOverrides,
                isAutoCalculated = true,
                timestamp = returnTimestamp
            }),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            ReferenceNumber = returnNumber,
            Source = "API",
            SuccessFlag = true
        });

        await DocumentNumberingSupport.SaveReturnChangesWithRetryAsync(
            _context,
            salesReturn,
            returnTimestamp,
            _logger);
        returnNumber = salesReturn.ReturnNumber;

        if (stockChanges.Count > 0)
        {
            await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                _context,
                _tenantContext.RequiredTenantId,
                stockChanges);
        }

        var response = new SalesReturnResponse
        {
            ReturnId = salesReturn.Id,
            ReturnNumber = returnNumber,
            TotalAmount = salesReturn.TotalAmount,
            RefundAmount = approvedPayoutAmount,
            RefundMethod = refundAllocation.Method,
            CashAmount = refundAllocation.CashAmount,
            CardAmount = refundAllocation.CardAmount,
            CustomerCreditAmount = creditToCustomerAmount,
            IsAutoCalculated = true,
            Message = "Sales return created successfully"
        };

        idempotencyRecord.ResponseStatusCode = StatusCodes.Status200OK;
        idempotencyRecord.ResponseJson = SerializeSalesReturnPayload(response);
        idempotencyRecord.CompletedAtUtc = DateTime.UtcNow;
        idempotencyRecord.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var refundAccountingCompletedSynchronously = await TryCompleteSynchronousRefundAccountingAsync(
            salesReturn,
            refundAllocation,
            returnNumber,
            HttpContext.RequestAborted);

        _logger.LogInformation(
            "Sales return {ReturnNumber} created for invoice {InvoiceNumber}. Server-calculated total {TotalAmount}, payout {PayoutAmount}, credited amount {CreditAmount}. RefundAccountingCompletedSynchronously={RefundAccountingCompletedSynchronously} | TenantId: {TenantId} | TraceId: {TraceId}",
            returnNumber,
            originalInvoice.InvoiceNumber,
            salesReturn.TotalAmount,
            approvedPayoutAmount,
            creditToCustomerAmount,
            refundAccountingCompletedSynchronously,
            _tenantContext.RequiredTenantId,
            GetTraceId());

        return Ok(response);
    }

    private async Task<(SalesRequestIdempotency? Record, ActionResult<SalesReturnResponse>? ShortCircuitResult)> TryClaimSalesReturnIdempotencyAsync(
        string idempotencyKey,
        string requestHash)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claimId = Guid.NewGuid();
            var nowUtc = DateTime.UtcNow;
            int rowsInserted;

            if (IsPostgresProvider())
            {
                rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO ""SalesRequestIdempotency""
                    (""Id"", ""TenantId"", ""Key"", ""Operation"", ""RequestHash"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                    VALUES
                    ({claimId}, {tenantId}, {idempotencyKey}, {SalesReturnCreateOperation}, {requestHash}, {nowUtc}, {nowUtc}, {false})
                    ON CONFLICT (""TenantId"", ""Key"", ""Operation"") DO NOTHING");
            }
            else if (IsSqlServerProvider())
            {
                rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO [SalesRequestIdempotency]
                        ([Id], [TenantId], [Key], [Operation], [RequestHash], [CreatedAt], [UpdatedAt], [IsDeleted])
                    SELECT
                        {claimId}, {tenantId}, {idempotencyKey}, {SalesReturnCreateOperation}, {requestHash}, {nowUtc}, {nowUtc}, {false}
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM [SalesRequestIdempotency] WITH (UPDLOCK, HOLDLOCK)
                        WHERE [TenantId] = {tenantId}
                          AND [Key] = {idempotencyKey}
                          AND [Operation] = {SalesReturnCreateOperation})");
            }
            else
            {
                throw new NotSupportedException(
                    $"Database provider '{_context.Database.ProviderName}' is not supported for sales return idempotency.");
            }

            if (rowsInserted > 0)
            {
                var claimedRecord = await _context.SalesRequestIdempotencyRecords
                    .FirstAsync(record =>
                        record.TenantId == tenantId &&
                        record.Key == idempotencyKey &&
                        record.Operation == SalesReturnCreateOperation);

                return (claimedRecord, null);
            }

            var existingRecord = await _context.SalesRequestIdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(record =>
                    record.TenantId == tenantId &&
                    record.Key == idempotencyKey &&
                    record.Operation == SalesReturnCreateOperation);

            if (existingRecord == null)
            {
                continue;
            }

            if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return (null, Conflict(new
                {
                    message = "Idempotency-Key was already used for a different sales return request."
                }));
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                return (null, BuildStoredSalesReturnResponse(existingRecord));
            }

            return (null, Conflict(new
            {
                message = "A sales return request with the same idempotency key is already being processed."
            }));
        }

        return (null, Conflict(new
        {
            message = "A sales return request with the same idempotency key is already being processed."
        }));
    }

    private async Task<SalesRequestIdempotency?> FindSalesReturnIdempotencyAsync(string idempotencyKey)
    {
        return await _context.SalesRequestIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(record =>
                record.TenantId == _tenantContext.RequiredTenantId &&
                record.Key == idempotencyKey &&
                record.Operation == SalesReturnCreateOperation);
    }

    private async Task<SalesInvoice?> LoadOriginalInvoiceForReturnMutationAsync(Guid originalInvoiceId)
    {
        IQueryable<SalesInvoice> query;

        if (IsPostgresProvider())
        {
            query = _context.SalesInvoices
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""SalesInvoices""
                    WHERE ""TenantId"" = {_tenantContext.RequiredTenantId}
                      AND ""Id"" = {originalInvoiceId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else if (IsSqlServerProvider())
        {
            query = _context.SalesInvoices
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [SalesInvoices] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {_tenantContext.RequiredTenantId}
                      AND [Id] = {originalInvoiceId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters();
        }
        else
        {
            query = _context.SalesInvoices
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
            .Reference(invoice => invoice.Customer)
            .LoadAsync();

        await _context.Entry(originalInvoice)
            .Reference(invoice => invoice.Warehouse)
            .LoadAsync();

        await _context.Entry(originalInvoice)
            .Collection(invoice => invoice.Items)
            .Query()
            .Include(item => item.Product)
            .Include(item => item.ProductVariant)
            .Include(item => item.Unit)
            .LoadAsync();

        return originalInvoice;
    }

    private async Task LockReturnProjectionProductsAsync(IEnumerable<Guid> productIds)
    {
        foreach (var productId in productIds.Distinct().OrderBy(id => id))
        {
            IQueryable<Product> query;

            if (IsPostgresProvider())
            {
                query = _context.Products
                    .FromSqlInterpolated($@"
                        SELECT *, xmin
                        FROM ""Products""
                        WHERE ""TenantId"" = {_tenantContext.RequiredTenantId}
                          AND ""Id"" = {productId}
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
                          AND [Id] = {productId}")
                    .IgnoreQueryFilters();
            }
            else
            {
                query = _context.Products
                    .IgnoreQueryFilters()
                    .Where(product =>
                        product.TenantId == _tenantContext.RequiredTenantId &&
                        product.Id == productId);
            }

            var product = await query.FirstOrDefaultAsync()
                ?? throw new InvalidOperationException(
                    $"Sales return projection product {productId} is no longer authoritative.");

            // The invoice snapshot may have tracked this product before the lock was acquired.
            await _context.Entry(product).ReloadAsync();
        }
    }

    private bool IsPostgresProvider() =>
        _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true ||
        _context.Database.ProviderName?.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true ||
        _context.Database.ProviderName?.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<OriginalInvoiceSnapshotBuildResult> BuildOriginalInvoiceSnapshotsAsync(SalesInvoice originalInvoice)
    {
        var invoiceProductIds = originalInvoice.Items
            .Select(item => item.ProductId)
            .Distinct()
            .ToList();

        var defaultVariantIds = await LoadDefaultVariantsAsync(invoiceProductIds);
        var groupedItems = new Dictionary<OriginalInvoiceSnapshotKey, List<SalesInvoiceItem>>();

        foreach (var originalItem in originalInvoice.Items)
        {
            if (originalItem.Quantity <= 0 || originalItem.BaseQuantity <= 0 || originalItem.ConversionFactor <= 0)
            {
                return OriginalInvoiceSnapshotBuildResult.Fail(
                    $"Original sales invoice contains an invalid quantity snapshot for {originalItem.Product?.Name ?? originalItem.ProductId.ToString()}");
            }

            var effectiveVariantId = originalItem.ProductVariantId;
            if (!effectiveVariantId.HasValue)
            {
                if (!defaultVariantIds.TryGetValue(originalItem.ProductId, out var defaultVariantId))
                {
                    return OriginalInvoiceSnapshotBuildResult.Fail(
                        $"Cannot resolve variant for legacy product {originalItem.Product?.Name ?? originalItem.ProductId.ToString()}");
                }

                effectiveVariantId = defaultVariantId;
            }

            var key = new OriginalInvoiceSnapshotKey(originalItem.ProductId, effectiveVariantId.Value);
            if (!groupedItems.TryGetValue(key, out var group))
            {
                group = new List<SalesInvoiceItem>();
                groupedItems[key] = group;
            }

            group.Add(originalItem);
        }

        var snapshots = new Dictionary<OriginalInvoiceSnapshotKey, OriginalInvoiceLineSnapshot>();
        foreach (var (key, group) in groupedItems)
        {
            var firstItem = group[0];
            var expectedUnitId = firstItem.UnitId;
            var expectedConversionFactor = firstItem.ConversionFactor;
            var expectedUnitPrice = RoundCurrency(firstItem.UnitPrice);
            var expectedTaxRate = RoundCurrency(firstItem.TaxRate);
            var expectedDiscountPerBaseUnit = GetPerBaseUnitAmount(firstItem.DiscountAmount, firstItem.BaseQuantity);
            var expectedTaxPerBaseUnit = GetPerBaseUnitAmount(firstItem.TaxAmount, firstItem.BaseQuantity);
            var expectedCostPriceSnapshot = ResolveOriginalSaleLineCostPriceSnapshot(firstItem);

            if (group.Any(item =>
                    item.UnitId != expectedUnitId ||
                    item.ConversionFactor != expectedConversionFactor ||
                    RoundCurrency(item.UnitPrice) != expectedUnitPrice ||
                    RoundCurrency(item.TaxRate) != expectedTaxRate ||
                    GetPerBaseUnitAmount(item.DiscountAmount, item.BaseQuantity) != expectedDiscountPerBaseUnit ||
                    GetPerBaseUnitAmount(item.TaxAmount, item.BaseQuantity) != expectedTaxPerBaseUnit ||
                    ResolveOriginalSaleLineCostPriceSnapshot(item) != expectedCostPriceSnapshot))
            {
                return OriginalInvoiceSnapshotBuildResult.Fail(
                    $"Original sales invoice contains multiple pricing or cost snapshots for {firstItem.Product?.Name ?? firstItem.ProductId.ToString()}. Process this return manually.");
            }

            snapshots[key] = new OriginalInvoiceLineSnapshot(
                key,
                firstItem.ProductId,
                key.EffectiveVariantId,
                expectedUnitId,
                expectedConversionFactor,
                RoundCurrency(group.Sum(item => item.Quantity)),
                group.Sum(item => item.BaseQuantity),
                expectedUnitPrice,
                expectedTaxRate,
                RoundCurrency(group.Sum(item => item.DiscountAmount)),
                RoundCurrency(group.Sum(item => item.TaxAmount)),
                RoundCurrency(group.Sum(item => item.TotalAmount)),
                expectedCostPriceSnapshot,
                RoundCurrency(group.Sum(ResolveOriginalSaleLineCostAmountSnapshot)),
                firstItem.Product?.Name ?? firstItem.ProductId.ToString());
        }

        return OriginalInvoiceSnapshotBuildResult.Ok(snapshots, defaultVariantIds);
    }

    private static OriginalInvoiceLineSnapshot? ResolveSnapshot(
        SalesReturnItemRequest requestItem,
        IReadOnlyDictionary<OriginalInvoiceSnapshotKey, OriginalInvoiceLineSnapshot> snapshots,
        IReadOnlyDictionary<Guid, Guid> defaultVariantIds,
        out string? errorMessage)
    {
        errorMessage = null;
        var productSnapshots = snapshots.Values
            .Where(snapshot => snapshot.ProductId == requestItem.ProductId)
            .ToList();

        if (productSnapshots.Count == 0)
        {
            errorMessage = $"Product {requestItem.ProductId} was not sold on the original invoice";
            return null;
        }

        Guid effectiveVariantId;
        if (requestItem.ProductVariantId.HasValue)
        {
            effectiveVariantId = requestItem.ProductVariantId.Value;
        }
        else if (productSnapshots.Select(snapshot => snapshot.EffectiveVariantId).Distinct().Count() == 1)
        {
            effectiveVariantId = productSnapshots[0].EffectiveVariantId;
        }
        else if (defaultVariantIds.TryGetValue(requestItem.ProductId, out var defaultVariantId)
                 && productSnapshots.Any(snapshot => snapshot.EffectiveVariantId == defaultVariantId))
        {
            effectiveVariantId = defaultVariantId;
        }
        else
        {
            errorMessage = $"Product {requestItem.ProductId} has multiple variant snapshots on the original invoice. Process this return manually.";
            return null;
        }

        var key = new OriginalInvoiceSnapshotKey(requestItem.ProductId, effectiveVariantId);
        if (!snapshots.TryGetValue(key, out var snapshot))
        {
            errorMessage = "Product variant was not sold on the original invoice";
            return null;
        }

        return snapshot;
    }

    private static OriginalInvoiceSnapshotKey ResolvePriorReturnKey(
        ReturnItem previousReturnItem,
        IReadOnlyDictionary<OriginalInvoiceSnapshotKey, OriginalInvoiceLineSnapshot> snapshots,
        IReadOnlyDictionary<Guid, Guid> defaultVariantIds,
        out string? errorMessage)
    {
        errorMessage = null;
        var productSnapshots = snapshots.Values
            .Where(snapshot => snapshot.ProductId == previousReturnItem.ProductId)
            .ToList();

        if (productSnapshots.Count == 0)
        {
            errorMessage = $"Historical return item {previousReturnItem.ProductId} no longer matches the original invoice";
            return default;
        }

        Guid effectiveVariantId;
        if (previousReturnItem.ProductVariantId.HasValue)
        {
            effectiveVariantId = previousReturnItem.ProductVariantId.Value;
        }
        else if (productSnapshots.Select(snapshot => snapshot.EffectiveVariantId).Distinct().Count() == 1)
        {
            effectiveVariantId = productSnapshots[0].EffectiveVariantId;
        }
        else if (defaultVariantIds.TryGetValue(previousReturnItem.ProductId, out var defaultVariantId)
                 && productSnapshots.Any(snapshot => snapshot.EffectiveVariantId == defaultVariantId))
        {
            effectiveVariantId = defaultVariantId;
        }
        else
        {
            errorMessage = $"Historical return items for product {previousReturnItem.ProductId} are ambiguous and must be reviewed manually";
            return default;
        }

        return new OriginalInvoiceSnapshotKey(previousReturnItem.ProductId, effectiveVariantId);
    }

    private async Task<Dictionary<Guid, Guid>> LoadDefaultVariantsAsync(IEnumerable<Guid> productIds)
    {
        var distinctProductIds = productIds
            .Distinct()
            .ToList();

        if (distinctProductIds.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        return await _context.Set<ProductVariant>()
            .Where(variant => distinctProductIds.Contains(variant.ProductId) && variant.IsDefault)
            .GroupBy(variant => variant.ProductId)
            .ToDictionaryAsync(group => group.Key, group => group.First().Id);
    }

    private static decimal[] AllocateAmountAcrossLines<T>(
        IReadOnlyList<T> lines,
        decimal totalAmount,
        Func<T, decimal> basisSelector)
    {
        var allocations = new decimal[lines.Count];
        if (lines.Count == 0 || totalAmount == 0)
        {
            return allocations;
        }

        var totalBasis = lines.Sum(basisSelector);
        if (totalBasis <= 0)
        {
            return allocations;
        }

        decimal allocatedAmount = 0;
        decimal accumulatedBasis = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            accumulatedBasis += basisSelector(lines[index]);
            var cumulativeAllocation = ProrateAmount(totalAmount, accumulatedBasis, totalBasis);
            allocations[index] = RoundCurrency(cumulativeAllocation - allocatedAmount);
            allocatedAmount = cumulativeAllocation;
        }

        return allocations;
    }

    private static decimal CalculateCumulativeProrated(
        decimal totalAmount,
        decimal originalBasis,
        decimal priorBasis,
        decimal currentBasis)
    {
        if (totalAmount == 0 || originalBasis <= 0 || currentBasis <= 0)
        {
            return 0;
        }

        var normalizedTotalAmount = RoundCurrency(totalAmount);
        var safePriorBasis = Math.Max(0, priorBasis);
        var safeCurrentBasis = Math.Max(0, currentBasis);
        var previousAllocation = ProrateAmount(normalizedTotalAmount, safePriorBasis, originalBasis);
        var cumulativeAllocation = ProrateAmount(normalizedTotalAmount, safePriorBasis + safeCurrentBasis, originalBasis);
        return RoundCurrency(cumulativeAllocation - previousAllocation);
    }

    private static decimal ProrateAmount(decimal amount, decimal partialBasis, decimal totalBasis)
    {
        if (amount == 0 || partialBasis <= 0 || totalBasis <= 0)
        {
            return 0;
        }

        var normalizedAmount = RoundCurrency(amount);
        var clampedBasis = Math.Min(partialBasis, totalBasis);
        return RoundCurrency(normalizedAmount * clampedBasis / totalBasis);
    }

    private static decimal GetPerBaseUnitAmount(decimal amount, decimal baseQuantity)
    {
        if (baseQuantity <= 0)
        {
            return 0;
        }

        return Math.Round(amount / baseQuantity, 6, MidpointRounding.AwayFromZero);
    }

    private static decimal ResolveOriginalSaleLineCostPriceSnapshot(SalesInvoiceItem item)
    {
        if (item.CostPriceSnapshot.HasValue)
        {
            return Math.Round(item.CostPriceSnapshot.Value, 6, MidpointRounding.AwayFromZero);
        }

        var costPrice = item.ProductVariant?.CostPrice ?? 0m;
        return Math.Round(costPrice, 6, MidpointRounding.AwayFromZero);
    }

    private static decimal ResolveOriginalSaleLineCostAmountSnapshot(SalesInvoiceItem item)
    {
        if (item.CostAmountSnapshot.HasValue)
        {
            return item.CostAmountSnapshot.Value;
        }

        var baseQuantity = item.BaseQuantity > 0m ? item.BaseQuantity : item.Quantity;
        return RoundCurrency(ResolveOriginalSaleLineCostPriceSnapshot(item) * baseQuantity);
    }

    private static decimal ResolveReturnCostAmountSnapshot(decimal costPriceSnapshot, decimal baseQuantity) =>
        RoundCurrency(costPriceSnapshot * baseQuantity);

    private static OriginalTenderCapacity CalculateRemainingTenderCapacity(
        SalesInvoice originalInvoice,
        IReadOnlyCollection<Return> previousReturns)
    {
        if (previousReturns.Any(previousReturn => !previousReturn.RefundMethod.HasValue))
        {
            return new OriginalTenderCapacity(0m, 0m, true);
        }

        var paidAmount = Math.Max(0m, Math.Min(
            RoundCurrency(originalInvoice.PaidAmount),
            RoundCurrency(originalInvoice.TotalAmount)));
        var explicitCashAmount = Math.Max(0m, RoundCurrency(originalInvoice.CashAmount ?? 0m));
        var explicitCardAmount = Math.Max(0m, RoundCurrency(originalInvoice.CardAmount ?? 0m));

        decimal originalCashAmount;
        decimal originalCardAmount;
        switch (originalInvoice.PaymentMethod)
        {
            case PaymentMethod.Cash:
                originalCashAmount = explicitCashAmount > 0m
                    ? Math.Min(explicitCashAmount, paidAmount)
                    : paidAmount;
                originalCardAmount = 0m;
                break;
            case PaymentMethod.Card:
                originalCashAmount = 0m;
                originalCardAmount = explicitCardAmount > 0m
                    ? Math.Min(explicitCardAmount, paidAmount)
                    : paidAmount;
                break;
            case PaymentMethod.Mixed:
                // Card settlement is exact; any over-tender/change belongs to the cash leg.
                originalCardAmount = Math.Min(explicitCardAmount, paidAmount);
                originalCashAmount = Math.Min(
                    explicitCashAmount,
                    Math.Max(0m, paidAmount - originalCardAmount));
                break;
            default:
                originalCashAmount = 0m;
                originalCardAmount = 0m;
                break;
        }

        var priorCashRefunds = RoundCurrency(previousReturns.Sum(previousReturn => previousReturn.CashRefundAmount));
        var priorCardRefunds = RoundCurrency(previousReturns.Sum(previousReturn => previousReturn.CardRefundAmount));
        return new OriginalTenderCapacity(
            Math.Max(0m, RoundCurrency(originalCashAmount - priorCashRefunds)),
            Math.Max(0m, RoundCurrency(originalCardAmount - priorCardRefunds)),
            false);
    }

    private static string? ValidateRefundAgainstOriginalTender(
        SalesInvoice originalInvoice,
        RefundAllocation allocation,
        IReadOnlyCollection<Return> previousReturns)
    {
        if (allocation.Method == RefundMethod.AccountCredit)
        {
            return null;
        }

        var capacity = CalculateRemainingTenderCapacity(originalInvoice, previousReturns);
        if (capacity.HasLegacySettlementGap)
        {
            return "Immediate payout is unavailable because a previous return has no settlement snapshot. Use AccountCredit or reconcile the legacy return first.";
        }

        if (allocation.CashAmount > capacity.RemainingCashAmount)
        {
            return $"Cash refund exceeds the remaining cash collected on the original invoice ({capacity.RemainingCashAmount:F2}).";
        }

        if (allocation.CardAmount > capacity.RemainingCardAmount)
        {
            return $"Card refund exceeds the remaining card amount collected on the original invoice ({capacity.RemainingCardAmount:F2}).";
        }

        return null;
    }

    private static string BuildSalesReturnRequestFingerprint(CreateSalesReturnRequest request)
    {
        return DeterministicRequestHasher.ComputeHash(new
        {
            request.CustomerId,
            request.WarehouseId,
            ReturnDate = NormalizeToUtc(request.ReturnDate),
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
            Notes = request.Notes?.Trim(),
            request.RefundMethod
        });
    }

    private static ActionResult<SalesReturnResponse> BuildStoredSalesReturnResponse(SalesRequestIdempotency record)
    {
        var statusCode = record.ResponseStatusCode ?? StatusCodes.Status200OK;

        if (string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            return new StatusCodeResult(statusCode);
        }

        var payload = JsonSerializer.Deserialize<SalesReturnResponse>(
            record.ResponseJson,
            SalesReturnIdempotencyJsonOptions)
            ?? throw new InvalidOperationException(
                $"Stored sales return response for idempotency record {record.Id} is empty.");
        return new ObjectResult(payload)
        {
            StatusCode = statusCode,
        };
    }

    private static string SerializeSalesReturnPayload(SalesReturnResponse response) =>
        JsonSerializer.Serialize(response, SalesReturnIdempotencyJsonOptions);

    private readonly record struct OriginalInvoiceSnapshotKey(Guid ProductId, Guid EffectiveVariantId);

    private sealed record OriginalInvoiceLineSnapshot(
        OriginalInvoiceSnapshotKey Key,
        Guid ProductId,
        Guid EffectiveVariantId,
        Guid? UnitId,
        decimal ConversionFactor,
        decimal OriginalQuantity,
        decimal OriginalBaseQuantity,
        decimal UnitPrice,
        decimal TaxRate,
        decimal LineDiscountAmount,
        decimal LineTaxAmount,
        decimal OriginalLineTotalAmount,
        decimal CostPriceSnapshot,
        decimal OriginalCostAmountSnapshot,
        string ProductName);

    private sealed record PendingReturnLine(
        OriginalInvoiceLineSnapshot Snapshot,
        decimal Quantity,
        decimal BaseQuantity,
        decimal GrossSubtotal,
        decimal LineDiscountAmount,
        decimal LineTaxAmount,
        string? Reason);

    private sealed record OriginalTenderCapacity(
        decimal RemainingCashAmount,
        decimal RemainingCardAmount,
        bool HasLegacySettlementGap);

    private sealed record OriginalInvoiceSnapshotBuildResult(
        bool Success,
        string? ErrorMessage,
        Dictionary<OriginalInvoiceSnapshotKey, OriginalInvoiceLineSnapshot>? Snapshots,
        Dictionary<Guid, Guid> DefaultVariantIds)
    {
        public static OriginalInvoiceSnapshotBuildResult Ok(
            Dictionary<OriginalInvoiceSnapshotKey, OriginalInvoiceLineSnapshot> snapshots,
            Dictionary<Guid, Guid> defaultVariantIds)
        {
            return new OriginalInvoiceSnapshotBuildResult(true, null, snapshots, defaultVariantIds);
        }

        public static OriginalInvoiceSnapshotBuildResult Fail(string errorMessage)
        {
            return new OriginalInvoiceSnapshotBuildResult(
                false,
                errorMessage,
                null,
                new Dictionary<Guid, Guid>());
        }
    }

    private sealed record SalesReturnInvoiceReadModelResult(
        bool Success,
        string? ErrorMessage,
        SalesReturnInvoiceResponseDto? Response)
    {
        public static SalesReturnInvoiceReadModelResult Ok(SalesReturnInvoiceResponseDto response) =>
            new(true, null, response);

        public static SalesReturnInvoiceReadModelResult Fail(string errorMessage) =>
            new(false, errorMessage, null);
    }

    private async Task MarkSynchronousRefundAccountingOutboxProcessedAsync(
        Guid tenantId,
        Guid returnId,
        Guid? journalId,
        CancellationToken cancellationToken)
    {
        if (!journalId.HasValue || journalId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Synchronous refund accounting post for return {returnId} did not return a journal id.");
        }

        var message = await _context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate =>
                    candidate.TenantId == tenantId &&
                    candidate.BusinessEntityId == returnId &&
                    candidate.EventType == "PosRefund" &&
                    candidate.IdempotencyKey == $"REFUND:{tenantId}:{returnId}",
                cancellationToken);

        if (message == null)
        {
            throw new InvalidOperationException(
                $"Synchronous refund accounting post for return {returnId} has no matching outbox row.");
        }

        if (message.ProcessedAtUtc.HasValue)
        {
            if (message.JournalId != journalId.Value)
            {
                throw new InvalidOperationException(
                    $"Synchronous refund accounting outbox row for return {returnId} was already processed with a different journal.");
            }

            return;
        }

        if (message.IsFailed ||
            (message.ClaimedUntilUtc.HasValue && message.ClaimedUntilUtc.Value > DateTime.UtcNow))
        {
            throw new InvalidOperationException(
                $"Synchronous refund accounting outbox row for return {returnId} is failed or claimed and cannot be completed by the request executor.");
        }

        message.MarkProcessed(journalId.Value);
    }

    private async Task<bool> TryCompleteSynchronousRefundAccountingAsync(
        Return salesReturn,
        RefundAllocation refundAllocation,
        string returnNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            var refundPosting = await _commerceAccounting.PostPosRefundAsync(
                salesReturn,
                refundAllocation,
                cancellationToken);
            if (!refundPosting.Success)
            {
                throw new InvalidOperationException(
                    $"POS refund accounting post failed for return {returnNumber}: {refundPosting.ErrorMessage}");
            }

            await MarkSynchronousRefundAccountingOutboxProcessedAsync(
                salesReturn.TenantId,
                salesReturn.Id,
                refundPosting.JournalId,
                cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Sales return {ReturnNumber} was committed but synchronous refund accounting completion failed. Leaving the outbox row pending for retry/reconciliation. | ReturnId: {ReturnId} | TenantId: {TenantId} | TraceId: {TraceId}",
                returnNumber,
                salesReturn.Id,
                salesReturn.TenantId,
                GetTraceId());
            return false;
        }
    }
}

// DTOs
public record CreateSalesReturnRequest
{
    public Guid? CustomerId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateTime ReturnDate { get; set; }
    public Guid? OriginalInvoiceId { get; set; }
    [Required]
    [MinLength(1)]
    [MaxLength(SalesReturnsController.MAX_SALES_RETURN_ITEMS)]
    public List<SalesReturnItemRequest> Items { get; set; } = new();
    [MaxLength(500)]
    public string? Reason { get; set; }
    [MaxLength(1000)]
    public string? Notes { get; set; }
    public RefundMethod? RefundMethod { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? CashAmount { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? CardAmount { get; set; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public record SalesReturnItemRequest
{
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public Guid? UnitId { get; set; } // Unit for the return
    [Range(typeof(decimal), "0.000001", "79228162514264337593543950335")]
    public decimal Quantity { get; set; } // Quantity in the selected unit
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? UnitPrice { get; set; }
    [Range(typeof(decimal), "0", "100")]
    public decimal? TaxRate { get; set; }
    [MaxLength(500)]
    public string? Reason { get; set; }
}

public record SalesReturnResponse
{
    public Guid ReturnId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal RefundAmount { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RefundMethod RefundMethod { get; set; }
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal CustomerCreditAmount { get; set; }
    public bool IsAutoCalculated { get; set; }
    public string Message { get; set; } = string.Empty;
}


/// <summary>
/// Custom JSON converter that handles null or missing RefundMethod values during deserialization
/// by defaulting to AccountCredit. This ensures backward compatibility with older cached
/// JSON that may not have the RefundMethod property.
/// </summary>
internal sealed class NullableRefundMethodConverter : JsonConverter<RefundMethod>
{
    public override RefundMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Handle null or missing values by returning the default (AccountCredit = 0)
        if (reader.TokenType == JsonTokenType.Null)
        {
            return RefundMethod.AccountCredit;
        }

        // Handle numeric values
        if (reader.TokenType == JsonTokenType.Number)
        {
            var value = reader.GetInt32();
            if (Enum.IsDefined(typeof(RefundMethod), value))
            {
                return (RefundMethod)value;
            }
            // Invalid enum value, default to AccountCredit
            return RefundMethod.AccountCredit;
        }

        // Handle string values
        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (string.IsNullOrWhiteSpace(stringValue))
            {
                return RefundMethod.AccountCredit;
            }

            if (Enum.TryParse<RefundMethod>(stringValue, ignoreCase: true, out var result))
            {
                return result;
            }
            // Invalid string value, default to AccountCredit
            return RefundMethod.AccountCredit;
        }

        // Unexpected token type, default to AccountCredit
        return RefundMethod.AccountCredit;
    }

    public override void Write(Utf8JsonWriter writer, RefundMethod value, JsonSerializerOptions options)
    {
        // Write as numeric value
        writer.WriteNumberValue((int)value);
    }
}
