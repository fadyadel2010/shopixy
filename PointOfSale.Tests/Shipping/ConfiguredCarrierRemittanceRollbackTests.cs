using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
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
using Xunit.Abstractions;

namespace PointOfSale.Tests.Shipping;

/// <summary>
/// Explicit configured-provider qualification, NOT a TestDatabaseScope fixture.
/// Seeds only a new manifested tenant inside the executor's own transaction.
/// No commit is permitted: deferred constraints are checked then a sentinel forces
/// production rollback. No broker, provider, stock service or hosted worker runs.
/// </summary>
public sealed class ConfiguredCarrierRemittanceRollbackTests(ITestOutputHelper output)
{
    [ConfiguredRemittanceRollbackTheory]
    [InlineData("positive", 1)][InlineData("positive", 3)]
    [InlineData("manual-positive", 1)][InlineData("manual-positive", 3)]
    [InlineData("journal", 1)][InlineData("balance", 1)]
    [InlineData("accounting-outbox", 1)][InlineData("domain-outbox", 1)]
    [InlineData("payment-state", 1)]
    public async Task CanonicalReceipt_ActualPostgres_ValidatesExactPostingOrFailure_ThenRollsBackEverything(string stage, int count)
    {
        // Factory must resolve normal Development configuration from Infrastructure.
        // The test module normally selects Test. Resolve the unchanged Development
        // factory explicitly, restoring the test process setting immediately.
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var cs = new NpgsqlConnectionStringBuilder(configured.Database.GetConnectionString());
        Assert.Equal("192.168.1.5", cs.Host); Assert.Equal(5432, cs.Port);
        Assert.Equal("PosEcommerceDb_Baseline", cs.Database); Assert.True(string.IsNullOrEmpty(cs.SearchPath));
        await using var observer = new NpgsqlConnection(cs.ConnectionString); await observer.OpenAsync();
        var before = await Fingerprints(observer);
        var fixture = new RollbackFixture(stage, count, cs.ConnectionString);
        var evidenceRoot = Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")
            ?? throw new InvalidOperationException("Private evidence directory required before fixture admission.");
        var manifest = Path.Combine(evidenceRoot, $"rollback-{stage}-{count}-{fixture.Tenant:N}.json");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new { fixture.Tenant, fixture.Actor, fixture.Configuration,
            fixture.Orders, fixture.Shipments, fixture.Operation, Stage = stage, Committed = false }));
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs.ConnectionString)
            .AddInterceptors(fixture, new FailWrite(stage), new PaymentStatusSaveChangesInterceptor(NullLogger<PaymentStatusSaveChangesInterceptor>.Instance)).Options;
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(fixture.Tenant)))
        {
            var lifecycle = Lifecycle(db);
            var service = new CarrierRemittanceService(lifecycle, new TestTenantContext(fixture.Tenant));
            var request = new CarrierRemittanceRequest(fixture.Operation, fixture.Orders,
                new DateTimeOffset(DateTime.UtcNow.AddMinutes(-1).Ticks / 10 * 10, TimeSpan.Zero), $"SYNTHETIC-{fixture.Operation:N}", true);
            var failure = stage == "manual-positive"
                ? await Record.ExceptionAsync(() => lifecycle.RecordManualCodSettlementAsync(fixture.Tenant, fixture.Actor, new(fixture.Operation, fixture.Orders)))
                : await Record.ExceptionAsync(() => service.ReceiveAsync(fixture.Tenant, fixture.Actor, request));
            Assert.NotNull(failure);
            while (failure.InnerException != null) failure = failure.InnerException;
            Assert.IsType<InvalidOperationException>(failure);
            Assert.Equal(stage is "positive" or "manual-positive" ? "QUALIFICATION_ROLLBACK_ONLY" : $"INJECTED_{stage}", failure.Message);
            Assert.False(PaymentStatusSaveChangesInterceptor.IsAuthorizedContext);
        }
        Assert.Equal(stage is "positive" or "manual-positive", fixture.CheckedFinalConstraints);
        // Independently enumerate every tenant-owned public table, not just known fixture tables.
        var tables = new List<string>();
        await using (var columns = new NpgsqlCommand("SELECT table_name FROM information_schema.columns WHERE table_schema='public' AND column_name='TenantId' ORDER BY table_name", observer))
        await using (var r = await columns.ExecuteReaderAsync()) while (await r.ReadAsync()) tables.Add(r.GetString(0));
        foreach (var table in tables)
        {
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM public.\"{table.Replace("\"", "\"\"")}\" WHERE \"TenantId\"=@tenant", observer);
            cmd.Parameters.AddWithValue("tenant", fixture.Tenant);
            Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
        }
        await using (var tenant = new NpgsqlCommand("SELECT count(*) FROM \"Tenants\" WHERE \"Id\"=@tenant", observer))
        { tenant.Parameters.AddWithValue("tenant", fixture.Tenant); Assert.Equal(0L, (long)(await tenant.ExecuteScalarAsync())!); }
        Assert.Equal(before, await Fingerprints(observer));
        output.WriteLine($"Stage={stage}; count={count}; synthetic tenant={fixture.Tenant}; final constraints={fixture.CheckedFinalConstraints}; rollback/independent zero-row cleanup verified; protected fingerprints identical.");
    }

    internal static PaymentLifecycleService Lifecycle(ApplicationDbContext db)
    {
        var correlation = Mock.Of<ICorrelationIdAccessor>(c => c.GetCorrelationId() == "shipping-rollback-only");
        var accounting = new CommerceAccountingService(new FinancialPostingService(db, NullLogger<FinancialPostingService>.Instance),
            new AccountResolver(db, NullLogger<AccountResolver>.Instance), Mock.Of<ICashboxLedgerAccountService>(), NullLogger<CommerceAccountingService>.Instance, db);
        var events = new Mock<IEventPublisher>(MockBehavior.Strict);
        events.Setup(e => e.PublishAsync(It.IsAny<PointOfSale.Core.Events.OrderPaymentStatusChangedEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        // This publisher is inert; no bus or hosted dispatcher runs in the fixture.
        return new(db, new AccountingOutboxService(db, correlation, NullLogger<AccountingOutboxService>.Instance), accounting,
            events.Object, new Mock<IMarketplaceEventOutboxService>(MockBehavior.Strict).Object,
            correlation, new Mock<IProductVariantStockRepository>(MockBehavior.Strict).Object, Mock.Of<IInventoryReservationMetricsService>(),
            Mock.Of<ICouponValidationService>(), NullLogger<PaymentLifecycleService>.Instance, Options.Create(new PaymentLifecycleSettings()));
    }

    private static async Task<string[]> Fingerprints(NpgsqlConnection connection)
    {
        // Existing records only, under READ ONLY. No customer/credential values leave the database.
        var result = new List<string>();
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, tx)) await ro.ExecuteNonQueryAsync();
        foreach (var table in new[] { "Orders", "Shipments", "LedgerAccounts", "LedgerJournalEntries", "LedgerLines", "TreasuryAccounts",
            "Cashboxes", "PaymentSettlementBatches", "PaymentSettlementOrders", "CarrierRemittances", "CarrierRemittanceOrders",
            "CarrierCollectionEvidence", "CarrierCodShipmentBindings", "AccountingOutboxMessages", "OutboxMessages", "OrderRefunds", "PaymentIntents", "__EFMigrationsHistory" })
        {
            await using var exists = new NpgsqlCommand("SELECT to_regclass(@table) IS NOT NULL", connection, tx);
            exists.Parameters.AddWithValue("table", $"public.\"{table}\"");
            if (!(bool)(await exists.ExecuteScalarAsync())!) continue;
            await using var cmd = new NpgsqlCommand($"SELECT count(*)::text || ':' || md5(coalesce(string_agg(h,'' ORDER BY h),'')) FROM (SELECT md5(xmin::text || ':' || to_jsonb(t)::text) h FROM public.\"{table}\" t) s", connection, tx);
            result.Add(table + ":" + (string)(await cmd.ExecuteScalarAsync())!);
        }
        await tx.RollbackAsync(); return result.ToArray();
    }

    private sealed class RollbackFixture(string stage, int count, string connection) : DbTransactionInterceptor
    {
        public Guid Tenant { get; } = Guid.NewGuid();
        public Guid Actor { get; } = Guid.NewGuid();
        public Guid Configuration { get; } = Guid.NewGuid();
        public Guid Operation { get; } = Guid.NewGuid();
        public Guid[] Orders { get; } = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        public Guid[] Shipments { get; } = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        public bool CheckedFinalConstraints { get; private set; }

        public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection c, TransactionEndEventData data, DbTransaction tx, CancellationToken ct = default)
        {
            Assert.Equal(IsolationLevel.Serializable, tx.IsolationLevel);
            // Share the actual transaction, never open/commit a separate seed unit.
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(c).Options;
            await using var seed = new ApplicationDbContext(options, new TestTenantContext(Tenant));
            await seed.Database.UseTransactionAsync(tx, ct);
            seed.Tenants.Add(new Tenant { Id = Tenant, Name = "SYNTHETIC ROLLBACK ONLY", Slug = $"rollback-{Tenant:N}", Currency = "EGP", IsActive = true, Status = TenantStatus.Active });
            seed.Users.Add(new User { Id = Actor, TenantId = Tenant, Username = $"rollback-{Actor:N}", Email = $"{Actor:N}@example.invalid", Role = UserRole.Owner, IsActive = true, PasswordHash = "not-a-login" });
            var config = new CarrierConfiguration { Id = Configuration, TenantId = Tenant, CarrierCode = "bosta", DisplayName = "SYNTHETIC ROLLBACK ONLY",
                AccountId = "synthetic-account", ApiKey = "not-a-provider-key", UseSandbox = true, IsEnabled = true, SupportsCod = true, SupportsWebhooks = true, Configuration = "{\"codCollectionCurrency\":\"EGP\"}" };
            seed.CarrierConfigurations.Add(config); await seed.SaveChangesAsync(ct);
            await new TenantAccountSeeder(seed, NullLogger<TenantAccountSeeder>.Instance).SeedAsync(Tenant, ct);
            var accounts = await seed.LedgerAccounts.ToDictionaryAsync(a => a.AccountCode, ct);
            using var ledger = LedgerMutationScope.Authorize(); using var money = CarrierMoneyPersistenceAuthority.Begin();
            var now = new DateTime(DateTime.UtcNow.AddMinutes(-3).Ticks / 10 * 10, DateTimeKind.Utc);
            for (var i = 0; i < count; i++)
            {
                var journal = JournalEntry.Create(Tenant, $"SYN-{Orders[i]:N}"[..24], now, "Order_COD", Orders[i], $"SYN-{Orders[i]:N}",
                    "Synthetic collection prerequisite (rollback only)", $"COD_DELIVERED:{Tenant}:{Orders[i]}", Actor);
                seed.JournalEntries.Add(journal);
                accounts["1130"].ApplyEntry(100, 0); accounts["4020"].ApplyEntry(0, 100);
                seed.LedgerEntries.Add(LedgerEntry.CreateDebit(Tenant, journal.Id, accounts["1130"].Id, 100, accounts["1130"].CurrentBalance, 1));
                seed.LedgerEntries.Add(LedgerEntry.CreateCredit(Tenant, journal.Id, accounts["4020"].Id, 100, accounts["4020"].CurrentBalance, 2));
                seed.Orders.Add(new Order { Id = Orders[i], TenantId = Tenant, OrderNumber = $"SYN-{Orders[i]:N}", Total = 100, Currency = "EGP", PaymentMethodCode = "COD", PaymentGateway = "cod",
                    PaymentStatus = PaymentStatus.Collected, IsPaid = true, PaidAt = now, FulfillmentMode = stage == "manual-positive" ? FulfillmentMode.Manual : FulfillmentMode.Carrier,
                    FulfillmentCarrierCode = stage == "manual-positive" ? null : "bosta", FulfillmentStatus = FulfillmentStatus.Delivered,
                    Status = OrderStatus.Confirmed, FinancialPostingStatus = FinancialPostingStatus.Posted, FinancialPostingJournalId = journal.Id, FinancialPostingPendingAtUtc = now, FinancialPostedAtUtc = now });
                var shipment = new Shipment { Id = Shipments[i], TenantId = Tenant, OrderId = Orders[i], ShipmentNumber = $"SYN-{Shipments[i]:N}", CarrierCode = stage == "manual-positive" ? "manual" : "bosta", Currency = "EGP",
                    IsCashOnDelivery = true, CodAmount = 100, Status = ShipmentStatus.Delivered, TrackingNumber = Orders[i].ToString("N"), ExternalReferenceId = stage == "manual-positive" ? null : Shipments[i].ToString("N"),
                    ExternalRegistrationState = stage == "manual-positive" ? null : "Completed", ExternalRegistrationCompletedAtUtc = stage == "manual-positive" ? null : now };
                seed.Shipments.Add(shipment);
                if (stage != "manual-positive")
                {
                var binding = new CarrierCodShipmentBinding { Id = Guid.NewGuid(), TenantId = Tenant, OrderId = Orders[i], ShipmentId = shipment.Id, ConfigurationId = Configuration,
                    CarrierCode = "bosta", AccountId = "synthetic-account", Environment = "Sandbox", Currency = "EGP", TrackingNumber = shipment.TrackingNumber,
                    DeliveryReference = shipment.ExternalReferenceId, ExternalReferenceId = shipment.ExternalReferenceId,
                    Fingerprint = CarrierCollectionMoneyAuthority.BindingFingerprint(config, shipment, "synthetic-account", "EGP", shipment.ExternalReferenceId), CreatedAt = now };
                seed.CarrierCodShipmentBindings.Add(binding);
                seed.CarrierCollectionEvidence.Add(new CarrierCollectionEvidence { Id = Guid.NewGuid(), TenantId = Tenant, OrderId = Orders[i], ShipmentId = shipment.Id,
                    ConfigurationId = Configuration, ShipmentBindingId = binding.Id, CarrierCode = "bosta", AccountId = binding.AccountId, Environment = "Sandbox", Currency = "EGP", TrackingNumber = shipment.TrackingNumber,
                    DeliveryReference = shipment.ExternalReferenceId, ObservedAmount = 100, ProviderEventAtUtc = now, ReceivedAtUtc = now,
                    SourceContract = "bosta:delivery-status-webhook:v1", TrustMethod = "ConfiguredWebhookSecret", PayloadHash = new string('B', 64),
                    ObservationFingerprint = CarrierCollectionMoneyAuthority.Hash(Orders[i].ToString()), AdmissionOutcome = "Qualified", AdmissionCode = "COD_AMOUNT_QUALIFIED", AuditIdentity = "synthetic-rollback-fixture" });
                }
                var receipt = AccountingOutboxMessage.Create(Tenant, "Order_COD", Orders[i], nameof(Order), "{}", journal.IdempotencyKey, "synthetic-rollback-fixture", sourceType: "Order_COD");
                receipt.MarkProcessed(journal.Id); seed.AccountingOutboxMessages.Add(receipt);
            }
            await seed.SaveChangesAsync(ct);
            return tx;
        }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction tx, TransactionEventData data, InterceptionResult result, CancellationToken ct = default)
        {
            Assert.True(stage is "positive" or "manual-positive");
            await using var check = tx.Connection!.CreateCommand(); check.Transaction = tx;
            check.CommandText = "SET CONSTRAINTS ALL IMMEDIATE"; await check.ExecuteNonQueryAsync(ct);
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(tx.Connection!).Options;
            await using var read = new ApplicationDbContext(options, new TestTenantContext(Tenant)); await read.Database.UseTransactionAsync(tx, ct);
            if (stage == "manual-positive")
            {
                var batch = await read.PaymentSettlementBatches.Include(b => b.Orders).SingleAsync(ct);
                Assert.Equal("MANUAL_COD_COLLECTION", batch.SettlementType); Assert.Equal(count, batch.Orders.Count);
                Assert.Equal(count * 100m, batch.GrossAmount); Assert.Equal(0m, batch.Fees);
                var manualResult = await Lifecycle(read).GetManualCodSettlementAsync(Tenant, Operation, ct);
                Assert.NotNull(manualResult); Assert.Equal("Posted", manualResult.AccountingOutcome);
                var manualAccounts = await read.LedgerAccounts.ToDictionaryAsync(a => a.AccountCode, ct);
                Assert.Equal(count * 100m, manualAccounts["1010"].CurrentBalance); Assert.Equal(0m, manualAccounts["1130"].CurrentBalance);
                Assert.All(await read.Orders.ToListAsync(ct), o => Assert.Equal(PaymentStatus.Settled, o.PaymentStatus));
                Assert.Equal(1, await read.JournalEntries.CountAsync(j => j.SourceType == "ManualCodSettlement", ct));
                CheckedFinalConstraints = true;
                throw new InvalidOperationException("QUALIFICATION_ROLLBACK_ONLY");
            }
            var operation = await read.CarrierRemittances.Include(r => r.Orders).SingleAsync(ct);
            Assert.Equal("Posted", operation.Outcome); Assert.Equal(Operation, operation.Id); Assert.Equal(count * 100m, operation.GrossAmount); Assert.Equal(0m, operation.Fees);
            Assert.Equal(count, operation.Orders.Count); Assert.Equal(count, await read.PaymentSettlementOrders.CountAsync(ct));
            Assert.All(await read.Orders.ToListAsync(ct), o => Assert.Equal(PaymentStatus.Settled, o.PaymentStatus));
            var accounts = await read.LedgerAccounts.ToDictionaryAsync(a => a.AccountCode, ct);
            Assert.Equal(count * 100m, accounts["1010"].CurrentBalance); Assert.Equal(0m, accounts["1130"].CurrentBalance);
            var journal = await read.JournalEntries.Include(j => j.LedgerEntries).SingleAsync(j => j.SourceType == CarrierRemittanceAuthority.SourceType, ct);
            Assert.Equal(2, journal.LedgerEntries.Count); Assert.Equal(Actor, journal.CreatedByUserId);
            Assert.Equal(count * 100m, Assert.Single(journal.LedgerEntries, l => l.AccountId == accounts["1010"].Id).DebitAmount);
            Assert.Equal(count * 100m, Assert.Single(journal.LedgerEntries, l => l.AccountId == accounts["1130"].Id).CreditAmount);
            Assert.Equal(count + 1, await read.OutboxMessages.CountAsync(m => m.TenantId == Tenant, ct));
            var receipt = await read.AccountingOutboxMessages.SingleAsync(m => m.TenantId == Tenant && m.SourceType == CarrierRemittanceAuthority.SourceType, ct);
            Assert.Equal(journal.Id, (await CarrierRemittanceAuthority.RecoverReceiptAsync(read, receipt, ct)).JournalId);
            Assert.Equal(journal.Id, (await Lifecycle(read).GetCarrierRemittanceAsync(Tenant, Actor, Operation, ct))!.JournalId);
            CheckedFinalConstraints = true;
            throw new InvalidOperationException("QUALIFICATION_ROLLBACK_ONLY");
        }
    }

    private sealed class FailWrite(string stage) : DbCommandInterceptor
    {
        private void Check(DbCommand command)
        {
            var target = stage switch { "journal" => "INSERT INTO \"LedgerJournalEntries\"", "balance" => "UPDATE \"LedgerAccounts\"",
                "accounting-outbox" => "INSERT INTO \"AccountingOutboxMessages\"", "domain-outbox" => "INSERT INTO \"OutboxMessages\"", "payment-state" => "UPDATE \"Orders\"", _ => "never" };
            // Seeding uses a different context without this interceptor; only production writes fail.
            if (command.CommandText.Contains(target, StringComparison.Ordinal)) throw new InvalidOperationException($"INJECTED_{stage}");
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData d, InterceptionResult<DbDataReader> r, CancellationToken ct = default)
        { Check(c); return ValueTask.FromResult(r); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData d, InterceptionResult<int> r, CancellationToken ct = default)
        { Check(c); return ValueTask.FromResult(r); }
    }
}

public sealed class ConfiguredRemittanceRollbackTheoryAttribute : TheoryAttribute
{
    public ConfiguredRemittanceRollbackTheoryAttribute()
    { if (Environment.GetEnvironmentVariable("SHIPPING_CONFIGURED_ROLLBACK_QUALIFY") != "1") Skip = "Explicit configured rollback-only qualification authorization required; no fallback/reset/provisioning."; }
}
