using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Localization;
using PointOfSale.Infrastructure.Data;
using System.Text.RegularExpressions;

namespace PointOfSale.Tests.Cms;

public sealed class FooterAuthoringRecoveryTests
{
    [Fact]
    public async Task Save_AdoptsResponseRevision_RejectsStaleRetry_AndStagesEventInsideTransaction()
    {
        await using var harness = await FooterHarness.CreateAsync();
        var tenantId = Guid.NewGuid();
        harness.Context.CmsPages.Add(CreatePublishedPage(tenantId, "about"));
        await harness.Context.SaveChangesAsync();

        var loaded = await harness.Service.GetConfigurationAsync(FooterAuthoringScope.Tenant, tenantId);
        var first = await harness.Service.SaveConfigurationAsync(
            FooterAuthoringScope.Tenant,
            tenantId,
            CreateRequest(loaded.Revision, "Company"));
        var second = await harness.Service.SaveConfigurationAsync(
            FooterAuthoringScope.Tenant,
            tenantId,
            CreateRequest(first.Revision, "Support"));

        Assert.NotEqual(loaded.Revision, first.Revision);
        Assert.NotEqual(first.Revision, second.Revision);
        Assert.Equal("Support", second.Sections.Single().Title);
        Assert.True(harness.Publisher.SawActiveTransaction);
        Assert.Equal(2, harness.Publisher.Events.OfType<StorefrontFooterUpdatedEvent>().Count());

        var conflict = await Assert.ThrowsAsync<FooterAuthoringException>(() =>
            harness.Service.SaveConfigurationAsync(
                FooterAuthoringScope.Tenant,
                tenantId,
                CreateRequest(first.Revision, "Stale")));
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal("FOOTER_CONCURRENCY_CONFLICT", conflict.Code);

        var unchanged = await harness.Service.GetConfigurationAsync(FooterAuthoringScope.Tenant, tenantId);
        Assert.Equal(second.Revision, unchanged.Revision);
        Assert.Equal("Support", unchanged.Sections.Single().Title);
    }

    [Fact]
    public async Task Validation_RejectsInvalidUrlsDuplicatesOrderingAndMissingPublishedTargetsWithoutPersistence()
    {
        await using var harness = await FooterHarness.CreateAsync();
        var tenantId = Guid.NewGuid();
        var loaded = await harness.Service.GetConfigurationAsync(FooterAuthoringScope.Tenant, tenantId);
        var request = CreateRequest(loaded.Revision, "Company");
        request.Sections![0].Links =
        [
            CreateLink(FooterLinkType.External, "Website", url: "example.com", order: 1),
            CreateLink(FooterLinkType.Cms, "Missing", cmsPageSlug: "missing", order: 0)
        ];
        request.SocialLinks =
        [
            CreateSocial("Instagram", "https://instagram.com/store", 0),
            CreateSocial("instagram", "https://instagram.com/other", 1)
        ];

        var failure = await Assert.ThrowsAsync<FooterAuthoringException>(() =>
            harness.Service.SaveConfigurationAsync(FooterAuthoringScope.Tenant, tenantId, request));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, failure.StatusCode);
        Assert.Equal("FOOTER_ORDER_INVALID", failure.Code);
        Assert.Empty(await harness.Context.StorefrontFooterSections.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(harness.Publisher.Events);

        request.Sections[0].Links = [CreateLink(FooterLinkType.Cms, "Missing", cmsPageSlug: "missing", order: 0)];
        failure = await Assert.ThrowsAsync<FooterAuthoringException>(() =>
            harness.Service.SaveConfigurationAsync(FooterAuthoringScope.Tenant, tenantId, request));
        Assert.Equal(StatusCodes.Status404NotFound, failure.StatusCode);
        Assert.Equal("FOOTER_CMS_TARGET_NOT_PUBLISHED", failure.Code);
    }

    [Fact]
    public async Task DuplicateSocialLinks_AreStructuredConflictsBeforeDatabaseConstraints()
    {
        await using var harness = await FooterHarness.CreateAsync();
        var loaded = await harness.Service.GetConfigurationAsync(FooterAuthoringScope.Global);
        var request = CreateRequest(loaded.Revision, "Company");
        request.SocialLinks =
        [
            CreateSocial("Instagram", "https://instagram.com/store", 0),
            CreateSocial("instagram", "https://instagram.com/other", 1)
        ];

        var failure = await Assert.ThrowsAsync<FooterAuthoringException>(() =>
            harness.Service.SaveConfigurationAsync(FooterAuthoringScope.Global, null, request));

        Assert.Equal(StatusCodes.Status409Conflict, failure.StatusCode);
        Assert.Equal("FOOTER_DUPLICATE_SOCIAL_LINK", failure.Code);
        Assert.Empty(await harness.Context.StorefrontSocialLinks.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task EventFailure_RollsBackTheCompleteFooterReplacement()
    {
        await using var harness = await FooterHarness.CreateAsync();
        var loaded = await harness.Service.GetConfigurationAsync(FooterAuthoringScope.Global);
        harness.Publisher.ThrowOnPublish = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.SaveConfigurationAsync(
                FooterAuthoringScope.Global,
                null,
                CreateRequest(loaded.Revision, "Must roll back")));

        Assert.Empty(await harness.Context.StorefrontFooterSections.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await harness.Context.StorefrontSocialLinks.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task GlobalAndMarketplaceScopes_RoundTripLocalizedSectionsThroughTheSameOwner()
    {
        await using var harness = await FooterHarness.CreateAsync();
        harness.Context.MarketplaceCmsPages.Add(CreateMarketplacePublishedPage("about"));
        await harness.Context.SaveChangesAsync();

        var global = await harness.Service.GetConfigurationAsync(FooterAuthoringScope.Global);
        var globalSaved = await harness.Service.SaveConfigurationAsync(
            FooterAuthoringScope.Global,
            null,
            CreateRequest(global.Revision, "Company", "الشركة"));
        var marketplace = await harness.Service.GetConfigurationAsync(FooterAuthoringScope.Marketplace);
        var marketplaceSaved = await harness.Service.SaveConfigurationAsync(
            FooterAuthoringScope.Marketplace,
            null,
            CreateRequest(marketplace.Revision, "Marketplace", "السوق"));

        Assert.Equal("الشركة", globalSaved.Sections.Single().TitleLocalized.Ar);
        Assert.Equal("السوق", marketplaceSaved.Sections.Single().TitleLocalized.Ar);
        Assert.Single(harness.Publisher.Events.OfType<StorefrontFooterUpdatedEvent>());
        Assert.Single(harness.Publisher.Events.OfType<MarketplaceFooterUpdatedEvent>());
    }

    private static AdminStorefrontFooterConfigurationRequest CreateRequest(
        string revision,
        string title,
        string? titleAr = null)
    {
        return new AdminStorefrontFooterConfigurationRequest
        {
            ExpectedRevision = revision,
            Sections =
            [
                new AdminStorefrontFooterSectionRequest
                {
                    Title = title,
                    TitleLocalized = new LocalizedField { En = title, Ar = titleAr },
                    Order = 0,
                    Links = [CreateLink(FooterLinkType.Internal, "Home", url: "/", order: 0)]
                }
            ],
            SocialLinks = [CreateSocial("Instagram", "https://instagram.com/store", 0)]
        };
    }

    private static AdminStorefrontFooterLinkRequest CreateLink(
        FooterLinkType type,
        string label,
        string? url = null,
        string? cmsPageSlug = null,
        int order = 0)
    {
        return new AdminStorefrontFooterLinkRequest
        {
            Label = label,
            LabelLocalized = new LocalizedField { En = label },
            Type = type,
            Url = url,
            CmsPageSlug = cmsPageSlug,
            Order = order
        };
    }

    private static AdminStorefrontSocialLinkRequest CreateSocial(string platform, string url, int order)
    {
        return new AdminStorefrontSocialLinkRequest
        {
            Platform = platform,
            Url = url,
            Order = order
        };
    }

    private static CmsPage CreatePublishedPage(Guid tenantId, string slug)
    {
        var now = DateTime.UtcNow;
        return new CmsPage
        {
            Id = Guid.NewGuid(),
            PageGroupId = Guid.NewGuid(),
            TenantId = tenantId,
            Slug = slug,
            Title = slug,
            Content = "<p>Page</p>",
            ContentType = CmsPageContentType.RichText,
            Version = 1,
            SchemaVersion = 1,
            IsDraft = false,
            IsPublished = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static MarketplaceCmsPage CreateMarketplacePublishedPage(string slug)
    {
        var now = DateTime.UtcNow;
        return new MarketplaceCmsPage
        {
            Id = Guid.NewGuid(),
            PageGroupId = Guid.NewGuid(),
            Slug = slug,
            Title = slug,
            Content = "<p>Page</p>",
            ContentType = CmsPageContentType.RichText,
            Version = 1,
            IsDraft = false,
            IsPublished = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private sealed class FooterHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext Context { get; }
        public RecordingEventPublisher Publisher { get; }
        public FooterAuthoringService Service { get; }

        private FooterHarness(SqliteConnection connection, ApplicationDbContext context)
        {
            _connection = connection;
            Context = context;
            Publisher = new RecordingEventPublisher(context);
            Service = new FooterAuthoringService(
                context,
                Publisher,
                new FooterAuthoringValidator(),
                new FooterAuthoringMapper());
        }

        public static async Task<FooterHarness> CreateAsync()
        {
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
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new FooterHarness(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class RecordingEventPublisher(ApplicationDbContext context) : IEventPublisher
    {
        public List<IntegrationEvent> Events { get; } = new();
        public bool SawActiveTransaction { get; private set; }
        public bool ThrowOnPublish { get; set; }

        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            SawActiveTransaction |= context.Database.CurrentTransaction != null;
            if (ThrowOnPublish)
            {
                throw new InvalidOperationException("Injected footer event failure.");
            }
            Events.Add(@event);
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
            where T : IntegrationEvent, ISensitiveIntegrationEvent => PublishAsync(@event, cancellationToken);
    }
}
