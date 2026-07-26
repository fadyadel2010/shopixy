using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.MarketplaceCms;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Cms;

public sealed class CmsPublicationReliabilityTests
{
    private static readonly ConcurrentDictionary<string, InMemoryDatabaseRoot> DatabaseRoots = new(StringComparer.Ordinal);

    [Fact]
    public async Task PublishStorefrontPageAsync_WhenOutboxPublishFails_DoesNotChangeLiveCmsTruth()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var pageGroupId = Guid.NewGuid();

        await using var seedContext = CreateDbContext(databaseName, tenantId);
        await SeedTenantAsync(seedContext, tenantId);
        SeedStorefrontPageVersions(seedContext, tenantId, pageGroupId);
        await seedContext.SaveChangesAsync();

        await using var testContext = CreateDbContext(databaseName, tenantId);
        var publisher = new Mock<IEventPublisher>();
        publisher
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<StorefrontCmsPagePublishedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));

        var service = CreateStorefrontService(testContext, publisher.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishPageAsync(
            tenantId,
            pageGroupId,
            2,
            CancellationToken.None));

        await using var verifyContext = CreateDbContext(databaseName, tenantId);
        var versions = await verifyContext.CmsPages
            .Where(page => page.TenantId == tenantId && page.PageGroupId == pageGroupId)
            .OrderBy(page => page.Version)
            .ToListAsync();

        Assert.Collection(
            versions,
            page =>
            {
                Assert.True(page.IsPublished);
                Assert.False(page.IsDraft);
                Assert.Equal(1, page.Version);
            },
            page =>
            {
                Assert.False(page.IsPublished);
                Assert.True(page.IsDraft);
                Assert.Equal(2, page.Version);
                Assert.Null(page.PublishedAt);
            });
    }

    [Fact]
    public async Task PublishMarketplacePageAsync_WhenOutboxPublishFails_DoesNotChangeLiveCmsTruth()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var pageGroupId = Guid.NewGuid();

        await using var seedContext = CreateDbContext(databaseName, Guid.NewGuid());
        SeedMarketplacePageVersions(seedContext, pageGroupId);
        await seedContext.SaveChangesAsync();

        await using var testContext = CreateDbContext(databaseName, Guid.NewGuid());
        var publisher = new Mock<IEventPublisher>();
        publisher
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<MarketplaceCmsPagePublishedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));

        var service = CreateMarketplaceService(testContext, publisher.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishPageAsync(
            pageGroupId,
            2,
            CancellationToken.None));

        await using var verifyContext = CreateDbContext(databaseName, Guid.NewGuid());
        var versions = await verifyContext.MarketplaceCmsPages
            .Where(page => page.PageGroupId == pageGroupId)
            .OrderBy(page => page.Version)
            .ToListAsync();

        Assert.Collection(
            versions,
            page =>
            {
                Assert.True(page.IsPublished);
                Assert.False(page.IsDraft);
                Assert.Equal(1, page.Version);
            },
            page =>
            {
                Assert.False(page.IsPublished);
                Assert.True(page.IsDraft);
                Assert.Equal(2, page.Version);
                Assert.Null(page.PublishedAt);
            });
    }

    private static ApplicationDbContext CreateDbContext(string databaseName, Guid tenantId)
    {
        var databaseRoot = DatabaseRoots.GetOrAdd(databaseName, _ => new InMemoryDatabaseRoot());
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static CmsPageService CreateStorefrontService(ApplicationDbContext dbContext, IEventPublisher publisher)
    {
        return new CmsPageService(
            dbContext,
            new CmsPageContentValidator(),
            new InMemoryCmsPageCacheService(),
            publisher,
            NullLogger<CmsPageService>.Instance);
    }

    private static MarketplaceCmsPageService CreateMarketplaceService(ApplicationDbContext dbContext, IEventPublisher publisher)
    {
        return new MarketplaceCmsPageService(
            dbContext,
            new MarketplaceCmsPageContentValidator(),
            new InMemoryMarketplaceCmsPageCacheService(),
            publisher,
            NullLogger<MarketplaceCmsPageService>.Instance);
    }

    private static async Task SeedTenantAsync(ApplicationDbContext dbContext, Guid tenantId)
    {
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Reliable CMS Tenant",
            Slug = "reliable-cms-tenant",
            Status = TenantStatus.Active,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();
    }

    private static void SeedStorefrontPageVersions(ApplicationDbContext dbContext, Guid tenantId, Guid pageGroupId)
    {
        var now = DateTime.UtcNow;
        dbContext.CmsPages.AddRange(
            new CmsPage
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PageGroupId = pageGroupId,
                Slug = "about",
                Title = "Live About",
                Content = "<p>Live</p>",
                ContentType = CmsPageContentType.RichText,
                Version = 1,
                SchemaVersion = 1,
                IsDraft = false,
                IsPublished = true,
                IsDeleted = false,
                PublishedAt = now.AddMinutes(-5),
                CreatedAt = now.AddMinutes(-10),
                UpdatedAt = now.AddMinutes(-5)
            },
            new CmsPage
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PageGroupId = pageGroupId,
                Slug = "about",
                Title = "Draft About",
                Content = "<p>Draft</p>",
                ContentType = CmsPageContentType.RichText,
                Version = 2,
                SchemaVersion = 1,
                IsDraft = true,
                IsPublished = false,
                IsDeleted = false,
                PublishedAt = null,
                CreatedAt = now.AddMinutes(-2),
                UpdatedAt = now.AddMinutes(-1)
            });
    }

    private static void SeedMarketplacePageVersions(ApplicationDbContext dbContext, Guid pageGroupId)
    {
        var now = DateTime.UtcNow;
        dbContext.MarketplaceCmsPages.AddRange(
            new MarketplaceCmsPage
            {
                Id = Guid.NewGuid(),
                PageGroupId = pageGroupId,
                Slug = "privacy",
                Title = "Live Privacy",
                Content = "<p>Live</p>",
                ContentType = CmsPageContentType.RichText,
                Version = 1,
                IsDraft = false,
                IsPublished = true,
                PublishedAt = now.AddMinutes(-5),
                CreatedAt = now.AddMinutes(-10),
                UpdatedAt = now.AddMinutes(-5)
            },
            new MarketplaceCmsPage
            {
                Id = Guid.NewGuid(),
                PageGroupId = pageGroupId,
                Slug = "privacy",
                Title = "Draft Privacy",
                Content = "<p>Draft</p>",
                ContentType = CmsPageContentType.RichText,
                Version = 2,
                IsDraft = true,
                IsPublished = false,
                PublishedAt = null,
                CreatedAt = now.AddMinutes(-2),
                UpdatedAt = now.AddMinutes(-1)
            });
    }

    private sealed class InMemoryCmsPageCacheService : ICmsPageCacheService
    {
        public Task<IReadOnlyList<PointOfSale.API.DTOs.Storefront.CmsPageDto>?> GetPublishedScopeAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PointOfSale.API.DTOs.Storefront.CmsPageDto>?>(null);

        public Task SetPublishedScopeAsync(
            Guid tenantId,
            IReadOnlyList<PointOfSale.API.DTOs.Storefront.CmsPageDto> pages,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InvalidatePublishedScopeAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class InMemoryMarketplaceCmsPageCacheService : IMarketplaceCmsPageCacheService
    {
        public Task<IReadOnlyList<PointOfSale.API.DTOs.Marketplace.MarketplaceCmsPageDto>?> GetPublishedPagesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PointOfSale.API.DTOs.Marketplace.MarketplaceCmsPageDto>?>(null);

        public Task SetPublishedPagesAsync(
            IReadOnlyList<PointOfSale.API.DTOs.Marketplace.MarketplaceCmsPageDto> pages,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InvalidatePublishedPagesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
