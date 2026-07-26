using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;

namespace PointOfSale.API.Services.Payments.ManualPayment;

public interface IManualPaymentRuntimeBridge
{
    Task<ManualPaymentExecutionResult> ExecuteAsync(
        ManualPaymentExecutionRequest request,
        CancellationToken cancellationToken = default);

    Task<ManualPaymentExecutionResult> ExecuteEvidenceAsync(
        ManualPaymentEvidenceExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ManualPaymentRuntimeBridge : IManualPaymentRuntimeBridge
{
    private readonly IManualPaymentProviderAdapter _adapter;
    private readonly PaymentRuntimeOrchestrator _orchestrator;

    public ManualPaymentRuntimeBridge(
        IManualPaymentProviderAdapter adapter,
        PaymentRuntimeOrchestrator orchestrator)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public Task<ManualPaymentExecutionResult> ExecuteAsync(
        ManualPaymentExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var adapterResult = _adapter.CreatePendingPayment(request.CanonicalRequest);
        var evidence = adapterResult.CanonicalEvidence
            ?? ManualPaymentExecutionFactory.ProviderErrorEvidence(request.Action, adapterResult.Error);

        var runtimeResult = _orchestrator.Execute(
            request.Context,
            evidence,
            request.Action,
            request.Session,
            request.PriorEvidence,
            request.PriorTransitions,
            request.PriorTransactions,
            request.PriorSettlements);

        return Task.FromResult(ManualPaymentExecutionResultFactory.Create(
            evidence,
            runtimeResult,
            adapterResult.Error,
            request.Metadata));
    }

    public Task<ManualPaymentExecutionResult> ExecuteEvidenceAsync(
        ManualPaymentEvidenceExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runtimeResult = _orchestrator.Execute(
            request.Context,
            request.Evidence,
            request.Action,
            request.Session,
            request.PriorEvidence,
            request.PriorTransitions,
            request.PriorTransactions,
            request.PriorSettlements);

        return Task.FromResult(ManualPaymentExecutionResultFactory.Create(
            request.Evidence,
            runtimeResult,
            null,
            request.Metadata));
    }
}

public sealed record ManualPaymentEvidenceExecutionRequest(
    RuntimeExecutionContext Context,
    PaymentAction Action,
    PaymentSession Session,
    ProviderEvidence Evidence,
    ManualPaymentExecutionMetadata? Metadata = null,
    IReadOnlyCollection<AdmittedEvidence>? PriorEvidence = null,
    IReadOnlyCollection<StateTransition>? PriorTransitions = null,
    IReadOnlyCollection<PaymentTransaction>? PriorTransactions = null,
    IReadOnlyCollection<PaymentSettlement>? PriorSettlements = null)
{
    public ManualPaymentExecutionMetadata Metadata { get; init; } = Metadata ?? new ManualPaymentExecutionMetadata();
}

public sealed record ManualPaymentExecutionRequest
{
    public RuntimeExecutionContext Context { get; init; }

    public PaymentAction Action { get; init; }

    public PaymentSession Session { get; init; }

    public ManualPaymentCanonicalRequest CanonicalRequest { get; init; }

    public IReadOnlyCollection<AdmittedEvidence>? PriorEvidence { get; init; }

    public IReadOnlyCollection<StateTransition>? PriorTransitions { get; init; }

    public IReadOnlyCollection<PaymentTransaction>? PriorTransactions { get; init; }

    public IReadOnlyCollection<PaymentSettlement>? PriorSettlements { get; init; }

    public ManualPaymentExecutionMetadata Metadata { get; init; }

    public ManualPaymentExecutionRequest()
    {
        Context = new RuntimeExecutionContext();
        Action = new PaymentAction();
        Session = new PaymentSession();
        CanonicalRequest = new ManualPaymentCanonicalRequest(
            Guid.Empty,
            Guid.Empty,
            new PaymentActionId(Guid.Empty),
            new ActionCorrelationId(string.Empty),
            0m,
            string.Empty,
            new ManualPaymentCustomer(string.Empty, null, null),
            null,
            false,
            ManualPaymentChannel.CustomInstructions,
            Array.AsReadOnly(Array.Empty<ManualPaymentChannelConfiguration>()),
            false,
            string.Empty,
            false,
            null,
            string.Empty,
            default);
        Metadata = new ManualPaymentExecutionMetadata();
    }

    public ManualPaymentExecutionRequest(
        RuntimeExecutionContext context,
        PaymentAction action,
        PaymentSession session,
        ManualPaymentCanonicalRequest canonicalRequest,
        ManualPaymentExecutionMetadata? metadata = null,
        IReadOnlyCollection<AdmittedEvidence>? priorEvidence = null,
        IReadOnlyCollection<StateTransition>? priorTransitions = null,
        IReadOnlyCollection<PaymentTransaction>? priorTransactions = null,
        IReadOnlyCollection<PaymentSettlement>? priorSettlements = null)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        CanonicalRequest = canonicalRequest ?? throw new ArgumentNullException(nameof(canonicalRequest));
        Metadata = metadata ?? new ManualPaymentExecutionMetadata();
        PriorEvidence = priorEvidence is null ? null : Array.AsReadOnly(priorEvidence.ToArray());
        PriorTransitions = priorTransitions is null ? null : Array.AsReadOnly(priorTransitions.ToArray());
        PriorTransactions = priorTransactions is null ? null : Array.AsReadOnly(priorTransactions.ToArray());
        PriorSettlements = priorSettlements is null ? null : Array.AsReadOnly(priorSettlements.ToArray());
    }
}

public sealed record ManualPaymentExecutionResult
{
    public ProviderEvidence ProviderEvidence { get; init; }

    public RuntimeResult RuntimeResult { get; init; }

    public ManualPaymentExecutionStatus ExecutionStatus { get; init; }

    public ManualPaymentExecutionTrace ExecutionTrace { get; init; }

    public ManualPaymentExecutionMetadata ExecutionMetadata { get; init; }

    public CanonicalProviderError? ProviderError { get; init; }

    public ManualPaymentExecutionResult()
    {
        ProviderEvidence = new ProviderEvidence();
        RuntimeResult = new RuntimeResult();
        ExecutionTrace = new ManualPaymentExecutionTrace();
        ExecutionMetadata = new ManualPaymentExecutionMetadata();
    }

    public ManualPaymentExecutionResult(
        ProviderEvidence providerEvidence,
        RuntimeResult runtimeResult,
        ManualPaymentExecutionStatus executionStatus,
        ManualPaymentExecutionTrace executionTrace,
        ManualPaymentExecutionMetadata executionMetadata,
        CanonicalProviderError? providerError = null)
    {
        ProviderEvidence = providerEvidence ?? throw new ArgumentNullException(nameof(providerEvidence));
        RuntimeResult = runtimeResult ?? throw new ArgumentNullException(nameof(runtimeResult));
        ExecutionStatus = executionStatus == ManualPaymentExecutionStatus.Unknown
            ? throw new ArgumentException("Execution status is required.", nameof(executionStatus))
            : executionStatus;
        ExecutionTrace = executionTrace ?? throw new ArgumentNullException(nameof(executionTrace));
        ExecutionMetadata = executionMetadata ?? new ManualPaymentExecutionMetadata();
        ProviderError = providerError;
    }
}

public enum ManualPaymentExecutionStatus
{
    Unknown = 0,
    RuntimeSucceeded = 1,
    RuntimeFailed = 2,
    ProviderFailed = 3
}

public sealed record ManualPaymentExecutionReference(string Value)
{
    public ManualPaymentExecutionReference() : this(string.Empty)
    {
    }
}

public sealed record ManualPaymentBridgeTraceId(string Value)
{
    public ManualPaymentBridgeTraceId() : this(string.Empty)
    {
    }
}

public sealed record ManualPaymentExecutionCorrelation(string Value)
{
    public ManualPaymentExecutionCorrelation() : this(string.Empty)
    {
    }
}

public sealed record ManualPaymentExecutionMetadata
{
    public IReadOnlyDictionary<string, string> Values { get; init; }

    public ManualPaymentExecutionMetadata(IReadOnlyDictionary<string, string>? values = null)
    {
        Values = Normalize(values);
    }

    private static IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values.OrderBy(pair => pair.Key.Trim(), StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Execution metadata keys are required.", nameof(values));
            }

            normalized[key.Trim()] = value ?? string.Empty;
        }

        return new ReadOnlyDictionary<string, string>(normalized);
    }
}

public sealed record ManualPaymentExecutionTrace
{
    public ManualPaymentBridgeTraceId Id { get; init; }

    public ManualPaymentExecutionReference Reference { get; init; }

    public ManualPaymentExecutionCorrelation Correlation { get; init; }

    public IReadOnlyCollection<string> Entries { get; init; }

    public ManualPaymentExecutionTrace()
    {
        Id = new ManualPaymentBridgeTraceId(string.Empty);
        Reference = new ManualPaymentExecutionReference(string.Empty);
        Correlation = new ManualPaymentExecutionCorrelation(string.Empty);
        Entries = Array.AsReadOnly(Array.Empty<string>());
    }

    public ManualPaymentExecutionTrace(
        ManualPaymentBridgeTraceId id,
        ManualPaymentExecutionReference reference,
        ManualPaymentExecutionCorrelation correlation,
        IReadOnlyCollection<string>? entries = null)
    {
        RequireText(id.Value, nameof(id));
        RequireText(reference.Value, nameof(reference));
        RequireText(correlation.Value, nameof(correlation));

        Id = new ManualPaymentBridgeTraceId(id.Value.Trim());
        Reference = new ManualPaymentExecutionReference(reference.Value.Trim());
        Correlation = new ManualPaymentExecutionCorrelation(correlation.Value.Trim());
        Entries = Array.AsReadOnly(entries?.Where(entry => !string.IsNullOrWhiteSpace(entry)).Select(entry => entry.Trim()).ToArray()
            ?? Array.Empty<string>());
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }
}

public static class ManualPaymentExecutionFactory
{
    public static RuntimeExecutionContext CreateContext(
        PaymentAction action,
        PaymentSession session,
        DateTimeOffset executionTimestamp)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(session);

        var executionId = DeterministicGuid(
            "manual-payment-runtime-bridge-execution",
            action.SessionId.Value.ToString("N"),
            action.AttemptId.Value.ToString("N"),
            action.Id.Value.ToString("N"),
            action.CorrelationId.Value);

        return RuntimeExecutionFactory.Create(
            new CorrelationId(action.CorrelationId.Value),
            session.Id,
            new ExecutionId(executionId),
            executionTimestamp,
            new PipelineMetadata(new Dictionary<string, string>
            {
                ["bridge"] = "manual-payment-runtime-bridge",
                ["provider"] = ManualPaymentProviderReference.ProviderName
            }),
            new ExecutionTraceId($"manual-payment-runtime-bridge:{executionId:N}"));
    }

    public static ManualPaymentExecutionRequest CreateRequest(
        PaymentAction action,
        PaymentSession session,
        ManualPaymentCanonicalRequest canonicalRequest,
        DateTimeOffset executionTimestamp,
        ManualPaymentExecutionMetadata? metadata = null,
        IReadOnlyCollection<AdmittedEvidence>? priorEvidence = null,
        IReadOnlyCollection<StateTransition>? priorTransitions = null,
        IReadOnlyCollection<PaymentTransaction>? priorTransactions = null,
        IReadOnlyCollection<PaymentSettlement>? priorSettlements = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(canonicalRequest);

        var context = CreateContext(action, session, executionTimestamp);

        return new ManualPaymentExecutionRequest(
            context,
            action,
            session,
            canonicalRequest,
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
            ManualPaymentProviderReference.ProviderName,
            "MANUAL_PROVIDER_ERROR_MISSING",
            "Manual Payment adapter did not return provider evidence or provider error details.");
        var source = string.Join(
            "|",
            "manual-provider-error",
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
            new EvidenceReference($"manual_payment:error:{resolvedError.Code}"),
            new EvidenceSource(ManualPaymentProviderReference.ProviderName),
            new EvidenceTimestamp(timestamp),
            new EvidenceHash(Hash(source)),
            new ProviderPayloadReference($"manual_payment:error:{resolvedError.Code}"),
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

public static class ManualPaymentExecutionResultFactory
{
    public static ManualPaymentExecutionResult Create(
        ProviderEvidence providerEvidence,
        RuntimeResult runtimeResult,
        CanonicalProviderError? providerError = null,
        ManualPaymentExecutionMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(providerEvidence);
        ArgumentNullException.ThrowIfNull(runtimeResult);

        var status = providerError is not null
            ? ManualPaymentExecutionStatus.ProviderFailed
            : runtimeResult.OverallStatus == RuntimeOverallStatus.Succeeded
                ? ManualPaymentExecutionStatus.RuntimeSucceeded
                : ManualPaymentExecutionStatus.RuntimeFailed;
        var trace = new ManualPaymentExecutionTrace(
            new ManualPaymentBridgeTraceId($"manual-payment-bridge:{runtimeResult.ExecutionContext.ExecutionId.Value:N}"),
            new ManualPaymentExecutionReference(providerEvidence.Reference.Value),
            new ManualPaymentExecutionCorrelation(providerEvidence.CorrelationId.Value),
            runtimeResult.ExecutionTrace.Entries.Select(entry => entry.Stage.ToString()).ToArray());

        return new ManualPaymentExecutionResult(
            providerEvidence,
            runtimeResult,
            status,
            trace,
            metadata ?? new ManualPaymentExecutionMetadata(),
            providerError);
    }
}
