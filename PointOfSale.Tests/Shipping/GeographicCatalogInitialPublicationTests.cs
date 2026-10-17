using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.GeographicAdministration;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Geographic;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

// Synthetic in-process catalog ONLY. No official source is fetched, no relational connection
// is opened, and no production catalog/source eligibility is certified by these fixtures.
public sealed class GeographicCatalogInitialPublicationTests
{
    [Fact]
    public async Task InitialWorkflow_StagesHierarchy_BindsReviewChecksum_ThenAppendsPublicationAndAuditInFixture()
    {
        await using var f = await Fixture.Create();
        var preview = await f.Service.InspectCatalogDraftAsync(f.Release);
        Assert.True(preview.Succeeded, preview.FailureCode);
        Assert.Equal(preview.ContentChecksumSha256, (await f.Service.InspectCatalogDraftAsync(f.Release)).ContentChecksumSha256);
        Assert.Null(await new GeographicCatalogService(new GeographicRepository(f.Db)).GetActivePublishedCatalogReleaseAsync(null));
        Assert.True((await f.Validate()).Succeeded);
        var review = await f.Db.GeographicAudit.SingleAsync(a => a.Action == "CatalogReleaseValidatedV1");
        Assert.Equal(f.Reviewer, review.ActorId);
        Assert.Equal(preview.ContentChecksumSha256, review.ValidationChecksumSha256);
        Assert.Contains("synthetic.example.invalid", review.Reason);
        Assert.Contains("geo-initial-v1:sha256:", review.CorrelationId);
        Assert.Equal(2, await f.Db.GeographicCatalogReleaseEdges.CountAsync());
        Assert.True((await f.Publish()).Succeeded);
        Assert.Equal(GeographicReleaseStatus.Published, (await f.Db.GeographicCatalogReleases.SingleAsync()).Status);
        Assert.Single(f.Db.GeographicCatalogPublications);
        Assert.Equal(f.Release, (await new GeographicCatalogService(new GeographicRepository(f.Db)).GetActivePublishedCatalogReleaseAsync(null))!.Id);
        Assert.False((await f.Publish()).Succeeded);
        Assert.Single(f.Db.GeographicCatalogPublications);
        Assert.Empty(f.Db.AddressAdmissionEvidence);
        Assert.Empty(f.Db.Orders);
        Assert.Empty(f.Db.Shipments);
    }

    [Theory]
    [InlineData("source", "CatalogSourceAndIdentityRequired")]
    [InlineData("country-parent", "CatalogCountryLineageInvalid")]
    [InlineData("inactive", "CatalogActivePlaceEvidenceRequired")]
    [InlineData("expired", "CatalogActivePlaceEvidenceRequired")]
    [InlineData("duplicate-code", "CatalogDuplicatePlaceIdentity")]
    [InlineData("manifest", "CatalogPlaceManifestInvalid")]
    [InlineData("profile", "CatalogCountryProfileInvalid")]
    [InlineData("postal", "CatalogPostalRuleInvalid")]
    [InlineData("parent", "CatalogHierarchyIncomplete")]
    [InlineData("cycle", "CatalogHierarchyCycle")]
    [InlineData("edge-manifest", "CatalogEdgeManifestInvalid")]
    [InlineData("required-level", "CatalogRequiredLevelMissing")]
    [InlineData("name", "CatalogLocalizationRequired")]
    [InlineData("alias", "CatalogAdvancedGovernanceWorkflowRequired")]
    public async Task InvalidOrUnsupportedSnapshot_CannotBecomeValidatedOrPublished(string variant, string expected)
    {
        await using var f = await Fixture.Create();
        switch (variant)
        {
            case "source": (await f.Db.GeographicCatalogReleases.SingleAsync()).SourceSummary = ""; break;
            case "country-parent": (await f.Db.GeographicPlaces.SingleAsync(p => p.Id == f.Country)).CountryPlaceId = f.Country; break;
            case "inactive": (await f.Db.GeographicPlaces.SingleAsync(p => p.Id == f.City)).Status = GeographicLifecycleStatus.Draft; break;
            case "expired": (await f.Db.GeographicPlaces.SingleAsync(p => p.Id == f.City)).ValidToUtc = DateTime.UtcNow.AddDays(-1); break;
            case "duplicate-code":
                var city = await f.Db.GeographicPlaces.SingleAsync(p => p.Id == f.City);
                f.Db.GeographicPlaces.Add(new GeographicPlace { Id = Guid.NewGuid(), StableKey = "duplicate", Source = "synthetic", CanonicalCode = city.CanonicalCode,
                    PlaceKind = city.PlaceKind, LevelCode = city.LevelCode, CountryPlaceId = f.Country, IntroducedCatalogReleaseId = f.Release, Status = GeographicLifecycleStatus.Active }); break;
            case "manifest": f.Db.GeographicCatalogReleasePlaces.Remove(await f.Db.GeographicCatalogReleasePlaces.FirstAsync()); break;
            case "profile": (await f.Db.GeographicCountryProfiles.SingleAsync()).Iso2 = "ZZ"; break;
            case "postal": (await f.Db.GeographicCountryProfiles.SingleAsync()).PostalCodeRegex = "["; break;
            case "parent":
                var missingEdge = await f.Db.GeographicPlaceHierarchyEdges.SingleAsync(e => e.ChildPlaceId == f.City);
                f.Db.GeographicCatalogReleaseEdges.Remove(await f.Db.GeographicCatalogReleaseEdges.SingleAsync(e => e.HierarchyEdgeId == missingEdge.Id));
                f.Db.GeographicPlaceHierarchyEdges.Remove(missingEdge); break;
            case "cycle": (await f.Db.GeographicPlaceHierarchyEdges.SingleAsync(e => e.ChildPlaceId == f.State)).ParentPlaceId = f.City; break;
            case "edge-manifest": f.Db.GeographicCatalogReleaseEdges.Remove(await f.Db.GeographicCatalogReleaseEdges.FirstAsync()); break;
            case "required-level": (await f.Db.GeographicCountryProfiles.SingleAsync()).RequiredLevelCodesForOperations = ["MISSING"]; break;
            case "name": f.Db.GeographicPlaceNames.Remove(await f.Db.GeographicPlaceNames.SingleAsync(n => n.PlaceId == f.City)); break;
            case "alias": f.Db.GeographicPlaceAliases.Add(new GeographicPlaceAlias { Id = Guid.NewGuid(), PlaceId = f.City, CountryPlaceId = f.Country,
                IntroducedCatalogReleaseId = f.Release, Alias = "synthetic", Source = "synthetic" }); break;
        }
        await f.Db.SaveChangesAsync();
        var auditCount = await f.Db.GeographicAudit.CountAsync();
        var preview = await f.Service.InspectCatalogDraftAsync(f.Release);
        Assert.False(preview.Succeeded);
        Assert.Equal(expected, preview.FailureCode);
        Assert.Equal(expected, (await f.Validate()).FailureCode);
        Assert.Equal(auditCount, await f.Db.GeographicAudit.CountAsync());
        Assert.Equal(GeographicReleaseStatus.Draft, (await f.Db.GeographicCatalogReleases.SingleAsync()).Status);
        Assert.False((await f.Publish()).Succeeded);
        Assert.Empty(f.Db.GeographicCatalogPublications);
    }

    [Theory]
    [InlineData("unconfirmed", "ReviewedOfficialSourceEvidenceRequired")]
    [InlineData("http", "ReviewedOfficialSourceEvidenceRequired")]
    [InlineData("credentials", "ReviewedOfficialSourceEvidenceRequired")]
    [InlineData("query", "ReviewedOfficialSourceEvidenceRequired")]
    [InlineData("hash", "ReviewedOfficialSourceEvidenceRequired")]
    [InlineData("changed", "CatalogContentChanged")]
    [InlineData("reason", "CatalogActorAndReasonRequired")]
    [InlineData("creator", "FourEyesApprovalRequired")]
    [InlineData("role", "CatalogActorAndReasonRequired")]
    public async Task ValidationRequiresIndependentExplicitBoundedSourceReview(string variant, string expected)
    {
        await using var f = await Fixture.Create();
        var request = await f.ReviewRequest(); var actor = f.Reviewer; var role = "Owner";
        request = variant switch
        {
            "unconfirmed" => request with { ConfirmOfficialSourceReviewed = false },
            "http" => request with { SourceEvidenceUri = "http://synthetic.example.invalid/catalog" },
            "credentials" => request with { SourceEvidenceUri = "https://user:secret@synthetic.example.invalid/catalog" },
            "query" => request with { SourceEvidenceUri = "https://synthetic.example.invalid/catalog?token=not-a-real-token" },
            "hash" => request with { SourceEvidenceSha256 = "not-a-hash" },
            "changed" => request with { ExpectedContentChecksumSha256 = new string('f', 64) },
            "reason" => request with { Reason = "" },
            _ => request
        };
        if (variant == "creator") actor = f.Creator;
        if (variant == "role") role = "Cashier";
        var result = await f.Service.ValidateCatalogDraftAsync(f.Release, request, actor, role);
        Assert.Equal(expected, result.FailureCode);
        Assert.Empty(f.Db.GeographicAudit.Where(a => a.Action == "CatalogReleaseValidatedV1"));
        Assert.Equal(GeographicReleaseStatus.Draft, (await f.Db.GeographicCatalogReleases.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("metadata")][InlineData("place")][InlineData("name")][InlineData("profile")]
    [InlineData("edge")][InlineData("alias")][InlineData("merge")][InlineData("deprecation")]
    public async Task AllSupportedCatalogWrites_RejectValidatedSnapshots(string mutation)
    {
        await using var f = await Fixture.Create();
        Assert.True((await f.Validate()).Succeeded);
        var before = (await f.Service.InspectCatalogDraftAsync(f.Release)).ContentChecksumSha256;
        var count = await f.Db.GeographicAudit.CountAsync();
        var result = mutation switch
        {
            "metadata" => await f.Service.EditCatalogDraftAsync(f.Release, new("replacement", "synthetic"), f.Creator, "Owner"),
            "place" => await f.Service.AddCatalogPlaceAsync(f.Place(Guid.NewGuid(), GeographicPlaceKind.Locality, "OTHER", "CITY"), f.Creator, "Owner"),
            "name" => await f.Service.AddLocalizationAsync(f.Name(f.City), f.Creator, "Owner"),
            "profile" => await f.Service.AddCountryProfileAsync(f.Profile(), f.Creator, "Owner"),
            "edge" => await f.Service.AddHierarchyEdgeAsync(new(f.Release, f.State, f.City, f.Country, "synthetic"), f.Creator, "Owner"),
            "alias" => await f.Service.AddAliasAsync(new(f.Release, f.City, f.Country, "en", GeographicAliasKind.Spelling, "synthetic", "synthetic", "synthetic", 100, "synthetic", false, "synthetic"), f.Creator, "Owner"),
            "merge" => await f.Service.AddMergeAsync(new(f.Release, f.City, f.State, PlaceMergeKind.Merge, "synthetic"), f.Creator, "Owner"),
            _ => await f.Service.AddDeprecationAsync(new(f.Release, f.City, null, PlaceDeprecationKind.ManualReviewOnly, "synthetic"), f.Creator, "Owner")
        };
        Assert.False(result.Succeeded);
        Assert.Equal(count, await f.Db.GeographicAudit.CountAsync());
        Assert.Equal(before, (await f.Service.InspectCatalogDraftAsync(f.Release)).ContentChecksumSha256);
    }

    [Fact]
    public async Task LegacyValidatedFlagAlone_DoesNotConferPublicationAuthority()
    {
        await using var f = await Fixture.Create();
        (await f.Db.GeographicCatalogReleases.SingleAsync()).Status = GeographicReleaseStatus.Validated;
        await f.Db.SaveChangesAsync();
        Assert.Equal("CatalogValidationEvidenceRequired", (await f.Publish()).FailureCode);
        Assert.Empty(f.Db.GeographicCatalogPublications);
    }

    [Fact]
    public async Task OutOfBandContentChange_AfterValidationCannotReuseChecksumApproval()
    {
        await using var f = await Fixture.Create();
        Assert.True((await f.Validate()).Succeeded);
        (await f.Db.GeographicPlaces.SingleAsync(p => p.Id == f.City)).CanonicalCode = "CHANGED-SYNTHETIC";
        await f.Db.SaveChangesAsync();
        Assert.Equal("CatalogContentChanged", (await f.Publish()).FailureCode);
        Assert.Empty(f.Db.GeographicCatalogPublications);
        Assert.Single(f.Db.GeographicAudit.Where(a => a.Action == "CatalogReleaseValidatedV1"));
    }

    [Fact]
    public async Task NonCreatorEditor_CannotReviewOrApproveOwnChanges()
    {
        await using var f = await Fixture.Create();
        Assert.True((await f.Service.EditCatalogDraftAsync(f.Release, new("synthetic source edited", "synthetic"), f.Reviewer, "Owner")).Succeeded);
        Assert.Equal("FourEyesApprovalRequired", (await f.Validate()).FailureCode);
        var request = await f.ReviewRequest();
        Assert.True((await f.Service.ValidateCatalogDraftAsync(f.Release, request, Guid.NewGuid(), "Owner")).Succeeded);
        Assert.Equal("FourEyesApprovalRequired", (await f.Publish()).FailureCode);
        Assert.Empty(f.Db.GeographicCatalogPublications);
    }

    [Fact]
    public async Task DuplicateOrForeignHierarchyStaging_AndFalseRollbackAreRejected()
    {
        await using var f = await Fixture.Create();
        var before = await f.Db.GeographicAudit.CountAsync();
        Assert.Equal("CatalogPrimaryParentAlreadyExists", (await f.Service.AddHierarchyEdgeAsync(new(f.Release, f.State, f.City, f.Country, "synthetic"), f.Creator, "Owner")).FailureCode);
        Assert.Equal("CatalogHierarchyInvalid", (await f.Service.AddHierarchyEdgeAsync(new(f.Release, Guid.NewGuid(), f.City, f.Country, "synthetic"), f.Creator, "Owner")).FailureCode);
        Assert.Equal(before, await f.Db.GeographicAudit.CountAsync());
        Assert.True((await f.Validate()).Succeeded);
        Assert.True((await f.Publish()).Succeeded);
        Assert.Equal("CatalogReplacementAndRollbackWorkflowRequired", (await f.Service.RollbackCatalogReleaseAsync(f.Release, new("synthetic"), f.Reviewer, "Owner")).FailureCode);
        Assert.Single(f.Db.GeographicCatalogPublications);
        Assert.Empty(f.Db.GeographicAudit.Where(a => a.Action == "CatalogReleaseRollbackActivated"));
    }

    [Fact]
    public async Task AdditionalPublicationHistory_CannotBeTreatedAsInitialActivation()
    {
        await using var f = await Fixture.Create();
        Assert.True((await f.Validate()).Succeeded);
        f.Db.GeographicCatalogPublications.Add(new GeographicCatalogPublication { Id = Guid.NewGuid(), CatalogReleaseId = f.Release,
            Reason = "Synthetic historical publication evidence", PublicationKind = GeographicPublicationKind.Publish });
        await f.Db.SaveChangesAsync();
        Assert.Equal("CatalogReplacementAndRollbackWorkflowRequired", (await f.Publish()).FailureCode);
        Assert.Single(f.Db.GeographicCatalogPublications);
    }

    [Fact]
    public void NewWorkflowRoutes_RetainCanonicalPosGeographicAuthority()
    {
        var controller = typeof(AdminGeographicAdministrationController);
        Assert.Equal("Owner,Admin,Manager", controller.GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>().Single(a => a.Roles != null).Roles);
        Assert.Contains(controller.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>(), a => a.Policy == PosPermissions.ShippingManage);
        foreach (var (action, verb) in new[] { ("AddHierarchyEdge", "POST"), ("InspectCatalogDraft", "GET"), ("ValidateCatalogDraft", "POST") })
        {
            Assert.NotNull(controller.GetMethod(action));
            Assert.True(PosEndpointPermissionCatalog.TryResolve("AdminGeographicAdministration", action, [verb], out var permission));
            Assert.Equal(PosPermissions.ShippingManage, permission);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Creator { get; } = Guid.NewGuid();
        public Guid Reviewer { get; } = Guid.NewGuid();
        public Guid Country { get; } = Guid.NewGuid();
        public Guid State { get; } = Guid.NewGuid();
        public Guid City { get; } = Guid.NewGuid();
        public Guid Release { get; private set; }
        public ApplicationDbContext Db { get; }
        public GeographicAdministrationService Service { get; }
        private Fixture()
        {
            Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"InitialCatalog-{Guid.NewGuid()}").Options,
                new TestTenantContext(Guid.NewGuid()));
            Service = new(Db);
        }
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            var draft = await f.Service.CreateCatalogDraftAsync(new(1, "synthetic-initial", "US", "Synthetic fixture only, not official catalog data", "synthetic"), f.Creator, "Owner");
            f.Release = draft.ResourceId!.Value;
            foreach (var request in new[] { f.Place(f.Country, GeographicPlaceKind.Country, "US", "COUNTRY"),
                         f.Place(f.State, GeographicPlaceKind.AdministrativeArea, "TEST-STATE", "STATE"), f.Place(f.City, GeographicPlaceKind.Locality, "TEST-CITY", "CITY") })
            {
                Assert.True((await f.Service.AddCatalogPlaceAsync(request, f.Creator, "Owner")).Succeeded);
                Assert.True((await f.Service.AddLocalizationAsync(f.Name(request.PlaceId!.Value), f.Creator, "Owner")).Succeeded);
            }
            Assert.True((await f.Service.AddCountryProfileAsync(f.Profile(), f.Creator, "Owner")).Succeeded);
            Assert.True((await f.Service.AddHierarchyEdgeAsync(new(f.Release, f.Country, f.State, f.Country, "synthetic"), f.Creator, "Owner")).Succeeded);
            Assert.True((await f.Service.AddHierarchyEdgeAsync(new(f.Release, f.State, f.City, f.Country, "synthetic"), f.Creator, "Owner")).Succeeded);
            return f;
        }
        public CatalogPlaceRequest Place(Guid id, GeographicPlaceKind kind, string code, string level) => new(Release, id, id.ToString("N"), kind,
            kind == GeographicPlaceKind.Country ? null : Country, level, code, null, null, GeographicLifecycleStatus.Active, null,
            DateTime.UtcNow.AddDays(-2), null, "synthetic-only", "synthetic");
        public LocalizationRequest Name(Guid id) => new(Release, id, "en", null, GeographicNameKind.Display,
            "Synthetic name", "synthetic name", "synthetic name", 1, true, DateTime.UtcNow.AddDays(-2), null, "synthetic");
        public CountryProfileRequest Profile() => new(Release, Country, "US", "USA", "en", ["en"], ["STATE", "CITY"], ["STATE"],
            GeographicPostalCodePolicy.Required, "\\A[0-9]{5}\\z", true, false, false, false, true, "synthetic");
        public async Task<CatalogValidationRequest> ReviewRequest() => new((await Service.InspectCatalogDraftAsync(Release)).ContentChecksumSha256 ?? new string('a', 64),
            "https://synthetic.example.invalid/catalog", new string('a', 64), true, "Synthetic fixture attestation only");
        public async Task<GeographicOperationResultDto> Validate() => await Service.ValidateCatalogDraftAsync(Release, await ReviewRequest(), Reviewer, "Owner");
        public Task<GeographicOperationResultDto> Publish() => Service.PublishCatalogReleaseAsync(Release, new("Synthetic publication assertion"), Reviewer, "Owner");
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
