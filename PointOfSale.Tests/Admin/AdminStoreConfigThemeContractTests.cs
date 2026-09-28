using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.Admin;
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

public sealed class AdminStoreConfigThemeContractTests
{
    // Database names already isolate test data. Reuse the EF provider root to avoid
    // crossing EF Core's internal service-provider limit in combined StoreConfig runs.
    private static InMemoryDatabaseRoot CreateInMemoryDatabaseRoot() => SharedInMemoryDatabaseRoot.Instance;

    [Fact]
    public async Task GetStoreConfigAsync_ReturnsMaterializedDraftAndLiveThemes()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var databaseRoot = CreateInMemoryDatabaseRoot();

        await using var seedContext = CreateDbContext(databaseName, tenantId, databaseRoot);
        SeedThemePresets(seedContext);
        seedContext.StoreConfigs.Add(CreateStoreConfig(
            tenantId,
            themePresetKey: "dark-minimal",
            themeDraftOverridesJson: """
            {
              "colors": {
                "primary": "#ff0055",
                "muted": "#556677"
              },
              "font": "Poppins",
              "borderRadius": 18
            }
            """,
            themeLiveOverridesJson: """
            {
              "colors": {
                "background": "#010203"
              }
            }
            """));
        await seedContext.SaveChangesAsync();

        await using var context = CreateDbContext(databaseName, tenantId, databaseRoot);
        var service = CreateService(context);

        var response = await service.GetStoreConfigAsync(tenantId, "trace-theme-contract", CancellationToken.None);

        Assert.Equal("dark-minimal", response.ThemePresetKey);
        Assert.Equal("dark-minimal", response.ThemeLivePresetKey);
        Assert.Equal("dark-minimal", response.DraftTheme.PresetKey);
        Assert.Equal("#ff0055", response.DraftTheme.Colors.Primary);
        Assert.Equal("#556677", response.DraftTheme.Colors.Muted);
        Assert.Equal("Poppins", response.DraftTheme.Font);
        Assert.Equal("16px", response.DraftTheme.BaseSize);
        Assert.Equal(18, response.DraftTheme.BorderRadius);

        Assert.Equal("dark-minimal", response.LiveTheme.PresetKey);
        Assert.Equal("#010203", response.LiveTheme.Colors.Background);
        Assert.Equal("#3b82f6", response.LiveTheme.Colors.Primary);
        Assert.Equal("Inter", response.LiveTheme.Font);
        Assert.Equal("16px", response.LiveTheme.BaseSize);
        Assert.Equal(12, response.LiveTheme.BorderRadius);
    }

    [Fact]
    public async Task GetStoreConfigAsync_WhenMissing_ReturnsDefaultCandidateWithoutPersistingSqlTruth()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var databaseRoot = CreateInMemoryDatabaseRoot();

        await using var seedContext = CreateDbContext(databaseName, tenantId, databaseRoot);
        SeedThemePresets(seedContext);
        await seedContext.SaveChangesAsync();

        await using var context = CreateDbContext(databaseName, tenantId, databaseRoot);
        var service = CreateService(context);

        var response = await service.GetStoreConfigAsync(tenantId, "trace-read-only-default", CancellationToken.None);

        Assert.Equal(tenantId, response.TenantId);
        Assert.Equal("My Store", response.StoreName);
        Assert.Equal("0", response.RowVersion);
        Assert.Equal(0, await context.StoreConfigs.CountAsync(item => item.TenantId == tenantId));
        Assert.DoesNotContain(
            context.ChangeTracker.Entries<StoreConfig>(),
            entry => entry.State != EntityState.Detached);
    }

    [Fact]
    public async Task UpdateThemeOverridesAsync_PersistsPresetSeparately_And_StripsPresetKeyFromOverrides()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var databaseRoot = CreateInMemoryDatabaseRoot();

        await using var seedContext = CreateDbContext(databaseName, tenantId, databaseRoot);
        SeedThemePresets(seedContext);
        seedContext.StoreConfigs.Add(CreateStoreConfig(tenantId));
        await seedContext.SaveChangesAsync();

        await using var context = CreateDbContext(databaseName, tenantId, databaseRoot);
        var service = CreateService(context);

        var response = await service.UpdateThemeOverridesAsync(
            tenantId,
            """
            {
              "presetKey": "dark-minimal",
              "colors": {
                "primary": "#111111",
                "secondary": "#222222"
              },
              "typography": {
                "fontFamily": "Outfit",
                "baseSize": "19px"
              },
              "borderRadius": 20
            }
            """,
            "trace-theme-update",
            CancellationToken.None);

        Assert.Equal("dark-minimal", response.ThemePresetKey);
        Assert.Equal("dark-minimal", response.DraftTheme.PresetKey);
        Assert.Equal("#111111", response.DraftTheme.Colors.Primary);
        Assert.Equal("#222222", response.DraftTheme.Colors.Secondary);
        Assert.Equal("Outfit", response.DraftTheme.Font);
        Assert.Equal("19px", response.DraftTheme.BaseSize);
        Assert.Equal(20, response.DraftTheme.BorderRadius);
        Assert.DoesNotContain("presetKey", response.ThemeDraftOverridesJson, StringComparison.OrdinalIgnoreCase);

        await using var verifyContext = CreateDbContext(databaseName, tenantId, databaseRoot);
        var config = await verifyContext.StoreConfigs.SingleAsync(item => item.TenantId == tenantId);

        Assert.Equal("dark-minimal", config.ThemePresetKey);
        Assert.Equal("default", config.ThemeLivePresetKey);
        Assert.DoesNotContain("presetKey", config.ThemeDraftOverridesJson, StringComparison.OrdinalIgnoreCase);
        
        // Parse JSON and assert semantic content instead of exact serialized format
        using var jsonDoc = JsonDocument.Parse(config.ThemeDraftOverridesJson);
        var primaryColor = jsonDoc.RootElement.GetProperty("colors").GetProperty("primary").GetString();
        Assert.Equal("#111111", primaryColor);
    }

    [Fact]
    public async Task PublishThemeAsync_PromotesCurrentPresetAndDraftThemeToLive()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var databaseRoot = CreateInMemoryDatabaseRoot();

        await using var seedContext = CreateDbContext(databaseName, tenantId, databaseRoot);
        SeedThemePresets(seedContext);
        seedContext.StoreConfigs.Add(CreateStoreConfig(
            tenantId,
            themePresetKey: "default",
            themeDraftOverridesJson: """
            {
              "colors": {
                "primary": "#abcdef"
              },
              "font": "Cairo",
              "borderRadius": 14
            }
            """,
            themeLiveOverridesJson: "{}"));
        await seedContext.SaveChangesAsync();

        await using var context = CreateDbContext(databaseName, tenantId, databaseRoot);
        var service = CreateService(context);

        await service.UpdateThemeOverridesAsync(
            tenantId,
            """
            {
              "presetKey": "dark-minimal",
              "colors": {
                "primary": "#445566",
                "background": "#101820"
              },
              "typography": {
                "fontFamily": "Tajawal",
                "baseSize": "21px"
              },
              "borderRadius": 22
            }
            """,
            "trace-theme-select",
            CancellationToken.None);

        var response = await service.PublishThemeAsync(tenantId, "trace-theme-publish", CancellationToken.None);

        Assert.Equal("dark-minimal", response.ThemePresetKey);
        Assert.Equal("dark-minimal", response.LiveTheme.PresetKey);
        Assert.Equal("#445566", response.LiveTheme.Colors.Primary);
        Assert.Equal("#101820", response.LiveTheme.Colors.Background);
        Assert.Equal("Tajawal", response.LiveTheme.Font);
        Assert.Equal("21px", response.LiveTheme.BaseSize);
        Assert.Equal(22, response.LiveTheme.BorderRadius);

        await using var verifyContext = CreateDbContext(databaseName, tenantId, databaseRoot);
        var config = await verifyContext.StoreConfigs.SingleAsync(item => item.TenantId == tenantId);

        Assert.Equal("dark-minimal", config.ThemePresetKey);
        Assert.Equal("dark-minimal", config.ThemeLivePresetKey);
        Assert.Equal(config.ThemeDraftOverridesJson, config.ThemeLiveOverridesJson);
        Assert.DoesNotContain("presetKey", config.ThemeLiveOverridesJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectThemePresetAsync_DoesNotChangeLivePresetBeforePublish()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var databaseRoot = CreateInMemoryDatabaseRoot();

        await using var seedContext = CreateDbContext(databaseName, tenantId, databaseRoot);
        SeedThemePresets(seedContext);
        seedContext.StoreConfigs.Add(CreateStoreConfig(tenantId));
        await seedContext.SaveChangesAsync();

        await using var context = CreateDbContext(databaseName, tenantId, databaseRoot);
        var service = CreateService(context);

        var response = await service.SelectThemePresetAsync(
            tenantId,
            "dark-minimal",
            "trace-theme-draft-selection",
            CancellationToken.None);

        Assert.Equal("dark-minimal", response.ThemePresetKey);
        Assert.Equal("default", response.ThemeLivePresetKey);
        Assert.Equal("dark-minimal", response.DraftTheme.PresetKey);
        Assert.Equal("default", response.LiveTheme.PresetKey);
    }

    [Fact]
    public async Task ActivateThemeAsync_ChangesOnlyStorefrontThemeAndPublishesProjectionEvent()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var databaseRoot = CreateInMemoryDatabaseRoot();
        var storeConfigId = Guid.NewGuid();

        await using var seedContext = CreateDbContext(databaseName, tenantId, databaseRoot);
        var originalUpdatedAt = DateTime.UtcNow.AddMinutes(-10);
        seedContext.StoreConfigs.Add(new StoreConfig
        {
            Id = storeConfigId,
            TenantId = tenantId,
            StoreName = "Theme Isolation Store",
            StoreDescription = "Preserve this description",
            ThemeId = "default",
            ThemePresetKey = "default",
            ThemeLivePresetKey = "default",
            ThemeDraftOverridesJson = """{"colors":{"primary":"#111111"}}""",
            ThemeLiveOverridesJson = """{"colors":{"background":"#eeeeee"}}""",
            CustomizationJson = """
            {
              "storefrontTheme": "modern",
              "pages": {
                "home": {
                  "draft": {
                    "sections": []
                  },
                  "live": {
                    "sections": []
                  }
                }
              },
              "seo": {
                "title": "Keep me"
              }
            }
            """,
            Behavior = new StoreBehaviorConfig
            {
                EnableGuestCheckout = false,
                ShowOutOfStockProducts = true,
                EnableSearch = false,
                FailCheckoutOnPromotionError = true
            },
            CommerceAiEnabled = true,
            CommerceAiWidgetStyle = "sales",
            CommerceAiWidgetPosition = "middle-right",
            CommerceAiAutoGreetingEnabled = true,
            CommerceAiAutoGreetingMessage = "Welcome back",
            CommerceAiPersonality = "professional",
            StorefrontStatus = "maintenance",
            MaintenanceMessage = "Maintenance message",
            UpdatedAtUtc = originalUpdatedAt,
            CreatedAtUtc = originalUpdatedAt.AddDays(-1)
        });
        await seedContext.SaveChangesAsync();

        await using var context = CreateDbContext(databaseName, tenantId, databaseRoot);
        var eventPublisher = new Mock<IEventPublisher>();
        eventPublisher.SetReturnsDefault(Task.CompletedTask);
        var service = CreateThemeActivationService(context, eventPublisher);

        var response = await service.ActivateThemeAsync(
            tenantId,
            new AdminThemeActivationRequest { ThemeId = "minimal" },
            "trace-theme-activation",
            CancellationToken.None);

        Assert.Equal("minimal", response.StorefrontTheme);
        Assert.Equal("#0f172a", response.Theme.Colors.Primary);
        Assert.Equal("#eeeeee", response.Theme.Colors.Background);
        Assert.Equal("Inter", response.Theme.Font.Family);
        Assert.Equal("15px", response.Theme.Font.BaseSize);
        Assert.Equal("6px", response.Theme.Ui.BorderRadius);
        Assert.Equal("compact", response.Theme.Layout.ProductCardVariant);
        Assert.Equal("minimal", response.Theme.Layout.HeaderVariant);

        await using var verifyContext = CreateDbContext(databaseName, tenantId, databaseRoot);
        var config = await verifyContext.StoreConfigs.SingleAsync(item => item.TenantId == tenantId);

        Assert.Equal("Theme Isolation Store", config.StoreName);
        Assert.Equal("Preserve this description", config.StoreDescription);
        Assert.Equal("default", config.ThemeId);
        Assert.Equal("default", config.ThemePresetKey);
        Assert.Equal("default", config.ThemeLivePresetKey);
        Assert.Equal("""{"colors":{"primary":"#111111"}}""", config.ThemeDraftOverridesJson);
        Assert.Equal("""{"colors":{"background":"#eeeeee"}}""", config.ThemeLiveOverridesJson);
        Assert.False(config.Behavior.EnableGuestCheckout);
        Assert.True(config.Behavior.ShowOutOfStockProducts);
        Assert.False(config.Behavior.EnableSearch);
        Assert.Equal("maintenance", config.StorefrontStatus);
        Assert.Equal("Maintenance message", config.MaintenanceMessage);
        Assert.True(config.CommerceAiEnabled);
        Assert.Equal("sales", config.CommerceAiWidgetStyle);
        Assert.Equal("middle-right", config.CommerceAiWidgetPosition);
        Assert.True(config.CommerceAiAutoGreetingEnabled);
        Assert.Equal("Welcome back", config.CommerceAiAutoGreetingMessage);
        Assert.Equal("professional", config.CommerceAiPersonality);
        Assert.True(config.UpdatedAtUtc > originalUpdatedAt);

        using var customization = JsonDocument.Parse(config.CustomizationJson);
        Assert.Equal("minimal", customization.RootElement.GetProperty("storefrontTheme").GetString());
        Assert.True(customization.RootElement.TryGetProperty("pages", out _));
        Assert.Equal("Keep me", customization.RootElement.GetProperty("seo").GetProperty("title").GetString());

        var runtimeConfig = StorefrontDraftConfigMapper.MapLive(config, null, "EGP", "EG");
        Assert.Equal("minimal", runtimeConfig.StorefrontTheme);
        Assert.Equal("#0f172a", runtimeConfig.Theme.Colors.Primary);
        Assert.Equal("#eeeeee", runtimeConfig.Theme.Colors.Background);
        Assert.Equal("15px", runtimeConfig.Theme.Font.BaseSize);
        Assert.Equal("6px", runtimeConfig.Theme.Ui.BorderRadius);

        eventPublisher.Verify(
            publisher => publisher.PublishAsync(
                It.Is<StorefrontThemeActivatedEvent>(@event =>
                    @event.TenantId == tenantId &&
                    @event.StoreConfigId == storeConfigId &&
                    @event.PreviousThemeId == "modern" &&
                    @event.ThemeId == "minimal" &&
                    @event.StoreName == "Theme Isolation Store"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        eventPublisher.Verify(
            publisher => publisher.PublishAsync(
                It.Is<StoreConfigUpdatedEvent>(@event =>
                    @event.TenantId == tenantId &&
                    @event.StoreConfigId == storeConfigId &&
                    @event.StoreName == "Theme Isolation Store"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        eventPublisher.Verify(
            publisher => publisher.PublishAsync(
                It.Is<TenantUpdatedEvent>(@event =>
                    @event.TenantId == tenantId &&
                    @event.StoreName == "Theme Isolation Store"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateThemeOverridesAsync_RejectsUnsupportedAuthorityShapedFields()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var databaseRoot = CreateInMemoryDatabaseRoot();

        await using var seedContext = CreateDbContext(databaseName, tenantId, databaseRoot);
        SeedThemePresets(seedContext);
        seedContext.StoreConfigs.Add(CreateStoreConfig(tenantId));
        await seedContext.SaveChangesAsync();

        await using var context = CreateDbContext(databaseName, tenantId, databaseRoot);
        var service = CreateService(context);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateThemeOverridesAsync(
                tenantId,
                """{"currency":"USD","featureFlags":{"checkout":false}}""",
                "trace-theme-authority-override",
                CancellationToken.None));

        Assert.Contains("not supported", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" #abc")]
    [InlineData("#abcd")]
    [InlineData("#11223344")]
    [InlineData("rgb(1,2,3)")]
    [InlineData("javascript:alert(1)")]
    [InlineData("red; background:url(javascript:alert(1))")]
    public async Task UpdateThemeOverridesAsync_RejectsMalformedOrUnsafeColorsWithoutPersistence(string value)
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var databaseRoot = CreateInMemoryDatabaseRoot();

        await using var seedContext = CreateDbContext(databaseName, tenantId, databaseRoot);
        SeedThemePresets(seedContext);
        seedContext.StoreConfigs.Add(CreateStoreConfig(tenantId));
        await seedContext.SaveChangesAsync();

        await using var context = CreateDbContext(databaseName, tenantId, databaseRoot);
        var service = CreateService(context);
        var payload = JsonSerializer.Serialize(new { colors = new { primary = value } });

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateThemeOverridesAsync(
                tenantId,
                payload,
                "trace-invalid-color",
                CancellationToken.None));

        Assert.Contains("supported hex color", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{}", (await context.StoreConfigs.SingleAsync()).ThemeDraftOverridesJson);
    }

    [Fact]
    public async Task UpdateThemeOverridesAsync_AcceptsShorthandHexAndPreservesAuthoredCase()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var databaseRoot = CreateInMemoryDatabaseRoot();

        await using var seedContext = CreateDbContext(databaseName, tenantId, databaseRoot);
        SeedThemePresets(seedContext);
        seedContext.StoreConfigs.Add(CreateStoreConfig(tenantId));
        await seedContext.SaveChangesAsync();

        await using var context = CreateDbContext(databaseName, tenantId, databaseRoot);
        var service = CreateService(context);
        var response = await service.UpdateThemeOverridesAsync(
            tenantId,
            """{"colors":{"primary":"#AbC","secondary":"#A1b2C3"}}""",
            "trace-valid-shorthand",
            CancellationToken.None);

        Assert.Equal("#AbC", response.DraftTheme.Colors.Primary);
        Assert.Equal("#A1b2C3", response.DraftTheme.Colors.Secondary);
    }

    [Theory]
    [InlineData("11px")]
    [InlineData("25px")]
    [InlineData("16rem")]
    [InlineData("16px; color:red")]
    public async Task UpdateThemeOverridesAsync_RejectsUnsupportedBaseSize(string baseSize)
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var databaseRoot = CreateInMemoryDatabaseRoot();

        await using var seedContext = CreateDbContext(databaseName, tenantId, databaseRoot);
        SeedThemePresets(seedContext);
        seedContext.StoreConfigs.Add(CreateStoreConfig(tenantId));
        await seedContext.SaveChangesAsync();

        await using var context = CreateDbContext(databaseName, tenantId, databaseRoot);
        var service = CreateService(context);
        var payload = JsonSerializer.Serialize(new { typography = new { baseSize } });

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateThemeOverridesAsync(
                tenantId,
                payload,
                "trace-invalid-base-size",
                CancellationToken.None));

        Assert.Contains("between 12px and 24px", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{}", (await context.StoreConfigs.SingleAsync()).ThemeDraftOverridesJson);
    }

    [Theory]
    [InlineData("Jost")]
    [InlineData("Inter, sans-serif")]
    [InlineData("url(javascript:alert(1))")]
    [InlineData("A" + "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task UpdateThemeOverridesAsync_RejectsUnsupportedFontFamily(string fontFamily)
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var databaseRoot = CreateInMemoryDatabaseRoot();

        await using var seedContext = CreateDbContext(databaseName, tenantId, databaseRoot);
        SeedThemePresets(seedContext);
        seedContext.StoreConfigs.Add(CreateStoreConfig(tenantId));
        await seedContext.SaveChangesAsync();

        await using var context = CreateDbContext(databaseName, tenantId, databaseRoot);
        var service = CreateService(context);
        var payload = JsonSerializer.Serialize(new { typography = new { fontFamily } });

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateThemeOverridesAsync(
                tenantId,
                payload,
                "trace-invalid-font",
                CancellationToken.None));

        Assert.Contains("not supported", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{}", (await context.StoreConfigs.SingleAsync()).ThemeDraftOverridesJson);
    }

    private static ApplicationDbContext CreateDbContext(string databaseName, Guid tenantId, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static AdminStoreConfigService CreateService(
        ApplicationDbContext context,
        Mock<IEventPublisher>? publishEndpoint = null)
    {
        publishEndpoint ??= new Mock<IEventPublisher>();
        publishEndpoint.SetReturnsDefault(Task.CompletedTask);

        var publishLockService = new Mock<IPublishLockService>();
        publishLockService
            .Setup(service => service.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(PublishLockResult.Success("theme-contract-lock"));

        var auditService = new Mock<IStoreConfigAuditService>();
        auditService.SetReturnsDefault(Task.CompletedTask);

        var cmsPageReadService = new Mock<ICmsPageReadService>(MockBehavior.Strict);
        var previewService = new StorefrontPreviewService(
            context,
            cmsPageReadService.Object,
            NullLogger<StorefrontPreviewService>.Instance);

        return new AdminStoreConfigService(
            context,
            publishEndpoint.Object,
            publishLockService.Object,
            auditService.Object,
            previewService,
            new StorefrontPublishTelemetry(),
            NullLogger<AdminStoreConfigService>.Instance,
            new HttpContextAccessor());
    }

    private static AdminThemeActivationService CreateThemeActivationService(
        ApplicationDbContext context,
        Mock<IEventPublisher>? publishEndpoint = null)
    {
        publishEndpoint ??= new Mock<IEventPublisher>();
        publishEndpoint.SetReturnsDefault(Task.CompletedTask);

        var publishLockService = new Mock<IPublishLockService>();
        publishLockService
            .Setup(service => service.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(PublishLockResult.Success("theme-activation-lock"));

        var readService = CreateService(context, publishEndpoint);

        return new AdminThemeActivationService(
            context,
            publishEndpoint.Object,
            publishLockService.Object,
            readService,
            new StorefrontPublishTelemetry(),
            NullLogger<AdminThemeActivationService>.Instance);
    }

    private static void SeedThemePresets(ApplicationDbContext context)
    {
        context.ThemePresets.AddRange(
            new ThemePreset
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Key = "default",
                Name = "Default Modern",
                BaseThemeJson = """
                {
                  "colors": {
                    "primary": "#2563eb",
                    "secondary": "#0f172a",
                    "background": "#ffffff",
                    "text": "#020617",
                    "muted": "#64748b"
                  },
                  "typography": {
                    "fontFamily": "Inter",
                    "baseSize": "16px"
                  },
                  "radius": {
                    "base": "8px"
                  }
                }
                """,
                CreatedAtUtc = DateTime.UtcNow
            },
            new ThemePreset
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Key = "dark-minimal",
                Name = "Dark Minimal",
                BaseThemeJson = """
                {
                  "colors": {
                    "primary": "#3b82f6",
                    "secondary": "#1e293b",
                    "background": "#0f172a",
                    "text": "#f8fafc",
                    "muted": "#94a3b8"
                  },
                  "typography": {
                    "fontFamily": "Inter",
                    "baseSize": "16px"
                  },
                  "radius": {
                    "base": "12px"
                  }
                }
                """,
                CreatedAtUtc = DateTime.UtcNow
            });
    }

    private static StoreConfig CreateStoreConfig(
        Guid tenantId,
        string themePresetKey = "default",
        string? themeLivePresetKey = null,
        string themeDraftOverridesJson = "{}",
        string themeLiveOverridesJson = "{}")
    {
        return new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Theme Contract Store",
            ThemeId = "default",
            ThemePresetKey = themePresetKey,
            ThemeLivePresetKey = themeLivePresetKey ?? themePresetKey,
            ThemeDraftOverridesJson = themeDraftOverridesJson,
            ThemeLiveOverridesJson = themeLiveOverridesJson,
            CustomizationJson = "{}",
            UpdatedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
