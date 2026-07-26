using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;
using PointOfSale.Infrastructure.Data;
using CorePaymentIntent = PointOfSale.Core.Entities.PaymentIntent;
using CorePaymentIntentStatus = PointOfSale.Core.Enums.PaymentIntentStatus;
using CorePaymentProvider = PointOfSale.Core.Enums.PaymentProvider;
using RuntimeEvidenceType = PointOfSale.Core.Payments.Runtime.Domain.EvidenceType;
using RuntimeCurrency = PointOfSale.Core.Payments.Runtime.Domain.Currency;
using RuntimePaymentStatus = PointOfSale.Core.Payments.Runtime.Domain.PaymentStatus;

namespace PointOfSale.API.Services.Payments.Fawry;

public interface IFawryWebhookRuntimeIntegrationService
{
    Task<FawryWebhookRuntimeAdmissionResult> EvaluateAndExecuteAsync(
        FawryWebhookRuntimeAdmissionRequest request,
        CancellationToken cancellationToken = default);

    Task PersistReplayRecordAsync(
        FawryWebhookRuntimeAdmissionResult result,
        Guid? paymentIntentId,
        Guid? orderId,
        CancellationToken cancellationToken = default);
}

public sealed class FawryWebhookRuntimeIntegrationService : IFawryWebhookRuntimeIntegrationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFawryTenantConfigurationResolver _tenantConfigurationResolver;
    private readonly IFawryWebhookEvidenceAdapter _webhookEvidenceAdapter;
    private readonly PaymentRuntimeOrchestrator _orchestrator;
    private readonly ILogger<FawryWebhookRuntimeIntegrationService> _logger;

    public FawryWebhookRuntimeIntegrationService(
        ApplicationDbContext dbContext,
        IFawryTenantConfigurationResolver tenantConfigurationResolver,
        IFawryWebhookEvidenceAdapter webhookEvidenceAdapter,
        PaymentRuntimeOrchestrator orchestrator,
        ILogger<FawryWebhookRuntimeIntegrationService> logger)
    {
        _dbContext = dbContext;
        _tenantConfigurationResolver = tenantConfigurationResolver;
        _webhookEvidenceAdapter = webhookEvidenceAdapter;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task<FawryWebhookRuntimeAdmissionResult> EvaluateAndExecuteAsync(
        FawryWebhookRuntimeAdmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        FawryCallback callback;
        try
        {
            callback = FawryWebhookPayloadParser.Parse(request.RawPayload);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException or JsonException)
        {
            return FawryWebhookRuntimeAdmissionResult.InvalidCallbackRejected(
                request.TenantId,
                "FAWRY_WEBHOOK_MALFORMED_PAYLOAD");
        }

        var webhookAuthority = await _tenantConfigurationResolver.ResolveWebhookAuthorityAsync(
            request.TenantId,
            cancellationToken);
        if (webhookAuthority.TenantId != request.TenantId)
        {
            throw FawryApplicationException.ConfigurationMissing();
        }

        var admission = FawryWebhookOutcomeAdmissionService.Evaluate(callback);
        var replayContext = await FawryWebhookPriorEvidenceLoader.LoadAsync(
            _dbContext,
            request.TenantId,
            callback.RequestId,
            callback.ProviderReference,
            cancellationToken);

        var duplicate = await _dbContext.PaymentWebhookLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(log =>
                log.TenantId == request.TenantId &&
                log.Provider == FawryProviderReference.ProviderName &&
                log.ProviderEventId == callback.RequestId &&
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
                "Fawry webhook reached runtime admission but local payment intent was not found | TenantId: {TenantId} | RequestId: {RequestId} | ProviderReference: {ProviderReference}",
                request.TenantId,
                callback.RequestId,
                callback.ProviderReference);

            return FawryWebhookRuntimeAdmissionResult.NotAdmitted(
                request.TenantId,
                callback.RequestId,
                callback.EventType,
                admission with
                {
                    Classification = FawryWebhookOutcomeClassification.Unknown,
                    ReasonCode = "LOCAL_PAYMENT_INTENT_NOT_FOUND"
                },
                replayContext);
        }

        var runtimeInputs = BuildRuntimeInputs(request, callback, localPaymentIntent);
        var adapterResult = await _webhookEvidenceAdapter.ProcessAsync(
            new FawryWebhookEvidenceRequest(
                request.RawPayload,
                webhookAuthority.SecureKey,
                runtimeInputs.Action,
                request.Kind,
                request.TimestampTolerance,
                request.ReceivedAtUtc,
                replayDetectionHook: replayContext.IsReplay
                    ? (_, _) => ValueTask.FromResult(FawryWebhookHookDecision.Reject("FAWRY_WEBHOOK_REPLAY_REJECTED", replayContext.ReasonCode))
                    : null,
                duplicateDetectionHook: duplicate
                    ? (_, _) => ValueTask.FromResult(FawryWebhookHookDecision.Reject("FAWRY_WEBHOOK_DUPLICATE_REJECTED", "PAYMENT_WEBHOOK_LOG_ALREADY_PROCESSED"))
                    : null),
            cancellationToken);

        var runtimeResult = _orchestrator.Execute(
            runtimeInputs.Context,
            adapterResult.ProviderEvidence,
            runtimeInputs.Action,
            runtimeInputs.Session,
            replayContext.PriorEvidence,
            null,
            null,
            null);

        if (replayContext.IsReplay || duplicate)
        {
            return FawryWebhookRuntimeAdmissionResult.Rejected(
                request.TenantId,
                callback.RequestId,
                callback.EventType,
                admission,
                replayContext with { IsDuplicate = duplicate },
                localPaymentIntent.Id,
                localPaymentIntent.OrderId,
                adapterResult,
                runtimeResult);
        }

        if (!admission.AdmittedToRuntime)
        {
            return FawryWebhookRuntimeAdmissionResult.NotAdmitted(
                request.TenantId,
                callback.RequestId,
                callback.EventType,
                admission,
                replayContext,
                localPaymentIntent.Id,
                localPaymentIntent.OrderId,
                adapterResult,
                runtimeResult);
        }

        return FawryWebhookRuntimeAdmissionResult.Admitted(
            request.TenantId,
            admission,
            replayContext,
            localPaymentIntent.Id,
            localPaymentIntent.OrderId,
            adapterResult,
            runtimeResult);
    }

    public async Task PersistReplayRecordAsync(
        FawryWebhookRuntimeAdmissionResult result,
        Guid? paymentIntentId,
        Guid? orderId,
        CancellationToken cancellationToken = default)
    {
        if (!result.AdmittedToRuntime ||
            result.RuntimeResult?.AdmittedEvidence is not { } evidence ||
            string.IsNullOrWhiteSpace(result.ProviderEventId))
        {
            return;
        }

        var exists = await _dbContext.FawryWebhookReplayRecords
            .IgnoreQueryFilters()
            .AnyAsync(record =>
                record.TenantId == result.TenantId &&
                record.Provider == FawryProviderReference.ProviderName &&
                record.ProviderEventId == result.ProviderEventId,
                cancellationToken);

        if (exists)
        {
            return;
        }

        _dbContext.FawryWebhookReplayRecords.Add(new FawryWebhookReplayRecord
        {
            Id = Guid.NewGuid(),
            TenantId = result.TenantId,
            Provider = FawryProviderReference.ProviderName,
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

    private async Task<CorePaymentIntent?> ResolveLocalPaymentIntentAsync(
        Guid tenantId,
        FawryCallback callback,
        CancellationToken cancellationToken)
    {
        return await _dbContext.PaymentIntents
            .Include(intent => intent.Order)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(intent =>
                intent.TenantId == tenantId &&
                !intent.IsDeleted &&
                intent.Provider == CorePaymentProvider.Fawry &&
                ((intent.ReferenceCode != null && intent.ReferenceCode == callback.FawryRefNumber) ||
                 (intent.ProviderIntentId != null && intent.ProviderIntentId == callback.FawryRefNumber) ||
                 (intent.ProviderIntentId != null && intent.ProviderIntentId == callback.MerchantRefNumber) ||
                 (intent.PaymentReference == callback.MerchantRefNumber)),
                cancellationToken);
    }

    private static FawryWebhookRuntimeInputs BuildRuntimeInputs(
        FawryWebhookRuntimeAdmissionRequest request,
        FawryCallback callback,
        CorePaymentIntent intent)
    {
        var intentCreated = new DateTimeOffset(intent.CreatedAt == default ? DateTime.UtcNow : intent.CreatedAt, TimeSpan.Zero);
        var evidenceTimestamp = callback.OccurredAtUtc ?? request.ReceivedAtUtc ?? DateTimeOffset.UtcNow;
        var created = intentCreated <= evidenceTimestamp ? intentCreated : evidenceTimestamp;
        var now = evidenceTimestamp < created ? created : evidenceTimestamp;
        var sessionId = new PaymentSessionId(DeterministicGuid(
            "fawry-webhook-session",
            intent.TenantId.ToString("N"),
            intent.OrderId.ToString("N"),
            intent.Id.ToString("N")));
        var attemptId = new PaymentAttemptId(DeterministicGuid(
            "fawry-webhook-attempt",
            sessionId.Value.ToString("N"),
            intent.Id.ToString("N")));
        var runtimeEntryStatus = ResolveRuntimeEntryStatus(callback, intent.Status);

        var attempt = PaymentAttemptFactory.Create(
            attemptId,
            sessionId,
            new AttemptNumber(1),
            created,
            new ProviderReference(FawryProviderReference.ProviderName),
            runtimeEntryStatus,
            new FailureReason(intent.FailureReason ?? string.Empty),
            0,
            new PaymentMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["local_payment_intent_id"] = intent.Id.ToString("N"),
                ["fawry_reference"] = intent.ReferenceCode ?? callback.FawryRefNumber
            }));

        var session = PaymentSessionFactory.Create(
            sessionId,
            new PaymentId(intent.Id),
            intent.OrderId,
            intent.TenantId,
            intent.TenantId,
            new PaymentReference("FAWRY"),
            new Money(intent.Amount, new RuntimeCurrency(intent.Currency)),
            runtimeEntryStatus,
            created,
            now,
            new PaymentMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["payment_reference"] = intent.PaymentReference,
                ["fawry_ref_number"] = callback.FawryRefNumber,
                ["merchant_reference"] = callback.MerchantRefNumber
            }),
            new AuditReferences(new[] { new CorrelationId(request.CorrelationId) }),
            new[] { attempt },
            Array.Empty<ProviderSession>());

        var actionId = new PaymentActionId(DeterministicGuid(
            "fawry-webhook-action",
            session.Id.Value.ToString("N"),
            attempt.Id.Value.ToString("N"),
            callback.RequestId));

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
                ["application_boundary"] = nameof(FawryWebhookRuntimeIntegrationService),
                ["fawry_event_type"] = callback.EventType,
                ["fawry_request_id"] = callback.RequestId,
                ["fawry_ref_number"] = callback.FawryRefNumber,
                ["merchant_reference"] = callback.MerchantRefNumber,
                ["local_payment_intent_id"] = intent.Id.ToString("N"),
                ["order_id"] = intent.OrderId.ToString("N"),
                [FawryRequestMetadataKeys.Amount] = intent.Amount.ToString(CultureInfo.InvariantCulture),
                [FawryRequestMetadataKeys.Currency] = intent.Currency
            }));

        var context = RuntimeExecutionFactory.Create(
            new CorrelationId(request.CorrelationId),
            session.Id,
            new ExecutionId(DeterministicGuid(
                "fawry-webhook-runtime",
                request.TenantId.ToString("N"),
                callback.RequestId,
                request.CorrelationId)),
            now,
            new PipelineMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["application_boundary"] = nameof(FawryWebhookRuntimeIntegrationService),
                ["fawry_event_type"] = callback.EventType,
                ["fawry_request_id"] = callback.RequestId
            }));

        return new FawryWebhookRuntimeInputs(context, session, action);
    }

    private static RuntimePaymentStatus MapPaymentStatus(CorePaymentIntentStatus status)
    {
        return status switch
        {
            CorePaymentIntentStatus.Paid => RuntimePaymentStatus.Approved,
            CorePaymentIntentStatus.Authorized => RuntimePaymentStatus.Authorized,
            CorePaymentIntentStatus.Failed => RuntimePaymentStatus.Failed,
            CorePaymentIntentStatus.Cancelled => RuntimePaymentStatus.Cancelled,
            CorePaymentIntentStatus.Expired => RuntimePaymentStatus.Expired,
            _ => RuntimePaymentStatus.Pending
        };
    }

    private static RuntimePaymentStatus ResolveRuntimeEntryStatus(
        FawryCallback callback,
        CorePaymentIntentStatus localStatus)
    {
        return callback.OrderStatus switch
        {
            "PAID" or "CAPTURED" => RuntimePaymentStatus.Approved,
            "AUTHORIZED" => RuntimePaymentStatus.Authorized,
            "REFUNDED" => RuntimePaymentStatus.RefundPending,
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

public static class FawryWebhookOutcomeAdmissionService
{
    public static FawryWebhookOutcomeAdmission Evaluate(FawryCallback callback)
    {
        var classification = callback.OrderStatus switch
        {
            "PAID" or "CAPTURED" or "AUTHORIZED" or "REFUNDED" => FawryWebhookOutcomeClassification.Terminal,
            "NEW" => FawryWebhookOutcomeClassification.NonTerminal,
            "FAILED" or "EXPIRED" or "CANCELED" or "CANCELLED" => FawryWebhookOutcomeClassification.Terminal,
            _ => FawryWebhookOutcomeClassification.Ignored
        };
        var admittedToRuntime = callback.OrderStatus is "PAID" or "CAPTURED" or "AUTHORIZED" or "REFUNDED";
        var eligibleForLifecyclePersistence = callback.OrderStatus is "PAID" or "CAPTURED";
        var reasonCode = callback.OrderStatus is "FAILED" or "EXPIRED" or "CANCELED" or "CANCELLED"
            ? "TERMINAL_NEGATIVE_APPLICATION_LIFECYCLE"
            : classification.ToString().ToUpperInvariant();

        return new FawryWebhookOutcomeAdmission(
            classification,
            admittedToRuntime,
            eligibleForLifecyclePersistence,
            reasonCode);
    }
}

public static class FawryWebhookPriorEvidenceLoader
{
    public static async Task<FawryWebhookReplayContext> LoadAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        string providerEventId,
        string providerReference,
        CancellationToken cancellationToken)
    {
        var records = await dbContext.FawryWebhookReplayRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(record =>
                record.TenantId == tenantId &&
                record.Provider == FawryProviderReference.ProviderName &&
                (record.ProviderEventId == providerEventId || record.ProviderReference == providerReference))
            .ToArrayAsync(cancellationToken);

        var replay = records.Any(record => record.ProviderEventId == providerEventId);
        var priorEvidence = records.Select(Rehydrate).ToArray();

        return new FawryWebhookReplayContext(
            priorEvidence,
            false,
            replay,
            replay ? "PROVIDER_EVENT_ALREADY_PROCESSED" : "NO_REPLAY")
        {
            DuplicateProviderEventId = replay ? providerEventId : null
        };
    }

    private static AdmittedEvidence Rehydrate(FawryWebhookReplayRecord record)
    {
        return AdmittedEvidenceFactory.Create(
            new EvidenceId(record.EvidenceId),
            new PaymentActionId(record.ActionId),
            RuntimeEvidenceType.Webhook,
            EvidenceStatus.Admitted,
            new EvidenceReference(record.EvidenceReference),
            new EvidenceSource(record.Provider),
            new EvidenceTimestamp(new DateTimeOffset(record.ProcessingTimestampUtc, TimeSpan.Zero)),
            new EvidenceHash(record.EvidenceHash),
            new ProviderPayloadReference(record.ProviderReference),
            new ActionCorrelationId(record.CorrelationId),
            new AdmissionReference($"fawry:webhook:replay:{record.ProviderEventId}"),
            new EvidenceMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider"] = record.Provider,
                ["provider_event_id"] = record.ProviderEventId,
                ["event_type"] = record.EventType,
                ["admission_classification"] = record.AdmissionClassification
            }));
    }
}

public static class FawryWebhookLifecycleAdmission
{
    public static bool IsLifecyclePersistenceAllowed(FawryWebhookRuntimeAdmissionResult result)
    {
        return result.AdmittedToRuntime &&
               result.Admission.EligibleForLifecyclePersistence &&
               result.RuntimeResult is
               {
                   OverallStatus: RuntimeOverallStatus.Succeeded,
                   TransitionResult.Status: TransitionStatus.Applied,
                   TransitionResult.Session.Status: RuntimePaymentStatus.Completed,
                   TransactionResult.Status: TransactionStatus.Created,
                   SettlementResult.Status: SettlementStatus.Pending
               };
    }

    public static HandlePaymentResultRequest CreateRequest(
        FawryWebhookRuntimeAdmissionResult result,
        DateTime processedAtUtc)
    {
        if (!IsLifecyclePersistenceAllowed(result))
        {
            throw FawryApplicationException.RuntimeRejected();
        }

        var metadata = result.AdapterResult!.ProviderEvidence.Metadata.Values;
        var amount = Decimal(metadata, "payment_amount");
        var currency = Text(metadata, "currency");
        var providerPaymentId = Text(metadata, "fawry_ref_number", result.AdapterResult.FawryRefNumber ?? result.ProviderEventId);

        return new HandlePaymentResultRequest
        {
            OrderId = result.OrderId!.Value,
            PaymentIntentId = result.PaymentIntentId!.Value,
            Outcome = PaymentLifecycleOutcome.Succeeded,
            Source = PaymentLifecycleSource.GatewayWebhook,
            Gateway = FawryProviderReference.ProviderName,
            PaymentReference = result.AdapterResult.Translation.MerchantReference,
            ProviderPaymentId = providerPaymentId,
            ProviderAmount = amount,
            ProviderCurrency = currency,
            Amount = amount,
            Currency = currency,
            WebhookEventId = result.ProviderEventId,
            ProcessedAtUtc = processedAtUtc
        };
    }

    private static string Text(IReadOnlyDictionary<string, string> metadata, string key, string? fallback = null)
    {
        return metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback ?? string.Empty;
    }

    private static decimal Decimal(IReadOnlyDictionary<string, string> metadata, string key)
    {
        return decimal.TryParse(Text(metadata, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;
    }
}

public sealed record FawryWebhookRuntimeAdmissionRequest(
    Guid TenantId,
    string RawPayload,
    string CorrelationId,
    FawryWebhookEvidenceKind Kind = FawryWebhookEvidenceKind.Callback,
    TimeSpan? TimestampTolerance = null,
    DateTimeOffset? ReceivedAtUtc = null);

public sealed record FawryWebhookRuntimeAdmissionResult
{
    public Guid TenantId { get; init; }

    public bool AdmittedToRuntime { get; init; }

    public bool RejectedAsReplay { get; init; }

    public bool RejectedAsDuplicate { get; init; }

    public bool InvalidCallback { get; init; }

    public string ProviderEventId { get; init; } = string.Empty;

    public string EventType { get; init; } = string.Empty;

    public FawryWebhookOutcomeAdmission Admission { get; init; } = new();

    public FawryWebhookReplayContext ReplayContext { get; init; } = new();

    public Guid? PaymentIntentId { get; init; }

    public Guid? OrderId { get; init; }

    public FawryWebhookEvidenceAdapterResult? AdapterResult { get; init; }

    public RuntimeResult? RuntimeResult { get; init; }

    public static FawryWebhookRuntimeAdmissionResult InvalidCallbackRejected(Guid tenantId, string reasonCode)
    {
        return new FawryWebhookRuntimeAdmissionResult
        {
            TenantId = tenantId,
            InvalidCallback = true,
            Admission = new FawryWebhookOutcomeAdmission(FawryWebhookOutcomeClassification.Invalid, false, false, reasonCode)
        };
    }

    public static FawryWebhookRuntimeAdmissionResult NotAdmitted(
        Guid tenantId,
        string providerEventId,
        string eventType,
        FawryWebhookOutcomeAdmission admission,
        FawryWebhookReplayContext replayContext,
        Guid? paymentIntentId = null,
        Guid? orderId = null,
        FawryWebhookEvidenceAdapterResult? adapterResult = null,
        RuntimeResult? runtimeResult = null)
    {
        return new FawryWebhookRuntimeAdmissionResult
        {
            TenantId = tenantId,
            ProviderEventId = providerEventId,
            EventType = eventType,
            Admission = admission,
            ReplayContext = replayContext,
            PaymentIntentId = paymentIntentId,
            OrderId = orderId,
            AdapterResult = adapterResult,
            RuntimeResult = runtimeResult
        };
    }

    public static FawryWebhookRuntimeAdmissionResult Rejected(
        Guid tenantId,
        string providerEventId,
        string eventType,
        FawryWebhookOutcomeAdmission admission,
        FawryWebhookReplayContext replayContext,
        Guid paymentIntentId,
        Guid orderId,
        FawryWebhookEvidenceAdapterResult adapterResult,
        RuntimeResult runtimeResult)
    {
        return new FawryWebhookRuntimeAdmissionResult
        {
            TenantId = tenantId,
            ProviderEventId = providerEventId,
            EventType = eventType,
            Admission = admission,
            ReplayContext = replayContext,
            PaymentIntentId = paymentIntentId,
            OrderId = orderId,
            AdapterResult = adapterResult,
            RuntimeResult = runtimeResult,
            RejectedAsReplay = replayContext.IsReplay,
            RejectedAsDuplicate = replayContext.IsDuplicate
        };
    }

    public static FawryWebhookRuntimeAdmissionResult Admitted(
        Guid tenantId,
        FawryWebhookOutcomeAdmission admission,
        FawryWebhookReplayContext replayContext,
        Guid paymentIntentId,
        Guid orderId,
        FawryWebhookEvidenceAdapterResult adapterResult,
        RuntimeResult runtimeResult)
    {
        return new FawryWebhookRuntimeAdmissionResult
        {
            TenantId = tenantId,
            ProviderEventId = adapterResult.FawryRequestId ?? adapterResult.ProviderEvidence.Reference.Value,
            EventType = adapterResult.Translation.EventType,
            Admission = admission,
            ReplayContext = replayContext,
            PaymentIntentId = paymentIntentId,
            OrderId = orderId,
            AdapterResult = adapterResult,
            RuntimeResult = runtimeResult,
            AdmittedToRuntime = adapterResult.Succeeded &&
                runtimeResult.OverallStatus == RuntimeOverallStatus.Succeeded
        };
    }
}

public sealed record FawryWebhookOutcomeAdmission(
    FawryWebhookOutcomeClassification Classification,
    bool AdmittedToRuntime,
    bool EligibleForLifecyclePersistence,
    string ReasonCode)
{
    public FawryWebhookOutcomeAdmission()
        : this(FawryWebhookOutcomeClassification.Unknown, false, false, "UNKNOWN")
    {
    }
}

public sealed record FawryWebhookReplayContext(
    IReadOnlyCollection<AdmittedEvidence> PriorEvidence,
    bool IsDuplicate,
    bool IsReplay,
    string ReasonCode)
{
    public string? DuplicateProviderEventId { get; init; }

    public FawryWebhookReplayContext()
        : this(Array.Empty<AdmittedEvidence>(), false, false, "NO_REPLAY")
    {
    }
}

public enum FawryWebhookOutcomeClassification
{
    Unknown = 0,
    Terminal = 1,
    NonTerminal = 2,
    Ignored = 3,
    Duplicate = 4,
    Replay = 5,
    Invalid = 6
}

internal sealed record FawryWebhookRuntimeInputs(
    RuntimeExecutionContext Context,
    PaymentSession Session,
    PaymentAction Action);
