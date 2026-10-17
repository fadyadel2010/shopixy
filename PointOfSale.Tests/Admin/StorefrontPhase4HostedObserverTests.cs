using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Admin;

public sealed class StorefrontPhase4HostedObserverTests
{
    [Phase4ConfiguredFact]
    public async Task OwnedHostedCleanup_IndependentSqlObserver_ReadOnly()
    {
        var tenant = Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var phones = await db.PhoneRiskProfiles.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && (x.PhoneNumber == "+201000000099" || x.PhoneNumber == "123"))
            .Select(x => new { x.Id, x.IsBlocked, x.SpamReports, x.BlockedReason }).ToListAsync();
        Assert.All(phones, x => Assert.False(x.IsBlocked));
        var templates = await db.WhatsAppMessageTemplates.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.TenantId == tenant && x.Content.StartsWith("P4QA"));
        Assert.Equal(0, templates);
        var customers = await db.OnlineCustomers.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.TenantId == tenant && x.Email.StartsWith("p4qaadmin"));
        Assert.Equal(0, customers);
        var settings = await db.Set<PointOfSale.Core.Entities.TenantAutomationSettings>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant).Select(x => new { x.EnableAbandonedRecovery, x.FirstMessageDelayMinutes, x.SecondMessageDelayMinutes, x.TimeZoneId }).SingleAsync();
        Assert.False(settings.EnableAbandonedRecovery);
        var account = await db.WhatsAppAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == Guid.Parse("138379f5-7754-4c20-8863-b3b7aad28888"))
            .Select(x => new { x.Id, x.Enabled, x.Status, x.StatusReason }).SingleAsync();
        var coupons = await db.Coupons.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && x.Code.StartsWith("P3QA"))
            .Select(x => new { x.IsDeleted, x.CurrentUsage }).ToListAsync();
        Assert.Equal(21, coupons.Count);
        Assert.All(coupons, x => { Assert.True(x.IsDeleted); Assert.Equal(0, x.CurrentUsage); });
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("STOREFRONT_PHASE4_EVIDENCE")!, "hosted-cleanup-independent-sql.json"), JsonSerializer.Serialize(new {
            observedUtc = DateTime.UtcNow, readOnly = true, phones, syntheticTemplateRows = templates, blockedRegistrationAccountRows = customers, settings,
            account, accountRestoration = "NOT restored: provider unavailable; stored disabled gate is not connection verification",
            priorCoupons = new { retained = coupons.Count, active = coupons.Count(x => !x.IsDeleted), usage = coupons.Sum(x => x.CurrentUsage) }
        }, new JsonSerializerOptions { WriteIndented = true }));
        await tx.RollbackAsync();
    }

    [Phase4ConfiguredFact]
    public async Task OwnedHostedLifecycle_IndependentSqlMongoFinancialAndMessageObserver_ReadOnly()
    {
        var tenant = Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
        const string prefix = "P4QA1792177832429";
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var orders = await db.Orders.IgnoreQueryFilters().AsNoTracking().Include(x => x.Items)
            .Where(x => x.TenantId == tenant && x.CustomerNotes != null && x.CustomerNotes.StartsWith(prefix)).OrderBy(x => x.CreatedAt).ToListAsync();
        Assert.NotEmpty(orders);
        var ids = orders.Select(x => x.Id).ToArray();
        var reservations = await db.InventoryReservations.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && ids.Contains(x.OrderId))
            .Select(x => new { x.OrderId, x.OrderItemId, x.ProductId, x.VariantId, x.WarehouseId, x.Status, x.ReservedQuantity, x.ReleasedQuantity, x.CommittedQuantity }).ToListAsync();
        var journals = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking().Include(x => x.LedgerEntries).Where(x => x.TenantId == tenant && ids.Contains(x.SourceId)).ToListAsync();
        Assert.All(journals, j => Assert.True(j.IsBalanced));
        var outbox = await db.AccountingOutboxMessages.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && ids.Contains(x.BusinessEntityId))
            .Select(x => new { x.BusinessEntityId, x.ProcessedAtUtc, x.JournalId, x.IdempotencyKey }).ToListAsync();
        var originalSession = Guid.Parse("1efa806b-9dd3-4118-a40e-df51b2e6f025");
        var sessions = await db.CheckoutSessions.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && (x.Id == originalSession || (x.ConvertedOrderId.HasValue && ids.Contains(x.ConvertedOrderId.Value))))
            .Select(x => new { x.Id, x.CartId, x.OnlineCustomerId, x.Status, x.ConvertedOrderId, x.CartTotal, x.Currency, x.InvalidatedAtUtc }).ToListAsync();
        var events = await db.Set<PointOfSale.Core.Entities.OutboxMessage>().IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && x.AggregateId.HasValue && ids.Contains(x.AggregateId.Value))
            .Select(x => new { x.EventType, x.AggregateId, x.EventVersion, x.ProcessedAtUtc, x.RetryCount, x.LastErrorMessage }).ToListAsync();
        var shipments = await db.Shipments.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && ids.Contains(x.OrderId))
            .Select(x => new { x.Id, x.OrderId, x.Status, x.IsCarrierManaged }).ToListAsync();
        var movements = await db.StockMovements.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && x.ReferenceId.HasValue && ids.Contains(x.ReferenceId.Value))
            .Select(x => new { x.ReferenceId, x.InventoryEventType, x.QuantityChange, x.ReservedQuantityChange, x.QuantityAfter, x.ReservedAfter, x.AvailableAfter }).ToListAsync();
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(TestConfiguration.GetSolutionRoot(), "PointOfSale.API"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true).AddJsonFile("appsettings.Development.local.json", true).AddEnvironmentVariables().Build();
        var mongo = new MongoClient(config["MongoDB:ConnectionString"]!).GetDatabase(config["MongoDB:DatabaseName"] ?? "ecommerce_read_db");
        var stringIds = ids.Select(id => id.ToString()).ToArray();
        var logs = await mongo.GetCollection<WhatsAppMessageLogDocument>("WhatsAppMessageLogs").Find(x => x.TenantId == tenant.ToString() && stringIds.Contains(x.RelatedEntityId)).CountDocumentsAsync();
        Assert.Equal(0, logs);
        var projections = new List<object>();
        foreach (var id in ids) projections.AddRange(await mongo.GetCollection<OrderDocument>("orders").Find(x => x.TenantId == tenant && x.OrderId == id)
            .Project(x => new { x.OrderId, x.Status, x.PaymentStatus, x.Total, x.Currency, x.OnlineCustomerId }).ToListAsync());
        var final = Environment.GetEnvironmentVariable("STOREFRONT_PHASE4_STAGE") == "final";
        if (final) {
            Assert.Equal(2, orders.Count);
            var cancelled = Assert.Single(orders, x => x.Status == OrderStatus.Cancelled);
            var completed = Assert.Single(orders, x => x.Status == OrderStatus.Completed);
            Assert.Equal(PaymentStatus.Collected, completed.PaymentStatus);
            Assert.Equal(FinancialPostingStatus.Posted, completed.FinancialPostingStatus);
            Assert.Equal(0, journals.Count(x => x.SourceId == cancelled.Id));
            var revenue = Assert.Single(journals, x => x.SourceId == completed.Id && x.SourceType == "Order_COD");
            Assert.Equal(completed.Total, revenue.TotalDebits);
            Assert.Equal($"COD_DELIVERED:{tenant}:{completed.Id}", revenue.IdempotencyKey);
            Assert.Single(journals, x => x.SourceId == completed.Id && x.SourceType == PointOfSale.Core.Models.Accounting.EcommerceCogsPolicy.StorefrontCogsSourceType);
            Assert.Equal(2, journals.Count(x => x.SourceId == completed.Id));
            Assert.All(journals, x => Assert.Equal(JournalStatus.Posted, x.Status));
            Assert.All(outbox, x => Assert.NotNull(x.ProcessedAtUtc));
            Assert.Equal(2, outbox.Count);
            var committed = Assert.Single(reservations, x => x.OrderId == completed.Id);
            Assert.Equal(1m, committed.CommittedQuantity);
            Assert.Single(movements, x => x.ReferenceId == completed.Id && x.InventoryEventType == InventoryEventType.StockDecreased && x.QuantityChange == -1m);
            Assert.All(reservations.Where(x => x.OrderId == cancelled.Id), x => { Assert.Equal(x.ReservedQuantity, x.ReleasedQuantity); Assert.Equal(0m, x.CommittedQuantity); });
            Assert.All(reservations.Where(x => x.OrderId == completed.Id), x => { Assert.Equal(x.ReservedQuantity, x.CommittedQuantity); Assert.Equal(0m, x.ReleasedQuantity); });
            Assert.All(sessions, x => Assert.Equal(CheckoutSessionStatus.ConvertedToOrder, x.Status));
            Assert.Equal(2, sessions.Count);
            Assert.Single(shipments, x => x.OrderId == completed.Id && !x.IsCarrierManaged);
        }
        var evidence = new { observedUtc = DateTime.UtcNow, readOnly = true, prefix,
            orders = orders.Select(x => new { x.Id, x.OrderNumber, status = x.Status.ToString(), paymentStatus = x.PaymentStatus.ToString(), financialPostingStatus = x.FinancialPostingStatus.ToString(), fulfillmentMode = x.FulfillmentMode.ToString(), fulfillmentStatus = x.FulfillmentStatus.ToString(), x.Total, x.Currency, x.OnlineCustomerId, items = x.Items.Select(i => new { i.Id, i.ProductId, i.VariantId, i.Quantity, i.UnitPrice, i.Total }) }),
            reservations, journals = journals.Select(j => new { j.Id, j.SourceId, j.SourceType, j.IdempotencyKey, status = j.Status.ToString(), j.TotalDebits, j.TotalCredits, j.IsBalanced,
                lines = j.LedgerEntries.Select(l => new { l.LineNumber, l.AccountId, l.DebitAmount, l.CreditAmount }) }), outbox, sessions, projections, events, shipments, movements, outboundMessageLogs = logs };
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("STOREFRONT_PHASE4_EVIDENCE")!, "hosted-orders-observer-" + (final ? "final" : "progress") + ".json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        await tx.RollbackAsync();
    }
}
