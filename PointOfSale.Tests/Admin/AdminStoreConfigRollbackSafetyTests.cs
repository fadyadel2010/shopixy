using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class AdminStoreConfigRollbackSafetyTests
{
    private static readonly ConcurrentDictionary<string, InMemoryDatabaseRoot> DatabaseRoots = new(StringComparer.Ordinal);

    [Fact]
    public async Task RollbackStoreConfigAsync_PreservesCurrentDraftsWhileRestoringSnapshotLiveState()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var snapshotCustomization = BuildCustomizationJson(
            liveHomeTitle: "Snapshot Home Live",
            draftHomeTitle: "Snapshot Home Draft",
            draftNavLabel: "Snapshot Draft Nav",
            liveNavLabel: "Snapshot Live Nav",
            draftTemplateName: null,
            liveTemplateName: "Snapshot Live Template");
        var currentCustomization = BuildCustomizationJson(
            liveHomeTitle: "Current Home Live",
            draftHomeTitle: "Current Home Draft",
            draftNavLabel: "Current Draft Nav",
            liveNavLabel: "Current Live Nav",
            draftTemplateName: "Current Draft Template",
            liveTemplateName: null);
        currentCustomization = AddDraftOnlyTemplate(
            currentCustomization,
            templateId: "cross-sell",
            draftTemplateName: "Current Draft Only Template");

        await SeedStoreConfigAsync(
            databaseName,
            tenantId,
            currentCustomization,
            CreateVersionsJson(snapshotCustomization),
            themePresetKey: "current-theme",
            themeLiveOverridesJson: "{\"palette\":\"current\"}");

        await using var rollbackContext = CreateDbContext(databaseName, tenantId);
        var service = CreateService(rollbackContext);

        await service.RollbackStoreConfigAsync(
            tenantId,
            versionNumber: 1,
            confirm: true,
            reason: "rollback safety test",
            traceId: "trace-rollback");

        await using var verifyContext = CreateDbContext(databaseName, tenantId);
        var config = await verifyContext.StoreConfigs.SingleAsync(x => x.TenantId == tenantId);
        var root = JsonNode.Parse(config.CustomizationJson)!.AsObject();

        Assert.Equal("current-theme", config.ThemePresetKey);
        Assert.Equal("snapshot-theme", config.ThemeLivePresetKey);
        Assert.Equal("{\"colors\":{\"primary\":\"#112233\"}}", config.ThemeLiveOverridesJson);
        Assert.Equal(2, config.LiveVersion);
        Assert.Equal("Snapshot Home Live", root["pages"]?["home"]?["live"]?["title"]?.GetValue<string>());
        Assert.Equal("Current Home Draft", root["pages"]?["home"]?["draft"]?["title"]?.GetValue<string>());
        Assert.Equal("Snapshot Live Nav", root["navigation"]?["live"]?["header"]?["links"]?[0]?["label"]?.GetValue<string>());
        Assert.Equal("Current Draft Nav", root["navigation"]?["draft"]?["header"]?["links"]?[0]?["label"]?.GetValue<string>());
        Assert.Equal("Current Draft Template", root["product_templates"]?["upsell"]?["draft"]?["name"]?.GetValue<string>());
        Assert.Equal("Snapshot Live Template", root["product_templates"]?["upsell"]?["live"]?["name"]?.GetValue<string>());
        Assert.Equal("Current Draft Only Template", root["product_templates"]?["cross-sell"]?["draft"]?["name"]?.GetValue<string>());
        Assert.True(root["product_templates"]?["cross-sell"]?["live"] is null);

        var versions = JsonNode.Parse(config.VersionsJson)!.AsArray();
        Assert.Equal(2, versions.Count);
        Assert.Equal(2, versions[1]?["Version"]?.GetValue<int>());
        Assert.Equal(1, versions[1]?["RestoredFromVersion"]?.GetValue<int>());
    }

    [Fact]
    public async Task RollbackStoreConfigAsync_PreservesLatestDraftWrittenByAnotherAdmin()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var snapshotCustomization = BuildCustomizationJson(
            liveHomeTitle: "Version One Live",
            draftHomeTitle: "Version One Draft",
            draftNavLabel: "Version One Draft Nav",
            liveNavLabel: "Version One Live Nav",
            draftTemplateName: null,
            liveTemplateName: null);
        var currentCustomization = BuildCustomizationJson(
            liveHomeTitle: "Current Live",
            draftHomeTitle: "Current Draft",
            draftNavLabel: "Current Draft Nav",
            liveNavLabel: "Current Live Nav",
            draftTemplateName: null,
            liveTemplateName: null);

        await SeedStoreConfigAsync(
            databaseName,
            tenantId,
            currentCustomization,
            CreateVersionsJson(snapshotCustomization),
            themePresetKey: "current-theme",
            themeLiveOverridesJson: "{\"palette\":\"current\"}");

        await using var adminAContext = CreateDbContext(databaseName, tenantId);
        await using var adminBContext = CreateDbContext(databaseName, tenantId);
        var adminAService = CreateService(adminAContext);
        var adminBService = CreateService(adminBContext);

        await adminAService.UpdatePageLayoutAsync(
            tenantId,
            "home",
            new Dictionary<string, object>
            {
                ["title"] = "Admin A Draft",
                ["sections"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["id"] = "hero",
                        ["type"] = "hero-banner"
                    }
                }
            },
            traceId: "admin-a");

        await adminBService.RollbackStoreConfigAsync(
            tenantId,
            versionNumber: 1,
            confirm: true,
            reason: "admin-b rollback",
            traceId: "admin-b");

        await using var verifyContext = CreateDbContext(databaseName, tenantId);
        var config = await verifyContext.StoreConfigs.SingleAsync(x => x.TenantId == tenantId);
        var root = JsonNode.Parse(config.CustomizationJson)!.AsObject();

        Assert.Equal("Version One Live", root["pages"]?["home"]?["live"]?["title"]?.GetValue<string>());
        Assert.Equal("hero-banner", root["pages"]?["home"]?["draft"]?["sections"]?[0]?["type"]?.GetValue<string>());
    }

    [Fact]
    public async Task GetVersionHistoryAsync_ReturnsStoredServerVersionsNewestFirst()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var customization = BuildCustomizationJson(
            liveHomeTitle: "Live Home",
            draftHomeTitle: "Draft Home",
            draftNavLabel: "Draft Nav",
            liveNavLabel: "Live Nav",
            draftTemplateName: null,
            liveTemplateName: null);

        var firstActorId = Guid.NewGuid();
        var secondActorId = Guid.NewGuid();
        var versionsJson = JsonSerializer.Serialize(new object[]
        {
            new
            {
                Version = 1,
                PublishedAt = DateTime.UtcNow.AddDays(-2),
                PublishedBy = "owner-one@example.com",
                PublishedByUserId = firstActorId.ToString("D"),
                ChangeType = "Theme Published",
                ThemePresetKey = "theme-one",
                ThemeLiveOverridesJson = "{\"palette\":\"one\"}",
                CustomizationJson = customization
            },
            new
            {
                Version = 2,
                PublishedAt = DateTime.UtcNow.AddDays(-1),
                PublishedBy = "owner-two@example.com",
                PublishedByUserId = secondActorId.ToString("D"),
                ChangeType = "Page Published",
                ThemePresetKey = "theme-two",
                ThemeLiveOverridesJson = "{\"palette\":\"two\"}",
                CustomizationJson = customization
            }
        });

        await SeedStoreConfigAsync(
            databaseName,
            tenantId,
            customization,
            versionsJson,
            themePresetKey: "theme-two",
            themeLiveOverridesJson: "{\"palette\":\"two\"}");

        await using var context = CreateDbContext(databaseName, tenantId);
        var service = CreateService(context);

        var history = await service.GetVersionHistoryAsync(tenantId, traceId: "trace-history");

        Assert.Equal(2, history.CurrentVersion);
        Assert.Equal(2, history.TotalVersions);
        Assert.Collection(
            history.Versions,
            first =>
            {
                Assert.Equal(2, first.Version);
                Assert.Equal("owner-two@example.com", first.PublishedBy);
                Assert.Equal(secondActorId.ToString("D"), first.PublishedByUserId);
                Assert.True(first.IsCurrent);
            },
            second =>
            {
                Assert.Equal(1, second.Version);
                Assert.Equal("owner-one@example.com", second.PublishedBy);
                Assert.Equal(firstActorId.ToString("D"), second.PublishedByUserId);
                Assert.False(second.IsCurrent);
            });
    }

    [Fact]
    public async Task PublishThemeAsync_StoresAuthenticatedActorInVersionHistory()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var customization = BuildCustomizationJson(
            liveHomeTitle: "Live Home",
            draftHomeTitle: "Draft Home",
            draftNavLabel: "Draft Nav",
            liveNavLabel: "Live Nav",
            draftTemplateName: null,
            liveTemplateName: null);

        await SeedStoreConfigAsync(
            databaseName,
            tenantId,
            customization,
            CreateVersionsJson(customization),
            themePresetKey: "theme-before",
            themeLiveOverridesJson: "{\"palette\":\"before\"}");

        var actorId = Guid.NewGuid();
        var httpContextAccessor = CreateHttpContextAccessor(
            actorId,
            userName: "admin-user",
            email: "admin@example.com");

        await using var context = CreateDbContext(databaseName, tenantId);
        var service = CreateService(context, httpContextAccessor);

        await service.PublishThemeAsync(tenantId, traceId: "trace-publish");

        await using var verifyContext = CreateDbContext(databaseName, tenantId);
        var verifyService = CreateService(verifyContext, httpContextAccessor);
        var history = await verifyService.GetVersionHistoryAsync(tenantId, traceId: "trace-history");

        var currentVersion = Assert.Single(history.Versions.Where(item => item.IsCurrent));
        Assert.Equal(2, currentVersion.Version);
        Assert.Equal(actorId.ToString("D"), currentVersion.PublishedByUserId);
        Assert.Contains("admin@example.com", currentVersion.PublishedBy, StringComparison.Ordinal);
        Assert.DoesNotContain("trace-publish", currentVersion.PublishedBy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RollbackStoreConfigAsync_WhenVersionDoesNotExist_LeavesLiveStateUntouched()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var customization = BuildCustomizationJson(
            liveHomeTitle: "Current Live",
            draftHomeTitle: "Current Draft",
            draftNavLabel: "Current Draft Nav",
            liveNavLabel: "Current Live Nav",
            draftTemplateName: null,
            liveTemplateName: null);

        await SeedStoreConfigAsync(
            databaseName,
            tenantId,
            customization,
            CreateVersionsJson(customization),
            themePresetKey: "current-theme",
            themeLiveOverridesJson: "{\"palette\":\"current\"}");

        await using var context = CreateDbContext(databaseName, tenantId);
        var service = CreateService(context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RollbackStoreConfigAsync(
                tenantId,
                versionNumber: 999,
                confirm: true,
                reason: "invalid test",
                traceId: "trace-invalid"));

        Assert.Contains("Version 999 not found", ex.Message, StringComparison.Ordinal);

        await using var verifyContext = CreateDbContext(databaseName, tenantId);
        var config = await verifyContext.StoreConfigs.SingleAsync(x => x.TenantId == tenantId);
        Assert.Equal(2, config.LiveVersion);
        Assert.Equal("current-theme", config.ThemePresetKey);
        Assert.Equal("{\"palette\":\"current\"}", config.ThemeLiveOverridesJson);
        Assert.Equal(customization, config.CustomizationJson);
    }

    [Fact]
    public async Task RollbackStoreConfigAsync_DoesNotResurrectHistoricalDraftWithoutCurrentDraft()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var snapshotCustomization = BuildCustomizationJson(
            liveHomeTitle: "Historical Live",
            draftHomeTitle: "Historical Draft",
            draftNavLabel: "Historical Draft Nav",
            liveNavLabel: "Historical Live Nav",
            draftTemplateName: "Historical Draft Template",
            liveTemplateName: "Historical Live Template");
        var currentCustomization = new JsonObject
        {
            ["pages"] = new JsonObject(),
            ["navigation"] = new JsonObject(),
            ["product_templates"] = new JsonObject()
        }.ToJsonString();

        await SeedStoreConfigAsync(
            databaseName,
            tenantId,
            currentCustomization,
            CreateVersionsJson(snapshotCustomization),
            themePresetKey: "current-theme",
            themeLiveOverridesJson: "{\"palette\":\"current\"}");

        await using var context = CreateDbContext(databaseName, tenantId);
        var service = CreateService(context);

        await service.RollbackStoreConfigAsync(
            tenantId,
            versionNumber: 1,
            confirm: true,
            reason: "draft resurrection test",
            traceId: "trace-draft");

        await using var verifyContext = CreateDbContext(databaseName, tenantId);
        var config = await verifyContext.StoreConfigs.SingleAsync(x => x.TenantId == tenantId);
        var root = JsonNode.Parse(config.CustomizationJson)!.AsObject();

        Assert.Equal("Historical Live", root["pages"]?["home"]?["live"]?["title"]?.GetValue<string>());
        Assert.Null(root["pages"]?["home"]?["draft"]);
        Assert.Null(root["navigation"]?["draft"]);
        Assert.Null(root["product_templates"]?["upsell"]?["draft"]);
    }

    [Fact]
    public async Task RollbackStoreConfigAsync_CreatesMonotonicPublicationOnRepeatedRestore()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var customization = BuildCustomizationJson(
            liveHomeTitle: "Live",
            draftHomeTitle: "Draft",
            draftNavLabel: "Draft Nav",
            liveNavLabel: "Live Nav",
            draftTemplateName: null,
            liveTemplateName: null);

        await SeedStoreConfigAsync(
            databaseName,
            tenantId,
            customization,
            CreateVersionsJson(customization),
            themePresetKey: "current-theme",
            themeLiveOverridesJson: "{\"palette\":\"current\"}");

        await using (var firstContext = CreateDbContext(databaseName, tenantId))
        {
            await CreateService(firstContext).RollbackStoreConfigAsync(
                tenantId, 1, true, "first", "trace-first");
        }

        await using (var secondContext = CreateDbContext(databaseName, tenantId))
        {
            await CreateService(secondContext).RollbackStoreConfigAsync(
                tenantId, 1, true, "second", "trace-second");
        }

        await using var verifyContext = CreateDbContext(databaseName, tenantId);
        var config = await verifyContext.StoreConfigs.SingleAsync(x => x.TenantId == tenantId);
        var versions = JsonNode.Parse(config.VersionsJson)!.AsArray();

        Assert.Equal(3, config.LiveVersion);
        Assert.Equal(new[] { 1, 2, 3 }, versions.Select(x => x!["Version"]!.GetValue<int>()).ToArray());
        Assert.All(versions.Skip(1), x => Assert.Equal(1, x!["RestoredFromVersion"]!.GetValue<int>()));
    }

    [Fact]
    public async Task RollbackStoreConfigAsync_WhenHistoricalThemePresetWasRetired_RejectsRestore()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var customization = BuildCustomizationJson(
            liveHomeTitle: "Live",
            draftHomeTitle: "Draft",
            draftNavLabel: "Draft Nav",
            liveNavLabel: "Live Nav",
            draftTemplateName: null,
            liveTemplateName: null);

        await SeedStoreConfigAsync(
            databaseName,
            tenantId,
            customization,
            CreateVersionsJson(customization).Replace("snapshot-theme", "retired-theme", StringComparison.Ordinal),
            themePresetKey: "current-theme",
            themeLiveOverridesJson: "{\"colors\":{\"primary\":\"#445566\"}}");

        await using var context = CreateDbContext(databaseName, tenantId);
        context.ThemePresets.Remove(await context.ThemePresets.SingleAsync(x => x.Key == "snapshot-theme"));
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(context).RollbackStoreConfigAsync(
                tenantId, 1, true, "retired theme", "trace-retired"));

        Assert.Contains("no longer available", ex.Message, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateDbContext(string databaseName, Guid tenantId)
    {
        var databaseRoot = DatabaseRoots.GetOrAdd(databaseName, _ => new InMemoryDatabaseRoot());
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static AdminStoreConfigService CreateService(
        ApplicationDbContext context,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        var eventPublisher = new Mock<IEventPublisher>();
        eventPublisher.SetReturnsDefault(Task.CompletedTask);

        var publishLockService = new Mock<IPublishLockService>();
        publishLockService.SetReturnsDefault(Task.FromResult(PublishLockResult.Success("test-lock")));

        var auditService = new Mock<IStoreConfigAuditService>();
        auditService.SetReturnsDefault(Task.CompletedTask);

        var cmsPageReadService = new Mock<ICmsPageReadService>(MockBehavior.Strict);
        var previewService = new StorefrontPreviewService(
            context,
            cmsPageReadService.Object,
            NullLogger<StorefrontPreviewService>.Instance);

        return new AdminStoreConfigService(
            context,
            eventPublisher.Object,
            publishLockService.Object,
            auditService.Object,
            previewService,
            new StorefrontPublishTelemetry(),
            NullLogger<AdminStoreConfigService>.Instance,
            httpContextAccessor ?? new HttpContextAccessor());
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(Guid userId, string userName, string email)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
                    new Claim(ClaimTypes.Name, userName),
                    new Claim(ClaimTypes.Email, email)
                },
                "TestAuth"))
        };

        return new HttpContextAccessor
        {
            HttpContext = httpContext
        };
    }

    private static async Task SeedStoreConfigAsync(
        string databaseName,
        Guid tenantId,
        string customizationJson,
        string versionsJson,
        string themePresetKey,
        string themeLiveOverridesJson)
    {
        await using var context = CreateDbContext(databaseName, tenantId);
        context.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Rollback Test Store",
            ThemeId = "default",
            ThemePresetKey = themePresetKey,
            ThemeLiveOverridesJson = themeLiveOverridesJson,
            ThemeDraftOverridesJson = "{\"palette\":\"draft\"}",
            StorefrontStatus = "live",
            CustomizationJson = customizationJson,
            VersionsJson = versionsJson,
            LiveVersion = 2,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            UpdatedAtUtc = DateTime.UtcNow
        });
        context.ThemePresets.Add(new ThemePreset
        {
            Id = Guid.NewGuid(),
            Key = "snapshot-theme",
            Name = "Snapshot Theme",
            BaseThemeJson = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        });

        await context.SaveChangesAsync();
    }

    private static string CreateVersionsJson(string snapshotCustomizationJson)
    {
        return JsonSerializer.Serialize(new[]
        {
            new
            {
                Version = 1,
                PublishedAt = DateTime.UtcNow.AddDays(-7),
                PublishedBy = "seed",
                ChangeType = "Page Published",
                ThemePresetKey = "snapshot-theme",
                ThemeLiveOverridesJson = "{\"colors\":{\"primary\":\"#112233\"}}",
                CustomizationJson = snapshotCustomizationJson
            }
        });
    }

    private static string BuildCustomizationJson(
        string liveHomeTitle,
        string draftHomeTitle,
        string draftNavLabel,
        string liveNavLabel,
        string? draftTemplateName,
        string? liveTemplateName)
    {
        var builder = new CustomizationBuilder()
            .WithHomeTitles(draftHomeTitle, liveHomeTitle)
            .WithNavLabels(draftNavLabel, liveNavLabel);

        if (draftTemplateName is not null || liveTemplateName is not null)
        {
            builder.WithTemplate("upsell", draftTemplateName, liveTemplateName);
        }

        return builder.Build();
    }

    private static string AddDraftOnlyTemplate(
        string customizationJson,
        string templateId,
        string draftTemplateName)
    {
        return CustomizationBuilder
            .FromJson(customizationJson)
            .AddDraftOnlyTemplate(templateId, draftTemplateName)
            .Build();
    }

    private sealed class CustomizationBuilder
    {
        private readonly JsonObject _root;
        private readonly JsonObject _templates;

        public CustomizationBuilder()
        {
            _root = new JsonObject
            {
                ["storefrontTheme"] = "modern",
                ["pages"] = new JsonObject
                {
                    ["home"] = new JsonObject
                    {
                        ["draft"] = new JsonObject
                        {
                            ["title"] = "Draft Home",
                            ["sections"] = new JsonArray
                            {
                                new JsonObject { ["id"] = "draft-hero", ["type"] = "hero" }
                            }
                        },
                        ["live"] = new JsonObject
                        {
                            ["title"] = "Live Home",
                            ["sections"] = new JsonArray
                            {
                                new JsonObject { ["id"] = "live-hero", ["type"] = "hero" }
                            }
                        }
                    }
                },
                ["navigation"] = new JsonObject
                {
                    ["draft"] = new JsonObject
                    {
                        ["header"] = new JsonObject
                        {
                            ["links"] = new JsonArray
                            {
                                new JsonObject { ["label"] = "Draft Nav", ["type"] = "internal", ["path"] = "/draft" }
                            }
                        },
                        ["footer"] = new JsonObject { ["links"] = new JsonArray() }
                    },
                    ["live"] = new JsonObject
                    {
                        ["header"] = new JsonObject
                        {
                            ["links"] = new JsonArray
                            {
                                new JsonObject { ["label"] = "Live Nav", ["type"] = "internal", ["path"] = "/live" }
                            }
                        },
                        ["footer"] = new JsonObject { ["links"] = new JsonArray() }
                    }
                }
            };

            _templates = new JsonObject();
            _root["product_templates"] = _templates;
        }

        private CustomizationBuilder(JsonObject root)
        {
            _root = root;
            _templates = root["product_templates"] as JsonObject ?? new JsonObject();
            _root["product_templates"] = _templates;
        }

        public static CustomizationBuilder FromJson(string json)
        {
            var root = JsonNode.Parse(json)!.AsObject();
            return new CustomizationBuilder(root);
        }

        public CustomizationBuilder WithHomeTitles(string draftTitle, string liveTitle)
        {
            _root["pages"]!["home"]!["draft"]!["title"] = draftTitle;
            _root["pages"]!["home"]!["live"]!["title"] = liveTitle;
            return this;
        }

        public CustomizationBuilder WithNavLabels(string draftLabel, string liveLabel)
        {
            _root["navigation"]!["draft"]!["header"]!["links"]![0]!["label"] = draftLabel;
            _root["navigation"]!["live"]!["header"]!["links"]![0]!["label"] = liveLabel;
            return this;
        }

        public CustomizationBuilder WithTemplate(string templateId, string? draftName, string? liveName)
        {
            if (draftName is null && liveName is null)
                return this;

            var template = new JsonObject();

            if (draftName is not null)
            {
                template["draft"] = new JsonObject
                {
                    ["templateId"] = templateId,
                    ["name"] = draftName,
                    ["sections"] = new JsonArray()
                };
            }

            if (liveName is not null)
            {
                template["live"] = new JsonObject
                {
                    ["templateId"] = templateId,
                    ["name"] = liveName,
                    ["sections"] = new JsonArray()
                };
            }

            _templates[templateId] = template;
            return this;
        }

        public CustomizationBuilder AddDraftOnlyTemplate(string templateId, string draftName)
        {
            _templates[templateId] = new JsonObject
            {
                ["draft"] = new JsonObject
                {
                    ["templateId"] = templateId,
                    ["name"] = draftName,
                    ["sections"] = new JsonArray()
                }
            };
            return this;
        }

        public string Build() => _root.ToJsonString();
    }
}
