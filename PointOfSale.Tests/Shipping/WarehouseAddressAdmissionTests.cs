using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Inventory;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Geographic;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

public sealed partial class WarehouseAddressAdmissionTests
{
    private readonly Guid tenant = Guid.NewGuid(), actor = Guid.NewGuid(), warehouse = Guid.NewGuid();
    private readonly Guid release = Guid.NewGuid(), country = Guid.NewGuid(), state = Guid.NewGuid(), locality = Guid.NewGuid();
    private readonly DbContextOptions<ApplicationDbContext> options;

    public WarehouseAddressAdmissionTests()
    {
        options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"WarehouseAdmission-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        using var db = Db();
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Synthetic", Slug = tenant.ToString("N"), IsActive = true, Status = TenantStatus.Active });
        db.Users.Add(new User { Id = actor, TenantId = tenant, Username = "operator", Email = "operator@example.invalid", PasswordHash = "synthetic", Role = UserRole.Manager, IsActive = true });
        db.Warehouses.Add(new Warehouse { Id = warehouse, TenantId = tenant, Name = "Synthetic warehouse", Address = "10 Test Street", Phone = "15555550100", IsActive = true });
        db.GeographicCatalogReleases.Add(new GeographicCatalogRelease { Id = release, Status = GeographicReleaseStatus.Published, ReleaseKey = "synthetic", ReleaseNumber = 1, CreatedByUserId = actor, PublishedAtUtc = DateTime.UtcNow.AddMinutes(-1), ChecksumSha256 = new string('A', 64), SourceSummary = "isolated synthetic fixture only" });
        db.GeographicCatalogPublications.Add(new GeographicCatalogPublication { Id = Guid.NewGuid(), CatalogReleaseId = release,
            PublishedAtUtc = DateTime.UtcNow.AddMinutes(-1), ActivatedAtUtc = DateTime.UtcNow.AddMinutes(-1), Reason = "isolated fixture" });
        foreach (var (id, kind, code, level, name) in new[] {
            (country, GeographicPlaceKind.Country, "US", "COUNTRY", "United States"),
            (state, GeographicPlaceKind.AdministrativeArea, "TN", "STATE", "Tennessee"),
            (locality, GeographicPlaceKind.Locality, "MEMPHIS", "CITY", "Memphis") })
        {
            db.GeographicPlaces.Add(new GeographicPlace { Id = id, PlaceKind = kind, CountryPlaceId = country, CanonicalCode = code, LevelCode = level,
                StableKey = id.ToString("N"), Status = GeographicLifecycleStatus.Active, IntroducedCatalogReleaseId = release, Source = "isolated synthetic fixture" });
            db.GeographicCatalogReleasePlaces.Add(new GeographicCatalogReleasePlace { CatalogReleaseId = release, PlaceId = id, PlaceStatus = GeographicLifecycleStatus.Active });
            db.GeographicPlaceNames.Add(new GeographicPlaceName { Id = Guid.NewGuid(), PlaceId = id, Name = name, LanguageTag = "en", IsPreferred = true, IntroducedCatalogReleaseId = release });
        }
        db.GeographicPlaceHierarchyEdges.AddRange(Edge(country, state), Edge(state, locality));
        foreach (var edge in db.ChangeTracker.Entries<GeographicPlaceHierarchyEdge>().Select(e => e.Entity).ToArray())
            db.GeographicCatalogReleaseEdges.Add(new GeographicCatalogReleaseEdge { CatalogReleaseId = release, HierarchyEdgeId = edge.Id });
        db.GeographicCountryProfiles.Add(new GeographicCountryProfile { Id = Guid.NewGuid(), CountryPlaceId = country, Iso2 = "US", Iso3 = "USA",
            IsActive = true, IntroducedCatalogReleaseId = release, RequiresStreetLine1 = true, PostalCodePolicy = GeographicPostalCodePolicy.Required,
            PostalCodeRegex = @"\A[0-9]{5}(-[0-9]{4})?\z", RequiredLevelCodesForDelivery = ["STATE", "CITY"], RequiredLevelCodesForOperations = ["STATE"] });
        db.SaveChanges();
    }

    private GeographicPlaceHierarchyEdge Edge(Guid parent, Guid child) => new() { Id = Guid.NewGuid(), ParentPlaceId = parent,
        ChildPlaceId = child, CountryPlaceId = country, IntroducedCatalogReleaseId = release };
    private ApplicationDbContext Db(Guid? tenantId = null) => new(options, new TestTenantContext(tenantId ?? tenant));
    private WarehouseAddressAdmissionRequest Request(string? postal = "38116") => new() { StreetLine1 = "10 Test Street", PostalCode = postal,
        CountryPlaceId = country, AdministrativeAreaPlaceId = state, LocalityPlaceId = locality, Reason = "Verified genuine operator-supplied test facts" };
    private WarehouseAddressAdmissionService Service(ApplicationDbContext db)
    {
        var repository = new GeographicRepository(db); var catalog = new GeographicCatalogService(repository);
        return new(db, new TestTenantContext(tenant), new GeographicResolver(catalog, repository), catalog);
    }
    private WarehousesController Controller(ApplicationDbContext db, bool authenticated = true) => new(db, new TestTenantContext(tenant),
        Mock.Of<PointOfSale.API.Services.IInventoryService>(), Mock.Of<IEventPublisher>(), NullLogger<WarehousesController>.Instance)
    {
        ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            authenticated ? [new Claim(ClaimTypes.NameIdentifier, actor.ToString())] : [], authenticated ? "test" : null)) } }
    };

    [Theory]
    [InlineData("US", "38116", "TN")]
    [InlineData("CA", "M5V 3L9", "ON")]
    public async Task AuthorizedApiAdmission_PersistsOriginalPostalOwnershipFingerprintAndAudit_AndRestoresFedExOrigin(string iso, string postal, string code)
    {
        await using var db = Db();
        if (iso == "CA")
        {
            (await db.GeographicPlaces.SingleAsync(p => p.Id == country)).CanonicalCode = iso;
            (await db.GeographicPlaces.SingleAsync(p => p.Id == state)).CanonicalCode = code;
            var profile = await db.GeographicCountryProfiles.SingleAsync(); profile.Iso2 = iso; profile.Iso3 = "CAN";
            profile.PostalCodeRegex = @"\A[ABCEGHJ-NPRSTVXY][0-9][ABCEGHJ-NPRSTV-Z] ?[0-9][ABCEGHJ-NPRSTV-Z][0-9]\z";
            await db.SaveChangesAsync();
        }
        var response = Assert.IsType<WarehouseAddressAdmissionResponse>(Assert.IsType<OkObjectResult>(
            await Controller(db).AdmitWarehouseAddress(warehouse, Request($" {postal} "), Service(db), default)).Value);
        await using var inspect = Db(); var evidence = await inspect.AddressAdmissionEvidence.SingleAsync();
        Assert.Equal(response.AddressAdmissionEvidenceId, evidence.Id); Assert.Equal(tenant, evidence.TenantId);
        Assert.Equal(warehouse, evidence.OwnerId); Assert.Equal(AddressAdmissionOwnerDomain.Warehouse, evidence.OwnerDomain);
        Assert.Equal(postal, evidence.PostalCode); Assert.Equal(state, evidence.AdministrativeAreaPlaceId); Assert.Equal(locality, evidence.LocalityPlaceId);
        Assert.NotNull(evidence.CanonicalAddressEvidenceJson);
        var canonical = CanonicalAddressEvidence.Restore(evidence.CanonicalAddressEvidenceJson!, evidence);
        Assert.Equal(iso, canonical.CountryIso2); Assert.Equal(postal, canonical.NormalizedPostalCode);
        Assert.Equal("warehouse-origin", canonical.Inputs.AddressRole);
        Assert.Equal(actor.ToString("N"), canonical.Inputs.PrincipalReference);
        Assert.Equal(CanonicalAddressEvidence.ValidatorVersion, evidence.NormalizationRuleVersion);
        Assert.Equal(CanonicalAddressEvidence.ComputeInputFingerprint(tenant, AddressAdmissionOwnerDomain.Warehouse, warehouse, response.Lineage, canonical.Inputs, postal), evidence.AddressFingerprintSha256);
        var audit = await inspect.GeographicAudit.SingleAsync(); Assert.Equal(actor, audit.ActorId); Assert.Equal(tenant, audit.TenantId);
        Assert.Equal(evidence.Id.ToString("N"), audit.CorrelationId); Assert.Equal(evidence.AddressFingerprintSha256, audit.ValidationChecksumSha256);
        var (shipment, order) = await AddShipment(inspect, iso);
        var origin = await RestoreOrigin(inspect, shipment, order);
        Assert.NotNull(origin); Assert.Equal(postal, origin.PostalCode); Assert.Equal(code, origin.AdministrativeAreaCode);
        Assert.Equal("10 Test Street", origin.Street); Assert.Equal(evidence.Id, origin.AddressAdmissionEvidenceId);
        Assert.Null(shipment.CarrierRequestSnapshotJson); Assert.Null(shipment.ParcelEvidenceJson); Assert.Equal(0, shipment.ExternalRegistrationAttemptCount);
    }

    [Theory]
    [InlineData(null, "PostalCodeRequired")]
    [InlineData("", "PostalCodeRequired")]
    [InlineData("guess", "PostalCodeInvalid")]
    [InlineData("38116 EXTRA", "PostalCodeInvalid")]
    [InlineData("38116\n", "success")]
    [InlineData("38\n116", "PostalCodeInvalid")]
    [InlineData("123456789012345678901234567890123", "PostalCodeInvalid")]
    public async Task PostalValidation_RejectsMissingMalformedOrOversizedInputWithoutDefault(string? postal, string expected)
    {
        await using var db = Db();
        var response = await Controller(db).AdmitWarehouseAddress(warehouse, Request(postal), Service(db), default);
        if (expected == "success") { Assert.IsType<OkObjectResult>(response); Assert.Equal("38116", (await db.AddressAdmissionEvidence.SingleAsync()).PostalCode); }
        else { var bad = Assert.IsType<BadRequestObjectResult>(response); Assert.Contains(expected, System.Text.Json.JsonSerializer.Serialize(bad.Value)); Assert.Empty(db.AddressAdmissionEvidence); Assert.Empty(db.GeographicAudit); }
    }

    [Theory]
    [InlineData("no-country", "CountryPlaceIdRequired")]
    [InlineData("no-state", "UnknownOrUnpublishedPlace")]
    [InlineData("no-locality", "UnknownOrUnpublishedPlace")]
    [InlineData("unknown", "UnknownOrUnpublishedPlace")]
    [InlineData("wrong-kind", "WarehousePlaceKindOrCountryInvalid")]
    [InlineData("foreign-country", "WarehousePlaceKindOrCountryInvalid")]
    [InlineData("retired", "WarehousePlaceKindOrCountryInvalid")]
    [InlineData("expired", "WarehousePlaceKindOrCountryInvalid")]
    [InlineData("hierarchy", "WarehouseHierarchyInvalid")]
    [InlineData("country-profile", "WarehouseCountryProfileRequired")]
    [InlineData("level", "RequiredAdministrativeLevelMissing")]
    [InlineData("unsupported-policy", "WarehousePostalPolicyUnsupported")]
    [InlineData("forbidden-postal", "PostalCodeForbidden")]
    [InlineData("missing-regex", "WarehousePostalRuleRequired")]
    [InlineData("invalid-regex", "WarehousePostalRuleInvalid")]
    public async Task GeographicAuthority_RejectsUnsupportedOrContradictoryFacts(string variant, string expected)
    {
        await using var db = Db(); var request = Request();
        var profile = await db.GeographicCountryProfiles.SingleAsync(); var place = await db.GeographicPlaces.SingleAsync(p => p.Id == state);
        switch (variant)
        {
            case "no-country": request.CountryPlaceId = Guid.Empty; break;
            case "no-state": request.AdministrativeAreaPlaceId = Guid.Empty; break;
            case "no-locality": request.LocalityPlaceId = Guid.Empty; break;
            case "unknown": request.LocalityPlaceId = Guid.NewGuid(); break;
            case "wrong-kind": request.AdministrativeAreaPlaceId = locality; request.LocalityPlaceId = state; break;
            case "foreign-country": place.CountryPlaceId = Guid.NewGuid(); break;
            case "retired": place.RetiredCatalogReleaseId = release; break;
            case "expired": place.ValidToUtc = DateTime.UtcNow.AddMinutes(-1); break;
            case "hierarchy": db.GeographicPlaceHierarchyEdges.Remove(await db.GeographicPlaceHierarchyEdges.SingleAsync(e => e.ChildPlaceId == locality)); break;
            case "country-profile": profile.IsActive = false; break;
            case "level": profile.RequiredLevelCodesForOperations = ["DISTRICT"]; break;
            case "unsupported-policy": profile.PostalCodePolicy = (GeographicPostalCodePolicy)99; break;
            case "forbidden-postal": profile.PostalCodePolicy = GeographicPostalCodePolicy.Forbidden; break;
            case "missing-regex": profile.PostalCodeRegex = null; break;
            case "invalid-regex": profile.PostalCodeRegex = "["; break;
        }
        await db.SaveChangesAsync();
        Assert.Equal(expected, (await Assert.ThrowsAsync<ArgumentException>(() => Service(db).AdmitAsync(warehouse, actor, request))).Message);
        Assert.Empty(db.AddressAdmissionEvidence); Assert.Empty(db.GeographicAudit);
    }

    [Theory]
    [InlineData("inactive")][InlineData("deleted")][InlineData("foreign")][InlineData("unknown")]
    public async Task WarehouseIsolation_RejectsInactiveDeletedForeignOrUnknownOwner(string variant)
    {
        var id = warehouse;
        if (variant == "foreign")
        {
            var foreign = Guid.NewGuid(); id = Guid.NewGuid(); await using var foreignDb = Db(foreign);
            foreignDb.Tenants.Add(new Tenant { Id = foreign, Name = "Foreign synthetic", Slug = foreign.ToString("N"), IsActive = true });
            foreignDb.Warehouses.Add(new Warehouse { Id = id, TenantId = foreign, Name = "Foreign", Address = "10 Test Street", IsActive = true });
            await foreignDb.SaveChangesAsync();
        }
        await using var db = Db();
        if (variant is "inactive" or "deleted") { var w = await db.Warehouses.SingleAsync(); w.IsActive = variant != "inactive"; w.IsDeleted = variant == "deleted"; await db.SaveChangesAsync(); }
        if (variant == "unknown") id = Guid.NewGuid();
        Assert.IsType<NotFoundObjectResult>(await Controller(db).AdmitWarehouseAddress(id, Request(), Service(db), default));
        Assert.Empty(db.AddressAdmissionEvidence); Assert.Empty(db.GeographicAudit);
    }

    [Theory]
    [InlineData("cashier")][InlineData("warehouse-role")][InlineData("inactive")][InlineData("deleted")][InlineData("foreign")][InlineData("unknown")]
    public async Task ActorSqlAuthority_RejectsUnauthorizedInactiveDeletedForeignOrUnknownActor(string variant)
    {
        await using var db = Db(); var user = await db.Users.SingleAsync(); var id = actor;
        if (variant == "foreign")
        {
            var foreign = Guid.NewGuid(); id = Guid.NewGuid(); await using var foreignDb = Db(foreign);
            foreignDb.Tenants.Add(new Tenant { Id = foreign, Name = "Foreign synthetic", Slug = foreign.ToString("N"), IsActive = true });
            foreignDb.Users.Add(new User { Id = id, TenantId = foreign, Username = "foreign-operator", Email = "foreign@example.invalid", PasswordHash = "synthetic", Role = UserRole.Owner, IsActive = true });
            await foreignDb.SaveChangesAsync();
        }
        switch (variant)
        {
            case "cashier": user.Role = UserRole.Cashier; break;
            case "warehouse-role": user.Role = UserRole.Warehouse; break;
            case "inactive": user.IsActive = false; break;
            case "deleted": user.IsDeleted = true; break;
            case "unknown": id = Guid.Empty; break;
        }
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db).AdmitAsync(warehouse, id, Request()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db).GetPlacesAsync(warehouse, id, null, null));
        Assert.Empty(db.AddressAdmissionEvidence);
    }

    [Theory]
    [InlineData("street")][InlineData("missing-street")][InlineData("phone")][InlineData("reason")]
    public async Task OperatorMustConfirmRealWarehouseFacts_NoGuessOrConfigurationFallback(string variant)
    {
        await using var db = Db(); var request = Request();
        if (variant == "street") request.StreetLine1 = "unrelated config street";
        if (variant == "missing-street") { (await db.Warehouses.SingleAsync()).Address = null; await db.SaveChangesAsync(); }
        if (variant == "phone") { (await db.Warehouses.SingleAsync()).Phone = null; await db.SaveChangesAsync(); }
        if (variant == "reason") request.Reason = " ";
        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).AdmitAsync(warehouse, actor, request));
        Assert.Empty(db.AddressAdmissionEvidence); Assert.Empty(db.GeographicAudit);
    }

    [Fact]
    public async Task LookupProvidesPublishedCanonicalIdsNamesAndPostalPolicy_WithoutAdmittingAnything()
    {
        await using var db = Db(); var service = Service(db);
        var countries = Assert.IsType<WarehouseAddressPlacesResponse>(Assert.IsType<OkObjectResult>(
            await Controller(db).GetWarehouseAddressPlaces(warehouse, service)).Value);
        Assert.Equal(release, countries.CatalogReleaseId); var c = Assert.Single(countries.Places);
        Assert.Equal(country, c.PlaceId); Assert.Equal("US", c.CanonicalCode); Assert.Equal(GeographicPostalCodePolicy.Required, c.PostalCodePolicy);
        var stateOption = Assert.Single((await service.GetPlacesAsync(warehouse, actor, country, null)).Places);
        Assert.Equal(state, stateOption.PlaceId); Assert.Equal("TN", stateOption.CanonicalCode); Assert.Equal("Tennessee", stateOption.DisplayName);
        Assert.Equal(locality, Assert.Single((await service.GetPlacesAsync(warehouse, actor, country, state)).Places).PlaceId);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetPlacesAsync(warehouse, actor, country, Guid.NewGuid()));
        Assert.Empty(db.AddressAdmissionEvidence); Assert.Empty(db.GeographicAudit);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task EvidenceAndAuditRemainAppendOnly(bool delete)
    {
        await using var db = Db(); await Service(db).AdmitAsync(warehouse, actor, Request());
        var evidence = await db.AddressAdmissionEvidence.SingleAsync();
        if (delete) db.AddressAdmissionEvidence.Remove(evidence); else evidence.PostalCode = "99999";
        Assert.Contains("append-only", (await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync())).Message);
        db.Entry(evidence).State = EntityState.Unchanged;
        var audit = await db.GeographicAudit.SingleAsync();
        if (delete) db.GeographicAudit.Remove(audit); else audit.Reason = "rewritten";
        Assert.Contains("append-only", (await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync())).Message);
    }

    [Fact]
    public async Task CorrectingPostalFactsAppendsNewEvidence_AndCannotEnrichFrozenOrigin()
    {
        await using var db = Db(); var first = await Service(db).AdmitAsync(warehouse, actor, Request("38116"));
        var (shipment, order) = await AddShipment(db, "US"); var frozen = await RestoreOrigin(db, shipment, order);
        var second = await Service(db).AdmitAsync(warehouse, actor, Request("38117"));
        Assert.NotEqual(first.AddressAdmissionEvidenceId, second.AddressAdmissionEvidenceId);
        Assert.Equal(2, await db.AddressAdmissionEvidence.CountAsync());
        Assert.Equal("38116", (await db.AddressAdmissionEvidence.SingleAsync(e => e.Id == first.AddressAdmissionEvidenceId)).PostalCode);
        Assert.Equal(frozen, await RestoreOrigin(db, shipment, order));
        shipment.OriginStreet = "unadmitted replacement street";
        Assert.Equal("CANONICAL_SHIPMENT_ORIGIN_SUBSTITUTION", (await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync())).Message);
    }

    [Fact]
    public async Task ChangingWarehouseStreetAfterAdmission_CannotReuseEvidenceAsNewOrigin()
    {
        await using var db = Db(); await Service(db).AdmitAsync(warehouse, actor, Request());
        (await db.Warehouses.SingleAsync()).Address = "unadmitted replacement street"; await db.SaveChangesAsync();
        var (shipment, order) = await AddShipment(db, "US");
        Assert.Equal("SHIPMENT_ORIGIN_EVIDENCE_CHANGED", (await Assert.ThrowsAsync<ArgumentException>(() => RestoreOrigin(db, shipment, order))).Message);
        Assert.Null(shipment.OriginSnapshotJson);
    }

    [Fact]
    public async Task NoPublishedCatalogFailsClosedAndDoesNotWriteEvidence()
    {
        await using var db = Db(); var catalog = new Mock<IGeographicCatalogService>();
        var repository = new GeographicRepository(db);
        var service = new WarehouseAddressAdmissionService(db, new TestTenantContext(tenant), new GeographicResolver(catalog.Object, repository), catalog.Object);
        Assert.Equal("PublishedCatalogReleaseRequired", (await Assert.ThrowsAsync<ArgumentException>(() => service.AdmitAsync(warehouse, actor, Request()))).Message);
        Assert.Empty(db.AddressAdmissionEvidence);
    }

    [Theory]
    [InlineData(UserRole.Owner, true)][InlineData(UserRole.Admin, true)][InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Warehouse, false)][InlineData(UserRole.Cashier, false)]
    public async Task BothApiActionsUseCanonicalWarehouseManagementPermission(UserRole role, bool expected)
    {
        foreach (var (action, verb) in new[] { ("GetWarehouseAddressPlaces", "GET"), ("AdmitWarehouseAddress", "POST") })
        {
            Assert.True(PosEndpointPermissionCatalog.TryResolve("Warehouses", action, [verb], out var permission));
            Assert.Equal(PosPermissions.InventoryApprove, permission);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.Role, role.ToString()), new Claim(ClaimTypes.NameIdentifier, actor.ToString()), new Claim("tenant_id", tenant.ToString()),
                new Claim(PosSecurityConstants.SessionBindingClaim, "test-session"), new Claim(PosSecurityConstants.SqlAuthorityClaim, PosSecurityConstants.SqlAuthorityValue)], "test"));
            var context = new AuthorizationHandlerContext([new PosPermissionRequirement(permission)], principal, null);
            await new PosPermissionAuthorizationHandler().HandleAsync(context); Assert.Equal(expected, context.HasSucceeded);
        }
        await using var db = Db(); Assert.IsType<ForbidResult>(await Controller(db, false).AdmitWarehouseAddress(warehouse, Request(), Service(db), default));
    }

    [Theory]
    [InlineData("valid", null)]
    [InlineData("country", "CanonicalCountryEvidenceConflictingOrMissing")]
    [InlineData("missing-country", "CanonicalCountryEvidenceConflictingOrMissing")]
    [InlineData("postal", "PostalCodeInvalid")]
    [InlineData("missing-postal", "PostalCodeRequired")]
    [InlineData("role", "CanonicalAddressRoleInvalid")]
    [InlineData("fingerprint", "AddressFingerprintRequired")]
    [InlineData("owner", "WarehouseOwnershipRequired")]
    [InlineData("publication", "CanonicalPublishedLineageRequired")]
    [InlineData("membership", "CanonicalPublishedMembershipRequired")]
    [InlineData("lineage", "WarehouseHierarchyInvalid")]
    public async Task CheckoutCanonicalAdmission_CapturesCompleteInputsOrFailsBeforeWrite(string variant, string? failure)
    {
        await using var db = Db();
        if (variant == "membership") { db.GeographicCatalogReleasePlaces.Remove(await db.GeographicCatalogReleasePlaces.FirstAsync()); await db.SaveChangesAsync(); }
        if (variant == "lineage") { db.GeographicPlaceHierarchyEdges.Remove(await db.GeographicPlaceHierarchyEdges.FirstAsync()); await db.SaveChangesAsync(); }
        var repository = new GeographicRepository(db);
        var resolver = new GeographicResolver(new GeographicCatalogService(repository), repository);
        var inputs = new CanonicalAddressInputs(variant == "role" ? "warehouse-origin" : "checkout-shipping", "session:isolated-owner", "owned-cart:checkout-authority",
            variant == "country" ? "CA" : variant == "missing-country" ? null : "US", "10 Test Street", null, "Memphis", "TN", null, null, null, null,
            "Test Recipient", "15555550100", "recipient@example.invalid");
        var candidate = new GeographicAddressCandidate(tenant, AddressAdmissionOwnerDomain.Checkout, variant == "owner" ? Guid.Empty : Guid.NewGuid(),
            new(country, state, locality, null, null), variant == "fingerprint" ? new string('Z', 64) : new string('A', 64),
            variant == "postal" ? "invalid" : variant == "missing-postal" ? null : "03811", null, null, null, inputs);
        if (variant != "fingerprint") candidate = candidate with { AddressFingerprintSha256 = CanonicalAddressEvidence.ComputeInputFingerprint(
            tenant, candidate.OwnerDomain, candidate.OwnerId!.Value, candidate.Lineage, inputs, candidate.PostalCode) };
        // A new isolated published release without activated publication tests the real capture gate.
        if (variant == "publication")
        {
            var mock = new Mock<IGeographicRepository>();
            mock.SetupGet(r => r.CountryProfiles).Returns(repository.CountryProfiles);
            mock.SetupGet(r => r.Places).Returns(repository.Places);
            mock.SetupGet(r => r.CatalogReleases).Returns(repository.CatalogReleases);
            mock.SetupGet(r => r.HierarchyEdges).Returns(repository.HierarchyEdges);
            mock.SetupGet(r => r.CatalogPublications).Returns(db.GeographicCatalogPublications.Where(p => p.Id == Guid.Empty));
            resolver = new GeographicResolver(new GeographicCatalogService(mock.Object), mock.Object);
        }
        var result = await resolver.AdmitAsync(candidate);
        Assert.Equal(failure == null, result.Succeeded);
        Assert.Equal(failure, result.FailureCode);
        if (failure != null) { Assert.Empty(db.AddressAdmissionEvidence); return; }
        var evidence = await db.AddressAdmissionEvidence.SingleAsync();
        var p = CanonicalAddressEvidence.Restore(evidence.CanonicalAddressEvidenceJson!, evidence);
        Assert.Equal("03811", p.NormalizedPostalCode); Assert.Equal(inputs, p.Inputs);
        var retained = evidence.CanonicalAddressEvidenceJson;
        foreach (var substitute in new[] { "tenant", "owner", "postal", "hash", "payload" })
        {
            var copy = new AddressAdmissionEvidence { Id = evidence.Id, TenantId = evidence.TenantId, OwnerId = evidence.OwnerId, OwnerDomain = evidence.OwnerDomain,
                CatalogReleaseId = evidence.CatalogReleaseId, CountryPlaceId = evidence.CountryPlaceId, AdministrativeAreaPlaceId = evidence.AdministrativeAreaPlaceId,
                LocalityPlaceId = evidence.LocalityPlaceId, PostalCode = evidence.PostalCode, AddressFingerprintSha256 = evidence.AddressFingerprintSha256,
                AdmittedAtUtc = evidence.AdmittedAtUtc, Status = evidence.Status };
            if (substitute == "tenant") copy.TenantId = Guid.NewGuid();
            if (substitute == "owner") copy.OwnerId = Guid.NewGuid();
            if (substitute == "postal") copy.PostalCode = "99999";
            if (substitute == "hash") copy.AddressFingerprintSha256 = new string('B', 64);
            Assert.Throws<ArgumentException>(() => CanonicalAddressEvidence.Restore(substitute == "payload" ? retained!.Replace("10 Test Street", "11 Test Street") : retained!, copy));
        }
    }

    [Theory]
    [InlineData("attempt")][InlineData("last-attempt")][InlineData("intent")][InlineData("tracking")]
    public async Task UncertainRegistration_CannotReconstructFreshOriginFromCurrentWarehouse(string variant)
    {
        await using var db = Db(); await Service(db).AdmitAsync(warehouse, actor, Request());
        var (shipment, order) = await AddShipment(db, "US");
        if (variant == "attempt") shipment.ExternalRegistrationAttemptCount = 1;
        if (variant == "last-attempt") shipment.ExternalRegistrationLastAttemptAtUtc = DateTime.UtcNow;
        if (variant == "intent") shipment.CarrierRequestSnapshotJson = "{}";
        if (variant == "tracking") shipment.TrackingNumber = "isolated-original-key";
        Assert.Null(await RestoreOrigin(db, shipment, order));
        Assert.Null(shipment.OriginSnapshotJson);
    }

    internal async Task<(Order Order, Shipment Shipment, AddressAdmissionEvidence Origin, AddressAdmissionEvidence Destination,
        AddressAdmissionEvidence Billing)> CreateCanonicalPairAsync(string carrier = "dhl", string paymentCode = "COD", string service = "standard", bool captureOrigin = true)
    {
        await using var db = Db();
        await Service(db).AdmitAsync(warehouse, actor, Request("03811"));
        var repository = new GeographicRepository(db);
        var resolver = new GeographicResolver(new GeographicCatalogService(repository), repository);
        var cart = Guid.NewGuid();
        var inputs = new CanonicalAddressInputs("checkout-shipping", "session:isolated-owner", "owned-cart:checkout-authority",
            "US", "20 Test Street", "Suite 2", "Memphis", "TN", "B", "2", "4", "Test landmark", "Test Recipient", "15555550100", "recipient@example.invalid");
        var lineage = new GeographicPlaceLineage(country, state, locality, null, null);
        async Task<AddressAdmissionEvidence> Admit(CanonicalAddressInputs address)
        {
            var result = await resolver.AdmitAsync(new(tenant, AddressAdmissionOwnerDomain.Checkout, cart, lineage,
                CanonicalAddressEvidence.ComputeInputFingerprint(tenant, AddressAdmissionOwnerDomain.Checkout, cart, lineage, address, "03811"),
                "03811", null, null, null, address));
            Assert.True(result.Succeeded, result.FailureCode);
            return await db.AddressAdmissionEvidence.SingleAsync(e => e.Id == result.AddressAdmissionEvidenceId);
        }
        var destination = await Admit(inputs);
        var billing = await Admit(inputs with { AddressRole = "checkout-billing" });
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, CartId = cart, WarehouseId = warehouse,
            OrderNumber = "isolated-canonical-order", Currency = "USD", Status = OrderStatus.Confirmed,
            PaymentMethodCode = paymentCode, PaymentStatus = paymentCode == "COD" ? PaymentStatus.Pending : PaymentStatus.Paid, FinancialPostingStatus = FinancialPostingStatus.NotRequired,
            FulfillmentMode = carrier == "manual" ? FulfillmentMode.Manual : FulfillmentMode.Carrier, FulfillmentCarrierCode = carrier == "manual" ? null : carrier,
            ShippingMethodId = Guid.NewGuid(), ShippingMethodName = "isolated fixture method", ShippingServiceLevel = service, ShippingCost = 25, Total = 125,
            GuestName = inputs.ContactName, GuestPhone = inputs.ContactPhone, GuestEmail = inputs.ContactEmail,
            ShippingAddressAdmissionEvidenceId = destination.Id, ShippingGeographicCatalogReleaseId = release,
            ShippingCountryPlaceId = country, ShippingAdministrativeAreaPlaceId = state, ShippingLocalityPlaceId = locality,
            ShippingCountry = "US", ShippingCity = inputs.City, ShippingState = inputs.State, ShippingZipCode = "03811",
            ShippingCountryDisplayName = "United States", ShippingAdministrativeAreaDisplayName = inputs.State, ShippingLocalityDisplayName = inputs.City,
            ShippingStreet = inputs.Street, ShippingStreet2 = inputs.Street2, ShippingBuilding = inputs.Building,
            ShippingFloor = inputs.Floor, ShippingApartment = inputs.Apartment, ShippingLandmark = inputs.Landmark,
            ShippingGeographicSnapshotVersion = 1, ShippingGeographicSnapshotCreatedAtUtc = DateTime.UtcNow,
            BillingAddressAdmissionEvidenceId = billing.Id, BillingGeographicCatalogReleaseId = release,
            BillingCountryPlaceId = country, BillingAdministrativeAreaPlaceId = state, BillingLocalityPlaceId = locality,
            BillingCountry = "US", BillingCity = inputs.City, BillingState = inputs.State, BillingZipCode = "03811",
            BillingStreet = inputs.Street, BillingStreet2 = inputs.Street2, BillingBuilding = inputs.Building,
            BillingFloor = inputs.Floor, BillingApartment = inputs.Apartment, BillingLandmark = inputs.Landmark,
            BillingGeographicSnapshotVersion = 1, BillingGeographicSnapshotCreatedAtUtc = DateTime.UtcNow };
        db.Orders.Add(order); await db.SaveChangesAsync();
        var readiness = new Mock<ICarrierIntegrationAuthority>();
        readiness.Setup(a => a.GetServiceLevelReadinessAsync(tenant, carrier, service, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierServiceLevelReadiness(carrier, service, true, true, true, true, []));
        readiness.Setup(a => a.GetIntegrationReadinessAsync(tenant, carrier, order.ShippingMethodId, warehouse,
            CarrierIntegrationPurpose.ShipmentCreation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierIntegrationReadiness(carrier, "isolated fixture", false, true, true, "Sandbox", CarrierProductionCapability.Partial, [], false, true, "Available"));
        var settings = Microsoft.Extensions.Options.Options.Create(new PointOfSale.Infrastructure.Redis.Configuration.RedisSettings { Enabled = false });
        using var redis = new PointOfSale.Infrastructure.Redis.Services.RedisConnectionFactory(settings,
            NullLogger<PointOfSale.Infrastructure.Redis.Services.RedisConnectionFactory>.Instance, connectionOverride: null);
        var events = new Mock<IEventPublisher>(); events.SetReturnsDefault(Task.CompletedTask);
        ICarrierIntegrationAuthority executionAuthority = readiness.Object;
        if (carrier == "manual")
        {
            var registry = new ShippingCarrierRegistry([new PointOfSale.Infrastructure.Shipping.Adapters.ManualCarrierDefinition()], NullLogger<ShippingCarrierRegistry>.Instance);
            executionAuthority = new CarrierIntegrationAuthority(db, registry, new CarrierCapabilityRegistry(registry),
                Mock.Of<ICarrierCredentialEncryptionService>(MockBehavior.Strict), NullLogger<CarrierIntegrationAuthority>.Instance);
        }
        var shipping = new PointOfSale.API.Services.Shipping.ShipmentService(db, redis, settings, events.Object,
            Mock.Of<IOrderFulfillmentService>(), Mock.Of<PointOfSale.Core.Interfaces.Marketplace.IMarketplaceEventOutboxService>(),
            NullLogger<PointOfSale.API.Services.Shipping.ShipmentService>.Instance, executionAuthority);
        await shipping.CreateShipmentAsync(tenant, order.Id, order.ShippingMethodId.Value, "isolated-canonical-creation");
        var shipment = await db.Shipments.SingleAsync();
        Assert.Equal(inputs.ContactEmail, shipment.RecipientEmail);
        // RequestedAt is local scheduling, not proof of an outbound attempt.
        Assert.Equal(0, shipment.ExternalRegistrationAttemptCount);
        if (captureOrigin) Assert.NotNull(await RestoreOrigin(db, shipment, order));
        var origin = await db.AddressAdmissionEvidence.SingleAsync(e => e.OwnerDomain == AddressAdmissionOwnerDomain.Warehouse);
        return (order, shipment, origin, destination, billing);
    }

    [Fact]
    public async Task ManualCanonicalCreation_UsesProductionReadinessWithoutExternalAccountOrCollectedMoney()
    {
        var pair = await CreateCanonicalPairAsync("manual");
        Assert.Equal(ShipmentStatus.Pending, pair.Shipment.Status);
        Assert.Equal("manual", pair.Shipment.CarrierCode); Assert.Null(pair.Order.FulfillmentCarrierCode);
        Assert.Equal(FulfillmentMode.Manual, pair.Order.FulfillmentMode);
        Assert.Equal(125, pair.Shipment.CodAmount); Assert.False(pair.Shipment.CodCollected);
        Assert.Null(pair.Shipment.ExternalRegistrationRequestedAtUtc); Assert.Equal(0, pair.Shipment.ExternalRegistrationAttemptCount);
        await using var db = Db();
        Assert.Empty(db.CarrierAccounts); Assert.Empty(db.CarrierConfigurations); Assert.Empty(db.CarrierConnectionTestRecords);
        Assert.Equal(pair.Destination.Id, pair.Shipment.DestinationAddressAdmissionEvidenceId);
    }

    [Fact]
    public async Task CanonicalPair_CreatesThroughProductionShipmentService_AndRestoresWithoutLiveCatalogOrWarehouse()
    {
        var pair = await CreateCanonicalPairAsync();
        var original = CanonicalShipmentAddresses.Restore(pair.Shipment, pair.Order, pair.Origin, pair.Destination);
        await using var db = Db();
        (await db.Warehouses.SingleAsync()).Address = "current warehouse changed";
        (await db.GeographicCountryProfiles.SingleAsync()).IsActive = false;
        await db.SaveChangesAsync();
        var restored = CanonicalShipmentAddresses.Restore(pair.Shipment, pair.Order, pair.Origin, pair.Destination);
        Assert.Equal(original.Fingerprint, restored.Fingerprint);
        Assert.Equal("03811", restored.Origin.NormalizedPostalCode);
        Assert.Equal("03811", restored.Destination.NormalizedPostalCode);
        Assert.Null(pair.Shipment.CarrierRequestSnapshotJson); Assert.Null(pair.Shipment.ExternalRegistrationLastAttemptAtUtc);
    }

    [Theory]
    [InlineData("tenant")][InlineData("order")][InlineData("warehouse")][InlineData("street")][InlineData("building")]
    [InlineData("email")][InlineData("postal")][InlineData("country")][InlineData("origin-mirror")][InlineData("origin-json")][InlineData("service")]
    public async Task CanonicalPair_RejectsAddressRoleIdentityAndProjectionSubstitution(string variant)
    {
        var pair = await CreateCanonicalPairAsync(); var s = pair.Shipment;
        switch (variant)
        {
            case "tenant": s.TenantId = Guid.NewGuid(); break;
            case "order": s.OrderId = Guid.NewGuid(); break;
            case "warehouse": pair.Order.WarehouseId = Guid.NewGuid(); break;
            case "street": s.DestinationStreet = "replacement"; break;
            case "building": s.DestinationBuilding = "replacement"; break;
            case "email": s.RecipientEmail = "other@example.invalid"; break;
            case "postal": s.ShippingZipCode = "99999"; break;
            case "country": s.ShippingCountry = "ZZ"; break;
            case "origin-mirror": s.OriginStreet = "replacement"; break;
            case "origin-json": s.OriginSnapshotJson = s.OriginSnapshotJson!.Replace("10 Test Street", "11 Test Street"); break;
            case "service": s.ServiceLevel = "replacement"; break;
        }
        Assert.Throws<ArgumentException>(() => CanonicalShipmentAddresses.Restore(s, pair.Order, pair.Origin, pair.Destination));
    }

    [Theory]
    [InlineData("postal")][InlineData("email")][InlineData("delete")][InlineData("soft-delete")]
    public async Task CanonicalDestination_RemainsImmutableBeforeFirstOutboundAttempt(string variant)
    {
        var pair = await CreateCanonicalPairAsync();
        await using var db = Db(); var shipment = await db.Shipments.SingleAsync();
        if (variant == "postal") shipment.ShippingZipCode = "99999";
        if (variant == "email") shipment.RecipientEmail = "other@example.invalid";
        if (variant == "delete") db.Shipments.Remove(shipment);
        if (variant == "soft-delete") shipment.IsDeleted = true;
        var error = await Record.ExceptionAsync(() => db.SaveChangesAsync());
        Assert.True(error is ArgumentException or InvalidOperationException, error?.ToString());
    }

    private async Task<(Shipment, Order)> AddShipment(ApplicationDbContext db, string iso)
    {
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, WarehouseId = warehouse, OrderNumber = "Synthetic admission order" };
        var shipment = new Shipment { Id = Guid.NewGuid(), TenantId = tenant, OrderId = order.Id, CarrierCode = "fedex", ShipmentNumber = "Synthetic origin",
            ShippingCountry = iso, Status = ShipmentStatus.PendingExternalRegistration };
        db.Orders.Add(order); db.Shipments.Add(shipment); await db.SaveChangesAsync(); return (shipment, order);
    }
    private async Task<ShipmentOriginDto?> RestoreOrigin(ApplicationDbContext db, Shipment shipment, Order order)
    {
        using var telemetry = new CommerceReliabilityTelemetry();
        var service = new CarrierShipmentService(db, Mock.Of<IShippingCarrierRegistry>(MockBehavior.Strict), Mock.Of<ICarrierCredentialEncryptionService>(),
            Mock.Of<IEventPublisher>(), Mock.Of<IOrderFulfillmentService>(), telemetry, new TestTenantContext(tenant),
            Mock.Of<ICustomsEvidenceAdmissionService>(MockBehavior.Strict), Mock.Of<ICarrierGeographicMappingService>(MockBehavior.Strict), NullLogger<CarrierShipmentService>.Instance);
        var method = typeof(CarrierShipmentService).GetMethod("ResolveOrRestoreShipmentOriginAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return await (Task<ShipmentOriginDto?>)method.Invoke(service, [shipment, order, CancellationToken.None])!;
    }
}
