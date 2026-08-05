using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.MarketplaceCms;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Cms;

public sealed class MarketplaceCmsPageWorkflowTests
{
    [Fact]
    public async Task CreatePageAsync_CreatesDraftWithoutPublishingIt()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateMarketplaceService(dbContext);
        var readService = CreateMarketplaceReadService(dbContext);

        var created = await service.CreatePageAsync(
            new MarketplaceCmsPageRequest
            {
                Slug = "About",
                Title = "About Shopixy Marketplace",
                ContentType = CmsPageContentType.RichText,
                Content = JsonSerializer.SerializeToElement("<p>Marketplace draft</p>"),
                Seo = new MarketplaceCmsPageSeoRequest
                {
                    MetaTitle = "About Shopixy Marketplace",
                    MetaDescription = "Marketplace origin story."
                }
            });

        var published = await readService.GetPublishedPageAsync("about", "en");
        var preview = await readService.GetPreviewPageByIdAsync(created.Id, created.Draft!.Version, "en");

        Assert.Equal("about", created.Slug);
        Assert.NotNull(created.Draft);
        Assert.Null(created.Published);
        Assert.Null(published);
        Assert.NotNull(preview);
        Assert.Equal("<p>Marketplace draft</p>", preview!.Content.GetString());
    }

    [Fact]
    public async Task PublishPageAsync_MakesLatestVersionPublic()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateMarketplaceService(dbContext);
        var readService = CreateMarketplaceReadService(dbContext);

        var created = await service.CreatePageAsync(
            new MarketplaceCmsPageRequest
            {
                Slug = "privacy",
                Title = "Marketplace Privacy",
                ContentType = CmsPageContentType.Blocks,
                Content = JsonSerializer.SerializeToElement(new object[]
                {
                    new
                    {
                        type = "text",
                        data = new
                        {
                            text = "Privacy commitments for the marketplace.",
                            alignment = "left"
                        }
                    }
                }),
                Seo = new MarketplaceCmsPageSeoRequest
                {
                    MetaTitle = "Marketplace Privacy",
                    MetaDescription = "Privacy for the global marketplace.",
                    OpenGraphImageUrl = "https://cdn.shopixy.test/marketplace-privacy.png"
                }
            });

        var publishedResponse = await service.PublishPageAsync(created.Id, created.Draft!.Version);
        var publishedPage = await readService.GetPublishedPageAsync("privacy", "en");

        Assert.Null(publishedResponse.Draft);
        Assert.NotNull(publishedResponse.Published);
        Assert.NotNull(publishedPage);
        Assert.Equal("Marketplace Privacy", publishedPage!.Seo.MetaTitle);
        Assert.Equal("https://cdn.shopixy.test/marketplace-privacy.png", publishedPage.Seo.OpenGraphImageUrl);
        Assert.Equal(JsonValueKind.Array, publishedPage.Content.ValueKind);
    }

    [Fact]
    public async Task PublishPageAsync_ResolvesArabicLocalizedContent_WhenRequested()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateMarketplaceService(dbContext);
        var readService = CreateMarketplaceReadService(dbContext);

        var created = await service.CreatePageAsync(
            new MarketplaceCmsPageRequest
            {
                Slug = "about",
                Title = "About Shopixy",
                TitleLocalized = new PointOfSale.Core.Localization.LocalizedField
                {
                    En = "About Shopixy",
                    Ar = "عن شوبيكسي"
                },
                ContentType = CmsPageContentType.Blocks,
                Content = JsonSerializer.SerializeToElement(new object[]
                {
                    new
                    {
                        type = "text",
                        data = new
                        {
                            text = "Marketplace origin story.",
                            alignment = "left"
                        }
                    }
                }),
                LocalizedContent = JsonSerializer.SerializeToElement(new
                {
                    ar = new object[]
                    {
                        new
                        {
                            type = "text",
                            data = new
                            {
                                text = "قصة نشأة السوق.",
                                alignment = "left"
                            }
                        }
                    }
                }),
                Seo = new MarketplaceCmsPageSeoRequest
                {
                    MetaDescription = "Marketplace origin story.",
                    MetaDescriptionLocalized = new PointOfSale.Core.Localization.LocalizedField
                    {
                        En = "Marketplace origin story.",
                        Ar = "قصة نشأة السوق."
                    }
                }
            });

        await service.PublishPageAsync(created.Id, created.Draft!.Version);
        var publishedPage = await readService.GetPublishedPageAsync("about", "ar");

        Assert.NotNull(publishedPage);
        Assert.Equal("عن شوبيكسي", publishedPage!.Title);
        Assert.Equal("قصة نشأة السوق.", publishedPage.Seo.MetaDescription);
        var firstBlock = publishedPage.Content.EnumerateArray().First();
        Assert.Equal("قصة نشأة السوق.", firstBlock.GetProperty("data").GetProperty("text").GetString());
    }

    [Fact]
    public async Task GetPageAsync_FallsBackWhenLegacyBlockContentIsMalformed()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTime.UtcNow;
        var pageGroupId = Guid.NewGuid();

        dbContext.MarketplaceCmsPages.Add(new MarketplaceCmsPage
        {
            Id = Guid.NewGuid(),
            PageGroupId = pageGroupId,
            Slug = "legacy-blocks",
            Title = "Legacy Blocks",
            ContentType = CmsPageContentType.Blocks,
            Content = "{not-valid-json",
            Version = 1,
            IsDraft = true,
            IsPublished = false,
            CreatedAt = now,
            UpdatedAt = now
        });

        await dbContext.SaveChangesAsync();

        var service = CreateMarketplaceService(dbContext);
        var page = await service.GetPageAsync(pageGroupId);

        Assert.NotNull(page);
        Assert.NotNull(page!.Draft);
        Assert.Equal(JsonValueKind.Array, page.Draft!.Content.ValueKind);
        Assert.Empty(page.Draft.Content.EnumerateArray());
    }

    [Fact]
    public async Task CreatePageAsync_EnforcesGlobalSlugUniqueness()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateMarketplaceService(dbContext);

        await service.CreatePageAsync(BuildMarketplaceRichTextRequest("terms", "Terms", "<p>Terms A</p>"));

        await Assert.ThrowsAsync<SlugConflictException>(() =>
            service.CreatePageAsync(BuildMarketplaceRichTextRequest("TERMS", "Terms B", "<p>Terms B</p>")));
    }

    [Fact]
    public async Task StoreAndMarketplacePages_WithSameSlug_RemainIsolated()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = await SeedTenantAsync(dbContext, "Store Tenant");

        dbContext.CmsPages.Add(CreateStorePublishedPage(tenantId, "about", "Store About", "<p>Store</p>"));
        dbContext.MarketplaceCmsPages.Add(CreateMarketplacePublishedPage("about", "Marketplace About", "<p>Marketplace</p>"));
        await dbContext.SaveChangesAsync();

        var storeReadService = CreateStoreReadService(dbContext);
        var marketplaceReadService = CreateMarketplaceReadService(dbContext);

        var storePage = await storeReadService.GetPublishedPageAsync(tenantId, "about");
        var marketplacePage = await marketplaceReadService.GetPublishedPageAsync("about", "en");

        Assert.NotNull(storePage);
        Assert.NotNull(marketplacePage);
        Assert.Equal("Store About", storePage!.Title);
        Assert.Equal("Marketplace About", marketplacePage!.Title);
        Assert.Equal("<p>Store</p>", storePage.Content.GetString());
        Assert.Equal("<p>Marketplace</p>", marketplacePage.Content.GetString());
    }

    [Fact]
    public async Task UpdatePageAsync_CreatesImmutableDraftVersion_AndRejectsStaleExpectedVersion()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateMarketplaceService(dbContext);

        var created = await service.CreatePageAsync(BuildMarketplaceRichTextRequest("authority", "Authority", "<p>v1</p>"));
        var updated = await service.UpdatePageAsync(
            created.Id,
            WithExpectedVersion(BuildMarketplaceRichTextRequest("authority", "Authority", "<p>v2</p>"), created.Draft!.Version));

        Assert.Equal(2, updated.Draft!.Version);
        Assert.Equal(2, await dbContext.MarketplaceCmsPages.CountAsync(page => page.PageGroupId == created.Id));
        Assert.Single(await dbContext.MarketplaceCmsPages.Where(page => page.PageGroupId == created.Id && page.IsDraft).ToListAsync());

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.UpdatePageAsync(
            created.Id,
            WithExpectedVersion(BuildMarketplaceRichTextRequest("authority", "Authority", "<p>stale</p>"), created.Draft.Version)));
    }

    [Fact]
    public async Task VersionContract_CreateUpdateUpdatePublishDelete_AdoptsEveryMutationVersion()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateMarketplaceService(dbContext);

        var created = await service.CreatePageAsync(
            BuildMarketplaceRichTextRequest("lifecycle", "Lifecycle", "<p>v1</p>"));
        Assert.Equal(1, created.Draft!.Version);

        var firstUpdate = await service.UpdatePageAsync(
            created.Id,
            WithExpectedVersion(
                BuildMarketplaceRichTextRequest("lifecycle", "Lifecycle", "<p>v2</p>"),
                created.Draft.Version));
        Assert.Equal(2, firstUpdate.Draft!.Version);

        var secondUpdate = await service.UpdatePageAsync(
            created.Id,
            WithExpectedVersion(
                BuildMarketplaceRichTextRequest("lifecycle", "Lifecycle", "<p>v3</p>"),
                firstUpdate.Draft.Version));
        Assert.Equal(3, secondUpdate.Draft!.Version);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.UpdatePageAsync(
            created.Id,
            WithExpectedVersion(
                BuildMarketplaceRichTextRequest("lifecycle", "Lifecycle", "<p>stale</p>"),
                firstUpdate.Draft.Version)));

        var current = await service.GetPageAsync(created.Id);
        Assert.Equal(3, current!.Draft!.Version);
        Assert.Equal("<p>v3</p>", current.Draft.Content.GetString());
        Assert.Equal(3, await dbContext.MarketplaceCmsPages.CountAsync(page => page.PageGroupId == created.Id));

        var published = await service.PublishPageAsync(created.Id, current.Draft.Version);
        Assert.Null(published.Draft);
        Assert.Equal(3, published.Published!.Version);

        await service.DeletePageAsync(created.Id, published.Published.Version);

        Assert.Null(await service.GetPageAsync(created.Id));
        Assert.False(await dbContext.MarketplaceCmsPages.AnyAsync(page =>
            page.PageGroupId == created.Id && (page.IsDraft || page.IsPublished)));
    }

    [Fact]
    public async Task PublishPageAsync_RequiresExactDraftVersion()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateMarketplaceService(dbContext);

        var created = await service.CreatePageAsync(BuildMarketplaceRichTextRequest("publish", "Publish", "<p>draft</p>"));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.PublishPageAsync(created.Id, created.Draft!.Version + 1));

        var published = await service.PublishPageAsync(created.Id, created.Draft!.Version);

        Assert.Null(published.Draft);
        Assert.Equal(created.Draft.Version, published.Published!.Version);
    }

    [Fact]
    public async Task PreviewPageByIdAsync_WhenVersionBound_DoesNotExposeNewerDraft()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateMarketplaceService(dbContext);
        var readService = CreateMarketplaceReadService(dbContext);

        var created = await service.CreatePageAsync(BuildMarketplaceRichTextRequest("preview", "Preview", "<p>v1</p>"));
        var firstVersion = created.Draft!.Version;
        await service.UpdatePageAsync(
            created.Id,
            WithExpectedVersion(BuildMarketplaceRichTextRequest("preview", "Preview", "<p>v2</p>"), firstVersion));

        var stalePreview = await readService.GetPreviewPageByIdAsync(created.Id, firstVersion, "en");
        var currentPreview = await readService.GetPreviewPageByIdAsync(created.Id, firstVersion + 1, "en");

        Assert.Null(stalePreview);
        Assert.Equal("<p>v2</p>", currentPreview!.Content.GetString());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static MarketplaceCmsPageService CreateMarketplaceService(ApplicationDbContext dbContext)
    {
        return new MarketplaceCmsPageService(
            dbContext,
            new MarketplaceCmsPageContentValidator(),
            new InMemoryMarketplaceCmsPageCacheService(),
            new NoOpEventPublisher(),
            NullLogger<MarketplaceCmsPageService>.Instance);
    }

    private static MarketplaceCmsPageReadService CreateMarketplaceReadService(ApplicationDbContext dbContext)
    {
        return new MarketplaceCmsPageReadService(
            dbContext,
            new InMemoryMarketplaceCmsPageCacheService(),
            NullLogger<MarketplaceCmsPageReadService>.Instance);
    }

    private static CmsPageReadService CreateStoreReadService(ApplicationDbContext dbContext)
    {
        return new CmsPageReadService(
            dbContext,
            StorefrontReadModelMongoTestHelper.CreateDatabase(dbContext),
            NullLogger<CmsPageReadService>.Instance);
    }

    private static async Task<Guid> SeedTenantAsync(ApplicationDbContext dbContext, string name)
    {
        var tenantId = Guid.NewGuid();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = name,
            Slug = name.Replace(' ', '-').ToLowerInvariant(),
            Status = TenantStatus.Active,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();
        return tenantId;
    }

    private static MarketplaceCmsPageRequest BuildMarketplaceRichTextRequest(string slug, string title, string html)
    {
        return new MarketplaceCmsPageRequest
        {
            Slug = slug,
            Title = title,
            ContentType = CmsPageContentType.RichText,
            Content = JsonSerializer.SerializeToElement(html),
            Seo = new MarketplaceCmsPageSeoRequest()
        };
    }

    private static MarketplaceCmsPageRequest WithExpectedVersion(MarketplaceCmsPageRequest request, int expectedVersion)
    {
        request.ExpectedVersion = expectedVersion;
        return request;
    }

    private static CmsPage CreateStorePublishedPage(Guid tenantId, string slug, string title, string content)
    {
        var now = DateTime.UtcNow;
        return new CmsPage
        {
            Id = Guid.NewGuid(),
            PageGroupId = Guid.NewGuid(),
            TenantId = tenantId,
            Slug = slug,
            Title = title,
            Content = content,
            ContentType = CmsPageContentType.RichText,
            Version = 1,
            SchemaVersion = 1,
            IsDeleted = false,
            IsDraft = false,
            IsPublished = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static MarketplaceCmsPage CreateMarketplacePublishedPage(string slug, string title, string content)
    {
        var now = DateTime.UtcNow;
        return new MarketplaceCmsPage
        {
            Id = Guid.NewGuid(),
            PageGroupId = Guid.NewGuid(),
            Slug = slug,
            Title = title,
            Content = content,
            ContentType = CmsPageContentType.RichText,
            Version = 1,
            IsDraft = false,
            IsPublished = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private sealed class InMemoryMarketplaceCmsPageCacheService : IMarketplaceCmsPageCacheService
    {
        private IReadOnlyList<PointOfSale.API.DTOs.Marketplace.MarketplaceCmsPageDto>? _publishedPages;

        public Task<IReadOnlyList<PointOfSale.API.DTOs.Marketplace.MarketplaceCmsPageDto>?> GetPublishedPagesAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_publishedPages);
        }

        public Task SetPublishedPagesAsync(
            IReadOnlyList<PointOfSale.API.DTOs.Marketplace.MarketplaceCmsPageDto> pages,
            CancellationToken cancellationToken = default)
        {
            _publishedPages = pages;
            return Task.CompletedTask;
        }

        public Task InvalidatePublishedPagesAsync(CancellationToken cancellationToken = default)
        {
            _publishedPages = null;
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
