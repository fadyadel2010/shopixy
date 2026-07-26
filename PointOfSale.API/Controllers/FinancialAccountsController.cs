using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Financial Accounts Controller
/// PHASE LCR: Transfer operations now go through TreasuryOperationService for ledger posting.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FinancialAccountsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<FinancialAccountsController> _logger;
    private readonly ITreasuryOperationService _treasuryOperationService;
    private readonly ITreasuryReadService _treasuryReadService;
    private readonly IAccountResolver _accountResolver;
    private readonly ICashboxLedgerAccountService _cashboxLedgerAccountService;
    private Guid TenantId => _tenantContext.RequiredTenantId;

    public FinancialAccountsController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<FinancialAccountsController> logger,
        ITreasuryOperationService treasuryOperationService,
        ITreasuryReadService treasuryReadService,
        IAccountResolver accountResolver,
        ICashboxLedgerAccountService cashboxLedgerAccountService)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
        _treasuryOperationService = treasuryOperationService;
        _treasuryReadService = treasuryReadService;
        _accountResolver = accountResolver;
        _cashboxLedgerAccountService = cashboxLedgerAccountService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<FinancialAccountDto>>> GetAccounts()
    {
        var tenantId = TenantId;
        var accounts = await _context.FinancialAccounts
            .Where(a => a.TenantId == tenantId && !a.IsDeleted)
            .Include(a => a.ParentAccount)
            .ToListAsync();

        var dtos = accounts.Select(a => new FinancialAccountDto
        {
            Id = a.Id,
            Name = a.Name,
            AccountNumber = a.AccountNumber,
            IsSystem = a.IsSystem,
            Balance = a.Balance,
            ParentAccountId = a.ParentAccountId,
            ParentAccountName = a.ParentAccount?.Name
        }).ToList();

        return Ok(dtos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<FinancialAccountDto>> GetAccount(Guid id)
    {
        var tenantId = TenantId;
        var account = await _context.FinancialAccounts
            .Include(a => a.ParentAccount)
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId && !a.IsDeleted);

        if (account == null) return NotFound();

        return Ok(new FinancialAccountDto
        {
            Id = account.Id,
            Name = account.Name,
            AccountNumber = account.AccountNumber,
            IsSystem = account.IsSystem,
            Balance = account.Balance,
            ParentAccountId = account.ParentAccountId,
            ParentAccountName = account.ParentAccount?.Name
        });
    }

    [HttpPost]
    public async Task<ActionResult<FinancialAccountDto>> CreateAccount(CreateFinancialAccountDto request)
    {
        var tenantId = TenantId;
        if (request.ParentAccountId.HasValue)
        {
            var parentExists = await _context.FinancialAccounts.AnyAsync(a =>
                a.Id == request.ParentAccountId.Value &&
                a.TenantId == tenantId &&
                !a.IsDeleted);
            if (!parentExists)
                return BadRequest(new { message = "Parent account not found" });
        }

        var account = new FinancialAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            AccountNumber = request.AccountNumber,
            ParentAccountId = request.ParentAccountId,
            IsSystem = false,
            Balance = 0,
            CreatedAt = DateTime.UtcNow
        };

        _context.FinancialAccounts.Add(account);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAccount), new { id = account.Id }, new FinancialAccountDto
        {
            Id = account.Id,
            Name = account.Name,
            AccountNumber = account.AccountNumber,
            IsSystem = account.IsSystem,
            Balance = account.Balance,
            ParentAccountId = account.ParentAccountId
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAccount(Guid id, UpdateFinancialAccountDto request)
    {
        var tenantId = TenantId;
        var account = await _context.FinancialAccounts
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId && !a.IsDeleted);
        if (account == null) return NotFound();

        if (account.IsSystem)
        {
            return BadRequest(new { message = "Cannot modify system accounts" });
        }

        if (request.ParentAccountId.HasValue)
        {
            if (request.ParentAccountId.Value == id)
                return BadRequest(new { message = "An account cannot be its own parent" });

            var parentExists = await _context.FinancialAccounts.AnyAsync(a =>
                a.Id == request.ParentAccountId.Value &&
                a.TenantId == tenantId &&
                !a.IsDeleted);
            if (!parentExists)
                return BadRequest(new { message = "Parent account not found" });
        }

        account.Name = request.Name;
        account.AccountNumber = request.AccountNumber;
        account.ParentAccountId = request.ParentAccountId;
        account.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAccount(Guid id)
    {
        var tenantId = TenantId;
        var account = await _context.FinancialAccounts
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId && !a.IsDeleted);
        if (account == null) return NotFound();

        if (account.IsSystem)
        {
            return BadRequest(new { message = "Cannot delete system accounts" });
        }

        var hasSubAccounts = await _context.FinancialAccounts.AnyAsync(a =>
            a.TenantId == tenantId &&
            a.ParentAccountId == id &&
            !a.IsDeleted);
        if (hasSubAccounts)
        {
            return BadRequest(new { message = "Cannot delete account with sub-accounts" });
        }

        var hasTransactions = await _context.FinancialTransactions.AnyAsync(t =>
            t.TenantId == tenantId &&
            t.AccountId == id);
        if (hasTransactions)
        {
            return BadRequest(new { message = "Cannot delete account with transactions" });
        }

        _context.FinancialAccounts.Remove(account);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer(TransferRequestDto request)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            decimal sourceBalance;
            FinancialAccount? sourceAccount = null;
            Cashbox? sourceCashbox = null;
            string fromAccountCode;

            if (request.SourceType == "Account")
            {
                sourceAccount = await _context.FinancialAccounts
                    .FirstOrDefaultAsync(fa => fa.Id == request.SourceId && fa.TenantId == _tenantContext.RequiredTenantId);
                if (sourceAccount == null) return BadRequest(new { message = "Source account not found" });

                fromAccountCode = await ResolveLedgerTransferAccountCodeAsync(sourceAccount);
                sourceBalance = await _treasuryReadService.GetAccountBalanceAsync(
                    _tenantContext.RequiredTenantId,
                    fromAccountCode);
            }
            else if (request.SourceType == "Cashbox")
            {
                sourceCashbox = await _context.Cashboxes
                    .FirstOrDefaultAsync(cb => cb.Id == request.SourceId && cb.TenantId == _tenantContext.RequiredTenantId);
                if (sourceCashbox == null) return BadRequest(new { message = "Source cashbox not found" });

                fromAccountCode = (await _cashboxLedgerAccountService.ResolveCashboxAccountAsync(
                    _tenantContext.RequiredTenantId,
                    sourceCashbox.Id)).AccountCode;
                sourceBalance = await _treasuryReadService.GetAccountBalanceAsync(
                    _tenantContext.RequiredTenantId,
                    fromAccountCode);
            }
            else
            {
                return BadRequest(new { message = "Invalid source type" });
            }

            FinancialAccount? destAccount = null;
            Cashbox? destCashbox = null;
            string toAccountCode;

            if (request.DestinationType == "Account")
            {
                destAccount = await _context.FinancialAccounts
                    .FirstOrDefaultAsync(fa => fa.Id == request.DestinationId && fa.TenantId == _tenantContext.RequiredTenantId);
                if (destAccount == null) return BadRequest(new { message = "Destination account not found" });

                toAccountCode = await ResolveLedgerTransferAccountCodeAsync(destAccount);
            }
            else if (request.DestinationType == "Cashbox")
            {
                destCashbox = await _context.Cashboxes
                    .FirstOrDefaultAsync(cb => cb.Id == request.DestinationId && cb.TenantId == _tenantContext.RequiredTenantId);
                if (destCashbox == null) return BadRequest(new { message = "Destination cashbox not found" });

                toAccountCode = (await _cashboxLedgerAccountService.ResolveCashboxAccountAsync(
                    _tenantContext.RequiredTenantId,
                    destCashbox.Id)).AccountCode;
            }
            else
            {
                return BadRequest(new { message = "Invalid destination type" });
            }

            // Validate that source and destination resolve to different ledger accounts.
            // ResolveLedgerTransferAccountCodeAsync maps multiple FinancialAccount instances
            // to the same treasury control accounts (e.g., AccountCodes.PosVisaAccount, AccountCodes.BankAccount).
            // Transfers that debit and credit the same ledger control account are not allowed
            // as they would have no net effect on the ledger.
            if (fromAccountCode == toAccountCode)
            {
                return BadRequest(new { message = $"Transfers between accounts that resolve to the same treasury control account ({fromAccountCode}) are not allowed. " +
                    $"Source: {sourceAccount?.Name ?? sourceCashbox?.Name}, Destination: {destAccount?.Name ?? destCashbox?.Name}" });
            }

            if (sourceBalance < request.Amount)
            {
                _logger.LogWarning(
                    "Transfer may cause negative balance. Source: {Source}, LedgerBalance: {Balance}, Amount: {Amount}",
                    sourceAccount?.Name ?? sourceCashbox?.Name,
                    sourceBalance,
                    request.Amount);
            }

            var operationId = Guid.NewGuid();
            var userId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid)
                ? uid
                : (Guid?)null;

            var journal = await _treasuryOperationService.TransferAsync(
                _tenantContext.RequiredTenantId,
                fromAccountCode,
                toAccountCode,
                request.Amount,
                request.Description ?? $"Transfer from {request.SourceType} to {request.DestinationType}",
                null,
                operationId,
                userId);

            _logger.LogInformation(
                "Transfer completed via Ledger. Journal #{JournalNumber}, From: {From} ({FromCode}), To: {To} ({ToCode}), Amount: {Amount}",
                journal.JournalNumber,
                sourceAccount?.Name ?? sourceCashbox?.Name,
                fromAccountCode,
                destAccount?.Name ?? destCashbox?.Name,
                toAccountCode,
                request.Amount);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                message = "Transfer completed successfully",
                journalNumber = journal.JournalNumber,
                operationId
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error processing transfer");
            return StatusCode(500, new { message = "An error occurred while processing the transfer" });
        }
    }

    private async Task<string> ResolveLedgerTransferAccountCodeAsync(FinancialAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        string ledgerAccountCode;

        if (account.Name.Contains("Visa", StringComparison.OrdinalIgnoreCase))
        {
            ledgerAccountCode = AccountCodes.PosVisaAccount;
        }
        else if (account.IsSystem || !string.IsNullOrWhiteSpace(account.AccountNumber))
        {
            ledgerAccountCode = AccountCodes.BankAccount;
        }
        else
        {
            throw new InvalidOperationException(
                $"Financial account '{account.Name}' is not mapped to a treasury ledger account. Configure a treasury account code before transferring funds.");
        }

        _ = await _accountResolver.GetByCodeAsync(
            _tenantContext.RequiredTenantId,
            ledgerAccountCode);

        return ledgerAccountCode;
    }
}
