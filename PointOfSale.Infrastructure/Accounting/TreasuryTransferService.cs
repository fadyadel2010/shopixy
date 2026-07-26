using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>
/// Handles treasury operations: transfers between accounts.
/// 
/// All transfers create proper journal entries via FinancialPostingService.
/// Idempotency is enforced via transfer reference.
/// </summary>
public class TreasuryTransferService : ITreasuryTransferService
{
    private readonly IFinancialPostingService _postingService;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<TreasuryTransferService> _logger;
    
    // Source types for journal entries
    private const string SourceTypeTreasuryTransfer = "TreasuryTransfer";
    private const string SourceTypeClearingSettlement = "ClearingSettlement";
    private const string SourceTypeBankDeposit = "BankDeposit";
    
    public TreasuryTransferService(
        IFinancialPostingService postingService,
        ApplicationDbContext db,
        ILogger<TreasuryTransferService> logger)
    {
        _postingService = postingService;
        _db = db;
        _logger = logger;
    }
    
    /// <inheritdoc />
    public async Task<Guid> TransferAsync(
        Guid tenantId,
        Guid fromAccountId,
        Guid toAccountId,
        decimal amount,
        string reference,
        Guid? transferId = null,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new TreasuryValidationException("Transfer amount must be positive");
        
        if (fromAccountId == toAccountId)
            throw new TreasuryValidationException("Cannot transfer to the same account");

        await RequireTreasuryTypeAsync(tenantId, fromAccountId,
            new[] { TreasuryAccountType.Cashbox, TreasuryAccountType.Bank }, "transfer source", ct);
        await RequireTreasuryTypeAsync(tenantId, toAccountId,
            new[] { TreasuryAccountType.Cashbox, TreasuryAccountType.Bank }, "transfer destination", ct);
        
        var id = transferId ?? Guid.NewGuid();
        var idempotencyKey = $"TRANSFER:{tenantId}:{id}";
        
        _logger.LogInformation(
            "Processing treasury transfer {TransferId}: {Amount} from {From} to {To}",
            id, amount, fromAccountId, toAccountId);
        
        var request = new PostingRequest
        {
            TenantId = tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = SourceTypeTreasuryTransfer,
            SourceId = id,
            SourceReference = reference,
            Description = $"Treasury transfer: {reference}",
            IdempotencyKey = idempotencyKey,
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(toAccountId, amount, $"Transfer in: {reference}"),
                PostingLine.Credit(fromAccountId, amount, $"Transfer out: {reference}")
            }
        };
        
        var journal = await _postingService.PostJournalAsync(request, ct);
        
        _logger.LogInformation(
            "Treasury transfer {TransferId} completed. Journal: {JournalNumber}",
            id, journal.JournalNumber);
        
        return journal.Id;
    }
    
    /// <inheritdoc />
    public async Task<Guid> SettleClearingAsync(
        Guid tenantId,
        Guid clearingAccountId,
        Guid targetAccountId,
        decimal amount,
        string reference,
        Guid? settlementId = null,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new TreasuryValidationException("Settlement amount must be positive");
        
        if (clearingAccountId == targetAccountId)
            throw new TreasuryValidationException("Cannot transfer to the same account");

        await RequireTreasuryTypeAsync(tenantId, clearingAccountId,
            new[] { TreasuryAccountType.Clearing }, "settlement clearing source", ct);
        await RequireTreasuryTypeAsync(tenantId, targetAccountId,
            new[] { TreasuryAccountType.Bank }, "settlement bank destination", ct);
        
        var id = settlementId ?? Guid.NewGuid();
        var idempotencyKey = $"SETTLEMENT:{tenantId}:{id}";
        
        _logger.LogInformation(
            "Processing clearing settlement {SettlementId}: {Amount} from clearing {Clearing} to {Target}",
            id, amount, clearingAccountId, targetAccountId);
        
        var request = new PostingRequest
        {
            TenantId = tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = SourceTypeClearingSettlement,
            SourceId = id,
            SourceReference = reference,
            Description = $"Clearing settlement: {reference}",
            IdempotencyKey = idempotencyKey,
            Lines = new List<PostingLine>
            {
                // DEBIT target (bank - receiving money)
                PostingLine.Debit(targetAccountId, amount, $"Settlement received: {reference}"),
                // CREDIT clearing (reducing clearing balance)
                PostingLine.Credit(clearingAccountId, amount, $"Settlement to bank: {reference}")
            }
        };
        
        var journal = await _postingService.PostJournalAsync(request, ct);
        
        _logger.LogInformation(
            "Clearing settlement {SettlementId} completed. Journal: {JournalNumber}",
            id, journal.JournalNumber);
        
        return journal.Id;
    }
    
    /// <inheritdoc />
    public async Task<Guid> DepositToBankAsync(
        Guid tenantId,
        Guid cashboxAccountId,
        Guid bankAccountId,
        decimal amount,
        string reference,
        Guid? depositId = null,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new TreasuryValidationException("Deposit amount must be positive");
        
        if (cashboxAccountId == bankAccountId)
            throw new TreasuryValidationException("Cannot transfer to the same account");

        await RequireTreasuryTypeAsync(tenantId, cashboxAccountId,
            new[] { TreasuryAccountType.Cashbox }, "deposit cashbox source", ct);
        await RequireTreasuryTypeAsync(tenantId, bankAccountId,
            new[] { TreasuryAccountType.Bank }, "deposit bank destination", ct);
        
        var id = depositId ?? Guid.NewGuid();
        var idempotencyKey = $"DEPOSIT:{tenantId}:{id}";
        
        _logger.LogInformation(
            "Processing bank deposit {DepositId}: {Amount} from cashbox {Cashbox} to bank {Bank}",
            id, amount, cashboxAccountId, bankAccountId);
        
        var request = new PostingRequest
        {
            TenantId = tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = SourceTypeBankDeposit,
            SourceId = id,
            SourceReference = reference,
            Description = $"Bank deposit: {reference}",
            IdempotencyKey = idempotencyKey,
            Lines = new List<PostingLine>
            {
                // DEBIT bank (receiving money)
                PostingLine.Debit(bankAccountId, amount, $"Deposit from cashbox: {reference}"),
                // CREDIT cashbox (cash going out)
                PostingLine.Credit(cashboxAccountId, amount, $"Deposit to bank: {reference}")
            }
        };
        
        var journal = await _postingService.PostJournalAsync(request, ct);
        
        _logger.LogInformation(
            "Bank deposit {DepositId} completed. Journal: {JournalNumber}",
            id, journal.JournalNumber);
        
        return journal.Id;
    }

    private async Task RequireTreasuryTypeAsync(
        Guid tenantId,
        Guid accountId,
        IReadOnlyCollection<TreasuryAccountType> allowedTypes,
        string role,
        CancellationToken ct)
    {
        var valid = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(account =>
                account.TenantId == tenantId &&
                account.AccountId == accountId &&
                account.IsActive &&
                !account.IsDeleted &&
                allowedTypes.Contains(account.Type) &&
                account.Account != null &&
                account.Account.TenantId == tenantId &&
                account.Account.AccountType == AccountType.Asset &&
                account.Account.IsActive,
                ct);

        if (!valid)
        {
            throw new TreasuryValidationException(
                $"Account '{accountId}' lacks active same-tenant treasury authority for {role}.");
        }
    }
}
