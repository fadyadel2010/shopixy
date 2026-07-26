using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.Core.Analytics.Attribution;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Attribution;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.API.Services.Analytics;

public class AnalyticsSessionJourneyService : IAnalyticsSessionJourneyService
{
    private const int JourneyLookbackHours = 48;
    private const int MaxVisitedPages = 20;
    private static readonly TimeSpan SessionGapThreshold = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan JourneyCacheDuration = TimeSpan.FromSeconds(30);

    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoCollection<AnalyticsEventDocument> _events;
    private readonly IAttributionContextStore _contextStore;
    private readonly IOrderAttributionStore _orderAttributionStore;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AnalyticsSessionJourneyService> _logger;

    public AnalyticsSessionJourneyService(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        IAttributionContextStore contextStore,
        IOrderAttributionStore orderAttributionStore,
        IMemoryCache cache,
        ILogger<AnalyticsSessionJourneyService> logger)
    {
        _dbContext = dbContext;
        _events = mongoDatabase.GetCollection<AnalyticsEventDocument>("analytics_events");
        _contextStore = contextStore;
        _orderAttributionStore = orderAttributionStore;
        _cache = cache;
        _logger = logger;

        EnsureIndexes();
    }

    public async Task<AnalyticsSessionJourneyResponse?> GetJourneyAsync(
        Guid tenantId,
        Guid orderId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = sessionId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSessionId))
        {
            return null;
        }

        var cacheKey = BuildCacheKey(tenantId, orderId);
        if (_cache.TryGetValue<AnalyticsSessionJourneyResponse>(cacheKey, out var cachedJourney))
        {
            if (string.Equals(cachedJourney?.SessionId, normalizedSessionId, StringComparison.Ordinal))
            {
                return cachedJourney;
            }

            return null;
        }

        var order = await _dbContext.Orders
            .AsNoTracking()
            .Where(o =>
                o.Id == orderId &&
                o.TenantId == tenantId &&
                !o.IsDeleted &&
                o.FinancialPostingStatus == FinancialPostingStatus.Posted &&
                (o.PaymentStatus == PaymentStatus.Collected ||
                 o.PaymentStatus == PaymentStatus.Settled ||
                 o.PaymentStatus == PaymentStatus.PartiallyRefunded ||
                 o.PaymentStatus == PaymentStatus.Refunded))
            .Select(o => new { o.Id, o.SessionId })
            .FirstOrDefaultAsync(cancellationToken);

        if (order == null)
        {
            return null;
        }

        var orderAttribution = await _orderAttributionStore.GetAttributionAsync(
            tenantId,
            orderId,
            AttributionModel.LastTouch,
            cancellationToken);

        var effectiveSessionId = !string.IsNullOrWhiteSpace(order.SessionId)
            ? order.SessionId.Trim()
            : orderAttribution?.SessionId?.Trim();

        if (string.IsNullOrWhiteSpace(effectiveSessionId) ||
            !string.Equals(effectiveSessionId, normalizedSessionId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "[Analytics Journey] Session mismatch | TenantId: {TenantId} | OrderId: {OrderId} | RequestedSessionId: {RequestedSessionId} | EffectiveSessionId: {EffectiveSessionId}",
                tenantId, orderId, normalizedSessionId, effectiveSessionId);
            return null;
        }

        var purchaseFilter = Builders<AnalyticsEventDocument>.Filter.And(
            Builders<AnalyticsEventDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<AnalyticsEventDocument>.Filter.Eq(x => x.SessionId, normalizedSessionId),
            Builders<AnalyticsEventDocument>.Filter.Eq(x => x.EventType, "PurchaseCompleted"),
            Builders<AnalyticsEventDocument>.Filter.Eq(x => x.OrderId, orderId));

        var purchaseEvent = await _events
            .Find(purchaseFilter)
            .Sort(Builders<AnalyticsEventDocument>.Sort.Descending(x => x.OccurredAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        if (purchaseEvent == null)
        {
            _logger.LogInformation(
                "[Analytics Journey] Purchase event not found | TenantId: {TenantId} | OrderId: {OrderId} | SessionId: {SessionId}",
                tenantId, orderId, normalizedSessionId);
            return null;
        }

        var purchaseCompletedAtUtc = purchaseEvent.OccurredAtUtc;
        var windowStartUtc = purchaseCompletedAtUtc.AddHours(-JourneyLookbackHours);

        var windowFilter = Builders<AnalyticsEventDocument>.Filter.And(
            Builders<AnalyticsEventDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<AnalyticsEventDocument>.Filter.Eq(x => x.SessionId, normalizedSessionId),
            Builders<AnalyticsEventDocument>.Filter.Gte(x => x.OccurredAtUtc, windowStartUtc),
            Builders<AnalyticsEventDocument>.Filter.Lte(x => x.OccurredAtUtc, purchaseCompletedAtUtc));

        var windowEvents = await _events
            .Find(windowFilter)
            .Sort(Builders<AnalyticsEventDocument>.Sort.Ascending(x => x.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        if (windowEvents.Count == 0)
        {
            windowEvents.Add(purchaseEvent);
        }

        var purchaseIndex = FindPurchaseEventIndex(windowEvents, purchaseEvent);
        var journeyStartIndex = FindJourneyStartIndex(windowEvents, purchaseIndex);
        var journeyEvents = windowEvents
            .Skip(journeyStartIndex)
            .Take(purchaseIndex - journeyStartIndex + 1)
            .ToList();

        var firstVisitAtUtc = journeyEvents.First().OccurredAtUtc;
        var checkoutVisitAtUtc = ResolveCheckoutVisitAtUtc(journeyEvents);
        var visitedPages = BuildVisitedPages(journeyEvents);
        var sessionsCount = CountLogicalSessions(windowEvents.Take(purchaseIndex + 1));
        var sourceContext = await ResolveSourceContextAsync(
            tenantId,
            normalizedSessionId,
            purchaseCompletedAtUtc,
            orderAttribution,
            cancellationToken);

        var response = new AnalyticsSessionJourneyResponse
        {
            SessionId = normalizedSessionId,
            OrderId = orderId,
            Source = sourceContext?.Source ?? "direct",
            Medium = sourceContext?.Medium,
            Device = ResolveDevice(journeyEvents, purchaseEvent),
            VisitedPages = visitedPages,
            DurationSeconds = Math.Max(0, (int)(purchaseCompletedAtUtc - firstVisitAtUtc).TotalSeconds),
            FirstVisitAtUtc = firstVisitAtUtc,
            CheckoutVisitAtUtc = checkoutVisitAtUtc,
            PurchaseCompletedAtUtc = purchaseCompletedAtUtc,
            SessionsCount = sessionsCount,
            EventCount = journeyEvents.Count
        };

        _cache.Set(cacheKey, response, JourneyCacheDuration);

        return response;
    }

    private void EnsureIndexes()
    {
        try
        {
            var indexKeys = Builders<AnalyticsEventDocument>.IndexKeys;
            var journeyIndex = new CreateIndexModel<AnalyticsEventDocument>(
                indexKeys.Ascending(x => x.TenantId)
                    .Ascending(x => x.SessionId)
                    .Ascending(x => x.OccurredAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_SessionId_OccurredAtUtc_Asc" });

            _events.Indexes.CreateOne(journeyIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Analytics Journey] Failed to ensure analytics_events journey index");
        }
    }

    private static int FindPurchaseEventIndex(
        IReadOnlyList<AnalyticsEventDocument> events,
        AnalyticsEventDocument purchaseEvent)
    {
        for (var index = events.Count - 1; index >= 0; index--)
        {
            var candidate = events[index];
            if (candidate.OrderId == purchaseEvent.OrderId &&
                candidate.OccurredAtUtc == purchaseEvent.OccurredAtUtc &&
                string.Equals(candidate.EventType, purchaseEvent.EventType, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return events.Count - 1;
    }

    private static int FindJourneyStartIndex(IReadOnlyList<AnalyticsEventDocument> events, int purchaseIndex)
    {
        var startIndex = 0;
        for (var index = purchaseIndex; index > 0; index--)
        {
            var currentEvent = events[index];
            var previousEvent = events[index - 1];

            if (currentEvent.OccurredAtUtc - previousEvent.OccurredAtUtc > SessionGapThreshold)
            {
                startIndex = index;
                break;
            }
        }

        return startIndex;
    }

    private static DateTime? ResolveCheckoutVisitAtUtc(IEnumerable<AnalyticsEventDocument> journeyEvents)
    {
        var beginCheckout = journeyEvents
            .FirstOrDefault(e => string.Equals(e.EventType, "BeginCheckout", StringComparison.Ordinal));

        if (beginCheckout != null)
        {
            return beginCheckout.OccurredAtUtc;
        }

        var checkoutPage = journeyEvents.FirstOrDefault(e =>
            NormalizePagePath(e.PageUrl)?.Contains("/checkout", StringComparison.OrdinalIgnoreCase) == true);

        return checkoutPage?.OccurredAtUtc;
    }

    private static List<string> BuildVisitedPages(IReadOnlyList<AnalyticsEventDocument> journeyEvents)
    {
        var pageEvents = journeyEvents
            .Where(e => string.Equals(e.EventType, "PageView", StringComparison.Ordinal))
            .ToList();

        var sourceEvents = pageEvents.Count > 0
            ? pageEvents
            : journeyEvents.Where(e => !string.IsNullOrWhiteSpace(e.PageUrl)).ToList();

        var visitedPages = new List<string>();
        string? previousPath = null;

        foreach (var evt in sourceEvents)
        {
            var normalizedPath = NormalizePagePath(evt.PageUrl);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                continue;
            }

            if (string.Equals(previousPath, normalizedPath, StringComparison.Ordinal))
            {
                continue;
            }

            visitedPages.Add(normalizedPath);
            previousPath = normalizedPath;
        }

        return visitedPages.TakeLast(MaxVisitedPages).ToList();
    }

    private static string ResolveDevice(
        IEnumerable<AnalyticsEventDocument> journeyEvents,
        AnalyticsEventDocument purchaseEvent)
    {
        return journeyEvents
            .Select(e => e.DeviceType)
            .LastOrDefault(d => !string.IsNullOrWhiteSpace(d))
            ?? purchaseEvent.DeviceType
            ?? "unknown";
    }

    private async Task<AttributionContext?> ResolveSourceContextAsync(
        Guid tenantId,
        string sessionId,
        DateTime purchaseCompletedAtUtc,
        OrderAttribution? orderAttribution,
        CancellationToken cancellationToken)
    {
        if (orderAttribution != null)
        {
            return new AttributionContext
            {
                TenantId = tenantId,
                SessionId = sessionId,
                Source = orderAttribution.Source,
                Medium = orderAttribution.Medium,
                Campaign = orderAttribution.Campaign,
                Content = orderAttribution.Content,
                Term = orderAttribution.Term,
                CapturedAtUtc = orderAttribution.AttributedAtUtc,
                CustomerId = orderAttribution.CustomerId
            };
        }

        var contexts = await _contextStore.GetAllContextsForSessionAsync(tenantId, sessionId, cancellationToken);
        return contexts
            .Where(c => c.CapturedAtUtc <= purchaseCompletedAtUtc)
            .OrderBy(c => c.CapturedAtUtc)
            .LastOrDefault();
    }

    private static int CountLogicalSessions(IEnumerable<AnalyticsEventDocument> events)
    {
        AnalyticsEventDocument? previousEvent = null;
        var sessions = 0;

        foreach (var evt in events)
        {
            if (previousEvent == null || evt.OccurredAtUtc - previousEvent.OccurredAtUtc > SessionGapThreshold)
            {
                sessions++;
            }

            previousEvent = evt;
        }

        return sessions;
    }

    private static string? NormalizePagePath(string? pageUrl)
    {
        if (string.IsNullOrWhiteSpace(pageUrl))
        {
            return null;
        }

        if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var absoluteUri))
        {
            return string.IsNullOrWhiteSpace(absoluteUri.AbsolutePath) ? "/" : absoluteUri.AbsolutePath;
        }

        var path = pageUrl.Split('?', '#')[0].Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        return path.StartsWith('/') ? path : $"/{path}";
    }

    private static string BuildCacheKey(Guid tenantId, Guid orderId)
    {
        return $"journey:{tenantId}:{orderId}";
    }
}
