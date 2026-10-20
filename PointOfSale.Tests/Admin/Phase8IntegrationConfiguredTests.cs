using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MongoDB.Driver;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Services;
using PointOfSale.Tests.Analytics;
using PointOfSale.Tests.Shipping;
using PointOfSale.Tests.AbandonedCart;
using PointOfSale.Tests.Infrastructure;
using StackExchange.Redis;

namespace PointOfSale.Tests.Admin;

public sealed class Phase8IntegrationConfiguredTests
{
    private const string Marker = "P8QA20261018-1116";
    private static readonly Guid Tenant = Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
    private static string Evidence => Environment.GetEnvironmentVariable("PHASE8_EVIDENCE")!;
    private static Task Write(string file, object value) => File.WriteAllTextAsync(Path.Combine(Evidence, file), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

    [NativeSchedulerConfiguredFact]
    public async Task LocalDiagnostics_ReadExistingAiKeysAndPreservePriorEvidence_NoInference()
    {
        var configuration = Phase6ConfiguredTests.Config();
        using var redis = await ConnectionMultiplexer.ConnectAsync(configuration["Redis:ConnectionString"]!);
        var records = new List<object>();
        foreach (var date in new[] { "2026-10-17", "2026-10-18" })
        {
            var key = $"ai:usage:{Tenant}:{date}";
            var value = await redis.GetDatabase().StringGetAsync(key);
            records.Add(new { date, present = value.HasValue, value = value.HasValue ? JsonSerializer.Deserialize<JsonElement>(value.ToString()) : (JsonElement?)null });
        }
        var root = TestConfiguration.GetSolutionRoot();
        var files = new[] { "browser-hosted-second.json", "browser-hosted-third.json", "merchant-ai-usage-observation.json" };
        var hashes = files.Select(file => new { file, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root, ".ai/evidence/storefront-phase7-20261018", file)))) }).ToArray();
        await Write("local-ai-uncertainty.json", new { observedUtc = DateTime.UtcNow, redisOperation = "StringGet only; no mutation", records, preservedEvidence = hashes,
            remoteRequestsMade = 0, providerAccountEvidenceAvailable = false,
            conclusion = "The two early Phase7 chat attempts may have reached remote OpenRouter inference. Execution and cost remain unknown. No before-snapshot exists. Internal token/request counts are not proof of provider execution, billing, or zero cost." });
    }

    [Phase8HostedAgeFact]
    public async Task AgeExactManifestedSession_ThenCanonicalAbandonment_WithoutMessageDelivery()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Evidence, "browser-quote-current.json")));
        Assert.Equal(Marker, manifest.RootElement.GetProperty("marker").GetString());
        var id = manifest.RootElement.GetProperty("sessionId").GetGuid();
        var cart = manifest.RootElement.GetProperty("cartId").GetGuid();
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var session = await db.CheckoutSessions.IgnoreQueryFilters().SingleAsync(x => x.Id == id && x.TenantId == Tenant && x.CartId == cart && x.Source == Marker && x.CustomerName == Marker);
        Assert.Equal(CheckoutSessionStatus.Active, session.Status);
        Assert.Null(session.ConvertedOrderId);
        Assert.Equal("EGP", session.Currency);
        var before = new { session.Id, session.CartId, session.Status, session.CartTotal, session.Currency, session.LastActivityAtUtc };
        await Write("hosted-age-intent.json", new { before, intended = "Age only this exact new session 31 minutes; invoke canonical service with captured publisher; no messaging or account mutation" });
        session.LastActivityAtUtc = DateTime.UtcNow.AddMinutes(-31);
        await db.SaveChangesAsync();
        var publisher = new Mock<IEventPublisher>(); publisher.SetReturnsDefault(Task.CompletedTask);
        using var telemetry = new CommerceReliabilityTelemetry();
        await new CheckoutSessionService(db, publisher.Object, telemetry, NullLogger<CheckoutSessionService>.Instance).MarkAsAbandonedAsync(id);
        await using var observer = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var after = await observer.CheckoutSessions.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == id && x.TenantId == Tenant);
        Assert.Equal(CheckoutSessionStatus.Abandoned, after.Status);
        Assert.Null(after.ConvertedOrderId);
        await Write("hosted-aged-observer.json", new { before, after = new { after.Id, after.CartId, after.Status, after.CartTotal, after.Currency }, canonicalService = true, controlledPublisher = true, independentReadback = true });
    }

    [Phase8HostedObserverFact]
    public async Task RetainedLifecycle_IndependentSqlMongoLedgerAttributionObserver_ReadOnly()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Evidence, "browser-submit.json")));
        using var committed = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Evidence, "submit-committed-readback.json")));
        Assert.Equal(1, committed.RootElement.GetProperty("count").GetInt32());
        var id = committed.RootElement.GetProperty("orders")[0].GetProperty("id").GetGuid();
        var couponId = manifest.RootElement.GetProperty("couponId").GetGuid();
        var sessionId = manifest.RootElement.GetProperty("sessionId").GetGuid();
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var order = await db.Orders.IgnoreQueryFilters().AsNoTracking().Include(x => x.Items).SingleAsync(x => x.TenantId == Tenant && x.Id == id && x.CustomerNotes != null && x.CustomerNotes.StartsWith(Marker));
        var siblings = await db.Orders.IgnoreQueryFilters().CountAsync(x => x.TenantId == Tenant && x.CustomerNotes != null && x.CustomerNotes.StartsWith(Marker));
        var coupon = await db.Coupons.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Tenant && x.Id == couponId).Select(x => new { x.Id, x.Code, x.IsDeleted, x.CurrentUsage }).SingleAsync();
        var sessions = await db.CheckoutSessions.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Tenant && x.Id == sessionId).Select(x => new { x.Id, x.CartId, x.Status, x.ConvertedOrderId }).ToListAsync();
        var shipments = await db.Shipments.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Tenant && x.OrderId == id).Select(x => new { x.Id, x.OrderId, x.Status, x.ShippingMethodId, x.OriginWarehouseId, x.ShippingCost, x.Currency, x.CarrierCode, x.IsCarrierManaged, x.ExternalRegistrationAttemptCount, x.ExternalRegistrationState, x.CodCollected }).ToListAsync();
        var members = await db.PaymentSettlementOrders.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Tenant && x.OrderId == id).Select(x => new { x.SettlementBatchId, x.OrderId, x.OrderTotal, x.Currency }).ToListAsync();
        var batches = members.Select(x => x.SettlementBatchId).ToArray();
        var journals = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Tenant && (x.SourceId == id || batches.Contains(x.SourceId)))
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.SourceType, x.SourceId, x.Status, x.IdempotencyKey, debit = x.LedgerEntries.Sum(l => l.DebitAmount), credit = x.LedgerEntries.Sum(l => l.CreditAmount) }).ToListAsync();
        var receipts = await db.AccountingOutboxMessages.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Tenant && (x.BusinessEntityId == id || batches.Contains(x.BusinessEntityId))).Select(x => new { x.IdempotencyKey, x.ProcessedAtUtc, x.JournalId }).ToListAsync();
        var reservations = await db.InventoryReservations.IgnoreQueryFilters().CountAsync(x => x.TenantId == Tenant && x.OrderId == id);
        var movements = await db.StockMovements.IgnoreQueryFilters().CountAsync(x => x.TenantId == Tenant && x.ReferenceId == id);
        var revenue = await db.LedgerEntries.IgnoreQueryFilters().Where(x => x.TenantId == Tenant && x.JournalEntry.SourceId == id && x.Account.AccountCode == "4020" && x.JournalEntry.Status == JournalStatus.Posted).SumAsync(x => x.CreditAmount - x.DebitAmount);
        using var marketing = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Evidence, "browser-marketing-cleanup.json")));
        var costId = marketing.RootElement.GetProperty("cost").GetProperty("campaignCostId").GetGuid();
        var cost = await db.CampaignCosts.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Tenant && x.CampaignCostId == costId)
            .Select(x => new { x.CampaignCostId, x.Source, x.Medium, x.Campaign, x.CostAmount, x.Currency, x.IsDeleted }).SingleAsync();
        var profit = revenue - cost.CostAmount;
        var roi = Math.Round(profit / cost.CostAmount * 100m, 2);
        var negativeSessionId = Guid.Parse("9f3f2b18-4f23-4768-beb0-0f8739216f79");
        var negativeSession = await db.CheckoutSessions.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == Tenant && x.Id == negativeSessionId)
            .Select(x => new { x.Id, x.CartId, x.Status, x.ConvertedOrderId }).SingleAsync();
        var negativeOrders = await db.Orders.IgnoreQueryFilters().CountAsync(x => x.TenantId == Tenant && x.CustomerNotes == Marker + "-NEGATIVE");
        var configuration = Phase6ConfiguredTests.Config();
        var mongo = new MongoClient(configuration["MongoDB:ConnectionString"]!).GetDatabase(configuration["MongoDB:DatabaseName"]!);
        var projection = await mongo.GetCollection<OrderDocument>("orders").Find(x => x.TenantId == Tenant && x.OrderId == id).SingleAsync();
        var dimensions = await mongo.GetCollection<OrderAttributionDocument>("order_attributions").Find(x => x.TenantId == Tenant && x.OrderId == id).ToListAsync();
        var contexts = await mongo.GetCollection<AttributionContextDocument>("attribution_contexts").Find(x => x.TenantId == Tenant && x.SessionId == order.SessionId).ToListAsync();
        var messages = await mongo.GetCollection<WhatsAppMessageLogDocument>("WhatsAppMessageLogs").Find(x => x.TenantId == Tenant.ToString() && x.RelatedEntityId == id.ToString()).CountDocumentsAsync();
        await Write("hosted-lifecycle-independent-observer.json", new { observedUtc = DateTime.UtcNow, readOnly = true, marker = Marker, siblings,
            order = new { order.Id, order.OrderNumber, order.Status, order.PaymentStatus, order.FinancialPostingStatus, order.FulfillmentStatus, order.Total, order.ShippingCost, order.DiscountAmount, order.Currency, order.SessionId, items = order.Items.Select(x => new { x.Id, x.ProductId, x.VariantId, x.Quantity, x.UnitPrice, x.Total }) },
            coupon, sessions, shipments, members, journals, receipts, reservations, movements, recognizedNetRevenue = revenue, cost, independentlyCalculatedProfit = profit, independentlyCalculatedRoi = roi, negativeSession, negativeOrders,
            projection = new { projection.OrderId, projection.Status, projection.PaymentStatus, projection.Total, projection.Currency },
            dimensions = dimensions.Select(x => new { x.OrderId, x.Model, x.Source, x.Medium, x.Campaign, x.OrderValue, x.AttributedAtUtc }),
            contexts = contexts.Select(x => new { x.SessionId, x.Source, x.Medium, x.Campaign, x.CapturedAtUtc }), outboundMessageLogs = messages });
        Assert.Equal(1, siblings); Assert.Equal(30m, order.Total); Assert.Equal(10m, order.ShippingCost); Assert.Equal("EGP", order.Currency);
        Assert.Equal(OrderStatus.Completed, order.Status); Assert.Equal(PaymentStatus.Settled, order.PaymentStatus); Assert.Equal(FinancialPostingStatus.Posted, order.FinancialPostingStatus);
        Assert.Equal(CheckoutSessionStatus.ConvertedToOrder, Assert.Single(sessions).Status); Assert.Equal(id, sessions[0].ConvertedOrderId);
        var shipment = Assert.Single(shipments); Assert.Equal(ShipmentStatus.Delivered, shipment.Status); Assert.False(shipment.IsCarrierManaged); Assert.Equal(0, shipment.ExternalRegistrationAttemptCount); Assert.Null(shipment.ExternalRegistrationState); Assert.False(shipment.CodCollected);
        Assert.Equal(10m, shipment.ShippingCost); Assert.Equal("EGP", shipment.Currency);
        Assert.Single(members); Assert.Equal(30m, members[0].OrderTotal);
        Assert.Equal(2, journals.Count); Assert.All(journals, x => { Assert.Equal(JournalStatus.Posted, x.Status); Assert.Equal(30m, x.debit); Assert.Equal(x.debit, x.credit); });
        Assert.Single(journals, x => x.SourceType == "Order_COD"); Assert.Single(journals, x => x.SourceType == "ManualCodSettlement");
        Assert.Equal(2, journals.Select(x => x.IdempotencyKey).Distinct().Count()); Assert.Equal(2, receipts.Count); Assert.All(receipts, x => Assert.NotNull(x.ProcessedAtUtc));
        Assert.Equal(0, reservations); Assert.Equal(0, movements); Assert.Equal(30m, revenue); Assert.Equal(0, messages);
        Assert.True(coupon.IsDeleted); Assert.Equal(1, coupon.CurrentUsage);
        Assert.True(cost.IsDeleted); Assert.Equal(12m, cost.CostAmount); Assert.Equal("EGP", cost.Currency);
        Assert.Equal(18m, profit); Assert.Equal(150m, roi);
        Assert.Equal(revenue, marketing.RootElement.GetProperty("roi").GetProperty("revenue").GetDecimal());
        Assert.Equal(roi, marketing.RootElement.GetProperty("roi").GetProperty("roiPercentage").GetDecimal());
        Assert.Equal(0, negativeOrders); Assert.Null(negativeSession.ConvertedOrderId); Assert.NotEqual(CheckoutSessionStatus.ConvertedToOrder, negativeSession.Status);
        Assert.Equal(order.Total, projection.Total); Assert.Equal(order.Status.ToString(), projection.Status); Assert.Equal(order.PaymentStatus.ToString(), projection.PaymentStatus);
        Assert.Equal(2, dimensions.Count);
        Assert.Equal(Marker.ToLowerInvariant(), Assert.Single(dimensions, x => x.Model == "LastTouch").Campaign);
        foreach (var dimension in dimensions)
        {
            var eligible = contexts.Where(x => x.CapturedAtUtc <= dimension.AttributedAtUtc)
                .OrderBy(x => x.CapturedAtUtc).ThenBy(x => x.Source, StringComparer.Ordinal).ThenBy(x => x.Campaign, StringComparer.Ordinal).ToList();
            var expected = dimension.Model == "FirstTouch" ? eligible.FirstOrDefault() : eligible.LastOrDefault();
            Assert.Equal(expected?.Source ?? "direct", dimension.Source);
            Assert.Equal(expected?.Medium ?? "none", dimension.Medium);
            Assert.Equal(expected?.Campaign, dimension.Campaign);
        }
        await tx.RollbackAsync();
    }
}

public sealed class Phase8HostedAgeFactAttribute : FactAttribute
{
    public Phase8HostedAgeFactAttribute() { if (Environment.GetEnvironmentVariable("PHASE8_AGE_HOSTED") != "1") Skip = "Exact manifested new hosted session opt-in PHASE8_AGE_HOSTED=1 required."; }
}
public sealed class Phase8HostedObserverFactAttribute : FactAttribute
{
    public Phase8HostedObserverFactAttribute() { if (Environment.GetEnvironmentVariable("PHASE8_OBSERVE_HOSTED") != "1") Skip = "Exact hosted order manifest opt-in PHASE8_OBSERVE_HOSTED=1 required."; }
}
