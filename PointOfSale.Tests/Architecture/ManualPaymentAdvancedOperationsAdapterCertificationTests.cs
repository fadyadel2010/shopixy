using System.Text.Json;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentAdvancedOperationsAdapterCertificationTests
{
    [Fact]
    public void CancelPendingPayment_MustTranslateOnlyToManualProviderEvidence()
    {
        var result = new ManualPaymentAdvancedOperationsAdapter().Translate(CreateAction(
            ManualPaymentAdvancedOperation.CancelPendingPayment,
            new Dictionary<string, string>
            {
                [ManualPaymentAdvancedOperationMetadataKeys.CurrentProviderStatus] = "PendingManualPayment"
            }));

        Assert.True(result.Succeeded);
        Assert.False(result.Rejected);
        Assert.Equal(ManualPaymentProviderAdvancedOperation.CancelPendingManualPayment, result.Request!.ProviderOperation);
        Assert.Equal(EvidenceStatus.Recorded, result.Evidence!.Status);
        Assert.Equal(EvidenceType.StatusInquiry, result.Evidence.Type);
        Assert.Equal("CancelPendingPayment", result.Evidence.Metadata.Values["operation"]);
        Assert.Equal("CancelPendingManualPayment", result.Evidence.Metadata.Values["provider_operation"]);
        Assert.Equal("Cancelled", result.Evidence.Metadata.Values["canonical_status"]);
        Assert.Equal(bool.TrueString, result.Evidence.Metadata.Values["translation_only"]);
    }

    [Fact]
    public void ExpirePendingPayment_MustTranslateOnlyToManualProviderEvidence()
    {
        var result = new ManualPaymentAdvancedOperationsAdapter().Translate(CreateAction(
            ManualPaymentAdvancedOperation.ExpirePendingPayment,
            new Dictionary<string, string>
            {
                [ManualPaymentAdvancedOperationMetadataKeys.CurrentProviderStatus] = "AwaitingCustomer"
            }));

        Assert.True(result.Succeeded);
        Assert.Equal(ManualPaymentProviderAdvancedOperation.ExpirePendingManualPayment, result.Request!.ProviderOperation);
        Assert.Equal(EvidenceStatus.Recorded, result.Evidence!.Status);
        Assert.Equal("Expired", result.Evidence.Metadata.Values["canonical_status"]);
    }

    [Fact]
    public void StatusInquiryOperations_MustTranslateSuppliedFactsWithoutLifecycleMutation()
    {
        var paymentStatus = new ManualPaymentAdvancedOperationsAdapter().Translate(CreateAction(
            ManualPaymentAdvancedOperation.PaymentStatusInquiry,
            new Dictionary<string, string>
            {
                [ManualPaymentAdvancedOperationMetadataKeys.CurrentProviderStatus] = "AwaitingCustomer"
            }));
        var evidenceStatus = new ManualPaymentAdvancedOperationsAdapter().Translate(CreateAction(
            ManualPaymentAdvancedOperation.EvidenceStatusInquiry,
            new Dictionary<string, string>
            {
                [ManualPaymentAdvancedOperationMetadataKeys.EvidenceReference] = "proof-123",
                [ManualPaymentAdvancedOperationMetadataKeys.CurrentEvidenceStatus] = "PendingReview"
            }));

        Assert.True(paymentStatus.Succeeded);
        Assert.True(evidenceStatus.Succeeded);
        Assert.Equal("AwaitingCustomer", paymentStatus.Evidence!.Metadata.Values["canonical_status"]);
        Assert.Equal("PendingReview", evidenceStatus.Evidence!.Metadata.Values["canonical_status"]);
        Assert.Equal("proof-123", evidenceStatus.Evidence.Metadata.Values["evidence_reference"]);
        Assert.Equal(EvidenceType.Receipt, evidenceStatus.Evidence.Type);
    }

    [Fact]
    public void CapabilityMatrix_MustDocumentSupportedNotSupportedAndOutOfScopeOperations()
    {
        var matrix = ManualPaymentAdvancedOperationCapabilityMatrix.Operations;
        var discovery = new ManualPaymentAdvancedOperationsAdapter().Translate(
            CreateAction(ManualPaymentAdvancedOperation.CapabilityDiscovery));

        Assert.True(matrix[ManualPaymentAdvancedOperation.CancelPendingPayment].Supported);
        Assert.True(matrix[ManualPaymentAdvancedOperation.ExpirePendingPayment].Supported);
        Assert.True(matrix[ManualPaymentAdvancedOperation.PaymentStatusInquiry].Supported);
        Assert.True(matrix[ManualPaymentAdvancedOperation.EvidenceStatusInquiry].Supported);
        Assert.True(matrix[ManualPaymentAdvancedOperation.CapabilityDiscovery].Supported);
        Assert.False(matrix[ManualPaymentAdvancedOperation.WithdrawSubmittedEvidence].Supported);
        Assert.Equal(ManualPaymentAdvancedOperationScope.NotSupported, matrix[ManualPaymentAdvancedOperation.WithdrawSubmittedEvidence].Scope);
        Assert.False(matrix[ManualPaymentAdvancedOperation.Refund].Supported);
        Assert.False(matrix[ManualPaymentAdvancedOperation.Capture].Supported);
        Assert.Equal(ManualPaymentAdvancedOperationScope.OutOfScope, matrix[ManualPaymentAdvancedOperation.Refund].Scope);
        Assert.Equal(ManualPaymentAdvancedOperationScope.OutOfScope, matrix[ManualPaymentAdvancedOperation.ApprovalExecution].Scope);
        Assert.True(discovery.Succeeded);
        Assert.Contains("CancelPendingPayment", discovery.Evidence!.Metadata.Values["supported_operations"], StringComparison.Ordinal);
        Assert.Contains("WithdrawSubmittedEvidence", discovery.Evidence.Metadata.Values["unsupported_operations"], StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedOperation_MustFailClosedWithRejectedEvidenceAndCanonicalProviderError()
    {
        var result = new ManualPaymentAdvancedOperationsAdapter().Translate(
            CreateAction(ManualPaymentAdvancedOperation.WithdrawSubmittedEvidence));

        Assert.False(result.Succeeded);
        Assert.True(result.Rejected);
        Assert.NotNull(result.Error);
        Assert.Equal(CanonicalProviderErrorType.NotSupported, result.Error!.Type);
        Assert.Equal("MANUAL_ADVANCED_OPERATION_UNSUPPORTED", result.Error.Code);
        Assert.Equal(EvidenceStatus.Rejected, result.Evidence!.Status);
        Assert.Equal("MANUAL_ADVANCED_OPERATION_UNSUPPORTED", result.Evidence.Metadata.Values["provider_error_code"]);
        Assert.Equal("Rejected", result.Evidence.Metadata.Values["canonical_status"]);
    }

    [Fact]
    public void InvalidStateTransition_MustFailClosedWithRejectedEvidence()
    {
        var result = new ManualPaymentAdvancedOperationsAdapter().Translate(CreateAction(
            ManualPaymentAdvancedOperation.CancelPendingPayment,
            new Dictionary<string, string>
            {
                [ManualPaymentAdvancedOperationMetadataKeys.CurrentProviderStatus] = "Completed"
            }));

        Assert.False(result.Succeeded);
        Assert.True(result.Rejected);
        Assert.Null(result.Error);
        Assert.Equal(EvidenceStatus.Rejected, result.Evidence!.Status);
        Assert.Equal("MANUAL_ADVANCED_OPERATION_INVALID_STATE", result.Evidence.Metadata.Values["provider_error_code"]);
        Assert.Equal("InvalidStateTransition", result.Evidence.Metadata.Values["provider_error_reason"]);
    }

    [Fact]
    public void ConfigurationMismatch_MustReturnCanonicalProviderErrorWithoutEvidence()
    {
        var result = new ManualPaymentAdvancedOperationsAdapter().Translate(CreateAction(
            ManualPaymentAdvancedOperation.PaymentStatusInquiry,
            new Dictionary<string, string>
            {
                [ManualPaymentAdvancedOperationMetadataKeys.ConfiguredProviderName] = "manual_wallet"
            }));

        Assert.False(result.Succeeded);
        Assert.False(result.Rejected);
        Assert.Null(result.Evidence);
        Assert.Equal(CanonicalProviderErrorType.Configuration, result.Error!.Type);
        Assert.Equal("MANUAL_ADVANCED_OPERATION_CONFIGURATION_MISMATCH", result.Error.Code);
        Assert.Equal("manual_payment", result.Error.Metadata["provider"]);
    }

    [Fact]
    public void Serialization_MustRoundTripEvidenceWithoutManualWorkflowTypes()
    {
        var result = new ManualPaymentAdvancedOperationsAdapter().Translate(CreateAction(
            ManualPaymentAdvancedOperation.PaymentStatusInquiry,
            new Dictionary<string, string>
            {
                [ManualPaymentAdvancedOperationMetadataKeys.CurrentProviderStatus] = "AwaitingCustomer"
            }));
        var json = JsonSerializer.Serialize(result.Evidence);
        var roundTrip = JsonSerializer.Deserialize<ProviderEvidence>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(result.Evidence!.Id, roundTrip.Id);
        Assert.DoesNotContain("PaymentLifecycleService", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepManualAdvancedAdapterTranslationOnly()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentAdvancedOperationsAdapter.cs");
        var runtimeFiles = Directory.GetFiles(Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"), "*.cs", SearchOption.AllDirectories);

        Assert.Contains("IManualPaymentAdvancedOperationsAdapter", adapter, StringComparison.Ordinal);
        Assert.Contains("ProviderEvidenceFactory.Create", adapter, StringComparison.Ordinal);
        Assert.Contains("CanonicalProviderError", adapter, StringComparison.Ordinal);
        Assert.Contains("CapabilityMatrix", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IConfiguration", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IOptions", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", adapter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SaveChanges", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("RefundService", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureAsync", adapter, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("ManualPaymentAdvancedOperationsAdapter", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ManualPaymentAdvancedOperation", source, StringComparison.Ordinal);
            Assert.DoesNotContain("manual_payment", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static PaymentAction CreateAction(
        ManualPaymentAdvancedOperation operation,
        IReadOnlyDictionary<string, string>? extraMetadata = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ManualPaymentAdvancedOperationMetadataKeys.Operation] = operation.ToString(),
            [ManualPaymentAdvancedOperationMetadataKeys.ProviderReference] = "manual-payment-reference",
            [ManualPaymentAdvancedOperationMetadataKeys.OperationReference] = "manual-operation-reference"
        };

        foreach (var (key, value) in extraMetadata ?? new Dictionary<string, string>())
        {
            values[key] = value;
        }

        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.StatusRefresh,
            PaymentActionStatus.Completed,
            new ActionReference("manual-advanced-operation"),
            new ActionCorrelationId("manual-advanced-correlation"),
            DateTimeOffset.Parse("2026-07-16T10:01:00Z"),
            DateTimeOffset.Parse("2026-07-16T10:02:00Z"),
            metadata: new PaymentActionMetadata(values));
    }

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
