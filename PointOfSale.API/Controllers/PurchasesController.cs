using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Inventory;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Infrastructure.DocumentNumbers;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Inventory;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Core.Pricing;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.API.DTOs;
using PointOfSale.API.Mappers;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PurchasesController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string RequestIdHeader = "X-Request-Id";
    private const string PurchaseAttemptIdHeader = "X-Payment-Attempt-Id";
    private const string PurchaseInvoiceSequenceName = "purchase_invoice_seq";
    private const int MaxPurchasePageSize = 100;
    private static readonly JsonSerializerOptions PurchaseIdempotencyJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<PurchasesController> _logger;
    private readonly PointOfSale.API.Services.ILedgerService _ledgerService;
    private readonly PointOfSale.API.Services.IPartyBalanceMaintenanceService _partyBalanceMaintenanceService;
    private readonly PointOfSale.API.Services.IValidationService _validationService;
    private readonly PointOfSale.API.Services.Audit.IAuditService _auditService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IAccountingOutboxService _accountingOutbox;
    private readonly ICommerceAccountingService _commerceAccounting;
    private readonly IFinancialPostingService _postingService;
    private readonly IAccountResolver _accountResolver;
    private readonly ITreasuryReadService _treasuryReadService;
    private readonly ICashboxLedgerAccountService _cashboxLedgerAccountService;
    private readonly PointOfSale.API.Services.IPurchaseFinancialIntegrityService _purchaseFinancialIntegrityService;

    private bool IsPostgresProvider() => _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    private bool IsSqlServerProvider() => _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    public PurchasesController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<PurchasesController> logger, 
        PointOfSale.API.Services.ILedgerService ledgerService,
        PointOfSale.API.Services.IPartyBalanceMaintenanceService partyBalanceMaintenanceService,
        PointOfSale.API.Services.IValidationService validationService,
        PointOfSale.API.Services.Audit.IAuditService auditService,
        IEventPublisher eventPublisher,
        IAccountingOutboxService accountingOutbox,
        ICommerceAccountingService commerceAccounting,
        IFinancialPostingService postingService,
        IAccountResolver accountResolver,
        ITreasuryReadService treasuryReadService,
        ICashboxLedgerAccountService cashboxLedgerAccountService,
        PointOfSale.API.Services.IPurchaseFinancialIntegrityService purchaseFinancialIntegrityService)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
        _ledgerService = ledgerService;
        _partyBalanceMaintenanceService = partyBalanceMaintenanceService;
        _validationService = validationService;
        _auditService = auditService;
        _eventPublisher = eventPublisher;
        _accountingOutbox = accountingOutbox;
        _commerceAccounting = commerceAccounting;
        _postingService = postingService;
        _accountResolver = accountResolver;
        _treasuryReadService = treasuryReadService;
        _cashboxLedgerAccountService = cashboxLedgerAccountService;
        _purchaseFinancialIntegrityService = purchaseFinancialIntegrityService;
    }

    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<PurchaseResponse>> CreatePurchase(CreatePurchaseRequest request)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var claim = await TryClaimPurchaseIdempotencyAsync("CREATE", null, request);
        if (claim.ShortCircuitResult != null)
        {
            await transaction.RollbackAsync();
            return claim.ShortCircuitResult;
        }

        var idempotencyRecord = claim.Record!;
        var pendingStockChanges = new List<PendingProductVariantStockChange>();

        try
        {
            // Validate supplier
            var supplier = await _context.Suppliers.FindAsync(request.SupplierId);
            if (supplier == null)
                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new { message = "Supplier not found" }));

            // Validate warehouse
            var warehouse = await _context.Warehouses.FindAsync(request.WarehouseId);
            if (warehouse == null || !warehouse.IsActive)
                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new { message = "Active warehouse not found" }));

            var preparedPurchaseItemsResult = await PreparePurchaseItemsAsync(request.Items);
            if (preparedPurchaseItemsResult.ErrorResult != null)
            {
                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    preparedPurchaseItemsResult.ErrorResult);
            }

            var preparedPurchaseItems = preparedPurchaseItemsResult.Items!;
            await LockPurchaseCostProductsAsync(preparedPurchaseItems.Select(item => item.Product.Id));
            await ReloadPurchaseCostAuthoritiesAsync(preparedPurchaseItems);
            var purchaseFinancials = CalculatePurchaseFinancialSummary(preparedPurchaseItems);
            var paymentSplit = ResolvePurchasePaymentSplit(request, purchaseFinancials.TotalAmount);
            if (!paymentSplit.Success)
            {
                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new { message = paymentSplit.ErrorMessage }));
            }

            SupplierCardAuthoritySnapshot? cardAuthority;
            try
            {
                cardAuthority = await ResolvePurchaseCardAuthorityAsync(request, paymentSplit.CardAmount);
            }
            catch (InvalidOperationException ex)
            {
                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new { message = ex.Message, code = "SUPPLIER_CARD_AUTHORITY_INVALID" }));
            }

            var cashboxValidationError = await ValidateCashboxCapacityAsync(
                warehouse.Id,
                paymentSplit.CashAmount,
                transaction,
                idempotencyRecord);
            if (cashboxValidationError != null)
            {
                return cashboxValidationError;
            }

            // PostgreSQL uses a database sequence so concurrent purchase requests
            // cannot ever observe the same "last invoice" and reuse the number.
            var invoiceNumber = await GenerateInvoiceNumberAsync();

            var invoice = new PurchaseInvoice
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                InvoiceNumber = invoiceNumber,
                InvoiceDate = request.InvoiceDate.Kind == DateTimeKind.Unspecified 
                    ? DateTime.SpecifyKind(request.InvoiceDate, DateTimeKind.Utc) 
                    : request.InvoiceDate.ToUniversalTime(),
                SupplierId = request.SupplierId,
                WarehouseId = request.WarehouseId,
                Status = InvoiceStatus.Completed,
                PaymentMethod = request.PaymentMethod,
                PaidAmount = paymentSplit.PaidAmount,
                DueDate = request.DueDate?.Kind == DateTimeKind.Unspecified 
                    ? DateTime.SpecifyKind(request.DueDate.Value, DateTimeKind.Utc) 
                    : request.DueDate?.ToUniversalTime(),
                Notes = request.Notes,
                CreatedAt = DateTime.UtcNow
            };
            AddPurchasePayments(invoice, paymentSplit.CashAmount, paymentSplit.CardAmount, cardAuthority);

            var lockedStocks = await ProductVariantStockMutationSupport.LockMultipleStocksAsync(
                _context,
                _tenantContext.RequiredTenantId,
                preparedPurchaseItems
                    .Select(item => new StockLockTarget(request.WarehouseId, item.Variant.Id, CreateIfMissing: true))
                    .DistinctBy(target => (target.WarehouseId, target.VariantId)));
            foreach (var purchaseItem in purchaseFinancials.Items)
            {
                var preparedItem = purchaseItem.PreparedItem;
                var itemRequest = preparedItem.Request;
                var product = preparedItem.Product;
                var variant = preparedItem.Variant;
                var unitId = preparedItem.UnitId;
                var conversionFactor = preparedItem.ConversionFactor;
                var baseQuantity = preparedItem.BaseQuantity;

                var baseUnitCost = itemRequest.UnitCost / conversionFactor;

                invoice.Items.Add(new PurchaseInvoiceItem
                {
                    Id = Guid.NewGuid(),
                    PurchaseInvoiceId = invoice.Id,
                    ProductId = itemRequest.ProductId,
                    ProductVariantId = variant.Id,
                    UnitId = unitId,
                    Quantity = itemRequest.Quantity,
                    ConversionFactor = conversionFactor,
                    BaseQuantity = baseQuantity,
                    UnitPrice = itemRequest.UnitCost,
                    TaxRate = itemRequest.TaxRate,
                    TaxAmount = purchaseItem.TaxAmount,
                    TotalAmount = purchaseItem.TotalAmount,
                    CreatedAt = DateTime.UtcNow
                });

                var variantStock = lockedStocks[(request.WarehouseId, variant.Id)];
                var oldQuantity = variantStock.Quantity;
                variant.CostPrice = VariantCostAuthority.ApplyPurchaseReceipt(
                    oldQuantity,
                    variant.CostPrice,
                    baseQuantity,
                    baseUnitCost);
                MirrorSimpleProductPurchasePrice(product, variant);
                variantStock.Quantity += baseQuantity;
                variantStock.UpdatedAt = DateTime.UtcNow;

                _context.StockMovements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.RequiredTenantId,
                    WarehouseId = request.WarehouseId,
                    ProductId = itemRequest.ProductId,
                    ProductVariantId = variant.Id,
                    MovementType = StockMovementType.Purchase,
                    UnitId = unitId,
                    ConversionFactor = conversionFactor,
                    QuantityChange = baseQuantity,
                    QuantityAfter = variantStock.Quantity,
                    AvailableAfter = variantStock.AvailableQuantity,
                    ReservedAfter = variantStock.ReservedQuantity,
                    DamagedAfter = variantStock.DamagedQuantity,
                    ReferenceId = invoice.Id,
                    ReferenceNumber = invoiceNumber,
                    Price = baseUnitCost,
                    CreatedAt = DateTime.UtcNow
                });

                pendingStockChanges.Add(ProductVariantStockMutationSupport.BuildPendingStockChange(
                    variant.Id,
                    itemRequest.ProductId,
                    request.WarehouseId,
                    oldQuantity,
                    variantStock.Quantity,
                    "Purchase",
                    referenceId: invoice.Id,
                    referenceType: "PurchaseInvoice",
                    sku: variant.SKU,
                    warehouseName: warehouse.Name));
            }

            invoice.Subtotal = purchaseFinancials.Subtotal;
            invoice.TaxAmount = purchaseFinancials.TaxAmount;
            invoice.TotalAmount = purchaseFinancials.TotalAmount;
            // invoice.RemainingAmount is computed

            _context.PurchaseInvoices.Add(invoice);

            // Always create ledger entry for the full purchase amount (we owe supplier)
            var purchaseBalanceAfter = await _ledgerService.ApplySupplierTransaction(
                supplier.Id,
                DateTime.UtcNow,
                TransactionType.Purchase,
                invoiceNumber,
                0,
                invoice.TotalAmount, // Credit balance - we owe supplier the full amount
                invoice.Id,
                $"Purchase Invoice {invoiceNumber}");

            var purchaseTransaction = new SupplierTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                SupplierId = supplier.Id,
                TransactionDate = DateTime.UtcNow,
                TransactionType = TransactionType.Purchase,
                DebitAmount = 0,
                CreditAmount = invoice.TotalAmount,
                ReferenceId = invoice.Id,
                ReferenceNumber = invoiceNumber,
                CreatedAt = DateTime.UtcNow,
                BalanceAfter = purchaseBalanceAfter
            };
            _context.SupplierTransactions.Add(purchaseTransaction);

            // If payment was made, record it as a separate transaction
            if (invoice.PaidAmount > 0)
            {
                var paymentNote = $"Payment for Purchase Invoice {invoiceNumber} - {invoice.PaymentMethod}";
                
                var paymentBalanceAfter = await _ledgerService.ApplySupplierTransaction(
                    supplier.Id,
                    DateTime.UtcNow,
                    TransactionType.PaymentMade,
                    invoiceNumber,
                    invoice.PaidAmount, // Debit balance - reduces the payable
                    0,
                    invoice.Id,
                    paymentNote);

                var paymentTransaction = new SupplierTransaction
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                    SupplierId = supplier.Id,
                    TransactionDate = DateTime.UtcNow,
                    TransactionType = TransactionType.PaymentMade,
                    DebitAmount = invoice.PaidAmount,
                    CreditAmount = 0,
                    ReferenceId = invoice.Id,
                    ReferenceNumber = invoiceNumber,
                    CreatedAt = DateTime.UtcNow,
                    BalanceAfter = paymentBalanceAfter
                };
                _context.SupplierTransactions.Add(paymentTransaction);
            }

            // ════════════════════════════════════════════════════════════════════════════
            // PHASE LCR: Enqueue to accounting outbox for ledger posting
            // ════════════════════════════════════════════════════════════════════════════
            await _accountingOutbox.EnqueuePosPurchaseWithCardAuthorityAsync(
                invoice.TenantId,
                invoice.Id,
                invoice.AccountingMutationVersion,
                paymentSplit.CashAmount,
                paymentSplit.CardAmount,
                cardAuthority?.FundingInstrumentId,
                cardAuthority?.FundingLedgerAccountId,
                cardAuthority?.FundingInstrumentVersion);

            var purchasePosting = await _commerceAccounting.PostPosPurchaseAsync(
                invoice,
                HttpContext.RequestAborted,
                operationKey: null,
                cashAmount: paymentSplit.CashAmount,
                cardAmount: paymentSplit.CardAmount,
                fundingInstrumentId: cardAuthority?.FundingInstrumentId,
                fundingLedgerAccountId: cardAuthority?.FundingLedgerAccountId,
                fundingInstrumentVersion: cardAuthority?.FundingInstrumentVersion);
            if (!purchasePosting.Success)
            {
                throw new InvalidOperationException(
                    $"POS purchase accounting post failed for invoice {invoiceNumber}: {purchasePosting.ErrorMessage}");
            }

            await MarkSynchronousPurchaseAccountingOutboxProcessedAsync(
                invoice.TenantId,
                invoice.Id,
                "PosPurchase",
                BuildBasePurchaseAccountingOutboxIdempotencyKey(
                    invoice.TenantId,
                    invoice.Id,
                    paymentSplit.CashAmount,
                    paymentSplit.CardAmount,
                    invoice.AccountingMutationVersion,
                    cardAuthority),
                invoice.AccountingMutationVersion,
                purchasePosting.JournalId,
                HttpContext.RequestAborted);
            
            // ════════════════════════════════════════════════════════════════════════════
            // LEGACY CODE - COMMENTED OUT (PHASE LCR)
            // ════════════════════════════════════════════════════════════════════════════
            // DO NOT modify balances directly - treasury posting is handled through the outbox.

            await _context.SaveChangesAsync();
            idempotencyRecord.PurchaseInvoiceId = invoice.Id;

            await _auditService.LogCreate("PurchaseInvoice", invoice.Id.ToString(), $"Created Purchase Invoice {invoiceNumber}", new { TotalAmount = invoice.TotalAmount, SupplierId = invoice.SupplierId }, invoiceNumber);

            if (pendingStockChanges.Count > 0)
            {
                await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                    _context,
                    _tenantContext.RequiredTenantId,
                    pendingStockChanges);
            }

            var result = await CompletePurchaseIdempotencyAsync(
                transaction,
                idempotencyRecord,
                Ok(new PurchaseResponse
                {
                    InvoiceId = invoice.Id,
                    InvoiceNumber = invoiceNumber,
                    TotalAmount = invoice.TotalAmount,
                    MutationVersion = invoice.AccountingMutationVersion,
                    Message = "Purchase completed successfully"
                }));

            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "Error creating purchase");
            return StatusCode(500, new { message = "An unexpected error occurred while processing the purchase." });
        }
    }

    private async Task<(PurchaseRequestIdempotency? Record, ActionResult<PurchaseResponse>? ShortCircuitResult)> TryClaimPurchaseIdempotencyAsync(
        string operation,
        Guid? purchaseInvoiceId,
        CreatePurchaseRequest request)
    {
        var resolution = ResolvePurchaseIdempotencyContext();
        if (resolution.ErrorResult != null)
        {
            return (null, resolution.ErrorResult);
        }

        var tenantId = _tenantContext.RequiredTenantId;
        var fingerprint = BuildPurchaseRequestFingerprint(operation, purchaseInvoiceId, request);
        var scopedIdempotencyKey = BuildSupplierMutationIdempotencyKey(resolution.IdempotencyKey!);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claimId = Guid.NewGuid();
            var nowUtc = DateTime.UtcNow;
            
            int rowsInserted;
            
            if (IsPostgresProvider())
            {
                rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO ""PurchaseRequestIdempotency""
                    (""Id"", ""TenantId"", ""Key"", ""Operation"", ""PurchaseInvoiceId"", ""PurchaseAttemptId"", ""RequestFingerprint"", ""Status"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                    VALUES
                    ({claimId}, {tenantId}, {scopedIdempotencyKey}, {operation}, {purchaseInvoiceId}, {resolution.PurchaseAttemptId}, {fingerprint}, {PurchaseRequestIdempotencyStatus.InProgress}, {nowUtc}, {nowUtc}, {false})
                    ON CONFLICT (""TenantId"", ""Key"", ""Operation"") DO NOTHING");
            }
            else if (IsSqlServerProvider())
            {
                rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO [PurchaseRequestIdempotency]
                        ([Id], [TenantId], [Key], [Operation], [PurchaseInvoiceId], [PurchaseAttemptId], [RequestFingerprint], [Status], [CreatedAt], [UpdatedAt], [IsDeleted])
                    SELECT
                        {claimId}, {tenantId}, {scopedIdempotencyKey}, {operation}, {purchaseInvoiceId}, {resolution.PurchaseAttemptId}, {fingerprint}, {PurchaseRequestIdempotencyStatus.InProgress}, {nowUtc}, {nowUtc}, {false}
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM [PurchaseRequestIdempotency] WITH (UPDLOCK, HOLDLOCK)
                        WHERE [TenantId] = {tenantId}
                          AND [Key] = {scopedIdempotencyKey}
                          AND [Operation] = {operation})");
            }
            else
            {
                throw new NotSupportedException($"Database provider '{_context.Database.ProviderName}' is not supported for purchase idempotency.");
            }

            if (rowsInserted > 0)
            {
                var claimedRecord = await _context.PurchaseRequestIdempotencyRecords
                    .FirstAsync(record =>
                        record.TenantId == tenantId &&
                        record.Key == scopedIdempotencyKey &&
                        record.Operation == operation);

                return (claimedRecord, null);
            }

            var existingRecord = await _context.PurchaseRequestIdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(record =>
                    record.TenantId == tenantId &&
                    record.Key == scopedIdempotencyKey &&
                    record.Operation == operation);

            if (existingRecord == null)
            {
                continue;
            }

            if (!string.Equals(existingRecord.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return (null, Conflict(new
                {
                    message = "Idempotency-Key was already used for a different purchase request."
                }));
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                return (null, BuildStoredPurchaseResponse(existingRecord));
            }

            return (null, Conflict(new
            {
                message = "A purchase request with the same idempotency key is already being processed."
            }));
        }

        return (null, Conflict(new
        {
            message = "A purchase request with the same idempotency key is already being processed."
        }));
    }

    private async Task<ActionResult<PurchaseResponse>> CompletePurchaseIdempotencyAsync(
        IDbContextTransaction transaction,
        PurchaseRequestIdempotency record,
        ActionResult<PurchaseResponse> result)
    {
        var snapshot = SnapshotPurchaseActionResult(result);

        record.Status = PurchaseRequestIdempotencyStatus.Completed;
        record.ResponseStatusCode = snapshot.StatusCode;
        record.ResponseJson = snapshot.ResponseJson;
        record.CompletedAtUtc = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return result;
    }

    private (string? IdempotencyKey, string? PurchaseAttemptId, ActionResult<PurchaseResponse>? ErrorResult) ResolvePurchaseIdempotencyContext()
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
                message = $"{IdempotencyKeyHeader} header is required for purchase create/update requests."
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

    private static string BuildPurchaseRequestFingerprint(string operation, Guid? purchaseInvoiceId, CreatePurchaseRequest request)
    {
        return DeterministicRequestHasher.ComputeHash(new
        {
            operation,
            purchaseInvoiceId,
            request,
        });
    }

    private static PurchaseActionSnapshot SnapshotPurchaseActionResult(ActionResult<PurchaseResponse> result)
    {
        if (result.Result is ObjectResult objectResult)
        {
            return new PurchaseActionSnapshot(
                objectResult.StatusCode ?? StatusCodes.Status200OK,
                SerializePurchasePayload(objectResult.Value));
        }

        if (result.Result is StatusCodeResult statusCodeResult)
        {
            return new PurchaseActionSnapshot(statusCodeResult.StatusCode, null);
        }

        return new PurchaseActionSnapshot(
            StatusCodes.Status200OK,
            SerializePurchasePayload(result.Value));
    }

    private static ActionResult<PurchaseResponse> BuildStoredPurchaseResponse(PurchaseRequestIdempotency record)
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

    private static string? SerializePurchasePayload(object? payload)
    {
        return payload == null
            ? null
            : JsonSerializer.Serialize(payload, PurchaseIdempotencyJsonOptions);
    }

    private async Task<(List<PreparedPurchaseItem>? Items, ActionResult<PurchaseResponse>? ErrorResult)> PreparePurchaseItemsAsync(
        IEnumerable<PurchaseItemRequest> requestItems,
        CancellationToken cancellationToken = default)
    {
        var preparedItems = new List<PreparedPurchaseItem>();

        foreach (var itemRequest in requestItems)
        {
            var product = await _context.Products.FindAsync(new object[] { itemRequest.ProductId }, cancellationToken);
            if (product == null)
            {
                return (null, BadRequest(new
                {
                    message = $"Product not found: {itemRequest.ProductId}"
                }));
            }

            decimal conversionFactor = 1;
            Guid? unitId = itemRequest.UnitId;

            if (!product.HasVariants)
            {
                await _context.ProductUnits
                    .FirstOrDefaultAsync(
                        pu => pu.ProductId == itemRequest.ProductId && pu.IsDefault && !pu.IsDeleted,
                        cancellationToken);
            }

            if (itemRequest.UnitId.HasValue)
            {
                var productUnit = await _context.ProductUnits
                    .FirstOrDefaultAsync(
                        pu => pu.ProductId == itemRequest.ProductId && pu.UnitId == itemRequest.UnitId,
                        cancellationToken);
                if (productUnit == null)
                {
                    return (null, BadRequest(new
                    {
                        message = $"Unit is not bound to product {product.Name}."
                    }));
                }

                conversionFactor = productUnit.ConversionFactor;
            }
            else
            {
                var defaultUnit = await _context.ProductUnits
                    .FirstOrDefaultAsync(
                        pu => pu.ProductId == itemRequest.ProductId && pu.IsDefault,
                        cancellationToken);
                if (defaultUnit != null)
                {
                    unitId = defaultUnit.UnitId;
                    conversionFactor = defaultUnit.ConversionFactor;
                }
            }

            var variant = await ProductVariantStockMutationSupport.ResolveVariantAsync(
                _context,
                itemRequest.ProductId,
                itemRequest.ProductVariantId,
                cancellationToken);

            if (variant == null)
            {
                return (null, BadRequest(new
                {
                    message = $"No variant found for product {product.Name}. Please select a variant."
                }));
            }

            if (itemRequest.UnitCost <= 0m)
            {
                return (null, BadRequest(new
                {
                    message = $"Unit cost must be greater than zero for product {product.Name}."
                }));
            }

            preparedItems.Add(new PreparedPurchaseItem(
                itemRequest,
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

    private async Task LockPurchaseCostProductsAsync(IEnumerable<Guid> productIds)
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
                throw new InvalidOperationException($"Purchase cost product {productId} is no longer authoritative.");
            }
        }
    }

    private async Task ReloadPurchaseCostAuthoritiesAsync(IEnumerable<PreparedPurchaseItem> preparedItems)
    {
        foreach (var product in preparedItems
                     .Select(item => item.Product)
                     .DistinctBy(product => product.Id)
                     .OrderBy(product => product.Id))
        {
            await _context.Entry(product).ReloadAsync();
        }

        foreach (var variant in preparedItems
                     .Select(item => item.Variant)
                     .DistinctBy(variant => variant.Id)
                     .OrderBy(variant => variant.Id))
        {
            await _context.Entry(variant).ReloadAsync();
        }
    }

    private void MirrorSimpleProductPurchasePrice(Product product, ProductVariant variant)
    {
        if (product.HasVariants)
        {
            return;
        }

        var defaultUnit = ResolveTrackedDefaultProductUnit(product);
        if (defaultUnit is not null)
        {
            defaultUnit.PurchasePrice = variant.CostPrice;
        }

        ProductPricingCompatibilityMirror.MirrorSimpleProductFromDefaultUnit(product, defaultUnit);
    }

    private ProductUnit? ResolveTrackedDefaultProductUnit(Product product) =>
        _context.ChangeTracker
            .Entries<ProductUnit>()
            .Select(entry => entry.Entity)
            .Where(unit => unit.ProductId == product.Id && !unit.IsDeleted)
            .FirstOrDefault(unit => unit.IsDefault);

    private static PurchaseFinancialSummary CalculatePurchaseFinancialSummary(
        IEnumerable<PreparedPurchaseItem> preparedItems)
    {
        var items = new List<PreparedPurchaseItemFinancials>();
        decimal subtotal = 0m;
        decimal totalTax = 0m;

        foreach (var preparedItem in preparedItems)
        {
            var itemSubtotal = RoundCurrency(preparedItem.Request.Quantity * preparedItem.Request.UnitCost);
            var itemTax = RoundCurrency(itemSubtotal * preparedItem.Request.TaxRate / 100m);
            var itemTotal = RoundCurrency(itemSubtotal + itemTax);

            items.Add(new PreparedPurchaseItemFinancials(
                preparedItem,
                itemSubtotal,
                itemTax,
                itemTotal));

            subtotal = RoundCurrency(subtotal + itemSubtotal);
            totalTax = RoundCurrency(totalTax + itemTax);
        }

        return new PurchaseFinancialSummary(
            items,
            subtotal,
            totalTax,
            RoundCurrency(subtotal + totalTax));
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record PurchaseActionSnapshot(int StatusCode, string? ResponseJson);
    private sealed record PreparedPurchaseItem(
        PurchaseItemRequest Request,
        Product Product,
        ProductVariant Variant,
        Guid? UnitId,
        decimal ConversionFactor,
        decimal BaseQuantity);
    private sealed record PreparedPurchaseItemFinancials(
        PreparedPurchaseItem PreparedItem,
        decimal ItemSubtotal,
        decimal TaxAmount,
        decimal TotalAmount);
    private sealed record PurchaseFinancialSummary(
        IReadOnlyList<PreparedPurchaseItemFinancials> Items,
        decimal Subtotal,
        decimal TaxAmount,
        decimal TotalAmount);



    /// <summary>
    /// Get purchase by ID with full details
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<PurchaseInvoiceResponseDto>> GetPurchase(Guid id)
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
            .Include(p => p.Payments)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (invoice == null)
            return NotFound();

        return Ok(invoice.ToResponseDto());
    }

    /// <summary>
    /// Update an existing purchase
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<PurchaseResponse>> UpdatePurchase(Guid id, CreatePurchaseRequest request)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var claim = await TryClaimPurchaseIdempotencyAsync("UPDATE", id, request);
        if (claim.ShortCircuitResult != null)
        {
            await transaction.RollbackAsync();
            return claim.ShortCircuitResult;
        }

        var idempotencyRecord = claim.Record!;
        var pendingStockChanges = new List<PendingProductVariantStockChange>();
        
        try
        {
            var invoice = await LoadPurchaseInvoiceForMutationAsync(id);

            if (invoice == null)
                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    NotFound(new { message = "Invoice not found" }));

            if (!request.ExpectedMutationVersion.HasValue)
            {
                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    Conflict(new
                    {
                        message = "ExpectedMutationVersion is required when updating a purchase.",
                        currentMutationVersion = invoice.AccountingMutationVersion
                    }));
            }

            if (!IsExpectedPurchaseMutationVersion(invoice, request.ExpectedMutationVersion))
            {
                ActionResult<PurchaseResponse> conflictResult = Conflict(new
                {
                    message = "Purchase was changed by another session. Reload the invoice before updating.",
                    currentMutationVersion = invoice.AccountingMutationVersion
                });

                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    conflictResult);
            }

            var hasPurchaseReturns = await _context.Returns.AnyAsync(returnObj =>
                returnObj.ReturnType == ReturnType.PurchaseReturn &&
                returnObj.OriginalPurchaseInvoiceId == invoice.Id);
            if (hasPurchaseReturns ||
                invoice.Status is InvoiceStatus.PartiallyReturned or InvoiceStatus.FullyReturned or InvoiceStatus.Cancelled)
            {
                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    Conflict(new
                    {
                        message = "A returned or cancelled purchase cannot be rewritten. Create a new correcting purchase document."
                    }));
            }

            var supplier = await _context.Suppliers.FindAsync(request.SupplierId);
            if (supplier == null)
            {
                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new { message = "Supplier not found" }));
            }

            var warehouse = await _context.Warehouses.FindAsync(request.WarehouseId);
            if (warehouse == null || !warehouse.IsActive)
            {
                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new { message = "Active warehouse not found" }));
            }

            // 0. Validate items BEFORE any mutations to avoid partial revert on error
            var preparedPurchaseItemsResult = await PreparePurchaseItemsAsync(request.Items);
            if (preparedPurchaseItemsResult.ErrorResult != null)
            {
                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    preparedPurchaseItemsResult.ErrorResult);
            }

            var preparedPurchaseItems = preparedPurchaseItemsResult.Items!;
            await LockPurchaseCostProductsAsync(
                preparedPurchaseItems.Select(item => item.Product.Id)
                    .Concat(invoice.Items.Select(item => item.ProductId)));
            await ReloadPurchaseCostAuthoritiesAsync(preparedPurchaseItems);
            var purchaseFinancials = CalculatePurchaseFinancialSummary(preparedPurchaseItems);
            var paymentSplit = ResolvePurchasePaymentSplit(request, purchaseFinancials.TotalAmount);
            if (!paymentSplit.Success)
            {
                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new { message = paymentSplit.ErrorMessage }));
            }

            var pendingCardReversalCredit = invoice.Payments
                .Where(payment =>
                    !payment.IsDeleted &&
                    payment.PaymentMethod == PaymentMethod.Card &&
                    payment.FundingInstrumentId == request.FundingInstrumentId)
                .Sum(payment => payment.Amount);
            SupplierCardAuthoritySnapshot? cardAuthority;
            try
            {
                cardAuthority = await ResolvePurchaseCardAuthorityAsync(
                    request,
                    paymentSplit.CardAmount,
                    pendingCardReversalCredit);
            }
            catch (InvalidOperationException ex)
            {
                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new { message = ex.Message, code = "SUPPLIER_CARD_AUTHORITY_INVALID" }));
            }

            // Calculate net cash delta for cashbox capacity validation
            // Only validate the incremental cash exposure, not the full new amount
            // If warehouse changes, validate the full new amount for the new warehouse
            
            // Read the authoritative prior cash allocation from the accounting outbox
            // This includes both initial creates (PosPurchase) and updates (PurchaseMutation, PurchaseReversal)
            var priorOutboxMessage = await _context.AccountingOutboxMessages
                .IgnoreQueryFilters()
                .Where(m => m.TenantId == invoice.TenantId &&
                           m.BusinessEntityId == invoice.Id &&
                           (m.EventType == "PosPurchase" || m.EventType == "PurchaseMutation" || m.EventType == "PurchaseReversal") &&
                           m.ProcessedAtUtc != null)
                .OrderByDescending(m => m.ProcessedAtUtc)
                .FirstOrDefaultAsync();
            
            decimal originalCashAmount = 0m;
            if (priorOutboxMessage != null)
            {
                // Extract cash amount from idempotency key pattern: PURCHASE:{tenantId}:{invoiceId}:C{cashAmount}:D{cardAmount}
                // or PURCHASE:{tenantId}:{invoiceId}:{operationKey}:C{cashAmount}:D{cardAmount} for mutations
                var idempotencyKey = priorOutboxMessage.IdempotencyKey;
                var cashPrefix = ":C";
                var cardPrefix = ":D";
                var cashIndex = idempotencyKey.IndexOf(cashPrefix);
                var cardIndex = idempotencyKey.IndexOf(cardPrefix);
                
                if (cashIndex >= 0 && cardIndex > cashIndex)
                {
                    var cashStr = idempotencyKey.Substring(cashIndex + cashPrefix.Length, cardIndex - cashIndex - cashPrefix.Length);
                    if (TryParseStoredCashAmount(cashStr, out var parsedCash))
                    {
                        originalCashAmount = parsedCash;
                    }
                    else
                    {
                        // IDEMPOTENCY HARDENING: Log parse failure to help diagnose malformed idempotency keys
                        // SECURITY: Sanitize idempotency key to avoid exposing financial amounts in logs
                        _logger.LogWarning(
                            "Failed to parse cash amount from idempotency key | SanitizedKey: {SanitizedKey} | CashIndex: {CashIndex} | CardIndex: {CardIndex} | Defaulting to 0m",
                            SanitizeIdempotencyKey(idempotencyKey),
                            cashIndex,
                            cardIndex);
                    }
                }
                else
                {
                    // IDEMPOTENCY HARDENING: Log extraction failure to help diagnose malformed idempotency keys
                    // SECURITY: Sanitize idempotency key to avoid exposing financial amounts in logs
                    _logger.LogWarning(
                        "Failed to extract cash amount from idempotency key (missing or invalid format) | SanitizedKey: {SanitizedKey} | CashIndex: {CashIndex} | CardIndex: {CardIndex} | Defaulting to 0m",
                        SanitizeIdempotencyKey(idempotencyKey),
                        cashIndex,
                        cardIndex);
                }
            }
            
            var warehouseChanged = invoice.WarehouseId != request.WarehouseId;
            var netCashDelta = warehouseChanged ? paymentSplit.CashAmount : (paymentSplit.CashAmount - originalCashAmount);

            var cashboxValidationError = await ValidateCashboxCapacityAsync(
                request.WarehouseId,
                netCashDelta,
                transaction,
                idempotencyRecord);
            if (cashboxValidationError != null)
            {
                return cashboxValidationError;
            }

            // 1. Revert previous effects
            await RevertPurchaseEffects(
                invoice,
                pendingStockChanges,
                journalPolicy: PointOfSale.API.Services.JournalPolicy.AllowExisting);
            await ReloadPurchaseCostAuthoritiesAsync(preparedPurchaseItems);

            // 2. Update Invoice Header
            invoice.SupplierId = request.SupplierId;
            invoice.WarehouseId = request.WarehouseId;
            invoice.PaymentMethod = request.PaymentMethod;
            invoice.PaidAmount = paymentSplit.PaidAmount;
            invoice.DueDate = request.DueDate?.Kind == DateTimeKind.Unspecified 
                    ? DateTime.SpecifyKind(request.DueDate.Value, DateTimeKind.Utc) 
                    : request.DueDate?.ToUniversalTime();
            invoice.Notes = request.Notes;
            invoice.InvoiceDate = request.InvoiceDate.Kind == DateTimeKind.Unspecified 
                    ? DateTime.SpecifyKind(request.InvoiceDate, DateTimeKind.Utc) 
                    : request.InvoiceDate.ToUniversalTime();
            invoice.AccountingMutationVersion++;

            _context.PurchaseInvoicePayments.RemoveRange(invoice.Payments);
            invoice.Payments.Clear();
            AddPurchasePayments(invoice, paymentSplit.CashAmount, paymentSplit.CardAmount, cardAuthority);
            _context.PurchaseInvoicePayments.AddRange(invoice.Payments);

            // 3. Clear old items
            _context.PurchaseInvoiceItems.RemoveRange(invoice.Items);
            invoice.Items.Clear();

            // 4. Process new items
            var lockedStocks = await ProductVariantStockMutationSupport.LockMultipleStocksAsync(
                _context,
                _tenantContext.RequiredTenantId,
                preparedPurchaseItems
                    .Select(item => new StockLockTarget(request.WarehouseId, item.Variant.Id, CreateIfMissing: true))
                    .DistinctBy(target => (target.WarehouseId, target.VariantId)));
            foreach (var purchaseItem in purchaseFinancials.Items)
            {
                var preparedItem = purchaseItem.PreparedItem;
                var itemRequest = preparedItem.Request;
                var product = preparedItem.Product;
                var variant = preparedItem.Variant;
                var unitId = preparedItem.UnitId;
                var conversionFactor = preparedItem.ConversionFactor;
                var baseQuantity = preparedItem.BaseQuantity;

                var baseUnitCost = itemRequest.UnitCost / conversionFactor;

                _context.PurchaseInvoiceItems.Add(new PurchaseInvoiceItem
                {
                    Id = Guid.NewGuid(),
                    PurchaseInvoiceId = invoice.Id,
                    ProductId = itemRequest.ProductId,
                    ProductVariantId = variant.Id,
                    UnitId = unitId,
                    Quantity = itemRequest.Quantity,
                    ConversionFactor = conversionFactor,
                    BaseQuantity = baseQuantity,
                    UnitPrice = itemRequest.UnitCost,
                    TaxRate = itemRequest.TaxRate,
                    TaxAmount = purchaseItem.TaxAmount,
                    TotalAmount = purchaseItem.TotalAmount,
                    CreatedAt = DateTime.UtcNow
                });

                var variantStock = lockedStocks[(request.WarehouseId, variant.Id)];
                var oldQuantity = variantStock.Quantity;
                variant.CostPrice = VariantCostAuthority.ApplyPurchaseReceipt(
                    oldQuantity,
                    variant.CostPrice,
                    baseQuantity,
                    baseUnitCost);
                MirrorSimpleProductPurchasePrice(product, variant);
                variantStock.Quantity += baseQuantity;
                variantStock.UpdatedAt = DateTime.UtcNow;

                _context.StockMovements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.RequiredTenantId,
                    WarehouseId = request.WarehouseId,
                    ProductId = itemRequest.ProductId,
                    ProductVariantId = variant.Id,
                    MovementType = StockMovementType.Purchase,
                    UnitId = unitId,
                    ConversionFactor = conversionFactor,
                    QuantityChange = baseQuantity,
                    QuantityAfter = variantStock.Quantity,
                    AvailableAfter = variantStock.AvailableQuantity,
                    ReservedAfter = variantStock.ReservedQuantity,
                    DamagedAfter = variantStock.DamagedQuantity,
                    ReferenceId = invoice.Id,
                    ReferenceNumber = invoice.InvoiceNumber,
                    Price = baseUnitCost,
                    CreatedAt = DateTime.UtcNow
                });

                pendingStockChanges.Add(ProductVariantStockMutationSupport.BuildPendingStockChange(
                    variant.Id,
                    itemRequest.ProductId,
                    request.WarehouseId,
                    oldQuantity,
                    variantStock.Quantity,
                    "Purchase",
                    referenceId: invoice.Id,
                    referenceType: "PurchaseInvoice",
                    sku: variant.SKU,
                    warehouseName: warehouse.Name));
            }

            invoice.Subtotal = purchaseFinancials.Subtotal;
            invoice.TaxAmount = purchaseFinancials.TaxAmount;
            invoice.TotalAmount = purchaseFinancials.TotalAmount;

            // Financials
            var purchaseBalanceAfter = await _ledgerService.ApplySupplierTransaction(
                supplier.Id,
                DateTime.UtcNow,
                TransactionType.Purchase,
                invoice.InvoiceNumber,
                0,
                invoice.TotalAmount,
                invoice.Id,
                $"Updated Purchase Invoice {invoice.InvoiceNumber}");

            var purchaseTransaction = new SupplierTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                SupplierId = supplier.Id,
                TransactionDate = DateTime.UtcNow,
                TransactionType = TransactionType.Purchase,
                DebitAmount = 0,
                CreditAmount = invoice.TotalAmount,
                ReferenceId = invoice.Id,
                ReferenceNumber = invoice.InvoiceNumber,
                CreatedAt = DateTime.UtcNow,
                BalanceAfter = purchaseBalanceAfter
            };
            _context.SupplierTransactions.Add(purchaseTransaction);

            if (invoice.PaidAmount > 0)
            {
                var paymentNote = $"Payment for Purchase Invoice {invoice.InvoiceNumber} - {invoice.PaymentMethod}";

                var paymentBalanceAfter = await _ledgerService.ApplySupplierTransaction(
                    supplier.Id,
                    DateTime.UtcNow,
                    TransactionType.PaymentMade,
                    invoice.InvoiceNumber,
                    invoice.PaidAmount,
                    0,
                    invoice.Id,
                    paymentNote);

                var paymentTransaction = new SupplierTransaction
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                    SupplierId = supplier.Id,
                    TransactionDate = DateTime.UtcNow,
                    TransactionType = TransactionType.PaymentMade,
                    DebitAmount = invoice.PaidAmount,
                    CreditAmount = 0,
                    ReferenceId = invoice.Id,
                    ReferenceNumber = invoice.InvoiceNumber,
                    CreatedAt = DateTime.UtcNow,
                    BalanceAfter = paymentBalanceAfter
                };
                _context.SupplierTransactions.Add(paymentTransaction);
                
                // DO NOT mutate balances directly - purchase mutation posting is handled through the outbox.
            }

            // ════════════════════════════════════════════════════════════════════════════
            // PHASE LCR: Enqueue to accounting outbox for ledger posting
            // Always enqueue for both paid and credit-only purchases
            // ════════════════════════════════════════════════════════════════════════════
            var purchaseOperationKey = BuildPurchaseMutationOperationKey(idempotencyRecord.Id);
            await _accountingOutbox.EnqueuePurchaseReversalWithCardAuthorityAsync(
                invoice.TenantId,
                invoice.Id,
                purchaseOperationKey,
                invoice.AccountingMutationVersion,
                paymentSplit.CashAmount,
                paymentSplit.CardAmount,
                cardAuthority?.FundingInstrumentId,
                cardAuthority?.FundingLedgerAccountId,
                cardAuthority?.FundingInstrumentVersion);

            var purchasePosting = await PostUpdatedPurchaseAccountingSynchronouslyAsync(
                invoice,
                purchaseOperationKey,
                HttpContext.RequestAborted,
                paymentSplit.CashAmount,
                paymentSplit.CardAmount,
                cardAuthority);

            await MarkSynchronousPurchaseAccountingOutboxProcessedAsync(
                invoice.TenantId,
                invoice.Id,
                "PurchaseReversal",
                BuildPurchaseMutationAccountingOutboxIdempotencyKey(
                    invoice.TenantId,
                    invoice.Id,
                    purchaseOperationKey,
                    paymentSplit.CashAmount,
                    paymentSplit.CardAmount,
                    invoice.AccountingMutationVersion,
                    cardAuthority),
                invoice.AccountingMutationVersion,
                purchasePosting.JournalId,
                HttpContext.RequestAborted);

            await _context.SaveChangesAsync();

            if (pendingStockChanges.Count > 0)
            {
                await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                    _context,
                    _tenantContext.RequiredTenantId,
                    pendingStockChanges);
            }

            var result = await CompletePurchaseIdempotencyAsync(
                transaction,
                idempotencyRecord,
                Ok(new PurchaseResponse
                {
                    InvoiceId = invoice.Id,
                    InvoiceNumber = invoice.InvoiceNumber,
                    TotalAmount = invoice.TotalAmount,
                    MutationVersion = invoice.AccountingMutationVersion,
                    Message = "Purchase updated successfully"
                }));

            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "Error updating purchase");
            return StatusCode(500, new { message = "An error occurred while updating the purchase" });
        }
    }

    /// <summary>
    /// Delete purchase invoice
    /// Posted/completed invoices cannot be deleted - use purchase return instead.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult> DeletePurchase(Guid id)
    {
        // Validate if invoice can be deleted
        var validationResult = await _validationService.CanDeletePurchaseInvoiceAsync(id);
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
            var invoice = await LoadPurchaseInvoiceForMutationAsync(id);

            if (invoice == null)
            {
                return NotFound(new { 
                    errorCode = "INVOICE_NOT_FOUND",
                    messageEn = "Purchase invoice not found.",
                    messageAr = "فاتورة الشراء غير موجودة."
                });
            }

            invoice.AccountingMutationVersion++;
            await RevertPurchaseEffects(
                invoice,
                pendingStockChanges,
                journalPolicy: PointOfSale.API.Services.JournalPolicy.RejectExisting);
            
            _context.PurchaseInvoices.Remove(invoice);
            await _context.SaveChangesAsync();

            if (pendingStockChanges.Count > 0)
            {
                await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                    _context,
                    _tenantContext.RequiredTenantId,
                    pendingStockChanges);
            }

            await transaction.CommitAsync();

            _logger.LogInformation("Purchase invoice deleted: {InvoiceId} - {InvoiceNumber}", invoice.Id, invoice.InvoiceNumber);
            await _auditService.LogDelete("PurchaseInvoice", invoice.Id.ToString(), $"Deleted Purchase Invoice {invoice.InvoiceNumber}", null, invoice.InvoiceNumber);

            return Ok(new { 
                messageEn = "Purchase deleted successfully.",
                messageAr = "تم حذف فاتورة الشراء بنجاح."
            });
        }
        catch (PostedJournalsExistException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex, "Cannot delete purchase due to existing posted journals | InvoiceId: {InvoiceId}", id);
            return Conflict(new { 
                errorCode = "POSTED_JOURNALS_EXIST",
                messageEn = "This purchase cannot be deleted because it has posted accounting journals. Please reverse the journals first.",
                messageAr = "لا يمكن حذف هذه الفاتورة لأنها تحتوي على قيود محاسبية مرحلة. يرجى عكس القيود أولاً.",
                details = ex.Message
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error deleting purchase");
            return StatusCode(500, new { 
                errorCode = "DELETE_ERROR",
                messageEn = "An error occurred while deleting the purchase.",
                messageAr = "حدث خطأ أثناء حذف فاتورة الشراء."
            });
        }
    }

    /// <summary>
    /// Check if a purchase invoice can be deleted - use this before showing delete button
    /// </summary>
    [HttpGet("{id}/can-delete")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> CanDeletePurchase(Guid id)
    {
        var invoice = await _context.PurchaseInvoices.FindAsync(id);
        if (invoice == null)
        {
            return NotFound();
        }

        var validationResult = await _validationService.CanDeletePurchaseInvoiceAsync(id);
        return Ok(new {
            canDelete = validationResult.IsValid,
            errorCode = validationResult.ErrorCode,
            messageEn = validationResult.MessageEn,
            messageAr = validationResult.MessageAr
        });
    }

    private async Task RevertPurchaseEffects(
        PurchaseInvoice invoice,
        List<PendingProductVariantStockChange>? pendingStockChanges = null,
        PointOfSale.API.Services.JournalPolicy journalPolicy = PointOfSale.API.Services.JournalPolicy.AllowExisting)
    {
        var resolvedItems = new List<(PurchaseInvoiceItem Item, ProductVariant Variant, Product? Product)>();

        foreach (var item in invoice.Items)
        {
            var variant = await ProductVariantStockMutationSupport.ResolveVariantAsync(
                _context,
                item.ProductId,
                item.ProductVariantId);

            if (variant != null)
            {
                resolvedItems.Add((item, variant, await _context.Products.FindAsync(item.ProductId)));
            }
        }

        var lockedStocks = await ProductVariantStockMutationSupport.LockMultipleStocksAsync(
            _context,
            _tenantContext.RequiredTenantId,
            resolvedItems
                .Select(entry => new StockLockTarget(invoice.WarehouseId, entry.Variant.Id, CreateIfMissing: false))
                .DistinctBy(target => (target.WarehouseId, target.VariantId)));

        foreach (var entry in resolvedItems.OrderBy(item => item.Variant.Id).ThenBy(item => item.Item.ProductId))
        {
            if (!lockedStocks.TryGetValue((invoice.WarehouseId, entry.Variant.Id), out var variantStock))
            {
                continue;
            }

            await _context.Entry(entry.Variant).ReloadAsync();
            if (entry.Product != null)
            {
                await _context.Entry(entry.Product).ReloadAsync();
            }

            var baseUnitCost = entry.Item.UnitPrice / entry.Item.ConversionFactor;
            entry.Variant.CostPrice = VariantCostAuthority.ApplyPurchaseReturn(
                variantStock.Quantity,
                entry.Variant.CostPrice,
                entry.Item.BaseQuantity,
                baseUnitCost);
            if (entry.Product != null)
            {
                MirrorSimpleProductPurchasePrice(entry.Product, entry.Variant);
            }

            ProductVariantStockMutationSupport.ValidateQuantityChange(
                variantStock,
                -entry.Item.BaseQuantity,
                allowNegativeStock: false,
                operationName: "PurchaseRevert");

            var oldQuantity = variantStock.Quantity;
            variantStock.Quantity -= entry.Item.BaseQuantity;
            variantStock.UpdatedAt = DateTime.UtcNow;

            _context.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,
                WarehouseId = invoice.WarehouseId,
                ProductId = entry.Item.ProductId,
                ProductVariantId = entry.Variant.Id,
                MovementType = StockMovementType.Adjustment,
                QuantityChange = -entry.Item.BaseQuantity,
                QuantityAfter = variantStock.Quantity,
                AvailableAfter = variantStock.AvailableQuantity,
                ReservedAfter = variantStock.ReservedQuantity,
                DamagedAfter = variantStock.DamagedQuantity,
                ReferenceId = invoice.Id,
                ReferenceLineId = entry.Item.Id,
                ReferenceNumber = invoice.InvoiceNumber,
                ReferenceType = "PurchaseInvoiceRevert",
                IdempotencyKey = $"purchase-invoice-revert:{_tenantContext.RequiredTenantId:N}:{invoice.Id:N}:{entry.Item.Id:N}",
                Source = "PurchaseInvoiceRevert",
                Notes = $"Reversal of purchase inventory movement for invoice {invoice.InvoiceNumber}",
                CreatedAt = DateTime.UtcNow
            });

            pendingStockChanges?.Add(ProductVariantStockMutationSupport.BuildPendingStockChange(
                entry.Variant.Id,
                entry.Item.ProductId,
                invoice.WarehouseId,
                oldQuantity,
                variantStock.Quantity,
                "PurchaseRevert",
                referenceId: invoice.Id,
                referenceType: "PurchaseInvoiceRevert",
                sku: entry.Variant.SKU));
        }
        
        // DO NOT modify balances directly - use LedgerService / journal reversal flows.
        await _purchaseFinancialIntegrityService.RevertLegacyTreasuryEffectsAsync(
            invoice,
            journalPolicy);

        // 4. Revert Supplier Ledger & Transactions
        var ledgerEntries = await _context.AccountLedgers
            .Where(l => l.ReferenceId == invoice.Id)
            .ToListAsync();
        _context.AccountLedgers.RemoveRange(ledgerEntries);

        var supplierTransactions = await _context.SupplierTransactions
            .Where(t => t.ReferenceId == invoice.Id)
            .ToListAsync();
        _context.SupplierTransactions.RemoveRange(supplierTransactions);

        await _context.SaveChangesAsync();

        await _partyBalanceMaintenanceService.RebuildPartyBalanceAsync(
            PartyType.Supplier,
            invoice.SupplierId,
            $"Purchase invoice deletion repair after removing ledger history for invoice {invoice.InvoiceNumber}");
    }

    private async Task<PurchaseInvoice?> LoadPurchaseInvoiceForMutationAsync(Guid invoiceId)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        IQueryable<PurchaseInvoice> query;

        if (IsPostgresProvider())
        {
            query = _context.PurchaseInvoices
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""PurchaseInvoices""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {invoiceId}
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
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {invoiceId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters();
        }
        else
        {
            query = _context.PurchaseInvoices
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
        await _context.Entry(invoice)
            .Collection(entry => entry.Payments)
            .LoadAsync();

        return invoice;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<PurchaseInvoiceResponseDto>>> GetPurchases(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPurchasePageSize);

        var query = _context.PurchaseInvoices
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
            .Include(p => p.Payments)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.ToLower();
            query = query.Where(p => 
                p.InvoiceNumber.ToLower().Contains(search) ||
                p.Supplier.Name.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(p => p.InvoiceDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<PurchaseInvoiceResponseDto>
        {
            Items = items.Select(item => item.ToResponseDto()).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    private async Task<string> GenerateInvoiceNumberAsync()
    {
        var timestampUtc = DateTime.UtcNow;

        if (IsPostgresProvider())
        {
            var nextNumber = await GetNextPurchaseInvoiceSequenceValueAsync();
            return FormatInvoiceNumber(timestampUtc, nextNumber);
        }

        await LockTenantForInvoiceNumberAsync(_tenantContext.RequiredTenantId);

        var prefix = $"PUR-{timestampUtc:yyyyMMdd}";
        var lastInvoiceNumber = await _context.PurchaseInvoices
            .Where(invoice =>
                invoice.TenantId == _tenantContext.RequiredTenantId &&
                invoice.InvoiceNumber.StartsWith(prefix))
            .OrderByDescending(invoice => invoice.CreatedAt)
            .ThenByDescending(invoice => invoice.InvoiceNumber)
            .Select(invoice => invoice.InvoiceNumber)
            .FirstOrDefaultAsync();

        return GenerateInvoiceNumber(lastInvoiceNumber, timestampUtc);
    }

    private async Task<long> GetNextPurchaseInvoiceSequenceValueAsync()
    {
        var connection = _context.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT nextval('{PurchaseInvoiceSequenceName}')";

            var currentTransaction = _context.Database.CurrentTransaction;
            if (currentTransaction != null)
            {
                command.Transaction = currentTransaction.GetDbTransaction();
            }

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }
        catch (PostgresException exception) when (IsMissingInvoiceSequence(exception))
        {
            throw new InvalidOperationException(
                $"PostgreSQL sequence {PurchaseInvoiceSequenceName} is missing. Apply the purchase invoice sequence migration before creating purchases.",
                exception);
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

    private static bool IsMissingInvoiceSequence(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UndefinedTable ||
        exception.SqlState == "42704";

    private static string GenerateInvoiceNumber(string? lastInvoiceNumber, DateTime timestampUtc)
    {
        var prefix = $"PUR-{timestampUtc:yyyyMMdd}";

        if (string.IsNullOrEmpty(lastInvoiceNumber) || !lastInvoiceNumber.StartsWith(prefix))
        {
            return $"{prefix}-0001";
        }

        var lastNumber = int.Parse(lastInvoiceNumber.Split('-').Last());
        var nextNumber = DocumentNumberingSupport.IncrementLegacyNumber(lastNumber);
        return $"{prefix}-{nextNumber:D4}";
    }

    private static string FormatInvoiceNumber(DateTime timestampUtc, long sequenceValue) =>
        $"PUR-{timestampUtc:yyyyMMdd}-{sequenceValue:D4}";

    private async Task<ActionResult<PurchaseResponse>?> ValidateCashboxCapacityAsync(
        Guid warehouseId,
        decimal cashAmount,
        IDbContextTransaction transaction,
        PurchaseRequestIdempotency idempotencyRecord)
    {
        if (cashAmount <= 0m)
        {
            return null;
        }

        try
        {
            var cashAccount = await _cashboxLedgerAccountService.ResolveWarehouseCashboxAccountAsync(
                _tenantContext.RequiredTenantId,
                warehouseId);
            var availableBalance = await _treasuryReadService.GetAccountBalanceAsync(
                _tenantContext.RequiredTenantId,
                cashAccount.AccountCode);

            if (availableBalance < cashAmount)
            {
                return await CompletePurchaseIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new
                    {
                        message = $"Insufficient funds in Cashbox. Available: {availableBalance:N2}, Required cash portion: {cashAmount:N2}. Please record a capital injection or cash deposit first, or reduce the cash payment amount.",
                        code = "INSUFFICIENT_FUNDS",
                        availableBalance,
                        requiredAmount = cashAmount,
                        shortfall = cashAmount - availableBalance
                    }));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not validate cashbox balance; failing purchase before any financial mutation | TenantId: {TenantId} | WarehouseId: {WarehouseId}",
                _tenantContext.RequiredTenantId,
                warehouseId);

            return await CompletePurchaseIdempotencyAsync(
                transaction,
                idempotencyRecord,
                StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "Cashbox balance could not be validated. The purchase was not recorded.",
                    code = "CASHBOX_VALIDATION_UNAVAILABLE"
                }));
        }

        return null;
    }

    private async Task MarkSynchronousPurchaseAccountingOutboxProcessedAsync(
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
                $"Synchronous {eventType} accounting post for purchase {invoiceId} did not return a journal id.");
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
                $"Synchronous {eventType} accounting post for purchase {invoiceId} has no matching outbox row.");
        }

        if (message.ProcessedAtUtc.HasValue)
        {
            if (!message.JournalId.HasValue)
            {
                throw new InvalidOperationException(
                    $"Synchronous {eventType} accounting outbox row for purchase {invoiceId} was already processed without a journal.");
            }

            if (message.JournalId.Value != journalId.Value)
            {
                throw new InvalidOperationException(
                    $"Synchronous {eventType} accounting outbox row for purchase {invoiceId} was already processed with a different journal.");
            }

            return;
        }

        if (message.IsFailed ||
            (message.ClaimedUntilUtc.HasValue && message.ClaimedUntilUtc.Value > DateTime.UtcNow))
        {
            throw new InvalidOperationException(
                $"Synchronous {eventType} accounting outbox row for purchase {invoiceId} is failed or claimed and cannot be completed by the request executor.");
        }

        message.MarkProcessed(journalId.Value);
    }

    private static string BuildBasePurchaseAccountingOutboxIdempotencyKey(
        Guid tenantId,
        Guid invoiceId,
        decimal cashAmount,
        decimal cardAmount,
        int mutationVersion,
        SupplierCardAuthoritySnapshot? authority) =>
        $"PURCHASE:{tenantId}:{invoiceId}:C{cashAmount.ToString("F2", CultureInfo.InvariantCulture)}:D{cardAmount.ToString("F2", CultureInfo.InvariantCulture)}:V{mutationVersion}{BuildCardAuthorityKeySuffix(cardAmount, authority)}";

    private static string BuildPurchaseMutationAccountingOutboxIdempotencyKey(
        Guid tenantId,
        Guid invoiceId,
        string operationKey,
        decimal cashAmount,
        decimal cardAmount,
        int mutationVersion,
        SupplierCardAuthoritySnapshot? authority) =>
        $"PURCHASE:{tenantId}:{invoiceId}:{operationKey}:C{cashAmount.ToString("F2", CultureInfo.InvariantCulture)}:D{cardAmount.ToString("F2", CultureInfo.InvariantCulture)}:V{mutationVersion}{BuildCardAuthorityKeySuffix(cardAmount, authority)}";

    private static string BuildCardAuthorityKeySuffix(decimal cardAmount, SupplierCardAuthoritySnapshot? authority) =>
        cardAmount > 0m && authority != null
            ? $":FI{authority.FundingInstrumentId:D}:FA{authority.FundingLedgerAccountId:D}:FV{authority.FundingInstrumentVersion}"
            : string.Empty;

    private static string BuildPurchaseMutationOperationKey(Guid idempotencyRecordId) =>
        $"purchase-mutation-{idempotencyRecordId:N}";

    private static bool IsExpectedPurchaseMutationVersion(PurchaseInvoice invoice, int? expectedMutationVersion) =>
        !expectedMutationVersion.HasValue ||
        expectedMutationVersion.Value == invoice.AccountingMutationVersion;

    /// <summary>
    /// Attempts to parse a stored cash amount with compatibility for legacy comma-as-decimal-separator values.
    /// First tries standard parsing, then retries treating comma as decimal separator if that fails.
    /// </summary>
    private static bool TryParseStoredCashAmount(string value, out decimal result)
    {
        // First attempt: Standard parsing with InvariantCulture (expects dot as decimal separator)
        if (decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        // Second attempt: Legacy compatibility - treat comma as decimal separator
        // This handles values like "10,50" that may have been stored with comma separators
        if (value.Contains(',') && !value.Contains('.'))
        {
            var normalizedValue = value.Replace(',', '.');
            if (decimal.TryParse(normalizedValue, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result))
            {
                return true;
            }
        }

        result = 0m;
        return false;
    }

    private static string SanitizeIdempotencyKey(string idempotencyKey)
    {
        // Mask financial amounts in idempotency keys to prevent exposure in logs
        // Pattern: PURCHASE:{tenantId}:{invoiceId}:C{cashAmount}:D{cardAmount}
        // or PURCHASE:{tenantId}:{invoiceId}:{operationKey}:C{cashAmount}:D{cardAmount}
        var cashPrefix = ":C";
        var cardPrefix = ":D";
        var cashIndex = idempotencyKey.IndexOf(cashPrefix);
        var cardIndex = idempotencyKey.IndexOf(cardPrefix);

        if (cashIndex >= 0 && cardIndex > cashIndex)
        {
            // Replace C{amount}:D{amount} with C***:D***
            var beforeCash = idempotencyKey.Substring(0, cashIndex);
            var afterCard = idempotencyKey.Substring(cardIndex + cardPrefix.Length);
            var cardEndIndex = afterCard.IndexOfAny(new[] { ':', '\0' });
            if (cardEndIndex >= 0)
            {
                afterCard = afterCard.Substring(cardEndIndex);
            }
            else
            {
                afterCard = string.Empty;
            }
            return $"{beforeCash}:C***:D***{afterCard}";
        }

        // SECURITY: Fallback for malformed keys - mask any potential amounts after :C or :D
        // Never return the raw key as it may contain financial data
        string sanitized;
        
        if (cashIndex >= 0)
        {
            // Found :C but not :D in expected position - mask everything after :C
            var beforeCash = idempotencyKey.Substring(0, cashIndex);
            sanitized = $"{beforeCash}:C***";
        }
        else if (cardIndex >= 0)
        {
            // Found :D but not :C - mask everything after :D
            var beforeCard = idempotencyKey.Substring(0, cardIndex);
            sanitized = $"{beforeCard}:D***";
        }
        else
        {
            // No financial markers found - fully mask to prevent leaking sensitive data
            sanitized = "REDACTED_IDEMPOTENCY";
        }
        
        return sanitized;
    }

    private static (bool Success, decimal PaidAmount, decimal CashAmount, decimal CardAmount, string? ErrorMessage) ResolvePurchasePaymentSplit(
        CreatePurchaseRequest request,
        decimal? totalAmount = null)
    {
        return PurchasePaymentSplitter.Resolve(request, totalAmount);
    }

    private async Task<SupplierCardAuthoritySnapshot?> ResolvePurchaseCardAuthorityAsync(
        CreatePurchaseRequest request,
        decimal cardAmount,
        decimal pendingReversalCredit = 0m)
    {
        if (cardAmount <= 0m)
        {
            if (request.FundingInstrumentId.HasValue || request.FundingInstrumentVersion.HasValue)
                throw new InvalidOperationException("FundingInstrumentId must be null when the purchase has no Card allocation.");
            return null;
        }

        return await SupplierCardAuthority.ResolveForOutflowAsync(
            _context,
            _tenantContext.RequiredTenantId,
            request.FundingInstrumentId,
            request.FundingInstrumentVersion,
            cardAmount,
            pendingReversalCredit,
            HttpContext.RequestAborted);
    }

    private static void AddPurchasePayments(
        PurchaseInvoice invoice,
        decimal cashAmount,
        decimal cardAmount,
        SupplierCardAuthoritySnapshot? cardAuthority)
    {
        var paymentDate = DateTime.UtcNow;

        if (cashAmount > 0m)
        {
            invoice.Payments.Add(new PurchaseInvoicePayment
            {
                Id = Guid.NewGuid(),
                PurchaseInvoiceId = invoice.Id,
                PaymentDate = paymentDate,
                Amount = cashAmount,
                PaymentMethod = PaymentMethod.Cash,
                ReferenceNumber = invoice.InvoiceNumber,
                Notes = $"Cash payment for purchase invoice {invoice.InvoiceNumber}",
                CreatedAt = paymentDate
            });
        }

        if (cardAmount > 0m)
        {
            invoice.Payments.Add(new PurchaseInvoicePayment
            {
                Id = Guid.NewGuid(),
                PurchaseInvoiceId = invoice.Id,
                PaymentDate = paymentDate,
                Amount = cardAmount,
                PaymentMethod = PaymentMethod.Card,
                FundingInstrumentId = cardAuthority?.FundingInstrumentId,
                FundingLedgerAccountId = cardAuthority?.FundingLedgerAccountId,
                FundingInstrumentVersion = cardAuthority?.FundingInstrumentVersion,
                ReferenceNumber = invoice.InvoiceNumber,
                Notes = $"Card payment for purchase invoice {invoice.InvoiceNumber}",
                CreatedAt = paymentDate
            });
        }
    }

    private async Task<PostingResult> PostUpdatedPurchaseAccountingSynchronouslyAsync(
        PurchaseInvoice invoice,
        string operationKey,
        CancellationToken cancellationToken,
        decimal cashAmount,
        decimal cardAmount,
        SupplierCardAuthoritySnapshot? cardAuthority)
    {
        var journalsToReverse = await _context.JournalEntries
            .IgnoreQueryFilters()
            .Where(journal =>
                journal.TenantId == invoice.TenantId &&
                journal.SourceId == invoice.Id &&
                journal.SourceType == "PurchaseInvoice" &&
                journal.Status == JournalStatus.Posted &&
                (string.IsNullOrEmpty(journal.IdempotencyKey) || !journal.IdempotencyKey.EndsWith($":{operationKey}")))
            .OrderBy(journal => journal.PostedAtUtc)
            .ThenBy(journal => journal.JournalNumber)
            .ToListAsync(cancellationToken);

        foreach (var journal in journalsToReverse)
        {
            await _postingService.ReverseJournalAsync(
                invoice.TenantId,
                journal.Id,
                $"Purchase update for {invoice.InvoiceNumber} ({operationKey})",
                ct: cancellationToken);
        }

        var purchasePosting = await _commerceAccounting.PostPosPurchaseAsync(
            invoice,
            cancellationToken,
            operationKey,
            cashAmount,
            cardAmount,
            cardAuthority?.FundingInstrumentId,
            cardAuthority?.FundingLedgerAccountId,
            cardAuthority?.FundingInstrumentVersion);
        if (!purchasePosting.Success)
        {
            throw new InvalidOperationException(
                $"Updated POS purchase accounting post failed for invoice {invoice.InvoiceNumber}: {purchasePosting.ErrorMessage}");
        }

        return purchasePosting;
    }
}

public record CreatePurchaseRequest
{
    public Guid SupplierId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateTime InvoiceDate { get; set; }
    [Required]
    [MinLength(1)]
    public List<PurchaseItemRequest> Items { get; set; } = new();
    [EnumDataType(typeof(PaymentMethod))]
    public PaymentMethod PaymentMethod { get; set; }
    [EnumDataType(typeof(PaymentMethod))]
    public PaymentMethod? InitialPaymentMethod { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal PaidAmount { get; set; }
    public DateTime? DueDate { get; set; }
    public int? ExpectedMutationVersion { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? CashAmount { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? CardAmount { get; set; }
    public Guid? FundingInstrumentId { get; set; }
    public int? FundingInstrumentVersion { get; set; }
    [MaxLength(1000)]
    public string? Notes { get; set; }
}

public record PurchaseItemRequest
{
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public Guid? UnitId { get; set; } // Unit selected for this purchase line
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal Quantity { get; set; } // Quantity in the selected unit
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal UnitCost { get; set; } // Cost per selected unit
    [Range(typeof(decimal), "0", "100")]
    public decimal TaxRate { get; set; }
}

public record PurchaseResponse
{
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int MutationVersion { get; set; }
    public string Message { get; set; } = string.Empty;
}
