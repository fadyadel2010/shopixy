using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;

namespace PointOfSale.API.Services.Payments.Cod;

public interface ICodRuntimeAdmissionBridge
{
    CodRuntimeAdmissionBridgeRequest TranslateRequest(CodRuntimeAdmissionRequest request);

    CodRuntimeAdmissionOutcome TranslateResponse(
        CodRuntimeAdmissionRequest request,
        RuntimeResult runtimeResult);

    CodRuntimeAdmissionOutcome TranslateError(
        CodRuntimeAdmissionRequest request,
        RuntimeFailure failure);
}

public sealed class CodRuntimeAdmissionBridge : ICodRuntimeAdmissionBridge
{
    public CodRuntimeAdmissionBridgeRequest TranslateRequest(CodRuntimeAdmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ids = CodRuntimeAdmissionBridgeIds.From(request);
        var correlation = ResolveCorrelation(request);
        var metadata = BuildRuntimeMetadata(request);
        var currencyCode = FinancialCurrencyGuard.RequireCurrency(
            metadata.TryGetValue("currency", out var currency) ? currency : null,
            "cod.currency",
            $"admissionId:{request.AdmissionId.Value}");
        var amount = metadata.TryGetValue("amount", out var amountText) &&
            decimal.TryParse(
                amountText,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedAmount)
                ? parsedAmount
                : 0m;

        var context = RuntimeExecutionFactory.Create(
            new CorrelationId(correlation),
            ids.SessionId,
            ids.ExecutionId,
            request.AdmissionTimestamp,
            new PipelineMetadata(metadata),
            new ExecutionTraceId($"cod:{request.AdmissionId.Value}"));
        var session = PaymentSessionFactory.Create(
            ids.SessionId,
            ids.PaymentId,
            ids.OrderId,
            ids.TenantId,
            ids.StoreId,
            new PaymentReference("COD"),
            new Money(amount, new Currency(currencyCode)),
            MapRequestedOutcomeToRuntimeStatus(request.RequestedOutcome),
            request.AdmissionTimestamp,
            request.AdmissionTimestamp,
            new PaymentMetadata(metadata));
        var action = PaymentActionFactory.Create(
            ids.ActionId,
            ids.SessionId,
            ids.AttemptId,
            PaymentActionType.ReceiveShipment,
            PaymentActionStatus.Completed,
            new ActionReference(request.CollectionReference.CollectionId),
            new ActionCorrelationId(correlation),
            request.AdmissionTimestamp,
            request.AdmissionTimestamp,
            metadata: new PaymentActionMetadata(metadata));
        var evidence = ProviderEvidenceFactory.Create(
            ids.EvidenceId,
            ids.ActionId,
            MapEvidenceType(request),
            EvidenceStatus.Recorded,
            new EvidenceReference(request.EvidenceMetadata.SourceReference ?? request.EvidenceIdentity.EvidenceId),
            new EvidenceSource(request.EvidenceIdentity.EvidenceSource),
            new EvidenceTimestamp(request.AdmissionTimestamp),
            new EvidenceHash(ComputeHash(request)),
            new ProviderPayloadReference(request.EvidenceMetadata.SourceReference ?? request.EvidenceIdentity.EvidenceId),
            new ActionCorrelationId(correlation),
            new EvidenceMetadata(metadata));

        return new CodRuntimeAdmissionBridgeRequest(context, session, action, evidence, request);
    }

    public CodRuntimeAdmissionOutcome TranslateResponse(
        CodRuntimeAdmissionRequest request,
        RuntimeResult runtimeResult)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(runtimeResult);

        if (runtimeResult.OverallStatus == RuntimeOverallStatus.Succeeded)
        {
            return Outcome(
                request,
                CodAdmissionOutcomeStatus.Accepted,
                "runtime_succeeded",
                "Runtime result translated to accepted COD admission response.",
                RuntimeMetadata(runtimeResult));
        }

        if (runtimeResult.Failure is not null)
        {
            return TranslateError(request, runtimeResult.Failure);
        }

        return Outcome(
            request,
            CodAdmissionOutcomeStatus.Rejected,
            "runtime_failed",
            "Runtime result translated to rejected COD admission response.",
            RuntimeMetadata(runtimeResult));
    }

    public CodRuntimeAdmissionOutcome TranslateError(
        CodRuntimeAdmissionRequest request,
        RuntimeFailure failure)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(failure);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime_failure_stage"] = failure.FailureStage.ToString(),
            ["runtime_failure_reason"] = failure.FailureReason.Value
        };

        foreach (var (key, value) in failure.FailureMetadata.Values)
        {
            metadata[$"runtime_failure_metadata_{key}"] = value;
        }

        return Outcome(
            request,
            CodAdmissionOutcomeStatus.Rejected,
            "runtime_failure",
            "Runtime failure translated to canonical COD admission failure.",
            metadata);
    }

    private static CodRuntimeAdmissionOutcome Outcome(
        CodRuntimeAdmissionRequest request,
        CodAdmissionOutcomeStatus status,
        string code,
        string description,
        IReadOnlyDictionary<string, string> metadata)
    {
        return new CodRuntimeAdmissionOutcome(
            request.AdmissionId,
            status,
            code,
            description,
            new CodAdmissionMetadata(
                metadata,
                request.EvidenceMetadata.SourceReference,
                request.EvidenceMetadata.ActorReference,
                request.EvidenceMetadata.Notes));
    }

    private static PaymentStatus MapRequestedOutcomeToRuntimeStatus(CodRequestedAdmissionOutcome outcome)
    {
        return outcome switch
        {
            CodRequestedAdmissionOutcome.CollectionAccepted => PaymentStatus.Collected,
            CodRequestedAdmissionOutcome.CollectionRejected => PaymentStatus.Rejected,
            CodRequestedAdmissionOutcome.CollectionDeferred => PaymentStatus.Pending,
            CodRequestedAdmissionOutcome.CollectionIncomplete => PaymentStatus.ManualReview,
            CodRequestedAdmissionOutcome.CollectionUnsupported => PaymentStatus.Rejected,
            _ => PaymentStatus.Rejected
        };
    }

    private static EvidenceType MapEvidenceType(CodRuntimeAdmissionRequest request)
    {
        var source = request.EvidenceIdentity.EvidenceSource;
        if (source.Equals("driver", StringComparison.OrdinalIgnoreCase))
        {
            return EvidenceType.DriverConfirmation;
        }

        if (source.Equals("carrier", StringComparison.OrdinalIgnoreCase) ||
            source.Equals("courier", StringComparison.OrdinalIgnoreCase))
        {
            return EvidenceType.CarrierConfirmation;
        }

        if (source.Equals("pos_admin", StringComparison.OrdinalIgnoreCase) ||
            source.Equals("manual_entry", StringComparison.OrdinalIgnoreCase))
        {
            return EvidenceType.OperatorDecision;
        }

        return EvidenceType.FulfillmentCollection;
    }

    private static IReadOnlyDictionary<string, string> BuildRuntimeMetadata(CodRuntimeAdmissionRequest request)
    {
        var values = new Dictionary<string, string>(request.EvidenceMetadata.Values, StringComparer.Ordinal)
        {
            ["cod_admission_id"] = request.AdmissionId.Value,
            ["cod_collection_id"] = request.CollectionReference.CollectionId,
            ["cod_evidence_id"] = request.EvidenceIdentity.EvidenceId,
            ["cod_evidence_type"] = request.EvidenceIdentity.EvidenceType,
            ["cod_evidence_source"] = request.EvidenceIdentity.EvidenceSource,
            ["cod_evidence_schema"] = request.EvidenceVersion.SchemaName,
            ["cod_evidence_version"] = $"{request.EvidenceVersion.Major}.{request.EvidenceVersion.Minor}",
            ["cod_requested_outcome"] = request.RequestedOutcome.ToString(),
            ["cod_runtime_context_environment"] = request.RuntimeContext.Environment,
            ["cod_runtime_context_payment_identity"] = request.RuntimeContext.PaymentIdentity
        };

        AddOptional(values, "cod_correlation_id", request.Correlation.CorrelationId);
        AddOptional(values, "cod_causation_id", request.Correlation.CausationId);
        AddOptional(values, "cod_request_id", request.Correlation.RequestId);
        AddOptional(values, "cod_trace_id", request.Correlation.TraceId);
        AddOptional(values, "cod_order_id", request.CollectionReference.OrderId);
        AddOptional(values, "cod_suborder_id", request.CollectionReference.SubOrderId);
        AddOptional(values, "cod_shipment_id", request.CollectionReference.ShipmentId);
        AddOptional(values, "cod_settlement_reference", request.CollectionReference.SettlementReference);
        AddOptional(values, "cod_source_reference", request.EvidenceMetadata.SourceReference);
        AddOptional(values, "cod_actor_reference", request.EvidenceMetadata.ActorReference);
        AddOptional(values, "cod_evidence_revision", request.EvidenceVersion.Revision);

        return new ReadOnlyDictionary<string, string>(values);
    }

    private static IReadOnlyDictionary<string, string> RuntimeMetadata(RuntimeResult result)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime_overall_status"] = result.OverallStatus.ToString(),
            ["runtime_payment_status"] = result.Status.ToString(),
            ["runtime_session_id"] = result.SessionId.Value.ToString("D")
        };

        if (result.PaymentTransaction is not null)
        {
            values["runtime_transaction_id"] = result.PaymentTransaction.TransactionId.Value.ToString("D");
        }

        if (result.PaymentSettlement is not null)
        {
            values["runtime_settlement_id"] = result.PaymentSettlement.SettlementId.Value.ToString("D");
        }

        return values;
    }

    private static string ResolveCorrelation(CodRuntimeAdmissionRequest request)
    {
        return FirstNonEmpty(
            request.Correlation.CorrelationId,
            request.Correlation.RequestId,
            request.Correlation.TraceId,
            request.AdmissionId.Value);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
    }

    private static void AddOptional(IDictionary<string, string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value.Trim();
        }
    }

    private static string ComputeHash(CodRuntimeAdmissionRequest request)
    {
        var material = string.Join(
            "|",
            request.AdmissionId.Value,
            request.EvidenceIdentity.EvidenceId,
            request.EvidenceIdentity.EvidenceType,
            request.EvidenceIdentity.EvidenceSource,
            request.CollectionReference.CollectionId,
            request.RequestedOutcome,
            request.AdmissionTimestamp.ToUnixTimeMilliseconds());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record CodRuntimeAdmissionBridgeRequest(
    RuntimeExecutionContext Context,
    PaymentSession Session,
    PaymentAction Action,
    ProviderEvidence Evidence,
    CodRuntimeAdmissionRequest AdmissionRequest);

internal sealed record CodRuntimeAdmissionBridgeIds(
    PaymentSessionId SessionId,
    PaymentId PaymentId,
    PaymentAttemptId AttemptId,
    PaymentActionId ActionId,
    EvidenceId EvidenceId,
    ExecutionId ExecutionId,
    Guid TenantId,
    Guid StoreId,
    Guid OrderId)
{
    public static CodRuntimeAdmissionBridgeIds From(CodRuntimeAdmissionRequest request)
    {
        return new CodRuntimeAdmissionBridgeIds(
            new PaymentSessionId(StableGuid("cod-session", request.RuntimeContext.PaymentIdentity)),
            new PaymentId(StableGuid("cod-payment", request.RuntimeContext.PaymentIdentity)),
            new PaymentAttemptId(StableGuid("cod-attempt", request.AdmissionId.Value)),
            new PaymentActionId(StableGuid("cod-action", request.AdmissionId.Value)),
            new EvidenceId(StableGuid("cod-evidence", request.EvidenceIdentity.EvidenceId)),
            new ExecutionId(StableGuid("cod-execution", request.AdmissionId.Value)),
            StableGuid("cod-tenant", request.RuntimeContext.TenantId),
            StableGuid("cod-store", request.RuntimeContext.TenantId),
            StableGuid("cod-order", request.CollectionReference.OrderId ?? request.CollectionReference.CollectionId));
    }

    private static Guid StableGuid(string scope, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}:{value}"));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
