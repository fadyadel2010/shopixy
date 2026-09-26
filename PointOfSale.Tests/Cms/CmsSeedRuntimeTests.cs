using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.Homepage;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Cms;

public sealed class CmsSeedRuntimeTests
{
    [Fact]
    public async Task FirstSeed_CreatesCompleteStorefrontCmsAndPublishesOneEventPerMutation()
    {
        await using var harness = await SeedHarness.CreateAsync(includeStoreConfig: true);

        var result = await harness.Service.SeedAsync(harness.TenantId, "seed-first");

        Assert.True(result.Success);
        Assert.Equal("Seeded", result.Status);
        Assert.Equal(new[] { "about", "contact", "privacy", "terms" }, result.SeededPages);
        Assert.True(result.NavigationCreated);
        Assert.True(result.HomepageCreated);
        Assert.True(result.FooterCreated);

        var pages = await harness.Context.CmsPages
            .AsNoTracking()
            .OrderBy(page => page.Slug)
            .ToListAsync();
        Assert.Equal(4, pages.Count);
        Assert.All(pages, page =>
        {
            Assert.True(page.IsPublished);
            Assert.False(page.IsDraft);
            Assert.Equal(1, page.Version);
        });

        var config = await harness.Context.StoreConfigs.AsNoTracking().SingleAsync();
        using var customization = JsonDocument.Parse(config.CustomizationJson);
        var navigation = customization.RootElement.GetProperty("navigation");
        Assert.Equal(2, navigation.GetProperty("live").GetProperty("header").GetProperty("links").GetArrayLength());
        Assert.Equal(4, navigation.GetProperty("live").GetProperty("footer").GetProperty("links").GetArrayLength());
        var homepageLayout = customization.RootElement.GetProperty("pages").GetProperty("home");
        Assert.Equal(4, homepageLayout.GetProperty("draft").GetProperty("sections").GetArrayLength());
        Assert.Equal(4, homepageLayout.GetProperty("live").GetProperty("sections").GetArrayLength());

        var homepage = await harness.Context.HomepageSections.AsNoTracking().OrderBy(section => section.Order).ToListAsync();
        Assert.Equal(4, homepage.Count);
        Assert.All(homepage, section => Assert.True(section.IsPublished));

        var footer = await harness.Context.StorefrontFooterSections
            .AsNoTracking()
            .Include(section => section.Links)
            .SingleAsync();
        Assert.Equal(4, footer.Links.Count);
        Assert.Equal(8, harness.Publisher.Events.Count);
        Assert.Equal(4, harness.Publisher.Events.OfType<StorefrontCmsPagePublishedEvent>().Count());
        Assert.Single(harness.Publisher.Events.OfType<NavigationPublishedEvent>());
        Assert.Single(harness.Publisher.Events.OfType<HomepageSectionsPublishedEvent>());
        Assert.Single(harness.Publisher.Events.OfType<StoreConfigPublishedEvent>());
        Assert.Single(harness.Publisher.Events.OfType<StorefrontFooterUpdatedEvent>());
        Assert.Equal(
            harness.Publisher.Events.Count,
            harness.Publisher.Events.Select(@event => @event.IdempotencyKey).Distinct().Count());
    }

    [Fact]
    public async Task RepeatedSeed_ReturnsAlreadyCompletedWithoutDuplicatesOrEvents()
    {
        await using var harness = await SeedHarness.CreateAsync(includeStoreConfig: true);
        await harness.Service.SeedAsync(harness.TenantId, "seed-repeat-1");
        var eventCount = harness.Publisher.Events.Count;

        var error = await Assert.ThrowsAsync<CmsSeedException>(() =>
            harness.Service.SeedAsync(harness.TenantId, "seed-repeat-2"));

        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
        Assert.Equal("CMS_SEED_ALREADY_COMPLETED", error.Code);
        Assert.Equal(4, await harness.Context.CmsPages.CountAsync());
        Assert.Equal(4, await harness.Context.HomepageSections.CountAsync());
        Assert.Single(await harness.Context.StorefrontFooterSections.ToListAsync());
        Assert.Equal(4, await harness.Context.StorefrontFooterLinks.CountAsync());
        Assert.Single(await harness.Context.StoreConfigs.ToListAsync());
        Assert.Equal(eventCount, harness.Publisher.Events.Count);
    }

    [Fact]
    public async Task ExistingTransaction_IsJoinedWithoutNestedTransactionOrEarlyCommit()
    {
        await using var harness = await SeedHarness.CreateAsync(includeStoreConfig: true);
        await using var transaction = await harness.Context.Database.BeginTransactionAsync();

        var result = await harness.Service.SeedAsync(harness.TenantId, "seed-existing-transaction");

        Assert.True(result.Success);
        Assert.Same(transaction, harness.Context.Database.CurrentTransaction);
        await transaction.CommitAsync();
        Assert.Equal(4, await harness.Context.CmsPages.CountAsync());
    }

    [Fact]
    public async Task MissingStoreConfig_FailsBeforeAnyMutationOrEvent()
    {
        await using var harness = await SeedHarness.CreateAsync(includeStoreConfig: false);

        var error = await Assert.ThrowsAsync<CmsSeedException>(() =>
            harness.Service.SeedAsync(harness.TenantId, "seed-no-config"));

        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
        Assert.Equal("CMS_SEED_STORE_CONFIG_REQUIRED", error.Code);
        Assert.Empty(await harness.Context.CmsPages.ToListAsync());
        Assert.Empty(harness.Publisher.Events);
    }

    [Fact]
    public async Task InvalidTenant_ReturnsNotFoundWithoutMutation()
    {
        await using var harness = await SeedHarness.CreateAsync(includeStoreConfig: true);

        var error = await Assert.ThrowsAsync<CmsSeedException>(() =>
            harness.Service.SeedAsync(Guid.NewGuid(), "seed-invalid-tenant"));

        Assert.Equal(StatusCodes.Status404NotFound, error.StatusCode);
        Assert.Equal("CMS_SEED_TENANT_NOT_FOUND", error.Code);
        Assert.Empty(await harness.Context.CmsPages.ToListAsync());
    }

    [Fact]
    public async Task ExistingTenant_PreservesAuthoredCanonicalPageAndCreatesOnlyMissingContent()
    {
        await using var harness = await SeedHarness.CreateAsync(includeStoreConfig: true);
        var authoredPageId = Guid.NewGuid();
        harness.Context.CmsPages.Add(new CmsPage
        {
            Id = Guid.NewGuid(),
            PageGroupId = authoredPageId,
            TenantId = harness.TenantId,
            Slug = "about",
            Title = "Our Custom Story",
            Content = "<p>Do not replace me.</p>",
            ContentType = CmsPageContentType.RichText,
            Version = 7,
            SchemaVersion = 1,
            IsPublished = true,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.SeedAsync(harness.TenantId, "seed-existing");

        Assert.Equal(new[] { "contact", "privacy", "terms" }, result.SeededPages);
        var about = await harness.Context.CmsPages.SingleAsync(page => page.Slug == "about");
        Assert.Equal(authoredPageId, about.PageGroupId);
        Assert.Equal("Our Custom Story", about.Title);
        Assert.Equal(7, about.Version);
        Assert.Equal(4, await harness.Context.CmsPages.CountAsync());
    }

    [Fact]
    public async Task DuplicateCanonicalSlug_ReturnsStructuredConflictWithoutMutation()
    {
        await using var harness = await SeedHarness.CreateAsync(includeStoreConfig: true);
        var now = DateTime.UtcNow;
        harness.Context.CmsPages.AddRange(
            CreatePage(harness.TenantId, Guid.NewGuid(), "about", isPublished: true, now),
            CreatePage(harness.TenantId, Guid.NewGuid(), "about", isPublished: false, now));
        await harness.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<CmsSeedException>(() =>
            harness.Service.SeedAsync(harness.TenantId, "seed-duplicate"));

        Assert.Equal("CMS_SEED_DUPLICATE_CONTENT", error.Code);
        Assert.Equal(2, await harness.Context.CmsPages.CountAsync());
        Assert.Empty(await harness.Context.HomepageSections.ToListAsync());
        Assert.Empty(harness.Publisher.Events);
    }

    [Fact]
    public async Task DraftOnlyCanonicalPage_ReturnsContentConflictWithoutPublishingIt()
    {
        await using var harness = await SeedHarness.CreateAsync(includeStoreConfig: true);
        harness.Context.CmsPages.Add(CreatePage(
            harness.TenantId,
            Guid.NewGuid(),
            "about",
            isPublished: false,
            DateTime.UtcNow));
        await harness.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<CmsSeedException>(() =>
            harness.Service.SeedAsync(harness.TenantId, "seed-draft-conflict"));

        Assert.Equal("CMS_SEED_CONTENT_CONFLICT", error.Code);
        var draft = await harness.Context.CmsPages.SingleAsync();
        Assert.True(draft.IsDraft);
        Assert.False(draft.IsPublished);
        Assert.Empty(harness.Publisher.Events);
    }

    [Fact]
    public async Task ExistingCmsAggregates_ArePreservedWhileMissingPagesAreRepaired()
    {
        await using var harness = await SeedHarness.CreateAsync(includeStoreConfig: true);
        var config = await harness.Context.StoreConfigs.SingleAsync();
        config.CustomizationJson = "{\"navigation\":{\"draft\":{\"header\":{\"links\":[]},\"footer\":{\"links\":[]}},\"live\":{\"header\":{\"links\":[]},\"footer\":{\"links\":[]}}},\"pages\":{\"home\":{\"draft\":{\"sections\":[]},\"live\":{\"sections\":[]}}}}";
        harness.Context.HomepageSections.Add(new HomepageSection
        {
            Id = Guid.NewGuid(),
            SectionGroupId = Guid.NewGuid(),
            TenantId = harness.TenantId,
            Type = HomepageSectionTypes.Hero,
            Order = 0,
            Data = "{\"title\":\"Custom\"}",
            Version = 3,
            SchemaVersion = 1,
            IsPublished = true,
            IsActive = true,
            PublishedAt = DateTime.UtcNow
        });
        harness.Context.StorefrontFooterSections.Add(new StorefrontFooterSection
        {
            Id = Guid.NewGuid(),
            TenantId = harness.TenantId,
            Title = "Custom Footer",
            SortOrder = 0,
            IsActive = true
        });
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.SeedAsync(harness.TenantId, "seed-preserve");

        Assert.False(result.NavigationCreated);
        Assert.False(result.HomepageCreated);
        Assert.False(result.FooterCreated);
        Assert.Equal(4, result.SeededPages.Count);
        Assert.Single(await harness.Context.HomepageSections.ToListAsync());
        Assert.Equal("Custom Footer", (await harness.Context.StorefrontFooterSections.SingleAsync()).Title);
        Assert.Equal(4, harness.Publisher.Events.Count);
    }

    [Fact]
    public async Task EventFailure_RollsBackEverySeedMutationAndRecordsNoSuccessfulEvent()
    {
        await using var harness = await SeedHarness.CreateAsync(includeStoreConfig: true, failEventPublication: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.SeedAsync(harness.TenantId, "seed-event-failure"));

        Assert.Empty(await harness.Context.CmsPages.AsNoTracking().ToListAsync());
        Assert.Empty(await harness.Context.HomepageSections.AsNoTracking().ToListAsync());
        Assert.Empty(await harness.Context.StorefrontFooterSections.AsNoTracking().ToListAsync());
        var config = await harness.Context.StoreConfigs.AsNoTracking().SingleAsync();
        Assert.Equal("{}", config.CustomizationJson);
        Assert.Equal(0, config.LiveVersion);
        Assert.Empty(harness.Publisher.Events);
    }

    [Fact]
    public async Task MalformedStoreConfig_ReturnsValidationWithoutPartialCommit()
    {
        await using var harness = await SeedHarness.CreateAsync(includeStoreConfig: true);
        var config = await harness.Context.StoreConfigs.SingleAsync();
        config.CustomizationJson = "not-json";
        await harness.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<CmsSeedException>(() =>
            harness.Service.SeedAsync(harness.TenantId, "seed-validation"));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, error.StatusCode);
        Assert.Equal("CMS_SEED_VALIDATION_FAILED", error.Code);
        Assert.Empty(await harness.Context.CmsPages.ToListAsync());
        Assert.Empty(harness.Publisher.Events);
    }

    private static CmsPage CreatePage(
        Guid tenantId,
        Guid pageGroupId,
        string slug,
        bool isPublished,
        DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            PageGroupId = pageGroupId,
            TenantId = tenantId,
            Slug = slug,
            Title = slug,
            Content = "<p>Existing</p>",
            ContentType = CmsPageContentType.RichText,
            Version = 1,
            SchemaVersion = 1,
            IsDraft = !isPublished,
            IsPublished = isPublished,
            PublishedAt = isPublished ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };

    private sealed class SeedHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SeedHarness(
            SqliteConnection connection,
            ApplicationDbContext context,
            Guid tenantId,
            RecordingEventPublisher publisher)
        {
            _connection = connection;
            Context = context;
            TenantId = tenantId;
            Publisher = publisher;
            Service = new CmsSeedService(
                context,
                new CmsPageContentValidator(),
                new HomepageSectionSchemaRegistry(),
                publisher,
                NullLogger<CmsSeedService>.Instance);
        }

        public ApplicationDbContext Context { get; }
        public Guid TenantId { get; }
        public RecordingEventPublisher Publisher { get; }
        public CmsSeedService Service { get; }

        public static async Task<SeedHarness> CreateAsync(
            bool includeStoreConfig,
            bool failEventPublication = false)
        {
            var tenantId = Guid.NewGuid();
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateFunction<string?, string?>("btrim", value => value?.Trim());
            connection.CreateFunction<string?, string?, string?>(
                "btrim",
                (value, characters) => value?.Trim((characters ?? " ").ToCharArray()));
            connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
            connection.CreateFunction<string?, string, string, string, string?>(
                "regexp_replace",
                (value, pattern, replacement, _) => value == null
                    ? null
                    : Regex.Replace(value, pattern, replacement));
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
            await context.Database.EnsureCreatedAsync();
            context.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "CMS Seed Tenant",
                Slug = $"cms-seed-{tenantId:N}",
                Status = TenantStatus.Active,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });

            await context.SaveChangesAsync();
            if (includeStoreConfig)
            {
                var configId = Guid.NewGuid();
                var now = DateTime.UtcNow;
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "StoreConfigs" (
                        "Id", "TenantId", "StoreName", "ThemeId",
                        "Theme_Colors_Primary", "Theme_Colors_Secondary", "Theme_Colors_Background", "Theme_Colors_Text", "Theme_Colors_Muted",
                        "Theme_Font_Family", "Theme_Font_BaseSize", "Theme_Ui_BorderRadius", "Theme_Ui_ButtonStyle",
                        "Theme_Layout_ProductCardVariant", "Theme_Layout_HeaderVariant",
                        "Behavior_EnableGuestCheckout", "Behavior_ShowOutOfStockProducts", "Behavior_EnableSearch", "Behavior_FailCheckoutOnPromotionError",
                        "StorefrontStatus", "UpdatedAtUtc", "CreatedAtUtc", "CustomizationJson",
                        "ThemePresetKey", "ThemeLivePresetKey", "ThemeDraftOverridesJson", "ThemeLiveOverridesJson", "VersionsJson", "LiveVersion", "xmin",
                        "PromotionRanking_Enabled", "PromotionRanking_BaseBoost", "PromotionRanking_HighDiscountBoost", "PromotionRanking_HighDiscountThreshold",
                        "PromotionRanking_MaxTotalBoost", "PromotionRanking_EnableTimeDecay", "PromotionRanking_TimeDecayScaleDays",
                        "PromotionRanking_TimeDecayOffsetDays", "PromotionRanking_TimeDecayMinimum",
                        "CommerceAiEnabled", "CommerceAiWidgetStyle", "CommerceAiWidgetPosition", "CommerceAiAutoGreetingEnabled",
                        "CommerceAiAutoGreetingMessage", "CommerceAiPersonality")
                    VALUES (
                        {configId}, {tenantId}, {"CMS Seed Store"}, {"default"},
                        {"#2563eb"}, {"#0f172a"}, {"#ffffff"}, {"#020617"}, {"#64748b"},
                        {"Inter"}, {"16px"}, {"8px"}, {"solid"}, {"default"}, {"standard"},
                        {true}, {false}, {true}, {true}, {"disabled"}, {now}, {now}, {"{}"},
                        {"default"}, {"default"}, {"{}"}, {"{}"}, {"[]"}, {0}, {1},
                        {true}, {5.0}, {10.0}, {20.0}, {25.0}, {true}, {7.0}, {1.0}, {0.3},
                        {false}, {"minimal"}, {"bottom-right"}, {false}, {string.Empty}, {"friendly"})
                    """);
            }

            return new SeedHarness(
                connection,
                context,
                tenantId,
                new RecordingEventPublisher(failEventPublication));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class RecordingEventPublisher(bool failPublication) : IEventPublisher
    {
        public List<IntegrationEvent> Events { get; } = new();

        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent => PublishAsync((object)@event, cancellationToken);

        public Task PublishAsync(object @event, CancellationToken cancellationToken = default)
        {
            if (failPublication)
            {
                throw new InvalidOperationException("Outbox unavailable.");
            }

            Events.Add(Assert.IsAssignableFrom<IntegrationEvent>(@event));
            return Task.CompletedTask;
        }

        public async Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            foreach (var @event in events)
            {
                await PublishAsync(@event, cancellationToken);
            }
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent =>
            PublishAsync(@event, cancellationToken);
    }
}

public sealed class CmsSeedControllerErrorMappingTests
{
    [Theory]
    [InlineData(StatusCodes.Status404NotFound, "CMS_SEED_TENANT_NOT_FOUND")]
    [InlineData(StatusCodes.Status409Conflict, "CMS_SEED_STORE_CONFIG_REQUIRED")]
    [InlineData(StatusCodes.Status409Conflict, "CMS_SEED_ALREADY_COMPLETED")]
    [InlineData(StatusCodes.Status409Conflict, "CMS_SEED_DUPLICATE_CONTENT")]
    [InlineData(StatusCodes.Status409Conflict, "CMS_SEED_CONFLICT")]
    [InlineData(StatusCodes.Status422UnprocessableEntity, "CMS_SEED_VALIDATION_FAILED")]
    public async Task ExpectedSeedFailure_MapsToStructuredNon500Response(int statusCode, string code)
    {
        var tenantId = Guid.NewGuid();
        var seedService = new Mock<ICmsSeedService>();
        seedService
            .Setup(service => service.SeedAsync(tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CmsSeedException(statusCode, code, "Expected seed failure."));
        var controller = CreateController(tenantId, seedService.Object);

        var result = await controller.SeedDefaultPages();

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(statusCode, response.StatusCode);
        var error = Assert.IsType<AdminCmsApiErrorResponse>(response.Value);
        Assert.Equal(code, error.Code);
        Assert.Equal("Expected seed failure.", error.Message);
    }

    [Fact]
    public async Task UnexpectedSeedFailure_RemainsInternalError()
    {
        var tenantId = Guid.NewGuid();
        var seedService = new Mock<ICmsSeedService>();
        seedService
            .Setup(service => service.SeedAsync(tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected."));
        var controller = CreateController(tenantId, seedService.Object);

        var result = await controller.SeedDefaultPages();

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, response.StatusCode);
        Assert.Equal("INTERNAL_ERROR", Assert.IsType<AdminCmsApiErrorResponse>(response.Value).Code);
    }

    private static AdminCmsPagesController CreateController(Guid tenantId, ICmsSeedService seedService) =>
        new(
            Mock.Of<IAdminCmsService>(),
            seedService,
            new CommerceReliabilityTelemetry(),
            new TestTenantContext(tenantId),
            null!,
            NullLogger<AdminCmsPagesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "cms-seed-controller-test"
                }
            }
        };
}
