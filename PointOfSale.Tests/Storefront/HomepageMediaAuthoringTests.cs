using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Storefront;

public sealed partial class HomepageMediaAuthoringTests
{
    [Fact]
    public void HistoricalUnknownSection_DoesNotGrantMediaDeliveryOrBreakOtherPublishedSections()
    {
        var id = Guid.NewGuid();
        var reference = HomepageMediaReferences.Create(id);
        var unknown = new { type = "retired-promo", enabled = true, settings = new { backgroundImage = reference } };
        var valid = new { type = "hero-banner", enabled = true, settings = new { backgroundImage = reference } };
        string Config(object[] sections) => JsonSerializer.Serialize(new { pages = new { home = new { live = new { sections } } } });
        Assert.False(HomepageMediaReferences.IsPublished(Config([unknown]), id));
        Assert.True(HomepageMediaReferences.IsPublished(Config([unknown, valid]), id));
    }

    [Fact]
    public async Task MediaReference_DraftReloadPublishReplaceDiscardRollback_PreserveAssetAuthority()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var a = Asset(tenantId);
        var b = Asset(tenantId);
        db.HomepageMediaAssets.AddRange(a, b);
        db.ThemePresets.Add(new ThemePreset { Id = Guid.NewGuid(), Key = "default", Name = "Default", BaseThemeJson = "{}" });
        await db.SaveChangesAsync();
        var service = Service(db);
        var saved = await service.UpdatePageLayoutAsync(tenantId, "home", Page(HomepageMediaReferences.Create(a.Id)), "draft", expectedRowVersion: 0);
        Assert.False(HomepageMediaReferences.IsPublished((await db.StoreConfigs.SingleAsync()).CustomizationJson, a.Id));
        await using (var reload = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            var draft = await Service(reload).GetStoreConfigAsync(tenantId, "reload");
            Assert.Contains(a.Id.ToString(), JsonSerializer.Serialize(draft.Pages));
        }
        await service.PublishPageAsync(tenantId, "home", "publish");
        var config = await db.StoreConfigs.SingleAsync();
        var versionA = config.LiveVersion;
        Assert.True(HomepageMediaReferences.IsPublished(config.CustomizationJson, a.Id));
        await service.UpdatePageLayoutAsync(tenantId, "home", Page(HomepageMediaReferences.Create(b.Id)), "replace");
        Assert.True(HomepageMediaReferences.IsPublished(config.CustomizationJson, a.Id));
        Assert.False(HomepageMediaReferences.IsPublished(config.CustomizationJson, b.Id));
        var admitted = await service.GetStoreConfigAsync(tenantId, "discard-source");
        var livePage = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(admitted.LivePages!.Home, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        await service.UpdatePageLayoutAsync(tenantId, "home", livePage, "discard");
        Assert.Contains(a.Id.ToString(), JsonSerializer.Serialize((await service.GetStoreConfigAsync(tenantId, "discarded")).Pages));
        await service.UpdatePageLayoutAsync(tenantId, "home", Page(HomepageMediaReferences.Create(b.Id)), "new-version");
        await service.PublishPageAsync(tenantId, "home", "publish-b");
        Assert.True(HomepageMediaReferences.IsPublished(config.CustomizationJson, b.Id));
        await service.RollbackStoreConfigAsync(tenantId, versionA, true, "restore asset", "rollback");
        Assert.True(HomepageMediaReferences.IsPublished(config.CustomizationJson, a.Id));
        Assert.False(HomepageMediaReferences.IsPublished(config.CustomizationJson, b.Id));
        Assert.Equal(2, await db.HomepageMediaAssets.CountAsync());
    }

    [Fact]
    public async Task ExistingUrl_RemainsRenderable_AndForeignReferenceIsRejected()
    {
        var tenantId = Guid.NewGuid();
        await using var db = Db(tenantId);
        var foreign = Asset(Guid.NewGuid());
        db.HomepageMediaAssets.Add(foreign);
        await db.SaveChangesAsync();
        var service = Service(db);
        const string url = "https://images.example.com/legacy.jpg";
        await service.UpdatePageLayoutAsync(tenantId, "home", Page(url), "legacy", expectedRowVersion: 0);
        await service.PublishPageAsync(tenantId, "home", "legacy-publish");
        var config = await db.StoreConfigs.SingleAsync();
        Assert.Contains(url, JsonSerializer.Serialize(StorefrontDraftConfigMapper.MapLive(config, null, "EGP", "EG").Pages));
        var before = config.CustomizationJson;
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePageLayoutAsync(tenantId, "home", Page(HomepageMediaReferences.Create(foreign.Id)), "foreign"));
        Assert.Equal(before, config.CustomizationJson);
    }

    [Fact]
    public async Task Upload_UsesPrivateTenantNamespace_AndContentIsDeniedUntilPublished()
    {
        var tenantId = Guid.NewGuid();
        await using var db = Db(tenantId);
        var storage = new Mock<IR2StorageService>();
        storage.Setup(s => s.UploadFileAsync(It.IsAny<Stream>(), "image/png", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream stream, string type, string path, CancellationToken token) => new R2UploadResult("https://unused-public.example/image", "image.png", $"{path}/image.png"));
        var controller = new HomepageMediaController(db, new TestTenantContext(tenantId), storage.Object, new FileValidationService())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j7WQAAAAASUVORK5CYII=");
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "hero.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
        var result = Assert.IsType<OkObjectResult>(await controller.Upload(file, default));
        var response = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("unused-public", response);
        var asset = await db.HomepageMediaAssets.SingleAsync();
        Assert.StartsWith($"tenants/{tenantId:D}/homepage/{asset.Id:D}/", asset.ObjectKey);
        Assert.IsType<NotFoundResult>(await controller.Content(asset.Id, default));
        storage.Verify(s => s.DownloadObjectAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TenantB_CannotResolveTenantAAsset_AndUploadRequiresAdminRole()
    {
        var tenantA = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var a = new ApplicationDbContext(options, new TestTenantContext(tenantA));
        var asset = Asset(tenantA);
        a.HomepageMediaAssets.Add(asset);
        await a.SaveChangesAsync();
        var tenantB = Guid.NewGuid();
        await using var b = new ApplicationDbContext(options, new TestTenantContext(tenantB));
        var controller = new HomepageMediaController(b, new TestTenantContext(tenantB), Mock.Of<IR2StorageService>(), new FileValidationService());
        Assert.Empty(await b.HomepageMediaAssets.ToListAsync());
        Assert.IsType<NotFoundResult>(await controller.Preview(asset.Id, default));
        Assert.IsType<NotFoundResult>(await controller.Content(asset.Id, default));
        var auth = typeof(HomepageMediaController).GetMethod(nameof(HomepageMediaController.Upload))!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("Owner,Admin", auth.Roles);
    }

    private static HomepageMediaAsset Asset(Guid tenantId) => new() { Id = Guid.NewGuid(), TenantId = tenantId, ObjectKey = Guid.NewGuid().ToString(), ContentType = "image/png", Size = 100, CreatedAtUtc = DateTime.UtcNow };
    private static ApplicationDbContext Db(Guid tenantId) => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TestTenantContext(tenantId));
    private static Dictionary<string, object> Page(string image)
    {
        var settings = StorefrontSectionContracts.All.Single(s => s.Type == "hero-banner").DefaultSettings.ToDictionary(k => k.Key, k => k.Value);
        settings["backgroundImage"] = image;
        return new() { ["sections"] = new[] { new { type = "hero-banner", enabled = true, settings } } };
    }
    private static AdminStoreConfigService Service(ApplicationDbContext db)
    {
        var publishLock = new Mock<IPublishLockService>();
        publishLock.Setup(s => s.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<string>())).ReturnsAsync(PublishLockResult.Success("test"));
        return new(db, Mock.Of<IEventPublisher>(), publishLock.Object, Mock.Of<IStoreConfigAuditService>(),
            new StorefrontPreviewService(db, Mock.Of<ICmsPageReadService>(), NullLogger<StorefrontPreviewService>.Instance),
            new StorefrontPublishTelemetry(), NullLogger<AdminStoreConfigService>.Instance,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
    }
}
