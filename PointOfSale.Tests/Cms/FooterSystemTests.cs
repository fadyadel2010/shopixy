using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.MarketplaceCms;
using PointOfSale.API.Services.MarketplaceFooter;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Cms;

public sealed class FooterSystemTests
{
    [Fact]
    public async Task StorefrontFooterReadService_UsesTenantConfigurationAndTenantSocialLinks()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        SeedLiveStorefrontAuthority(dbContext, tenantId);

        dbContext.CmsPages.Add(CreateStorePublishedPage(tenantId, "about", "Store About"));
        dbContext.StorefrontFooterSections.Add(CreateStorefrontSection(
            tenantId,
            "Company",
            [
                CreateStorefrontLink(FooterLinkType.Cms, label: "About this store", cmsPageSlug: "about", openInNewTab: true),
                CreateStorefrontLink(FooterLinkType.External, label: "Bad Link", url: "javascript:void(0)")
            ]));
        dbContext.StorefrontFooterSections.Add(CreateStorefrontSection(
            null,
            "Global",
            [CreateStorefrontLink(FooterLinkType.Internal, label: "Global Help", url: "/help")]));

        dbContext.StorefrontSocialLinks.Add(CreateStorefrontSocialLink(tenantId, "Instagram", "https://instagram.com/store"));
        dbContext.StorefrontSocialLinks.Add(CreateStorefrontSocialLink(null, "Facebook", "https://facebook.com/global-store"));

        await dbContext.SaveChangesAsync();

        var service = CreateStorefrontFooterReadService(dbContext);
        var footer = await service.GetFooterAsync(tenantId);

        Assert.Equal("Tenant", footer.FooterSource);
        Assert.Equal("Tenant", footer.SocialSource);
        Assert.Single(footer.Sections);
        Assert.Single(footer.Sections[0].Links);
        Assert.Equal("About this store", footer.Sections[0].Links[0].Label);
        Assert.Equal("/about", footer.Sections[0].Links[0].Url);
        Assert.True(footer.Sections[0].Links[0].OpenInNewTab);
        Assert.Single(footer.SocialLinks);
        Assert.Equal("Instagram", footer.SocialLinks[0].Platform);
    }

    [Fact]
    public async Task StorefrontFooterReadService_CmsTargetsFailClosedAgainstCurrentTenantPublishedAuthority()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        SeedLiveStorefrontAuthority(dbContext, tenantId);
        SeedLiveStorefrontAuthority(dbContext, foreignTenantId);

        var renamedPage = CreateStorePublishedPage(tenantId, "current-about", "Renamed About");
        dbContext.CmsPages.Add(renamedPage);
        dbContext.CmsPages.Add(CreateStorePublishedPage(foreignTenantId, "foreign-only", "Foreign"));
        var unpublished = CreateStorePublishedPage(tenantId, "unpublished", "Unpublished");
        unpublished.IsPublished = false;
        unpublished.IsDraft = true;
        unpublished.PublishedAt = null;
        dbContext.CmsPages.Add(unpublished);
        dbContext.StorefrontFooterSections.Add(CreateStorefrontSection(
            tenantId,
            "Company",
            [
                CreateStorefrontLink(FooterLinkType.Cms, label: "Renamed", cmsPageSlug: "about", cmsPageGroupId: renamedPage.PageGroupId, sortOrder: 0),
                CreateStorefrontLink(FooterLinkType.Cms, label: "Foreign", cmsPageSlug: "foreign-only", sortOrder: 2),
                CreateStorefrontLink(FooterLinkType.Cms, label: "Unpublished", cmsPageSlug: "unpublished", sortOrder: 3)
            ]));
        await dbContext.SaveChangesAsync();

        var footer = await CreateStorefrontFooterReadService(dbContext).GetFooterAsync(tenantId);

        var link = Assert.Single(Assert.Single(footer.Sections).Links);
        Assert.Equal("Renamed", link.Label);
        Assert.Equal("/current-about", link.Url);
        Assert.DoesNotContain(footer.Sections.SelectMany(section => section.Links), item =>
            item.Label is "Foreign" or "Unpublished");
    }

    [Fact]
    public async Task StorefrontFooterReadService_FallsBackToGlobalConfiguration_WhenTenantFooterAndSocialBecomeEmpty()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        SeedLiveStorefrontAuthority(dbContext, tenantId);

        dbContext.StorefrontFooterSections.Add(CreateStorefrontSection(
            tenantId,
            "Tenant Footer",
            [CreateStorefrontLink(FooterLinkType.Cms, label: "Missing", cmsPageSlug: "missing-page")]));
        dbContext.StorefrontSocialLinks.Add(CreateStorefrontSocialLink(tenantId, "Broken", "notaurl"));

        dbContext.StorefrontFooterSections.Add(CreateStorefrontSection(
            null,
            "Global Footer",
            [CreateStorefrontLink(FooterLinkType.Internal, label: "Support", url: "/support")]));
        dbContext.StorefrontSocialLinks.Add(CreateStorefrontSocialLink(null, "Facebook", "https://facebook.com/storefront-global"));

        await dbContext.SaveChangesAsync();

        var service = CreateStorefrontFooterReadService(dbContext);
        var footer = await service.GetFooterAsync(tenantId);

        Assert.Equal("Global", footer.FooterSource);
        Assert.Equal("Global", footer.SocialSource);
        Assert.Single(footer.Sections);
        Assert.Equal("Support", footer.Sections[0].Links[0].Label);
        Assert.Equal("/support", footer.Sections[0].Links[0].Url);
        Assert.Single(footer.SocialLinks);
        Assert.Equal("Facebook", footer.SocialLinks[0].Platform);
    }

    [Fact]
    public async Task StorefrontFooterReadService_DoesNotLeakMarketplaceFooterCmsOrSocialData()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        SeedLiveStorefrontAuthority(dbContext, tenantId);

        dbContext.CmsPages.Add(CreateStorePublishedPage(tenantId, "about", "Store About"));
        dbContext.MarketplaceCmsPages.Add(CreateMarketplacePublishedPage("about", "Marketplace About"));
        dbContext.MarketplaceFooterSections.Add(CreateMarketplaceSection(
            "Marketplace Footer",
            [CreateMarketplaceLink(FooterLinkType.Cms, label: "Marketplace About", cmsPageSlug: "about")]));
        dbContext.MarketplaceSocialLinks.Add(CreateMarketplaceSocialLink("TikTok", "https://tiktok.com/@shopixy"));

        await dbContext.SaveChangesAsync();

        var service = CreateStorefrontFooterReadService(dbContext);
        var footer = await service.GetFooterAsync(tenantId);

        Assert.Equal("Derived", footer.FooterSource);
        Assert.Equal("None", footer.SocialSource);
        Assert.Single(footer.Sections);
        Assert.Single(footer.Sections[0].Links);
        Assert.Equal("Store About", footer.Sections[0].Links[0].Label);
        Assert.Equal("/about", footer.Sections[0].Links[0].Url);
        Assert.Empty(footer.SocialLinks);
    }

    [Fact]
    public async Task MarketplaceFooterReadService_UsesMarketplaceDataOnly_AndRespectsSavedLinkBehavior()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();

        dbContext.CmsPages.Add(CreateStorePublishedPage(tenantId, "about", "Store About"));
        dbContext.StorefrontFooterSections.Add(CreateStorefrontSection(
            tenantId,
            "Store Footer",
            [CreateStorefrontLink(FooterLinkType.Internal, label: "Store Only", url: "/store-only")]));
        dbContext.MarketplaceCmsPages.Add(CreateMarketplacePublishedPage("about", "Marketplace About"));
        dbContext.MarketplaceFooterSections.Add(CreateMarketplaceSection(
            "Marketplace Footer",
            [
                CreateMarketplaceLink(FooterLinkType.Cms, label: "About Shopixy", cmsPageSlug: "about", sortOrder: 0),
                CreateMarketplaceLink(FooterLinkType.External, label: "Twitter", url: "https://x.com/shopixy", openInNewTab: false, sortOrder: 1)
            ]));
        dbContext.MarketplaceSocialLinks.Add(CreateMarketplaceSocialLink("Instagram", "https://instagram.com/shopixy"));
        dbContext.MarketplaceSocialLinks.Add(CreateMarketplaceSocialLink("Inactive", "https://example.com/inactive", isActive: false));

        await dbContext.SaveChangesAsync();

        var service = CreateMarketplaceFooterReadService(dbContext);
        var footer = await service.GetFooterAsync("en");

        Assert.Equal("Configured", footer.Source);
        Assert.Single(footer.Sections);
        Assert.Equal(2, footer.Sections[0].Links.Count);
        Assert.Equal("/en/about", footer.Sections[0].Links.Single(link => link.Label == "About Shopixy").Url);
        Assert.False(footer.Sections[0].Links.Single(link => link.Label == "Twitter").OpenInNewTab);
        Assert.Single(footer.SocialLinks);
        Assert.Equal("Instagram", footer.SocialLinks[0].Platform);
        Assert.DoesNotContain(footer.Sections[0].Links, link => link.Label == "Store Only");
    }

    [Fact]
    public async Task MarketplaceFooterReadService_ResolvesArabicTitlesAndLabels_WhenAvailable()
    {
        await using var dbContext = CreateDbContext();

        dbContext.MarketplaceCmsPages.Add(new MarketplaceCmsPage
        {
            Id = Guid.NewGuid(),
            PageGroupId = Guid.NewGuid(),
            Slug = "about",
            Title = "About Shopixy",
            TitleJson = "{\"en\":\"About Shopixy\",\"ar\":\"عن شوبيكسي\"}",
            Content = "<p>Marketplace page</p>",
            ContentJson = "{\"en\":\"<p>Marketplace page</p>\",\"ar\":\"<p>صفحة السوق</p>\"}",
            ContentType = CmsPageContentType.RichText,
            Version = 1,
            IsDraft = false,
            IsPublished = true,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        dbContext.MarketplaceFooterSections.Add(new MarketplaceFooterSection
        {
            Id = Guid.NewGuid(),
            Title = "Company",
            TitleJson = "{\"en\":\"Company\",\"ar\":\"الشركة\"}",
            SortOrder = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Links =
            [
                new MarketplaceFooterLink
                {
                    Id = Guid.NewGuid(),
                    Label = "About Shopixy",
                    LabelJson = "{\"en\":\"About Shopixy\",\"ar\":\"عن شوبيكسي\"}",
                    LinkType = FooterLinkType.Cms,
                    CmsPageSlug = "about",
                    SortOrder = 0,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            ]
        });

        await dbContext.SaveChangesAsync();

        var service = CreateMarketplaceFooterReadService(dbContext);
        var footer = await service.GetFooterAsync("ar");

        Assert.Equal("ar", footer.Language);
        Assert.Equal("الشركة", footer.Sections[0].Title);
        Assert.Equal("عن شوبيكسي", footer.Sections[0].Links[0].Label);
        Assert.Equal("/ar/about", footer.Sections[0].Links[0].Url);
    }

    [Fact]
    public async Task MarketplaceCmsPageSeeder_SeedsDefaultPagesOnlyOnce()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();

        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IMarketplaceCmsPageContentValidator, MarketplaceCmsPageContentValidator>();
        services.AddSingleton<IMarketplaceCmsPageCacheService, InMemoryMarketplaceCmsPageCacheService>();
        services.AddSingleton<IEventPublisher, NoOpEventPublisher>();
        services.AddScoped<IMarketplaceCmsPageService, MarketplaceCmsPageService>();

        using var serviceProvider = services.BuildServiceProvider();
        var seeder = new MarketplaceCmsPageSeeder(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MarketplaceCmsPageSeeder>.Instance);

        await seeder.StartAsync(CancellationToken.None);
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await using var seedScope = serviceProvider.CreateAsyncScope();
            var seedContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (await seedContext.MarketplaceCmsPages.IgnoreQueryFilters().CountAsync() == 4)
            {
                break;
            }

            await Task.Delay(20);
        }

        await seeder.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pages = await dbContext.MarketplaceCmsPages
            .AsNoTracking()
            .OrderBy(page => page.Slug)
            .ToListAsync();

        Assert.Equal(4, pages.Count);
        Assert.All(pages, page =>
        {
            Assert.True(page.IsPublished);
            Assert.False(page.IsDraft);
            Assert.NotNull(page.PublishedAt);
        });

        Assert.Equal(
            ["about", "contact", "privacy", "terms"],
            pages.Select(page => page.Slug).ToArray());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static StorefrontFooterReadService CreateStorefrontFooterReadService(ApplicationDbContext dbContext)
    {
        return new StorefrontFooterReadService(
            dbContext,
            NullLogger<StorefrontFooterReadService>.Instance);
    }

    private static void SeedLiveStorefrontAuthority(ApplicationDbContext dbContext, Guid tenantId)
    {
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Footer Test Store",
            Slug = $"footer-{tenantId:N}",
            IsActive = true,
            Status = TenantStatus.Active
        });
        dbContext.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Footer Test Store",
            StorefrontStatus = "live"
        });
    }

    private static MarketplaceFooterReadService CreateMarketplaceFooterReadService(ApplicationDbContext dbContext)
    {
        return new MarketplaceFooterReadService(
            dbContext,
            new InMemoryMarketplaceFooterCacheService(),
            NullLogger<MarketplaceFooterReadService>.Instance);
    }

    private static CmsPage CreateStorePublishedPage(Guid tenantId, string slug, string title)
    {
        var now = DateTime.UtcNow;
        return new CmsPage
        {
            Id = Guid.NewGuid(),
            PageGroupId = Guid.NewGuid(),
            TenantId = tenantId,
            Slug = slug,
            Title = title,
            Content = "<p>Store page</p>",
            ContentType = CmsPageContentType.RichText,
            Version = 1,
            SchemaVersion = 1,
            IsDraft = false,
            IsPublished = true,
            IsDeleted = false,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static MarketplaceCmsPage CreateMarketplacePublishedPage(string slug, string title)
    {
        var now = DateTime.UtcNow;
        return new MarketplaceCmsPage
        {
            Id = Guid.NewGuid(),
            PageGroupId = Guid.NewGuid(),
            Slug = slug,
            Title = title,
            Content = "<p>Marketplace page</p>",
            ContentType = CmsPageContentType.RichText,
            Version = 1,
            IsDraft = false,
            IsPublished = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static StorefrontFooterSection CreateStorefrontSection(
        Guid? tenantId,
        string title,
        IEnumerable<StorefrontFooterLink> links)
    {
        var now = DateTime.UtcNow;
        var sectionId = Guid.NewGuid();
        var materializedLinks = links.ToList();

        foreach (var link in materializedLinks)
        {
            link.SectionId = sectionId;
            link.CreatedAt = now;
            link.UpdatedAt = now;
        }

        return new StorefrontFooterSection
        {
            Id = sectionId,
            TenantId = tenantId,
            Title = title,
            SortOrder = 0,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Links = materializedLinks
        };
    }

    private static StorefrontFooterLink CreateStorefrontLink(
        FooterLinkType type,
        string label,
        string? url = null,
        string? cmsPageSlug = null,
        Guid? cmsPageGroupId = null,
        bool openInNewTab = false,
        bool isActive = true,
        int sortOrder = 0)
    {
        return new StorefrontFooterLink
        {
            Id = Guid.NewGuid(),
            Label = label,
            LinkType = type,
            Url = url,
            CmsPageGroupId = cmsPageGroupId,
            CmsPageSlug = cmsPageSlug,
            OpenInNewTab = openInNewTab,
            SortOrder = sortOrder,
            IsActive = isActive
        };
    }

    private static StorefrontSocialLink CreateStorefrontSocialLink(
        Guid? tenantId,
        string platform,
        string url,
        bool isActive = true)
    {
        var now = DateTime.UtcNow;
        return new StorefrontSocialLink
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Platform = platform,
            Url = url,
            SortOrder = 0,
            IsActive = isActive,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static MarketplaceFooterSection CreateMarketplaceSection(
        string title,
        IEnumerable<MarketplaceFooterLink> links)
    {
        var now = DateTime.UtcNow;
        var sectionId = Guid.NewGuid();
        var materializedLinks = links.ToList();

        foreach (var link in materializedLinks)
        {
            link.SectionId = sectionId;
            link.CreatedAt = now;
            link.UpdatedAt = now;
        }

        return new MarketplaceFooterSection
        {
            Id = sectionId,
            Title = title,
            SortOrder = 0,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Links = materializedLinks
        };
    }

    private static MarketplaceFooterLink CreateMarketplaceLink(
        FooterLinkType type,
        string label,
        string? url = null,
        string? cmsPageSlug = null,
        bool openInNewTab = false,
        bool isActive = true,
        int sortOrder = 0)
    {
        return new MarketplaceFooterLink
        {
            Id = Guid.NewGuid(),
            Label = label,
            LinkType = type,
            Url = url,
            CmsPageSlug = cmsPageSlug,
            OpenInNewTab = openInNewTab,
            SortOrder = sortOrder,
            IsActive = isActive
        };
    }

    private static MarketplaceSocialLink CreateMarketplaceSocialLink(
        string platform,
        string url,
        bool isActive = true)
    {
        var now = DateTime.UtcNow;
        return new MarketplaceSocialLink
        {
            Id = Guid.NewGuid(),
            Platform = platform,
            Url = url,
            SortOrder = 0,
            IsActive = isActive,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private sealed class InMemoryMarketplaceCmsPageCacheService : IMarketplaceCmsPageCacheService
    {
        private IReadOnlyList<PointOfSale.API.DTOs.Marketplace.MarketplaceCmsPageDto>? _pages;

        public Task<IReadOnlyList<PointOfSale.API.DTOs.Marketplace.MarketplaceCmsPageDto>?> GetPublishedPagesAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_pages);
        }

        public Task SetPublishedPagesAsync(
            IReadOnlyList<PointOfSale.API.DTOs.Marketplace.MarketplaceCmsPageDto> pages,
            CancellationToken cancellationToken = default)
        {
            _pages = pages;
            return Task.CompletedTask;
        }

        public Task InvalidatePublishedPagesAsync(CancellationToken cancellationToken = default)
        {
            _pages = null;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryMarketplaceFooterCacheService : IMarketplaceFooterCacheService
    {
        private PointOfSale.API.DTOs.Marketplace.MarketplaceFooterDto? _footer;

        public Task<PointOfSale.API.DTOs.Marketplace.MarketplaceFooterDto?> GetAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_footer);
        }

        public Task SetAsync(
            PointOfSale.API.DTOs.Marketplace.MarketplaceFooterDto footer,
            DateTime? lastUpdatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            _footer = footer;
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(CancellationToken cancellationToken = default)
        {
            _footer = null;
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpEventPublisher : IEventPublisher
    {
        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            return Task.CompletedTask;
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent
        {
            return Task.CompletedTask;
        }
    }
}
