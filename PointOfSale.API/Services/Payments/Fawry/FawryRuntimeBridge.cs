using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;

namespace PointOfSale.API.Services.Payments.Fawry;

public interface IFawryRuntimeBridge
{
    Task<FawryExecutionResult> ExecuteAsync(
        FawryExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class FawryRuntimeBridge : IFawryRuntimeBridge
{
    private readonly IFawryPaymentAdapter _adapter;
    private readonly PaymentRuntimeOrchestrator _orchestrator;

    public FawryRuntimeBridge(
        IFawryPaymentAdapter adapter,
        PaymentRuntimeOrchestrator orchestrator)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public async Task<FawryExecutionResult> ExecuteAsync(
        FawryExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var adapterResult = await CreateAdapterResultAsync(request, cancellationToken);
        var evidence = adapterResult.Evidence
            ?? FawryExecutionFactory.ProviderErrorEvidence(request.Action, adapterResult.Error);

        var runtimeResult = _orchestrator.Execute(
            request.Context,
            evidence,
            request.Action,
            request.Session,
            request.PriorEvidence,
            request.PriorTransitions,
            request.PriorTransactions,
            request.PriorSettlements);

        return FawryExecutionResultFactory.Create(
            evidence,
            runtimeResult,
            adapterResult.Error,
            request.Metadata);
    }

    private async Task<FawryPaymentAdapterResult> CreateAdapterResultAsync(
        FawryExecutionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var credentials = request.CredentialHandoff.Consume(
                request.Action,
                request.Context.ExecutionTimestamp);

            return await _adapter.InitializePaymentAsync(
                request.Action,
                credentials,
                cancellationToken);
        }
        catch (FawryCredentialHandoffException exception)
        {
            return FawryPaymentAdapterResult.Failed(
                CanonicalProviderError.Configuration(
                    FawryProviderReference.ProviderName,
                    exception.Code,
                    exception.Message));
        }
    }
}

public sealed record FawryExecutionRequest
{
    public RuntimeExecutionContext Context { get; init; }

    public PaymentAction Action { get; init; }

    public PaymentSession Session { get; init; }

    public FawryCredentialHandoff CredentialHandoff { get; init; }

    public IReadOnlyCollection<AdmittedEvidence>? PriorEvidence { get; init; }

    public IReadOnlyCollection<StateTransition>? PriorTransitions { get; init; }

    public IReadOnlyCollection<PaymentTransaction>? PriorTransactions { get; init; }

    public IReadOnlyCollection<PaymentSettlement>? PriorSettlements { get; init; }

    public FawryExecutionMetadata Metadata { get; init; }

    public FawryExecutionRequest()
    {
        Context = new RuntimeExecutionContext();
        Action = new PaymentAction();
        Session = new PaymentSession();
        CredentialHandoff = FawryCredentialHandoff.Missing();
        Metadata = new FawryExecutionMetadata();
    }

    public FawryExecutionRequest(
        RuntimeExecutionContext context,
        PaymentAction action,
        PaymentSession session,
        FawryCredentialHandoff credentialHandoff,
        FawryExecutionMetadata? metadata = null,
        IReadOnlyCollection<AdmittedEvidence>? priorEvidence = null,
        IReadOnlyCollection<StateTransition>? priorTransitions = null,
        IReadOnlyCollection<PaymentTransaction>? priorTransactions = null,
        IReadOnlyCollection<PaymentSettlement>? priorSettlements = null)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        CredentialHandoff = credentialHandoff ?? throw new ArgumentNullException(nameof(credentialHandoff));
        Metadata = metadata ?? new FawryExecutionMetadata();
        PriorEvidence = priorEvidence is null ? null : Array.AsReadOnly(priorEvidence.ToArray());
        PriorTransitions = priorTransitions is null ? null : Array.AsReadOnly(priorTransitions.ToArray());
        PriorTransactions = priorTransactions is null ? null : Array.AsReadOnly(priorTransactions.ToArray());
        PriorSettlements = priorSettlements is null ? null : Array.AsReadOnly(priorSettlements.ToArray());
    }
}

public sealed class FawryCredentialHandoff
{
    private readonly Guid _tenantId;
    private readonly Guid _actionId;
    private readonly DateTimeOffset _expiresAtUtc;
    private FawryPaymentCredentials? _credentials;
    private int _consumed;

    private FawryCredentialHandoff(
        Guid tenantId,
        Guid actionId,
        FawryPaymentCredentials? credentials,
        DateTimeOffset expiresAtUtc)
    {
        _tenantId = tenantId;
        _actionId = actionId;
        _credentials = credentials;
        _expiresAtUtc = expiresAtUtc.ToUniversalTime();
    }

    public bool IsConsumed => Volatile.Read(ref _consumed) != 0;

    public static FawryCredentialHandoff Create(
        Guid tenantId,
        PaymentAction action,
        FawryPaymentCredentials credentials,
        DateTimeOffset issuedAtUtc,
        TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(credentials);

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Fawry credential handoff tenant id is required.", nameof(tenantId));
        }

        credentials.Validate();
        var resolvedLifetime = lifetime ?? TimeSpan.FromMinutes(5);
        if (resolvedLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentException("Fawry credential handoff lifetime must be positive.", nameof(lifetime));
        }

        return new FawryCredentialHandoff(
            tenantId,
            action.Id.Value,
            credentials,
            issuedAtUtc.ToUniversalTime().Add(resolvedLifetime));
    }

    public static FawryCredentialHandoff Missing() =>
        new(Guid.Empty, Guid.Empty, null, DateTimeOffset.MinValue);

    public FawryPaymentCredentials Consume(
        PaymentAction action,
        DateTimeOffset executionTimestampUtc)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Interlocked.CompareExchange(ref _consumed, 1, 0) != 0)
        {
            throw new FawryCredentialHandoffException(
                "FAWRY_CREDENTIAL_HANDOFF_REUSED",
                "Fawry credential handoff was already consumed.");
        }

        var credentials = Interlocked.Exchange(ref _credentials, null);
        if (credentials is null)
        {
            throw new FawryCredentialHandoffException(
                "FAWRY_CREDENTIAL_HANDOFF_MISSING",
                "Fawry credential handoff is missing credentials.");
        }

        if (executionTimestampUtc.ToUniversalTime() > _expiresAtUtc)
        {
            throw new FawryCredentialHandoffException(
                "FAWRY_CREDENTIAL_HANDOFF_STALE",
                "Fawry credential handoff is stale.");
        }

        if (action.Id.Value != _actionId)
        {
            throw new FawryCredentialHandoffException(
                "FAWRY_CREDENTIAL_HANDOFF_OPERATION_MISMATCH",
                "Fawry credential handoff does not match the admitted operation.");
        }

        var actionTenantId = ResolveTenantId(action);
        if (actionTenantId != _tenantId)
        {
            throw new FawryCredentialHandoffException(
                "FAWRY_CREDENTIAL_HANDOFF_TENANT_MISMATCH",
                "Fawry credential handoff does not match the admitted tenant.");
        }

        return credentials;
    }

    private static Guid ResolveTenantId(PaymentAction action)
    {
        if (!action.Metadata.Values.TryGetValue("tenantId", out var value) ||
            string.IsNullOrWhiteSpace(value) ||
            !Guid.TryParse(value.Trim(), out var tenantId) ||
            tenantId == Guid.Empty)
        {
            throw new FawryCredentialHandoffException(
                "FAWRY_CREDENTIAL_HANDOFF_TENANT_MISSING",
                "Fawry credential handoff requires tenant identity metadata.");
        }

        return tenantId;
    }
}

public sealed class FawryCredentialHandoffException : Exception
{
    public FawryCredentialHandoffException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed record FawryExecutionResult
{
    public ProviderEvidence ProviderEvidence { get; init; }

    public RuntimeResult RuntimeResult { get; init; }

    public FawryExecutionStatus ExecutionStatus { get; init; }

    public FawryExecutionTrace ExecutionTrace { get; init; }

    public FawryExecutionMetadata ExecutionMetadata { get; init; }

    public CanonicalProviderError? ProviderError { get; init; }

    public FawryExecutionResult()
    {
        ProviderEvidence = new ProviderEvidence();
        RuntimeResult = new RuntimeResult();
        ExecutionTrace = new FawryExecutionTrace();
        ExecutionMetadata = new FawryExecutionMetadata();
    }

    public FawryExecutionResult(
        ProviderEvidence providerEvidence,
        RuntimeResult runtimeResult,
        FawryExecutionStatus executionStatus,
        FawryExecutionTrace executionTrace,
        FawryExecutionMetadata executionMetadata,
        CanonicalProviderError? providerError = null)
    {
        ProviderEvidence = providerEvidence ?? throw new ArgumentNullException(nameof(providerEvidence));
        RuntimeResult = runtimeResult ?? throw new ArgumentNullException(nameof(runtimeResult));
        ExecutionStatus = executionStatus == FawryExecutionStatus.Unknown
            ? throw new ArgumentException("Execution status is required.", nameof(executionStatus))
            : executionStatus;
        ExecutionTrace = executionTrace ?? throw new ArgumentNullException(nameof(executionTrace));
        ExecutionMetadata = executionMetadata ?? new FawryExecutionMetadata();
        ProviderError = providerError;
    }
}

public enum FawryExecutionStatus
{
    Unknown = 0,
    RuntimeSucceeded = 1,
    RuntimeFailed = 2,
    ProviderFailed = 3
}

public sealed record FawryExecutionReference(string Value)
{
    public FawryExecutionReference() : this(string.Empty)
    {
    }
}

public sealed record FawryBridgeTraceId(string Value)
{
    public FawryBridgeTraceId() : this(string.Empty)
    {
    }
}

public sealed record FawryExecutionCorrelation(string Value)
{
    public FawryExecutionCorrelation() : this(string.Empty)
    {
    }
}

public sealed record FawryExecutionMetadata
{
    public IReadOnlyDictionary<string, string> Values { get; init; }

    public FawryExecutionMetadata(IReadOnlyDictionary<string, string>? values = null)
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

public sealed record FawryExecutionTrace
{
    public FawryBridgeTraceId Id { get; init; }

    public FawryExecutionReference Reference { get; init; }

    public FawryExecutionCorrelation Correlation { get; init; }

    public IReadOnlyCollection<string> Entries { get; init; }

    public FawryExecutionTrace()
    {
        Id = new FawryBridgeTraceId(string.Empty);
        Reference = new FawryExecutionReference(string.Empty);
        Correlation = new FawryExecutionCorrelation(string.Empty);
        Entries = Array.AsReadOnly(Array.Empty<string>());
    }

    public FawryExecutionTrace(
        FawryBridgeTraceId id,
        FawryExecutionReference reference,
        FawryExecutionCorrelation correlation,
        IReadOnlyCollection<string>? entries = null)
    {
        RequireText(id.Value, nameof(id));
        RequireText(reference.Value, nameof(reference));
        RequireText(correlation.Value, nameof(correlation));

        Id = new FawryBridgeTraceId(id.Value.Trim());
        Reference = new FawryExecutionReference(reference.Value.Trim());
        Correlation = new FawryExecutionCorrelation(correlation.Value.Trim());
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

public static class FawryExecutionFactory
{
    public static FawryExecutionRequest CreateRequest(
        PaymentAction action,
        PaymentSession session,
        FawryPaymentCredentials credentials,
        DateTimeOffset executionTimestamp,
        FawryExecutionMetadata? metadata = null,
        IReadOnlyCollection<AdmittedEvidence>? priorEvidence = null,
        IReadOnlyCollection<StateTransition>? priorTransitions = null,
        IReadOnlyCollection<PaymentTransaction>? priorTransactions = null,
        IReadOnlyCollection<PaymentSettlement>? priorSettlements = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(credentials);

        var executionId = DeterministicGuid(
            "fawry-runtime-bridge-execution",
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
                ["bridge"] = "fawry-runtime-bridge",
                ["provider"] = FawryProviderReference.ProviderName
            }),
            new ExecutionTraceId($"fawry-runtime-bridge:{executionId:N}"));

        var credentialHandoff = FawryCredentialHandoff.Create(
            session.TenantId,
            action,
            credentials,
            executionTimestamp);

        return new FawryExecutionRequest(
            context,
            action,
            session,
            credentialHandoff,
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
            FawryProviderReference.ProviderName,
            "FAWRY_PROVIDER_ERROR_MISSING",
            "Fawry adapter did not return provider error details.");
        var source = string.Join(
            "|",
            "fawry-provider-error",
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
            new EvidenceReference($"fawry:error:{resolvedError.Code}"),
            new EvidenceSource(FawryProviderReference.ProviderName),
            new EvidenceTimestamp(timestamp),
            new EvidenceHash(Hash(source)),
            new ProviderPayloadReference($"fawry:error:{resolvedError.Code}"),
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

public static class FawryExecutionResultFactory
{
    public static FawryExecutionResult Create(
        ProviderEvidence providerEvidence,
        RuntimeResult runtimeResult,
        CanonicalProviderError? providerError = null,
        FawryExecutionMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(providerEvidence);
        ArgumentNullException.ThrowIfNull(runtimeResult);

        var status = providerError is not null
            ? FawryExecutionStatus.ProviderFailed
            : runtimeResult.OverallStatus == RuntimeOverallStatus.Succeeded
                ? FawryExecutionStatus.RuntimeSucceeded
                : FawryExecutionStatus.RuntimeFailed;
        var trace = new FawryExecutionTrace(
            new FawryBridgeTraceId($"fawry-bridge:{runtimeResult.ExecutionContext.ExecutionId.Value:N}"),
            new FawryExecutionReference(providerEvidence.Reference.Value),
            new FawryExecutionCorrelation(providerEvidence.CorrelationId.Value),
            runtimeResult.ExecutionTrace.Entries.Select(entry => entry.Stage.ToString()).ToArray());

        return new FawryExecutionResult(
            providerEvidence,
            runtimeResult,
            status,
            trace,
            metadata ?? new FawryExecutionMetadata(),
            providerError);
    }
}
