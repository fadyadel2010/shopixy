using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;

namespace PointOfSale.API.Services.Payments.Stripe;

public interface IStripeRuntimeBridge
{
    Task<StripeExecutionResult> ExecuteAsync(
        StripeExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class StripeRuntimeBridge : IStripeRuntimeBridge
{
    private readonly IStripePaymentIntentAdapter _adapter;
    private readonly PaymentRuntimeOrchestrator _orchestrator;

    public StripeRuntimeBridge(
        IStripePaymentIntentAdapter adapter,
        PaymentRuntimeOrchestrator orchestrator)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public async Task<StripeExecutionResult> ExecuteAsync(
        StripeExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var adapterResult = await _adapter.CreateAsync(request.Action, cancellationToken);
        var evidence = adapterResult.Evidence
            ?? StripeExecutionFactory.ProviderErrorEvidence(request.Action, adapterResult.Error);

        var runtimeResult = _orchestrator.Execute(
            request.Context,
            evidence,
            request.Action,
            request.Session,
            request.PriorEvidence,
            request.PriorTransitions,
            request.PriorTransactions,
            request.PriorSettlements);

        return StripeExecutionResultFactory.Create(
            evidence,
            runtimeResult,
            adapterResult.Error,
            request.Metadata);
    }
}

public sealed record StripeExecutionRequest
{
    public RuntimeExecutionContext Context { get; init; }

    public PaymentAction Action { get; init; }

    public PaymentSession Session { get; init; }

    public IReadOnlyCollection<AdmittedEvidence>? PriorEvidence { get; init; }

    public IReadOnlyCollection<StateTransition>? PriorTransitions { get; init; }

    public IReadOnlyCollection<PaymentTransaction>? PriorTransactions { get; init; }

    public IReadOnlyCollection<PaymentSettlement>? PriorSettlements { get; init; }

    public ExecutionMetadata Metadata { get; init; }

    public StripeExecutionRequest()
    {
        Context = new RuntimeExecutionContext();
        Action = new PaymentAction();
        Session = new PaymentSession();
        Metadata = new ExecutionMetadata();
    }

    public StripeExecutionRequest(
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

public sealed record StripeExecutionResult
{
    public ProviderEvidence ProviderEvidence { get; init; }

    public RuntimeResult RuntimeResult { get; init; }

    public StripeExecutionStatus ExecutionStatus { get; init; }

    public ExecutionTrace ExecutionTrace { get; init; }

    public ExecutionMetadata ExecutionMetadata { get; init; }

    public CanonicalProviderError? ProviderError { get; init; }

    public StripeExecutionResult()
    {
        ProviderEvidence = new ProviderEvidence();
        RuntimeResult = new RuntimeResult();
        ExecutionTrace = new ExecutionTrace();
        ExecutionMetadata = new ExecutionMetadata();
    }

    public StripeExecutionResult(
        ProviderEvidence providerEvidence,
        RuntimeResult runtimeResult,
        StripeExecutionStatus executionStatus,
        ExecutionTrace executionTrace,
        ExecutionMetadata executionMetadata,
        CanonicalProviderError? providerError = null)
    {
        ProviderEvidence = providerEvidence ?? throw new ArgumentNullException(nameof(providerEvidence));
        RuntimeResult = runtimeResult ?? throw new ArgumentNullException(nameof(runtimeResult));
        ExecutionStatus = executionStatus == StripeExecutionStatus.Unknown
            ? throw new ArgumentException("Execution status is required.", nameof(executionStatus))
            : executionStatus;
        ExecutionTrace = executionTrace ?? throw new ArgumentNullException(nameof(executionTrace));
        ExecutionMetadata = executionMetadata ?? new ExecutionMetadata();
        ProviderError = providerError;
    }
}

public enum StripeExecutionStatus
{
    Unknown = 0,
    RuntimeSucceeded = 1,
    RuntimeFailed = 2,
    ProviderFailed = 3
}

public sealed record ExecutionReference(string Value)
{
    public ExecutionReference() : this(string.Empty)
    {
    }
}

public sealed record BridgeTraceId(string Value)
{
    public BridgeTraceId() : this(string.Empty)
    {
    }
}

public sealed record ExecutionCorrelation(string Value)
{
    public ExecutionCorrelation() : this(string.Empty)
    {
    }
}

public sealed record ExecutionMetadata
{
    public IReadOnlyDictionary<string, string> Values { get; init; }

    public ExecutionMetadata(IReadOnlyDictionary<string, string>? values = null)
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

public sealed record ExecutionTrace
{
    public BridgeTraceId Id { get; init; }

    public ExecutionReference Reference { get; init; }

    public ExecutionCorrelation Correlation { get; init; }

    public IReadOnlyCollection<string> Entries { get; init; }

    public ExecutionTrace()
    {
        Id = new BridgeTraceId(string.Empty);
        Reference = new ExecutionReference(string.Empty);
        Correlation = new ExecutionCorrelation(string.Empty);
        Entries = Array.AsReadOnly(Array.Empty<string>());
    }

    public ExecutionTrace(
        BridgeTraceId id,
        ExecutionReference reference,
        ExecutionCorrelation correlation,
        IReadOnlyCollection<string>? entries = null)
    {
        RequireText(id.Value, nameof(id));
        RequireText(reference.Value, nameof(reference));
        RequireText(correlation.Value, nameof(correlation));

        Id = new BridgeTraceId(id.Value.Trim());
        Reference = new ExecutionReference(reference.Value.Trim());
        Correlation = new ExecutionCorrelation(correlation.Value.Trim());
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

public static class StripeExecutionFactory
{
    public static StripeExecutionRequest CreateRequest(
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
            "stripe-runtime-bridge-execution",
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
                ["bridge"] = "stripe-runtime-bridge",
                ["provider"] = StripeProviderReference.ProviderName
            }),
            new ExecutionTraceId($"stripe-runtime-bridge:{executionId:N}"));

        return new StripeExecutionRequest(
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
            "STRIPE_PROVIDER_ERROR_MISSING",
            "Stripe adapter did not return provider error details.");
        var source = string.Join(
            "|",
            "stripe-provider-error",
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
            new EvidenceReference($"stripe:error:{resolvedError.Code}"),
            new EvidenceSource(StripeProviderReference.ProviderName),
            new EvidenceTimestamp(timestamp),
            new EvidenceHash(Hash(source)),
            new ProviderPayloadReference($"stripe:payment_intent:error:{resolvedError.Code}"),
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

public static class StripeExecutionResultFactory
{
    public static StripeExecutionResult Create(
        ProviderEvidence providerEvidence,
        RuntimeResult runtimeResult,
        CanonicalProviderError? providerError = null,
        ExecutionMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(providerEvidence);
        ArgumentNullException.ThrowIfNull(runtimeResult);

        var status = providerError is not null
            ? StripeExecutionStatus.ProviderFailed
            : runtimeResult.OverallStatus == RuntimeOverallStatus.Succeeded
                ? StripeExecutionStatus.RuntimeSucceeded
                : StripeExecutionStatus.RuntimeFailed;
        var trace = new ExecutionTrace(
            new BridgeTraceId($"stripe-bridge:{runtimeResult.ExecutionContext.ExecutionId.Value:N}"),
            new ExecutionReference(providerEvidence.Reference.Value),
            new ExecutionCorrelation(providerEvidence.CorrelationId.Value),
            runtimeResult.ExecutionTrace.Entries.Select(entry => entry.Stage.ToString()).ToArray());

        return new StripeExecutionResult(
            providerEvidence,
            runtimeResult,
            status,
            trace,
            metadata ?? new ExecutionMetadata(),
            providerError);
    }
}
