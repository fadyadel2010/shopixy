using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.GeographicAdministration;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Controllers;

public sealed record BostaSyncRequest(string CityId);
public sealed record BostaBootstrapRequest(string CityId, string Reason);
public sealed record BostaMappingChoice(Guid CandidateId, Guid CanonicalPlaceId);
public sealed record BostaMappingDraftRequest(Guid SyncRunId, Guid CatalogReleaseId, string ExpectedSourceHashSha256,
    IReadOnlyList<BostaMappingChoice> Choices, string Reason);

[ApiController]
[Route("api/admin/shipping/carrier-accounts/{carrierAccountId:guid}/bosta/geography")]
[Authorize(Roles = "Owner,Admin,Manager")]
[Authorize(Policy = PosPermissions.ShippingManage)]
public sealed class AdminBostaGeographyController(ApplicationDbContext db, ITenantContext tenantContext,
    ICarrierIntegrationAuthority authority, IShippingCarrierRegistry registry, IGeographicAdministrationService geography) : ControllerBase
{
    private Guid Tenant => tenantContext.RequiredTenantId;
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : Guid.Empty;
    private string Role => User.IsInRole("Owner") ? "Owner" : User.IsInRole("Admin") ? "Admin" : "Manager";

    private Task<CarrierAccount?> Account(Guid id, CancellationToken ct) => db.CarrierAccounts.IgnoreQueryFilters().AsNoTracking()
        .SingleOrDefaultAsync(a => a.Id == id && a.TenantId == Tenant && !a.IsDeleted && a.CarrierCode == "bosta", ct);

    private async Task<BostaGeographyResult> Discover(CarrierAccount account, string? cityId, CancellationToken ct)
    {
        var saved = await authority.ResolveConnectionCredentialsAsync(Tenant, "bosta", ct);
        if (!saved.Succeeded || saved.Credentials == null || saved.Configuration?.CarrierCode != "bosta" ||
            account.Environment?.ToString() != (saved.Credentials.UseSandbox ? "Sandbox" : "Production"))
            return new(false, [], "BOSTA_SAVED_CONFIGURATION_REQUIRED", DateTime.UtcNow);
        if (registry.GetAdapter("bosta") is not IBostaGeographyDiscovery discovery)
            return new(false, [], "BOSTA_GEOGRAPHY_DISCOVERY_UNAVAILABLE", DateTime.UtcNow);
        var result = await discovery.GetGeographyAsync(saved.Credentials, cityId, ct);
        var current = await Account(account.Id, ct);
        var currentSaved = await authority.ResolveConnectionCredentialsAsync(Tenant, "bosta", ct);
        return current?.ActiveCredentialReferenceId == account.ActiveCredentialReferenceId && current?.Environment == account.Environment &&
            BostaPickupVerificationCache.SameConfiguration(saved, currentSaved)
            ? result : new(false, [], "BOSTA_SAVED_CONFIGURATION_CHANGED", DateTime.UtcNow);
    }

    [HttpGet]
    public async Task<IActionResult> Read(Guid carrierAccountId, [FromQuery] string? cityId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var account = await Account(carrierAccountId, ct);
        if (account == null) return NotFound(new {code = "UNKNOWN_CARRIER_ACCOUNT"});
        var result = await Discover(account, cityId, ct);
        if (!result.Succeeded) return StatusCode(502, new {code = result.ErrorCode});
        var now = DateTime.UtcNow;
        var catalog = await db.GeographicCatalogReleases.AsNoTracking().Where(r => r.Status == GeographicReleaseStatus.Published && r.CountryScopeIso2 == "EG" &&
            db.GeographicCatalogPublications.Any(p => p.CatalogReleaseId == r.Id && p.ActivatedAtUtc <= now && p.SupersededAtUtc == null))
            .OrderByDescending(r => r.PublishedAtUtc).ThenByDescending(r => r.ReleaseNumber).FirstOrDefaultAsync(ct);
        var places = catalog == null ? [] : await db.GeographicPlaces.AsNoTracking().Where(p => p.IntroducedCatalogReleaseId == catalog.Id && p.Status == GeographicLifecycleStatus.Active && p.RetiredCatalogReleaseId == null)
            .Select(p => new {p.Id, p.CanonicalCode, p.PlaceKind, p.CountryPlaceId}).ToArrayAsync(ct);
        return Ok(new {accountId = account.Id, environment = account.Environment.ToString(), result.ObservedAtUtc, result.Places, catalogReleaseId = catalog?.Id, canonicalPlaces = places});
    }

    [HttpPost("sync")]
    public Task<IActionResult> Synchronize(Guid carrierAccountId, BostaSyncRequest request, CancellationToken ct) =>
        SynchronizeCore(carrierAccountId, request.CityId, false, "Explicit Admin reference synchronization", ct);

    [HttpPost("bootstrap")]
    public Task<IActionResult> Bootstrap(Guid carrierAccountId, BostaBootstrapRequest request, CancellationToken ct) =>
        SynchronizeCore(carrierAccountId, request.CityId, true, request.Reason, ct);

    private async Task<IActionResult> SynchronizeCore(Guid carrierAccountId, string cityId, bool registerProvider, string reason, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (Actor == Guid.Empty) return Forbid();
        var account = await Account(carrierAccountId, ct);
        if (account == null) return NotFound(new {code = "UNKNOWN_CARRIER_ACCOUNT"});
        var result = await Discover(account, cityId, ct);
        if (!result.Succeeded) return StatusCode(502, new {code = result.ErrorCode});
        if (result.Places.Any(place => JsonSerializer.Serialize(place).Length > 512))
            return StatusCode(502, new {code = "BOSTA_GEOGRAPHY_EVIDENCE_TOO_LARGE"});
        var staged = await geography.SynchronizeBostaReferenceAsync(Tenant, account.Id, account.ActiveCredentialReferenceId,
            result, account.Environment == CarrierAccountEnvironment.Sandbox ? CarrierGeographicEnvironment.Sandbox : CarrierGeographicEnvironment.Production,
            registerProvider, reason, Actor, Role, ct);
        return staged.Succeeded ? await ReadSync(carrierAccountId, staged.ResourceId!.Value, ct) : Conflict(new {code = staged.FailureCode});
    }

    [HttpGet("sync/{runId:guid}")]
    public async Task<IActionResult> ReadSync(Guid carrierAccountId, Guid runId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var account = await Account(carrierAccountId, ct);
        if (account == null) return NotFound();
        var binding = $"bosta-v1:{account.Id:N}:{account.ActiveCredentialReferenceId:N}";
        var run = await db.CarrierGeographicSyncRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId && r.TenantId == Tenant &&
            r.AccountScopeId == Tenant && r.AccountScopeKind == CarrierGeographicAccountScopeKind.TenantProviderAccount && r.SourceVersion == binding &&
            r.Environment == (account.Environment == CarrierAccountEnvironment.Sandbox ? CarrierGeographicEnvironment.Sandbox : CarrierGeographicEnvironment.Production), ct);
        if (run == null) return NotFound();
        return Ok(new { run.Id, sourceHashSha256 = run.SourceHashSha256, run.SourceUri, run.SourceVersion, run.CompletedAtUtc,
            candidates = await db.CarrierGeographicSyncCandidates.AsNoTracking().Where(c => c.SyncRunId == run.Id)
                .Select(c => new {c.Id, c.ProviderPlaceType, c.ProviderPlaceId, c.ProviderPlaceCode, c.ProviderDisplayName, c.ProviderParentPlaceId,
                    c.CandidateCanonicalPlaceId, c.CandidateStatus, serviceable = c.CandidateStatus != CarrierGeographicSyncCandidateStatus.Rejected}).ToArrayAsync(ct) });
    }

    [HttpPost("drafts")]
    public async Task<IActionResult> AcceptMappings(Guid carrierAccountId, BostaMappingDraftRequest request, CancellationToken ct)
    {
        if (Actor == Guid.Empty) return Forbid();
        var account = await Account(carrierAccountId, ct);
        if (account == null) return NotFound(new {code = "UNKNOWN_CARRIER_ACCOUNT"});
        var result = await geography.AcceptBostaMappingAsync(Tenant, account.Id, account.ActiveCredentialReferenceId, request, Actor, Role, ct);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpGet("releases/{releaseId:guid}")]
    public async Task<IActionResult> ReadRelease(Guid carrierAccountId, Guid releaseId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var account = await Account(carrierAccountId, ct);
        if (account == null) return NotFound();
        var release = await db.CarrierGeographicMappingReleases.AsNoTracking().SingleOrDefaultAsync(r => r.Id == releaseId && r.TenantId == Tenant &&
            r.AccountScopeKind == CarrierGeographicAccountScopeKind.TenantProviderAccount && r.AccountScopeId == Tenant &&
            db.CarrierProviders.Any(p => p.Id == r.CarrierProviderId && p.CarrierCode == "bosta") &&
            r.Environment == (account.Environment == CarrierAccountEnvironment.Sandbox ? CarrierGeographicEnvironment.Sandbox : CarrierGeographicEnvironment.Production), ct);
        if (release == null) return NotFound();
        return Ok(new {release.Id, release.Status, release.ChecksumSha256, release.CatalogReleaseId, release.FreshnessExpiresAtUtc,
            mappings = await db.CarrierGeographicMappings.AsNoTracking().Where(m => m.MappingReleaseId == release.Id)
                .Select(m => new {m.CanonicalPlaceId, m.ProviderPlaceId, m.ProviderPlaceType, m.ProviderDisplayName}).ToArrayAsync(ct)});
    }
}
