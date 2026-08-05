using System.Reflection;
using System.Text.Json;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;

namespace PointOfSale.Tests.Architecture;

public sealed class BuilderRuntimeRecoveryTests
{
    [Fact]
    public void ProductTemplateAdmission_MustRequireOneEnabledProductHero()
    {
        AssertTemplateFailure(Array.Empty<Dictionary<string, object?>>(), "exactly one mandatory Product Hero");
        AssertTemplateFailure(new[] { BuildProductHero(enabled: false) }, "cannot be disabled");
        AssertTemplateFailure(new[] { BuildProductHero(), BuildProductHero("section_runtime_hero_two") }, "duplicate Product Hero");

        var normalized = NormalizePage(BuildPage(new[]
        {
            BuildProductHero(),
            BuildSection(
                "section_supplemental_specs",
                "product-specs",
                new Dictionary<string, object?>
                {
                    ["layoutVariant"] = "default",
                    ["title"] = "Details"
                })
        }), "default");

        var sections = normalized.RootElement.GetProperty("sections").EnumerateArray().ToArray();
        Assert.Equal(2, sections.Length);
        Assert.Equal("product-hero", sections[0].GetProperty("type").GetString());
        Assert.True(sections[0].GetProperty("enabled").GetBoolean());
        Assert.Equal(0, sections[0].GetProperty("order").GetInt32());
    }

    [Fact]
    public void BuilderAdmission_MustRejectBusinessTruthAndProductRuntimeReplacement()
    {
        AssertTemplateFailure(new[]
        {
            BuildProductHero(),
            BuildSection(
                "section_customer_review_truth",
                "customer-reviews",
                new Dictionary<string, object?>())
        }, "not supported");

        var homeWithRuntimeHero = BuildPage(new[] { BuildProductHero() });
        AssertFailure(homeWithRuntimeHero, authoritativeTemplateId: null, "only admitted for product templates");
    }

    [Fact]
    public void BuilderAdmission_MustValidateNestedTestimonialsUrlsAndGuidsSemantically()
    {
        AssertTemplateFailure(new[]
        {
            BuildProductHero(),
            BuildSection(
                "section_unsafe_testimonial",
                "testimonials",
                new Dictionary<string, object?>
                {
                    ["title"] = "Testimonials",
                    ["subtitle"] = string.Empty,
                    ["testimonials"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["name"] = "A",
                            ["avatar"] = "javascript:alert(1)",
                            ["text"] = "Quote"
                        }
                    }
                })
        }, "safe URL");

        AssertTemplateFailure(new[]
        {
            BuildProductHero(),
            BuildSection(
                "section_review_shaped_testimonial",
                "testimonials",
                new Dictionary<string, object?>
                {
                    ["title"] = "Testimonials",
                    ["subtitle"] = string.Empty,
                    ["testimonials"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["name"] = "A",
                            ["avatar"] = string.Empty,
                            ["text"] = "Quote",
                            ["rating"] = 5
                        }
                    }
                })
        }, "unsupported property 'rating'");

        AssertTemplateFailure(new[]
        {
            BuildProductHero(),
            BuildSection(
                "section_invalid_curated_ids",
                "product-grid",
                new Dictionary<string, object?>
                {
                    ["title"] = "Products",
                    ["subtitle"] = string.Empty,
                    ["layout"] = "grid",
                    ["source"] = "curated",
                    ["maxItems"] = 8,
                    ["productIds"] = new[] { "not-a-guid" }
                })
        }, "valid GUID");
    }

    [Fact]
    public void RuntimeMapping_MustPreserveLegacyLayoutsWithoutPreservingReviewOrCapabilityAuthority()
    {
        var config = new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            StoreName = "Legacy Builder Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            ThemeDraftOverridesJson = "{}",
            ThemeLiveOverridesJson = "{}",
            CustomizationJson = """
                {
                  "product_templates": {
                    "legacy": {
                      "live": {
                        "templateId": "legacy",
                        "name": "Legacy",
                        "sections": [
                          {
                            "id": "legacy_reviews",
                            "type": "customer-reviews",
                            "enabled": true,
                            "order": 1,
                            "settings": {
                              "layoutVariant": "default",
                              "title": "Customer Reviews",
                              "reviews": [
                                { "name": "A", "rating": 5, "text": "A quote", "avatar": "" }
                              ]
                            }
                          },
                          {
                            "id": "legacy_hero",
                            "type": "product-hero",
                            "enabled": true,
                            "order": 0,
                            "settings": {
                              "layoutVariant": "fashion",
                              "showDescription": false,
                              "showMediaGallery": false,
                              "showVariantSelector": false,
                              "showAddToCart": false,
                              "showSku": false,
                              "showStockStatus": false
                            }
                          }
                        ]
                      }
                    }
                  }
                }
                """
        };

        var runtime = StorefrontDraftConfigMapper.MapLive(config, null, "EGP", "EG");
        var sections = runtime.ProductTemplates["legacy"].Sections;
        var testimonial = Assert.Single(sections, section => section.Type == "testimonials");
        var hero = Assert.Single(sections, section => section.Type == "product-hero");
        var testimonialJson = JsonSerializer.Serialize(testimonial.Settings);
        var heroJson = JsonSerializer.Serialize(hero.Settings);

        Assert.DoesNotContain("rating", testimonialJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A quote", testimonialJson, StringComparison.Ordinal);
        Assert.Equal("{\"layoutVariant\":\"fashion\"}", heroJson);
    }

    [Fact]
    public void CanonicalBuilderContract_MustHaveNoReviewsCapabilityTogglesOrUnmappedFields()
    {
        using var document = JsonDocument.Parse(
            CertificationSource.ReadRepoFile("theme-authority/shopixy-theme-authority.json"));
        var contracts = document.RootElement.GetProperty("sectionContracts").EnumerateArray().ToArray();
        var serialized = JsonSerializer.Serialize(contracts);

        Assert.DoesNotContain("customer-reviews", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"reviews\"", serialized, StringComparison.OrdinalIgnoreCase);

        var forbiddenCapabilityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "showPrice", "showRating", "showAddToCart", "showDescription",
            "showMediaGallery", "showVariantSelector", "showSku", "showStockStatus",
            "showThumbnails", "maxImages", "useDescription", "items"
        };

        foreach (var contract in contracts)
        {
            var fields = contract.GetProperty("fields").EnumerateArray().ToArray();
            Assert.Equal(
                fields.Length,
                fields.Select(field => field.GetProperty("key").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.DoesNotContain(fields, field => forbiddenCapabilityKeys.Contains(field.GetProperty("key").GetString()!));
            Assert.All(fields, field => Assert.False(string.IsNullOrWhiteSpace(field.GetProperty("rendererConsumer").GetString())));
        }

        var expectedConsumerMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["hero-banner"] = ["title", "subtitle", "ctaText", "ctaLink", "backgroundImage", "backgroundColor", "overlayOpacity", "textColor", "alignment", "height"],
            ["product-grid"] = ["title", "subtitle", "layout", "source", "maxItems", "productIds"],
            ["featured-products"] = ["title", "subtitle", "displayStyle", "source", "maxItems", "productIds", "showBadge", "badgeText"],
            ["category-grid"] = ["title", "subtitle", "source", "maxItems", "categoryIds", "columns"],
            ["subcategory-grid"] = ["title", "subtitle", "source", "maxItems", "categoryIds", "columns"],
            ["newsletter-signup"] = ["heading", "subheading", "buttonText", "backgroundColor", "textColor"],
            ["text-block"] = ["heading", "body", "alignment", "maxWidth"],
            ["image-block"] = ["imageUrl", "altText", "caption", "aspectRatio", "maxWidth"],
            ["video-block"] = ["title", "videoUrl", "autoplay", "showControls", "aspectRatio"],
            ["testimonials"] = ["title", "subtitle", "testimonials"],
            ["product-hero"] = ["layoutVariant"],
            ["product-gallery"] = ["layoutVariant", "title"],
            ["product-specs"] = ["layoutVariant", "title"],
            ["product-video"] = ["title"],
            ["related-products"] = ["layoutVariant", "title", "limit"]
        };

        Assert.Equal(expectedConsumerMap.Keys.Order(), contracts.Select(contract => contract.GetProperty("type").GetString()).Order());
        foreach (var contract in contracts)
        {
            var type = contract.GetProperty("type").GetString()!;
            var actualFields = contract.GetProperty("fields").EnumerateArray()
                .Select(field => field.GetProperty("key").GetString())
                .ToArray();
            Assert.Equal(expectedConsumerMap[type], actualFields);
        }
    }

    [Fact]
    public void BuilderUi_MustExposeOnlyLiveBuilderModesAndNoBroadNativePreview()
    {
        var page = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");
        var topBar = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/components/builder/BuilderTopBar.jsx");
        var sidebar = CertificationSource.ReadRepoFile("PointOfSale.Web/src/components/Sidebar.jsx");
        var dashboard = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/StorefrontDashboard.jsx");
        var canvas = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/components/builder/BuilderCanvas.jsx");

        Assert.Contains("const validPages = ['home', 'product-template']", page, StringComparison.Ordinal);
        Assert.DoesNotContain("page=product'", topBar + sidebar + dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("page=category", topBar + sidebar + dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("ThemeNativeCanvas", canvas, StringComparison.Ordinal);
        Assert.DoesNotContain("config.apply", canvas, StringComparison.Ordinal);
    }

    [Fact]
    public void BuilderLifecycle_MustPublishTemplateDeletionAndRestorePreviewState()
    {
        var service = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");
        var provider = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/modules/storefront/components/builder/BuilderThemeProvider.jsx");

        var deleteStart = service.IndexOf("DeleteProductTemplateAsync(", StringComparison.Ordinal);
        var deleteEnd = service.IndexOf("/// <inheritdoc />", deleteStart + 1, StringComparison.Ordinal);
        Assert.True(deleteStart >= 0 && deleteEnd > deleteStart);
        var deleteMethod = service[deleteStart..deleteEnd];

        Assert.Contains("_publishLockService.TryAcquireAsync", deleteMethod, StringComparison.Ordinal);
        Assert.Contains("CreateAndAppendVersionSnapshot", deleteMethod, StringComparison.Ordinal);
        Assert.Contains("StoreConfigPublishAction.Published", deleteMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreConfigPublishAction.Updated", deleteMethod, StringComparison.Ordinal);
        Assert.Contains("setConfig(previousConfigRef.current)", provider, StringComparison.Ordinal);
        Assert.Contains("setResolvedTenant(previousTenantRef.current, { overwrite: true })", provider, StringComparison.Ordinal);
        Assert.Contains("clearResolvedTenant()", provider, StringComparison.Ordinal);
        Assert.Contains("return () =>", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductTemplateResolver_MustInjectRuntimeHeroAndDiscardAuthoredHeroes()
    {
        var resolver = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.ts");
        var runtime = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx");

        Assert.Contains("filter((section) => section.type !== 'product-hero'", resolver, StringComparison.Ordinal);
        Assert.Contains("type: 'product-hero'", resolver, StringComparison.Ordinal);
        Assert.Contains("enabled: true", resolver, StringComparison.Ordinal);
        Assert.Contains("const showDescription = true", runtime, StringComparison.Ordinal);
        Assert.Contains("const showMediaGallery = true", runtime, StringComparison.Ordinal);
        Assert.Contains("const showVariantSelector = true", runtime, StringComparison.Ordinal);
        Assert.Contains("const showAddToCart = true", runtime, StringComparison.Ordinal);
        Assert.Contains("const showSku = true", runtime, StringComparison.Ordinal);
        Assert.Contains("const showStockStatus = true", runtime, StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> BuildProductHero(
        string id = "section_runtime_hero_one",
        bool enabled = true) =>
        BuildSection(
            id,
            "product-hero",
            new Dictionary<string, object?> { ["layoutVariant"] = "default" },
            enabled);

    private static Dictionary<string, object?> BuildSection(
        string id,
        string type,
        Dictionary<string, object?> settings,
        bool enabled = true) =>
        new()
        {
            ["id"] = id,
            ["type"] = type,
            ["enabled"] = enabled,
            ["settings"] = settings
        };

    private static Dictionary<string, object> BuildPage(IEnumerable<Dictionary<string, object?>> sections) =>
        new()
        {
            ["templateId"] = "default",
            ["name"] = "Default Template",
            ["sections"] = sections.ToArray()
        };

    private static void AssertTemplateFailure(
        IEnumerable<Dictionary<string, object?>> sections,
        string expectedMessage) =>
        AssertFailure(BuildPage(sections), "default", expectedMessage);

    private static void AssertFailure(
        Dictionary<string, object> page,
        string? authoritativeTemplateId,
        string expectedMessage)
    {
        var exception = Assert.Throws<TargetInvocationException>(() =>
        {
            using var ignored = NormalizePage(page, authoritativeTemplateId);
        });
        var inner = Assert.IsType<ArgumentException>(exception.InnerException);
        Assert.Contains(expectedMessage, inner.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonDocument NormalizePage(
        Dictionary<string, object> page,
        string? authoritativeTemplateId)
    {
        var method = typeof(AdminStoreConfigService).GetMethod(
            "NormalizeBuilderPageComposition",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("NormalizeBuilderPageComposition was not found.");

        var result = method.Invoke(null, new object?[] { page, null, authoritativeTemplateId })
            ?? throw new InvalidOperationException("NormalizeBuilderPageComposition returned null.");
        return JsonDocument.Parse(JsonSerializer.Serialize(result));
    }
}
