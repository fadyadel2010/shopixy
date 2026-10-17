using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Tests.Admin;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Integration;

public sealed class Phase5ConfiguredEvidenceTests
{
    [ConfiguredAdminIntegrationFact]
    public async Task OwnedCheckout_OrderPaymentStockAndNoFinancialPosting_ReadOnly()
    {
        var tenant = Guid.Parse(Environment.GetEnvironmentVariable("POS_ADMIN_OBSERVE_TENANT")!);
        var customerId = Guid.Parse(Environment.GetEnvironmentVariable("PHASE5_CUSTOMER_ID")!);
        var orderId = Guid.Parse(Environment.GetEnvironmentVariable("PHASE5_ORDER_ID")!);
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var order = await db.Orders.IgnoreQueryFilters().AsNoTracking().Include(o => o.Items)
            .SingleAsync(o => o.TenantId == tenant && o.OnlineCustomerId == customerId && o.Id == orderId);
        var physical = PointOfSale.Core.Policies.OrderInventoryReservationSnapshot.PhysicalItems(order);
        var reservations = await db.InventoryReservations.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.OrderId == orderId && r.TenantId == tenant)
            .Select(r => new {r.OrderItemId, r.ProductId, r.VariantId, r.WarehouseId, r.ReservedQuantity,
                r.CommittedQuantity, r.ReleasedQuantity, r.ExpiresAtUtc, r.Status}).ToListAsync();
        var intents = await db.PaymentIntents.IgnoreQueryFilters().AsNoTracking().Where(i => i.OrderId == orderId && i.TenantId == tenant)
            .Select(i => new {i.Id, i.Status, i.Amount, i.Currency, i.ExpiresAtUtc}).ToListAsync();
        var customer = await db.OnlineCustomers.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == customerId && c.TenantId == tenant);
        var journals = await db.JournalEntries.IgnoreQueryFilters().CountAsync(j => j.TenantId == tenant && j.SourceId == orderId);
        var accountingMessages = await db.AccountingOutboxMessages.IgnoreQueryFilters()
            .CountAsync(m => m.TenantId == tenant && m.BusinessEntityId == orderId);
        var shipments = await db.Shipments.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenant && s.OrderId == orderId);
        var configuration = new ConfigurationBuilder().SetBasePath(Path.Combine(TestConfiguration.GetSolutionRoot(), "PointOfSale.API"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true)
            .AddJsonFile("appsettings.Development.local.json", true).AddEnvironmentVariables().Build();
        var mongo = new MongoClient(configuration["MongoDB:ConnectionString"]!)
            .GetDatabase(configuration["MongoDB:DatabaseName"] ?? "ecommerce_read_db");
        var documents = await mongo.GetCollection<OrderDocument>("orders")
            .Find(o => o.OrderId == orderId && o.TenantId == tenant).ToListAsync();
        var messageLogs = await mongo.GetCollection<WhatsAppMessageLogDocument>("WhatsAppMessageLogs")
            .Find(l => l.TenantId == tenant.ToString() && l.RelatedEntityId == orderId.ToString()
                && l.MessageType == "ORDER_CONFIRMATION")
            .Project(l => new {l.Id, l.MessageType, l.Status, l.SendAttempts, l.SentAtUtc,
                ProviderMessageIdPresent = l.ProviderMessageId != null, l.IsRetryable}).ToListAsync();
        Assert.Equal("COD", order.PaymentMethodCode);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        Assert.Equal(40m, order.Total);
        Assert.Equal("EGP", order.Currency);
        Assert.True(customer.EmailVerified);
        // Checkout OTP proves a specific normalized phone for a limited Redis
        // session/customer scope. It does not promote the SQL profile phone flag.
        // The hosted verification-status evidence checks that separate authority.
        Assert.Single(order.Items);
        Assert.Equal(Guid.Parse("1435b729-5fbe-48f1-a306-2e12051ad24b"), order.Items.Single().VariantId);
        Assert.Equal(physical.Sum(i => i.Quantity), reservations.Sum(r => r.ReservedQuantity - r.CommittedQuantity - r.ReleasedQuantity));
        Assert.All(reservations, r => Assert.Contains(physical, i => i.Id == r.OrderItemId && i.VariantId == r.VariantId && i.WarehouseId == r.WarehouseId));
        Assert.Equal(0, journals);
        Assert.Equal(0, accountingMessages);
        Assert.Equal(0, shipments);
        var document = Assert.Single(documents);
        Assert.Equal(customerId, document.OnlineCustomerId);
        Assert.Equal(order.Status.ToString(), document.Status);
        Assert.Equal(order.PaymentStatus.ToString(), document.PaymentStatus);
        Assert.Equal(order.Total, document.Total);
        Assert.Equal(order.Currency, document.Currency);
        Assert.Equal(order.Items.Single().VariantId, Assert.Single(document.Items).VariantId);
        Assert.Equal("SENT", Assert.Single(messageLogs).Status);
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!,
            "owned-checkout-invariants.json"), JsonSerializer.Serialize(new {ObservedUtc=DateTime.UtcNow,ReadOnly=true,
            order.Id,order.OrderNumber,order.TenantId,order.OnlineCustomerId,order.Status,order.PaymentStatus,order.Total,order.Currency,
            PhysicalLineCount=physical.Count, reservations,intents,OrderJournalCount=journals,
            AccountingOutboxCount=accountingMessages,ShipmentCount=shipments,MongoMatched=true,
            OrderConfirmationLogs=messageLogs,
            customer.EmailVerified,customer.PhoneVerified},new JsonSerializerOptions {WriteIndented=true}));
        await tx.RollbackAsync();
    }

    [ConfiguredAdminIntegrationFact]
    public async Task ExistingTenant_CancellationsAndOwnedTestIdentity_ReadOnly()
    {
        var tenant = Guid.Parse(Environment.GetEnvironmentVariable("POS_ADMIN_OBSERVE_TENANT")!);
        var customerId = Guid.Parse(Environment.GetEnvironmentVariable("PHASE5_CUSTOMER_ID")!);
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var customer = await db.OnlineCustomers.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == customerId && c.TenantId == tenant)
            .Select(c => new {c.Id, c.TenantId, c.CreatedAtUtc, c.EmailVerified, c.PhoneVerified, c.IsActive}).SingleAsync();
        var tokenStates = await db.OnlineCustomerEmailVerifications.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.OnlineCustomerId == customerId && v.TenantId == tenant)
            .Select(v => new {v.CreatedAtUtc, v.ExpiresAtUtc, v.UsedAtUtc}).ToListAsync();
        var cancellations = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.TenantId == tenant && o.Status == OrderStatus.Cancelled)
            .OrderBy(o => o.CreatedAt).Select(o => new {o.Id, o.OrderNumber, o.CreatedAt, o.CancelledAt,
                o.PaymentMethodCode, o.PaymentStatus, o.CheckoutState, o.FulfillmentStatus, o.CancellationReason}).ToListAsync();
        var ids = cancellations.Select(o => o.Id).ToArray();
        var intents = await db.PaymentIntents.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.TenantId == tenant && ids.Contains(i.OrderId))
            .Select(i => new {i.Id, i.OrderId, i.Status, i.CreatedAt, i.ExpiresAtUtc, i.FailedAt, i.CapturedAt,
                i.AuthorizedAt, i.FailureCode, i.WebhookEventCount}).ToListAsync();
        var reservations = await db.InventoryReservations.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.TenantId == tenant && ids.Contains(r.OrderId))
            .Select(r => new {r.OrderId, r.Status, r.CreatedAt, r.ExpiresAtUtc, r.ReservedQuantity,
                r.CommittedQuantity, r.ReleasedQuantity}).ToListAsync();
        var webhooks = await db.PaymentWebhookLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(w => w.TenantId == tenant && w.OrderId.HasValue && ids.Contains(w.OrderId.Value))
            .Select(w => new {w.OrderId, w.EventType, w.CreatedAt, w.ProcessedAt, w.ProcessingStatus, w.SignatureVerified}).ToListAsync();
        var idStrings = ids.Select(id => id.ToString()).ToArray();
        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.TenantId == tenant && idStrings.Contains(a.EntityId))
            .Select(a => new {a.EntityId, a.TimestampUtc, a.ActionType, a.EntityType, a.Source, ActorPresent = a.UserId != null, a.SuccessFlag}).ToListAsync();
        var events = await db.OutboxMessages.AsNoTracking().Where(e => e.TenantId == tenant && e.AggregateId.HasValue && ids.Contains(e.AggregateId.Value))
            .Select(e => new {e.AggregateId, e.EventType, e.CreatedAtUtc, e.ProcessedAtUtc, e.RetryCount}).ToListAsync();
        var ownedOrders = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.TenantId == tenant && o.OnlineCustomerId == customerId)
            .Select(o => new {o.Id, o.TenantId, o.OnlineCustomerId, o.Status, o.PaymentStatus, o.Total,
                o.Currency, o.CartId, o.WarehouseId, o.PaymentMethodCode, o.ShippingMethodId}).ToListAsync();
        var evidence = new {ObservedUtc = DateTime.UtcNow, ReadOnly = true, customer, tokenStates,
            Cancellations = cancellations.Select(o => new {o.Id, o.OrderNumber, o.CreatedAt, o.CancelledAt,
                o.PaymentMethodCode, o.PaymentStatus, o.CheckoutState, o.FulfillmentStatus,
                ReasonCategory = Classify(o.CancellationReason)}), intents, reservations, webhooks, audits, events, ownedOrders};
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!,
            "phase5-cancellation-and-identity-readonly.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions {WriteIndented = true}));
        await tx.RollbackAsync();
    }

    private static string Classify(string? reason) => string.IsNullOrWhiteSpace(reason) ? "missing"
        : reason.Contains("Inventory reservation expired", StringComparison.OrdinalIgnoreCase) ? "inventory-cleanup-expiry"
        : reason.Contains("expir", StringComparison.OrdinalIgnoreCase) ? "expiry-mentioned-unattributed"
        : reason.Contains("synthetic", StringComparison.OrdinalIgnoreCase) || reason.Contains("test", StringComparison.OrdinalIgnoreCase) ? "synthetic-test"
        : reason.Contains("customer", StringComparison.OrdinalIgnoreCase) ? "customer-mentioned-unattributed"
        : reason.Contains("admin", StringComparison.OrdinalIgnoreCase) ? "admin-mentioned-unattributed"
        : reason.Contains("manual", StringComparison.OrdinalIgnoreCase) ? "manual-mentioned-unattributed"
        : "other-retained-not-disclosed";
}
