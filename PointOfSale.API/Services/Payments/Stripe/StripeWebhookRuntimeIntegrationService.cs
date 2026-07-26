using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Infrastructure.Data;
using Stripe;
using StripePaymentIntentEntity = Stripe.PaymentIntent;
using CorePaymentIntent = PointOfSale.Core.Entities.PaymentIntent;
using CorePaymentIntentStatus = PointOfSale.Core.Enums.PaymentIntentStatus;
using RuntimeCurrency = PointOfSale.Core.Payments.Runtime.Domain.Currency;
using RuntimeEvidenceType = PointOfSale.Core.Payments.Runtime.Domain.EvidenceType;
using RuntimePaymentStatus = PointOfSale.Core.Payments.Runtime.Domain.PaymentStatus;

namespace PointOfSale.API.Services.Payments.Stripe;

public interface IStripeWebhookRuntimeIntegrationService
{
    Task<StripeWebhookRuntimeAdmissionResult> EvaluateAndExecuteAsync(
        StripeWebhookRuntimeAdmissionRequest request,
        CancellationToken cancellationToken = default);

    Task PersistReplayRecordAsync(
        StripeWebhookRuntimeAdmissionResult result,
        Guid? paymentIntentId,
        Guid? orderId,
        CancellationToken cancellationToken = default);
}

public sealed class StripeWebhookRuntimeIntegrationService : IStripeWebhookRuntimeIntegrationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IStripeWebhookEvidenceAdapter _webhookEvidenceAdapter;
    private readonly ILogger<StripeWebhookRuntimeIntegrationService> _logger;

    public StripeWebhookRuntimeIntegrationService(
        ApplicationDbContext dbContext,
        IStripeWebhookEvidenceAdapter webhookEvidenceAdapter,
        ILogger<StripeWebhookRuntimeIntegrationService> logger)
    {
        _dbContext = dbContext;
        _webhookEvidenceAdapter = webhookEvidenceAdapter;
        _logger = logger;
    }

    public async Task<StripeWebhookRuntimeAdmissionResult> EvaluateAndExecuteAsync(
        StripeWebhookRuntimeAdmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var admission = StripeWebhookOutcomeAdmissionService.Evaluate(request.StripeEvent);
        if (!admission.AdmittedToRuntime)
        {
            return StripeWebhookRuntimeAdmissionResult.NotAdmitted(
                request.TenantId,
                request.StripeEvent.Id,
                request.StripeEvent.Type,
                admission);
        }

        var replayContext = await StripeWebhookPriorEvidenceLoader.LoadAsync(
            _dbContext,
            request.TenantId,
            request.StripeEvent.Id,
            cancellationToken);

        if (replayContext.IsDuplicate || replayContext.IsReplay)
        {
            return StripeWebhookRuntimeAdmissionResult.ReplayRejected(
                request.TenantId,
                request.StripeEvent.Id,
                request.StripeEvent.Type,
                admission,
                replayContext);
        }

        var localPaymentIntent = await ResolveLocalPaymentIntentAsync(
            request.TenantId,
            request.StripeEvent,
            cancellationToken);

        if (localPaymentIntent is null)
        {
            _logger.LogWarning(
                "Stripe webhook admitted but local payment intent was not found | TenantId: {TenantId} | EventId: {EventId} | EventType: {EventType}",
                request.TenantId,
                request.StripeEvent.Id,
                request.StripeEvent.Type);

            return StripeWebhookRuntimeAdmissionResult.NotAdmitted(
                request.TenantId,
                request.StripeEvent.Id,
                request.StripeEvent.Type,
                admission with
                {
                    Classification = StripeWebhookOutcomeClassification.Unknown,
                    ReasonCode = "LOCAL_PAYMENT_INTENT_NOT_FOUND"
                });
        }

        var runtimeInputs = BuildRuntimeInputs(request, localPaymentIntent);
        var adapterResult = await _webhookEvidenceAdapter.ProcessAsync(
            new StripeWebhookEvidenceRequest(
                request.RawPayload,
                request.SignatureHeader,
                request.WebhookSecret,
                runtimeInputs.Context,
                runtimeInputs.Action,
                runtimeInputs.Session,
                request.TimestampToleranceSeconds,
                replayContext.PriorEvidence),
            cancellationToken);

        return StripeWebhookRuntimeAdmissionResult.Admitted(
            request.TenantId,
            admission,
            replayContext,
            localPaymentIntent.Id,
            localPaymentIntent.OrderId,
            adapterResult);
    }

    public async Task PersistReplayRecordAsync(
        StripeWebhookRuntimeAdmissionResult result,
        Guid? paymentIntentId,
        Guid? orderId,
        CancellationToken cancellationToken = default)
    {
        if (!result.AdmittedToRuntime ||
            result.AdapterResult?.RuntimeResult.AdmittedEvidence is not { } evidence ||
            string.IsNullOrWhiteSpace(result.StripeEventId))
        {
            return;
        }

        var exists = await _dbContext.StripeWebhookReplayRecords
            .IgnoreQueryFilters()
            .AnyAsync(record =>
                record.TenantId == result.TenantId &&
                record.Provider == "stripe" &&
                record.ProviderEventId == result.StripeEventId,
                cancellationToken);

        if (exists)
        {
            return;
        }

        _dbContext.StripeWebhookReplayRecords.Add(new StripeWebhookReplayRecord
        {
            Id = Guid.NewGuid(),
            TenantId = result.TenantId,
            Provider = "stripe",
            ProviderEventId = result.StripeEventId,
            EventType = result.StripeEventType ?? string.Empty,
            EvidenceId = evidence.EvidenceId.Value,
            ActionId = evidence.ActionId.Value,
            EvidenceHash = evidence.EvidenceHash.Value,
            EvidenceReference = evidence.EvidenceReference.Value,
            ProviderReference = evidence.PayloadReference.Value,
            CorrelationId = evidence.CorrelationId.Value,
            ProcessingTimestampUtc = DateTime.UtcNow,
            PaymentIntentId = paymentIntentId ?? result.PaymentIntentId,
            OrderId = orderId ?? result.OrderId,
            AdmissionClassification = result.Admission.Classification.ToString(),
            CreatedAt = DateTime.UtcNow
        });
    }

    private async Task<CorePaymentIntent?> ResolveLocalPaymentIntentAsync(
        Guid tenantId,
        Event stripeEvent,
        CancellationToken cancellationToken)
    {
        var providerIntentId = StripeWebhookRuntimeObjectResolver.ResolvePaymentIntentId(stripeEvent);
        var orderId = StripeWebhookRuntimeObjectResolver.ResolveOrderId(stripeEvent);
        var chargeId = StripeWebhookRuntimeObjectResolver.ResolveChargeId(stripeEvent);

        return await _dbContext.PaymentIntents
            .Include(intent => intent.Order)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(intent =>
                intent.TenantId == tenantId &&
                !intent.IsDeleted &&
                intent.Provider == PaymentProvider.Stripe &&
                ((providerIntentId != null && intent.ProviderIntentId == providerIntentId) ||
                 (chargeId != null && intent.ProviderPaymentId == chargeId) ||
                 (orderId != null && intent.OrderId == orderId.Value)),
                cancellationToken);
    }

    private static StripeWebhookRuntimeInputs BuildRuntimeInputs(
        StripeWebhookRuntimeAdmissionRequest request,
        CorePaymentIntent intent)
    {
        var created = new DateTimeOffset(intent.CreatedAt == default ? DateTime.UtcNow : intent.CreatedAt, TimeSpan.Zero);
        var now = new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero);
        var sessionId = new PaymentSessionId(DeterministicGuid(
            "stripe-webhook-session",
            intent.TenantId.ToString("N"),
            intent.OrderId.ToString("N"),
            intent.Id.ToString("N")));
        var attemptId = new PaymentAttemptId(DeterministicGuid(
            "stripe-webhook-attempt",
            sessionId.Value.ToString("N"),
            intent.Id.ToString("N")));

        var runtimeEntryStatus = ResolveRuntimeEntryStatus(request.StripeEvent, intent.Status);

        var attempt = PaymentAttemptFactory.Create(
            attemptId,
            sessionId,
            new AttemptNumber(1),
            created,
            new ProviderReference(StripeProviderReference.ProviderName),
            runtimeEntryStatus,
            new FailureReason(intent.FailureReason ?? string.Empty),
            0,
            new PaymentMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["local_payment_intent_id"] = intent.Id.ToString("N"),
                ["stripe_payment_intent_id"] = intent.ProviderIntentId ?? string.Empty
            }));

        var session = PaymentSessionFactory.Create(
            sessionId,
            new PaymentId(intent.Id),
            intent.OrderId,
            intent.TenantId,
            intent.TenantId,
            new PaymentReference("STRIPE"),
            new Money(intent.Amount, new RuntimeCurrency(intent.Currency)),
            runtimeEntryStatus,
            created,
            now,
            new PaymentMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["payment_reference"] = intent.PaymentReference,
                ["stripe_payment_intent_id"] = intent.ProviderIntentId ?? string.Empty
            }),
            new AuditReferences(new[] { new CorrelationId(request.CorrelationId) }),
            new[] { attempt },
            Array.Empty<ProviderSession>());

        var actionId = new PaymentActionId(DeterministicGuid(
            "stripe-webhook-action",
            session.Id.Value.ToString("N"),
            attempt.Id.Value.ToString("N"),
            request.StripeEvent.Id));

        var action = PaymentActionFactory.Create(
            actionId,
            session.Id,
            attempt.Id,
            PaymentActionType.StatusRefresh,
            PaymentActionStatus.Completed,
            new ActionReference(intent.PaymentReference),
            new ActionCorrelationId(request.CorrelationId),
            created,
            now,
            metadata: new PaymentActionMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["application_boundary"] = nameof(StripeWebhookRuntimeIntegrationService),
                ["stripe_event_id"] = request.StripeEvent.Id,
                ["stripe_event_type"] = request.StripeEvent.Type,
                ["local_payment_intent_id"] = intent.Id.ToString("N"),
                ["order_id"] = intent.OrderId.ToString("N")
            }));

        var context = RuntimeExecutionFactory.Create(
            new CorrelationId(request.CorrelationId),
            session.Id,
            new ExecutionId(DeterministicGuid(
                "stripe-webhook-runtime",
                request.TenantId.ToString("N"),
                request.StripeEvent.Id,
                request.CorrelationId)),
            now,
            new PipelineMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["application_boundary"] = nameof(StripeWebhookRuntimeIntegrationService),
                ["stripe_event_id"] = request.StripeEvent.Id,
                ["stripe_event_type"] = request.StripeEvent.Type
            }));

        return new StripeWebhookRuntimeInputs(context, session, action);
    }

    private static RuntimePaymentStatus MapPaymentStatus(CorePaymentIntentStatus status)
    {
        return status switch
        {
            CorePaymentIntentStatus.Paid => RuntimePaymentStatus.Approved,
            CorePaymentIntentStatus.Authorized => RuntimePaymentStatus.Authorized,
            CorePaymentIntentStatus.Failed => RuntimePaymentStatus.Failed,
            CorePaymentIntentStatus.Cancelled => RuntimePaymentStatus.Cancelled,
            _ => RuntimePaymentStatus.Pending
        };
    }

    private static RuntimePaymentStatus ResolveRuntimeEntryStatus(
        Event stripeEvent,
        CorePaymentIntentStatus localStatus)
    {
        return stripeEvent.Type switch
        {
            "payment_intent.succeeded" => RuntimePaymentStatus.Approved,
            "checkout.session.completed" => RuntimePaymentStatus.Approved,
            "charge.refunded" => RuntimePaymentStatus.RefundPending,
            _ => MapPaymentStatus(localStatus)
        };
    }

    private static Guid DeterministicGuid(params string[] components)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', components)));
        var bytes = hash.Take(16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

public static class StripeWebhookOutcomeAdmissionService
{
    public static StripeWebhookOutcomeAdmission Evaluate(Event stripeEvent)
    {
        var classification = stripeEvent.Type switch
        {
            "payment_intent.succeeded" => StripeWebhookOutcomeClassification.Terminal,
            "payment_intent.payment_failed" => StripeWebhookOutcomeClassification.Terminal,
            "payment_intent.canceled" => StripeWebhookOutcomeClassification.Terminal,
            "charge.refunded" => StripeWebhookOutcomeClassification.Terminal,
            "payment_intent.processing" => StripeWebhookOutcomeClassification.NonTerminal,
            "payment_intent.requires_action" => StripeWebhookOutcomeClassification.NonTerminal,
            "payment_intent.created" => StripeWebhookOutcomeClassification.NonTerminal,
            "checkout.session.completed" => ClassifyCheckoutSession(stripeEvent),
            "charge.dispute.created" => StripeWebhookOutcomeClassification.Disputed,
            "charge.dispute.closed" => StripeWebhookOutcomeClassification.Disputed,
            _ => StripeWebhookOutcomeClassification.Ignored
        };

        var admittedToRuntime = stripeEvent.Type is "payment_intent.succeeded" or "charge.refunded"
            || (stripeEvent.Type == "checkout.session.completed"
                && classification == StripeWebhookOutcomeClassification.Terminal);
        var reasonCode = stripeEvent.Type is "payment_intent.payment_failed" or "payment_intent.canceled"
            ? "TERMINAL_NEGATIVE_APPLICATION_LIFECYCLE"
            : classification.ToString().ToUpperInvariant();

        return new StripeWebhookOutcomeAdmission(
            classification,
            admittedToRuntime,
            reasonCode);
    }

    private static StripeWebhookOutcomeClassification ClassifyCheckoutSession(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not global::Stripe.Checkout.Session session)
        {
            return StripeWebhookOutcomeClassification.Unknown;
        }

        return string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
            ? StripeWebhookOutcomeClassification.Terminal
            : StripeWebhookOutcomeClassification.NonTerminal;
    }
}

public static class StripeWebhookPriorEvidenceLoader
{
    public static async Task<StripeWebhookReplayContext> LoadAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        string providerEventId,
        CancellationToken cancellationToken)
    {
        var duplicate = await dbContext.StripeWebhookReplayRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(record => record.TenantId == tenantId && record.Provider == "stripe")
            .AnyAsync(record => record.ProviderEventId == providerEventId, cancellationToken);

        return new StripeWebhookReplayContext(
            Array.Empty<AdmittedEvidence>(),
            duplicate,
            duplicate,
            duplicate ? "PROVIDER_EVENT_ALREADY_PROCESSED" : "NO_REPLAY");
    }
}

public static class StripeWebhookRuntimeObjectResolver
{
    public static string? ResolvePaymentIntentId(Event stripeEvent)
    {
        return stripeEvent.Data.Object switch
        {
            StripePaymentIntentEntity paymentIntent => paymentIntent.Id,
            Charge charge => charge.PaymentIntentId,
            _ => null
        };
    }

    public static string? ResolveChargeId(Event stripeEvent)
    {
        return stripeEvent.Data.Object is Charge charge ? charge.Id : null;
    }

    public static Guid? ResolveOrderId(Event stripeEvent)
    {
        var metadata = stripeEvent.Data.Object switch
        {
            StripePaymentIntentEntity paymentIntent => paymentIntent.Metadata,
            Charge charge => charge.Metadata,
            global::Stripe.Checkout.Session session => session.Metadata,
            _ => null
        };

        if (metadata is not null &&
            metadata.TryGetValue("orderId", out var orderIdValue) &&
            Guid.TryParse(orderIdValue, out var orderId))
        {
            return orderId;
        }

        return null;
    }
}

public sealed record StripeWebhookRuntimeAdmissionRequest(
    Guid TenantId,
    Event StripeEvent,
    string RawPayload,
    string SignatureHeader,
    string WebhookSecret,
    string CorrelationId,
    long TimestampToleranceSeconds = StripeWebhookSignatureVerifier.DefaultToleranceSeconds);

public sealed record StripeWebhookRuntimeAdmissionResult
{
    public Guid TenantId { get; init; }

    public bool AdmittedToRuntime { get; init; }

    public bool RejectedAsReplay { get; init; }

    public string ProviderEventId { get; init; } = string.Empty;

    public string EventType { get; init; } = string.Empty;

    public StripeWebhookOutcomeAdmission Admission { get; init; } = new();

    public StripeWebhookReplayContext ReplayContext { get; init; } = new();

    public Guid? PaymentIntentId { get; init; }

    public Guid? OrderId { get; init; }

    public StripeWebhookEvidenceAdapterResult? AdapterResult { get; init; }

    public string? StripeEventId => AdapterResult?.StripeEventId ?? ProviderEventId;

    public string? StripeEventType => AdapterResult?.StripeEventType ?? EventType;

    public static StripeWebhookRuntimeAdmissionResult NotAdmitted(
        Guid tenantId,
        string providerEventId,
        string eventType,
        StripeWebhookOutcomeAdmission admission)
    {
        return new StripeWebhookRuntimeAdmissionResult
        {
            TenantId = tenantId,
            ProviderEventId = providerEventId,
            EventType = eventType,
            Admission = admission
        };
    }

    public static StripeWebhookRuntimeAdmissionResult ReplayRejected(
        Guid tenantId,
        string providerEventId,
        string eventType,
        StripeWebhookOutcomeAdmission admission,
        StripeWebhookReplayContext replayContext)
    {
        return new StripeWebhookRuntimeAdmissionResult
        {
            TenantId = tenantId,
            ProviderEventId = providerEventId,
            EventType = eventType,
            Admission = admission,
            ReplayContext = replayContext,
            RejectedAsReplay = true
        };
    }

    public static StripeWebhookRuntimeAdmissionResult Admitted(
        Guid tenantId,
        StripeWebhookOutcomeAdmission admission,
        StripeWebhookReplayContext replayContext,
        Guid paymentIntentId,
        Guid orderId,
        StripeWebhookEvidenceAdapterResult adapterResult)
    {
        return new StripeWebhookRuntimeAdmissionResult
        {
            TenantId = tenantId,
            ProviderEventId = adapterResult.StripeEventId ?? adapterResult.ProviderEvidence.Reference.Value,
            EventType = adapterResult.StripeEventType ?? string.Empty,
            Admission = admission,
            ReplayContext = replayContext,
            PaymentIntentId = paymentIntentId,
            OrderId = orderId,
            AdapterResult = adapterResult,
            AdmittedToRuntime = adapterResult.Succeeded
                && adapterResult.RuntimeResult.OverallStatus == RuntimeOverallStatus.Succeeded
        };
    }
}

public sealed record StripeWebhookOutcomeAdmission(
    StripeWebhookOutcomeClassification Classification,
    bool AdmittedToRuntime,
    string ReasonCode)
{
    public StripeWebhookOutcomeAdmission()
        : this(StripeWebhookOutcomeClassification.Unknown, false, "UNKNOWN")
    {
    }
}

public sealed record StripeWebhookReplayContext(
    IReadOnlyCollection<AdmittedEvidence> PriorEvidence,
    bool IsDuplicate,
    bool IsReplay,
    string ReasonCode)
{
    public string? DuplicateProviderEventId { get; init; }

    public StripeWebhookReplayContext()
        : this(Array.Empty<AdmittedEvidence>(), false, false, "NO_REPLAY")
    {
    }
}

public enum StripeWebhookOutcomeClassification
{
    Unknown = 0,
    Terminal = 1,
    NonTerminal = 2,
    Ignored = 3,
    Duplicate = 4,
    Replay = 5,
    Disputed = 6
}

internal sealed record StripeWebhookRuntimeInputs(
    RuntimeExecutionContext Context,
    PaymentSession Session,
    PaymentAction Action);
