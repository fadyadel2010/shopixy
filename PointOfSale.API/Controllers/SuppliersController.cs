using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.API.DTOs;
using PointOfSale.API.Mappers;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const int MaxFinancialIdempotencyKeyLength = 100;
    private static readonly Regex FinancialIdempotencyKeyPattern = new(
        "^[A-Za-z0-9-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions FinancialMutationJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<SuppliersController> _logger;
    private readonly PointOfSale.API.Services.IValidationService _validationService;
    private readonly PointOfSale.API.Services.Audit.IAuditService _auditService;
    private readonly ILedgerService _ledgerService;
    private readonly IAccountingOutboxService _accountingOutbox;
    private readonly IAccountResolver _accountResolver;
    private readonly ITreasuryReadService _treasuryReadService;
    private readonly ICashboxLedgerAccountService _cashboxLedgerAccountService;
    private readonly IFinancialMutationIdempotencyService _financialMutationIdempotency;
    private readonly ICommerceAccountingService? _commerceAccounting;

    public SuppliersController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<SuppliersController> logger,
        PointOfSale.API.Services.IValidationService validationService,
        PointOfSale.API.Services.Audit.IAuditService auditService,
        ILedgerService ledgerService,
        IAccountingOutboxService accountingOutbox,
        IAccountResolver accountResolver,
        ITreasuryReadService treasuryReadService,
        ICashboxLedgerAccountService cashboxLedgerAccountService,
        IFinancialMutationIdempotencyService financialMutationIdempotency,
        ICommerceAccountingService? commerceAccounting = null)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
        _validationService = validationService;
        _auditService = auditService;
        _ledgerService = ledgerService;
        _accountingOutbox = accountingOutbox;
        _accountResolver = accountResolver;
        _treasuryReadService = treasuryReadService;
        _cashboxLedgerAccountService = cashboxLedgerAccountService;
        _financialMutationIdempotency = financialMutationIdempotency;
        _commerceAccounting = commerceAccounting;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<SupplierResponseDto>>> GetSuppliers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        var query = _context.Suppliers
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.ToLower();
            query = query.Where(s => 
                s.Name.ToLower().Contains(search) ||
                (s.Email != null && s.Email.ToLower().Contains(search)) ||
                (s.Phone != null && s.Phone.Contains(search)));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(s => s.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResult<SupplierResponseDto>
        {
            Items = items.Select(item => item.ToResponseDto()).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SupplierResponseDto>> GetSupplier(Guid id)
    {
        var supplier = await _context.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == id);

        if (supplier == null)
            return NotFound();

        return Ok(supplier.ToResponseDto());
    }

    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<SupplierResponseDto>> CreateSupplier(CreateSupplierRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
            Name = request.Name.Trim(),
            Email = request.Email?.Trim(),
            Phone = request.Phone?.Trim(),
            Address = request.Address?.Trim(),
            TaxNumber = request.TaxNumber?.Trim(),
            PaymentTermsDays = request.PaymentTermsDays,
            Notes = request.Notes?.Trim(),
            Balance = 0,
            CreatedAt = DateTime.UtcNow
        };

        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync();

        await _auditService.LogCreate("Supplier", supplier.Id.ToString(), $"Created supplier {supplier.Name}", supplier, supplier.Id.ToString());

        return CreatedAtAction(nameof(GetSupplier), new { id = supplier.Id }, supplier.ToResponseDto());
    }

    [HttpPost("{id}/payments")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<SupplierPaymentResponse>> CreateSupplierPayment(
        Guid id,
        CreateSupplierPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var operationId = ResolveFinancialIdempotencyKey();
        if (operationId == null)
        {
            return BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} header is required and must contain only letters, numbers, or hyphens.",
            });
        }

        var requestHash = BuildSupplierPaymentRequestHash(id, request);
        var scopedIdempotencyKey = BuildSupplierMutationIdempotencyKey(operationId);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var claim = await _financialMutationIdempotency.TryClaimAsync(
            operation: "SUPPLIER_PAYMENT_CREATE",
            key: scopedIdempotencyKey,
            requestHash: requestHash,
            cancellationToken: cancellationToken);

        if (claim.ShortCircuitResult != null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ToSupplierPaymentActionResult(claim.ShortCircuitResult);
        }

        var idempotencyRecord = claim.Record!;

        try
        {
            if (request.Amount <= 0)
            {
                return await CompleteFinancialMutationAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new { message = "Payment amount must be greater than zero." }),
                    cancellationToken);
            }

            if (request.PaymentMethod is PaymentMethod.Credit or PaymentMethod.Mixed)
            {
                return await CompleteFinancialMutationAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new { message = "Supplier payments must use a concrete funding method." }),
                    cancellationToken);
            }

            var supplier = await _context.Suppliers
                .FirstOrDefaultAsync(item => item.Id == id && !item.IsDeleted, cancellationToken);

            if (supplier == null)
            {
                return await CompleteFinancialMutationAsync(
                    transaction,
                    idempotencyRecord,
                    NotFound(new { message = "Supplier not found." }),
                    cancellationToken);
            }

            var normalizedReference = string.IsNullOrWhiteSpace(request.ReferenceNumber)
                ? $"SPAY-{DateTime.UtcNow:yyyyMMddHHmmss}"
                : request.ReferenceNumber.Trim();
            var normalizedNotes = string.IsNullOrWhiteSpace(request.Notes)
                ? null
                : request.Notes.Trim();

            SupplierCardAuthoritySnapshot? cardAuthority = null;
            IActionResult? fundingValidation = null;
            if (request.PaymentMethod == PaymentMethod.Card)
            {
                try
                {
                    cardAuthority = await SupplierCardAuthority.ResolveForOutflowAsync(
                        _context,
                        _tenantContext.RequiredTenantId,
                        request.FundingInstrumentId,
                        request.FundingInstrumentVersion,
                        RoundCurrency(request.Amount),
                        cancellationToken: cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    fundingValidation = BadRequest(new { message = ex.Message, code = "SUPPLIER_CARD_AUTHORITY_INVALID" });
                }
            }
            else
            {
                if (request.FundingInstrumentId.HasValue || request.FundingInstrumentVersion.HasValue)
                {
                    fundingValidation = BadRequest(new
                    {
                        message = "FundingInstrumentId must be null for non-Card supplier payments.",
                        code = "SUPPLIER_CARD_AUTHORITY_NOT_ALLOWED"
                    });
                }
                else
                {
                    fundingValidation = await ValidateSupplierFundingAvailabilityAsync(
                        request.PaymentMethod,
                        request.Amount,
                        cancellationToken);
                }
            }

            if (fundingValidation is not null)
            {
                return await CompleteFinancialMutationAsync(
                    transaction,
                    idempotencyRecord,
                    fundingValidation,
                    cancellationToken);
            }

            var paymentTimestamp = DateTime.UtcNow;
            var payment = new SupplierPayment
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,
                SupplierId = supplier.Id,
                PurchaseInvoiceId = null,
                PaymentDate = paymentTimestamp,
                Amount = RoundCurrency(request.Amount),
                PaymentMethod = request.PaymentMethod,
                FundingInstrumentId = cardAuthority?.FundingInstrumentId,
                FundingLedgerAccountId = cardAuthority?.FundingLedgerAccountId,
                FundingInstrumentVersion = cardAuthority?.FundingInstrumentVersion,
                IdempotencyKey = operationId,
                ReferenceNumber = normalizedReference,
                Notes = normalizedNotes,
                CreatedAt = paymentTimestamp,
                UpdatedAt = paymentTimestamp,
            };

            _context.SupplierPayments.Add(payment);

            var balanceAfter = await _ledgerService.ApplySupplierTransaction(
                supplier.Id,
                paymentTimestamp,
                TransactionType.PaymentMade,
                normalizedReference,
                debitAmount: payment.Amount,
                creditAmount: 0m,
                referenceId: payment.Id,
                notes: normalizedNotes ?? $"Supplier payment {normalizedReference}");

            _context.SupplierTransactions.Add(new SupplierTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,
                SupplierId = supplier.Id,
                TransactionDate = paymentTimestamp,
                TransactionType = TransactionType.PaymentMade,
                DebitAmount = payment.Amount,
                CreditAmount = 0m,
                BalanceAfter = balanceAfter,
                ReferenceId = payment.Id,
                ReferenceNumber = normalizedReference,
                Notes = normalizedNotes,
                CreatedAt = paymentTimestamp,
                UpdatedAt = paymentTimestamp,
            });
            balanceAfter = await _ledgerService.RebuildPartyTransactionBalancesAsync(
                PartyType.Supplier,
                supplier.Id,
                cancellationToken);

            await _accountingOutbox.EnqueueSupplierPaymentAsync(payment.TenantId, payment.Id, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            if (payment.PaymentMethod == PaymentMethod.Card)
            {
                if (_commerceAccounting == null)
                    throw new InvalidOperationException("Supplier Card accounting authority is unavailable.");
                var posting = await _commerceAccounting.PostSupplierPaymentAsync(payment, cancellationToken);
                if (!posting.Success || !posting.JournalId.HasValue)
                    throw new InvalidOperationException(posting.ErrorMessage ?? "Supplier Card accounting posting failed.");

                var outbox = await _context.AccountingOutboxMessages
                    .IgnoreQueryFilters()
                    .SingleAsync(message =>
                        message.TenantId == payment.TenantId &&
                        message.EventType == "SupplierPayment" &&
                        message.BusinessEntityId == payment.Id,
                        cancellationToken);
                outbox.MarkProcessed(posting.JournalId.Value);
                await _context.SaveChangesAsync(cancellationToken);
            }

            var response = new SupplierPaymentResponse
            {
                Id = payment.Id,
                SupplierId = payment.SupplierId,
                SupplierName = supplier.Name,
                PaymentDate = payment.PaymentDate,
                Amount = payment.Amount,
                PaymentMethod = payment.PaymentMethod,
                FundingInstrumentId = payment.FundingInstrumentId,
                FundingInstrumentVersion = payment.FundingInstrumentVersion,
                ReferenceNumber = payment.ReferenceNumber,
                Notes = payment.Notes,
                BalanceAfter = balanceAfter,
                IdempotencyKey = payment.IdempotencyKey,
            };

            await _auditService.LogCreate(
                "SupplierPayment",
                payment.Id.ToString(),
                $"Recorded supplier payment {normalizedReference} for supplier {supplier.Name}",
                response,
                normalizedReference);

            return await CompleteFinancialMutationAsync(
                transaction,
                idempotencyRecord,
                CreatedAtAction(nameof(GetSupplier), new { id = supplier.Id }, response),
                cancellationToken);
        }
        catch (NoCashboxAccountException ex)
        {
            _logger.LogWarning(ex, "No active cashbox configured for tenant {TenantId} when creating supplier payment", ex.TenantId);
            return await CompleteFinancialMutationAsync(
                transaction,
                idempotencyRecord,
                BadRequest(new
                {
                    message = "No active cashbox configured for tenant. Please configure a cashbox before making cash payments.",
                    code = "NO_CASHBOX_CONFIGURED"
                }),
                cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error creating supplier payment for supplier {SupplierId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "An error occurred while creating the supplier payment.",
            });
        }
    }

    [HttpGet("{id}/payments")]
    public async Task<ActionResult<IReadOnlyList<SupplierPaymentHistoryResponse>>> GetSupplierPayments(
        Guid id,
        CancellationToken cancellationToken)
    {
        var supplierExists = await _context.Suppliers
            .AsNoTracking()
            .AnyAsync(supplier => supplier.Id == id && !supplier.IsDeleted, cancellationToken);
        if (!supplierExists)
        {
            return NotFound(new { message = "Supplier not found." });
        }

        var payments = await _context.SupplierPayments
            .AsNoTracking()
            .Where(payment => payment.SupplierId == id)
            .OrderByDescending(payment => payment.PaymentDate)
            .ThenByDescending(payment => payment.CreatedAt)
            .Select(payment => new SupplierPaymentHistoryResponse
            {
                Id = payment.Id,
                SupplierId = payment.SupplierId,
                TransactionDate = payment.PaymentDate,
                Amount = payment.Amount,
                PaymentMethod = payment.PaymentMethod,
                FundingInstrumentId = payment.FundingInstrumentId,
                FundingInstrumentVersion = payment.FundingInstrumentVersion,
                ReferenceNumber = payment.ReferenceNumber,
                Notes = payment.Notes,
                IdempotencyKey = payment.IdempotencyKey,
            })
            .ToListAsync(cancellationToken);

        return Ok(payments);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> UpdateSupplier(Guid id, UpdateSupplierRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var supplier = await _context.Suppliers.FirstOrDefaultAsync(entity => entity.Id == id);

        if (supplier == null)
            return NotFound();

        var beforeState = new { supplier.Name, supplier.Email, supplier.Phone, supplier.Address, supplier.TaxNumber };

        supplier.Name = request.Name.Trim();
        supplier.Email = request.Email?.Trim();
        supplier.Phone = request.Phone?.Trim();
        supplier.Address = request.Address?.Trim();
        supplier.TaxNumber = request.TaxNumber?.Trim();
        supplier.PaymentTermsDays = request.PaymentTermsDays;
        supplier.Notes = request.Notes?.Trim();
        supplier.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await _auditService.LogUpdate("Supplier", supplier.Id.ToString(), $"Updated supplier {supplier.Name}", beforeState, supplier, supplier.Id.ToString());

        return NoContent();
    }

    /// <summary>
    /// Get all suppliers with their opening balance status
    /// </summary>
    [HttpGet("opening-balances")]
    public async Task<ActionResult<IEnumerable<SupplierOpeningBalanceResponse>>> GetSupplierOpeningBalances()
    {
        try
        {
            var suppliers = await _context.Suppliers
                .Where(s => !s.IsDeleted)
                .OrderBy(s => s.Name)
                .Select(s => new SupplierOpeningBalanceResponse
                {
                    SupplierId = s.Id,
                    SupplierName = s.Name,
                    Phone = s.Phone,
                    OpeningBalanceAmount = s.OpeningBalanceAmount,
                    BalanceType = s.BalanceType,
                    OpeningBalanceDate = s.OpeningBalanceDate,
                    HasOpeningBalance = s.OpeningBalanceAmount.HasValue
                })
                .ToListAsync();

            return Ok(suppliers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting supplier opening balances");
            return StatusCode(500, new { message = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// Save opening balances for multiple suppliers
    /// </summary>
    [HttpPost("opening-balances")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> SaveSupplierOpeningBalances(
        SaveSupplierOpeningBalancesRequest request,
        CancellationToken cancellationToken)
    {
        var normalization = NormalizeSupplierOpeningBalanceRequest(request);
        if (normalization.ErrorResult != null)
        {
            return normalization.ErrorResult;
        }

        var normalizedItems = normalization.Items!;
        var requestHash = BuildSupplierOpeningBalanceRequestHash(normalizedItems);
        var operationIdResolution = ResolveOptionalSupplierMutationOperationId(requestHash);
        if (operationIdResolution.ErrorResult != null)
        {
            return operationIdResolution.ErrorResult;
        }

        var scopedIdempotencyKey = BuildSupplierMutationIdempotencyKey(operationIdResolution.OperationId!);
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var claim = await _financialMutationIdempotency.TryClaimAsync(
            operation: "SUPPLIER_OPENING_BALANCE_BATCH",
            key: scopedIdempotencyKey,
            requestHash: requestHash,
            cancellationToken: cancellationToken);

        if (claim.ShortCircuitResult != null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return claim.ShortCircuitResult;
        }

        var idempotencyRecord = claim.Record!;

        try
        {
            var results = new List<object>();
            var supplierIds = normalizedItems
                .Select(item => item.SupplierId)
                .Distinct()
                .ToList();
            var suppliers = await _context.Suppliers
                .Where(supplier => supplierIds.Contains(supplier.Id) && !supplier.IsDeleted)
                .ToDictionaryAsync(supplier => supplier.Id, cancellationToken);
            var suppliersWithExistingOpeningBalance = await _context.AccountLedgers
                .Where(entry =>
                    entry.TenantId == _tenantContext.RequiredTenantId &&
                    entry.PartyType == PartyType.Supplier &&
                    entry.TransactionType == TransactionType.OpeningBalance &&
                    supplierIds.Contains(entry.PartyId))
                .Select(entry => entry.PartyId)
                .ToHashSetAsync(cancellationToken);
            var processedSuppliers = new HashSet<Guid>();

            foreach (var item in normalizedItems)
            {
                if (!processedSuppliers.Add(item.SupplierId))
                {
                    results.Add(new
                    {
                        supplierId = item.SupplierId,
                        success = true,
                        skipped = true,
                        message = "Duplicate supplier entry ignored after the first occurrence."
                    });
                    continue;
                }

                if (!suppliers.TryGetValue(item.SupplierId, out var supplier))
                {
                    results.Add(new { supplierId = item.SupplierId, success = false, message = "Supplier not found" });
                    continue;
                }

                if (supplier.OpeningBalanceAmount.HasValue || suppliersWithExistingOpeningBalance.Contains(item.SupplierId))
                {
                    results.Add(new { supplierId = item.SupplierId, success = false, message = "Opening balance already exists for this supplier" });
                    continue;
                }

                supplier.OpeningBalanceAmount = item.Amount;
                supplier.BalanceType = item.BalanceType;
                supplier.OpeningBalanceDate = item.EffectiveDateUtc;

                var referenceNumber = BuildSupplierOpeningBalanceReferenceNumber(supplier.Id);
                var debitAmount = item.BalanceType == "Debit" ? item.Amount : 0m;
                var creditAmount = item.BalanceType == "Credit" ? item.Amount : 0m;
                var balanceAfter = await _ledgerService.ApplySupplierTransaction(
                    supplier.Id,
                    item.EffectiveDateUtc,
                    TransactionType.OpeningBalance,
                    referenceNumber,
                    debitAmount,
                    creditAmount,
                    referenceId: null,
                    notes: item.Notes ?? "Opening Balance",
                    userId: null);

                _context.SupplierTransactions.Add(new SupplierTransaction
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.RequiredTenantId,
                    SupplierId = supplier.Id,
                    TransactionDate = item.EffectiveDateUtc,
                    TransactionType = TransactionType.OpeningBalance,
                    IsOpeningBalance = true,
                    DebitAmount = debitAmount,
                    CreditAmount = creditAmount,
                    BalanceAfter = balanceAfter,
                    ReferenceNumber = referenceNumber,
                    Notes = item.Notes,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });

                var openingBalanceEntryId = _context.AccountLedgers.Local
                    .Where(entry =>
                        entry.TenantId == supplier.TenantId &&
                        entry.PartyType == PartyType.Supplier &&
                        entry.PartyId == supplier.Id &&
                        entry.TransactionType == TransactionType.OpeningBalance &&
                        entry.DocumentNumber == referenceNumber)
                    .OrderByDescending(entry => entry.CreatedAt)
                    .ThenByDescending(entry => entry.Id)
                    .Select(entry => entry.Id)
                    .FirstOrDefault();

                if (openingBalanceEntryId == Guid.Empty)
                {
                    throw new InvalidOperationException($"Supplier opening balance ledger entry for supplier {supplier.Id} was not tracked.");
                }

                await _accountingOutbox.EnqueueSupplierOpeningBalanceAsync(
                    supplier.TenantId,
                    openingBalanceEntryId,
                    cancellationToken);
                suppliersWithExistingOpeningBalance.Add(supplier.Id);

                results.Add(new
                {
                    supplierId = item.SupplierId,
                    success = true,
                    skipped = false,
                    message = "Opening balance saved successfully",
                    balanceAfter
                });
            }

            return await CompleteFinancialMutationResultAsync(
                transaction,
                idempotencyRecord,
                Ok(new { message = "Opening balances processed", results }),
                cancellationToken);
        }
        catch (DuplicateOpeningBalanceException ex)
        {
            _logger.LogWarning(
                ex,
                "Duplicate supplier opening balance rejected | SupplierId: {SupplierId}",
                ex.PartyId);
            
            // Clear tracked changes to prevent partial commits
            _context.ChangeTracker.Clear();

            // Re-attach the idempotency record so CompleteAsync can persist
            // the completion fields (CompletedAtUtc, ResponseStatusCode, ResponseJson).
            _context.Attach(idempotencyRecord);

            return await CompleteFinancialMutationResultAsync(
                transaction,
                idempotencyRecord,
                Conflict(new { message = "Opening balance already exists for this supplier." }),
                cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error saving supplier opening balances");
            return StatusCode(500, new { message = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// Delete a supplier (soft delete)
    /// Suppliers cannot be deleted if they have a balance or transaction history.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> DeleteSupplier(Guid id)
    {
        var supplier = await _context.Suppliers.FirstOrDefaultAsync(entity => entity.Id == id);

        if (supplier == null || supplier.IsDeleted)
        {
            return NotFound(new { 
                errorCode = "SUPPLIER_NOT_FOUND",
                messageEn = "Supplier not found.",
                messageAr = "المورد غير موجود."
            });
        }

        // Validate if supplier can be deleted
        var validationResult = await _validationService.CanDeleteSupplierAsync(id);
        if (!validationResult.IsValid)
        {
            return BadRequest(new {
                errorCode = validationResult.ErrorCode,
                messageEn = validationResult.MessageEn,
                messageAr = validationResult.MessageAr,
                data = validationResult.Data
            });
        }

        // Soft delete
        supplier.IsDeleted = true;
        supplier.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();

        _logger.LogInformation("Supplier deleted: {SupplierId} - {SupplierName}", supplier.Id, supplier.Name);
        await _auditService.LogDelete("Supplier", supplier.Id.ToString(), $"Deleted supplier {supplier.Name}", null, supplier.Id.ToString());

        return NoContent();
    }

    /// <summary>
    /// Check if a supplier can be deleted - use this before showing delete button
    /// </summary>
    [HttpGet("{id}/can-delete")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> CanDeleteSupplier(Guid id)
    {
        var supplier = await _context.Suppliers.FirstOrDefaultAsync(entity => entity.Id == id);
        if (supplier == null || supplier.IsDeleted)
        {
            return NotFound();
        }

        var validationResult = await _validationService.CanDeleteSupplierAsync(id);
        return Ok(new {
            canDelete = validationResult.IsValid,
            errorCode = validationResult.ErrorCode,
            messageEn = validationResult.MessageEn,
            messageAr = validationResult.MessageAr
        });
    }

    private async Task<ActionResult<SupplierPaymentResponse>> CompleteFinancialMutationAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        PointOfSale.Core.Entities.SalesRequestIdempotency record,
        IActionResult result,
        CancellationToken cancellationToken)
    {
        var completedResult = await _financialMutationIdempotency.CompleteAsync(
            transaction,
            record,
            result,
            SnapshotFinancialMutationResult,
            cancellationToken);

        return ToSupplierPaymentActionResult(completedResult);
    }

    private static ActionResult<SupplierPaymentResponse> ToSupplierPaymentActionResult(IActionResult result) =>
        result as ActionResult ?? new StatusCodeResult(StatusCodes.Status500InternalServerError);

    private Task<IActionResult> CompleteFinancialMutationResultAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        SalesRequestIdempotency record,
        IActionResult result,
        CancellationToken cancellationToken)
    {
        return _financialMutationIdempotency.CompleteAsync(
            transaction,
            record,
            result,
            SnapshotFinancialMutationResult,
            cancellationToken);
    }

    private async Task<IActionResult?> ValidateSupplierFundingAvailabilityAsync(
        PaymentMethod paymentMethod,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        string accountCode;
        string accountName;

        switch (paymentMethod)
        {
            case PaymentMethod.Cash:
                var cashboxAccount = await _cashboxLedgerAccountService.ResolveDefaultCashboxAccountAsync(
                    tenantId,
                    cancellationToken);
                accountCode = cashboxAccount.AccountCode;
                accountName = cashboxAccount.Name;
                break;
            case PaymentMethod.Card:
                throw new InvalidOperationException("Supplier Card funding must use explicit instrument authority.");
            case PaymentMethod.BankTransfer:
                accountCode = AccountCodes.BankAccount;
                accountName = AccountCodes.BankAccount;
                break;
            default:
                throw new InvalidOperationException($"Unsupported payment method '{paymentMethod}'.");
        }

        // Verify account exists and is active
        var account = await _accountResolver.GetByCodeAsync(tenantId, accountCode, cancellationToken);
        
        if (!account.IsActive)
        {
            return BadRequest(new
            {
                message = $"Account {accountName} is inactive and cannot be used for payments.",
                code = "ACCOUNT_INACTIVE",
                accountCode,
            });
        }

        // Get account balance
        var availableBalance = await _treasuryReadService.GetAccountBalanceAsync(
            tenantId,
            accountCode,
            cancellationToken);

        // Verify sufficient funds
        if (availableBalance < amount)
        {
            return BadRequest(new
            {
                message = $"Insufficient funds in account {accountName}.",
                code = "INSUFFICIENT_FUNDS",
                accountCode,
                availableBalance,
                requiredAmount = amount,
                shortfall = amount - availableBalance,
            });
        }

        return null;
    }

    private string? ResolveFinancialIdempotencyKey()
    {
        return ValidateFinancialIdempotencyKey(Request.Headers[IdempotencyKeyHeader].FirstOrDefault());
    }

    private (string? OperationId, IActionResult? ErrorResult) ResolveOptionalSupplierMutationOperationId(string fallbackOperationId)
    {
        var rawHeaderValue = Request.Headers[IdempotencyKeyHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(rawHeaderValue))
        {
            return (fallbackOperationId, null);
        }

        var validatedOperationId = ValidateFinancialIdempotencyKey(rawHeaderValue);
        if (validatedOperationId == null)
        {
            return (null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} must contain only letters, numbers, or hyphens when provided.",
            }));
        }

        return (validatedOperationId, null);
    }

    private string BuildSupplierMutationIdempotencyKey(string operationId) =>
        $"SUPPLIER:{_tenantContext.RequiredTenantId}:{operationId}";

    private static string? ValidateFinancialIdempotencyKey(string? key)
    {
        var normalizedKey = key?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedKey) &&
            normalizedKey.Length <= MaxFinancialIdempotencyKeyLength &&
            FinancialIdempotencyKeyPattern.IsMatch(normalizedKey))
        {
            return normalizedKey;
        }

        return null;
    }

    private static string BuildSupplierPaymentRequestHash(
        Guid supplierId,
        CreateSupplierPaymentRequest request)
    {
        var payload = JsonSerializer.Serialize(new
        {
            supplierId,
            request.Amount,
            request.PaymentMethod,
            request.FundingInstrumentId,
            request.FundingInstrumentVersion,
            request.ReferenceNumber,
            request.Notes,
        }, FinancialMutationJsonOptions);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private (IReadOnlyList<NormalizedSupplierOpeningBalanceItem>? Items, IActionResult? ErrorResult) NormalizeSupplierOpeningBalanceRequest(
        SaveSupplierOpeningBalancesRequest request)
    {
        if (request.SupplierBalances == null || request.SupplierBalances.Count == 0)
        {
            return (null, BadRequest(new
            {
                message = "At least one supplier opening balance is required."
            }));
        }

        var normalizedItems = new List<NormalizedSupplierOpeningBalanceItem>();
        var firstOccurrenceBySupplier = new Dictionary<Guid, NormalizedSupplierOpeningBalanceItem>();

        foreach (var item in request.SupplierBalances)
        {
            if (item.SupplierId == Guid.Empty)
            {
                return (null, BadRequest(new
                {
                    message = "SupplierId is required for every opening balance item."
                }));
            }

            var roundedAmount = RoundCurrency(item.Amount);
            if (roundedAmount <= 0m)
            {
                return (null, BadRequest(new
                {
                    message = $"Amount for supplier {item.SupplierId} must be greater than zero. Provided: {item.Amount}, Rounded: {roundedAmount}"
                }));
            }

            var normalizedBalanceType = NormalizeBalanceType(item.BalanceType);
            if (normalizedBalanceType == null)
            {
                return (null, BadRequest(new
                {
                    message = $"BalanceType for supplier {item.SupplierId} must be 'Debit' or 'Credit'."
                }));
            }

            var normalizedItem = new NormalizedSupplierOpeningBalanceItem(
                item.SupplierId,
                roundedAmount,
                normalizedBalanceType,
                NormalizeUtc(item.Date),
                NormalizeNotes(item.Notes));

            if (firstOccurrenceBySupplier.TryGetValue(item.SupplierId, out var firstOccurrence) &&
                firstOccurrence != normalizedItem)
            {
                return (null, Conflict(new
                {
                    message = $"Supplier {item.SupplierId} has conflicting opening balance entries in the same request."
                }));
            }

            firstOccurrenceBySupplier[item.SupplierId] = normalizedItem;
            normalizedItems.Add(normalizedItem);
        }

        if (normalizedItems.Count == 0)
        {
            return (null, BadRequest(new
            {
                message = "At least one positive supplier opening balance is required."
            }));
        }

        return (normalizedItems, null);
    }

    private static string BuildSupplierOpeningBalanceRequestHash(
        IEnumerable<NormalizedSupplierOpeningBalanceItem> items)
    {
        var payload = JsonSerializer.Serialize(
            items
                .GroupBy(item => item.SupplierId)
                .Select(group => group.First())
                .OrderBy(item => item.SupplierId)
                .Select(item => new
                {
                    item.SupplierId,
                    item.Amount,
                    item.BalanceType,
                    item.EffectiveDateUtc,
                    item.Notes
                }),
            FinancialMutationJsonOptions);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private static string BuildSupplierOpeningBalanceReferenceNumber(Guid supplierId) =>
        $"OB-SUP-{supplierId.ToString()[..8]}";

    private static string? NormalizeBalanceType(string? balanceType)
    {
        var normalized = balanceType?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Equals("Debit", StringComparison.OrdinalIgnoreCase))
        {
            return "Debit";
        }

        if (normalized.Equals("Credit", StringComparison.OrdinalIgnoreCase))
        {
            return "Credit";
        }

        return null;
    }

    private static string? NormalizeNotes(string? notes)
    {
        var normalized = notes?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private static (int StatusCode, string? ResponseJson) SnapshotFinancialMutationResult(IActionResult result)
    {
        if (result is ObjectResult objectResult)
        {
            return (
                objectResult.StatusCode ?? StatusCodes.Status200OK,
                SerializeFinancialPayload(objectResult.Value));
        }

        if (result is StatusCodeResult statusCodeResult)
        {
            return (statusCodeResult.StatusCode, null);
        }

        return (StatusCodes.Status200OK, null);
    }

    private static string? SerializeFinancialPayload(object? payload)
    {
        return payload == null
            ? null
            : JsonSerializer.Serialize(payload, FinancialMutationJsonOptions);
    }

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private sealed record NormalizedSupplierOpeningBalanceItem(
        Guid SupplierId,
        decimal Amount,
        string BalanceType,
        DateTime EffectiveDateUtc,
        string? Notes);
}

public record CreateSupplierRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string? Email { get; set; }
    
    [MaxLength(50)]
    public string? Phone { get; set; }
    
    public string? Address { get; set; }
    
    [MaxLength(50)]
    public string? TaxNumber { get; set; }
    
    public int PaymentTermsDays { get; set; }
    
    public string? Notes { get; set; }
}

public record UpdateSupplierRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string? Email { get; set; }
    
    [MaxLength(50)]
    public string? Phone { get; set; }
    
    public string? Address { get; set; }
    
    [MaxLength(50)]
    public string? TaxNumber { get; set; }
    
    public int PaymentTermsDays { get; set; }
    
    public string? Notes { get; set; }
}

public record CreateSupplierPaymentRequest
{
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public Guid? FundingInstrumentId { get; set; }
    public int? FundingInstrumentVersion { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

public record SupplierPaymentResponse
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public Guid? FundingInstrumentId { get; set; }
    public int? FundingInstrumentVersion { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public decimal BalanceAfter { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public record SupplierPaymentHistoryResponse
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public DateTime TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public Guid? FundingInstrumentId { get; set; }
    public int? FundingInstrumentVersion { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

// Opening Balance DTOs
public record SupplierOpeningBalanceResponse
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public decimal? OpeningBalanceAmount { get; set; }
    public string? BalanceType { get; set; }
    public DateTime? OpeningBalanceDate { get; set; }
    public bool HasOpeningBalance { get; set; }
}

public record SaveSupplierOpeningBalancesRequest
{
    public List<SupplierOpeningBalanceItem> SupplierBalances { get; set; } = new();
}

public record SupplierOpeningBalanceItem
{
    public Guid SupplierId { get; set; }
    public decimal Amount { get; set; }
    public string BalanceType { get; set; } = "Debit"; // "Debit" or "Credit"
    public DateTime Date { get; set; }
    public string? Notes { get; set; }
}
