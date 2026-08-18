using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;

namespace PointOfSale.API.Services.Payments.Paymob;

public interface IPaymobWebhookEvidenceAdapter
{
    Task<PaymobWebhookEvidenceAdapterResult> ProcessAsync(
        PaymobWebhookEvidenceRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PaymobWebhookEvidenceAdapter : IPaymobWebhookEvidenceAdapter
{
    private readonly PaymentRuntimeOrchestrator _orchestrator;

    public PaymobWebhookEvidenceAdapter(PaymentRuntimeOrchestrator orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public async Task<PaymobWebhookEvidenceAdapterResult> ProcessAsync(
        PaymobWebhookEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var verification = PaymobWebhookHmacVerifier.Verify(request);
        if (!verification.Succeeded)
        {
            return ExecuteProviderError(request, verification.Error);
        }

        var replay = await InvokeHookAsync(request.ReplayDetectionHook, verification.Callback!, cancellationToken);
        if (replay.Rejected)
        {
            return ExecuteProviderError(
                request,
                PaymobWebhookErrorTranslator.Replay(replay.Code, replay.Message));
        }

        var duplicate = await InvokeHookAsync(request.DuplicateDetectionHook, verification.Callback!, cancellationToken);
        if (duplicate.Rejected)
        {
            return ExecuteProviderError(
                request,
                PaymobWebhookErrorTranslator.Duplicate(duplicate.Code, duplicate.Message));
        }

        var translation = PaymobWebhookEvidenceTranslator.Translate(
            request.Action,
            verification.Callback!,
            request.RawPayload);
        if (!translation.Succeeded)
        {
            return ExecuteProviderError(request, translation.Error);
        }

        var runtimeResult = ExecuteRuntime(request, translation.Evidence!);
        return PaymobWebhookEvidenceAdapterResult.Success(
            translation.Evidence!,
            runtimeResult,
            verification.Callback!,
            translation.Translation,
            translation.ProviderError);
    }

    private PaymobWebhookEvidenceAdapterResult ExecuteProviderError(
        PaymobWebhookEvidenceRequest request,
        CanonicalProviderError? error)
    {
        var resolvedError = error ?? CanonicalProviderError.Configuration(
            PaymobProviderReference.ProviderName,
            "PAYMOB_WEBHOOK_ERROR_MISSING",
            "Paymob webhook adapter failed without error details.");
        var evidence = PaymobWebhookEvidenceTranslator.ProviderErrorEvidence(
            request.Action,
            request.RawPayload,
            resolvedError);
        var runtimeResult = ExecuteRuntime(request, evidence);

        return PaymobWebhookEvidenceAdapterResult.Failed(evidence, runtimeResult, resolvedError);
    }

    private RuntimeResult ExecuteRuntime(
        PaymobWebhookEvidenceRequest request,
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

    private static async ValueTask<PaymobWebhookHookDecision> InvokeHookAsync(
        PaymobWebhookDetectionHook? hook,
        PaymobCallback callback,
        CancellationToken cancellationToken)
    {
        if (hook is null)
        {
            return PaymobWebhookHookDecision.Accepted();
        }

        return await hook(
            PaymobWebhookHookContext.FromCallback(callback),
            cancellationToken);
    }
}

public sealed record PaymobWebhookEvidenceRequest
{
    public string RawPayload { get; init; }

    public string HmacSignature { get; init; }

    public string HmacSecret { get; init; }

    public TimeSpan? TimestampTolerance { get; init; }

    public DateTimeOffset? ReceivedAtUtc { get; init; }

    public RuntimeExecutionContext Context { get; init; }

    public PaymentAction Action { get; init; }

    public PaymentSession Session { get; init; }

    public IReadOnlyCollection<AdmittedEvidence>? PriorEvidence { get; init; }

    public IReadOnlyCollection<StateTransition>? PriorTransitions { get; init; }

    public IReadOnlyCollection<PaymentTransaction>? PriorTransactions { get; init; }

    public IReadOnlyCollection<PaymentSettlement>? PriorSettlements { get; init; }

    public PaymobWebhookDetectionHook? ReplayDetectionHook { get; init; }

    public PaymobWebhookDetectionHook? DuplicateDetectionHook { get; init; }

    public PaymobWebhookEvidenceRequest()
    {
        RawPayload = string.Empty;
        HmacSignature = string.Empty;
        HmacSecret = string.Empty;
        Context = new RuntimeExecutionContext();
        Action = new PaymentAction();
        Session = new PaymentSession();
    }

    public PaymobWebhookEvidenceRequest(
        string rawPayload,
        string hmacSignature,
        string hmacSecret,
        RuntimeExecutionContext context,
        PaymentAction action,
        PaymentSession session,
        TimeSpan? timestampTolerance = null,
        DateTimeOffset? receivedAtUtc = null,
        IReadOnlyCollection<AdmittedEvidence>? priorEvidence = null,
        IReadOnlyCollection<StateTransition>? priorTransitions = null,
        IReadOnlyCollection<PaymentTransaction>? priorTransactions = null,
        IReadOnlyCollection<PaymentSettlement>? priorSettlements = null,
        PaymobWebhookDetectionHook? replayDetectionHook = null,
        PaymobWebhookDetectionHook? duplicateDetectionHook = null)
    {
        RawPayload = rawPayload ?? string.Empty;
        HmacSignature = hmacSignature ?? string.Empty;
        HmacSecret = hmacSecret ?? string.Empty;
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        TimestampTolerance = timestampTolerance;
        ReceivedAtUtc = receivedAtUtc;
        PriorEvidence = priorEvidence is null ? null : Array.AsReadOnly(priorEvidence.ToArray());
        PriorTransitions = priorTransitions is null ? null : Array.AsReadOnly(priorTransitions.ToArray());
        PriorTransactions = priorTransactions is null ? null : Array.AsReadOnly(priorTransactions.ToArray());
        PriorSettlements = priorSettlements is null ? null : Array.AsReadOnly(priorSettlements.ToArray());
        ReplayDetectionHook = replayDetectionHook;
        DuplicateDetectionHook = duplicateDetectionHook;
    }
}

public sealed record PaymobWebhookEvidenceAdapterResult
{
    public bool Succeeded { get; init; }

    public ProviderEvidence ProviderEvidence { get; init; }

    public RuntimeResult RuntimeResult { get; init; }

    public CanonicalProviderError? Error { get; init; }

    public string? PaymobTransactionId { get; init; }

    public string? PaymobEventType { get; init; }

    public PaymobWebhookTranslationMetadata Translation { get; init; }

    public PaymobWebhookEvidenceAdapterResult()
    {
        ProviderEvidence = new ProviderEvidence();
        RuntimeResult = new RuntimeResult();
        Translation = new PaymobWebhookTranslationMetadata();
    }

    private PaymobWebhookEvidenceAdapterResult(
        bool succeeded,
        ProviderEvidence providerEvidence,
        RuntimeResult runtimeResult,
        CanonicalProviderError? error,
        string? paymobTransactionId,
        string? paymobEventType,
        PaymobWebhookTranslationMetadata? translation)
    {
        Succeeded = succeeded;
        ProviderEvidence = providerEvidence ?? throw new ArgumentNullException(nameof(providerEvidence));
        RuntimeResult = runtimeResult ?? throw new ArgumentNullException(nameof(runtimeResult));
        Error = error;
        PaymobTransactionId = paymobTransactionId;
        PaymobEventType = paymobEventType;
        Translation = translation ?? new PaymobWebhookTranslationMetadata();
    }

    public static PaymobWebhookEvidenceAdapterResult Success(
        ProviderEvidence evidence,
        RuntimeResult runtimeResult,
        PaymobCallback callback,
        PaymobWebhookTranslationMetadata translation,
        CanonicalProviderError? providerError)
    {
        return new PaymobWebhookEvidenceAdapterResult(
            true,
            evidence,
            runtimeResult,
            providerError,
            callback.TransactionId,
            callback.EventType,
            translation);
    }

    public static PaymobWebhookEvidenceAdapterResult Failed(
        ProviderEvidence evidence,
        RuntimeResult runtimeResult,
        CanonicalProviderError error)
    {
        return new PaymobWebhookEvidenceAdapterResult(
            false,
            evidence,
            runtimeResult,
            error,
            null,
            null,
            null);
    }
}

public sealed record PaymobWebhookTranslationMetadata
{
    public string EventType { get; init; }

    public string TransactionId { get; init; }

    public string ProviderReference { get; init; }

    public string TransactionReference { get; init; }

    public string ExternalStatus { get; init; }

    public string PayloadHash { get; init; }

    public string EvidenceHash { get; init; }

    public PaymobWebhookTranslationMetadata()
    {
        EventType = string.Empty;
        TransactionId = string.Empty;
        ProviderReference = string.Empty;
        TransactionReference = string.Empty;
        ExternalStatus = string.Empty;
        PayloadHash = string.Empty;
        EvidenceHash = string.Empty;
    }

    public PaymobWebhookTranslationMetadata(
        string eventType,
        string transactionId,
        string providerReference,
        string transactionReference,
        string externalStatus,
        string payloadHash,
        string evidenceHash)
    {
        EventType = eventType;
        TransactionId = transactionId;
        ProviderReference = providerReference;
        TransactionReference = transactionReference;
        ExternalStatus = externalStatus;
        PayloadHash = payloadHash;
        EvidenceHash = evidenceHash;
    }
}

public delegate ValueTask<PaymobWebhookHookDecision> PaymobWebhookDetectionHook(
    PaymobWebhookHookContext context,
    CancellationToken cancellationToken);

public sealed record PaymobWebhookHookContext(
    string Provider,
    string EventType,
    string TransactionId,
    string ProviderReference,
    string EvidenceReference,
    string PayloadHash,
    DateTimeOffset OccurredAtUtc)
{
    public static PaymobWebhookHookContext FromCallback(PaymobCallback callback)
    {
        return new PaymobWebhookHookContext(
            PaymobProviderReference.ProviderName,
            callback.EventType,
            callback.TransactionId,
            callback.ProviderReference,
            callback.EvidenceReference,
            callback.PayloadHash,
            callback.OccurredAtUtc);
    }
}

public sealed record PaymobWebhookHookDecision
{
    public bool Rejected { get; init; }

    public string Code { get; init; }

    public string Message { get; init; }

    private PaymobWebhookHookDecision()
    {
        Code = string.Empty;
        Message = string.Empty;
    }

    public static PaymobWebhookHookDecision Accepted()
    {
        return new PaymobWebhookHookDecision();
    }

    public static PaymobWebhookHookDecision Reject(string code, string message)
    {
        return new PaymobWebhookHookDecision
        {
            Rejected = true,
            Code = string.IsNullOrWhiteSpace(code) ? "PAYMOB_WEBHOOK_HOOK_REJECTED" : code.Trim(),
            Message = string.IsNullOrWhiteSpace(message) ? "Paymob webhook hook rejected the callback." : message.Trim()
        };
    }
}

public static class PaymobWebhookHmacVerifier
{
    public static PaymobWebhookVerificationResult Verify(PaymobWebhookEvidenceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.HmacSignature))
        {
            return PaymobWebhookVerificationResult.Failed(
                CanonicalProviderError.Validation(PaymobProviderReference.ProviderName, "PAYMOB_WEBHOOK_HMAC_MISSING", "Paymob HMAC signature is required."));
        }

        if (string.IsNullOrWhiteSpace(request.HmacSecret))
        {
            return PaymobWebhookVerificationResult.Failed(
                CanonicalProviderError.Configuration(PaymobProviderReference.ProviderName, "PAYMOB_WEBHOOK_HMAC_SECRET_MISSING", "Paymob HMAC secret is required."));
        }

        try
        {
            var callback = PaymobWebhookPayloadParser.Parse(request.RawPayload);
            if (request.TimestampTolerance.HasValue)
            {
                var timestampValidation = ValidateTimestamp(
                    callback,
                    request.ReceivedAtUtc ?? DateTimeOffset.UtcNow,
                    request.TimestampTolerance.Value);
                if (timestampValidation is not null)
                {
                    return PaymobWebhookVerificationResult.Failed(timestampValidation);
                }
            }

            var expected = ComputeHmac(callback.HmacPayload, request.HmacSecret);
            if (!FixedTimeHexEquals(expected, request.HmacSignature))
            {
                return PaymobWebhookVerificationResult.Failed(
                    CanonicalProviderError.Validation(PaymobProviderReference.ProviderName, "PAYMOB_WEBHOOK_HMAC_INVALID", "Paymob HMAC signature is invalid."));
            }

            return PaymobWebhookVerificationResult.Success(callback);
        }
        catch (JsonException exception)
        {
            return PaymobWebhookVerificationResult.Failed(
                CanonicalProviderError.Validation(PaymobProviderReference.ProviderName, "PAYMOB_WEBHOOK_MALFORMED_PAYLOAD", exception.Message));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            return PaymobWebhookVerificationResult.Failed(
                CanonicalProviderError.Validation(PaymobProviderReference.ProviderName, "PAYMOB_WEBHOOK_MALFORMED_PAYLOAD", exception.Message));
        }
    }

    public static string ComputeHmac(string canonicalPayload, string hmacSecret)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(hmacSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalPayload))).ToLowerInvariant();
    }

    private static CanonicalProviderError? ValidateTimestamp(
        PaymobCallback callback,
        DateTimeOffset receivedAtUtc,
        TimeSpan tolerance)
    {
        if (tolerance <= TimeSpan.Zero)
        {
            return CanonicalProviderError.Configuration(
                PaymobProviderReference.ProviderName,
                "PAYMOB_WEBHOOK_TIMESTAMP_TOLERANCE_INVALID",
                "Paymob webhook timestamp tolerance must be positive.");
        }

        var delta = (receivedAtUtc.ToUniversalTime() - callback.OccurredAtUtc.ToUniversalTime()).Duration();
        return delta > tolerance
            ? CanonicalProviderError.Validation(PaymobProviderReference.ProviderName, "PAYMOB_WEBHOOK_TIMESTAMP_EXPIRED", "Paymob webhook timestamp is outside the allowed tolerance.")
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

public sealed record PaymobWebhookVerificationResult
{
    public bool Succeeded { get; init; }

    public PaymobCallback? Callback { get; init; }

    public CanonicalProviderError? Error { get; init; }

    private PaymobWebhookVerificationResult()
    {
    }

    public static PaymobWebhookVerificationResult Success(PaymobCallback callback)
    {
        return new PaymobWebhookVerificationResult
        {
            Succeeded = true,
            Callback = callback
        };
    }

    public static PaymobWebhookVerificationResult Failed(CanonicalProviderError error)
    {
        return new PaymobWebhookVerificationResult
        {
            Succeeded = false,
            Error = error
        };
    }
}

public static class PaymobWebhookPayloadParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static PaymobCallback Parse(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            throw new ArgumentException("Paymob callback payload is required.", nameof(rawPayload));
        }

        if (!rawPayload.TrimStart().StartsWith('{'))
        {
            return ParseQueryString(rawPayload);
        }

        using var document = JsonDocument.Parse(rawPayload);
        var root = document.RootElement;
        var transaction = root.TryGetProperty("obj", out var obj) && obj.ValueKind == JsonValueKind.Object
            ? obj
            : root;
        var payload = JsonSerializer.Deserialize<PaymobOfficialCallbackPayload>(
            transaction.GetRawText(),
            JsonOptions);

        if (payload is null)
        {
            throw new JsonException("Paymob callback payload could not be deserialized.");
        }

        var eventType = ResolveEventType(root);
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new InvalidOperationException("Paymob callback event type is required.");
        }

        var transactionId = Required(payload.Id, "Paymob transaction id");
        var orderId = Required(payload.Order?.Id, "Paymob order id");
        var occurredAtUtc = ParseTimestamp(payload.CreatedAt);
        var payloadHash = PaymobHash.Create(rawPayload);
        var hmacPayload = BuildHmacPayload(transaction);
        var providerReference = $"paymob:order:{orderId}";
        var transactionReference = $"paymob:transaction:{transactionId}";

        return new PaymobCallback(
            eventType,
            transactionId.ToString(CultureInfo.InvariantCulture),
            orderId,
            payload.Order?.MerchantOrderId ?? string.Empty,
            providerReference,
            transactionReference,
            BuildIdempotencyKey(transactionId, payload),
            MapExternalStatus(payload),
            occurredAtUtc,
            payloadHash,
            hmacPayload,
            payload);
    }

    private static PaymobCallback ParseQueryString(string rawPayload)
    {
        var values = ParseQueryValues(rawPayload);
        var transactionId = Required(ParseLong(values, "id"), "Paymob transaction id");
        var orderId = Required(ParseLong(values, "order"), "Paymob order id");
        var occurredAtUtc = ParseTimestamp(Get(values, "created_at"));
        var payloadHash = PaymobHash.Create(rawPayload);
        var hmacPayload = BuildHmacPayload(values);
        var providerReference = $"paymob:order:{orderId}";
        var transactionReference = $"paymob:transaction:{transactionId}";
        var payload = new PaymobOfficialCallbackPayload
        {
            Id = transactionId,
            Pending = ParseBool(values, "pending"),
            AmountCents = ParseLong(values, "amount_cents"),
            Success = ParseBool(values, "success"),
            IsAuth = ParseBool(values, "is_auth"),
            IsCapture = ParseBool(values, "is_capture"),
            IsStandalonePayment = ParseBool(values, "is_standalone_payment"),
            IsVoided = ParseBool(values, "is_voided"),
            IsRefunded = ParseBool(values, "is_refunded"),
            Is3dSecure = ParseBool(values, "is_3d_secure"),
            IntegrationId = ParseLong(values, "integration_id"),
            HasParentTransaction = ParseBool(values, "has_parent_transaction"),
            Order = new PaymobOfficialOrder { Id = orderId },
            CreatedAt = Get(values, "created_at"),
            Currency = Get(values, "currency"),
            SourceData = new PaymobOfficialSourceData
            {
                Pan = Get(values, "source_data.pan"),
                SubType = Get(values, "source_data.sub_type"),
                Type = Get(values, "source_data.type")
            },
            Owner = ParseLong(values, "owner"),
            ErrorOccured = ParseBool(values, "error_occured")
        };

        return new PaymobCallback(
            "CALLBACK",
            transactionId.ToString(CultureInfo.InvariantCulture),
            orderId,
            string.Empty,
            providerReference,
            transactionReference,
            BuildIdempotencyKey(transactionId, payload),
            MapExternalStatus(payload),
            occurredAtUtc,
            payloadHash,
            hmacPayload,
            payload);
    }

    private static string ResolveEventType(JsonElement root)
    {
        if (root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
        {
            return type.GetString() ?? string.Empty;
        }

        return root.TryGetProperty("obj", out _) ? "TRANSACTION" : "CALLBACK";
    }

    private static string BuildIdempotencyKey(long transactionId, PaymobOfficialCallbackPayload payload)
    {
        return payload.IsRefunded
            ? $"paymob:webhook:{transactionId}:refund:{payload.AmountCents ?? 0}"
            : $"paymob:webhook:{transactionId}";
    }

    private static string BuildHmacPayload(JsonElement transaction)
    {
        return string.Concat(
            GetJsonNumericString(transaction, "amount_cents"),
            GetJsonString(transaction, "created_at"),
            GetJsonString(transaction, "currency"),
            GetJsonBooleanString(transaction, "error_occured"),
            GetJsonBooleanString(transaction, "has_parent_transaction"),
            GetJsonNumericString(transaction, "id"),
            GetJsonNumericString(transaction, "integration_id"),
            GetJsonBooleanString(transaction, "is_3d_secure"),
            GetJsonBooleanString(transaction, "is_auth"),
            GetJsonBooleanString(transaction, "is_capture"),
            GetJsonBooleanString(transaction, "is_refunded"),
            GetJsonBooleanString(transaction, "is_standalone_payment"),
            GetJsonBooleanString(transaction, "is_voided"),
            GetOrderValue(transaction),
            GetJsonNumericString(transaction, "owner"),
            GetJsonBooleanString(transaction, "pending"),
            GetJsonNestedString(transaction, "source_data", "pan"),
            GetJsonNestedString(transaction, "source_data", "sub_type"),
            GetJsonNestedString(transaction, "source_data", "type"),
            GetJsonBooleanString(transaction, "success"));
    }

    private static string BuildHmacPayload(IReadOnlyDictionary<string, string> values)
    {
        return string.Concat(
            Get(values, "amount_cents"),
            Get(values, "created_at"),
            Get(values, "currency"),
            NormalizeBoolean(Get(values, "error_occured")),
            NormalizeBoolean(Get(values, "has_parent_transaction")),
            Get(values, "id"),
            Get(values, "integration_id"),
            NormalizeBoolean(Get(values, "is_3d_secure")),
            NormalizeBoolean(Get(values, "is_auth")),
            NormalizeBoolean(Get(values, "is_capture")),
            NormalizeBoolean(Get(values, "is_refunded")),
            NormalizeBoolean(Get(values, "is_standalone_payment")),
            NormalizeBoolean(Get(values, "is_voided")),
            Get(values, "order"),
            Get(values, "owner"),
            NormalizeBoolean(Get(values, "pending")),
            Get(values, "source_data.pan"),
            Get(values, "source_data.sub_type"),
            Get(values, "source_data.type"),
            NormalizeBoolean(Get(values, "success")));
    }

    private static string GetOrderValue(JsonElement transaction)
    {
        if (!transaction.TryGetProperty("order", out var order))
        {
            return string.Empty;
        }

        return order.ValueKind == JsonValueKind.Object
            ? GetJsonNumericString(order, "id")
            : GetElementString(order);
    }

    private static DateTimeOffset ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Paymob callback created_at is required.");
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : throw new FormatException("Paymob callback created_at is invalid.");
    }

    private static string MapExternalStatus(PaymobOfficialCallbackPayload payload)
    {
        if (payload.Pending)
        {
            return "pending";
        }

        if (payload.IsRefunded)
        {
            return "refunded";
        }

        if (payload.IsVoided)
        {
            return "voided";
        }

        if (payload.Success)
        {
            return payload.IsAuth && !payload.IsCapture
                ? "authorized"
                : "succeeded";
        }

        if (payload.ErrorOccured)
        {
            return "failed";
        }

        return "declined";
    }

    private static long Required(long? value, string name)
    {
        if (!value.HasValue || value.Value <= 0)
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        return value.Value;
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? GetElementString(property)
            : string.Empty;
    }

    private static string GetJsonNumericString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => string.Empty
        };
    }

    private static string GetJsonBooleanString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed ? "true" : "false",
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => string.Empty
        };
    }

    private static string GetJsonNestedString(JsonElement element, string parentPropertyName, string propertyName)
    {
        return element.TryGetProperty(parentPropertyName, out var parent) && parent.ValueKind == JsonValueKind.Object
            ? GetJsonString(parent, propertyName)
            : string.Empty;
    }

    private static string GetElementString(JsonElement property)
    {
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => property.GetRawText().Trim('"')
        };
    }

    private static IReadOnlyDictionary<string, string> ParseQueryValues(string rawPayload)
    {
        var query = rawPayload.TrimStart('?');
        return query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var pieces = part.Split('=', 2);
                var key = WebUtility.UrlDecode(pieces[0]) ?? string.Empty;
                var value = pieces.Length == 2 ? WebUtility.UrlDecode(pieces[1]) ?? string.Empty : string.Empty;
                return new KeyValuePair<string, string>(key, value);
            })
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static long? ParseLong(IReadOnlyDictionary<string, string> values, string key)
    {
        return long.TryParse(Get(values, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool ParseBool(IReadOnlyDictionary<string, string> values, string key)
    {
        return bool.TryParse(Get(values, key), out var parsed) && parsed;
    }

    private static string NormalizeBoolean(string value)
    {
        return bool.TryParse(value, out var parsed)
            ? parsed.ToString().ToLowerInvariant()
            : value;
    }
}

public static class PaymobWebhookEvidenceTranslator
{
    private static readonly ISet<string> SupportedEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "TRANSACTION",
        "CALLBACK"
    };

    public static PaymobWebhookTranslationResult Translate(
        PaymentAction action,
        PaymobCallback callback,
        string rawPayload)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(callback);

        if (!SupportedEvents.Contains(callback.EventType))
        {
            return PaymobWebhookTranslationResult.Failed(
                CanonicalProviderError.Validation(PaymobProviderReference.ProviderName, "PAYMOB_WEBHOOK_EVENT_UNSUPPORTED", $"Paymob event '{callback.EventType}' is not mapped to canonical evidence."));
        }

        var providerError = PaymobWebhookErrorTranslator.TranslateProviderFailure(callback);
        var payloadHash = callback.PayloadHash;
        var evidenceHash = PaymobHash.Create(
            callback.EventType,
            callback.TransactionId,
            callback.OrderId.ToString(CultureInfo.InvariantCulture),
            callback.ExternalStatus,
            callback.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            payloadHash);
        var evidenceId = PaymobDeterministicId.Create(
            "paymob-webhook-evidence",
            callback.EventType,
            callback.TransactionId,
            callback.OrderId.ToString(CultureInfo.InvariantCulture),
            evidenceHash);
        var metadata = PaymobWebhookMetadataFactory.Create(
            action,
            callback,
            payloadHash,
            evidenceHash,
            providerError);

        var evidence = ProviderEvidenceFactory.Create(
            new EvidenceId(evidenceId),
            action.Id,
            EvidenceType.Webhook,
            EvidenceStatus.Recorded,
            new EvidenceReference(callback.EvidenceReference),
            new EvidenceSource(PaymobProviderReference.ProviderName),
            new EvidenceTimestamp(callback.OccurredAtUtc),
            new EvidenceHash(evidenceHash),
            new ProviderPayloadReference(callback.TransactionReference),
            action.CorrelationId,
            new EvidenceMetadata(metadata));
        var translation = new PaymobWebhookTranslationMetadata(
            callback.EventType,
            callback.TransactionId,
            callback.ProviderReference,
            callback.TransactionReference,
            callback.ExternalStatus,
            payloadHash,
            evidenceHash);

        return PaymobWebhookTranslationResult.Success(evidence, translation, providerError);
    }

    public static ProviderEvidence ProviderErrorEvidence(
        PaymentAction action,
        string rawPayload,
        CanonicalProviderError error)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(error);

        var payloadHash = PaymobHash.Create(rawPayload ?? string.Empty);
        var source = string.Join(
            "|",
            "paymob-webhook-provider-error",
            action.Id.Value.ToString("N"),
            action.CorrelationId.Value,
            error.Type,
            error.Code,
            payloadHash);
        var evidenceId = PaymobDeterministicId.Create(source);
        var timestamp = action.CreatedUtc.AddTicks(-1);
        var metadata = error.Metadata
            .Concat(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["adapter"] = nameof(PaymobWebhookEvidenceAdapter),
                ["provider"] = PaymobProviderReference.ProviderName,
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
            new EvidenceReference($"paymob:webhook:error:{error.Code}"),
            new EvidenceSource(PaymobProviderReference.ProviderName),
            new EvidenceTimestamp(timestamp),
            new EvidenceHash(PaymobHash.Create(source)),
            new ProviderPayloadReference($"paymob:webhook:error:{error.Code}"),
            action.CorrelationId,
            new EvidenceMetadata(metadata));
    }
}

public sealed record PaymobWebhookTranslationResult
{
    public bool Succeeded { get; init; }

    public ProviderEvidence? Evidence { get; init; }

    public PaymobWebhookTranslationMetadata Translation { get; init; }

    public CanonicalProviderError? Error { get; init; }

    public CanonicalProviderError? ProviderError { get; init; }

    private PaymobWebhookTranslationResult()
    {
        Translation = new PaymobWebhookTranslationMetadata();
    }

    public static PaymobWebhookTranslationResult Success(
        ProviderEvidence evidence,
        PaymobWebhookTranslationMetadata translation,
        CanonicalProviderError? providerError)
    {
        return new PaymobWebhookTranslationResult
        {
            Succeeded = true,
            Evidence = evidence,
            Translation = translation,
            ProviderError = providerError
        };
    }

    public static PaymobWebhookTranslationResult Failed(CanonicalProviderError error)
    {
        return new PaymobWebhookTranslationResult
        {
            Succeeded = false,
            Error = error
        };
    }
}

public static class PaymobWebhookErrorTranslator
{
    public static CanonicalProviderError? TranslateProviderFailure(PaymobCallback callback)
    {
        if (callback.Payload.Success && !callback.Payload.ErrorOccured)
        {
            return null;
        }

        var message = FirstNonBlank(
            callback.Payload.Data?.Message,
            callback.Payload.DataMessage,
            "Paymob transaction was not successful.");
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = PaymobProviderReference.ProviderName,
            ["provider_event_id"] = callback.TransactionId,
            ["provider_order_id"] = callback.OrderId.ToString(CultureInfo.InvariantCulture),
            ["external_status"] = callback.ExternalStatus
        };

        if (!string.IsNullOrWhiteSpace(callback.Payload.Data?.TxnResponseCode))
        {
            metadata["provider_response_code"] = callback.Payload.Data.TxnResponseCode;
        }

        return new CanonicalProviderError(
            CanonicalProviderErrorType.ProviderRejected,
            "PAYMOB_TRANSACTION_FAILED",
            message,
            callback.TransactionId,
            metadata);
    }

    public static CanonicalProviderError Replay(string code, string message)
    {
        return new CanonicalProviderError(
            CanonicalProviderErrorType.Validation,
            string.IsNullOrWhiteSpace(code) ? "PAYMOB_WEBHOOK_REPLAY_REJECTED" : code,
            string.IsNullOrWhiteSpace(message) ? "Paymob webhook replay was rejected." : message,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider"] = PaymobProviderReference.ProviderName,
                ["hook"] = "replay"
            });
    }

    public static CanonicalProviderError Duplicate(string code, string message)
    {
        return new CanonicalProviderError(
            CanonicalProviderErrorType.Validation,
            string.IsNullOrWhiteSpace(code) ? "PAYMOB_WEBHOOK_DUPLICATE_REJECTED" : code,
            string.IsNullOrWhiteSpace(message) ? "Paymob webhook duplicate was rejected." : message,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider"] = PaymobProviderReference.ProviderName,
                ["hook"] = "duplicate"
            });
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
    }
}

public static class PaymobWebhookMetadataFactory
{
    public static IReadOnlyDictionary<string, string> Create(
        PaymentAction action,
        PaymobCallback callback,
        string payloadHash,
        string evidenceHash,
        CanonicalProviderError? providerError)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["adapter"] = nameof(PaymobWebhookEvidenceAdapter),
            ["provider"] = PaymobProviderReference.ProviderName,
            ["event_type"] = callback.EventType,
            ["event_id"] = callback.TransactionId,
            ["provider_event_id"] = callback.TransactionId,
            ["provider_reference"] = callback.ProviderReference,
            ["provider_order_id"] = callback.OrderId.ToString(CultureInfo.InvariantCulture),
            ["provider_merchant_order_id"] = callback.MerchantOrderId,
            ["transaction_reference"] = callback.TransactionReference,
            ["external_status"] = callback.ExternalStatus,
            ["success"] = callback.Payload.Success.ToString(CultureInfo.InvariantCulture),
            ["pending"] = callback.Payload.Pending.ToString(CultureInfo.InvariantCulture),
            ["amount_cents"] = (callback.Payload.AmountCents ?? 0).ToString(CultureInfo.InvariantCulture),
            ["currency"] = callback.Payload.Currency ?? string.Empty,
            ["occurred_timestamp"] = callback.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["correlation_id"] = action.CorrelationId.Value,
            ["event_payload_hash"] = payloadHash,
            ["evidence_hash"] = evidenceHash,
            ["evidence_id_source"] = "event_type|transaction_id|order_id|evidence_hash"
        };

        if (!string.IsNullOrWhiteSpace(callback.Payload.SourceData?.Type))
        {
            values["source_data_type"] = callback.Payload.SourceData.Type;
        }

        if (!string.IsNullOrWhiteSpace(callback.Payload.SourceData?.SubType))
        {
            values["source_data_sub_type"] = callback.Payload.SourceData.SubType;
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

public sealed record PaymobCallback(
    string EventType,
    string TransactionId,
    long OrderId,
    string MerchantOrderId,
    string ProviderReference,
    string TransactionReference,
    string EvidenceReference,
    string ExternalStatus,
    DateTimeOffset OccurredAtUtc,
    string PayloadHash,
    string HmacPayload,
    PaymobOfficialCallbackPayload Payload);

public sealed record PaymobOfficialCallbackPayload
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("pending")]
    public bool Pending { get; init; }

    [JsonPropertyName("amount_cents")]
    public long? AmountCents { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("is_auth")]
    public bool IsAuth { get; init; }

    [JsonPropertyName("is_capture")]
    public bool IsCapture { get; init; }

    [JsonPropertyName("is_standalone_payment")]
    public bool IsStandalonePayment { get; init; }

    [JsonPropertyName("is_voided")]
    public bool IsVoided { get; init; }

    [JsonPropertyName("is_refunded")]
    public bool IsRefunded { get; init; }

    [JsonPropertyName("is_3d_secure")]
    public bool Is3dSecure { get; init; }

    [JsonPropertyName("integration_id")]
    public long? IntegrationId { get; init; }

    [JsonPropertyName("has_parent_transaction")]
    public bool HasParentTransaction { get; init; }

    [JsonPropertyName("order")]
    public PaymobOfficialOrder? Order { get; init; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("source_data")]
    public PaymobOfficialSourceData? SourceData { get; init; }

    [JsonPropertyName("owner")]
    public long? Owner { get; init; }

    [JsonPropertyName("error_occured")]
    public bool ErrorOccured { get; init; }

    [JsonPropertyName("data")]
    public PaymobOfficialCallbackData? Data { get; init; }

    [JsonPropertyName("data.message")]
    public string? DataMessage { get; init; }
}

public sealed record PaymobOfficialOrder
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("merchant_order_id")]
    public string? MerchantOrderId { get; init; }
}

public sealed record PaymobOfficialSourceData
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("pan")]
    public string? Pan { get; init; }

    [JsonPropertyName("sub_type")]
    public string? SubType { get; init; }
}

public sealed record PaymobOfficialCallbackData
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("txn_response_code")]
    public string? TxnResponseCode { get; init; }
}
