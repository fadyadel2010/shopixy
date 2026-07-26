using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentEvidenceAdapterCertificationTests
{
    [Fact]
    public void EvidenceMapping_MustTranslateSubmittedProofToPendingManualReviewEvidence()
    {
        var result = new ManualPaymentEvidenceAdapter().Translate(CreateSubmission());

        Assert.True(result.Succeeded);
        Assert.False(result.Rejected);
        Assert.Null(result.Error);
        Assert.NotNull(result.Evidence);
        Assert.Equal(EvidenceType.Receipt, result.Evidence!.Type);
        Assert.Equal(EvidenceStatus.PendingReview, result.Evidence.Status);
        Assert.Equal("manual_payment", result.Evidence.Source.Value);
        Assert.Equal("manual_payment:evidence:proof-123", result.Evidence.Reference.Value);
        Assert.Equal("manual_payment:proof:proof-123", result.Evidence.PayloadReference.Value);
        Assert.Equal("EvidenceSubmitted", result.Evidence.Metadata.Values["provider_status"]);
        Assert.Equal("ManualReview", result.Evidence.Metadata.Values["canonical_status"]);
        Assert.Equal("EvidenceSubmitted", result.Evidence.Metadata.Values["evidence_outcome"]);
        Assert.Equal("vodafone_cash", result.Evidence.Metadata.Values["channel"]);
        Assert.Equal("manual-v1", result.Evidence.Metadata.Values["instruction_version"]);
    }

    [Fact]
    public void ReferenceMapping_MustRecordPresentOrMissingReferenceWithoutApprovingPayment()
    {
        var present = new ManualPaymentEvidenceAdapter().Translate(CreateSubmission(referenceRequired: true, referenceNumber: " REF-123 "));
        var optionalMissing = new ManualPaymentEvidenceAdapter().Translate(CreateSubmission(referenceRequired: false, referenceNumber: null));

        Assert.Equal("ReferencePresent", present.Evidence!.Metadata.Values["reference_status"]);
        Assert.Equal("REF-123", present.Evidence.Metadata.Values["reference_number"]);
        Assert.Equal("ReferenceMissing", optionalMissing.Evidence!.Metadata.Values["reference_status"]);
        Assert.Equal(string.Empty, optionalMissing.Evidence.Metadata.Values["reference_number"]);
        Assert.Equal(EvidenceStatus.PendingReview, present.Evidence.Status);
        Assert.Equal(EvidenceStatus.PendingReview, optionalMissing.Evidence.Status);
    }

    [Fact]
    public void ProofMapping_MustRecordProofIdentifierAndMetadataOnly()
    {
        var result = new ManualPaymentEvidenceAdapter().Translate(CreateSubmission());

        Assert.Equal("ProofPresent", result.Evidence!.Metadata.Values["proof_status"]);
        Assert.Equal("proof-123", result.Evidence.Metadata.Values["proof_identifier"]);
        Assert.Equal("image/png", result.Evidence.Metadata.Values["proof_metadata_content_type"]);
        Assert.Equal("4096", result.Evidence.Metadata.Values["proof_metadata_size_bytes"]);
    }

    [Fact]
    public void MissingProof_MustReturnRejectedProviderEvidence()
    {
        var result = new ManualPaymentEvidenceAdapter().Translate(CreateSubmission(proofIdentifier: null, proofMetadata: new ManualPaymentProofMetadata()));

        AssertRejected(result, "MANUAL_EVIDENCE_PROOF_MISSING", "ProofMissing");
    }

    [Fact]
    public void MissingRequiredReference_MustReturnRejectedProviderEvidence()
    {
        var result = new ManualPaymentEvidenceAdapter().Translate(CreateSubmission(referenceRequired: true, referenceNumber: null));

        AssertRejected(result, "MANUAL_EVIDENCE_REFERENCE_MISSING", "ReferenceMissing");
    }

    [Fact]
    public void UnsupportedChannel_MustReturnRejectedProviderEvidence()
    {
        var result = new ManualPaymentEvidenceAdapter().Translate(CreateSubmission(channel: ManualPaymentChannel.Instapay));

        AssertRejected(result, "MANUAL_EVIDENCE_CHANNEL_UNSUPPORTED", "UnsupportedChannel");
    }

    [Fact]
    public void ExpiredSubmission_MustRemainTranslationOnlyEvidence()
    {
        var result = new ManualPaymentEvidenceAdapter().Translate(CreateSubmission(
            submittedAtUtc: DateTimeOffset.Parse("2026-07-16T12:00:00Z"),
            expiresAtUtc: DateTimeOffset.Parse("2026-07-16T10:00:00Z")));

        Assert.True(result.Succeeded);
        Assert.Equal(EvidenceStatus.PendingReview, result.Evidence!.Status);
        Assert.Equal("ExpiredSubmission", result.Evidence.Metadata.Values["submission_status"]);
        Assert.Equal("ExpiredSubmission", result.Evidence.Metadata.Values["evidence_outcome"]);
    }

    [Fact]
    public void ConfigurationMismatch_MustReturnCanonicalProviderError()
    {
        var result = new ManualPaymentEvidenceAdapter().Translate(CreateSubmission(configuredProviderName: "manual_wallet"));

        Assert.False(result.Succeeded);
        Assert.False(result.Rejected);
        Assert.Null(result.Evidence);
        Assert.NotNull(result.Error);
        Assert.Equal(CanonicalProviderErrorType.Configuration, result.Error!.Type);
        Assert.Equal("MANUAL_EVIDENCE_CONFIGURATION_MISMATCH", result.Error.Code);
        Assert.Equal("manual_payment", result.Error.Metadata["provider"]);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepEvidenceAdapterTranslationOnly()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var source = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentEvidenceAdapter.cs");
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.Contains("IManualPaymentEvidenceAdapter", source, StringComparison.Ordinal);
        Assert.Contains("ProviderEvidenceFactory.Create", source, StringComparison.Ordinal);
        Assert.Contains("CanonicalProviderError.Configuration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SaveChanges", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IConfiguration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FileStream", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Blob", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Ocr", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Approve", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RejectPayment", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReviewWorkflow", source, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var runtimeSource = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("ManualPaymentEvidenceAdapter", runtimeSource, StringComparison.Ordinal);
            Assert.DoesNotContain("manual_payment", runtimeSource, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("VodafoneCash", runtimeSource, StringComparison.Ordinal);
            Assert.DoesNotContain("Instapay", runtimeSource, StringComparison.Ordinal);
        }
    }

    private static void AssertRejected(ManualPaymentEvidenceAdapterResult result, string code, string expectedFact)
    {
        Assert.False(result.Succeeded);
        Assert.True(result.Rejected);
        Assert.Null(result.Error);
        Assert.NotNull(result.Evidence);
        Assert.Equal(EvidenceStatus.Rejected, result.Evidence!.Status);
        Assert.Equal("Rejected", result.Evidence.Metadata.Values["provider_status"]);
        Assert.Equal("Rejected", result.Evidence.Metadata.Values["canonical_status"]);
        Assert.Equal("RejectedProviderEvidence", result.Evidence.Metadata.Values["evidence_outcome"]);
        Assert.Equal(code, result.Evidence.Metadata.Values["provider_error_code"]);
        Assert.Contains(expectedFact, result.Evidence.Metadata.Values.Values);
    }

    private static ManualPaymentEvidenceSubmission CreateSubmission(
        ManualPaymentChannel channel = ManualPaymentChannel.VodafoneCash,
        bool referenceRequired = true,
        string? referenceNumber = "REF-123",
        string? proofIdentifier = "proof-123",
        ManualPaymentProofMetadata? proofMetadata = null,
        DateTimeOffset? submittedAtUtc = null,
        DateTimeOffset? expiresAtUtc = null,
        string configuredProviderName = ManualPaymentProviderReference.ProviderName) =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            new PaymentActionId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            new ActionCorrelationId("manual-evidence-correlation"),
            channel,
            new[] { ManualPaymentChannel.VodafoneCash },
            referenceRequired,
            referenceNumber,
            proofIdentifier,
            proofMetadata ?? new ManualPaymentProofMetadata(new Dictionary<string, string>
            {
                ["content_type"] = "image/png",
                ["size_bytes"] = "4096"
            }),
            submittedAtUtc ?? DateTimeOffset.Parse("2026-07-16T09:00:00Z"),
            expiresAtUtc ?? DateTimeOffset.Parse("2026-07-17T09:00:00Z"),
            "customer note supplied",
            "manual-v1",
            configuredProviderName);

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
