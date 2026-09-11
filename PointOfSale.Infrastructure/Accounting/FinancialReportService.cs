using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Accounting.Reports;
using PointOfSale.Infrastructure.Data;
using System.Data;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>
/// Financial reporting service implementation.
/// All reports sourced exclusively from the immutable ledger.
/// 
/// PHASE 4: Financial Reporting Engine
/// 
/// CRITICAL: No business tables are read. Ledger is the ONLY source of truth.
/// 
/// PHASE C3: Implements ILedgerOnlyService for architecture test enforcement.
/// Any access to business tables will cause CI/CD to fail.
/// </summary>
public class FinancialReportService : IFinancialReportService, ILedgerOnlyService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<FinancialReportService> _logger;
    
    public FinancialReportService(
        ApplicationDbContext db,
        ILogger<FinancialReportService> logger)
    {
        _db = db;
        _logger = logger;
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // TRIAL BALANCE
    // ═══════════════════════════════════════════════════════════════════════════
    
    public async Task<TrialBalanceReport> GetTrialBalanceAsync(
        Guid tenantId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct = default)
    {
        await using var transaction = await BeginConsistentReadTransactionAsync(ct);

        _logger.LogInformation(
            "Generating Trial Balance for tenant {TenantId} from {From} to {To}",
            tenantId, fromDate, toDate);
        
        // NOTE: NormalBalance is a computed property [NotMapped] derived from AccountType.
        // We cannot use it in EF Core GroupBy. Instead, we group by AccountType only
        // and derive NormalBalance in-memory based on the AccountType.
        var rawLines = await _db.LedgerEntries
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId &&
                        (l.JournalEntry!.Status == JournalStatus.Posted ||
                         l.JournalEntry.Status == JournalStatus.Reversed) &&
                        l.JournalEntry.JournalDate >= fromDate &&
                        l.JournalEntry.JournalDate <= toDate)
            .GroupBy(l => new 
            { 
                l.AccountId, 
                l.Account!.AccountCode, 
                l.Account.Name, 
                l.Account.AccountType
            })
            .Select(g => new 
            {
                g.Key.AccountId,
                g.Key.AccountCode,
                AccountName = g.Key.Name,
                g.Key.AccountType,
                TotalDebits = g.Sum(l => l.DebitAmount),
                TotalCredits = g.Sum(l => l.CreditAmount)
            })
            .OrderBy(l => l.AccountCode)
            .ToListAsync(ct);
        
        // Map to TrialBalanceLine with NormalBalance derived from AccountType
        var lines = rawLines.Select(g => 
        {
            var normalBalance = g.AccountType.GetNormalBalance();
            return new TrialBalanceLine
            {
                AccountId = g.AccountId,
                AccountCode = g.AccountCode,
                AccountName = g.AccountName,
                AccountType = g.AccountType,
                NormalBalance = normalBalance,
                TotalDebits = g.TotalDebits,
                TotalCredits = g.TotalCredits,
                Balance = normalBalance == NormalBalance.Debit
                    ? g.TotalDebits - g.TotalCredits
                    : g.TotalCredits - g.TotalDebits
            };
        }).ToList();
        
        var report = new TrialBalanceReport
        {
            TenantId = tenantId,
            FromDate = fromDate,
            ToDate = toDate,
            Lines = lines
        };
        
        if (!report.IsBalanced)
        {
            _logger.LogWarning(
                "Trial Balance is OUT OF BALANCE! Variance: {Variance}",
                report.Variance);
        }
        
        return report;
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // GENERAL LEDGER
    // ═══════════════════════════════════════════════════════════════════════════
    
    public async Task<GeneralLedgerReport> GetGeneralLedgerAsync(
        Guid tenantId,
        Guid accountId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct = default)
    {
        await using var transaction = await BeginConsistentReadTransactionAsync(ct);

        _logger.LogInformation(
            "Generating General Ledger for account {AccountId} from {From} to {To}",
            accountId, fromDate, toDate);
        
        // Get account details
        var account = await _db.LedgerAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId && a.TenantId == tenantId, ct);
        
        if (account == null)
            throw new InvalidOperationException($"Account {accountId} not found");
        
        // Get opening balance (sum of all entries before fromDate)
        var openingBalance = await GetAccountBalanceAsOfAsync(tenantId, accountId, fromDate.AddDays(-1), ct);
        
        // Get entries in period
        var entries = await _db.LedgerEntries
            .AsNoTracking()
            .Where(l => l.AccountId == accountId &&
                        l.TenantId == tenantId &&
                        (l.JournalEntry!.Status == JournalStatus.Posted ||
                         l.JournalEntry.Status == JournalStatus.Reversed) &&
                        l.JournalEntry.JournalDate >= fromDate &&
                        l.JournalEntry.JournalDate <= toDate)
            .OrderBy(l => l.JournalEntry!.JournalDate)
            .ThenBy(l => l.LineNumber)
            .Select(l => new GeneralLedgerEntry
            {
                JournalDate = l.JournalEntry!.JournalDate,
                JournalNumber = l.JournalEntry.JournalNumber,
                JournalId = l.JournalEntry.Id,
                SourceType = l.JournalEntry.SourceType,
                SourceReference = l.JournalEntry.SourceReference,
                Description = l.LineDescription ?? l.JournalEntry.Description,
                DebitAmount = l.DebitAmount,
                CreditAmount = l.CreditAmount,
                BalanceAfter = l.BalanceAfter
            })
            .ToListAsync(ct);
        
        // Calculate closing balance
        var closingBalance = await GetAccountBalanceAsOfAsync(tenantId, accountId, toDate, ct);
        
        return new GeneralLedgerReport
        {
            TenantId = tenantId,
            AccountId = accountId,
            AccountCode = account.AccountCode,
            AccountName = account.Name,
            FromDate = fromDate,
            ToDate = toDate,
            OpeningBalance = openingBalance,
            Entries = entries,
            ClosingBalance = closingBalance
        };
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // PROFIT & LOSS
    // ═══════════════════════════════════════════════════════════════════════════
    
    public async Task<ProfitAndLossReport> GetProfitAndLossAsync(
        Guid tenantId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct = default)
    {
        await using var transaction = await BeginConsistentReadTransactionAsync(ct);

        _logger.LogInformation(
            "Generating Profit & Loss for tenant {TenantId} from {From} to {To}",
            tenantId, fromDate, toDate);
        
        // P&L account types: Revenue, COGS, Expense
        var plAccountTypes = new[] 
        { 
            AccountType.Revenue, 
            AccountType.CostOfGoodsSold, 
            AccountType.Expense 
        };
        
        // NOTE: NormalBalance is a computed property [NotMapped] derived from AccountType.
        // We cannot use it in EF Core GroupBy. Instead, we group by AccountType only
        // and derive NormalBalance in-memory based on the AccountType.
        var rawLines = await (
            from line in _db.LedgerEntries.AsNoTracking()
            join journal in _db.JournalEntries.AsNoTracking() on line.JournalEntryId equals journal.Id
            join account in _db.LedgerAccounts.AsNoTracking() on line.AccountId equals account.Id
            where line.TenantId == tenantId &&
                  journal.TenantId == tenantId &&
                  account.TenantId == tenantId &&
                  (journal.Status == JournalStatus.Posted ||
                   journal.Status == JournalStatus.Reversed) &&
                  journal.JournalDate >= fromDate &&
                  journal.JournalDate <= toDate &&
                  plAccountTypes.Contains(account.AccountType)
            group line by new
            {
                line.AccountId,
                account.AccountCode,
                account.Name,
                account.AccountType
            }
            into g
            select new
            {
                g.Key.AccountId,
                g.Key.AccountCode,
                AccountName = g.Key.Name,
                g.Key.AccountType,
                TotalDebits = g.Sum(l => l.DebitAmount),
                TotalCredits = g.Sum(l => l.CreditAmount)
            })
            .OrderBy(l => l.AccountType)
            .ThenBy(l => l.AccountCode)
            .ToListAsync(ct);
        
        // Map to ProfitAndLossLine with NormalBalance derived from AccountType
        // For Revenue (Credit-normal): Credit - Debit = positive revenue
        // For Expense/COGS (Debit-normal): Debit - Credit = positive expense
        var lines = rawLines.Select(g => 
        {
            var normalBalance = g.AccountType.GetNormalBalance();
            return new ProfitAndLossLine
            {
                AccountId = g.AccountId,
                AccountCode = g.AccountCode,
                AccountName = g.AccountName,
                AccountType = g.AccountType,
                Amount = normalBalance == NormalBalance.Credit
                    ? g.TotalCredits - g.TotalDebits
                    : g.TotalDebits - g.TotalCredits
            };
        }).ToList();
        
        return new ProfitAndLossReport
        {
            TenantId = tenantId,
            FromDate = fromDate,
            ToDate = toDate,
            RevenueLines = lines.Where(l => l.AccountType == AccountType.Revenue).ToList(),
            CostOfGoodsSoldLines = lines.Where(l => l.AccountType == AccountType.CostOfGoodsSold).ToList(),
            ExpenseLines = lines.Where(l => l.AccountType == AccountType.Expense).ToList()
        };
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // BALANCE SHEET
    // ═══════════════════════════════════════════════════════════════════════════
    
    public async Task<BalanceSheetReport> GetBalanceSheetAsync(
        Guid tenantId,
        DateTime asOfDate,
        CancellationToken ct = default)
    {
        await using var transaction = await BeginConsistentReadTransactionAsync(ct);

        _logger.LogInformation(
            "Generating Balance Sheet for tenant {TenantId} as of {AsOfDate}",
            tenantId, asOfDate);
        
        // Include temporary P&L accounts so current earnings can be presented in equity.
        var reportAccountTypes = new[]
        { 
            AccountType.Asset, 
            AccountType.Liability, 
            AccountType.Equity,
            AccountType.Revenue,
            AccountType.CostOfGoodsSold,
            AccountType.Expense
        };
        
        // Aggregate balances in SQL so the report scales with account count.
        var rawBalances = await _db.LedgerEntries
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId &&
                        (l.JournalEntry!.Status == JournalStatus.Posted ||
                         l.JournalEntry.Status == JournalStatus.Reversed) &&
                        l.JournalEntry.JournalDate <= asOfDate &&
                        reportAccountTypes.Contains(l.Account.AccountType))
            .GroupBy(l => new
            {
                l.AccountId,
                l.Account.AccountCode,
                l.Account.Name,
                l.Account.AccountType
            })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.AccountCode,
                AccountName = g.Key.Name,
                g.Key.AccountType,
                TotalDebits = g.Sum(l => l.DebitAmount),
                TotalCredits = g.Sum(l => l.CreditAmount)
            })
            .OrderBy(l => l.AccountCode)
            .ToListAsync(ct);

        var allLines = rawBalances
            .Select(balance => new BalanceSheetLine
            {
                AccountId = balance.AccountId,
                AccountCode = balance.AccountCode,
                AccountName = balance.AccountName,
                AccountType = balance.AccountType,
                Balance = ComputeBalance(balance.AccountType, balance.TotalDebits, balance.TotalCredits),
                IsCurrent = IsCurrent(balance.AccountCode)
            })
            .Where(line => line.Balance != 0)
            .ToList();

        var lines = allLines
            .Where(line => line.AccountType is AccountType.Asset or AccountType.Liability or AccountType.Equity)
            .ToList();
        var currentEarnings =
            allLines.Where(line => line.AccountType == AccountType.Revenue).Sum(line => line.Balance) -
            allLines.Where(line => line.AccountType is AccountType.CostOfGoodsSold or AccountType.Expense).Sum(line => line.Balance);
        if (currentEarnings != 0m)
        {
            lines.Add(new BalanceSheetLine
            {
                AccountId = Guid.Empty,
                AccountCode = "3999",
                AccountName = "Current Earnings",
                AccountType = AccountType.Equity,
                Balance = currentEarnings,
                IsCurrent = false
            });
        }
        
        // Calculate current ratio
        var currentAssets = lines
            .Where(l => l.AccountType == AccountType.Asset && l.IsCurrent)
            .Sum(l => l.Balance);
        var currentLiabilities = lines
            .Where(l => l.AccountType == AccountType.Liability && l.IsCurrent)
            .Sum(l => l.Balance);
        
        var currentRatio = currentLiabilities != 0 
            ? Math.Round(currentAssets / currentLiabilities, 2) 
            : (decimal?)null;
        
        var report = new BalanceSheetReport
        {
            TenantId = tenantId,
            AsOfDate = asOfDate,
            AssetLines = lines.Where(l => l.AccountType == AccountType.Asset)
                .OrderBy(l => l.AccountCode).ToList(),
            LiabilityLines = lines.Where(l => l.AccountType == AccountType.Liability)
                .OrderBy(l => l.AccountCode).ToList(),
            EquityLines = lines.Where(l => l.AccountType == AccountType.Equity)
                .OrderBy(l => l.AccountCode).ToList(),
            CurrentRatio = currentRatio
        };
        
        if (!report.IsBalanced)
        {
            _logger.LogWarning(
                "Balance Sheet is OUT OF BALANCE! Variance: {Variance}",
                report.Variance);
        }
        
        return report;
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CASH FLOW
    // ═══════════════════════════════════════════════════════════════════════════
    
    public async Task<CashFlowReport> GetCashFlowAsync(
        Guid tenantId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct = default)
    {
        await using var transaction = await BeginConsistentReadTransactionAsync(ct);

        _logger.LogInformation(
            "Generating Cash Flow for tenant {TenantId} from {From} to {To}",
            tenantId, fromDate, toDate);
        
        // 1. Get P&L for net profit
        var pnl = await GetProfitAndLossAsync(tenantId, fromDate, toDate, ct);
        
        // Snapshot all balances once per boundary instead of querying per account.
        var openingBalances = await GetAccountBalancesAsOfAsync(tenantId, fromDate.AddDays(-1), ct);
        var closingBalances = await GetAccountBalancesAsOfAsync(tenantId, toDate, ct);

        var openingCash = openingBalances
            .Where(IsCashEquivalent)
            .Sum(account => account.Balance);

        var closingCash = closingBalances
            .Where(IsCashEquivalent)
            .Sum(account => account.Balance);
        
        // 3. Calculate working capital changes
        var workingCapitalChanges = new List<CashFlowAdjustment>();
        
        // Receivables - increase = cash outflow
        var receivablesChange =
            GetAccountCodesBalance(closingBalances, AccountCodes.AccountsReceivable, AccountCodes.MarketplaceVendorReceivable) -
            GetAccountCodesBalance(openingBalances, AccountCodes.AccountsReceivable, AccountCodes.MarketplaceVendorReceivable);
        if (receivablesChange != 0)
        {
            workingCapitalChanges.Add(new CashFlowAdjustment
            {
                Description = "Change in Accounts Receivable",
                AccountCode = $"{AccountCodes.AccountsReceivable}/{AccountCodes.MarketplaceVendorReceivable}",
                Amount = -receivablesChange // Increase in AR = cash decrease
            });
        }
        
        // Inventory (1300-1399) - increase = cash outflow
        var inventoryChange = GetPrefixBalance(closingBalances, "13") - GetPrefixBalance(openingBalances, "13");
        if (inventoryChange != 0)
        {
            workingCapitalChanges.Add(new CashFlowAdjustment
            {
                Description = "Change in Inventory",
                AccountCode = "13xx",
                Amount = -inventoryChange // Increase in Inventory = cash decrease
            });
        }
        
        // Payables (2000-2099) - increase = cash inflow
        var payablesChange = GetPrefixBalance(closingBalances, "20") - GetPrefixBalance(openingBalances, "20");
        if (payablesChange != 0)
        {
            workingCapitalChanges.Add(new CashFlowAdjustment
            {
                Description = "Change in Accounts Payable",
                AccountCode = "20xx",
                Amount = payablesChange // Increase in AP = cash increase
            });
        }
        
        var financingActivities = new List<CashFlowItem>();
        var equityChange = closingBalances
            .Where(account => account.AccountType == AccountType.Equity)
            .Sum(account => account.Balance) - openingBalances
            .Where(account => account.AccountType == AccountType.Equity)
            .Sum(account => account.Balance);
        if (equityChange != 0m)
        {
            financingActivities.Add(new CashFlowItem
            {
                Description = "Net owner equity movement",
                AccountCode = "3xxx",
                Amount = equityChange
            });
        }

        return new CashFlowReport
        {
            TenantId = tenantId,
            FromDate = fromDate,
            ToDate = toDate,
            OpeningCashBalance = openingCash,
            ClosingCashBalance = closingCash,
            NetProfit = pnl.NetProfit,
            WorkingCapitalChanges = workingCapitalChanges,
            NonCashAdjustments = new List<CashFlowAdjustment>(), // Would need depreciation accounts
            InvestingActivities = new List<CashFlowItem>(),
            FinancingActivities = financingActivities
        };
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CLEARING AGING
    // ═══════════════════════════════════════════════════════════════════════════
    
    public async Task<ClearingAgingReport> GetClearingAgingAsync(
        Guid tenantId,
        DateTime asOfDate,
        CancellationToken ct = default)
    {
        await using var transaction = await BeginConsistentReadTransactionAsync(ct);

        _logger.LogInformation(
            "Generating Clearing Aging for tenant {TenantId} as of {AsOfDate}",
            tenantId, asOfDate);
        
        var currentWindowStart = asOfDate.AddDays(-7);
        var days8To14WindowStart = asOfDate.AddDays(-14);
        var days15To30WindowStart = asOfDate.AddDays(-30);

        // Age buckets are computed in SQL to avoid loading full clearing ledgers into memory.
        var rawLines = await _db.LedgerEntries
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId &&
                        (l.JournalEntry!.Status == JournalStatus.Posted ||
                         l.JournalEntry.Status == JournalStatus.Reversed) &&
                        l.JournalEntry.JournalDate <= asOfDate &&
                        l.Account.IsClearing)
            .GroupBy(l => new
            {
                l.AccountId,
                l.Account.AccountCode,
                l.Account.Name,
                l.Account.AccountType
            })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.AccountCode,
                AccountName = g.Key.Name,
                g.Key.AccountType,
                OldestTransactionDate = g.Min(l => (DateTime?)l.JournalEntry!.JournalDate),
                TransactionCount = g.Count(),
                TotalDebits = g.Sum(l => l.DebitAmount),
                TotalCredits = g.Sum(l => l.CreditAmount),
                CurrentDebits = g.Where(l => l.JournalEntry!.JournalDate >= currentWindowStart).Sum(l => l.DebitAmount),
                CurrentCredits = g.Where(l => l.JournalEntry!.JournalDate >= currentWindowStart).Sum(l => l.CreditAmount),
                Days8To14Debits = g.Where(l => l.JournalEntry!.JournalDate < currentWindowStart &&
                                               l.JournalEntry.JournalDate >= days8To14WindowStart)
                    .Sum(l => l.DebitAmount),
                Days8To14Credits = g.Where(l => l.JournalEntry!.JournalDate < currentWindowStart &&
                                                l.JournalEntry.JournalDate >= days8To14WindowStart)
                    .Sum(l => l.CreditAmount),
                Days15To30Debits = g.Where(l => l.JournalEntry!.JournalDate < days8To14WindowStart &&
                                                l.JournalEntry.JournalDate >= days15To30WindowStart)
                    .Sum(l => l.DebitAmount),
                Days15To30Credits = g.Where(l => l.JournalEntry!.JournalDate < days8To14WindowStart &&
                                                 l.JournalEntry.JournalDate >= days15To30WindowStart)
                    .Sum(l => l.CreditAmount),
                Over30DaysDebits = g.Where(l => l.JournalEntry!.JournalDate < days15To30WindowStart).Sum(l => l.DebitAmount),
                Over30DaysCredits = g.Where(l => l.JournalEntry!.JournalDate < days15To30WindowStart).Sum(l => l.CreditAmount)
            })
            .OrderBy(line => line.AccountCode)
            .ToListAsync(ct);

        var lines = rawLines
            .Select(line =>
            {
                var pendingAmount = ComputeBalance(line.AccountType, line.TotalDebits, line.TotalCredits);
                return new ClearingAgingLine
                {
                    AccountId = line.AccountId,
                    AccountCode = line.AccountCode,
                    AccountName = line.AccountName,
                    PendingAmount = pendingAmount,
                    OldestTransactionDate = line.OldestTransactionDate,
                    AgeDays = line.OldestTransactionDate.HasValue
                        ? (int)(asOfDate - line.OldestTransactionDate.Value).TotalDays
                        : null,
                    TransactionCount = line.TransactionCount,
                    Current = ComputeBalance(line.AccountType, line.CurrentDebits, line.CurrentCredits),
                    Days8To14 = ComputeBalance(line.AccountType, line.Days8To14Debits, line.Days8To14Credits),
                    Days15To30 = ComputeBalance(line.AccountType, line.Days15To30Debits, line.Days15To30Credits),
                    Over30Days = ComputeBalance(line.AccountType, line.Over30DaysDebits, line.Over30DaysCredits)
                };
            })
            .Where(line => line.PendingAmount != 0)
            .ToList();
        
        return new ClearingAgingReport
        {
            TenantId = tenantId,
            AsOfDate = asOfDate,
            Lines = lines.OrderBy(l => l.AccountCode).ToList()
        };
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // ACCOUNT SUMMARY
    // ═══════════════════════════════════════════════════════════════════════════
    
    public async Task<List<AccountSummary>> GetAccountSummariesAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        await using var transaction = await BeginConsistentReadTransactionAsync(ct);

        var balances = await GetAccountBalancesAsOfAsync(tenantId, DateTime.UtcNow, ct);
        var balanceLookup = balances.ToDictionary(account => account.AccountId, account => account.Balance);

        var accountRows = await _db.LedgerAccounts
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.AccountCode)
            .Select(a => new
            {
                a.Id,
                a.AccountCode,
                a.Name,
                AccountType = a.AccountType.ToString(),
                a.IsClearing,
                IsTreasury = a.IsSystem
            })
            .ToListAsync(ct);

        return accountRows
            .Select(account => new AccountSummary
            {
                AccountId = account.Id,
                AccountCode = account.AccountCode,
                AccountName = account.Name,
                AccountType = account.AccountType,
                CurrentBalance = balanceLookup.GetValueOrDefault(account.Id),
                IsClearing = account.IsClearing,
                IsTreasury = account.IsTreasury
            })
            .ToList();
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Gets an account's balance as of a specific date.
    /// </summary>
    private async Task<decimal> GetAccountBalanceAsOfAsync(
        Guid tenantId, 
        Guid accountId, 
        DateTime asOfDate,
        CancellationToken ct)
    {
        var accountType = await _db.LedgerAccounts
            .AsNoTracking()
            .Where(a => a.Id == accountId && a.TenantId == tenantId)
            .Select(a => (AccountType?)a.AccountType)
            .FirstOrDefaultAsync(ct);

        if (!accountType.HasValue)
            return 0;

        var totals = await _db.LedgerEntries
            .AsNoTracking()
            .Where(l => l.AccountId == accountId &&
                        l.TenantId == tenantId &&
                        (l.JournalEntry!.Status == JournalStatus.Posted ||
                         l.JournalEntry.Status == JournalStatus.Reversed) &&
                        l.JournalEntry.JournalDate <= asOfDate)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalDebits = g.Sum(l => l.DebitAmount),
                TotalCredits = g.Sum(l => l.CreditAmount)
            })
            .FirstOrDefaultAsync(ct);

        return ComputeBalance(
            accountType.Value,
            totals?.TotalDebits ?? 0,
            totals?.TotalCredits ?? 0);
    }
    
    /// <summary>
    /// Determines if an account is a current (short-term) account based on code.
    /// </summary>
    private static bool IsCurrent(string accountCode)
    {
        // Convention: 1000-1499 are current assets, 2000-2499 are current liabilities
        if (int.TryParse(accountCode, out var code))
        {
            return (code >= 1000 && code < 1500) || (code >= 2000 && code < 2500);
        }
        return false;
    }

    private async Task<IDbContextTransaction?> BeginConsistentReadTransactionAsync(CancellationToken ct)
    {
        if (!_db.Database.IsRelational() || _db.Database.CurrentTransaction != null)
        {
            return null;
        }

        return await _db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
    }

    private async Task<List<AccountBalanceAggregate>> GetAccountBalancesAsOfAsync(
        Guid tenantId,
        DateTime asOfDate,
        CancellationToken ct)
    {
        var rawBalances = await (
            from line in _db.LedgerEntries.AsNoTracking()
            join journal in _db.JournalEntries.AsNoTracking() on line.JournalEntryId equals journal.Id
            join account in _db.LedgerAccounts.AsNoTracking() on line.AccountId equals account.Id
            where line.TenantId == tenantId &&
                  journal.TenantId == tenantId &&
                  account.TenantId == tenantId &&
                  (journal.Status == JournalStatus.Posted ||
                   journal.Status == JournalStatus.Reversed) &&
                  journal.JournalDate <= asOfDate
            group line by new
            {
                line.AccountId,
                account.AccountCode,
                account.Name,
                account.AccountType
            }
            into g
            select new
            {
                g.Key.AccountId,
                g.Key.AccountCode,
                AccountName = g.Key.Name,
                g.Key.AccountType,
                TotalDebits = g.Sum(l => l.DebitAmount),
                TotalCredits = g.Sum(l => l.CreditAmount)
            })
            .ToListAsync(ct);

        return rawBalances
            .Select(balance => new AccountBalanceAggregate
            {
                AccountId = balance.AccountId,
                AccountCode = balance.AccountCode,
                AccountName = balance.AccountName,
                AccountType = balance.AccountType,
                Balance = ComputeBalance(balance.AccountType, balance.TotalDebits, balance.TotalCredits)
            })
            .ToList();
    }

    private static decimal ComputeBalance(AccountType accountType, decimal totalDebits, decimal totalCredits)
    {
        return accountType.GetNormalBalance() == NormalBalance.Debit
            ? totalDebits - totalCredits
            : totalCredits - totalDebits;
    }

    private static decimal GetPrefixBalance(IEnumerable<AccountBalanceAggregate> balances, string prefix)
    {
        return balances
            .Where(balance => balance.AccountCode.StartsWith(prefix, StringComparison.Ordinal))
            .Sum(balance => balance.Balance);
    }

    private static decimal GetAccountCodesBalance(IEnumerable<AccountBalanceAggregate> balances, params string[] accountCodes)
    {
        var codeSet = accountCodes.ToHashSet(StringComparer.Ordinal);
        return balances
            .Where(balance => codeSet.Contains(balance.AccountCode))
            .Sum(balance => balance.Balance);
    }

    private static bool IsCashEquivalent(AccountBalanceAggregate account) =>
        account.AccountCode.StartsWith("10", StringComparison.Ordinal) ||
        account.AccountCode.StartsWith("11", StringComparison.Ordinal);

    private sealed class AccountBalanceAggregate
    {
        public Guid AccountId { get; init; }
        public string AccountCode { get; init; } = string.Empty;
        public string AccountName { get; init; } = string.Empty;
        public AccountType AccountType { get; init; }
        public decimal Balance { get; init; }
    }
}
