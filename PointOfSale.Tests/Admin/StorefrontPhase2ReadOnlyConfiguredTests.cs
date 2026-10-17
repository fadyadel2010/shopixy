using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Admin;

public sealed class StorefrontPhase2ReadOnlyConfiguredTests
{
    [ConfiguredRemittanceRollbackTheory]
    [InlineData("read-only")]
    public async Task Phase2Configured_ExistingTenantReadOnlySnapshot(string scenario)
    {
        Assert.Equal("read-only", scenario);
        var rawTenant = Environment.GetEnvironmentVariable("STOREFRONT_PHASE2_TENANT");
        Assert.True(Guid.TryParse(rawTenant, out var tenant), "An explicitly selected existing tenant is required.");
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        await using var db = new ApplicationDbContext(options, new TestTenantContext(tenant));
        var config = await db.StoreConfigs.AsNoTracking().SingleAsync(c => c.TenantId == tenant);
        var cms = await db.CmsPages.AsNoTracking().Where(p => p.TenantId == tenant).OrderBy(p => p.Id).ToListAsync();
        var sections = await db.StorefrontFooterSections.AsNoTracking().IgnoreQueryFilters().Where(s => s.TenantId == tenant).OrderBy(s => s.Id).ToListAsync();
        var sectionIds = sections.Select(s => s.Id).ToArray();
        var links = await db.StorefrontFooterLinks.AsNoTracking().IgnoreQueryFilters().Where(l => sectionIds.Contains(l.SectionId)).OrderBy(l => l.Id).ToListAsync();
        var social = await db.StorefrontSocialLinks.AsNoTracking().IgnoreQueryFilters().Where(s => s.TenantId == tenant).OrderBy(s => s.Id).ToListAsync();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { config, cms, sections, links, social }))));
        var live = StorefrontDraftConfigMapper.MapLive(config, null, "EGP", "EG");
        var evidence = Environment.GetEnvironmentVariable("STOREFRONT_PHASE2_EVIDENCE");
        var stage = Environment.GetEnvironmentVariable("STOREFRONT_PHASE2_SNAPSHOT_STAGE");
        Assert.Contains(stage, new[] { "before-final-rollback", "after-final-rollback" });
        Assert.False(db.ChangeTracker.HasChanges());
        if (!string.IsNullOrWhiteSpace(evidence))
            await File.WriteAllTextAsync(Path.Combine(evidence, $"merchant-readonly-{stage}.json"), JsonSerializer.Serialize(new {
                observedUtc = DateTime.UtcNow, evidence = "configured-merchant-SQL-read-only", tenantId = tenant,
                config.StorefrontStatus, config.LiveVersion, rowVersion = config.RowVersion.ToString(),
                cmsVersionRecords = cms.Count, footerSections = sections.Count, footerLinks = links.Count, socialLinks = social.Count,
                hasLivePages = live.Pages != null, fingerprint, writes = 0
            }));
    }
}
