using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Admin;

public sealed partial class AdminStoreConfigThemeContractTests
{
    [ConfiguredRemittanceRollbackTheory]
    [InlineData("phase2")]
    public async Task ConfiguredPostgres_Phase2ContentPublicationAndRestore_RollsBackAllFixtures(string scenario)
    {
        Assert.Equal("phase2", scenario);
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var tenant = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        var facts = new List<string>();
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'; SET LOCAL statement_timeout = '30s'");
            db.Tenants.AddRange(
                new Tenant { Id = tenant, Name = "SYNTHETIC Phase2 rollback only", Slug = $"p2-{tenant:N}", Status = TenantStatus.Active },
                new Tenant { Id = foreignTenant, Name = "SYNTHETIC Phase2 foreign", Slug = $"p2-{foreignTenant:N}", Status = TenantStatus.Active });
            db.StoreConfigs.Add(CreateStoreConfig(tenant));
            await db.SaveChangesAsync();
            var events = new Mock<IEventPublisher>();
            events.SetReturnsDefault(Task.CompletedTask);
            var service = CreateService(db, events);
            var settings = StorefrontSectionContracts.All.Single(s => s.Type == "hero-banner").DefaultSettings.ToDictionary(p => p.Key, p => p.Value);
            settings["title"] = "SYNTHETIC Homepage مرحباً";
            var page = new Dictionary<string, object> { ["sections"] = new[] { new { type = "hero-banner", enabled = true, settings } } };
            var read = await service.GetStoreConfigAsync(tenant, "p2-read");
            var saved = await service.UpdatePageLayoutAsync(tenant, "home", page, "p2-save", expectedRowVersion: uint.Parse(read.RowVersion));
            db.ChangeTracker.Clear();
            var stored = await db.StoreConfigs.SingleAsync();
            Assert.DoesNotContain("SYNTHETIC Homepage", JsonSerializer.Serialize(StorefrontDraftConfigMapper.MapLive(stored, null, "EGP", "EG").Pages));
            var published = await service.PublishPageAsync(tenant, "home", "p2-publish", expectedRowVersion: uint.Parse(saved.RowVersion));
            db.ChangeTracker.Clear();
            stored = await db.StoreConfigs.SingleAsync();
            Assert.Contains("SYNTHETIC Homepage", JsonSerializer.Serialize(StorefrontDraftConfigMapper.MapLive(stored, null, "EGP", "EG").Pages));
            var homeVersion = (await service.GetVersionHistoryAsync(tenant, "p2-history")).CurrentVersion;
            facts.Add("Homepage draft reload excludes live; publish and SQL live mapper agree; real version snapshot recorded");

            var cms = new CmsPageService(db, new CmsPageContentValidator(), events.Object, NullLogger<CmsPageService>.Instance);
            var cmsRead = new CmsPageReadService(db, Mock.Of<IMongoDatabase>(), NullLogger<CmsPageReadService>.Instance);
            var draft = await cms.CreatePageAsync(tenant, new AdminCmsPageRequest { Slug = $"p2-{tenant:N}", Title = "SYNTHETIC CMS", ContentType = CmsPageContentType.RichText,
                Content = JsonSerializer.SerializeToElement("<p>Safe content</p><script>alert(1)</script>"), SchemaVersion = 1 });
            Assert.DoesNotContain("<script", draft.Draft!.Content.GetString()!);
            Assert.Null(await cmsRead.GetPublishedPageAsync(tenant, draft.Slug));
            Assert.Null(await cms.GetPageAsync(foreignTenant, draft.Id));
            // Seed a controlled published fixture, not a claim that CMS publish ran
            // inside this caller-owned transaction (that service owns its transaction).
            var livePage = await db.CmsPages.SingleAsync(p => p.PageGroupId == draft.Id);
            livePage.IsDraft = false; livePage.IsPublished = true; livePage.PublishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            Assert.NotNull(await cmsRead.GetPublishedPageAsync(tenant, draft.Slug));
            facts.Add("Real CMS create/sanitization/draft exclusion/foreign read; fixture-backed public SQL CMS read (not lifecycle publish)");

            var nav = new AdminCmsService(db, cms, cmsRead, Mock.Of<ICmsPagePreviewTokenService>(), new NavigationAdmissionService(db), events.Object, NullLogger<AdminCmsService>.Instance);
            read = await service.GetStoreConfigAsync(tenant, "p2-nav-read");
            var navSaved = await nav.UpdateNavigationAsync(tenant, new AdminNavigationRequest {
                Header = [new() { Label = "Page", Type = "cms", TargetId = draft.Id, Enabled = true }], Footer = []
            }, uint.Parse(read.RowVersion), "p2-nav-save");
            var navLive = await nav.PublishNavigationAsync(tenant, uint.Parse(navSaved.RowVersion), "p2-nav-publish");
            Assert.False(navLive.HasPendingChanges);
            Assert.Equal(draft.Slug, navLive.Live!.Header.Single().Slug);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => nav.PublishNavigationAsync(tenant, uint.Parse(navLive.RowVersion) - 1, "p2-nav-stale"));
            facts.Add("Navigation draft/publish readback binds the tenant CMS identity and canonical slug; stale xmin rejected");

            var footer = new FooterAuthoringService(db, events.Object, new FooterAuthoringValidator(), new FooterAuthoringMapper());
            var originalFooter = await footer.GetConfigurationAsync(FooterAuthoringScope.Tenant, tenant);
            var footerSaved = await footer.SaveConfigurationAsync(FooterAuthoringScope.Tenant, tenant, new() {
                ExpectedRevision = originalFooter.Revision,
                Sections = [new() { Title = "SYNTHETIC footer", Order = 0, IsActive = true,
                    Links = [new() { Label = "Page", Type = FooterLinkType.Cms, CmsPageSlug = draft.Slug, Order = 0, IsActive = true }] }], SocialLinks = []
            });
            var footerRead = await new StorefrontFooterReadService(db, NullLogger<StorefrontFooterReadService>.Instance).GetFooterAsync(tenant);
            Assert.Equal("Tenant", footerRead.FooterSource);
            Assert.Equal("/" + draft.Slug, footerRead.Sections.Single().Links.Single().Url);
            Assert.NotEqual(originalFooter.Revision, footerSaved.Revision);
            facts.Add("Footer revision/persistence/public SQL consumer agree; tenant CMS stable reference resolves");

            read = await service.GetStoreConfigAsync(tenant, "p2-restore-read");
            var restored = await service.RollbackStoreConfigAsync(tenant, homeVersion, true, "SYNTHETIC fixture restore", "p2-restore", expectedRowVersion: uint.Parse(read.RowVersion));
            var history = await service.GetVersionHistoryAsync(tenant, "p2-restored-history");
            Assert.True(history.CurrentVersion > homeVersion);
            Assert.Equal(homeVersion, history.Versions.Single(v => v.IsCurrent).RestoredFromVersion);
            Assert.NotNull(restored.Pages);
            Assert.Equal(footerSaved.Revision, (await footer.GetConfigurationAsync(FooterAuthoringScope.Tenant, tenant)).Revision);
            Assert.NotNull(await cms.GetPageAsync(tenant, draft.Id));
            events.Verify(e => e.PublishAsync(It.IsAny<StoreConfigPublishedEvent>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            events.Verify(e => e.PublishAsync(It.IsAny<StoreConfigRolledBackEvent>(), It.IsAny<CancellationToken>()), Times.Once);
            events.Verify(e => e.PublishAsync(It.IsAny<NavigationPublishedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
            events.Verify(e => e.PublishAsync(It.IsAny<StorefrontFooterUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
            facts.Add("Restore is a new monotonic publication; CMS and separate Footer preserved; controlled publication events verified");
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenant || t.Id == foreignTenant));
        Assert.False(await observer.StoreConfigs.AnyAsync());
        Assert.False(await observer.CmsPages.AnyAsync());
        Assert.False(await observer.StorefrontFooterSections.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenant));
        var evidence = Environment.GetEnvironmentVariable("STOREFRONT_PHASE2_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(evidence))
            await File.WriteAllTextAsync(Path.Combine(evidence, "configured-content-rollback.json"), JsonSerializer.Serialize(new { observedUtc = DateTime.UtcNow, facts, rolledBack = true, freshObserverAbsent = true }));
    }
}
