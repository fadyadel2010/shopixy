namespace PointOfSale.Tests.Architecture;

public sealed class CanonicalCustomsAuthorityCertificationTests
{
    [Fact]
    public void Customs1Document_MustExistAndRemainArchitectureOnly()
    {
        var customs1 = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-1_CANONICAL_CUSTOMS_AUTHORITY.md");

        Assert.Contains("# CUSTOMS-1 - Canonical Customs Authority", customs1, StringComparison.Ordinal);
        Assert.Contains("Status: Architecture authority freeze", customs1, StringComparison.Ordinal);
        Assert.Contains("Scope: Carrier-neutral Customs Authority only", customs1, StringComparison.Ordinal);
        Assert.Contains("Runtime impact: none", customs1, StringComparison.Ordinal);
        Assert.Contains("Backend impact: none", customs1, StringComparison.Ordinal);
        Assert.Contains("Frontend impact: none", customs1, StringComparison.Ordinal);
        Assert.Contains("Carrier adapter impact: none", customs1, StringComparison.Ordinal);
        Assert.Contains("International shipment implementation impact: none", customs1, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomsPipeline_MustRemainCarrierNeutral()
    {
        var customs1 = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-1_CANONICAL_CUSTOMS_AUTHORITY.md");

        Assert.Contains("Product", customs1, StringComparison.Ordinal);
        Assert.Contains("-> Customs Evidence", customs1, StringComparison.Ordinal);
        Assert.Contains("-> Shipment Customs Snapshot", customs1, StringComparison.Ordinal);
        Assert.Contains("-> Carrier Neutral Customs DTO", customs1, StringComparison.Ordinal);
        Assert.Contains("-> Carrier Adapter", customs1, StringComparison.Ordinal);
        Assert.Contains("-> Provider", customs1, StringComparison.Ordinal);

        Assert.Contains("DHL `exportDeclaration`", customs1, StringComparison.Ordinal);
        Assert.Contains("FedEx customs", customs1, StringComparison.Ordinal);
        Assert.Contains("UPS customs", customs1, StringComparison.Ordinal);
        Assert.Contains("belong to later carrier adapter phases", customs1, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomsAuthorityMatrix_MustFreezeRequiredOwners()
    {
        var customs1 = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-1_CANONICAL_CUSTOMS_AUTHORITY.md");

        foreach (var requiredConcern in new[]
                 {
                     "HS Code",
                     "Commodity Description",
                     "Country Of Manufacture",
                     "Country Of Origin",
                     "Declared Customs Value",
                     "Quantity",
                     "Net Weight",
                     "Gross Weight",
                     "Units",
                     "Export Classification",
                     "Dangerous Goods flag",
                     "Commercial Invoice Evidence",
                     "Customs Snapshot",
                     "SQL Truth",
                     "Runtime Authority"
                 })
        {
            Assert.Contains(requiredConcern, customs1, StringComparison.Ordinal);
        }

        Assert.Contains("Who owns HS Code? | Customs Evidence", customs1, StringComparison.Ordinal);
        Assert.Contains("Who owns Country Of Origin? | Customs Evidence", customs1, StringComparison.Ordinal);
        Assert.Contains("Who owns Customs Value? | Customs Evidence", customs1, StringComparison.Ordinal);
        Assert.Contains("Who owns Commodity? | Customs Evidence", customs1, StringComparison.Ordinal);
        Assert.Contains("Who owns Customs Snapshot? | Shipment owns", customs1, StringComparison.Ordinal);
        Assert.Contains("Who owns Export Evidence? | Customs Platform owns", customs1, StringComparison.Ordinal);
        Assert.Contains("Who owns SQL Truth? | PostgreSQL Customs Evidence", customs1, StringComparison.Ordinal);
        Assert.Contains("Who owns Runtime Authority? | Customs admission", customs1, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomsSnapshot_MustBeShipmentOwnedAndImmutableAfterRegistrationBegins()
    {
        var customs1 = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-1_CANONICAL_CUSTOMS_AUTHORITY.md");

        Assert.Contains("Shipment owns the immutable shipment customs snapshot", customs1, StringComparison.Ordinal);
        Assert.Contains("Once shipment registration begins, the Shipment Customs Snapshot is immutable.", customs1, StringComparison.Ordinal);
        Assert.Contains("Product edits cannot rewrite historical shipment customs.", customs1, StringComparison.Ordinal);
        Assert.Contains("Carrier retry and recovery reuse the existing snapshot.", customs1, StringComparison.Ordinal);
        Assert.Contains("Provider responses, labels, tracking, invoice PDFs, webhooks, and adapter", customs1, StringComparison.Ordinal);
        Assert.Contains("cannot mutate the snapshot", customs1, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomsRecovery_MustReuseSnapshotAndRejectCarrierOrLiveProductRebuild()
    {
        var customs1 = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-1_CANONICAL_CUSTOMS_AUTHORITY.md");

        Assert.Contains("Recovery must reuse the Shipment Customs Snapshot when it exists.", customs1, StringComparison.Ordinal);
        Assert.Contains("rebuild customs from current Product rows", customs1, StringComparison.Ordinal);
        Assert.Contains("carrier metadata, provider responses", customs1, StringComparison.Ordinal);
        Assert.Contains("Redis", customs1, StringComparison.Ordinal);
        Assert.Contains("Mongo", customs1, StringComparison.Ordinal);
        Assert.Contains("Elasticsearch", customs1, StringComparison.Ordinal);
        Assert.Contains("browser state", customs1, StringComparison.Ordinal);
        Assert.Contains("If the snapshot is missing, malformed, partial", customs1, StringComparison.Ordinal);
        Assert.Contains("recovery fails closed", customs1, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomsFailClosedRules_MustRejectIncompleteOrNonAdmittedAuthority()
    {
        var customs1 = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-1_CANONICAL_CUSTOMS_AUTHORITY.md");

        foreach (var failure in new[]
                 {
                     "HS Code is missing",
                     "country of origin is missing",
                     "commodity description is missing",
                     "declared customs value is missing",
                     "quantity is missing, zero, negative",
                     "snapshot is incomplete",
                     "authority is not admitted by Customs",
                     "a carrier adapter attempts to supply or overwrite customs authority"
                 })
        {
            Assert.Contains(failure, customs1, StringComparison.Ordinal);
        }

        Assert.Contains("no provider call", customs1, StringComparison.Ordinal);
        Assert.Contains("no label request", customs1, StringComparison.Ordinal);
        Assert.Contains("no successful carrier", customs1, StringComparison.Ordinal);
        Assert.Contains("registration", customs1, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomsArtifacts_MustRecordExplicitSuccessAnswers()
    {
        var customs1 = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-1_CANONICAL_CUSTOMS_AUTHORITY.md");

        Assert.Contains("Reviewed ONLY Customs Authority? **Yes.**", customs1, StringComparison.Ordinal);
        Assert.Contains("Runtime modified? **No.**", customs1, StringComparison.Ordinal);
        Assert.Contains("Backend modified? **No runtime backend files modified.**", customs1, StringComparison.Ordinal);
        Assert.Contains("Frontend modified? **No.**", customs1, StringComparison.Ordinal);
        Assert.Contains("Carrier owns Customs? **NO.**", customs1, StringComparison.Ordinal);
        Assert.Contains("Snapshot immutable? **Yes, once shipment registration begins.**", customs1, StringComparison.Ordinal);
        Assert.Contains("Recommend next phase: **CUSTOMS-2 - Customs Evidence SQL Model and Admission Runtime.**", customs1, StringComparison.Ordinal);
    }
}
