using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.GeographicAdministration;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Entities.Geographic;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/geographic")]
[Authorize(Roles = "Owner,Admin,Manager")]
public sealed class AdminGeographicAdministrationController : ControllerBase
{
    private readonly IGeographicAdministrationService _service;

    public AdminGeographicAdministrationController(IGeographicAdministrationService service)
    {
        _service = service;
    }

    [HttpPost("catalog/drafts")]
    public Task<IActionResult> CreateCatalogDraft(CreateCatalogDraftRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.CreateCatalogDraftAsync(request, actorId, role, cancellationToken));

    [HttpPut("catalog/drafts/{catalogReleaseId:guid}")]
    public Task<IActionResult> EditCatalogDraft(Guid catalogReleaseId, EditCatalogDraftRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.EditCatalogDraftAsync(catalogReleaseId, request, actorId, role, cancellationToken));

    [HttpPost("catalog/places")]
    public Task<IActionResult> AddCatalogPlace(CatalogPlaceRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.AddCatalogPlaceAsync(request, actorId, role, cancellationToken));

    [HttpPost("catalog/localizations")]
    public Task<IActionResult> AddLocalization(LocalizationRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.AddLocalizationAsync(request, actorId, role, cancellationToken));

    [HttpPost("catalog/aliases")]
    public Task<IActionResult> AddAlias(AliasRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.AddAliasAsync(request, actorId, role, cancellationToken));

    [HttpPost("catalog/country-profiles")]
    public Task<IActionResult> AddCountryProfile(CountryProfileRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.AddCountryProfileAsync(request, actorId, role, cancellationToken));

    [HttpPost("catalog/merges")]
    public Task<IActionResult> AddMerge(PlaceMergeRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.AddMergeAsync(request, actorId, role, cancellationToken));

    [HttpPost("catalog/deprecations")]
    public Task<IActionResult> AddDeprecation(PlaceDeprecationRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.AddDeprecationAsync(request, actorId, role, cancellationToken));

    [HttpPost("catalog/releases/{catalogReleaseId:guid}/publish")]
    public Task<IActionResult> PublishCatalogRelease(Guid catalogReleaseId, ReleaseActionRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.PublishCatalogReleaseAsync(catalogReleaseId, request, actorId, role, cancellationToken));

    [HttpPost("catalog/releases/{targetCatalogReleaseId:guid}/rollback")]
    public Task<IActionResult> RollbackCatalogRelease(Guid targetCatalogReleaseId, ReleaseActionRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.RollbackCatalogReleaseAsync(targetCatalogReleaseId, request, actorId, role, cancellationToken));

    [HttpPost("carrier-mappings/drafts")]
    public Task<IActionResult> CreateMappingDraft(CreateMappingDraftRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.CreateMappingDraftAsync(request, actorId, role, cancellationToken));

    [HttpPost("carrier-mappings")]
    public Task<IActionResult> AddMapping(MappingRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.AddMappingAsync(request, actorId, role, cancellationToken));

    [HttpPost("carrier-mappings/releases/{mappingReleaseId:guid}/publish")]
    public Task<IActionResult> PublishMappingRelease(Guid mappingReleaseId, ReleaseActionRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.PublishMappingReleaseAsync(mappingReleaseId, request, actorId, role, cancellationToken));

    [HttpPost("carrier-mappings/releases/{targetMappingReleaseId:guid}/rollback")]
    public Task<IActionResult> RollbackMappingRelease(Guid targetMappingReleaseId, ReleaseActionRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.RollbackMappingReleaseAsync(targetMappingReleaseId, request, actorId, role, cancellationToken));

    [HttpPost("sync/runs")]
    public Task<IActionResult> RecordSyncRun(SyncRunRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.RecordSyncRunAsync(request, actorId, role, cancellationToken));

    [HttpPost("sync/candidates")]
    public Task<IActionResult> AddSyncCandidate(SyncCandidateRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.AddSyncCandidateAsync(request, actorId, role, cancellationToken));

    [HttpPost("sync/candidates/{syncCandidateId:guid}/approve")]
    public Task<IActionResult> ApproveSyncCandidate(Guid syncCandidateId, CandidateReviewRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.ReviewSyncCandidateAsync(syncCandidateId, CarrierGeographicSyncCandidateStatus.Approved, request, actorId, role, cancellationToken));

    [HttpPost("sync/candidates/{syncCandidateId:guid}/reject")]
    public Task<IActionResult> RejectSyncCandidate(Guid syncCandidateId, CandidateReviewRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.ReviewSyncCandidateAsync(syncCandidateId, CarrierGeographicSyncCandidateStatus.Rejected, request, actorId, role, cancellationToken));

    [HttpPost("overrides")]
    public Task<IActionResult> CreateOverride(OverrideRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.CreateOverrideAsync(request, actorId, role, cancellationToken));

    [HttpPost("overrides/{overrideId:guid}/approve")]
    public Task<IActionResult> ApproveOverride(Guid overrideId, ReleaseActionRequest request, CancellationToken cancellationToken) => Execute((actorId, role) => _service.ApproveOverrideAsync(overrideId, request, actorId, role, cancellationToken));

    [HttpGet("audit")]
    public async Task<IActionResult> GetAudit([FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.GetAuditAsync(limit, cancellationToken));
    }

    private async Task<IActionResult> Execute(Func<Guid, string, Task<GeographicOperationResultDto>> action)
    {
        var actorId = ResolveActorId();
        if (actorId == Guid.Empty)
        {
            return Forbid();
        }

        var result = await action(actorId, ResolveActorRole());
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    private Guid ResolveActorId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("userId");
        return Guid.TryParse(value, out var actorId) ? actorId : Guid.Empty;
    }

    private string ResolveActorRole()
    {
        if (User.IsInRole("Owner")) return "Owner";
        if (User.IsInRole("Admin")) return "Admin";
        if (User.IsInRole("Manager")) return "Manager";
        return "POSAdmin";
    }
}
