using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Treasury;

/// <summary>
/// PHASE TOJ: Treasury Operations Journaling Service Implementation
/// 
/// This service routes ALL treasury money mutations through the Ledger.
/// Every operation creates a balanced, auditable journal entry.
/// 
/// ⚠️ NO DIRECT DATABASE WRITES TO LEGACY TABLES
/// 
/// Idempotency Keys:
/// - TREASURY_DEPOSIT:{tenantId}:{operationId}
/// - TREASURY_WITHDRAW:{tenantId}:{operationId}
/// - TREASURY_TRANSFER:{tenantId}:{operationId}
/// - TREASURY_EXPENSE:{tenantId}:{operationId}
/// - TREASURY_CASHIN:{tenantId}:{operationId}
/// - TREASURY_CASHOUT:{tenantId}:{operationId}
/// </summary>
public class TreasuryOperationService : ITreasuryOperationService
{
    private readonly IAccountResolver _accountResolver;
    private readonly IFinancialPostingService _postingService;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<TreasuryOperationService> _logger;

    // Source Types for journal entries
    private const string SOURCE_TYPE_DEPOSIT = "TreasuryDeposit";
    private const string SOURCE_TYPE_WITHDRAW = "TreasuryWithdraw";
    private const string SOURCE_TYPE_TRANSFER = "TreasuryTransfer";
    private const string SOURCE_TYPE_EXPENSE = "TreasuryExpense";
    private const string SOURCE_TYPE_CASHIN = "TreasuryCashIn";
    private const string SOURCE_TYPE_CASHOUT = "TreasuryCashOut";

    public TreasuryOperationService(
        IAccountResolver accountResolver,
        IFinancialPostingService postingService,
        ApplicationDbContext db,
        ILogger<TreasuryOperationService> logger)
    {
        _accountResolver = accountResolver;
        _postingService = postingService;
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<JournalEntry> DepositAsync(
        Guid tenantId,
        string sourceAccountCode,
        string targetAccountCode,
        decimal amount,
        string description,
        string? reference = null,
        Guid? operationId = null,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        ValidateAmount(amount);
        
        var opId = operationId ?? Guid.NewGuid();
        var idempotencyKey = $"TREASURY_DEPOSIT:{tenantId}:{opId}";
        
        _logger.LogInformation(
            "Phase TOJ: Deposit {Amount} from {Source} to {Target} [IdempotencyKey: {Key}]",
            amount, sourceAccountCode, targetAccountCode, idempotencyKey);

        // Resolve accounts
        var accounts = await _accountResolver.GetByCodesAsync(
            tenantId, 
            new[] { sourceAccountCode, targetAccountCode }, 
            ct);

        var sourceAccount = accounts[sourceAccountCode];
        var targetAccount = accounts[targetAccountCode];
        if (sourceAccount.Id == targetAccount.Id)
            throw new TreasuryValidationException("Source and destination treasury accounts must be different");

        await EnsureTreasuryAccountAsync(tenantId, sourceAccount, SOURCE_TYPE_DEPOSIT, ct);
        await EnsureTreasuryAccountAsync(tenantId, targetAccount, SOURCE_TYPE_DEPOSIT, ct);

        // Build posting request
        // Deposit: Money goes INTO target (DEBIT), comes FROM source (CREDIT)
        var request = new PostingRequest
        {
            TenantId = tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = SOURCE_TYPE_DEPOSIT,
            SourceId = opId,
            SourceReference = reference,
            Description = description,
            IdempotencyKey = idempotencyKey,
            UserId = userId,
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(targetAccount.Id, amount, $"Deposit to {targetAccount.Name}"),
                PostingLine.Credit(sourceAccount.Id, amount, $"Deposit from {sourceAccount.Name}")
            }
        };

        var journal = await _postingService.PostJournalAsync(request, ct);
        
        _logger.LogInformation(
            "Phase TOJ: Deposit completed. Journal #{JournalNumber}, ID: {JournalId}",
            journal.JournalNumber, journal.Id);

        return journal;
    }

    /// <inheritdoc />
    public async Task<JournalEntry> WithdrawAsync(
        Guid tenantId,
        string sourceAccountCode,
        string targetAccountCode,
        decimal amount,
        string description,
        string? reference = null,
        Guid? operationId = null,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        ValidateAmount(amount);
        
        var opId = operationId ?? Guid.NewGuid();
        var idempotencyKey = $"TREASURY_WITHDRAW:{tenantId}:{opId}";
        
        _logger.LogInformation(
            "Phase TOJ: Withdraw {Amount} from {Source} to {Target} [IdempotencyKey: {Key}]",
            amount, sourceAccountCode, targetAccountCode, idempotencyKey);

        var accounts = await _accountResolver.GetByCodesAsync(
            tenantId, 
            new[] { sourceAccountCode, targetAccountCode }, 
            ct);

        var sourceAccount = accounts[sourceAccountCode];
        var targetAccount = accounts[targetAccountCode];
        if (sourceAccount.Id == targetAccount.Id)
            throw new TreasuryValidationException("Source and destination treasury accounts must be different");

        await EnsureTreasuryAccountAsync(tenantId, sourceAccount, SOURCE_TYPE_WITHDRAW, ct);
        await EnsureTreasuryAccountAsync(tenantId, targetAccount, SOURCE_TYPE_WITHDRAW, ct);

        // Withdraw: Money comes FROM source (CREDIT), goes INTO target (DEBIT)
        var request = new PostingRequest
        {
            TenantId = tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = SOURCE_TYPE_WITHDRAW,
            SourceId = opId,
            SourceReference = reference,
            Description = description,
            IdempotencyKey = idempotencyKey,
            UserId = userId,
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(targetAccount.Id, amount, $"Withdrawal to {targetAccount.Name}"),
                PostingLine.Credit(sourceAccount.Id, amount, $"Withdrawal from {sourceAccount.Name}")
            }
        };

        var journal = await _postingService.PostJournalAsync(request, ct);
        
        _logger.LogInformation(
            "Phase TOJ: Withdraw completed. Journal #{JournalNumber}, ID: {JournalId}",
            journal.JournalNumber, journal.Id);

        return journal;
    }

    /// <inheritdoc />
    public async Task<JournalEntry> TransferAsync(
        Guid tenantId,
        string fromAccountCode,
        string toAccountCode,
        decimal amount,
        string description,
        string? reference = null,
        Guid? operationId = null,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        ValidateAmount(amount);
        
        var opId = operationId ?? Guid.NewGuid();
        var idempotencyKey = $"TREASURY_TRANSFER:{tenantId}:{opId}";
        
        _logger.LogInformation(
            "Phase TOJ: Transfer {Amount} from {From} to {To} [IdempotencyKey: {Key}]",
            amount, fromAccountCode, toAccountCode, idempotencyKey);

        var accounts = await _accountResolver.GetByCodesAsync(
            tenantId, 
            new[] { fromAccountCode, toAccountCode }, 
            ct);

        var fromAccount = accounts[fromAccountCode];
        var toAccount = accounts[toAccountCode];
        if (fromAccount.Id == toAccount.Id)
            throw new TreasuryValidationException("Source and destination treasury accounts must be different");

        await EnsureTreasuryAccountAsync(tenantId, fromAccount, SOURCE_TYPE_TRANSFER, ct);
        await EnsureTreasuryAccountAsync(tenantId, toAccount, SOURCE_TYPE_TRANSFER, ct);

        // Transfer: Money goes INTO toAccount (DEBIT), comes FROM fromAccount (CREDIT)
        var request = new PostingRequest
        {
            TenantId = tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = SOURCE_TYPE_TRANSFER,
            SourceId = opId,
            SourceReference = reference,
            Description = description,
            IdempotencyKey = idempotencyKey,
            UserId = userId,
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(toAccount.Id, amount, $"Transfer to {toAccount.Name}"),
                PostingLine.Credit(fromAccount.Id, amount, $"Transfer from {fromAccount.Name}")
            }
        };

        var journal = await _postingService.PostJournalAsync(request, ct);
        
        _logger.LogInformation(
            "Phase TOJ: Transfer completed. Journal #{JournalNumber}, ID: {JournalId}",
            journal.JournalNumber, journal.Id);

        return journal;
    }

    /// <inheritdoc />
    public async Task<JournalEntry> RecordExpenseAsync(
        Guid tenantId,
        string paidFromAccountCode,
        string expenseAccountCode,
        decimal amount,
        string description,
        string? reference = null,
        Guid? operationId = null,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        await Task.CompletedTask;
        throw new TreasuryValidationException(
            "Direct treasury expense posting is disabled. Outflows must originate from an approved SQL expense or other certified liability workflow.");
    }

    /// <inheritdoc />
    public async Task<JournalEntry> CashInAsync(
        Guid tenantId,
        string targetAccountCode,
        string creditAccountCode,
        decimal amount,
        string description,
        string? reference = null,
        Guid? operationId = null,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        await Task.CompletedTask;
        throw new TreasuryValidationException(
            "Direct treasury cash-in is disabled. Inflows must originate from certified payment collection or accounting settlement authority.");
    }

    /// <inheritdoc />
    public async Task<JournalEntry> CashOutAsync(
        Guid tenantId,
        string sourceAccountCode,
        string debitAccountCode,
        decimal amount,
        string description,
        string? reference = null,
        Guid? operationId = null,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        await Task.CompletedTask;
        throw new TreasuryValidationException(
            "Direct treasury cash-out is disabled. Outflows must originate from certified liability, payout, refund, or approved expense authority.");
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0)
            throw new TreasuryValidationException("Amount must be positive");
    }

    private async Task EnsureTreasuryAccountAsync(
        Guid tenantId,
        Account account,
        string operationType,
        CancellationToken ct)
    {
        var isTreasuryAccount = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(treasuryAccount =>
                treasuryAccount.TenantId == tenantId &&
                treasuryAccount.AccountId == account.Id &&
                treasuryAccount.IsActive &&
                !treasuryAccount.IsDeleted &&
                (treasuryAccount.Type == TreasuryAccountType.Cashbox ||
                 treasuryAccount.Type == TreasuryAccountType.Bank) &&
                treasuryAccount.Account != null &&
                treasuryAccount.Account.TenantId == tenantId &&
                treasuryAccount.Account.AccountType == AccountType.Asset &&
                treasuryAccount.Account.IsActive,
                ct);

        if (!isTreasuryAccount)
        {
            throw new TreasuryValidationException(
                $"{operationType} requires active same-tenant cashbox or bank treasury account authority for account '{account.AccountCode}'");
        }
    }

}
