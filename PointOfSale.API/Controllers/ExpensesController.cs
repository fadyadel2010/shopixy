using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Pagination;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.Infrastructure.Data;
using PointOfSale.API.DTOs;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExpensesController : ControllerBase
{
    private const string AccountingGovernanceRoles = "Owner,Admin,Manager";
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string RequestIdHeader = "X-Request-Id";
    private const int MaxExpenseIdempotencyKeyLength = 100;
    private const int MaxExpensePageSize = 100;
    private const int MaxExpenseReportItems = 1000;
    private static readonly Regex ExpenseIdempotencyKeyPattern = new(
        "^[A-Za-z0-9-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions ExpenseIdempotencyJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ExpensesController> _logger;
    private readonly IAccountingOutboxService _accountingOutbox;

    public ExpensesController(
        ApplicationDbContext context, 
        ITenantContext tenantContext, 
        ILogger<ExpensesController> logger,
        IAccountingOutboxService accountingOutbox)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
        _accountingOutbox = accountingOutbox;
    }

    private bool IsPostgresProvider() => _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    private bool IsSqlServerProvider() => _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;


    [HttpGet]
    public async Task<ActionResult<PagedResult<ExpenseDto>>> GetExpenses(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] string? paymentMethod = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] bool? isApproved = null,
        CancellationToken cancellationToken = default)
    {
        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize, MaxExpensePageSize);
        var skip = PaginationGuard.GetSkip(page, pageSize);

        var query = _context.Expenses
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.Supplier)
            .Include(e => e.Cashbox)
            .Include(e => e.Bank)
            .Where(e => e.TenantId == _tenantContext.RequiredTenantId)
            .AsQueryable();

        if (categoryId.HasValue)
            query = query.Where(e => e.ExpenseCategoryId == categoryId.Value);

        if (!string.IsNullOrEmpty(paymentMethod))
            query = query.Where(e => e.PaymentMethod == paymentMethod);

        if (fromDate.HasValue)
            query = query.Where(e => e.ExpenseDate >= DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc));

        if (toDate.HasValue)
            query = query.Where(e => e.ExpenseDate <= DateTime.SpecifyKind(toDate.Value, DateTimeKind.Utc));

        if (isApproved.HasValue)
            query = query.Where(e => e.IsApproved == isApproved.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var expenses = await query
            .OrderByDescending(e => e.ExpenseDate)
            .Skip(skip)
            .Take(pageSize)
            .Select(e => new ExpenseDto(
                e.Id,
                e.ExpenseCategoryId,
                e.Category!.Name,
                e.ExpenseDate,
                e.Amount,
                e.Description,
                e.Notes,
                e.PaymentMethod,
                e.SupplierId,
                e.Supplier != null ? e.Supplier.Name : null,
                e.CashboxId,
                e.Cashbox != null ? e.Cashbox.Name : null,
                e.BankId,
                e.Bank != null ? e.Bank.BankName : null,
                e.ReceiptPath,
                e.ReferenceNumber,
                e.IsRecurring,
                e.RecurringFrequency,
                e.RequiresApproval,
                e.IsApproved,
                e.ApprovedBy,
                e.ApprovedAt
            ))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<ExpenseDto>
        {
            Items = expenses,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ExpenseDto>> GetExpense(Guid id, CancellationToken cancellationToken)
    {
        var expense = await _context.Expenses
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.Supplier)
            .Include(e => e.Cashbox)
            .Include(e => e.Bank)
            .Where(e => e.Id == id && e.TenantId == _tenantContext.RequiredTenantId)
            .Select(e => new ExpenseDto(
                e.Id,
                e.ExpenseCategoryId,
                e.Category!.Name,
                e.ExpenseDate,
                e.Amount,
                e.Description,
                e.Notes,
                e.PaymentMethod,
                e.SupplierId,
                e.Supplier != null ? e.Supplier.Name : null,
                e.CashboxId,
                e.Cashbox != null ? e.Cashbox.Name : null,
                e.BankId,
                e.Bank != null ? e.Bank.BankName : null,
                e.ReceiptPath,
                e.ReferenceNumber,
                e.IsRecurring,
                e.RecurringFrequency,
                e.RequiresApproval,
                e.IsApproved,
                e.ApprovedBy,
                e.ApprovedAt
            ))
            .FirstOrDefaultAsync(cancellationToken);

        if (expense == null)
            return NotFound();

        return Ok(expense);
    }

    [HttpPost]
    public async Task<ActionResult<ExpenseDto>> CreateExpense(CreateExpenseDto request, CancellationToken cancellationToken)
    {
        if (!ValidateExpenseMutationRequest(request, out var validationError))
        {
            return BadRequest(new { message = validationError });
        }

        var idempotencyKey = ResolveExpenseIdempotencyKey();
        if (idempotencyKey.ErrorResult != null)
        {
            return ToExpenseActionResult(idempotencyKey.ErrorResult);
        }

        var referenceValidation = await ValidateExpenseReferencesAsync(request, cancellationToken);
        if (referenceValidation != null)
        {
            return ToExpenseActionResult(referenceValidation);
        }

        var requestHash = BuildExpenseRequestHash("EXPENSE_CREATE", request);
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        SalesRequestIdempotency? idempotencyRecord = null;

        try
        {
            var claim = await TryClaimExpenseIdempotencyAsync("EXPENSE_CREATE", idempotencyKey.Key!, requestHash, cancellationToken);
            if (claim.ShortCircuitResult != null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return ToExpenseActionResult(claim.ShortCircuitResult);
            }

            idempotencyRecord = claim.Record!;
            var approvalActorId = ResolveCurrentUserId();
            var mutationVersion = request.RequiresApproval ? 0 : 1;
            var expense = new Expense
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                ExpenseCategoryId = request.ExpenseCategoryId,
                ExpenseDate = DateTime.SpecifyKind(request.ExpenseDate, DateTimeKind.Utc),
                Amount = RoundCurrency(request.Amount),
                Description = request.Description,
                Notes = request.Notes,
                PaymentMethod = request.PaymentMethod,
                SupplierId = request.SupplierId,
                CashboxId = request.CashboxId,
                BankId = request.BankId,
                ReferenceNumber = request.ReferenceNumber,
                IsRecurring = request.IsRecurring,
                RecurringFrequency = request.RecurringFrequency,
                RequiresApproval = request.RequiresApproval,
                IsApproved = !request.RequiresApproval, // Auto-approve if not required
                ApprovedBy = !request.RequiresApproval ? approvalActorId : null,
                ApprovedAt = !request.RequiresApproval ? DateTime.UtcNow : null,
                AccountingMutationVersion = mutationVersion,
                CreatedAt = DateTime.UtcNow
            };

            _context.Expenses.Add(expense);

            if (ShouldAccountExpense(expense))
            {
                await _accountingOutbox.EnqueuePosExpenseAsync(
                    expense.TenantId,
                    expense.Id,
                    BuildExpenseOperationKey(expense.AccountingMutationVersion),
                    expense.AccountingMutationVersion);
            }

            // DO NOT mutate balances directly - expense posting must stay on the outbox/ledger path.

            await _context.SaveChangesAsync(cancellationToken);

            var createdExpense = await LoadExpenseDtoAsync(expense.Id, cancellationToken);
            var result = CreatedAtAction(nameof(GetExpense), new { id = expense.Id }, createdExpense);

            _logger.LogInformation("Expense created: {ExpenseId} - {Description} - Amount: {Amount}", 
                expense.Id, expense.Description, expense.Amount);

            return ToExpenseActionResult(await CompleteExpenseIdempotencyAsync(transaction, idempotencyRecord, result, cancellationToken));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "Error creating expense");
            return StatusCode(500, new { message = "An unexpected error occurred" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateExpense(Guid id, UpdateExpenseDto request, CancellationToken cancellationToken)
    {
        if (!ValidateExpenseMutationRequest(request, out var validationError))
        {
            return BadRequest(new { message = validationError });
        }

        var idempotencyKey = ResolveExpenseIdempotencyKey();
        if (idempotencyKey.ErrorResult != null)
        {
            return idempotencyKey.ErrorResult;
        }

        var referenceValidation = await ValidateExpenseReferencesAsync(request, cancellationToken);
        if (referenceValidation != null)
        {
            return referenceValidation;
        }

        var requestHash = BuildExpenseRequestHash("EXPENSE_UPDATE", new { ExpenseId = id, Request = request });
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        SalesRequestIdempotency? idempotencyRecord = null;

        try
        {
            var claim = await TryClaimExpenseIdempotencyAsync("EXPENSE_UPDATE", idempotencyKey.Key!, requestHash, cancellationToken);
            if (claim.ShortCircuitResult != null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return claim.ShortCircuitResult;
            }

            idempotencyRecord = claim.Record!;
            // SECURITY: IgnoreQueryFilters is intentional here so updates can resolve
            // historical/soft-deleted rows, but the tenant boundary is reapplied immediately.
            var expense = await _context.Expenses
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == _tenantContext.RequiredTenantId, cancellationToken);

            if (expense == null || expense.IsDeleted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return NotFound();
            }

            expense.ExpenseCategoryId = request.ExpenseCategoryId;
            expense.ExpenseDate = DateTime.SpecifyKind(request.ExpenseDate, DateTimeKind.Utc);
            expense.Amount = RoundCurrency(request.Amount);
            expense.Description = request.Description;
            expense.Notes = request.Notes;
            expense.PaymentMethod = request.PaymentMethod;
            expense.SupplierId = request.SupplierId;
            expense.CashboxId = request.CashboxId;
            expense.BankId = request.BankId;
            expense.ReferenceNumber = request.ReferenceNumber;
            expense.IsRecurring = request.IsRecurring;
            expense.RecurringFrequency = request.RecurringFrequency;
            expense.UpdatedAt = DateTime.UtcNow;

            if (await ShouldEnqueueExpenseMutationAsync(expense, cancellationToken))
            {
                expense.AccountingMutationVersion++;
                await _accountingOutbox.EnqueueExpenseReversalAsync(
                    expense.TenantId,
                    expense.Id,
                    BuildExpenseOperationKey(expense.AccountingMutationVersion),
                    expense.AccountingMutationVersion);
            }

            await _context.SaveChangesAsync(cancellationToken);
            var result = NoContent();
            return await CompleteExpenseIdempotencyAsync(transaction, idempotencyRecord, result, cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "Error updating expense {ExpenseId}", id);
            return StatusCode(500, new { message = "An unexpected error occurred" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteExpense(Guid id, CancellationToken cancellationToken)
    {
        var idempotencyKey = ResolveExpenseIdempotencyKey();
        if (idempotencyKey.ErrorResult != null)
        {
            return idempotencyKey.ErrorResult;
        }

        var requestHash = BuildExpenseRequestHash("EXPENSE_DELETE", new { ExpenseId = id });
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        SalesRequestIdempotency? idempotencyRecord = null;

        try
        {
            var claim = await TryClaimExpenseIdempotencyAsync("EXPENSE_DELETE", idempotencyKey.Key!, requestHash, cancellationToken);
            if (claim.ShortCircuitResult != null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return claim.ShortCircuitResult;
            }

            idempotencyRecord = claim.Record!;
            // SECURITY: IgnoreQueryFilters is intentional here so deletes can target
            // previously soft-deleted rows, but the tenant predicate is reapplied below.
            var expense = await _context.Expenses
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == _tenantContext.RequiredTenantId, cancellationToken);

            if (expense == null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return NotFound();
            }

            if (!expense.IsDeleted)
            {
                expense.IsDeleted = true;
                expense.UpdatedAt = DateTime.UtcNow;

                if (await ShouldEnqueueExpenseMutationAsync(expense, cancellationToken, includeCurrentApprovalState: true))
                {
                    expense.AccountingMutationVersion++;
                    await _accountingOutbox.EnqueueExpenseReversalAsync(
                        expense.TenantId,
                        expense.Id,
                        BuildExpenseOperationKey(expense.AccountingMutationVersion),
                        expense.AccountingMutationVersion);
                }
            }

            _logger.LogInformation("Expense deleted: {ExpenseId} - {Description}", expense.Id, expense.Description);

            await _context.SaveChangesAsync(cancellationToken);
            var result = NoContent();
            return await CompleteExpenseIdempotencyAsync(transaction, idempotencyRecord, result, cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "Error deleting expense {ExpenseId}", id);
            return StatusCode(500, new { message = "An unexpected error occurred" });
        }
    }

    [HttpPost("{id}/approve")]
    [Authorize(Roles = AccountingGovernanceRoles)]
    public async Task<IActionResult> ApproveExpense(Guid id, [FromBody] ApproveExpenseDto request, CancellationToken cancellationToken)
    {
        var idempotencyKey = ResolveExpenseIdempotencyKey();
        if (idempotencyKey.ErrorResult != null)
        {
            return idempotencyKey.ErrorResult;
        }

        var requestHash = BuildExpenseRequestHash("EXPENSE_APPROVE", new { ExpenseId = id, Request = request });
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        SalesRequestIdempotency? idempotencyRecord = null;

        try
        {
            var claim = await TryClaimExpenseIdempotencyAsync("EXPENSE_APPROVE", idempotencyKey.Key!, requestHash, cancellationToken);
            if (claim.ShortCircuitResult != null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return claim.ShortCircuitResult;
            }

            idempotencyRecord = claim.Record!;
            // SECURITY: IgnoreQueryFilters is intentional here so approval changes can
            // inspect historical rows, but the tenant predicate is still mandatory.
            var expense = await _context.Expenses
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == _tenantContext.RequiredTenantId, cancellationToken);

            if (expense == null || expense.IsDeleted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return NotFound();
            }

            if (!expense.RequiresApproval)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return BadRequest(new { message = "This expense does not require approval" });
            }

            var previousApprovalState = expense.IsApproved;
            var actorId = ResolveCurrentUserId();

            expense.IsApproved = request.Approved;
            expense.ApprovedBy = request.Approved ? actorId : null;
            expense.ApprovedAt = request.Approved ? DateTime.UtcNow : null;
            expense.UpdatedAt = DateTime.UtcNow;

            if (request.Approved != previousApprovalState)
            {
                expense.AccountingMutationVersion++;
                var operationKey = BuildExpenseOperationKey(expense.AccountingMutationVersion);

                if (request.Approved)
                {
                    await _accountingOutbox.EnqueuePosExpenseAsync(
                        expense.TenantId,
                        expense.Id,
                        operationKey,
                        expense.AccountingMutationVersion);
                }
                else
                {
                    await _accountingOutbox.EnqueueExpenseReversalAsync(
                        expense.TenantId,
                        expense.Id,
                        operationKey,
                        expense.AccountingMutationVersion);
                }
            }

            _logger.LogInformation("Expense {Action}: {ExpenseId} by {UserId}", 
                request.Approved ? "approved" : "rejected", expense.Id, actorId);

            await _context.SaveChangesAsync(cancellationToken);
            var result = Ok(new { message = request.Approved ? "Expense approved" : "Expense rejected" });
            return await CompleteExpenseIdempotencyAsync(transaction, idempotencyRecord, result, cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "Error approving expense {ExpenseId}", id);
            return StatusCode(500, new { message = "An unexpected error occurred" });
        }
    }

    private async Task<ExpenseDto> LoadExpenseDtoAsync(Guid expenseId, CancellationToken cancellationToken)
    {
        return await _context.Expenses
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.Supplier)
            .Include(e => e.Cashbox)
            .Include(e => e.Bank)
            .Where(e => e.Id == expenseId && e.TenantId == _tenantContext.RequiredTenantId)
            .Select(e => new ExpenseDto(
                e.Id,
                e.ExpenseCategoryId,
                e.Category!.Name,
                e.ExpenseDate,
                e.Amount,
                e.Description,
                e.Notes,
                e.PaymentMethod,
                e.SupplierId,
                e.Supplier != null ? e.Supplier.Name : null,
                e.CashboxId,
                e.Cashbox != null ? e.Cashbox.Name : null,
                e.BankId,
                e.Bank != null ? e.Bank.BankName : null,
                e.ReceiptPath,
                e.ReferenceNumber,
                e.IsRecurring,
                e.RecurringFrequency,
                e.RequiresApproval,
                e.IsApproved,
                e.ApprovedBy,
                e.ApprovedAt
            ))
            .FirstAsync(cancellationToken);
    }

    private async Task<bool> ShouldEnqueueExpenseMutationAsync(
        Expense expense,
        CancellationToken cancellationToken,
        bool includeCurrentApprovalState = false)
    {
        if (includeCurrentApprovalState && expense.IsApproved)
        {
            return true;
        }

        if (expense.AccountingMutationVersion > 0)
        {
            return true;
        }

        return await HasAnyExpenseJournalAsync(expense.Id, cancellationToken);
    }

    private async Task<bool> HasAnyExpenseJournalAsync(Guid expenseId, CancellationToken cancellationToken)
    {
        // SECURITY: IgnoreQueryFilters is intentional here because journal lookups
        // must include historical rows, but the tenant predicate is reapplied below.
        return await _context.JournalEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(j =>
                j.TenantId == _tenantContext.RequiredTenantId &&
                j.SourceId == expenseId &&
                j.SourceType == "Expense",
                cancellationToken);
    }

    private static bool ShouldAccountExpense(Expense expense) =>
        expense.IsApproved && !expense.IsDeleted;

    private static string BuildExpenseOperationKey(int mutationVersion) => $"v{mutationVersion}";

    private Guid? ResolveCurrentUserId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? User.FindFirstValue("sub")
                        ?? User.FindFirstValue("userId");

        return Guid.TryParse(rawUserId, out var parsedUserId)
            ? parsedUserId
            : null;
    }

    private (string? Key, IActionResult? ErrorResult) ResolveExpenseIdempotencyKey()
    {
        var headerKey = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();
        var requestId = Request.Headers[RequestIdHeader].FirstOrDefault()?.Trim();

        if (!string.IsNullOrWhiteSpace(headerKey) &&
            !string.IsNullOrWhiteSpace(requestId) &&
            !string.Equals(headerKey, requestId, StringComparison.Ordinal))
        {
            return (null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} and {RequestIdHeader} must match when both are provided."
            }));
        }

        var normalizedKey = ValidateExpenseIdempotencyKey(headerKey ?? requestId);
        if (normalizedKey == null)
        {
            return (null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} header is required for expense mutations."
            }));
        }

        return (normalizedKey, null);
    }

    private string? ValidateExpenseIdempotencyKey(string? key)
    {
        var normalizedKey = key?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedKey) &&
            normalizedKey.Length <= MaxExpenseIdempotencyKeyLength &&
            ExpenseIdempotencyKeyPattern.IsMatch(normalizedKey))
        {
            return normalizedKey;
        }

        _logger.LogWarning(
            "Invalid expense idempotency key received for tenant {TenantId}",
            _tenantContext.TenantId);

        return null;
    }

    private static string BuildExpenseRequestHash(string operation, object request)
    {
        return DeterministicRequestHasher.ComputeHash(new
        {
            operation,
            request,
        });
    }

    private async Task<(SalesRequestIdempotency? Record, IActionResult? ShortCircuitResult)> TryClaimExpenseIdempotencyAsync(
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var claimId = Guid.NewGuid();
            var nowUtc = DateTime.UtcNow;
            var rowsInserted = await TryInsertExpenseIdempotencyClaimAsync(
                claimId,
                tenantId,
                key,
                operation,
                requestHash,
                nowUtc,
                cancellationToken);

            if (rowsInserted > 0)
            {
                var claimedRecord = await _context.SalesRequestIdempotencyRecords
                    .FirstAsync(record =>
                        record.TenantId == tenantId &&
                        record.Key == key &&
                        record.Operation == operation,
                        cancellationToken);

                return (claimedRecord, null);
            }

            var existingRecord = await _context.SalesRequestIdempotencyRecords
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(record =>
                    record.TenantId == tenantId &&
                    record.Key == key &&
                    record.Operation == operation,
                    cancellationToken);

            if (existingRecord?.IsDeleted == true)
            {
                await DeleteExpenseIdempotencyRecordByIdAsync(existingRecord.Id, cancellationToken);
                continue;
            }

            if (existingRecord == null)
            {
                continue;
            }

            if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return (null, BadRequest(new
                {
                    message = "Idempotency key reuse with different payload"
                }));
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                return (null, BuildStoredExpenseResponse(existingRecord));
            }

            var rowsDeleted = await DeletePendingExpenseIdempotencyClaimAsync(tenantId, key, operation, cancellationToken);
            if (rowsDeleted > 0)
            {
                continue;
            }

            return (null, Conflict(new
            {
                message = "An expense request with the same idempotency key is already being processed."
            }));
        }

        return (null, Conflict(new
        {
            message = "An expense request with the same idempotency key is already being processed."
        }));
    }

    private async Task<int> TryInsertExpenseIdempotencyClaimAsync(
        Guid claimId,
        Guid tenantId,
        string key,
        string operation,
        string requestHash,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""SalesRequestIdempotency""
                (""Id"", ""TenantId"", ""Key"", ""Operation"", ""RequestHash"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES
                ({claimId}, {tenantId}, {key}, {operation}, {requestHash}, {nowUtc}, {nowUtc}, {false})
                ON CONFLICT (""TenantId"", ""Key"", ""Operation"") DO NOTHING",
                cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO [SalesRequestIdempotency]
                    ([Id], [TenantId], [Key], [Operation], [RequestHash], [CreatedAt], [UpdatedAt], [IsDeleted])
                SELECT
                    {claimId}, {tenantId}, {key}, {operation}, {requestHash}, {nowUtc}, {nowUtc}, {false}
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [SalesRequestIdempotency] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Key] = {key}
                      AND [Operation] = {operation})",
                cancellationToken);
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
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.Entry(claim).State = EntityState.Detached;
            return 0;
        }
    }

    private async Task<int> DeleteExpenseIdempotencyRecordByIdAsync(Guid recordId, CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM ""SalesRequestIdempotency""
                WHERE ""Id"" = {recordId}",
                cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM [SalesRequestIdempotency]
                WHERE [Id] = {recordId}",
                cancellationToken);
        }

        // SECURITY: IgnoreQueryFilters is intentional so stale soft-deleted idempotency
        // claims can be reclaimed, while the delete still targets one concrete record ID.
        return await _context.SalesRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record => record.Id == recordId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<int> DeletePendingExpenseIdempotencyClaimAsync(
        Guid tenantId,
        string key,
        string operation,
        CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM ""SalesRequestIdempotency""
                WHERE ""TenantId"" = {tenantId}
                  AND ""Key"" = {key}
                  AND ""Operation"" = {operation}
                  AND ""ResponseStatusCode"" IS NULL
                  AND ""CompletedAtUtc"" IS NULL",
                cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM [SalesRequestIdempotency]
                WHERE [TenantId] = {tenantId}
                  AND [Key] = {key}
                  AND [Operation] = {operation}
                  AND [ResponseStatusCode] IS NULL
                  AND [CompletedAtUtc] IS NULL",
                cancellationToken);
        }

        // SECURITY: IgnoreQueryFilters is intentional so abandoned pending claims can
        // be reclaimed, while the tenant/key/operation predicate is reapplied below.
        return await _context.SalesRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == tenantId &&
                record.Key == key &&
                record.Operation == operation &&
                record.ResponseStatusCode == null &&
                record.CompletedAtUtc == null)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<IActionResult> CompleteExpenseIdempotencyAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        SalesRequestIdempotency record,
        IActionResult result,
        CancellationToken cancellationToken)
    {
        var snapshot = SnapshotExpenseActionResult(result);

        record.ResponseStatusCode = snapshot.StatusCode;
        record.ResponseJson = snapshot.ResponseJson;
        record.CompletedAtUtc = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    private static ActionResult<ExpenseDto> ToExpenseActionResult(IActionResult result) =>
        result as ActionResult ?? new StatusCodeResult(StatusCodes.Status500InternalServerError);

    private static ExpenseActionSnapshot SnapshotExpenseActionResult(IActionResult result)
    {
        if (result is ObjectResult objectResult)
        {
            return new ExpenseActionSnapshot(
                objectResult.StatusCode ?? StatusCodes.Status200OK,
                SerializeExpensePayload(objectResult.Value));
        }

        if (result is StatusCodeResult statusCodeResult)
        {
            return new ExpenseActionSnapshot(statusCodeResult.StatusCode, null);
        }

        return new ExpenseActionSnapshot(StatusCodes.Status200OK, null);
    }

    private static IActionResult BuildStoredExpenseResponse(SalesRequestIdempotency record)
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

    private static string? SerializeExpensePayload(object? payload)
    {
        return payload == null
            ? null
            : JsonSerializer.Serialize(payload, ExpenseIdempotencyJsonOptions);
    }

    private sealed record ExpenseActionSnapshot(int StatusCode, string? ResponseJson);

    [HttpPost("report")]
    public async Task<ActionResult<ExpenseReportResponseDto>> GetReport(ExpenseReportRequestDto request, CancellationToken cancellationToken)
    {
        if (request.FromDate.HasValue && request.ToDate.HasValue && request.FromDate.Value > request.ToDate.Value)
        {
            return BadRequest(new { message = "FromDate must be less than or equal to ToDate." });
        }

        var filteredExpenses = _context.Expenses
            .AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.RequiredTenantId)
            .AsQueryable();

        if (request.FromDate.HasValue)
            filteredExpenses = filteredExpenses.Where(e => e.ExpenseDate >= DateTime.SpecifyKind(request.FromDate.Value, DateTimeKind.Utc));

        if (request.ToDate.HasValue)
            filteredExpenses = filteredExpenses.Where(e => e.ExpenseDate <= DateTime.SpecifyKind(request.ToDate.Value, DateTimeKind.Utc));

        if (request.CategoryId.HasValue)
            filteredExpenses = filteredExpenses.Where(e => e.ExpenseCategoryId == request.CategoryId.Value);

        if (!string.IsNullOrEmpty(request.PaymentMethod))
            filteredExpenses = filteredExpenses.Where(e => e.PaymentMethod == request.PaymentMethod);

        if (request.SupplierId.HasValue)
            filteredExpenses = filteredExpenses.Where(e => e.SupplierId == request.SupplierId.Value);

        if (request.IsApproved.HasValue)
            filteredExpenses = filteredExpenses.Where(e => e.IsApproved == request.IsApproved.Value);

        var totalMatchingExpenses = await filteredExpenses.CountAsync(cancellationToken);
        var expenses = await filteredExpenses
            .Include(e => e.Category)
            .Include(e => e.Supplier)
            .OrderBy(e => e.ExpenseDate)
            .Take(MaxExpenseReportItems)
            .ToListAsync(cancellationToken);

        if (totalMatchingExpenses > MaxExpenseReportItems)
        {
            Response.Headers["X-Result-Limit"] = MaxExpenseReportItems.ToString();
            Response.Headers["X-Result-Truncated"] = "true";
            _logger.LogWarning(
                "Expense report truncated to {MaxItems} rows for tenant {TenantId}. Matched rows: {MatchedRows}",
                MaxExpenseReportItems,
                _tenantContext.RequiredTenantId,
                totalMatchingExpenses);
        }

        var items = expenses.Select(e => new ExpenseReportItemDto(
            e.ExpenseDate,
            e.Category!.Name,
            e.Description,
            e.Amount,
            e.PaymentMethod,
            e.Supplier?.Name,
            e.ReferenceNumber,
            e.IsApproved
        )).ToList();

        var totalExpenses = await filteredExpenses.SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;
        var approvedExpenses = await filteredExpenses
            .Where(e => e.IsApproved)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;
        var pendingExpenses = await filteredExpenses
            .Where(e => !e.IsApproved)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        var expensesByCategory = (await filteredExpenses
                .GroupBy(e => e.Category!.Name)
                .Select(group => new { group.Key, Total = group.Sum(expense => expense.Amount) })
                .ToListAsync(cancellationToken))
            .ToDictionary(item => item.Key, item => item.Total);

        var expensesByPaymentMethod = (await filteredExpenses
                .GroupBy(e => e.PaymentMethod)
                .Select(group => new { group.Key, Total = group.Sum(expense => expense.Amount) })
                .ToListAsync(cancellationToken))
            .ToDictionary(item => item.Key, item => item.Total);

        var summary = new ExpenseReportSummaryDto(
            totalExpenses,
            approvedExpenses,
            pendingExpenses,
            expensesByCategory,
            expensesByPaymentMethod
        );

        return Ok(new ExpenseReportResponseDto(items, summary));
    }

    private async Task<string?> ValidateExpenseReferencesAsync(
        Guid categoryId,
        string paymentMethod,
        Guid? supplierId,
        Guid? cashboxId,
        Guid? bankId,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        var categoryExists = await _context.ExpenseCategories
            .AsNoTracking()
            .AnyAsync(category => category.Id == categoryId && category.TenantId == tenantId && category.IsActive, cancellationToken);
        if (!categoryExists)
        {
            return "Expense category not found or inactive.";
        }

        if (supplierId.HasValue)
        {
            var supplierExists = await _context.Suppliers
                .AsNoTracking()
                .AnyAsync(supplier => supplier.Id == supplierId.Value && supplier.TenantId == tenantId && !supplier.IsDeleted, cancellationToken);
            if (!supplierExists)
            {
                return "Supplier not found.";
            }
        }

        if (string.Equals(paymentMethod, "Cash", StringComparison.Ordinal))
        {
            if (bankId.HasValue)
            {
                return "Cash expenses cannot target a bank account.";
            }

            if (cashboxId.HasValue)
            {
                var cashboxExists = await _context.Cashboxes
                    .AsNoTracking()
                    .AnyAsync(cashbox => cashbox.Id == cashboxId.Value && cashbox.TenantId == tenantId && !cashbox.IsDeleted, cancellationToken);
                if (!cashboxExists)
                {
                    return "Cashbox not found.";
                }
            }
        }
        else if (string.Equals(paymentMethod, "BankTransfer", StringComparison.Ordinal))
        {
            if (cashboxId.HasValue)
            {
                return "Bank transfer expenses cannot target a cashbox.";
            }

            if (!bankId.HasValue)
            {
                return "Bank transfer expenses require a bank account.";
            }

            var bankExists = await _context.Banks
                .AsNoTracking()
                .AnyAsync(bank => bank.Id == bankId.Value && bank.TenantId == tenantId && bank.IsActive && !bank.IsDeleted, cancellationToken);
            if (!bankExists)
            {
                return "Bank account not found or inactive.";
            }
        }
        else if (string.Equals(paymentMethod, "Card", StringComparison.Ordinal) ||
                 string.Equals(paymentMethod, "Visa", StringComparison.Ordinal))
        {
            if (cashboxId.HasValue || bankId.HasValue)
            {
                return "Card expenses post through the configured card clearing account.";
            }
        }

        return null;
    }

    private async Task<IActionResult?> ValidateExpenseReferencesAsync(
        CreateExpenseDto request,
        CancellationToken cancellationToken)
    {
        var error = await ValidateExpenseReferencesAsync(
            request.ExpenseCategoryId,
            request.PaymentMethod,
            request.SupplierId,
            request.CashboxId,
            request.BankId,
            cancellationToken);

        return error == null ? null : BadRequest(new { message = error });
    }

    private async Task<IActionResult?> ValidateExpenseReferencesAsync(
        UpdateExpenseDto request,
        CancellationToken cancellationToken)
    {
        var error = await ValidateExpenseReferencesAsync(
            request.ExpenseCategoryId,
            request.PaymentMethod,
            request.SupplierId,
            request.CashboxId,
            request.BankId,
            cancellationToken);

        return error == null ? null : BadRequest(new { message = error });
    }

    private static bool ValidateExpenseMutationRequest(CreateExpenseDto request, out string error) =>
        ValidateExpenseMutationFields(request.Amount, request.Description, request.PaymentMethod, out error);

    private static bool ValidateExpenseMutationRequest(UpdateExpenseDto request, out string error) =>
        ValidateExpenseMutationFields(request.Amount, request.Description, request.PaymentMethod, out error);

    private static bool ValidateExpenseMutationFields(
        decimal amount,
        string description,
        string paymentMethod,
        out string error)
    {
        if (amount <= 0m)
        {
            error = "Expense amount must be greater than zero.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            error = "Expense description is required.";
            return false;
        }

        if (!IsSupportedExpensePaymentMethod(paymentMethod))
        {
            error = "Unsupported expense payment method. Supported methods: Cash, Card, Visa, BankTransfer.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsSupportedExpensePaymentMethod(string paymentMethod) =>
        string.Equals(paymentMethod, "Cash", StringComparison.Ordinal) ||
        string.Equals(paymentMethod, "Card", StringComparison.Ordinal) ||
        string.Equals(paymentMethod, "Visa", StringComparison.Ordinal) ||
        string.Equals(paymentMethod, "BankTransfer", StringComparison.Ordinal);

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
