using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Cms;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Models;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class HeaderAnnouncementAuthoringTests
{
    [Fact]
    public async Task HeaderAnnouncement_UsesExistingAtomicNavigationDraftAndPublish()
    {
        var tenantId = Guid.NewGuid();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TestTenantContext(tenantId));
        db.StoreConfigs.Add(new StoreConfig { Id = Guid.NewGuid(), TenantId = tenantId, StoreName = "Announcement", CustomizationJson = "{}" });
        await db.SaveChangesAsync();
        var service = new AdminCmsService(db, Mock.Of<ICmsPageService>(), Mock.Of<ICmsPageReadService>(), Mock.Of<ICmsPagePreviewTokenService>(),
            new NavigationAdmissionService(db), Mock.Of<IEventPublisher>(), NullLogger<AdminCmsService>.Instance);
        var announcement = new HeaderAnnouncementConfig { Enabled = true, Text = "New collection", TextAr = "مجموعة جديدة", Link = "/products", BackgroundColor = "#112233", TextColor = "#ffffff" };
        var saved = await service.UpdateNavigationAsync(tenantId, new AdminNavigationRequest { Announcement = announcement }, 0, "draft");
        Assert.Equal(announcement.Text, saved.Draft.Announcement!.Text);
        Assert.Null(saved.Live!.Announcement);
        var loaded = await service.GetNavigationAsync(tenantId, "reload");
        Assert.Equal(announcement.TextAr, loaded.Draft.Announcement!.TextAr);
        var published = await service.PublishNavigationAsync(tenantId, uint.Parse(loaded.RowVersion), "publish");
        Assert.Equal(announcement.Link, published.Live!.Announcement!.Link);
        Assert.Equal(announcement.BackgroundColor, published.Live.Announcement.BackgroundColor);
        announcement.Enabled = false;
        var hiddenDraft = await service.UpdateNavigationAsync(tenantId, new AdminNavigationRequest { Announcement = announcement }, uint.Parse(published.RowVersion), "hide-draft");
        Assert.True(hiddenDraft.Live!.Announcement!.Enabled);
        var hidden = await service.PublishNavigationAsync(tenantId, uint.Parse(hiddenDraft.RowVersion), "hide-publish");
        Assert.False(hidden.Live!.Announcement!.Enabled);
        Assert.Contains("announcement", (await db.StoreConfigs.SingleAsync()).VersionsJson);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//another-host/path")]
    [InlineData("https://user:password@example.com")]
    [InlineData("/\\another-host/path")]
    public void Announcement_RejectsUnsafeLink(string link) => Assert.Throws<ArgumentException>(() => HeaderAnnouncementValidator.Validate(new() { Link = link }));

    [Fact]
    public void LegacyHeader_HasNoStoredAnnouncement_AndDoesNotMigrateCopy()
    {
        using var json = JsonDocument.Parse("{\"header\":{\"links\":[]},\"footer\":{\"links\":[]}}");
        Assert.Null(NavigationStorageAdapter.FromStorage(json.RootElement).Announcement);
    }
}
