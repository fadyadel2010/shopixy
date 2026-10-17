using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Controllers.Admin;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Analytics;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Analytics.Attribution;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Analytics.Alerts;
using PointOfSale.Infrastructure.Analytics.Funnels;
using PointOfSale.Infrastructure.Attribution;
using PointOfSale.Infrastructure.CampaignCost;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.Infrastructure.Analytics;
using StackExchange.Redis;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Redis.Configuration;

namespace PointOfSale.Tests.Analytics;

public sealed class Phase6MarketingTests
{
    private readonly Guid tenant = Guid.NewGuid();
    private ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TestTenantContext(tenant));

    [Theory]
    [InlineData("999", 10, "Meta")]
    [InlineData("PERCENTAGE", 101, "Meta")]
    [InlineData("FIXED", -1, "Meta")]
    [InlineData("FIXED", 10, "999")]
    [InlineData("FIXED", 10, "Unknown")]
    public async Task Pixel_InvalidStrategyValueOrProvider_NeverPersists(string strategy, decimal value, string provider)
    {
        var config = new Mock<IPixelTrackingConfigService>();
        var controller = new AdminPixelTrackingController(config.Object, Mock.Of<IPixelValueResolver>(), Mock.Of<IPixelOverrideStatsService>(), Mock.Of<IProductPixelOverrideService>(), new TestTenantContext(tenant), NullLogger<AdminPixelTrackingController>.Instance)
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<BadRequestObjectResult>(await controller.UpdateConfig(new() { Strategy = strategy, Value = value, AppliesTo = [provider] }, default));
        config.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("PERCENTAGE", 0, 0)]
    [InlineData("PERCENTAGE", 50, 300)]
    [InlineData("FIXED", 10, 10)]
    public void Pixel_PreviewHonorsZeroAndSupportedArithmetic(string strategy, decimal value, decimal expected)
    {
        var controller = new AdminPixelTrackingController(Mock.Of<IPixelTrackingConfigService>(), Mock.Of<IPixelValueResolver>(), Mock.Of<IPixelOverrideStatsService>(), Mock.Of<IProductPixelOverrideService>(), new TestTenantContext(tenant), NullLogger<AdminPixelTrackingController>.Instance);
        var result = Assert.IsType<OkObjectResult>(controller.GetPreview(600, strategy, value).Result);
        Assert.Equal(expected, Assert.IsType<AdminPixelTrackingDtos.PixelValuePreviewDto>(result.Value).PixelReportedValue);
    }

    [Fact]
    public async Task Funnel_UsesPostedPurchaseAuthority_NotRawPurchaseEvents_AndDoesNotInventMonotonicity()
    {
        var metrics = new Mock<IAnalyticsMetricReader>();
        metrics.Setup(m => m.GetCounterValueAsync(tenant, "begincheckout", 60, default)).ReturnsAsync(2);
        metrics.Setup(m => m.GetCounterValueAsync(tenant, "purchasecompleted", 60, default)).ReturnsAsync(999);
        metrics.Setup(m => m.GetMetricValueAsync(tenant, AnalyticsMetric.PurchaseCompleted, 60, default)).ReturnsAsync(3);
        var service = new AnalyticsFunnelService(metrics.Object, Mock.Of<IAnalyticsHotStoreService>(), Mock.Of<IProductReadRepository>(), NullLogger<AnalyticsFunnelService>.Instance);
        var result = (await service.ComputeFunnelAsync(tenant, "checkout_funnel"))!;
        Assert.Equal(2, result.EntryCount); Assert.Equal(3, result.CompletionCount); Assert.Equal(150, result.OverallConversionRate);
        metrics.Verify(m => m.GetCounterValueAsync(tenant, "purchasecompleted", 60, default), Times.Never);
    }

    [Fact]
    public async Task Funnel_DependencyFailureIsNotSuccessfulEmptyMetrics()
    {
        var metrics = new Mock<IAnalyticsMetricReader>();
        metrics.Setup(m => m.GetCounterValueAsync(tenant, "begincheckout", 60, default)).ThrowsAsync(new IOException("unavailable"));
        var service = new AnalyticsFunnelService(metrics.Object, Mock.Of<IAnalyticsHotStoreService>(), Mock.Of<IProductReadRepository>(), NullLogger<AnalyticsFunnelService>.Instance);
        await Assert.ThrowsAsync<IOException>(() => service.ComputeFunnelAsync(tenant, "checkout_funnel"));
    }

    private static CreateCampaignCostRequest Cost(string campaign = "p6qa") => new() { Source = " QA ", Medium = "CPC", Campaign = campaign, CostAmount = 10, Currency = "EGP", PeriodStartUtc = DateTime.UtcNow.Date, PeriodEndUtc = DateTime.UtcNow.Date.AddDays(1) };

    [Fact]
    public async Task Cost_ReplacementRetainsHistoryAndCannotOverwriteAnotherCampaign()
    {
        await using var db = Db();
        var service = new CampaignCostService(db, NullLogger<CampaignCostService>.Instance);
        var original = await service.CreateOrUpdateAsync(tenant, Cost());
        var other = await service.CreateOrUpdateAsync(tenant, Cost("other"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReplaceAsync(tenant, original.CampaignCostId, Cost("other")));
        Assert.False(original.IsDeleted); Assert.Equal(10, other.CostAmount);
        var next = Cost("edited"); next.CostAmount = 0;
        var saved = (await service.ReplaceAsync(tenant, original.CampaignCostId, next, original.UpdatedAtUtc))!;
        Assert.NotEqual(original.CampaignCostId, saved.CampaignCostId); Assert.True(original.IsDeleted); Assert.Equal(0, saved.CostAmount);
        Assert.Equal("qa", saved.Source); Assert.Equal("cpc", saved.Medium);
        Assert.Null(await service.GetByIdAsync(Guid.NewGuid(), saved.CampaignCostId));
    }

    [Fact]
    public async Task Cost_StaleRevisionAndFractionalMinorUnitsRejected_AdjacentPeriodsExcluded()
    {
        await using var db = Db();
        var service = new CampaignCostService(db, NullLogger<CampaignCostService>.Instance);
        var saved = await service.CreateOrUpdateAsync(tenant, Cost());
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.ReplaceAsync(tenant, saved.CampaignCostId, Cost(), saved.UpdatedAtUtc.AddSeconds(-1)));
        var invalid = Cost(); invalid.CostAmount = 1.001m;
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateOrUpdateAsync(tenant, invalid));
        Assert.Empty(await service.GetByPeriodAsync(tenant, saved.PeriodEndUtc, saved.PeriodEndUtc.AddDays(1)));
        Assert.Empty(await service.GetByPeriodAsync(tenant, saved.PeriodStartUtc.AddDays(-1), saved.PeriodStartUtc));
    }

    [Fact]
    public async Task Attribution_DeliveryOrderAndPostConversionVisitsCannotChangeFirstLastTouch()
    {
        var at = DateTime.UtcNow.AddHours(-1);
        var contexts = new[] { Touch("second", at.AddMinutes(-10)), Touch("future", at.AddMinutes(1)), Touch("first", at.AddMinutes(-20)) };
        var store = new Mock<IAttributionContextStore>();
        store.Setup(s => s.GetAllContextsForSessionAsync(tenant, "p6-session", default)).ReturnsAsync(contexts.ToList());
        var resolver = new OrderAttributionResolver(store.Object, Mock.Of<IOrderAttributionStore>(), NullLogger<OrderAttributionResolver>.Instance);
        var purchase = AnalyticsEvent.Create(tenant, AnalyticsEventType.PurchaseCompleted);
        purchase.OccurredAtUtc = at; purchase.Actor.SessionId = "p6-session"; purchase.Context.Currency = "EGP"; purchase.Payload.OrderId = Guid.NewGuid(); purchase.Payload.Value = 100;
        var first = (await resolver.ResolveWithoutStoringAsync(purchase, AttributionModel.FirstTouch))!;
        var last = (await resolver.ResolveWithoutStoringAsync(purchase, AttributionModel.LastTouch))!;
        Assert.Equal("first", first.Source); Assert.Equal("second", last.Source);
        Assert.Equal(at, last.AttributedAtUtc); Assert.Equal(TimeSpan.FromMinutes(10), last.TimeToConversion);
    }

    private AttributionContext Touch(string source, DateTime at) => new() { TenantId = tenant, SessionId = "p6-session", Source = source, Medium = "cpc", CapturedAtUtc = at };

    [Fact]
    public async Task Roi_CampaignEndpointSuppliesOverallRoiAndCpa_ForActualCards()
    {
        var roi = new Mock<ICampaignRoiService>();
        roi.Setup(r => r.CalculateAsync(tenant, It.IsAny<DateTime>(), It.IsAny<DateTime>(), AttributionModel.LastTouch, default))
            .ReturnsAsync([new() { Revenue = 100, Cost = 40, Profit = 60, Orders = 2 }]);
        var controller = new AdminCampaignCostController(Mock.Of<ICampaignCostService>(), roi.Object, new TestTenantContext(tenant), Mock.Of<IAnalyticsTenantCurrencyResolver>());
        var response = Assert.IsType<CampaignCostRoiResponse>(Assert.IsType<OkObjectResult>(await controller.GetRoiByCampaign("today", null, null)).Value);
        Assert.Equal(150m, response.OverallRoi); Assert.Equal(20m, response.CostPerAcquisition);
    }

    [Fact]
    public async Task Roi_IncompatibleCurrencyIsUnavailable_NotInflatedBySilentlyDiscardedSpend()
    {
        await using var db = Db(); db.Tenants.Add(new Tenant { Id = tenant, Name = "P6QA", Slug = "p6qa", Currency = "EGP" }); await db.SaveChangesAsync();
        var costs = new Mock<ICampaignCostService>();
        costs.Setup(c => c.GetByPeriodAsync(tenant, It.IsAny<DateTime>(), It.IsAny<DateTime>(), default)).ReturnsAsync([new() { Currency = "USD", CostAmount = 100 }]);
        var attribution = new Mock<IOrderAttributionStore>();
        attribution.Setup(a => a.GetRevenueByCampaignAsync(tenant, It.IsAny<DateTime>(), It.IsAny<DateTime>(), AttributionModel.LastTouch, default)).ReturnsAsync([]);
        var service = new CampaignRoiService(costs.Object, attribution.Object, db, NullLogger<CampaignRoiService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CalculateAsync(tenant, DateTime.UtcNow.Date, DateTime.UtcNow, AttributionModel.LastTouch));
    }

    [Fact]
    public async Task Pixel_ColdBackgroundScopeReadsAndUpdatesOnlyExplicitTenant_NotAnEmptyScopedDefault()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var services = new ServiceCollection().AddScoped(_ => new ApplicationDbContext(options)).BuildServiceProvider();
        using (var scope = services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.PixelTrackingConfigs.Add(new() { TenantId = tenant, IsEnabled = true, Strategy = PixelOverrideStrategy.Percentage, Value = 50, AppliesToJson = "[2]" });
            await db.SaveChangesAsync();
        }
        var redis = new Mock<IConnectionMultiplexer>(); redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(Mock.Of<IDatabase>());
        var service = new PixelTrackingConfigService(services.GetRequiredService<IServiceScopeFactory>(), redis.Object, NullLogger<PixelTrackingConfigService>.Instance);
        var cold = await service.GetConfigAsync(tenant); Assert.True(cold.IsEnabled); Assert.Equal(50, cold.Value); Assert.Contains(PixelProvider.Meta, cold.AppliesTo);
        cold.Value = 25; await service.UpdateConfigAsync(cold);
        Assert.Equal(25, (await service.GetConfigAsync(tenant)).Value);
        Assert.False((await service.GetConfigAsync(Guid.NewGuid())).IsEnabled);
        using var fresh = services.CreateScope();
        Assert.Single(await fresh.ServiceProvider.GetRequiredService<ApplicationDbContext>().PixelTrackingConfigs.IgnoreQueryFilters().ToListAsync());
    }

    [Theory]
    [InlineData("last_7_days", 7)]
    [InlineData("last_30_days", 30)]
    public async Task ReportingPresets_AreExactlyNamedUtcCalendarDays(string period, int days)
    {
        var attribution = new Mock<IOrderAttributionStore>();
        attribution.Setup(a => a.GetRevenueBySourceAsync(tenant, It.IsAny<DateTime>(), It.IsAny<DateTime>(), AttributionModel.LastTouch, default)).ReturnsAsync([]);
        var controller = new AdminAnalyticsAttributionController(attribution.Object, Mock.Of<IAttributionContextStore>(), new TestTenantContext(tenant));
        var response = Assert.IsType<RevenueSummaryResponse>(Assert.IsType<OkObjectResult>(await controller.GetRevenueSummary(period, null, null)).Value);
        Assert.Equal(days, (response.EndDate - response.StartDate).TotalDays);
        Assert.Equal(DateTimeKind.Utc, response.StartDate.Kind);
    }

    [Fact]
    public async Task MixedCostCurrencies_DoNotProduceAnInventedCombinedTotal()
    {
        var costs = new Mock<ICampaignCostService>();
        costs.Setup(s => s.GetByPeriodAsync(tenant, It.IsAny<DateTime>(), It.IsAny<DateTime>(), default)).ReturnsAsync([new() { CostAmount = 10, Currency = "EGP" }, new() { CostAmount = 20, Currency = "USD" }]);
        var currency = new Mock<IAnalyticsTenantCurrencyResolver>();
        currency.Setup(c => c.ResolveAsync(tenant, It.IsAny<IEnumerable<Guid?>>(), default)).ReturnsAsync(new AnalyticsCurrencyResolution("EGP", new Dictionary<Guid, string?>()));
        var controller = new AdminCampaignCostController(costs.Object, Mock.Of<ICampaignRoiService>(), new TestTenantContext(tenant), currency.Object);
        var response = Assert.IsType<CampaignCostsResponse>(Assert.IsType<OkObjectResult>(await controller.GetCampaignCosts("today", null, null)).Value);
        Assert.Null(response.TotalCost); Assert.Equal(2, response.Costs.Count);
    }

    [Fact]
    public async Task UnknownAttributionModelIsRejectedInsteadOfAValidLookingEmptyReport()
    {
        var controller = new AdminAnalyticsAttributionController(Mock.Of<IOrderAttributionStore>(), Mock.Of<IAttributionContextStore>(), new TestTenantContext(tenant));
        Assert.IsType<BadRequestObjectResult>(await controller.GetRevenueSummary("today", null, null, (AttributionModel)999));
    }

    [Fact]
    public async Task Roi_UtmDelimitersCannotMergeDistinctSourcesOrCampaigns()
    {
        await using var db = Db(); db.Tenants.Add(new Tenant { Id = tenant, Name = "P6QA", Slug = "p6qa-keys", Currency = "EGP" }); await db.SaveChangesAsync();
        var costs = new Mock<ICampaignCostService>();
        costs.Setup(c => c.GetByPeriodAsync(tenant, It.IsAny<DateTime>(), It.IsAny<DateTime>(), default)).ReturnsAsync([
            new() { Source = "x|y", Medium = "z", Campaign = "c", Currency = "EGP", CostAmount = 10 },
            new() { Source = "x", Medium = "y|z", Campaign = "c", Currency = "EGP", CostAmount = 20 }]);
        var attribution = new Mock<IOrderAttributionStore>();
        attribution.Setup(a => a.GetRevenueByCampaignAsync(tenant, It.IsAny<DateTime>(), It.IsAny<DateTime>(), AttributionModel.LastTouch, default)).ReturnsAsync([]);
        attribution.Setup(a => a.GetRevenueBySourceAsync(tenant, It.IsAny<DateTime>(), It.IsAny<DateTime>(), AttributionModel.LastTouch, default)).ReturnsAsync([]);
        var service = new CampaignRoiService(costs.Object, attribution.Object, db, NullLogger<CampaignRoiService>.Instance);
        Assert.Equal(2, (await service.CalculateAsync(tenant, DateTime.UtcNow.Date, DateTime.UtcNow, AttributionModel.LastTouch)).Count);
        var sources = await service.GetRoiBySourceAsync(tenant, DateTime.UtcNow.Date, DateTime.UtcNow, AttributionModel.LastTouch);
        Assert.Contains(sources, s => s.Source == "x|y" && s.Medium == "z" && s.TotalCost == 10);
        Assert.Contains(sources, s => s.Source == "x" && s.Medium == "y|z" && s.TotalCost == 20);
    }

    [Fact]
    public async Task Attribution_AuthorityFailureIsNotReportedAsZeroRevenue()
    {
        var authority = new Mock<IStorefrontAnalyticsAuthorityReader>();
        authority.Setup(a => a.GetCommerceSnapshotAsync(tenant, It.IsAny<DateTime>(), It.IsAny<DateTime>(), default)).ThrowsAsync(new IOException("SQL unavailable"));
        var database = new Mock<IMongoDatabase>();
        database.Setup(d => d.GetCollection<OrderAttributionDocument>("order_attributions", null)).Returns(Mock.Of<IMongoCollection<OrderAttributionDocument>>());
        var store = new OrderAttributionStore(Mock.Of<IConnectionMultiplexer>(), database.Object, authority.Object, Options.Create(new RedisSettings()), NullLogger<OrderAttributionStore>.Instance);
        await Assert.ThrowsAsync<IOException>(() => store.GetRevenueBySourceAsync(tenant, DateTime.UtcNow.Date, DateTime.UtcNow, AttributionModel.LastTouch));
    }

    [Theory]
    [InlineData("https://qa.example/previous", false)]
    [InlineData("https://www.google.com/search", true)]
    public async Task Attribution_InternalNavigationDoesNotStealCampaignCredit_ExternalReferralsStillWork(string referrer, bool external)
    {
        var contexts = new Mock<IAttributionContextStore>();
        contexts.Setup(s => s.GetFirstTouchContextAsync(tenant, "p6-session", default)).ReturnsAsync(Touch("p6qa", DateTime.UtcNow.AddMinutes(-10)));
        var resolver = new AttributionContextResolver(contexts.Object, NullLogger<AttributionContextResolver>.Instance);
        var page = AnalyticsEvent.Create(tenant, AnalyticsEventType.PageView); page.Actor.SessionId = "p6-session";
        page.Context.PageUrl = "https://qa.example/products"; page.Context.Referrer = referrer;
        var result = await resolver.ResolveFromEventAsync(page);
        if (external) { Assert.Equal("google", result!.Source); Assert.Equal("organic", result.Medium); }
        else { Assert.Null(result); contexts.Verify(s => s.StoreContextAsync(It.IsAny<AttributionContext>(), It.IsAny<CancellationToken>()), Times.Never); }
    }
}
