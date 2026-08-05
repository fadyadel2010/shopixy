using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Cms;

public sealed class StorefrontNavigationRuntimeTests
{
    [Fact]
    public async Task GetNavigation_ReturnsPublishedLiveHeaderAndFooterNavigation()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Navigation Runtime Store",
            Slug = $"navigation-runtime-{tenantId:N}",
            IsActive = true,
            Status = TenantStatus.Active
        });
        context.Categories.Add(new Category
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Smart Phones",
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        });
        context.CmsPages.Add(new CmsPage
        {
            Id = Guid.NewGuid(),
            PageGroupId = Guid.NewGuid(),
            TenantId = tenantId,
            Slug = "footer-navigation",
            Title = "Footer Navigation",
            Content = "<p>Footer navigation</p>",
            ContentType = CmsPageContentType.RichText,
            Version = 1,
            SchemaVersion = 1,
            IsDraft = false,
            IsPublished = true,
            IsDeleted = false,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        context.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Navigation Runtime Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            StorefrontStatus = "live",
            CustomizationJson = """
                {
                  "navigation": {
                    "draft": {
                      "header": { "links": [] },
                      "footer": { "links": [] }
                    },
                    "live": {
                      "header": { "links": [
                        { "label": "Header Navigation", "type": "internal", "path": "/header-navigation", "enabled": true, "order": 0 },
                        { "label": "Smart Phones", "type": "category", "slug": "smart-phones", "enabled": true, "order": 1 }
                      ] },
                      "footer": { "links": [
                        { "label": "CMS Footer Link", "type": "cms", "slug": "footer-navigation", "enabled": true, "order": 0 }
                      ] }
                    }
                  }
                }
                """,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var controller = new StorefrontNavigationController(
            context,
            NullLogger<StorefrontNavigationController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection()
                        .AddSingleton<ITenantContext>(new TestTenantContext(tenantId))
                        .BuildServiceProvider()
                }
            }
        };

        var result = await controller.GetNavigation(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var navigation = Assert.IsType<NavigationDto>(ok.Value);

        Assert.Equal(["Header Navigation", "Smart Phones"], navigation.Header.Links.Select(link => link.Label));
        Assert.Equal("/header-navigation", navigation.Header.Links[0].Path);
        Assert.Equal("smart-phones", navigation.Header.Links[1].Slug);
        Assert.Equal("CMS Footer Link", navigation.Footer.Links.Single().Label);
        Assert.Equal("footer-navigation", navigation.Footer.Links.Single().Slug);
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }
}
