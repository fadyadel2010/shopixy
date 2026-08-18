using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.API.Services.Payments.Fawry;

public interface IFawryWebhookEvidenceAdapter
{
    Task<FawryWebhookEvidenceAdapterResult> ProcessAsync(
        FawryWebhookEvidenceRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class FawryWebhookEvidenceAdapter : IFawryWebhookEvidenceAdapter
{
    public async Task<FawryWebhookEvidenceAdapterResult> ProcessAsync(
        FawryWebhookEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var verification = FawryWebhookSignatureVerifier.Verify(request);
        if (!verification.Succeeded)
        {
            return FawryWebhookEvidenceAdapterResult.Failed(
                FawryWebhookEvidenceTranslator.ProviderErrorEvidence(request.Action, request.RawPayload, verification.Error!),
                verification.Error!);
        }

        var replay = await InvokeHookAsync(request.ReplayDetectionHook, verification.Callback!, cancellationToken);
        if (replay.Rejected)
        {
            var error = FawryWebhookErrorTranslator.Replay(replay.Code, replay.Message);
            return FawryWebhookEvidenceAdapterResult.Failed(
                FawryWebhookEvidenceTranslator.ProviderErrorEvidence(request.Action, request.RawPayload, error),
                error);
        }

        var duplicate = await InvokeHookAsync(request.DuplicateDetectionHook, verification.Callback!, cancellationToken);
        if (duplicate.Rejected)
        {
            var error = FawryWebhookErrorTranslator.Duplicate(duplicate.Code, duplicate.Message);
            return FawryWebhookEvidenceAdapterResult.Failed(
                FawryWebhookEvidenceTranslator.ProviderErrorEvidence(request.Action, request.RawPayload, error),
                error);
        }

        var translation = FawryWebhookEvidenceTranslator.Translate(
            request.Action,
            verification.Callback!,
            request.RawPayload,
            request.Kind);
        if (!translation.Succeeded)
        {
            return FawryWebhookEvidenceAdapterResult.Failed(
                FawryWebhookEvidenceTranslator.ProviderErrorEvidence(request.Action, request.RawPayload, translation.Error!),
                translation.Error!);
        }

        return FawryWebhookEvidenceAdapterResult.Success(
            translation.Evidence!,
            verification.Callback!,
            translation.Translation,
            translation.ProviderError);
    }

    private static async ValueTask<FawryWebhookHookDecision> InvokeHookAsync(
        FawryWebhookDetectionHook? hook,
        FawryCallback callback,
        CancellationToken cancellationToken)
    {
        if (hook is null)
        {
            return FawryWebhookHookDecision.Accepted();
        }

        return await hook(FawryWebhookHookContext.FromCallback(callback), cancellationToken);
    }
}

public sealed record FawryWebhookEvidenceRequest
{
    public string RawPayload { get; init; }

    public string SecureKey { get; init; }

    public PaymentAction Action { get; init; }

    public FawryWebhookEvidenceKind Kind { get; init; }

    public TimeSpan? TimestampTolerance { get; init; }

    public DateTimeOffset? ReceivedAtUtc { get; init; }

    public FawryWebhookDetectionHook? ReplayDetectionHook { get; init; }

    public FawryWebhookDetectionHook? DuplicateDetectionHook { get; init; }

    public FawryWebhookEvidenceRequest()
    {
        RawPayload = string.Empty;
        SecureKey = string.Empty;
        Action = new PaymentAction();
        Kind = FawryWebhookEvidenceKind.Callback;
    }

    public FawryWebhookEvidenceRequest(
        string rawPayload,
        string secureKey,
        PaymentAction action,
        FawryWebhookEvidenceKind kind = FawryWebhookEvidenceKind.Callback,
        TimeSpan? timestampTolerance = null,
        DateTimeOffset? receivedAtUtc = null,
        FawryWebhookDetectionHook? replayDetectionHook = null,
        FawryWebhookDetectionHook? duplicateDetectionHook = null)
    {
        RawPayload = rawPayload ?? string.Empty;
        SecureKey = secureKey ?? string.Empty;
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Kind = kind;
        TimestampTolerance = timestampTolerance;
        ReceivedAtUtc = receivedAtUtc;
        ReplayDetectionHook = replayDetectionHook;
        DuplicateDetectionHook = duplicateDetectionHook;
    }
}

public enum FawryWebhookEvidenceKind
{
    Callback = 0,
    Webhook = 1
}

public sealed record FawryWebhookEvidenceAdapterResult
{
    public bool Succeeded { get; init; }

    public ProviderEvidence ProviderEvidence { get; init; }

    public CanonicalProviderError? Error { get; init; }

    public string? FawryRequestId { get; init; }

    public string? FawryRefNumber { get; init; }

    public string? OrderStatus { get; init; }

    public FawryWebhookTranslationMetadata Translation { get; init; }

    public FawryWebhookEvidenceAdapterResult()
    {
        ProviderEvidence = new ProviderEvidence();
        Translation = new FawryWebhookTranslationMetadata();
    }

    private FawryWebhookEvidenceAdapterResult(
        bool succeeded,
        ProviderEvidence providerEvidence,
        CanonicalProviderError? error,
        string? fawryRequestId,
        string? fawryRefNumber,
        string? orderStatus,
        FawryWebhookTranslationMetadata? translation)
    {
        Succeeded = succeeded;
        ProviderEvidence = providerEvidence ?? throw new ArgumentNullException(nameof(providerEvidence));
        Error = error;
        FawryRequestId = fawryRequestId;
        FawryRefNumber = fawryRefNumber;
        OrderStatus = orderStatus;
        Translation = translation ?? new FawryWebhookTranslationMetadata();
    }

    public static FawryWebhookEvidenceAdapterResult Success(
        ProviderEvidence evidence,
        FawryCallback callback,
        FawryWebhookTranslationMetadata translation,
        CanonicalProviderError? providerError)
    {
        return new FawryWebhookEvidenceAdapterResult(
            true,
            evidence,
            providerError,
            callback.RequestId,
            callback.FawryRefNumber,
            callback.OrderStatus,
            translation);
    }

    public static FawryWebhookEvidenceAdapterResult Failed(
        ProviderEvidence evidence,
        CanonicalProviderError error)
    {
        return new FawryWebhookEvidenceAdapterResult(false, evidence, error, null, null, null, null);
    }
}

public sealed record FawryWebhookTranslationMetadata
{
    public string EventType { get; init; }
    public string RequestId { get; init; }
    public string ProviderReference { get; init; }
    public string MerchantReference { get; init; }
    public string OrderStatus { get; init; }
    public string PayloadHash { get; init; }
    public string EvidenceHash { get; init; }

    public FawryWebhookTranslationMetadata()
    {
        EventType = string.Empty;
        RequestId = string.Empty;
        ProviderReference = string.Empty;
        MerchantReference = string.Empty;
        OrderStatus = string.Empty;
        PayloadHash = string.Empty;
        EvidenceHash = string.Empty;
    }

    public FawryWebhookTranslationMetadata(
        string eventType,
        string requestId,
        string providerReference,
        string merchantReference,
        string orderStatus,
        string payloadHash,
        string evidenceHash)
    {
        EventType = eventType;
        RequestId = requestId;
        ProviderReference = providerReference;
        MerchantReference = merchantReference;
        OrderStatus = orderStatus;
        PayloadHash = payloadHash;
        EvidenceHash = evidenceHash;
    }
}

public delegate ValueTask<FawryWebhookHookDecision> FawryWebhookDetectionHook(
    FawryWebhookHookContext context,
    CancellationToken cancellationToken);

public sealed record FawryWebhookHookContext(
    string Provider,
    string EventType,
    string RequestId,
    string ProviderReference,
    string EvidenceReference,
    string PayloadHash,
    DateTimeOffset? OccurredAtUtc)
{
    public static FawryWebhookHookContext FromCallback(FawryCallback callback)
    {
        return new FawryWebhookHookContext(
            FawryProviderReference.ProviderName,
            callback.EventType,
            callback.RequestId,
            callback.ProviderReference,
            callback.EvidenceReference,
            callback.PayloadHash,
            callback.OccurredAtUtc);
    }
}

public sealed record FawryWebhookHookDecision
{
    public bool Rejected { get; init; }
    public string Code { get; init; }
    public string Message { get; init; }

    private FawryWebhookHookDecision()
    {
        Code = string.Empty;
        Message = string.Empty;
    }

    public static FawryWebhookHookDecision Accepted()
    {
        return new FawryWebhookHookDecision();
    }

    public static FawryWebhookHookDecision Reject(string code, string message)
    {
        return new FawryWebhookHookDecision
        {
            Rejected = true,
            Code = string.IsNullOrWhiteSpace(code) ? "FAWRY_WEBHOOK_HOOK_REJECTED" : code.Trim(),
            Message = string.IsNullOrWhiteSpace(message) ? "Fawry webhook hook rejected the callback." : message.Trim()
        };
    }
}

public static class FawryWebhookSignatureVerifier
{
    public static FawryWebhookVerificationResult Verify(FawryWebhookEvidenceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SecureKey))
        {
            return FawryWebhookVerificationResult.Failed(
                CanonicalProviderError.Configuration(FawryProviderReference.ProviderName, "FAWRY_WEBHOOK_SECRET_MISSING", "Fawry secure key is required."));
        }

        try
        {
            var callback = FawryWebhookPayloadParser.Parse(request.RawPayload);
            if (request.TimestampTolerance.HasValue)
            {
                var timestampError = ValidateTimestamp(
                    callback,
                    request.ReceivedAtUtc ?? DateTimeOffset.UtcNow,
                    request.TimestampTolerance.Value);
                if (timestampError is not null)
                {
                    return FawryWebhookVerificationResult.Failed(timestampError);
                }
            }

            var expected = ComputeMessageSignature(callback.SignaturePayload, request.SecureKey);
            if (!FixedTimeHexEquals(expected, callback.MessageSignature))
            {
                return FawryWebhookVerificationResult.Failed(
                    CanonicalProviderError.Validation(FawryProviderReference.ProviderName, "FAWRY_WEBHOOK_SIGNATURE_INVALID", "Fawry message signature is invalid."));
            }

            return FawryWebhookVerificationResult.Success(callback);
        }
        catch (JsonException exception)
        {
            return FawryWebhookVerificationResult.Failed(
                CanonicalProviderError.Validation(FawryProviderReference.ProviderName, "FAWRY_WEBHOOK_MALFORMED_PAYLOAD", exception.Message));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            return FawryWebhookVerificationResult.Failed(
                CanonicalProviderError.Validation(FawryProviderReference.ProviderName, "FAWRY_WEBHOOK_MALFORMED_PAYLOAD", exception.Message));
        }
    }

    public static string ComputeMessageSignature(string signaturePayload, string secureKey)
    {
        return FawryHash.CreateRaw(string.Concat(signaturePayload, secureKey));
    }

    private static CanonicalProviderError? ValidateTimestamp(
        FawryCallback callback,
        DateTimeOffset receivedAtUtc,
        TimeSpan tolerance)
    {
        if (tolerance <= TimeSpan.Zero)
        {
            return CanonicalProviderError.Configuration(
                FawryProviderReference.ProviderName,
                "FAWRY_WEBHOOK_TIMESTAMP_TOLERANCE_INVALID",
                "Fawry webhook timestamp tolerance must be positive.");
        }

        if (!callback.OccurredAtUtc.HasValue)
        {
            return null;
        }

        var delta = (receivedAtUtc.ToUniversalTime() - callback.OccurredAtUtc.Value.ToUniversalTime()).Duration();
        return delta > tolerance
            ? CanonicalProviderError.Validation(FawryProviderReference.ProviderName, "FAWRY_WEBHOOK_TIMESTAMP_EXPIRED", "Fawry webhook timestamp is outside the allowed tolerance.")
            : null;
    }

    private static bool FixedTimeHexEquals(string expectedHex, string? actualHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex) || string.IsNullOrWhiteSpace(actualHex))
        {
            return false;
        }

        try
        {
            var expectedBytes = Convert.FromHexString(expectedHex);
            var actualBytes = Convert.FromHexString(actualHex.Trim());
            return expectedBytes.Length == actualBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed record FawryWebhookVerificationResult
{
    public bool Succeeded { get; init; }
    public FawryCallback? Callback { get; init; }
    public CanonicalProviderError? Error { get; init; }

    private FawryWebhookVerificationResult()
    {
    }

    public static FawryWebhookVerificationResult Success(FawryCallback callback)
    {
        return new FawryWebhookVerificationResult
        {
            Succeeded = true,
            Callback = callback
        };
    }

    public static FawryWebhookVerificationResult Failed(CanonicalProviderError error)
    {
        return new FawryWebhookVerificationResult
        {
            Succeeded = false,
            Error = error
        };
    }
}

public static class FawryWebhookPayloadParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static FawryCallback Parse(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            throw new ArgumentException("Fawry callback payload is required.", nameof(rawPayload));
        }

        var payload = JsonSerializer.Deserialize<FawryOfficialNotificationPayload>(rawPayload, JsonOptions);
        if (payload is null)
        {
            throw new JsonException("Fawry callback payload could not be deserialized.");
        }

        var fawryRefNumber = Required(payload.FawryRefNumber, "Fawry reference number");
        var merchantRefNumber = Required(payload.MerchantRefNumber, "Fawry merchant reference number");
        var orderStatus = Required(payload.OrderStatus, "Fawry order status").ToUpperInvariant();
        var paymentMethod = Required(payload.PaymentMethod, "Fawry payment method").ToUpperInvariant();
        var messageSignature = Required(payload.MessageSignature, "Fawry message signature");
        var paymentAmount = FormatAmount(payload.PaymentAmount, "Fawry payment amount");
        var orderAmount = FormatAmount(payload.OrderAmount, "Fawry order amount");
        var paymentReferenceNumber = FirstNonBlank(payload.PaymentReferenceNumber, payload.PaymentRefrenceNumber);
        var requestId = FirstNonBlank(payload.RequestId, fawryRefNumber);
        var occurredAtUtc = ParseTimestamp(payload.PaymentTime);
        var payloadHash = FawryHash.CreateRaw(rawPayload);
        var signaturePayload = string.Concat(
            fawryRefNumber,
            merchantRefNumber,
            paymentAmount,
            orderAmount,
            orderStatus,
            paymentMethod,
            paymentReferenceNumber);

        return new FawryCallback(
            ResolveEventType(orderStatus),
            requestId,
            fawryRefNumber,
            merchantRefNumber,
            paymentReferenceNumber,
            orderStatus,
            paymentMethod,
            paymentAmount,
            orderAmount,
            payload.FailureErrorCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            payload.FailureReason ?? string.Empty,
            $"fawry:order:{fawryRefNumber}",
            $"fawry:webhook:{requestId}",
            occurredAtUtc,
            payloadHash,
            signaturePayload,
            messageSignature);
    }

    private static string ResolveEventType(string orderStatus)
    {
        return $"ORDER_{orderStatus}";
    }

    private static string Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        return value.Trim();
    }

    private static string FormatAmount(decimal? value, string name)
    {
        if (!value.HasValue)
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        return value.Value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : throw new FormatException("Fawry paymentTime is invalid.");
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}

public static class FawryWebhookEvidenceTranslator
{
    private static readonly ISet<string> SupportedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "NEW",
        "PAID",
        "CANCELED",
        "CANCELLED",
        "REFUNDED",
        "EXPIRED",
        "PARTIAL_REFUNDED",
        "PARTIALLY_REFUNDED",
        "FAILED",
        "CAPTURED",
        "AUTHORIZED"
    };

    public static FawryWebhookTranslationResult Translate(
        PaymentAction action,
        FawryCallback callback,
        string rawPayload,
        FawryWebhookEvidenceKind kind)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(callback);

        if (!SupportedStatuses.Contains(callback.OrderStatus))
        {
            return FawryWebhookTranslationResult.Failed(
                CanonicalProviderError.Validation(
                    FawryProviderReference.ProviderName,
                    "FAWRY_WEBHOOK_EVENT_UNSUPPORTED",
                    $"Fawry order status '{callback.OrderStatus}' is not mapped to canonical evidence."));
        }

        var providerError = FawryWebhookErrorTranslator.TranslateProviderFailure(callback);
        var evidenceHash = FawryHash.Create(
            callback.EventType,
            callback.RequestId,
            callback.FawryRefNumber,
            callback.MerchantRefNumber,
            callback.OrderStatus,
            callback.PayloadHash);
        var evidenceId = FawryDeterministicId.Create(
            "fawry-webhook-evidence",
            callback.EventType,
            callback.RequestId,
            callback.FawryRefNumber,
            evidenceHash);
        var metadata = FawryWebhookMetadataFactory.Create(action, callback, evidenceHash, providerError, kind);

        var evidence = ProviderEvidenceFactory.Create(
            new EvidenceId(evidenceId),
            action.Id,
            EvidenceType.Webhook,
            EvidenceStatus.Recorded,
            new EvidenceReference(callback.EvidenceReference),
            new EvidenceSource(FawryProviderReference.ProviderName),
            new EvidenceTimestamp(callback.OccurredAtUtc ?? action.LastUpdatedUtc),
            new EvidenceHash(evidenceHash),
            new ProviderPayloadReference(callback.ProviderReference),
            action.CorrelationId,
            new EvidenceMetadata(metadata));
        var translation = new FawryWebhookTranslationMetadata(
            callback.EventType,
            callback.RequestId,
            callback.ProviderReference,
            callback.MerchantRefNumber,
            callback.OrderStatus,
            callback.PayloadHash,
            evidenceHash);

        return FawryWebhookTranslationResult.Success(evidence, translation, providerError);
    }

    public static ProviderEvidence ProviderErrorEvidence(
        PaymentAction action,
        string rawPayload,
        CanonicalProviderError error)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(error);

        var payloadHash = FawryHash.CreateRaw(rawPayload ?? string.Empty);
        var source = string.Join(
            "|",
            "fawry-webhook-provider-error",
            action.Id.Value.ToString("N"),
            action.CorrelationId.Value,
            error.Type,
            error.Code,
            payloadHash);
        var evidenceId = FawryDeterministicId.Create("fawry-webhook-provider-error", source);
        var metadata = error.Metadata
            .Concat(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["adapter"] = nameof(FawryWebhookEvidenceAdapter),
                ["provider"] = FawryProviderReference.ProviderName,
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
            new EvidenceReference($"fawry:webhook:error:{error.Code}"),
            new EvidenceSource(FawryProviderReference.ProviderName),
            new EvidenceTimestamp(action.CreatedUtc.AddTicks(-1)),
            new EvidenceHash(FawryHash.Create(source)),
            new ProviderPayloadReference($"fawry:webhook:error:{error.Code}"),
            action.CorrelationId,
            new EvidenceMetadata(metadata));
    }
}

public sealed record FawryWebhookTranslationResult
{
    public bool Succeeded { get; init; }
    public ProviderEvidence? Evidence { get; init; }
    public FawryWebhookTranslationMetadata Translation { get; init; }
    public CanonicalProviderError? Error { get; init; }
    public CanonicalProviderError? ProviderError { get; init; }

    private FawryWebhookTranslationResult()
    {
        Translation = new FawryWebhookTranslationMetadata();
    }

    public static FawryWebhookTranslationResult Success(
        ProviderEvidence evidence,
        FawryWebhookTranslationMetadata translation,
        CanonicalProviderError? providerError)
    {
        return new FawryWebhookTranslationResult
        {
            Succeeded = true,
            Evidence = evidence,
            Translation = translation,
            ProviderError = providerError
        };
    }

    public static FawryWebhookTranslationResult Failed(CanonicalProviderError error)
    {
        return new FawryWebhookTranslationResult
        {
            Succeeded = false,
            Error = error
        };
    }
}

public static class FawryWebhookErrorTranslator
{
    public static CanonicalProviderError? TranslateProviderFailure(FawryCallback callback)
    {
        if (!callback.OrderStatus.Equals("FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = FawryProviderReference.ProviderName,
            ["provider_event_id"] = callback.RequestId,
            ["provider_reference"] = callback.FawryRefNumber,
            ["external_status"] = callback.OrderStatus
        };

        if (!string.IsNullOrWhiteSpace(callback.FailureErrorCode))
        {
            metadata["provider_response_code"] = callback.FailureErrorCode;
        }

        return CanonicalProviderError.Create(
            FawryProviderReference.ProviderName,
            CanonicalProviderErrorType.ProviderRejected,
            "FAWRY_TRANSACTION_FAILED",
            string.IsNullOrWhiteSpace(callback.FailureReason) ? "Fawry transaction failed." : callback.FailureReason,
            callback.RequestId,
            metadata);
    }

    public static CanonicalProviderError Replay(string code, string message)
    {
        return CanonicalProviderError.Create(
            FawryProviderReference.ProviderName,
            CanonicalProviderErrorType.Validation,
            string.IsNullOrWhiteSpace(code) ? "FAWRY_WEBHOOK_REPLAY_REJECTED" : code,
            string.IsNullOrWhiteSpace(message) ? "Fawry webhook replay was rejected." : message,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["hook"] = "replay" });
    }

    public static CanonicalProviderError Duplicate(string code, string message)
    {
        return CanonicalProviderError.Create(
            FawryProviderReference.ProviderName,
            CanonicalProviderErrorType.Validation,
            string.IsNullOrWhiteSpace(code) ? "FAWRY_WEBHOOK_DUPLICATE_REJECTED" : code,
            string.IsNullOrWhiteSpace(message) ? "Fawry webhook duplicate was rejected." : message,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["hook"] = "duplicate" });
    }
}

public static class FawryWebhookMetadataFactory
{
    public static IReadOnlyDictionary<string, string> Create(
        PaymentAction action,
        FawryCallback callback,
        string evidenceHash,
        CanonicalProviderError? providerError,
        FawryWebhookEvidenceKind kind)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["adapter"] = nameof(FawryWebhookEvidenceAdapter),
            ["provider"] = FawryProviderReference.ProviderName,
            ["evidence_ingress"] = kind.ToString(),
            ["event_type"] = callback.EventType,
            ["event_id"] = callback.RequestId,
            ["provider_event_id"] = callback.RequestId,
            ["provider_reference"] = callback.ProviderReference,
            ["fawry_ref_number"] = callback.FawryRefNumber,
            ["merchant_reference"] = callback.MerchantRefNumber,
            ["payment_reference_number"] = callback.PaymentReferenceNumber,
            ["order_status"] = callback.OrderStatus,
            ["canonical_status"] = FawryChargeResponseMapper.MapStatus(callback.OrderStatus).ToString(),
            ["payment_method"] = callback.PaymentMethod,
            ["payment_amount"] = callback.PaymentAmount,
            ["order_amount"] = callback.OrderAmount,
            ["occurred_timestamp"] = callback.OccurredAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ["correlation_id"] = action.CorrelationId.Value,
            ["event_payload_hash"] = callback.PayloadHash,
            ["evidence_hash"] = evidenceHash,
            ["evidence_id_source"] = "event_type|request_id|fawry_ref_number|evidence_hash"
        };

        if (action.Metadata.Values.TryGetValue(FawryRequestMetadataKeys.Currency, out var currency) &&
            !string.IsNullOrWhiteSpace(currency))
        {
            values["currency"] = currency.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(callback.FailureErrorCode))
        {
            values["failure_error_code"] = callback.FailureErrorCode;
        }

        if (!string.IsNullOrWhiteSpace(callback.FailureReason))
        {
            values["failure_reason"] = callback.FailureReason;
        }

        if (providerError is not null)
        {
            values["provider_error_type"] = providerError.Type.ToString();
            values["provider_error_code"] = providerError.Code;
            values["provider_error_message"] = providerError.Message;
        }

        return new ReadOnlyDictionary<string, string>(
            values
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.Ordinal));
    }
}

public sealed record FawryCallback(
    string EventType,
    string RequestId,
    string FawryRefNumber,
    string MerchantRefNumber,
    string PaymentReferenceNumber,
    string OrderStatus,
    string PaymentMethod,
    string PaymentAmount,
    string OrderAmount,
    string FailureErrorCode,
    string FailureReason,
    string ProviderReference,
    string EvidenceReference,
    DateTimeOffset? OccurredAtUtc,
    string PayloadHash,
    string SignaturePayload,
    string MessageSignature);

public sealed record FawryOfficialNotificationPayload
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }

    [JsonPropertyName("fawryRefNumber")]
    public string? FawryRefNumber { get; init; }

    [JsonPropertyName("merchantRefNumber")]
    public string? MerchantRefNumber { get; init; }

    [JsonPropertyName("paymentAmount")]
    public decimal? PaymentAmount { get; init; }

    [JsonPropertyName("orderAmount")]
    public decimal? OrderAmount { get; init; }

    [JsonPropertyName("orderStatus")]
    public string? OrderStatus { get; init; }

    [JsonPropertyName("paymentMethod")]
    public string? PaymentMethod { get; init; }

    [JsonPropertyName("paymentTime")]
    public string? PaymentTime { get; init; }

    [JsonPropertyName("paymentReferenceNumber")]
    public string? PaymentReferenceNumber { get; init; }

    [JsonPropertyName("paymentRefrenceNumber")]
    public string? PaymentRefrenceNumber { get; init; }

    [JsonPropertyName("failureErrorCode")]
    public int? FailureErrorCode { get; init; }

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; init; }

    [JsonPropertyName("messageSignature")]
    public string? MessageSignature { get; init; }
}
