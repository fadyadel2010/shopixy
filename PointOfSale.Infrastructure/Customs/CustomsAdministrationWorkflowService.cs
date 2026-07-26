using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities.Customs;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Customs;

public sealed class CustomsAdministrationWorkflowService : ICustomsAdministrationWorkflowService
{
    private static readonly HashSet<string> SupportedWeightUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "kg",
        "g",
        "lb",
        "oz"
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CustomsAdministrationWorkflowService> _logger;

    public CustomsAdministrationWorkflowService(
        ApplicationDbContext dbContext,
        ILogger<CustomsAdministrationWorkflowService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CustomsWorkflowResult> CreateDraftAsync(
        Guid tenantId,
        CustomsWorkflowEvidencePayload payload,
        CustomsAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!HasRole(actor, CustomsAdministrationRole.Editor))
        {
            return await RejectAsync(tenantId, null, null, CustomsAdministrationAuditAction.DraftCreated, actor, "UNAUTHORIZED_EDITOR", correlationId, cancellationToken);
        }

        var failure = await ValidatePayloadAsync(tenantId, payload, cancellationToken);
        if (failure is not null)
        {
            return await RejectAsync(tenantId, null, null, CustomsAdministrationAuditAction.DraftCreated, actor, failure, correlationId, cancellationToken);
        }

        var normalized = Normalize(payload);
        var checksum = ComputeSha256(JsonSerializer.Serialize(normalized));
        var now = DateTime.UtcNow;
        var versionId = Guid.NewGuid();
        var evidence = new CustomsEvidence
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceKind = payload.SourceKind,
            SourceId = payload.SourceId,
            ProductId = payload.ProductId,
            ProductVariantId = payload.ProductVariantId,
            Status = CustomsEvidenceStatus.Draft,
            CurrentVersionId = versionId,
            CurrentVersionNumber = 1,
            AdmittedAtUtc = now,
            AdmittedBy = NormalizeActor(actor),
            CorrelationId = NormalizeCorrelation(correlationId),
            CreatedByActor = NormalizeActor(actor),
            LastEditedByActor = NormalizeActor(actor),
            CreatedAt = now
        };

        _dbContext.CustomsEvidence.Add(evidence);
        AddVersion(tenantId, evidence.Id, versionId, 1, normalized, checksum, actor, correlationId, now);
        AddHistory(tenantId, evidence.Id, versionId, 1, "DraftCreated", "Customs draft created.", actor, correlationId, now);
        AddWorkflowAudit(tenantId, CustomsAdministrationAuditAction.DraftCreated, actor, "Customs draft created.", correlationId, evidence, versionId, checksum, null, CustomsEvidenceStatus.Draft);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Success(evidence, versionId, checksum);
    }

    public async Task<CustomsWorkflowResult> EditDraftAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        CustomsWorkflowEvidencePayload payload,
        CustomsAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!HasRole(actor, CustomsAdministrationRole.Editor))
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.DraftEdited, actor, "UNAUTHORIZED_EDITOR", correlationId, cancellationToken);
        }

        var evidence = await LoadEvidenceAsync(tenantId, customsEvidenceId, cancellationToken);
        if (evidence is null)
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.DraftEdited, actor, "CUSTOMS_EVIDENCE_NOT_FOUND", correlationId, cancellationToken);
        }

        if (evidence.Status is not (CustomsEvidenceStatus.Draft or CustomsEvidenceStatus.Rejected or CustomsEvidenceStatus.Reopened))
        {
            return await InvalidTransitionAsync(evidence, CustomsEvidenceStatus.Draft, CustomsAdministrationAuditAction.DraftEdited, actor, correlationId, cancellationToken);
        }

        var failure = await ValidatePayloadAsync(tenantId, payload, cancellationToken);
        if (failure is not null)
        {
            return await RejectAsync(tenantId, evidence.Id, evidence.CurrentVersionId, CustomsAdministrationAuditAction.DraftEdited, actor, failure, correlationId, cancellationToken);
        }

        var normalized = Normalize(payload);
        var checksum = ComputeSha256(JsonSerializer.Serialize(normalized));
        var now = DateTime.UtcNow;
        var from = evidence.Status;
        var versionId = Guid.NewGuid();
        evidence.SourceKind = payload.SourceKind;
        evidence.SourceId = payload.SourceId;
        evidence.ProductId = payload.ProductId;
        evidence.ProductVariantId = payload.ProductVariantId;
        evidence.Status = CustomsEvidenceStatus.Draft;
        evidence.CurrentVersionId = versionId;
        evidence.CurrentVersionNumber += 1;
        evidence.AdmittedAtUtc = now;
        evidence.AdmittedBy = NormalizeActor(actor);
        evidence.CorrelationId = NormalizeCorrelation(correlationId);
        evidence.LastEditedByActor = NormalizeActor(actor);
        evidence.SubmittedByActor = null;

        AddVersion(tenantId, evidence.Id, versionId, evidence.CurrentVersionNumber, normalized, checksum, actor, correlationId, now);
        AddHistory(tenantId, evidence.Id, versionId, evidence.CurrentVersionNumber, "DraftEdited", "Customs draft edited; prior approval is no longer current.", actor, correlationId, now);
        AddWorkflowAudit(tenantId, CustomsAdministrationAuditAction.DraftEdited, actor, "Customs draft edited.", correlationId, evidence, versionId, checksum, from, CustomsEvidenceStatus.Draft);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Success(evidence, versionId, checksum);
    }

    public async Task<CustomsWorkflowResult> SubmitForReviewAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        CustomsAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!HasRole(actor, CustomsAdministrationRole.Editor))
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.SubmittedForReview, actor, "UNAUTHORIZED_EDITOR", correlationId, cancellationToken);
        }

        var evidence = await LoadEvidenceAsync(tenantId, customsEvidenceId, cancellationToken);
        if (evidence is null)
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.SubmittedForReview, actor, "CUSTOMS_EVIDENCE_NOT_FOUND", correlationId, cancellationToken);
        }

        if (evidence.Status != CustomsEvidenceStatus.Draft)
        {
            return await InvalidTransitionAsync(evidence, CustomsEvidenceStatus.UnderReview, CustomsAdministrationAuditAction.SubmittedForReview, actor, correlationId, cancellationToken);
        }

        var version = await LoadCurrentVersionAsync(evidence, cancellationToken);
        if (version is null)
        {
            return await RejectAsync(tenantId, evidence.Id, evidence.CurrentVersionId, CustomsAdministrationAuditAction.SubmittedForReview, actor, "VERSION_MISMATCH", correlationId, cancellationToken);
        }

        var from = evidence.Status;
        evidence.Status = CustomsEvidenceStatus.UnderReview;
        evidence.SubmittedByActor = NormalizeActor(actor);
        AddHistory(tenantId, evidence.Id, version.Id, version.VersionNumber, "SubmittedForReview", "Customs draft submitted for review.", actor, correlationId, DateTime.UtcNow);
        AddWorkflowAudit(tenantId, CustomsAdministrationAuditAction.SubmittedForReview, actor, "Customs draft submitted for review.", correlationId, evidence, version.Id, version.EvidenceFingerprintSha256, from, CustomsEvidenceStatus.UnderReview);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Success(evidence, version.Id, version.EvidenceFingerprintSha256);
    }

    public async Task<CustomsWorkflowResult> ApproveAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        string validationChecksumSha256,
        CustomsAdministrationActor actor,
        string reason,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!HasRole(actor, CustomsAdministrationRole.Approver))
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.Approved, actor, "UNAUTHORIZED_APPROVER", correlationId, cancellationToken);
        }

        var evidence = await LoadEvidenceAsync(tenantId, customsEvidenceId, cancellationToken);
        if (evidence is null)
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.Approved, actor, "CUSTOMS_EVIDENCE_NOT_FOUND", correlationId, cancellationToken);
        }

        if (evidence.Status != CustomsEvidenceStatus.UnderReview)
        {
            return await InvalidTransitionAsync(evidence, CustomsEvidenceStatus.Approved, CustomsAdministrationAuditAction.Approved, actor, correlationId, cancellationToken);
        }

        if (IsSameActor(actor, evidence.CreatedByActor) || IsSameActor(actor, evidence.LastEditedByActor) || IsSameActor(actor, evidence.SubmittedByActor))
        {
            return await RejectAsync(tenantId, evidence.Id, evidence.CurrentVersionId, CustomsAdministrationAuditAction.Approved, actor, "SELF_APPROVAL_REJECTED", correlationId, cancellationToken);
        }

        var version = await LoadCurrentVersionAsync(evidence, cancellationToken);
        if (version is null || !ChecksumMatches(validationChecksumSha256, version.EvidenceFingerprintSha256))
        {
            return await RejectAsync(tenantId, evidence.Id, evidence.CurrentVersionId, CustomsAdministrationAuditAction.Approved, actor, "STALE_VALIDATION_CHECKSUM", correlationId, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var from = evidence.Status;
        evidence.Status = CustomsEvidenceStatus.Approved;
        _dbContext.CustomsEvidenceApprovals.Add(new CustomsEvidenceApproval
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomsEvidenceId = evidence.Id,
            CustomsEvidenceVersionId = version.Id,
            VersionNumber = version.VersionNumber,
            ValidationChecksumSha256 = version.EvidenceFingerprintSha256,
            ApprovedByActor = NormalizeActor(actor),
            Reason = NormalizeReason(reason, "Customs evidence approved."),
            CorrelationId = NormalizeCorrelation(correlationId),
            ApprovedAtUtc = now,
            CreatedAt = now
        });
        AddHistory(tenantId, evidence.Id, version.Id, version.VersionNumber, "Approved", NormalizeReason(reason, "Customs evidence approved."), actor, correlationId, now);
        AddWorkflowAudit(tenantId, CustomsAdministrationAuditAction.Approved, actor, NormalizeReason(reason, "Customs evidence approved."), correlationId, evidence, version.Id, version.EvidenceFingerprintSha256, from, CustomsEvidenceStatus.Approved);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Success(evidence, version.Id, version.EvidenceFingerprintSha256);
    }

    public async Task<CustomsWorkflowResult> RejectAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        CustomsAdministrationActor actor,
        string reason,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!HasRole(actor, CustomsAdministrationRole.Reviewer) && !HasRole(actor, CustomsAdministrationRole.Approver))
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.Rejected, actor, "UNAUTHORIZED_REVIEWER", correlationId, cancellationToken);
        }

        var evidence = await LoadEvidenceAsync(tenantId, customsEvidenceId, cancellationToken);
        if (evidence is null)
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.Rejected, actor, "CUSTOMS_EVIDENCE_NOT_FOUND", correlationId, cancellationToken);
        }

        if (evidence.Status != CustomsEvidenceStatus.UnderReview)
        {
            return await InvalidTransitionAsync(evidence, CustomsEvidenceStatus.Rejected, CustomsAdministrationAuditAction.Rejected, actor, correlationId, cancellationToken);
        }

        var version = await LoadCurrentVersionAsync(evidence, cancellationToken);
        var from = evidence.Status;
        evidence.Status = CustomsEvidenceStatus.Rejected;
        AddHistory(tenantId, evidence.Id, evidence.CurrentVersionId, evidence.CurrentVersionNumber, "Rejected", NormalizeReason(reason, "Customs evidence rejected."), actor, correlationId, DateTime.UtcNow);
        AddWorkflowAudit(tenantId, CustomsAdministrationAuditAction.Rejected, actor, NormalizeReason(reason, "Customs evidence rejected."), correlationId, evidence, evidence.CurrentVersionId, version?.EvidenceFingerprintSha256, from, CustomsEvidenceStatus.Rejected);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Success(evidence, evidence.CurrentVersionId, version?.EvidenceFingerprintSha256);
    }

    public async Task<CustomsWorkflowResult> ReopenAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        CustomsAdministrationActor actor,
        string reason,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!HasRole(actor, CustomsAdministrationRole.Editor) && !HasRole(actor, CustomsAdministrationRole.Reviewer))
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.Reopened, actor, "UNAUTHORIZED_REOPEN", correlationId, cancellationToken);
        }

        var evidence = await LoadEvidenceAsync(tenantId, customsEvidenceId, cancellationToken);
        if (evidence is null)
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.Reopened, actor, "CUSTOMS_EVIDENCE_NOT_FOUND", correlationId, cancellationToken);
        }

        if (evidence.Status != CustomsEvidenceStatus.Rejected)
        {
            return await InvalidTransitionAsync(evidence, CustomsEvidenceStatus.Reopened, CustomsAdministrationAuditAction.Reopened, actor, correlationId, cancellationToken);
        }

        var version = await LoadCurrentVersionAsync(evidence, cancellationToken);
        var from = evidence.Status;
        evidence.Status = CustomsEvidenceStatus.Reopened;
        evidence.SubmittedByActor = null;
        AddHistory(tenantId, evidence.Id, evidence.CurrentVersionId, evidence.CurrentVersionNumber, "Reopened", NormalizeReason(reason, "Customs evidence reopened."), actor, correlationId, DateTime.UtcNow);
        AddWorkflowAudit(tenantId, CustomsAdministrationAuditAction.Reopened, actor, NormalizeReason(reason, "Customs evidence reopened."), correlationId, evidence, evidence.CurrentVersionId, version?.EvidenceFingerprintSha256, from, CustomsEvidenceStatus.Reopened);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Success(evidence, evidence.CurrentVersionId, version?.EvidenceFingerprintSha256);
    }

    public async Task<CustomsWorkflowResult> PublishAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        CustomsAdministrationActor actor,
        string reason,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!HasRole(actor, CustomsAdministrationRole.PublicationOperator))
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.Published, actor, "UNAUTHORIZED_PUBLICATION_OPERATOR", correlationId, cancellationToken);
        }

        var evidence = await LoadEvidenceAsync(tenantId, customsEvidenceId, cancellationToken);
        if (evidence is null)
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.Published, actor, "CUSTOMS_EVIDENCE_NOT_FOUND", correlationId, cancellationToken);
        }

        if (evidence.Status != CustomsEvidenceStatus.Approved)
        {
            return await InvalidTransitionAsync(evidence, CustomsEvidenceStatus.Published, CustomsAdministrationAuditAction.Published, actor, correlationId, cancellationToken);
        }

        if (IsSameActor(actor, evidence.CreatedByActor) || IsSameActor(actor, evidence.LastEditedByActor))
        {
            return await RejectAsync(tenantId, evidence.Id, evidence.CurrentVersionId, CustomsAdministrationAuditAction.Published, actor, "SELF_APPROVAL_REJECTED", correlationId, cancellationToken);
        }

        var version = await LoadCurrentVersionAsync(evidence, cancellationToken);
        var approval = version is null
            ? null
            : await LoadCurrentApprovalAsync(tenantId, evidence.Id, version.Id, version.EvidenceFingerprintSha256, cancellationToken);
        if (version is null || approval is null)
        {
            return await RejectAsync(tenantId, evidence.Id, evidence.CurrentVersionId, CustomsAdministrationAuditAction.Published, actor, "MISSING_APPROVAL", correlationId, cancellationToken);
        }

        if (IsSameActor(actor, approval.ApprovedByActor))
        {
            return await RejectAsync(tenantId, evidence.Id, version.Id, CustomsAdministrationAuditAction.Published, actor, "ROLE_SEPARATION_REJECTED", correlationId, cancellationToken);
        }

        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var now = DateTime.UtcNow;
        var publicationLock = await AcquirePublicationLockAsync(tenantId, evidence, actor, correlationId, now, cancellationToken);
        var active = await LoadActivePublishedEvidenceForProductAsync(evidence, cancellationToken);
        foreach (var published in active.Where(published => published.Id != evidence.Id))
        {
            var publishedVersion = await LoadCurrentVersionAsync(published, cancellationToken);
            published.Status = CustomsEvidenceStatus.Superseded;
            AddPublication(tenantId, published, publishedVersion, CustomsPublicationAction.Superseded, actor, NormalizeReason(reason, "Superseded by new published Customs evidence."), correlationId, now, evidence.Id, version.Id);
            AddWorkflowAudit(tenantId, CustomsAdministrationAuditAction.Superseded, actor, NormalizeReason(reason, "Superseded by new published Customs evidence."), correlationId, published, published.CurrentVersionId, publishedVersion?.EvidenceFingerprintSha256, CustomsEvidenceStatus.Published, CustomsEvidenceStatus.Superseded);
        }

        var from = evidence.Status;
        evidence.Status = CustomsEvidenceStatus.Published;
        AddPublication(tenantId, evidence, version, CustomsPublicationAction.Published, actor, NormalizeReason(reason, "Customs evidence published."), correlationId, now);
        AddHistory(tenantId, evidence.Id, version.Id, version.VersionNumber, "Published", NormalizeReason(reason, "Customs evidence published."), actor, correlationId, now);
        AddWorkflowAudit(tenantId, CustomsAdministrationAuditAction.Published, actor, NormalizeReason(reason, "Customs evidence published."), correlationId, evidence, version.Id, version.EvidenceFingerprintSha256, from, CustomsEvidenceStatus.Published);
        publicationLock.LockedUntilUtc = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return Success(evidence, version.Id, version.EvidenceFingerprintSha256);
    }

    public async Task<CustomsWorkflowResult> RetireAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        CustomsAdministrationActor actor,
        string reason,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!HasRole(actor, CustomsAdministrationRole.PublicationOperator))
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.Retired, actor, "UNAUTHORIZED_PUBLICATION_OPERATOR", correlationId, cancellationToken);
        }

        var evidence = await LoadEvidenceAsync(tenantId, customsEvidenceId, cancellationToken);
        if (evidence is null)
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.Retired, actor, "CUSTOMS_EVIDENCE_NOT_FOUND", correlationId, cancellationToken);
        }

        if (evidence.Status != CustomsEvidenceStatus.Published)
        {
            return await InvalidTransitionAsync(evidence, CustomsEvidenceStatus.Retired, CustomsAdministrationAuditAction.Retired, actor, correlationId, cancellationToken);
        }

        var version = await LoadCurrentVersionAsync(evidence, cancellationToken);
        var from = evidence.Status;
        evidence.Status = CustomsEvidenceStatus.Retired;
        AddPublication(tenantId, evidence, version, CustomsPublicationAction.Retired, actor, NormalizeReason(reason, "Customs evidence retired."), correlationId, DateTime.UtcNow);
        AddWorkflowAudit(tenantId, CustomsAdministrationAuditAction.Retired, actor, NormalizeReason(reason, "Customs evidence retired."), correlationId, evidence, evidence.CurrentVersionId, version?.EvidenceFingerprintSha256, from, CustomsEvidenceStatus.Retired);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Success(evidence, evidence.CurrentVersionId, version?.EvidenceFingerprintSha256);
    }

    public async Task<CustomsWorkflowResult> SupersedeAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        Guid replacementCustomsEvidenceId,
        CustomsAdministrationActor actor,
        string reason,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!HasRole(actor, CustomsAdministrationRole.PublicationOperator))
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.Superseded, actor, "UNAUTHORIZED_PUBLICATION_OPERATOR", correlationId, cancellationToken);
        }

        var evidence = await LoadEvidenceAsync(tenantId, customsEvidenceId, cancellationToken);
        var replacement = await LoadEvidenceAsync(tenantId, replacementCustomsEvidenceId, cancellationToken);
        if (evidence is null || replacement is null)
        {
            return await RejectAsync(tenantId, customsEvidenceId, null, CustomsAdministrationAuditAction.Superseded, actor, "CUSTOMS_EVIDENCE_NOT_FOUND", correlationId, cancellationToken);
        }

        if (evidence.Status != CustomsEvidenceStatus.Published || replacement.Status != CustomsEvidenceStatus.Published)
        {
            return await InvalidTransitionAsync(evidence, CustomsEvidenceStatus.Superseded, CustomsAdministrationAuditAction.Superseded, actor, correlationId, cancellationToken);
        }

        var version = await LoadCurrentVersionAsync(evidence, cancellationToken);
        var replacementVersion = await LoadCurrentVersionAsync(replacement, cancellationToken);
        var from = evidence.Status;
        evidence.Status = CustomsEvidenceStatus.Superseded;
        AddPublication(tenantId, evidence, version, CustomsPublicationAction.Superseded, actor, NormalizeReason(reason, "Customs evidence superseded."), correlationId, DateTime.UtcNow, replacement.Id, replacementVersion?.Id);
        AddWorkflowAudit(tenantId, CustomsAdministrationAuditAction.Superseded, actor, NormalizeReason(reason, "Customs evidence superseded."), correlationId, evidence, evidence.CurrentVersionId, version?.EvidenceFingerprintSha256, from, CustomsEvidenceStatus.Superseded);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Success(evidence, evidence.CurrentVersionId, version?.EvidenceFingerprintSha256);
    }

    public async Task<CustomsWorkflowResult> ValidateAdmissionAsync(
        CustomsAdmissionValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasRole(request.Actor, CustomsAdministrationRole.AdmissionService))
        {
            return await RejectAsync(request.TenantId, request.CustomsEvidenceId, request.CustomsEvidenceVersionId, CustomsAdministrationAuditAction.AdmissionRejected, request.Actor, "UNAUTHORIZED_ADMISSION_SERVICE", request.CorrelationId, cancellationToken);
        }

        var evidence = await _dbContext.CustomsEvidence
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == request.TenantId &&
                candidate.Id == request.CustomsEvidenceId &&
                !candidate.IsDeleted,
                cancellationToken);
        if (evidence is null)
        {
            return await RejectAsync(request.TenantId, request.CustomsEvidenceId, request.CustomsEvidenceVersionId, CustomsAdministrationAuditAction.AdmissionRejected, request.Actor, "CUSTOMS_EVIDENCE_NOT_FOUND", request.CorrelationId, cancellationToken);
        }

        var failure = await GetAdmissionFailureAsync(evidence, request.CustomsEvidenceVersionId, request.ProductId, request.ProductVariantId, request.VersionNumber, request.ValidationChecksumSha256, cancellationToken);
        if (failure is not null)
        {
            return await RejectAsync(request.TenantId, request.CustomsEvidenceId, request.CustomsEvidenceVersionId, CustomsAdministrationAuditAction.AdmissionRejected, request.Actor, failure, request.CorrelationId, cancellationToken);
        }

        AddWorkflowAudit(request.TenantId, CustomsAdministrationAuditAction.AdmissionValidated, request.Actor, "Published Customs evidence admitted for shipment.", request.CorrelationId, evidence, request.CustomsEvidenceVersionId, request.ValidationChecksumSha256, CustomsEvidenceStatus.Published, CustomsEvidenceStatus.Published);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new CustomsWorkflowResult(true, evidence.Id, request.CustomsEvidenceVersionId, request.VersionNumber, evidence.Status, request.ValidationChecksumSha256, null, null);
    }

    private async Task<string?> GetAdmissionFailureAsync(
        CustomsEvidence evidence,
        Guid versionId,
        Guid productId,
        Guid? productVariantId,
        int versionNumber,
        string checksum,
        CancellationToken cancellationToken)
    {
        if (evidence.Status != CustomsEvidenceStatus.Published)
        {
            return evidence.Status switch
            {
                CustomsEvidenceStatus.Draft => "DRAFT_EVIDENCE_REJECTED",
                CustomsEvidenceStatus.Rejected => "REJECTED_EVIDENCE_REJECTED",
                CustomsEvidenceStatus.Retired => "RETIRED_EVIDENCE_REJECTED",
                CustomsEvidenceStatus.Superseded => "SUPERSEDED_EVIDENCE_REJECTED",
                CustomsEvidenceStatus.Approved => "MISSING_PUBLICATION",
                _ => "EVIDENCE_NOT_PUBLISHED"
            };
        }

        if (evidence.ProductId != productId || evidence.ProductVariantId != productVariantId)
        {
            return "WRONG_PRODUCT";
        }

        if (evidence.CurrentVersionId != versionId || evidence.CurrentVersionNumber != versionNumber)
        {
            return "VERSION_MISMATCH";
        }

        var version = await _dbContext.CustomsEvidenceVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == evidence.TenantId &&
                candidate.Id == versionId &&
                candidate.CustomsEvidenceId == evidence.Id &&
                candidate.VersionNumber == versionNumber &&
                !candidate.IsDeleted,
                cancellationToken);
        if (version is null)
        {
            return "VERSION_MISMATCH";
        }

        if (!ChecksumMatches(checksum, version.EvidenceFingerprintSha256))
        {
            return "CHECKSUM_MISMATCH";
        }

        var approval = await LoadCurrentApprovalAsync(evidence.TenantId, evidence.Id, version.Id, version.EvidenceFingerprintSha256, cancellationToken);
        if (approval is null)
        {
            return "MISSING_APPROVAL";
        }

        var publication = await _dbContext.CustomsEvidencePublications
            .AsNoTracking()
            .AnyAsync(candidate =>
                candidate.TenantId == evidence.TenantId &&
                candidate.CustomsEvidenceId == evidence.Id &&
                candidate.CustomsEvidenceVersionId == version.Id &&
                candidate.ValidationChecksumSha256 == version.EvidenceFingerprintSha256 &&
                candidate.Action == CustomsPublicationAction.Published &&
                !candidate.IsDeleted,
                cancellationToken);
        return publication ? null : "MISSING_PUBLICATION";
    }

    private async Task<CustomsEvidence?> LoadEvidenceAsync(Guid tenantId, Guid customsEvidenceId, CancellationToken cancellationToken)
    {
        return await _dbContext.CustomsEvidence.FirstOrDefaultAsync(candidate =>
            candidate.TenantId == tenantId &&
            candidate.Id == customsEvidenceId &&
            !candidate.IsDeleted,
            cancellationToken);
    }

    private async Task<CustomsEvidenceVersion?> LoadCurrentVersionAsync(CustomsEvidence evidence, CancellationToken cancellationToken)
    {
        return await _dbContext.CustomsEvidenceVersions
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == evidence.TenantId &&
                candidate.Id == evidence.CurrentVersionId &&
                candidate.CustomsEvidenceId == evidence.Id &&
                candidate.VersionNumber == evidence.CurrentVersionNumber &&
                !candidate.IsDeleted,
                cancellationToken);
    }

    private async Task<CustomsEvidenceApproval?> LoadCurrentApprovalAsync(
        Guid tenantId,
        Guid evidenceId,
        Guid versionId,
        string checksum,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CustomsEvidenceApprovals
            .AsNoTracking()
            .OrderByDescending(candidate => candidate.ApprovedAtUtc)
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == tenantId &&
                candidate.CustomsEvidenceId == evidenceId &&
                candidate.CustomsEvidenceVersionId == versionId &&
                candidate.ValidationChecksumSha256 == checksum &&
                !candidate.IsDeleted,
                cancellationToken);
    }

    private async Task<List<CustomsEvidence>> LoadActivePublishedEvidenceForProductAsync(
        CustomsEvidence evidence,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CustomsEvidence
            .Where(candidate =>
                candidate.TenantId == evidence.TenantId &&
                candidate.ProductId == evidence.ProductId &&
                candidate.ProductVariantId == evidence.ProductVariantId &&
                candidate.Status == CustomsEvidenceStatus.Published &&
                !candidate.IsDeleted)
            .ToListAsync(cancellationToken);
    }

    private async Task<CustomsPublicationLock> AcquirePublicationLockAsync(
        Guid tenantId,
        CustomsEvidence evidence,
        CustomsAdministrationActor actor,
        string correlationId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var lockKey = PublicationLockKey(evidence);
        var existing = await _dbContext.CustomsPublicationLocks
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == tenantId &&
                candidate.LockKey == lockKey,
                cancellationToken);
        if (existing is null)
        {
            var created = new CustomsPublicationLock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                LockKey = lockKey,
                LockedAtUtc = now,
                LockedUntilUtc = now.AddMinutes(5),
                LockedByActor = NormalizeActor(actor),
                CorrelationId = NormalizeCorrelation(correlationId),
                CreatedAt = now
            };
            _dbContext.CustomsPublicationLocks.Add(created);
            return created;
        }

        if (existing.LockedUntilUtc > now && !IsSameActor(actor, existing.LockedByActor))
        {
            throw new InvalidOperationException("Customs publication lock is already held for this tenant/product/version scope.");
        }

        existing.LockedAtUtc = now;
        existing.LockedUntilUtc = now.AddMinutes(5);
        existing.LockedByActor = NormalizeActor(actor);
        existing.CorrelationId = NormalizeCorrelation(correlationId);
        return existing;
    }

    private void AddPublication(
        Guid tenantId,
        CustomsEvidence evidence,
        CustomsEvidenceVersion? version,
        CustomsPublicationAction action,
        CustomsAdministrationActor actor,
        string reason,
        string correlationId,
        DateTime now,
        Guid? supersededEvidenceId = null,
        Guid? supersededVersionId = null)
    {
        _dbContext.CustomsEvidencePublications.Add(new CustomsEvidencePublication
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomsEvidenceId = evidence.Id,
            CustomsEvidenceVersionId = version?.Id ?? evidence.CurrentVersionId,
            SupersededCustomsEvidenceId = supersededEvidenceId,
            SupersededCustomsEvidenceVersionId = supersededVersionId,
            ProductId = evidence.ProductId,
            ProductVariantId = evidence.ProductVariantId,
            VersionNumber = version?.VersionNumber ?? evidence.CurrentVersionNumber,
            Action = action,
            ValidationChecksumSha256 = version?.EvidenceFingerprintSha256 ?? new string('0', 64),
            PublishedByActor = NormalizeActor(actor),
            Reason = reason,
            CorrelationId = NormalizeCorrelation(correlationId),
            OccurredAtUtc = now,
            CreatedAt = now
        });
    }

    private void AddVersion(
        Guid tenantId,
        Guid evidenceId,
        Guid versionId,
        int versionNumber,
        NormalizedEvidencePayload normalized,
        string checksum,
        CustomsAdministrationActor actor,
        string correlationId,
        DateTime now)
    {
        _dbContext.CustomsEvidenceVersions.Add(new CustomsEvidenceVersion
        {
            Id = versionId,
            TenantId = tenantId,
            CustomsEvidenceId = evidenceId,
            VersionNumber = versionNumber,
            HsCode = normalized.HsCode,
            CommodityDescription = normalized.CommodityDescription,
            CountryOfOriginIso2 = normalized.CountryOfOriginIso2,
            CountryOfManufactureIso2 = normalized.CountryOfManufactureIso2,
            DeclaredValue = normalized.DeclaredValue,
            Currency = normalized.Currency,
            NetWeight = normalized.NetWeight,
            WeightUnit = normalized.WeightUnit,
            QuantityUnit = normalized.QuantityUnit,
            DangerousGoods = normalized.DangerousGoods,
            ExportClassification = normalized.ExportClassification,
            ValueSource = normalized.ValueSource,
            SourceEvidenceJson = normalized.SourceEvidenceJson,
            EvidenceFingerprintSha256 = checksum,
            AdmittedAtUtc = now,
            AdmittedBy = NormalizeActor(actor),
            CorrelationId = NormalizeCorrelation(correlationId),
            CreatedAt = now
        });
        _dbContext.CustomsEvidenceFingerprints.Add(new CustomsEvidenceFingerprint
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomsEvidenceId = evidenceId,
            CustomsEvidenceVersionId = versionId,
            FingerprintSha256 = checksum,
            CreatedAtUtc = now,
            CreatedAt = now
        });
    }

    private void AddHistory(
        Guid tenantId,
        Guid evidenceId,
        Guid versionId,
        int versionNumber,
        string action,
        string reason,
        CustomsAdministrationActor actor,
        string correlationId,
        DateTime now)
    {
        _dbContext.CustomsEvidenceHistory.Add(new CustomsEvidenceHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomsEvidenceId = evidenceId,
            CustomsEvidenceVersionId = versionId,
            VersionNumber = versionNumber,
            Action = action,
            Reason = reason,
            Actor = NormalizeActor(actor),
            CorrelationId = NormalizeCorrelation(correlationId),
            OccurredAtUtc = now,
            CreatedAt = now
        });
    }

    private void AddWorkflowAudit(
        Guid tenantId,
        CustomsAdministrationAuditAction action,
        CustomsAdministrationActor actor,
        string reason,
        string correlationId,
        CustomsEvidence? evidence,
        Guid? versionId,
        string? checksum,
        CustomsEvidenceStatus? fromStatus,
        CustomsEvidenceStatus? toStatus,
        string? failureCode = null)
    {
        _dbContext.CustomsAdministrationAudit.Add(new CustomsAdministrationAudit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Action = action,
            CustomsEvidenceId = evidence?.Id,
            CustomsEvidenceVersionId = versionId,
            ProductId = evidence?.ProductId,
            ProductVariantId = evidence?.ProductVariantId,
            Actor = NormalizeActor(actor),
            Role = string.Join(",", actor.Roles.OrderBy(role => role.ToString())),
            Reason = reason,
            CorrelationId = NormalizeCorrelation(correlationId),
            ValidationChecksumSha256 = checksum,
            FailureCode = failureCode,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            OccurredAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
    }

    private async Task<CustomsWorkflowResult> InvalidTransitionAsync(
        CustomsEvidence evidence,
        CustomsEvidenceStatus targetStatus,
        CustomsAdministrationAuditAction action,
        CustomsAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        AddWorkflowAudit(evidence.TenantId, action, actor, "Invalid Customs workflow state transition rejected.", correlationId, evidence, evidence.CurrentVersionId, null, evidence.Status, targetStatus, "INVALID_TRANSITION");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new CustomsWorkflowResult(false, evidence.Id, evidence.CurrentVersionId, evidence.CurrentVersionNumber, evidence.Status, null, "INVALID_TRANSITION", "Invalid Customs workflow state transition rejected.");
    }

    private async Task<CustomsWorkflowResult> RejectAsync(
        Guid tenantId,
        Guid? evidenceId,
        Guid? versionId,
        CustomsAdministrationAuditAction action,
        CustomsAdministrationActor actor,
        string failureCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        AddWorkflowAudit(tenantId, action, actor, failureCode, correlationId, evidenceId.HasValue ? new CustomsEvidence { Id = evidenceId.Value, TenantId = tenantId } : null, versionId, null, null, null, failureCode);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new CustomsWorkflowResult(false, evidenceId, versionId, null, null, null, failureCode, failureCode);
    }

    private async Task<string?> ValidatePayloadAsync(Guid tenantId, CustomsWorkflowEvidencePayload payload, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || payload.SourceId == Guid.Empty)
        {
            return "MALFORMED_EVIDENCE";
        }

        if (string.IsNullOrWhiteSpace(payload.HsCode))
        {
            return "MISSING_HS_CODE";
        }

        if (string.IsNullOrWhiteSpace(payload.CommodityDescription))
        {
            return "MISSING_COMMODITY";
        }

        if (!IsIso2(payload.CountryOfOriginIso2))
        {
            return "MISSING_COUNTRY_OF_ORIGIN";
        }

        if (payload.DeclaredValue <= 0)
        {
            return "MISSING_DECLARED_VALUE";
        }

        if (!IsCurrency(payload.Currency))
        {
            return "MISSING_CURRENCY";
        }

        if (payload.NetWeight <= 0 || !SupportedWeightUnits.Contains(payload.WeightUnit))
        {
            return "INVALID_WEIGHT";
        }

        if (string.IsNullOrWhiteSpace(payload.QuantityUnit))
        {
            return "INVALID_UNITS";
        }

        try
        {
            JsonDocument.Parse(payload.SourceEvidenceJson);
        }
        catch (JsonException)
        {
            return "MALFORMED_EVIDENCE";
        }

        var countryIso2 = payload.CountryOfOriginIso2.Trim().ToUpperInvariant();
        var supportedCountry = await _dbContext.GeographicCountryProfiles
            .AsNoTracking()
            .AnyAsync(profile => profile.Iso2 == countryIso2 && profile.IsActive, cancellationToken);
        return supportedCountry ? null : "UNSUPPORTED_COUNTRY";
    }

    private static CustomsWorkflowResult Success(CustomsEvidence evidence, Guid? versionId, string? checksum)
    {
        return new CustomsWorkflowResult(true, evidence.Id, versionId, evidence.CurrentVersionNumber, evidence.Status, checksum, null, null);
    }

    private static bool HasRole(CustomsAdministrationActor actor, CustomsAdministrationRole role) =>
        actor.Roles.Contains(role);

    private static bool IsSameActor(CustomsAdministrationActor actor, string? other) =>
        string.Equals(NormalizeActor(actor), other?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ChecksumMatches(string submitted, string expected) =>
        string.Equals(submitted?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeActor(CustomsAdministrationActor actor) =>
        string.IsNullOrWhiteSpace(actor.ActorId) ? "system" : actor.ActorId.Trim();

    private static string NormalizeCorrelation(string correlationId) =>
        string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId.Trim();

    private static string NormalizeReason(string reason, string fallback) =>
        string.IsNullOrWhiteSpace(reason) ? fallback : reason.Trim();

    private static string PublicationLockKey(CustomsEvidence evidence) =>
        $"{evidence.ProductId?.ToString("N") ?? "none"}:{evidence.ProductVariantId?.ToString("N") ?? "none"}";

    private static bool IsIso2(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().Length == 2 &&
        value.Trim().All(char.IsLetter);

    private static bool IsCurrency(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().Length == 3 &&
        value.Trim().All(char.IsLetter);

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static NormalizedEvidencePayload Normalize(CustomsWorkflowEvidencePayload payload)
    {
        return new NormalizedEvidencePayload(
            payload.HsCode.Trim().ToUpperInvariant(),
            payload.CommodityDescription.Trim(),
            payload.CountryOfOriginIso2.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(payload.CountryOfManufactureIso2) ? null : payload.CountryOfManufactureIso2.Trim().ToUpperInvariant(),
            payload.DeclaredValue,
            payload.Currency.Trim().ToUpperInvariant(),
            payload.NetWeight,
            payload.WeightUnit.Trim().ToLowerInvariant(),
            payload.QuantityUnit.Trim(),
            payload.DangerousGoods,
            string.IsNullOrWhiteSpace(payload.ExportClassification) ? null : payload.ExportClassification.Trim(),
            payload.ValueSource.Trim(),
            payload.SourceEvidenceJson.Trim());
    }

    private sealed record NormalizedEvidencePayload(
        string HsCode,
        string CommodityDescription,
        string CountryOfOriginIso2,
        string? CountryOfManufactureIso2,
        decimal DeclaredValue,
        string Currency,
        decimal NetWeight,
        string WeightUnit,
        string QuantityUnit,
        bool? DangerousGoods,
        string? ExportClassification,
        string ValueSource,
        string SourceEvidenceJson);
}
