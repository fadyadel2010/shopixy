using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Consumers.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Services.Affiliate;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Accounting;

public sealed class AffiliateReversalAccountingConsumerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ApplicationDbContext _db;
    private readonly FinancialPostingService _postingService;
    private readonly AffiliateCommissionReversedAccountingConsumer _consumer;
    private readonly Account _cash;
    private readonly Account _expense;
    private readonly Account _payable;

    public AffiliateReversalAccountingConsumerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"T07_{Guid.NewGuid()}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new ApplicationDbContext(options, new TestTenantContext(_tenantId));

        _cash = Account.Create(_tenantId, AccountCodes.CashboxMain, "Cashbox", AccountType.Asset, preventNegative: true);
        _cash.SetOpeningBalance(1_000m);
        _expense = Account.Create(
            _tenantId,
            AccountCodes.AffiliateMarketingExpense,
            "Affiliate Marketing Expense",
            AccountType.Expense);
        _payable = Account.Create(
            _tenantId,
            AccountCodes.AffiliatePayable,
            "Affiliate Payable",
            AccountType.Liability,
            preventNegative: true);

        _db.Tenants.Add(new Tenant { Id = _tenantId, Name = "T07", Currency = "EGP" });
        _db.LedgerAccounts.AddRange(_cash, _expense, _payable);
        _db.SaveChanges();

        _postingService = new FinancialPostingService(
            _db,
            NullLogger<FinancialPostingService>.Instance);
        var accountResolver = new AccountResolver(_db, NullLogger<AccountResolver>.Instance);
        _consumer = new AffiliateCommissionReversedAccountingConsumer(
            _db,
            accountResolver,
            _postingService,
            new AffiliateTelemetry(),
            NullLogger<AffiliateCommissionReversedAccountingConsumer>.Instance);
    }

    [Fact]
    public async Task FullReversal_PostsPayableDebitAndExpenseCredit_WithoutCashMovement()
    {
        var scenario = await CreateScenarioAsync(120m, 120m, includeApproval: true);
        var originalJournal = await GetEarnedJournalAsync(scenario.Earned.OrderId);
        var cashBefore = _cash.CurrentBalance;

        await _consumer.Consume(CreateConsumeContext(scenario.Event));

        var reversalJournal = await GetReversalJournalAsync(scenario.Reversal.Id);
        Assert.Equal("AffiliateCommissionReversed", reversalJournal.SourceType);
        Assert.Equal(scenario.Reversal.Id, reversalJournal.SourceId);
        Assert.Equal($"AFFILIATE_COMM_REVERSED:{scenario.Reversal.Id}", reversalJournal.SourceReference);
        Assert.Equal(JournalStatus.Posted, reversalJournal.Status);
        Assert.True(reversalJournal.IsBalanced);
        Assert.Equal(120m, reversalJournal.TotalDebits);
        Assert.Equal(120m, reversalJournal.TotalCredits);
        Assert.Contains(reversalJournal.LedgerEntries, line =>
            line.AccountId == _payable.Id && line.DebitAmount == 120m && line.CreditAmount == 0m);
        Assert.Contains(reversalJournal.LedgerEntries, line =>
            line.AccountId == _expense.Id && line.CreditAmount == 120m && line.DebitAmount == 0m);
        Assert.Equal(0m, _payable.CurrentBalance);
        Assert.Equal(0m, _expense.CurrentBalance);
        Assert.Equal(cashBefore, _cash.CurrentBalance);

        var reloadedOriginal = await GetEarnedJournalAsync(scenario.Earned.OrderId);
        Assert.Equal(originalJournal.Id, reloadedOriginal.Id);
        Assert.Equal(JournalStatus.Posted, reloadedOriginal.Status);
        Assert.Null(reloadedOriginal.ReversedByJournalId);
    }

    [Fact]
    public async Task PartialReversal_ReducesPayableExpenseAndPayoutEligibilityToNetAmount()
    {
        var scenario = await CreateScenarioAsync(100m, 30m, includeApproval: true);

        await _consumer.Consume(CreateConsumeContext(scenario.Event));

        Assert.Equal(70m, _payable.CurrentBalance);
        Assert.Equal(70m, _expense.CurrentBalance);

        var payoutService = new AffiliatePayoutService(
            _db,
            NullLogger<AffiliatePayoutService>.Instance);
        var balance = await payoutService.GetAvailableBalanceAsync(
            scenario.Earned.AffiliateAccountId,
            _tenantId);
        Assert.Equal(70m, balance.TotalApproved);
        Assert.Equal(70m, balance.RawAvailable);
        Assert.Equal(70m, balance.AvailableForWithdrawal);
    }

    [Fact]
    public async Task DuplicateReversalEvent_CreatesOneJournalAndOneLedgerEffect()
    {
        var scenario = await CreateScenarioAsync(100m, 40m);

        await _consumer.Consume(CreateConsumeContext(scenario.Event));
        await _consumer.Consume(CreateConsumeContext(scenario.Event));

        Assert.Equal(1, await _db.JournalEntries.IgnoreQueryFilters().CountAsync(journal =>
            journal.SourceType == "AffiliateCommissionReversed" &&
            journal.SourceId == scenario.Reversal.Id));
        Assert.Equal(60m, _payable.CurrentBalance);
        Assert.Equal(60m, _expense.CurrentBalance);
    }

    [Fact]
    public async Task ChangedPayloadWithSameReversalIdentity_IsRejectedWithoutSecondEffect()
    {
        var scenario = await CreateScenarioAsync(100m, 40m);
        await _consumer.Consume(CreateConsumeContext(scenario.Event));
        scenario.Event.ReversedAmount = 41m;

        await Assert.ThrowsAsync<InvalidJournalException>(() =>
            _consumer.Consume(CreateConsumeContext(scenario.Event)));

        Assert.Equal(1, await _db.JournalEntries.IgnoreQueryFilters().CountAsync(journal =>
            journal.SourceType == "AffiliateCommissionReversed" &&
            journal.SourceId == scenario.Reversal.Id));
        Assert.Equal(60m, _payable.CurrentBalance);
    }

    [Fact]
    public async Task ReversalBeforeEarnedAccounting_IsRejectedForRetryWithoutMutation()
    {
        var scenario = await CreateScenarioAsync(100m, 40m, postEarnedJournal: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _consumer.Consume(CreateConsumeContext(scenario.Event)));

        Assert.Contains("AFFILIATE_REVERSAL_EARNED_JOURNAL_NOT_READY", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await _db.JournalEntries.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(0m, _payable.CurrentBalance);
        Assert.Equal(0m, _expense.CurrentBalance);
    }

    [Fact]
    public async Task ApprovalBeforeReversal_DoesNotChangeAccountingResult()
    {
        var scenario = await CreateScenarioAsync(100m, 25m, includeApproval: true);

        await _consumer.Consume(CreateConsumeContext(scenario.Event));

        Assert.Equal(75m, _payable.CurrentBalance);
        Assert.Equal(75m, _expense.CurrentBalance);
        Assert.Equal(1, await _db.AffiliateCommissionLedger.IgnoreQueryFilters().CountAsync(entry =>
            entry.Id == scenario.Approved!.Id && entry.EntryType == CommissionEntryType.Approved));
    }

    [Fact]
    public async Task MultiplePartialReversals_PostEachExactSourceOnce()
    {
        var first = await CreateScenarioAsync(100m, 30m);
        var secondReversal = AffiliateCommissionLedger.CreateReversalWithCustomKey(
            _tenantId,
            first.Earned.Id,
            first.Earned.AttributionId,
            first.Earned.AffiliateAccountId,
            first.Earned.OrderId,
            first.Earned.OrderItemId,
            200m,
            20m,
            first.Earned.CommissionRate,
            first.Earned.Currency,
            $"AFFILIATE_COMM:REVERSED:T07-SECOND:{first.Earned.OrderItemId:N}:200.0000");
        _db.AffiliateCommissionLedger.Add(secondReversal);
        await _db.SaveChangesAsync();

        await _consumer.Consume(CreateConsumeContext(first.Event));
        await _consumer.Consume(CreateConsumeContext(CreateEvent(secondReversal)));

        Assert.Equal(2, await _db.JournalEntries.IgnoreQueryFilters().CountAsync(journal =>
            journal.SourceType == "AffiliateCommissionReversed"));
        Assert.Equal(50m, _payable.CurrentBalance);
        Assert.Equal(50m, _expense.CurrentBalance);
    }

    [Fact]
    public async Task CrossTenantEvent_CannotReadOrPostLocalTenantReversal()
    {
        var scenario = await CreateScenarioAsync(100m, 30m);
        var foreignTenant = Guid.NewGuid();
        scenario.Event.TenantId = foreignTenant;
        scenario.Event.StoreTenantId = foreignTenant;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _consumer.Consume(CreateConsumeContext(scenario.Event)));

        Assert.Equal(0, await _db.JournalEntries.IgnoreQueryFilters().CountAsync(journal =>
            journal.SourceType == "AffiliateCommissionReversed"));
        Assert.Equal(100m, _payable.CurrentBalance);
        Assert.Equal(100m, _expense.CurrentBalance);
    }

    [Fact]
    public async Task ReversalReports_AreBalancedReducePnlAndLeaveCashFlowUnchanged()
    {
        var scenario = await CreateScenarioAsync(100m, 30m);
        await _consumer.Consume(CreateConsumeContext(scenario.Event));
        var reports = new FinancialReportService(_db, NullLogger<FinancialReportService>.Instance);
        var from = DateTime.UtcNow.Date.AddDays(-1);
        var to = DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);

        var trialBalance = await reports.GetTrialBalanceAsync(_tenantId, from, to);
        var balanceSheet = await reports.GetBalanceSheetAsync(_tenantId, to);
        var pnl = await reports.GetProfitAndLossAsync(_tenantId, from, to);
        var cashFlow = await reports.GetCashFlowAsync(_tenantId, from, to);
        var payableLedger = await reports.GetGeneralLedgerAsync(_tenantId, _payable.Id, from, to);

        Assert.True(trialBalance.IsBalanced);
        Assert.True(balanceSheet.IsBalanced);
        Assert.Equal(70m, Assert.Single(pnl.ExpenseLines, line =>
            line.AccountCode == AccountCodes.AffiliateMarketingExpense).Amount);
        Assert.Equal(0m, cashFlow.NetCashChange);
        Assert.Equal(0m, cashFlow.OperatingCashFlow);
        Assert.True(cashFlow.Reconciles);
        Assert.Equal(70m, payableLedger.ClosingBalance);
        Assert.Equal(2, payableLedger.Entries.Count);
    }

    [Fact]
    public async Task ReversalAfterPaidCommission_FailsClosedBecauseNoRecoveryPolicyExists()
    {
        var scenario = await CreateScenarioAsync(100m, 100m, includeApproval: true);
        await _postingService.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionPaid",
            SourceId = Guid.NewGuid(),
            Description = "T07 paid authority",
            IdempotencyKey = $"T07:PAID:{scenario.Earned.OrderId}",
            Lines =
            [
                PostingLine.Debit(_payable.Id, 100m),
                PostingLine.Credit(_cash.Id, 100m)
            ]
        });

        await Assert.ThrowsAsync<InsufficientFundsException>(() =>
            _consumer.Consume(CreateConsumeContext(scenario.Event)));

        Assert.Equal(0, await _db.JournalEntries.IgnoreQueryFilters().CountAsync(journal =>
            journal.SourceType == "AffiliateCommissionReversed"));
        Assert.Equal(0m, _payable.CurrentBalance);
    }

    [Fact]
    public async Task PostingFailure_DoesNotCreatePartialJournalOrLedgerLines()
    {
        var scenario = await CreateScenarioAsync(100m, 30m);
        var resolver = new Mock<IAccountResolver>();
        resolver.Setup(service => service.GetByCodesAsync(
                _tenantId,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("6110 unavailable"));
        var consumer = new AffiliateCommissionReversedAccountingConsumer(
            _db,
            resolver.Object,
            _postingService,
            new AffiliateTelemetry(),
            NullLogger<AffiliateCommissionReversedAccountingConsumer>.Instance);
        var linesBefore = await _db.LedgerEntries.IgnoreQueryFilters().CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            consumer.Consume(CreateConsumeContext(scenario.Event)));

        Assert.Equal(linesBefore, await _db.LedgerEntries.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await _db.JournalEntries.IgnoreQueryFilters().CountAsync(journal =>
            journal.SourceType == "AffiliateCommissionReversed"));
        Assert.Equal(100m, _payable.CurrentBalance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Scenario> CreateScenarioAsync(
        decimal earnedCommission,
        decimal reversedCommission,
        bool includeApproval = false,
        bool postEarnedJournal = true)
    {
        var baseAmount = earnedCommission * 10m;
        var reversedBase = reversedCommission * 10m;
        var earned = AffiliateCommissionLedger.CreateEarnedFromSnapshot(
            _tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            baseAmount,
            0.10m,
            earnedCommission,
            "EGP");
        var approved = includeApproval ? AffiliateCommissionLedger.CreateApproved(earned) : null;
        var reversal = AffiliateCommissionLedger.CreateReversalWithCustomKey(
            _tenantId,
            earned.Id,
            earned.AttributionId,
            earned.AffiliateAccountId,
            earned.OrderId,
            earned.OrderItemId,
            reversedBase,
            reversedCommission,
            earned.CommissionRate,
            earned.Currency,
            $"AFFILIATE_COMM:REVERSED:T07:{earned.OrderItemId:N}:{reversedBase:F4}");

        _db.AffiliateCommissionLedger.Add(earned);
        if (approved != null)
        {
            _db.AffiliateCommissionLedger.Add(approved);
        }
        _db.AffiliateCommissionLedger.Add(reversal);
        await _db.SaveChangesAsync();

        if (postEarnedJournal)
        {
            await _postingService.PostJournalAsync(new PostingRequest
            {
                TenantId = _tenantId,
                JournalDate = earned.RecordedAtUtc,
                SourceType = "AffiliateCommissionEarned",
                SourceId = earned.OrderId,
                SourceReference = $"AFFILIATE_COMM_EARNED:{earned.OrderId}",
                Description = "T07 earned authority",
                IdempotencyKey = $"AFFILIATE_COMM_EARNED:{_tenantId}:{earned.OrderId}",
                Lines =
                [
                    PostingLine.Debit(_expense.Id, earnedCommission),
                    PostingLine.Credit(_payable.Id, earnedCommission)
                ]
            });
        }

        return new Scenario(earned, reversal, approved, CreateEvent(reversal));
    }

    private static AffiliateCommissionReversedEvent CreateEvent(AffiliateCommissionLedger reversal) => new()
    {
        TenantId = reversal.StoreTenantId,
        StoreTenantId = reversal.StoreTenantId,
        OrderId = reversal.OrderId,
        OrderNumber = $"T07-{reversal.OrderId:N}",
        OrderItemId = reversal.OrderItemId,
        AffiliateAccountId = reversal.AffiliateAccountId,
        OriginalLedgerEntryId = reversal.OriginalEntryId!.Value,
        ReversalLedgerEntryId = reversal.Id,
        ReversedAmount = reversal.CommissionAmount,
        Currency = reversal.Currency,
        OccurredAtUtc = reversal.RecordedAtUtc,
        CorrelationId = reversal.OrderId.ToString(),
        IdempotencyKey = reversal.IdempotencyKey
    };

    private async Task<JournalEntry> GetEarnedJournalAsync(Guid orderId) =>
        await _db.JournalEntries
            .IgnoreQueryFilters()
            .Include(journal => journal.LedgerEntries)
            .SingleAsync(journal =>
                journal.SourceType == "AffiliateCommissionEarned" &&
                journal.SourceId == orderId);

    private async Task<JournalEntry> GetReversalJournalAsync(Guid reversalId) =>
        await _db.JournalEntries
            .IgnoreQueryFilters()
            .Include(journal => journal.LedgerEntries)
            .SingleAsync(journal =>
                journal.SourceType == "AffiliateCommissionReversed" &&
                journal.SourceId == reversalId);

    private static ConsumeContext<T> CreateConsumeContext<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.Setup(item => item.Message).Returns(message);
        context.Setup(item => item.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private sealed record Scenario(
        AffiliateCommissionLedger Earned,
        AffiliateCommissionLedger Reversal,
        AffiliateCommissionLedger? Approved,
        AffiliateCommissionReversedEvent Event);
}
