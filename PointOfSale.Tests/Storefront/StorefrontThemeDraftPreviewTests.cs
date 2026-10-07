using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Storefront;

public sealed class StorefrontThemeDraftPreviewTests
{
    [Fact]
    public async Task Preview_ReusesDraftHomepageThemeOverridesAndAtomicNavigation_WithoutChangingLiveAuthority()
    {
        var tenantId = Guid.NewGuid();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TestTenantContext(tenantId));
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "Preview", Currency = "EGP", Country = "EG", Status = TenantStatus.Active, IsActive = true });
        var config = new StoreConfig
        {
            Id = Guid.NewGuid(), TenantId = tenantId, StoreName = "Preview", LiveVersion = 7,
            ThemeDraftOverridesJson = "{\"colors\":{\"primary\":\"#112233\"}}",
            ThemeLiveOverridesJson = "{\"colors\":{\"primary\":\"#445566\"}}",
            CustomizationJson = """
            {"storefrontTheme":"pearl","pages":{"home":{
              "draft":{"sections":[{"id":"draft","type":"hero-banner","order":2,"enabled":false,"settings":{"title":"Draft","subtitle":"","ctaText":"Shop","ctaLink":"/products","backgroundImage":"https://example.test/draft.png","backgroundColor":"#111827","overlayOpacity":40,"textColor":"#ffffff","alignment":"left","height":"large","highlights":[]}}]},
              "live":{"sections":[{"id":"live","type":"hero-banner","order":0,"enabled":true,"settings":{"title":"Live","subtitle":"","ctaText":"Shop","ctaLink":"/products","backgroundImage":"https://example.test/live.png","backgroundColor":"#111827","overlayOpacity":40,"textColor":"#ffffff","alignment":"left","height":"large","highlights":[]}}]}}},
            "navigation":{"draft":{"header":{"announcement":{"enabled":true,"text":"New","textAr":"جديد"},"links":[{"label":"Draft header","type":"internal","path":"/"}]},"footer":{"links":[{"label":"Draft footer","type":"internal","path":"/products"}]}},
            "live":{"header":{"announcement":{"enabled":true,"text":"Old"},"links":[]},"footer":{"links":[]}}}}
            """
        };
        db.StoreConfigs.Add(config);
        await db.SaveChangesAsync();
        var before = config.CustomizationJson;
        var service = new StorefrontPreviewService(db, Mock.Of<ICmsPageReadService>(), NullLogger<StorefrontPreviewService>.Instance);
        var result = await service.GetConfigPreviewAsync(tenantId);
        Assert.True(result.Found);
        var preview = result.Config!;
        Assert.Equal("pearl", preview.StorefrontTheme);
        Assert.Equal("#112233", preview.Theme.Colors.Primary);
        Assert.Equal("draft", Assert.Single(preview.Pages.Home.Sections).Id);
        Assert.False(preview.Pages.Home.Sections[0].Enabled);
        Assert.Equal("New", preview.PreviewChrome!.Navigation.Header.Announcement!.Text);
        Assert.Equal("Draft footer", Assert.Single(preview.PreviewChrome.Navigation.Footer.Links).Label);
        Assert.Equal(2, preview.PreviewChrome.FooterByLanguage.Count);
        var live = StorefrontDraftConfigMapper.MapLive(config, null, "EGP", "EG");
        Assert.Null(live.PreviewChrome);
        Assert.Equal("live", Assert.Single(live.Pages.Home.Sections).Id);
        Assert.Equal("#445566", live.Theme.Colors.Primary);
        Assert.Equal(before, config.CustomizationJson);
        Assert.Equal(7, config.LiveVersion);
        var candidateTheme = (await service.GetConfigPreviewAsync(tenantId, CancellationToken.None, "fashion")).Config!;
        Assert.Equal("fashion", candidateTheme.StorefrontTheme);
        Assert.Equal("pearl", StorefrontDraftConfigMapper.MapLive(config, null, "EGP", "EG").StorefrontTheme);
        Assert.False((await service.GetConfigPreviewAsync(Guid.NewGuid())).Found);

        // Discard through the existing accepted live->draft boundary; preview resolver
        // must not retain an earlier draft after reload.
        var state = JsonNode.Parse(config.CustomizationJson)!;
        state["pages"]!["home"]!["draft"] = state["pages"]!["home"]!["live"]!.DeepClone();
        state["navigation"]!["draft"] = state["navigation"]!["live"]!.DeepClone();
        config.CustomizationJson = state.ToJsonString();
        await db.SaveChangesAsync();
        var discarded = (await service.GetConfigPreviewAsync(tenantId)).Config!;
        Assert.Equal("live", Assert.Single(discarded.Pages.Home.Sections).Id);
        Assert.Equal("Old", discarded.PreviewChrome!.Navigation.Header.Announcement!.Text);
    }

    [Fact]
    public void ExplicitlyEmptyDraftNavigation_NeverFallsBackToLiveLinksOrAnnouncement()
    {
        var result = StorefrontPreviewService.ResolveDraftNavigation(
            "{\"navigation\":{\"draft\":{},\"live\":{\"header\":{\"announcement\":{\"text\":\"Live secret\"},\"links\":[{\"label\":\"Live\"}]}}}}", DateTime.UtcNow);
        Assert.Empty(result.Header.Links);
        Assert.Null(result.Header.Announcement);
    }
}
