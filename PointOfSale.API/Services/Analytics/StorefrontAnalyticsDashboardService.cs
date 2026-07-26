using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.API.DTOs.Analytics;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Analytics;
using PointOfSale.Infrastructure.Analytics.Funnels;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.API.Services.Analytics;

public class StorefrontAnalyticsDashboardService : IStorefrontAnalyticsDashboardService
{
    private static readonly string[] SupportedRanges = ["today", "7d", "30d", "this_month"];
    private static readonly string[] TrackedEventTypes =
    [
        nameof(AnalyticsEventType.PageView),
        nameof(AnalyticsEventType.ProductView),
        nameof(AnalyticsEventType.AddToCart),
        nameof(AnalyticsEventType.BeginCheckout)
    ];

    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoCollection<AnalyticsEventDocument> _analyticsEvents;
    private readonly IStorefrontAnalyticsAuthorityReader _authorityReader;
    private readonly IAnalyticsFunnelService _funnelService;

    public StorefrontAnalyticsDashboardService(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        IStorefrontAnalyticsAuthorityReader authorityReader,
        IAnalyticsFunnelService funnelService)
    {
        _dbContext = dbContext;
        _analyticsEvents = mongoDatabase.GetCollection<AnalyticsEventDocument>("analytics_events");
        _authorityReader = authorityReader;
        _funnelService = funnelService;
    }

    public async Task<StorefrontAnalyticsDashboardResponse> GetDashboardAsync(
        Guid tenantId,
        string? range,
        CancellationToken cancellationToken = default)
    {
        var normalizedRange = NormalizeRange(range);
        var (startUtc, endUtc) = ResolveDateRange(normalizedRange);

        // EF Core DbContext is not thread-safe, so PostgreSQL queries must remain sequential.
        // Mongo aggregation can run independently while EF queries are in flight.
        var eventSummaryTask = GetEventSummaryAsync(tenantId, startUtc, endUtc, cancellationToken);

        var commerce = await _authorityReader.GetCommerceSnapshotAsync(
            tenantId,
            startUtc,
            endUtc,
            cancellationToken);
        var topProducts = await GetTopSellingProductsAsync(
            tenantId,
            commerce.NetRevenueByOrder,
            cancellationToken);
        var abandonedSummary = await GetAbandonedCheckoutSummaryAsync(tenantId, startUtc, endUtc, cancellationToken);
        var eventSummary = await eventSummaryTask;
        eventSummary.EventCounts[nameof(AnalyticsEventType.PurchaseCompleted)] = commerce.PurchaseCount;

        var response = new StorefrontAnalyticsDashboardResponse
        {
            Visits = eventSummary.Visits,
            Orders = commerce.PurchaseCount,
            Revenue = RoundMoney(commerce.NetRevenue),
            AverageOrderValue = commerce.PurchaseCount > 0
                ? RoundMoney(commerce.NetRevenue / commerce.PurchaseCount)
                : 0m,
            AddToCart = eventSummary.GetCount(nameof(AnalyticsEventType.AddToCart)),
            CheckoutStarted = eventSummary.GetCount(nameof(AnalyticsEventType.BeginCheckout)),
            AbandonedCheckouts = abandonedSummary.Count,
            ConversionRate = eventSummary.Visits > 0
                ? RoundRate((decimal)commerce.PurchaseCount / eventSummary.Visits * 100m)
                : 0m,
            AbandonedRevenue = RoundMoney(abandonedSummary.Revenue),
            TopSellingProducts = topProducts,
            Funnel = BuildFunnelSummary(eventSummary.EventCounts)
        };

        return response;
    }

    private async Task<StorefrontEventSummary> GetEventSummaryAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var filter = Builders<AnalyticsEventDocument>.Filter.And(
            Builders<AnalyticsEventDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<AnalyticsEventDocument>.Filter.Gte(x => x.OccurredAtUtc, startUtc),
            Builders<AnalyticsEventDocument>.Filter.Lt(x => x.OccurredAtUtc, endUtc),
            Builders<AnalyticsEventDocument>.Filter.In(x => x.EventType, TrackedEventTypes));

        var facet = new BsonDocument("$facet", new BsonDocument
        {
            {
                "counts",
                new BsonArray
                {
                    new BsonDocument("$group", new BsonDocument
                    {
                        { "_id", "$EventType" },
                        { "count", new BsonDocument("$sum", 1) }
                    })
                }
            },
            {
                "visits",
                new BsonArray
                {
                    new BsonDocument("$match", new BsonDocument
                    {
                        { "EventType", nameof(AnalyticsEventType.PageView) },
                        {
                            "SessionId",
                            new BsonDocument
                            {
                                { "$type", "string" },
                                { "$ne", string.Empty }
                            }
                        }
                    }),
                    new BsonDocument("$group", new BsonDocument("_id", "$SessionId")),
                    new BsonDocument("$count", "count")
                }
            }
        });

        var result = await _analyticsEvents
            .Aggregate()
            .Match(filter)
            .AppendStage<BsonDocument>(facet)
            .SingleOrDefaultAsync(cancellationToken);

        var summary = new StorefrontEventSummary();
        if (result == null)
        {
            return summary;
        }

        if (result.TryGetValue("counts", out var countsValue))
        {
            foreach (var countDoc in countsValue.AsBsonArray.Select(x => x.AsBsonDocument))
            {
                var eventType = countDoc.GetValue("_id", new BsonString(string.Empty)).AsString;
                var count = countDoc.GetValue("count", new BsonInt64(0)).ToInt64();
                summary.EventCounts[eventType] = count;
            }
        }

        if (result.TryGetValue("visits", out var visitsValue) && visitsValue.AsBsonArray.Count > 0)
        {
            summary.Visits = visitsValue.AsBsonArray[0].AsBsonDocument.GetValue("count", new BsonInt64(0)).ToInt64();
        }

        return summary;
    }

    private async Task<List<StorefrontTopSellingProductDto>> GetTopSellingProductsAsync(
        Guid tenantId,
        IReadOnlyDictionary<Guid, decimal> netRevenueByOrder,
        CancellationToken cancellationToken)
    {
        var financiallyRecognizedOrderIds = netRevenueByOrder
            .Where(entry => entry.Value > 0m)
            .Select(entry => entry.Key)
            .ToArray();

        if (financiallyRecognizedOrderIds.Length == 0)
        {
            return [];
        }

        return await _dbContext.OrderItems
            .AsNoTracking()
            .Where(i =>
                i.Order.TenantId == tenantId &&
                financiallyRecognizedOrderIds.Contains(i.OrderId))
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g => new StorefrontTopSellingProductDto
            {
                ProductName = g.Key.ProductName,
                Sales = g.Sum(x => (long)x.Quantity)
            })
            .OrderByDescending(x => x.Sales)
            .ThenBy(x => x.ProductName)
            .Take(5)
            .ToListAsync(cancellationToken);
    }

    private async Task<StorefrontAbandonedCheckoutSummary> GetAbandonedCheckoutSummaryAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var summary = await _dbContext.CheckoutSessions
            .AsNoTracking()
            .Where(s =>
                s.TenantId == tenantId &&
                s.Status == CheckoutSessionStatus.Abandoned &&
                s.AbandonedAtUtc.HasValue &&
                s.AbandonedAtUtc.Value >= startUtc &&
                s.AbandonedAtUtc.Value < endUtc)
            .GroupBy(_ => 1)
            .Select(g => new StorefrontAbandonedCheckoutSummary
            {
                Count = g.LongCount(),
                Revenue = g.Sum(x => x.CartTotal ?? 0m)
            })
            .SingleOrDefaultAsync(cancellationToken);

        return summary ?? new StorefrontAbandonedCheckoutSummary();
    }

    private StorefrontFunnelSummaryDto BuildFunnelSummary(IReadOnlyDictionary<string, long> eventCounts)
    {
        var funnel = new StorefrontFunnelSummaryDto();
        var definition = _funnelService.GetFunnelDefinition("purchase_funnel");

        if (definition == null)
        {
            funnel.PageView = GetEventCount(eventCounts, nameof(AnalyticsEventType.PageView));
            funnel.ViewProduct = GetEventCount(eventCounts, nameof(AnalyticsEventType.ProductView));
            funnel.AddToCart = GetEventCount(eventCounts, nameof(AnalyticsEventType.AddToCart));
            funnel.BeginCheckout = GetEventCount(eventCounts, nameof(AnalyticsEventType.BeginCheckout));
            funnel.PurchaseCompleted = GetEventCount(eventCounts, nameof(AnalyticsEventType.PurchaseCompleted));
            return funnel;
        }

        foreach (var step in definition.Steps)
        {
            var count = GetEventCount(eventCounts, step.EventType.ToString());

            switch (step.EventType)
            {
                case AnalyticsEventType.PageView:
                    funnel.PageView = count;
                    break;
                case AnalyticsEventType.ProductView:
                    funnel.ViewProduct = count;
                    break;
                case AnalyticsEventType.AddToCart:
                    funnel.AddToCart = count;
                    break;
                case AnalyticsEventType.BeginCheckout:
                    funnel.BeginCheckout = count;
                    break;
                case AnalyticsEventType.PurchaseCompleted:
                    funnel.PurchaseCompleted = count;
                    break;
            }
        }

        return funnel;
    }

    private static (DateTime startUtc, DateTime endUtc) ResolveDateRange(string range)
    {
        var now = DateTime.UtcNow;
        var todayUtc = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
        var startOfMonthUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return range switch
        {
            "today" => (todayUtc, todayUtc.AddDays(1)),
            "7d" => (todayUtc.AddDays(-6), todayUtc.AddDays(1)),
            "30d" => (todayUtc.AddDays(-29), todayUtc.AddDays(1)),
            "this_month" => (startOfMonthUtc, todayUtc.AddDays(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(range), $"Unsupported range '{range}'")
        };
    }

    private static string NormalizeRange(string? range)
    {
        return range?.Trim().ToLowerInvariant() switch
        {
            null or "" => "7d",
            "today" => "today",
            "7d" or "last_7_days" => "7d",
            "30d" or "last_30_days" => "30d",
            "this_month" => "this_month",
            _ => throw new ArgumentOutOfRangeException(nameof(range), $"Supported values: {string.Join(", ", SupportedRanges)}")
        };
    }

    private static long GetEventCount(IReadOnlyDictionary<string, long> eventCounts, string key)
    {
        return eventCounts.TryGetValue(key, out var count) ? count : 0;
    }

    private static decimal RoundMoney(decimal value)
    {
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal RoundRate(decimal value)
    {
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private sealed class StorefrontEventSummary
    {
        public long Visits { get; set; }
        public Dictionary<string, long> EventCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public long GetCount(string eventType)
        {
            return EventCounts.TryGetValue(eventType, out var count) ? count : 0;
        }
    }

    private sealed class StorefrontAbandonedCheckoutSummary
    {
        public long Count { get; set; }
        public decimal Revenue { get; set; }
    }
}
