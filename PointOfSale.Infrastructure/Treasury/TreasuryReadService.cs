using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Treasury;

/// <summary>
/// PHASE TLW: Treasury Ledger Read Wiring implementation.
/// 
/// ALL reads come from Ledger tables:
/// - Accounts (for balances)
/// - JournalEntries (for journal metadata)
/// - LedgerEntries (for individual debit/credit movements)
/// 
/// ❌ NO references to legacy treasury transaction tables or mutable balance shadows.
/// </summary>
public class TreasuryReadService : ITreasuryReadService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<TreasuryReadService> _logger;
    
    public TreasuryReadService(
        ApplicationDbContext db,
        ILogger<TreasuryReadService> logger)
    {
        _db = db;
        _logger = logger;
    }
    
    /// <inheritdoc />
    public async Task EnsureAccountExistsAsync(
        Guid tenantId, 
        string accountCode, 
        CancellationToken ct = default)
    {
        var exists = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(account =>
                account.TenantId == tenantId &&
                account.IsActive &&
                !account.IsDeleted &&
                account.Account != null &&
                account.Account.TenantId == tenantId &&
                account.Account.IsActive &&
                account.Account.AccountType == AccountType.Asset &&
                account.Account.AccountCode == accountCode,
                ct);
        
        if (!exists)
        {
            throw new AccountNotFoundException(
                $"Ledger account '{accountCode}' not found for tenant {tenantId}. " +
                $"This indicates a misconfigured tenant. Please ensure the chart of accounts is properly seeded.");
        }
    }
    
    /// <inheritdoc />
    public async Task<decimal> GetAccountBalanceAsync(
        Guid tenantId, 
        string accountCode, 
        CancellationToken ct = default)
    {
        var account = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId &&
                item.IsActive &&
                !item.IsDeleted &&
                item.Account != null &&
                item.Account.TenantId == tenantId &&
                item.Account.IsActive &&
                item.Account.AccountType == AccountType.Asset &&
                item.Account.AccountCode == accountCode)
            .Select(item => item.Account)
            .FirstOrDefaultAsync(ct);
        
        if (account == null)
        {
            _logger.LogWarning(
                "Account {AccountCode} not found for tenant {TenantId}", 
                accountCode, tenantId);
            return 0;
        }
        
        return account.CurrentBalance;
    }
    
    /// <inheritdoc />
    public async Task<Dictionary<string, decimal>> GetAccountBalancesAsync(
        Guid tenantId,
        IEnumerable<string> accountCodes,
        CancellationToken ct = default)
    {
        var codes = accountCodes.ToList();
        
        var accounts = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(account =>
                account.TenantId == tenantId &&
                account.IsActive &&
                !account.IsDeleted &&
                account.Account != null &&
                account.Account.TenantId == tenantId &&
                account.Account.IsActive &&
                account.Account.AccountType == AccountType.Asset &&
                codes.Contains(account.Account.AccountCode))
            .Select(account => new
            {
                account.Account!.AccountCode,
                account.Account.CurrentBalance
            })
            .ToListAsync(ct);
        
        return accounts.ToDictionary(a => a.AccountCode, a => a.CurrentBalance);
    }
    
    /// <inheritdoc />
    public async Task<IReadOnlyList<TreasuryMovementDto>> GetMovementsAsync(
        Guid tenantId,
        string accountCode,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        return await GetMovementsAsync(tenantId, new[] { accountCode }, from, to, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TreasuryMovementDto>> GetMovementsAsync(
        Guid tenantId,
        IEnumerable<string> accountCodes,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var codes = accountCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (codes.Count == 0)
        {
            return Array.Empty<TreasuryMovementDto>();
        }

        var accounts = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(account =>
                account.TenantId == tenantId &&
                account.IsActive &&
                !account.IsDeleted &&
                account.Account != null &&
                account.Account.TenantId == tenantId &&
                account.Account.IsActive &&
                account.Account.AccountType == AccountType.Asset &&
                codes.Contains(account.Account.AccountCode))
            .Select(account => new
            {
                Id = account.AccountId,
                account.Account!.AccountCode,
                account.Account.Name
            })
            .ToListAsync(ct);

        if (accounts.Count == 0)
        {
            _logger.LogWarning(
                "Accounts {AccountCodes} not found for tenant {TenantId}",
                string.Join(", ", codes),
                tenantId);
            return Array.Empty<TreasuryMovementDto>();
        }

        var accountIds = accounts.Select(account => account.Id).ToList();

        return await _db.LedgerEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry =>
                entry.TenantId == tenantId &&
                accountIds.Contains(entry.AccountId) &&
                entry.JournalEntry.JournalDate >= from.Date &&
                entry.JournalEntry.JournalDate <= to.Date &&
                (entry.JournalEntry.Status == JournalStatus.Posted ||
                 entry.JournalEntry.Status == JournalStatus.Reversed))
            .OrderBy(entry => entry.JournalEntry.JournalDate)
            .ThenBy(entry => entry.JournalEntry.PostedAtUtc)
            .ThenBy(entry => entry.LineNumber)
            .Select(entry => new TreasuryMovementDto
            {
                Id = entry.Id,
                JournalId = entry.JournalEntry.Id,
                ReferenceId = entry.JournalEntry.SourceId,
                AccountId = entry.AccountId,
                AccountCode = entry.Account.AccountCode,
                AccountName = entry.Account.Name,
                Date = entry.JournalEntry.JournalDate,
                TimestampUtc = entry.JournalEntry.PostedAtUtc,
                JournalNumber = entry.JournalEntry.JournalNumber,
                Description = entry.LineDescription ?? entry.JournalEntry.Description,
                SourceType = entry.JournalEntry.SourceType,
                SourceReference = entry.JournalEntry.SourceReference,
                CreatedByUserId = entry.JournalEntry.CreatedByUserId,
                CreatedByUserName = _db.Users
                    .Where(user => user.Id == entry.JournalEntry.CreatedByUserId)
                    .Select(user => user.Username)
                    .FirstOrDefault(),
                Debit = entry.DebitAmount,
                Credit = entry.CreditAmount,
                BalanceAfter = entry.BalanceAfter
            })
            .ToListAsync(ct);
    }
    
    /// <inheritdoc />
    public async Task<TreasuryDashboardDto> GetDashboardAsync(
        Guid tenantId,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var dashboard = new TreasuryDashboardDto
        {
            FromDate = from.Date,
            ToDate = to.Date
        };
        
        // Get all accounts for this tenant
        var accounts = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.IsActive)
            .ToListAsync(ct);

        var treasuryAccounts = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(account => account.Account)
            .Where(account =>
                account.TenantId == tenantId &&
                account.IsActive &&
                account.Account != null &&
                account.Account.TenantId == tenantId &&
                account.Account.IsActive &&
                account.Account.AccountType == AccountType.Asset)
            .ToListAsync(ct);

        // Cash & Bank balances (current balance from account)
        var cashboxTreasuryAccounts = treasuryAccounts
            .Where(account => account.Type == TreasuryAccountType.Cashbox)
            .ToList();
        var bankTreasuryAccounts = treasuryAccounts
            .Where(account => account.Type == TreasuryAccountType.Bank)
            .ToList();
        
        // Include both dedicated cashbox accounts AND generic accounts (with blank ReferenceCode)
        // to ensure balances on generic cashbox are not lost when dedicated cashboxes exist
        var dedicatedCashboxTreasuryAccounts = cashboxTreasuryAccounts
            .Where(account => !string.IsNullOrWhiteSpace(account.ReferenceCode))
            .ToList();
        var genericCashboxTreasuryAccounts = cashboxTreasuryAccounts
            .Where(account => string.IsNullOrWhiteSpace(account.ReferenceCode))
            .ToList();
        
        // Union of dedicated and generic accounts - include both to capture all balances
        var effectiveCashboxAccounts = dedicatedCashboxTreasuryAccounts
            .Concat(genericCashboxTreasuryAccounts)
            .ToList();

        dashboard.CashboxBalance = effectiveCashboxAccounts.Sum(account => account.Account?.CurrentBalance ?? 0m);

        dashboard.BankBalance = bankTreasuryAccounts.Sum(account => account.Account?.CurrentBalance ?? 0m);
        dashboard.TotalCashAndBank = dashboard.CashboxBalance + dashboard.BankBalance;
        
        // For P&L items, we need to sum activity in the date range
        // Revenue = Sum of credits to revenue accounts in period
        // Expenses = Sum of debits to expense accounts in period
        // COGS = Sum of debits to COGS accounts in period
        
        var revenueAccountIds = accounts
            .Where(a => a.AccountType == AccountType.Revenue)
            .Select(a => a.Id)
            .ToList();
        
        var expenseAccountIds = accounts
            .Where(a => a.AccountType == AccountType.Expense)
            .Select(a => a.Id)
            .ToList();
        
        // Query ledger entries for the period
        var periodEntries = await _db.LedgerEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(l => l.JournalEntry)
            .Where(l => 
                l.TenantId == tenantId &&
                l.JournalEntry.JournalDate >= from.Date &&
                l.JournalEntry.JournalDate <= to.Date &&
                (l.JournalEntry.Status == JournalStatus.Posted ||
                 l.JournalEntry.Status == JournalStatus.Reversed))
            .ToListAsync(ct);
        
        // Revenue = credits to revenue accounts
        dashboard.Revenue = periodEntries
            .Where(e => revenueAccountIds.Contains(e.AccountId))
            .Sum(e => e.CreditAmount - e.DebitAmount); // Revenue is credit-normal
        
        // Expenses = debits to expense accounts
        dashboard.Expenses = periodEntries
            .Where(e => expenseAccountIds.Contains(e.AccountId))
            .Sum(e => e.DebitAmount - e.CreditAmount); // Expense is debit-normal
        
        // COGS - specific account
        var cogsAccount = accounts.FirstOrDefault(a => a.AccountCode == AccountCodes.CostOfGoodsSold);
        if (cogsAccount != null)
        {
            dashboard.CostOfGoodsSold = periodEntries
                .Where(e => e.AccountId == cogsAccount.Id)
                .Sum(e => e.DebitAmount - e.CreditAmount);
        }
        
        // Net Profit = Revenue - COGS - Expenses
        dashboard.NetProfit = dashboard.Revenue - dashboard.CostOfGoodsSold - dashboard.Expenses;
        
        // Clearing accounts with non-zero balances
        var clearingAccounts = accounts
            .Where(a => a.IsClearing && a.CurrentBalance != 0)
            .Select(a => new ClearingAccountDto
            {
                AccountCode = a.AccountCode,
                Name = a.Name,
                Balance = a.CurrentBalance,
                SourceType = a.ClearingSourceType,
                SourceCode = a.ClearingSourceCode
            })
            .ToList();
        
        dashboard.ClearingAccounts = clearingAccounts;
        dashboard.TotalPendingClearing = clearingAccounts.Sum(c => c.Balance);
        
        return dashboard;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TreasuryAccountSnapshotDto>> GetTreasuryAccountsAsync(
        Guid tenantId,
        TreasuryAccountType? type = null,
        CancellationToken ct = default)
    {
        IQueryable<TreasuryAccount> query = _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(account =>
                account.TenantId == tenantId &&
                account.IsActive &&
                account.Account != null &&
                account.Account.TenantId == tenantId &&
                account.Account.IsActive &&
                account.Account.AccountType == AccountType.Asset);

        if (type.HasValue)
        {
            query = query.Where(account => account.Type == type.Value);
        }

        return await query
            .OrderByDescending(account => account.IsDefault)
            .ThenBy(account => account.DisplayOrder)
            .ThenBy(account => account.Account!.AccountCode)
            .Select(account => new TreasuryAccountSnapshotDto
            {
                AccountId = account.AccountId,
                AccountCode = account.Account!.AccountCode,
                Name = account.Name,
                NameAr = account.NameAr,
                CurrentBalance = account.Account.CurrentBalance,
                Type = account.Type,
                LedgerAccountType = account.Account.AccountType.ToString(),
                IsDefault = account.IsDefault,
                IsClearing = account.Account.IsClearing,
                PreventNegative = account.Account.PreventNegative,
                ReferenceCode = account.ReferenceCode,
                Location = account.Location
            })
            .ToListAsync(ct);
    }
}
