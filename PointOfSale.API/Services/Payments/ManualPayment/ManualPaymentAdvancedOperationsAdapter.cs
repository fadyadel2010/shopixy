using System.Collections.ObjectModel;
using System.Globalization;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.API.Services.Payments.ManualPayment;

public interface IManualPaymentAdvancedOperationsAdapter
{
    ManualPaymentAdvancedOperationResult Translate(PaymentAction action);
}

public sealed class ManualPaymentAdvancedOperationsAdapter : IManualPaymentAdvancedOperationsAdapter
{
    public ManualPaymentAdvancedOperationResult Translate(PaymentAction action)
    {
        try
        {
            var request = ManualPaymentAdvancedOperationFactory.Create(action);
            if (!ManualPaymentAdvancedOperationCapabilityMatrix.IsSupported(request.Operation))
            {
                return ManualPaymentAdvancedOperationResult.CreateRejected(
                    request,
                    ManualPaymentAdvancedOperationEvidenceFactory.Rejected(
                        action,
                        request,
                        "MANUAL_ADVANCED_OPERATION_UNSUPPORTED",
                        "UnsupportedOperation"),
                    CanonicalProviderError.NotSupported(
                        ManualPaymentProviderReference.ProviderName,
                        "MANUAL_ADVANCED_OPERATION_UNSUPPORTED",
                        "Manual Payment advanced operation is not supported."));
            }

            if (!ManualPaymentAdvancedOperationStatePolicy.IsAllowed(request))
            {
                return ManualPaymentAdvancedOperationResult.CreateRejected(
                    request,
                    ManualPaymentAdvancedOperationEvidenceFactory.Rejected(
                        action,
                        request,
                        "MANUAL_ADVANCED_OPERATION_INVALID_STATE",
                        "InvalidStateTransition"));
            }

            return ManualPaymentAdvancedOperationResult.Success(
                request,
                ManualPaymentAdvancedOperationEvidenceFactory.Recorded(action, request));
        }
        catch (ManualPaymentAdvancedConfigurationMismatchException exception)
        {
            return ManualPaymentAdvancedOperationResult.Failed(
                CanonicalProviderError.Configuration(
                    ManualPaymentProviderReference.ProviderName,
                    exception.Code,
                    exception.Message));
        }
        catch (ArgumentException exception)
        {
            return ManualPaymentAdvancedOperationResult.Failed(
                CanonicalProviderError.Validation(
                    ManualPaymentProviderReference.ProviderName,
                    "MANUAL_ADVANCED_OPERATION_REQUEST_INVALID",
                    exception.Message));
        }
    }
}

public static class ManualPaymentAdvancedOperationFactory
{
    public static ManualPaymentAdvancedOperationRequest Create(PaymentAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        RequiredGuid(action.Id.Value, nameof(action.Id));
        RequiredText(action.CorrelationId.Value, nameof(action.CorrelationId));

        var operation = ManualPaymentAdvancedOperationMetadata.ResolveOperation(action);
        var metadata = ManualPaymentAdvancedOperationMetadata.FromAction(action, operation).Values;
        var configuredProviderName = Optional(metadata, ManualPaymentAdvancedOperationMetadataKeys.ConfiguredProviderName);
        if (!string.IsNullOrWhiteSpace(configuredProviderName) &&
            !string.Equals(configuredProviderName, ManualPaymentProviderReference.ProviderName, StringComparison.Ordinal))
        {
            throw new ManualPaymentAdvancedConfigurationMismatchException(
                "MANUAL_ADVANCED_OPERATION_CONFIGURATION_MISMATCH",
                "Manual Payment advanced operation configuration does not match the Manual Payment provider.");
        }

        return new ManualPaymentAdvancedOperationRequest(
            operation,
            MapProviderOperation(operation),
            Required(metadata, ManualPaymentAdvancedOperationMetadataKeys.ProviderReference),
            Optional(metadata, ManualPaymentAdvancedOperationMetadataKeys.EvidenceReference),
            Optional(metadata, ManualPaymentAdvancedOperationMetadataKeys.CurrentProviderStatus),
            Optional(metadata, ManualPaymentAdvancedOperationMetadataKeys.CurrentEvidenceStatus),
            Optional(metadata, ManualPaymentAdvancedOperationMetadataKeys.OperationReference) ?? action.Reference.Value,
            metadata);
    }

    private static ManualPaymentProviderAdvancedOperation MapProviderOperation(ManualPaymentAdvancedOperation operation) => operation switch
    {
        ManualPaymentAdvancedOperation.CancelPendingPayment => ManualPaymentProviderAdvancedOperation.CancelPendingManualPayment,
        ManualPaymentAdvancedOperation.ExpirePendingPayment => ManualPaymentProviderAdvancedOperation.ExpirePendingManualPayment,
        ManualPaymentAdvancedOperation.PaymentStatusInquiry => ManualPaymentProviderAdvancedOperation.PaymentStatusInquiry,
        ManualPaymentAdvancedOperation.EvidenceStatusInquiry => ManualPaymentProviderAdvancedOperation.EvidenceStatusInquiry,
        ManualPaymentAdvancedOperation.CapabilityDiscovery => ManualPaymentProviderAdvancedOperation.CapabilityDiscovery,
        ManualPaymentAdvancedOperation.WithdrawSubmittedEvidence => ManualPaymentProviderAdvancedOperation.Unsupported,
        ManualPaymentAdvancedOperation.Refund => ManualPaymentProviderAdvancedOperation.Unsupported,
        ManualPaymentAdvancedOperation.PartialRefund => ManualPaymentProviderAdvancedOperation.Unsupported,
        ManualPaymentAdvancedOperation.Capture => ManualPaymentProviderAdvancedOperation.Unsupported,
        ManualPaymentAdvancedOperation.Settlement => ManualPaymentProviderAdvancedOperation.Unsupported,
        ManualPaymentAdvancedOperation.ApprovalExecution => ManualPaymentProviderAdvancedOperation.Unsupported,
        ManualPaymentAdvancedOperation.ReviewExecution => ManualPaymentProviderAdvancedOperation.Unsupported,
        _ => ManualPaymentProviderAdvancedOperation.Unsupported
    };

    private static string Required(IReadOnlyDictionary<string, string> values, string key)
    {
        var value = Optional(values, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Manual Payment advanced operation field '{key}' is required.");
        }

        return value;
    }

    private static string? Optional(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static void RequiredGuid(Guid value, string fieldName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"Manual Payment advanced operation field '{fieldName}' is required.");
        }
    }

    private static void RequiredText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Manual Payment advanced operation field '{fieldName}' is required.");
        }
    }
}

public static class ManualPaymentAdvancedOperationEvidenceFactory
{
    public static ProviderEvidence Recorded(PaymentAction action, ManualPaymentAdvancedOperationRequest request)
    {
        var canonicalStatus = request.Operation switch
        {
            ManualPaymentAdvancedOperation.CancelPendingPayment => "Cancelled",
            ManualPaymentAdvancedOperation.ExpirePendingPayment => "Expired",
            ManualPaymentAdvancedOperation.PaymentStatusInquiry => NormalizeStatus(request.CurrentProviderStatus, "AwaitingCustomer"),
            ManualPaymentAdvancedOperation.EvidenceStatusInquiry => NormalizeStatus(request.CurrentEvidenceStatus, "PendingReview"),
            ManualPaymentAdvancedOperation.CapabilityDiscovery => "CapabilityDiscovery",
            _ => "Recorded"
        };

        return Create(action, request, EvidenceStatus.Recorded, canonicalStatus, null, null);
    }

    public static ProviderEvidence Rejected(
        PaymentAction action,
        ManualPaymentAdvancedOperationRequest request,
        string code,
        string reason)
    {
        return Create(action, request, EvidenceStatus.Rejected, "Rejected", code, reason);
    }

    private static ProviderEvidence Create(
        PaymentAction action,
        ManualPaymentAdvancedOperationRequest request,
        EvidenceStatus status,
        string canonicalStatus,
        string? errorCode,
        string? errorReason)
    {
        var reference = string.Concat(
            "manual_payment:advanced:",
            request.Operation.ToString(),
            ":",
            request.ProviderReference,
            status == EvidenceStatus.Rejected ? ":rejected" : string.Empty);
        var hashSource = string.Join(
            "|",
            action.Id.Value.ToString("N"),
            action.CorrelationId.Value,
            request.Operation,
            request.ProviderOperation,
            request.ProviderReference,
            request.EvidenceReference ?? string.Empty,
            status,
            canonicalStatus,
            errorCode ?? string.Empty);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = ManualPaymentProviderReference.ProviderName,
            ["adapter"] = nameof(ManualPaymentAdvancedOperationsAdapter),
            ["operation"] = request.Operation.ToString(),
            ["provider_operation"] = request.ProviderOperation.ToString(),
            ["provider_reference"] = request.ProviderReference,
            ["operation_reference"] = request.OperationReference,
            ["provider_status"] = status == EvidenceStatus.Rejected ? "Rejected" : request.ProviderOperation.ToString(),
            ["canonical_status"] = canonicalStatus,
            ["translation_only"] = bool.TrueString,
            ["supported"] = ManualPaymentAdvancedOperationCapabilityMatrix.IsSupported(request.Operation).ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(request.EvidenceReference))
        {
            metadata["evidence_reference"] = request.EvidenceReference!;
        }

        if (!string.IsNullOrWhiteSpace(request.CurrentProviderStatus))
        {
            metadata["current_provider_status"] = request.CurrentProviderStatus!;
        }

        if (!string.IsNullOrWhiteSpace(request.CurrentEvidenceStatus))
        {
            metadata["current_evidence_status"] = request.CurrentEvidenceStatus!;
        }

        if (request.Operation == ManualPaymentAdvancedOperation.CapabilityDiscovery)
        {
            metadata["supported_operations"] = string.Join(",", ManualPaymentAdvancedOperationCapabilityMatrix.SupportedOperations.Select(item => item.ToString()).OrderBy(item => item, StringComparer.Ordinal));
            metadata["unsupported_operations"] = string.Join(",", ManualPaymentAdvancedOperationCapabilityMatrix.UnsupportedOperations.Select(item => item.ToString()).OrderBy(item => item, StringComparer.Ordinal));
        }

        if (errorCode is not null)
        {
            metadata["provider_error_code"] = errorCode;
            metadata["provider_error_reason"] = errorReason ?? "Rejected";
        }

        return ProviderEvidenceFactory.Create(
            new EvidenceId(ManualPaymentProviderReference.CreateDeterministicId("manual-advanced-operation", hashSource)),
            action.Id,
            request.Operation == ManualPaymentAdvancedOperation.EvidenceStatusInquiry
                ? EvidenceType.Receipt
                : EvidenceType.StatusInquiry,
            status,
            new EvidenceReference(reference),
            new EvidenceSource(ManualPaymentProviderReference.ProviderName),
            new EvidenceTimestamp(action.LastUpdatedUtc),
            new EvidenceHash(ManualPaymentProviderReference.Hash(hashSource)),
            new ProviderPayloadReference(reference),
            action.CorrelationId,
            new EvidenceMetadata(metadata));
    }

    private static string NormalizeStatus(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}

public static class ManualPaymentAdvancedOperationCapabilityMatrix
{
    public static IReadOnlyDictionary<ManualPaymentAdvancedOperation, ManualPaymentAdvancedOperationCapability> Operations { get; } =
        new ReadOnlyDictionary<ManualPaymentAdvancedOperation, ManualPaymentAdvancedOperationCapability>(
            new Dictionary<ManualPaymentAdvancedOperation, ManualPaymentAdvancedOperationCapability>
            {
                [ManualPaymentAdvancedOperation.CancelPendingPayment] = new(true, ManualPaymentAdvancedOperationScope.Supported, "CancelPendingManualPayment", "Translates a provider-local pending-payment cancellation fact only."),
                [ManualPaymentAdvancedOperation.ExpirePendingPayment] = new(true, ManualPaymentAdvancedOperationScope.Supported, "ExpirePendingManualPayment", "Translates a provider-local pending-payment expiration fact only."),
                [ManualPaymentAdvancedOperation.PaymentStatusInquiry] = new(true, ManualPaymentAdvancedOperationScope.Supported, "PaymentStatusInquiry", "Translates supplied provider-model payment status facts only."),
                [ManualPaymentAdvancedOperation.EvidenceStatusInquiry] = new(true, ManualPaymentAdvancedOperationScope.Supported, "EvidenceStatusInquiry", "Translates supplied provider-model evidence status facts only."),
                [ManualPaymentAdvancedOperation.CapabilityDiscovery] = new(true, ManualPaymentAdvancedOperationScope.Supported, "CapabilityDiscovery", "Returns the Manual Payment advanced-operation capability matrix as evidence."),
                [ManualPaymentAdvancedOperation.WithdrawSubmittedEvidence] = new(false, ManualPaymentAdvancedOperationScope.NotSupported, "Unsupported", "Manual Payment has no provider-owned evidence withdrawal capability in the certified model."),
                [ManualPaymentAdvancedOperation.Refund] = new(false, ManualPaymentAdvancedOperationScope.OutOfScope, "Unsupported", "Refund execution is explicitly outside Manual Payment advanced-operation translation certification."),
                [ManualPaymentAdvancedOperation.PartialRefund] = new(false, ManualPaymentAdvancedOperationScope.OutOfScope, "Unsupported", "Partial refund execution is explicitly outside Manual Payment advanced-operation translation certification."),
                [ManualPaymentAdvancedOperation.Capture] = new(false, ManualPaymentAdvancedOperationScope.OutOfScope, "Unsupported", "Capture is not a Manual Payment provider capability."),
                [ManualPaymentAdvancedOperation.Settlement] = new(false, ManualPaymentAdvancedOperationScope.OutOfScope, "Unsupported", "Settlement is not owned by the Manual Payment provider adapter."),
                [ManualPaymentAdvancedOperation.ApprovalExecution] = new(false, ManualPaymentAdvancedOperationScope.OutOfScope, "Unsupported", "Operator approval execution is workflow authority, not adapter translation."),
                [ManualPaymentAdvancedOperation.ReviewExecution] = new(false, ManualPaymentAdvancedOperationScope.OutOfScope, "Unsupported", "Operator review execution is workflow authority, not adapter translation.")
            });

    public static IReadOnlyCollection<ManualPaymentAdvancedOperation> SupportedOperations { get; } =
        Array.AsReadOnly(Operations.Where(pair => pair.Value.Supported).Select(pair => pair.Key).ToArray());

    public static IReadOnlyCollection<ManualPaymentAdvancedOperation> UnsupportedOperations { get; } =
        Array.AsReadOnly(Operations.Where(pair => !pair.Value.Supported).Select(pair => pair.Key).ToArray());

    public static bool IsSupported(ManualPaymentAdvancedOperation operation)
    {
        return Operations.TryGetValue(operation, out var capability) && capability.Supported;
    }
}

public static class ManualPaymentAdvancedOperationStatePolicy
{
    private static readonly HashSet<string> PendingProviderStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pending",
        "PendingManualPayment",
        "AwaitingCustomer",
        "ActionRequired"
    };

    public static bool IsAllowed(ManualPaymentAdvancedOperationRequest request)
    {
        if (request.Operation is not (ManualPaymentAdvancedOperation.CancelPendingPayment or ManualPaymentAdvancedOperation.ExpirePendingPayment))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(request.CurrentProviderStatus) &&
               PendingProviderStatuses.Contains(request.CurrentProviderStatus.Trim());
    }
}

public sealed record ManualPaymentAdvancedOperationResult
{
    public bool Succeeded { get; init; }
    public bool Rejected { get; init; }
    public ManualPaymentAdvancedOperationRequest? Request { get; init; }
    public ProviderEvidence? Evidence { get; init; }
    public CanonicalProviderError? Error { get; init; }

    private ManualPaymentAdvancedOperationResult()
    {
    }

    public static ManualPaymentAdvancedOperationResult Success(
        ManualPaymentAdvancedOperationRequest request,
        ProviderEvidence evidence) =>
        new() { Succeeded = true, Request = request, Evidence = evidence };

    public static ManualPaymentAdvancedOperationResult CreateRejected(
        ManualPaymentAdvancedOperationRequest request,
        ProviderEvidence evidence,
        CanonicalProviderError? error = null) =>
        new() { Rejected = true, Request = request, Evidence = evidence, Error = error };

    public static ManualPaymentAdvancedOperationResult Failed(CanonicalProviderError error) =>
        new() { Error = error };
}

public sealed record ManualPaymentAdvancedOperationRequest(
    ManualPaymentAdvancedOperation Operation,
    ManualPaymentProviderAdvancedOperation ProviderOperation,
    string ProviderReference,
    string? EvidenceReference,
    string? CurrentProviderStatus,
    string? CurrentEvidenceStatus,
    string OperationReference,
    IReadOnlyDictionary<string, string> CanonicalMetadata);

public sealed record ManualPaymentAdvancedOperationCapability(
    bool Supported,
    ManualPaymentAdvancedOperationScope Scope,
    string ProviderOperation,
    string Notes);

public enum ManualPaymentAdvancedOperation
{
    Unknown = 0,
    CancelPendingPayment = 1,
    ExpirePendingPayment = 2,
    PaymentStatusInquiry = 3,
    EvidenceStatusInquiry = 4,
    WithdrawSubmittedEvidence = 5,
    CapabilityDiscovery = 6,
    Refund = 7,
    PartialRefund = 8,
    Capture = 9,
    Settlement = 10,
    ApprovalExecution = 11,
    ReviewExecution = 12
}

public enum ManualPaymentProviderAdvancedOperation
{
    Unsupported = 0,
    CancelPendingManualPayment = 1,
    ExpirePendingManualPayment = 2,
    PaymentStatusInquiry = 3,
    EvidenceStatusInquiry = 4,
    CapabilityDiscovery = 5
}

public enum ManualPaymentAdvancedOperationScope
{
    Supported = 0,
    NotSupported = 1,
    OutOfScope = 2
}

public sealed record ManualPaymentAdvancedOperationMetadata(IReadOnlyDictionary<string, string> Values)
{
    public static ManualPaymentAdvancedOperation ResolveOperation(PaymentAction action)
    {
        var raw = action.Metadata.Values.TryGetValue(ManualPaymentAdvancedOperationMetadataKeys.Operation, out var value)
            ? value
            : action.Reference.Value;

        return raw.Trim().ToLowerInvariant() switch
        {
            "cancel_pending_payment" or "cancel-pending-payment" or "cancelpendingpayment" => ManualPaymentAdvancedOperation.CancelPendingPayment,
            "expire_pending_payment" or "expire-pending-payment" or "expirependingpayment" => ManualPaymentAdvancedOperation.ExpirePendingPayment,
            "payment_status_inquiry" or "payment-status-inquiry" or "paymentstatusinquiry" or "status_inquiry" => ManualPaymentAdvancedOperation.PaymentStatusInquiry,
            "evidence_status_inquiry" or "evidence-status-inquiry" or "evidencestatusinquiry" => ManualPaymentAdvancedOperation.EvidenceStatusInquiry,
            "withdraw_submitted_evidence" or "withdraw-submitted-evidence" or "withdrawsubmittedevidence" => ManualPaymentAdvancedOperation.WithdrawSubmittedEvidence,
            "capability_discovery" or "capability-discovery" or "capabilitydiscovery" => ManualPaymentAdvancedOperation.CapabilityDiscovery,
            "refund" => ManualPaymentAdvancedOperation.Refund,
            "partial_refund" or "partial-refund" or "partialrefund" => ManualPaymentAdvancedOperation.PartialRefund,
            "capture" => ManualPaymentAdvancedOperation.Capture,
            "settlement" => ManualPaymentAdvancedOperation.Settlement,
            "approval_execution" or "approval-execution" => ManualPaymentAdvancedOperation.ApprovalExecution,
            "review_execution" or "review-execution" => ManualPaymentAdvancedOperation.ReviewExecution,
            _ => throw new ArgumentException("Canonical Manual Payment advanced operation is not recognized.")
        };
    }

    public static ManualPaymentAdvancedOperationMetadata FromAction(PaymentAction action, ManualPaymentAdvancedOperation operation)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["operation"] = operation.ToString(),
            ["payment_action_id"] = action.Id.Value.ToString("N"),
            ["payment_session_id"] = action.SessionId.Value.ToString("N"),
            ["payment_attempt_id"] = action.AttemptId.Value.ToString("N"),
            ["correlation_id"] = action.CorrelationId.Value,
            ["action_reference"] = action.Reference.Value
        };

        foreach (var (key, value) in action.Metadata.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(key) && value is not null)
            {
                values[key.Trim()] = value.Trim();
            }
        }

        return new ManualPaymentAdvancedOperationMetadata(new ReadOnlyDictionary<string, string>(values));
    }
}

public static class ManualPaymentAdvancedOperationMetadataKeys
{
    public const string Operation = "Operation";
    public const string ProviderReference = "ProviderReference";
    public const string EvidenceReference = "EvidenceReference";
    public const string CurrentProviderStatus = "CurrentProviderStatus";
    public const string CurrentEvidenceStatus = "CurrentEvidenceStatus";
    public const string OperationReference = "OperationReference";
    public const string ConfiguredProviderName = "ConfiguredProviderName";
}

public sealed class ManualPaymentAdvancedConfigurationMismatchException : Exception
{
    public ManualPaymentAdvancedConfigurationMismatchException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
