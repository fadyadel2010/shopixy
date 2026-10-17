using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.GeographicAdministration;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Inventory;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Geographic;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

public sealed class GeographicCatalogAdmissionDiagnosisTests
{
    [Fact]
    public async Task EmptyCanonicalCatalog_RealLookupFailsClosedWithoutInventingCountries()
    {
        var tenant = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var warehouse = Guid.NewGuid();
        await using var db = Db(tenant);
        db.Users.Add(new User { Id = actor, TenantId = tenant, Username = "synthetic", Email = "synthetic@example.invalid",
            PasswordHash = "synthetic", Role = UserRole.Owner, IsActive = true });
        db.Warehouses.Add(new Warehouse { Id = warehouse, TenantId = tenant, Name = "Synthetic", IsActive = true });
        await db.SaveChangesAsync();
        var repo = new GeographicRepository(db);
        var catalog = new GeographicCatalogService(repo);
        var service = new WarehouseAddressAdmissionService(db, new TestTenantContext(tenant), new GeographicResolver(catalog, repo), catalog);
        var failure = await Assert.ThrowsAsync<ArgumentException>(() => service.GetPlacesAsync(warehouse, actor, null, null));
        Assert.Equal("PublishedCatalogReleaseRequired", failure.Message);
        Assert.Empty(db.GeographicPlaces);
        Assert.Empty(db.AddressAdmissionEvidence);
        Assert.Empty(db.GeographicAudit);
    }

    [Theory]
    [InlineData("missing", "CatalogReleaseNotFound")]
    [InlineData("draft", "ValidatedCatalogReleaseRequired")]
    [InlineData("self-approval", "FourEyesApprovalRequired")]
    public async Task AdminPublication_ExistingGuardsRejectWithoutPublication(string variant, string expected)
    {
        await using var db = Db(Guid.NewGuid());
        var creator = Guid.NewGuid();
        var release = Guid.NewGuid();
        if (variant != "missing")
        {
            // Deliberately synthetic release; never an authoritative dataset or live publish.
            db.GeographicCatalogReleases.Add(new GeographicCatalogRelease { Id = release, CreatedByUserId = creator,
                ReleaseKey = "synthetic", ChecksumSha256 = new string('a', 64),
                Status = variant == "draft" ? GeographicReleaseStatus.Draft : GeographicReleaseStatus.Validated });
            await db.SaveChangesAsync();
        }
        var result = await new GeographicAdministrationService(db).PublishCatalogReleaseAsync(release,
            new ReleaseActionRequest("Synthetic rejection assertion"), variant == "self-approval" ? creator : Guid.NewGuid(), "Owner");
        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.FailureCode);
        Assert.Empty(db.GeographicCatalogPublications);
        Assert.Empty(db.GeographicAudit);
    }

    [Fact]
    public async Task FoundationPublicationService_IsNotAnImportOrValidationEscapeHatch()
    {
        var result = await new GeographicPublicationService().PublishCatalogReleaseAsync(Guid.NewGuid(), Guid.NewGuid(), "Synthetic");
        Assert.False(result.Succeeded);
        Assert.Equal("PublicationWorkflowNotImplementedInFoundation", result.FailureCode);
    }

    private static ApplicationDbContext Db(Guid tenant) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"GeographicDiagnosis-{Guid.NewGuid()}").Options,
        new TestTenantContext(tenant));
}
