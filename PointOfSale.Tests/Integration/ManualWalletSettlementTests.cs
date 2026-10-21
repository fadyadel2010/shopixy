using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Payments.ManualPayment;
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
using PointOfSale.Tests.Infrastructure;
using PaymentMethod = PointOfSale.Core.Enums.PaymentMethod;

namespace PointOfSale.Tests.Integration;

public sealed class ManualWalletSettlementTests : IDisposable
{
    // TestDatabaseScope creates/migrates/resets ONLY a uniquely named disposable test database.
    private readonly TestDatabaseScope _scope = TestDatabaseScope.Open();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();
    private readonly ApplicationDbContext _db;
    public ManualWalletSettlementTests() => _db = _scope.CreateDbContext(_tenant);

    private PaymentLifecycleService Lifecycle(ApplicationDbContext db, Mock<IEventPublisher>? events = null, ICommerceAccountingService? accounting = null)
    {
        var correlation = Mock.Of<ICorrelationIdAccessor>(c => c.GetCorrelationId() == "wallet-settlement-test-only");
        events ??= new Mock<IEventPublisher>();
        events.SetReturnsDefault(Task.CompletedTask);
        var marketplace = new Mock<IMarketplaceEventOutboxService>();
        marketplace.SetReturnsDefault(Task.CompletedTask);
        accounting ??= new CommerceAccountingService(new FinancialPostingService(db, NullLogger<FinancialPostingService>.Instance),
            new AccountResolver(db, NullLogger<AccountResolver>.Instance), Mock.Of<ICashboxLedgerAccountService>(), NullLogger<CommerceAccountingService>.Instance, db);
        return new PaymentLifecycleService(db, new AccountingOutboxService(db, correlation, NullLogger<AccountingOutboxService>.Instance),
            accounting, events.Object, marketplace.Object, correlation, Mock.Of<IProductVariantStockRepository>(),
            Mock.Of<IInventoryReservationMetricsService>(), Mock.Of<ICouponValidationService>(), NullLogger<PaymentLifecycleService>.Instance,
            Options.Create(new PaymentLifecycleSettings()));
    }

    private async Task Seed()
    {
        _db.Tenants.Add(new Tenant { Id = _tenant, Name = "TEST-ONLY Wallet settlement", Slug = $"wallet-{_tenant:N}", Currency = "EGP", IsActive = true, Status = TenantStatus.Active });
        _db.Users.Add(new User { Id = _actor, TenantId = _tenant, Username = $"wallet-{_actor:N}", Email = $"{_actor:N}@example.invalid", Role = UserRole.Owner, IsActive = true, PasswordHash = "test-only" });
        await _db.SaveChangesAsync();
        await new TenantAccountSeeder(_db, NullLogger<TenantAccountSeeder>.Instance).SeedAsync(_tenant);
    }

    private async Task<Order> Wallet(bool approve = true, decimal amount = 100m)
    {
        var order = new Order { TenantId = _tenant, OrderNumber = $"TEST-WALLET-{Guid.NewGuid():N}", Total = amount, Subtotal = amount,
            Currency = "EGP", PaymentMethodCode = "MANUALWALLET", PaymentMethod = PaymentMethod.BankTransfer,
            PaymentGateway = "ManualWallet", PaymentStatus = PaymentStatus.AwaitingVerification, Status = OrderStatus.Pending,
            FulfillmentMode = FulfillmentMode.Manual, FulfillmentStatus = FulfillmentStatus.Created };
        order.TransitionCheckoutState(CheckoutState.PaymentPending);
        _db.Orders.Add(order);
        var intent = new PaymentIntent { TenantId = _tenant, OrderId = order.Id, Amount = amount, Currency = "EGP",
            Provider = PaymentProvider.ManualWallet, PaymentMethod = PaymentMethod.BankTransfer, Status = PaymentIntentStatus.AwaitingVerification,
            PaymentReference = $"TEST-PAY-{Guid.NewGuid():N}", IdempotencyKey = Guid.NewGuid().ToString(),
            WalletType = "VODAFONECASH", ProofImageUrl = $"tenants/{_tenant}/manual-wallet-proofs/test-only.png",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1), ProofImageExpiresAtUtc = DateTime.UtcNow.AddDays(30) };
        _db.PaymentIntents.Add(intent);
        await _db.SaveChangesAsync();
        if (approve)
        {
            var approved = await Lifecycle(_db).HandlePaymentResultAsync(new HandlePaymentResultRequest {
                TenantId = _tenant, OrderId = order.Id, PaymentIntentId = intent.Id, Outcome = PaymentLifecycleOutcome.Succeeded,
                Source = PaymentLifecycleSource.PaymentReviewApproval, Actor = _actor.ToString(), ReviewProofRetentionExpiresAtUtc = DateTime.UtcNow.AddDays(30) });
            Assert.True(approved.Success, approved.ErrorMessage);
            _db.ChangeTracker.Clear();
            order = await _db.Orders.SingleAsync(o => o.Id == order.Id);
        }
        return order;
    }

    private async Task<ManualWalletSettlementRequest> Request(params Order[] orders)
    {
        var ids = orders.Select(o => o.Id).ToArray();
        var preview = await Lifecycle(_db).PreviewManualWalletSettlementAsync(_tenant, ids);
        return new(Guid.NewGuid(), ids, preview.PreviewToken);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Approval_ThenWholeWalletSettlement_BalancedSeparateEvents_ExactlyOnce(int count)
    {
        await Seed();
        var orders = new List<Order>();
        for (var i = 0; i < count; i++) orders.Add(await Wallet(amount: 100m + i));
        var amount = orders.Sum(o => o.Total);
        var cashbox = await _db.LedgerAccounts.SingleAsync(a => a.AccountCode == "1010");
        var clearing = await _db.LedgerAccounts.SingleAsync(a => a.AccountCode == "1140");
        Assert.Equal(0m, cashbox.CurrentBalance);
        Assert.Equal(amount, clearing.CurrentBalance);
        Assert.Equal(count, await _db.JournalEntries.CountAsync(j => j.SourceType == "Order_Wallet"));
        Assert.Equal(count, (await Lifecycle(_db).GetManualWalletSettlementSelectionAsync(_tenant)).Payments.Count(p => p.Eligible));
        var before = await new FinancialReportService(_db, NullLogger<FinancialReportService>.Instance).GetClearingAgingAsync(_tenant, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(amount, Assert.Single(before.Lines).PendingAmount);
        Assert.Equal(count, Assert.Single(before.Lines).TransactionCount);
        var request = await Request(orders.ToArray());
        var result = await Lifecycle(_db).RecordManualWalletSettlementAsync(_tenant, _actor, request);
        await using var fresh = _scope.CreateDbContext(_tenant);
        var replay = await Lifecycle(fresh).RecordManualWalletSettlementAsync(_tenant, _actor, request);
        Assert.True(replay.WasIdempotent);
        Assert.Equal(result.JournalId, replay.JournalId);
        Assert.Empty((await Lifecycle(fresh).GetManualWalletSettlementSelectionAsync(_tenant)).Payments);
        var journal = await fresh.JournalEntries.Include(j => j.LedgerEntries).SingleAsync(j => j.SourceType == "ManualWalletSettlement");
        Assert.Equal(amount, journal.TotalDebits);
        Assert.Equal(amount, journal.TotalCredits);
        Assert.Equal(_actor, journal.CreatedByUserId);
        Assert.Equal(2, journal.LedgerEntries.Count);
        Assert.Equal(amount, Assert.Single(journal.LedgerEntries, l => l.AccountId == cashbox.Id).DebitAmount);
        Assert.Equal(amount, Assert.Single(journal.LedgerEntries, l => l.AccountId == clearing.Id).CreditAmount);
        Assert.Equal(amount, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1010")).CurrentBalance);
        Assert.Equal(0m, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1140")).CurrentBalance);
        Assert.Equal(0m, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1130")).CurrentBalance);
        Assert.Equal(amount, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "4020")).CurrentBalance);
        Assert.All(await fresh.PaymentSettlementOrders.ToListAsync(), m => { Assert.NotNull(m.PaymentIntentId); Assert.Equal("EGP", m.Currency); });
        Assert.All(journal.LedgerEntries, l => Assert.Equal(_tenant, l.TenantId));
        var receipt = await fresh.AccountingOutboxMessages.SingleAsync(m => m.EventType == "ManualWalletSettlement");
        Assert.Equal(result.JournalId, receipt.JournalId);
        Assert.NotNull(receipt.ProcessedAtUtc);
        Assert.Equal(result.JournalId, (await ManualWalletSettlementAuthority.RecoverReceiptAsync(fresh, receipt, default)).JournalId);
        Assert.Equal(count + 1, await fresh.JournalEntries.CountAsync());
        Assert.Empty((await new FinancialReportService(fresh, NullLogger<FinancialReportService>.Instance).GetClearingAgingAsync(_tenant, DateTime.UtcNow.AddMinutes(1))).Lines);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("rejected")]
    [InlineData("voided")]
    [InlineData("cancelled")]
    [InlineData("refund")]
    [InlineData("unposted")]
    [InlineData("reversed")]
    [InlineData("unbalanced")]
    [InlineData("intent-amount")]
    [InlineData("foreign-tenant")]
    [InlineData("zero")]
    [InlineData("wrong-currency")]
    [InlineData("cod")]
    [InlineData("deleted")]
    public async Task InvalidSources_FailAtomically_AndCannotBeSelected(string defect)
    {
        await Seed();
        var order = await Wallet(defect is not ("pending" or "rejected" or "zero"), defect == "zero" ? 0m : 100m);
        if (defect == "rejected") (await _db.PaymentIntents.SingleAsync()).Status = PaymentIntentStatus.Rejected;
        if (defect == "voided") (await _db.PaymentIntents.SingleAsync()).Status = PaymentIntentStatus.Cancelled;
        if (defect == "cancelled") order.Status = OrderStatus.Cancelled;
        if (defect == "unposted") order.FinancialPostingStatus = FinancialPostingStatus.Pending;
        if (defect == "intent-amount") (await _db.PaymentIntents.SingleAsync()).Amount = 99m;
        if (defect == "wrong-currency") (await _db.Tenants.SingleAsync()).Currency = "USD";
        if (defect == "deleted") order.IsDeleted = true;
        await _db.SaveChangesAsync();
        // Corrupt only this disposable fixture to prove settlement independently rejects wrong source identity.
        // Normal application edits of immutable order payment-method snapshots are already forbidden.
        if (defect == "cod") await _db.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentMethodCode, "COD"));
        if (defect == "refund") Assert.True((await Lifecycle(_db).RecordRefundAsync(order.Id, 10m, "test-refund", "TEST-ONLY")).Success);
        if (defect == "reversed") await _db.JournalEntries.Where(j => j.SourceType == "Order_Wallet").ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JournalStatus.Reversed));
        if (defect == "unbalanced") await _db.LedgerEntries.Where(l => l.CreditAmount > 0m).ExecuteUpdateAsync(s => s.SetProperty(l => l.CreditAmount, 99m));
        var journalCount = await _db.JournalEntries.CountAsync();
        var balance = await _db.LedgerAccounts.AsNoTracking().OrderBy(a => a.AccountCode).Select(a => a.CurrentBalance).ToListAsync();
        var tenant = defect == "foreign-tenant" ? Guid.NewGuid() : _tenant;
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Lifecycle(_db).RecordManualWalletSettlementAsync(tenant, _actor, new(Guid.NewGuid(), new[] { order.Id }, "stale")));
        await using var fresh = _scope.CreateDbContext(_tenant);
        Assert.Empty(await fresh.PaymentSettlementBatches.ToListAsync());
        Assert.Empty(await fresh.PaymentSettlementOrders.ToListAsync());
        Assert.Equal(journalCount, await fresh.JournalEntries.CountAsync());
        Assert.Equal(balance, await fresh.LedgerAccounts.OrderBy(a => a.AccountCode).Select(a => a.CurrentBalance).ToListAsync());
        Assert.DoesNotContain((await Lifecycle(fresh).GetManualWalletSettlementSelectionAsync(_tenant)).Payments, p => p.Eligible && p.OrderId == order.Id && defect != "foreign-tenant");
    }

    [Fact]
    public async Task StalePreview_AlreadySettled_IdentityCollision_AndRefundDestination()
    {
        await Seed();
        var order = await Wallet();
        var request = await Request(order);
        await Assert.ThrowsAsync<ManualWalletSettlementException>(() => Lifecycle(_db).RecordManualWalletSettlementAsync(_tenant, _actor, request with { PreviewToken = "forged" }));
        Assert.Empty(await _db.PaymentSettlementBatches.ToListAsync());
        var result = await Lifecycle(_db).RecordManualWalletSettlementAsync(_tenant, _actor, request);
        await Assert.ThrowsAsync<ManualWalletSettlementException>(() => Lifecycle(_db).RecordManualWalletSettlementAsync(_tenant, _actor, request with { SettlementId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ManualWalletSettlementException>(() => Lifecycle(_db).RecordManualWalletSettlementAsync(_tenant, _actor, request with { OrderIds = new[] { Guid.NewGuid() } }));
        Assert.Null(await Lifecycle(_db).GetManualWalletSettlementAsync(Guid.NewGuid(), request.SettlementId));
        var refund = await Lifecycle(_db).RecordRefundAsync(order.Id, 25m, "post-settlement-wallet", "TEST-ONLY");
        Assert.True(refund.Success, refund.ErrorMessage);
        var journal = await _db.JournalEntries.Include(j => j.LedgerEntries).SingleAsync(j => j.SourceType == "Order_Refund");
        Assert.Equal(25m, Assert.Single(journal.LedgerEntries, l => l.AccountId == result.DestinationAccountId).CreditAmount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentConfirmations_AtMostOneJournal_RecoveryConverges(bool sameIdentity)
    {
        await Seed();
        var order = await Wallet();
        var first = await Request(order);
        var second = sameIdentity ? first : first with { SettlementId = Guid.NewGuid() };
        async Task<ManualWalletSettlementResult?> Submit(ManualWalletSettlementRequest request)
        {
            await using var context = _scope.CreateDbContext(_tenant);
            try { return await Lifecycle(context).RecordManualWalletSettlementAsync(_tenant, _actor, request); }
            catch (ManualWalletSettlementException) { return null; }
        }
        var results = await Task.WhenAll(Submit(first), Submit(second));
        Assert.Contains(results, r => r != null);
        if (!sameIdentity) Assert.Single(results, r => r != null);
        var winner = results.First(r => r != null)!;
        Assert.NotNull(await Submit(winner.SettlementId == first.SettlementId ? first : second));
        await using var fresh = _scope.CreateDbContext(_tenant);
        Assert.Single(await fresh.PaymentSettlementBatches.ToListAsync());
        Assert.Single(await fresh.PaymentSettlementOrders.ToListAsync());
        Assert.Equal(2, await fresh.JournalEntries.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostingOrEventFailure_RollsBackEverything_ThenSameRequestSucceeds(bool afterPosting)
    {
        await Seed();
        var order = await Wallet();
        var request = await Request(order);
        var events = new Mock<IEventPublisher>();
        ICommerceAccountingService? accounting = null;
        if (afterPosting) events.Setup(e => e.PublishAsync(It.IsAny<OrderPaymentStatusChangedEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("injected outbox failure"));
        else
        {
            var fail = new Mock<ICommerceAccountingService>();
            fail.Setup(a => a.PostManualWalletSettlementAsync(_tenant, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(PostingResult.Failed("injected"));
            accounting = fail.Object;
        }
        await Assert.ThrowsAnyAsync<Exception>(() => Lifecycle(_db, events, accounting).RecordManualWalletSettlementAsync(_tenant, _actor, request));
        await using var fresh = _scope.CreateDbContext(_tenant);
        Assert.Empty(await fresh.PaymentSettlementBatches.ToListAsync());
        Assert.Empty(await fresh.PaymentSettlementOrders.ToListAsync());
        Assert.Single(await fresh.JournalEntries.ToListAsync());
        Assert.Empty(await fresh.AccountingOutboxMessages.Where(m => m.EventType == "ManualWalletSettlement").ToListAsync());
        Assert.Equal(100m, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1140")).CurrentBalance);
        Assert.Equal("Posted", (await Lifecycle(fresh).RecordManualWalletSettlementAsync(_tenant, _actor, request)).AccountingOutcome);
    }

    [Fact]
    public async Task Aging_RemovesSettledOldReceipt_NotUnrelatedCurrentReceipt()
    {
        await Seed();
        var old = await Wallet();
        var current = await Wallet(amount: 50m);
        await _db.JournalEntries.Where(j => j.SourceId == old.Id).ExecuteUpdateAsync(s => s.SetProperty(j => j.JournalDate, DateTime.UtcNow.AddDays(-40)));
        var report = new FinancialReportService(_db, NullLogger<FinancialReportService>.Instance);
        var before = Assert.Single((await report.GetClearingAgingAsync(_tenant, DateTime.UtcNow.AddMinutes(1))).Lines);
        Assert.Equal(100m, before.Over30Days);
        Assert.Equal(50m, before.Current);
        Assert.Equal(2, before.TransactionCount);
        await Lifecycle(_db).RecordManualWalletSettlementAsync(_tenant, _actor, await Request(old));
        var after = Assert.Single((await report.GetClearingAgingAsync(_tenant, DateTime.UtcNow.AddMinutes(1))).Lines);
        Assert.Equal(50m, after.PendingAmount);
        Assert.Equal(50m, after.Current);
        Assert.Equal(0m, after.Over30Days);
        Assert.Equal(1, after.TransactionCount);
        Assert.Equal(0, after.AgeDays);
        Assert.Equal(current.Id, Assert.Single((await Lifecycle(_db).GetManualWalletSettlementSelectionAsync(_tenant)).Payments).OrderId);
    }

    [Fact]
    public async Task SettledPayment_DoesNotReappearInManualApprovalQueue()
    {
        await Seed();
        var settled = await Wallet();
        await Lifecycle(_db).RecordManualWalletSettlementAsync(_tenant, _actor, await Request(settled));
        var pending = await Wallet(approve: false);
        var controller = new AdminManualPaymentController(Mock.Of<IManualPaymentReviewApplicationService>(),
            Mock.Of<IManualPaymentCapabilityAdmissionAuthority>(), Mock.Of<IManualPaymentExpirationAuthority>(),
            Mock.Of<IManualPaymentRetentionAuthority>(), new TestTenantContext(_tenant), _db, Mock.Of<IR2StorageService>(),
            Options.Create(new R2Options()), NullLogger<AdminManualPaymentController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var response = Assert.IsType<OkObjectResult>(await controller.GetPendingPayments());
        var queue = Assert.IsType<PendingManualPaymentPagedResponse>(response.Value);
        Assert.Equal(pending.Id, Assert.Single(queue.Items).OrderId);
    }

    [Fact]
    public async Task ReversedApproval_ExcludesReceiptFromEligibilityAndAging()
    {
        await Seed();
        var reversed = await Wallet();
        var remaining = await Wallet(amount: 50m);
        await _db.JournalEntries.Where(j => j.SourceId == reversed.Id).ExecuteUpdateAsync(s => s.SetProperty(j => j.JournalDate, DateTime.UtcNow.AddDays(-40)));
        await new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance)
            .ReverseJournalAsync(_tenant, reversed.FinancialPostingJournalId!.Value, "TEST-ONLY reversal", _actor);
        var selection = await Lifecycle(_db).GetManualWalletSettlementSelectionAsync(_tenant);
        Assert.DoesNotContain(selection.Payments, p => p.OrderId == reversed.Id && p.Eligible);
        Assert.Contains(selection.Payments, p => p.OrderId == remaining.Id && p.Eligible);
        var aging = Assert.Single((await new FinancialReportService(_db, NullLogger<FinancialReportService>.Instance)
            .GetClearingAgingAsync(_tenant, DateTime.UtcNow.AddMinutes(1))).Lines);
        Assert.Equal(50m, aging.PendingAmount);
        Assert.Equal(1, aging.TransactionCount);
        Assert.Equal(0m, aging.Over30Days);
    }

    [Fact]
    public async Task WalletAndCod_AreIndependent_AndMixedSelectionCannotPartiallyPost()
    {
        await Seed();
        var wallet = await Wallet();
        var cod = new Order { TenantId = _tenant, OrderNumber = $"TEST-COD-{Guid.NewGuid():N}", Total = 30m, Currency = "EGP",
            PaymentMethodCode = "COD", PaymentGateway = "cod", PaymentStatus = PaymentStatus.Pending,
            FulfillmentMode = FulfillmentMode.Manual, FulfillmentStatus = FulfillmentStatus.Delivered, Status = OrderStatus.Delivered };
        cod.TransitionCheckoutState(CheckoutState.PaymentPending);
        _db.Orders.Add(cod);
        await _db.SaveChangesAsync();
        var collected = await Lifecycle(_db).MarkOfflineCollectionDeliveredAsync(_tenant, cod.Id, "TEST-ONLY delivery", _actor.ToString());
        Assert.True(collected.Success, collected.ErrorMessage);
        var request = await Request(wallet);
        await Assert.ThrowsAsync<ManualWalletSettlementException>(() => Lifecycle(_db).RecordManualWalletSettlementAsync(_tenant, _actor,
            request with { OrderIds = new[] { wallet.Id, cod.Id } }));
        Assert.Empty(await _db.PaymentSettlementBatches.ToListAsync());
        Assert.Equal(wallet.Id, Assert.Single((await Lifecycle(_db).GetManualWalletSettlementSelectionAsync(_tenant)).Payments).OrderId);
        Assert.Equal(cod.Id, Assert.Single((await Lifecycle(_db).GetManualCodSettlementSelectionAsync(_tenant)).Orders).OrderId);
        await Lifecycle(_db).RecordManualWalletSettlementAsync(_tenant, _actor, request);
        await Lifecycle(_db).RecordManualCodSettlementAsync(_tenant, _actor, new(Guid.NewGuid(), new[] { cod.Id }));
        await using var fresh = _scope.CreateDbContext(_tenant);
        Assert.Equal(130m, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1010")).CurrentBalance);
        Assert.Equal(0m, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1140")).CurrentBalance);
        Assert.Equal(0m, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1130")).CurrentBalance);
        Assert.Equal(2, await fresh.PaymentSettlementBatches.CountAsync());
    }

    [Theory]
    [InlineData(UserRole.Owner, true)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Manager, false)]
    [InlineData(UserRole.Cashier, false)]
    public async Task ActorAuthority_IsRevalidatedInSql(UserRole role, bool allowed)
    {
        await Seed();
        var order = await Wallet();
        var request = await Request(order);
        (await _db.Users.SingleAsync()).Role = role;
        await _db.SaveChangesAsync();
        if (allowed) Assert.Equal("Posted", (await Lifecycle(_db).RecordManualWalletSettlementAsync(_tenant, _actor, request)).AccountingOutcome);
        else await Assert.ThrowsAsync<ManualWalletSettlementException>(() => Lifecycle(_db).RecordManualWalletSettlementAsync(_tenant, _actor, request));
    }

    [Theory]
    [InlineData("grossAmount", "999")]
    [InlineData("currency", "\"USD\"")]
    [InlineData("tenantId", "\"00000000-0000-0000-0000-000000000001\"")]
    [InlineData("destinationAccountId", "\"00000000-0000-0000-0000-000000000001\"")]
    public void StrictRequest_RejectsCallerMoneyAndAuthority(string field, string value)
    {
        var body = $"{{\"settlementId\":\"{Guid.NewGuid()}\",\"orderIds\":[\"{Guid.NewGuid()}\"],\"previewToken\":\"x\",\"{field}\":{value}}}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ManualWalletSettlementApiRequest>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    public void Dispose() { _db.Dispose(); _scope.Dispose(); }
}
