using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Infrastructure.DocumentNumbers;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Pos;
using PointOfSale.API.Services.Inventory;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;
using PointOfSale.API.DTOs;
using PointOfSale.API.Mappers;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SalesController : ControllerBase
{
    private const string PaymentAttemptIdHeader = "X-Payment-Attempt-Id";
    private const string PosCartIdHeader = "X-Pos-Cart-Id";
    private const int MaxSalesIdempotencyKeyLength = 100;
    private const string SalesInvoiceNumberSequenceName = "PosSalesInvoiceNumberSeq";
    private static readonly Regex SalesIdempotencyKeyPattern = new(
        "^[A-Za-z0-9-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // Keep hashing and replay storage on one deterministic serializer configuration.
    private static readonly JsonSerializerOptions SalesIdempotencyJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<SalesController> _logger;
    private readonly PointOfSale.API.Services.ILedgerService _ledgerService;
    private readonly PointOfSale.API.Services.IPartyBalanceMaintenanceService _partyBalanceMaintenanceService;
    private readonly PointOfSale.API.Services.ISaleFinancialIntegrityService _saleFinancialIntegrityService;
    private readonly PointOfSale.API.Services.IValidationService _validationService;
    private readonly PointOfSale.API.Services.Audit.IAuditService _auditService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IAccountingOutboxService _accountingOutbox;
    private readonly ICommerceAccountingService _commerceAccounting;
    private readonly IFinancialPostingService _postingService;
    private readonly IPosCartRuntimeService? _posCartRuntimeService;

    private bool IsPostgresProvider() => _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    private bool IsSqlServerProvider() => _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    public SalesController(
        ApplicationDbContext context, 
        ITenantContext tenantContext,
        ILogger<SalesController> logger, 
        PointOfSale.API.Services.ILedgerService ledgerService,
        PointOfSale.API.Services.IPartyBalanceMaintenanceService partyBalanceMaintenanceService,
        PointOfSale.API.Services.ISaleFinancialIntegrityService saleFinancialIntegrityService,
        PointOfSale.API.Services.IValidationService validationService,
        PointOfSale.API.Services.Audit.IAuditService auditService,
        IEventPublisher eventPublisher,
        IAccountingOutboxService accountingOutbox,
        ICommerceAccountingService commerceAccounting,
        IFinancialPostingService postingService,
        IPosCartRuntimeService? posCartRuntimeService = null)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
        _ledgerService = ledgerService;
        _partyBalanceMaintenanceService = partyBalanceMaintenanceService;
        _saleFinancialIntegrityService = saleFinancialIntegrityService;
        _validationService = validationService;
        _auditService = auditService;
        _eventPublisher = eventPublisher;
        _accountingOutbox = accountingOutbox;
        _commerceAccounting = commerceAccounting;
        _postingService = postingService;
        _posCartRuntimeService = posCartRuntimeService;
    }

    /// <summary>
    /// Create a new sale
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SaleResponse>> CreateSale(
        CreateSaleRequest request,
        [FromHeader(Name = PaymentAttemptIdHeader)] string? paymentAttemptId = null,
        [FromHeader(Name = PosCartIdHeader)] string? posCartId = null)
    {
        // Validate payment attempt ID only if provided
        string? normalizedPaymentAttemptId = null;
        if (!string.IsNullOrWhiteSpace(paymentAttemptId))
        {
            normalizedPaymentAttemptId = ValidateSalesIdempotencyKey(paymentAttemptId);
            if (normalizedPaymentAttemptId == null)
            {
                return BadRequest(new { message = "Invalid idempotency key" });
            }
        }

        AddPaymentAttemptHeader(normalizedPaymentAttemptId);
        var normalizedPosCartId = ValidateOptionalSalesIdempotencyKey(posCartId);
        if (!string.IsNullOrWhiteSpace(posCartId) && normalizedPosCartId == null)
        {
            return BadRequest(new { message = "Invalid POS cart id" });
        }

        AddPosCartIdHeader(normalizedPosCartId);
        var createIdempotencyKey = normalizedPosCartId ?? normalizedPaymentAttemptId;

        // Require at least one idempotency key
        if (createIdempotencyKey == null)
        {
            return BadRequest(new { message = "Either X-Payment-Attempt-Id or X-Pos-Cart-Id header is required" });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        decimal? calculatedTotalAmount = null;
        SalesRequestIdempotency? idempotencyRecord = null;
        var pendingStockChanges = new List<PendingProductVariantStockChange>();
        Guid? checkoutCartId = null;
        Guid? checkoutCashierId = null;
        
        try
        {
            if (Guid.TryParse(normalizedPosCartId, out var parsedCartId) && _posCartRuntimeService != null)
            {
                if (!TryResolveCurrentUserId(out var cashierId))
                {
                    await transaction.RollbackAsync();
                    return Unauthorized(new { message = "Authenticated cashier identity is required." });
                }

                var cartCheckout = await _posCartRuntimeService.PrepareCheckoutAsync(
                    parsedCartId,
                    request,
                    cashierId,
                    HttpContext.RequestAborted);
                if (!cartCheckout.Succeeded)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(cartCheckout.StatusCode, new { message = cartCheckout.Message });
                }

                request = cartCheckout.Request;
                checkoutCartId = parsedCartId;
                checkoutCashierId = cashierId;
            }

            var requestHash = BuildSalesRequestHash("CREATE", BuildCreateSaleIdempotencyPayload(request));

            // Claim, execute, and persist the replay response inside one transaction so retries never see partial state.
            var claim = await TryClaimSalesIdempotencyAsync("CREATE", createIdempotencyKey, requestHash);
            if (claim.ShortCircuitResult != null)
            {
                await transaction.RollbackAsync();
                return claim.ShortCircuitResult;
            }

            idempotencyRecord = claim.Record!;
            EnsureSupportedPaymentMethod(request.PaymentMethod);

            // Validate warehouse exists
            var warehouse = await LoadActiveWarehouseAsync(request.WarehouseId);
            if (warehouse == null)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Warehouse not found" });
            }

            // Validate customer if provided
            Customer? customer = null;
            if (request.CustomerId.HasValue)
            {
                customer = await LoadActiveCustomerAsync(request.CustomerId.Value);
                if (customer == null)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = "Customer not found" });
                }
            }

            // Generate invoice number
            var invoiceNumber = await GenerateInvoiceNumberAsync();

            // Create sales invoice
            var invoice = new SalesInvoice
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                InvoiceNumber = invoiceNumber,
                InvoiceDate = DateTime.UtcNow,
                CustomerId = request.CustomerId,
                WarehouseId = request.WarehouseId,
                SalesRepresentativeId = request.SalesRepresentativeId,
                Status = InvoiceStatus.Completed,
                PaymentMethod = request.PaymentMethod,
                CashAmount = request.CashAmount,
                CardAmount = request.CardAmount,
                Notes = request.Notes,
                CreatedAt = DateTime.UtcNow
            };

            // Add invoice items and calculate totals
            decimal subtotal = 0;
            decimal totalTax = 0;
            decimal lineDiscountTotal = 0;
            var preparedSaleItemsResult = await PrepareSaleItemsAsync(request.Items);
            if (preparedSaleItemsResult.ErrorResult != null)
            {
                await transaction.RollbackAsync();
                return preparedSaleItemsResult.ErrorResult;
            }

            var preparedSaleItems = preparedSaleItemsResult.Items!;
            var lockedStocks = await ProductVariantStockMutationSupport.LockMultipleStocksAsync(
                _context,
                _tenantContext.RequiredTenantId,
                preparedSaleItems
                    .Select(item => new StockLockTarget(request.WarehouseId, item.Variant.Id, CreateIfMissing: false))
                    .DistinctBy(target => (target.WarehouseId, target.VariantId)));

            foreach (var preparedItem in preparedSaleItems)
            {
                var itemRequest = preparedItem.Request;
                var product = preparedItem.Product;
                var variant = preparedItem.Variant;
                var unitId = preparedItem.UnitId;
                var conversionFactor = preparedItem.ConversionFactor;
                var baseQuantity = preparedItem.BaseQuantity;
                var variantStock = lockedStocks.GetValueOrDefault((request.WarehouseId, variant.Id));

                if (variantStock == null)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new
                    {
                        message = $"Insufficient stock for {product.Name} ({variant.SKU ?? "default"}). Available: 0 (base units), Requested: {baseQuantity} (base units)"
                    });
                }

                ProductVariantStockMutationSupport.ValidateAvailableQuantity(
                    variantStock,
                    baseQuantity,
                    allowNegativeStock: false,
                    operationName: "Sale");

                var itemSubtotal = itemRequest.Quantity * itemRequest.UnitPrice;
                var itemDiscount = itemRequest.DiscountAmount;
                var taxableAmount = itemSubtotal - itemDiscount;
                var itemTax = product.PriceIncludesTax ? 0 : (taxableAmount * itemRequest.TaxRate / 100);
                var itemTotal = taxableAmount + itemTax;
                var costPriceSnapshot = ResolveSaleCostPriceSnapshot(variant);
                var costAmountSnapshot = ResolveSaleCostAmountSnapshot(costPriceSnapshot, baseQuantity);

                var invoiceItem = new SalesInvoiceItem
                {
                    Id = Guid.NewGuid(),
                    SalesInvoiceId = invoice.Id,
                    ProductId = itemRequest.ProductId,
                    ProductVariantId = variant.Id,
                    UnitId = unitId,
                    Quantity = itemRequest.Quantity,
                    ConversionFactor = conversionFactor,
                    BaseQuantity = baseQuantity,
                    UnitPrice = itemRequest.UnitPrice,
                    DiscountAmount = itemDiscount,
                    TaxRate = itemRequest.TaxRate,
                    TaxAmount = itemTax,
                    TotalAmount = itemTotal,
                    CostPriceSnapshot = costPriceSnapshot,
                    CostAmountSnapshot = costAmountSnapshot,
                    // Only set foreign key IDs, not navigation properties
                    // EF Core will handle navigation properties automatically
                    CreatedAt = DateTime.UtcNow
                };

                invoice.Items.Add(invoiceItem);
                subtotal += itemSubtotal;
                lineDiscountTotal += itemDiscount;
                totalTax += itemTax;

                var oldQuantity = variantStock.Quantity;
                variantStock.Quantity -= baseQuantity;

                _context.StockMovements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.RequiredTenantId,
                    WarehouseId = request.WarehouseId,
                    ProductId = itemRequest.ProductId,
                    ProductVariantId = variant.Id,
                    MovementType = StockMovementType.Sale,
                    UnitId = unitId,
                    ConversionFactor = conversionFactor,
                    QuantityChange = -baseQuantity,
                    QuantityAfter = variantStock.Quantity,
                    AvailableAfter = variantStock.AvailableQuantity,
                    ReservedAfter = variantStock.ReservedQuantity,
                    DamagedAfter = variantStock.DamagedQuantity,
                    ReferenceId = invoice.Id,
                    ReferenceLineId = invoiceItem.Id,
                    ReferenceNumber = invoiceNumber,
                    Price = costPriceSnapshot,
                    CreatedAt = DateTime.UtcNow
                });

                pendingStockChanges.Add(ProductVariantStockMutationSupport.BuildPendingStockChange(
                    variant.Id,
                    itemRequest.ProductId,
                    request.WarehouseId,
                    oldQuantity,
                    variantStock.Quantity,
                    "Sale",
                    referenceId: invoice.Id,
                    referenceType: "SalesInvoice",
                    sku: variant.SKU,
                    warehouseName: warehouse.Name));
            }

            ApplySaleTotals(
                invoice,
                subtotal,
                lineDiscountTotal,
                totalTax,
                request);
            calculatedTotalAmount = RoundCurrency(invoice.TotalAmount);
            var paymentAmounts = ValidateAndNormalizePaymentAmounts(
                request.PaymentMethod,
                invoice.TotalAmount,
                request.PaidAmount,
                request.CashAmount,
                request.CardAmount);
            invoice.PaidAmount = paymentAmounts.PaidAmount;
            invoice.CashAmount = paymentAmounts.CashAmount;
            invoice.CardAmount = paymentAmounts.CardAmount;

            _context.SalesInvoices.Add(invoice);

            // Create ledger entries for customer sales (if customer is selected)
            if (customer != null)
            {
                // Always create ledger entry for the full sale amount (customer owes us)
                await _ledgerService.CreateLedgerEntry(
                    Core.Enums.PartyType.Customer,
                    customer.Id,
                    DateTime.UtcNow,
                    TransactionType.Sale,
                    invoiceNumber,
                    invoice.TotalAmount, // Debit - customer owes us the full amount
                    0,
                    invoice.Id,
                    $"Sale Invoice {invoiceNumber}");

                // IMPORTANT: BalanceAfter must be assigned AFTER CreateLedgerEntry
                // because LedgerService updates customer.Balance internally
                var saleTransaction = new CustomerTransaction
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                    CustomerId = customer.Id,
                    TransactionDate = DateTime.UtcNow,
                    TransactionType = TransactionType.Sale,
                    DebitAmount = invoice.TotalAmount,
                    CreditAmount = 0,
                    ReferenceId = invoice.Id,
                    ReferenceNumber = invoiceNumber,
                    CreatedAt = DateTime.UtcNow,
                    BalanceAfter = customer.Balance // Set AFTER ledger entry to capture correct balance
                };
                _context.CustomerTransactions.Add(saleTransaction);

                // If payment was made, record it as a separate transaction
                var customerLedgerPaymentAmount = ResolveCustomerLedgerPaymentAmount(invoice);
                if (customerLedgerPaymentAmount > 0)
                {
                    var paymentNote = $"Payment for Sale Invoice {invoiceNumber} - {invoice.PaymentMethod}";
                    if (invoice.PaymentMethod == PaymentMethod.Mixed)
                    {
                        paymentNote += $" (Cash: ${invoice.CashAmount:F2}, Card: ${invoice.CardAmount:F2})";
                    }
                    
                    await _ledgerService.CreateLedgerEntry(
                        Core.Enums.PartyType.Customer,
                        customer.Id,
                        DateTime.UtcNow,
                        TransactionType.PaymentReceived,
                        invoiceNumber,
                        0,
                        customerLedgerPaymentAmount, // Credit - reduces receivable, excluding returned cash change
                        invoice.Id,
                        paymentNote);

                    // IMPORTANT: BalanceAfter must be assigned AFTER CreateLedgerEntry
                    // because LedgerService updates customer.Balance internally
                    var paymentTransaction = new CustomerTransaction
                    {
                        Id = Guid.NewGuid(),
                        TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                        CustomerId = customer.Id,
                        TransactionDate = DateTime.UtcNow,
                        TransactionType = TransactionType.PaymentReceived,
                        DebitAmount = 0,
                        CreditAmount = customerLedgerPaymentAmount,
                        ReferenceId = invoice.Id,
                        ReferenceNumber = invoiceNumber,
                        CreatedAt = DateTime.UtcNow,
                        BalanceAfter = customer.Balance // Set AFTER ledger entry to capture correct balance
                    };
                    _context.CustomerTransactions.Add(paymentTransaction);
                }
            }

            // ════════════════════════════════════════════════════════════════════════════
            // PHASE LCR: Stage the sale accounting outbox row inside the sale transaction.
            // The synchronous post below is the executor for this request; after it succeeds,
            // the matching outbox row is marked processed in the same transaction so the
            // background worker remains a recovery path, not a second financial authority.
            // ════════════════════════════════════════════════════════════════════════════
            await _accountingOutbox.EnqueuePosSaleAsync(
                invoice.TenantId,
                invoice.Id,
                invoice.AccountingMutationVersion);

            var salePosting = await _commerceAccounting.PostPosSaleAsync(invoice);
            if (!salePosting.Success)
            {
                throw new InvalidOperationException(
                    $"POS sale accounting post failed for invoice {invoice.InvoiceNumber}: {salePosting.ErrorMessage}");
            }

            await MarkSynchronousSaleAccountingOutboxProcessedAsync(
                invoice.TenantId,
                invoice.Id,
                "PosSale",
                BuildBaseSaleAccountingOutboxIdempotencyKey(invoice.TenantId, invoice.Id),
                invoice.AccountingMutationVersion,
                salePosting.JournalId,
                HttpContext.RequestAborted);
            
            // DO NOT mutate balances directly - sales posting is handled through the outbox and ledger.

            if (pendingStockChanges.Count > 0)
            {
                await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                    _context,
                    _tenantContext.RequiredTenantId,
                    pendingStockChanges);
            }

            if (checkoutCartId.HasValue && checkoutCashierId.HasValue && _posCartRuntimeService != null)
            {
                await _posCartRuntimeService.CompleteFromSaleAsync(
                    checkoutCartId.Value,
                    invoice.Id,
                    checkoutCashierId.Value,
                    HttpContext.RequestAborted);
            }

            var result = await CompleteSalesIdempotencyAsync(
                transaction,
                idempotencyRecord,
                Ok(new SaleResponse
                {
                    InvoiceId = invoice.Id,
                    InvoiceNumber = invoiceNumber,
                    TotalAmount = RoundCurrency(invoice.TotalAmount),
                    PaidAmount = RoundCurrency(invoice.PaidAmount),
                    ChangeAmount = RoundCurrency(invoice.PaidAmount - invoice.TotalAmount),
                    Message = "Sale completed successfully"
                }));

            try
            {
                await _auditService.LogCreate("SalesInvoice", invoice.Id.ToString(), $"Created Sale Invoice {invoiceNumber}", new { TotalAmount = invoice.TotalAmount, CustomerId = invoice.CustomerId }, invoiceNumber);
            }
            catch (Exception auditEx)
            {
                _logger.LogWarning(
                    auditEx,
                    "Sales audit logging failed after successful commit for invoice {InvoiceId} tenant {TenantId}",
                    invoice.Id,
                    _tenantContext.RequiredTenantId);
            }

            return result;
        }
        catch (ValidationException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex,
                "Validation error creating sale for key {Key} tenant {TenantId}. PaymentMethod: {PaymentMethod}, TotalAmount: {TotalAmount}, PaidAmount: {PaidAmount}, CashAmount: {CashAmount}, CardAmount: {CardAmount}",
                createIdempotencyKey,
                _tenantContext.RequiredTenantId,
                request.PaymentMethod,
                calculatedTotalAmount,
                request.PaidAmount,
                request.CashAmount,
                request.CardAmount);
            _context.ChangeTracker.Clear();
            return BadRequest(new { message = "Sale request contains invalid payment or pricing data." });
        }
        catch (InsufficientStockException ex)
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            _logger.LogWarning(
                ex,
                "Insufficient stock creating sale for key {Key} tenant {TenantId}. VariantId: {VariantId}, WarehouseId: {WarehouseId}, Available: {AvailableQuantity}, Requested: {RequestedQuantity}",
                createIdempotencyKey,
                _tenantContext.RequiredTenantId,
                ex.VariantId,
                ex.WarehouseId,
                ex.AvailableQuantity,
                ex.RequestedQuantity);
            return Conflict(new
            {
                message = "Insufficient stock",
                availableStock = ex.AvailableQuantity,
                requestedQuantity = ex.RequestedQuantity
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            _logger.LogError(
                ex,
                "Error creating sale for key {Key} tenant {TenantId}",
                createIdempotencyKey,
                _tenantContext.RequiredTenantId);
            return StatusCode(500, new { message = "An error occurred while processing the sale" });
        }
    }

    private async Task<(SalesRequestIdempotency? Record, ActionResult<SaleResponse>? ShortCircuitResult)> TryClaimSalesIdempotencyAsync(
        string operation,
        string key,
        string requestHash)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        const int staleClaimMinutes = 5; // Configurable staleness cutoff

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var claimId = Guid.NewGuid();
            var claimToken = Guid.NewGuid().ToString("N"); // Generate unique claim token
            var nowUtc = DateTime.UtcNow;

            // SECURITY: Check if the current key is already bound to a different payload
            // BEFORE attempting cross-key hash recovery to prevent a reused key from
            // wrongly resolving to another completed row.
            var existingRecord = await _context.SalesRequestIdempotencyRecords
                // SECURITY: IgnoreQueryFilters is intentional so soft-deleted or
                // stale claims can be reconciled, while the tenant/key/operation
                // predicates are reapplied immediately below.
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(record =>
                    record.TenantId == tenantId &&
                    record.Key == key &&
                    record.Operation == operation);

            // If the record is soft-deleted, we can reclaim the key
            if (existingRecord?.IsDeleted == true)
            {
                _logger.LogInformation(
                    "Idempotency RECLAIMING soft-deleted key {Key} tenant {TenantId} operation {Operation}",
                    key,
                    tenantId,
                    operation);

                // Hard delete the soft-deleted record so we can insert a new one
                await DeleteSalesIdempotencyRecordByIdAsync(existingRecord.Id);

                continue;
            }

            // If key exists and is bound to a different payload, reject immediately
            if (existingRecord != null && !string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Idempotency CONFLICT key {Key} tenant {TenantId} operation {Operation}",
                    key,
                    tenantId,
                    operation);

                return (null, BadRequest(new
                {
                    message = "Idempotency key reuse with different payload"
                }));
            }

            // If key exists with matching payload and has a response, return it
            if (existingRecord?.ResponseStatusCode.HasValue == true)
            {
                _logger.LogInformation(
                    "Idempotency HIT key {Key} tenant {TenantId} operation {Operation}",
                    key,
                    tenantId,
                    operation);

                return (null, BuildStoredSaleResponse(existingRecord));
            }

            // Now that we've verified the current key binding, attempt cross-key hash recovery for UPDATE operations
            if (operation == "UPDATE" && existingRecord == null)
            {
                var recoveredByHash = await TryRecoverSalesUpdateIdempotencyByRequestHashAsync(
                    key,
                    requestHash,
                    nowUtc,
                    staleClaimMinutes);

                if (recoveredByHash != null)
                {
                    return (null, recoveredByHash);
                }
            }

            var rowsInserted = await TryInsertSalesIdempotencyClaimAsync(
                claimId,
                tenantId,
                key,
                operation,
                requestHash,
                claimToken,
                nowUtc);

            if (rowsInserted > 0)
            {
                _logger.LogInformation(
                    "Idempotency NEW key {Key} tenant {TenantId} operation {Operation} claimToken {ClaimToken}",
                    key,
                    tenantId,
                    operation,
                    claimToken);

                var claimedRecord = await _context.SalesRequestIdempotencyRecords
                    .FirstAsync(record =>
                        record.TenantId == tenantId &&
                        record.Key == key &&
                        record.Operation == operation);

                return (claimedRecord, null);
            }

            // Re-fetch existing record after failed insert (may have been created by concurrent request)
            existingRecord = await _context.SalesRequestIdempotencyRecords
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(record =>
                    record.TenantId == tenantId &&
                    record.Key == key &&
                    record.Operation == operation);

            if (existingRecord == null)
            {
                continue;
            }

            // Verify payload match again after re-fetch
            if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Idempotency CONFLICT key {Key} tenant {TenantId} operation {Operation}",
                    key,
                    tenantId,
                    operation);

                return (null, BadRequest(new
                {
                    message = "Idempotency key reuse with different payload"
                }));
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                _logger.LogInformation(
                    "Idempotency HIT key {Key} tenant {TenantId} operation {Operation}",
                    key,
                    tenantId,
                    operation);

                return (null, BuildStoredSaleResponse(existingRecord));
            }

            // Check if the existing claim is stale (older than cutoff)
            var claimAge = nowUtc - (existingRecord.ClaimTimestampUtc ?? existingRecord.CreatedAt);
            if (claimAge.TotalMinutes < staleClaimMinutes)
            {
                _logger.LogWarning(
                    "Idempotency CONFLICT key {Key} tenant {TenantId} operation {Operation} - active claim exists (age: {AgeSeconds}s, cutoff: {CutoffMinutes}m)",
                    key,
                    tenantId,
                    operation,
                    claimAge.TotalSeconds,
                    staleClaimMinutes);

                return (null, Conflict(new
                {
                    conflictCode = "SALE_IN_PROGRESS",
                    message = "A sale request with the same payment attempt id is already being processed."
                }));
            }

            _logger.LogWarning(
                "Sales idempotency stale claim detected for key {Key} tenant {TenantId} operation {Operation} (age: {AgeMinutes}m); reclaiming key",
                key,
                tenantId,
                operation,
                claimAge.TotalMinutes);

            var rowsDeleted = await DeleteStaleSalesIdempotencyClaimAsync(tenantId, key, operation, staleClaimMinutes);

            if (rowsDeleted > 0)
            {
                continue;
            }

            _logger.LogWarning(
                "Idempotency CONFLICT key {Key} tenant {TenantId} operation {Operation} - could not reclaim stale claim",
                key,
                tenantId,
                operation);

            return (null, Conflict(new
            {
                conflictCode = "SALE_IN_PROGRESS",
                message = "A sale request with the same payment attempt id is already being processed."
            }));
        }

        _logger.LogWarning(
            "Idempotency CONFLICT key {Key} tenant {TenantId} operation {Operation} - max retry attempts exceeded",
            key,
            tenantId,
            operation);

        return (null, Conflict(new
        {
            conflictCode = "SALE_IN_PROGRESS",
            message = "A sale request with the same payment attempt id is already being processed."
        }));
    }

    private async Task<ActionResult<SaleResponse>?> TryRecoverSalesUpdateIdempotencyByRequestHashAsync(
        string key,
        string requestHash,
        DateTime nowUtc,
        int staleClaimMinutes)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var existingRecord = await _context.SalesRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(record =>
                record.TenantId == tenantId &&
                record.Operation == "UPDATE" &&
                record.RequestHash == requestHash &&
                !record.IsDeleted)
            .OrderByDescending(record => record.CompletedAtUtc.HasValue)
            .ThenByDescending(record => record.CompletedAtUtc)
            .ThenByDescending(record => record.CreatedAt)
            .FirstOrDefaultAsync();

        if (existingRecord == null || existingRecord.Key == key)
        {
            return null;
        }

        if (existingRecord.ResponseStatusCode.HasValue)
        {
            _logger.LogInformation(
                "Idempotency HASH HIT key {Key} recovered from key {ExistingKey} tenant {TenantId} operation UPDATE",
                key,
                existingRecord.Key,
                tenantId);

            return BuildStoredSaleResponse(existingRecord);
        }

        var claimAge = nowUtc - (existingRecord.ClaimTimestampUtc ?? existingRecord.CreatedAt);
        if (claimAge.TotalMinutes < staleClaimMinutes)
        {
            _logger.LogWarning(
                "Idempotency HASH CONFLICT key {Key} tenant {TenantId} operation UPDATE - active claim {ExistingKey} has same request hash (age: {AgeSeconds}s)",
                key,
                tenantId,
                existingRecord.Key,
                claimAge.TotalSeconds);

            return Conflict(new
            {
                conflictCode = "SALE_IN_PROGRESS",
                message = "A sale update with the same payload is already being processed."
            });
        }

        return null;
    }

    private async Task<int> TryInsertSalesIdempotencyClaimAsync(
        Guid claimId,
        Guid tenantId,
        string key,
        string operation,
        string requestHash,
        string claimToken,
        DateTime nowUtc)
    {
        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""SalesRequestIdempotency""
                (""Id"", ""TenantId"", ""Key"", ""Operation"", ""RequestHash"", ""ClaimToken"", ""ClaimTimestampUtc"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES
                ({claimId}, {tenantId}, {key}, {operation}, {requestHash}, {claimToken}, {nowUtc}, {nowUtc}, {nowUtc}, {false})
                ON CONFLICT (""TenantId"", ""Key"", ""Operation"") DO NOTHING");
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO [SalesRequestIdempotency]
                    ([Id], [TenantId], [Key], [Operation], [RequestHash], [ClaimToken], [ClaimTimestampUtc], [CreatedAt], [UpdatedAt], [IsDeleted])
                SELECT
                    {claimId}, {tenantId}, {key}, {operation}, {requestHash}, {claimToken}, {nowUtc}, {nowUtc}, {nowUtc}, {false}
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [SalesRequestIdempotency] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Key] = {key}
                      AND [Operation] = {operation})");
        }

        var claim = new SalesRequestIdempotency
        {
            Id = claimId,
            TenantId = tenantId,
            Key = key,
            Operation = operation,
            RequestHash = requestHash,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
            IsDeleted = false,
        };

        _context.SalesRequestIdempotencyRecords.Add(claim);

        try
        {
            return await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _context.Entry(claim).State = EntityState.Detached;
            return 0;
        }
    }

    private async Task<int> DeleteSalesIdempotencyRecordByIdAsync(Guid recordId)
    {
        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM ""SalesRequestIdempotency""
                WHERE ""Id"" = {recordId}");
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM [SalesRequestIdempotency]
                WHERE [Id] = {recordId}");
        }

        return await _context.SalesRequestIdempotencyRecords
            // SECURITY: IgnoreQueryFilters is intentional so reclaimed soft-deleted
            // idempotency claims can be hard-deleted by their concrete record ID.
            .IgnoreQueryFilters()
            .Where(record => record.Id == recordId)
            .ExecuteDeleteAsync();
    }

    private async Task<int> DeleteStaleSalesIdempotencyClaimAsync(Guid tenantId, string key, string operation, int staleClaimMinutes)
    {
        var cutoffUtc = DateTime.UtcNow.AddMinutes(-staleClaimMinutes);

        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM ""SalesRequestIdempotency""
                WHERE ""TenantId"" = {tenantId}
                  AND ""Key"" = {key}
                  AND ""Operation"" = {operation}
                  AND ""ResponseStatusCode"" IS NULL
                  AND ""CompletedAtUtc"" IS NULL
                  AND COALESCE(""ClaimTimestampUtc"", ""CreatedAt"") < {cutoffUtc}");
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM [SalesRequestIdempotency]
                WHERE [TenantId] = {tenantId}
                  AND [Key] = {key}
                  AND [Operation] = {operation}
                  AND [ResponseStatusCode] IS NULL
                  AND [CompletedAtUtc] IS NULL
                  AND COALESCE([ClaimTimestampUtc], [CreatedAt]) < {cutoffUtc}");
        }

        return await _context.SalesRequestIdempotencyRecords
            // SECURITY: IgnoreQueryFilters is intentional so abandoned in-flight
            // claims can be reclaimed, while tenant/key/operation are still enforced.
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == tenantId &&
                record.Key == key &&
                record.Operation == operation &&
                record.ResponseStatusCode == null &&
                record.CompletedAtUtc == null &&
                (record.ClaimTimestampUtc ?? record.CreatedAt) < cutoffUtc)
            .ExecuteDeleteAsync();
    }

    private async Task<ActionResult<SaleResponse>> CompleteSalesIdempotencyAsync(
        IDbContextTransaction transaction,
        SalesRequestIdempotency record,
        ActionResult<SaleResponse> result)
    {
        // Validate that we still hold the claim token before completing
        // This prevents race conditions where another process might have stolen the claim
        var currentRecord = await _context.SalesRequestIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == record.Id);

        if (currentRecord == null || currentRecord.ClaimToken != record.ClaimToken)
        {
            _logger.LogError(
                "Claim token validation failed for idempotency record {RecordId}. Expected token: {ExpectedToken}, Current token: {CurrentToken}",
                record.Id,
                record.ClaimToken,
                currentRecord?.ClaimToken);

            await transaction.RollbackAsync();
            throw new InvalidOperationException("Idempotency claim token validation failed. The claim may have been stolen by another process.");
        }

        // Persist the exact HTTP status/body before commit so a post-commit retry can replay byte-equivalent JSON.
        var snapshot = SnapshotSaleActionResult(result);

        record.ResponseStatusCode = snapshot.StatusCode;
        record.ResponseJson = snapshot.ResponseJson;
        record.CompletedAtUtc = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return result;
    }

    private string? ValidateSalesIdempotencyKey(string? paymentAttemptId)
    {
        var normalizedKey = paymentAttemptId?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedKey) &&
            normalizedKey.Length <= MaxSalesIdempotencyKeyLength &&
            SalesIdempotencyKeyPattern.IsMatch(normalizedKey))
        {
            return normalizedKey;
        }

        _logger.LogWarning(
            "Invalid sales idempotency key received for tenant {TenantId}",
            _tenantContext.TenantId);

        return null;
    }

    private string? ValidateOptionalSalesIdempotencyKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return ValidateSalesIdempotencyKey(key);
    }

    private static string BuildSalesRequestHash(string operation, object request)
    {
        return DeterministicRequestHasher.ComputeHash(new
        {
            operation,
            request,
        });
    }

    /// <summary>
    /// Build idempotency payload for CreateSale operations.
    /// EXCLUDES ExpectedMutationVersion since it's not relevant for creates.
    /// </summary>
    private static object BuildCreateSaleIdempotencyPayload(CreateSaleRequest request)
    {
        return new
        {
            request.CustomerId,
            request.WarehouseId,
            request.SalesRepresentativeId,
            // ExpectedMutationVersion intentionally excluded for CreateSale
            request.DiscountPercentage,
            request.DiscountAmount,
            request.PaymentMethod,
            request.PaidAmount,
            request.CashAmount,
            request.CardAmount,
            Notes = request.Notes?.Trim(),
            Items = (request.Items ?? [])
                .Select(item => new
                {
                    item.ProductId,
                    item.ProductVariantId,
                    item.UnitId,
                    item.Quantity,
                    item.DiscountAmount
                })
                .OrderBy(item => item.ProductId)
                .ThenBy(item => item.ProductVariantId)
                .ThenBy(item => item.UnitId)
                .ThenBy(item => item.Quantity)
                .ThenBy(item => item.DiscountAmount)
                .ToList()
        };
    }

    /// <summary>
    /// Build idempotency payload for UpdateSale operations.
    /// INCLUDES ExpectedMutationVersion for optimistic concurrency control.
    /// </summary>
    private static object BuildUpdateSaleIdempotencyPayload(CreateSaleRequest request)
    {
        return new
        {
            request.CustomerId,
            request.WarehouseId,
            request.SalesRepresentativeId,
            request.ExpectedMutationVersion, // Required for UpdateSale concurrency
            request.DiscountPercentage,
            request.DiscountAmount,
            request.PaymentMethod,
            request.PaidAmount,
            request.CashAmount,
            request.CardAmount,
            Notes = request.Notes?.Trim(),
            Items = (request.Items ?? [])
                .Select(item => new
                {
                    item.ProductId,
                    item.ProductVariantId,
                    item.UnitId,
                    item.Quantity,
                    item.DiscountAmount
                })
                .OrderBy(item => item.ProductId)
                .ThenBy(item => item.ProductVariantId)
                .ThenBy(item => item.UnitId)
                .ThenBy(item => item.Quantity)
                .ThenBy(item => item.DiscountAmount)
                .ToList()
        };
    }

    private static SaleActionSnapshot SnapshotSaleActionResult(ActionResult<SaleResponse> result)
    {
        if (result.Result is ObjectResult objectResult)
        {
            return new SaleActionSnapshot(
                objectResult.StatusCode ?? StatusCodes.Status200OK,
                SerializeSalePayload(objectResult.Value));
        }

        if (result.Result is StatusCodeResult statusCodeResult)
        {
            return new SaleActionSnapshot(statusCodeResult.StatusCode, null);
        }

        return new SaleActionSnapshot(
            StatusCodes.Status200OK,
            SerializeSalePayload(result.Value));
    }

    private static ActionResult<SaleResponse> BuildStoredSaleResponse(SalesRequestIdempotency record)
    {
        var statusCode = record.ResponseStatusCode ?? StatusCodes.Status200OK;

        if (string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            // PHASE 3 HARDENING: ResponseStatusCode is set but ResponseJson is null/empty.
            // This is an interrupted-commit edge case: the transaction was completing
            // (ResponseStatusCode was written) but the process was killed before
            // ResponseJson was serialized and committed.
            //
            // Returning an empty StatusCodeResult(200) would silently yield a bodyless
            // replay with no InvoiceId — a corruption-level defect from the caller's
            // perspective (they cannot reconstruct the invoice identity).
            //
            // The correct fail-closed behaviour is to signal SALE_IN_PROGRESS (409) so
            // the caller retries. The stale-claim reclaim path in TryClaimSalesIdempotencyAsync
            // will reclaim this interrupted record after the staleness window expires (5 min)
            // and allow a clean re-execution. This preserves replay safety because no new
            // durable business rows are created until the stale record is evicted.
            return new ObjectResult(new
            {
                conflictCode = "SALE_IN_PROGRESS",
                message = "A previous sale attempt is still being finalized. Please retry after a short delay."
            })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }

        using var document = JsonDocument.Parse(record.ResponseJson);
        var payload = document.RootElement.Clone();
        return new ObjectResult(payload)
        {
            StatusCode = statusCode,
        };
    }

    private static string? SerializeSalePayload(object? payload)
    {
        return payload == null
            ? null
            : JsonSerializer.Serialize(payload, SalesIdempotencyJsonOptions);
    }

    private async Task<(List<PreparedSaleItem>? Items, ActionResult<SaleResponse>? ErrorResult)> PrepareSaleItemsAsync(
        IEnumerable<SaleItemRequest> requestItems,
        CancellationToken cancellationToken = default)
    {
        var itemRequests = requestItems.ToList();
        var preparedItems = new List<PreparedSaleItem>();

        // PERFORMANCE: Batch load all products to avoid N+1 queries
        var productIds = itemRequests.Select(r => r.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // PERFORMANCE: Batch load all product units to avoid N+1 queries
        var productUnits = await _context.ProductUnits
            .Where(pu => productIds.Contains(pu.ProductId))
            .ToListAsync(cancellationToken);

        // PERFORMANCE: Batch resolve all variants to avoid N+1 queries
        // Use sequential processing to avoid EF Core concurrency errors and index-based keys to avoid duplicate key collisions
        var variantDict = new Dictionary<int, ProductVariant?>();
        for (int i = 0; i < itemRequests.Count; i++)
        {
            var itemRequest = itemRequests[i];
            var variant = await ProductVariantStockMutationSupport.ResolveVariantAsync(
                _context,
                itemRequest.ProductId,
                itemRequest.ProductVariantId,
                cancellationToken);
            variantDict[i] = variant;
        }

        for (int i = 0; i < itemRequests.Count; i++)
        {
            var itemRequest = itemRequests[i];
            if (!products.TryGetValue(itemRequest.ProductId, out var product))
            {
                return (null, BadRequest(new
                {
                    message = "Product not found"
                }));
            }

            decimal conversionFactor = 1;
            Guid? unitId = itemRequest.UnitId;

            if (itemRequest.UnitId.HasValue)
            {
                var productUnit = productUnits.FirstOrDefault(
                    pu => pu.ProductId == itemRequest.ProductId && pu.UnitId == itemRequest.UnitId);

                if (productUnit == null)
                {
                    return (null, BadRequest(new
                    {
                        message = $"Unit is not configured for product {product.Name}."
                    }));
                }

                if (productUnit.ConversionFactor <= 0)
                {
                    return (null, BadRequest(new
                    {
                        message = $"Unit conversion is invalid for product {product.Name}."
                    }));
                }

                conversionFactor = productUnit.ConversionFactor;
            }
            else
            {
                var defaultUnit = productUnits.FirstOrDefault(
                    pu => pu.ProductId == itemRequest.ProductId && pu.IsDefault);

                if (defaultUnit == null)
                {
                    return (null, BadRequest(new
                    {
                        message = $"Default unit is not configured for product {product.Name}."
                    }));
                }

                if (defaultUnit.ConversionFactor <= 0)
                {
                    return (null, BadRequest(new
                    {
                        message = $"Default unit conversion is invalid for product {product.Name}."
                    }));
                }

                unitId = defaultUnit.UnitId;
                conversionFactor = defaultUnit.ConversionFactor;
            }

            var variant = variantDict.GetValueOrDefault(i);

            if (variant == null)
            {
                return (null, BadRequest(new
                {
                    message = $"No variant found for product {product.Name}. Please select a variant."
                }));
            }

            var authoritativeUnitPrice = ResolveAuthoritativeSaleUnitPrice(variant, conversionFactor, productUnits.FirstOrDefault(
                pu => pu.ProductId == itemRequest.ProductId && pu.UnitId == unitId));
            var authoritativeTaxRate = RoundCurrency(product.TaxRate);
            var lineSubtotal = RoundCurrency(itemRequest.Quantity * authoritativeUnitPrice);
            var authoritativeDiscountAmount = RoundCurrency(itemRequest.DiscountAmount);

            if (authoritativeDiscountAmount > lineSubtotal)
            {
                return (null, BadRequest(new
                {
                    message = $"Discount exceeds line subtotal for product {product.Name}."
                }));
            }

            var authoritativeRequest = itemRequest with
            {
                UnitPrice = authoritativeUnitPrice,
                TaxRate = authoritativeTaxRate,
                DiscountAmount = authoritativeDiscountAmount
            };

            preparedItems.Add(new PreparedSaleItem(
                authoritativeRequest,
                product,
                variant,
                unitId,
                conversionFactor,
                itemRequest.Quantity * conversionFactor));
        }

        return (preparedItems
            .OrderBy(item => item.Variant.Id)
            .ThenBy(item => item.Product.Id)
            .ToList(), null);
    }

    private async Task<Customer?> LoadActiveCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        await _context.Customers
            .FirstOrDefaultAsync(customer =>
                customer.Id == customerId &&
                customer.TenantId == _tenantContext.RequiredTenantId &&
                !customer.IsDeleted,
                cancellationToken);

    private async Task<Warehouse?> LoadActiveWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken = default) =>
        await _context.Warehouses
            .FirstOrDefaultAsync(warehouse =>
                warehouse.Id == warehouseId &&
                warehouse.TenantId == _tenantContext.RequiredTenantId &&
                warehouse.IsActive &&
                !warehouse.IsDeleted,
                cancellationToken);

    private bool TryResolveCurrentUserId(out Guid userId)
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                  User.FindFirstValue("sub") ??
                  User.FindFirstValue("userId");
        return Guid.TryParse(raw, out userId) && userId != Guid.Empty;
    }

    private sealed record SaleActionSnapshot(int StatusCode, string? ResponseJson);
    private sealed record PreparedSaleItem(
        SaleItemRequest Request,
        Product Product,
        ProductVariant Variant,
        Guid? UnitId,
        decimal ConversionFactor,
        decimal BaseQuantity);

    /// <summary>
    /// Update an existing sale
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<SaleResponse>> UpdateSale(
        Guid id,
        CreateSaleRequest request,
        [FromHeader(Name = PaymentAttemptIdHeader)] string? paymentAttemptId = null)
    {
        if (ValidateSalesIdempotencyKey(paymentAttemptId) is not { } normalizedPaymentAttemptId)
        {
            return BadRequest(new { message = "Invalid idempotency key" });
        }

        AddPaymentAttemptHeader(normalizedPaymentAttemptId);

        var requestHash = BuildSalesRequestHash("UPDATE", new
        {
            SaleId = id,
            Request = BuildUpdateSaleIdempotencyPayload(request)
        });

        await using var transaction = await _context.Database.BeginTransactionAsync();
        decimal? calculatedTotalAmount = null;
        SalesRequestIdempotency? idempotencyRecord = null;
        var pendingStockChanges = new List<PendingProductVariantStockChange>();

        try
        {
            var claim = await TryClaimSalesIdempotencyAsync("UPDATE", normalizedPaymentAttemptId, requestHash);
            if (claim.ShortCircuitResult != null)
            {
                await transaction.RollbackAsync();
                return claim.ShortCircuitResult;
            }

            idempotencyRecord = claim.Record!;
            EnsureSupportedPaymentMethod(request.PaymentMethod);

            var invoice = await LoadSalesInvoiceForMutationAsync(id);

            if (invoice == null)
                return NotFound(new { message = "Invoice not found" });

            if (invoice.Status != InvoiceStatus.Draft)
            {
                ActionResult<SaleResponse> conflictResult = Conflict(new
                {
                    message = "Only draft sales invoices can be rewritten. Use sales returns to reverse completed invoices."
                });

                return await CompleteSalesIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    conflictResult);
            }

            if (!IsExpectedSaleMutationVersion(invoice, request.ExpectedMutationVersion))
            {
                ActionResult<SaleResponse> conflictResult = Conflict(new
                {
                    message = "Sale was changed by another session. Reload the invoice before updating.",
                    currentMutationVersion = invoice.AccountingMutationVersion
                });

                return await CompleteSalesIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    conflictResult);
            }

            // 1. Revert previous effects
            await RevertSaleEffects(invoice, pendingStockChanges, journalPolicy: JournalPolicy.AllowExisting);

            // 2. Update Invoice Header
            invoice.CustomerId = request.CustomerId;
            invoice.WarehouseId = request.WarehouseId;
            invoice.SalesRepresentativeId = request.SalesRepresentativeId;
            invoice.PaymentMethod = request.PaymentMethod;
            invoice.CashAmount = request.CashAmount;
            invoice.CardAmount = request.CardAmount;
            invoice.Notes = request.Notes;
            invoice.AccountingMutationVersion++;

            // 3. Clear old items
            _context.SalesInvoiceItems.RemoveRange(invoice.Items);
            // Must save changes so EF tracks removal before adding new ones with potentially same IDs (though we generate new IDs)
            // But we already cleared the collection.
            
            // 4. Validate new inputs
            var warehouse = await LoadActiveWarehouseAsync(request.WarehouseId);
            if (warehouse == null) return BadRequest(new { message = "Warehouse not found" });

            Customer? customer = null;
            if (request.CustomerId.HasValue)
            {
                customer = await LoadActiveCustomerAsync(request.CustomerId.Value);
                if (customer == null) return BadRequest(new { message = "Customer not found" });
            }

            // 5. Process new items
            decimal subtotal = 0;
            decimal totalTax = 0;
            decimal lineDiscountTotal = 0;
            var preparedSaleItemsResult = await PrepareSaleItemsAsync(request.Items);
            if (preparedSaleItemsResult.ErrorResult != null)
            {
                await transaction.RollbackAsync();
                return preparedSaleItemsResult.ErrorResult;
            }

            var preparedSaleItems = preparedSaleItemsResult.Items!;
            var lockedStocks = await ProductVariantStockMutationSupport.LockMultipleStocksAsync(
                _context,
                _tenantContext.RequiredTenantId,
                preparedSaleItems
                    .Select(item => new StockLockTarget(request.WarehouseId, item.Variant.Id, CreateIfMissing: false))
                    .DistinctBy(target => (target.WarehouseId, target.VariantId)));

            foreach (var preparedItem in preparedSaleItems)
            {
                var itemRequest = preparedItem.Request;
                var product = preparedItem.Product;
                var variant = preparedItem.Variant;
                var unitId = preparedItem.UnitId;
                var conversionFactor = preparedItem.ConversionFactor;
                var baseQuantity = preparedItem.BaseQuantity;
                var variantStock = lockedStocks.GetValueOrDefault((request.WarehouseId, variant.Id));

                if (variantStock == null)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new
                    {
                        message = $"Insufficient stock for {product.Name} ({variant.SKU ?? "default"}). Available: 0, Requested: {baseQuantity}"
                    });
                }

                ProductVariantStockMutationSupport.ValidateAvailableQuantity(
                    variantStock,
                    baseQuantity,
                    allowNegativeStock: false,
                    operationName: "Sale");

                var itemSubtotal = itemRequest.Quantity * itemRequest.UnitPrice;
                var itemDiscount = itemRequest.DiscountAmount;
                var taxableAmount = itemSubtotal - itemDiscount;
                var itemTax = product.PriceIncludesTax ? 0 : (taxableAmount * itemRequest.TaxRate / 100);
                var itemTotal = taxableAmount + itemTax;
                var costPriceSnapshot = ResolveSaleCostPriceSnapshot(variant);
                var costAmountSnapshot = ResolveSaleCostAmountSnapshot(costPriceSnapshot, baseQuantity);
                var invoiceItemId = Guid.NewGuid();

                _context.SalesInvoiceItems.Add(new SalesInvoiceItem
                {
                    Id = invoiceItemId,
                    SalesInvoiceId = invoice.Id,
                    ProductId = itemRequest.ProductId,
                    ProductVariantId = variant.Id,
                    UnitId = unitId,
                    Quantity = itemRequest.Quantity,
                    ConversionFactor = conversionFactor,
                    BaseQuantity = baseQuantity,
                    UnitPrice = itemRequest.UnitPrice,
                    DiscountAmount = itemDiscount,
                    TaxRate = itemRequest.TaxRate,
                    TaxAmount = itemTax,
                    TotalAmount = itemTotal,
                    CostPriceSnapshot = costPriceSnapshot,
                    CostAmountSnapshot = costAmountSnapshot,
                    // Only set foreign key IDs, not navigation properties
                    // EF Core will handle navigation properties automatically
                    CreatedAt = DateTime.UtcNow
                });

                subtotal += itemSubtotal;
                lineDiscountTotal += itemDiscount;
                totalTax += itemTax;

                var oldQuantity = variantStock.Quantity;
                variantStock.Quantity -= baseQuantity;

                _context.StockMovements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.RequiredTenantId,
                    WarehouseId = request.WarehouseId,
                    ProductId = itemRequest.ProductId,
                    ProductVariantId = variant.Id,
                    MovementType = StockMovementType.Sale,
                    UnitId = unitId,
                    ConversionFactor = conversionFactor,
                    QuantityChange = -baseQuantity,
                    QuantityAfter = variantStock.Quantity,
                    AvailableAfter = variantStock.AvailableQuantity,
                    ReservedAfter = variantStock.ReservedQuantity,
                    DamagedAfter = variantStock.DamagedQuantity,
                    ReferenceId = invoice.Id,
                    ReferenceLineId = invoiceItemId,
                    ReferenceNumber = invoice.InvoiceNumber,
                    Price = costPriceSnapshot,
                    CreatedAt = DateTime.UtcNow
                });

                pendingStockChanges.Add(ProductVariantStockMutationSupport.BuildPendingStockChange(
                    variant.Id,
                    itemRequest.ProductId,
                    request.WarehouseId,
                    oldQuantity,
                    variantStock.Quantity,
                    "Sale",
                    referenceId: invoice.Id,
                    referenceType: "SalesInvoice",
                    sku: variant.SKU,
                    warehouseName: warehouse.Name));
            }

            ApplySaleTotals(
                invoice,
                subtotal,
                lineDiscountTotal,
                totalTax,
                request);
            calculatedTotalAmount = RoundCurrency(invoice.TotalAmount);
            var paymentAmounts = ValidateAndNormalizePaymentAmounts(
                request.PaymentMethod,
                invoice.TotalAmount,
                request.PaidAmount,
                request.CashAmount,
                request.CardAmount);
            invoice.PaidAmount = paymentAmounts.PaidAmount;
            invoice.CashAmount = paymentAmounts.CashAmount;
            invoice.CardAmount = paymentAmounts.CardAmount;

            // 7. Re-apply Financials
             if (customer != null)
            {
                await _ledgerService.CreateLedgerEntry(
                    Core.Enums.PartyType.Customer,
                    customer.Id,
                    DateTime.UtcNow,
                    TransactionType.Sale,
                    invoice.InvoiceNumber,
                    invoice.TotalAmount, 
                    0,
                    invoice.Id,
                    $"Updated Sale Invoice {invoice.InvoiceNumber}");

                // IMPORTANT: BalanceAfter must be assigned AFTER CreateLedgerEntry
                // because LedgerService updates customer.Balance internally
                var saleTransaction = new CustomerTransaction
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                    CustomerId = customer.Id,
                    TransactionDate = DateTime.UtcNow,
                    TransactionType = TransactionType.Sale,
                    DebitAmount = invoice.TotalAmount,
                    CreditAmount = 0,
                    ReferenceId = invoice.Id,
                    ReferenceNumber = invoice.InvoiceNumber,
                    CreatedAt = DateTime.UtcNow,
                    BalanceAfter = customer.Balance // Set AFTER ledger entry to capture correct balance
                };
                _context.CustomerTransactions.Add(saleTransaction);

                var customerLedgerPaymentAmount = ResolveCustomerLedgerPaymentAmount(invoice);
                if (customerLedgerPaymentAmount > 0)
                {
                    var paymentNote = $"Payment for Sale Invoice {invoice.InvoiceNumber} - {invoice.PaymentMethod}";
                    if (invoice.PaymentMethod == PaymentMethod.Mixed)
                    {
                        paymentNote += $" (Cash: ${invoice.CashAmount:F2}, Card: ${invoice.CardAmount:F2})";
                    }

                    await _ledgerService.CreateLedgerEntry(
                        Core.Enums.PartyType.Customer,
                        customer.Id,
                        DateTime.UtcNow,
                        TransactionType.PaymentReceived,
                        invoice.InvoiceNumber,
                        0,
                        customerLedgerPaymentAmount,
                        invoice.Id,
                        paymentNote);

                    // IMPORTANT: BalanceAfter must be assigned AFTER CreateLedgerEntry
                    // because LedgerService updates customer.Balance internally
                    var paymentTransaction = new CustomerTransaction
                    {
                        Id = Guid.NewGuid(),
                        TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                        CustomerId = customer.Id,
                        TransactionDate = DateTime.UtcNow,
                        TransactionType = TransactionType.PaymentReceived,
                        DebitAmount = 0,
                        CreditAmount = customerLedgerPaymentAmount,
                        ReferenceId = invoice.Id,
                        ReferenceNumber = invoice.InvoiceNumber,
                        CreatedAt = DateTime.UtcNow,
                        BalanceAfter = customer.Balance // Set AFTER ledger entry to capture correct balance
                    };
                    _context.CustomerTransactions.Add(paymentTransaction);
                }
            }

            // ════════════════════════════════════════════════════════════════════════════
            // PHASE LCR: Accounting reversal and reposting for sale update
            // ════════════════════════════════════════════════════════════════════════════
            // EnqueueSaleReversalAsync stages a durable accounting row. The synchronous
            // reversal/repost below is the executor for this request and marks that row
            // processed before commit, preventing a second worker-side financial mutation.
            await _accountingOutbox.EnqueueSaleReversalAsync(
                invoice.TenantId,
                invoice.Id,
                normalizedPaymentAttemptId,
                invoice.AccountingMutationVersion);

            var updatedSalePosting = await PostUpdatedSaleAccountingSynchronouslyAsync(
                invoice,
                normalizedPaymentAttemptId,
                HttpContext.RequestAborted);

            await MarkSynchronousSaleAccountingOutboxProcessedAsync(
                invoice.TenantId,
                invoice.Id,
                "SaleReversal",
                BuildSaleMutationAccountingOutboxIdempotencyKey(invoice.TenantId, invoice.Id, normalizedPaymentAttemptId),
                invoice.AccountingMutationVersion,
                updatedSalePosting.JournalId,
                HttpContext.RequestAborted);
            
            // DO NOT mutate balances directly - updated sale posting is handled through the outbox and ledger.

            if (pendingStockChanges.Count > 0)
            {
                await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                    _context,
                    _tenantContext.RequiredTenantId,
                    pendingStockChanges);
            }

            var result = await CompleteSalesIdempotencyAsync(
                transaction,
                idempotencyRecord!,
                Ok(new SaleResponse
                {
                    InvoiceId = invoice.Id,
                    InvoiceNumber = invoice.InvoiceNumber,
                    TotalAmount = RoundCurrency(invoice.TotalAmount),
                    PaidAmount = RoundCurrency(invoice.PaidAmount),
                    ChangeAmount = RoundCurrency(invoice.PaidAmount - invoice.TotalAmount),
                    Message = "Sale updated successfully"
                }));

            return result;
        }
        catch (ValidationException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex,
                "Validation error updating sale. PaymentMethod: {PaymentMethod}, TotalAmount: {TotalAmount}, PaidAmount: {PaidAmount}, CashAmount: {CashAmount}, CardAmount: {CardAmount}",
                request.PaymentMethod,
                calculatedTotalAmount,
                request.PaidAmount,
                request.CashAmount,
                request.CardAmount);
            return BadRequest(new { message = "Sale update contains invalid payment or pricing data." });
        }
        catch (InsufficientStockException ex)
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            _logger.LogWarning(
                ex,
                "Insufficient stock updating sale for key {Key} tenant {TenantId}. VariantId: {VariantId}, WarehouseId: {WarehouseId}, Available: {AvailableQuantity}, Requested: {RequestedQuantity}",
                normalizedPaymentAttemptId,
                _tenantContext.RequiredTenantId,
                ex.VariantId,
                ex.WarehouseId,
                ex.AvailableQuantity,
                ex.RequestedQuantity);
            return Conflict(new
            {
                message = "Insufficient stock",
                availableStock = ex.AvailableQuantity,
                requestedQuantity = ex.RequestedQuantity
            });
        }
        catch(Exception ex)
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "Error updating sale for key {Key}", normalizedPaymentAttemptId);
            return StatusCode(500, new { message = "An error occurred while updating the sale" });
        }
    }

    /// <summary>
    /// Delete sales invoice
    /// Posted/completed invoices cannot be deleted - use sales return instead.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult> DeleteSale(Guid id)
    {
        // Validate if invoice can be deleted
        var validationResult = await _validationService.CanDeleteSalesInvoiceAsync(id);
        if (!validationResult.IsValid)
        {
            return BadRequest(new {
                errorCode = validationResult.ErrorCode,
                messageEn = validationResult.MessageEn,
                messageAr = validationResult.MessageAr,
                data = validationResult.Data
            });
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        var pendingStockChanges = new List<PendingProductVariantStockChange>();
        try
        {
            var invoice = await LoadSalesInvoiceForMutationAsync(id);

            if (invoice == null)
            {
                return NotFound(new { 
                    errorCode = "INVOICE_NOT_FOUND",
                    messageEn = "Sales invoice not found.",
                    messageAr = "فاتورة البيع غير موجودة."
                });
            }

            invoice.AccountingMutationVersion++;
            await RevertSaleEffects(
                invoice,
                pendingStockChanges,
                journalPolicy: JournalPolicy.RejectExisting);
            
            _context.SalesInvoices.Remove(invoice);
            await _context.SaveChangesAsync();

            if (pendingStockChanges.Count > 0)
            {
                await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                    _context,
                    _tenantContext.RequiredTenantId,
                    pendingStockChanges);
            }

            await transaction.CommitAsync();

            _logger.LogInformation("Sales invoice deleted: {InvoiceId} - {InvoiceNumber}", invoice.Id, invoice.InvoiceNumber);
            await _auditService.LogDelete("SalesInvoice", invoice.Id.ToString(), $"Deleted Sale Invoice {invoice.InvoiceNumber}", null, invoice.InvoiceNumber);

            return Ok(new { 
                messageEn = "Sale deleted successfully.",
                messageAr = "تم حذف فاتورة البيع بنجاح."
            });
        }
        catch (PostedJournalsExistException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex, "Cannot delete sale due to existing posted journals | InvoiceId: {InvoiceId}", id);
            return Conflict(new { 
                errorCode = "POSTED_JOURNALS_EXIST",
                messageEn = "This sale cannot be deleted because it has posted accounting journals. Please reverse the journals first.",
                messageAr = "لا يمكن حذف هذه الفاتورة لأنها تحتوي على قيود محاسبية مرحلة. يرجى عكس القيود أولاً.",
                details = ex.Message
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error deleting sale");
            return StatusCode(500, new { 
                errorCode = "DELETE_ERROR",
                messageEn = "An error occurred while deleting the sale.",
                messageAr = "حدث خطأ أثناء حذف فاتورة البيع."
            });
        }
    }

    /// <summary>
    /// Check if a sales invoice can be deleted - use this before showing delete button
    /// </summary>
    [HttpGet("{id}/can-delete")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> CanDeleteSale(Guid id)
    {
        var invoice = await _context.SalesInvoices.FindAsync(id);
        if (invoice == null)
        {
            return NotFound();
        }

        var validationResult = await _validationService.CanDeleteSalesInvoiceAsync(id);
        return Ok(new {
            canDelete = validationResult.IsValid,
            errorCode = validationResult.ErrorCode,
            messageEn = validationResult.MessageEn,
            messageAr = validationResult.MessageAr
        });
    }

    private async Task<PostingResult> PostUpdatedSaleAccountingSynchronouslyAsync(
        SalesInvoice invoice,
        string operationKey,
        CancellationToken ct)
    {
        var journalsToReverse = await _context.JournalEntries
            .IgnoreQueryFilters()
            .Where(journal =>
                journal.TenantId == invoice.TenantId &&
                journal.SourceId == invoice.Id &&
                journal.Status == JournalStatus.Posted &&
                (journal.SourceType == "SalesInvoice" || journal.SourceType == "SalesInvoice_COGS") &&
                (string.IsNullOrEmpty(journal.IdempotencyKey) || !journal.IdempotencyKey.EndsWith($":{operationKey}")))
            .OrderBy(journal => journal.PostedAtUtc)
            .ThenBy(journal => journal.JournalNumber)
            .ToListAsync(ct);

        foreach (var journal in journalsToReverse)
        {
            await _postingService.ReverseJournalAsync(
                invoice.TenantId,
                journal.Id,
                $"Sale update for {invoice.InvoiceNumber} ({operationKey})",
                ct: ct);
        }

        var salePosting = await _commerceAccounting.PostPosSaleAsync(invoice, ct, operationKey);
        if (!salePosting.Success)
        {
            throw new InvalidOperationException(
                $"Updated POS sale accounting post failed for invoice {invoice.InvoiceNumber}: {salePosting.ErrorMessage}");
        }

        return salePosting;
    }

    private async Task MarkSynchronousSaleAccountingOutboxProcessedAsync(
        Guid tenantId,
        Guid invoiceId,
        string eventType,
        string idempotencyKey,
        int mutationVersion,
        Guid? journalId,
        CancellationToken ct)
    {
        if (!journalId.HasValue || journalId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Synchronous {eventType} accounting post for sale {invoiceId} did not return a journal id.");
        }

        var message = await _context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate =>
                    candidate.TenantId == tenantId &&
                    candidate.BusinessEntityId == invoiceId &&
                    candidate.EventType == eventType &&
                    candidate.IdempotencyKey == idempotencyKey &&
                    candidate.AggregateVersion == mutationVersion,
                ct);

        if (message == null)
        {
            throw new InvalidOperationException(
                $"Synchronous {eventType} accounting post for sale {invoiceId} has no matching outbox row.");
        }

        if (message.ProcessedAtUtc.HasValue)
        {
            if (!message.JournalId.HasValue)
            {
                throw new InvalidOperationException(
                    $"Synchronous {eventType} accounting outbox row for sale {invoiceId} was already processed without a journal.");
            }

            if (message.JournalId.Value != journalId.Value)
            {
                throw new InvalidOperationException(
                    $"Synchronous {eventType} accounting outbox row for sale {invoiceId} was already processed with a different journal.");
            }

            return;
        }

        if (message.IsFailed ||
            (message.ClaimedUntilUtc.HasValue && message.ClaimedUntilUtc.Value > DateTime.UtcNow))
        {
            throw new InvalidOperationException(
                $"Synchronous {eventType} accounting outbox row for sale {invoiceId} is failed or claimed and cannot be completed by the request executor.");
        }

        message.MarkProcessed(journalId.Value);
    }

    private static string BuildBaseSaleAccountingOutboxIdempotencyKey(Guid tenantId, Guid invoiceId) =>
        $"SALE:{tenantId}:{invoiceId}";

    private static string BuildSaleMutationAccountingOutboxIdempotencyKey(Guid tenantId, Guid invoiceId, string operationKey) =>
        $"SALE:{tenantId}:{invoiceId}:{operationKey}";

    private async Task RevertSaleEffects(
        SalesInvoice invoice,
        List<PendingProductVariantStockChange>? pendingStockChanges = null,
        JournalPolicy journalPolicy = JournalPolicy.AllowExisting)
    {
        var resolvedItems = new List<(SalesInvoiceItem Item, ProductVariant Variant)>();

        foreach (var item in invoice.Items)
        {
            var variant = await ProductVariantStockMutationSupport.ResolveVariantAsync(
                _context,
                item.ProductId,
                item.ProductVariantId);

            if (variant != null)
            {
                resolvedItems.Add((item, variant));
            }
        }

        var lockedStocks = await ProductVariantStockMutationSupport.LockMultipleStocksAsync(
            _context,
            _tenantContext.RequiredTenantId,
            resolvedItems
                .Select(entry => new StockLockTarget(invoice.WarehouseId, entry.Variant.Id, CreateIfMissing: true))
                .DistinctBy(target => (target.WarehouseId, target.VariantId)));

        foreach (var entry in resolvedItems.OrderBy(item => item.Variant.Id).ThenBy(item => item.Item.ProductId))
        {
            var variantStock = lockedStocks[(invoice.WarehouseId, entry.Variant.Id)];
            var oldQuantity = variantStock.Quantity;
            variantStock.Quantity += entry.Item.BaseQuantity;
            variantStock.UpdatedAt = DateTime.UtcNow;

            _context.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,
                WarehouseId = invoice.WarehouseId,
                ProductId = entry.Item.ProductId,
                ProductVariantId = entry.Variant.Id,
                MovementType = StockMovementType.Adjustment,
                QuantityChange = entry.Item.BaseQuantity,
                QuantityAfter = variantStock.Quantity,
                AvailableAfter = variantStock.AvailableQuantity,
                ReservedAfter = variantStock.ReservedQuantity,
                DamagedAfter = variantStock.DamagedQuantity,
                ReferenceId = invoice.Id,
                ReferenceLineId = entry.Item.Id,
                ReferenceNumber = invoice.InvoiceNumber,
                ReferenceType = "SalesInvoiceRevert",
                IdempotencyKey = $"sales-invoice-revert:{_tenantContext.RequiredTenantId:N}:{invoice.Id:N}:{entry.Item.Id:N}",
                Source = "SalesInvoiceRevert",
                Notes = $"Reversal of sale inventory movement for invoice {invoice.InvoiceNumber}",
                CreatedAt = DateTime.UtcNow
            });

            pendingStockChanges?.Add(ProductVariantStockMutationSupport.BuildPendingStockChange(
                entry.Variant.Id,
                entry.Item.ProductId,
                invoice.WarehouseId,
                oldQuantity,
                variantStock.Quantity,
                "SaleRevert",
                referenceId: invoice.Id,
                referenceType: "SalesInvoiceRevert",
                sku: entry.Variant.SKU));
        }
        
        // 2. Revert legacy treasury side effects without mutating balances directly.
        // DO NOT modify balances directly - use LedgerService / accounting journal reversal flows.
        await _saleFinancialIntegrityService.RevertLegacyTreasuryEffectsAsync(
            invoice,
            journalPolicy);

        // 3. Revert Customer Ledger & Transactions
        if (invoice.CustomerId.HasValue)
        {
            var ledgerEntries = await _context.AccountLedgers
                .Where(l => l.ReferenceId == invoice.Id)
                .ToListAsync();
            
            _context.AccountLedgers.RemoveRange(ledgerEntries);

            var customerTransactions = await _context.CustomerTransactions
                .Where(t => t.ReferenceId == invoice.Id)
                .ToListAsync();
            
            _context.CustomerTransactions.RemoveRange(customerTransactions);

            await _context.SaveChangesAsync();
            await _partyBalanceMaintenanceService.RebuildPartyBalanceAsync(
                PartyType.Customer,
                invoice.CustomerId.Value,
                $"Sales invoice deletion repair after removing ledger history for invoice {invoice.InvoiceNumber}");
        }
    }

    private async Task<SalesInvoice?> LoadSalesInvoiceForMutationAsync(Guid invoiceId)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        IQueryable<SalesInvoice> query;

        if (IsPostgresProvider())
        {
            query = _context.SalesInvoices
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""SalesInvoices""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {invoiceId}
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
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {invoiceId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters();
        }
        else
        {
            query = _context.SalesInvoices
                .IgnoreQueryFilters()
                .Where(invoice =>
                    invoice.TenantId == tenantId &&
                    invoice.Id == invoiceId &&
                    !invoice.IsDeleted);
        }

        var invoice = await query.FirstOrDefaultAsync();
        if (invoice == null)
        {
            return null;
        }

        await _context.Entry(invoice)
            .Collection(entry => entry.Items)
            .LoadAsync();

        return invoice;
    }

    /// <summary>
    /// Get sale by ID with full details
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<SalesInvoiceResponseDto>> GetSale(Guid id)
    {
        var invoice = await _context.SalesInvoices
            .AsNoTracking()
            .AsSplitQuery()
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
                        .ThenInclude(st => st.Warehouse)
            .Include(s => s.Items)
                .ThenInclude(i => i.Unit)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (invoice == null)
            return NotFound();

        return Ok(invoice.ToResponseDto());
    }

    /// <summary>
    /// Get all sales with pagination and filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<SalesInvoiceResponseDto>>> GetSales(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize);
        var skip = PaginationGuard.GetSkip(page, pageSize);

        var query = _context.SalesInvoices
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
                        .ThenInclude(st => st.Warehouse)
            .Include(s => s.Items)
                .ThenInclude(i => i.Unit)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.ToLower();
            query = query.Where(s => 
                s.InvoiceNumber.ToLower().Contains(search) ||
                (s.Customer != null && s.Customer.Name.ToLower().Contains(search)));
        }

        if (startDate.HasValue)
            query = query.Where(s => s.InvoiceDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(s => s.InvoiceDate <= endDate.Value);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(s => s.InvoiceDate)
            .ThenByDescending(s => s.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResult<SalesInvoiceResponseDto>
        {
            Items = items.Select(item => item.ToResponseDto()).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>
    /// Process a return for a sale
    /// 
    /// VARIANT-CENTRIC: Uses ProductVariantStocks and publishes events
    /// </summary>
    [HttpPost("{id}/return")]
    public ActionResult<SaleResponse> ProcessReturn(Guid id, ReturnRequest request)
    {
        _logger.LogWarning(
            "Legacy return endpoint /api/sales/{InvoiceId}/return was called. This endpoint is retired in favor of /api/salesreturns for hardened refund processing.",
            id);

        return StatusCode(410, new
        {
            message = "Legacy return endpoint is retired. Use /api/salesreturns for server-authoritative refund processing."
        });
#if false
        using var transaction = await _context.Database.BeginTransactionAsync();

        // Track stock changes for durable stock sync outbox staging before commit
        var stockChanges = new List<(Guid VariantId, Guid ProductId, string? SKU, Guid WarehouseId, decimal OldQuantity, decimal NewQuantity)>();

        try
        {
            var originalInvoice = await _context.SalesInvoices
                .Include(s => s.Items)
                    .ThenInclude(i => i.ProductVariant)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (originalInvoice == null)
                return NotFound(new { message = "Invoice not found" });

            // Validate return items
            foreach (var returnItem in request.Items)
            {
                var originalItem = originalInvoice.Items.FirstOrDefault(i => i.ProductVariantId == returnItem.ProductVariantId && i.ProductId == returnItem.ProductId);
                if (originalItem == null)
                    return BadRequest(new { message = $"Item not found in original invoice" });

                if (returnItem.Quantity > originalItem.Quantity)
                    return BadRequest(new { message = $"Cannot return more than sold quantity for item" });
            }

            // Generate return invoice number
            var lastInvoice = await _context.SalesInvoices
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();
            var invoiceNumber = GenerateInvoiceNumber(lastInvoice?.InvoiceNumber);

            // Create return invoice (negative values)
            var returnInvoice = new SalesInvoice
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,
                InvoiceNumber = invoiceNumber + "-R",
                InvoiceDate = DateTime.UtcNow,
                CustomerId = originalInvoice.CustomerId,
                WarehouseId = originalInvoice.WarehouseId,
                Status = InvoiceStatus.Completed,
                PaymentMethod = originalInvoice.PaymentMethod,
                Notes = $"Return for {originalInvoice.InvoiceNumber}: {request.Reason}",
                CreatedAt = DateTime.UtcNow
            };

            decimal subtotal = 0;
            decimal totalTax = 0;

            foreach (var returnItem in request.Items)
            {
                var originalItem = originalInvoice.Items.First(i => i.ProductVariantId == returnItem.ProductVariantId && i.ProductId == returnItem.ProductId);
                
                // Resolve effective VariantId
                Guid effectiveVariantId;
                if (originalItem.ProductVariantId.HasValue)
                {
                    effectiveVariantId = originalItem.ProductVariantId.Value;
                }
                else
                {
                    // Legacy invoice - resolve to default variant
                    var defaultVariant = await _context.Set<ProductVariant>()
                        .FirstOrDefaultAsync(v => v.ProductId == originalItem.ProductId && v.IsDefault && v.IsActive);
                    
                    if (defaultVariant == null)
                    {
                        var product = await _context.Products.FindAsync(originalItem.ProductId);
                        return BadRequest(new { message = $"Cannot resolve variant for legacy product {product?.Name}. No default variant found." });
                    }
                    effectiveVariantId = defaultVariant.Id;
                }
                
                // Get unit information - use original item's unit if not specified
                decimal conversionFactor = originalItem.ConversionFactor;
                Guid? unitId = returnItem.UnitId ?? originalItem.UnitId;
                
                if (returnItem.UnitId.HasValue && returnItem.UnitId != originalItem.UnitId)
                {
                    var productUnit = await _context.ProductUnits
                        .FirstOrDefaultAsync(pu => pu.ProductId == returnItem.ProductId && pu.UnitId == returnItem.UnitId);
                    
                    if (productUnit != null)
                    {
                        conversionFactor = productUnit.ConversionFactor;
                    }
                }

                // Calculate base quantity for the return
                var baseQuantity = returnItem.Quantity * conversionFactor;
                
                // Calculate negative amounts
                var itemSubtotal = returnItem.Quantity * originalItem.UnitPrice;
                var itemTax = originalItem.TaxAmount / originalItem.Quantity * returnItem.Quantity;
                
                var invoiceItem = new SalesInvoiceItem
                {
                    Id = Guid.NewGuid(),
                    SalesInvoiceId = returnInvoice.Id,
                    ProductId = originalItem.ProductId,
                    ProductVariantId = effectiveVariantId,
                    UnitId = unitId,
                    Quantity = -returnItem.Quantity,
                    ConversionFactor = conversionFactor,
                    BaseQuantity = -baseQuantity,
                    UnitPrice = originalItem.UnitPrice,
                    DiscountAmount = 0,
                    TaxRate = originalItem.TaxRate,
                    TaxAmount = -itemTax,
                    TotalAmount = -(itemSubtotal + itemTax),
                    CreatedAt = DateTime.UtcNow
                };

                returnInvoice.Items.Add(invoiceItem);
                subtotal += itemSubtotal;
                totalTax += itemTax;

                // VARIANT-CENTRIC: Update ProductVariantStocks (source of truth)
                var variantStock = await _context.Set<ProductVariantStock>()
                    .FirstOrDefaultAsync(vs => 
                        vs.WarehouseId == originalInvoice.WarehouseId && 
                        vs.VariantId == effectiveVariantId);

                if (variantStock != null)
                {
                    var oldQuantity = variantStock.Quantity;
                    variantStock.Quantity += baseQuantity; // Add base quantity back to stock

                    var variant = await _context.Set<ProductVariant>().FindAsync(effectiveVariantId);
                    stockChanges.Add((effectiveVariantId, originalItem.ProductId, variant?.SKU, originalInvoice.WarehouseId, oldQuantity, variantStock.Quantity));

                    // Stock movement
                    var movement = new StockMovement
                    {
                        Id = Guid.NewGuid(),
                        TenantId = _tenantContext.RequiredTenantId,
                        WarehouseId = originalInvoice.WarehouseId,
                        ProductId = originalItem.ProductId,
                        ProductVariantId = effectiveVariantId,
                        MovementType = StockMovementType.SalesReturn,
                        UnitId = unitId,
                        ConversionFactor = conversionFactor,
                        QuantityChange = baseQuantity,
                        QuantityAfter = variantStock.Quantity,
                        ReferenceId = returnInvoice.Id,
                        ReferenceNumber = returnInvoice.InvoiceNumber,
                        Price = originalItem.UnitPrice / conversionFactor,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.StockMovements.Add(movement);
                }
                else
                {
                    // Create new stock record if it doesn't exist
                    variantStock = new ProductVariantStock
                    {
                        Id = Guid.NewGuid(),
                        TenantId = _tenantContext.RequiredTenantId,
                        VariantId = effectiveVariantId,
                        WarehouseId = originalInvoice.WarehouseId,
                        Quantity = baseQuantity,
                        ReservedQuantity = 0,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Set<ProductVariantStock>().Add(variantStock);

                    var variant = await _context.Set<ProductVariant>().FindAsync(effectiveVariantId);
                    stockChanges.Add((effectiveVariantId, originalItem.ProductId, variant?.SKU, originalInvoice.WarehouseId, 0, variantStock.Quantity));

                    var movement = new StockMovement
                    {
                        Id = Guid.NewGuid(),
                        TenantId = _tenantContext.RequiredTenantId,
                        WarehouseId = originalInvoice.WarehouseId,
                        ProductId = originalItem.ProductId,
                        ProductVariantId = effectiveVariantId,
                        MovementType = StockMovementType.SalesReturn,
                        UnitId = unitId,
                        ConversionFactor = conversionFactor,
                        QuantityChange = baseQuantity,
                        QuantityAfter = variantStock.Quantity,
                        ReferenceId = returnInvoice.Id,
                        ReferenceNumber = returnInvoice.InvoiceNumber,
                        Price = originalItem.UnitPrice / conversionFactor,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.StockMovements.Add(movement);
                }
            }

            returnInvoice.Subtotal = -subtotal;
            returnInvoice.TaxAmount = -totalTax;
            returnInvoice.TotalAmount = -(subtotal + totalTax);
            returnInvoice.PaidAmount = returnInvoice.TotalAmount;

            _context.SalesInvoices.Add(returnInvoice);

            // Update customer balance and ledger if applicable
            if (originalInvoice.CustomerId.HasValue)
            {
                var customer = await _context.Customers.FindAsync(originalInvoice.CustomerId.Value);
                if (customer != null)
                {
                    await _ledgerService.CreateLedgerEntry(
                        Core.Enums.PartyType.Customer,
                        customer.Id,
                        DateTime.UtcNow,
                        TransactionType.SalesReturn,
                        returnInvoice.InvoiceNumber,
                        0,
                        -returnInvoice.TotalAmount,
                        returnInvoice.Id,
                        $"Sales Return for {originalInvoice.InvoiceNumber}: {request.Reason}");

                    // IMPORTANT: BalanceAfter must be assigned AFTER CreateLedgerEntry
                    // because LedgerService updates customer.Balance internally
                    var transaction_record = new CustomerTransaction
                    {
                        Id = Guid.NewGuid(),
                        TenantId = _tenantContext.RequiredTenantId,
                        CustomerId = customer.Id,
                        TransactionDate = DateTime.UtcNow,
                        TransactionType = TransactionType.SalesReturn,
                        DebitAmount = 0,
                        CreditAmount = -returnInvoice.TotalAmount,
                        ReferenceId = returnInvoice.Id,
                        ReferenceNumber = returnInvoice.InvoiceNumber,
                        CreatedAt = DateTime.UtcNow,
                        BalanceAfter = customer.Balance // Set AFTER ledger entry to capture correct balance
                    };
                    _context.CustomerTransactions.Add(transaction_record);
                }
            }

            // ════════════════════════════════════════════════════════════════════════════
            // PHASE LCR: Enqueue to accounting outbox for ledger posting
            // ════════════════════════════════════════════════════════════════════════════
            // Legacy code: infer refund allocation from original payment method
            var refundMethod = returnInvoice.PaymentMethod == PaymentMethod.Cash ? RefundMethod.Cash : RefundMethod.Card;
            var refundAmount = Math.Abs(returnInvoice.TotalAmount);
            await _accountingOutbox.EnqueuePosRefundAsync(
                returnInvoice.TenantId, 
                returnInvoice.Id,
                returnInvoice.Id,
                refundMethod,
                refundMethod == RefundMethod.Cash ? refundAmount : 0m,
                refundMethod == RefundMethod.Card ? refundAmount : 0m,
                null);
            
            // DO NOT mutate balances directly - refund posting is handled through the outbox and ledger.

            await _context.SaveChangesAsync();

            if (stockChanges.Count > 0)
            {
                var pendingStockChanges = stockChanges
                    .Select(change => ProductVariantStockMutationSupport.BuildPendingStockChange(
                        change.VariantId,
                        change.ProductId,
                        change.WarehouseId,
                        change.OldQuantity,
                        change.NewQuantity,
                        "SalesReturn",
                        referenceId: returnInvoice.Id,
                        referenceType: "SalesReturn",
                        sku: change.SKU))
                    .ToList();

                await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                    _context,
                    _tenantContext.RequiredTenantId,
                    pendingStockChanges);
            }

            await transaction.CommitAsync();

            _logger.LogInformation("Return processed for invoice {InvoiceNumber}. Stock restored to ProductVariantStocks.", originalInvoice.InvoiceNumber);

            return Ok(new SaleResponse
            {
                InvoiceId = returnInvoice.Id,
                InvoiceNumber = returnInvoice.InvoiceNumber,
                TotalAmount = returnInvoice.TotalAmount,
                Message = "Return processed successfully"
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error processing return");
            return StatusCode(500, new { message = "An error occurred while processing the return" });
        }
#endif
    }
    [HttpGet("receipt/{id}")]
    public async Task<ActionResult<ReceiptData>> GetReceipt(Guid id)
    {
        var invoice = await _context.SalesInvoices
            .Include(s => s.Customer)
            .Include(s => s.Warehouse)
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (invoice == null)
            return NotFound();

        var receipt = new ReceiptData
        {
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceDate = invoice.InvoiceDate,
            WarehouseName = invoice.Warehouse.Name,
            WarehouseAddress = invoice.Warehouse.Address,
            CustomerName = invoice.Customer?.Name,
            Items = invoice.Items.Select(i => new ReceiptItem
            {
                ProductName = i.Product.Name,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                Discount = i.DiscountAmount,
                Total = i.TotalAmount
            }).ToList(),
            Subtotal = invoice.Subtotal,
            Discount = invoice.DiscountAmount,
            Tax = invoice.TaxAmount,
            Total = invoice.TotalAmount,
            Paid = invoice.PaidAmount,
            Change = invoice.PaidAmount - invoice.TotalAmount,
            PaymentMethod = invoice.PaymentMethod.ToString()
        };

        return Ok(receipt);
    }

    private void AddPaymentAttemptHeader(string? paymentAttemptId)
    {
        if (!string.IsNullOrWhiteSpace(paymentAttemptId))
        {
            Response.Headers[PaymentAttemptIdHeader] = paymentAttemptId;
        }
    }

    private void AddPosCartIdHeader(string? posCartId)
    {
        if (!string.IsNullOrWhiteSpace(posCartId))
        {
            Response.Headers[PosCartIdHeader] = posCartId;
        }
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal ResolveSaleCostPriceSnapshot(ProductVariant variant) =>
        Math.Round(variant.CostPrice, 6, MidpointRounding.AwayFromZero);

    private static decimal ResolveSaleCostAmountSnapshot(decimal costPriceSnapshot, decimal baseQuantity) =>
        RoundCurrency(costPriceSnapshot * baseQuantity);

    private static bool IsExpectedSaleMutationVersion(SalesInvoice invoice, int? expectedMutationVersion) =>
        !expectedMutationVersion.HasValue ||
        expectedMutationVersion.Value == invoice.AccountingMutationVersion;

    private static decimal ResolveCustomerLedgerPaymentAmount(SalesInvoice invoice)
    {
        var paidAmount = RoundCurrency(invoice.PaidAmount);
        var totalAmount = RoundCurrency(invoice.TotalAmount);

        if (paidAmount <= 0 || totalAmount <= 0)
        {
            return 0m;
        }

        return Math.Min(paidAmount, totalAmount);
    }

    private static void ApplySaleTotals(
        SalesInvoice invoice,
        decimal subtotal,
        decimal lineDiscountTotal,
        decimal totalTax,
        CreateSaleRequest request)
    {
        var roundedSubtotal = RoundCurrency(subtotal);
        var roundedLineDiscountTotal = RoundCurrency(lineDiscountTotal);
        var discountableSubtotal = RoundCurrency(roundedSubtotal - roundedLineDiscountTotal);

        if (discountableSubtotal < 0)
        {
            throw new ValidationException("Invalid sale discounts");
        }

        var invoiceDiscount = request.DiscountPercentage > 0
            ? RoundCurrency(discountableSubtotal * request.DiscountPercentage / 100)
            : RoundCurrency(request.DiscountAmount);

        if (invoiceDiscount < 0 || invoiceDiscount > discountableSubtotal)
        {
            throw new ValidationException("Invalid sale discount");
        }

        invoice.Subtotal = roundedSubtotal;
        invoice.DiscountAmount = invoiceDiscount;
        invoice.DiscountPercentage = request.DiscountPercentage;
        invoice.TaxAmount = RoundCurrency(totalTax);
        invoice.TotalAmount = RoundCurrency(discountableSubtotal - invoiceDiscount + invoice.TaxAmount);
    }

    private static decimal ResolveAuthoritativeSaleUnitPrice(
        ProductVariant variant,
        decimal conversionFactor,
        ProductUnit? productUnit)
    {
        var configuredUnitPrice = productUnit?.SellingPrice;
        if (configuredUnitPrice.HasValue)
        {
            return RoundCurrency(configuredUnitPrice.Value);
        }

        return RoundCurrency(variant.Price * conversionFactor);
    }

    private static void EnsureSupportedPaymentMethod(PaymentMethod paymentMethod)
    {
        if (paymentMethod != PaymentMethod.Mixed
            && paymentMethod != PaymentMethod.Cash
            && paymentMethod != PaymentMethod.Card)
        {
            throw new ValidationException("Unsupported payment method");
        }
    }

    private static (decimal PaidAmount, decimal? CashAmount, decimal? CardAmount) ValidateAndNormalizePaymentAmounts(
        PaymentMethod paymentMethod,
        decimal totalAmount,
        decimal paidAmount,
        decimal? cashAmount,
        decimal? cardAmount)
    {
        EnsureSupportedPaymentMethod(paymentMethod);

        var roundedPaidAmount = RoundCurrency(paidAmount);
        var roundedTotalAmount = RoundCurrency(totalAmount);
        var normalizedCashAmount = RoundCurrency(cashAmount ?? 0);
        var normalizedCardAmount = RoundCurrency(cardAmount ?? 0);

        if (roundedTotalAmount < 0 || roundedPaidAmount < 0)
        {
            throw new ValidationException("Invalid payment amount");
        }

        if (paymentMethod == PaymentMethod.Mixed)
        {
            if (normalizedCashAmount < 0 || normalizedCardAmount < 0)
            {
                throw new ValidationException("Invalid payment split");
            }

            decimal totalPaid;
            try
            {
                totalPaid = RoundCurrency(checked(normalizedCashAmount + normalizedCardAmount));
            }
            catch (OverflowException)
            {
                throw new ValidationException("Invalid payment split");
            }

            if (Math.Abs(totalPaid - roundedTotalAmount) > 0.01m)
            {
                throw new ValidationException("Invalid payment split");
            }

            return (totalPaid, normalizedCashAmount, normalizedCardAmount);
        }

        if (paymentMethod == PaymentMethod.Cash)
        {
            if (normalizedCardAmount != 0)
            {
                throw new ValidationException("Invalid cash payment");
            }

            if (cashAmount.HasValue && normalizedCashAmount != roundedPaidAmount)
            {
                throw new ValidationException("Invalid cash payment");
            }

            if (roundedPaidAmount < roundedTotalAmount)
            {
                throw new ValidationException("Invalid cash payment");
            }

            return (roundedPaidAmount, roundedPaidAmount, RoundCurrency(0));
        }

        if (paymentMethod == PaymentMethod.Card)
        {
            if (normalizedCashAmount != 0)
            {
                throw new ValidationException("Invalid card payment");
            }

            if (cardAmount.HasValue && normalizedCardAmount != roundedPaidAmount)
            {
                throw new ValidationException("Invalid card payment");
            }

            if (roundedPaidAmount != roundedTotalAmount)
            {
                throw new ValidationException("Invalid card payment");
            }

            return (roundedPaidAmount, RoundCurrency(0), roundedPaidAmount);
        }

        throw new ValidationException("Unsupported payment method");
    }

    private async Task<string> GenerateInvoiceNumberAsync()
    {
        var timestampUtc = DateTime.UtcNow;
        var sequenceValue = await TryGetNextInvoiceSequenceAsync();
        if (sequenceValue.HasValue)
        {
            return $"INV-{timestampUtc:yyyyMMdd}-{sequenceValue.Value:D4}";
        }

        await LockTenantForInvoiceNumberAsync(_tenantContext.RequiredTenantId);

        var prefix = $"INV-{timestampUtc:yyyyMMdd}";
        
        // Query all invoices with the prefix and extract the maximum valid suffix
        var invoiceNumbers = await _context.SalesInvoices
            .Where(invoice =>
                invoice.TenantId == _tenantContext.RequiredTenantId &&
                invoice.InvoiceNumber.StartsWith(prefix))
            .Select(invoice => invoice.InvoiceNumber)
            .ToListAsync();

        // Find the maximum valid suffix from all matching invoice numbers
        var maxSuffix = 0;
        foreach (var invoiceNumber in invoiceNumbers)
        {
            var parts = invoiceNumber.Split('-');
            if (parts.Length >= 3)
            {
                var suffixPart = parts[^1]; // Last part after the prefix
                if (int.TryParse(suffixPart, out var suffix))
                {
                    maxSuffix = Math.Max(maxSuffix, suffix);
                }
            }
        }

        return GenerateInvoiceNumberFromMaxSuffix(prefix, maxSuffix);
    }

    private async Task<long?> TryGetNextInvoiceSequenceAsync()
    {
        if (!IsPostgresProvider())
        {
            return null;
        }

        var connection = _context.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT nextval('\"{SalesInvoiceNumberSequenceName}\"')";

            var currentTransaction = _context.Database.CurrentTransaction;
            if (currentTransaction != null)
            {
                command.Transaction = currentTransaction.GetDbTransaction();
            }

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }
        catch (PostgresException ex) when (IsMissingInvoiceSequence(ex))
        {
            _logger.LogWarning(
                ex,
                "Sales invoice number sequence {SequenceName} is missing. Falling back to locked invoice-number generation.",
                SalesInvoiceNumberSequenceName);
            return null;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task LockTenantForInvoiceNumberAsync(Guid tenantId)
    {
        Tenant? tenant;

        if (IsPostgresProvider())
        {
            tenant = await _context.Tenants
                .FromSqlInterpolated($@"SELECT * FROM ""Tenants"" WHERE ""Id"" = {tenantId} FOR UPDATE")
                .FirstOrDefaultAsync();
        }
        else if (IsSqlServerProvider())
        {
            tenant = await _context.Tenants
                .FromSqlInterpolated($@"SELECT * FROM [Tenants] WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [Id] = {tenantId}")
                .FirstOrDefaultAsync();
        }
        else
        {
            tenant = await _context.Tenants.FirstOrDefaultAsync(entity => entity.Id == tenantId);
        }

        if (tenant == null)
        {
            throw new ValidationException("Tenant not found");
        }
    }

    private async Task<ProductVariantStock?> LoadProductVariantStockForUpdateAsync(Guid warehouseId, Guid variantId)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        IQueryable<ProductVariantStock> query;

        if (IsPostgresProvider())
        {
            query = _context.ProductVariantStocks
                .FromSqlInterpolated($@"
                    SELECT * FROM ""ProductVariantStocks""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""WarehouseId"" = {warehouseId}
                      AND ""VariantId"" = {variantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                // SECURITY: IgnoreQueryFilters is intentional because the lock query
                // already reapplies TenantId/VariantId/WarehouseId inside SQL and must
                // bypass the global filter to load the locked row shape correctly.
                .IgnoreQueryFilters();
        }
        else if (IsSqlServerProvider())
        {
            query = _context.ProductVariantStocks
                .FromSqlInterpolated($@"
                    SELECT * FROM [ProductVariantStocks] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [WarehouseId] = {warehouseId}
                      AND [VariantId] = {variantId}
                      AND [IsDeleted] = 0")
                // SECURITY: IgnoreQueryFilters is intentional because tenant scoping
                // is enforced inside the lock SQL before the row is materialized.
                .IgnoreQueryFilters();
        }
        else
        {
            query = _context.ProductVariantStocks
                // SECURITY: IgnoreQueryFilters is intentional here only to bypass the
                // global filter while the explicit tenant predicate is reapplied below.
                .IgnoreQueryFilters()
                .Where(stock =>
                    stock.TenantId == tenantId &&
                    stock.WarehouseId == warehouseId &&
                    stock.VariantId == variantId &&
                    !stock.IsDeleted);
        }

        return await query.FirstOrDefaultAsync();
    }

    private static bool IsMissingInvoiceSequence(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UndefinedTable ||
        exception.SqlState == "42704";

    private string GenerateInvoiceNumber(string? lastInvoiceNumber)
    {
        return GenerateInvoiceNumber(lastInvoiceNumber, DateTime.UtcNow);
    }

    private static string GenerateInvoiceNumber(string? lastInvoiceNumber, DateTime timestampUtc)
    {
        var prefix = $"INV-{timestampUtc:yyyyMMdd}";
        
        if (string.IsNullOrEmpty(lastInvoiceNumber) || !lastInvoiceNumber.StartsWith(prefix))
        {
            return $"{prefix}-0001";
        }

        var suffix = lastInvoiceNumber.Split('-').Last();
        if (!int.TryParse(suffix, out var lastNumber))
        {
            // Malformed suffix - default to 0 to start fresh sequence
            lastNumber = 0;
        }
        var nextNumber = DocumentNumberingSupport.IncrementLegacyNumber(lastNumber);
        return $"{prefix}-{nextNumber:D4}";
    }

    private static string GenerateInvoiceNumberFromMaxSuffix(string prefix, int maxSuffix)
    {
        var nextNumber = DocumentNumberingSupport.IncrementLegacyNumber(maxSuffix);
        return $"{prefix}-{nextNumber:D4}";
    }

}

// DTOs
public record CreateSaleRequest
{
    public Guid? CustomerId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? SalesRepresentativeId { get; set; }
    public int? ExpectedMutationVersion { get; set; }
    [Required]
    [MinLength(1)]
    public List<SaleItemRequest> Items { get; set; } = new();
    [Range(typeof(decimal), "0", "100")]
    public decimal DiscountPercentage { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal DiscountAmount { get; set; }
    [EnumDataType(typeof(PaymentMethod))]
    public PaymentMethod PaymentMethod { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal PaidAmount { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? CashAmount { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? CardAmount { get; set; }
    [MaxLength(1000)]
    public string? Notes { get; set; }
}

public record SaleItemRequest
{
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public Guid? UnitId { get; set; } // Unit selected for this sale line
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal Quantity { get; set; } // Quantity in the selected unit
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal UnitPrice { get; set; } // Price per selected unit
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal DiscountAmount { get; set; }
    [Range(typeof(decimal), "0", "100")]
    public decimal TaxRate { get; set; }
}

public record SaleResponse
{
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal ChangeAmount { get; set; }
    public string Message { get; set; } = string.Empty;
}

public record ReceiptData
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public string? WarehouseName { get; set; }
    public string? WarehouseAddress { get; set; }
    public string? CustomerName { get; set; }
    public List<ReceiptItem> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public decimal Change { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
}

public record ReceiptItem
{
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
}

public record ReturnRequest
{
    public List<ReturnItemRequest> Items { get; set; } = new();
    public string? Reason { get; set; }
}

public record ReturnItemRequest
{
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public Guid? UnitId { get; set; } // Unit for the return
    public decimal Quantity { get; set; } // Quantity in the selected unit
}
