using System.Data;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MongoDB.Driver;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Analytics.Attribution;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Analytics;
using PointOfSale.Infrastructure.Analytics.Alerts;
using PointOfSale.Infrastructure.Analytics.Dispatchers;
using PointOfSale.Infrastructure.Analytics.Funnels;
using PointOfSale.Infrastructure.Attribution;
using PointOfSale.Infrastructure.CampaignCost;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;
using StackExchange.Redis;
using Order = PointOfSale.Core.Entities.Order;

namespace PointOfSale.Tests.Analytics;

public sealed class Phase6ConfiguredTests
{
    internal static IConfiguration Config() => new ConfigurationBuilder().SetBasePath(Path.Combine(TestConfiguration.GetSolutionRoot(), "PointOfSale.API"))
        .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true).AddJsonFile("appsettings.Development.local.json", true).AddEnvironmentVariables().Build();
    private static string Evidence => Environment.GetEnvironmentVariable("PHASE6_EVIDENCE")!;
    private static async Task Write(string file, object value)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        var archive = Path.Combine(Evidence, "configured-invocations", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff"));
        Directory.CreateDirectory(archive);
        await File.WriteAllTextAsync(Path.Combine(archive, file), json);
        await File.WriteAllTextAsync(Path.Combine(Evidence, file), json);
    }

    [Phase6ConfiguredFact]
    public async Task ExistingMerchantAndPriorFinancialHistory_IndependentReadOnlyObserver()
    {
        var tenant = Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
        var priorIds = new[] { "ee42f429-fa3d-46b6-988a-b900cc3e8648", "258be5dc-92d9-4489-a1c4-0872d7f8b2b0", "45772d30-62df-4982-883e-852608e9f61f", "a0755b91-1f50-442b-9c16-df9d32e11cfd" }.Select(Guid.Parse).ToArray();
        var batch = Guid.Parse("6a557a6b-dc8c-4d0c-afc8-d934a569b457");
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var orders = await db.Orders.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && priorIds.Contains(x.Id))
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.SessionId, x.Status, x.PaymentStatus, x.Total, x.Currency, x.FinancialPostingStatus }).ToListAsync();
        var journals = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && (priorIds.Contains(x.SourceId) || x.SourceId == batch))
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.SourceId, x.SourceType, x.Status, debit = x.LedgerEntries.Sum(l => l.DebitAmount), credit = x.LedgerEntries.Sum(l => l.CreditAmount), x.IdempotencyKey }).ToListAsync();
        var whatsapp = await db.WhatsAppAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && x.Id == Guid.Parse("138379f5-7754-4c20-8863-b3b7aad28888"))
            .Select(x => new { x.Id, x.Enabled, x.Status }).SingleAsync();
        Assert.False(whatsapp.Enabled); Assert.Equal(4, orders.Count); Assert.All(journals, x => Assert.Equal(x.debit, x.credit));
        Assert.Equal(40m, orders.Single(x => x.Id == priorIds[1]).Total); Assert.Equal(32.35m, orders.Single(x => x.Id == priorIds[3]).Total);
        Assert.Equal(OrderStatus.Completed, orders.Single(x => x.Id == priorIds[3]).Status);
        Assert.Equal(4, journals.Count);
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { orders, journals, whatsapp }))));
        var stage = Environment.GetEnvironmentVariable("PHASE6_STAGE") ?? "baseline";
        var costs = await db.CampaignCosts.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && x.Campaign.StartsWith("p6qa20261018"))
            .OrderBy(x => x.CreatedAtUtc).Select(x => new { x.CampaignCostId, x.Source, x.Medium, x.Campaign, x.PeriodStartUtc, x.PeriodEndUtc, x.CostAmount, x.Currency, x.IsDeleted }).ToListAsync();
        if (stage == "final") {
            using var baseline = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Evidence, "merchant-observer-baseline.json")));
            Assert.Equal(baseline.RootElement.GetProperty("fingerprint").GetString(), fingerprint);
            Assert.All(costs, c => Assert.True(c.IsDeleted));
        }
        var config = await db.PixelTrackingConfigs.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant).Select(x => new { x.Id, x.IsEnabled, x.Strategy, x.Value, x.AppliesToJson }).ToListAsync();
        if (stage == "final") {
            var saved = Assert.Single(config);
            Assert.False(saved.IsEnabled); Assert.Equal(PixelOverrideStrategy.Percentage, saved.Strategy);
            Assert.Equal(100m, saved.Value); Assert.Equal("[]", saved.AppliesToJson);
        }
        await Write("merchant-observer-" + stage + ".json", new { observedUtc = DateTime.UtcNow, readOnly = true, fingerprint, orders, journals, whatsapp, costs, config });
        await tx.RollbackAsync();
        var configuration = Config();
        var mongo = new MongoClient(configuration["MongoDB:ConnectionString"]!).GetDatabase(configuration["MongoDB:DatabaseName"]!);
        if (File.Exists(Path.Combine(Evidence, "browser-tracking-final.json"))) {
            using var tracking = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Evidence, "browser-tracking-final.json")));
            var ids = tracking.RootElement.GetProperty("events").EnumerateArray().Select(x => x.GetProperty("eventId").GetGuid()).Distinct().ToArray();
            var events = await mongo.GetCollection<AnalyticsEventDocument>("analytics_events").Find(x => x.TenantId == tenant && ids.Contains(x.EventId)).ToListAsync();
            Assert.Equal(ids.Length, events.Count);
            Assert.All(events, e => Assert.DoesNotContain("token=", e.PageUrl));
            var sessions = events.Select(e => e.SessionId).Distinct().ToArray();
            var contexts = await mongo.GetCollection<AttributionContextDocument>("attribution_contexts").Find(x => x.TenantId == tenant && sessions.Contains(x.SessionId)).ToListAsync();
            Assert.Contains(contexts, c => c.Campaign != null && c.Campaign.StartsWith("p6qa20261018"));
            Assert.DoesNotContain(contexts, c => c.Source == "ahramstore" && c.Medium == "referral");
            var deliveryKeys = await db.ProviderDeliveryOutbox.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant).Select(x => x.DeliveryKey).ToListAsync();
            var externalQueueMatches = deliveryKeys.Count(key => ids.Any(id => key.Contains(id.ToString("D"), StringComparison.Ordinal)));
            Assert.Equal(0, externalQueueMatches);
            await Write("hosted-events-independent-observer.json", new { ids, events = events.Select(e => new { e.EventId, e.EventType, e.SessionId, e.UtmSource, e.UtmMedium, e.UtmCampaign, e.UtmTerm, e.UtmContent, e.Currency, e.Value }), contexts = contexts.Select(c => new { c.SessionId, c.Source, c.Medium, c.Campaign, c.CapturedAtUtc }), duplicateFree = true, externalQueueMatches, observer = "independent Mongo Find and SQL outbox read" });
        }
        if (File.Exists(Path.Combine(Evidence, "browser-report-readback-final.json"))) {
            using var browser = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Evidence, "browser-report-readback-final.json")));
            var funnel = browser.RootElement.GetProperty("funnel");
            var end = funnel.GetProperty("computedAtUtc").GetDateTime().ToUniversalTime();
            var start = end.AddMinutes(-funnel.GetProperty("windowMinutes").GetInt32());
            var redisSettings = configuration.GetSection("Redis").Get<RedisSettings>()!;
            using var redis = await ConnectionMultiplexer.ConnectAsync(redisSettings.ConnectionString);
            var sourceCounts = new List<long>();
            foreach (var name in new[] { "pageview", "productview", "addtocart", "begincheckout" }) {
                var hour = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0, DateTimeKind.Utc);
                long count = 0;
                while (hour <= end) {
                    var value = await redis.GetDatabase().StringGetAsync($"{redisSettings.InstanceName}:analytics:tenant:{tenant:N}:counter:{name}:{hour:yyyyMMddHH}");
                    if (value.HasValue) count += long.Parse(value!);
                    hour = hour.AddHours(1);
                }
                sourceCounts.Add(count);
            }
            var purchaseIds = await db.LedgerEntries.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == tenant && e.Account.AccountCode == "4020" && e.CreditAmount > 0 &&
                (e.JournalEntry.Status == JournalStatus.Posted || e.JournalEntry.Status == JournalStatus.Reversed) && e.JournalEntry.PostedAtUtc >= start && e.JournalEntry.PostedAtUtc < end &&
                new[] { "Order_Payment", "Order_COD", "Order_Wallet", "Order_UnresolvedPaymentClearance" }.Contains(e.JournalEntry.SourceType))
                .Select(e => e.JournalEntry.SourceId).Distinct().ToListAsync();
            sourceCounts.Add(await db.Orders.IgnoreQueryFilters().CountAsync(o => o.TenantId == tenant && o.Source == OrderSource.Ecommerce && purchaseIds.Contains(o.Id)));
            Assert.Equal(sourceCounts, funnel.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("count").GetInt64()));
            var summary = browser.RootElement.GetProperty("summary");
            var from = summary.GetProperty("startDate").GetDateTime(); var to = summary.GetProperty("endDate").GetDateTime();
            // Do not use the production reader or a design-time context's tenant filters
            // as the oracle. Independently join the explicit tenant's recognition IDs,
            // ecommerce orders, ledger net revenue and Mongo dimensions.
            var recognized = await db.LedgerEntries.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == tenant && e.Account.AccountCode == "4020" && e.CreditAmount > 0 &&
                (e.JournalEntry.Status == JournalStatus.Posted || e.JournalEntry.Status == JournalStatus.Reversed) && e.JournalEntry.PostedAtUtc >= from && e.JournalEntry.PostedAtUtc < to &&
                new[] { "Order_Payment", "Order_COD", "Order_Wallet", "Order_UnresolvedPaymentClearance" }.Contains(e.JournalEntry.SourceType))
                .Select(e => e.JournalEntry.SourceId).Distinct().ToListAsync();
            var ecommerce = await db.Orders.IgnoreQueryFilters().AsNoTracking().Where(o => o.TenantId == tenant && o.Source == OrderSource.Ecommerce && recognized.Contains(o.Id)).Select(o => o.Id).ToListAsync();
            var net = await db.LedgerEntries.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == tenant && ecommerce.Contains(e.JournalEntry.SourceId) && e.Account.AccountCode == "4020" &&
                (e.JournalEntry.Status == JournalStatus.Posted || e.JournalEntry.Status == JournalStatus.Reversed) && e.JournalEntry.PostedAtUtc < to)
                .GroupBy(e => e.JournalEntry.SourceId).Select(g => new { OrderId = g.Key, Net = g.Sum(e => e.CreditAmount - e.DebitAmount) }).ToDictionaryAsync(x => x.OrderId, x => x.Net);
            var source = await mongo.GetCollection<OrderAttributionDocument>("order_attributions").Find(x => x.TenantId == tenant && x.Model == "LastTouch" && ecommerce.Contains(x.OrderId)).ToListAsync();
            var expectedRevenue = source.Sum(x => net.GetValueOrDefault(x.OrderId, 0));
            await Write("hosted-report-independent-observer.json", new { end, start, independentFunnelCounts = sourceCounts, actualApiCounts = funnel.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("count").GetInt64()).ToArray(),
                attributedOrderIds = source.Select(x => x.OrderId), expectedRevenue, sourceOrderCount = ecommerce.Count, sourceRevenue = net.Values.Sum(), dimensions = source.Count,
                method = "Direct Redis bucket reads + independent SQL recognition IDs; Mongo dimensions joined to SQL net revenue" });
            Assert.Equal(expectedRevenue, summary.GetProperty("totalRevenue").GetDecimal());
            Assert.Equal(source.Select(x => x.OrderId).Distinct().Count(), summary.GetProperty("totalOrders").GetInt32());
        }
    }

    [Phase6ConfiguredFact]
    public async Task ExistingDatabase_EventIdentityFunnelAttributionAndLedgerRoi_RollbackAndFreshObserver()
    {
        var tenant = Guid.NewGuid(); var session = "P6QA-" + Guid.NewGuid().ToString("N");
        var configuration = Config();
        using var redis = await ConnectionMultiplexer.ConnectAsync(configuration["Redis:ConnectionString"]!);
        var redisSettings = Options.Create(configuration.GetSection("Redis").Get<RedisSettings>()!);
        var mongo = new MongoClient(configuration["MongoDB:ConnectionString"]!).GetDatabase(configuration["MongoDB:DatabaseName"]!);
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var events = new List<AnalyticsEvent>(); var keys = new List<RedisKey>();
        await Write("configured-manifest-start.json", new { tenant, session, orders = ids, rollbackOnlySql = true, controlledExternalDispatch = true });
        try {
            await using var db = new ApplicationDbContext(options, new TestTenantContext(tenant));
            await using var tx = await db.Database.BeginTransactionAsync();
            db.Tenants.Add(new Tenant { Id = tenant, Name = "P6QA ROLLBACK ONLY", Slug = "p6qa-" + tenant.ToString("N"), Currency = "EGP", Status = TenantStatus.Active, IsActive = true });
            var sales = Account.Create(tenant, "4020", "P6QA sales", AccountType.Revenue);
            var clearing = Account.Create(tenant, "1130", "P6QA clearing", AccountType.Asset);
            db.LedgerAccounts.AddRange(sales, clearing);
            var amounts = new[] { 100m, 20m, 30m, 100m, 50m, 0m };
            var campaigns = new[] { "profit", "loss", "free", "refunded", "", "cancelled" };
            using var ledger = LedgerMutationScope.Authorize();
            for (int i = 0; i < ids.Length; i++) {
                db.Orders.Add(new Order { Id = ids[i], TenantId = tenant, OrderNumber = "P6QA-" + ids[i].ToString("N"), Source = OrderSource.Ecommerce, Currency = "EGP", Total = amounts[i], SessionId = session + "-" + i,
                    PaymentStatus = i == 5 ? PaymentStatus.Failed : PaymentStatus.Collected, Status = i == 5 ? OrderStatus.Cancelled : OrderStatus.Confirmed });
                if (i == 5) continue;
                var journal = JournalEntry.Create(tenant, "P6QA-" + i, DateTime.UtcNow, "Order_COD", ids[i], null, "Synthetic analytics fixture / rollback only", "P6QA-" + ids[i], null);
                db.JournalEntries.Add(journal);
                clearing.ApplyEntry(amounts[i], 0); sales.ApplyEntry(0, amounts[i]);
                db.LedgerEntries.Add(LedgerEntry.CreateDebit(tenant, journal.Id, clearing.Id, amounts[i], clearing.CurrentBalance, 1));
                db.LedgerEntries.Add(LedgerEntry.CreateCredit(tenant, journal.Id, sales.Id, amounts[i], sales.CurrentBalance, 2));
            }
            await db.SaveChangesAsync();
            var authority = new StorefrontAnalyticsAuthorityReader(db);
            var contexts = new AttributionContextStore(redis, mongo, redisSettings, NullLogger<AttributionContextStore>.Instance);
            var attributions = new OrderAttributionStore(redis, mongo, authority, redisSettings, NullLogger<OrderAttributionStore>.Instance);
            var hot = new AnalyticsHotStoreService(redis, redisSettings, NullLogger<AnalyticsHotStoreService>.Instance);
            var cold = new AnalyticsColdStoreService(mongo, NullLogger<AnalyticsColdStoreService>.Instance);
            var contextResolver = new AttributionContextResolver(contexts, NullLogger<AttributionContextResolver>.Instance);
            var orderResolver = new OrderAttributionResolver(contexts, attributions, NullLogger<OrderAttributionResolver>.Instance);
            var settings = new Mock<IAnalyticsSettingsService>();
            var pipeline = new AnalyticsDispatcherPipeline([], settings.Object, NullLogger<AnalyticsDispatcherPipeline>.Instance);
            var telemetry = new AnalyticsReliabilityTelemetry();
            var consumer = new AnalyticsEventConsumer(hot, cold, pipeline, contextResolver, orderResolver, telemetry, NullLogger<AnalyticsEventConsumer>.Instance);
            async Task Consume(AnalyticsEvent e) {
                if (!events.Any(x => x.EventId == e.EventId)) events.Add(e);
                var c = Mock.Of<ConsumeContext<AnalyticsEventReceivedEvent>>(x => x.Message == AnalyticsEventReceivedEvent.Create(e) && x.CancellationToken == CancellationToken.None);
                await consumer.Consume(c);
            }
            var now = DateTime.UtcNow.AddMinutes(-2);
            AnalyticsEvent Event(AnalyticsEventType type, string actor, int minute, string? campaign = null) {
                var e = AnalyticsEvent.Create(tenant, type); e.Actor.SessionId = actor; e.Context.Currency = "EGP"; e.Context.PageUrl = "https://qa.example/"; e.OccurredAtUtc = now.AddMinutes(minute); e.Context.UtmSource = "p6qa"; e.Context.UtmMedium = "cpc"; e.Context.UtmCampaign = campaign; return e;
            }
            // Out-of-order and duplicate deliveries through the real consumer/stores.
            var late = Event(AnalyticsEventType.PageView, session + "-0", -1, "profit");
            var early = Event(AnalyticsEventType.PageView, session + "-0", -5, "first");
            await Consume(late); await Consume(early); await Consume(late);
            await Consume(Event(AnalyticsEventType.PageView, session + "-abandoned", -4, "abandoned"));
            await Consume(Event(AnalyticsEventType.PageView, session + "-partial", -3, "partial"));
            for (int i = 0; i < 2; i++) {
                var product = Event(AnalyticsEventType.ProductView, session + "-" + i, -2); product.Payload.ProductId = Guid.NewGuid(); await Consume(product);
                var cart = Event(AnalyticsEventType.AddToCart, session + "-" + i, -1); cart.Payload.ProductId = product.Payload.ProductId; cart.Payload.Quantity = 1; await Consume(cart); await Consume(cart);
            }
            await Consume(Event(AnalyticsEventType.BeginCheckout, session + "-0", 0));
            for (int i = 1; i < 4; i++) await contextResolver.ResolveFromEventAsync(Event(AnalyticsEventType.PageView, session + "-" + i, -1, campaigns[i]));
            // Actual server purchase consumer obtains persisted order/session/total, not browser revenue.
            var publisher = new Mock<IEventPublisher>();
            publisher.Setup(p => p.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>())).Callback<AnalyticsEventReceivedEvent, CancellationToken>((e, _) => events.Add(e.AnalyticsEvent)).Returns(Task.CompletedTask);
            var purchases = new AuthoritativePurchaseAnalyticsConsumer(db, publisher.Object, NullLogger<AuthoritativePurchaseAnalyticsConsumer>.Instance);
            for (int i = 0; i < 6; i++) {
                var paid = new OrderPaidEvent { TenantId = tenant, OrderId = ids[i], PaidAt = now.AddSeconds(30), AmountPaid = 99999, Currency = "EGP", Provider = "internal-qa", PaymentMethod = "COD" };
                await purchases.Consume(Mock.Of<ConsumeContext<OrderPaidEvent>>(x => x.Message == paid && x.CancellationToken == CancellationToken.None));
            }
            foreach (var purchase in events.Where(x => x.Type == AnalyticsEventType.PurchaseCompleted).ToArray()) { Assert.NotEqual(99999, purchase.Payload.Value); await Consume(purchase); await Consume(purchase); }
            // A later visit must not win attribution for the earlier conversion.
            await contextResolver.ResolveFromEventAsync(Event(AnalyticsEventType.PageView, session + "-0", 1, "future"));
            Assert.Equal("profit", (await orderResolver.ResolveWithoutStoringAsync(events.First(x => x.Type == AnalyticsEventType.PurchaseCompleted), AttributionModel.LastTouch))!.Campaign);
            var refund = JournalEntry.Create(tenant, "P6QA-refund", DateTime.UtcNow, "Order_Refund", ids[3], null, "Synthetic full refund / rollback only", "P6QA-refund-" + ids[3], null);
            db.JournalEntries.Add(refund); sales.ApplyEntry(100, 0); clearing.ApplyEntry(0, 100);
            db.LedgerEntries.Add(LedgerEntry.CreateDebit(tenant, refund.Id, sales.Id, 100, sales.CurrentBalance, 1));
            db.LedgerEntries.Add(LedgerEntry.CreateCredit(tenant, refund.Id, clearing.Id, 100, clearing.CurrentBalance, 2));
            await db.SaveChangesAsync();
            var costs = new CampaignCostService(db, NullLogger<CampaignCostService>.Instance);
            foreach (var (name, spend) in new[] { ("profit", 40m), ("loss", 40m), ("free", 0m), ("refunded", 10m), ("cost-only", 25m) })
                await costs.CreateOrUpdateAsync(tenant, new() { Source = "p6qa", Medium = "cpc", Campaign = name, CostAmount = spend, Currency = "EGP", PeriodStartUtc = DateTime.UtcNow.Date, PeriodEndUtc = DateTime.UtcNow.Date.AddDays(1) });
            var from = DateTime.UtcNow.Date; var to = from.AddDays(1);
            var roi = new CampaignRoiService(costs, attributions, db, NullLogger<CampaignRoiService>.Instance);
            var campaignResults = await roi.CalculateAsync(tenant, from, to, AttributionModel.LastTouch);
            Assert.Equal(150m, campaignResults.Sum(x => x.Revenue)); Assert.Equal(115m, campaignResults.Sum(x => x.Cost));
            Assert.Equal(150m, campaignResults.Single(x => x.Campaign == "profit").RoiPercentage);
            Assert.Equal(-50m, campaignResults.Single(x => x.Campaign == "loss").RoiPercentage);
            Assert.Null(campaignResults.Single(x => x.Campaign == "free").RoiPercentage);
            Assert.Equal(-100m, campaignResults.Single(x => x.Campaign == "refunded").RoiPercentage);
            Assert.Equal(-100m, campaignResults.Single(x => x.Campaign == "cost-only").RoiPercentage);
            var sourceResults = await roi.GetRoiBySourceAsync(tenant, from, to, AttributionModel.LastTouch);
            Assert.Equal(200m, sourceResults.Sum(x => x.TotalRevenue));
            var metrics = new AnalyticsMetricReader(redis, authority, redisSettings, NullLogger<AnalyticsMetricReader>.Instance);
            var funnels = new AnalyticsFunnelService(metrics, hot, Mock.Of<IProductReadRepository>(), NullLogger<AnalyticsFunnelService>.Instance);
            var funnel = (await funnels.ComputeFunnelAsync(tenant, "purchase_funnel", 60))!;
            Assert.Equal(new long[] { 4, 2, 2, 1, 5 }, funnel.Steps.Select(s => s.Count)); Assert.Equal(125m, funnel.OverallConversionRate);
            Assert.Equal(5, (await authority.GetCommerceSnapshotAsync(tenant, from, to)).PurchaseCount);
            Assert.Empty((await attributions.GetAttributionPageAsync(Guid.NewGuid(), from, to)).Items);
            Assert.Null(await attributions.GetAttributionAsync(tenant, ids[5], AttributionModel.LastTouch));
            var page = await attributions.GetAttributionPageAsync(tenant, from, to, AttributionModel.LastTouch);
            Assert.Equal(5, page.TotalCount); Assert.Equal(0m, page.Items.Single(x => x.OrderId == ids[3]).OrderValue);
            var documentCount = await mongo.GetCollection<AnalyticsEventDocument>("analytics_events").CountDocumentsAsync(x => x.TenantId == tenant);
            Assert.Equal(events.Count, documentCount);
            await Write("configured-calculations.json", new { tenant, session, orders = ids, events = events.Select(x => new { x.EventId, x.Type, x.OccurredAtUtc, x.Actor.SessionId }), documentCount,
                expectedFunnel = new[] { 4, 2, 2, 1, 5 }, actualFunnel = funnel, campaignResults, sourceResults, expectedCampaignRevenue = 150m, expectedSpend = 115m, expectedCampaignRoi = 30.43m, expectedSourceRevenue = 200m, expectedSourceRoi = 73.91m,
                orderAssociations = page.Items, sqlRollback = true, externalDispatchers = 0 });
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        } finally {
            // Only the newly generated, manifested tenant. No earlier-phase data is selected.
            await mongo.GetCollection<AnalyticsEventDocument>("analytics_events").DeleteManyAsync(x => x.TenantId == tenant);
            await mongo.GetCollection<AttributionContextDocument>("attribution_contexts").DeleteManyAsync(x => x.TenantId == tenant);
            await mongo.GetCollection<OrderAttributionDocument>("order_attributions").DeleteManyAsync(x => x.TenantId == tenant);
            foreach (var endpoint in redis.GetEndPoints()) keys.AddRange(redis.GetServer(endpoint).Keys(pattern: "*" + tenant.ToString("N") + "*"));
            if (keys.Count > 0) await redis.GetDatabase().KeyDeleteAsync(keys.Distinct().ToArray());
            await Write("configured-cleanup-keys.json", new { tenant, keys = keys.Select(x => x.ToString()).ToArray() });
        }
        await using var observer = new ApplicationDbContext(options);
        Assert.False(await observer.Tenants.IgnoreQueryFilters().AnyAsync(x => x.Id == tenant));
        Assert.False(await observer.Orders.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
        Assert.False(await observer.JournalEntries.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
        Assert.False(await observer.CampaignCosts.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
        var freshMongo = new MongoClient(configuration["MongoDB:ConnectionString"]!).GetDatabase(configuration["MongoDB:DatabaseName"]!);
        Assert.Equal(0, await freshMongo.GetCollection<AnalyticsEventDocument>("analytics_events").CountDocumentsAsync(x => x.TenantId == tenant));
        Assert.Equal(0, await freshMongo.GetCollection<AttributionContextDocument>("attribution_contexts").CountDocumentsAsync(x => x.TenantId == tenant));
        Assert.Equal(0, await freshMongo.GetCollection<OrderAttributionDocument>("order_attributions").CountDocumentsAsync(x => x.TenantId == tenant));
        await Write("configured-cleanup-observer.json", new { tenant, freshSqlAbsent = true, freshMongoAbsent = true, deletedKeysAbsent = keys.All(k => !redis.GetDatabase().KeyExists(k)), observedUtc = DateTime.UtcNow });
    }
}

public sealed class Phase6ConfiguredFactAttribute : FactAttribute
{
    public Phase6ConfiguredFactAttribute() { if (Environment.GetEnvironmentVariable("PHASE6_CONFIGURED") != "1") Skip = "Existing configured QA database opt-in required; no provisioning fallback."; }
}
