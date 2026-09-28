using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Storefront;

public sealed class StorefrontPreviewServiceTests
{
    [Fact]
    public async Task GetConfigPreviewAsync_ReturnsDraftConfigWithoutFallingBackToLive()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();

        await using var seedContext = CreateDbContext(databaseName, tenantId);
        seedContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Preview Tenant",
            Slug = "preview-tenant",
            Country = "US",
            Currency = "USD",
            IsActive = true
        });
        seedContext.ThemePresets.Add(new ThemePreset
        {
            Id = Guid.NewGuid(),
            Key = "default",
            Name = "Default",
            BaseThemeJson = """
            {
              "colors": {
                "primary": "#2563eb",
                "secondary": "#0f172a",
                "background": "#ffffff",
                "text": "#111827",
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
            """
        });
        seedContext.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Draft First Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            ThemeDraftOverridesJson = """
            {
              "colors": {
                "primary": "#ff0055",
                "text": "#112233"
              },
              "font": "Poppins",
              "borderRadius": 24
            }
            """,
            ThemeLiveOverridesJson = """
            {
              "colors": {
                "primary": "#00aa55"
              }
            }
            """,
            CustomizationJson = BuildCustomizationJson(),
            UpdatedAtUtc = new DateTime(2026, 4, 19, 10, 30, 0, DateTimeKind.Utc)
        });
        await seedContext.SaveChangesAsync();

        await using var testContext = CreateDbContext(databaseName, tenantId);
        var cmsPageReadService = new Mock<ICmsPageReadService>(MockBehavior.Strict);
        var service = new StorefrontPreviewService(
            testContext,
            cmsPageReadService.Object,
            NullLogger<StorefrontPreviewService>.Instance);

        var result = await service.GetConfigPreviewAsync(tenantId, CancellationToken.None);

        Assert.True(result.Found);
        Assert.NotNull(result.Config);

        var preview = result.Config!;
        Assert.Equal(tenantId, preview.TenantId);
        Assert.Equal("Draft First Store", preview.StoreName);
        Assert.Equal("USD", preview.Currency);
        Assert.Equal("US", preview.DefaultCountryCode);
        Assert.Equal("#ff0055", preview.Theme.Colors.Primary);
        Assert.Equal("#112233", preview.Theme.Colors.Text);
        Assert.Equal("Poppins", preview.Theme.Font.Family);
        Assert.Equal("24px", preview.Theme.Ui.BorderRadius);

        Assert.Single(preview.Pages.Home.Sections);
        Assert.Equal("draft-home", preview.Pages.Home.Sections[0].Id);
        Assert.Equal("Draft home hero", preview.Pages.Home.Sections[0].Settings["title"]);

        Assert.Empty(preview.Pages.Product.Sections);
        Assert.DoesNotContain(preview.ProductTemplates.Keys, key => string.Equals(key, "live-only", StringComparison.OrdinalIgnoreCase));
        Assert.True(preview.ProductTemplates.ContainsKey("featured"));
        Assert.Equal("draft-template", preview.ProductTemplates["featured"].Sections[0].Id);
    }

    [Fact]
    public async Task ThemePreview_ReturnsDraftAppearanceForRequestedThemeWithoutFallingBackToLive()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();

        await using var seedContext = CreateDbContext(databaseName, tenantId);
        seedContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Theme Preview Tenant",
            Slug = "theme-preview-tenant",
            Country = "EG",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        seedContext.ThemePresets.Add(new ThemePreset
        {
            Id = Guid.NewGuid(),
            Key = "default",
            Name = "Default",
            BaseThemeJson = "{}"
        });
        seedContext.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Draft Theme Preview Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            ThemeLivePresetKey = "default",
            ThemeDraftOverridesJson = """
            {
              "colors": {
                "primary": "#7c3aed",
                "secondary": "#0f766e",
                "background": "#fff7ed",
                "text": "#1c1917",
                "muted": "#78716c"
              },
              "typography": {
                "fontFamily": "Cairo",
                "baseSize": "18px"
              },
              "borderRadius": 14
            }
            """,
            ThemeLiveOverridesJson = """
            {
              "colors": { "primary": "#2563eb" },
              "typography": { "fontFamily": "Inter", "baseSize": "16px" },
              "borderRadius": 8
            }
            """,
            CustomizationJson = """{"storefrontTheme":"alizon"}"""
        });
        await seedContext.SaveChangesAsync();

        await using var testContext = CreateDbContext(databaseName, tenantId);
        var service = new StorefrontThemePreviewService(
            testContext,
            NullLogger<StorefrontThemePreviewService>.Instance);

        var result = await service.GetConfigAsync(tenantId, "minimal", CancellationToken.None);

        Assert.True(result.Found);
        Assert.NotNull(result.Config);
        Assert.Equal("minimal", result.Config.StorefrontTheme);
        Assert.Equal("#7c3aed", result.Config.Theme.Colors.Primary);
        Assert.Equal("#0f766e", result.Config.Theme.Colors.Secondary);
        Assert.Equal("#fff7ed", result.Config.Theme.Colors.Background);
        Assert.Equal("#1c1917", result.Config.Theme.Colors.Text);
        Assert.Equal("#78716c", result.Config.Theme.Colors.Muted);
        Assert.Equal("Cairo", result.Config.Theme.Font.Family);
        Assert.Equal("18px", result.Config.Theme.Font.BaseSize);
        Assert.Equal("14px", result.Config.Theme.Ui.BorderRadius);
    }

    private static ApplicationDbContext CreateDbContext(string databaseName, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, SharedInMemoryDatabaseRoot.Instance)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static string BuildCustomizationJson()
    {
        return new JsonObject
        {
            ["storefrontTheme"] = "modern",
            ["pages"] = new JsonObject
            {
                ["home"] = new JsonObject
                {
                    ["draft"] = new JsonObject
                    {
                        ["sections"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = "draft-home",
                                ["type"] = "hero-banner",
                                ["enabled"] = true,
                                ["order"] = 0,
                                ["settings"] = SectionSettings("hero-banner", "Draft home hero")
                            }
                        }
                    },
                    ["live"] = new JsonObject
                    {
                        ["sections"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = "live-home",
                                ["type"] = "hero-banner",
                                ["enabled"] = true,
                                ["order"] = 0,
                                ["settings"] = SectionSettings("hero-banner", "Live home hero")
                            }
                        }
                    }
                },
                ["product"] = new JsonObject
                {
                    ["live"] = new JsonObject
                    {
                        ["sections"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = "live-product",
                                ["type"] = "product-details",
                                ["enabled"] = true,
                                ["order"] = 0
                            }
                        }
                    }
                }
            },
            ["product_templates"] = new JsonObject
            {
                ["featured"] = new JsonObject
                {
                    ["draft"] = new JsonObject
                    {
                        ["templateId"] = "featured",
                        ["name"] = "Featured Draft",
                        ["sections"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = "draft-template",
                                ["type"] = "product-hero",
                                ["enabled"] = true,
                                ["order"] = 0,
                                ["settings"] = SectionSettings("product-hero")
                            }
                        }
                    },
                    ["live"] = new JsonObject
                    {
                        ["templateId"] = "featured",
                        ["name"] = "Featured Live",
                        ["sections"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = "live-template",
                                ["type"] = "product-hero",
                                ["enabled"] = true,
                                ["order"] = 0,
                                ["settings"] = SectionSettings("product-hero")
                            }
                        }
                    }
                },
                ["live-only"] = new JsonObject
                {
                    ["live"] = new JsonObject
                    {
                        ["templateId"] = "live-only",
                        ["name"] = "Live Only",
                        ["sections"] = new JsonArray()
                    }
                }
            }
        }.ToJsonString();
    }

    private static JsonNode SectionSettings(string sectionType, string? title = null)
    {
        var settings = JsonSerializer.SerializeToNode(
            StorefrontSectionContracts.All.Single(section => section.Type == sectionType).DefaultSettings)!;
        if (title != null)
        {
            settings["title"] = title;
        }

        return settings;
    }
}
