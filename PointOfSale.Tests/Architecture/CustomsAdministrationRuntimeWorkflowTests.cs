using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities.Customs;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Infrastructure.Customs;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class CustomsAdministrationRuntimeWorkflowTests
{
    [Fact]
    public async Task Workflow_ShouldEnforceApprovalSeparationPublicationAndAdmission()
    {
        await using var db = CreateDbContext();
        SeedCountry(db, "EG", "EGY");
        var service = CreateService(db);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var draft = await service.CreateDraftAsync(
            tenantId,
            Payload(productId),
            Actor("editor", CustomsAdministrationRole.Editor),
            "corr-1");

        Assert.True(draft.Succeeded);
        Assert.Equal(CustomsEvidenceStatus.Draft, draft.Status);

        var submitted = await service.SubmitForReviewAsync(
            tenantId,
            draft.CustomsEvidenceId!.Value,
            Actor("editor", CustomsAdministrationRole.Editor),
            "corr-2");
        Assert.True(submitted.Succeeded);
        Assert.Equal(CustomsEvidenceStatus.UnderReview, submitted.Status);

        var selfApproval = await service.ApproveAsync(
            tenantId,
            draft.CustomsEvidenceId.Value,
            draft.ValidationChecksumSha256!,
            Actor("editor", CustomsAdministrationRole.Approver),
            "self approval attempt",
            "corr-3");
        Assert.False(selfApproval.Succeeded);
        Assert.Equal("SELF_APPROVAL_REJECTED", selfApproval.FailureCode);

        var approved = await service.ApproveAsync(
            tenantId,
            draft.CustomsEvidenceId.Value,
            draft.ValidationChecksumSha256!,
            Actor("approver", CustomsAdministrationRole.Approver),
            "approved",
            "corr-4");
        Assert.True(approved.Succeeded);
        Assert.Equal(CustomsEvidenceStatus.Approved, approved.Status);

        var admissionBeforePublish = await service.ValidateAdmissionAsync(new CustomsAdmissionValidationRequest(
            tenantId,
            approved.CustomsEvidenceId!.Value,
            approved.CustomsEvidenceVersionId!.Value,
            productId,
            null,
            approved.VersionNumber!.Value,
            approved.ValidationChecksumSha256!,
            Actor("admission", CustomsAdministrationRole.AdmissionService),
            "corr-5"));
        Assert.False(admissionBeforePublish.Succeeded);
        Assert.Equal("MISSING_PUBLICATION", admissionBeforePublish.FailureCode);

        var published = await service.PublishAsync(
            tenantId,
            approved.CustomsEvidenceId.Value,
            Actor("publisher", CustomsAdministrationRole.PublicationOperator),
            "publish",
            "corr-6");
        Assert.True(published.Succeeded);
        Assert.Equal(CustomsEvidenceStatus.Published, published.Status);

        var admitted = await service.ValidateAdmissionAsync(new CustomsAdmissionValidationRequest(
            tenantId,
            published.CustomsEvidenceId!.Value,
            published.CustomsEvidenceVersionId!.Value,
            productId,
            null,
            published.VersionNumber!.Value,
            published.ValidationChecksumSha256!,
            Actor("admission", CustomsAdministrationRole.AdmissionService),
            "corr-7"));
        Assert.True(admitted.Succeeded);

        Assert.True(await db.CustomsEvidenceApprovals.CountAsync() > 0);
        Assert.True(await db.CustomsEvidencePublications.CountAsync() > 0);
        Assert.Contains(await db.CustomsAdministrationAudit.ToListAsync(), audit => audit.Action == CustomsAdministrationAuditAction.AdmissionValidated);
    }

    [Fact]
    public async Task Workflow_ShouldRejectInvalidTransitionsAndDraftAdmission()
    {
        await using var db = CreateDbContext();
        SeedCountry(db, "EG", "EGY");
        var service = CreateService(db);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var draft = await service.CreateDraftAsync(
            tenantId,
            Payload(productId),
            Actor("editor", CustomsAdministrationRole.Editor),
            "corr-8");

        var invalidPublish = await service.PublishAsync(
            tenantId,
            draft.CustomsEvidenceId!.Value,
            Actor("publisher", CustomsAdministrationRole.PublicationOperator),
            "publish draft",
            "corr-9");
        Assert.False(invalidPublish.Succeeded);
        Assert.Equal("INVALID_TRANSITION", invalidPublish.FailureCode);

        var draftAdmission = await service.ValidateAdmissionAsync(new CustomsAdmissionValidationRequest(
            tenantId,
            draft.CustomsEvidenceId.Value,
            draft.CustomsEvidenceVersionId!.Value,
            productId,
            null,
            draft.VersionNumber!.Value,
            draft.ValidationChecksumSha256!,
            Actor("admission", CustomsAdministrationRole.AdmissionService),
            "corr-10"));
        Assert.False(draftAdmission.Succeeded);
        Assert.Equal("DRAFT_EVIDENCE_REJECTED", draftAdmission.FailureCode);
    }

    [Fact]
    public async Task Publication_ShouldSupersedePreviousActiveVersionAndRetirementBlocksAdmission()
    {
        await using var db = CreateDbContext();
        SeedCountry(db, "EG", "EGY");
        var service = CreateService(db);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var first = await PublishEvidenceAsync(service, tenantId, productId, "A");
        var second = await PublishEvidenceAsync(service, tenantId, productId, "B");

        var firstEvidence = await db.CustomsEvidence.FindAsync(first.CustomsEvidenceId!.Value);
        var secondEvidence = await db.CustomsEvidence.FindAsync(second.CustomsEvidenceId!.Value);
        Assert.Equal(CustomsEvidenceStatus.Superseded, firstEvidence!.Status);
        Assert.Equal(CustomsEvidenceStatus.Published, secondEvidence!.Status);

        var supersededAdmission = await service.ValidateAdmissionAsync(new CustomsAdmissionValidationRequest(
            tenantId,
            first.CustomsEvidenceId.Value,
            first.CustomsEvidenceVersionId!.Value,
            productId,
            null,
            first.VersionNumber!.Value,
            first.ValidationChecksumSha256!,
            Actor("admission", CustomsAdministrationRole.AdmissionService),
            "corr-11"));
        Assert.False(supersededAdmission.Succeeded);
        Assert.Equal("SUPERSEDED_EVIDENCE_REJECTED", supersededAdmission.FailureCode);

        var retired = await service.RetireAsync(
            tenantId,
            second.CustomsEvidenceId.Value,
            Actor("publisher", CustomsAdministrationRole.PublicationOperator),
            "retire",
            "corr-12");
        Assert.True(retired.Succeeded);

        var retiredAdmission = await service.ValidateAdmissionAsync(new CustomsAdmissionValidationRequest(
            tenantId,
            second.CustomsEvidenceId.Value,
            second.CustomsEvidenceVersionId!.Value,
            productId,
            null,
            second.VersionNumber!.Value,
            second.ValidationChecksumSha256!,
            Actor("admission", CustomsAdministrationRole.AdmissionService),
            "corr-13"));
        Assert.False(retiredAdmission.Succeeded);
        Assert.Equal("RETIRED_EVIDENCE_REJECTED", retiredAdmission.FailureCode);
    }

    [Fact]
    public void Customs4Report_MustExistAndRecordRuntimeScope()
    {
        var report = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-4_CUSTOMS_ADMINISTRATION_RUNTIME_WORKFLOW.md");

        foreach (var heading in new[]
                 {
                     "Executive Summary",
                     "Runtime Workflow",
                     "Authorization Matrix",
                     "State Machine",
                     "Publication Engine",
                     "Admission Runtime",
                     "Audit Runtime",
                     "Regression Tests",
                     "Verification",
                     "Risks",
                     "Final Recommendation"
                 })
        {
            Assert.Contains($"## {heading}", report, StringComparison.Ordinal);
        }

        Assert.Contains("Reviewed ONLY Customs Administration Runtime? **Yes.**", report, StringComparison.Ordinal);
        Assert.Contains("Frontend modified? **No.**", report, StringComparison.Ordinal);
        Assert.Contains("Authorization implemented? **Yes.**", report, StringComparison.Ordinal);
        Assert.Contains("Publication implemented? **Yes.**", report, StringComparison.Ordinal);
        Assert.Contains("Admission enforced? **Yes.**", report, StringComparison.Ordinal);
    }

    private static async Task<CustomsWorkflowResult> PublishEvidenceAsync(
        ICustomsAdministrationWorkflowService service,
        Guid tenantId,
        Guid productId,
        string suffix)
    {
        var draft = await service.CreateDraftAsync(
            tenantId,
            Payload(productId, suffix),
            Actor($"editor-{suffix}", CustomsAdministrationRole.Editor),
            $"corr-{suffix}-1");
        var submitted = await service.SubmitForReviewAsync(
            tenantId,
            draft.CustomsEvidenceId!.Value,
            Actor($"editor-{suffix}", CustomsAdministrationRole.Editor),
            $"corr-{suffix}-2");
        Assert.True(submitted.Succeeded);
        var approved = await service.ApproveAsync(
            tenantId,
            draft.CustomsEvidenceId.Value,
            draft.ValidationChecksumSha256!,
            Actor($"approver-{suffix}", CustomsAdministrationRole.Approver),
            "approved",
            $"corr-{suffix}-3");
        Assert.True(approved.Succeeded);
        var published = await service.PublishAsync(
            tenantId,
            draft.CustomsEvidenceId.Value,
            Actor($"publisher-{suffix}", CustomsAdministrationRole.PublicationOperator),
            "published",
            $"corr-{suffix}-4");
        Assert.True(published.Succeeded);
        return published;
    }

    private static CustomsAdministrationWorkflowService CreateService(ApplicationDbContext db) =>
        new(db, NullLogger<CustomsAdministrationWorkflowService>.Instance);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static void SeedCountry(ApplicationDbContext db, string iso2, string iso3)
    {
        db.GeographicCountryProfiles.Add(new GeographicCountryProfile
        {
            Id = Guid.NewGuid(),
            CountryPlaceId = Guid.NewGuid(),
            Iso2 = iso2,
            Iso3 = iso3,
            DefaultLanguageTag = "en",
            PostalCodePolicy = GeographicPostalCodePolicy.Optional,
            RequiresStreetLine1 = false,
            AllowsCoordinates = false,
            RequiresCoordinateProvenance = false,
            OfflinePackageEnabled = false,
            IsActive = true,
            IntroducedCatalogReleaseId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private static CustomsAdministrationActor Actor(string actorId, params CustomsAdministrationRole[] roles) =>
        new(actorId, roles);

    private static CustomsWorkflowEvidencePayload Payload(Guid productId, string suffix = "") =>
        new(
            CustomsEvidenceSourceKind.Product,
            productId,
            productId,
            null,
            $"6204{suffix}",
            $"Cotton shirts {suffix}".Trim(),
            "EG",
            null,
            100m,
            "EGP",
            0.5m,
            "kg",
            "pcs",
            false,
            null,
            "ProductCustomsProfile",
            "{\"source\":\"test\"}");
}
