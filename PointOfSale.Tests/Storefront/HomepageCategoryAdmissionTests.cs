using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;

namespace PointOfSale.Tests.Storefront;

public sealed partial class HomepageMediaAuthoringTests
{
    [Theory]
    [InlineData("category-grid", false)]
    [InlineData("subcategory-grid", false)]
    [InlineData("category-grid", true)]
    [InlineData("subcategory-grid", true)]
    public async Task HomepageCuratedCategories_RejectForeignReferencesAtSaveAndPublish(string type, bool publish)
    {
        var tenant = Guid.NewGuid();
        await using var db = Db(tenant);
        var foreign = new Category { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Name = "Foreign category" };
        db.Categories.Add(foreign);
        var config = new StoreConfig { Id = Guid.NewGuid(), TenantId = tenant, StoreName = "Fixture", CustomizationJson = "{}" };
        db.StoreConfigs.Add(config);
        await db.SaveChangesAsync();
        var composition = CategoryPage(type, foreign.Id);
        if (publish)
        {
            // Historical/previously accepted draft: publish must revalidate ownership.
            config.CustomizationJson = JsonSerializer.Serialize(new { pages = new { home = new { draft = composition } } });
            await db.SaveChangesAsync();
        }
        var before = config.CustomizationJson;
        var service = Service(db);
        await Assert.ThrowsAsync<ArgumentException>(() => publish
            ? service.PublishPageAsync(tenant, "home", "phase2-publish")
            : service.UpdatePageLayoutAsync(tenant, "home", composition, "phase2-save"));
        db.ChangeTracker.Clear();
        Assert.Equal(before, (await db.StoreConfigs.SingleAsync()).CustomizationJson);
    }

    [Fact]
    public async Task HomepageCuratedCategories_AcceptsOwnedLiveLineage_RejectsDeletedAncestor()
    {
        var tenant = Guid.NewGuid();
        await using var db = Db(tenant);
        var parent = new Category { Id = Guid.NewGuid(), TenantId = tenant, Name = "Parent" };
        var child = new Category { Id = Guid.NewGuid(), TenantId = tenant, Name = "Child", ParentCategoryId = parent.Id };
        db.Categories.AddRange(parent, child);
        await db.SaveChangesAsync();
        var service = Service(db);
        await service.UpdatePageLayoutAsync(tenant, "home", CategoryPage("subcategory-grid", child.Id), "owned", expectedRowVersion: 0);
        parent.IsDeleted = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => service.PublishPageAsync(tenant, "home", "deleted-ancestor"));
    }

    private static Dictionary<string, object> CategoryPage(string type, Guid id)
    {
        var settings = StorefrontSectionContracts.All.Single(s => s.Type == type).DefaultSettings.ToDictionary(k => k.Key, k => k.Value);
        settings["source"] = "curated";
        settings["categoryIds"] = new[] { id.ToString("D") };
        return new() { ["sections"] = new[] { new { type, enabled = true, settings } } };
    }
}
