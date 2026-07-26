using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;
using Stripe;
using StripeEvent = Stripe.Event;
using StripeException = Stripe.StripeException;

namespace PointOfSale.API.Services.Payments.Stripe;

public interface IStripeWebhookEvidenceAdapter
{
    Task<StripeWebhookEvidenceAdapterResult> ProcessAsync(
        StripeWebhookEvidenceRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class StripeWebhookEvidenceAdapter : IStripeWebhookEvidenceAdapter
{
    private readonly PaymentRuntimeOrchestrator _orchestrator;

    public StripeWebhookEvidenceAdapter(PaymentRuntimeOrchestrator orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public Task<StripeWebhookEvidenceAdapterResult> ProcessAsync(
        StripeWebhookEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var verification = StripeWebhookSignatureVerifier.Verify(request);
        if (!verification.Succeeded)
        {
            return Task.FromResult(ExecuteProviderError(request, verification.Error));
        }

        var translation = StripeWebhookEvidenceTranslator.Translate(
            request.Action,
            verification.Event!,
            request.RawPayload);
        if (!translation.Succeeded)
        {
            return Task.FromResult(ExecuteProviderError(request, translation.Error));
        }

        var runtimeResult = ExecuteRuntime(request, translation.Evidence!);
        return Task.FromResult(StripeWebhookEvidenceAdapterResult.Success(
            translation.Evidence!,
            runtimeResult,
            verification.Event!,
            translation.Translation));
    }

    private StripeWebhookEvidenceAdapterResult ExecuteProviderError(
        StripeWebhookEvidenceRequest request,
        CanonicalProviderError? error)
    {
        var resolvedError = error ?? CanonicalProviderError.Configuration(
            "STRIPE_WEBHOOK_ERROR_MISSING",
            "Stripe webhook adapter failed without error details.");
        var evidence = StripeWebhookEvidenceTranslator.ProviderErrorEvidence(
            request.Action,
            request.RawPayload,
            resolvedError);
        var runtimeResult = ExecuteRuntime(request, evidence);

        return StripeWebhookEvidenceAdapterResult.Failed(evidence, runtimeResult, resolvedError);
    }

    private RuntimeResult ExecuteRuntime(
        StripeWebhookEvidenceRequest request,
        ProviderEvidence evidence)
    {
        return _orchestrator.Execute(
            request.Context,
            evidence,
            request.Action,
            request.Session,
            request.PriorEvidence,
            request.PriorTransitions,
            request.PriorTransactions,
            request.PriorSettlements);
    }
}

public sealed record StripeWebhookEvidenceRequest
{
    public string RawPayload { get; init; }

    public string SignatureHeader { get; init; }

    public string WebhookSecret { get; init; }

    public long TimestampToleranceSeconds { get; init; }

    public RuntimeExecutionContext Context { get; init; }

    public PaymentAction Action { get; init; }

    public PaymentSession Session { get; init; }

    public IReadOnlyCollection<AdmittedEvidence>? PriorEvidence { get; init; }

    public IReadOnlyCollection<StateTransition>? PriorTransitions { get; init; }

    public IReadOnlyCollection<PaymentTransaction>? PriorTransactions { get; init; }

    public IReadOnlyCollection<PaymentSettlement>? PriorSettlements { get; init; }

    public StripeWebhookEvidenceRequest()
    {
        RawPayload = string.Empty;
        SignatureHeader = string.Empty;
        WebhookSecret = string.Empty;
        TimestampToleranceSeconds = StripeWebhookSignatureVerifier.DefaultToleranceSeconds;
        Context = new RuntimeExecutionContext();
        Action = new PaymentAction();
        Session = new PaymentSession();
    }

    public StripeWebhookEvidenceRequest(
        string rawPayload,
        string signatureHeader,
        string webhookSecret,
        RuntimeExecutionContext context,
        PaymentAction action,
        PaymentSession session,
        long timestampToleranceSeconds = StripeWebhookSignatureVerifier.DefaultToleranceSeconds,
        IReadOnlyCollection<AdmittedEvidence>? priorEvidence = null,
        IReadOnlyCollection<StateTransition>? priorTransitions = null,
        IReadOnlyCollection<PaymentTransaction>? priorTransactions = null,
        IReadOnlyCollection<PaymentSettlement>? priorSettlements = null)
    {
        RawPayload = rawPayload ?? string.Empty;
        SignatureHeader = signatureHeader ?? string.Empty;
        WebhookSecret = webhookSecret ?? string.Empty;
        TimestampToleranceSeconds = timestampToleranceSeconds;
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        PriorEvidence = priorEvidence is null ? null : Array.AsReadOnly(priorEvidence.ToArray());
        PriorTransitions = priorTransitions is null ? null : Array.AsReadOnly(priorTransitions.ToArray());
        PriorTransactions = priorTransactions is null ? null : Array.AsReadOnly(priorTransactions.ToArray());
        PriorSettlements = priorSettlements is null ? null : Array.AsReadOnly(priorSettlements.ToArray());
    }
}

public sealed record StripeWebhookEvidenceAdapterResult
{
    public bool Succeeded { get; init; }

    public ProviderEvidence ProviderEvidence { get; init; }

    public RuntimeResult RuntimeResult { get; init; }

    public CanonicalProviderError? Error { get; init; }

    public string? StripeEventId { get; init; }

    public string? StripeEventType { get; init; }

    public WebhookTranslationMetadata Translation { get; init; }

    public StripeWebhookEvidenceAdapterResult()
    {
        ProviderEvidence = new ProviderEvidence();
        RuntimeResult = new RuntimeResult();
        Translation = new WebhookTranslationMetadata();
    }

    private StripeWebhookEvidenceAdapterResult(
        bool succeeded,
        ProviderEvidence providerEvidence,
        RuntimeResult runtimeResult,
        CanonicalProviderError? error,
        string? stripeEventId,
        string? stripeEventType,
        WebhookTranslationMetadata? translation)
    {
        Succeeded = succeeded;
        ProviderEvidence = providerEvidence ?? throw new ArgumentNullException(nameof(providerEvidence));
        RuntimeResult = runtimeResult ?? throw new ArgumentNullException(nameof(runtimeResult));
        Error = error;
        StripeEventId = stripeEventId;
        StripeEventType = stripeEventType;
        Translation = translation ?? new WebhookTranslationMetadata();
    }

    public static StripeWebhookEvidenceAdapterResult Success(
        ProviderEvidence evidence,
        RuntimeResult runtimeResult,
        StripeEvent stripeEvent,
        WebhookTranslationMetadata translation)
    {
        return new StripeWebhookEvidenceAdapterResult(
            true,
            evidence,
            runtimeResult,
            null,
            stripeEvent.Id,
            stripeEvent.Type,
            translation);
    }

    public static StripeWebhookEvidenceAdapterResult Failed(
        ProviderEvidence evidence,
        RuntimeResult runtimeResult,
        CanonicalProviderError error)
    {
        return new StripeWebhookEvidenceAdapterResult(
            false,
            evidence,
            runtimeResult,
            error,
            null,
            null,
            null);
    }
}

public sealed record WebhookTranslationMetadata
{
    public string EventId { get; init; }

    public string EventType { get; init; }

    public string ProviderReference { get; init; }

    public string StripeObjectId { get; init; }

    public string PayloadHash { get; init; }

    public string EvidenceHash { get; init; }

    public WebhookTranslationMetadata()
    {
        EventId = string.Empty;
        EventType = string.Empty;
        ProviderReference = string.Empty;
        StripeObjectId = string.Empty;
        PayloadHash = string.Empty;
        EvidenceHash = string.Empty;
    }

    public WebhookTranslationMetadata(
        string eventId,
        string eventType,
        string providerReference,
        string stripeObjectId,
        string payloadHash,
        string evidenceHash)
    {
        EventId = eventId;
        EventType = eventType;
        ProviderReference = providerReference;
        StripeObjectId = stripeObjectId;
        PayloadHash = payloadHash;
        EvidenceHash = evidenceHash;
    }
}

public static class StripeWebhookSignatureVerifier
{
    public const long DefaultToleranceSeconds = 300;

    public static StripeWebhookVerificationResult Verify(StripeWebhookEvidenceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SignatureHeader))
        {
            return StripeWebhookVerificationResult.Failed(
                CanonicalProviderError.Validation("STRIPE_WEBHOOK_SIGNATURE_MISSING", "Stripe-Signature header is required."));
        }

        if (string.IsNullOrWhiteSpace(request.WebhookSecret))
        {
            return StripeWebhookVerificationResult.Failed(
                CanonicalProviderError.Configuration("STRIPE_WEBHOOK_SECRET_MISSING", "Stripe webhook secret is required."));
        }

        if (request.TimestampToleranceSeconds <= 0)
        {
            return StripeWebhookVerificationResult.Failed(
                CanonicalProviderError.Configuration("STRIPE_WEBHOOK_TOLERANCE_INVALID", "Stripe webhook timestamp tolerance must be positive."));
        }

        try
        {
            var stripeEvent = EventUtility.ConstructEvent(
                request.RawPayload,
                request.SignatureHeader,
                request.WebhookSecret,
                request.TimestampToleranceSeconds);

            return StripeWebhookVerificationResult.Success(stripeEvent);
        }
        catch (JsonException exception)
        {
            return StripeWebhookVerificationResult.Failed(
                CanonicalProviderError.Validation("STRIPE_WEBHOOK_MALFORMED_PAYLOAD", exception.Message));
        }
        catch (StripeException exception)
        {
            return StripeWebhookVerificationResult.Failed(
                StripeWebhookErrorTranslator.Translate(exception));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return StripeWebhookVerificationResult.Failed(
                CanonicalProviderError.Validation("STRIPE_WEBHOOK_MALFORMED_PAYLOAD", exception.Message));
        }
    }
}

public sealed record StripeWebhookVerificationResult
{
    public bool Succeeded { get; init; }

    public StripeEvent? Event { get; init; }

    public CanonicalProviderError? Error { get; init; }

    private StripeWebhookVerificationResult()
    {
    }

    public static StripeWebhookVerificationResult Success(StripeEvent stripeEvent)
    {
        return new StripeWebhookVerificationResult
        {
            Succeeded = true,
            Event = stripeEvent
        };
    }

    public static StripeWebhookVerificationResult Failed(CanonicalProviderError error)
    {
        return new StripeWebhookVerificationResult
        {
            Succeeded = false,
            Error = error
        };
    }
}

public static class StripeWebhookEvidenceTranslator
{
    private static readonly ISet<string> SupportedEvents = new HashSet<string>(StringComparer.Ordinal)
    {
        "payment_intent.created",
        "payment_intent.processing",
        "payment_intent.requires_action",
        "payment_intent.succeeded",
        "payment_intent.payment_failed",
        "payment_intent.canceled",
        "charge.succeeded",
        "charge.refunded",
        "charge.dispute.created",
        "charge.dispute.closed",
        "checkout.session.completed"
    };

    public static StripeWebhookTranslationResult Translate(
        PaymentAction action,
        StripeEvent stripeEvent,
        string rawPayload)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(stripeEvent);

        if (string.IsNullOrWhiteSpace(stripeEvent.Id) || string.IsNullOrWhiteSpace(stripeEvent.Type))
        {
            return StripeWebhookTranslationResult.Failed(
                CanonicalProviderError.Validation("STRIPE_WEBHOOK_EVENT_INVALID", "Stripe event id and type are required."));
        }

        if (!SupportedEvents.Contains(stripeEvent.Type))
        {
            return StripeWebhookTranslationResult.Failed(
                CanonicalProviderError.Validation("STRIPE_WEBHOOK_EVENT_UNKNOWN", $"Stripe event '{stripeEvent.Type}' is not mapped to canonical evidence."));
        }

        var stripeObjectId = StripeWebhookObjectIdResolver.Resolve(stripeEvent, rawPayload);
        if (string.IsNullOrWhiteSpace(stripeObjectId))
        {
            return StripeWebhookTranslationResult.Failed(
                CanonicalProviderError.Validation("STRIPE_WEBHOOK_OBJECT_ID_MISSING", "Stripe event object id is required."));
        }

        var occurred = stripeEvent.Created == default
            ? action.LastUpdatedUtc
            : new DateTimeOffset(DateTime.SpecifyKind(stripeEvent.Created, DateTimeKind.Utc));
        var payloadHash = StripeHash.Create(rawPayload ?? string.Empty);
        var evidenceHash = StripeHash.Create(
            stripeEvent.Id,
            stripeEvent.Type,
            stripeObjectId,
            occurred.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            payloadHash);
        var evidenceId = StripeDeterministicId.Create(
            "stripe-webhook-evidence",
            stripeEvent.Id,
            stripeEvent.Type,
            stripeObjectId,
            evidenceHash);
        var providerReference = StripeWebhookProviderReference.Resolve(stripeEvent.Type, stripeObjectId);
        var metadata = StripeWebhookMetadataFactory.Create(
            action,
            stripeEvent,
            stripeObjectId,
            payloadHash,
            evidenceHash);

        var evidence = ProviderEvidenceFactory.Create(
            new EvidenceId(evidenceId),
            action.Id,
            EvidenceType.Webhook,
            EvidenceStatus.Recorded,
            new EvidenceReference(stripeEvent.Id),
            new EvidenceSource(StripeProviderReference.ProviderName),
            new EvidenceTimestamp(occurred),
            new EvidenceHash(evidenceHash),
            new ProviderPayloadReference(providerReference),
            action.CorrelationId,
            new EvidenceMetadata(metadata));
        var translation = new WebhookTranslationMetadata(
            stripeEvent.Id,
            stripeEvent.Type,
            providerReference,
            stripeObjectId,
            payloadHash,
            evidenceHash);

        return StripeWebhookTranslationResult.Success(evidence, translation);
    }

    public static ProviderEvidence ProviderErrorEvidence(
        PaymentAction action,
        string rawPayload,
        CanonicalProviderError error)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(error);

        var payloadHash = StripeHash.Create(rawPayload ?? string.Empty);
        var source = string.Join(
            "|",
            "stripe-webhook-provider-error",
            action.Id.Value.ToString("N"),
            action.CorrelationId.Value,
            error.Type,
            error.Code,
            payloadHash);
        var evidenceId = StripeDeterministicId.Create(source);
        var timestamp = action.CreatedUtc.AddTicks(-1);
        var metadata = error.Metadata
            .Concat(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["adapter"] = "StripeWebhookEvidenceAdapter",
                ["provider_error_type"] = error.Type.ToString(),
                ["provider_error_code"] = error.Code,
                ["provider_error_message"] = error.Message,
                ["event_payload_hash"] = payloadHash,
                ["correlation_id"] = action.CorrelationId.Value
            })
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

        return ProviderEvidenceFactory.Create(
            new EvidenceId(evidenceId),
            action.Id,
            EvidenceType.Webhook,
            EvidenceStatus.Rejected,
            new EvidenceReference($"stripe:webhook:error:{error.Code}"),
            new EvidenceSource(StripeProviderReference.ProviderName),
            new EvidenceTimestamp(timestamp),
            new EvidenceHash(StripeHash.Create(source)),
            new ProviderPayloadReference($"stripe:webhook:error:{error.Code}"),
            action.CorrelationId,
            new EvidenceMetadata(metadata));
    }
}

public sealed record StripeWebhookTranslationResult
{
    public bool Succeeded { get; init; }

    public ProviderEvidence? Evidence { get; init; }

    public WebhookTranslationMetadata Translation { get; init; }

    public CanonicalProviderError? Error { get; init; }

    private StripeWebhookTranslationResult()
    {
        Translation = new WebhookTranslationMetadata();
    }

    public static StripeWebhookTranslationResult Success(
        ProviderEvidence evidence,
        WebhookTranslationMetadata translation)
    {
        return new StripeWebhookTranslationResult
        {
            Succeeded = true,
            Evidence = evidence,
            Translation = translation
        };
    }

    public static StripeWebhookTranslationResult Failed(CanonicalProviderError error)
    {
        return new StripeWebhookTranslationResult
        {
            Succeeded = false,
            Error = error
        };
    }
}

public static class StripeWebhookErrorTranslator
{
    public static CanonicalProviderError Translate(StripeException exception)
    {
        var message = exception.Message ?? "Stripe webhook verification failed.";
        var code = message.Contains("timestamp", StringComparison.OrdinalIgnoreCase)
            ? "STRIPE_WEBHOOK_TIMESTAMP_EXPIRED"
            : "STRIPE_WEBHOOK_SIGNATURE_INVALID";
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = StripeProviderReference.ProviderName,
            ["stripe_exception"] = exception.GetType().Name
        };

        return new CanonicalProviderError(
            CanonicalProviderErrorType.Validation,
            code,
            message,
            null,
            metadata);
    }
}

public static class StripeWebhookObjectIdResolver
{
    public static string Resolve(StripeEvent stripeEvent, string rawPayload)
    {
        return stripeEvent.Data?.Object switch
        {
            PaymentIntent paymentIntent => paymentIntent.Id,
            Charge charge => charge.Id,
            Dispute dispute => dispute.Id,
            global::Stripe.Checkout.Session session => session.Id,
            _ => ResolveFromJson(rawPayload)
        } ?? string.Empty;
    }

    private static string ResolveFromJson(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return string.Empty;
        }

        using var document = JsonDocument.Parse(rawPayload);
        return document.RootElement
            .GetProperty("data")
            .GetProperty("object")
            .GetProperty("id")
            .GetString() ?? string.Empty;
    }
}

public static class StripeWebhookProviderReference
{
    public static string Resolve(string eventType, string objectId)
    {
        var prefix = eventType.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "event";
        return $"stripe:{prefix}:{objectId}";
    }
}

public static class StripeWebhookMetadataFactory
{
    public static IReadOnlyDictionary<string, string> Create(
        PaymentAction action,
        StripeEvent stripeEvent,
        string stripeObjectId,
        string payloadHash,
        string evidenceHash)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["adapter"] = "StripeWebhookEvidenceAdapter",
            ["provider"] = StripeProviderReference.ProviderName,
            ["event_id"] = stripeEvent.Id,
            ["provider_event_id"] = stripeEvent.Id,
            ["event_type"] = stripeEvent.Type,
            ["provider_reference"] = stripeObjectId,
            ["stripe_object_id"] = stripeObjectId,
            ["occurred_timestamp"] = new DateTimeOffset(DateTime.SpecifyKind(stripeEvent.Created, DateTimeKind.Utc)).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["correlation_id"] = action.CorrelationId.Value,
            ["event_payload_hash"] = payloadHash,
            ["evidence_hash"] = evidenceHash,
            ["evidence_id_source"] = "stripe_event_id|event_type|stripe_object_id|evidence_hash"
        };

        values["provider_metadata_livemode"] = stripeEvent.Livemode.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(stripeEvent.ApiVersion))
        {
            values["provider_metadata_api_version"] = stripeEvent.ApiVersion;
        }

        if (!string.IsNullOrWhiteSpace(stripeEvent.Account))
        {
            values["provider_metadata_account"] = stripeEvent.Account;
        }

        foreach (var (key, value) in ExtractStripeMetadata(stripeEvent))
        {
            values[$"stripe_metadata_{key}"] = value;
        }

        return new ReadOnlyDictionary<string, string>(
            values
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.Ordinal));
    }

    private static IReadOnlyDictionary<string, string> ExtractStripeMetadata(StripeEvent stripeEvent)
    {
        return stripeEvent.Data?.Object switch
        {
            PaymentIntent paymentIntent => Normalize(paymentIntent.Metadata),
            Charge charge => Normalize(charge.Metadata),
            Dispute dispute => Normalize(dispute.Metadata),
            global::Stripe.Checkout.Session session => Normalize(session.Metadata),
            _ => new ReadOnlyDictionary<string, string>(new Dictionary<string, string>())
        };
    }

    private static IReadOnlyDictionary<string, string> Normalize(IDictionary<string, string>? values)
    {
        return new ReadOnlyDictionary<string, string>(
            (values ?? new Dictionary<string, string>())
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.Ordinal));
    }
}
