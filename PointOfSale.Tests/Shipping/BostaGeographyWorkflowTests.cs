using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Geographic;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Shipping;

// Provider IDs/public labels captured by GET on 2026-10-15. ALL canonical places,
// accounts, people and shipment effects in this fixture are synthetic, process-local.
internal sealed class BostaGeographyFixture : IAsyncDisposable
{
    internal const string CountryId = "60e4482c7cb7d4bc4849c4d5", CityId = "FceDyHXwpSYYF9zGW", DistrictId = "VPRW3NEl5WQ", PickupId = "PZ_CVuhF4R";
    internal const string PickupJson = """{"success":true,"data":{"total":1,"list":[{"_id":"PZ_CVuhF4R","address":{"country":{"_id":"60e4482c7cb7d4bc4849c4d5","code":"EG"},"city":{"_id":"FceDyHXwpSYYF9zGW","name":"Cairo"},"district":{"_id":"VPRW3NEl5WQ","name":"Zahraa Nasr City"}}}]}}""";
    internal const string CitiesJson = """{"success":true,"data":{"list":[{"_id":"FceDyHXwpSYYF9zGW","name":"Cairo","code":"EG-01","pickupAvailability":true,"dropOffAvailability":true}]}}""";
    internal const string DistrictsJson = """{"success":true,"data":[{"districtId":"VPRW3NEl5WQ","districtName":"Zahraa Nasr City","pickupAvailability":true,"dropOffAvailability":true}]}""";
    public Guid Tenant { get; } = Guid.NewGuid();
    public Guid Creator { get; } = Guid.NewGuid();
    public Guid Reviewer { get; } = Guid.NewGuid();
    public Guid Catalog { get; } = Guid.NewGuid();
    public Guid Country { get; } = Guid.NewGuid();
    public Guid City { get; } = Guid.NewGuid();
    public Guid Locality { get; } = Guid.NewGuid();
    public Guid District { get; } = Guid.NewGuid();
    public CarrierAccount Account { get; private set; } = null!;
    public ApplicationDbContext Db { get; }
    public GeographicAdministrationService Administration { get; }
    public CarrierGeographicMappingService Mapping { get; }
    public Mock<ICarrierIntegrationAuthority> Authority { get; } = new(MockBehavior.Strict);
    public Mock<IShippingCarrierRegistry> Registry { get; } = new();
    public TenantContext TenantContext { get; } = new();
    public BostaAdapter Adapter { get; }
    public Guid RunId { get; private set; }
    public string Hash { get; private set; } = "";
    public BostaMappingChoice[] Choices { get; private set; } = [];
    public Guid Release { get; private set; }

    public BostaGeographyFixture()
    {
        TenantContext.SetTenantId(Tenant);
        Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("Bosta-test-only-" + Guid.NewGuid()).Options, TenantContext);
        Administration = new(Db); Mapping = new(new GeographicRepository(Db));
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(f => f.CreateClient("Bosta")).Returns(new HttpClient(new ReferenceTransport()));
        Adapter = new(factory.Object, NullLogger<BostaAdapter>.Instance); Registry.Setup(r => r.GetAdapter("bosta")).Returns(Adapter);
        Authority.Setup(a => a.ResolveConnectionCredentialsAsync(Tenant, "bosta", It.IsAny<CancellationToken>())).ReturnsAsync(new CarrierCredentialsResolution(true, "bosta",
            new() {ApiKey = "synthetic-only", ExtraConfig = new() {["businessLocationId"] = PickupId}},
            JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>("""{"CarrierCode":"bosta","UseSandbox":false}"""), []));
    }

    public static async Task<BostaGeographyFixture> Create()
    {
        var f = new BostaGeographyFixture(); var now = DateTime.UtcNow;
        f.Account = new() { Id = Guid.NewGuid(), TenantId = f.Tenant, CarrierCode = "bosta", Environment = CarrierAccountEnvironment.Production, ActiveCredentialReferenceId = Guid.NewGuid() };
        f.Db.CarrierAccounts.Add(f.Account);
        f.Db.CarrierProviders.Add(new() {Id = Guid.NewGuid(), CarrierCode = "bosta", DisplayName = "Bosta", IsActive = true, RequiresMappingForDelivery = true});
        f.Db.GeographicCatalogReleases.Add(new() {Id = f.Catalog, ReleaseNumber = 1, ReleaseKey = "TEST-ONLY-BOSTA-CANONICAL", CountryScopeIso2 = "EG", Status = GeographicReleaseStatus.Published, PublishedAtUtc = now});
        f.Db.GeographicCatalogPublications.Add(new() {Id = Guid.NewGuid(), CatalogReleaseId = f.Catalog, PublicationKind = GeographicPublicationKind.Publish,
            PublishedAtUtc = now, ActivatedAtUtc = now, PublishedByUserId = f.Reviewer, Reason = "Synthetic test-only active catalog", CreatedAtUtc = now});
        var ids = new[] { f.Country, f.City, f.Locality, f.District };
        for (var i = 0; i < ids.Length; i++)
        {
            f.Db.GeographicPlaces.Add(new() {Id = ids[i], IntroducedCatalogReleaseId = f.Catalog, StableKey = "TEST-ONLY:" + ids[i], Source = "SYNTHETIC-TEST-ONLY",
                PlaceKind = (GeographicPlaceKind)i, CountryPlaceId = i == 0 ? null : f.Country, CanonicalCode = i == 0 ? "EG" : "SYNTHETIC-" + i,
                LevelCode = i.ToString(), Status = GeographicLifecycleStatus.Active, ValidFromUtc = now.AddDays(-1)});
            if (i > 0) f.Db.GeographicPlaceHierarchyEdges.Add(new() {Id = Guid.NewGuid(), ParentPlaceId = ids[i-1], ChildPlaceId = ids[i], CountryPlaceId = f.Country,
                IntroducedCatalogReleaseId = f.Catalog, IsPrimary = true, Depth = 1});
        }
        f.Db.GeographicCountryProfiles.Add(new() {Id = Guid.NewGuid(), CountryPlaceId = f.Country, Iso2 = "EG", Iso3 = "EGY", IsActive = true,
            IntroducedCatalogReleaseId = f.Catalog, SupportedLanguageTags = ["en"], RequiredLevelCodesForDelivery = [], RequiredLevelCodesForOperations = [], PostalCodePolicy = GeographicPostalCodePolicy.NotUsed});
        await f.Db.SaveChangesAsync();
        return f;
    }

    public AdminBostaGeographyController Controller() => new(Db, TenantContext, Authority.Object, Registry.Object, Administration) {
        ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Creator.ToString()), new Claim(ClaimTypes.Role, "Owner")], "synthetic-test")) } } };

    public async Task Synchronize()
    {
        var response = Assert.IsType<OkObjectResult>(await Controller().Synchronize(Account.Id, new(CityId), default));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
        RunId = json.RootElement.GetProperty("Id").GetGuid(); Hash = json.RootElement.GetProperty("sourceHashSha256").GetString()!;
        Choices = (await Db.CarrierGeographicSyncCandidates.Where(c => c.SyncRunId == RunId).ToListAsync()).Select(c => new BostaMappingChoice(c.Id,
            c.ProviderPlaceType == "country" ? Country : c.ProviderPlaceType == "city" ? City : District)).ToArray();
    }
    public BostaMappingDraftRequest DraftRequest() => new(RunId, Catalog, Hash, Choices, "SYNTHETIC TEST-ONLY review, not a production destination");
    public async Task Accept()
    {
        var result = await Administration.AcceptBostaMappingAsync(Tenant, Account.Id, Account.ActiveCredentialReferenceId, DraftRequest(), Creator, "Owner");
        Assert.True(result.Succeeded, result.FailureCode); Release = result.ResourceId!.Value;
    }
    public async Task Publish()
    {
        var preview = await Administration.InspectMappingDraftAsync(Release); Assert.True(preview.Succeeded, preview.FailureCode);
        var validation = await Administration.ValidateMappingDraftAsync(Release, new(preview.ContentChecksumSha256!, "Synthetic independent review"), Reviewer, "Owner");
        Assert.True(validation.Succeeded, validation.FailureCode);
        var publication = await Administration.PublishMappingReleaseAsync(Release, new("Synthetic publication", preview.ContentChecksumSha256), Reviewer, "Owner");
        Assert.True(publication.Succeeded, publication.FailureCode);
    }
    public CarrierGeographicMappingRequest Destination(Guid? district = null) => new("bosta", CarrierGeographicEnvironment.Production,
        CarrierGeographicAccountScopeKind.TenantProviderAccount, Tenant, Tenant, "ShipmentRegistration", Catalog,
        new(Country, City, Locality, district ?? District, null), ["country", "city", "district"], null, true);
    public ValueTask DisposeAsync() => Db.DisposeAsync();
    private sealed class ReferenceTransport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var body = request.RequestUri!.AbsolutePath switch {
                "/api/v2/pickup-locations" => PickupJson,
                "/api/v2/cities" => CitiesJson,
                "/api/v2/cities/FceDyHXwpSYYF9zGW/districts" => DistrictsJson,
                _ => throw new InvalidOperationException("No external or mutating transport allowed") };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content = new StringContent(body)});
        }
    }
}

public sealed class BostaGeographyWorkflowTests
{
    [Fact]
    public async Task Real_reference_identifiers_flow_through_sync_review_publication_persisted_readback_and_destination_admission()
    {
        await using var f = await BostaGeographyFixture.Create(); await f.Synchronize(); await f.Accept();
        Assert.False((await f.Mapping.ResolvePublishedMappingAsync(f.Destination())).Succeeded);
        await f.Publish(); f.Db.ChangeTracker.Clear();
        var admitted = await f.Mapping.ResolvePublishedMappingAsync(f.Destination());
        Assert.True(admitted.Succeeded, admitted.FailureCode); Assert.Equal(f.Release, admitted.MappingReleaseId);
        Assert.Equal(BostaGeographyFixture.DistrictId, admitted.Items.Single(i => i.ProviderPlaceType == "district").ProviderPlaceId);
        Assert.IsType<OkObjectResult>(await f.Controller().ReadRelease(f.Account.Id, f.Release, default));
        Assert.False((await f.Mapping.ResolvePublishedMappingAsync(f.Destination(Guid.NewGuid()))).Succeeded);
        Assert.False((await f.Mapping.ResolvePublishedMappingAsync(f.Destination() with {Environment = CarrierGeographicEnvironment.Sandbox})).Succeeded);
        Assert.False((await f.Mapping.ResolvePublishedMappingAsync(f.Destination() with {TenantId = Guid.NewGuid()})).Succeeded);
    }

    [Theory]
    [InlineData("source")][InlineData("foreign")][InlineData("stale")][InlineData("rejected")][InlineData("unpublished")]
    public async Task Stale_foreign_unreviewable_or_substituted_reference_cannot_create_mapping_draft(string variant)
    {
        await using var f = await BostaGeographyFixture.Create(); await f.Synchronize(); var request = f.DraftRequest();
        if (variant == "source") request = request with {ExpectedSourceHashSha256 = new string('f',64)};
        if (variant == "foreign") request = request with {SyncRunId = Guid.NewGuid()};
        if (variant == "stale") { (await f.Db.CarrierGeographicSyncRuns.SingleAsync()).CompletedAtUtc = DateTime.UtcNow.AddDays(-2); await f.Db.SaveChangesAsync(); }
        if (variant == "rejected") { (await f.Db.CarrierGeographicSyncCandidates.FirstAsync()).CandidateStatus = CarrierGeographicSyncCandidateStatus.Rejected; await f.Db.SaveChangesAsync(); }
        if (variant == "unpublished") request = request with {CatalogReleaseId = Guid.NewGuid()};
        var result = await f.Administration.AcceptBostaMappingAsync(f.Tenant, f.Account.Id, f.Account.ActiveCredentialReferenceId, request, f.Creator, "Owner");
        Assert.False(result.Succeeded); Assert.Empty(f.Db.CarrierGeographicMappingReleases);
    }

    [Fact]
    public async Task Four_eyes_hash_fencing_and_validated_draft_isolation_prevent_changed_publication()
    {
        await using var f = await BostaGeographyFixture.Create(); await f.Synchronize(); await f.Accept();
        var preview = await f.Administration.InspectMappingDraftAsync(f.Release);
        Assert.Equal("FourEyesApprovalRequired", (await f.Administration.ValidateMappingDraftAsync(f.Release, new(preview.ContentChecksumSha256!, "synthetic"), f.Creator,"Owner")).FailureCode);
        Assert.Equal("MappingContentChanged", (await f.Administration.ValidateMappingDraftAsync(f.Release,new(new string('f',64),"synthetic"),f.Reviewer,"Owner")).FailureCode);
        Assert.True((await f.Administration.ValidateMappingDraftAsync(f.Release,new(preview.ContentChecksumSha256!,"synthetic"),f.Reviewer,"Owner")).Succeeded);
        (await f.Db.CarrierGeographicMappings.FirstAsync()).ProviderDisplayName = "Changed after review"; await f.Db.SaveChangesAsync();
        Assert.Equal("MappingContentChanged", (await f.Administration.PublishMappingReleaseAsync(f.Release,new("synthetic",preview.ContentChecksumSha256),f.Reviewer,"Owner")).FailureCode);
        Assert.Empty(f.Db.CarrierGeographicMappingPublications);
    }

    [Theory]
    [InlineData("credentials")][InlineData("environment")][InlineData("catalog")][InlineData("freshness")][InlineData("parent")]
    public async Task Revalidation_rejects_stale_authority_and_parent_substitution(string change)
    {
        await using var f = await BostaGeographyFixture.Create(); await f.Synchronize(); await f.Accept();
        var preview = await f.Administration.InspectMappingDraftAsync(f.Release);
        Assert.True((await f.Administration.ValidateMappingDraftAsync(f.Release, new(preview.ContentChecksumSha256!, "Synthetic independent review"), f.Reviewer, "Owner")).Succeeded);
        if (change == "credentials") f.Account.ActiveCredentialReferenceId = Guid.NewGuid();
        if (change == "environment") f.Account.Environment = CarrierAccountEnvironment.Sandbox;
        if (change == "catalog") (await f.Db.GeographicCatalogReleases.SingleAsync()).Status = GeographicReleaseStatus.Superseded;
        if (change == "freshness") (await f.Db.CarrierGeographicSyncRuns.SingleAsync()).CompletedAtUtc = DateTime.UtcNow.AddDays(-2);
        if (change == "parent") (await f.Db.CarrierGeographicMappings.SingleAsync(m => m.ProviderPlaceType == "district")).ProviderParentMappingId = null;
        await f.Db.SaveChangesAsync();
        var result = await f.Administration.PublishMappingReleaseAsync(f.Release, new("Synthetic publication", preview.ContentChecksumSha256), f.Reviewer, "Owner");
        Assert.False(result.Succeeded); Assert.Empty(await f.Db.CarrierGeographicMappingPublications.ToListAsync());
        Assert.False((await f.Mapping.ResolvePublishedMappingAsync(f.Destination())).Succeeded);
    }

    [Fact]
    public async Task Generic_candidate_routes_cannot_forge_or_rewrite_authenticated_Bosta_reference()
    {
        await using var f = await BostaGeographyFixture.Create(); await f.Synchronize();
        var candidate = await f.Db.CarrierGeographicSyncCandidates.FirstAsync();
        Assert.True(await f.Administration.SyncBelongsToTenantAsync(candidate.Id, f.Tenant, true));
        Assert.False(await f.Administration.SyncBelongsToTenantAsync(candidate.Id, Guid.NewGuid(), true));
        var result = await f.Administration.ReviewSyncCandidateAsync(candidate.Id, CarrierGeographicSyncCandidateStatus.Approved,
            new("Cannot bypass dedicated review"), f.Reviewer, "Owner");
        Assert.Equal("BOSTA_REVIEWED_MAPPING_WORKFLOW_REQUIRED", result.FailureCode);
        Assert.Equal(CarrierGeographicSyncCandidateStatus.PendingReview, candidate.CandidateStatus);
        Assert.Empty(await f.Administration.GetTenantAuditAsync(Guid.NewGuid(), 200));
    }

    [Fact]
    public async Task New_publication_and_rollback_append_evidence_without_mutating_history_or_falling_back()
    {
        await using var f = await BostaGeographyFixture.Create(); await f.Synchronize(); await f.Accept(); await f.Publish();
        var original = f.Release;
        var first = await f.Db.CarrierGeographicMappingPublications.AsNoTracking().SingleAsync();
        await f.Synchronize(); await f.Accept(); await f.Publish();
        Assert.NotEqual(original, f.Release);
        Assert.Equal(f.Release, (await f.Mapping.ResolvePublishedMappingAsync(f.Destination())).MappingReleaseId);
        var retained = await f.Db.CarrierGeographicMappingPublications.AsNoTracking().SingleAsync(p => p.Id == first.Id);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(retained));
        var originalChecksum = (await f.Db.CarrierGeographicMappingReleases.AsNoTracking().SingleAsync(r => r.Id == original)).ChecksumSha256;
        var rollback = await f.Administration.RollbackMappingReleaseAsync(original, new("Synthetic rollback review", originalChecksum), f.Reviewer, "Owner");
        Assert.True(rollback.Succeeded, rollback.FailureCode);
        Assert.Equal(original, (await f.Mapping.ResolvePublishedMappingAsync(f.Destination())).MappingReleaseId);
        Assert.Equal(3, await f.Db.CarrierGeographicMappingPublications.CountAsync());
    }
}
