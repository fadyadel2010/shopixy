using System.Data.Common;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Controllers;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
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
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class CarrierRemittancePostgresTests : IDisposable
{
    private readonly TestDatabaseScope scope = TestDatabaseScope.Open();
    private readonly Guid tenant = Guid.NewGuid();
    private readonly Guid actor = Guid.NewGuid();
    private readonly Guid config = Guid.NewGuid();
    private readonly ICorrelationIdAccessor correlation = Mock.Of<ICorrelationIdAccessor>(c => c.GetCorrelationId() == "isolated-carrier-receipt");

    private CarrierRemittanceService Service(ApplicationDbContext db, Guid? contextTenant = null) => new(Lifecycle(db),
        new TestTenantContext(contextTenant ?? tenant));

    private PaymentLifecycleService Lifecycle(ApplicationDbContext db)
    {
        var events = new Mock<IEventPublisher>(); events.SetReturnsDefault(Task.CompletedTask);
        var marketplace = new Mock<IMarketplaceEventOutboxService>(); marketplace.SetReturnsDefault(Task.CompletedTask);
        var stock = new Mock<IProductVariantStockRepository>(); stock.SetReturnsDefault(Task.CompletedTask);
        var accounting = new CommerceAccountingService(new FinancialPostingService(db, NullLogger<FinancialPostingService>.Instance),
            new AccountResolver(db, NullLogger<AccountResolver>.Instance), Mock.Of<ICashboxLedgerAccountService>(), NullLogger<CommerceAccountingService>.Instance, db);
        return new(db, new AccountingOutboxService(db, correlation, NullLogger<AccountingOutboxService>.Instance), accounting, events.Object,
            marketplace.Object, correlation, stock.Object, Mock.Of<IInventoryReservationMetricsService>(), Mock.Of<ICouponValidationService>(),
            NullLogger<PaymentLifecycleService>.Instance, Options.Create(new PaymentLifecycleSettings()));
    }

    private async Task Seed()
    {
        await using var db = scope.CreateDbContext(tenant);
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Isolated carrier cash receipt", Slug = $"receipt-{tenant:N}", Currency = "EGP", IsActive = true, Status = TenantStatus.Active });
        db.Users.Add(new User { Id = actor, TenantId = tenant, Username = $"receipt-{actor:N}", Email = $"{actor:N}@example.invalid", Role = UserRole.Owner, IsActive = true, PasswordHash = "test-only" });
        db.CarrierConfigurations.Add(new CarrierConfiguration { Id = config, TenantId = tenant, CarrierCode = "bosta", DisplayName = "Isolated carrier", AccountId = "account-1",
            ApiKey = "isolated-fixture-key", UseSandbox = true, IsEnabled = true, SupportsCod = true, SupportsWebhooks = true,
            Configuration = "{\"codCollectionCurrency\":\"EGP\"}" });
        await db.SaveChangesAsync();
        await new TenantAccountSeeder(db, NullLogger<TenantAccountSeeder>.Instance).SeedAsync(tenant);
    }

    private async Task<Guid> Order(bool collect = true, bool evidence = true, bool binding = true, string outcome = "Qualified", Guid? configurationId = null, string accountId = "account-1")
    {
        await using var db = scope.CreateDbContext(tenant);
        var id = Guid.NewGuid(); var shipment = Guid.NewGuid(); var bindingId = Guid.NewGuid(); var evidenceId = Guid.NewGuid();
        var configuration = configurationId ?? config;
        db.Orders.Add(new Order { Id = id, TenantId = tenant, OrderNumber = $"CARRIER-{id:N}", Total = 100, Currency = "EGP", PaymentMethodCode = "COD", PaymentGateway = "cod",
            PaymentStatus = PaymentStatus.Pending, FulfillmentMode = FulfillmentMode.Carrier, FulfillmentCarrierCode = "bosta", FulfillmentStatus = FulfillmentStatus.Delivered, Status = OrderStatus.Confirmed });
        var shipmentEntity = new Shipment { Id = shipment, TenantId = tenant, OrderId = id, ShipmentNumber = $"SHP-{shipment:N}", CarrierCode = "bosta", Currency = "EGP",
            IsCashOnDelivery = true, CodAmount = 100, Status = ShipmentStatus.Delivered, TrackingNumber = id.ToString("N"), ExternalReferenceId = shipment.ToString("N"),
            ExternalRegistrationState = "Completed", ExternalRegistrationCompletedAtUtc = DateTime.UtcNow.AddHours(-1) };
        db.Shipments.Add(shipmentEntity);
        var configurationEntity = await db.CarrierConfigurations.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == config);
        using (CarrierMoneyPersistenceAuthority.Begin())
        {
            if (binding) db.CarrierCodShipmentBindings.Add(new CarrierCodShipmentBinding { Id = bindingId, TenantId = tenant, OrderId = id, ShipmentId = shipment,
                ConfigurationId = configuration, CarrierCode = "bosta", AccountId = accountId, Environment = "Sandbox", Currency = "EGP", TrackingNumber = id.ToString("N"),
                DeliveryReference = shipment.ToString("N"), ExternalReferenceId = shipment.ToString("N"),
                Fingerprint = CarrierCollectionMoneyAuthority.BindingFingerprint(configurationEntity, shipmentEntity, accountId, "EGP", shipment.ToString("N")),
                CreatedAt = DateTime.UtcNow.AddHours(-1) });
            if (evidence) db.CarrierCollectionEvidence.Add(new CarrierCollectionEvidence { Id = evidenceId, TenantId = tenant, OrderId = id, ShipmentId = shipment,
                ConfigurationId = configuration, ShipmentBindingId = binding ? bindingId : null, CarrierCode = "bosta", AccountId = accountId, Environment = "Sandbox", Currency = "EGP",
                TrackingNumber = id.ToString("N"), DeliveryReference = shipment.ToString("N"), ObservedAmount = 100, ProviderEventAtUtc = DateTime.UtcNow.AddMinutes(-2),
                ReceivedAtUtc = DateTime.UtcNow, SourceContract = "bosta:delivery-status-webhook:v1", TrustMethod = "ConfiguredWebhookSecret", PayloadHash = new string('B', 64),
                ObservationFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id.ToString()))),
                AdmissionOutcome = outcome, AdmissionCode = outcome == "Qualified" ? "COD_AMOUNT_QUALIFIED" : "COD_ACCOUNT_BINDING_REQUIRED", AuditIdentity = "isolated-observed-money-fixture" });
            await db.SaveChangesAsync();
        }
        if (collect)
        {
            var result = await Lifecycle(db).RecordCarrierCodCollectionAsync(tenant, id, shipment, evidenceId);
            Assert.True(result.Success, $"{result.FailureCode}: {result.ErrorMessage}");
        }
        return id;
    }

    private static CarrierRemittanceRequest Request(params Guid[] orders) => new(Guid.NewGuid(), orders,
        DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds()), $"RECEIPT-{Guid.NewGuid():N}", true);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task QualifiedPostedCollection_PhysicalReceipt_PostsExactLinesBalancesOutboxesAndReplay(int count)
    {
        await Seed(); var ids = new List<Guid>(); for (var i = 0; i < count; i++) ids.Add(await Order());
        await using var db = scope.CreateDbContext(tenant);
        Assert.Equal(count, (await Service(db).SelectAsync(tenant, actor)).Orders.Count);
        var request = Request(ids.ToArray()); var result = await Service(db).ReceiveAsync(tenant, actor, request);
        Assert.Equal("Posted", result.Outcome); Assert.Equal("OperatorAttestedPhysicalReceipt", result.ConfirmationBasis);
        Assert.Equal(100m * count, result.ReceivedAmount); Assert.Equal(0m, result.Fees);
        await using var fresh = scope.CreateDbContext(tenant);
        var replay = await Service(fresh).ReceiveAsync(tenant, actor, request with { OrderIds = ids.AsEnumerable().Reverse().ToArray() });
        Assert.True(replay.WasIdempotent); Assert.Equal(result.JournalId, replay.JournalId); Assert.Equal(actor, replay.OriginalOperatorId);
        Assert.Equal(result.JournalId, (await Service(fresh).GetAsync(tenant, actor, request.OperationId))!.JournalId);
        var journal = await fresh.JournalEntries.Include(j => j.LedgerEntries).SingleAsync(j => j.SourceType == CarrierRemittanceAuthority.SourceType);
        Assert.Equal(JournalStatus.Posted, journal.Status); Assert.Equal(actor, journal.CreatedByUserId); Assert.Equal(2, journal.LedgerEntries.Count);
        var accounts = await fresh.LedgerAccounts.ToDictionaryAsync(a => a.AccountCode);
        Assert.Equal(count * 100m, Assert.Single(journal.LedgerEntries, l => l.AccountId == accounts["1010"].Id).DebitAmount);
        Assert.Equal(count * 100m, Assert.Single(journal.LedgerEntries, l => l.AccountId == accounts["1130"].Id).CreditAmount);
        Assert.Equal(count * 100m, accounts["1010"].CurrentBalance); Assert.Equal(0m, accounts["1130"].CurrentBalance);
        Assert.Equal(count, await fresh.PaymentSettlementOrders.CountAsync()); Assert.Equal(count, await fresh.CarrierRemittanceOrders.CountAsync());
        Assert.Single(await fresh.CarrierRemittances.ToListAsync()); Assert.Equal(count + 1, await fresh.JournalEntries.CountAsync());
        Assert.All(await fresh.Orders.ToListAsync(), o => Assert.Equal(PaymentStatus.Settled, o.PaymentStatus));
        var receipt = await fresh.AccountingOutboxMessages.SingleAsync(m => m.SourceType == CarrierRemittanceAuthority.SourceType);
        Assert.Equal(journal.Id, receipt.JournalId); Assert.NotNull(receipt.ProcessedAtUtc);
        Assert.Equal(journal.Id, (await CarrierRemittanceAuthority.RecoverReceiptAsync(fresh, receipt, default)).JournalId);
        Assert.Equal(count + 1, await fresh.OutboxMessages.CountAsync());
        Assert.Empty((await Service(fresh).SelectAsync(tenant, actor)).Orders);
    }

    [Theory]
    [InlineData("delivered-only")]
    [InlineData("mutable-flag")]
    [InlineData("missing-binding")]
    [InlineData("review")]
    [InlineData("posting-pending")]
    [InlineData("mismatched-journal")]
    [InlineData("journal-reversed")]
    [InlineData("refund")]
    [InlineData("hold")]
    [InlineData("dispute")]
    [InlineData("account-changed")]
    [InlineData("environment-changed")]
    [InlineData("currency")]
    [InlineData("inactive-cashbox")]
    [InlineData("inactive-clearing")]
    [InlineData("deleted-shipment")]
    [InlineData("residual")]
    [InlineData("duplicate")]
    [InlineData("balance-drift")]
    public async Task InconsistentUnqualifiedOrConflictingCollection_FailsWithoutAnyReceiptMovement(string defect)
    {
        await Seed(); var collected = defect is not ("delivered-only" or "mutable-flag" or "missing-binding" or "review");
        var id = await Order(collected, evidence: defect is not ("delivered-only" or "mutable-flag"), binding: defect != "missing-binding",
            outcome: defect is "review" or "missing-binding" ? "Review" : "Qualified");
        await using var db = scope.CreateDbContext(tenant);
        if (defect == "mutable-flag") await db.Shipments.ExecuteUpdateAsync(s => s.SetProperty(x => x.CodCollected, true));
        if (defect == "posting-pending") await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(x => x.FinancialPostingStatus, FinancialPostingStatus.Pending));
        if (defect == "mismatched-journal") await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(x => x.FinancialPostingJournalId, Guid.NewGuid()));
        if (defect == "journal-reversed") await db.Database.ExecuteSqlRawAsync("UPDATE \"LedgerJournalEntries\" SET \"Status\"=2");
        if (defect == "hold") await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, OrderStatus.OnHold));
        if (defect == "dispute")
        {
            db.PaymentIntents.Add(new PaymentIntent { Id = Guid.NewGuid(), TenantId = tenant, OrderId = id, Status = PaymentIntentStatus.Suspicious,
                PaymentReference = $"DISPUTE-{id:N}", Currency = "EGP", Amount = 100 }); await db.SaveChangesAsync();
        }
        if (defect == "account-changed") await db.CarrierConfigurations.ExecuteUpdateAsync(s => s.SetProperty(x => x.AccountId, "other-account"));
        if (defect == "environment-changed") await db.CarrierConfigurations.ExecuteUpdateAsync(s => s.SetProperty(x => x.UseSandbox, false));
        if (defect == "currency") await db.Tenants.ExecuteUpdateAsync(s => s.SetProperty(x => x.Currency, "USD"));
        if (defect == "inactive-cashbox") await db.TreasuryAccounts.Where(a => a.Type == TreasuryAccountType.Cashbox).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        if (defect == "inactive-clearing") await db.LedgerAccounts.Where(a => a.AccountCode == "1130").ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        if (defect == "deleted-shipment") await db.Shipments.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
        if (defect == "balance-drift") await db.LedgerAccounts.Where(a => a.AccountCode == "1130").ExecuteUpdateAsync(s => s.SetProperty(a => a.CurrentBalance, 99m));
        if (defect == "refund") Assert.True((await Lifecycle(db).RecordRefundAsync(id, 10, "prior-refund", "fixture")).Success);
        if (defect == "residual")
        {
            var clearing = await db.LedgerAccounts.SingleAsync(a => a.AccountCode == "1130"); var sales = await db.LedgerAccounts.SingleAsync(a => a.AccountCode == "4020");
            await new FinancialPostingService(db, NullLogger<FinancialPostingService>.Instance).PostJournalAsync(new() { TenantId = tenant, SourceId = id, SourceType = "IsolatedResidual",
                IdempotencyKey = $"residual:{id}", Description = "Isolated conflict", JournalDate = DateTime.UtcNow,
                Lines = [PointOfSale.Core.Models.Accounting.PostingLine.Debit(sales.Id, 1), PointOfSale.Core.Models.Accounting.PostingLine.Credit(clearing.Id, 1)] });
        }
        var before = await db.LedgerAccounts.AsNoTracking().OrderBy(a => a.AccountCode).Select(a => a.CurrentBalance).ToListAsync();
        var journals = await db.JournalEntries.CountAsync();
        await Assert.ThrowsAsync<CarrierRemittanceException>(() => Service(db).ReceiveAsync(tenant, actor, defect == "duplicate" ? Request(id, id) : Request(id)));
        await using var fresh = scope.CreateDbContext(tenant);
        Assert.Empty(await fresh.CarrierRemittances.ToListAsync()); Assert.Empty(await fresh.PaymentSettlementOrders.ToListAsync());
        Assert.Equal(journals, await fresh.JournalEntries.CountAsync());
        Assert.Equal(before, await fresh.LedgerAccounts.OrderBy(a => a.AccountCode).Select(a => a.CurrentBalance).ToListAsync());
    }

    [Theory]
    [InlineData("no-attestation")]
    [InlineData("empty-reference")]
    [InlineData("future-time")]
    [InlineData("bank-channel")]
    [InlineData("cashier")]
    [InlineData("inactive-actor")]
    [InlineData("foreign-tenant")]
    [InlineData("inactive-tenant")]
    public async Task ReceiptAndCurrentActorAdmission_FailClosed(string defect)
    {
        await Seed(); var id = await Order(); var request = Request(id);
        await using var db = scope.CreateDbContext(tenant);
        if (defect == "no-attestation") request = request with { ActualReceiptAttested = false };
        if (defect == "empty-reference") request = request with { ReceiptReference = " " };
        if (defect == "future-time") request = request with { ReceivedAt = DateTimeOffset.UtcNow.AddDays(1) };
        if (defect == "bank-channel") request = request with { ReceiptChannel = "Bank" };
        if (defect == "cashier") await db.Users.ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, UserRole.Cashier));
        if (defect == "inactive-actor") await db.Users.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        if (defect == "inactive-tenant") await db.Tenants.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        await Assert.ThrowsAsync<CarrierRemittanceException>(() => Service(db, defect == "foreign-tenant" ? Guid.NewGuid() : tenant).ReceiveAsync(tenant, actor, request));
        Assert.Empty(await db.CarrierRemittances.ToListAsync());
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("time")]
    [InlineData("notes")]
    [InlineData("orders")]
    public async Task SameOperationChangedFacts_Conflict_AndNewOperationCannotReuseOrders(string change)
    {
        await Seed(); var id = await Order(); var another = await Order(); var request = Request(id);
        await using var db = scope.CreateDbContext(tenant); await Service(db).ReceiveAsync(tenant, actor, request);
        var changed = change switch { "reference" => request with { ReceiptReference = "changed" }, "time" => request with { ReceivedAt = request.ReceivedAt.AddSeconds(1) },
            "notes" => request with { Notes = "changed" }, _ => request with { OrderIds = [another] } };
        await Assert.ThrowsAsync<CarrierRemittanceException>(() => Service(db).ReceiveAsync(tenant, actor, changed));
        await Assert.ThrowsAsync<CarrierRemittanceException>(() => Service(db).ReceiveAsync(tenant, actor, Request(id)));
        Assert.Single(await db.CarrierRemittances.ToListAsync());
    }

    [Fact]
    public async Task ReusedMerchantReference_CannotBookAnotherReceipt()
    {
        await Seed(); var first = await Order(); var second = await Order(); var request = Request(first);
        await using var db = scope.CreateDbContext(tenant); await Service(db).ReceiveAsync(tenant, actor, request);
        var error = await Assert.ThrowsAsync<CarrierRemittanceException>(() => Service(db).ReceiveAsync(tenant, actor, Request(second) with { ReceiptReference = " " + request.ReceiptReference.ToLowerInvariant() + " " }));
        Assert.Equal("REMITTANCE_RECEIPT_REFERENCE_REVIEW_REQUIRED", error.Code); Assert.Single(await db.CarrierRemittances.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentOverlap_OnlyOnePermanentAllocationAndPosting(bool sameOperation)
    {
        await Seed(); var id = await Order(); var request = Request(id);
        async Task<CarrierRemittanceResult?> Send(CarrierRemittanceRequest r)
        {
            await using var db = scope.CreateDbContext(tenant);
            try { return await Service(db).ReceiveAsync(tenant, actor, r); } catch (CarrierRemittanceException) { return null; }
        }
        var results = await Task.WhenAll(Send(request), Send(sameOperation ? request : Request(id)));
        Assert.Contains(results, r => r != null); if (!sameOperation) Assert.Single(results, r => r != null);
        await using var fresh = scope.CreateDbContext(tenant);
        Assert.Single(await fresh.CarrierRemittances.ToListAsync()); Assert.Single(await fresh.PaymentSettlementOrders.ToListAsync());
        Assert.Equal(2, await fresh.JournalEntries.CountAsync()); Assert.Equal(100, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1010")).CurrentBalance);
    }

    [Fact]
    public async Task LostResponse_RefundDestination_AndReplayAuthorizationPreserveOriginalActor()
    {
        await Seed(); var id = await Order(); var request = Request(id);
        await using var db = scope.CreateDbContext(tenant); var original = await Service(db).ReceiveAsync(tenant, actor, request);
        var recovered = await Service(db).GetAsync(tenant, actor, request.OperationId); Assert.Equal(original.JournalId, recovered!.JournalId);
        var refund = await Lifecycle(db).RecordRefundAsync(id, 25, "cash-receipt-refund", "isolated refund"); Assert.True(refund.Success, refund.ErrorMessage);
        await using var fresh = scope.CreateDbContext(tenant);
        var refundJournal = await fresh.JournalEntries.Include(j => j.LedgerEntries).SingleAsync(j => j.SourceType == "Order_Refund");
        Assert.Equal(25, Assert.Single(refundJournal.LedgerEntries, l => l.AccountId == original.DestinationAccountId).CreditAmount);
        Assert.Equal(75, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1010")).CurrentBalance);
        Assert.Equal(0, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1130")).CurrentBalance);
        await fresh.Users.ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, UserRole.Cashier));
        await Assert.ThrowsAsync<CarrierRemittanceException>(() => Service(fresh).GetAsync(tenant, actor, request.OperationId));
        await Assert.ThrowsAsync<CarrierRemittanceException>(() => Service(fresh).ReceiveAsync(tenant, actor, request));
    }

    [Theory]
    [InlineData("journal")]
    [InlineData("balance")]
    [InlineData("accounting-outbox")]
    [InlineData("domain-outbox")]
    [InlineData("commit")]
    [InlineData("deferred-commit")]
    [InlineData("payment-domain-commit")]
    public async Task InjectedSqlOrCommitFailure_RollsBackAllFinancialWrites(string stage)
    {
        await Seed(); var id = await Order(); var request = Request(id);
        await using var baseDb = scope.CreateDbContext(tenant);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(baseDb.Database.GetConnectionString())
            .AddInterceptors(new Failure(stage), new CommitFailure(stage)).Options;
        await using (var failing = new ApplicationDbContext(options, new TestTenantContext(tenant)))
            await Assert.ThrowsAnyAsync<Exception>(() => Service(failing).ReceiveAsync(tenant, actor, request));
        await using var fresh = scope.CreateDbContext(tenant);
        Assert.Empty(await fresh.CarrierRemittances.ToListAsync()); Assert.Empty(await fresh.CarrierRemittanceOrders.ToListAsync());
        Assert.Empty(await fresh.PaymentSettlementBatches.ToListAsync()); Assert.Empty(await fresh.PaymentSettlementOrders.ToListAsync()); Assert.Empty(await fresh.OutboxMessages.ToListAsync());
        Assert.Single(await fresh.JournalEntries.ToListAsync()); Assert.Single(await fresh.AccountingOutboxMessages.ToListAsync());
        Assert.Equal(100, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1130")).CurrentBalance);
        Assert.Equal(0, (await fresh.LedgerAccounts.SingleAsync(a => a.AccountCode == "1010")).CurrentBalance);
        Assert.Equal(PaymentStatus.Collected, (await fresh.Orders.SingleAsync()).PaymentStatus);
        Assert.Equal("Posted", (await Service(fresh).ReceiveAsync(tenant, actor, request)).Outcome);
    }

    private sealed class Failure(string stage) : DbCommandInterceptor
    {
        private void Check(DbCommand c)
        {
            var match = stage switch { "journal" => "INSERT INTO \"LedgerJournalEntries\"", "balance" => "UPDATE \"LedgerAccounts\"",
                "accounting-outbox" => "INSERT INTO \"AccountingOutboxMessages\"", "domain-outbox" => "INSERT INTO \"OutboxMessages\"", _ => "never-injected" };
            if (c.CommandText.Contains(match, StringComparison.Ordinal)) throw new InvalidOperationException($"Injected {stage} SQL failure");
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Check(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { Check(command); return ValueTask.FromResult(result); }
    }

    [Fact]
    public async Task BatchCannotMixImmutableCarrierAccountBindings()
    {
        await Seed(); var first = await Order();
        await using var db = scope.CreateDbContext(tenant);
        // A tenant has one canonical configuration per carrier. Changing its account
        // cannot rewrite the first shipment's immutable provenance to pool its money.
        await db.CarrierConfigurations.ExecuteUpdateAsync(s => s.SetProperty(c => c.AccountId, "account-2"));
        var second = await Order(accountId: "account-2");
        var error = await Assert.ThrowsAsync<CarrierRemittanceException>(() => Service(db).ReceiveAsync(tenant, actor, Request(first, second)));
        Assert.Equal("COD_ACCOUNT_BINDING_CHANGED", error.Code); Assert.Empty(await db.PaymentSettlementOrders.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnyPriorPermanentAllocation_EvenSoftDeleted_BlocksNewCarrierMembership(bool deleted)
    {
        await Seed(); var id = await Order();
        await using var db = scope.CreateDbContext(tenant);
        var batch = new PaymentSettlementBatch { Id = Guid.NewGuid(), TenantId = tenant, SettlementType = "OFFLINE_COLLECTION", SettlementId = $"legacy-{Guid.NewGuid()}",
            Currency = "EGP", GrossAmount = 100, OrderCount = 1, SettledAtUtc = DateTime.UtcNow, CreatedBy = actor };
        batch.Orders.Add(new PaymentSettlementOrder { Id = Guid.NewGuid(), TenantId = tenant, SettlementBatchId = batch.Id, OrderId = id, Currency = "EGP", OrderTotal = 100, IsDeleted = deleted });
        db.PaymentSettlementBatches.Add(batch); await db.SaveChangesAsync();
        // The additive carrier-only trigger must not suppress an existing non-carrier SQL update.
        await db.PaymentSettlementOrders.Where(m => m.OrderId == id).IgnoreQueryFilters().ExecuteUpdateAsync(s => s.SetProperty(m => m.IsDeleted, !deleted));
        Assert.Equal(!deleted, await db.PaymentSettlementOrders.IgnoreQueryFilters().AsNoTracking().Where(m => m.OrderId == id).Select(m => m.IsDeleted).SingleAsync());
        await db.PaymentSettlementOrders.IgnoreQueryFilters().Where(m => m.OrderId == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsDeleted, deleted));
        var error = await Assert.ThrowsAsync<CarrierRemittanceException>(() => Service(db).ReceiveAsync(tenant, actor, Request(id)));
        Assert.Equal("REMITTANCE_ALREADY_ALLOCATED", error.Code); Assert.Empty(await db.CarrierRemittances.ToListAsync());
    }

    [Fact]
    public async Task PermanentSqlIdentities_Immutability_GlobalFilters_AndPostingSourceCannotBeBypassed()
    {
        await Seed(); var id = await Order(); var request = Request(id);
        await using var db = scope.CreateDbContext(tenant); var result = await Service(db).ReceiveAsync(tenant, actor, request);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CarrierRemittances\" SET \"ReceiptReference\"='changed' WHERE \"Id\"={request.OperationId}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"CarrierRemittanceOrders\" WHERE \"RemittanceId\"={request.OperationId}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PaymentSettlementOrders\" SET \"IsDeleted\"=true WHERE \"OrderId\"={id}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PaymentSettlementBatches\" WHERE \"Id\"={request.OperationId}"));
        var legacy = new PaymentSettlementBatch { Id = Guid.NewGuid(), TenantId = tenant, SettlementType = "OFFLINE_COLLECTION", SettlementId = "isolated-legacy", Currency = "EGP", GrossAmount = 100, OrderCount = 1, SettledAtUtc = DateTime.UtcNow };
        db.PaymentSettlementBatches.Add(legacy); await db.SaveChangesAsync();
        var reusedReference = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"CarrierRemittances\" SELECT (jsonb_populate_record(NULL::\"CarrierRemittances\", to_jsonb(r) || jsonb_build_object('Id', {legacy.Id}))).* FROM \"CarrierRemittances\" r WHERE \"Id\"={request.OperationId}"));
        Assert.Equal("23505", reusedReference.SqlState);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PaymentSettlementOrders\" SELECT {Guid.NewGuid()}, \"TenantId\", {legacy.Id}, \"OrderId\", \"PaymentIntentId\", \"OrderTotal\", \"Currency\", \"CreatedAt\", \"UpdatedAt\", \"CreatedBy\", \"UpdatedBy\", \"IsDeleted\" FROM \"PaymentSettlementOrders\" WHERE \"OrderId\"={id}"));
        Assert.Equal("23505", duplicate.SqlState);
        var other = await Order();
        var append = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PaymentSettlementOrders\" SELECT {Guid.NewGuid()}, \"TenantId\", \"SettlementBatchId\", {other}, \"PaymentIntentId\", \"OrderTotal\", \"Currency\", \"CreatedAt\", \"UpdatedAt\", \"CreatedBy\", \"UpdatedBy\", \"IsDeleted\" FROM \"PaymentSettlementOrders\" WHERE \"OrderId\"={id}"));
        Assert.Equal("55000", append.SqlState);
        await using var foreign = scope.CreateDbContext(Guid.NewGuid());
        Assert.Empty(await foreign.CarrierRemittances.ToListAsync()); Assert.Empty(await foreign.CarrierRemittanceOrders.ToListAsync());
        await Assert.ThrowsAsync<CarrierRemittanceException>(() => CarrierRemittanceAuthority.BuildPostingAsync(db, tenant, request.OperationId, default));
        db.ChangeTracker.Clear(); var receipt = await db.CarrierRemittances.SingleAsync(); receipt.Notes = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Null(await Service(foreign, tenant).GetAsync(tenant, actor, request.OperationId));
    }

    [Theory]
    [InlineData("short-journal")]
    [InlineData("missing-evidence")]
    [InlineData("later-review")]
    [InlineData("collection-receipt-pending")]
    public async Task ExactPostedJournalAndImmutableEvidenceRemainMandatory(string defect)
    {
        await Seed(); var id = await Order(defect != "missing-evidence", evidence: defect != "missing-evidence");
        await using var db = scope.CreateDbContext(tenant);
        if (defect == "missing-evidence")
        {
            var accounts = await db.LedgerAccounts.ToDictionaryAsync(a => a.AccountCode);
            var order = await db.Orders.SingleAsync();
            var journal = await new FinancialPostingService(db, NullLogger<FinancialPostingService>.Instance).PostJournalAsync(new() {
                TenantId = tenant, SourceId = id, SourceType = "Order_COD", SourceReference = order.OrderNumber, JournalDate = DateTime.UtcNow,
                Description = "Isolated legacy flags-only collection", IdempotencyKey = $"COD_DELIVERED:{tenant}:{id}",
                Lines = [PointOfSale.Core.Models.Accounting.PostingLine.Debit(accounts["1130"].Id, 100), PointOfSale.Core.Models.Accounting.PostingLine.Credit(accounts["4020"].Id, 100)] });
            await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentStatus, PaymentStatus.Collected).SetProperty(o => o.IsPaid, true)
                .SetProperty(o => o.PaidAt, DateTime.UtcNow).SetProperty(o => o.FinancialPostingStatus, FinancialPostingStatus.Posted)
                .SetProperty(o => o.FinancialPostingPendingAtUtc, DateTime.UtcNow).SetProperty(o => o.FinancialPostedAtUtc, DateTime.UtcNow)
                .SetProperty(o => o.FinancialPostingJournalId, journal.Id));
        }
        if (defect == "short-journal") await db.Database.ExecuteSqlRawAsync("UPDATE \"LedgerLines\" SET \"DebitAmount\"=99 WHERE \"DebitAmount\"=100");
        if (defect == "later-review")
        {
            using var scope = CarrierMoneyPersistenceAuthority.Begin(); var evidence = await db.CarrierCollectionEvidence.SingleAsync();
            await CarrierCollectionMoneyAuthority.StageReviewAsync(db, evidence, "COD_DISPUTE_REVIEW", default); await db.SaveChangesAsync();
        }
        if (defect == "collection-receipt-pending") await db.AccountingOutboxMessages.ExecuteUpdateAsync(s => s.SetProperty(o => o.ProcessedAtUtc, (DateTime?)null));
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<CarrierRemittanceException>(() => Service(db).ReceiveAsync(tenant, actor, Request(id)));
        Assert.Empty(await db.CarrierRemittances.ToListAsync());
    }

    [Fact]
    public async Task AuthorizedReplayByAnotherCurrentOperator_RetainsOriginalActor_AndForeignOrderFails()
    {
        await Seed(); var id = await Order(); var request = Request(id);
        await using var db = scope.CreateDbContext(tenant); var result = await Service(db).ReceiveAsync(tenant, actor, request);
        var second = Guid.NewGuid(); db.Users.Add(new User { Id = second, TenantId = tenant, Username = $"second-{second}", Email = $"{second}@example.invalid", Role = UserRole.Admin, IsActive = true, PasswordHash = "test" });
        await db.SaveChangesAsync();
        var replay = await Service(db).ReceiveAsync(tenant, second, request); Assert.Equal(actor, replay.OriginalOperatorId); Assert.Equal(result.JournalId, replay.JournalId);
        var other = Guid.NewGuid(); var otherActor = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = other, Name = "Foreign isolated tenant", Slug = $"foreign-{other}", Currency = "EGP", IsActive = true, Status = TenantStatus.Active });
        db.Users.Add(new User { Id = otherActor, TenantId = other, Username = $"foreign-{otherActor}", Email = $"{otherActor}@example.invalid", Role = UserRole.Owner, IsActive = true, PasswordHash = "test" }); await db.SaveChangesAsync();
        await using var foreign = scope.CreateDbContext(other);
        Assert.Null(await Service(foreign, other).GetAsync(other, otherActor, request.OperationId));
        await Assert.ThrowsAsync<CarrierRemittanceException>(() => Service(foreign, other).ReceiveAsync(other, otherActor, Request(id)));
    }

    [Fact]
    public async Task ControllerUsesAuthenticatedActorAndSqlPermission_ForExecutionAndRecovery()
    {
        await Seed(); var id = await Order(); var request = Request(id);
        await using var db = scope.CreateDbContext(tenant);
        var controller = new CarrierRemittancesController(Service(db), new TestTenantContext(tenant)) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, actor.ToString()), new Claim(ClaimTypes.Role, "Owner")], "test-sql-operator")) } } };
        var selected = Assert.IsType<OkObjectResult>(await controller.Select()); Assert.Single(Assert.IsType<CarrierRemittanceSelection>(selected.Value).Orders);
        var posted = Assert.IsType<CarrierRemittanceResult>(Assert.IsType<OkObjectResult>(await controller.Receive(request)).Value);
        Assert.Equal(actor, posted.OriginalOperatorId); Assert.IsType<OkObjectResult>(await controller.Get(request.OperationId));
        await db.Users.ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.Cashier));
        Assert.IsType<ForbidResult>(await controller.Get(request.OperationId)); Assert.IsType<ForbidResult>(await controller.Receive(request));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CarrierRemittanceRequest>("{\"tenantId\":\"forged\",\"actorId\":\"forged\",\"amount\":1}"));
        Assert.True(PointOfSale.API.Security.PosEndpointPermissionCatalog.TryResolve("CarrierRemittances", "Get", ["GET"], out var permission));
        Assert.Equal(PointOfSale.Core.Security.PosPermissions.StorefrontPaymentsManage, permission);
    }
    [Theory]
    [InlineData("allocation")]
    [InlineData("batch-type")]
    public async Task NonCarrierSqlUpdate_CannotEnterPermanentCarrierHistory(string change)
    {
        await Seed(); var first = await Order(); var second = await Order();
        await using var db = scope.CreateDbContext(tenant);
        var receipt = await Service(db).ReceiveAsync(tenant, actor, Request(first));
        var legacy = new PaymentSettlementBatch { Id = Guid.NewGuid(), TenantId = tenant, SettlementType = "OFFLINE_COLLECTION",
            SettlementId = $"legacy-{Guid.NewGuid()}", Currency = "EGP", GrossAmount = 100, OrderCount = 1, SettledAtUtc = DateTime.UtcNow };
        var member = new PaymentSettlementOrder { Id = Guid.NewGuid(), TenantId = tenant, SettlementBatchId = legacy.Id,
            OrderId = second, Currency = "EGP", OrderTotal = 100 };
        legacy.Orders.Add(member); db.PaymentSettlementBatches.Add(legacy); await db.SaveChangesAsync();
        // Valid non-carrier UPDATE must still persist NEW, rather than being suppressed by the trigger.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PaymentSettlementBatches\" SET \"BankReference\"='legacy-note' WHERE \"Id\"={legacy.Id}");
        Assert.Equal("legacy-note", await db.PaymentSettlementBatches.AsNoTracking().Where(b => b.Id == legacy.Id).Select(b => b.BankReference).SingleAsync());
        var error = change == "allocation"
            ? await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PaymentSettlementOrders\" SET \"SettlementBatchId\"={receipt.OperationId} WHERE \"Id\"={member.Id}"))
            : await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PaymentSettlementBatches\" SET \"SettlementType\"='CARRIER_CASH_RECEIPT' WHERE \"Id\"={legacy.Id}"));
        Assert.Equal("55000", error.SqlState);
        Assert.Equal(legacy.Id, await db.PaymentSettlementOrders.AsNoTracking().Where(m => m.Id == member.Id).Select(m => m.SettlementBatchId).SingleAsync());
        Assert.Equal("OFFLINE_COLLECTION", await db.PaymentSettlementBatches.AsNoTracking().Where(b => b.Id == legacy.Id).Select(b => b.SettlementType).SingleAsync());
        Assert.Single(await db.CarrierRemittanceOrders.ToListAsync());
    }

    [Fact]
    public async Task AccountingReceiptRecovery_RejectsIncompletePostedFinancialLineage()
    {
        await Seed(); var id = await Order();
        await using var db = scope.CreateDbContext(tenant);
        var result = await Service(db).ReceiveAsync(tenant, actor, Request(id));
        var message = await db.AccountingOutboxMessages.SingleAsync(m => m.SourceType == CarrierRemittanceAuthority.SourceType);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"LedgerLines\" SET \"DebitAmount\"=99 WHERE \"JournalEntryId\"={result.JournalId} AND \"DebitAmount\"=100");
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CarrierRemittanceAuthority.RecoverReceiptAsync(db, message, default));
        Assert.Single(await db.CarrierRemittances.ToListAsync());
        Assert.Equal(2, await db.JournalEntries.CountAsync());
    }

    private sealed class CommitFailure(string stage) : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction tx, TransactionEventData data, InterceptionResult result, CancellationToken ct = default)
        {
            if (stage == "commit") throw new InvalidOperationException("Injected final commit failure");
            if (stage == "deferred-commit")
            {
                await using var command = tx.Connection!.CreateCommand(); command.Transaction = tx;
                command.CommandText = "DELETE FROM \"AccountingOutboxMessages\" WHERE \"SourceType\"='CarrierCashRemittance'";
                Assert.Equal(1, await command.ExecuteNonQueryAsync(ct));
                // PostgreSQL itself rejects COMMIT through the deferred Posted/outbox
                // constraint, after all journal, balance and state writes have executed.
            }
            if (stage == "payment-domain-commit")
            {
                await using var command = tx.Connection!.CreateCommand(); command.Transaction = tx;
                command.CommandText = "DELETE FROM \"OutboxMessages\" WHERE \"EventType\" LIKE '%OrderPaymentStatusChangedEvent%'";
                Assert.Equal(1, await command.ExecuteNonQueryAsync(ct));
            }
            return result;
        }
    }

    public void Dispose() => scope.Dispose();
}
