using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs;
using PointOfSale.API.Extensions;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TreasuryController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<TreasuryController> _logger;
    private readonly ITreasuryReadService _treasuryReadService;
    private readonly ITreasuryOperationService _treasuryOperationService;
    private readonly ICashboxLedgerAccountService _cashboxLedgerAccountService;

    public TreasuryController(
        ApplicationDbContext context, 
        ITenantContext tenantContext, 
        ILogger<TreasuryController> logger,
        ITreasuryReadService treasuryReadService,
        ITreasuryOperationService treasuryOperationService,
        ICashboxLedgerAccountService cashboxLedgerAccountService)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
        _treasuryReadService = treasuryReadService;
        _treasuryOperationService = treasuryOperationService;
        _cashboxLedgerAccountService = cashboxLedgerAccountService;
    }

    /// <summary>
    /// Get treasury summary (current balance and today's activity)
    /// </summary>
    [HttpGet("summary")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<TreasurySummaryDto>> GetSummary()
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;
            var today = DateTime.UtcNow.Date;

            var cashboxAccounts = await _cashboxLedgerAccountService.ListCashboxAccountsAsync(tenantId);
            var cashboxAccountCodes = cashboxAccounts
                .Select(account => account.AccountCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var currentBalance = cashboxAccountCodes.Count == 0
                ? 0m
                : (await _treasuryReadService.GetAccountBalancesAsync(tenantId, cashboxAccountCodes))
                    .Values
                    .Sum();

            var todayTransactions = cashboxAccountCodes.Count == 0
                ? Array.Empty<TreasuryMovementDto>()
                : await _treasuryReadService.GetMovementsAsync(
                    tenantId,
                    cashboxAccountCodes,
                    today,
                    today);

            // Group by JournalId to avoid counting ledger lines as separate transactions
            var journalGroups = todayTransactions
                .GroupBy(t => t.JournalId)
                .Select(g => new
                {
                    JournalId = g.Key,
                    TotalDebit = g.Sum(x => x.Debit),
                    TotalCredit = g.Sum(x => x.Credit)
                })
                .ToList();

            var totalDeposits = journalGroups.Sum(g => g.TotalDebit);
            var totalWithdrawals = journalGroups.Sum(g => g.TotalCredit);

            var summary = new TreasurySummaryDto
            {
                CurrentBalance = currentBalance,
                TotalDepositsToday = totalDeposits,
                TotalWithdrawalsToday = totalWithdrawals,
                NetChangeToday = totalDeposits - totalWithdrawals,
                TransactionCountToday = journalGroups.Count
            };

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting treasury summary");
            return StatusCode(500, new { message = "An unexpected error occurred while retrieving the treasury summary." });
        }
    }

    /// <summary>
    /// Get recent treasury transactions with filtering
    /// </summary>
    [HttpGet("transactions")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<PagedResult<TreasuryTransactionDto>>> GetTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int? transactionType = null,
        [FromQuery] int? paymentMethod = null)
    {
        try
        {
            page = PaginationGuard.NormalizePage(page);
            pageSize = PaginationGuard.NormalizePageSize(pageSize);
            var skip = PaginationGuard.GetSkip(page, pageSize);

            var tenantId = _tenantContext.RequiredTenantId;
            var fromDate = startDate.HasValue
                ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)
                : DateTime.UtcNow.Date.AddMonths(-1);
            var toDate = endDate.HasValue
                ? endDate.Value.NormalizeInclusiveEndUtc()
                : DateTime.UtcNow.Date.NormalizeInclusiveEndUtc();

            var treasuryAccounts = await _treasuryReadService.GetTreasuryAccountsAsync(tenantId);
            var accountMetadata = treasuryAccounts
                .Select(account => new TreasuryAccountMetadata(
                    account.AccountId,
                    ResolveAccountPaymentMethod(account.Type)))
                .ToList();

            if (accountMetadata.Count == 0)
            {
                var cashboxAccounts = await _cashboxLedgerAccountService.ListCashboxAccountsAsync(tenantId);
                accountMetadata = cashboxAccounts
                    .Select(account => new TreasuryAccountMetadata(account.AccountId, 0))
                    .ToList();
            }

            if (paymentMethod is 1 or 2)
            {
                accountMetadata = accountMetadata
                    .Where(account => account.PaymentMethod == paymentMethod.Value)
                    .ToList();
            }

            if (accountMetadata.Count == 0)
            {
                return Ok(new PagedResult<TreasuryTransactionDto>
                {
                    Items = new List<TreasuryTransactionDto>(),
                    TotalCount = 0,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = 0
                });
            }

            var accountPaymentLookup = accountMetadata
                .ToDictionary(account => account.AccountId, account => account.PaymentMethod);
            var movementQuery = BuildTreasuryMovementQuery(
                tenantId,
                accountMetadata.Select(account => account.AccountId).ToList(),
                fromDate,
                toDate);

            if (transactionType.HasValue)
            {
                movementQuery = transactionType.Value switch
                {
                    1 => movementQuery.Where(movement => movement.Debit > 0),
                    2 => movementQuery.Where(movement => movement.Credit > 0),
                    _ => movementQuery.Where(_ => false)
                };
            }

            if (paymentMethod.HasValue)
            {
                movementQuery = paymentMethod.Value switch
                {
                    1 or 2 => movementQuery,
                    3 => movementQuery.Where(movement => movement.SourceType == "SupplierPayment"),
                    4 => movementQuery.Where(movement => movement.SourceType == "SalesInvoice" || movement.SourceType == "Return"),
                    _ => movementQuery.Where(_ => false)
                };
            }

            var totalCount = await movementQuery.CountAsync();
            var movements = await movementQuery
                .OrderByDescending(movement => movement.TimestampUtc)
                .ThenByDescending(movement => movement.JournalNumber)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync();

            var dtos = movements
                .Select(movement =>
                {
                    var resolvedPaymentMethod = ResolvePaymentMethod(movement, accountPaymentLookup);
                    var resolvedTransactionType = GetTransactionType(movement);
                    var timestamp = GetMovementTimestampUtc(movement);

                    return new TreasuryTransactionDto
                    {
                        Id = movement.Id,
                        TransactionNumber = movement.JournalNumber,
                        TransactionDate = timestamp,
                        TransactionType = resolvedTransactionType,
                        TransactionTypeName = resolvedTransactionType == 1 ? "Deposit" : "Withdrawal",
                        PaymentMethod = resolvedPaymentMethod,
                        PaymentMethodName = GetPaymentMethodName(resolvedPaymentMethod),
                        TargetAccountId = movement.AccountId,
                        TargetAccountName = movement.AccountName,
                        Amount = movement.Debit > 0 ? movement.Debit : movement.Credit,
                        BalanceAfter = movement.BalanceAfter,
                        ReferenceNumber = string.IsNullOrWhiteSpace(movement.SourceReference)
                            ? movement.JournalNumber
                            : movement.SourceReference,
                        Notes = movement.Description,
                        CreatedByUserId = movement.CreatedByUserId,
                        CreatedByUserName = movement.CreatedByUserName,
                        CreatedAt = timestamp
                    };
                })
                .ToList();

            var result = new PagedResult<TreasuryTransactionDto>
            {
                Items = dtos,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting treasury transactions");
            return StatusCode(500, new { message = "An unexpected error occurred while retrieving treasury transactions." });
        }
    }

    /// <summary>
    /// Get accounts by payment method type for dropdown
    /// </summary>
    [HttpGet("accounts/{paymentMethod}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<List<AccountOptionDto>>> GetAccountsByPaymentMethod(int paymentMethod)
    {
        try
        {
            List<AccountOptionDto> accounts = new();

            switch (paymentMethod)
            {
                case 1: // Bank
                    accounts = await _context.Banks
                        .Where(b => b.TenantId == _tenantContext.RequiredTenantId && b.IsActive)
                        .Select(b => new AccountOptionDto
                        {
                            Id = b.Id,
                            Name = b.BankName + (b.AccountNumber != null ? $" - {b.AccountNumber}" : ""),
                            CurrentBalance = null // Banks don't have a direct balance field
                        })
                        .ToListAsync();
                    break;

                case 2: // Visa
                    // Visa is the treasury itself, no dropdown needed -> NO, Visa is a target account now
                    // But we don't need to select a specific Visa account if there is only one system one.
                    // So returning empty list is fine if frontend doesn't expect options.
                    break;

                case 3: // Supplier
                    accounts = await _context.Suppliers
                        .Where(s => s.TenantId == _tenantContext.RequiredTenantId)
                        .Select(s => new AccountOptionDto
                        {
                            Id = s.Id,
                            Name = s.Name,
                            CurrentBalance = s.Balance
                        })
                        .ToListAsync();
                    break;

                case 4: // Customer
                    accounts = await _context.Customers
                        .Where(c => c.TenantId == _tenantContext.RequiredTenantId)
                        .Select(c => new AccountOptionDto
                        {
                            Id = c.Id,
                            Name = c.Name,
                            CurrentBalance = c.Balance
                        })
                        .ToListAsync();
                    break;

                default:
                    return BadRequest(new { message = "Invalid payment method" });
            }

            return Ok(accounts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting accounts for payment method {PaymentMethod}", paymentMethod);
            return StatusCode(500, new { message = "Error retrieving accounts" });
        }
    }
    // ════════════════════════════════════════════════════════════════════════════
    // ⚠️ PHASE TOJ: LEGACY CreateTransaction ENDPOINT - REMOVED
    // 
    // This endpoint has been REMOVED because direct writes to legacy treasury tables are blocked.
    //
    // ✅ USE LEDGER-BASED ENDPOINTS INSTEAD:
    // - POST /api/treasury/operations/deposit    → Deposit money
    // - POST /api/treasury/operations/withdraw   → Withdraw money
    // - POST /api/treasury/operations/transfer   → Transfer between accounts
    // - POST /api/treasury/operations/expense    → Record expense
    // - POST /api/treasury/operations/cash-in    → Cash in
    // - POST /api/treasury/operations/cash-out   → Cash out
    //
    // See TreasuryOperationService for implementation.
    // ════════════════════════════════════════════════════════════════════════════
    
    // ════════════════════════════════════════════════════════════════════════════
    // PHASE TLW: LEDGER-BASED ENDPOINTS
    // These endpoints read ONLY from the Ledger system (Accounts, JournalEntries, LedgerEntries)
    // ════════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// PHASE TLW: Get account balance from Ledger.
    /// Replaces legacy cashbox/visa balance endpoints.
    /// </summary>
    [HttpGet("balance/{accountCode}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<decimal>> GetLedgerBalance(string accountCode)
    {
        try
        {
            var balance = await _treasuryReadService.GetAccountBalanceAsync(
                _tenantContext.RequiredTenantId, 
                accountCode);
            
            return Ok(new { accountCode, balance });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting balance for account {AccountCode}", accountCode);
            return StatusCode(500, new { message = "An unexpected error occurred while retrieving the account balance." });
        }
    }
    
    /// <summary>
    /// PHASE TLW: Get account movements from Ledger.
    /// Replaces legacy transaction list endpoints.
    /// </summary>
    [HttpGet("movements/{accountCode}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<IReadOnlyList<TreasuryMovementDto>>> GetLedgerMovements(
        string accountCode,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        try
        {
            // Ensure UTC kind for PostgreSQL compatibility
            var fromDate = from.HasValue 
                ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc) 
                : DateTime.UtcNow.Date.AddMonths(-1);
            var toDate = to.HasValue 
                ? DateTime.SpecifyKind(to.Value, DateTimeKind.Utc) 
                : DateTime.UtcNow.Date;
            
            var movements = await _treasuryReadService.GetMovementsAsync(
                _tenantContext.RequiredTenantId,
                accountCode,
                fromDate,
                toDate);
            
            return Ok(movements);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting movements for account {AccountCode}", accountCode);
            return StatusCode(500, new { message = "An unexpected error occurred while retrieving account movements." });
        }
    }
    
    /// <summary>
    /// PHASE TLW: Get Treasury dashboard data from Ledger.
    /// All metrics computed from Ledger accounts.
    /// </summary>
    [HttpGet("dashboard")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<TreasuryDashboardDto>> GetLedgerDashboard(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        try
        {
            // Ensure UTC kind for PostgreSQL compatibility
            var fromDate = from.HasValue 
                ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc) 
                : DateTime.UtcNow.Date.AddMonths(-1);
            var toDate = to.HasValue 
                ? DateTime.SpecifyKind(to.Value, DateTimeKind.Utc) 
                : DateTime.UtcNow.Date;
            
            var dashboard = await _treasuryReadService.GetDashboardAsync(
                _tenantContext.RequiredTenantId,
                fromDate,
                toDate);
            
            return Ok(dashboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting treasury dashboard");
            return StatusCode(500, new { message = "An unexpected error occurred while retrieving the treasury dashboard." });
        }
    }
    
    /// <summary>
    /// PHASE TLW: Get balances for multiple accounts.
    /// Useful for dashboard cards.
    /// </summary>
    [HttpGet("balances")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<Dictionary<string, decimal>>> GetMultipleBalances(
        [FromQuery] string accountCodes)
    {
        try
        {
            var codes = accountCodes.Split(',', StringSplitOptions.RemoveEmptyEntries);
            
            var balances = await _treasuryReadService.GetAccountBalancesAsync(
                _tenantContext.RequiredTenantId,
                codes);
            
            return Ok(balances);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting multiple balances");
            return StatusCode(500, new { message = "An unexpected error occurred while retrieving account balances." });
        }
    }
    
    /// <summary>
    /// TREASURY TRANSFERS UI: Get all treasury accounts for dropdown.
    /// Returns ledger accounts suitable for transfers (Cashbox, Bank, Clearing).
    /// </summary>
    [HttpGet("accounts")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<List<TreasuryAccountOptionDto>>> GetTreasuryAccounts()
    {
        try
        {
            var accounts = await _treasuryReadService.GetTreasuryAccountsAsync(_tenantContext.RequiredTenantId);

            return Ok(accounts.Select(account => new TreasuryAccountOptionDto
            {
                AccountCode = account.AccountCode,
                Name = account.Name,
                NameAr = account.NameAr,
                CurrentBalance = account.CurrentBalance,
                AccountType = account.Type.ToString(),
                IsClearing = account.IsClearing,
                PreventNegative = account.PreventNegative
            }).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting treasury accounts");
            return StatusCode(500, new { message = "An unexpected error occurred while retrieving treasury accounts." });
        }
    }
    
    // ════════════════════════════════════════════════════════════════════════════
    // PHASE TOJ: LEDGER-BASED TREASURY OPERATIONS
    // ALL money mutations go through journals - NO DIRECT DATABASE WRITES
    // ════════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// PHASE TOJ: Deposit money (transfer from one account to another).
    /// Creates a balanced journal entry in the ledger.
    /// </summary>
    [HttpPost("operations/deposit")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    [EnableRateLimiting("FinancialWrite")]
    public async Task<ActionResult> LedgerDeposit([FromBody] TreasuryDepositRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            
            var journal = await _treasuryOperationService.DepositAsync(
                _tenantContext.RequiredTenantId,
                request.SourceAccountCode,
                request.TargetAccountCode,
                request.Amount,
                request.Description ?? $"Deposit from {request.SourceAccountCode} to {request.TargetAccountCode}",
                request.Reference,
                request.OperationId,
                userId);
            
            return Ok(new
            {
                success = true,
                journalId = journal.Id,
                journalNumber = journal.JournalNumber,
                message = $"Deposit of {request.Amount:C} posted successfully"
            });
        }
        catch (InsufficientFundsException ex)
        {
            return LedgerDepositBadRequest(request, ex);
        }
        catch (AccountNotFoundException ex)
        {
            return LedgerDepositBadRequest(request, ex);
        }
        catch (InactiveAccountException ex)
        {
            return LedgerDepositBadRequest(request, ex);
        }
        catch (InvalidJournalException ex)
        {
            return LedgerDepositBadRequest(request, ex);
        }
        catch (TreasuryValidationException ex)
        {
            return LedgerDepositBadRequest(request, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phase TOJ: Error posting deposit");
            return StatusCode(500, new { success = false, message = "An unexpected error occurred while posting the deposit." });
        }
    }
    
    /// <summary>
    /// PHASE TOJ: Withdraw money (transfer from one account to another).
    /// Creates a balanced journal entry in the ledger.
    /// </summary>
    [HttpPost("operations/withdraw")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    [EnableRateLimiting("FinancialWrite")]
    public async Task<ActionResult> LedgerWithdraw([FromBody] TreasuryWithdrawRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            
            var journal = await _treasuryOperationService.WithdrawAsync(
                _tenantContext.RequiredTenantId,
                request.SourceAccountCode,
                request.TargetAccountCode,
                request.Amount,
                request.Description ?? $"Withdrawal from {request.SourceAccountCode} to {request.TargetAccountCode}",
                request.Reference,
                request.OperationId,
                userId);
            
            return Ok(new
            {
                success = true,
                journalId = journal.Id,
                journalNumber = journal.JournalNumber,
                message = $"Withdrawal of {request.Amount:C} posted successfully"
            });
        }
        catch (InsufficientFundsException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Withdrawal validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (AccountNotFoundException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Withdrawal validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (InactiveAccountException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Withdrawal validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (InvalidJournalException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Withdrawal validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (TreasuryValidationException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Withdrawal validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phase TOJ: Error posting withdrawal");
            return StatusCode(500, new { success = false, message = "An unexpected error occurred while posting the withdrawal." });
        }
    }
    
    /// <summary>
    /// PHASE TOJ: Transfer money between any two ledger accounts.
    /// Creates a balanced journal entry in the ledger.
    /// </summary>
    [HttpPost("operations/transfer")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    [EnableRateLimiting("FinancialWrite")]
    public async Task<ActionResult> LedgerTransfer([FromBody] TreasuryTransferRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            
            var journal = await _treasuryOperationService.TransferAsync(
                _tenantContext.RequiredTenantId,
                request.FromAccountCode,
                request.ToAccountCode,
                request.Amount,
                request.Description ?? $"Transfer from {request.FromAccountCode} to {request.ToAccountCode}",
                request.Reference,
                request.OperationId,
                userId);
            
            return Ok(new
            {
                success = true,
                journalId = journal.Id,
                journalNumber = journal.JournalNumber,
                message = $"Transfer of {request.Amount:C} posted successfully"
            });
        }
        catch (InsufficientFundsException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Transfer validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (AccountNotFoundException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Transfer validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (InactiveAccountException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Transfer validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (InvalidJournalException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Transfer validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (TreasuryValidationException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Transfer validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phase TOJ: Error posting transfer");
            return StatusCode(500, new { success = false, message = "An unexpected error occurred while posting the transfer." });
        }
    }
    
    /// <summary>
    /// PHASE TOJ: Record expense paid from treasury account.
    /// Creates: DEBIT Expense, CREDIT PaidFrom
    /// </summary>
    [HttpPost("operations/expense")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    [EnableRateLimiting("FinancialWrite")]
    public async Task<ActionResult> LedgerExpense([FromBody] TreasuryExpenseRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            
            var journal = await _treasuryOperationService.RecordExpenseAsync(
                _tenantContext.RequiredTenantId,
                request.PaidFromAccountCode,
                request.ExpenseAccountCode,
                request.Amount,
                request.Description,
                request.Reference,
                request.OperationId,
                userId);
            
            return Ok(new
            {
                success = true,
                journalId = journal.Id,
                journalNumber = journal.JournalNumber,
                message = $"Expense of {request.Amount:C} posted successfully"
            });
        }
        catch (InsufficientFundsException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Expense validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (AccountNotFoundException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Expense validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (InactiveAccountException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Expense validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (InvalidJournalException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Expense validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (TreasuryValidationException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Expense validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phase TOJ: Error posting expense");
            return StatusCode(500, new { success = false, message = "An unexpected error occurred while posting the expense." });
        }
    }
    
    /// <summary>
    /// PHASE TOJ: Cash In - receive money into treasury.
    /// </summary>
    [HttpPost("operations/cash-in")]
    [EnableRateLimiting("FinancialWrite")]
    public async Task<ActionResult> LedgerCashIn([FromBody] TreasuryCashInRequest request)
    {
        await Task.CompletedTask;
        return StatusCode(StatusCodes.Status410Gone, new
        {
            success = false,
            code = "TREASURY_INFLOW_WORKFLOW_REQUIRED",
            message = "Direct cash-in is disabled. Use certified payment collection or accounting settlement workflows."
        });
    }
    
    /// <summary>
    /// PHASE TOJ: Cash Out - pay money from treasury.
    /// </summary>
    [HttpPost("operations/cash-out")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    [EnableRateLimiting("FinancialWrite")]
    public async Task<ActionResult> LedgerCashOut([FromBody] TreasuryCashOutRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            
            var journal = await _treasuryOperationService.CashOutAsync(
                _tenantContext.RequiredTenantId,
                request.SourceAccountCode,
                request.DebitAccountCode,
                request.Amount,
                request.Description ?? "Cash paid",
                request.Reference,
                request.OperationId,
                userId);
            
            return Ok(new
            {
                success = true,
                journalId = journal.Id,
                journalNumber = journal.JournalNumber,
                message = $"Cash out of {request.Amount:C} posted successfully"
            });
        }
        catch (InsufficientFundsException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Cash out validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (AccountNotFoundException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Cash out validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (InactiveAccountException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Cash out validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (InvalidJournalException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Cash out validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (TreasuryValidationException ex)
        {
            _logger.LogWarning(ex, "Phase TOJ: Cash out validation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phase TOJ: Error posting cash out");
            return StatusCode(500, new { success = false, message = "An unexpected error occurred while posting the cash-out operation." });
        }
    }
    
    private ActionResult LedgerDepositBadRequest(TreasuryDepositRequest request, Exception ex)
    {
        _logger.LogWarning(
            ex,
            "Phase TOJ: Deposit rejected. Source={SourceAccountCode}, Target={TargetAccountCode}, Amount={Amount}",
            request.SourceAccountCode,
            request.TargetAccountCode,
            request.Amount);

        return BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }

    private Guid? GetCurrentUserId()
    {
        return Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId) 
            ? userId 
            : null;
    }

    private static int GetTransactionType(TreasuryMovementDto movement) => movement.Debit > 0 ? 1 : 2;

    private static DateTime GetMovementTimestampUtc(TreasuryMovementDto movement)
    {
        if (movement.TimestampUtc != default)
        {
            return movement.TimestampUtc.Kind == DateTimeKind.Utc
                ? movement.TimestampUtc
                : DateTime.SpecifyKind(movement.TimestampUtc, DateTimeKind.Utc);
        }

        return movement.Date.Kind == DateTimeKind.Utc
            ? movement.Date
            : DateTime.SpecifyKind(movement.Date, DateTimeKind.Utc);
    }

    private static int ResolvePaymentMethod(
        TreasuryMovementDto movement,
        IReadOnlyDictionary<Guid, int> accountPaymentLookup)
    {
        if (accountPaymentLookup.TryGetValue(movement.AccountId, out var accountPaymentMethod) &&
            accountPaymentMethod != 0)
        {
            return accountPaymentMethod;
        }

        return movement.SourceType switch
        {
            "SupplierPayment" => 3,
            "SalesInvoice" or "Return" => 4,
            _ => 0
        };
    }

    private IQueryable<TreasuryMovementDto> BuildTreasuryMovementQuery(
        Guid tenantId,
        IReadOnlyCollection<Guid> accountIds,
        DateTime fromDate,
        DateTime toDate)
    {
        return _context.ProjectToTreasuryMovements(tenantId, accountIds, fromDate, toDate);
    }

    private static int ResolveAccountPaymentMethod(TreasuryAccountType accountType) =>
        accountType switch
        {
            TreasuryAccountType.Bank => 1,
            TreasuryAccountType.Clearing => 2,
            _ => 0
        };

    private sealed record TreasuryAccountMetadata(Guid AccountId, int PaymentMethod);

    // ════════════════════════════════════════════════════════════════════════════
    // ⚠️ PHASE TOJ: LEGACY HELPER METHODS - REMOVED (Except GetPaymentMethodName)
    //
    // The following methods were only used by the legacy CreateTransaction endpoint:
    // - GetTargetAccountName
    // - UpdateTargetAccount  
    // - GenerateTransactionNumber
    //
    // These have been removed as part of the ledger migration.
    // All treasury operations now go through TreasuryOperationService.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Helper to convert payment method int to display name.
    /// Still used by GetTransactions for displaying historical legacy data.
    /// </summary>
    private static string GetPaymentMethodName(int paymentMethod)
    {
        return paymentMethod switch
        {
            1 => "Bank",
            2 => "Visa",
            3 => "Supplier",
            4 => "Customer",
            _ => "Unknown"
        };
    }
}
