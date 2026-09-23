namespace PointOfSale.Tests.Architecture;

public sealed class CustomsEvidenceRuntimeFoundationTests
{
    [Fact]
    public void CustomsRuntimeFoundation_MustUseCarrierNeutralEntitiesOnly()
    {
        var entities = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Customs/CustomsEntities.cs");
        var migration = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs");

        foreach (var required in new[]
                 {
                     "CustomsEvidence",
                     "CustomsEvidenceVersion",
                     "ShipmentCustomsSnapshot",
                     "CustomsAdmissionAudit",
                     "CustomsEvidenceHistory",
                     "CustomsEvidenceFingerprint",
                     "ShipmentCustomsSnapshotLine"
                 })
        {
            Assert.Contains(required, entities, StringComparison.Ordinal);
            Assert.Contains(required, migration, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("exportDeclaration", entities, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DHL", entities, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FedEx", entities, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPS", entities, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE \"Products\"", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE \"Orders\"", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void Admission_MustFailClosedForRequiredCustomsEvidence()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Customs/CustomsEvidenceAdmissionService.cs");

        foreach (var failure in new[]
                 {
                     "MISSING_HS_CODE",
                     "MISSING_COMMODITY",
                     "MISSING_COUNTRY_OF_ORIGIN",
                     "MISSING_DECLARED_VALUE",
                     "MISSING_CURRENCY",
                     "INVALID_QUANTITY",
                     "INVALID_WEIGHT",
                     "INVALID_UNITS",
                     "UNSUPPORTED_COUNTRY",
                     "MALFORMED_EVIDENCE",
                     "CUSTOMS_EVIDENCE_NOT_ADMITTED",
                     "CUSTOMS_SNAPSHOT_MISSING"
                 })
        {
            Assert.Contains(failure, service, StringComparison.Ordinal);
        }

        Assert.Contains("JsonDocument.Parse(request.SourceEvidenceJson)", service, StringComparison.Ordinal);
        Assert.Contains("GeographicCountryProfiles", service, StringComparison.Ordinal);
    }

    [Fact]
    public void VersioningAndImmutability_MustBeAppendOnly()
    {
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Customs/CustomsEvidenceAdmissionService.cs");

        Assert.Contains("GuardAppendOnlyEvidence<CustomsEvidenceVersion>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<ShipmentCustomsSnapshot>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<ShipmentCustomsSnapshotLine>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<CustomsAdmissionAudit>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("CurrentVersionNumber += 1", service, StringComparison.Ordinal);
        Assert.Contains("EvidenceVersionCreated", service, StringComparison.Ordinal);
        Assert.Contains("IsRegistrationFrozen = true", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentRegistration_MustAcquireSnapshotBeforeAdapterCall_AndRecoveryMustReuseSqlSnapshot()
    {
        var carrierService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var customsService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Customs/CustomsEvidenceAdmissionService.cs");

        Assert.Contains("ResolveOrRestoreShipmentCustomsAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("CreateOrReuseShipmentSnapshotAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("createRequest.Customs = customsResolution.Customs", carrierService, StringComparison.Ordinal);
        Assert.Contains("adapter.CreateShipmentAsync(createRequest", carrierService, StringComparison.Ordinal);
        Assert.True(
            carrierService.IndexOf("ResolveOrRestoreShipmentCustomsAsync", StringComparison.Ordinal) <
            carrierService.IndexOf("adapter.CreateShipmentAsync(createRequest", StringComparison.Ordinal));

        Assert.Contains("Recovery reused SQL shipment customs snapshot only.", customsService, StringComparison.Ordinal);
        Assert.DoesNotContain("Products", ExtractMethod(customsService, "RecoverShipmentSnapshotAsync"), StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierMetadata", ExtractMethod(customsService, "RecoverShipmentSnapshotAsync"), StringComparison.Ordinal);
    }

    [Fact]
    public void CarrierNeutralDto_MustNotBeProviderSpecific()
    {
        var dto = CertificationSource.ReadRepoFile("PointOfSale.Core/DTOs/Customs/CarrierNeutralCustomsDto.cs");
        var adapterContract = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/IShippingCarrierAdapter.cs");

        Assert.Contains("CarrierNeutralCustomsDto", dto, StringComparison.Ordinal);
        Assert.Contains("HsCode", dto, StringComparison.Ordinal);
        Assert.Contains("CommodityDescription", dto, StringComparison.Ordinal);
        Assert.Contains("CountryOfOriginIso2", dto, StringComparison.Ordinal);
        Assert.Contains("DeclaredValue", dto, StringComparison.Ordinal);
        Assert.Contains("Customs { get; set; }", adapterContract, StringComparison.Ordinal);
        Assert.DoesNotContain("exportDeclaration", dto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dhl", dto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FedEx", dto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPS", dto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Customs2Report_MustRecordVerificationAndSuccessAnswers()
    {
        var report = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-2_CUSTOMS_EVIDENCE_RUNTIME_FOUNDATION.md");

        foreach (var heading in new[]
                 {
                     "Executive Summary",
                     "Runtime Model",
                     "SQL Model",
                     "Admission Pipeline",
                     "Versioning Model",
                     "Snapshot Model",
                     "Recovery Model",
                     "Audit Model",
                     "Regression Tests",
                     "Verification",
                     "Risks",
                     "Final Recommendation"
                 })
        {
            Assert.Contains($"## {heading}", report, StringComparison.Ordinal);
        }

        Assert.Contains("Reviewed ONLY Customs Runtime Foundation? **Yes.**", report, StringComparison.Ordinal);
        Assert.Contains("Carrier-neutral DTO implemented? **Yes.**", report, StringComparison.Ordinal);
        Assert.Contains("Recommend next phase: **Customs operational admission rollout and carrier-specific contract mapping certification.**", report, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {signature}");
        var next = source.IndexOf("\n    private", start + signature.Length, StringComparison.Ordinal);
        return next > start ? source[start..next] : source[start..];
    }
}
