using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontAnalyticsAuthorityCertificationTests
{
    [Fact]
    public void FinancialMetrics_MustDeriveFromPostedTenantCommerceAndLedgerAuthority()
    {
        var reader = ReadRepoFile("PointOfSale.Infrastructure/Analytics/StorefrontAnalyticsAuthorityReader.cs");

        Assert.Contains("order.TenantId == tenantId", reader, StringComparison.Ordinal);
        Assert.Contains("order.Source == OrderSource.Ecommerce", reader, StringComparison.Ordinal);
        Assert.Contains("entry.JournalEntry.Status == JournalStatus.Posted", reader, StringComparison.Ordinal);
        Assert.Contains("StorefrontRevenueSourceTypes.Contains(entry.JournalEntry.SourceType)", reader, StringComparison.Ordinal);
        Assert.Contains("entry.Account.AccountCode == AccountCodes.EcommerceSales", reader, StringComparison.Ordinal);
        Assert.Contains("entry.CreditAmount > 0m", reader, StringComparison.Ordinal);
        Assert.Contains("entry.CreditAmount - entry.DebitAmount", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("order.Total", reader, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontDashboard_MustNotPromoteCacheOrPurchaseProjectionToAuthority()
    {
        var dashboard = ReadRepoFile("PointOfSale.API/Services/Analytics/StorefrontAnalyticsDashboardService.cs");

        Assert.Contains("_authorityReader.GetCommerceSnapshotAsync", dashboard, StringComparison.Ordinal);
        Assert.Contains("commerce.NetRevenue", dashboard, StringComparison.Ordinal);
        Assert.Contains("commerce.PurchaseCount", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("IDistributedCache", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetCachedAsync", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(AnalyticsEventType.PurchaseCompleted)\n    ];", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("o.CreatedAt >= startUtc", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("g.Sum(x => x.Total)", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public void RealtimeAndFunnelPurchases_MustNotReadRedisPurchaseCounters()
    {
        var realtime = ReadRepoFile("PointOfSale.API/Services/Analytics/AnalyticsRealtimeService.cs");
        var metricReader = ReadRepoFile("PointOfSale.Infrastructure/Analytics/Alerts/AnalyticsMetricReader.cs");

        Assert.Contains("_authorityReader.GetCommerceSnapshotAsync", realtime, StringComparison.Ordinal);
        Assert.Contains("eventType == AnalyticsEventType.PurchaseCompleted", realtime, StringComparison.Ordinal);
        Assert.DoesNotContain("GetCounterValueAsync(tenantId, \"purchasecompleted\"", realtime, StringComparison.Ordinal);
        Assert.Contains("GetPurchaseCountAsync", metricReader, StringComparison.Ordinal);
        Assert.DoesNotContain("AnalyticsMetric.PurchaseCompleted => await GetCounterValueAsync", metricReader, StringComparison.Ordinal);
    }

    [Fact]
    public void AttributionRevenue_MustOverlayMongoDimensionsWithSqlAuthority()
    {
        var store = ReadRepoFile("PointOfSale.Infrastructure/Attribution/OrderAttributionStore.cs");

        Assert.Contains("_authorityReader.GetCommerceSnapshotAsync", store, StringComparison.Ordinal);
        Assert.Contains("_authorityReader.GetNetRevenueByOrderAsync", store, StringComparison.Ordinal);
        Assert.Contains("attribution.OrderValue = commerce.NetRevenueByOrder", store, StringComparison.Ordinal);
        Assert.DoesNotContain("g.Sum(x => x.OrderValue)", store, StringComparison.Ordinal);
    }

    [Fact]
    public void EventConsumer_MustFenceReplayDurablyBeforeProjectionMutation()
    {
        var consumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/AnalyticsEventConsumer.cs");
        var durableInsert = consumer.IndexOf("_coldStore.StoreEventWithOutcomeAsync", StringComparison.Ordinal);
        var redisProjection = consumer.IndexOf("_hotStore.StoreEventAsync", StringComparison.Ordinal);

        Assert.True(durableInsert >= 0);
        Assert.True(redisProjection > durableInsert);
        Assert.Contains("AnalyticsEventStoreOutcome.AlreadyExists", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontSdk_MustNotExposeOrEmitPurchaseCompleted()
    {
        var hook = ReadRepoFile("PointOfSale.Storefront/src/analytics/hooks/useAnalytics.ts");
        var checkoutSuccess = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/CheckoutSuccess.tsx");
        var purchaseConsumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/AuthoritativePurchaseAnalyticsConsumer.cs");

        Assert.DoesNotContain("purchaseCompleted", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("AnalyticsEventType.PurchaseCompleted", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("purchaseCompleted", checkoutSuccess, StringComparison.Ordinal);
        Assert.DoesNotContain("AnalyticsEventType.PurchaseCompleted", checkoutSuccess, StringComparison.Ordinal);
        Assert.Contains("AnalyticsEventType.PurchaseCompleted", purchaseConsumer, StringComparison.Ordinal);
        Assert.Contains("OrderPaidEvent", purchaseConsumer, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyticsInputs_MustPreserveSafeUtmEvidenceAndBoundHistoricalQueries()
    {
        var validator = ReadRepoFile("PointOfSale.API/Services/Analytics/AnalyticsEventValidator.cs");
        var attribution = ReadRepoFile("PointOfSale.API/Controllers/AdminAnalyticsAttributionController.cs");
        var campaign = ReadRepoFile("PointOfSale.API/Controllers/AdminCampaignCostController.cs");

        Assert.Contains("ExtractUtmParameters", validator, StringComparison.Ordinal);
        Assert.Contains("UtmSource", validator, StringComparison.Ordinal);
        Assert.Contains("GetLeftPart(UriPartial.Path)", validator, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(take, 1, 200)", attribution, StringComparison.Ordinal);
        Assert.Contains("endUtc.AddDays(-366)", attribution, StringComparison.Ordinal);
        Assert.Contains("endUtc.AddDays(-366)", campaign, StringComparison.Ordinal);
        Assert.Contains("Campaign cost currency must match tenant currency", campaign, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyticsExports_MustNotExposeAnIndependentStorefrontAuthorityPath()
    {
        var controllers = ReadRepoFiles("PointOfSale.API/Controllers", "*.cs");
        var analyticsControllers = string.Join(
            Environment.NewLine,
            controllers.Where(file => file.path.Contains("Analytics", StringComparison.OrdinalIgnoreCase))
                .Select(file => file.content));

        Assert.DoesNotContain("text/csv", analyticsControllers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("application/vnd.openxmlformats", analyticsControllers, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderRuntime_MustExposeSharedDeliveryProvidersThroughGenericInfrastructure()
    {
        var providerEnum = ReadRepoFile("PointOfSale.Core/Analytics/AnalyticsProvider.cs");
        var dispatchers = ReadRepoFiles("PointOfSale.Infrastructure/Analytics/Dispatchers", "*.cs")
            .Select(file => file.path)
            .ToArray();

        Assert.Contains("GoogleAnalytics = 1", providerEnum, StringComparison.Ordinal);
        Assert.Contains("Meta = 2", providerEnum, StringComparison.Ordinal);
        Assert.Contains("AdminRealtime = 3", providerEnum, StringComparison.Ordinal);
        Assert.Contains("TikTok = 4", providerEnum, StringComparison.Ordinal);
        Assert.DoesNotContain("Snapchat", providerEnum, StringComparison.Ordinal);
        Assert.DoesNotContain("LinkedIn", providerEnum, StringComparison.Ordinal);
        Assert.DoesNotContain("Pinterest", providerEnum, StringComparison.Ordinal);

        Assert.Contains(dispatchers, path => path.EndsWith("GoogleAnalyticsDispatcher.cs", StringComparison.Ordinal));
        Assert.Contains(dispatchers, path => path.EndsWith("MetaConversionsApiDispatcher.cs", StringComparison.Ordinal));
        Assert.Contains(dispatchers, path => path.EndsWith("TikTokAnalyticsDispatcher.cs", StringComparison.Ordinal));
        Assert.Contains(dispatchers, path => path.EndsWith("TikTokHttpRequestCustomizer.cs", StringComparison.Ordinal));
        Assert.Contains(dispatchers, path => path.EndsWith("AdminRealtimeDispatcher.cs", StringComparison.Ordinal));
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = Path.Combine(TestConfiguration.GetSolutionRoot(), relativePath);

        return File.ReadAllText(path);
    }

    private static IReadOnlyList<(string path, string content)> ReadRepoFiles(
        string relativeDirectory,
        string pattern)
    {
        var directory = Path.Combine(TestConfiguration.GetSolutionRoot(), relativeDirectory);

        return Directory.GetFiles(directory, pattern)
            .Select(path => (path, File.ReadAllText(path)))
            .ToList();
    }
}
