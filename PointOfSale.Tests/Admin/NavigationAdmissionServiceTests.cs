using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Cms;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class NavigationAdmissionServiceTests
{
    [Fact]
    public async Task GetNavigationAsync_LegacySlugCandidates_AreAdmittedIntoCanonicalDraftIdentity()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var category = AddCategory(context, tenantId, "Shoes");
        var page = AddCmsPage(context, tenantId, "about", isPublished: false);
        context.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Legacy Navigation Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            CustomizationJson = $$"""
                {
                  "navigation": {
                    "draft": {
                      "header": { "links": [
                        { "label": "Home", "type": "internal", "path": "/" },
                        { "label": "Shoes", "type": "category", "slug": "{{category.Id:D}}" }
                      ] },
                      "footer": { "links": [
                        { "label": "About", "type": "cms", "slug": "about" }
                      ] }
                    }
                  }
                }
                """,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var loaded = await CreateAdminCmsService(context).GetNavigationAsync(
            tenantId,
            "trace-navigation-load");

        Assert.Equal("custom", loaded.Draft.Header[0].Type);
        Assert.Equal(category.Id, loaded.Draft.Header[1].TargetId);
        Assert.Equal(page.PageGroupId, loaded.Draft.Footer[0].TargetId);
    }

    [Fact]
    public async Task GetNavigationAsync_WhenStoreConfigHasNoNavigation_ReturnsValidEmptyFooterWithPersistedRowVersion()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        AddCmsPage(context, tenantId, "about", isPublished: true);
        AddCmsPage(context, tenantId, "contact", isPublished: true);
        AddCmsPage(context, tenantId, "privacy", isPublished: true);
        AddCmsPage(context, tenantId, "terms", isPublished: true);
        context.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Default Navigation Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            CustomizationJson = "{}",
            RowVersion = 17,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var loaded = await CreateAdminCmsService(context).GetNavigationAsync(
            tenantId,
            "trace-navigation-default-load");

        Assert.Equal("17", loaded.RowVersion);
        Assert.Equal("Home", loaded.Draft.Header[0].Label);
        Assert.Equal("collection", loaded.Draft.Header[1].Type);
        Assert.Empty(loaded.Draft.Footer);
        Assert.Empty(loaded.ValidationWarnings);
    }

    [Fact]
    public async Task UpdateNavigationAsync_AcceptedSave_PersistsCanonicalDraftAndReturnsIt()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var category = AddCategory(context, tenantId, "Shoes");
        context.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Navigation Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            CustomizationJson = "{}",
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var service = CreateAdminCmsService(context);

        var saved = await service.UpdateNavigationAsync(
            tenantId,
            new AdminNavigationRequest
            {
                RowVersion = "0",
                Header = [Link("category", "Shoes", targetId: category.Id)],
                Footer = [Link("custom", "Help", path: "/help")]
            },
            0,
            "trace-navigation-save");

        Assert.Equal(category.Id, saved.Draft.Header.Single().TargetId);
        Assert.Equal("shoes", saved.Draft.Header.Single().Slug);
        Assert.Equal("custom", saved.Draft.Footer.Single().Type);
        using var document = JsonDocument.Parse(context.StoreConfigs.Single().CustomizationJson);
        var storedCategory = document.RootElement
            .GetProperty("navigation")
            .GetProperty("draft")
            .GetProperty("header")
            .GetProperty("links")[0];
        Assert.Equal("category", storedCategory.GetProperty("authoringType").GetString());
        Assert.Equal(category.Id.ToString("D"), storedCategory.GetProperty("targetId").GetString());
    }

    [Fact]
    public async Task PublishNavigationAsync_PromotesAdmittedDraftToLiveForHeaderAndFooter()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var category = AddCategory(context, tenantId, "Smart Phones");
        var page = AddCmsPage(context, tenantId, "footer-navigation", isPublished: true);
        context.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Navigation Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            CustomizationJson = "{}",
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var service = CreateAdminCmsService(context);

        var saved = await service.UpdateNavigationAsync(
            tenantId,
            new AdminNavigationRequest
            {
                RowVersion = "0",
                Header =
                [
                    Link("custom", "Header Navigation", path: "/header-navigation"),
                    Link("category", "Smart Phones", targetId: category.Id)
                ],
                Footer =
                [
                    Link("cms", "CMS Footer Link", targetId: page.PageGroupId)
                ]
            },
            0,
            "trace-navigation-publish-save");

        var published = await service.PublishNavigationAsync(
            tenantId,
            uint.Parse(saved.RowVersion),
            "trace-navigation-publish");

        Assert.False(published.HasPendingChanges);
        Assert.NotNull(published.Live);
        Assert.Equal("Header Navigation", published.Live!.Header[0].Label);
        Assert.Equal("smart-phones", published.Live.Header[1].Slug);
        Assert.Equal("CMS Footer Link", published.Live.Footer.Single().Label);
        Assert.Equal("footer-navigation", published.Live.Footer.Single().Slug);

        using var document = JsonDocument.Parse(context.StoreConfigs.Single().CustomizationJson);
        var navigation = document.RootElement.GetProperty("navigation");
        Assert.True(JsonElement.DeepEquals(
            navigation.GetProperty("draft"),
            navigation.GetProperty("live")));
    }

    [Fact]
    public async Task UpdateNavigationAsync_RejectedSave_DoesNotMutatePersistedDraft()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        const string original = "{\"navigation\":{\"draft\":{\"header\":{\"links\":[]},\"footer\":{\"links\":[]}}}}";
        context.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Navigation Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            CustomizationJson = original,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var service = CreateAdminCmsService(context);

        var exception = await Assert.ThrowsAsync<NavigationAdmissionException>(() =>
            service.UpdateNavigationAsync(
                tenantId,
                new AdminNavigationRequest
                {
                    RowVersion = "0",
                    Header = [Link("external", "Unsafe", path: "javascript:alert(1)")],
                    Footer = []
                },
                0,
                "trace-navigation-invalid-save"));

        Assert.Equal("NAVIGATION_INVALID_EXTERNAL_URL", exception.Code);
        Assert.Equal(original, context.StoreConfigs.Single().CustomizationJson);
    }

    [Fact]
    public async Task GetNavigationAsync_BrokenCmsTarget_LoadsMixedNavigationAndRepairCanPublish()
    {
        var tenantId = Guid.NewGuid();
        var missingTargetId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var validPage = AddCmsPage(context, tenantId, "valid-footer-page", isPublished: true);
        context.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Broken CMS Navigation Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            RowVersion = 21,
            CustomizationJson = $$"""
                {
                  "navigation": {
                    "draft": {
                      "header": { "links": [
                        { "label": "Header Custom", "authoringType": "custom", "type": "internal", "path": "/header-custom" }
                      ] },
                      "footer": { "links": [
                        { "label": "Valid Footer", "authoringType": "cms", "type": "cms", "targetId": "{{validPage.PageGroupId:D}}" },
                        { "label": "Broken Footer", "authoringType": "cms", "type": "cms", "targetId": "{{missingTargetId:D}}" }
                      ] }
                    },
                    "live": { "header": { "links": [] }, "footer": { "links": [] } }
                  }
                }
                """,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var service = CreateAdminCmsService(context);

        var loaded = await service.GetNavigationAsync(tenantId, "trace-navigation-broken-cms-load");

        Assert.Equal("21", loaded.RowVersion);
        Assert.Equal("Header Custom", loaded.Draft.Header.Single().Label);
        Assert.Equal("valid-footer-page", loaded.Draft.Footer[0].Slug);
        Assert.Equal("Broken Footer", loaded.Draft.Footer[1].Label);
        Assert.Equal(missingTargetId, loaded.Draft.Footer[1].TargetId);
        Assert.Equal("invalid", loaded.Draft.Footer[1].ValidationStatus);
        Assert.Equal("NAVIGATION_CMS_PAGE_NOT_FOUND", loaded.Draft.Footer[1].ValidationCode);
        var warning = Assert.Single(loaded.ValidationWarnings);
        Assert.Equal("draft", warning.Snapshot);
        Assert.Equal("footer", warning.Menu);
        Assert.Equal(1, warning.Index);
        Assert.Equal(missingTargetId, warning.TargetId);
        Assert.Single(loaded.BrokenLinks);

        var saved = await service.UpdateNavigationAsync(
            tenantId,
            new AdminNavigationRequest
            {
                RowVersion = loaded.RowVersion,
                Header = loaded.Draft.Header,
                Footer = [Link("cms", "Valid Footer", targetId: validPage.PageGroupId)]
            },
            uint.Parse(loaded.RowVersion),
            "trace-navigation-repair-save");
        var published = await service.PublishNavigationAsync(
            tenantId,
            uint.Parse(saved.RowVersion),
            "trace-navigation-repair-publish");

        Assert.Empty(saved.ValidationWarnings);
        Assert.False(published.HasPendingChanges);
        Assert.Equal("Valid Footer", published.Live!.Footer.Single().Label);
        Assert.Empty(published.ValidationWarnings);
    }

    [Fact]
    public async Task GetNavigationAsync_CmsTargetStates_MarksOnlyInvalidLinksAndKeepsDraftOnlyRepairable()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var deletedPage = AddCmsPage(context, tenantId, "deleted-footer-page", isPublished: false, isDeleted: true);
        var inactivePage = AddCmsPage(context, tenantId, "inactive-footer-page", isPublished: false);
        inactivePage.IsDraft = false;
        inactivePage.IsPublished = false;
        inactivePage.PublishedAt = null;
        var draftOnlyPage = AddCmsPage(context, tenantId, "draft-only-footer-page", isPublished: false);
        context.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "CMS State Navigation Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            RowVersion = 22,
            CustomizationJson = $$"""
                {
                  "navigation": {
                    "draft": {
                      "header": { "links": [] },
                      "footer": { "links": [
                        { "label": "Deleted", "authoringType": "cms", "type": "cms", "targetId": "{{deletedPage.PageGroupId:D}}" },
                        { "label": "Inactive", "authoringType": "cms", "type": "cms", "targetId": "{{inactivePage.PageGroupId:D}}" },
                        { "label": "Draft Only", "authoringType": "cms", "type": "cms", "targetId": "{{draftOnlyPage.PageGroupId:D}}" }
                      ] }
                    },
                    "live": {
                      "header": { "links": [] },
                      "footer": { "links": [
                        { "label": "Draft Only Live", "authoringType": "cms", "type": "cms", "targetId": "{{draftOnlyPage.PageGroupId:D}}" }
                      ] }
                    }
                  }
                }
                """,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var loaded = await CreateAdminCmsService(context).GetNavigationAsync(
            tenantId,
            "trace-navigation-cms-state-load");

        Assert.Equal("invalid", loaded.Draft.Footer[0].ValidationStatus);
        Assert.Equal("NAVIGATION_CMS_PAGE_DELETED", loaded.Draft.Footer[0].ValidationCode);
        Assert.Equal("invalid", loaded.Draft.Footer[1].ValidationStatus);
        Assert.Equal("NAVIGATION_CMS_PAGE_NOT_FOUND", loaded.Draft.Footer[1].ValidationCode);
        Assert.Null(loaded.Draft.Footer[2].ValidationStatus);
        Assert.Equal(draftOnlyPage.PageGroupId, loaded.Draft.Footer[2].TargetId);
        Assert.Equal("draft-only-footer-page", loaded.Draft.Footer[2].Slug);
        Assert.Equal("invalid", loaded.Live!.Footer.Single().ValidationStatus);
        Assert.Equal("NAVIGATION_CMS_PAGE_UNPUBLISHED", loaded.Live.Footer.Single().ValidationCode);
        Assert.Contains(loaded.ValidationWarnings, warning => warning.Snapshot == "draft" && warning.Code == "NAVIGATION_CMS_PAGE_DELETED");
        Assert.Contains(loaded.ValidationWarnings, warning => warning.Snapshot == "draft" && warning.Code == "NAVIGATION_CMS_PAGE_NOT_FOUND");
        Assert.Contains(loaded.ValidationWarnings, warning => warning.Snapshot == "live" && warning.Code == "NAVIGATION_CMS_PAGE_UNPUBLISHED");
    }

    [Fact]
    public async Task AdmitAsync_AllCanonicalLinkTypes_ResolvesBackendOwnedRoutes()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var category = AddCategory(context, tenantId, "Running Shoes");
        var product = AddProduct(context, tenantId, category.Id, "Road Runner", "road-runner");
        var page = AddCmsPage(context, tenantId, "about", isPublished: true);
        await context.SaveChangesAsync();

        var admitted = await new NavigationAdmissionService(context).AdmitAsync(
            tenantId,
            new AdminNavigationRequest
            {
                RowVersion = "12",
                Header =
                [
                    Link("category", "Shoes", targetId: category.Id),
                    Link("product", "Road Runner", targetId: product.Id),
                    Link("cms", "About", targetId: page.PageGroupId)
                ],
                Footer =
                [
                    Link("external", "Partner", path: "https://example.com/shop"),
                    Link("collection", "Catalog", collectionKey: NavigationAdmissionService.AllProductsCollectionKey),
                    Link("custom", "Help", path: "/help")
                ]
            },
            NavigationAdmissionMode.Publish);

        Assert.Equal(category.Id, admitted.Header[0].TargetId);
        Assert.Equal("running-shoes", admitted.Header[0].Slug);
        Assert.Equal(product.Id, admitted.Header[1].TargetId);
        Assert.Equal("/products/road-runner", admitted.Header[1].Path);
        Assert.Equal(page.PageGroupId, admitted.Header[2].TargetId);
        Assert.Equal("about", admitted.Header[2].Slug);
        Assert.Equal("/products", admitted.Footer[1].Path);
        Assert.Equal("/help", admitted.Footer[2].Path);
        Assert.Equal([0, 1, 2], admitted.Header.Select(link => link.Order));
    }

    [Fact]
    public async Task AdmitAsync_CategoryWithDeletedAncestor_ReturnsHiddenCategoryFailure()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var deletedParent = AddCategory(context, tenantId, "Hidden", isDeleted: true);
        var child = AddCategory(context, tenantId, "Child", parentId: deletedParent.Id);
        await context.SaveChangesAsync();

        var exception = await AssertAdmissionFailureAsync(
            context,
            tenantId,
            Link("category", "Child", targetId: child.Id));

        Assert.Equal("NAVIGATION_CATEGORY_HIDDEN", exception.Code);
        Assert.Equal(child.Id, exception.TargetId);
    }

    [Fact]
    public async Task AdmitAsync_DeletedCategory_ReturnsDeletedCategoryFailure()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var category = AddCategory(context, tenantId, "Deleted", isDeleted: true);
        await context.SaveChangesAsync();

        var exception = await AssertAdmissionFailureAsync(
            context,
            tenantId,
            Link("category", "Deleted", targetId: category.Id));

        Assert.Equal("NAVIGATION_CATEGORY_DELETED", exception.Code);
    }

    [Fact]
    public async Task AdmitAsync_MissingAndAmbiguousCategoryIdentity_ReturnCanonicalFailures()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var first = AddCategory(context, tenantId, "Men Shoes");
        AddCategory(context, tenantId, "Men-Shoes");
        await context.SaveChangesAsync();

        var missing = await AssertAdmissionFailureAsync(
            context,
            tenantId,
            Link("category", "Missing", targetId: Guid.NewGuid()));
        var legacyAmbiguity = await AssertAdmissionFailureAsync(
            context,
            tenantId,
            Link("category", "Legacy", slug: "men-shoes"));
        var routeAmbiguity = await AssertAdmissionFailureAsync(
            context,
            tenantId,
            Link("category", "Selected", targetId: first.Id));

        Assert.Equal("NAVIGATION_CATEGORY_NOT_FOUND", missing.Code);
        Assert.Equal("NAVIGATION_CATEGORY_IDENTITY_AMBIGUOUS", legacyAmbiguity.Code);
        Assert.Equal("NAVIGATION_CATEGORY_ROUTE_AMBIGUOUS", routeAmbiguity.Code);
    }

    [Fact]
    public async Task AdmitAsync_HiddenAndMissingProducts_ReturnStructuredFailures()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var category = AddCategory(context, tenantId, "Shoes");
        var hidden = AddProduct(context, tenantId, category.Id, "Hidden", "hidden", isVisible: false);
        await context.SaveChangesAsync();

        var hiddenFailure = await AssertAdmissionFailureAsync(
            context,
            tenantId,
            Link("product", "Hidden", targetId: hidden.Id));
        var missingFailure = await AssertAdmissionFailureAsync(
            context,
            tenantId,
            Link("product", "Missing", targetId: Guid.NewGuid()));

        Assert.Equal("NAVIGATION_PRODUCT_HIDDEN", hiddenFailure.Code);
        Assert.Equal("NAVIGATION_PRODUCT_NOT_FOUND", missingFailure.Code);
    }

    [Fact]
    public async Task AdmitAsync_DeletedProduct_ReturnsDeletedProductFailure()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var category = AddCategory(context, tenantId, "Shoes");
        var product = AddProduct(context, tenantId, category.Id, "Deleted", "deleted", isDeleted: true);
        await context.SaveChangesAsync();

        var exception = await AssertAdmissionFailureAsync(
            context,
            tenantId,
            Link("product", "Deleted", targetId: product.Id));

        Assert.Equal("NAVIGATION_PRODUCT_DELETED", exception.Code);
    }

    [Fact]
    public async Task AdmitAsync_ForeignTarget_ReturnsTenantFailureWithoutAcceptingSlugEvidence()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var foreignCategory = AddCategory(context, foreignTenantId, "Foreign");
        await context.SaveChangesAsync();

        var exception = await AssertAdmissionFailureAsync(
            context,
            tenantId,
            Link("category", "Foreign", targetId: foreignCategory.Id));

        Assert.Equal("NAVIGATION_TARGET_WRONG_TENANT", exception.Code);
        Assert.Equal(foreignCategory.Id, exception.TargetId);
    }

    [Fact]
    public async Task AdmitAsync_DraftCmsPage_IsAcceptedForDraftAndRejectedForPublish()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var page = AddCmsPage(context, tenantId, "draft-page", isPublished: false);
        await context.SaveChangesAsync();
        var service = new NavigationAdmissionService(context);
        var request = Request(Link("cms", "Draft", targetId: page.PageGroupId));

        var draft = await service.AdmitAsync(tenantId, request, NavigationAdmissionMode.Draft);
        var publishFailure = await Assert.ThrowsAsync<NavigationAdmissionException>(() =>
            service.AdmitAsync(tenantId, request, NavigationAdmissionMode.Publish));

        Assert.Equal(page.PageGroupId, draft.Header.Single().TargetId);
        Assert.Equal("NAVIGATION_CMS_PAGE_UNPUBLISHED", publishFailure.Code);
    }

    [Fact]
    public async Task AdmitAsync_DeletedCmsPage_ReturnsDeletedPageFailure()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var page = AddCmsPage(context, tenantId, "deleted-page", isPublished: false, isDeleted: true);
        await context.SaveChangesAsync();

        var exception = await AssertAdmissionFailureAsync(
            context,
            tenantId,
            Link("cms", "Deleted", targetId: page.PageGroupId));

        Assert.Equal("NAVIGATION_CMS_PAGE_DELETED", exception.Code);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:secret@example.com")]
    [InlineData("//example.com")]
    public async Task AdmitAsync_InvalidExternalUrl_ReturnsCanonicalUrlFailure(string url)
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);

        var exception = await AssertAdmissionFailureAsync(
            context,
            tenantId,
            Link("external", "Invalid", path: url));

        Assert.Equal("NAVIGATION_INVALID_EXTERNAL_URL", exception.Code);
        Assert.Equal("path", exception.Field);
    }

    [Fact]
    public async Task GetTargetsAsync_ReturnsOnlyTenantVisibleAndSelectableTargets()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var visibleCategory = AddCategory(context, tenantId, "Visible");
        var deletedCategory = AddCategory(context, tenantId, "Deleted", isDeleted: true);
        var visibleProduct = AddProduct(context, tenantId, visibleCategory.Id, "Visible Product", "visible-product");
        AddProduct(context, tenantId, visibleCategory.Id, "Hidden Product", "hidden-product", isVisible: false);
        AddProduct(context, tenantId, deletedCategory.Id, "Hidden Lineage Product", "hidden-lineage");
        var publishedPage = AddCmsPage(context, tenantId, "published", isPublished: true);
        var draftPage = AddCmsPage(context, tenantId, "draft", isPublished: false);
        await context.SaveChangesAsync();

        var targets = await new NavigationAdmissionService(context).GetTargetsAsync(tenantId);

        Assert.Equal([visibleCategory.Id], targets.Categories.Select(option => option.TargetId));
        Assert.Equal([visibleProduct.Id], targets.Products.Select(option => option.TargetId));
        Assert.Contains(targets.CmsPages, option => option.TargetId == publishedPage.PageGroupId && option.State == "published");
        Assert.Contains(targets.CmsPages, option => option.TargetId == draftPage.PageGroupId && option.State == "draft");
        Assert.Equal(NavigationAdmissionService.AllProductsCollectionKey, targets.Collections.Single().CollectionKey);
    }

    [Fact]
    public void NavigationStorageAdapter_RoundTripsCanonicalIdentityAndMapsRuntimeTypeOnce()
    {
        var productId = Guid.NewGuid();
        var stored = NavigationStorageAdapter.ToStorage(
            Link("product", "Product", targetId: productId, path: "/products/product"),
            0);
        var element = JsonSerializer.SerializeToElement(new
        {
            header = new { links = new[] { stored } },
            footer = new { links = Array.Empty<object>() }
        });

        var roundTrip = NavigationStorageAdapter.FromStorage(element).Header.Single();

        Assert.Equal("internal", stored["type"]);
        Assert.Equal("product", stored["authoringType"]);
        Assert.Equal(productId, roundTrip.TargetId);
        Assert.Equal("product", roundTrip.Type);
        Assert.Equal("/products/product", roundTrip.Path);
    }

    private static async Task<NavigationAdmissionException> AssertAdmissionFailureAsync(
        ApplicationDbContext context,
        Guid tenantId,
        AdminNavigationLinkRequest link,
        NavigationAdmissionMode mode = NavigationAdmissionMode.Draft)
    {
        return await Assert.ThrowsAsync<NavigationAdmissionException>(() =>
            new NavigationAdmissionService(context).AdmitAsync(tenantId, Request(link), mode));
    }

    private static AdminNavigationRequest Request(AdminNavigationLinkRequest link) => new()
    {
        RowVersion = "1",
        Header = [link],
        Footer = []
    };

    private static AdminNavigationLinkRequest Link(
        string type,
        string label,
        Guid? targetId = null,
        string? path = null,
        string? collectionKey = null,
        string? slug = null) => new()
    {
        Type = type,
        Label = label,
        TargetId = targetId,
        Path = path,
        CollectionKey = collectionKey,
        Slug = slug,
        Enabled = true
    };

    private static Category AddCategory(
        ApplicationDbContext context,
        Guid tenantId,
        string name,
        Guid? parentId = null,
        bool isDeleted = false)
    {
        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            ParentCategoryId = parentId,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow
        };
        context.Categories.Add(category);
        return category;
    }

    private static Product AddProduct(
        ApplicationDbContext context,
        Guid tenantId,
        Guid categoryId,
        string name,
        string slug,
        bool isVisible = true,
        bool isDeleted = false)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CategoryId = categoryId,
            Name = name,
            Slug = slug,
            IsActive = true,
            IsOnlineVisible = isVisible,
            PublishStatus = PublishStatus.Published,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow
        };
        context.Products.Add(product);
        return product;
    }

    private static CmsPage AddCmsPage(
        ApplicationDbContext context,
        Guid tenantId,
        string slug,
        bool isPublished,
        bool isDeleted = false)
    {
        var page = new CmsPage
        {
            Id = Guid.NewGuid(),
            PageGroupId = Guid.NewGuid(),
            TenantId = tenantId,
            Slug = slug,
            Title = slug,
            Content = "{}",
            IsDraft = !isPublished,
            IsPublished = isPublished,
            PublishedAt = isPublished ? DateTime.UtcNow : null,
            IsDeleted = isDeleted,
            Version = 1
        };
        context.CmsPages.Add(page);
        return page;
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static AdminCmsService CreateAdminCmsService(ApplicationDbContext context) => new(
        context,
        Mock.Of<ICmsPageService>(),
        Mock.Of<ICmsPageReadService>(),
        Mock.Of<ICmsPagePreviewTokenService>(),
        new NavigationAdmissionService(context),
        Mock.Of<IEventPublisher>(),
        NullLogger<AdminCmsService>.Instance);
}
