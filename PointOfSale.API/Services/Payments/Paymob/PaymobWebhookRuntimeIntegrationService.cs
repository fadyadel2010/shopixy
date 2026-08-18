using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;
using PointOfSale.Infrastructure.Data;
using CorePaymentIntent = PointOfSale.Core.Entities.PaymentIntent;
using CorePaymentIntentStatus = PointOfSale.Core.Enums.PaymentIntentStatus;
using CorePaymentProvider = PointOfSale.Core.Enums.PaymentProvider;
using RuntimeCurrency = PointOfSale.Core.Payments.Runtime.Domain.Currency;
using RuntimePaymentStatus = PointOfSale.Core.Payments.Runtime.Domain.PaymentStatus;

namespace PointOfSale.API.Services.Payments.Paymob;

public interface IPaymobWebhookRuntimeIntegrationService
{
    Task<PaymobWebhookRuntimeAdmissionResult> EvaluateAndExecuteAsync(
        PaymobWebhookRuntimeAdmissionRequest request,
        CancellationToken cancellationToken = default);

    Task PersistReplayRecordAsync(
        PaymobWebhookRuntimeAdmissionResult result,
        Guid? paymentIntentId,
        Guid? orderId,
        CancellationToken cancellationToken = default);
}

public sealed class PaymobWebhookRuntimeIntegrationService : IPaymobWebhookRuntimeIntegrationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPaymobWebhookEvidenceAdapter _webhookEvidenceAdapter;
    private readonly ILogger<PaymobWebhookRuntimeIntegrationService> _logger;

    public PaymobWebhookRuntimeIntegrationService(
        ApplicationDbContext dbContext,
        IPaymobWebhookEvidenceAdapter webhookEvidenceAdapter,
        ILogger<PaymobWebhookRuntimeIntegrationService> logger)
    {
        _dbContext = dbContext;
        _webhookEvidenceAdapter = webhookEvidenceAdapter;
        _logger = logger;
    }

    public async Task<PaymobWebhookRuntimeAdmissionResult> EvaluateAndExecuteAsync(
        PaymobWebhookRuntimeAdmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var verification = PaymobWebhookHmacVerifier.Verify(new PaymobWebhookEvidenceRequest
        {
            RawPayload = request.RawPayload,
            HmacSignature = request.HmacSignature,
            HmacSecret = request.HmacSecret,
            TimestampTolerance = request.TimestampTolerance,
            ReceivedAtUtc = request.ReceivedAtUtc
        });

        if (!verification.Succeeded || verification.Callback is null)
        {
            return await ExecuteInvalidCallbackAsync(
                request,
                verification.Error?.Code ?? "PAYMOB_WEBHOOK_INVALID_CALLBACK",
                cancellationToken);
        }

        var callback = verification.Callback;
        var providerEventId = BuildProviderEventId(callback);
        var admission = PaymobWebhookOutcomeAdmissionService.Evaluate(callback);
        var replayContext = await PaymobWebhookPriorEvidenceLoader.LoadAsync(
            _dbContext,
            request.TenantId,
            providerEventId,
            callback.ProviderReference,
            cancellationToken);

        var duplicate = await _dbContext.PaymentWebhookLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(log =>
                log.TenantId == request.TenantId &&
                log.Provider == "paymob" &&
                log.ProviderEventId == providerEventId &&
                (log.ProcessingStatus == WebhookProcessingStatus.Completed ||
                 log.ProcessingStatus == WebhookProcessingStatus.Skipped),
                cancellationToken);

        var localPaymentIntent = await ResolveLocalPaymentIntentAsync(
            request.TenantId,
            callback,
            cancellationToken);

        if (localPaymentIntent is null)
        {
            _logger.LogWarning(
                "Paymob webhook admitted to application boundary but local payment intent was not found | TenantId: {TenantId} | TransactionId: {TransactionId} | ProviderReference: {ProviderReference}",
                request.TenantId,
                providerEventId,
                callback.ProviderReference);

            return PaymobWebhookRuntimeAdmissionResult.NotAdmitted(
                request.TenantId,
                providerEventId,
                callback.EventType,
                admission with
                {
                    Classification = PaymobWebhookOutcomeClassification.Unknown,
                    ReasonCode = "LOCAL_PAYMENT_INTENT_NOT_FOUND"
                },
                replayContext);
        }

        var runtimeInputs = BuildRuntimeInputs(request, callback, localPaymentIntent);

        if (replayContext.IsReplay || duplicate)
        {
            var adapterResult = await _webhookEvidenceAdapter.ProcessAsync(
                new PaymobWebhookEvidenceRequest(
                    request.RawPayload,
                    request.HmacSignature,
                    request.HmacSecret,
                    runtimeInputs.Context,
                    runtimeInputs.Action,
                    runtimeInputs.Session,
                    request.TimestampTolerance,
                    request.ReceivedAtUtc,
                    replayContext.PriorEvidence,
                    replayDetectionHook: replayContext.IsReplay
                        ? (_, _) => ValueTask.FromResult(PaymobWebhookHookDecision.Reject("PAYMOB_WEBHOOK_REPLAY_REJECTED", replayContext.ReasonCode))
                        : null,
                    duplicateDetectionHook: duplicate
                        ? (_, _) => ValueTask.FromResult(PaymobWebhookHookDecision.Reject("PAYMOB_WEBHOOK_DUPLICATE_REJECTED", "PAYMENT_WEBHOOK_LOG_ALREADY_PROCESSED"))
                        : null),
                cancellationToken);

            return PaymobWebhookRuntimeAdmissionResult.Rejected(
                request.TenantId,
                providerEventId,
                callback.EventType,
                admission,
                replayContext with { IsDuplicate = duplicate },
                localPaymentIntent.Id,
                localPaymentIntent.OrderId,
                adapterResult);
        }

        if (!admission.AdmittedToRuntime)
        {
            return PaymobWebhookRuntimeAdmissionResult.NotAdmitted(
                request.TenantId,
                providerEventId,
                callback.EventType,
                admission,
                replayContext);
        }

        var admittedResult = await _webhookEvidenceAdapter.ProcessAsync(
            new PaymobWebhookEvidenceRequest(
                request.RawPayload,
                request.HmacSignature,
                request.HmacSecret,
                runtimeInputs.Context,
                runtimeInputs.Action,
                runtimeInputs.Session,
                request.TimestampTolerance,
                request.ReceivedAtUtc,
                replayContext.PriorEvidence),
            cancellationToken);

        return PaymobWebhookRuntimeAdmissionResult.Admitted(
            request.TenantId,
            providerEventId,
            admission,
            replayContext,
            localPaymentIntent.Id,
            localPaymentIntent.OrderId,
            admittedResult);
    }

    private static string BuildProviderEventId(PaymobCallback callback)
    {
        return callback.Payload.IsRefunded
            ? $"{callback.TransactionId}:refund:{callback.Payload.AmountCents ?? 0}"
            : callback.TransactionId;
    }

    public async Task PersistReplayRecordAsync(
        PaymobWebhookRuntimeAdmissionResult result,
        Guid? paymentIntentId,
        Guid? orderId,
        CancellationToken cancellationToken = default)
    {
        if (!result.AdmittedToRuntime ||
            result.AdapterResult?.RuntimeResult.AdmittedEvidence is not { } evidence ||
            string.IsNullOrWhiteSpace(result.ProviderEventId))
        {
            return;
        }

        var exists = await _dbContext.PaymobWebhookReplayRecords
            .IgnoreQueryFilters()
            .AnyAsync(record =>
                record.TenantId == result.TenantId &&
                record.Provider == PaymobProviderReference.ProviderName &&
                record.ProviderEventId == result.ProviderEventId,
                cancellationToken);

        if (exists)
        {
            return;
        }

        _dbContext.PaymobWebhookReplayRecords.Add(new PaymobWebhookReplayRecord
        {
            Id = Guid.NewGuid(),
            TenantId = result.TenantId,
            Provider = PaymobProviderReference.ProviderName,
            ProviderEventId = result.ProviderEventId,
            EventType = result.EventType,
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

    private async Task<PaymobWebhookRuntimeAdmissionResult> ExecuteInvalidCallbackAsync(
        PaymobWebhookRuntimeAdmissionRequest request,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var callback = PaymobWebhookPayloadParser.Parse(request.RawPayload);
            var localPaymentIntent = await ResolveLocalPaymentIntentAsync(
                request.TenantId,
                callback,
                cancellationToken);

            if (localPaymentIntent is null)
            {
                return PaymobWebhookRuntimeAdmissionResult.InvalidCallbackRejected(request.TenantId, reasonCode);
            }

            var runtimeInputs = BuildRuntimeInputs(request, callback, localPaymentIntent);
            var adapterResult = await _webhookEvidenceAdapter.ProcessAsync(
                new PaymobWebhookEvidenceRequest(
                    request.RawPayload,
                    request.HmacSignature,
                    request.HmacSecret,
                    runtimeInputs.Context,
                    runtimeInputs.Action,
                    runtimeInputs.Session,
                    request.TimestampTolerance,
                    request.ReceivedAtUtc),
                cancellationToken);

            return PaymobWebhookRuntimeAdmissionResult.InvalidCallbackRejected(
                request.TenantId,
                callback.TransactionId,
                callback.EventType,
                reasonCode,
                localPaymentIntent.Id,
                localPaymentIntent.OrderId,
                adapterResult);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException or System.Text.Json.JsonException)
        {
            return PaymobWebhookRuntimeAdmissionResult.InvalidCallbackRejected(request.TenantId, reasonCode);
        }
    }

    private async Task<CorePaymentIntent?> ResolveLocalPaymentIntentAsync(
        Guid tenantId,
        PaymobCallback callback,
        CancellationToken cancellationToken)
    {
        var paymobOrderId = callback.OrderId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var transactionId = callback.TransactionId;
        var merchantReferenceId = Guid.TryParse(callback.MerchantOrderId, out var parsedMerchantReferenceId)
            ? parsedMerchantReferenceId
            : (Guid?)null;

        var query = _dbContext.PaymentIntents
            .Include(intent => intent.Order)
            .IgnoreQueryFilters()
            .Where(intent =>
                intent.TenantId == tenantId &&
                !intent.IsDeleted &&
                intent.Provider == CorePaymentProvider.Paymob);

        var intent = await query.FirstOrDefaultAsync(
            candidate => candidate.ProviderIntentId != null && candidate.ProviderIntentId == paymobOrderId,
            cancellationToken);
        intent ??= await query.FirstOrDefaultAsync(
            candidate => candidate.ProviderPaymentId != null && candidate.ProviderPaymentId == transactionId,
            cancellationToken);

        if (intent != null || !merchantReferenceId.HasValue)
        {
            return intent;
        }

        intent = await query.FirstOrDefaultAsync(
            candidate => candidate.Id == merchantReferenceId.Value,
            cancellationToken);
        if (intent != null)
        {
            return intent;
        }

        var legacyCandidates = await query
            .Where(candidate => candidate.OrderId == merchantReferenceId.Value)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ThenByDescending(candidate => candidate.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        return legacyCandidates.Count == 1 ? legacyCandidates[0] : null;
    }

    private static PaymobWebhookRuntimeInputs BuildRuntimeInputs(
        PaymobWebhookRuntimeAdmissionRequest request,
        PaymobCallback callback,
        CorePaymentIntent intent)
    {
        var created = new DateTimeOffset(intent.CreatedAt == default ? DateTime.UtcNow : intent.CreatedAt, TimeSpan.Zero);
        var now = request.ReceivedAtUtc ?? DateTimeOffset.UtcNow;
        var sessionId = new PaymentSessionId(DeterministicGuid(
            "paymob-webhook-session",
            intent.TenantId.ToString("N"),
            intent.OrderId.ToString("N"),
            intent.Id.ToString("N")));
        var attemptId = new PaymentAttemptId(DeterministicGuid(
            "paymob-webhook-attempt",
            sessionId.Value.ToString("N"),
            intent.Id.ToString("N")));
        var runtimeEntryStatus = ResolveRuntimeEntryStatus(callback, intent.Status);

        var attempt = PaymentAttemptFactory.Create(
            attemptId,
            sessionId,
            new AttemptNumber(1),
            created,
            new ProviderReference(PaymobProviderReference.ProviderName),
            runtimeEntryStatus,
            new FailureReason(intent.FailureReason ?? string.Empty),
            0,
            new PaymentMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["local_payment_intent_id"] = intent.Id.ToString("N"),
                ["paymob_order_id"] = intent.ProviderIntentId ?? callback.OrderId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));

        var session = PaymentSessionFactory.Create(
            sessionId,
            new PaymentId(intent.Id),
            intent.OrderId,
            intent.TenantId,
            intent.TenantId,
            new PaymentReference("PAYMOB"),
            new Money(intent.Amount, new RuntimeCurrency(intent.Currency)),
            runtimeEntryStatus,
            created,
            now,
            new PaymentMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["payment_reference"] = intent.PaymentReference,
                ["paymob_order_id"] = intent.ProviderIntentId ?? callback.OrderId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["paymob_transaction_id"] = callback.TransactionId
            }),
            new AuditReferences(new[] { new CorrelationId(request.CorrelationId) }),
            new[] { attempt },
            Array.Empty<ProviderSession>());

        var actionId = new PaymentActionId(DeterministicGuid(
            "paymob-webhook-action",
            session.Id.Value.ToString("N"),
            attempt.Id.Value.ToString("N"),
            callback.TransactionId));

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
                ["application_boundary"] = nameof(PaymobWebhookRuntimeIntegrationService),
                ["paymob_event_type"] = callback.EventType,
                ["paymob_transaction_id"] = callback.TransactionId,
                ["paymob_order_id"] = callback.OrderId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["local_payment_intent_id"] = intent.Id.ToString("N"),
                ["order_id"] = intent.OrderId.ToString("N")
            }));

        var context = RuntimeExecutionFactory.Create(
            new CorrelationId(request.CorrelationId),
            session.Id,
            new ExecutionId(DeterministicGuid(
                "paymob-webhook-runtime",
                request.TenantId.ToString("N"),
                callback.TransactionId,
                request.CorrelationId)),
            now,
            new PipelineMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["application_boundary"] = nameof(PaymobWebhookRuntimeIntegrationService),
                ["paymob_event_type"] = callback.EventType,
                ["paymob_transaction_id"] = callback.TransactionId
            }));

        return new PaymobWebhookRuntimeInputs(context, session, action);
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
        PaymobCallback callback,
        CorePaymentIntentStatus localStatus)
    {
        var finality = PaymobTransactionFinalityClassifier.Classify(
            callback.Payload.Pending,
            callback.Payload.Success,
            callback.Payload.IsAuth,
            callback.Payload.IsCapture,
            callback.Payload.IsStandalonePayment,
            callback.Payload.IsVoided,
            callback.Payload.IsRefunded,
            callback.Payload.ErrorOccured);

        if (finality == PaymobTransactionFinality.Refunded)
        {
            return RuntimePaymentStatus.RefundPending;
        }

        if (finality == PaymobTransactionFinality.Authorized)
        {
            return RuntimePaymentStatus.Authorized;
        }

        if (finality == PaymobTransactionFinality.Captured)
        {
            return RuntimePaymentStatus.Approved;
        }

        return MapPaymentStatus(localStatus);
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

public static class PaymobWebhookOutcomeAdmissionService
{
    public static PaymobWebhookOutcomeAdmission Evaluate(PaymobCallback callback)
    {
        var finality = PaymobTransactionFinalityClassifier.Classify(
            callback.Payload.Pending,
            callback.Payload.Success,
            callback.Payload.IsAuth,
            callback.Payload.IsCapture,
            callback.Payload.IsStandalonePayment,
            callback.Payload.IsVoided,
            callback.Payload.IsRefunded,
            callback.Payload.ErrorOccured);
        var classification = finality == PaymobTransactionFinality.Pending
            ? PaymobWebhookOutcomeClassification.NonTerminal
            : PaymobWebhookOutcomeClassification.Terminal;
        var terminalNegativeOutcome = callback.Payload.ErrorOccured || callback.Payload.IsVoided;
        var admittedToRuntime =
            classification == PaymobWebhookOutcomeClassification.Terminal &&
            !terminalNegativeOutcome;
        var reasonCode = terminalNegativeOutcome
            ? "TERMINAL_NEGATIVE_APPLICATION_LIFECYCLE"
            : classification.ToString().ToUpperInvariant();

        return new PaymobWebhookOutcomeAdmission(classification, admittedToRuntime, reasonCode);
    }
}

public static class PaymobWebhookPriorEvidenceLoader
{
    public static async Task<PaymobWebhookReplayContext> LoadAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        string providerEventId,
        string providerReference,
        CancellationToken cancellationToken)
    {
        var records = await dbContext.PaymobWebhookReplayRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(record =>
                record.TenantId == tenantId &&
                record.Provider == PaymobProviderReference.ProviderName &&
                (record.ProviderEventId == providerEventId || record.ProviderReference == providerReference))
            .ToArrayAsync(cancellationToken);

        var replay = records.Any(record => record.ProviderEventId == providerEventId);
        var priorEvidence = records.Select(Rehydrate).ToArray();

        return new PaymobWebhookReplayContext(
            priorEvidence,
            false,
            replay,
            replay ? "PROVIDER_EVENT_ALREADY_PROCESSED" : "NO_REPLAY")
        {
            DuplicateProviderEventId = replay ? providerEventId : null
        };
    }

    private static AdmittedEvidence Rehydrate(PaymobWebhookReplayRecord record)
    {
        return AdmittedEvidenceFactory.Create(
            new EvidenceId(record.EvidenceId),
            new PaymentActionId(record.ActionId),
            EvidenceType.Webhook,
            EvidenceStatus.Admitted,
            new EvidenceReference(record.EvidenceReference),
            new EvidenceSource(record.Provider),
            new EvidenceTimestamp(new DateTimeOffset(record.ProcessingTimestampUtc, TimeSpan.Zero)),
            new EvidenceHash(record.EvidenceHash),
            new ProviderPayloadReference(record.ProviderReference),
            new ActionCorrelationId(record.CorrelationId),
            new AdmissionReference($"paymob:webhook:replay:{record.ProviderEventId}"),
            new EvidenceMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider"] = record.Provider,
                ["provider_event_id"] = record.ProviderEventId,
                ["event_type"] = record.EventType,
                ["admission_classification"] = record.AdmissionClassification
            }));
    }
}

public sealed record PaymobWebhookRuntimeAdmissionRequest(
    Guid TenantId,
    string RawPayload,
    string HmacSignature,
    string HmacSecret,
    string CorrelationId,
    TimeSpan? TimestampTolerance = null,
    DateTimeOffset? ReceivedAtUtc = null);

public sealed record PaymobWebhookRuntimeAdmissionResult
{
    public Guid TenantId { get; init; }

    public bool AdmittedToRuntime { get; init; }

    public bool RejectedAsReplay { get; init; }

    public bool RejectedAsDuplicate { get; init; }

    public bool InvalidCallback { get; init; }

    public string ProviderEventId { get; init; } = string.Empty;

    public string EventType { get; init; } = string.Empty;

    public PaymobWebhookOutcomeAdmission Admission { get; init; } = new();

    public PaymobWebhookReplayContext ReplayContext { get; init; } = new();

    public Guid? PaymentIntentId { get; init; }

    public Guid? OrderId { get; init; }

    public PaymobWebhookEvidenceAdapterResult? AdapterResult { get; init; }

    public static PaymobWebhookRuntimeAdmissionResult InvalidCallbackRejected(Guid tenantId, string reasonCode)
    {
        return new PaymobWebhookRuntimeAdmissionResult
        {
            TenantId = tenantId,
            InvalidCallback = true,
            Admission = new PaymobWebhookOutcomeAdmission(PaymobWebhookOutcomeClassification.Invalid, false, reasonCode)
        };
    }

    public static PaymobWebhookRuntimeAdmissionResult InvalidCallbackRejected(
        Guid tenantId,
        string providerEventId,
        string eventType,
        string reasonCode,
        Guid paymentIntentId,
        Guid orderId,
        PaymobWebhookEvidenceAdapterResult adapterResult)
    {
        return new PaymobWebhookRuntimeAdmissionResult
        {
            TenantId = tenantId,
            ProviderEventId = providerEventId,
            EventType = eventType,
            InvalidCallback = true,
            Admission = new PaymobWebhookOutcomeAdmission(PaymobWebhookOutcomeClassification.Invalid, false, reasonCode),
            PaymentIntentId = paymentIntentId,
            OrderId = orderId,
            AdapterResult = adapterResult
        };
    }

    public static PaymobWebhookRuntimeAdmissionResult NotAdmitted(
        Guid tenantId,
        string providerEventId,
        string eventType,
        PaymobWebhookOutcomeAdmission admission,
        PaymobWebhookReplayContext replayContext)
    {
        return new PaymobWebhookRuntimeAdmissionResult
        {
            TenantId = tenantId,
            ProviderEventId = providerEventId,
            EventType = eventType,
            Admission = admission,
            ReplayContext = replayContext
        };
    }

    public static PaymobWebhookRuntimeAdmissionResult Rejected(
        Guid tenantId,
        string providerEventId,
        string eventType,
        PaymobWebhookOutcomeAdmission admission,
        PaymobWebhookReplayContext replayContext,
        Guid paymentIntentId,
        Guid orderId,
        PaymobWebhookEvidenceAdapterResult adapterResult)
    {
        return new PaymobWebhookRuntimeAdmissionResult
        {
            TenantId = tenantId,
            ProviderEventId = providerEventId,
            EventType = eventType,
            Admission = admission,
            ReplayContext = replayContext,
            PaymentIntentId = paymentIntentId,
            OrderId = orderId,
            AdapterResult = adapterResult,
            RejectedAsReplay = replayContext.IsReplay,
            RejectedAsDuplicate = replayContext.IsDuplicate
        };
    }

    public static PaymobWebhookRuntimeAdmissionResult Admitted(
        Guid tenantId,
        string providerEventId,
        PaymobWebhookOutcomeAdmission admission,
        PaymobWebhookReplayContext replayContext,
        Guid paymentIntentId,
        Guid orderId,
        PaymobWebhookEvidenceAdapterResult adapterResult)
    {
        return new PaymobWebhookRuntimeAdmissionResult
        {
            TenantId = tenantId,
            ProviderEventId = providerEventId,
            EventType = adapterResult.PaymobEventType ?? string.Empty,
            Admission = admission,
            ReplayContext = replayContext,
            PaymentIntentId = paymentIntentId,
            OrderId = orderId,
            AdapterResult = adapterResult,
            AdmittedToRuntime = adapterResult.Succeeded &&
                adapterResult.RuntimeResult.OverallStatus == RuntimeOverallStatus.Succeeded
        };
    }
}

public sealed record PaymobWebhookOutcomeAdmission(
    PaymobWebhookOutcomeClassification Classification,
    bool AdmittedToRuntime,
    string ReasonCode)
{
    public PaymobWebhookOutcomeAdmission()
        : this(PaymobWebhookOutcomeClassification.Unknown, false, "UNKNOWN")
    {
    }
}

public sealed record PaymobWebhookReplayContext(
    IReadOnlyCollection<AdmittedEvidence> PriorEvidence,
    bool IsDuplicate,
    bool IsReplay,
    string ReasonCode)
{
    public string? DuplicateProviderEventId { get; init; }

    public PaymobWebhookReplayContext()
        : this(Array.Empty<AdmittedEvidence>(), false, false, "NO_REPLAY")
    {
    }
}

public enum PaymobWebhookOutcomeClassification
{
    Unknown = 0,
    Terminal = 1,
    NonTerminal = 2,
    Ignored = 3,
    Duplicate = 4,
    Replay = 5,
    Invalid = 6
}

internal sealed record PaymobWebhookRuntimeInputs(
    RuntimeExecutionContext Context,
    PaymentSession Session,
    PaymentAction Action);
