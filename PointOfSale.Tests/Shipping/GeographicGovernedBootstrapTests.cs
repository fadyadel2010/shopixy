using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.GeographicAdministration;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

// In-process fixtures exercise governance only; neither official-source review nor
// relational/live acceptance is claimed by these synthetic catalog manifests.
public sealed class GeographicGovernedBootstrapTests
{
    private static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase("governed-bootstrap-" + Guid.NewGuid()).Options, new TestTenantContext(Guid.NewGuid()));
    private static CatalogImportManifest Manifest()
    {
        var country = Guid.NewGuid(); var area = Guid.NewGuid(); var city = Guid.NewGuid();
        return new(1, "synthetic-import-only", "US", "Synthetic fixture, not authoritative data",
            "https://synthetic.example.invalid/official-source", new string('a', 64), [
                new(country, "synthetic-country", GeographicPlaceKind.Country, null, null, "COUNTRY", "US", null, null, [new("en", "Synthetic country")]),
                new(area, "synthetic-area", GeographicPlaceKind.AdministrativeArea, country, country, "AREA", "TEST-AREA", null, null, [new("en", "Synthetic area")]),
                new(city, "synthetic-city", GeographicPlaceKind.Locality, country, area, "CITY", "TEST-CITY", null, null, [new("en", "Synthetic city")])],
            [new(country, "US", "USA", "en", ["en"], ["AREA", "CITY"], ["AREA"], GeographicPostalCodePolicy.Required, "\\A[0-9]{5}\\z", true, false, false, false)]);
    }

    [Fact]
    public async Task Import_preserves_canonical_IDs_parent_edges_and_source_without_publishing_then_requires_independent_bound_review()
    {
        await using var db = Db(); var service = new GeographicAdministrationService(db); var manifest = Manifest();
        var editor = Guid.NewGuid(); var reviewer = Guid.NewGuid();
        var preview = await service.PreviewCatalogImportAsync(manifest);
        Assert.True(preview.Succeeded, string.Join(",", preview.Issues.Select(i => i.Code)));
        Assert.Empty(db.GeographicCatalogReleases);
        var stage = await service.StageCatalogImportAsync(new(manifest, preview.ManifestChecksumSha256, "Synthetic import"), editor, "Owner");
        Assert.True(stage.Succeeded, stage.FailureCode); var release = stage.ResourceId!.Value;
        db.ChangeTracker.Clear();
        var read = (await service.ReadCatalogReleaseAsync(release))!;
        Assert.Equal(manifest.Places.Select(p => p.Id).Order(), read.Places.Select(p => p.Id).Order());
        Assert.Equal(manifest.SourceEvidenceUri, read.SourceEvidenceUri);
        Assert.Equal(manifest.SourceEvidenceSha256, read.SourceEvidenceSha256);
        Assert.Equal(2, await db.GeographicCatalogReleaseEdges.CountAsync());
        Assert.True(read.Validation.Succeeded, read.Validation.FailureCode);
        Assert.Empty(db.GeographicCatalogPublications); Assert.Empty(db.CarrierGeographicMappingPublications);
        var request = new CatalogValidationRequest(read.Validation.ContentChecksumSha256!, read.SourceEvidenceUri!, read.SourceEvidenceSha256!, true, "Synthetic independent review");
        Assert.Equal("FourEyesApprovalRequired", (await service.ValidateCatalogDraftAsync(release, request, editor, "Owner")).FailureCode);
        Assert.Equal("CatalogImportSourceEvidenceChanged", (await service.ValidateCatalogDraftAsync(release, request with { SourceEvidenceSha256 = new string('b', 64) }, reviewer, "Owner")).FailureCode);
        Assert.True((await service.ValidateCatalogDraftAsync(release, request, reviewer, "Owner")).Succeeded);
        Assert.Equal("CatalogContentChanged", (await service.PublishCatalogReleaseAsync(release, new("Synthetic", new string('c', 64)), reviewer, "Owner")).FailureCode);
        Assert.True((await service.PublishCatalogReleaseAsync(release, new("Explicit synthetic publication", read.Validation.ContentChecksumSha256), reviewer, "Owner")).Succeeded);
        Assert.Single(db.GeographicCatalogPublications); Assert.Empty(db.CarrierAccounts); Assert.Empty(db.Orders); Assert.Empty(db.Shipments);
        Assert.False((await service.PreviewCatalogImportAsync(manifest)).Succeeded);
        Assert.Single(await service.ListCatalogReleasesAsync());
    }

    [Theory]
    [InlineData("duplicate-id")][InlineData("duplicate-key")][InlineData("duplicate-code")][InlineData("parent")]
    [InlineData("cross-country")][InlineData("kind")][InlineData("name")][InlineData("profile")]
    [InlineData("source")][InlineData("hash")][InlineData("postal")][InlineData("required-level")]
    public async Task Invalid_imports_are_reviewable_but_cannot_persist_partial_catalogs(string variant)
    {
        await using var db = Db(); var service = new GeographicAdministrationService(db); var m = Manifest(); var places = m.Places.ToArray();
        switch (variant)
        {
            case "duplicate-id": places[2] = places[2] with { Id = places[1].Id }; break;
            case "duplicate-key": places[2] = places[2] with { StableKey = places[1].StableKey.ToUpperInvariant() }; break;
            case "duplicate-code": places = [.. places, places[2] with { Id = Guid.NewGuid(), StableKey = "synthetic-other" }]; break;
            case "parent": places[2] = places[2] with { ParentPlaceId = Guid.NewGuid() }; break;
            case "cross-country": places[2] = places[2] with { CountryPlaceId = Guid.NewGuid() }; break;
            case "kind": places[2] = places[2] with { PlaceKind = GeographicPlaceKind.Zone }; break;
            case "name": places[2] = places[2] with { Names = [] }; break;
            case "profile": m = m with { CountryProfiles = [.. m.CountryProfiles, m.CountryProfiles[0]] }; break;
            case "source": m = m with { SourceEvidenceUri = "https://user:secret@synthetic.example.invalid/file?token=no" }; break;
            case "hash": m = m with { SourceEvidenceSha256 = "unverified" }; break;
            case "postal": m = m with { CountryProfiles = [m.CountryProfiles[0] with { PostalCodeRegex = "[" }] }; break;
            case "required-level": m = m with { CountryProfiles = [m.CountryProfiles[0] with { RequiredLevelCodesForDelivery = ["MISSING"] }] }; break;
        }
        m = m with { Places = places };
        var preview = await service.PreviewCatalogImportAsync(m); Assert.False(preview.Succeeded); Assert.NotEmpty(preview.Issues);
        Assert.False((await service.StageCatalogImportAsync(new(m, preview.ManifestChecksumSha256, "Synthetic"), Guid.NewGuid(), "Owner")).Succeeded);
        Assert.Empty(db.GeographicCatalogReleases); Assert.Empty(db.GeographicPlaces); Assert.Empty(db.GeographicAudit);
    }

    [Fact]
    public async Task Changed_preview_and_existing_country_or_identity_cannot_be_silently_imported()
    {
        await using var db = Db(); var service = new GeographicAdministrationService(db); var m = Manifest();
        Assert.Equal("CatalogImportContentChanged", (await service.StageCatalogImportAsync(new(m, new string('f', 64), "Synthetic"), Guid.NewGuid(), "Owner")).FailureCode);
        Assert.Empty(db.GeographicCatalogReleases);
        db.GeographicCountryProfiles.Add(new() { Id = Guid.NewGuid(), CountryPlaceId = Guid.NewGuid(), Iso2 = "US", Iso3 = "USA" }); await db.SaveChangesAsync();
        Assert.Contains((await service.PreviewCatalogImportAsync(m)).Issues, i => i.Code == "CatalogImportExistingCountryRequiresReconciliation");
        Assert.Empty(db.GeographicPlaces);
    }

    [Fact]
    public async Task Explicit_provider_bootstrap_uses_authenticated_reference_path_and_never_activates_or_publishes()
    {
        await using var f = await BostaGeographyFixture.Create(); f.Db.CarrierProviders.RemoveRange(f.Db.CarrierProviders); await f.Db.SaveChangesAsync();
        var before = System.Text.Json.JsonSerializer.Serialize(f.Account);
        var controller = f.Controller();
        Assert.IsType<ConflictObjectResult>(await controller.Synchronize(f.Account.Id, new(BostaGeographyFixture.CityId), default));
        Assert.Empty(f.Db.CarrierGeographicSyncRuns);
        var response = await controller.Bootstrap(f.Account.Id, new(BostaGeographyFixture.CityId, "Explicit synthetic metadata bootstrap"), default);
        Assert.IsType<OkObjectResult>(response); Assert.Single(f.Db.CarrierProviders); Assert.Single(f.Db.CarrierGeographicSyncRuns);
        Assert.Equal(3, await f.Db.CarrierGeographicSyncCandidates.CountAsync());
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(f.Account));
        Assert.Contains(f.Db.GeographicAudit, a => a.Action == "BostaGeographyBootstrapReviewedV1");
        Assert.Empty(f.Db.CarrierGeographicMappingPublications); Assert.Empty(f.Db.Shipments);
        var run = await f.Db.CarrierGeographicSyncRuns.SingleAsync();
        Assert.IsType<NotFoundResult>(await controller.ReadSync(Guid.NewGuid(), run.Id, default));
        f.Account.ActiveCredentialReferenceId = Guid.NewGuid(); await f.Db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await controller.ReadSync(f.Account.Id, run.Id, default));
    }

    [Fact]
    public async Task Disabled_geographic_provider_is_not_silently_reenabled_by_bootstrap()
    {
        await using var f = await BostaGeographyFixture.Create();
        (await f.Db.CarrierProviders.SingleAsync()).IsActive = false; await f.Db.SaveChangesAsync();
        Assert.IsType<ConflictObjectResult>(await f.Controller().Bootstrap(f.Account.Id, new(BostaGeographyFixture.CityId, "Synthetic"), default));
        Assert.False((await f.Db.CarrierProviders.SingleAsync()).IsActive); Assert.Empty(f.Db.CarrierGeographicSyncRuns);
    }

    [Theory]
    [InlineData("duplicate-canonical")][InlineData("incompatible-kind")][InlineData("missing-parent")][InlineData("unserviceable")]
    public async Task Mapping_selection_rejects_ambiguity_and_incompatible_references_before_any_candidate_is_approved(string variant)
    {
        await using var f = await BostaGeographyFixture.Create(); await f.Synchronize(); var request = f.DraftRequest();
        var district = await f.Db.CarrierGeographicSyncCandidates.SingleAsync(c => c.ProviderPlaceType == "district");
        if (variant == "duplicate-canonical") request = request with { Choices = request.Choices.Select(c => c.CandidateId == district.Id ? c with { CanonicalPlaceId = f.City } : c).ToArray() };
        if (variant == "incompatible-kind") request = request with { Choices = request.Choices.Select(c => c.CandidateId == district.Id ? c with { CanonicalPlaceId = f.Country } : c with { CanonicalPlaceId = c.CanonicalPlaceId == f.Country ? f.District : c.CanonicalPlaceId }).ToArray() };
        if (variant == "missing-parent") district.ProviderParentPlaceId = "foreign-parent";
        if (variant == "unserviceable") district.CandidateStatus = CarrierGeographicSyncCandidateStatus.Rejected;
        await f.Db.SaveChangesAsync();
        var result = await f.Administration.AcceptBostaMappingAsync(f.Tenant, f.Account.Id, f.Account.ActiveCredentialReferenceId, request, f.Creator, "Owner");
        Assert.False(result.Succeeded);
        Assert.Empty(f.Db.CarrierGeographicMappingReleases); Assert.Empty(f.Db.CarrierGeographicMappings);
        Assert.DoesNotContain(f.Db.CarrierGeographicSyncCandidates, c => c.CandidateStatus == CarrierGeographicSyncCandidateStatus.Approved);
        Assert.Empty(f.Db.ChangeTracker.Entries<CarrierGeographicSyncCandidate>().Where(e => e.State == EntityState.Modified));
    }
}
