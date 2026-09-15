using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Treasury;
using Xunit;

namespace PointOfSale.Tests.Accounting;

public sealed class FinancialReportServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Guid _tenantId = Guid.NewGuid();

    public FinancialReportServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options, new TestTenantContext(_tenantId));
    }

    [Fact]
    public async Task GetCashFlowAsync_IncludesSeededAccountsReceivableCodeInWorkingCapital()
    {
        var receivable = Account.Create(
            _tenantId,
            AccountCodes.AccountsReceivable,
            "Accounts Receivable",
            AccountType.Asset);
        var revenue = Account.Create(
            _tenantId,
            AccountCodes.PosSales,
            "POS Sales",
            AccountType.Revenue);

        _db.LedgerAccounts.AddRange(receivable, revenue);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(
            _db,
            NullLogger<FinancialPostingService>.Instance);
        var reportService = new FinancialReportService(
            _db,
            NullLogger<FinancialReportService>.Instance);
        var fromDate = new DateTime(2026, 07, 01);
        var toDate = new DateTime(2026, 07, 31);

        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = fromDate.AddDays(4),
            SourceType = "SalesInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Credit sale creates receivable",
            IdempotencyKey = $"REPORT-CF-AR:{_tenantId}:{Guid.NewGuid():N}",
            Lines =
            [
                PostingLine.Debit(receivable.Id, 250m),
                PostingLine.Credit(revenue.Id, 250m)
            ]
        });

        _db.ChangeTracker.Clear();
        Assert.Equal(1, await _db.JournalEntries.CountAsync());
        Assert.Equal(2, await _db.LedgerEntries.CountAsync());

        var report = await reportService.GetCashFlowAsync(_tenantId, fromDate, toDate);

        var receivableAdjustment = Assert.Single(
            report.WorkingCapitalChanges,
            adjustment => adjustment.Description == "Change in Accounts Receivable");
        Assert.Equal($"{AccountCodes.AccountsReceivable}/{AccountCodes.MarketplaceVendorReceivable}", receivableAdjustment.AccountCode);
        Assert.Equal(-250m, receivableAdjustment.Amount);
        Assert.Equal(250m, report.NetProfit);
        Assert.Equal(0m, report.OperatingCashFlow);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetBalanceSheetAsync_IncludesCurrentEarningsInEquity()
    {
        var cash = Account.Create(_tenantId, AccountCodes.CashboxMain, "Cashbox", AccountType.Asset);
        var revenue = Account.Create(_tenantId, AccountCodes.PosSales, "POS Sales", AccountType.Revenue);
        _db.LedgerAccounts.AddRange(cash, revenue);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportService = new FinancialReportService(_db, NullLogger<FinancialReportService>.Instance);
        var journalDate = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = journalDate,
            SourceType = "SalesInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Current period sale",
            IdempotencyKey = $"REPORT-BS-EARNINGS:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(cash.Id, 125m),
                PostingLine.Credit(revenue.Id, 125m)
            ]
        });

        var report = await reportService.GetBalanceSheetAsync(_tenantId, journalDate.Date.AddDays(1).AddTicks(-1));

        var currentEarnings = Assert.Single(report.EquityLines, line => line.AccountCode == "3999");
        Assert.Equal(125m, currentEarnings.Balance);
        Assert.True(report.IsBalanced);
    }

    [Fact]
    public async Task GetCashFlowAsync_ExcludesClearingFromCashEquivalents()
    {
        var clearing = Account.Create(_tenantId, AccountCodes.VisaClearing, "Visa Clearing", AccountType.Asset);
        var equity = Account.Create(_tenantId, AccountCodes.OpeningBalanceEquity, "Opening Equity", AccountType.Equity);
        _db.LedgerAccounts.AddRange(clearing, equity);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportService = new FinancialReportService(_db, NullLogger<FinancialReportService>.Instance);
        var fromDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = fromDate.AddDays(1),
            SourceType = "OpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Owner contribution held in clearing",
            IdempotencyKey = $"REPORT-CF-EQUITY:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(clearing.Id, 100m),
                PostingLine.Credit(equity.Id, 100m)
            ]
        });

        var report = await reportService.GetCashFlowAsync(_tenantId, fromDate, toDate);

        Assert.Equal(0m, report.ClosingCashBalance);
        Assert.Empty(report.FinancingActivities);
        Assert.Equal(0m, report.NetCashChange);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_AccruedAffiliateExpenseWithoutPayment_HasNoCashImpact()
    {
        var cash = Account.Create(_tenantId, AccountCodes.CashboxMain, "Cashbox", AccountType.Asset);
        var equity = Account.Create(_tenantId, AccountCodes.OpeningBalanceEquity, "Opening Equity", AccountType.Equity);
        var affiliateExpense = Account.Create(
            _tenantId,
            AccountCodes.AffiliateMarketingExpense,
            "Affiliate Marketing Expense",
            AccountType.Expense);
        var affiliatePayable = Account.Create(
            _tenantId,
            AccountCodes.AffiliatePayable,
            "Affiliate Payable",
            AccountType.Liability);
        _db.LedgerAccounts.AddRange(cash, equity, affiliateExpense, affiliatePayable);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportService = new FinancialReportService(_db, NullLogger<FinancialReportService>.Instance);
        var fromDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = fromDate.AddDays(10);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = fromDate.AddDays(-1),
            SourceType = "OpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Opening cash",
            IdempotencyKey = $"T03-OPENING:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(cash.Id, 500m),
                PostingLine.Credit(equity.Id, 500m)
            ]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = fromDate.AddDays(2),
            SourceType = "AffiliateCommissionEarned",
            SourceId = Guid.NewGuid(),
            Description = "Accrued affiliate commission",
            IdempotencyKey = $"T03-AFFILIATE-ACCRUAL:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(affiliateExpense.Id, 120m),
                PostingLine.Credit(affiliatePayable.Id, 120m)
            ]
        });
        _db.TreasuryAccounts.Add(TreasuryAccount.CreateCashbox(_tenantId, cash.Id, "Cashbox"));
        await _db.SaveChangesAsync();

        var report = await reportService.GetCashFlowAsync(_tenantId, fromDate, toDate);

        var liabilityAdjustment = Assert.Single(
            report.WorkingCapitalChanges,
            adjustment => adjustment.Description == "Change in Accounts Payable");
        Assert.Equal($"20xx/{AccountCodes.AffiliatePayable}", liabilityAdjustment.AccountCode);
        Assert.Equal(120m, liabilityAdjustment.Amount);
        Assert.Equal(-120m, report.NetProfit);
        Assert.Equal(0m, report.OperatingCashFlow);
        Assert.Equal(500m, report.OpeningCashBalance);
        Assert.Equal(500m, report.ClosingCashBalance);
        Assert.Equal(0m, report.NetCashChange);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_AffiliatePaymentInLaterPeriod_ReportsCashOutflowOnce()
    {
        var cash = Account.Create(_tenantId, AccountCodes.CashboxMain, "Cashbox", AccountType.Asset);
        var equity = Account.Create(_tenantId, AccountCodes.OpeningBalanceEquity, "Opening Equity", AccountType.Equity);
        var affiliateExpense = Account.Create(
            _tenantId,
            AccountCodes.AffiliateMarketingExpense,
            "Affiliate Marketing Expense",
            AccountType.Expense);
        var affiliatePayable = Account.Create(
            _tenantId,
            AccountCodes.AffiliatePayable,
            "Affiliate Payable",
            AccountType.Liability);
        _db.LedgerAccounts.AddRange(cash, equity, affiliateExpense, affiliatePayable);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportService = new FinancialReportService(_db, NullLogger<FinancialReportService>.Instance);
        var paymentDate = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = paymentDate.AddDays(-2),
            SourceType = "OpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Opening cash",
            IdempotencyKey = $"T03-PAYMENT-OPENING:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(cash.Id, 500m),
                PostingLine.Credit(equity.Id, 500m)
            ]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = paymentDate.AddDays(-1),
            SourceType = "AffiliateCommissionEarned",
            SourceId = Guid.NewGuid(),
            Description = "Accrued affiliate commission",
            IdempotencyKey = $"T03-PAYMENT-ACCRUAL:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(affiliateExpense.Id, 120m),
                PostingLine.Credit(affiliatePayable.Id, 120m)
            ]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = paymentDate,
            SourceType = "AffiliateCommissionPaid",
            SourceId = Guid.NewGuid(),
            Description = "Paid affiliate commission",
            IdempotencyKey = $"T03-AFFILIATE-PAYMENT:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(affiliatePayable.Id, 120m),
                PostingLine.Credit(cash.Id, 120m)
            ]
        });
        _db.TreasuryAccounts.Add(TreasuryAccount.CreateCashbox(_tenantId, cash.Id, "Cashbox"));
        await _db.SaveChangesAsync();

        var report = await reportService.GetCashFlowAsync(_tenantId, paymentDate, paymentDate);

        Assert.Equal(0m, report.NetProfit);
        Assert.Equal(-120m, Assert.Single(report.WorkingCapitalChanges).Amount);
        Assert.Equal(-120m, report.OperatingCashFlow);
        Assert.Equal(500m, report.OpeningCashBalance);
        Assert.Equal(380m, report.ClosingCashBalance);
        Assert.Equal(-120m, report.NetCashChange);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_CurrentOperatingCategories_ReconcileWithoutDoubleCounting()
    {
        var cash = Account.Create(_tenantId, AccountCodes.CashboxMain, "Cashbox", AccountType.Asset);
        var revenue = Account.Create(_tenantId, AccountCodes.PosSales, "Sales", AccountType.Revenue);
        var expense = Account.Create(_tenantId, AccountCodes.Expenses, "Expenses", AccountType.Expense);
        var inventory = Account.Create(_tenantId, AccountCodes.Inventory, "Inventory", AccountType.Asset);
        var inputTax = Account.Create(_tenantId, AccountCodes.InputTaxReceivable, "Input Tax", AccountType.Asset);
        var receivable = Account.Create(_tenantId, AccountCodes.AccountsReceivable, "Receivable", AccountType.Asset);
        var marketplaceReceivable = Account.Create(
            _tenantId,
            AccountCodes.MarketplaceVendorReceivable,
            "Marketplace Receivable",
            AccountType.Asset);
        var supplierPayable = Account.Create(_tenantId, AccountCodes.SupplierPayables, "Supplier Payable", AccountType.Liability);
        var customerDeposits = Account.Create(_tenantId, AccountCodes.CustomerDeposits, "Customer Deposits", AccountType.Liability);
        var suspense = Account.Create(_tenantId, AccountCodes.TreasurySuspense, "Treasury Suspense", AccountType.Liability);
        var marketplacePayable = Account.Create(
            _tenantId,
            AccountCodes.MarketplaceVendorPayable,
            "Marketplace Payable",
            AccountType.Liability);
        var salesTax = Account.Create(_tenantId, AccountCodes.SalesTaxPayable, "Sales Tax", AccountType.Liability);
        _db.LedgerAccounts.AddRange(
            cash,
            revenue,
            expense,
            inventory,
            inputTax,
            receivable,
            marketplaceReceivable,
            supplierPayable,
            customerDeposits,
            suspense,
            marketplacePayable,
            salesTax);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportService = new FinancialReportService(_db, NullLogger<FinancialReportService>.Instance);
        var reportDate = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "SalesInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Cash sale with output tax",
            IdempotencyKey = $"T03-CASH-SALE:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(cash.Id, 115m),
                PostingLine.Credit(revenue.Id, 100m),
                PostingLine.Credit(salesTax.Id, 15m)
            ]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "SalesInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Credit sale",
            IdempotencyKey = $"T03-CREDIT-SALE:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(receivable.Id, 50m),
                PostingLine.Credit(revenue.Id, 50m)
            ]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "PurchaseInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Inventory and input tax accrued to supplier",
            IdempotencyKey = $"T03-PURCHASE:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(inventory.Id, 40m),
                PostingLine.Debit(inputTax.Id, 6m),
                PostingLine.Credit(supplierPayable.Id, 46m)
            ]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "CustomerPayment",
            SourceId = Guid.NewGuid(),
            Description = "Customer deposit",
            IdempotencyKey = $"T03-DEPOSIT:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(cash.Id, 30m),
                PostingLine.Credit(customerDeposits.Id, 30m)
            ]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "CommerceHubFinancialTransaction",
            SourceId = Guid.NewGuid(),
            Description = "Marketplace receivable timing entry",
            IdempotencyKey = $"T03-MARKETPLACE-RECEIVABLE:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(marketplaceReceivable.Id, 20m),
                PostingLine.Credit(suspense.Id, 20m)
            ]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "CommerceHubFinancialTransaction",
            SourceId = Guid.NewGuid(),
            Description = "Marketplace payable accrual",
            IdempotencyKey = $"T03-MARKETPLACE-PAYABLE:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(expense.Id, 25m),
                PostingLine.Credit(marketplacePayable.Id, 25m)
            ]
        });
        _db.TreasuryAccounts.Add(TreasuryAccount.CreateCashbox(_tenantId, cash.Id, "Cashbox"));
        await _db.SaveChangesAsync();

        var report = await reportService.GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(125m, report.NetProfit);
        Assert.Equal(-70m, Assert.Single(
            report.WorkingCapitalChanges,
            adjustment => adjustment.Description == "Change in Accounts Receivable").Amount);
        Assert.Equal(-46m, Assert.Single(
            report.WorkingCapitalChanges,
            adjustment => adjustment.Description == "Change in Inventory").Amount);
        Assert.Equal(136m, Assert.Single(
            report.WorkingCapitalChanges,
            adjustment => adjustment.Description == "Change in Accounts Payable").Amount);
        Assert.Equal(145m, report.OperatingCashFlow);
        Assert.Equal(145m, report.ClosingCashBalance);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_ExcludesForeignTenantAffiliateAccrual()
    {
        var foreignTenantId = Guid.NewGuid();
        var foreignExpense = Account.Create(
            foreignTenantId,
            AccountCodes.AffiliateMarketingExpense,
            "Foreign Affiliate Expense",
            AccountType.Expense);
        var foreignPayable = Account.Create(
            foreignTenantId,
            AccountCodes.AffiliatePayable,
            "Foreign Affiliate Payable",
            AccountType.Liability);
        _db.LedgerAccounts.AddRange(foreignExpense, foreignPayable);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportService = new FinancialReportService(_db, NullLogger<FinancialReportService>.Instance);
        var reportDate = new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = foreignTenantId,
            JournalDate = reportDate,
            SourceType = "AffiliateCommissionEarned",
            SourceId = Guid.NewGuid(),
            Description = "Foreign affiliate accrual",
            IdempotencyKey = $"T03-FOREIGN-AFFILIATE:{foreignTenantId}",
            Lines =
            [
                PostingLine.Debit(foreignExpense.Id, 900m),
                PostingLine.Credit(foreignPayable.Id, 900m)
            ]
        });

        var report = await reportService.GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(0m, report.NetProfit);
        Assert.Empty(report.WorkingCapitalChanges);
        Assert.Equal(0m, report.NetCashChange);
        Assert.Equal(0m, report.ClosingCashBalance);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task ClearingDashboardAndAging_AgreeWithTenantLedger_WhenCachesAreReconciled()
    {
        var journalDate = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
        var asOfDate = journalDate.AddDays(2);
        var visaClearing = Account.Create(
            _tenantId,
            AccountCodes.VisaClearing,
            "Visa Clearing",
            AccountType.Asset,
            isClearing: true);
        var codClearing = Account.Create(
            _tenantId,
            AccountCodes.CodClearing,
            "COD Clearing",
            AccountType.Asset,
            isClearing: true);
        var walletClearing = Account.Create(
            _tenantId,
            AccountCodes.WalletClearing,
            "Wallet Clearing",
            AccountType.Asset,
            isClearing: true);
        var revenue = Account.Create(
            _tenantId,
            AccountCodes.EcommerceSales,
            "E-commerce Sales",
            AccountType.Revenue);
        var foreignClearing = Account.Create(
            Guid.NewGuid(),
            "1199",
            "Foreign Clearing",
            AccountType.Asset,
            isClearing: true);
        foreignClearing.SetOpeningBalance(2040m);

        _db.LedgerAccounts.AddRange(
            visaClearing,
            codClearing,
            walletClearing,
            revenue,
            foreignClearing);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = journalDate,
            SourceType = "T02ClearingFixture",
            SourceId = Guid.NewGuid(),
            Description = "Controlled clearing collections",
            IdempotencyKey = $"T02-CLEARING-COLLECTIONS:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(visaClearing.Id, 165m),
                PostingLine.Debit(codClearing.Id, 400m),
                PostingLine.Credit(revenue.Id, 565m)
            ]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = journalDate.AddDays(1),
            SourceType = "T02ClearingFixture",
            SourceId = Guid.NewGuid(),
            Description = "Controlled pre-settlement refunds",
            IdempotencyKey = $"T02-CLEARING-REFUNDS:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(revenue.Id, 510m),
                PostingLine.Credit(visaClearing.Id, 110m),
                PostingLine.Credit(codClearing.Id, 400m)
            ]
        });

        var authoritativeLedgerBalance = await _db.LedgerEntries
            .IgnoreQueryFilters()
            .Where(line =>
                line.TenantId == _tenantId &&
                line.Account.IsClearing &&
                (line.JournalEntry.Status == JournalStatus.Posted ||
                 line.JournalEntry.Status == JournalStatus.Reversed) &&
                line.JournalEntry.JournalDate <= asOfDate)
            .SumAsync(line => line.DebitAmount - line.CreditAmount);
        var dashboard = await new TreasuryReadService(
                _db,
                NullLogger<TreasuryReadService>.Instance)
            .GetDashboardAsync(_tenantId, journalDate, asOfDate);
        var aging = await new FinancialReportService(
                _db,
                NullLogger<FinancialReportService>.Instance)
            .GetClearingAgingAsync(_tenantId, asOfDate);

        Assert.Equal(authoritativeLedgerBalance, dashboard.TotalPendingClearing);
        Assert.Equal(authoritativeLedgerBalance, aging.TotalPending);
        Assert.Equal(
            dashboard.ClearingAccounts.Select(account => account.AccountCode),
            aging.Lines.Select(line => line.AccountCode));
        Assert.DoesNotContain(dashboard.ClearingAccounts, account => account.AccountCode == foreignClearing.AccountCode);
        Assert.DoesNotContain(aging.Lines, line => line.AccountCode == foreignClearing.AccountCode);
    }

    [Fact]
    public async Task GetAccountSummariesAsync_UnmappedSystemAccount_IsNotTreasury()
    {
        var account = await AddAccountAsync("T05-A", AccountType.Asset, isSystem: true);

        var summary = Assert.Single(await CreateReportService().GetAccountSummariesAsync(_tenantId));

        Assert.Equal(account.Id, summary.AccountId);
        Assert.Equal("T05-A", summary.AccountCode);
        Assert.Equal("T05 A", summary.AccountName);
        Assert.Equal(nameof(AccountType.Asset), summary.AccountType);
        Assert.Equal(0m, summary.CurrentBalance);
        Assert.False(summary.IsTreasury);
    }

    [Fact]
    public async Task GetAccountSummariesAsync_MappedAssets_AreTreasuryRegardlessOfSystemFlag()
    {
        var systemAccount = await AddAccountAsync("T05-B", AccountType.Asset, isSystem: true);
        var nonSystemAccount = await AddAccountAsync("T05-C", AccountType.Asset);
        await AddTreasuryMappingAsync(systemAccount, TreasuryAccountType.Cashbox);
        await AddTreasuryMappingAsync(nonSystemAccount, TreasuryAccountType.Bank);

        var summaries = await CreateReportService().GetAccountSummariesAsync(_tenantId);

        Assert.True(Assert.Single(summaries, row => row.AccountId == systemAccount.Id).IsTreasury);
        Assert.True(Assert.Single(summaries, row => row.AccountId == nonSystemAccount.Id).IsTreasury);
    }

    [Fact]
    public async Task GetAccountSummariesAsync_ForeignTenantMapping_DoesNotClassifyAccount()
    {
        var account = await AddAccountAsync("T05-D", AccountType.Asset);
        await AddTreasuryMappingAsync(account, TreasuryAccountType.Bank, Guid.NewGuid());

        var summary = Assert.Single(await CreateReportService().GetAccountSummariesAsync(_tenantId));

        Assert.False(summary.IsTreasury);
    }

    [Fact]
    public async Task GetAccountSummariesAsync_InactiveOrDeletedMappings_DoNotClassifyAccounts()
    {
        var inactiveAccount = await AddAccountAsync("T05-E", AccountType.Asset);
        var deletedAccount = await AddAccountAsync("T05-F", AccountType.Asset);
        var inactiveMapping = await AddTreasuryMappingAsync(inactiveAccount, TreasuryAccountType.Bank);
        var deletedMapping = await AddTreasuryMappingAsync(deletedAccount, TreasuryAccountType.Clearing);
        inactiveMapping.IsActive = false;
        deletedMapping.IsDeleted = true;
        await _db.SaveChangesAsync();

        var summaries = await CreateReportService().GetAccountSummariesAsync(_tenantId);

        Assert.False(Assert.Single(summaries, row => row.AccountId == inactiveAccount.Id).IsTreasury);
        Assert.False(Assert.Single(summaries, row => row.AccountId == deletedAccount.Id).IsTreasury);
    }

    [Fact]
    public async Task GetAccountSummariesAsync_InactiveOrDeletedAccounts_AreNotClassified()
    {
        var inactiveAccount = await AddAccountAsync("T05-G", AccountType.Asset);
        var deletedAccount = await AddAccountAsync("T05-H", AccountType.Asset);
        await AddTreasuryMappingAsync(inactiveAccount, TreasuryAccountType.Bank);
        await AddTreasuryMappingAsync(deletedAccount, TreasuryAccountType.Clearing);
        inactiveAccount.IsActive = false;
        deletedAccount.IsDeleted = true;
        await _db.SaveChangesAsync();

        var summaries = await CreateReportService().GetAccountSummariesAsync(_tenantId);

        Assert.False(Assert.Single(summaries, row => row.AccountId == inactiveAccount.Id).IsTreasury);
        Assert.False(Assert.Single(summaries, row => row.AccountId == deletedAccount.Id).IsTreasury);
    }

    [Fact]
    public async Task GetAccountSummariesAsync_NonAssetMapping_DoesNotClassifyAccount()
    {
        var account = await AddAccountAsync("T05-I", AccountType.Liability);
        await AddTreasuryMappingAsync(account, TreasuryAccountType.Bank);

        var summary = Assert.Single(await CreateReportService().GetAccountSummariesAsync(_tenantId));

        Assert.False(summary.IsTreasury);
    }

    [Fact]
    public async Task GetAccountSummariesAsync_UnsupportedTreasuryType_DoesNotClassifyAccount()
    {
        var account = await AddAccountAsync("T05-I2", AccountType.Asset);
        _db.TreasuryAccounts.Add(new TreasuryAccount
        {
            TenantId = _tenantId,
            AccountId = account.Id,
            Type = (TreasuryAccountType)999,
            Name = "Unsupported",
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var summary = Assert.Single(await CreateReportService().GetAccountSummariesAsync(_tenantId));

        Assert.False(summary.IsTreasury);
    }

    [Fact]
    public async Task GetAccountSummariesAsync_MultipleTenants_UsesOnlyRequestedTenantAuthority()
    {
        var tenantAccount = await AddAccountAsync("T05-J", AccountType.Asset);
        var foreignTenantId = Guid.NewGuid();
        var foreignAccount = Account.Create(foreignTenantId, "T05-JX", "Foreign", AccountType.Asset);
        _db.LedgerAccounts.Add(foreignAccount);
        await _db.SaveChangesAsync();
        await AddTreasuryMappingAsync(tenantAccount, TreasuryAccountType.Bank);
        await AddTreasuryMappingAsync(foreignAccount, TreasuryAccountType.Bank, foreignTenantId);

        var summaries = await CreateReportService().GetAccountSummariesAsync(_tenantId);

        var summary = Assert.Single(summaries);
        Assert.Equal(tenantAccount.Id, summary.AccountId);
        Assert.True(summary.IsTreasury);
        Assert.DoesNotContain(summaries, row => row.AccountId == foreignAccount.Id);
    }

    [Fact]
    public async Task GetCashFlowAsync_CapitalizationAndPartyOpening_ClassifiesOnlyCapitalizationAsFinancing()
    {
        var cash = Account.Create(_tenantId, AccountCodes.CashboxMain, "Cashbox", AccountType.Asset);
        var receivable = Account.Create(_tenantId, AccountCodes.AccountsReceivable, "Receivable", AccountType.Asset);
        var equity = Account.Create(_tenantId, AccountCodes.OpeningBalanceEquity, "Opening Equity", AccountType.Equity);
        _db.LedgerAccounts.AddRange(cash, receivable, equity);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportDate = new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = TreasuryInitialCapitalization.AccountingSourceType,
            SourceId = Guid.NewGuid(),
            SourceReference = "TIC-REPORT-TEST",
            Description = "Initial capitalization",
            IdempotencyKey = $"REPORT-TIC:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(cash.Id, 100m),
                PostingLine.Credit(equity.Id, 100m)
            ]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "CustomerOpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Customer opening balance",
            IdempotencyKey = $"REPORT-CUSTOMER-OPENING:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(receivable.Id, 60m),
                PostingLine.Credit(equity.Id, 60m)
            ]
        });
        _db.TreasuryAccounts.Add(TreasuryAccount.CreateCashbox(_tenantId, cash.Id, "Cashbox"));
        await _db.SaveChangesAsync();

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Empty(report.WorkingCapitalChanges);
        Assert.Equal(100m, Assert.Single(report.FinancingActivities).Amount);
        Assert.Equal(100m, report.ClosingCashBalance);
        Assert.Equal(100m, report.NetCashChange);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_PartyOpeningReversalInPeriod_HasNoCashFlowEffect()
    {
        var payable = Account.Create(_tenantId, AccountCodes.SupplierPayables, "Supplier Payable", AccountType.Liability);
        var equity = Account.Create(_tenantId, AccountCodes.OpeningBalanceEquity, "Opening Equity", AccountType.Equity);
        _db.LedgerAccounts.AddRange(payable, equity);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportDate = DateTime.UtcNow.Date;
        var original = await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate.AddDays(-1),
            SourceType = "SupplierOpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Supplier opening balance",
            IdempotencyKey = $"REPORT-SUPPLIER-OPENING:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(equity.Id, 75m),
                PostingLine.Credit(payable.Id, 75m)
            ]
        });
        await posting.ReverseJournalAsync(_tenantId, original.Id, "Correct supplier opening balance");

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Empty(report.WorkingCapitalChanges);
        Assert.Empty(report.FinancingActivities);
        Assert.Equal(0m, report.NetCashChange);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_SupplierCardFundingAndReversal_UseLedgerCashMovement()
    {
        var reportDate = DateTime.UtcNow.Date;
        var cash = Account.Create(
            _tenantId, AccountCodes.CashboxMain, "Cashbox", AccountType.Asset, preventNegative: true);
        cash.Id = Guid.NewGuid();
        var equity = Account.Create(_tenantId, AccountCodes.OpeningBalanceEquity, "Opening Equity", AccountType.Equity);
        equity.Id = Guid.NewGuid();
        var card = Account.Create(
            _tenantId, "T06C-CARD-ASSET", "Supplier card asset", AccountType.Asset, preventNegative: true);
        card.Id = Guid.NewGuid();
        card.DesignateAsSupplierCardFunding();
        var treasury = TreasuryAccount.CreateCashbox(_tenantId, cash.Id, "Cashbox");
        treasury.Id = Guid.NewGuid();
        var instrument = SupplierCardFundingInstrument.Create(
            _tenantId, "Purchasing card", card.Id, treasury.Id, "Test issuer", "1234", true);
        _db.LedgerAccounts.AddRange(cash, equity, card);
        _db.TreasuryAccounts.Add(treasury);
        _db.SupplierCardFundingInstruments.Add(instrument);
        await _db.SaveChangesAsync();

        var openingJournal = JournalEntry.Create(
            _tenantId,
            $"JE-CF-SCF-OPENING-{Guid.NewGuid():N}",
            reportDate,
            "OpeningBalance",
            Guid.NewGuid(),
            null,
            "Opening cash",
            $"CF-SCF-OPENING:{_tenantId}",
            null);
        cash.ApplyEntry(1000m, 0m);
        equity.ApplyEntry(0m, 1000m);
        _db.JournalEntries.Add(openingJournal);
        _db.LedgerEntries.AddRange(
            LedgerEntry.CreateDebit(_tenantId, openingJournal.Id, cash.Id, 1000m, 1000m, 1),
            LedgerEntry.CreateCredit(_tenantId, openingJournal.Id, equity.Id, 1000m, 1000m, 2));
        using (LedgerMutationScope.Authorize())
        {
            await _db.SaveChangesAsync();
        }

        var funding = new SupplierCardFunding
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            FundingInstrumentId = instrument.Id,
            FundingLedgerAccountId = card.Id,
            FundingSourceTreasuryAccountId = treasury.Id,
            FundingSourceLedgerAccountId = cash.Id,
            FundingInstrumentVersion = instrument.AuthorityVersion,
            FundingDate = reportDate,
            Amount = 100m,
            IdempotencyKey = "cash-flow-supplier-card-funding",
            RequestHash = new string('A', 64)
        };
        _db.SupplierCardFundings.Add(funding);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var journal = await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "SupplierCardFunding",
            SourceId = funding.Id,
            Description = "Fund supplier card",
            IdempotencyKey = $"SUPPLIER_CARD_FUNDING:{_tenantId}:{funding.Id}",
            Lines =
            [
                PostingLine.Debit(card.Id, 100m),
                PostingLine.Credit(cash.Id, 100m)
            ]
        });

        var fundedReport = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(900m, fundedReport.ClosingCashBalance);
        Assert.Equal(-100m, Assert.Single(fundedReport.InvestingActivities).Amount);
        Assert.Equal(1000m, fundedReport.FinancingCashFlow);
        Assert.Equal(900m, fundedReport.NetCashChange);
        Assert.True(fundedReport.Reconciles);

        await posting.ReverseJournalAsync(_tenantId, journal.Id, "Reverse test funding");
        var reversedReport = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(1000m, reversedReport.ClosingCashBalance);
        Assert.Empty(reversedReport.InvestingActivities);
        Assert.Equal(1000m, reversedReport.NetCashChange);
        Assert.True(reversedReport.Reconciles);
    }

    [Theory]
    [InlineData("Cash", -100)]
    [InlineData("Bank", -100)]
    [InlineData("SupplierCard", 0)]
    [InlineData("Credit", 0)]
    [InlineData("Mixed", -40)]
    public async Task GetCashFlowAsync_PurchaseSettlement_UsesOnlyPostedCashLines(
        string scenario,
        decimal expectedCashMovement)
    {
        var reportDate = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        var cash = Account.Create(_tenantId, AccountCodes.CashboxMain, "Cashbox", AccountType.Asset);
        var bank = Account.Create(_tenantId, AccountCodes.BankAccount, "Bank", AccountType.Asset);
        var card = Account.Create(_tenantId, "FUTURE-NONCASH-ASSET", "Non-cash settlement asset", AccountType.Asset);
        var inventory = Account.Create(_tenantId, AccountCodes.Inventory, "Inventory", AccountType.Asset);
        var payable = Account.Create(_tenantId, AccountCodes.SupplierPayables, "Supplier payable", AccountType.Liability);
        var equity = Account.Create(_tenantId, AccountCodes.OpeningBalanceEquity, "Opening equity", AccountType.Equity);
        _db.LedgerAccounts.AddRange(cash, bank, card, inventory, payable, equity);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate.AddDays(-1),
            SourceType = "OpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Deterministic opening assets",
            IdempotencyKey = $"CF-PURCHASE-OPENING:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(cash.Id, 100m),
                PostingLine.Debit(bank.Id, 100m),
                PostingLine.Debit(card.Id, 100m),
                PostingLine.Credit(equity.Id, 300m)
            ]
        });

        var settlementLines = scenario switch
        {
            "Cash" => new List<PostingLine> { PostingLine.Debit(inventory.Id, 100m), PostingLine.Credit(cash.Id, 100m) },
            "Bank" => new List<PostingLine> { PostingLine.Debit(inventory.Id, 100m), PostingLine.Credit(bank.Id, 100m) },
            "SupplierCard" => new List<PostingLine> { PostingLine.Debit(inventory.Id, 100m), PostingLine.Credit(card.Id, 100m) },
            "Credit" => new List<PostingLine> { PostingLine.Debit(inventory.Id, 100m), PostingLine.Credit(payable.Id, 100m) },
            "Mixed" => new List<PostingLine>
            {
                PostingLine.Debit(inventory.Id, 100m),
                PostingLine.Credit(cash.Id, 40m),
                PostingLine.Credit(card.Id, 30m),
                PostingLine.Credit(payable.Id, 30m)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "PurchaseInvoice",
            SourceId = Guid.NewGuid(),
            Description = $"{scenario} inventory purchase",
            IdempotencyKey = $"CF-PURCHASE:{_tenantId}:{scenario}",
            Lines = settlementLines
        });
        _db.TreasuryAccounts.AddRange(
            TreasuryAccount.CreateCashbox(_tenantId, cash.Id, "Cashbox"),
            TreasuryAccount.CreateBank(_tenantId, bank.Id, "Bank"));
        await _db.SaveChangesAsync();

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(expectedCashMovement, report.OperatingCashFlow);
        Assert.Equal(expectedCashMovement, report.NetCashChange);
        Assert.Equal(expectedCashMovement, report.ClosingCashBalance - report.OpeningCashBalance);
        Assert.Empty(report.InvestingActivities);
        Assert.Empty(report.FinancingActivities);
        Assert.True(report.Reconciles);
    }

    [Theory]
    [InlineData("Cash", 100)]
    [InlineData("SupplierCard", 0)]
    [InlineData("Mixed", 40)]
    public async Task GetCashFlowAsync_PurchaseReversal_NegatesOriginalPostedCashLines(
        string scenario,
        decimal expectedCashMovement)
    {
        var reportDate = DateTime.UtcNow.Date;
        var cash = Account.Create(_tenantId, AccountCodes.CashboxMain, "Cashbox", AccountType.Asset);
        var card = Account.Create(_tenantId, "REVERSAL-NONCASH-ASSET", "Non-cash settlement asset", AccountType.Asset);
        var inventory = Account.Create(_tenantId, AccountCodes.Inventory, "Inventory", AccountType.Asset);
        var payable = Account.Create(_tenantId, AccountCodes.SupplierPayables, "Supplier payable", AccountType.Liability);
        var equity = Account.Create(_tenantId, AccountCodes.OpeningBalanceEquity, "Opening equity", AccountType.Equity);
        _db.LedgerAccounts.AddRange(cash, card, inventory, payable, equity);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate.AddDays(-2),
            SourceType = "OpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Deterministic opening assets",
            IdempotencyKey = $"CF-REVERSAL-OPENING:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(cash.Id, 100m),
                PostingLine.Debit(card.Id, 100m),
                PostingLine.Credit(equity.Id, 200m)
            ]
        });
        var settlementLines = scenario switch
        {
            "Cash" => new List<PostingLine> { PostingLine.Debit(inventory.Id, 100m), PostingLine.Credit(cash.Id, 100m) },
            "SupplierCard" => new List<PostingLine> { PostingLine.Debit(inventory.Id, 100m), PostingLine.Credit(card.Id, 100m) },
            "Mixed" => new List<PostingLine>
            {
                PostingLine.Debit(inventory.Id, 100m),
                PostingLine.Credit(cash.Id, 40m),
                PostingLine.Credit(card.Id, 30m),
                PostingLine.Credit(payable.Id, 30m)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var original = await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate.AddDays(-1),
            SourceType = "PurchaseInvoice",
            SourceId = Guid.NewGuid(),
            Description = $"{scenario} inventory purchase",
            IdempotencyKey = $"CF-REVERSAL-PURCHASE:{_tenantId}:{scenario}",
            Lines = settlementLines
        });
        await posting.ReverseJournalAsync(_tenantId, original.Id, "Reverse deterministic purchase");
        _db.TreasuryAccounts.Add(TreasuryAccount.CreateCashbox(_tenantId, cash.Id, "Cashbox"));
        await _db.SaveChangesAsync();

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate.AddDays(1));

        Assert.Equal(expectedCashMovement, report.OperatingCashFlow);
        Assert.Equal(expectedCashMovement, report.NetCashChange);
        Assert.Equal(expectedCashMovement, report.ClosingCashBalance - report.OpeningCashBalance);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_IntegrationStyle_CardPurchaseMatchesExactAhramStoreDefect()
    {
        var reportDate = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        var cash = Account.Create(_tenantId, AccountCodes.CashboxMain, "Cashbox Main", AccountType.Asset);
        var card = Account.Create(_tenantId, "CARD-FUNDING-ASSET", "Supplier card funding asset", AccountType.Asset);
        var inventory = Account.Create(_tenantId, AccountCodes.Inventory, "Inventory", AccountType.Asset);
        var equity = Account.Create(_tenantId, AccountCodes.OpeningBalanceEquity, "Opening equity", AccountType.Equity);
        _db.LedgerAccounts.AddRange(cash, card, inventory, equity);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate.AddDays(-1),
            SourceType = TreasuryInitialCapitalization.AccountingSourceType,
            SourceId = Guid.NewGuid(),
            Description = "Initial capitalization",
            IdempotencyKey = $"CF-AHRAM-CAPITAL:{_tenantId}",
            Lines = [PostingLine.Debit(cash.Id, 1000m), PostingLine.Credit(equity.Id, 1000m)]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate.AddDays(-1),
            SourceType = "SupplierCardFunding",
            SourceId = Guid.NewGuid(),
            Description = "Fund supplier card",
            IdempotencyKey = $"CF-AHRAM-FUNDING:{_tenantId}",
            Lines = [PostingLine.Debit(card.Id, 100m), PostingLine.Credit(cash.Id, 100m)]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "PurchaseInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Purchase PUR-20260914-1010",
            IdempotencyKey = $"CF-AHRAM-PURCHASE:{_tenantId}",
            Lines = [PostingLine.Debit(inventory.Id, 20m), PostingLine.Credit(card.Id, 20m)]
        });
        _db.TreasuryAccounts.Add(TreasuryAccount.CreateCashbox(_tenantId, cash.Id, "Cashbox Main"));
        await _db.SaveChangesAsync();

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate.AddDays(-13), reportDate.AddDays(1));

        Assert.Equal(0m, report.OpeningCashBalance);
        Assert.Equal(900m, report.ClosingCashBalance);
        Assert.Equal(0m, report.OperatingCashFlow);
        Assert.Equal(-100m, report.InvestingCashFlow);
        Assert.Equal(1000m, report.FinancingCashFlow);
        Assert.Equal(900m, report.NetCashChange);
        Assert.Equal(-20m, Assert.Single(
            report.WorkingCapitalChanges,
            adjustment => adjustment.Description == "Change in Inventory").Amount);
        Assert.Equal(20m, Assert.Single(
            report.WorkingCapitalChanges,
            adjustment => adjustment.Description == "Ledger-derived non-cash settlement adjustment").Amount);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_ForeignTenantCardPurchase_DoesNotAffectRequestedTenant()
    {
        var foreignTenantId = Guid.NewGuid();
        var foreignCard = Account.Create(foreignTenantId, "FOREIGN-CARD-ASSET", "Foreign card", AccountType.Asset);
        var foreignInventory = Account.Create(foreignTenantId, AccountCodes.Inventory, "Foreign inventory", AccountType.Asset);
        _db.LedgerAccounts.AddRange(foreignCard, foreignInventory);
        await _db.SaveChangesAsync();
        var reportDate = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = foreignTenantId,
            JournalDate = reportDate,
            SourceType = "PurchaseInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Foreign card purchase",
            IdempotencyKey = $"CF-FOREIGN-CARD:{foreignTenantId}",
            Lines = [PostingLine.Debit(foreignInventory.Id, 20m), PostingLine.Credit(foreignCard.Id, 20m)]
        });

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Empty(report.WorkingCapitalChanges);
        Assert.Equal(0m, report.NetCashChange);
        Assert.Equal(0m, report.ClosingCashBalance);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_CashboxToBankTransfer_NetsToZero()
    {
        var reportDate = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        var cash = Account.Create(_tenantId, AccountCodes.CashboxMain, "Cashbox", AccountType.Asset);
        var bank = Account.Create(_tenantId, AccountCodes.BankAccount, "Bank", AccountType.Asset);
        var equity = Account.Create(_tenantId, AccountCodes.OpeningBalanceEquity, "Opening equity", AccountType.Equity);
        _db.LedgerAccounts.AddRange(cash, bank, equity);
        await _db.SaveChangesAsync();
        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate.AddDays(-1),
            SourceType = "OpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Opening cash",
            IdempotencyKey = $"CF-TRANSFER-OPENING:{_tenantId}",
            Lines = [PostingLine.Debit(cash.Id, 100m), PostingLine.Credit(equity.Id, 100m)]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "TreasuryTransfer",
            SourceId = Guid.NewGuid(),
            Description = "Cashbox to bank",
            IdempotencyKey = $"CF-TRANSFER:{_tenantId}",
            Lines = [PostingLine.Debit(bank.Id, 100m), PostingLine.Credit(cash.Id, 100m)]
        });
        _db.TreasuryAccounts.AddRange(
            TreasuryAccount.CreateCashbox(_tenantId, cash.Id, "Cashbox"),
            TreasuryAccount.CreateBank(_tenantId, bank.Id, "Bank"));
        await _db.SaveChangesAsync();

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(100m, report.OpeningCashBalance);
        Assert.Equal(100m, report.ClosingCashBalance);
        Assert.Equal(0m, report.NetCashChange);
        Assert.Empty(report.InvestingActivities);
        Assert.Empty(report.FinancingActivities);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_UsesJournalDateInsteadOfPostingTimestamp()
    {
        var reportDate = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        var card = Account.Create(_tenantId, "DATED-CARD-ASSET", "Dated card", AccountType.Asset);
        var inventory = Account.Create(_tenantId, AccountCodes.Inventory, "Inventory", AccountType.Asset);
        _db.LedgerAccounts.AddRange(card, inventory);
        await _db.SaveChangesAsync();
        await new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance)
            .PostJournalAsync(new PostingRequest
            {
                TenantId = _tenantId,
                JournalDate = reportDate.AddDays(-1),
                SourceType = "PurchaseInvoice",
                SourceId = Guid.NewGuid(),
                Description = "Prior journal-date card purchase",
                IdempotencyKey = $"CF-JOURNAL-DATE:{_tenantId}",
                Lines = [PostingLine.Debit(inventory.Id, 20m), PostingLine.Credit(card.Id, 20m)]
            });

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Empty(report.WorkingCapitalChanges);
        Assert.Equal(0m, report.NetCashChange);
        Assert.True(report.Reconciles);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T-11 CONTRACT: mapped Cashbox/Bank only, no prefix fallback
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetCashFlowAsync_CustomNonnumericMappedCashboxAndBank_AreCashEquivalents()
    {
        var cash = await AddAccountAsync("CASH-DESK-A", AccountType.Asset);
        var bank = await AddAccountAsync("BANK-MAIN-A", AccountType.Asset);
        var equity = await AddAccountAsync(AccountCodes.OpeningBalanceEquity, AccountType.Equity);
        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportDate = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "OpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Custom mapped opening",
            IdempotencyKey = $"CF-CUSTOM-MAPPED:{_tenantId}",
            Lines = [PostingLine.Debit(cash.Id, 60m), PostingLine.Debit(bank.Id, 40m), PostingLine.Credit(equity.Id, 100m)]
        });
        await AddTreasuryMappingAsync(cash, TreasuryAccountType.Cashbox);
        await AddTreasuryMappingAsync(bank, TreasuryAccountType.Bank);

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(0m, report.OpeningCashBalance);
        Assert.Equal(100m, report.ClosingCashBalance);
        Assert.Equal(100m, report.NetCashChange);
        Assert.Equal(100m, Assert.Single(report.FinancingActivities).Amount);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_UnmappedPrefixAssets_AreExcludedFromCash()
    {
        var posVisa = await AddAccountAsync(AccountCodes.PosVisaAccount, AccountType.Asset); // 1020
        var legacy = await AddAccountAsync("1000", AccountType.Asset);
        var reserve = await AddAccountAsync(AccountCodes.CashFloatReserve, AccountType.Asset); // 1040
        var arbitrary = await AddAccountAsync("1099", AccountType.Asset);
        var equity = await AddAccountAsync(AccountCodes.OpeningBalanceEquity, AccountType.Equity);
        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportDate = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "OpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Unmapped prefix assets",
            IdempotencyKey = $"CF-UNMAPPED-PREFIX:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(posVisa.Id, 10m),
                PostingLine.Debit(legacy.Id, 20m),
                PostingLine.Debit(reserve.Id, 30m),
                PostingLine.Debit(arbitrary.Id, 40m),
                PostingLine.Credit(equity.Id, 100m)
            ]
        });

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(0m, report.OpeningCashBalance);
        Assert.Equal(0m, report.ClosingCashBalance);
        Assert.Empty(report.InvestingActivities);
        Assert.Empty(report.FinancingActivities);
        Assert.Equal(0m, report.NetCashChange);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_MappedClearing_IsExcludedFromCash()
    {
        var clearing = Account.Create(_tenantId, AccountCodes.VisaClearing, "Visa Clearing", AccountType.Asset, isClearing: true);
        var equity = await AddAccountAsync(AccountCodes.OpeningBalanceEquity, AccountType.Equity);
        _db.LedgerAccounts.Add(clearing);
        await _db.SaveChangesAsync();
        await AddTreasuryMappingAsync(clearing, TreasuryAccountType.Clearing);
        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportDate = new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "OpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Mapped clearing funding",
            IdempotencyKey = $"CF-MAPPED-CLEARING:{_tenantId}",
            Lines = [PostingLine.Debit(clearing.Id, 200m), PostingLine.Credit(equity.Id, 200m)]
        });

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(0m, report.ClosingCashBalance);
        Assert.Empty(report.FinancingActivities);
        Assert.Empty(report.InvestingActivities);
        Assert.Equal(0m, report.NetCashChange);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_SupplierCardAndArbitraryAssets_AreNonCash()
    {
        var cash = await AddAccountAsync("CASH-SUP-A", AccountType.Asset);
        var card = Account.Create(_tenantId, "SUP-CARD-A", "Supplier card", AccountType.Asset);
        card.DesignateAsSupplierCardFunding();
        _db.LedgerAccounts.Add(card);
        await _db.SaveChangesAsync();
        var arbitrary = await AddAccountAsync("77XX-ASSET", AccountType.Asset);
        var equity = await AddAccountAsync(AccountCodes.OpeningBalanceEquity, AccountType.Equity);
        var inventory = await AddAccountAsync(AccountCodes.Inventory, AccountType.Asset);
        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportDate = new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate.AddDays(-1),
            SourceType = "OpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Opening incl non-cash assets",
            IdempotencyKey = $"CF-NONCASH-OPENING:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(cash.Id, 100m),
                PostingLine.Debit(card.Id, 50m),
                PostingLine.Debit(arbitrary.Id, 70m),
                PostingLine.Credit(equity.Id, 220m)
            ]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "PurchaseInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Card purchase has no cash impact",
            IdempotencyKey = $"CF-NONCASH-PURCHASE:{_tenantId}",
            Lines = [PostingLine.Debit(inventory.Id, 30m), PostingLine.Credit(card.Id, 30m)]
        });
        await AddTreasuryMappingAsync(cash, TreasuryAccountType.Cashbox);

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(100m, report.OpeningCashBalance);
        Assert.Equal(100m, report.ClosingCashBalance);
        Assert.Equal(0m, report.NetCashChange);
        Assert.Empty(report.InvestingActivities);
        Assert.Empty(report.FinancingActivities);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_InvalidMappings_AreExcludedFromCash()
    {
        var cash = await AddAccountAsync("CASH-VALID", AccountType.Asset);
        var inactiveMapped = await AddAccountAsync("CASH-INACTIVE-MAP", AccountType.Asset);
        var deletedMapped = await AddAccountAsync("CASH-DELETED-MAP", AccountType.Asset);
        var foreignMapped = await AddAccountAsync("CASH-FOREIGN-MAP", AccountType.Asset);
        var nonAsset = await AddAccountAsync("NONASSET-MAP", AccountType.Liability);
        var unsupported = await AddAccountAsync("CASH-UNSUPPORTED", AccountType.Asset);
        var inactiveAccount = await AddAccountAsync("CASH-INACTIVE-ACCT", AccountType.Asset);
        var deletedAccount = await AddAccountAsync("CASH-DELETED-ACCT", AccountType.Asset);

        var equity = await AddAccountAsync(AccountCodes.OpeningBalanceEquity, AccountType.Equity);
        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportDate = new DateTime(2026, 8, 9, 0, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "OpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Only valid cash counts",
            IdempotencyKey = $"CF-INVALID-MAP:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(cash.Id, 25m),
                PostingLine.Debit(inactiveMapped.Id, 11m),
                PostingLine.Debit(deletedMapped.Id, 12m),
                PostingLine.Debit(unsupported.Id, 13m),
                PostingLine.Debit(inactiveAccount.Id, 14m),
                PostingLine.Debit(deletedAccount.Id, 15m),
                PostingLine.Credit(equity.Id, 90m)
            ]
        });
        await AddTreasuryMappingAsync(cash, TreasuryAccountType.Cashbox);
        var inactiveMapping = await AddTreasuryMappingAsync(inactiveMapped, TreasuryAccountType.Cashbox);
        var deletedMapping = await AddTreasuryMappingAsync(deletedMapped, TreasuryAccountType.Bank);
        inactiveMapping.IsActive = false;
        deletedMapping.IsDeleted = true;
        await AddTreasuryMappingAsync(foreignMapped, TreasuryAccountType.Cashbox, Guid.NewGuid());
        await AddTreasuryMappingAsync(nonAsset, TreasuryAccountType.Bank);
        _db.TreasuryAccounts.Add(new TreasuryAccount
        {
            TenantId = _tenantId,
            AccountId = unsupported.Id,
            Type = (TreasuryAccountType)999,
            Name = "Unsupported",
            IsActive = true
        });
        await AddTreasuryMappingAsync(inactiveAccount, TreasuryAccountType.Cashbox);
        await AddTreasuryMappingAsync(deletedAccount, TreasuryAccountType.Bank);
        inactiveAccount.IsActive = false;
        deletedAccount.IsDeleted = true;
        await _db.SaveChangesAsync();

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(25m, report.ClosingCashBalance);
        Assert.Equal(25m, report.NetCashChange);
        Assert.Equal(25m, Assert.Single(report.FinancingActivities).Amount);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_ClearingCollection_HasNoCashImpact()
    {
        var cash = await AddAccountAsync("CASH-COLL", AccountType.Asset);
        await AddTreasuryMappingAsync(cash, TreasuryAccountType.Cashbox);
        var clearing = Account.Create(_tenantId, "CLEAR-COLL-A", "Collection clearing", AccountType.Asset, isClearing: true);
        _db.LedgerAccounts.Add(clearing);
        await _db.SaveChangesAsync();
        var revenue = await AddAccountAsync(AccountCodes.EcommerceSales, AccountType.Revenue);
        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportDate = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "SalesInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Clearing collection",
            IdempotencyKey = $"CF-CLEARING-COLLECT:{_tenantId}",
            Lines = [PostingLine.Debit(clearing.Id, 300m), PostingLine.Credit(revenue.Id, 300m)]
        });

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(0m, report.OpeningCashBalance);
        Assert.Equal(0m, report.ClosingCashBalance);
        Assert.Empty(report.InvestingActivities);
        Assert.Empty(report.FinancingActivities);
        Assert.Equal(300m, report.NetProfit);
        Assert.Equal(0m, report.OperatingCashFlow);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_ClearingSettlementToBank_IsOperatingNotInvesting()
    {
        var bank = await AddAccountAsync("BANK-SETTLE", AccountType.Asset);
        var clearing = Account.Create(_tenantId, "CLEAR-SETTLE-A", "Settlement clearing", AccountType.Asset, isClearing: true);
        _db.LedgerAccounts.Add(clearing);
        await _db.SaveChangesAsync();
        var revenue = await AddAccountAsync(AccountCodes.EcommerceSales, AccountType.Revenue);
        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportDate = new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate.AddDays(-1),
            SourceType = "SalesInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Clearing collection",
            IdempotencyKey = $"CF-SETTLE-COLLECT:{_tenantId}",
            Lines = [PostingLine.Debit(clearing.Id, 300m), PostingLine.Credit(revenue.Id, 300m)]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "Settlement",
            SourceId = Guid.NewGuid(),
            Description = "Clearing to bank settlement",
            IdempotencyKey = $"CF-SETTLE-BANK:{_tenantId}",
            Lines = [PostingLine.Debit(bank.Id, 300m), PostingLine.Credit(clearing.Id, 300m)]
        });
        await AddTreasuryMappingAsync(bank, TreasuryAccountType.Bank);

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(0m, report.OpeningCashBalance);
        Assert.Equal(300m, report.ClosingCashBalance);
        Assert.Equal(300m, report.NetCashChange);
        Assert.Empty(report.InvestingActivities);
        Assert.Empty(report.FinancingActivities);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_ClearingSettlementWithFees_IsOperatingNotInvesting()
    {
        var bank = await AddAccountAsync("BANK-SETTLE-FEE", AccountType.Asset);
        var clearing = Account.Create(_tenantId, "CLEAR-SETTLE-FEE", "Settlement clearing", AccountType.Asset, isClearing: true);
        _db.LedgerAccounts.Add(clearing);
        await _db.SaveChangesAsync();
        var fees = await AddAccountAsync(AccountCodes.GatewayFees, AccountType.Expense);
        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportDate = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "Settlement",
            SourceId = Guid.NewGuid(),
            Description = "Clearing settlement with fees",
            IdempotencyKey = $"CF-SETTLE-FEE:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(bank.Id, 95m),
                PostingLine.Debit(fees.Id, 5m),
                PostingLine.Credit(clearing.Id, 100m)
            ]
        });
        await AddTreasuryMappingAsync(bank, TreasuryAccountType.Bank);

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(95m, report.ClosingCashBalance);
        Assert.Equal(95m, report.NetCashChange);
        Assert.Empty(report.InvestingActivities);
        Assert.Empty(report.FinancingActivities);
        Assert.Equal(-5m, report.NetProfit);
        Assert.Equal(95m, report.OperatingCashFlow);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_InternalTransfer_NetsToZero()
    {
        var cash = await AddAccountAsync("CASH-XFER-A", AccountType.Asset);
        var bank = await AddAccountAsync("BANK-XFER-A", AccountType.Asset);
        var equity = await AddAccountAsync(AccountCodes.OpeningBalanceEquity, AccountType.Equity);
        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportDate = new DateTime(2026, 8, 13, 0, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate.AddDays(-1),
            SourceType = "OpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Opening cash",
            IdempotencyKey = $"CF-XFER-OPENING:{_tenantId}",
            Lines = [PostingLine.Debit(cash.Id, 80m), PostingLine.Credit(equity.Id, 80m)]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "TreasuryTransfer",
            SourceId = Guid.NewGuid(),
            Description = "Internal transfer",
            IdempotencyKey = $"CF-XFER:{_tenantId}",
            Lines = [PostingLine.Debit(bank.Id, 80m), PostingLine.Credit(cash.Id, 80m)]
        });
        await AddTreasuryMappingAsync(cash, TreasuryAccountType.Cashbox);
        await AddTreasuryMappingAsync(bank, TreasuryAccountType.Bank);

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate, reportDate);

        Assert.Equal(80m, report.OpeningCashBalance);
        Assert.Equal(80m, report.ClosingCashBalance);
        Assert.Equal(0m, report.NetCashChange);
        Assert.Empty(report.InvestingActivities);
        Assert.Empty(report.FinancingActivities);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_TenantDateAndReversalBoundaries_AreRespected()
    {
        var cash = await AddAccountAsync("CASH-BOUND", AccountType.Asset);
        var revenue = await AddAccountAsync(AccountCodes.PosSales, AccountType.Revenue);
        var equity = await AddAccountAsync(AccountCodes.OpeningBalanceEquity, AccountType.Equity);
        var foreignTenantId = Guid.NewGuid();
        var foreignCash = Account.Create(foreignTenantId, "FOREIGN-CASH", "Foreign cash", AccountType.Asset);
        var foreignRevenue = Account.Create(foreignTenantId, AccountCodes.PosSales, "Foreign sales", AccountType.Revenue);
        _db.LedgerAccounts.AddRange(foreignCash, foreignRevenue);
        await _db.SaveChangesAsync();
        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var fromDate = DateTime.UtcNow.Date;
        var toDate = DateTime.UtcNow.Date;
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = foreignTenantId,
            JournalDate = fromDate,
            SourceType = "SalesInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Foreign cash sale",
            IdempotencyKey = $"CF-BOUND-FOREIGN:{foreignTenantId}",
            Lines = [PostingLine.Debit(foreignCash.Id, 500m), PostingLine.Credit(foreignRevenue.Id, 500m)]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = fromDate.AddDays(-2),
            SourceType = "SalesInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Before-period cash sale",
            IdempotencyKey = $"CF-BOUND-BEFORE:{_tenantId}",
            Lines = [PostingLine.Debit(cash.Id, 70m), PostingLine.Credit(revenue.Id, 70m)]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = toDate.AddDays(2),
            SourceType = "SalesInvoice",
            SourceId = Guid.NewGuid(),
            Description = "After-period cash sale",
            IdempotencyKey = $"CF-BOUND-AFTER:{_tenantId}",
            Lines = [PostingLine.Debit(cash.Id, 90m), PostingLine.Credit(revenue.Id, 90m)]
        });
        var inPeriod = await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = fromDate,
            SourceType = "SalesInvoice",
            SourceId = Guid.NewGuid(),
            Description = "In-period cash sale",
            IdempotencyKey = $"CF-BOUND-IN:{_tenantId}",
            Lines = [PostingLine.Debit(cash.Id, 50m), PostingLine.Credit(revenue.Id, 50m)]
        });
        await posting.ReverseJournalAsync(_tenantId, inPeriod.Id, "Reverse in-period sale");
        await AddTreasuryMappingAsync(cash, TreasuryAccountType.Cashbox);

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, fromDate, toDate);

        Assert.Equal(70m, report.OpeningCashBalance);
        Assert.Equal(70m, report.ClosingCashBalance);
        Assert.Equal(0m, report.NetProfit);
        Assert.Equal(0m, report.OperatingCashFlow);
        Assert.Equal(0m, report.NetCashChange);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetCashFlowAsync_SettlementAdjustment_UsesTruthfulNonPrefixLabel()
    {
        var cash = await AddAccountAsync("CASH-LABEL", AccountType.Asset);
        var card = await AddAccountAsync("CARD-LABEL", AccountType.Asset);
        var inventory = await AddAccountAsync(AccountCodes.Inventory, AccountType.Asset);
        var equity = await AddAccountAsync(AccountCodes.OpeningBalanceEquity, AccountType.Equity);
        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportDate = new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate.AddDays(-1),
            SourceType = TreasuryInitialCapitalization.AccountingSourceType,
            SourceId = Guid.NewGuid(),
            Description = "Capitalization",
            IdempotencyKey = $"CF-LABEL-CAP:{_tenantId}",
            Lines = [PostingLine.Debit(cash.Id, 1000m), PostingLine.Credit(equity.Id, 1000m)]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate.AddDays(-1),
            SourceType = "SupplierCardFunding",
            SourceId = Guid.NewGuid(),
            Description = "Fund card",
            IdempotencyKey = $"CF-LABEL-FUND:{_tenantId}",
            Lines = [PostingLine.Debit(card.Id, 100m), PostingLine.Credit(cash.Id, 100m)]
        });
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = reportDate,
            SourceType = "PurchaseInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Card purchase",
            IdempotencyKey = $"CF-LABEL-PURCHASE:{_tenantId}",
            Lines = [PostingLine.Debit(inventory.Id, 20m), PostingLine.Credit(card.Id, 20m)]
        });
        await AddTreasuryMappingAsync(cash, TreasuryAccountType.Cashbox);

        var report = await CreateReportService().GetCashFlowAsync(_tenantId, reportDate.AddDays(-13), reportDate.AddDays(1));

        var adjustment = Assert.Single(
            report.WorkingCapitalChanges,
            a => a.Description == "Ledger-derived non-cash settlement adjustment");
        Assert.Equal("Mapped Cashbox/Bank", adjustment.AccountCode);
        Assert.DoesNotContain("10xx", report.WorkingCapitalChanges.SelectMany(a => new[] { a.AccountCode ?? string.Empty, a.Description ?? string.Empty }));
        Assert.DoesNotContain("11xx", report.WorkingCapitalChanges.SelectMany(a => new[] { a.AccountCode ?? string.Empty, a.Description ?? string.Empty }));
        Assert.Equal(20m, adjustment.Amount);
        Assert.True(report.Reconciles);
    }

    private FinancialReportService CreateReportService() =>
        new(_db, NullLogger<FinancialReportService>.Instance);

    private async Task<Account> AddAccountAsync(
        string accountCode,
        AccountType accountType,
        bool isSystem = false)
    {
        var account = Account.Create(
            _tenantId,
            accountCode,
            accountCode.Replace('-', ' '),
            accountType,
            isSystem: isSystem);
        _db.LedgerAccounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    private async Task<TreasuryAccount> AddTreasuryMappingAsync(
        Account account,
        TreasuryAccountType type,
        Guid? tenantId = null)
    {
        var mappingTenantId = tenantId ?? _tenantId;
        var mapping = type switch
        {
            TreasuryAccountType.Cashbox => TreasuryAccount.CreateCashbox(
                mappingTenantId,
                account.Id,
                account.Name),
            TreasuryAccountType.Bank => TreasuryAccount.CreateBank(
                mappingTenantId,
                account.Id,
                account.Name),
            TreasuryAccountType.Clearing => TreasuryAccount.CreateClearing(
                mappingTenantId,
                account.Id,
                account.Name),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        _db.TreasuryAccounts.Add(mapping);
        await _db.SaveChangesAsync();
        return mapping;
    }

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }
}
