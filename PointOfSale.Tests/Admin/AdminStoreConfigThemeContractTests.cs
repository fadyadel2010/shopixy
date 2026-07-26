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
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class AdminStoreConfigThemeContractTests
{
    private static InMemoryDatabaseRoot CreateInMemoryDatabaseRoot() => new();

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
        Assert.Equal(18, response.DraftTheme.BorderRadius);

        Assert.Equal("dark-minimal", response.LiveTheme.PresetKey);
        Assert.Equal("#010203", response.LiveTheme.Colors.Background);
        Assert.Equal("#3b82f6", response.LiveTheme.Colors.Primary);
        Assert.Equal("Inter", response.LiveTheme.Font);
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
              "font": "Outfit",
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
              "font": "Tajawal",
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

    private static ApplicationDbContext CreateDbContext(string databaseName, Guid tenantId, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static AdminStoreConfigService CreateService(ApplicationDbContext context)
    {
        var publishEndpoint = new Mock<IEventPublisher>();
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
