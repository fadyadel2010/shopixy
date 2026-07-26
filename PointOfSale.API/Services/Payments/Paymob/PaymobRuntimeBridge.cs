using System.Security.Cryptography;
using System.Text;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;

namespace PointOfSale.API.Services.Payments.Paymob;

public interface IPaymobRuntimeBridge
{
    Task<PaymobExecutionResult> ExecuteAsync(
        PaymobExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PaymobRuntimeBridge : IPaymobRuntimeBridge
{
    private readonly IPaymobPaymentAdapter _adapter;
    private readonly PaymentRuntimeOrchestrator _orchestrator;

    public PaymobRuntimeBridge(
        IPaymobPaymentAdapter adapter,
        PaymentRuntimeOrchestrator orchestrator)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public async Task<PaymobExecutionResult> ExecuteAsync(
        PaymobExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var adapterResult = await _adapter.CreateAsync(request.Action, cancellationToken);
        var evidence = adapterResult.Evidence
            ?? PaymobExecutionFactory.ProviderErrorEvidence(request.Action, adapterResult.Error);

        var runtimeResult = _orchestrator.Execute(
            request.Context,
            evidence,
            request.Action,
            request.Session,
            request.PriorEvidence,
            request.PriorTransitions,
            request.PriorTransactions,
            request.PriorSettlements);

        return PaymobExecutionResultFactory.Create(
            evidence,
            runtimeResult,
            adapterResult.Error,
            request.Metadata);
    }
}

public sealed record PaymobExecutionRequest
{
    public RuntimeExecutionContext Context { get; init; }

    public PaymentAction Action { get; init; }

    public PaymentSession Session { get; init; }

    public IReadOnlyCollection<AdmittedEvidence>? PriorEvidence { get; init; }

    public IReadOnlyCollection<StateTransition>? PriorTransitions { get; init; }

    public IReadOnlyCollection<PaymentTransaction>? PriorTransactions { get; init; }

    public IReadOnlyCollection<PaymentSettlement>? PriorSettlements { get; init; }

    public ExecutionMetadata Metadata { get; init; }

    public PaymobExecutionRequest()
    {
        Context = new RuntimeExecutionContext();
        Action = new PaymentAction();
        Session = new PaymentSession();
        Metadata = new ExecutionMetadata();
    }

    public PaymobExecutionRequest(
        RuntimeExecutionContext context,
        PaymentAction action,
        PaymentSession session,
        ExecutionMetadata? metadata = null,
        IReadOnlyCollection<AdmittedEvidence>? priorEvidence = null,
        IReadOnlyCollection<StateTransition>? priorTransitions = null,
        IReadOnlyCollection<PaymentTransaction>? priorTransactions = null,
        IReadOnlyCollection<PaymentSettlement>? priorSettlements = null)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Metadata = metadata ?? new ExecutionMetadata();
        PriorEvidence = priorEvidence is null ? null : Array.AsReadOnly(priorEvidence.ToArray());
        PriorTransitions = priorTransitions is null ? null : Array.AsReadOnly(priorTransitions.ToArray());
        PriorTransactions = priorTransactions is null ? null : Array.AsReadOnly(priorTransactions.ToArray());
        PriorSettlements = priorSettlements is null ? null : Array.AsReadOnly(priorSettlements.ToArray());
    }
}

public sealed record PaymobExecutionResult
{
    public ProviderEvidence ProviderEvidence { get; init; }

    public RuntimeResult RuntimeResult { get; init; }

    public PaymobExecutionStatus ExecutionStatus { get; init; }

    public ExecutionTrace ExecutionTrace { get; init; }

    public ExecutionMetadata ExecutionMetadata { get; init; }

    public CanonicalProviderError? ProviderError { get; init; }

    public PaymobExecutionResult()
    {
        ProviderEvidence = new ProviderEvidence();
        RuntimeResult = new RuntimeResult();
        ExecutionTrace = new ExecutionTrace();
        ExecutionMetadata = new ExecutionMetadata();
    }

    public PaymobExecutionResult(
        ProviderEvidence providerEvidence,
        RuntimeResult runtimeResult,
        PaymobExecutionStatus executionStatus,
        ExecutionTrace executionTrace,
        ExecutionMetadata executionMetadata,
        CanonicalProviderError? providerError = null)
    {
        ProviderEvidence = providerEvidence ?? throw new ArgumentNullException(nameof(providerEvidence));
        RuntimeResult = runtimeResult ?? throw new ArgumentNullException(nameof(runtimeResult));
        ExecutionStatus = executionStatus == PaymobExecutionStatus.Unknown
            ? throw new ArgumentException("Execution status is required.", nameof(executionStatus))
            : executionStatus;
        ExecutionTrace = executionTrace ?? throw new ArgumentNullException(nameof(executionTrace));
        ExecutionMetadata = executionMetadata ?? new ExecutionMetadata();
        ProviderError = providerError;
    }
}

public enum PaymobExecutionStatus
{
    Unknown = 0,
    RuntimeSucceeded = 1,
    RuntimeFailed = 2,
    ProviderFailed = 3
}

public static class PaymobExecutionFactory
{
    public static PaymobExecutionRequest CreateRequest(
        PaymentAction action,
        PaymentSession session,
        DateTimeOffset executionTimestamp,
        ExecutionMetadata? metadata = null,
        IReadOnlyCollection<AdmittedEvidence>? priorEvidence = null,
        IReadOnlyCollection<StateTransition>? priorTransitions = null,
        IReadOnlyCollection<PaymentTransaction>? priorTransactions = null,
        IReadOnlyCollection<PaymentSettlement>? priorSettlements = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(session);

        var executionId = DeterministicGuid(
            "paymob-runtime-bridge-execution",
            action.SessionId.Value.ToString("N"),
            action.AttemptId.Value.ToString("N"),
            action.Id.Value.ToString("N"),
            action.CorrelationId.Value);
        var context = RuntimeExecutionFactory.Create(
            new CorrelationId(action.CorrelationId.Value),
            session.Id,
            new ExecutionId(executionId),
            executionTimestamp,
            new PipelineMetadata(new Dictionary<string, string>
            {
                ["bridge"] = "paymob-runtime-bridge",
                ["provider"] = PaymobProviderReference.ProviderName
            }),
            new ExecutionTraceId($"paymob-runtime-bridge:{executionId:N}"));

        return new PaymobExecutionRequest(
            context,
            action,
            session,
            metadata,
            priorEvidence,
            priorTransitions,
            priorTransactions,
            priorSettlements);
    }

    public static ProviderEvidence ProviderErrorEvidence(
        PaymentAction action,
        CanonicalProviderError? error)
    {
        ArgumentNullException.ThrowIfNull(action);

        var resolvedError = error ?? CanonicalProviderError.Configuration(
            PaymobProviderReference.ProviderName,
            "PAYMOB_PROVIDER_ERROR_MISSING",
            "Paymob adapter did not return provider error details.");
        var source = string.Join(
            "|",
            "paymob-provider-error",
            action.Id.Value.ToString("N"),
            action.CorrelationId.Value,
            resolvedError.Type,
            resolvedError.Code);
        var evidenceId = DeterministicGuid(source);
        var timestamp = action.CreatedUtc.AddTicks(-1);
        var metadata = resolvedError.Metadata
            .Concat(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider_error_type"] = resolvedError.Type.ToString(),
                ["provider_error_code"] = resolvedError.Code,
                ["provider_error_message"] = resolvedError.Message,
                ["correlation_id"] = action.CorrelationId.Value
            })
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

        return ProviderEvidenceFactory.Create(
            new EvidenceId(evidenceId),
            action.Id,
            EvidenceType.ProviderNotification,
            EvidenceStatus.Rejected,
            new EvidenceReference($"paymob:error:{resolvedError.Code}"),
            new EvidenceSource(PaymobProviderReference.ProviderName),
            new EvidenceTimestamp(timestamp),
            new EvidenceHash(Hash(source)),
            new ProviderPayloadReference($"paymob:error:{resolvedError.Code}"),
            action.CorrelationId,
            new EvidenceMetadata(metadata));
    }

    private static Guid DeterministicGuid(params string[] components)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', components)));
        var bytes = hash.Take(16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static string Hash(string source)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }
}

public static class PaymobExecutionResultFactory
{
    public static PaymobExecutionResult Create(
        ProviderEvidence providerEvidence,
        RuntimeResult runtimeResult,
        CanonicalProviderError? providerError = null,
        ExecutionMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(providerEvidence);
        ArgumentNullException.ThrowIfNull(runtimeResult);

        var status = providerError is not null
            ? PaymobExecutionStatus.ProviderFailed
            : runtimeResult.OverallStatus == RuntimeOverallStatus.Succeeded
                ? PaymobExecutionStatus.RuntimeSucceeded
                : PaymobExecutionStatus.RuntimeFailed;
        var trace = new ExecutionTrace(
            new BridgeTraceId($"paymob-bridge:{runtimeResult.ExecutionContext.ExecutionId.Value:N}"),
            new PointOfSale.API.Services.Payments.ProviderContracts.ExecutionReference(providerEvidence.Reference.Value),
            new ExecutionCorrelation(providerEvidence.CorrelationId.Value),
            runtimeResult.ExecutionTrace.Entries.Select(entry => entry.Stage.ToString()).ToArray());

        return new PaymobExecutionResult(
            providerEvidence,
            runtimeResult,
            status,
            trace,
            metadata ?? new ExecutionMetadata(),
            providerError);
    }
}
