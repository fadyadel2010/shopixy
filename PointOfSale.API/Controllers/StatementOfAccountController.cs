using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services;
using PointOfSale.API.DTOs;
using PointOfSale.API.Services.Audit;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StatementOfAccountController : ControllerBase
{
    private const string AccountingGovernanceRoles = "Owner,Admin,Manager";
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const int MaxFinancialIdempotencyKeyLength = 100;
    private static readonly Regex FinancialIdempotencyKeyPattern = new(
        "^[A-Za-z0-9-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions FinancialMutationJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<StatementOfAccountController> _logger;
    private readonly ILedgerService _ledgerService;
    private readonly IAuditService _auditService;
    private readonly IAccountingOutboxService _accountingOutbox;
    private readonly IFinancialMutationIdempotencyService _financialMutationIdempotency;

    public StatementOfAccountController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<StatementOfAccountController> logger,
        ILedgerService ledgerService,
        IAuditService auditService,
        IAccountingOutboxService accountingOutbox,
        IFinancialMutationIdempotencyService financialMutationIdempotency)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
        _ledgerService = ledgerService;
        _auditService = auditService;
        _accountingOutbox = accountingOutbox;
        _financialMutationIdempotency = financialMutationIdempotency;
    }

    /// <summary>
    /// Get Statement of Account with filtering
    /// </summary>
    [HttpPost("query")]
    public async Task<ActionResult<SOAResponse>> GetStatementOfAccount(SOAFilterRequest request)
    {
        try
        {
            request.Page = PaginationGuard.NormalizePage(request.Page);
            request.PageSize = PaginationGuard.NormalizePageSize(request.PageSize);
            var skip = PaginationGuard.GetSkip(request.Page, request.PageSize);

            var query = _context.AccountLedgers
                .AsNoTracking()
                .Where(a => a.TenantId == _tenantContext.RequiredTenantId)
                .Where(a => a.PartyType == request.PartyType)
                .AsQueryable();

            // Filter by party
            if (request.PartyId.HasValue)
            {
                query = query.Where(a => a.PartyId == request.PartyId.Value);
            }

            // Filter by date range
            if (request.StartDate.HasValue)
            {
                query = query.Where(a => a.TransactionDate >= request.StartDate.Value);
            }

            if (request.EndDate.HasValue)
            {
                query = query.Where(a => a.TransactionDate <= request.EndDate.Value);
            }

            // Calculate opening balance (transactions before start date)
            decimal openingBalance = 0;
            if (request.StartDate.HasValue && request.PartyId.HasValue)
            {
                var openingQuery = _context.AccountLedgers
                    .Where(a => a.TenantId == _tenantContext.RequiredTenantId &&
                               a.PartyType == request.PartyType &&
                               a.PartyId == request.PartyId.Value &&
                               a.TransactionDate < request.StartDate.Value)
                    .OrderByDescending(a => a.TransactionDate)
                    .FirstOrDefaultAsync();
                
                var lastTransaction = await openingQuery;
                openingBalance = lastTransaction?.RunningBalance ?? 0;
            }

            // Get total count
            var totalCount = await query.CountAsync();

            // Get paginated transactions
            var transactions = await query
                .OrderBy(a => a.TransactionDate)
                .ThenBy(a => a.CreatedAt)
                .Skip(skip)
                .Take(request.PageSize)
                .Select(a => new SOATransaction
                {
                    Id = a.Id,
                    TransactionDate = a.TransactionDate,
                    TransactionType = a.TransactionType,
                    DocumentNumber = a.DocumentNumber,
                    DebitAmount = a.DebitAmount,
                    CreditAmount = a.CreditAmount,
                    RunningBalance = a.RunningBalance,
                    Notes = a.Notes,
                    UserName = a.User != null ? a.User.Username : null
                })
                .ToListAsync();

            var aggregates = await query
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    OpeningBalanceAdjustment = g
                        .Where(a => a.TransactionType == TransactionType.OpeningBalance)
                        .Sum(a => (decimal?)(a.DebitAmount - a.CreditAmount)),
                    TotalDebit = g
                        .Where(a => a.TransactionType != TransactionType.OpeningBalance)
                        .Sum(a => (decimal?)a.DebitAmount),
                    TotalCredit = g
                        .Where(a => a.TransactionType != TransactionType.OpeningBalance)
                        .Sum(a => (decimal?)a.CreditAmount)
                })
                .FirstOrDefaultAsync();

            var openingBalanceAdjustment = aggregates?.OpeningBalanceAdjustment ?? 0m;
            var totalDebit = aggregates?.TotalDebit ?? 0m;
            var totalCredit = aggregates?.TotalCredit ?? 0m;

            var effectiveOpeningBalance = openingBalance + openingBalanceAdjustment;
            
            var closingBalance = effectiveOpeningBalance;
            closingBalance += totalDebit - totalCredit;

            // If we moved Opening Balance transactions to the header, we might want to filter them out of the list
            // However, for pagination consistency, we should probably keep them in the list but maybe mark them?
            // Or, if the user wants them in the "Opening Balance" card, they usually imply they shouldn't be in the list.
            // But removing them from the paginated list 'transactions' is tricky because we already fetched page N.
            // For now, let's just update the Summary cards as requested. The transaction will still appear in the table, 
            // which is actually good for audit trails.
            
            // WAIT: If we add it to Opening Balance AND keep it in the list (which contributes to Total Debit/Credit visually),
            // the user might be confused. 
            // BUT, the "Total Debit" and "Total Credit" calculated above EXCLUDE the opening balance transactions.
            // So the summary cards will show:
            // Opening Balance: $500 (Includes the OB transaction)
            // Total Debit: $100 (Excludes OB)
            // Closing: $600
            
            // This seems to be what is requested.

            // Get party name
            string? partyName = null;
            if (request.PartyId.HasValue)
            {
                if (request.PartyType == PartyType.Customer)
                {
                    var customer = await _context.Customers
                        .AsNoTracking()
                        .FirstOrDefaultAsync(item =>
                            item.Id == request.PartyId.Value &&
                            item.TenantId == _tenantContext.RequiredTenantId);
                    partyName = customer?.Name;
                }
                else
                {
                    var supplier = await _context.Suppliers
                        .AsNoTracking()
                        .FirstOrDefaultAsync(item =>
                            item.Id == request.PartyId.Value &&
                            item.TenantId == _tenantContext.RequiredTenantId);
                    partyName = supplier?.Name;
                }
            }

            var response = new SOAResponse
            {
                PartyType = request.PartyType,
                PartyId = request.PartyId,
                PartyName = partyName,
                Summary = new SOASummary
                {
                    OpeningBalance = Math.Abs(effectiveOpeningBalance),
                    TotalDebit = totalDebit,
                    TotalCredit = totalCredit,
                    ClosingBalance = Math.Abs(closingBalance),
                    BalanceType = DetermineBalanceType(closingBalance, request.PartyType)
                },
                Transactions = transactions,
                TotalCount = totalCount,
                Page = request.Page,
                PageSize = request.PageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize)
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving statement of account");
            return StatusCode(500, new { message = "An error occurred while retrieving the statement" });
        }
    }

    /// <summary>
    /// Get balance for a specific party
    /// </summary>
    [HttpGet("balance/{partyType}/{partyId}")]
    public async Task<ActionResult<PartyBalanceResponse>> GetPartyBalance(PartyType partyType, Guid partyId)
    {
        try
        {
            var ledgerQuery = _context.AccountLedgers
                .AsNoTracking()
                .Where(a =>
                    a.TenantId == _tenantContext.RequiredTenantId &&
                    a.PartyType == partyType &&
                    a.PartyId == partyId);

            var aggregates = partyType == PartyType.Customer
                ? await ledgerQuery
                    .GroupBy(_ => 1)
                    .Select(group => new
                    {
                        Balance = group.Sum(entry => entry.DebitAmount - entry.CreditAmount),
                        LastPaymentDate = group
                            .Where(entry => entry.TransactionType == TransactionType.PaymentReceived)
                            .Max(entry => (DateTime?)entry.TransactionDate),
                        TotalPaid = group
                            .Where(entry => entry.TransactionType == TransactionType.PaymentReceived)
                            .Sum(entry => (decimal?)entry.CreditAmount) ?? 0m,
                        TotalInvoiced = group
                            .Where(entry => entry.TransactionType == TransactionType.Sale)
                            .Sum(entry => (decimal?)entry.DebitAmount) ?? 0m
                    })
                    .FirstOrDefaultAsync()
                : await ledgerQuery
                    .GroupBy(_ => 1)
                    .Select(group => new
                    {
                        Balance = group.Sum(entry => entry.DebitAmount - entry.CreditAmount),
                        LastPaymentDate = group
                            .Where(entry => entry.TransactionType == TransactionType.PaymentMade)
                            .Max(entry => (DateTime?)entry.TransactionDate),
                        TotalPaid = group
                            .Where(entry => entry.TransactionType == TransactionType.PaymentMade)
                            .Sum(entry => (decimal?)entry.DebitAmount) ?? 0m,
                        TotalInvoiced = group
                            .Where(entry => entry.TransactionType == TransactionType.Purchase)
                            .Sum(entry => (decimal?)entry.CreditAmount) ?? 0m
                    })
                    .FirstOrDefaultAsync();
            var balance = aggregates?.Balance ?? 0m;

            string partyName = string.Empty;
            if (partyType == PartyType.Customer)
            {
                var customer = await _context.Customers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item =>
                        item.Id == partyId &&
                        item.TenantId == _tenantContext.RequiredTenantId);
                partyName = customer?.Name ?? string.Empty;
            }
            else
            {
                var supplier = await _context.Suppliers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item =>
                        item.Id == partyId &&
                        item.TenantId == _tenantContext.RequiredTenantId);
                partyName = supplier?.Name ?? string.Empty;
            }

            return Ok(new PartyBalanceResponse
            {
                PartyId = partyId,
                PartyName = partyName,
                PartyType = partyType,
                Balance = Math.Abs(balance),
                BalanceType = DetermineBalanceType(balance, partyType),
                LastPaymentDate = aggregates?.LastPaymentDate,
                TotalPaid = aggregates?.TotalPaid ?? 0m,
                TotalInvoiced = aggregates?.TotalInvoiced ?? 0m
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving party balance");
            return StatusCode(500, new { message = "An error occurred while retrieving the balance" });
        }
    }

    /// <summary>
    /// Create opening balance for a party
    /// </summary>
    [HttpPost("opening-balance")]
    public async Task<ActionResult> CreateOpeningBalance(
        CreateOpeningBalanceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Amount <= 0m)
        {
            return BadRequest(new { message = "Opening balance amount must be greater than zero." });
        }

        if (request.PartyType == PartyType.Supplier)
        {
            var requestHash = BuildOpeningBalanceRequestHash(request);
            var operationIdResolution = ResolveOptionalSupplierMutationOperationId(requestHash);
            if (operationIdResolution.ErrorResult != null)
            {
                return operationIdResolution.ErrorResult;
            }

            var scopedIdempotencyKey = BuildPartyMutationIdempotencyKey(PartyType.Supplier, operationIdResolution.OperationId!);
            await using var supplierTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            var claim = await _financialMutationIdempotency.TryClaimAsync(
                operation: "SUPPLIER_OPENING_BALANCE_CREATE",
                key: scopedIdempotencyKey,
                requestHash: requestHash,
                cancellationToken: cancellationToken);

            if (claim.ShortCircuitResult != null)
            {
                await supplierTransaction.RollbackAsync(cancellationToken);
                return claim.ShortCircuitResult as ActionResult
                    ?? new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            var idempotencyRecord = claim.Record!;

            try
            {
                var supplier = await _context.Suppliers
                    .FirstOrDefaultAsync(item =>
                        item.Id == request.PartyId &&
                        item.TenantId == _tenantContext.RequiredTenantId &&
                        !item.IsDeleted,
                        cancellationToken);
                if (supplier == null)
                {
                    return await CompleteFinancialMutationResultAsync(
                        supplierTransaction,
                        idempotencyRecord,
                        BadRequest(new { message = "Supplier not found" }),
                        cancellationToken);
                }

                var existingOpeningBalance = await _context.AccountLedgers
                    .AnyAsync(entry =>
                        entry.TenantId == _tenantContext.RequiredTenantId &&
                        entry.PartyType == PartyType.Supplier &&
                        entry.PartyId == request.PartyId &&
                        entry.TransactionType == TransactionType.OpeningBalance,
                        cancellationToken);

                if (supplier.OpeningBalanceAmount.HasValue || existingOpeningBalance)
                {
                    return await CompleteFinancialMutationResultAsync(
                        supplierTransaction,
                        idempotencyRecord,
                        Conflict(new { message = "Opening balance already exists for this supplier" }),
                        cancellationToken);
                }

                var effectiveDateUtc = NormalizeUtc(request.EffectiveDate);
                var normalizedNotes = NormalizeNotes(request.Notes);
                var roundedAmount = RoundCurrency(request.Amount);
                var debitAmount = request.IsDebit ? roundedAmount : 0m;
                var creditAmount = request.IsDebit ? 0m : roundedAmount;
                var documentNumber = BuildSupplierOpeningBalanceReferenceNumber(request.PartyId);

                var balanceAfter = await _ledgerService.ApplySupplierTransaction(
                    request.PartyId,
                    effectiveDateUtc,
                    TransactionType.OpeningBalance,
                    documentNumber,
                    debitAmount,
                    creditAmount,
                    referenceId: null,
                    notes: normalizedNotes ?? "Opening Balance");

                // Only update supplier fields after successful ledger write
                supplier.OpeningBalanceAmount = roundedAmount;
                supplier.BalanceType = request.IsDebit ? "Debit" : "Credit";
                supplier.OpeningBalanceDate = effectiveDateUtc;

                _context.SupplierTransactions.Add(new SupplierTransaction
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.RequiredTenantId,
                    SupplierId = request.PartyId,
                    TransactionDate = effectiveDateUtc,
                    TransactionType = TransactionType.OpeningBalance,
                    DebitAmount = debitAmount,
                    CreditAmount = creditAmount,
                    BalanceAfter = balanceAfter,
                    ReferenceNumber = documentNumber,
                    IsOpeningBalance = true,
                    Notes = normalizedNotes,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                });

                var openingBalanceEntryId = _context.AccountLedgers.Local
                    .Where(entry =>
                        entry.TenantId == supplier.TenantId &&
                        entry.PartyType == PartyType.Supplier &&
                        entry.PartyId == supplier.Id &&
                        entry.TransactionType == TransactionType.OpeningBalance &&
                        entry.DocumentNumber == documentNumber)
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

                return await CompleteFinancialMutationResultAsync(
                    supplierTransaction,
                    idempotencyRecord,
                    Ok(new { message = "Opening balance created successfully" }),
                    cancellationToken);
            }
            catch (DuplicateOpeningBalanceException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Duplicate supplier opening balance rejected | SupplierId: {SupplierId}",
                    ex.PartyId);
                return await CompleteFinancialMutationResultAsync(
                    supplierTransaction,
                    idempotencyRecord,
                    Conflict(new { message = "Opening balance already exists for this supplier" }),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                await supplierTransaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Error creating supplier opening balance");
                return StatusCode(500, new { message = "An error occurred while creating opening balance" });
            }
        }

        var customerRequestHash = BuildOpeningBalanceRequestHash(request);
        var customerOperationIdResolution = ResolveOptionalSupplierMutationOperationId(customerRequestHash);
        if (customerOperationIdResolution.ErrorResult != null)
        {
            return customerOperationIdResolution.ErrorResult;
        }

        var customerScopedIdempotencyKey = BuildPartyMutationIdempotencyKey(PartyType.Customer, customerOperationIdResolution.OperationId!);
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var customerClaim = await _financialMutationIdempotency.TryClaimAsync(
            operation: "CUSTOMER_OPENING_BALANCE_CREATE",
            key: customerScopedIdempotencyKey,
            requestHash: customerRequestHash,
            cancellationToken: cancellationToken);

        if (customerClaim.ShortCircuitResult != null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return customerClaim.ShortCircuitResult as ActionResult
                ?? new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        var customerIdempotencyRecord = customerClaim.Record!;

        try
        {
            var customer = await _context.Customers
                .FirstOrDefaultAsync(item =>
                    item.Id == request.PartyId &&
                    item.TenantId == _tenantContext.RequiredTenantId &&
                    !item.IsDeleted,
                    cancellationToken);
            if (customer == null)
            {
                return await CompleteFinancialMutationResultAsync(
                    transaction,
                    customerIdempotencyRecord,
                    BadRequest(new { message = "Customer not found" }),
                    cancellationToken);
            }

            var existingOpening = await _context.AccountLedgers
                .AnyAsync(a =>
                    a.TenantId == _tenantContext.RequiredTenantId &&
                    a.PartyType == PartyType.Customer &&
                    a.PartyId == request.PartyId &&
                    a.TransactionType == TransactionType.OpeningBalance,
                    cancellationToken);

            if (customer.OpeningBalanceAmount.HasValue || existingOpening)
            {
                return await CompleteFinancialMutationResultAsync(
                    transaction,
                    customerIdempotencyRecord,
                    Conflict(new { message = "Opening balance already exists for this customer" }),
                    cancellationToken);
            }

            var effectiveDateUtc = NormalizeUtc(request.EffectiveDate);
            var normalizedNotes = NormalizeNotes(request.Notes);
            var roundedAmount = RoundCurrency(request.Amount);
            var debitAmount = request.IsDebit ? roundedAmount : 0m;
            var creditAmount = request.IsDebit ? 0m : roundedAmount;
            var documentNumber = BuildCustomerOpeningBalanceReferenceNumber(request.PartyId);
            var balanceAfter = await _ledgerService.ApplyCustomerTransaction(
                request.PartyId,
                effectiveDateUtc,
                TransactionType.OpeningBalance,
                documentNumber,
                debitAmount,
                creditAmount,
                referenceId: null,
                notes: normalizedNotes ?? "Opening Balance");

            customer.OpeningBalanceAmount = roundedAmount;
            customer.BalanceType = request.IsDebit ? "Debit" : "Credit";
            customer.OpeningBalanceDate = effectiveDateUtc;

            _context.CustomerTransactions.Add(new CustomerTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,
                CustomerId = request.PartyId,
                TransactionDate = effectiveDateUtc,
                TransactionType = TransactionType.OpeningBalance,
                DebitAmount = debitAmount,
                CreditAmount = creditAmount,
                BalanceAfter = balanceAfter,
                ReferenceNumber = documentNumber,
                IsOpeningBalance = true,
                Notes = normalizedNotes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });

            var openingBalanceEntryId = _context.AccountLedgers.Local
                .Where(entry =>
                    entry.TenantId == customer.TenantId &&
                    entry.PartyType == PartyType.Customer &&
                    entry.PartyId == customer.Id &&
                    entry.TransactionType == TransactionType.OpeningBalance &&
                    entry.DocumentNumber == documentNumber)
                .OrderByDescending(entry => entry.CreatedAt)
                .ThenByDescending(entry => entry.Id)
                .Select(entry => entry.Id)
                .FirstOrDefault();

            if (openingBalanceEntryId == Guid.Empty)
            {
                throw new InvalidOperationException($"Customer opening balance ledger entry for customer {customer.Id} was not tracked.");
            }

            await _accountingOutbox.EnqueueCustomerOpeningBalanceAsync(
                customer.TenantId,
                openingBalanceEntryId,
                cancellationToken);

            return await CompleteFinancialMutationResultAsync(
                transaction,
                customerIdempotencyRecord,
                Ok(new { message = "Opening balance created successfully" }),
                cancellationToken);
        }
        catch (DuplicateOpeningBalanceException ex)
        {
            _logger.LogWarning(
                ex,
                "Duplicate customer opening balance rejected | CustomerId: {CustomerId}",
                ex.PartyId);
            return await CompleteFinancialMutationResultAsync(
                transaction,
                customerIdempotencyRecord,
                Conflict(new { message = "Opening balance already exists for this customer" }),
                cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error creating customer opening balance");
            return StatusCode(500, new { message = "An error occurred while creating opening balance" });
        }
    }

    /// <summary>
    /// Create adjustment (Credit Note, Debit Note, etc.)
    /// </summary>
    [HttpPost("adjustment")]
    [Authorize(Roles = AccountingGovernanceRoles)]
    public async Task<ActionResult<PartyLedgerAdjustmentResponse>> CreateAdjustment(
        CreateAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = ResolveFinancialIdempotencyKey();
        if (idempotencyKey == null)
        {
            return BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} header is required and must contain only letters, numbers, or hyphens."
            });
        }

        var requestHash = BuildAdjustmentRequestHash(request);
        var scopedIdempotencyKey = BuildPartyMutationIdempotencyKey(request.PartyType, idempotencyKey);
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var claim = await _financialMutationIdempotency.TryClaimAsync(
            operation: "SOA_ADJUSTMENT_CREATE",
            key: scopedIdempotencyKey,
            requestHash: requestHash,
            cancellationToken: cancellationToken);

        if (claim.ShortCircuitResult != null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ToPartyLedgerAdjustmentActionResult(claim.ShortCircuitResult);
        }

        var idempotencyRecord = claim.Record!;

        try
        {
            // Validate adjustment type
            if (request.AdjustmentType != TransactionType.CreditNote &&
                request.AdjustmentType != TransactionType.DebitNote &&
                request.AdjustmentType != TransactionType.CreditAdjustment &&
                request.AdjustmentType != TransactionType.DebitAdjustment)
            {
                return await CompleteFinancialMutationAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new { message = "Invalid adjustment type" }),
                    cancellationToken);
            }

            if (request.Amount <= 0)
            {
                return await CompleteFinancialMutationAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new { message = "Adjustment amount must be greater than zero." }),
                    cancellationToken);
            }

            // Validate party exists
            Customer? customer = null;
            Supplier? supplier = null;
            if (request.PartyType == PartyType.Customer)
            {
                customer = await _context.Customers
                    .FirstOrDefaultAsync(item =>
                        item.Id == request.PartyId &&
                        item.TenantId == _tenantContext.RequiredTenantId &&
                        !item.IsDeleted,
                        cancellationToken);
                if (customer == null)
                {
                    return await CompleteFinancialMutationAsync(
                        transaction,
                        idempotencyRecord,
                        BadRequest(new { message = "Customer not found" }),
                        cancellationToken);
                }
            }
            else
            {
                supplier = await _context.Suppliers
                    .FirstOrDefaultAsync(item =>
                        item.Id == request.PartyId &&
                        item.TenantId == _tenantContext.RequiredTenantId &&
                        !item.IsDeleted,
                        cancellationToken);
                if (supplier == null)
                {
                    return await CompleteFinancialMutationAsync(
                        transaction,
                        idempotencyRecord,
                        BadRequest(new { message = "Supplier not found" }),
                        cancellationToken);
                }
            }

            // Determine debit/credit based on adjustment type
            bool isDebit = request.AdjustmentType == TransactionType.DebitNote || 
                          request.AdjustmentType == TransactionType.DebitAdjustment;
            
            var debitAmount = isDebit ? request.Amount : 0;
            var creditAmount = isDebit ? 0 : request.Amount;
            var prefix = request.AdjustmentType switch
            {
                TransactionType.CreditNote => "CN",
                TransactionType.DebitNote => "DN",
                TransactionType.CreditAdjustment => "CA",
                TransactionType.DebitAdjustment => "DA",
                _ => "ADJ"
            };

            var adjustmentTimestamp = DateTime.UtcNow;
            var documentNumber = string.IsNullOrWhiteSpace(request.ReferenceNumber)
                ? $"{prefix}-{adjustmentTimestamp:yyyyMMddHHmmss}"
                : request.ReferenceNumber.Trim();
            var normalizedNotes = string.IsNullOrWhiteSpace(request.Notes)
                ? null
                : request.Notes.Trim();

            var adjustment = new PartyLedgerAdjustment
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,
                PartyType = request.PartyType,
                PartyId = request.PartyId,
                TransactionType = request.AdjustmentType,
                AdjustmentDate = adjustmentTimestamp,
                Amount = RoundCurrency(request.Amount),
                IdempotencyKey = idempotencyKey,
                ReferenceNumber = documentNumber,
                Notes = normalizedNotes,
                CreatedAt = adjustmentTimestamp,
                UpdatedAt = adjustmentTimestamp,
            };

            _context.PartyLedgerAdjustments.Add(adjustment);

            decimal balanceAfter;
            if (request.PartyType == PartyType.Supplier)
            {
                balanceAfter = await _ledgerService.ApplySupplierTransaction(
                    request.PartyId,
                    adjustmentTimestamp,
                    request.AdjustmentType,
                    documentNumber,
                    debitAmount,
                    creditAmount,
                    referenceId: adjustment.Id,
                    notes: normalizedNotes);

                _context.SupplierTransactions.Add(new SupplierTransaction
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.RequiredTenantId,
                    SupplierId = request.PartyId,
                    TransactionDate = adjustmentTimestamp,
                    TransactionType = request.AdjustmentType,
                    DebitAmount = debitAmount,
                    CreditAmount = creditAmount,
                    BalanceAfter = balanceAfter,
                    ReferenceId = adjustment.Id,
                    ReferenceNumber = documentNumber,
                    Notes = normalizedNotes,
                    CreatedAt = adjustmentTimestamp,
                    UpdatedAt = adjustmentTimestamp,
                });
            }
            else
            {
                await _ledgerService.CreateLedgerEntry(
                    request.PartyType,
                    request.PartyId,
                    adjustmentTimestamp,
                    request.AdjustmentType,
                    documentNumber,
                    debitAmount,
                    creditAmount,
                    referenceId: adjustment.Id,
                    notes: normalizedNotes);

                balanceAfter = customer!.Balance;

                _context.CustomerTransactions.Add(new CustomerTransaction
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.RequiredTenantId,
                    CustomerId = request.PartyId,
                    TransactionDate = adjustmentTimestamp,
                    TransactionType = request.AdjustmentType,
                    DebitAmount = debitAmount,
                    CreditAmount = creditAmount,
                    BalanceAfter = balanceAfter,
                    ReferenceId = adjustment.Id,
                    ReferenceNumber = documentNumber,
                    Notes = normalizedNotes,
                    CreatedAt = adjustmentTimestamp,
                    UpdatedAt = adjustmentTimestamp,
                });
            }

            await _accountingOutbox.EnqueuePartyLedgerAdjustmentAsync(
                adjustment.TenantId,
                adjustment.Id,
                cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            var response = new PartyLedgerAdjustmentResponse
            {
                Id = adjustment.Id,
                PartyType = adjustment.PartyType,
                PartyId = adjustment.PartyId,
                PartyName = request.PartyType == PartyType.Customer ? customer!.Name : supplier!.Name,
                AdjustmentType = adjustment.TransactionType,
                AdjustmentDate = adjustment.AdjustmentDate,
                Amount = adjustment.Amount,
                ReferenceNumber = adjustment.ReferenceNumber,
                Notes = adjustment.Notes,
                BalanceAfter = balanceAfter,
                IdempotencyKey = adjustment.IdempotencyKey,
            };

            await _auditService.LogCreate(
                "PartyLedgerAdjustment",
                adjustment.Id.ToString(),
                $"Recorded {request.PartyType} adjustment {documentNumber}",
                response,
                documentNumber);

            return await CompleteFinancialMutationAsync(
                transaction,
                idempotencyRecord,
                Ok(response),
                cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error creating adjustment");
            return StatusCode(500, new { message = "An error occurred while creating adjustment" });
        }
    }

    private async Task<ActionResult<PartyLedgerAdjustmentResponse>> CompleteFinancialMutationAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        SalesRequestIdempotency record,
        IActionResult result,
        CancellationToken cancellationToken)
    {
        var completedResult = await _financialMutationIdempotency.CompleteAsync(
            transaction,
            record,
            result,
            SnapshotFinancialMutationResult,
            cancellationToken);

        return ToPartyLedgerAdjustmentActionResult(completedResult);
    }

    private static ActionResult<PartyLedgerAdjustmentResponse> ToPartyLedgerAdjustmentActionResult(IActionResult result) =>
        result as ActionResult ?? new StatusCodeResult(StatusCodes.Status500InternalServerError);

    private async Task<ActionResult> CompleteFinancialMutationResultAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        SalesRequestIdempotency record,
        IActionResult result,
        CancellationToken cancellationToken)
    {
        var completedResult = await _financialMutationIdempotency.CompleteAsync(
            transaction,
            record,
            result,
            SnapshotFinancialMutationResult,
            cancellationToken);

        return completedResult as ActionResult
            ?? new StatusCodeResult(StatusCodes.Status500InternalServerError);
    }

    private string? ResolveFinancialIdempotencyKey()
    {
        return ValidateFinancialIdempotencyKey(Request.Headers[IdempotencyKeyHeader].FirstOrDefault());
    }

    private (string? OperationId, ActionResult? ErrorResult) ResolveOptionalSupplierMutationOperationId(string fallbackOperationId)
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
                message = $"{IdempotencyKeyHeader} must contain only letters, numbers, or hyphens when provided."
            }));
        }

        return (validatedOperationId, null);
    }

    private string BuildSupplierMutationIdempotencyKey(string operationId) =>
        $"SUPPLIER:{_tenantContext.RequiredTenantId}:{operationId}";

    private string BuildPartyMutationIdempotencyKey(PartyType partyType, string operationId) =>
        $"{partyType.ToString().ToUpperInvariant()}:{_tenantContext.RequiredTenantId}:{operationId}";

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

    private static string BuildOpeningBalanceRequestHash(CreateOpeningBalanceRequest request)
    {
        var payload = JsonSerializer.Serialize(new
        {
            request.PartyType,
            request.PartyId,
            request.Amount,
            request.IsDebit,
            EffectiveDate = request.EffectiveDate.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(request.EffectiveDate, DateTimeKind.Utc)
                : request.EffectiveDate.ToUniversalTime(),
            request.Notes,
        }, FinancialMutationJsonOptions);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private static string BuildAdjustmentRequestHash(CreateAdjustmentRequest request)
    {
        var payload = JsonSerializer.Serialize(new
        {
            request.PartyType,
            request.PartyId,
            request.AdjustmentType,
            request.Amount,
            request.ReferenceNumber,
            request.Notes,
        }, FinancialMutationJsonOptions);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private static string BuildSupplierOpeningBalanceReferenceNumber(Guid supplierId) =>
        $"OB-SUP-{supplierId.ToString()[..8]}";

    private static string BuildCustomerOpeningBalanceReferenceNumber(Guid customerId) =>
        $"OB-CUS-{customerId.ToString()[..8]}";

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

    /// <summary>
    /// Get all parties with outstanding balances
    /// </summary>
    [HttpGet("outstanding/{partyType}")]
    public async Task<ActionResult<List<PartyBalanceResponse>>> GetOutstandingBalances(
        PartyType partyType,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            page = PaginationGuard.NormalizePage(page);
            pageSize = PaginationGuard.NormalizePageSize(pageSize);
            var skip = PaginationGuard.GetSkip(page, pageSize);
            var normalizedSearch = search?.Trim();
            var today = DateTime.UtcNow.Date;

            List<PartyBalanceResponse> result;
            int totalCount;
            decimal totalDebit;
            decimal totalCredit;

            if (partyType == PartyType.Customer)
            {
                var customersQuery =
                    from customer in _context.Customers.AsNoTracking()
                    join balance in (_context.AccountLedgers.AsNoTracking()
                            .Where(entry => entry.PartyType == PartyType.Customer)
                            .GroupBy(entry => entry.PartyId)
                            .Select(ledgerGroup => new
                            {
                                PartyId = ledgerGroup.Key,
                                Balance = ledgerGroup.Sum(entry => entry.DebitAmount - entry.CreditAmount)
                            }))
                        on customer.Id equals balance.PartyId
                    where balance.Balance != 0m
                    select new
                    {
                        customer.Id,
                        customer.Name,
                        balance.Balance
                    };

                if (!string.IsNullOrWhiteSpace(normalizedSearch))
                {
                    customersQuery = customersQuery.Where(c => EF.Functions.ILike(c.Name, $"%{normalizedSearch}%"));
                }

                totalCount = await customersQuery.CountAsync();
                totalDebit = await customersQuery
                    .Where(c => c.Balance > 0m)
                    .Select(c => (decimal?)c.Balance)
                    .SumAsync() ?? 0m;
                totalCredit = Math.Abs(await customersQuery
                    .Where(c => c.Balance < 0m)
                    .Select(c => (decimal?)c.Balance)
                    .SumAsync() ?? 0m);

                // Materialize paged customer IDs first
                var customers = await customersQuery
                    .OrderBy(c => c.Name)
                    .ThenBy(c => c.Id)
                    .Skip(skip)
                    .Take(pageSize)
                    .ToListAsync();

                var customerIds = customers.Select(c => c.Id).ToList();

                // Precompute aggregates for customer transactions (filtered to paged customers only)
                var customerAggregates = await _context.AccountLedgers
                    .Where(entry =>
                        entry.PartyType == PartyType.Customer &&
                        customerIds.Contains(entry.PartyId))
                    .GroupBy(entry => entry.PartyId)
                    .Select(g => new
                    {
                        CustomerId = g.Key,
                        LastPaymentDate = g
                            .Where(entry => entry.TransactionType == TransactionType.PaymentReceived)
                            .Max(entry => (DateTime?)entry.TransactionDate),
                        TotalPaid = g
                            .Where(entry => entry.TransactionType == TransactionType.PaymentReceived)
                            .Sum(entry => (decimal?)entry.CreditAmount) ?? 0m,
                        TotalInvoiced = g
                            .Where(entry => entry.TransactionType == TransactionType.Sale)
                            .Sum(entry => (decimal?)entry.DebitAmount) ?? 0m
                    })
                    .ToListAsync();

                var customerAggregatesDict = customerAggregates.ToDictionary(a => a.CustomerId);

                result = customers.Select(c =>
                {
                    customerAggregatesDict.TryGetValue(c.Id, out var aggregates);
                    return new PartyBalanceResponse
                    {
                        PartyId = c.Id,
                        PartyName = c.Name,
                        PartyType = partyType,
                        Balance = Math.Abs(c.Balance),
                        BalanceType = DetermineBalanceType(c.Balance, partyType),
                        LastPaymentDate = aggregates?.LastPaymentDate,
                        TotalPaid = aggregates?.TotalPaid ?? 0m,
                        TotalInvoiced = aggregates?.TotalInvoiced ?? 0m
                    };
                }).ToList();
            }
            else
            {
                var suppliersQuery =
                    from supplier in _context.Suppliers.AsNoTracking()
                    join balance in (_context.AccountLedgers.AsNoTracking()
                            .Where(entry => entry.PartyType == PartyType.Supplier)
                            .GroupBy(entry => entry.PartyId)
                            .Select(ledgerGroup => new
                            {
                                PartyId = ledgerGroup.Key,
                                Balance = ledgerGroup.Sum(entry => entry.DebitAmount - entry.CreditAmount)
                            }))
                        on supplier.Id equals balance.PartyId
                    where balance.Balance != 0m
                    select new
                    {
                        supplier.Id,
                        supplier.Name,
                        balance.Balance
                    };

                if (!string.IsNullOrWhiteSpace(normalizedSearch))
                {
                    suppliersQuery = suppliersQuery.Where(s => EF.Functions.ILike(s.Name, $"%{normalizedSearch}%"));
                }

                totalCount = await suppliersQuery.CountAsync();
                totalDebit = await suppliersQuery
                    .Where(s => s.Balance > 0m)
                    .Select(s => (decimal?)s.Balance)
                    .SumAsync() ?? 0m;
                totalCredit = Math.Abs(await suppliersQuery
                    .Where(s => s.Balance < 0m)
                    .Select(s => (decimal?)s.Balance)
                    .SumAsync() ?? 0m);

                // Materialize paged supplier IDs first
                var suppliers = await suppliersQuery
                    .OrderBy(s => s.Name)
                    .ThenBy(s => s.Id)
                    .Skip(skip)
                    .Take(pageSize)
                    .ToListAsync();

                var supplierIds = suppliers.Select(s => s.Id).ToList();

                // Precompute aggregates for supplier transactions (filtered to paged suppliers only)
                var supplierTransactionAggregates = await _context.SupplierTransactions
                    .Where(t => supplierIds.Contains(t.SupplierId))
                    .GroupBy(t => t.SupplierId)
                    .Select(g => new
                    {
                        SupplierId = g.Key,
                        LastPaymentDate = g
                            .Where(t => t.TransactionType == TransactionType.PaymentMade)
                            .Max(t => (DateTime?)t.TransactionDate),
                        TotalPaid = g
                            .Where(t => t.TransactionType == TransactionType.PaymentMade)
                            .Sum(t => (decimal?)t.DebitAmount) ?? 0m,
                        TotalInvoiced = g
                            .Where(t => t.TransactionType == TransactionType.Purchase)
                            .Sum(t => (decimal?)t.CreditAmount) ?? 0m
                    })
                    .ToListAsync();

                var supplierTransactionAggregatesDict = supplierTransactionAggregates.ToDictionary(a => a.SupplierId);

                // Precompute aggregates for purchase invoices (filtered to paged suppliers only)
                var purchaseInvoiceAggregates = await _context.PurchaseInvoices
                    .Where(p => supplierIds.Contains(p.SupplierId))
                    .GroupBy(p => p.SupplierId)
                    .Select(g => new
                    {
                        SupplierId = g.Key,
                        NextFutureDueDate = g
                            .Where(p => p.DueDate >= today)
                            .Min(p => (DateTime?)p.DueDate),
                        LatestDueDate = g.Max(p => (DateTime?)p.DueDate)
                    })
                    .ToListAsync();

                var purchaseInvoiceAggregatesDict = purchaseInvoiceAggregates.ToDictionary(a => a.SupplierId);

                result = suppliers.Select(s =>
                {
                    supplierTransactionAggregatesDict.TryGetValue(s.Id, out var transactionAggregates);
                    purchaseInvoiceAggregatesDict.TryGetValue(s.Id, out var invoiceAggregates);
                    return new PartyBalanceResponse
                    {
                        PartyId = s.Id,
                        PartyName = s.Name,
                        PartyType = partyType,
                        Balance = Math.Abs(s.Balance),
                        BalanceType = DetermineBalanceType(s.Balance, partyType),
                        LastPaymentDate = transactionAggregates?.LastPaymentDate,
                        TotalPaid = transactionAggregates?.TotalPaid ?? 0m,
                        TotalInvoiced = transactionAggregates?.TotalInvoiced ?? 0m,
                        DueDate = invoiceAggregates?.NextFutureDueDate ?? invoiceAggregates?.LatestDueDate
                    };
                }).ToList();
            }

            var totalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize);

            Response.Headers["X-Total-Count"] = totalCount.ToString();
            Response.Headers["X-Page"] = page.ToString();
            Response.Headers["X-Page-Size"] = pageSize.ToString();
            Response.Headers["X-Total-Pages"] = totalPages.ToString();
            Response.Headers["X-Total-Debit"] = totalDebit.ToString();
            Response.Headers["X-Total-Credit"] = totalCredit.ToString();

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving outstanding balances");
            return StatusCode(500, new { message = "An error occurred while retrieving outstanding balances" });
        }
    }

    private string DetermineBalanceType(decimal balance, PartyType partyType)
    {
        if (balance == 0) return "Zero";
        return balance > 0 ? "Debit" : "Credit";
    }
}
