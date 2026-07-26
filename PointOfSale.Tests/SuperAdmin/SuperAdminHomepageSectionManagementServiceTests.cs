using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.DTOs.Homepage;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Homepage;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Configuration;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Tests.SuperAdmin;

public sealed class SuperAdminHomepageSectionManagementServiceTests
{
    [Fact]
    public async Task UpdateSectionAsync_CreatesDraftWithoutAffectingLiveHomepage()
    {
        using var dbContext = CreateDbContext();
        var liveSection = CreateHeroSection(order: 0, title: "Live Hero");
        dbContext.HomepageSections.Add(liveSection);
        await dbContext.SaveChangesAsync();

        var cacheService = CreateCacheService();
        var managementService = CreateManagementService(dbContext, cacheService);
        var readService = CreateReadService(dbContext, cacheService);

        await managementService.UpdateSectionAsync(
            liveSection.SectionGroupId,
            new HomepageSectionWriteRequest
            {
                Type = HomepageSectionTypes.Hero,
                Order = 0,
                SchemaVersion = 1,
                Data = CreateHeroData("Draft Hero"),
                IsActive = true,
                TenantId = null
            },
            actorId: null);

        var liveSections = await readService.GetActiveSectionsAsync(null, HomepageSectionReadMode.Published);
        var previewSections = await readService.GetActiveSectionsAsync(null, HomepageSectionReadMode.Preview);

        Assert.Single(liveSections);
        Assert.Single(previewSections);
        Assert.Equal("Live Hero", liveSections[0].Data.GetProperty("title").GetString());
        Assert.Equal("Draft Hero", previewSections[0].Data.GetProperty("title").GetString());
        Assert.True(previewSections[0].IsDraft);
        Assert.False(liveSections[0].IsDraft);
    }

    [Fact]
    public async Task PublishSectionsAsync_ReplacesLiveContentAndArchivesPreviousVersion()
    {
        using var dbContext = CreateDbContext();
        var liveSection = CreateHeroSection(order: 0, title: "Live Hero");
        dbContext.HomepageSections.Add(liveSection);
        await dbContext.SaveChangesAsync();

        var cacheService = CreateCacheService();
        var managementService = CreateManagementService(dbContext, cacheService);
        var readService = CreateReadService(dbContext, cacheService);

        await managementService.UpdateSectionAsync(
            liveSection.SectionGroupId,
            new HomepageSectionWriteRequest
            {
                Type = HomepageSectionTypes.Hero,
                Order = 0,
                SchemaVersion = 1,
                Data = CreateHeroData("Published Draft"),
                IsActive = true,
                TenantId = null
            },
            actorId: null);

        await managementService.PublishSectionsAsync(
            new PublishHomepageSectionsRequest(),
            actorId: null);

        var liveSections = await readService.GetActiveSectionsAsync(null, HomepageSectionReadMode.Published);
        var rows = await dbContext.HomepageSections
            .Where(section => section.SectionGroupId == liveSection.SectionGroupId)
            .OrderBy(section => section.Version)
            .ToListAsync();

        Assert.Single(liveSections);
        Assert.Equal("Published Draft", liveSections[0].Data.GetProperty("title").GetString());
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => !row.IsPublished && !row.IsDraft && row.Version == 1);
        Assert.Contains(rows, row => row.IsPublished && !row.IsDraft && row.Version == 2);
    }

    [Fact]
    public async Task ReorderSectionsAsync_NormalizesDraftOrderingWithoutChangingLiveUntilPublish()
    {
        using var dbContext = CreateDbContext();
        var first = CreateHeroSection(order: 0, title: "First");
        var second = CreateHeroSection(order: 1, title: "Second");
        var third = CreateHeroSection(order: 2, title: "Third");
        dbContext.HomepageSections.AddRange(first, second, third);
        await dbContext.SaveChangesAsync();

        var cacheService = CreateCacheService();
        var managementService = CreateManagementService(dbContext, cacheService);
        var readService = CreateReadService(dbContext, cacheService);

        var previewSections = await managementService.ReorderSectionsAsync(
            new ReorderHomepageSectionsRequest
            {
                SectionIds = [third.SectionGroupId, first.SectionGroupId, second.SectionGroupId]
            },
            actorId: null);

        var liveSections = await readService.GetActiveSectionsAsync(null, HomepageSectionReadMode.Published);
        var persistedDraftOrders = await dbContext.HomepageSections
            .Where(section => section.IsDraft && !section.IsDeleted)
            .OrderBy(section => section.Order)
            .Select(section => new { section.SectionGroupId, section.Order })
            .ToListAsync();

        Assert.Equal(
            new[] { third.SectionGroupId, first.SectionGroupId, second.SectionGroupId },
            previewSections.Select(section => section.Id).ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, previewSections.Select(section => section.Order).ToArray());
        Assert.Equal(new[] { first.SectionGroupId, second.SectionGroupId, third.SectionGroupId }, liveSections.Select(section => section.Id).ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, persistedDraftOrders.Select(item => item.Order).ToArray());
    }

    [Fact]
    public async Task ReadService_UsesTenantPublishedOverrideBeforeGlobalFallback()
    {
        using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();

        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Tenant One",
            Status = TenantStatus.Active
        });

        var globalSection = CreateHeroSection(order: 0, title: "Global Hero");
        var tenantSection = CreateHeroSection(order: 0, title: "Tenant Hero", tenantId: tenantId);
        dbContext.HomepageSections.AddRange(globalSection, tenantSection);
        await dbContext.SaveChangesAsync();

        var readService = CreateReadService(dbContext, CreateCacheService());

        var tenantSections = await readService.GetActiveSectionsAsync(tenantId, HomepageSectionReadMode.Published);
        var fallbackSections = await readService.GetActiveSectionsAsync(Guid.NewGuid(), HomepageSectionReadMode.Published);

        Assert.Single(tenantSections);
        Assert.Equal("Tenant Hero", tenantSections[0].Data.GetProperty("title").GetString());

        Assert.Single(fallbackSections);
        Assert.Equal("Global Hero", fallbackSections[0].Data.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ReadService_SkipsInvalidSectionsWithoutBreakingPage()
    {
        using var dbContext = CreateDbContext();

        dbContext.HomepageSections.AddRange(
            CreateHeroSection(order: 0, title: "Valid Hero"),
            new HomepageSection
            {
                Id = Guid.NewGuid(),
                SectionGroupId = Guid.NewGuid(),
                Type = "unknown",
                Order = 1,
                Data = JsonSerializer.Serialize(new { title = "Broken" }),
                Version = 1,
                SchemaVersion = 1,
                IsActive = true,
                IsDeleted = false,
                IsDraft = false,
                IsPublished = true,
                PublishedAt = DateTime.UtcNow,
                TenantId = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        await dbContext.SaveChangesAsync();

        var readService = CreateReadService(dbContext, CreateCacheService());
        var sections = await readService.GetActiveSectionsAsync(null, HomepageSectionReadMode.Published);

        Assert.Single(sections);
        Assert.Equal("Valid Hero", sections[0].Data.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ReadService_SanitizesCuratedProductIdsForSuppressedVendors()
    {
        using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var listingId = Guid.NewGuid();
        var occurredAtUtc = DateTime.UtcNow.AddMinutes(-5);

        await SeedMarketplaceListingAsync(dbContext, tenantId, listingId, occurredAtUtc);
        dbContext.HomepageSections.Add(
            new HomepageSection
            {
                Id = Guid.NewGuid(),
                SectionGroupId = Guid.NewGuid(),
                Type = HomepageSectionTypes.Products,
                Order = 0,
                Data = CreateProductsData(listingId),
                Version = 1,
                SchemaVersion = 1,
                IsActive = true,
                IsDeleted = false,
                IsDraft = false,
                IsPublished = true,
                PublishedAt = occurredAtUtc,
                TenantId = null,
                CreatedAt = occurredAtUtc,
                UpdatedAt = occurredAtUtc
            });
        await dbContext.SaveChangesAsync();

        var visibilityService = new Mock<ICommerceHubPublicVendorVisibilityService>();
        visibilityService
            .Setup(x => x.FilterVisibleListingIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        var readService = CreateReadService(dbContext, CreateCacheService(), visibilityService.Object);
        var sections = await readService.GetActiveSectionsAsync(null, HomepageSectionReadMode.Published);

        Assert.Single(sections);
        Assert.Equal(0, sections[0].Data.GetProperty("productIds").GetArrayLength());
    }

    [Fact]
    public async Task CreateSectionAsync_RejectsCuratedProductsForSuppressedVendors()
    {
        using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var listingId = Guid.NewGuid();
        var occurredAtUtc = DateTime.UtcNow.AddMinutes(-5);

        await SeedMarketplaceListingAsync(dbContext, tenantId, listingId, occurredAtUtc);

        var visibilityService = new Mock<ICommerceHubPublicVendorVisibilityService>();
        visibilityService
            .Setup(x => x.FilterVisibleListingIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        var managementService = CreateManagementService(dbContext, CreateCacheService(), visibilityService.Object);

        var exception = await Assert.ThrowsAsync<HomepageSectionManagementException>(() =>
            managementService.CreateSectionAsync(
                new HomepageSectionWriteRequest
                {
                    Type = HomepageSectionTypes.Products,
                    Order = 0,
                    SchemaVersion = 1,
                    Data = JsonSerializer.Deserialize<JsonElement>(CreateProductsData(listingId)),
                    IsActive = true,
                    TenantId = null
                },
                actorId: null));

        Assert.Equal("HOMEPAGE_SECTION_REFERENCE_INVALID", exception.Code);
    }

    [Fact]
    public async Task PublishSectionsAsync_InvalidatesPublishedCache()
    {
        using var dbContext = CreateDbContext();
        var liveSection = CreateHeroSection(order: 0, title: "Cached Live");
        dbContext.HomepageSections.Add(liveSection);
        await dbContext.SaveChangesAsync();

        var cacheService = CreateCacheService();
        var managementService = CreateManagementService(dbContext, cacheService);
        var readService = CreateReadService(dbContext, cacheService);

        var firstRead = await readService.GetActiveSectionsAsync(null, HomepageSectionReadMode.Published);
        Assert.Equal("Cached Live", firstRead[0].Data.GetProperty("title").GetString());

        await managementService.UpdateSectionAsync(
            liveSection.SectionGroupId,
            new HomepageSectionWriteRequest
            {
                Type = HomepageSectionTypes.Hero,
                Order = 0,
                SchemaVersion = 1,
                Data = CreateHeroData("Fresh Publish"),
                IsActive = true,
                TenantId = null
            },
            actorId: null);

        await managementService.PublishSectionsAsync(new PublishHomepageSectionsRequest(), actorId: null);

        var secondRead = await readService.GetActiveSectionsAsync(null, HomepageSectionReadMode.Published);
        Assert.Equal("Fresh Publish", secondRead[0].Data.GetProperty("title").GetString());
    }

    [Fact]
    public async Task PublishSectionsAsync_ClearsPublishedRowsBeforePromotingDraftRows()
    {
        using var dbContext = CreateSpyDbContext();
        var liveSection = CreateHeroSection(order: 0, title: "Live Hero");
        dbContext.HomepageSections.Add(liveSection);
        await dbContext.SaveChangesAsync();

        var cacheService = CreateCacheService();
        var managementService = CreateManagementService(dbContext, cacheService);

        await managementService.UpdateSectionAsync(
            liveSection.SectionGroupId,
            new HomepageSectionWriteRequest
            {
                Type = HomepageSectionTypes.Hero,
                Order = 0,
                SchemaVersion = 1,
                Data = CreateHeroData("Published Draft"),
                IsActive = true,
                TenantId = null
            },
            actorId: null);

        dbContext.ResetHomepageSectionSaveSnapshots();

        await managementService.PublishSectionsAsync(
            new PublishHomepageSectionsRequest(),
            actorId: null);

        Assert.Equal(2, dbContext.HomepageSectionSaveSnapshots.Count);

        var archivalBatch = dbContext.HomepageSectionSaveSnapshots[0];
        var promotionBatch = dbContext.HomepageSectionSaveSnapshots[1];

        Assert.Single(archivalBatch);
        Assert.Equal(liveSection.Id, archivalBatch[0].Id);
        Assert.Equal(liveSection.SectionGroupId, archivalBatch[0].SectionGroupId);
        Assert.False(archivalBatch[0].IsDraft);
        Assert.False(archivalBatch[0].IsPublished);

        Assert.Single(promotionBatch);
        Assert.NotEqual(liveSection.Id, promotionBatch[0].Id);
        Assert.Equal(liveSection.SectionGroupId, promotionBatch[0].SectionGroupId);
        Assert.False(promotionBatch[0].IsDraft);
        Assert.True(promotionBatch[0].IsPublished);
    }

    [Fact]
    public async Task PublishSectionsAsync_StagesProjectionEventBeforeFinalSave()
    {
        using var dbContext = CreateDbContext();
        var liveSection = CreateHeroSection(order: 0, title: "Live Hero");
        dbContext.HomepageSections.Add(liveSection);
        await dbContext.SaveChangesAsync();

        var cacheService = CreateCacheService();
        var publisher = new RecordingHomepageEventPublisher(dbContext);
        var managementService = CreateManagementService(dbContext, cacheService, eventPublisher: publisher);

        await managementService.UpdateSectionAsync(
            liveSection.SectionGroupId,
            new HomepageSectionWriteRequest
            {
                Type = HomepageSectionTypes.Hero,
                Order = 0,
                SchemaVersion = 1,
                Data = CreateHeroData("Published Draft"),
                IsActive = true,
                TenantId = null
            },
            actorId: null);

        await managementService.PublishSectionsAsync(
            new PublishHomepageSectionsRequest(),
            actorId: null);

        Assert.True(publisher.SawPendingHomepageMutationOnPublish);
        Assert.Single(publisher.PublishedEvents);
        Assert.Equal(1, publisher.PublishedEvents[0].SectionCount);
    }

    [Fact]
    public async Task SaveDraftAsync_ReplacesDraftWorkspaceWithoutChangingLiveHomepage()
    {
        using var dbContext = CreateDbContext();
        var firstLive = CreateHeroSection(order: 0, title: "Live Hero");
        var secondLive = CreateHeroSection(order: 1, title: "Live Secondary");
        dbContext.HomepageSections.AddRange(firstLive, secondLive);
        await dbContext.SaveChangesAsync();

        var cacheService = CreateCacheService();
        var managementService = CreateManagementService(dbContext, cacheService);
        var readService = CreateReadService(dbContext, cacheService);

        var savedDraft = await managementService.SaveDraftAsync(
            new SaveHomepageDraftRequest
            {
                Sections =
                [
                    new SaveHomepageDraftSectionRequest
                    {
                        Id = firstLive.SectionGroupId,
                        Type = HomepageSectionTypes.Hero,
                        Order = 0,
                        SchemaVersion = 1,
                        Data = CreateHeroData("Draft Hero"),
                        IsActive = true
                    },
                    new SaveHomepageDraftSectionRequest
                    {
                        Type = HomepageSectionTypes.Banner,
                        Order = 1,
                        SchemaVersion = 1,
                        Data = CreateBannerData("Draft Banner"),
                        IsActive = true
                    }
                ]
            },
            actorId: null);

        var liveSections = await readService.GetActiveSectionsAsync(null, HomepageSectionReadMode.Published);
        var previewSections = await readService.GetActiveSectionsAsync(null, HomepageSectionReadMode.Preview);
        var deletedDraftSection = await dbContext.HomepageSections
            .Where(section => section.IsDraft && section.SectionGroupId == secondLive.SectionGroupId)
            .SingleAsync();

        Assert.Equal(2, savedDraft.Count);
        Assert.Equal(new[] { "Draft Hero", "Draft Banner" }, previewSections.Select(section => section.Data.GetProperty("title").GetString()).ToArray());
        Assert.Equal(new[] { "Live Hero", "Live Secondary" }, liveSections.Select(section => section.Data.GetProperty("title").GetString()).ToArray());
        Assert.True(previewSections.All(section => section.IsDraft));
        Assert.False(liveSections.Any(section => section.IsDraft));
        Assert.True(deletedDraftSection.IsDeleted);
        Assert.False(deletedDraftSection.IsActive);
    }

    [Fact]
    public async Task HomepageTemplateSeeder_CreatesPublishedStarterHomepageForNewTenant()
    {
        using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Starter Tenant",
            Status = TenantStatus.Active
        });
        await dbContext.SaveChangesAsync();

        var cacheService = CreateCacheService();
        var managementService = CreateManagementService(dbContext, cacheService);
        var readService = CreateReadService(dbContext, cacheService);
        var templateSeeder = CreateTemplateSeeder(dbContext, managementService);

        var created = await templateSeeder.EnsurePublishedTemplateAsync(tenantId);
        var liveSections = await readService.GetActiveSectionsAsync(tenantId, HomepageSectionReadMode.Published);

        Assert.True(created);
        Assert.Equal(
            new[]
            {
                HomepageSectionTypes.Hero,
                HomepageSectionTypes.Categories,
                HomepageSectionTypes.Products,
                HomepageSectionTypes.Banner
            },
            liveSections.Select(section => section.Type).ToArray());
        Assert.True(liveSections.All(section => section.IsPublished && !section.IsDraft));
        Assert.Equal("Welcome to Shopixy", liveSections[0].Data.GetProperty("title").GetString());
        Assert.Equal(0, liveSections[1].Data.GetProperty("categoryIds").GetArrayLength());
        Assert.Equal(0, liveSections[2].Data.GetProperty("productIds").GetArrayLength());
    }

    [Fact]
    public async Task HomepageTemplateSeeder_DoesNotOverrideExistingHomepage()
    {
        using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Existing Tenant",
            Status = TenantStatus.Active
        });
        dbContext.HomepageSections.Add(CreateHeroSection(order: 0, title: "Existing Hero", tenantId: tenantId));
        await dbContext.SaveChangesAsync();

        var cacheService = CreateCacheService();
        var managementService = CreateManagementService(dbContext, cacheService);
        var readService = CreateReadService(dbContext, cacheService);
        var templateSeeder = CreateTemplateSeeder(dbContext, managementService);

        var created = await templateSeeder.EnsurePublishedTemplateAsync(tenantId);
        var liveSections = await readService.GetActiveSectionsAsync(tenantId, HomepageSectionReadMode.Published);

        Assert.False(created);
        Assert.Single(liveSections);
        Assert.Equal("Existing Hero", liveSections[0].Data.GetProperty("title").GetString());
    }

    [Fact]
    public async Task HomepageTemplateSeeder_GeneratedDraftCanBePublished()
    {
        using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Publish Tenant",
            Status = TenantStatus.Active
        });
        dbContext.HomepageSections.Add(CreateHeroSection(order: 0, title: "Old Hero", tenantId: tenantId));
        await dbContext.SaveChangesAsync();

        var cacheService = CreateCacheService();
        var managementService = CreateManagementService(dbContext, cacheService);
        var readService = CreateReadService(dbContext, cacheService);
        var templateSeeder = CreateTemplateSeeder(dbContext, managementService);

        var generatedDraft = await templateSeeder.GenerateDraftTemplateAsync(tenantId, overwriteExisting: true);
        await managementService.PublishSectionsAsync(
            new PublishHomepageSectionsRequest
            {
                TenantId = tenantId
            },
            actorId: null);

        var liveSections = await readService.GetActiveSectionsAsync(tenantId, HomepageSectionReadMode.Published);

        Assert.Equal(4, generatedDraft.Count);
        Assert.Equal(4, liveSections.Count);
        Assert.Equal(
            new[]
            {
                HomepageSectionTypes.Hero,
                HomepageSectionTypes.Categories,
                HomepageSectionTypes.Products,
                HomepageSectionTypes.Banner
            },
            liveSections.Select(section => section.Type).ToArray());
        Assert.Equal("Welcome to Shopixy", liveSections[0].Data.GetProperty("title").GetString());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static SpyApplicationDbContext CreateSpyDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SpyApplicationDbContext(options);
    }

    private static SuperAdminHomepageSectionManagementService CreateManagementService(
        ApplicationDbContext dbContext,
        IHomepageSectionCacheService cacheService,
        ICommerceHubPublicVendorVisibilityService? publicVendorVisibilityService = null,
        IEventPublisher? eventPublisher = null)
    {
        var mongoDbContext = new MongoDbContext(
            Options.Create(new MongoDbSettings
            {
                ConnectionString = "mongodb://127.0.0.1:27017",
                DatabaseName = $"shopixy-homepage-tests-{Guid.NewGuid():N}"
            }),
            NullLogger<MongoDbContext>.Instance);

        return new SuperAdminHomepageSectionManagementService(
            dbContext,
            mongoDbContext,
            new HomepageSectionSchemaRegistry(),
            cacheService,
            publicVendorVisibilityService ?? CreatePublicVendorVisibilityService(),
            eventPublisher ?? Mock.Of<IEventPublisher>(),
            NullLogger<SuperAdminHomepageSectionManagementService>.Instance);
    }

    private static HomepageSectionReadService CreateReadService(
        ApplicationDbContext dbContext,
        IHomepageSectionCacheService cacheService,
        ICommerceHubPublicVendorVisibilityService? publicVendorVisibilityService = null)
    {
        return new HomepageSectionReadService(
            dbContext,
            new HomepageSectionSchemaRegistry(),
            cacheService,
            publicVendorVisibilityService ?? CreatePublicVendorVisibilityService(),
            NullLogger<HomepageSectionReadService>.Instance);
    }

    private static HomepageTemplateSeeder CreateTemplateSeeder(
        ApplicationDbContext dbContext,
        ISuperAdminHomepageSectionManagementService managementService)
    {
        return new HomepageTemplateSeeder(
            dbContext,
            managementService,
            NullLogger<HomepageTemplateSeeder>.Instance);
    }

    private static IHomepageSectionCacheService CreateCacheService()
    {
        return new TestHomepageSectionCacheService();
    }

    private static ICommerceHubPublicVendorVisibilityService CreatePublicVendorVisibilityService()
    {
        var service = new Mock<ICommerceHubPublicVendorVisibilityService>();
        service
            .Setup(item => item.FilterVisibleListingIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) => ids.Where(id => id != Guid.Empty).ToHashSet());
        service
            .Setup(item => item.GetSuppressedVendorIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());
        service
            .Setup(item => item.IsVendorSuppressedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return service.Object;
    }

    private static HomepageSection CreateHeroSection(
        int order,
        string title,
        bool isActive = true,
        Guid? tenantId = null)
    {
        var timestamp = DateTime.UtcNow.AddMinutes(order);
        var sectionGroupId = Guid.NewGuid();

        return new HomepageSection
        {
            Id = Guid.NewGuid(),
            SectionGroupId = sectionGroupId,
            Type = HomepageSectionTypes.Hero,
            Order = order,
            Data = JsonSerializer.Serialize(new
            {
                title,
                subtitle = $"{title} subtitle",
                imageUrl = "https://cdn.shopixy.test/hero.jpg",
                ctaText = "Shop now",
                ctaLink = "/collections/new"
            }),
            Version = 1,
            SchemaVersion = 1,
            IsActive = isActive,
            IsDeleted = false,
            IsDraft = false,
            IsPublished = true,
            PublishedAt = timestamp,
            DraftGroupId = null,
            TenantId = tenantId,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    private static string CreateProductsData(Guid productId)
    {
        return JsonSerializer.Serialize(new
        {
            source = "curated",
            productIds = new[] { productId.ToString("D") },
            title = "Featured",
            subtitle = "Featured products",
            maxItems = 8,
            layout = "grid"
        });
    }

    private static async Task SeedMarketplaceListingAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid listingId,
        DateTime occurredAtUtc)
    {
        var categoryId = Guid.NewGuid();
        var commerceHubCategoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        dbContext.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = "Visibility Vendor",
                Currency = "EGP",
                Status = TenantStatus.Active
            },
            new Category
            {
                Id = categoryId,
                TenantId = tenantId,
                Name = "Shoes",
                CreatedAt = occurredAtUtc
            },
            new CommerceHubCategory
            {
                Id = commerceHubCategoryId,
                Name = "Marketplace Shoes",
                Slug = "marketplace-shoes",
                IsActive = true,
                CreatedAtUtc = occurredAtUtc
            },
            new Product
            {
                Id = productId,
                TenantId = tenantId,
                CategoryId = categoryId,
                Name = "Runner Pro",
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                TrackStock = false,
                CreatedAt = occurredAtUtc
            },
            new ProductVariant
            {
                Id = variantId,
                TenantId = tenantId,
                ProductId = productId,
                Price = 150m,
                OnlinePrice = 150m,
                IsActive = true,
                IsDefault = true,
                CreatedAt = occurredAtUtc
            });

        var listing = CommerceHubListing.Create(
            tenantId,
            productId,
            variantId,
            commerceHubCategoryId,
            commerceHubPrice: null);
        listing.Id = listingId;
        listing.CreatedAt = occurredAtUtc;

        dbContext.Add(listing);
        await dbContext.SaveChangesAsync();
    }

    private static JsonElement CreateHeroData(string title)
    {
        return JsonSerializer.SerializeToElement(new
        {
            title,
            subtitle = $"{title} subtitle",
            imageUrl = "https://cdn.shopixy.test/hero.jpg",
            ctaText = "Shop now",
            ctaLink = "/collections/new"
        });
    }

    private static JsonElement CreateBannerData(string title)
    {
        return JsonSerializer.SerializeToElement(new
        {
            title,
            subtitle = $"{title} subtitle",
            imageUrl = "https://cdn.shopixy.test/banner.jpg",
            ctaText = "Explore offer",
            ctaLink = "/collections/featured"
        });
    }

    private sealed class TestHomepageSectionCacheService : IHomepageSectionCacheService
    {
        private readonly Dictionary<string, IReadOnlyList<HomepageSectionDto>> _store = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<HomepageSectionDto>?> GetPublishedScopeAsync(
            Guid? tenantId,
            CancellationToken cancellationToken = default)
        {
            _store.TryGetValue(GetKey(tenantId), out var value);
            return Task.FromResult(value);
        }

        public Task SetPublishedScopeAsync(
            Guid? tenantId,
            IReadOnlyList<HomepageSectionDto> sections,
            CancellationToken cancellationToken = default)
        {
            _store[GetKey(tenantId)] = sections;
            return Task.CompletedTask;
        }

        public Task InvalidatePublishedScopeAsync(
            Guid? tenantId,
            CancellationToken cancellationToken = default)
        {
            _store.Remove(GetKey(tenantId));
            return Task.CompletedTask;
        }

        private static string GetKey(Guid? tenantId)
        {
            return tenantId?.ToString("N") ?? "global";
        }
    }

    private sealed class RecordingHomepageEventPublisher(ApplicationDbContext dbContext) : IEventPublisher
    {
        public List<HomepageSectionsPublishedEvent> PublishedEvents { get; } = [];

        public bool SawPendingHomepageMutationOnPublish { get; private set; }

        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            if (@event is HomepageSectionsPublishedEvent homepageEvent)
            {
                SawPendingHomepageMutationOnPublish = dbContext.ChangeTracker
                    .Entries<HomepageSection>()
                    .Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
                PublishedEvents.Add(homepageEvent);
            }

            return Task.CompletedTask;
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            foreach (var @event in events)
            {
                PublishAsync(@event, cancellationToken);
            }

            return Task.CompletedTask;
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SpyApplicationDbContext : ApplicationDbContext
    {
        public SpyApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public List<List<HomepageSectionSaveSnapshot>> HomepageSectionSaveSnapshots { get; } = [];

        public void ResetHomepageSectionSaveSnapshots()
        {
            HomepageSectionSaveSnapshots.Clear();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = ChangeTracker.Entries<HomepageSection>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(entry => new HomepageSectionSaveSnapshot(
                    entry.Entity.Id,
                    entry.Entity.SectionGroupId,
                    entry.Entity.IsDraft,
                    entry.Entity.IsPublished))
                .ToList();

            if (snapshot.Count > 0)
            {
                HomepageSectionSaveSnapshots.Add(snapshot);
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed record HomepageSectionSaveSnapshot(
        Guid Id,
        Guid SectionGroupId,
        bool IsDraft,
        bool IsPublished);
}
