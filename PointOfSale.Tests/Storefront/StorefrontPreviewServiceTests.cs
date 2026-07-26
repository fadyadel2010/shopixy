using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Storefront;

public sealed class StorefrontPreviewServiceTests
{
    private static readonly ConcurrentDictionary<string, InMemoryDatabaseRoot> DatabaseRoots = new(StringComparer.Ordinal);

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
        Assert.Equal("Draft home hero", preview.Pages.Home.Sections[0].Settings["headline"]);

        Assert.Empty(preview.Pages.Product.Sections);
        Assert.DoesNotContain(preview.ProductTemplates.Keys, key => string.Equals(key, "live-only", StringComparison.OrdinalIgnoreCase));
        Assert.True(preview.ProductTemplates.ContainsKey("featured"));
        Assert.Equal("draft-template", preview.ProductTemplates["featured"].Sections[0].Id);
    }

    private static ApplicationDbContext CreateDbContext(string databaseName, Guid tenantId)
    {
        var databaseRoot = DatabaseRoots.GetOrAdd(databaseName, _ => new InMemoryDatabaseRoot());
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
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
                                ["type"] = "hero",
                                ["enabled"] = true,
                                ["order"] = 0,
                                ["settings"] = new JsonObject
                                {
                                    ["headline"] = "Draft home hero"
                                }
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
                                ["type"] = "hero",
                                ["enabled"] = true,
                                ["order"] = 0,
                                ["settings"] = new JsonObject
                                {
                                    ["headline"] = "Live home hero"
                                }
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
                                ["type"] = "product-media",
                                ["enabled"] = true,
                                ["order"] = 0
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
                                ["type"] = "product-media",
                                ["enabled"] = true,
                                ["order"] = 0
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
}
