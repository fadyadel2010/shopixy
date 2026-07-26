using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.API.DTOs;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BanksController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<BanksController> _logger;
    private Guid TenantId => _tenantContext.RequiredTenantId;

    public BanksController(ApplicationDbContext context, ITenantContext tenantContext, ILogger<BanksController> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<BankDto>>> GetBanks(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        var tenantId = TenantId;
        var query = _context.Banks.Where(b => b.TenantId == tenantId && !b.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.ToLower();
            query = query.Where(b =>
                b.BankName.ToLower().Contains(search) ||
                (b.AccountNumber != null && b.AccountNumber.Contains(search))
            );
        }

        var totalCount = await query.CountAsync();

        var banks = await query
            .OrderBy(b => b.BankName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new BankDto(
                b.Id,
                b.BankName,
                b.BranchName,
                b.AccountNumber,
                b.IBAN,
                b.Notes,
                b.IsActive,
                b.OpeningBalanceAmount,
                b.OpeningBalanceDate
            ))
            .ToListAsync();

        return Ok(new PagedResult<BankDto>
        {
            Items = banks,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<BankDto>> GetBank(Guid id)
    {
        var tenantId = TenantId;
        var bank = await _context.Banks
            .Where(b => b.Id == id && b.TenantId == tenantId && !b.IsDeleted)
            .Select(b => new BankDto(
                b.Id,
                b.BankName,
                b.BranchName,
                b.AccountNumber,
                b.IBAN,
                b.Notes,
                b.IsActive,
                b.OpeningBalanceAmount,
                b.OpeningBalanceDate
            ))
            .FirstOrDefaultAsync();

        if (bank == null)
            return NotFound();

        return Ok(bank);
    }

    [HttpPost]
    public async Task<ActionResult<BankDto>> CreateBank(CreateBankDto request)
    {
        var tenantId = TenantId;
        if ((request.OpeningBalanceAmount ?? 0m) != 0m)
        {
            return BadRequest(new
            {
                message = "Bank opening balances must be posted through ledger-backed treasury operations."
            });
        }

        if (await _context.Banks.AnyAsync(b => b.TenantId == tenantId && b.BankName == request.BankName && !b.IsDeleted))
        {
            return BadRequest(new { message = "A bank with this name already exists" });
        }

        var bank = new Bank
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,  // Multi-tenant
            BankName = request.BankName,
            BranchName = request.BranchName,
            AccountNumber = request.AccountNumber,
            IBAN = request.IBAN,
            Notes = request.Notes,
            IsActive = request.IsActive,
            OpeningBalanceAmount = null,
            OpeningBalanceDate = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Banks.Add(bank);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Bank created: {BankId} - {BankName}", bank.Id, bank.BankName);

        var bankDto = new BankDto(
            bank.Id,
            bank.BankName,
            bank.BranchName,
            bank.AccountNumber,
            bank.IBAN,
            bank.Notes,
            bank.IsActive,
            bank.OpeningBalanceAmount,
            bank.OpeningBalanceDate
        );

        return CreatedAtAction(nameof(GetBank), new { id = bank.Id }, bankDto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBank(Guid id, UpdateBankDto request)
    {
        var tenantId = TenantId;
        var bank = await _context.Banks
            .FirstOrDefaultAsync(b => b.Id == id && b.TenantId == tenantId && !b.IsDeleted);
        if (bank == null)
            return NotFound();

        if (await _context.Banks.AnyAsync(b => b.TenantId == tenantId && b.BankName == request.BankName && b.Id != id && !b.IsDeleted))
        {
            return BadRequest(new { message = "A bank with this name already exists" });
        }

        bank.BankName = request.BankName;
        bank.BranchName = request.BranchName;
        bank.AccountNumber = request.AccountNumber;
        bank.IBAN = request.IBAN;
        bank.Notes = request.Notes;
        bank.IsActive = request.IsActive;
        bank.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBank(Guid id)
    {
        var tenantId = TenantId;
        var bank = await _context.Banks
            .FirstOrDefaultAsync(b => b.Id == id && b.TenantId == tenantId && !b.IsDeleted);
        if (bank == null)
            return NotFound();

        if (bank.IsActive)
        {
            return BadRequest(new { message = "Cannot delete an active bank. Deactivate it first." });
        }

        var hasTransactions = await _context.BankTransactions
            .AnyAsync(transaction =>
                transaction.TenantId == tenantId &&
                transaction.BankId == bank.Id);

        if (hasTransactions || (bank.OpeningBalanceAmount.HasValue && bank.OpeningBalanceAmount != 0m))
        {
            return BadRequest(new { message = "Cannot delete bank with financial history" });
        }

        bank.IsDeleted = true;
        bank.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Bank deleted: {BankId} - {BankName}", bank.Id, bank.BankName);

        return NoContent();
    }

    [HttpPatch("{id}/toggle-status")]
    public async Task<IActionResult> ToggleStatus(Guid id)
    {
        var tenantId = TenantId;
        var bank = await _context.Banks
            .FirstOrDefaultAsync(b => b.Id == id && b.TenantId == tenantId && !b.IsDeleted);
        if (bank == null)
            return NotFound();

        bank.IsActive = !bank.IsActive;
        bank.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { isActive = bank.IsActive });
    }

    [HttpGet("opening-balances")]
    public async Task<ActionResult<IEnumerable<BankDto>>> GetOpeningBalances()
    {
        var tenantId = TenantId;
        var banks = await _context.Banks
            .Where(b => b.TenantId == tenantId && !b.IsDeleted && b.IsActive)
            .OrderBy(b => b.BankName)
            .Select(b => new BankDto(
                b.Id,
                b.BankName,
                b.BranchName,
                b.AccountNumber,
                b.IBAN,
                b.Notes,
                b.IsActive,
                b.OpeningBalanceAmount,
                b.OpeningBalanceDate
            ))
            .ToListAsync();

        return Ok(banks);
    }

    [HttpPost("statement")]
    public async Task<ActionResult<BankStatementResponseDto>> GetStatement(BankStatementRequestDto request)
    {
        var tenantId = TenantId;
        var bankExists = await _context.Banks.AnyAsync(b =>
            b.Id == request.BankId &&
            b.TenantId == tenantId &&
            !b.IsDeleted);

        if (!bankExists)
            return NotFound();

        // 1. Calculate Initial Balance (before FromDate)
        decimal initialBalance = 0;
        if (request.FromDate.HasValue)
        {
            var previousTransactions = await _context.BankTransactions
                .Where(t => t.TenantId == tenantId &&
                            t.BankId == request.BankId &&
                            t.TransactionDate < DateTime.SpecifyKind(request.FromDate.Value, DateTimeKind.Utc))
                .ToListAsync();
            
            foreach(var t in previousTransactions)
            {
                 if (t.TransactionType == 1 || t.TransactionType == 3 || t.TransactionType == 5) // Deposit, TransferIn, Opening
                     initialBalance += t.Amount;
                 else // Withdrawal, TransferOut
                     initialBalance -= t.Amount;
            }
        }

        // 2. Fetch Transactions in Range
        var query = _context.BankTransactions
            .Where(t => t.TenantId == tenantId && t.BankId == request.BankId);

        if (request.FromDate.HasValue)
            query = query.Where(t => t.TransactionDate >= DateTime.SpecifyKind(request.FromDate.Value, DateTimeKind.Utc));
        
        if (request.ToDate.HasValue)
            query = query.Where(t => t.TransactionDate <= DateTime.SpecifyKind(request.ToDate.Value, DateTimeKind.Utc));

        if (request.TransactionType.HasValue && request.TransactionType.Value > 0)
            query = query.Where(t => t.TransactionType == request.TransactionType.Value);

        var transactions = await query
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();

        // 3. Build Result List
        var resultItems = new List<BankStatementItemDto>();
        decimal runningBalance = initialBalance;
        
        decimal totalDeposits = 0;
        decimal totalWithdrawals = 0;

        // Add "Balance Brought Forward" row if FromDate is set and IncludeOpeningBalance is true
        if (request.IncludeOpeningBalance && request.FromDate.HasValue)
        {
             resultItems.Add(new BankStatementItemDto(
                 request.FromDate.Value,
                 "Balance Brought Forward",
                 "",
                 "Opening Balance",
                 initialBalance > 0 ? initialBalance : 0,
                 initialBalance < 0 ? -initialBalance : 0,
                 initialBalance,
                 "OpeningBalance"
             ));
        }

        foreach (var t in transactions)
        {
            decimal debit = 0;
            decimal credit = 0;
            string rowType = "Default";
            string typeStr = "";

            switch (t.TransactionType)
            {
                case 1: // Deposit
                    debit = t.Amount;
                    runningBalance += t.Amount;
                    totalDeposits += t.Amount;
                    rowType = "Deposit";
                    typeStr = "Deposit";
                    break;
                case 2: // Withdrawal
                    credit = t.Amount;
                    runningBalance -= t.Amount;
                    totalWithdrawals += t.Amount;
                    rowType = "Withdrawal";
                    typeStr = "Withdrawal";
                    break;
                case 3: // TransferIn
                    debit = t.Amount;
                    runningBalance += t.Amount;
                    totalDeposits += t.Amount;
                    rowType = "Transfer";
                    typeStr = "Transfer In";
                    break;
                case 4: // TransferOut
                    credit = t.Amount;
                    runningBalance -= t.Amount;
                    totalWithdrawals += t.Amount;
                    rowType = "Transfer";
                    typeStr = "Transfer Out";
                    break;
                case 5: // OpeningBalance
                    debit = t.Amount;
                    runningBalance += t.Amount;
                    // Opening balance is not included in period totals usually
                    rowType = "OpeningBalance";
                    typeStr = "Opening Balance";
                    break;
                default:
                    typeStr = "Unknown";
                    break;
            }

            resultItems.Add(new BankStatementItemDto(
                t.TransactionDate,
                typeStr,
                t.ReferenceNumber,
                t.Description ?? t.Notes,
                debit,
                credit,
                runningBalance,
                rowType
            ));
        }

        var summary = new BankStatementSummaryDto(
            totalDeposits,
            totalWithdrawals,
            totalDeposits - totalWithdrawals,
            runningBalance
        );

        return Ok(new BankStatementResponseDto(resultItems, summary));
    }

    [HttpPost("opening-balances")]
    public async Task<IActionResult> SaveOpeningBalances(SaveBankOpeningBalancesRequest request)
    {
        await Task.CompletedTask;
        return StatusCode(StatusCodes.Status410Gone, new
        {
            message = "Legacy bank opening-balance writes are disabled. Use ledger-backed treasury operations."
        });
    }
}
