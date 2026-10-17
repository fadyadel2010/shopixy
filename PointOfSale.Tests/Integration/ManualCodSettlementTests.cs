using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Infrastructure.Treasury;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.API.Controllers;
using System.Text.Json;

namespace PointOfSale.Tests.Integration;

public sealed class ManualCodSettlementTests : IDisposable
{
    private readonly TestDatabaseScope _scope = TestDatabaseScope.Open();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();
    private readonly ApplicationDbContext _db;
    public ManualCodSettlementTests() => _db = _scope.CreateDbContext(_tenant);

    private PaymentLifecycleService Lifecycle(ApplicationDbContext db, Mock<IEventPublisher>? events = null,
        ICommerceAccountingService? accounting = null)
    {
        var correlation = Mock.Of<ICorrelationIdAccessor>(c => c.GetCorrelationId() == "manual-cod-certification");
        events ??= new Mock<IEventPublisher>();
        events.SetReturnsDefault(Task.CompletedTask);
        var marketplace = new Mock<IMarketplaceEventOutboxService>();
        marketplace.SetReturnsDefault(Task.CompletedTask);
        accounting ??= new CommerceAccountingService(new FinancialPostingService(db, NullLogger<FinancialPostingService>.Instance),
            new AccountResolver(db, NullLogger<AccountResolver>.Instance), Mock.Of<ICashboxLedgerAccountService>(),
            NullLogger<CommerceAccountingService>.Instance, db);
        return new PaymentLifecycleService(db, new AccountingOutboxService(db, correlation, NullLogger<AccountingOutboxService>.Instance),
            accounting, events.Object, marketplace.Object, correlation, Mock.Of<IProductVariantStockRepository>(),
            Mock.Of<IInventoryReservationMetricsService>(), Mock.Of<ICouponValidationService>(),
            NullLogger<PaymentLifecycleService>.Instance, Options.Create(new PaymentLifecycleSettings()));
    }

    private async Task Seed()
    {
        _db.Tenants.Add(new Tenant { Id = _tenant, Name = "Disposable Manual COD certification", Slug = $"manual-cod-{_tenant:N}",
            Currency = "EGP", IsActive = true, Status = TenantStatus.Active });
        _db.Users.Add(new User { Id = _actor, TenantId = _tenant, Username = $"manual-{_actor:N}", Email = $"{_actor:N}@example.invalid",
            Role = UserRole.Owner, IsActive = true, PasswordHash = "test-only" });
        await _db.SaveChangesAsync();
        await new TenantAccountSeeder(_db, NullLogger<TenantAccountSeeder>.Instance).SeedAsync(_tenant);
    }

    private async Task<Order> Order(decimal amount = 100m, bool collect = true, Action<Order>? configure = null)
    {
        var order = new Order { TenantId = _tenant, OrderNumber = $"MANUAL-FIXTURE-{Guid.NewGuid():N}", Total = amount, Currency = "EGP",
            PaymentMethodCode = "COD", PaymentGateway = "cod", PaymentStatus = PaymentStatus.Pending,
            FulfillmentMode = FulfillmentMode.Manual, FulfillmentStatus = FulfillmentStatus.Delivered, Status = OrderStatus.Delivered };
        order.TransitionCheckoutState(CheckoutState.PaymentPending);
        configure?.Invoke(order);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        if (collect)
        {
            var result = await Lifecycle(_db).MarkOfflineCollectionDeliveredAsync(_tenant, order.Id, "synthetic-manual-delivery", "fixture-operator");
            Assert.True(result.Success, result.ErrorMessage);
            _db.ChangeTracker.Clear();
            order = await _db.Orders.SingleAsync(o => o.Id == order.Id);
        }
        return order;
    }

    private ManualCodSettlementRequest Request(params Order[] orders) => new(Guid.NewGuid(), orders.Select(o => o.Id).ToArray());

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task RealCollection_Settlement_Replay_Actor_Balances_AndOfficialLedger(int count)
    {
        await Seed();
        var orders = new List<Order>();
        for (var i = 0; i < count; i++) orders.Add(await Order(100m + i));
        var gross = orders.Sum(o => o.Total);
        var service = Lifecycle(_db);
        var selection = await service.GetManualCodSettlementSelectionAsync(_tenant);
        Assert.Equal(count, selection.Orders.Count);
        var request = Request(orders.ToArray());
        var result = await service.RecordManualCodSettlementAsync(_tenant, _actor, request);
        Assert.Equal("Posted", result.AccountingOutcome);
        await using var retryDb = _scope.CreateDbContext(_tenant);
        var replay = await Lifecycle(retryDb).RecordManualCodSettlementAsync(_tenant, _actor, request);
        Assert.True(replay.WasIdempotent);
        Assert.Equal(result.JournalId, replay.JournalId);
        Assert.Empty((await Lifecycle(retryDb).GetManualCodSettlementSelectionAsync(_tenant)).Orders);
        await using var fresh = _scope.CreateDbContext(_tenant);
        Assert.Equal(count, await fresh.PaymentSettlementOrders.CountAsync());
        var batch = Assert.Single(await fresh.PaymentSettlementBatches.ToListAsync());
        Assert.Equal(_actor, batch.CreatedBy);
        Assert.Equal(gross, batch.GrossAmount);
        Assert.Equal(0m, batch.Fees);
        Assert.All(await fresh.Orders.ToListAsync(), o => Assert.Equal(PaymentStatus.Settled, o.PaymentStatus));
        var journal = await fresh.JournalEntries.Include(j => j.LedgerEntries).SingleAsync(j => j.SourceType == "ManualCodSettlement");
        Assert.Equal(_actor, journal.CreatedByUserId);
        Assert.Equal(gross, journal.TotalDebits);
        Assert.Equal(gross, journal.TotalCredits);
        Assert.Equal(2, journal.LedgerEntries.Count);
        Assert.Equal(ManualCodSettlementAuthority.Key(_tenant, request.SettlementId.ToString("D")), journal.IdempotencyKey);
        var treasury = new TreasuryReadService(fresh, NullLogger<TreasuryReadService>.Instance);
        Assert.Equal(gross, await treasury.GetAccountBalanceAsync(_tenant, "1010"));
        Assert.Equal(0m, await treasury.GetAccountBalanceAsync(_tenant, "1130"));
        Assert.Equal(0m, await treasury.GetAccountBalanceAsync(_tenant, "1030"));
        var report = new FinancialReportService(fresh, NullLogger<FinancialReportService>.Instance);
        foreach (var code in new[] { "1010", "1130" })
        {
            var account = await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == code);
            var ledger = await report.GetGeneralLedgerAsync(_tenant, account.Id, DateTime.UtcNow.Date.AddDays(-1), DateTime.UtcNow.Date.AddDays(1));
            var line = Assert.Single(ledger.Entries, l => l.JournalId == journal.Id);
            Assert.Equal(gross, code == "1010" ? line.DebitAmount : line.CreditAmount);
            Assert.Equal(account.CurrentBalance, ledger.ClosingBalance);
        }
        var outbox = await fresh.AccountingOutboxMessages.SingleAsync(m => m.EventType == "ManualCodSettlement");
        Assert.Equal(journal.Id, outbox.JournalId);
        Assert.NotNull(outbox.ProcessedAtUtc);
        var recovery = await ManualCodSettlementAuthority.RecoverReceiptAsync(fresh, outbox, default);
        Assert.True(recovery.WasIdempotent);
        Assert.Equal(count + 1, await fresh.JournalEntries.CountAsync());
    }

    [Theory]
    [InlineData("carrier")]
    [InlineData("uncollected")]
    [InlineData("pending-posting")]
    [InlineData("foreign-tenant")]
    [InlineData("wrong-currency")]
    [InlineData("duplicate-order")]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("deleted")]
    [InlineData("refund")]
    [InlineData("cancelled")]
    [InlineData("inactive-cashbox")]
    [InlineData("clearing-substitution")]
    [InlineData("wrong-default-cashbox")]
    [InlineData("missing-journal")]
    [InlineData("partial-precision")]
    public async Task IneligibleRequests_FailWithoutAnyPartialBatchJournalStateOrMovement(string defect)
    {
        await Seed();
        var collect = defect is not ("carrier" or "uncollected" or "zero" or "negative" or "partial-precision");
        Guid? foreignTenant = null;
        if (defect == "foreign-tenant")
        {
            foreignTenant = Guid.NewGuid();
            _db.Tenants.Add(new Tenant { Id = foreignTenant.Value, Name = "Foreign disposable tenant", Slug = $"foreign-{foreignTenant:N}",
                Currency = "EGP", IsActive = true, Status = TenantStatus.Active });
            await _db.SaveChangesAsync();
            collect = false;
        }
        var order = await Order(defect == "zero" ? 0m : defect == "negative" ? -1m : defect == "partial-precision" ? 1.001m : 100m,
            collect, o => {
                if (foreignTenant.HasValue) o.TenantId = foreignTenant.Value;
                if (defect == "carrier") { o.FulfillmentMode = FulfillmentMode.Carrier; o.FulfillmentCarrierCode = "bosta"; }
            });
        if (defect == "pending-posting") order.FinancialPostingStatus = FinancialPostingStatus.Pending;
        if (defect == "missing-journal") order.FinancialPostingJournalId = Guid.NewGuid();
        if (defect == "wrong-currency") { var tenant = await _db.Tenants.SingleAsync(); tenant.Currency = "USD"; }
        if (defect == "deleted") order.IsDeleted = true;
        if (defect == "cancelled") order.Status = OrderStatus.Cancelled;
        if (defect == "inactive-cashbox") (await _db.TreasuryAccounts.SingleAsync(a => a.Type == TreasuryAccountType.Cashbox)).IsActive = false;
        if (defect is "clearing-substitution" or "wrong-default-cashbox")
        {
            // Deliberately corrupt isolated fixture SQL to prove admission rejects a pre-existing invalid mapping.
            // Normal application changes are separately blocked by the mapping immutability guard.
            var substitute = (await _db.LedgerAccounts.SingleAsync(a => a.AccountCode == (defect == "clearing-substitution" ? "1130" : "1030"))).Id;
            await _db.TreasuryAccounts.Where(a => a.AccountId == substitute).ExecuteDeleteAsync();
            await _db.TreasuryAccounts.Where(a => a.Type == TreasuryAccountType.Cashbox)
                .ExecuteUpdateAsync(updates => updates.SetProperty(a => a.AccountId, substitute));
        }
        await _db.SaveChangesAsync();
        if (defect == "refund") Assert.True((await Lifecycle(_db).RecordRefundAsync(order.Id, 10m, "pre-settlement", "fixture")).Success);
        await using var before = _scope.CreateDbContext(_tenant);
        var balanceBefore = await before.LedgerAccounts.OrderBy(a => a.AccountCode).Select(a => a.CurrentBalance).ToListAsync();
        var journalCount = await before.JournalEntries.CountAsync();
        var request = defect == "duplicate-order" ? new ManualCodSettlementRequest(Guid.NewGuid(), new[] { order.Id, order.Id }) : Request(order);
        var rejection = await Assert.ThrowsAsync<ManualCodSettlementException>(() => Lifecycle(_db).RecordManualCodSettlementAsync(
            _tenant, _actor, request));
        if (defect == "foreign-tenant") Assert.Equal("COD_MANUAL_ORDER_NOT_FOUND", rejection.Code);
        await using var fresh = _scope.CreateDbContext(_tenant);
        Assert.Empty(await fresh.PaymentSettlementBatches.ToListAsync());
        Assert.Empty(await fresh.PaymentSettlementOrders.ToListAsync());
        Assert.Equal(journalCount, await fresh.JournalEntries.CountAsync());
        Assert.Equal(balanceBefore, await fresh.LedgerAccounts.OrderBy(a => a.AccountCode).Select(a => a.CurrentBalance).ToListAsync());
        Assert.DoesNotContain(await fresh.Orders.IgnoreQueryFilters().Where(o => o.TenantId == _tenant).ToListAsync(), o => o.PaymentStatus == PaymentStatus.Settled);
    }

    [Fact]
    public async Task AlreadySettledAndIdentityCollision_FailClosed_ThenRefundCreditsActualCashbox()
    {
        await Seed();
        var order = await Order();
        var another = await Order(50m);
        var request = Request(order);
        var result = await Lifecycle(_db).RecordManualCodSettlementAsync(_tenant, _actor, request);
        await Assert.ThrowsAsync<ManualCodSettlementException>(() => Lifecycle(_db).RecordManualCodSettlementAsync(_tenant, _actor, Request(order)));
        await Assert.ThrowsAsync<ManualCodSettlementException>(() => Lifecycle(_db).RecordManualCodSettlementAsync(_tenant, _actor,
            new(request.SettlementId, new[] { another.Id })));
        var refund = await Lifecycle(_db).RecordRefundAsync(order.Id, 25m, "cashbox-refund", "Disposable refund");
        Assert.True(refund.Success, refund.ErrorMessage);
        await using var fresh = _scope.CreateDbContext(_tenant);
        var journal = await fresh.JournalEntries.Include(j => j.LedgerEntries).SingleAsync(j => j.SourceType == "Order_Refund");
        Assert.Equal(25m, Assert.Single(journal.LedgerEntries, l => l.AccountId == result.DestinationAccountId).CreditAmount);
        var treasury = new TreasuryReadService(fresh, NullLogger<TreasuryReadService>.Instance);
        Assert.Equal(75m, await treasury.GetAccountBalanceAsync(_tenant, "1010"));
        Assert.Equal(0m, await treasury.GetAccountBalanceAsync(_tenant, "1030"));
        Assert.Single(await fresh.PaymentSettlementBatches.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentOverlappingOrders_OneJournal_AndRetryConverges(bool sameIdentity)
    {
        await Seed();
        var order = await Order();
        var first = Request(order);
        var second = sameIdentity ? first : Request(order);
        async Task<ManualCodSettlementResult?> Submit(ManualCodSettlementRequest request)
        {
            await using var context = _scope.CreateDbContext(_tenant);
            try { return await Lifecycle(context).RecordManualCodSettlementAsync(_tenant, _actor, request); }
            catch (ManualCodSettlementException) { return null; }
        }
        var results = await Task.WhenAll(Submit(first), Submit(second));
        Assert.Contains(results, r => r != null);
        if (!sameIdentity) Assert.Single(results, r => r != null);
        var success = results.First(r => r != null)!;
        Assert.NotNull(await Submit(new(success.SettlementId, new[] { order.Id })));
        await using var fresh = _scope.CreateDbContext(_tenant);
        Assert.Single(await fresh.PaymentSettlementBatches.ToListAsync());
        Assert.Single(await fresh.PaymentSettlementOrders.ToListAsync());
        Assert.Equal(2, await fresh.JournalEntries.CountAsync());
        Assert.Equal(100m, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1010")).CurrentBalance);
        Assert.Equal(0m, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1130")).CurrentBalance);
    }

    [Theory]
    [InlineData("after-batch")]
    [InlineData("after-posting-before-event")]
    [InlineData("cancellation-before-commit")]
    public async Task FailureAtAtomicBoundaries_RollsBack_AndSameIdentityRetrySucceeds(string boundary)
    {
        await Seed();
        var order = await Order();
        var request = Request(order);
        var events = new Mock<IEventPublisher>();
        events.SetReturnsDefault(Task.CompletedTask);
        ICommerceAccountingService? accounting = null;
        if (boundary == "after-batch")
        {
            var fail = new Mock<ICommerceAccountingService>();
            fail.Setup(a => a.PostManualCodSettlementAsync(_tenant, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(PostingResult.Failed("Injected posting failure"));
            accounting = fail.Object;
        }
        else events.Setup(e => e.PublishAsync(It.IsAny<OrderPaymentStatusChangedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(boundary == "cancellation-before-commit" ? new OperationCanceledException() : new InvalidOperationException("Injected outbox publication failure"));
        await Assert.ThrowsAnyAsync<Exception>(() => Lifecycle(_db, events, accounting).RecordManualCodSettlementAsync(_tenant, _actor, request));
        await using var fresh = _scope.CreateDbContext(_tenant);
        Assert.Empty(await fresh.PaymentSettlementBatches.ToListAsync());
        Assert.Empty(await fresh.PaymentSettlementOrders.ToListAsync());
        Assert.Single(await fresh.JournalEntries.ToListAsync());
        Assert.Equal(PaymentStatus.Collected, (await fresh.Orders.SingleAsync()).PaymentStatus);
        Assert.Equal(0m, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1010")).CurrentBalance);
        Assert.Equal(100m, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1130")).CurrentBalance);
        var success = await Lifecycle(fresh).RecordManualCodSettlementAsync(_tenant, _actor, request);
        Assert.Equal("Posted", success.AccountingOutcome);
    }

    [Theory]
    [InlineData("amount", "0")]
    [InlineData("amount", "-1")]
    [InlineData("grossAmount", "999")]
    [InlineData("currency", "\"USD\"")]
    [InlineData("destinationAccountId", "\"00000000-0000-0000-0000-000000000001\"")]
    public void CallerMoneyCurrencyAndDestinationFields_AreRejectedByStrictApiContract(string field, string value)
    {
        var body = $"{{\"settlementId\":\"{Guid.NewGuid()}\",\"orderIds\":[\"{Guid.NewGuid()}\"],\"{field}\":{value}}}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ManualCodSettlementApiRequest>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task PermanentDatabaseIdentityAndMembership_RejectSoftDeletedCollisions()
    {
        await Seed();
        var order = await Order();
        var request = Request(order);
        var result = await Lifecycle(_db).RecordManualCodSettlementAsync(_tenant, _actor, request);
        await using (var context = _scope.CreateDbContext(_tenant))
        {
            context.PaymentSettlementBatches.Add(new PaymentSettlementBatch { TenantId = _tenant, SettlementType = "MANUAL_COD_COLLECTION",
                SettlementId = request.SettlementId.ToString("D"), Currency = "EGP", IsDeleted = true });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        await using (var context = _scope.CreateDbContext(_tenant))
        {
            context.PaymentSettlementOrders.Add(new PaymentSettlementOrder { TenantId = _tenant, SettlementBatchId = result.BatchId,
                OrderId = order.Id, OrderTotal = 100m, Currency = "EGP", IsDeleted = true });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        await using var fresh = _scope.CreateDbContext(_tenant);
        Assert.Single(await fresh.PaymentSettlementBatches.ToListAsync());
        Assert.Single(await fresh.PaymentSettlementOrders.ToListAsync());
    }

    [Theory]
    [InlineData(UserRole.Owner, true)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Manager, false)]
    [InlineData(UserRole.Cashier, false)]
    [InlineData(UserRole.Warehouse, false)]
    public async Task FinancialActorMustBeActiveSameTenantAuthorizedOperator(UserRole role, bool allowed)
    {
        await Seed();
        var order = await Order();
        (await _db.Users.SingleAsync()).Role = role;
        await _db.SaveChangesAsync();
        if (allowed) Assert.Equal("Posted", (await Lifecycle(_db).RecordManualCodSettlementAsync(_tenant, _actor, Request(order))).AccountingOutcome);
        else
        {
            await Assert.ThrowsAsync<ManualCodSettlementException>(() => Lifecycle(_db).RecordManualCodSettlementAsync(_tenant, _actor, Request(order)));
            await using var fresh = _scope.CreateDbContext(_tenant);
            Assert.Empty(await fresh.PaymentSettlementBatches.ToListAsync());
            Assert.Single(await fresh.JournalEntries.ToListAsync());
            Assert.Equal(PaymentStatus.Collected, (await fresh.Orders.SingleAsync()).PaymentStatus);
            Assert.Equal(0m, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1010")).CurrentBalance);
        }
    }

    public void Dispose() { _db.Dispose(); _scope.Dispose(); }
}
