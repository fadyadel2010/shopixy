using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Data;
using System.Data;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>
/// Production-grade double-entry posting engine.
/// 
/// Guarantees:
/// - Atomic transactions (all or nothing)
/// - Idempotent operations (safe to retry)
/// - Serializable isolation (no race conditions)
/// - Immutable journals (no updates/deletes)
/// - Balanced entries (debits = credits)
/// - Negative balance prevention (for flagged accounts)
/// </summary>
public class FinancialPostingService : IFinancialPostingService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<FinancialPostingService> _logger;
    
    public FinancialPostingService(
        ApplicationDbContext db,
        ILogger<FinancialPostingService> logger)
    {
        _db = db;
        _logger = logger;
    }
    
    /// <inheritdoc />
    public async Task<JournalEntry> PostJournalAsync(
        PostingRequest request,
        CancellationToken ct = default)
    {
        return await PostJournalAsync(request, reversalAuthorized: false, ct: ct);
    }

    private async Task<JournalEntry> PostJournalAsync(
        PostingRequest request,
        bool reversalAuthorized,
        CancellationToken ct)
    {
        // ═══════════════════════════════════════════════════════════════════
        // STEP 1: IDEMPOTENCY CHECK (before transaction)
        // ═══════════════════════════════════════════════════════════════════
        // NOTE: Must use IgnoreQueryFilters() because this may be called from background
        // processors (outbox worker) that don't have tenant context set
        var existingJournal = await _db.JournalEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(j => j.LedgerEntries)
            .FirstOrDefaultAsync(j =>
                j.TenantId == request.TenantId &&
                j.IdempotencyKey == request.IdempotencyKey,
                ct);
        
        if (existingJournal != null)
        {
            EnsureIdempotentRequestMatches(existingJournal, request);
            _logger.LogInformation(
                "Idempotent posting: returning existing journal {JournalNumber} for key {Key}",
                existingJournal.JournalNumber,
                request.IdempotencyKey);
            return existingJournal;
        }
        
        // ═══════════════════════════════════════════════════════════════════
        // STEP 2: VALIDATION (before transaction)
        // ═══════════════════════════════════════════════════════════════════
        await ValidateJournalAsync(request, reversalAuthorized, ct);
        
        // ═══════════════════════════════════════════════════════════════════
        // STEP 3: BEGIN SERIALIZABLE TRANSACTION (if not already in one)
        // ═══════════════════════════════════════════════════════════════════
        var ownsTransaction = _db.Database.CurrentTransaction == null && _db.Database.IsRelational();
        var strategy = ownsTransaction ? _db.Database.CreateExecutionStrategy() : null;
        
        return await (strategy?.ExecuteAsync(async () => await PostJournalInternalAsync(request, ownsTransaction, ct)) 
                      ?? PostJournalInternalAsync(request, ownsTransaction, ct));
    }

    private async Task<JournalEntry> PostJournalInternalAsync(
        PostingRequest request,
        bool ownsTransaction,
        CancellationToken ct)
    {
        using var mutationScope = LedgerMutationScope.Authorize();
        await using var transaction = ownsTransaction
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        
        try
        {
            // ═══════════════════════════════════════════════════════════
            // STEP 4: RE-CHECK IDEMPOTENCY (inside transaction)
            // This handles race condition where another thread inserted
            // ═══════════════════════════════════════════════════════════
            var existing = await _db.JournalEntries
                .IgnoreQueryFilters()
                .Include(j => j.LedgerEntries)
                .FirstOrDefaultAsync(j =>
                    j.TenantId == request.TenantId &&
                    j.IdempotencyKey == request.IdempotencyKey,
                    ct);
            
            if (existing != null)
            {
                EnsureIdempotentRequestMatches(existing, request);
                _logger.LogInformation(
                    "Idempotent posting (race): returning existing journal {JournalNumber}",
                    existing.JournalNumber);
                return existing;
            }
            
            // ═══════════════════════════════════════════════════════════
            // STEP 5: LOCK AND LOAD ACCOUNTS (ordered by ID to prevent deadlocks)
            // ═══════════════════════════════════════════════════════════
            var accountIds = request.Lines
                .Select(l => l.AccountId)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            
            var accounts = await _db.LedgerAccounts
                .IgnoreQueryFilters()
                .Where(a => accountIds.Contains(a.Id) && a.TenantId == request.TenantId)
                .OrderBy(a => a.Id)
                .ToListAsync(ct);
            
            if (accounts.Count != accountIds.Count)
            {
                var foundIds = accounts.Select(a => a.Id).ToHashSet();
                var missingIds = accountIds.Where(id => !foundIds.Contains(id)).ToList();
                throw new AccountNotFoundException($"Accounts not found: {string.Join(", ", missingIds)}");
            }
            
            var accountMap = accounts.ToDictionary(a => a.Id);
            
            // Check for inactive accounts
            var inactiveAccounts = accounts.Where(a => !a.IsActive).ToList();
            if (inactiveAccounts.Count != 0)
            {
                throw new InactiveAccountException(inactiveAccounts.Select(a => a.AccountCode));
            }

            await ValidateTreasuryInflowAuthorityAsync(request, accountIds, ct);
            await ValidateTreasuryOutflowAuthorityAsync(request, accountIds, ct);
            
            // ═══════════════════════════════════════════════════════════
            // STEP 6: VALIDATE PREVENT NEGATIVE (projected balances)
            // ═══════════════════════════════════════════════════════════
            ValidateNoNegativeBalances(request.Lines, accountMap);
            
            // ═══════════════════════════════════════════════════════════
            // STEP 7: GENERATE JOURNAL NUMBER
            // ═══════════════════════════════════════════════════════════
            var journalNumber = await GenerateJournalNumberAsync(request.TenantId, request.JournalDate, ct);
            
            // ═══════════════════════════════════════════════════════════
            // STEP 8: CREATE JOURNAL ENTRY
            // ═══════════════════════════════════════════════════════════
            var journal = JournalEntry.Create(
                request.TenantId,
                journalNumber,
                request.JournalDate,
                request.SourceType,
                request.SourceId,
                request.SourceReference,
                request.Description,
                request.IdempotencyKey,
                request.UserId);

            if (string.Equals(request.SourceType, "Reversal", StringComparison.Ordinal))
            {
                journal.MarkAsReversal(request.SourceId);
            }
            
            _db.JournalEntries.Add(journal);
            
            // ═══════════════════════════════════════════════════════════
            // STEP 9: CREATE LEDGER ENTRIES AND UPDATE BALANCES
            // ═══════════════════════════════════════════════════════════
            int lineNumber = 1;
            foreach (var line in request.Lines)
            {
                var account = accountMap[line.AccountId];
                
                // Apply entry to account balance
                account.ApplyEntry(line.DebitAmount, line.CreditAmount);
                
                // Create ledger entry with balance after
                LedgerEntry entry;
                if (line.DebitAmount > 0)
                {
                    entry = LedgerEntry.CreateDebit(
                        request.TenantId,
                        journal.Id,
                        account.Id,
                        line.DebitAmount,
                        account.CurrentBalance,
                        lineNumber++,
                        line.Description);
                }
                else
                {
                    entry = LedgerEntry.CreateCredit(
                        request.TenantId,
                        journal.Id,
                        account.Id,
                        line.CreditAmount,
                        account.CurrentBalance,
                        lineNumber++,
                        line.Description);
                }
                
                _db.LedgerEntries.Add(entry);
            }
            
            // ═══════════════════════════════════════════════════════════
            // STEP 10: SAVE AND COMMIT
            // ═══════════════════════════════════════════════════════════
            await _db.SaveChangesAsync(ct);
            if (ownsTransaction && transaction != null)
            {
                await transaction.CommitAsync(ct);
            }
            
            _logger.LogInformation(
                "Posted journal {JournalNumber} with {LineCount} entries, Key={Key}",
                journalNumber,
                request.Lines.Count,
                request.IdempotencyKey);
            
            return journal;
        }
        catch (Exception ex)
        {
            if (ownsTransaction && transaction != null)
            {
                await transaction.RollbackAsync(ct);
            }
            _logger.LogError(ex, "Failed to post journal for key {Key}", request.IdempotencyKey);
            throw;
        }
    }
    
    /// <inheritdoc />
    public async Task<JournalEntry> ReverseJournalAsync(
        Guid tenantId,
        Guid journalId,
        string reason,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reversal reason is required", nameof(reason));
        
        // Load original journal
        var original = await _db.JournalEntries
            .IgnoreQueryFilters()
            .Include(j => j.LedgerEntries)
            .FirstOrDefaultAsync(j => j.Id == journalId && j.TenantId == tenantId, ct);
        
        if (original == null)
            throw new JournalNotFoundException(journalId);
        
        if (original.Status == JournalStatus.Reversed)
            throw new JournalAlreadyReversedException(journalId);
        
        // Build reversal request with swapped debits/credits
        var reversalRequest = new PostingRequest
        {
            TenantId = tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Reversal",
            SourceId = journalId,
            SourceReference = original.JournalNumber,
            Description = $"Reversal of {original.JournalNumber}: {reason}",
            IdempotencyKey = $"REVERSE:{tenantId}:{journalId}",
            UserId = userId,
            Lines = original.LedgerEntries.Select(e => new PostingLine
            {
                AccountId = e.AccountId,
                DebitAmount = e.CreditAmount, // Swap: credit becomes debit
                CreditAmount = e.DebitAmount, // Swap: debit becomes credit
                Description = $"Reversal: {e.LineDescription}"
            }).ToList()
        };
        
        var ownsTransaction = _db.Database.CurrentTransaction == null && _db.Database.IsRelational();
        var strategy = ownsTransaction ? _db.Database.CreateExecutionStrategy() : null;

        return await (strategy?.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, ct);

            try
            {
                var result = await ReverseJournalInternalAsync(
                    tenantId,
                    journalId,
                    reason,
                    reversalRequest,
                    ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }) ?? ReverseJournalInternalAsync(
            tenantId,
            journalId,
            reason,
            reversalRequest,
            ct));
    }

    private async Task<JournalEntry> ReverseJournalInternalAsync(
        Guid tenantId,
        Guid journalId,
        string reason,
        PostingRequest reversalRequest,
        CancellationToken ct)
    {
        using var mutationScope = LedgerMutationScope.Authorize();
        var reversalJournal = await PostJournalAsync(
            reversalRequest,
            reversalAuthorized: true,
            ct: ct);

        var original = await _db.JournalEntries
            .IgnoreQueryFilters()
            .FirstAsync(j => j.Id == journalId && j.TenantId == tenantId, ct);

        if (original.Status != JournalStatus.Reversed)
        {
            original.MarkReversed(reversalJournal.Id, reason);
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Reversed journal {OriginalNumber} with reversal {ReversalNumber}",
            original.JournalNumber,
            reversalJournal.JournalNumber);

        return reversalJournal;
    }
    
    /// <inheritdoc />
    public async Task ValidateJournalAsync(PostingRequest request, CancellationToken ct = default)
    {
        await ValidateJournalAsync(request, reversalAuthorized: false, ct: ct);
    }

    private async Task ValidateJournalAsync(
        PostingRequest request,
        bool reversalAuthorized,
        CancellationToken ct)
    {
        if (request.TenantId == Guid.Empty)
        {
            throw new InvalidJournalException("TenantId is required");
        }

        if (request.SourceId == Guid.Empty)
        {
            throw new InvalidJournalException("SourceId is required");
        }

        if (string.Equals(request.SourceType, "Reversal", StringComparison.Ordinal) &&
            !reversalAuthorized)
        {
            throw new InvalidJournalException(
                "Reversal journals may only be created through ReverseJournalAsync.");
        }

        // Rule 1: Must have at least 2 lines
        if (request.Lines.Count < 2)
        {
            throw new InvalidJournalException(
                $"Journal must have at least 2 lines, got {request.Lines.Count}");
        }
        
        decimal totalDebits = 0;
        decimal totalCredits = 0;
        
        for (int i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            var lineNum = i + 1;
            
            // Rule 2: Exactly one side must be > 0
            bool hasDebit = line.DebitAmount > 0;
            bool hasCredit = line.CreditAmount > 0;
            
            if (!hasDebit && !hasCredit)
            {
                throw new InvalidJournalException(
                    $"Line {lineNum}: Must have either debit or credit > 0");
            }
            
            if (hasDebit && hasCredit)
            {
                throw new InvalidJournalException(
                    $"Line {lineNum}: Cannot have both debit and credit > 0");
            }
            
            // Rule 3: Amounts must be positive
            if (line.DebitAmount < 0)
            {
                throw new InvalidJournalException(
                    $"Line {lineNum}: Debit amount cannot be negative");
            }
            
            if (line.CreditAmount < 0)
            {
                throw new InvalidJournalException(
                    $"Line {lineNum}: Credit amount cannot be negative");
            }
            
            totalDebits += line.DebitAmount;
            totalCredits += line.CreditAmount;
        }
        
        // Rule 4: Must balance (debits == credits)
        if (totalDebits != totalCredits)
        {
            throw new UnbalancedJournalException(totalDebits, totalCredits);
        }
        
        // Rule 5: All accounts must exist and be active
        // NOTE: Must use IgnoreQueryFilters() because this may be called from background
        // processors (outbox worker) that don't have tenant context set
        var accountIds = request.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id) && a.TenantId == request.TenantId)
            .ToListAsync(ct);
        
        if (accounts.Count != accountIds.Count)
        {
            var foundIds = accounts.Select(a => a.Id).ToHashSet();
            var missingIds = accountIds.Where(id => !foundIds.Contains(id)).ToList();
            throw new AccountNotFoundException($"Accounts not found: {string.Join(", ", missingIds)}");
        }
        
        var inactiveAccounts = accounts.Where(a => !a.IsActive).ToList();
        if (inactiveAccounts.Count != 0)
        {
            throw new InactiveAccountException(inactiveAccounts.Select(a => a.AccountCode));
        }
        
        // Rule 6: Required fields
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new InvalidJournalException("IdempotencyKey is required");
        }
        
        if (string.IsNullOrWhiteSpace(request.SourceType))
        {
            throw new InvalidJournalException("SourceType is required");
        }
        
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            throw new InvalidJournalException("Description is required");
        }
        // Validate the dedicated source even if a substituted destination has no mapped cash inflow.
        if (request.SourceType == ManualCodSettlementAuthority.SourceType)
            await ValidateManualCodSettlementInflowAsync(request, ct);
        if (request.SourceType == ManualWalletSettlementAuthority.SourceType)
            await ValidateManualWalletSettlementInflowAsync(request, ct);
        if (request.SourceType == CarrierRemittanceAuthority.SourceType)
            await ValidateCarrierRemittanceInflowAsync(request, ct);
    }
    
    /// <inheritdoc />
    public async Task<JournalEntry?> GetJournalAsync(
        Guid tenantId,
        Guid journalId,
        CancellationToken ct = default)
    {
        return await _db.JournalEntries
            .Include(j => j.LedgerEntries)
                .ThenInclude(le => le.Account)
            .FirstOrDefaultAsync(j => j.Id == journalId && j.TenantId == tenantId, ct);
    }
    
    /// <inheritdoc />
    public async Task<JournalEntry?> GetJournalByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        return await _db.JournalEntries
            .Include(j => j.LedgerEntries)
            .FirstOrDefaultAsync(j => 
                j.TenantId == tenantId && 
                j.IdempotencyKey == idempotencyKey, 
                ct);
    }
    
    /// <inheritdoc />
    public async Task<bool> ExistsByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        return await _db.JournalEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(j => 
                j.TenantId == tenantId && 
                j.IdempotencyKey == idempotencyKey, 
                ct);
    }
    
    #region Private Helpers

    private static void EnsureIdempotentRequestMatches(
        JournalEntry existingJournal,
        PostingRequest request)
    {
        var existingLines = existingJournal.LedgerEntries
            .OrderBy(line => line.LineNumber)
            .Select(line => new
            {
                line.AccountId,
                line.DebitAmount,
                line.CreditAmount
            })
            .ToList();
        var requestedLines = request.Lines
            .Select(line => new
            {
                line.AccountId,
                line.DebitAmount,
                line.CreditAmount
            })
            .ToList();

        var identityMatches =
            existingJournal.TenantId == request.TenantId &&
            existingJournal.SourceType == request.SourceType &&
            existingJournal.SourceId == request.SourceId;

        if (!identityMatches || !existingLines.SequenceEqual(requestedLines))
        {
            throw new InvalidJournalException(
                $"Idempotency key '{request.IdempotencyKey}' is already bound to a different accounting posting.");
        }
    }

    
    /// <summary>
    /// Validates that no PreventNegative account will go negative.
    /// Must be called after loading accounts but before applying entries.
    /// </summary>
    private void ValidateNoNegativeBalances(
        List<PostingLine> lines,
        Dictionary<Guid, Account> accountMap)
    {
        // Group lines by account and calculate net effect
        var accountEffects = lines
            .GroupBy(l => l.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                NetDebit = g.Sum(l => l.DebitAmount),
                NetCredit = g.Sum(l => l.CreditAmount)
            });
        
        foreach (var effect in accountEffects)
        {
            var account = accountMap[effect.AccountId];
            
            if (!account.PreventNegative)
                continue;
            
            // Calculate projected balance
            decimal projectedBalance;
            if (account.NormalBalance == NormalBalance.Debit)
            {
                // Debit-normal: credits decrease balance
                projectedBalance = account.CurrentBalance + effect.NetDebit - effect.NetCredit;
            }
            else
            {
                // Credit-normal: debits decrease balance
                projectedBalance = account.CurrentBalance + effect.NetCredit - effect.NetDebit;
            }
            
            if (projectedBalance < 0)
            {
                var requestedAmount = account.NormalBalance == NormalBalance.Debit
                    ? effect.NetCredit
                    : effect.NetDebit;
                    
                throw new InsufficientFundsException(
                    account.AccountCode,
                    account.CurrentBalance,
                    requestedAmount);
            }
        }
    }

    private async Task ValidateTreasuryInflowAuthorityAsync(
        PostingRequest request,
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken ct)
    {
        var cashAccountIds = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(account =>
                account.TenantId == request.TenantId &&
                accountIds.Contains(account.AccountId) &&
                account.IsActive &&
                !account.IsDeleted &&
                (account.Type == TreasuryAccountType.Cashbox ||
                 account.Type == TreasuryAccountType.Bank))
            .Select(account => account.AccountId)
            .ToListAsync(ct);

        var netCashIncrease = request.Lines
            .Where(line => cashAccountIds.Contains(line.AccountId))
            .Sum(line => line.DebitAmount - line.CreditAmount);

        if (netCashIncrease <= 0m)
        {
            return;
        }

        var authorizedAmount = request.SourceType switch
        {
            "ManualCodSettlement" => await ValidateManualCodSettlementInflowAsync(request, ct),
            "ManualWalletSettlement" => await ValidateManualWalletSettlementInflowAsync(request, ct),
            "CarrierCashRemittance" => await ValidateCarrierRemittanceInflowAsync(request, ct),
            "SalesInvoice" => await _db.SalesInvoices
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(invoice =>
                    invoice.Id == request.SourceId &&
                    invoice.TenantId == request.TenantId &&
                    !invoice.IsDeleted &&
                    invoice.Status == InvoiceStatus.Completed &&
                    invoice.PaidAmount > 0m)
                .Select(invoice => (decimal?)invoice.PaidAmount)
                .FirstOrDefaultAsync(ct),
            "Order_Payment" => await _db.Orders
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(order =>
                    order.Id == request.SourceId &&
                    order.TenantId == request.TenantId &&
                    !order.IsDeleted &&
                    (order.PaymentStatus == PaymentStatus.Collected ||
                     order.PaymentStatus == PaymentStatus.Settled ||
                     order.PaymentStatus == PaymentStatus.PartiallyRefunded ||
                     order.PaymentStatus == PaymentStatus.Refunded))
                .Select(order => (decimal?)order.Total)
                .FirstOrDefaultAsync(ct),
            "CustomerPayment" => await _db.CustomerPayments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(payment =>
                    payment.Id == request.SourceId &&
                    payment.TenantId == request.TenantId &&
                    !payment.IsDeleted &&
                    payment.Amount > 0m)
                .Select(payment => (decimal?)payment.Amount)
                .FirstOrDefaultAsync(ct),
            "PurchaseReturn" => await ResolvePurchaseReturnCashAuthorityAsync(request, ct),
            "GatewaySettlement" => await _db.PaymentSettlementBatches
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(batch =>
                    batch.Id == request.SourceId &&
                    batch.TenantId == request.TenantId &&
                    !batch.IsDeleted &&
                    batch.SettlementType == "GATEWAY" &&
                    batch.SettlementId == request.SourceReference &&
                    batch.SettledAtUtc.Date == request.JournalDate.Date &&
                    batch.GrossAmount > 0m &&
                    batch.OrderCount > 0)
                .Select(batch => (decimal?)batch.GrossAmount)
                .FirstOrDefaultAsync(ct),
            "CODSettlement" => await _db.PaymentSettlementBatches
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(batch =>
                    batch.Id == request.SourceId &&
                    batch.TenantId == request.TenantId &&
                    !batch.IsDeleted &&
                    batch.SettlementType == "OFFLINE_COLLECTION" &&
                    batch.SettlementId == request.SourceReference &&
                    batch.SettledAtUtc.Date == request.JournalDate.Date &&
                    batch.GrossAmount > 0m &&
                    batch.OrderCount > 0)
                .Select(batch => (decimal?)batch.GrossAmount)
                .FirstOrDefaultAsync(ct),
            "CommerceHubFinancialTransaction" => await ResolveCommerceHubCashAuthorityAsync(
                request,
                CommerceHubFinancialTransactionDirection.Credit,
                ct),
            TreasuryInitialCapitalization.AccountingSourceType =>
                await ValidateInitialCapitalizationAuthorityAsync(request, cashAccountIds, ct),
            "Reversal" => await _db.LedgerEntries
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(entry =>
                    entry.TenantId == request.TenantId &&
                    entry.JournalEntryId == request.SourceId &&
                    cashAccountIds.Contains(entry.AccountId))
                .SumAsync(entry => (decimal?)(entry.CreditAmount - entry.DebitAmount), ct),
            _ => null
        };

        if (!authorizedAmount.HasValue || authorizedAmount.Value <= 0m)
        {
            throw new InvalidJournalException(
                $"Treasury inflow requires certified payment or accounting SQL authority; source '{request.SourceType}' is not authorized.");
        }

        var priorEntries = _db.LedgerEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry =>
                entry.TenantId == request.TenantId &&
                cashAccountIds.Contains(entry.AccountId) &&
                entry.JournalEntry.Status == JournalStatus.Posted &&
                entry.JournalEntry.SourceType == request.SourceType);

        priorEntries = request.SourceType is "GatewaySettlement" or "CODSettlement"
            ? priorEntries.Where(entry => entry.JournalEntry.SourceReference == request.SourceReference)
            : priorEntries.Where(entry => entry.JournalEntry.SourceId == request.SourceId);

        var previouslyPostedIncrease = await priorEntries
            .SumAsync(entry => (decimal?)(entry.DebitAmount - entry.CreditAmount), ct) ?? 0m;

        if (previouslyPostedIncrease + netCashIncrease > authorizedAmount.Value)
        {
            throw new InvalidJournalException(
                $"Treasury inflow exceeds authoritative source amount for '{request.SourceType}' and source '{request.SourceId}'.");
        }
    }

    private async Task<decimal?> ResolvePurchaseReturnCashAuthorityAsync(
        PostingRequest request,
        CancellationToken ct)
    {
        var returnRecord = await _db.Returns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(item => item.OriginalPurchaseInvoice)
                .ThenInclude(invoice => invoice!.Payments)
            .SingleOrDefaultAsync(item =>
                item.Id == request.SourceId &&
                item.TenantId == request.TenantId &&
                !item.IsDeleted &&
                item.ReturnType == ReturnType.PurchaseReturn &&
                item.OriginalPurchaseInvoiceId.HasValue &&
                item.RefundMethod == RefundMethod.Cash &&
                item.CashRefundAmount > 0m &&
                item.CashRefundAmount == item.TotalAmount &&
                item.CardRefundAmount == 0m &&
                item.CustomerCreditAmount == 0m,
                ct);
        var invoice = returnRecord?.OriginalPurchaseInvoice;
        if (returnRecord == null || invoice == null || invoice.IsDeleted)
        {
            return null;
        }

        var paidAmount = Math.Max(0m, Math.Min(
            RoundCurrency(invoice.PaidAmount),
            RoundCurrency(invoice.TotalAmount)));
        var cashPayments = RoundCurrency(invoice.Payments
            .Where(payment => !payment.IsDeleted && payment.PaymentMethod == PointOfSale.Core.Enums.PaymentMethod.Cash)
            .Sum(payment => payment.Amount));
        var originalCashPaid = cashPayments > 0m
            ? Math.Min(cashPayments, paidAmount)
            : invoice.PaymentMethod == PointOfSale.Core.Enums.PaymentMethod.Cash ? paidAmount : 0m;
        var cumulativeCashRefunds = await _db.Returns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item =>
                item.TenantId == request.TenantId &&
                !item.IsDeleted &&
                item.ReturnType == ReturnType.PurchaseReturn &&
                item.OriginalPurchaseInvoiceId == invoice.Id)
            .SumAsync(item => (decimal?)item.CashRefundAmount, ct) ?? 0m;

        return cumulativeCashRefunds <= originalCashPaid
            ? returnRecord.CashRefundAmount
            : null;
    }

    private async Task<decimal?> ValidateManualWalletSettlementInflowAsync(PostingRequest request, CancellationToken ct)
    {
        var authority = await ManualWalletSettlementAuthority.BuildPostingAsync(_db, request.TenantId, request.SourceId, ct);
        if (request.SourceReference != authority.SourceReference || request.IdempotencyKey != authority.IdempotencyKey ||
            request.UserId != authority.UserId || request.JournalDate != authority.JournalDate || request.Lines.Count != 2 ||
            !request.Lines.Select(l => (l.AccountId, l.DebitAmount, l.CreditAmount))
                .SequenceEqual(authority.Lines.Select(l => (l.AccountId, l.DebitAmount, l.CreditAmount))))
            throw new InvalidJournalException("Manual wallet settlement does not match its SQL source authority.");
        return authority.Lines[0].DebitAmount;
    }

    private async Task<decimal?> ValidateManualCodSettlementInflowAsync(PostingRequest request, CancellationToken ct)
    {
        var authority = await ManualCodSettlementAuthority.BuildPostingAsync(_db, request.TenantId, request.SourceId, ct);
        if (request.SourceReference != authority.SourceReference || request.IdempotencyKey != authority.IdempotencyKey ||
            request.UserId != authority.UserId || request.JournalDate != authority.JournalDate || request.Lines.Count != 2 ||
            !request.Lines.Select(l => (l.AccountId, l.DebitAmount, l.CreditAmount))
                .SequenceEqual(authority.Lines.Select(l => (l.AccountId, l.DebitAmount, l.CreditAmount))))
            throw new InvalidJournalException("Manual COD settlement does not match its SQL source authority.");
        return authority.Lines[0].DebitAmount;
    }

    private async Task<decimal?> ValidateCarrierRemittanceInflowAsync(PostingRequest request, CancellationToken ct)
    {
        var source = await CarrierRemittanceAuthority.BuildPostingAsync(_db, request.TenantId, request.SourceId, ct);
        if (request.SourceReference != source.SourceReference || request.IdempotencyKey != source.IdempotencyKey ||
            request.UserId != source.UserId || request.JournalDate != source.JournalDate || request.Lines.Count != 2 ||
            !request.Lines.Select(l => (l.AccountId, l.DebitAmount, l.CreditAmount))
                .SequenceEqual(source.Lines.Select(l => (l.AccountId, l.DebitAmount, l.CreditAmount))))
            throw new InvalidJournalException("Carrier remittance does not match dedicated SQL receipt authority.");
        return source.Lines[0].DebitAmount;
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private async Task<decimal?> ValidateInitialCapitalizationAuthorityAsync(
        PostingRequest request,
        IReadOnlyCollection<Guid> cashAccountIds,
        CancellationToken ct)
    {
        var document = await _db.TreasuryInitialCapitalizations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(item => item.Allocations)
            .SingleOrDefaultAsync(item =>
                item.Id == request.SourceId &&
                item.TenantId == request.TenantId,
                ct);
        if (document == null || document.IsDeleted ||
            document.Status != TreasuryInitialCapitalizationStatus.Draft ||
            document.TotalAmount <= 0m ||
            document.Allocations.Count == 0 ||
            document.Allocations.Sum(item => item.Amount) != document.TotalAmount ||
            request.UserId != document.CreatedByUserId ||
            request.JournalDate.Date != document.CutoverDate.Date ||
            !string.Equals(request.SourceReference, document.DocumentNumber, StringComparison.Ordinal) ||
            !string.Equals(
                request.IdempotencyKey,
                $"TREASURY_INITIAL_CAPITALIZATION:{request.TenantId}:{document.OperationId}",
                StringComparison.Ordinal))
        {
            return null;
        }

        var ownerIsCurrent = await _db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(user =>
                user.Id == document.CreatedByUserId &&
                user.TenantId == request.TenantId &&
                user.Role == UserRole.Owner &&
                user.IsActive &&
                !user.IsDeleted,
                ct);
        if (!ownerIsCurrent)
            return null;

        var openingEquity = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(account =>
                account.Id == document.OpeningEquityLedgerAccountId &&
                account.TenantId == request.TenantId,
                ct);
        if (openingEquity == null || !openingEquity.IsActive || openingEquity.IsDeleted ||
            openingEquity.AccountType != AccountType.Equity || openingEquity.IsClearing ||
            openingEquity.AccountCode != AccountCodes.OpeningBalanceEquity ||
            document.OpeningEquityAccountCodeSnapshot != AccountCodes.OpeningBalanceEquity)
        {
            return null;
        }

        var destinationIds = document.Allocations.Select(item => item.TreasuryAccountId).ToList();
        var destinations = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(item => item.Account)
            .Where(item => item.TenantId == request.TenantId && destinationIds.Contains(item.Id))
            .ToListAsync(ct);
        if (destinations.Count != destinationIds.Count)
            return null;

        foreach (var allocation in document.Allocations)
        {
            var destination = destinations.SingleOrDefault(item => item.Id == allocation.TreasuryAccountId);
            if (destination == null || !destination.IsActive || destination.IsDeleted ||
                destination.Type is not (TreasuryAccountType.Cashbox or TreasuryAccountType.Bank) ||
                destination.Type != allocation.TreasuryAccountTypeSnapshot ||
                destination.AccountId != allocation.LedgerAccountId ||
                !cashAccountIds.Contains(allocation.LedgerAccountId) ||
                destination.Account == null || destination.Account.TenantId != request.TenantId ||
                !destination.Account.IsActive || destination.Account.IsDeleted ||
                destination.Account.AccountType != AccountType.Asset || destination.Account.IsClearing ||
                destination.Account.AccountCode is "1020" or "1110" or "1040" or "2030" or "3010" ||
                destination.Account.AccountCode != allocation.LedgerAccountCodeSnapshot ||
                allocation.Amount <= 0m)
            {
                return null;
            }
        }

        var groupedLines = request.Lines
            .GroupBy(line => line.AccountId)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Debit = group.Sum(line => line.DebitAmount),
                    Credit = group.Sum(line => line.CreditAmount)
                });
        if (groupedLines.Count != document.Allocations.Count + 1 ||
            !groupedLines.TryGetValue(document.OpeningEquityLedgerAccountId, out var equityLine) ||
            equityLine.Debit != 0m || equityLine.Credit != document.TotalAmount)
        {
            return null;
        }

        foreach (var allocation in document.Allocations)
        {
            if (!groupedLines.TryGetValue(allocation.LedgerAccountId, out var line) ||
                line.Debit != allocation.Amount || line.Credit != 0m)
            {
                return null;
            }
        }

        var activeDocumentCount = await _db.TreasuryInitialCapitalizations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(item =>
                item.TenantId == request.TenantId &&
                !item.IsDeleted &&
                item.Status != TreasuryInitialCapitalizationStatus.Reversed,
                ct);
        if (activeDocumentCount != 1)
            return null;

        var priorDocuments = await _db.TreasuryInitialCapitalizations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == request.TenantId && item.Id != document.Id)
            .OrderBy(item => item.CreatedAtUtc)
            .ToListAsync(ct);
        if (priorDocuments.Count == 0 && document.PredecessorCapitalizationId.HasValue)
            return null;
        if (priorDocuments.Count > 0)
        {
            var predecessor = priorDocuments.SingleOrDefault(item => item.Id == document.PredecessorCapitalizationId);
            if (predecessor == null || predecessor.Status != TreasuryInitialCapitalizationStatus.Reversed ||
                predecessor.CutoverDate != document.CutoverDate || predecessor != priorDocuments[^1])
            {
                return null;
            }
        }

        var allTreasuryAccountIds = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == request.TenantId &&
                (item.Type == TreasuryAccountType.Cashbox || item.Type == TreasuryAccountType.Bank))
            .Select(item => item.AccountId)
            .Distinct()
            .ToListAsync(ct);
        var allowedJournalIds = priorDocuments
            .Where(item => item.Status == TreasuryInitialCapitalizationStatus.Reversed)
            .SelectMany(item => new[] { item.PostedJournalId, item.ReversalJournalId })
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToHashSet();
        var unrelatedMovementExists = await _db.LedgerEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(entry =>
                entry.TenantId == request.TenantId &&
                allTreasuryAccountIds.Contains(entry.AccountId) &&
                (entry.JournalEntry.Status == JournalStatus.Posted || entry.JournalEntry.Status == JournalStatus.Reversed) &&
                !allowedJournalIds.Contains(entry.JournalEntryId),
                ct);
        return unrelatedMovementExists ? null : document.TotalAmount;
    }

    private async Task ValidateTreasuryOutflowAuthorityAsync(
        PostingRequest request,
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken ct)
    {
        var cashAccountIds = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(account =>
                account.TenantId == request.TenantId &&
                accountIds.Contains(account.AccountId) &&
                account.IsActive &&
                !account.IsDeleted &&
                (account.Type == TreasuryAccountType.Cashbox ||
                 account.Type == TreasuryAccountType.Bank))
            .Select(account => account.AccountId)
            .ToListAsync(ct);

        var netCashDecrease = request.Lines
            .Where(line => cashAccountIds.Contains(line.AccountId))
            .Sum(line => line.CreditAmount - line.DebitAmount);

        if (netCashDecrease <= 0m)
        {
            return;
        }

        var authorizedAmount = request.SourceType switch
        {
            "PurchaseInvoice" => await _db.PurchaseInvoices
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(invoice =>
                    invoice.Id == request.SourceId &&
                    invoice.TenantId == request.TenantId &&
                    !invoice.IsDeleted &&
                    invoice.Status == InvoiceStatus.Completed &&
                    invoice.PaidAmount > 0m)
                .Select(invoice => (decimal?)invoice.PaidAmount)
                .FirstOrDefaultAsync(ct),
            "SupplierPayment" => await _db.SupplierPayments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(payment =>
                    payment.Id == request.SourceId &&
                    payment.TenantId == request.TenantId &&
                    !payment.IsDeleted &&
                    payment.Amount > 0m)
                .Select(payment => (decimal?)payment.Amount)
                .FirstOrDefaultAsync(ct),
            "SupplierCardFunding" => await _db.SupplierCardFundings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(funding =>
                    funding.Id == request.SourceId &&
                    funding.TenantId == request.TenantId &&
                    !funding.IsDeleted &&
                    funding.Amount > 0m &&
                    cashAccountIds.Contains(funding.FundingSourceLedgerAccountId))
                .Select(funding => (decimal?)funding.Amount)
                .FirstOrDefaultAsync(ct),
            "Return" => await _db.Returns
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(returnRecord =>
                    returnRecord.Id == request.SourceId &&
                    returnRecord.TenantId == request.TenantId &&
                    !returnRecord.IsDeleted &&
                    returnRecord.ReturnType == ReturnType.SalesReturn &&
                    returnRecord.TotalAmount > 0m)
                .Select(returnRecord => (decimal?)returnRecord.TotalAmount)
                .FirstOrDefaultAsync(ct),
            "Expense" => await _db.Expenses
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(expense =>
                    expense.Id == request.SourceId &&
                    expense.TenantId == request.TenantId &&
                    !expense.IsDeleted &&
                    expense.Amount > 0m &&
                    (!expense.RequiresApproval || expense.IsApproved))
                .Select(expense => (decimal?)expense.Amount)
                .FirstOrDefaultAsync(ct),
            "Order_Refund" => await ResolveOrderRefundCashAuthorityAsync(request, cashAccountIds, ct),
            "CommerceHubFinancialTransaction" => await ResolveCommerceHubCashAuthorityAsync(
                request,
                CommerceHubFinancialTransactionDirection.Debit,
                ct),
            "AffiliateCommissionPaid" => await _db.AffiliatePayoutTransferAllocations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(allocation =>
                    allocation.ReferenceId == request.SourceId &&
                    allocation.StoreTenantId == request.TenantId &&
                    !allocation.IsDeleted &&
                    allocation.Amount > 0m)
                .SumAsync(allocation => (decimal?)allocation.Amount, ct),
            "Reversal" => await _db.LedgerEntries
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(entry =>
                    entry.TenantId == request.TenantId &&
                    entry.JournalEntryId == request.SourceId &&
                    cashAccountIds.Contains(entry.AccountId))
                .SumAsync(entry => (decimal?)(entry.DebitAmount - entry.CreditAmount), ct),
            _ => null
        };

        if (!authorizedAmount.HasValue || authorizedAmount.Value <= 0m)
        {
            throw new InvalidJournalException(
                $"Treasury outflow requires certified liability, refund, payout, or accounting SQL authority; source '{request.SourceType}' is not authorized.");
        }

        var priorEntries = _db.LedgerEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry =>
                entry.TenantId == request.TenantId &&
                cashAccountIds.Contains(entry.AccountId) &&
                entry.JournalEntry.Status == JournalStatus.Posted &&
                entry.JournalEntry.SourceType == request.SourceType &&
                entry.JournalEntry.SourceId == request.SourceId);

        var previouslyPostedDecrease = await priorEntries
            .SumAsync(entry => (decimal?)(entry.CreditAmount - entry.DebitAmount), ct) ?? 0m;

        if (previouslyPostedDecrease + netCashDecrease > authorizedAmount.Value)
        {
            throw new InvalidJournalException(
                $"Treasury outflow exceeds authoritative source amount for '{request.SourceType}' and source '{request.SourceId}'.");
        }
    }

    private async Task<decimal?> ResolveOrderRefundCashAuthorityAsync(
        PostingRequest request, IReadOnlyCollection<Guid> cashAccountIds, CancellationToken ct)
    {
        var refunds = await _db.OrderRefunds.IgnoreQueryFilters().AsNoTracking()
            .Where(refund => refund.OrderId == request.SourceId && refund.TenantId == request.TenantId &&
                !refund.IsDeleted && refund.Amount > 0m && refund.IsPostSettlementRefund &&
                (refund.FinancialPostingStatus == FinancialPostingStatus.Pending ||
                 refund.FinancialPostingStatus == FinancialPostingStatus.Posted)).ToListAsync(ct);
        var matches = refunds.Where(refund =>
            request.IdempotencyKey == $"ECOM_REFUND:{refund.TenantId}:{refund.OrderId}:{refund.RefundId}").ToList();
        if (matches.Count != 1)
            return null;
        var refund = matches[0];
        var context = new EcommerceRefundAccountingContext(true, refund.SettlementBatchId,
            refund.SettlementType, refund.SettlementId, refund.PayoutBatchId, refund.SettledAtUtc);
        var destinationId = await SettlementRefundLineage.ResolveDestinationAccountAsync(
            _db, refund.TenantId, refund.OrderId, context, ct);
        var outflows = request.Lines.Where(line => cashAccountIds.Contains(line.AccountId) && line.CreditAmount > 0m).ToList();
        if (outflows.Count != 1 || outflows[0].AccountId != destinationId || outflows[0].CreditAmount != refund.Amount)
            return null;

        // The cumulative order cap below still protects all its refund postings;
        // this individual request must additionally match its immutable refund row.
        return refunds.Sum(item => item.Amount);
    }

    private async Task<decimal?> ResolveCommerceHubCashAuthorityAsync(
        PostingRequest request,
        CommerceHubFinancialTransactionDirection expectedDirection,
        CancellationToken ct)
    {
        var transaction = await _db.CommerceHubFinancialTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.Id == request.SourceId &&
                item.Direction == expectedDirection &&
                item.FinancialBucket == CommerceHubFinancialBucket.Cash &&
                item.Amount > 0m &&
                (expectedDirection != CommerceHubFinancialTransactionDirection.Debit ||
                 item.TransactionType == CommerceHubFinancialTransactionType.Refund ||
                 item.TransactionType == CommerceHubFinancialTransactionType.Payout),
                ct);
        if (transaction == null)
            return null;

        var hasTenantAuthority = await _db.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(message =>
                message.TenantId == request.TenantId &&
                !message.IsDeleted &&
                message.EventType == "CommerceHubLedgerPosting" &&
                message.SourceType == "CommerceHubFinancialTransaction" &&
                message.BusinessEntityId == transaction.Id &&
                message.IdempotencyKey == request.IdempotencyKey,
                ct);

        return hasTenantAuthority ? transaction.Amount : null;
    }
    
    /// <summary>
    /// Generates the next sequential journal number for the tenant.
    /// Uses SELECT FOR UPDATE to ensure uniqueness.
    /// </summary>
    private async Task<string> GenerateJournalNumberAsync(
        Guid tenantId,
        DateTime journalDate,
        CancellationToken ct)
    {
        var year = journalDate.Year;
        
        // Try to get existing sequence
        var sequence = await _db.JournalNumberSequences
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        
        if (sequence == null)
        {
            // Create new sequence for this tenant
            sequence = new JournalNumberSequence
            {
                TenantId = tenantId,
                CurrentYear = year,
                NextNumber = 1
            };
            _db.JournalNumberSequences.Add(sequence);
        }
        
        // Allocate next number
        var journalNumber = sequence.AllocateNext(year);
        
        return journalNumber;
    }
    
    #endregion
}
