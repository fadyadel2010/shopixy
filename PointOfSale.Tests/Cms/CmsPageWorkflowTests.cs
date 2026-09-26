using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Cms;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Cms;

public sealed class CmsPageWorkflowTests
{
    [Fact]
    public async Task CreatePageAsync_CreatesDraftWithoutExposingItPublicly()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = await SeedTenantAsync(dbContext, "Draft Tenant");

        var pageService = CreatePageService(dbContext);
        var readService = CreateReadService(dbContext);

        var created = await pageService.CreatePageAsync(
            tenantId,
            new AdminCmsPageRequest
            {
                Slug = "About Us",
                Title = "About Shopixy",
                ContentType = CmsPageContentType.RichText,
                Content = JsonSerializer.SerializeToElement("<p>Draft about page</p>"),
                SchemaVersion = 1,
                Seo = new AdminCmsPageSeoRequest
                {
                    MetaTitle = "About Shopixy",
                    MetaDescription = "Learn more about our marketplace."
                }
            });

        var published = await readService.GetPublishedPageAsync(tenantId, "about-us");
        var preview = await readService.GetPreviewPageAsync(tenantId, "about-us");

        Assert.Equal("about-us", created.Slug);
        Assert.NotNull(created.Draft);
        Assert.Null(created.Published);
        Assert.Null(published);
        Assert.NotNull(preview);
        Assert.Equal("About Shopixy", preview!.Seo.MetaTitle);
        Assert.Equal("<p>Draft about page</p>", preview.Content.GetString());
    }

    [Fact]
    public async Task PublishPageAsync_MakesLatestVersionPublicAndKeepsSeo()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = await SeedTenantAsync(dbContext, "Published Tenant");

        var pageService = CreatePageService(dbContext);
        var readService = CreateReadService(dbContext);

        var created = await pageService.CreatePageAsync(
            tenantId,
            new AdminCmsPageRequest
            {
                Slug = "privacy-policy",
                Title = "Privacy Policy",
                ContentType = CmsPageContentType.Blocks,
                Content = JsonSerializer.SerializeToElement(new object[]
                {
                    new
                    {
                        type = "text",
                        data = new
                        {
                            text = "We respect your privacy.",
                            alignment = "left"
                        }
                    }
                }),
                SchemaVersion = 1,
                Seo = new AdminCmsPageSeoRequest
                {
                    MetaTitle = "Privacy Policy",
                    MetaDescription = "Our privacy commitments.",
                    OpenGraphImageUrl = "https://cdn.shopixy.test/privacy-og.png"
                }
            });

        var publishedResult = await pageService.PublishPageAsync(
            tenantId,
            created.Id,
            created.Draft!.Version);
        var publishedPage = await readService.GetPublishedPageAsync(tenantId, "privacy-policy");
        var previewPage = await readService.GetPreviewPageAsync(tenantId, "privacy-policy");

        Assert.Null(publishedResult.Draft);
        Assert.NotNull(publishedResult.Published);
        Assert.NotNull(publishedResult.Published!.PublishedAtUtc);
        Assert.NotNull(publishedPage);
        Assert.NotNull(previewPage);
        Assert.Equal("Privacy Policy", publishedPage!.Seo.MetaTitle);
        Assert.Equal("Our privacy commitments.", publishedPage.Seo.MetaDescription);
        Assert.Equal("https://cdn.shopixy.test/privacy-og.png", publishedPage.Seo.OpenGraphImageUrl);
        Assert.Equal(JsonValueKind.Array, publishedPage.Content.ValueKind);
        Assert.Equal("text", publishedPage.Content.EnumerateArray().First().GetProperty("type").GetString());
    }

    [Fact]
    public async Task CreatePageAsync_EnforcesSlugUniquenessPerTenantWhileAllowingTenantIsolation()
    {
        await using var dbContext = CreateDbContext();
        var tenantOneId = await SeedTenantAsync(dbContext, "Tenant One");
        var tenantTwoId = await SeedTenantAsync(dbContext, "Tenant Two");

        var pageService = CreatePageService(dbContext);

        await pageService.CreatePageAsync(
            tenantOneId,
            BuildRichTextRequest("contact", "Contact", "<p>Tenant one contact</p>"));

        await Assert.ThrowsAsync<SlugConflictException>(() => pageService.CreatePageAsync(
            tenantOneId,
            BuildRichTextRequest("CONTACT", "Duplicate Contact", "<p>Tenant one duplicate</p>")));

        var tenantTwoPage = await pageService.CreatePageAsync(
            tenantTwoId,
            BuildRichTextRequest("contact", "Contact", "<p>Tenant two contact</p>"));

        Assert.Equal("contact", tenantTwoPage.Slug);
    }

    [Fact]
    public async Task GetPublishedPageAsync_DoesNotFallbackAcrossTenantBoundaries()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = await SeedTenantAsync(dbContext, "Primary Tenant");
        var secondTenantId = await SeedTenantAsync(dbContext, "Secondary Tenant");

        dbContext.CmsPages.AddRange(
            CreatePublishedPage(tenantId, "about", "Primary About", "<p>Primary</p>"),
            CreatePublishedPage(secondTenantId, "about", "Secondary About", "<p>Secondary</p>"));
        await dbContext.SaveChangesAsync();

        var readService = CreateReadService(dbContext);

        var tenantPage = await readService.GetPublishedPageAsync(tenantId, "about");
        var crossTenantPage = await readService.GetPublishedPageAsync(Guid.NewGuid(), "about");

        Assert.NotNull(tenantPage);
        Assert.Equal("Primary About", tenantPage!.Title);
        Assert.Equal("<p>Primary</p>", tenantPage.Content.GetString());

        Assert.Null(crossTenantPage);
    }

    [Fact]
    public async Task UpdatePageAsync_CreatesImmutableDraftVersionAndRejectsStaleReplay()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = await SeedTenantAsync(dbContext, "Versioned Draft Tenant");
        var pageService = CreatePageService(dbContext);

        var created = await pageService.CreatePageAsync(
            tenantId,
            BuildRichTextRequest("about", "About v1", "<p>v1</p>"));

        var update = BuildRichTextRequest("about", "About v2", "<p>v2</p>");
        update.ExpectedVersion = created.Draft!.Version;
        var updated = await pageService.UpdatePageAsync(tenantId, created.Id, update);

        var staleReplay = BuildRichTextRequest("about", "Stale overwrite", "<p>stale</p>");
        staleReplay.ExpectedVersion = created.Draft.Version;
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            pageService.UpdatePageAsync(tenantId, created.Id, staleReplay));

        var versions = await dbContext.CmsPages
            .Where(page => page.TenantId == tenantId && page.PageGroupId == created.Id)
            .OrderBy(page => page.Version)
            .ToListAsync();

        Assert.Equal(2, versions.Count);
        Assert.False(versions[0].IsDraft);
        Assert.Equal("About v1", versions[0].Title);
        Assert.True(versions[1].IsDraft);
        Assert.Equal("About v2", versions[1].Title);
        Assert.Equal(2, updated.Draft!.Version);
    }

    [Fact]
    public async Task CreatePageAsync_RejectsUnsafeSeoMediaBeforeSqlPersistence()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = await SeedTenantAsync(dbContext, "Unsafe SEO Tenant");
        var pageService = CreatePageService(dbContext);

        var request = BuildRichTextRequest("unsafe-seo", "Unsafe SEO", "<p>content</p>");
        request.Seo.OpenGraphImageUrl = "javascript:alert(1)";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pageService.CreatePageAsync(tenantId, request));

        Assert.False(await dbContext.CmsPages.AnyAsync(page => page.TenantId == tenantId));
    }

    [Fact]
    public async Task CreatePageAsync_RejectsUnsafeSeoTextBeforeSqlPersistence()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = await SeedTenantAsync(dbContext, "Unsafe SEO Text Tenant");
        var pageService = CreatePageService(dbContext);

        var request = BuildRichTextRequest("unsafe-seo-text", "Unsafe SEO Text", "<p>content</p>");
        request.Seo.MetaTitle = "Injected\r\n<meta name=\"robots\" content=\"noindex\">";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pageService.CreatePageAsync(tenantId, request));

        Assert.False(await dbContext.CmsPages.AnyAsync(page => page.TenantId == tenantId));
    }

    [Fact]
    public async Task CreatePageAsync_PersistsOnlyModeledBackendSeoFields()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = await SeedTenantAsync(dbContext, "Modeled SEO Tenant");
        var pageService = CreatePageService(dbContext);

        var request = BuildRichTextRequest("modeled-seo", "Modeled SEO", "<p>content</p>");
        request.Seo = new AdminCmsPageSeoRequest
        {
            MetaTitle = "  Modeled SEO  ",
            MetaDescription = "  Backend-owned description  ",
            MetaKeywords = " cms, seo ",
            OpenGraphImageUrl = "/media/cms/modeled.png"
        };

        var created = await pageService.CreatePageAsync(tenantId, request);
        var stored = await dbContext.CmsPages.SingleAsync(page => page.TenantId == tenantId);

        Assert.Equal("Modeled SEO", created.Draft!.Seo.MetaTitle);
        Assert.Equal("Backend-owned description", created.Draft.Seo.MetaDescription);
        Assert.Equal("cms, seo", created.Draft.Seo.MetaKeywords);
        Assert.Equal("/media/cms/modeled.png", created.Draft.Seo.OpenGraphImageUrl);
        Assert.Equal("Modeled SEO", stored.MetaTitle);
        Assert.Equal("Backend-owned description", stored.MetaDescription);
        Assert.Equal("cms, seo", stored.MetaKeywords);
        Assert.Equal("/media/cms/modeled.png", stored.OpenGraphImageUrl);
    }

    [Fact]
    public async Task PublishPageAsync_RejectsReviewedVersionAfterNewerDraftExists()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = await SeedTenantAsync(dbContext, "Publish Replay Tenant");
        var pageService = CreatePageService(dbContext);

        var created = await pageService.CreatePageAsync(
            tenantId,
            BuildRichTextRequest("terms", "Terms v1", "<p>v1</p>"));

        var update = BuildRichTextRequest("terms", "Terms v2", "<p>v2</p>");
        update.ExpectedVersion = created.Draft!.Version;
        var updated = await pageService.UpdatePageAsync(tenantId, created.Id, update);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            pageService.PublishPageAsync(tenantId, created.Id, created.Draft.Version));

        var current = await pageService.GetPageAsync(tenantId, created.Id);
        Assert.NotNull(current);
        Assert.Null(current!.Published);
        Assert.Equal(updated.Draft!.Version, current.Draft!.Version);
        Assert.Equal("Terms v2", current.Draft.Title);
    }

    [Fact]
    public async Task GetPreviewPageByIdAsync_WithVersion_DoesNotDriftToNewerDraft()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = await SeedTenantAsync(dbContext, "Preview Version Tenant");
        var pageService = CreatePageService(dbContext);
        var readService = CreateReadService(dbContext);

        var created = await pageService.CreatePageAsync(
            tenantId,
            BuildRichTextRequest("contact", "Contact v1", "<p>v1</p>"));

        var update = BuildRichTextRequest("contact", "Contact v2", "<p>v2</p>");
        update.ExpectedVersion = created.Draft!.Version;
        var updated = await pageService.UpdatePageAsync(tenantId, created.Id, update);

        var stalePreview = await readService.GetPreviewPageByIdAsync(
            tenantId,
            created.Id,
            created.Draft.Version);
        var currentPreview = await readService.GetPreviewPageByIdAsync(
            tenantId,
            created.Id,
            updated.Draft!.Version);

        Assert.Null(stalePreview);
        Assert.NotNull(currentPreview);
        Assert.Equal("Contact v2", currentPreview!.Title);
    }

    [Fact]
    public async Task GetVersionHistoryAsync_ReturnsBackendOwnedSnapshotsIncludingInactivePublishedVersions()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = await SeedTenantAsync(dbContext, "History Tenant");
        var pageService = CreatePageService(dbContext);

        var created = await pageService.CreatePageAsync(
            tenantId,
            BuildRichTextRequest("history", "History v1", "<p>v1</p>"));
        await pageService.PublishPageAsync(tenantId, created.Id, created.Draft!.Version);

        var update = BuildRichTextRequest("history", "History v2", "<p>v2</p>");
        update.ExpectedVersion = 1;
        var updated = await pageService.UpdatePageAsync(tenantId, created.Id, update);

        var history = await pageService.GetVersionHistoryAsync(tenantId, created.Id);

        Assert.NotNull(history);
        Assert.Equal(created.Id, history!.PageId);
        Assert.Equal(2, history.CurrentDraftVersion);
        Assert.Equal(1, history.PublishedVersion);
        Assert.Equal(new[] { 2, 1 }, history.Versions.Select(item => item.Version).ToArray());
        Assert.Equal("History v1", history.Versions.Single(item => item.Version == 1).Snapshot.Title);
        Assert.Equal(updated.Draft!.RecordId, history.Versions.Single(item => item.Version == 2).Snapshot.RecordId);
    }

    [Fact]
    public async Task RollbackPageAsync_ResolvesSnapshotInBackendAndCreatesNewPublishedVersion()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = await SeedTenantAsync(dbContext, "Rollback Tenant");
        var pageService = CreatePageService(dbContext);
        var readService = CreateReadService(dbContext);

        var created = await pageService.CreatePageAsync(
            tenantId,
            BuildRichTextRequest("rollback", "Rollback v1", "<p>v1</p>"));
        var publishedV1 = await pageService.PublishPageAsync(tenantId, created.Id, created.Draft!.Version);

        var update = BuildRichTextRequest("rollback", "Rollback v2", "<p>v2</p>");
        update.ExpectedVersion = publishedV1.Published!.Version;
        var draftV2 = await pageService.UpdatePageAsync(tenantId, created.Id, update);
        await pageService.PublishPageAsync(tenantId, created.Id, draftV2.Draft!.Version);

        var rollback = await pageService.RollbackPageAsync(
            tenantId,
            created.Id,
            rollbackVersion: 1,
            expectedVersion: 2,
            reason: "certification rollback");
        var publicPage = await readService.GetPublishedPageAsync(tenantId, "rollback");
        var history = await pageService.GetVersionHistoryAsync(tenantId, created.Id);

        Assert.Null(rollback.Draft);
        Assert.NotNull(rollback.Published);
        Assert.Equal(3, rollback.Published!.Version);
        Assert.Equal("Rollback v1", rollback.Published.Title);
        Assert.Equal("<p>v1</p>", publicPage!.Content.GetString());
        Assert.Equal(3, history!.PublishedVersion);
        Assert.Equal(new[] { 3, 2, 1 }, history.Versions.Select(item => item.Version).ToArray());
    }

    [Fact]
    public async Task RollbackPageAsync_RejectsStaleExpectedVersionReplay()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = await SeedTenantAsync(dbContext, "Rollback Replay Tenant");
        var pageService = CreatePageService(dbContext);

        var created = await pageService.CreatePageAsync(
            tenantId,
            BuildRichTextRequest("rollback-replay", "Rollback Replay v1", "<p>v1</p>"));
        await pageService.PublishPageAsync(tenantId, created.Id, created.Draft!.Version);

        var update = BuildRichTextRequest("rollback-replay", "Rollback Replay v2", "<p>v2</p>");
        update.ExpectedVersion = 1;
        await pageService.UpdatePageAsync(tenantId, created.Id, update);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => pageService.RollbackPageAsync(
            tenantId,
            created.Id,
            rollbackVersion: 1,
            expectedVersion: 1,
            reason: "stale replay"));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new CmsWorkflowTestDbContext(options);
    }

    private sealed class CmsWorkflowTestDbContext(DbContextOptions<ApplicationDbContext> options)
        : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<CmsPage>().HasQueryFilter(page => !page.IsDeleted);
        }
    }

    private static CmsPageService CreatePageService(ApplicationDbContext dbContext)
    {
        return new CmsPageService(
            dbContext,
            new CmsPageContentValidator(),
            new NoOpEventPublisher(),
            NullLogger<CmsPageService>.Instance);
    }

    private static CmsPageReadService CreateReadService(ApplicationDbContext dbContext)
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

    private static AdminCmsPageRequest BuildRichTextRequest(string slug, string title, string html)
    {
        return new AdminCmsPageRequest
        {
            Slug = slug,
            Title = title,
            ContentType = CmsPageContentType.RichText,
            Content = JsonSerializer.SerializeToElement(html),
            SchemaVersion = 1,
            Seo = new AdminCmsPageSeoRequest()
        };
    }

    private static CmsPage CreatePublishedPage(Guid tenantId, string slug, string title, string content)
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
