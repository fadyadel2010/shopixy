using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Data;
using PointOfSale.API.DTOs;
using PointOfSale.API.Mappers;
using PointOfSale.Core.Exceptions.Accounting;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomersController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const int MaxFinancialIdempotencyKeyLength = 100;
    private static readonly Regex FinancialIdempotencyKeyPattern = new(
        "^[A-Za-z0-9-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions FinancialMutationJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions FinancialMutationResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<CustomersController> _logger;
    private readonly PointOfSale.API.Services.IValidationService _validationService;
    private readonly PointOfSale.API.Services.Audit.IAuditService _auditService;
    private readonly ILedgerService _ledgerService;
    private readonly IFinancialMutationIdempotencyService _financialMutationIdempotency;
    private readonly IAccountingOutboxService _accountingOutbox;
    private readonly IAccountResolver _accountResolver;
    private readonly ICashboxLedgerAccountService _cashboxLedgerAccountService;

    public CustomersController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<CustomersController> logger,
        PointOfSale.API.Services.IValidationService validationService,
        PointOfSale.API.Services.Audit.IAuditService auditService,
        ILedgerService ledgerService,
        IFinancialMutationIdempotencyService financialMutationIdempotency,
        IAccountingOutboxService accountingOutbox,
        IAccountResolver accountResolver,
        ICashboxLedgerAccountService cashboxLedgerAccountService)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
        _validationService = validationService;
        _auditService = auditService;
        _ledgerService = ledgerService;
        _financialMutationIdempotency = financialMutationIdempotency;
        _accountingOutbox = accountingOutbox;
        _accountResolver = accountResolver;
        _cashboxLedgerAccountService = cashboxLedgerAccountService;
    }

    /// <summary>
    /// Get all customers with optional search and pagination
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerResponseDto>>> GetCustomers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        var query = _context.Customers
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(search) ||
                (c.Phone != null && c.Phone.Contains(search)) ||
                (c.Email != null && c.Email.ToLower().Contains(search))
            );
        }

        var totalCount = await query.CountAsync();

        var customers = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(customer => new
            {
                Customer = customer,
                Balance = _context.AccountLedgers
                    .Where(entry =>
                        entry.PartyType == PartyType.Customer &&
                        entry.PartyId == customer.Id)
                    .Sum(entry => (decimal?)(entry.DebitAmount - entry.CreditAmount)) ?? 0m
            })
            .ToListAsync();

        return Ok(new PagedResult<CustomerResponseDto>
        {
            Items = customers
                .Select(item => item.Customer.ToResponseDto(item.Balance))
                .ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>
    /// Get customer by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<CustomerResponseDto>> GetCustomer(Guid id)
    {
        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);

        if (customer == null)
            return NotFound();

        var balance = await _context.AccountLedgers
            .Where(entry =>
                entry.PartyType == PartyType.Customer &&
                entry.PartyId == customer.Id)
            .SumAsync(entry => (decimal?)(entry.DebitAmount - entry.CreditAmount)) ?? 0m;

        return Ok(customer.ToResponseDto(balance));
    }

    [HttpGet("{id}/payments")]
    public async Task<ActionResult<IReadOnlyList<CustomerPaymentResponse>>> GetCustomerPayments(
        Guid id,
        CancellationToken cancellationToken)
    {
        var customerExists = await _context.Customers
            .AsNoTracking()
            .AnyAsync(customer => customer.Id == id && !customer.IsDeleted, cancellationToken);
        if (!customerExists)
        {
            return NotFound(new { message = "Customer not found." });
        }

        var payments = await _context.CustomerPayments
            .AsNoTracking()
            .Where(payment => payment.CustomerId == id)
            .OrderByDescending(payment => payment.PaymentDate)
            .ThenByDescending(payment => payment.CreatedAt)
            .Select(payment => new CustomerPaymentResponse
            {
                Id = payment.Id,
                CustomerId = payment.CustomerId,
                TransactionDate = payment.PaymentDate,
                Amount = payment.Amount,
                PaymentMethod = payment.PaymentMethod,
                CashAmount = payment.CashAmount,
                CardAmount = payment.CardAmount,
                BankTransferAmount = payment.BankTransferAmount,
                ReferenceNumber = payment.ReferenceNumber,
                Notes = payment.Notes,
                BalanceAfter = _context.CustomerTransactions
                    .Where(transaction =>
                        transaction.CustomerId == payment.CustomerId &&
                        transaction.ReferenceId == payment.Id &&
                        transaction.TransactionType == TransactionType.PaymentReceived)
                    .Select(transaction => transaction.BalanceAfter)
                    .FirstOrDefault(),
                IdempotencyKey = payment.IdempotencyKey,
            })
            .ToListAsync(cancellationToken);

        return Ok(payments);
    }

    [HttpPost("{id}/payments")]
    [Authorize(Roles = "Owner,Admin,Manager,Cashier")]
    public async Task<ActionResult<CustomerPaymentResponse>> CreateCustomerPayment(
        Guid id,
        CreateCustomerPaymentRequest request,
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

        var normalization = NormalizeCustomerPayment(request);
        if (normalization.ErrorResult != null)
        {
            return ToCustomerPaymentActionResult(normalization.ErrorResult);
        }

        var normalized = normalization.Payment!;
        var requestHash = BuildCustomerPaymentRequestHash(id, normalized);
        var scopedIdempotencyKey = BuildCustomerMutationIdempotencyKey(operationId);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var claim = await _financialMutationIdempotency.TryClaimAsync(
            operation: "CUSTOMER_PAYMENT_CREATE",
            key: scopedIdempotencyKey,
            requestHash: requestHash,
            cancellationToken: cancellationToken);

        if (claim.ShortCircuitResult != null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ToCustomerPaymentActionResult(claim.ShortCircuitResult);
        }

        var idempotencyRecord = claim.Record!;

        try
        {
            var customer = await _context.Customers
                .FirstOrDefaultAsync(item => item.Id == id && !item.IsDeleted, cancellationToken);
            if (customer == null)
            {
                return await CompleteCustomerPaymentMutationAsync(
                    transaction,
                    idempotencyRecord,
                    NotFound(new { message = "Customer not found." }),
                    cancellationToken);
            }

            await ValidateCustomerReceivingAccountsAsync(normalized, cancellationToken);

            var paymentTimestamp = normalized.PaymentDate ?? DateTime.UtcNow;
            var referenceNumber = string.IsNullOrWhiteSpace(normalized.ReferenceNumber)
                ? $"CPAY-{DateTime.UtcNow:yyyyMMddHHmmss}"
                : normalized.ReferenceNumber;
            var payment = new CustomerPayment
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,
                CustomerId = customer.Id,
                PaymentDate = paymentTimestamp,
                Amount = normalized.Amount,
                PaymentMethod = normalized.PaymentMethod,
                CashAmount = normalized.CashAmount,
                CardAmount = normalized.CardAmount,
                BankTransferAmount = normalized.BankTransferAmount,
                IdempotencyKey = operationId,
                ReferenceNumber = referenceNumber,
                Notes = normalized.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            _context.CustomerPayments.Add(payment);

            var balanceAfter = await _ledgerService.ApplyCustomerTransaction(
                customer.Id,
                paymentTimestamp,
                TransactionType.PaymentReceived,
                referenceNumber,
                debitAmount: 0m,
                creditAmount: payment.Amount,
                referenceId: payment.Id,
                notes: payment.Notes ?? $"Customer payment {referenceNumber}");

            _context.CustomerTransactions.Add(new CustomerTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = payment.TenantId,
                CustomerId = customer.Id,
                TransactionDate = paymentTimestamp,
                TransactionType = TransactionType.PaymentReceived,
                DebitAmount = 0m,
                CreditAmount = payment.Amount,
                BalanceAfter = balanceAfter,
                ReferenceId = payment.Id,
                ReferenceNumber = referenceNumber,
                Notes = payment.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            balanceAfter = await _ledgerService.RebuildPartyTransactionBalancesAsync(
                PartyType.Customer,
                customer.Id,
                cancellationToken);

            await _accountingOutbox.EnqueueCustomerPaymentAsync(payment.TenantId, payment.Id, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            var response = ToCustomerPaymentResponse(payment, balanceAfter);
            await _auditService.LogCreate(
                "CustomerPayment",
                payment.Id.ToString(),
                $"Recorded customer payment {referenceNumber} for customer {customer.Name}",
                response,
                referenceNumber);

            return await CompleteCustomerPaymentMutationAsync(
                transaction,
                idempotencyRecord,
                CreatedAtAction(nameof(GetCustomerPayments), new { id = customer.Id }, response),
                cancellationToken);
        }
        catch (NoCashboxAccountException ex)
        {
            _logger.LogWarning(ex, "No active cashbox configured for customer payment in tenant {TenantId}", ex.TenantId);
            return await CompleteCustomerPaymentMutationAsync(
                transaction,
                idempotencyRecord,
                BadRequest(new
                {
                    message = "No active cashbox configured for tenant. Please configure a cashbox before receiving cash payments.",
                    code = "NO_CASHBOX_CONFIGURED"
                }),
                cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error creating customer payment for customer {CustomerId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "An error occurred while creating the customer payment.",
            });
        }
    }

    /// <summary>
    /// Create a new customer
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Manager,Cashier")]
    public async Task<ActionResult<CustomerResponseDto>> CreateCustomer(CreateCustomerRequest request)
    {
        // Check if customer with same phone already exists
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var existingCustomer = await _context.Customers
                .FirstOrDefaultAsync(c => c.Phone == request.Phone && !c.IsDeleted);

            if (existingCustomer != null)
                return BadRequest(new { message = "A customer with this phone number already exists" });
        }

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant: Set tenant ID
            Name = request.Name,
            Phone = request.Phone,
            Email = request.Email,
            Address = request.Address,
            TaxNumber = request.TaxNumber,
            Balance = 0, // Start with zero balance
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Customer created: {CustomerId} - {CustomerName}", customer.Id, customer.Name);
        await _auditService.LogCreate("Customer", customer.Id.ToString(), $"Created customer {customer.Name}", customer, customer.Id.ToString());

        return CreatedAtAction(nameof(GetCustomer), new { id = customer.Id }, customer.ToResponseDto(0m));
    }

    /// <summary>
    /// Update an existing customer
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> UpdateCustomer(Guid id, UpdateCustomerRequest request)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(entity => entity.Id == id);
        if (customer == null || customer.IsDeleted)
            return NotFound();

        // Check if phone is being changed and if it's already in use
        if (!string.IsNullOrWhiteSpace(request.Phone) && request.Phone != customer.Phone)
        {
            var existingCustomer = await _context.Customers
                .FirstOrDefaultAsync(c => c.Phone == request.Phone && c.Id != id && !c.IsDeleted);

            if (existingCustomer != null)
                return BadRequest(new { message = "A customer with this phone number already exists" });
        }

        
        var beforeState = new { customer.Name, customer.Phone, customer.Email, customer.Address, customer.TaxNumber };

        customer.Name = request.Name;
        customer.Phone = request.Phone;
        customer.Email = request.Email;
        customer.Address = request.Address ?? customer.Address;
        customer.TaxNumber = request.TaxNumber ?? customer.TaxNumber;
        customer.Notes = request.Notes ?? customer.Notes;
        customer.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await _auditService.LogUpdate("Customer", customer.Id.ToString(), $"Updated customer {customer.Name}", beforeState, customer, customer.Id.ToString());

        return NoContent();
    }

    /// <summary>
    /// Get all customers with their opening balance status
    /// </summary>
    [HttpGet("opening-balances")]
    public async Task<ActionResult<IEnumerable<CustomerOpeningBalanceResponse>>> GetCustomerOpeningBalances()
    {
        try
        {
            var customers = await _context.Customers
                .Where(c =>
                    c.TenantId == _tenantContext.RequiredTenantId &&
                    !c.IsDeleted)
                .OrderBy(c => c.Name)
                .Select(c => new CustomerOpeningBalanceResponse
                {
                    CustomerId = c.Id,
                    CustomerName = c.Name,
                    Phone = c.Phone,
                    OpeningBalanceAmount = c.OpeningBalanceAmount,
                    BalanceType = c.BalanceType,
                    OpeningBalanceDate = c.OpeningBalanceDate,
                    HasOpeningBalance = c.OpeningBalanceAmount.HasValue
                })
                .ToListAsync();

            return Ok(customers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer opening balances");
            return StatusCode(500, new { message = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// Save opening balances for multiple customers
    /// </summary>
    [HttpPost("opening-balances")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> SaveCustomerOpeningBalances(
        SaveCustomerOpeningBalancesRequest request,
        CancellationToken cancellationToken)
    {
        var normalization = NormalizeCustomerOpeningBalanceRequest(request);
        if (normalization.ErrorResult != null)
        {
            return normalization.ErrorResult;
        }

        var normalizedItems = normalization.Items!;
        var requestHash = BuildCustomerOpeningBalanceRequestHash(normalizedItems);
        var operationIdResolution = ResolveOptionalCustomerMutationOperationId(requestHash);
        if (operationIdResolution.ErrorResult != null)
        {
            return operationIdResolution.ErrorResult;
        }

        var scopedIdempotencyKey = BuildCustomerMutationIdempotencyKey(operationIdResolution.OperationId!);
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var claim = await _financialMutationIdempotency.TryClaimAsync(
            operation: "CUSTOMER_OPENING_BALANCE_BATCH",
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
            var customerIds = normalizedItems
                .Select(item => item.CustomerId)
                .Distinct()
                .ToList();
            var customers = await _context.Customers
                .Where(customer =>
                    customer.TenantId == _tenantContext.RequiredTenantId &&
                    customerIds.Contains(customer.Id) &&
                    !customer.IsDeleted)
                .ToDictionaryAsync(customer => customer.Id, cancellationToken);
            var customersWithExistingOpeningBalance = await _context.AccountLedgers
                .Where(entry =>
                    entry.TenantId == _tenantContext.RequiredTenantId &&
                    entry.PartyType == PartyType.Customer &&
                    entry.TransactionType == TransactionType.OpeningBalance &&
                    customerIds.Contains(entry.PartyId))
                .Select(entry => entry.PartyId)
                .ToHashSetAsync(cancellationToken);
            var processedCustomers = new HashSet<Guid>();

            foreach (var item in normalizedItems)
            {
                if (!processedCustomers.Add(item.CustomerId))
                {
                    results.Add(new
                    {
                        customerId = item.CustomerId,
                        success = true,
                        skipped = true,
                        message = "Duplicate customer entry ignored after the first occurrence."
                    });
                    continue;
                }

                if (!customers.TryGetValue(item.CustomerId, out var customer))
                {
                    results.Add(new { customerId = item.CustomerId, success = false, message = "Customer not found" });
                    continue;
                }

                if (customer.OpeningBalanceAmount.HasValue || customersWithExistingOpeningBalance.Contains(item.CustomerId))
                {
                    results.Add(new { customerId = item.CustomerId, success = false, message = "Opening balance already exists for this customer" });
                    continue;
                }

                customer.OpeningBalanceAmount = item.Amount;
                customer.BalanceType = item.BalanceType;
                customer.OpeningBalanceDate = item.EffectiveDateUtc;

                var debitAmount = item.BalanceType == "Debit" ? item.Amount : 0m;
                var creditAmount = item.BalanceType == "Credit" ? item.Amount : 0m;
                var referenceNumber = BuildCustomerOpeningBalanceReferenceNumber(customer.Id);

                var balanceAfter = await _ledgerService.ApplyCustomerTransaction(
                    customer.Id,
                    item.EffectiveDateUtc,
                    TransactionType.OpeningBalance,
                    referenceNumber,
                    debitAmount,
                    creditAmount,
                    referenceId: null,
                    notes: item.Notes ?? "Opening Balance");

                _context.CustomerTransactions.Add(new CustomerTransaction
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.RequiredTenantId,
                    CustomerId = customer.Id,
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
                        entry.TenantId == customer.TenantId &&
                        entry.PartyType == PartyType.Customer &&
                        entry.PartyId == customer.Id &&
                        entry.TransactionType == TransactionType.OpeningBalance &&
                        entry.DocumentNumber == referenceNumber)
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

                customersWithExistingOpeningBalance.Add(customer.Id);
                results.Add(new
                {
                    customerId = item.CustomerId,
                    success = true,
                    skipped = false,
                    message = "Opening balance saved successfully",
                    balanceAfter
                });

                _logger.LogInformation("Opening balance set for customer: {CustomerId} - {CustomerName}, Amount: {Amount}, Type: {Type}",
                    customer.Id, customer.Name, item.Amount, item.BalanceType);
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
                "Duplicate customer opening balance rejected | CustomerId: {CustomerId}",
                ex.PartyId);

            _context.ChangeTracker.Clear();
            _context.Attach(idempotencyRecord);

            return await CompleteFinancialMutationResultAsync(
                transaction,
                idempotencyRecord,
                Conflict(new { message = "Opening balance already exists for this customer." }),
                cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error saving customer opening balances");
            return StatusCode(500, new { message = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// Delete a customer (soft delete)
    /// Customers cannot be deleted if they have a balance or transaction history.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> DeleteCustomer(Guid id)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(entity => entity.Id == id);
        if (customer == null || customer.IsDeleted)
        {
            return NotFound(new { 
                errorCode = "CUSTOMER_NOT_FOUND",
                messageEn = "Customer not found.",
                messageAr = "العميل غير موجود."
            });
        }

        // Validate if customer can be deleted
        var validationResult = await _validationService.CanDeleteCustomerAsync(id);
        if (!validationResult.IsValid)
        {
            return BadRequest(new {
                errorCode = validationResult.ErrorCode,
                messageEn = validationResult.MessageEn,
                messageAr = validationResult.MessageAr,
                data = validationResult.Data
            });
        }

        customer.IsDeleted = true;
        customer.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Customer deleted: {CustomerId} - {CustomerName}", customer.Id, customer.Name);
        await _auditService.LogDelete("Customer", customer.Id.ToString(), $"Deleted customer {customer.Name}", null, customer.Id.ToString());

        return NoContent();
    }

    /// <summary>
    /// Check if a customer can be deleted - use this before showing delete button
    /// </summary>
    [HttpGet("{id}/can-delete")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> CanDeleteCustomer(Guid id)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(entity => entity.Id == id);
        if (customer == null || customer.IsDeleted)
        {
            return NotFound();
        }

        var validationResult = await _validationService.CanDeleteCustomerAsync(id);
        return Ok(new {
            canDelete = validationResult.IsValid,
            errorCode = validationResult.ErrorCode,
            messageEn = validationResult.MessageEn,
            messageAr = validationResult.MessageAr
        });
    }

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

    private async Task<ActionResult<CustomerPaymentResponse>> CompleteCustomerPaymentMutationAsync(
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

        return ToCustomerPaymentActionResult(completedResult);
    }

    private static ActionResult<CustomerPaymentResponse> ToCustomerPaymentActionResult(IActionResult result) =>
        result as ActionResult ?? new StatusCodeResult(StatusCodes.Status500InternalServerError);

    private string? ResolveFinancialIdempotencyKey() =>
        ValidateFinancialIdempotencyKey(Request.Headers[IdempotencyKeyHeader].FirstOrDefault());

    private async Task ValidateCustomerReceivingAccountsAsync(
        NormalizedCustomerPayment payment,
        CancellationToken cancellationToken)
    {
        var accountCodes = new HashSet<string>(StringComparer.Ordinal);
        if (payment.CashAmount > 0m)
        {
            var cashbox = await _cashboxLedgerAccountService.ResolveDefaultCashboxAccountAsync(
                _tenantContext.RequiredTenantId,
                cancellationToken);
            accountCodes.Add(cashbox.AccountCode);
        }
        if (payment.CardAmount > 0m)
        {
            accountCodes.Add(AccountCodes.VisaClearing);
        }
        if (payment.BankTransferAmount > 0m)
        {
            accountCodes.Add(AccountCodes.BankAccount);
        }

        foreach (var accountCode in accountCodes)
        {
            var account = await _accountResolver.GetByCodeAsync(
                _tenantContext.RequiredTenantId,
                accountCode,
                cancellationToken);
            if (!account.IsActive)
            {
                throw new InvalidOperationException($"Receiving account '{accountCode}' is inactive.");
            }
        }
    }

    private static (NormalizedCustomerPayment? Payment, IActionResult? ErrorResult) NormalizeCustomerPayment(
        CreateCustomerPaymentRequest request)
    {
        var amount = RoundCurrency(request.Amount);
        if (amount <= 0m)
        {
            return (null, new BadRequestObjectResult(new { message = "Payment amount must be greater than zero." }));
        }

        if (request.ReferenceNumber?.Trim().Length > 100)
        {
            return (null, new BadRequestObjectResult(new { message = "Reference number cannot exceed 100 characters." }));
        }
        if (request.Notes?.Trim().Length > 1000)
        {
            return (null, new BadRequestObjectResult(new { message = "Notes cannot exceed 1000 characters." }));
        }

        var paymentDate = request.PaymentDate.HasValue
            ? NormalizeUtc(request.PaymentDate.Value)
            : (DateTime?)null;
        if (paymentDate > DateTime.UtcNow.AddMinutes(5))
        {
            return (null, new BadRequestObjectResult(new { message = "Payment date cannot be in the future." }));
        }

        decimal cashAmount;
        decimal cardAmount;
        decimal bankTransferAmount;
        switch (request.PaymentMethod)
        {
            case PaymentMethod.Cash:
                cashAmount = amount;
                cardAmount = 0m;
                bankTransferAmount = 0m;
                break;
            case PaymentMethod.Card:
                cashAmount = 0m;
                cardAmount = amount;
                bankTransferAmount = 0m;
                break;
            case PaymentMethod.BankTransfer:
                cashAmount = 0m;
                cardAmount = 0m;
                bankTransferAmount = amount;
                break;
            case PaymentMethod.Mixed:
                cashAmount = RoundCurrency(request.CashAmount ?? 0m);
                if (cashAmount <= 0m || cashAmount >= amount)
                {
                    return (null, new BadRequestObjectResult(new
                    {
                        message = "Mixed payments require a positive cash amount smaller than the total amount."
                    }));
                }
                cardAmount = amount - cashAmount;
                bankTransferAmount = 0m;
                break;
            default:
                return (null, new BadRequestObjectResult(new
                {
                    message = "Customer payments must use cash, card, bank transfer, or a cash/card mix."
                }));
        }

        return (new NormalizedCustomerPayment(
            amount,
            request.PaymentMethod,
            cashAmount,
            cardAmount,
            bankTransferAmount,
            paymentDate,
            NormalizeNotes(request.ReferenceNumber),
            NormalizeNotes(request.Notes)), null);
    }

    private static string BuildCustomerPaymentRequestHash(Guid customerId, NormalizedCustomerPayment payment)
    {
        var payload = JsonSerializer.Serialize(new
        {
            customerId,
            payment.Amount,
            payment.PaymentMethod,
            payment.CashAmount,
            payment.CardAmount,
            payment.BankTransferAmount,
            payment.PaymentDate,
            payment.ReferenceNumber,
            payment.Notes,
        }, FinancialMutationJsonOptions);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private static CustomerPaymentResponse ToCustomerPaymentResponse(CustomerPayment payment, decimal balanceAfter) =>
        new()
        {
            Id = payment.Id,
            CustomerId = payment.CustomerId,
            TransactionDate = payment.PaymentDate,
            Amount = payment.Amount,
            PaymentMethod = payment.PaymentMethod,
            CashAmount = payment.CashAmount,
            CardAmount = payment.CardAmount,
            BankTransferAmount = payment.BankTransferAmount,
            ReferenceNumber = payment.ReferenceNumber,
            Notes = payment.Notes,
            BalanceAfter = balanceAfter,
            IdempotencyKey = payment.IdempotencyKey,
        };

    private (IReadOnlyList<NormalizedCustomerOpeningBalanceItem>? Items, IActionResult? ErrorResult) NormalizeCustomerOpeningBalanceRequest(
        SaveCustomerOpeningBalancesRequest request)
    {
        if (request.CustomerBalances == null || request.CustomerBalances.Count == 0)
        {
            return (null, BadRequest(new { message = "At least one customer opening balance is required." }));
        }

        var normalizedItems = new List<NormalizedCustomerOpeningBalanceItem>();
        var firstOccurrenceByCustomer = new Dictionary<Guid, NormalizedCustomerOpeningBalanceItem>();

        foreach (var item in request.CustomerBalances)
        {
            if (item.CustomerId == Guid.Empty)
            {
                return (null, BadRequest(new { message = "CustomerId is required for every opening balance item." }));
            }

            var roundedAmount = RoundCurrency(item.Amount);
            if (roundedAmount <= 0m)
            {
                return (null, BadRequest(new
                {
                    message = $"Amount for customer {item.CustomerId} must be greater than zero. Provided: {item.Amount}, Rounded: {roundedAmount}"
                }));
            }

            var normalizedBalanceType = NormalizeBalanceType(item.BalanceType);
            if (normalizedBalanceType == null)
            {
                return (null, BadRequest(new
                {
                    message = $"BalanceType for customer {item.CustomerId} must be 'Debit' or 'Credit'."
                }));
            }

            var normalizedItem = new NormalizedCustomerOpeningBalanceItem(
                item.CustomerId,
                roundedAmount,
                normalizedBalanceType,
                NormalizeUtc(item.Date),
                NormalizeNotes(item.Notes));

            if (firstOccurrenceByCustomer.TryGetValue(item.CustomerId, out var firstOccurrence) &&
                firstOccurrence != normalizedItem)
            {
                return (null, Conflict(new
                {
                    message = $"Customer {item.CustomerId} has conflicting opening balance entries in the same request."
                }));
            }

            firstOccurrenceByCustomer[item.CustomerId] = normalizedItem;
            normalizedItems.Add(normalizedItem);
        }

        return (normalizedItems, null);
    }

    private (string? OperationId, IActionResult? ErrorResult) ResolveOptionalCustomerMutationOperationId(string fallbackOperationId)
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

    private string BuildCustomerMutationIdempotencyKey(string operationId) =>
        $"CUSTOMER:{_tenantContext.RequiredTenantId}:{operationId}";

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

    private static string BuildCustomerOpeningBalanceRequestHash(
        IEnumerable<NormalizedCustomerOpeningBalanceItem> items)
    {
        var payload = JsonSerializer.Serialize(
            items
                .GroupBy(item => item.CustomerId)
                .Select(group => group.First())
                .OrderBy(item => item.CustomerId)
                .Select(item => new
                {
                    item.CustomerId,
                    item.Amount,
                    item.BalanceType,
                    item.EffectiveDateUtc,
                    item.Notes
                }),
            FinancialMutationJsonOptions);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private static string BuildCustomerOpeningBalanceReferenceNumber(Guid customerId) =>
        $"OB-CUS-{customerId.ToString()[..8]}";

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

    private static string? SerializeFinancialPayload(object? payload) =>
        payload == null
            ? null
            : JsonSerializer.Serialize(payload, FinancialMutationResponseJsonOptions);

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private sealed record NormalizedCustomerOpeningBalanceItem(
        Guid CustomerId,
        decimal Amount,
        string BalanceType,
        DateTime EffectiveDateUtc,
        string? Notes);

    private sealed record NormalizedCustomerPayment(
        decimal Amount,
        PaymentMethod PaymentMethod,
        decimal CashAmount,
        decimal CardAmount,
        decimal BankTransferAmount,
        DateTime? PaymentDate,
        string? ReferenceNumber,
        string? Notes);
}

// DTOs
public record CreateCustomerRequest(
    string Name,
    string Phone,
    string? Email,
    string? Address,
    string? TaxNumber,
    string? Notes
);

public record UpdateCustomerRequest(
    string Name,
    string Phone,
    string? Email,
    string? Address,
    string? TaxNumber,
    string? Notes
);

public record CreateCustomerPaymentRequest
{
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal? CashAmount { get; set; }
    public DateTime? PaymentDate { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

public record CustomerPaymentResponse
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public DateTime TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal BankTransferAmount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public decimal BalanceAfter { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

// Opening Balance DTOs
public record CustomerOpeningBalanceResponse
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public decimal? OpeningBalanceAmount { get; set; }
    public string? BalanceType { get; set; }
    public DateTime? OpeningBalanceDate { get; set; }
    public bool HasOpeningBalance { get; set; }
}

public record SaveCustomerOpeningBalancesRequest
{
    public List<CustomerOpeningBalanceItem> CustomerBalances { get; set; } = new();
}

public record CustomerOpeningBalanceItem
{
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string BalanceType { get; set; } = "Debit"; // "Debit" or "Credit"
    public DateTime Date { get; set; }
    public string? Notes { get; set; }
}
