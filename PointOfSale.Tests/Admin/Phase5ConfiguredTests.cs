using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Admin;

public sealed class Phase5ConfiguredTests
{
    private static readonly Guid Store = Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
    private static string Evidence => Environment.GetEnvironmentVariable("PHASE5_EVIDENCE")!;
    private static Task Write(string name, object data) => File.WriteAllTextAsync(Path.Combine(Evidence, name), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));

    [Phase5ConfiguredFact]
    public async Task ConfiguredMethodConcurrency_RejectsStaleWritesAndReviewedVersion_Rollback()
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var tenant = Guid.NewGuid(); var payment = Guid.NewGuid(); var shipping = Guid.NewGuid();
        await using (var first = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await first.Database.BeginTransactionAsync()) {
            first.Tenants.Add(new Tenant { Id = tenant, Name = "P5QA rollback", Slug = "p5-" + tenant.ToString("N") });
            first.TenantPaymentMethods.Add(new TenantPaymentMethod { Id = payment, TenantId = tenant, Code = "COD", DisplayName = "P5QA", SupportsOffline = true });
            first.ShippingMethods.Add(new ShippingMethod { Id = shipping, TenantId = tenant, Name = "P5QA", IsActive = false });
            await first.SaveChangesAsync();
            await using var second = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(tx.GetDbTransaction().Connection!).Options, new TestTenantContext(tenant));
            await second.Database.UseTransactionAsync(tx.GetDbTransaction());
            var stalePayment = await second.TenantPaymentMethods.SingleAsync();
            var staleShipping = await second.ShippingMethods.SingleAsync();
            var currentPayment = await first.TenantPaymentMethods.SingleAsync(); currentPayment.Version++; currentPayment.DisplayName = "P5QA winner";
            var currentShipping = await first.ShippingMethods.SingleAsync(); currentShipping.UpdatedAt = DateTime.UtcNow; currentShipping.Name = "P5QA winner";
            await first.SaveChangesAsync();
            stalePayment.Version++; stalePayment.DisplayName = "stale loser";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
            second.ChangeTracker.Clear();
            second.Attach(staleShipping); staleShipping.Name = "stale loser";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
            using var telemetry = new CommerceReliabilityTelemetry();
            var events = new Mock<IEventPublisher>();
            var controller = new PaymentMethodsAdminController(first, events.Object, new EphemeralDataProtectionProvider(), telemetry, NullLogger<PaymentMethodsAdminController>.Instance, new TestTenantContext(tenant))
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            controller.Request.Headers.IfMatch = "1";
            Assert.IsType<ConflictObjectResult>(await controller.UpsertPaymentMethod("COD", new() { DisplayName = "stale editor" }, default));
            events.VerifyNoOtherCalls();
            first.ChangeTracker.Clear();
            Assert.Equal("P5QA winner", (await first.TenantPaymentMethods.SingleAsync()).DisplayName);
            Assert.Equal("P5QA winner", (await first.ShippingMethods.SingleAsync()).Name);
            await first.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.Tenants.IgnoreQueryFilters().AnyAsync(x => x.Id == tenant));
        Assert.Empty(await observer.TenantPaymentMethods.IgnoreQueryFilters().Where(x => x.TenantId == tenant).ToListAsync());
        Assert.Empty(await observer.ShippingMethods.IgnoreQueryFilters().Where(x => x.TenantId == tenant).ToListAsync());
        await Write("method-concurrency-rollback.json", new { tenant, payment, shipping, paymentStaleWriteRefused = true, shippingStaleWriteRefused = true, staleReviewedVersionRefused = true, freshObserverAbsent = true });
    }

    [Phase5ConfiguredFact]
    public async Task IndependentConfiguredObserver_ConfigurationAndSyntheticBusinessEffects_ReadOnly()
    {
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var paymentMethods = await db.TenantPaymentMethods.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Store)
            .Select(x => new { x.Id, x.Code, x.DisplayName, x.IsEnabled, x.IsDeleted, x.IsSandbox, x.Version, x.Fee, x.FeeType, hasEncryptedConfiguration = x.ConfigEncrypted != null }).ToListAsync();
        var methods = await db.ShippingMethods.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Store)
            .Select(x => new { x.Id, x.Name, x.IsActive, x.IsDeleted, x.BaseRate, x.PerKgRate, x.FreeShippingThreshold, x.FulfillmentWarehouseId, x.AvailableCountries }).ToListAsync();
        var orders = await db.Orders.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Store && x.CustomerNotes != null && x.CustomerNotes.StartsWith("P5QA"))
            .Select(x => new { x.Id, x.OrderNumber, x.Status, x.PaymentStatus, x.FinancialPostingStatus, x.FulfillmentStatus, x.Total, x.ShippingCost, x.Currency, x.ShippingMethodId }).ToListAsync();
        var ids = orders.Select(x => x.Id).ToArray();
        var settlementMembers = await db.PaymentSettlementOrders.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Store && ids.Contains(x.OrderId)).ToListAsync();
        var batchIds = settlementMembers.Select(x => x.SettlementBatchId).ToArray();
        var batches = await db.PaymentSettlementBatches.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Store && batchIds.Contains(x.Id))
            .Select(x => new { x.Id, x.SettlementId, x.SettlementType, x.GrossAmount, x.Fees, x.Currency, x.CreatedBy }).ToListAsync();
        var shipments = await db.Shipments.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Store && ids.Contains(x.OrderId))
            .Select(x => new { x.Id, x.OrderId, x.Status, x.CarrierCode, x.TrackingNumber, x.ShippingCost, x.ExternalRegistrationAttemptCount, x.ExternalRegistrationState, x.CodCollected }).ToListAsync();
        var journals = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking().Include(x => x.LedgerEntries).Where(x => x.TenantId == Store && (ids.Contains(x.SourceId) || batchIds.Contains(x.SourceId))).ToListAsync();
        Assert.All(journals, j => Assert.True(j.IsBalanced));
        var outbox = await db.AccountingOutboxMessages.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Store && (ids.Contains(x.BusinessEntityId) || batchIds.Contains(x.BusinessEntityId)))
            .Select(x => new { x.BusinessEntityId, x.JournalId, x.ProcessedAtUtc, x.IdempotencyKey }).ToListAsync();
        var reservations = await db.InventoryReservations.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Store && ids.Contains(x.OrderId))
            .Select(x => new { x.OrderId, x.Status, x.ReservedQuantity, x.CommittedQuantity, x.ReleasedQuantity }).ToListAsync();
        var movements = await db.StockMovements.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Store && x.ReferenceId.HasValue && ids.Contains(x.ReferenceId.Value))
            .Select(x => new { x.ReferenceId, x.QuantityChange, x.ReservedQuantityChange, x.InventoryEventType }).ToListAsync();
        var cancellations = await db.ShipmentCancellationOperations.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Store && ids.Contains(x.OrderId))
            .Select(x => new { x.Id, x.OrderId, x.ShipmentId, x.Outcome, x.ConfirmationBasis }).ToListAsync();
        var whatsapp = await db.WhatsAppAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Store && x.Id == Guid.Parse("138379f5-7754-4c20-8863-b3b7aad28888"))
            .Select(x => new { x.Id, x.Enabled, x.Status }).SingleAsync();
        Assert.False(whatsapp.Enabled);
        var priorOrders = await db.Orders.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Store && (x.Id == Guid.Parse("ee42f429-fa3d-46b6-988a-b900cc3e8648") || x.Id == Guid.Parse("258be5dc-92d9-4489-a1c4-0872d7f8b2b0")))
            .Select(x => new { x.Id, x.Status, x.PaymentStatus, x.Total }).ToListAsync();
        var stage = Environment.GetEnvironmentVariable("PHASE5_STAGE") ?? "baseline";
        var ledger = await db.LedgerAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Store)
            .Select(x => new { x.Id, x.AccountCode, x.CurrentBalance }).ToListAsync();
        var projections = new List<object>();
        long messageLogs = 0;
        if (stage == "final") {
            Assert.Equal(2, orders.Count);
            var rejected = Assert.Single(orders, x => x.PaymentStatus == PaymentStatus.Rejected);
            Assert.Equal(OrderStatus.Cancelled, rejected.Status);
            Assert.DoesNotContain(journals, x => x.SourceId == rejected.Id);
            var settled = Assert.Single(orders, x => x.PaymentStatus == PaymentStatus.Settled);
            Assert.Equal(OrderStatus.Completed, settled.Status);
            Assert.Equal(FinancialPostingStatus.Posted, settled.FinancialPostingStatus);
            Assert.Equal(32.35m, settled.Total);
            Assert.Equal(7.35m, settled.ShippingCost);
            var batch = Assert.Single(batches);
            Assert.Equal(32.35m, batch.GrossAmount);
            Assert.Single(settlementMembers);
            Assert.Equal(2, journals.Count);
            Assert.Equal(32.35m, Assert.Single(journals, x => x.SourceType == "Order_COD").TotalDebits);
            Assert.Equal(32.35m, Assert.Single(journals, x => x.SourceType == "ManualCodSettlement").TotalDebits);
            Assert.Equal(journals.Count, journals.Select(x => x.IdempotencyKey).Distinct().Count());
            Assert.Equal(2, outbox.Count); Assert.All(outbox, x => Assert.NotNull(x.ProcessedAtUtc));
            Assert.Empty(reservations); Assert.Empty(movements);
            var shipment = Assert.Single(shipments);
            Assert.Equal(ShipmentStatus.Delivered, shipment.Status);
            Assert.Equal(0, shipment.ExternalRegistrationAttemptCount);
            Assert.Null(shipment.ExternalRegistrationState);
            Assert.False(shipment.CodCollected); // Carrier collection evidence, not internal Manual money authority.
            Assert.False(Assert.Single(paymentMethods, x => x.Code == "MANUALWALLET").IsEnabled);
            Assert.False(Assert.Single(paymentMethods, x => x.Code == "MANUALWALLET").hasEncryptedConfiguration);
            Assert.True(Assert.Single(methods, x => x.Id == settled.ShippingMethodId).IsDeleted);
            var configuration = new ConfigurationBuilder().SetBasePath(Path.Combine(TestConfiguration.GetSolutionRoot(), "PointOfSale.API"))
                .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true).AddJsonFile("appsettings.Development.local.json", true).AddEnvironmentVariables().Build();
            var mongo = new MongoClient(configuration["MongoDB:ConnectionString"]!).GetDatabase(configuration["MongoDB:DatabaseName"] ?? "ecommerce_read_db");
            foreach (var order in orders) {
                var projection = await mongo.GetCollection<OrderDocument>("orders").Find(x => x.TenantId == Store && x.OrderId == order.Id).SingleAsync();
                Assert.Equal(order.Total, projection.Total);
                Assert.Equal(order.PaymentStatus.ToString(), projection.PaymentStatus);
                Assert.Equal(order.Status.ToString(), projection.Status);
                projections.Add(new { projection.OrderId, projection.Status, projection.PaymentStatus, projection.ShippingStatus, projection.Total, projection.Currency });
            }
            var stringIds = ids.Select(x => x.ToString()).ToArray();
            messageLogs = await mongo.GetCollection<WhatsAppMessageLogDocument>("WhatsAppMessageLogs").Find(x => x.TenantId == Store.ToString() && stringIds.Contains(x.RelatedEntityId)).CountDocumentsAsync();
            Assert.Equal(0, messageLogs);
            using var baseline = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Evidence, "configured-observer-before-cod.json")));
            foreach (var account in ledger) {
                var old = baseline.RootElement.GetProperty("ledger").EnumerateArray().Single(x => x.GetProperty("Id").GetGuid() == account.Id).GetProperty("CurrentBalance").GetDecimal();
                Assert.Equal(account.AccountCode is "1010" or "4020" ? 32.35m : 0m, account.CurrentBalance - old);
            }
        }
        await Write("configured-observer-" + stage + ".json", new { observedUtc = DateTime.UtcNow, readOnly = true, paymentMethods, methods, orders, shipments, reservations, movements, cancellations, whatsapp, priorOrders,
            ledger, batches, projections, outboundMessageLogs = messageLogs, settlementMembers = settlementMembers.Select(x => new { x.OrderId, x.SettlementBatchId, x.OrderTotal, x.Currency }),
            journals = journals.Select(j => new { j.Id, j.SourceId, j.SourceType, j.IdempotencyKey, j.TotalDebits, j.TotalCredits, j.IsBalanced, lines = j.LedgerEntries.Select(l => new { l.AccountId, l.DebitAmount, l.CreditAmount }) }), outbox });
        await tx.RollbackAsync();
    }
}

public sealed class Phase5ConfiguredFactAttribute : FactAttribute
{
    public Phase5ConfiguredFactAttribute() { if (Environment.GetEnvironmentVariable("PHASE5_CONFIGURED") != "1") Skip = "Explicit existing configured QA database opt-in required; no provisioning fallback."; }
}
