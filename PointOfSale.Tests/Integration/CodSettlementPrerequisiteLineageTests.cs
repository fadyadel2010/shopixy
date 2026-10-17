using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class CodSettlementPrerequisiteLineageTests : IDisposable
{
    private readonly TestDatabaseScope _scope = TestDatabaseScope.Open();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly ApplicationDbContext _db;

    public CodSettlementPrerequisiteLineageTests() => _db = _scope.CreateDbContext(_tenant);

    private async Task<(Order Order, PaymentSettlementBatch Batch)> History(string type, string destination, decimal fees = 0m)
    {
        _db.Tenants.Add(new Tenant { Id = _tenant, Name = "COD prerequisite fixture", Slug = $"cod-prereq-{_tenant:N}", Currency = "EGP", IsActive = true, Status = TenantStatus.Active });
        var order = new Order
        {
            TenantId = _tenant, OrderNumber = $"FIXTURE-{Guid.NewGuid():N}"[..24], Total = 100m, Currency = "EGP",
            PaymentMethodCode = "COD", PaymentGateway = "cod", PaymentStatus = PaymentStatus.Settled,
            FulfillmentMode = FulfillmentMode.Manual, FulfillmentStatus = FulfillmentStatus.Delivered,
            Status = OrderStatus.Delivered, IsPaid = true, PaidAt = DateTime.UtcNow
        };
        order.TransitionCheckoutState(CheckoutState.PaymentPending);
        order.TransitionCheckoutState(CheckoutState.Paid);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        var batch = await SettlementHistoryFixture.SeedAsync(_db, _tenant, order, type, destination, fees);
        var accounts = await _db.LedgerAccounts.ToDictionaryAsync(item => item.AccountCode);
        var collection = JournalEntry.Create(_tenant, $"COLLECTION-{Guid.NewGuid():N}"[..24], DateTime.UtcNow,
            "Order_COD", order.Id, order.OrderNumber, "Synthetic pre-existing collection history", $"COD_DELIVERED:{_tenant}:{order.Id}", null);
        _db.JournalEntries.Add(collection);
        using (LedgerMutationScope.Authorize())
        {
            accounts["1130"].ApplyEntry(100m, 0m);
            accounts["4020"].ApplyEntry(0m, 100m);
            _db.LedgerEntries.Add(LedgerEntry.CreateDebit(_tenant, collection.Id, accounts["1130"].Id, 100m, 0m, 1));
            _db.LedgerEntries.Add(LedgerEntry.CreateCredit(_tenant, collection.Id, accounts["4020"].Id, 100m, 100m, 2));
            order.FinancialPostingStatus = FinancialPostingStatus.Posted;
            order.FinancialPostingJournalId = collection.Id;
            order.FinancialPostedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return (order, batch);
    }

    private static EcommerceRefundAccountingContext Context(PaymentSettlementBatch batch) =>
        new(true, batch.Id, batch.SettlementType, batch.SettlementId, batch.PayoutBatchId, batch.SettledAtUtc);

    private static PaymentLifecycleService Lifecycle(ApplicationDbContext db, Mock<IEventPublisher>? events = null)
    {
        var correlation = Mock.Of<ICorrelationIdAccessor>(item => item.GetCorrelationId() == "cod-prerequisite-test");
        var commerce = new CommerceAccountingService(new FinancialPostingService(db, NullLogger<FinancialPostingService>.Instance),
            new AccountResolver(db, NullLogger<AccountResolver>.Instance), Mock.Of<ICashboxLedgerAccountService>(),
            NullLogger<CommerceAccountingService>.Instance, db);
        events ??= new Mock<IEventPublisher>();
        events.SetReturnsDefault(Task.CompletedTask);
        var marketplace = new Mock<IMarketplaceEventOutboxService>();
        marketplace.SetReturnsDefault(Task.CompletedTask);
        return new PaymentLifecycleService(db,
            new AccountingOutboxService(db, correlation, NullLogger<AccountingOutboxService>.Instance), commerce,
            events.Object, marketplace.Object, correlation, Mock.Of<IProductVariantStockRepository>(),
            Mock.Of<IInventoryReservationMetricsService>(), Mock.Of<ICouponValidationService>(),
            NullLogger<PaymentLifecycleService>.Instance, Options.Create(new PaymentLifecycleSettings()));
    }

    [Theory]
    [InlineData("OFFLINE_COLLECTION", "1030", 2)]
    [InlineData("GATEWAY", "1030", 2)]
    [InlineData("MANUAL_COD_COLLECTION", "1010", 0)]
    [InlineData("MANUAL_COD_COLLECTION", "1030", 0)]
    public async Task Refund_UsesActualHistoricalDestination_NotFulfillmentOrSettlementKind(string type, string destination, int fees)
    {
        var (order, batch) = await History(type, destination, fees);
        var account = await _db.LedgerAccounts.SingleAsync(item => item.AccountCode == destination);
        Assert.Equal(account.Id, await SettlementRefundLineage.ResolveDestinationAccountAsync(_db, _tenant, order.Id, Context(batch)));
        var events = new Mock<IEventPublisher>();
        var service = Lifecycle(_db, events);
        var first = await service.RecordRefundAsync(order.Id, 25m, "same-refund", "Prerequisite refund fixture");
        Assert.True(first.Success, first.ErrorMessage);
        var retry = await service.RecordRefundAsync(order.Id, 25m, "same-refund", "retry");
        Assert.True(retry.Success, retry.ErrorMessage);
        Assert.True(retry.WasIdempotent);
        _db.ChangeTracker.Clear();
        var refundJournal = await _db.JournalEntries.Include(item => item.LedgerEntries).SingleAsync(item => item.SourceType == "Order_Refund");
        Assert.Equal(25m, Assert.Single(refundJournal.LedgerEntries, line => line.AccountId == account.Id).CreditAmount);
        Assert.Equal(refundJournal.TotalDebits, refundJournal.TotalCredits);
        Assert.Equal(3, await _db.JournalEntries.CountAsync()); // Two fixture histories + one refund; no settlement execution.
        Assert.Equal(1, await _db.PaymentSettlementBatches.CountAsync());
        Assert.Single(await _db.OrderRefunds.ToListAsync());
        Assert.Equal(PaymentStatus.PartiallyRefunded, (await _db.Orders.SingleAsync()).PaymentStatus);
        Assert.Equal(100m - fees - 25m, (await _db.LedgerAccounts.SingleAsync(item => item.Id == account.Id)).CurrentBalance);
        Assert.Equal(0m, (await _db.LedgerAccounts.SingleAsync(item => item.AccountCode == "1130")).CurrentBalance);
        events.Verify(item => item.PublishAsync(It.IsAny<OrderRefundedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("missing-journal")]
    [InlineData("ambiguous")]
    [InlineData("wrong-tenant")]
    [InlineData("wrong-currency")]
    [InlineData("wrong-snapshot")]
    [InlineData("reversed")]
    public async Task MissingAmbiguousForeignOrStaleHistory_FailsClosedWithoutFinancialMutation(string defect)
    {
        var (order, batch) = await History("OFFLINE_COLLECTION", "1030");
        var context = Context(batch);
        if (defect == "wrong-snapshot") context = context with { SettlementId = "forged" };
        if (defect == "wrong-currency")
        {
            var tenant = await _db.Tenants.SingleAsync();
            tenant.Currency = "USD";
            await _db.SaveChangesAsync();
        }
        if (defect == "missing-journal") context = context with { SettlementBatchId = Guid.NewGuid() };
        if (defect == "ambiguous")
        {
            using var ledger = LedgerMutationScope.Authorize();
            _db.JournalEntries.Add(JournalEntry.Create(_tenant, "AMBIGUOUS", batch.SettledAtUtc,
                "CODSettlement", batch.Id, batch.SettlementId, "Ambiguous test-only history", "AMBIGUOUS", null));
            await _db.SaveChangesAsync();
        }
        if (defect == "reversed")
        {
            using var ledger = LedgerMutationScope.Authorize();
            var journal = await _db.JournalEntries.SingleAsync(item => item.SourceId == batch.Id);
            // No reversal operation is executed. Only a synthetic history locator
            // satisfies the relational FK while exercising fail-closed reading.
            var reversal = JournalEntry.Create(_tenant, "REVERSAL-LOCATOR", batch.SettledAtUtc,
                "Reversal", journal.Id, journal.JournalNumber, "Fixture reversal locator", "REVERSAL-LOCATOR", null);
            _db.JournalEntries.Add(reversal);
            journal.MarkReversed(reversal.Id, "Reversed fixture history");
            await _db.SaveChangesAsync();
        }
        var count = await _db.JournalEntries.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => SettlementRefundLineage.ResolveDestinationAccountAsync(
            _db, defect == "wrong-tenant" ? Guid.NewGuid() : _tenant, order.Id, context));
        Assert.Equal(count, await _db.JournalEntries.CountAsync());
        Assert.Empty(await _db.OrderRefunds.ToListAsync());
    }

    [Fact]
    public async Task StaleTrackedSettlementFacts_AreNotRefundAuthority_AndPersistedLineageIsImmutable()
    {
        var (order, batch) = await History("OFFLINE_COLLECTION", "1030");
        var snapshot = Context(batch);
        var bank = await _db.LedgerAccounts.SingleAsync(item => item.AccountCode == "1030");
        batch.SettlementId = "stale-tracked-change";
        Assert.Equal(bank.Id, await SettlementRefundLineage.ResolveDestinationAccountAsync(_db, _tenant, order.Id, snapshot));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.SaveChangesAsync());
        _db.ChangeTracker.Clear();
        var line = await _db.LedgerEntries.FirstAsync();
        line.SetAccountId(Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.SaveChangesAsync());
        _db.ChangeTracker.Clear();
        var membership = await _db.PaymentSettlementOrders.SingleAsync();
        membership.IsDeleted = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task BatchWithoutPostedHistory_CannotGuessBankOrCashbox()
    {
        _db.Tenants.Add(new Tenant { Id = _tenant, Name = "COD prerequisite fixture", Slug = $"cod-prereq-{_tenant:N}", Currency = "EGP", IsActive = true, Status = TenantStatus.Active });
        var order = new Order { TenantId = _tenant, OrderNumber = $"NO-HISTORY-{Guid.NewGuid():N}", Total = 100m,
            Currency = "EGP", PaymentMethodCode = "COD", PaymentStatus = PaymentStatus.Settled };
        _db.Orders.Add(order);
        var batch = new PaymentSettlementBatch { TenantId = _tenant, SettlementType = "OFFLINE_COLLECTION",
            SettlementId = "batch-only", GrossAmount = 100m, Currency = "EGP", OrderCount = 1, SettledAtUtc = DateTime.UtcNow.Date };
        _db.PaymentSettlementBatches.Add(batch);
        _db.PaymentSettlementOrders.Add(new PaymentSettlementOrder { TenantId = _tenant, SettlementBatchId = batch.Id,
            OrderId = order.Id, OrderTotal = 100m, Currency = "EGP" });
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => SettlementRefundLineage.ResolveDestinationAccountAsync(_db, _tenant, order.Id, Context(batch)));
        Assert.Empty(await _db.JournalEntries.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentRefundResolutionAndRetry_CreatesOneRefundJournal()
    {
        var (order, _) = await History("MANUAL_COD_COLLECTION", "1010");
        _db.ChangeTracker.Clear();
        async Task<bool> Submit()
        {
            await using var db = _scope.CreateDbContext(_tenant);
            return (await Lifecycle(db).RecordRefundAsync(order.Id, 25m, "concurrent-refund", "Concurrency fixture")).Success;
        }
        var attempts = await Task.WhenAll(Submit(), Submit());
        Assert.Contains(true, attempts);
        Assert.True(await Submit()); // Serialization loser/network retry converges on persisted refund.
        await using var verify = _scope.CreateDbContext(_tenant);
        Assert.Single(await verify.OrderRefunds.ToListAsync());
        var journal = Assert.Single(await verify.JournalEntries.Include(item => item.LedgerEntries).Where(item => item.SourceType == "Order_Refund").ToListAsync());
        Assert.Equal(journal.TotalDebits, journal.TotalCredits);
        Assert.Equal(75m, (await verify.LedgerAccounts.SingleAsync(item => item.AccountCode == "1010")).CurrentBalance);
        Assert.Equal(3, await verify.JournalEntries.CountAsync());
    }

    public void Dispose()
    {
        _db.Dispose();
        _scope.Dispose();
    }
}
