using System.Text.Json.Nodes;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class AdminStoreConfigPublishReliabilityTests
{
    [Fact]
    public async Task FirstHomepageDraft_CreatesStoreConfigFromReadOnlyVersionZero_WithoutPublishing()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(databaseName, tenantId);
        var service = CreateService(context, new Mock<IEventPublisher>().Object);

        var initial = await service.GetStoreConfigAsync(tenantId, "initial");
        Assert.Equal("0", initial.RowVersion);
        Assert.Empty(await context.StoreConfigs.ToListAsync());

        var saved = await service.UpdatePageLayoutAsync(
            tenantId, "home", new Dictionary<string, object> { ["sections"] = Array.Empty<object>() },
            "first-draft", expectedRowVersion: 0);

        Assert.Equal(tenantId, saved.TenantId);
        var persisted = await context.StoreConfigs.SingleAsync(x => x.TenantId == tenantId);
        var home = JsonNode.Parse(persisted.CustomizationJson)!["pages"]!["home"]!;
        Assert.NotNull(home["draft"]?["sections"]);
        Assert.Null(home["live"]?["sections"]);
        Assert.Equal(0, persisted.LiveVersion);
    }

    [Fact]
    public async Task FirstHomepageDraft_RejectsStaleNonzeroVersionWithoutCreatingStoreConfig()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(Guid.NewGuid().ToString("N"), tenantId);
        var service = CreateService(context, new Mock<IEventPublisher>().Object);

        await Assert.ThrowsAsync<PointOfSale.API.Exceptions.ConcurrencyConflictException>(() =>
            service.UpdatePageLayoutAsync(tenantId, "home",
                new Dictionary<string, object> { ["sections"] = Array.Empty<object>() },
                "stale", expectedRowVersion: 1));
        Assert.Empty(await context.StoreConfigs.ToListAsync());
    }

    [Fact]
    public async Task CuratedHomepageDraft_RejectsForeignProducts_AndAcceptsOwnActiveProducts()
    {
        var tenantId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        var ownId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        await using var seedContext = CreateDbContext(databaseName, tenantId);
        seedContext.Products.AddRange(
            new Product { Id = foreignId, TenantId = Guid.NewGuid(), Name = "Foreign", CategoryId = Guid.NewGuid() },
            new Product { Id = ownId, TenantId = tenantId, Name = "Own", CategoryId = Guid.NewGuid() });
        await seedContext.SaveChangesAsync();

        Dictionary<string, object> Page(Guid id) => JsonSerializer.Deserialize<Dictionary<string, object>>($$"""
            { "sections": [{ "type": "featured-products", "settings": {
                "title": "Featured", "subtitle": "", "displayStyle": "grid", "source": "curated",
                "maxItems": 8, "productIds": ["{{id}}"], "showBadge": true, "badgeText": "Featured"
            } }] }
            """)!;

        await using (var foreignContext = CreateDbContext(databaseName, tenantId))
        {
            var foreignService = CreateService(foreignContext, new Mock<IEventPublisher>().Object);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                foreignService.UpdatePageLayoutAsync(tenantId, "home", Page(foreignId), "foreign", expectedRowVersion: 0));
        }

        await using var context = CreateDbContext(databaseName, tenantId);
        Assert.Empty(await context.StoreConfigs.ToListAsync());
        await CreateService(context, new Mock<IEventPublisher>().Object).UpdatePageLayoutAsync(
            tenantId, "home", Page(ownId), "own", expectedRowVersion: 0);
        var persisted = await context.StoreConfigs.SingleAsync();
        Assert.Equal(ownId.ToString(), JsonNode.Parse(persisted.CustomizationJson)!
            ["pages"]!["home"]!["draft"]!["sections"]![0]!["settings"]!["productIds"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task PublishPageAsync_WhenOutboxPublishFails_DoesNotPersistLiveMutation()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();

        await using var seedContext = CreateDbContext(databaseName, tenantId);
        seedContext.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Reliable Publish Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            CustomizationJson = BuildPageCustomizationJson(),
            UpdatedAtUtc = DateTime.UtcNow
        });
        await seedContext.SaveChangesAsync();

        await using var testContext = CreateDbContext(databaseName, tenantId);
        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<StoreConfigPublishedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));

        var service = CreateService(testContext, publishEndpoint.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishPageAsync(
            tenantId,
            "home",
            "trace-publish",
            CancellationToken.None));

        await using var verifyContext = CreateDbContext(databaseName, tenantId);
        var config = await verifyContext.StoreConfigs.SingleAsync(x => x.TenantId == tenantId);
        var root = JsonNode.Parse(config.CustomizationJson)!.AsObject();

        Assert.Equal("Live hero", root["pages"]?["home"]?["live"]?["title"]?.GetValue<string>());
        Assert.Equal("Draft hero", root["pages"]?["home"]?["draft"]?["title"]?.GetValue<string>());
        Assert.Equal(0, config.LiveVersion);
    }

    [Fact]
    public async Task HomepagePublication_StagesTenantScopedRevisionEvent_WhileDraftSaveCannotPublish()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(Guid.NewGuid().ToString("N"), tenantId);
        var config = new StoreConfig { Id = Guid.NewGuid(), TenantId = tenantId, StoreName = "Event fixture",
            ThemeId = "default", ThemePresetKey = "default", LiveVersion = 8,
            VersionsJson = "[{\"Version\":8}]", CustomizationJson = BuildPageCustomizationJson() };
        context.StoreConfigs.Add(config);
        await context.SaveChangesAsync();
        var publisher = new Mock<IEventPublisher>();
        StoreConfigPublishedEvent? captured = null;
        publisher.Setup(p => p.PublishAsync(It.IsAny<StoreConfigPublishedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<StoreConfigPublishedEvent, CancellationToken>((message, _) => captured = message)
            .Returns(Task.CompletedTask);
        var service = CreateService(context, publisher.Object);
        await service.UpdatePageLayoutAsync(tenantId, "home", new Dictionary<string, object> { ["sections"] = Array.Empty<object>() }, "draft");
        Assert.Null(captured);
        Assert.Equal(8, config.LiveVersion);
        await service.PublishPageAsync(tenantId, "home", "publish");
        Assert.NotNull(captured);
        Assert.Equal(tenantId, captured!.TenantId);
        Assert.Equal(config.Id, captured.StoreConfigId);
        Assert.Equal(9, captured.ConfigVersion);
        Assert.Equal(9, captured.EventVersion);
        Assert.Equal("publish", captured.CorrelationId);
        Assert.False(string.IsNullOrWhiteSpace(captured.IdempotencyKey));
        Assert.DoesNotContain("CustomizationJson", JsonSerializer.Serialize(captured));
        Assert.DoesNotContain("preview", JsonSerializer.Serialize(captured), StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateDbContext(string databaseName, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, SharedInMemoryDatabaseRoot.Instance)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static AdminStoreConfigService CreateService(ApplicationDbContext context, IEventPublisher publishEndpoint)
    {
        var publishLockService = new Mock<IPublishLockService>();
        publishLockService
            .Setup(x => x.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(PublishLockResult.Success("test-lock"));

        var auditService = new Mock<IStoreConfigAuditService>();
        auditService.SetReturnsDefault(Task.CompletedTask);

        var cmsPageReadService = new Mock<ICmsPageReadService>(MockBehavior.Strict);
        var previewService = new StorefrontPreviewService(
            context,
            cmsPageReadService.Object,
            NullLogger<StorefrontPreviewService>.Instance);

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };

        return new AdminStoreConfigService(
            context,
            publishEndpoint,
            publishLockService.Object,
            auditService.Object,
            previewService,
            new StorefrontPublishTelemetry(),
            NullLogger<AdminStoreConfigService>.Instance,
            httpContextAccessor);
    }

    private static string BuildPageCustomizationJson()
    {
        return new JsonObject
        {
            ["pages"] = new JsonObject
            {
                ["home"] = new JsonObject
                {
                    ["draft"] = new JsonObject
                    {
                        ["title"] = "Draft hero",
                        ["sections"] = new JsonArray()
                    },
                    ["live"] = new JsonObject
                    {
                        ["title"] = "Live hero",
                        ["sections"] = new JsonArray()
                    }
                }
            }
        }.ToJsonString();
    }
}
