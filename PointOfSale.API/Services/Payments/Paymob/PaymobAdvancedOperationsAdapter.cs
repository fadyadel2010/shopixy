using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.API.Services.Payments.Paymob;

public interface IPaymobAdvancedOperationsAdapter
{
    Task<PaymobAdvancedOperationResult> ExecuteAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);
}

public partial interface IPaymobOfficialApiClient
{
    Task<PaymobOperationResponse> RefundAsync(
        PaymobOperationAmountRequest request,
        string authToken,
        CancellationToken cancellationToken);

    Task<PaymobOperationResponse> VoidAsync(
        PaymobOperationTransactionRequest request,
        string authToken,
        CancellationToken cancellationToken);

    Task<PaymobOperationResponse> CaptureAsync(
        PaymobOperationAmountRequest request,
        string authToken,
        CancellationToken cancellationToken);

    Task<PaymobOperationResponse> RetrieveTransactionAsync(
        string transactionId,
        string authToken,
        CancellationToken cancellationToken);
}

public sealed partial class PaymobOfficialApiHttpClient
{
    public async Task<PaymobOperationResponse> RefundAsync(
        PaymobOperationAmountRequest request,
        string authToken,
        CancellationToken cancellationToken)
    {
        return await PostAsync<PaymobOperationAmountRequest, PaymobOperationResponse>(
            "acceptance/void_refund/refund",
            request,
            authToken,
            cancellationToken);
    }

    public async Task<PaymobOperationResponse> VoidAsync(
        PaymobOperationTransactionRequest request,
        string authToken,
        CancellationToken cancellationToken)
    {
        return await PostAsync<PaymobOperationTransactionRequest, PaymobOperationResponse>(
            "acceptance/void_refund/void",
            request,
            authToken,
            cancellationToken);
    }

    public async Task<PaymobOperationResponse> CaptureAsync(
        PaymobOperationAmountRequest request,
        string authToken,
        CancellationToken cancellationToken)
    {
        return await PostAsync<PaymobOperationAmountRequest, PaymobOperationResponse>(
            "acceptance/capture",
            request,
            authToken,
            cancellationToken);
    }

    public async Task<PaymobOperationResponse> RetrieveTransactionAsync(
        string transactionId,
        string authToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            throw new ArgumentException("Paymob transaction id is required.", nameof(transactionId));
        }

        return await GetAsync<PaymobOperationResponse>(
            $"acceptance/transactions/{Uri.EscapeDataString(transactionId.Trim())}",
            authToken,
            cancellationToken);
    }
}

public sealed class PaymobAdvancedOperationsAdapter : IPaymobAdvancedOperationsAdapter
{
    private readonly IPaymobOfficialApiClient _client;
    private readonly IPaymobAdapterCredentialHandoff _credentialHandoff;

    public PaymobAdvancedOperationsAdapter(
        IPaymobOfficialApiClient client,
        IPaymobAdapterCredentialHandoff credentialHandoff)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _credentialHandoff = credentialHandoff ?? throw new ArgumentNullException(nameof(credentialHandoff));
    }

    public async Task<PaymobAdvancedOperationResult> ExecuteAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var credentials = _credentialHandoff.ConsumeFor(action);
            var request = PaymobOperationFactory.Create(action, credentials);
            if (!PaymobCapabilityMatrix.IsSupported(request.Operation))
            {
                return PaymobAdvancedOperationResult.Failed(PaymobProviderErrorTranslator.Validation(
                    "PAYMOB_OPERATION_UNSUPPORTED",
                    $"Paymob does not support canonical operation '{request.Operation}'."));
            }

            var auth = await _client.AuthenticateAsync(request.AuthRequest, cancellationToken);
            request = request.WithAuthToken(auth.Token);
            var response = request.Operation switch
            {
                PaymobAdvancedOperation.Refund or PaymobAdvancedOperation.PartialRefund => await _client.RefundAsync(
                    request.AmountRequest!,
                    auth.Token,
                    cancellationToken),
                PaymobAdvancedOperation.Void => await _client.VoidAsync(
                    request.TransactionRequest!,
                    auth.Token,
                    cancellationToken),
                PaymobAdvancedOperation.Capture => await _client.CaptureAsync(
                    request.AmountRequest!,
                    auth.Token,
                    cancellationToken),
                PaymobAdvancedOperation.TransactionInquiry => await _client.RetrieveTransactionAsync(
                    request.ProviderReference.Value,
                    auth.Token,
                    cancellationToken),
                _ => throw new ArgumentException("Unsupported Paymob advanced operation.")
            };
            var evidence = PaymobOperationEvidenceFactory.FromOperationResponse(action, request, response);

            return PaymobAdvancedOperationResult.Success(evidence, request, response);
        }
        catch (PaymobOfficialApiException exception)
        {
            return PaymobAdvancedOperationResult.Failed(PaymobProviderErrorTranslator.Translate(exception));
        }
        catch (HttpRequestException exception)
        {
            return PaymobAdvancedOperationResult.Failed(PaymobProviderErrorTranslator.Translate(exception));
        }
        catch (JsonException exception)
        {
            return PaymobAdvancedOperationResult.Failed(
                PaymobProviderErrorTranslator.Validation("PAYMOB_OPERATION_RESPONSE_INVALID", exception.Message));
        }
        catch (ArgumentException exception)
        {
            return PaymobAdvancedOperationResult.Failed(
                PaymobProviderErrorTranslator.Validation("PAYMOB_OPERATION_REQUEST_INVALID", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return PaymobAdvancedOperationResult.Failed(
                PaymobProviderErrorTranslator.Configuration("PAYMOB_OPERATION_ADAPTER_INVALID", exception.Message));
        }
    }
}

public static class PaymobOperationFactory
{
    public static PaymobAdvancedOperationRequest Create(
        PaymentAction action,
        PaymobTenantRuntimeCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(credentials);

        var operation = PaymobOperationMetadata.ResolveOperation(action);
        var providerReference = new PaymobOperationReference(Required(action.Metadata.Values, PaymobOperationMetadataKeys.ProviderReference));
        ValidateProviderReference(providerReference.Value);
        var idempotencyKey = PaymobOperationIdempotencyKey.Build(action, operation);
        var operationReference = Optional(action.Metadata.Values, PaymobOperationMetadataKeys.OperationReference);
        var authRequest = new PaymobAuthRequest(credentials.ApiKey);

        if (operation is PaymobAdvancedOperation.Refund or PaymobAdvancedOperation.PartialRefund or PaymobAdvancedOperation.Capture)
        {
            var amount = PaymobMoneyMapper.ToMinorUnits(
                Required(action.Metadata.Values, PaymobOperationMetadataKeys.Amount),
                Required(action.Metadata.Values, PaymobOperationMetadataKeys.Currency));
            return PaymobAdvancedOperationRequest.ForAmount(
                operation,
                providerReference,
                authRequest,
                new PaymobOperationAmountRequest(string.Empty, providerReference.Value, amount),
                idempotencyKey,
                operationReference,
                PaymobOperationMetadata.FromAction(action, operation).Values);
        }

        if (operation == PaymobAdvancedOperation.Void)
        {
            return PaymobAdvancedOperationRequest.ForTransaction(
                operation,
                providerReference,
                authRequest,
                new PaymobOperationTransactionRequest(string.Empty, providerReference.Value),
                idempotencyKey,
                operationReference,
                PaymobOperationMetadata.FromAction(action, operation).Values);
        }

        return PaymobAdvancedOperationRequest.ForInquiry(
            operation,
            providerReference,
            authRequest,
            idempotencyKey,
            operationReference,
            PaymobOperationMetadata.FromAction(action, operation).Values);
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key)
    {
        var value = Optional(values, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Canonical Paymob operation field '{key}' is required.");
        }

        return value;
    }

    private static string? Optional(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static void ValidateProviderReference(string providerReference)
    {
        if (!long.TryParse(providerReference, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new ArgumentException("Paymob advanced operations require the numeric Paymob transaction id as ProviderReference.");
        }
    }
}

public static class PaymobOperationEvidenceFactory
{
    public static ProviderEvidence FromOperationResponse(
        PaymentAction action,
        PaymobAdvancedOperationRequest request,
        PaymobOperationResponse response)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        var transactionId = Required(response.Id, "Paymob operation response transaction id");
        var transactionReference = $"paymob:transaction:{transactionId}";
        var externalStatus = MapExternalStatus(response);
        var timestamp = ParseTimestamp(response.CreatedAt) ?? action.LastUpdatedUtc;
        var metadata = PaymobOperationResponseMetadata.Create(action, request, response, externalStatus);
        var evidenceHash = PaymobHash.Create(
            request.Operation.ToString(),
            request.ProviderReference.Value,
            transactionId,
            externalStatus,
            request.IdempotencyKey.Value,
            action.CorrelationId.Value);
        var evidenceId = PaymobDeterministicId.Create(
            "paymob-advanced-operation-evidence",
            action.Id.Value.ToString("N"),
            request.Operation.ToString(),
            request.ProviderReference.Value,
            transactionId,
            evidenceHash);

        return ProviderEvidenceFactory.Create(
            new EvidenceId(evidenceId),
            action.Id,
            EvidenceType.ProviderNotification,
            EvidenceStatus.Recorded,
            new EvidenceReference(transactionReference),
            new EvidenceSource(PaymobProviderReference.ProviderName),
            new EvidenceTimestamp(timestamp),
            new EvidenceHash(evidenceHash),
            new ProviderPayloadReference(transactionReference),
            action.CorrelationId,
            new EvidenceMetadata(metadata));
    }

    internal static string MapExternalStatus(PaymobOperationResponse response)
    {
        if (response.Pending)
        {
            return "pending";
        }

        if (response.IsVoided)
        {
            return "voided";
        }

        if (response.IsRefunded)
        {
            return "refunded";
        }

        if (response.Success)
        {
            return response.IsAuth && !response.IsCapture ? "authorized" : "succeeded";
        }

        return "failed";
    }

    private static string Required(long? value, string name)
    {
        if (!value.HasValue || value.Value <= 0)
        {
            throw new ArgumentException($"{name} is required.");
        }

        return value.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}

public static class PaymobOperationResponseMetadata
{
    public static IReadOnlyDictionary<string, string> Create(
        PaymentAction action,
        PaymobAdvancedOperationRequest request,
        PaymobOperationResponse response,
        string externalStatus)
    {
        var transactionId = (response.Id ?? 0).ToString(CultureInfo.InvariantCulture);
        var providerReference = response.Order?.Id is > 0
            ? $"paymob:order:{response.Order.Id.Value.ToString(CultureInfo.InvariantCulture)}"
            : $"paymob:transaction:{transactionId}";
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["adapter"] = nameof(PaymobAdvancedOperationsAdapter),
            ["provider"] = PaymobProviderReference.ProviderName,
            ["operation"] = request.Operation.ToString(),
            ["operation_reference"] = request.OperationReference.Value,
            ["provider_reference"] = providerReference,
            ["transaction_reference"] = $"paymob:transaction:{transactionId}",
            ["provider_transaction_id"] = transactionId,
            ["external_status"] = externalStatus,
            ["idempotency_key"] = request.IdempotencyKey.Value,
            ["correlation_id"] = action.CorrelationId.Value,
            ["payment_action_id"] = action.Id.Value.ToString("N"),
            ["success"] = response.Success.ToString(CultureInfo.InvariantCulture),
            ["pending"] = response.Pending.ToString(CultureInfo.InvariantCulture),
            ["is_auth"] = response.IsAuth.ToString(CultureInfo.InvariantCulture),
            ["is_capture"] = response.IsCapture.ToString(CultureInfo.InvariantCulture),
            ["is_voided"] = response.IsVoided.ToString(CultureInfo.InvariantCulture),
            ["is_refunded"] = response.IsRefunded.ToString(CultureInfo.InvariantCulture)
        };

        if (response.AmountCents.HasValue)
        {
            values["amount_cents"] = response.AmountCents.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(response.Currency))
        {
            values["currency"] = response.Currency.Trim();
        }

        foreach (var (key, value) in request.CanonicalMetadata)
        {
            values[$"canonical_{key}"] = value;
        }

        return new ReadOnlyDictionary<string, string>(
            values
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.Ordinal));
    }
}

public sealed record PaymobAdvancedOperationResult
{
    public bool Succeeded { get; init; }
    public ProviderEvidence? Evidence { get; init; }
    public PaymobAdvancedOperationRequest? Request { get; init; }
    public PaymobOperationResponse? ProviderResponse { get; init; }
    public CanonicalProviderError? Error { get; init; }

    private PaymobAdvancedOperationResult()
    {
    }

    public static PaymobAdvancedOperationResult Success(
        ProviderEvidence evidence,
        PaymobAdvancedOperationRequest request,
        PaymobOperationResponse providerResponse)
    {
        return new PaymobAdvancedOperationResult
        {
            Succeeded = true,
            Evidence = evidence,
            Request = request,
            ProviderResponse = providerResponse
        };
    }

    public static PaymobAdvancedOperationResult Failed(CanonicalProviderError error)
    {
        return new PaymobAdvancedOperationResult
        {
            Succeeded = false,
            Error = error
        };
    }
}

public sealed record PaymobOperationTransactionRequest(
    [property: JsonPropertyName("auth_token")] string AuthToken,
    [property: JsonPropertyName("transaction_id")] string TransactionId);

public sealed record PaymobOperationAmountRequest(
    [property: JsonPropertyName("auth_token")] string AuthToken,
    [property: JsonPropertyName("transaction_id")] string TransactionId,
    [property: JsonPropertyName("amount_cents")] long AmountCents);

public sealed record PaymobOperationResponse
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

    [JsonPropertyName("is_voided")]
    public bool IsVoided { get; init; }

    [JsonPropertyName("is_refunded")]
    public bool IsRefunded { get; init; }

    [JsonPropertyName("order")]
    public PaymobOfficialOrder? Order { get; init; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("data")]
    public PaymobOfficialCallbackData? Data { get; init; }

    [JsonPropertyName("data.message")]
    public string? DataMessage { get; init; }
}

public sealed record PaymobAdvancedOperationRequest
{
    public PaymobAdvancedOperation Operation { get; init; }
    public PaymobOperationReference ProviderReference { get; init; }
    public PaymobOperationReference OperationReference { get; init; }
    public PaymobAuthRequest AuthRequest { get; init; }
    public PaymobOperationIdempotencyKey IdempotencyKey { get; init; }
    public PaymobOperationAmountRequest? AmountRequest { get; init; }
    public PaymobOperationTransactionRequest? TransactionRequest { get; init; }
    public IReadOnlyDictionary<string, string> CanonicalMetadata { get; init; }

    private PaymobAdvancedOperationRequest(
        PaymobAdvancedOperation operation,
        PaymobOperationReference providerReference,
        PaymobAuthRequest authRequest,
        PaymobOperationIdempotencyKey idempotencyKey,
        string? operationReference,
        IReadOnlyDictionary<string, string> canonicalMetadata,
        PaymobOperationAmountRequest? amountRequest = null,
        PaymobOperationTransactionRequest? transactionRequest = null)
    {
        Operation = operation == PaymobAdvancedOperation.Unknown
            ? throw new ArgumentException("Paymob operation is required.", nameof(operation))
            : operation;
        ProviderReference = providerReference;
        OperationReference = new PaymobOperationReference(
            string.IsNullOrWhiteSpace(operationReference)
                ? $"{operation}:{providerReference.Value}"
                : operationReference.Trim());
        AuthRequest = authRequest;
        IdempotencyKey = idempotencyKey;
        AmountRequest = amountRequest;
        TransactionRequest = transactionRequest;
        CanonicalMetadata = new ReadOnlyDictionary<string, string>(
            canonicalMetadata
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.Ordinal));
    }

    public PaymobAdvancedOperationRequest WithAuthToken(string authToken)
    {
        return this with
        {
            AmountRequest = AmountRequest is null ? null : AmountRequest with { AuthToken = authToken },
            TransactionRequest = TransactionRequest is null ? null : TransactionRequest with { AuthToken = authToken }
        };
    }

    public static PaymobAdvancedOperationRequest ForAmount(
        PaymobAdvancedOperation operation,
        PaymobOperationReference providerReference,
        PaymobAuthRequest authRequest,
        PaymobOperationAmountRequest amountRequest,
        PaymobOperationIdempotencyKey idempotencyKey,
        string? operationReference,
        IReadOnlyDictionary<string, string> canonicalMetadata)
    {
        return new PaymobAdvancedOperationRequest(operation, providerReference, authRequest, idempotencyKey, operationReference, canonicalMetadata, amountRequest: amountRequest);
    }

    public static PaymobAdvancedOperationRequest ForTransaction(
        PaymobAdvancedOperation operation,
        PaymobOperationReference providerReference,
        PaymobAuthRequest authRequest,
        PaymobOperationTransactionRequest transactionRequest,
        PaymobOperationIdempotencyKey idempotencyKey,
        string? operationReference,
        IReadOnlyDictionary<string, string> canonicalMetadata)
    {
        return new PaymobAdvancedOperationRequest(operation, providerReference, authRequest, idempotencyKey, operationReference, canonicalMetadata, transactionRequest: transactionRequest);
    }

    public static PaymobAdvancedOperationRequest ForInquiry(
        PaymobAdvancedOperation operation,
        PaymobOperationReference providerReference,
        PaymobAuthRequest authRequest,
        PaymobOperationIdempotencyKey idempotencyKey,
        string? operationReference,
        IReadOnlyDictionary<string, string> canonicalMetadata)
    {
        return new PaymobAdvancedOperationRequest(operation, providerReference, authRequest, idempotencyKey, operationReference, canonicalMetadata);
    }
}

public enum PaymobAdvancedOperation
{
    Unknown = 0,
    Refund = 1,
    PartialRefund = 2,
    Void = 3,
    Capture = 4,
    TransactionInquiry = 5,
    PaymentStatus = 6
}

public static class PaymobCapabilityMatrix
{
    public static IReadOnlyDictionary<PaymobAdvancedOperation, PaymobOperationCapability> Operations { get; } =
        new ReadOnlyDictionary<PaymobAdvancedOperation, PaymobOperationCapability>(
            new Dictionary<PaymobAdvancedOperation, PaymobOperationCapability>
            {
                [PaymobAdvancedOperation.Refund] = new(true, "acceptance/void_refund/refund", "Full refund against an existing Paymob transaction."),
                [PaymobAdvancedOperation.PartialRefund] = new(true, "acceptance/void_refund/refund", "Partial refund using the official refund amount field."),
                [PaymobAdvancedOperation.Void] = new(true, "acceptance/void_refund/void", "Void for eligible uncaptured Paymob transactions."),
                [PaymobAdvancedOperation.Capture] = new(true, "acceptance/capture", "Capture for eligible authorized Paymob transactions."),
                [PaymobAdvancedOperation.TransactionInquiry] = new(true, "acceptance/transactions/{transaction_id}", "Retrieve the official transaction state."),
                [PaymobAdvancedOperation.PaymentStatus] = new(false, string.Empty, "No separate Paymob payment-status operation is modeled; use transaction inquiry.")
            });

    public static bool IsSupported(PaymobAdvancedOperation operation)
    {
        return Operations.TryGetValue(operation, out var capability) && capability.Supported;
    }
}

public sealed record PaymobOperationCapability(bool Supported, string OfficialOperation, string Notes);

public sealed record PaymobOperationReference(string Value)
{
    public PaymobOperationReference() : this(string.Empty)
    {
    }
}

public sealed record PaymobOperationIdempotencyKey(string Value)
{
    public static PaymobOperationIdempotencyKey Build(PaymentAction action, PaymobAdvancedOperation operation)
    {
        if (action.Metadata.Values.TryGetValue(PaymobOperationMetadataKeys.IdempotencyKey, out var supplied) &&
            !string.IsNullOrWhiteSpace(supplied))
        {
            return new PaymobOperationIdempotencyKey(supplied.Trim());
        }

        var providerReference = action.Metadata.Values.TryGetValue(PaymobOperationMetadataKeys.ProviderReference, out var reference)
            ? reference.Trim()
            : action.Reference.Value;
        var amount = action.Metadata.Values.TryGetValue(PaymobOperationMetadataKeys.Amount, out var suppliedAmount)
            ? suppliedAmount.Trim()
            : string.Empty;
        var source = string.Join(
            "|",
            "paymob",
            "advanced_operation",
            operation,
            action.SessionId.Value.ToString("N"),
            action.AttemptId.Value.ToString("N"),
            action.Id.Value.ToString("N"),
            providerReference,
            amount,
            action.CorrelationId.Value);
        var hash = PaymobHash.Create(source);

        return new PaymobOperationIdempotencyKey($"paymob:op:{operation}:{action.Id.Value:N}:{hash[..24]}");
    }
}

public sealed record PaymobOperationMetadata(IReadOnlyDictionary<string, string> Values)
{
    public static PaymobAdvancedOperation ResolveOperation(PaymentAction action)
    {
        var raw = action.Metadata.Values.TryGetValue(PaymobOperationMetadataKeys.Operation, out var value)
            ? value
            : action.Reference.Value;

        return raw.Trim().ToLowerInvariant() switch
        {
            "refund" => PaymobAdvancedOperation.Refund,
            "partial_refund" or "partial-refund" or "partialrefund" => PaymobAdvancedOperation.PartialRefund,
            "void" => PaymobAdvancedOperation.Void,
            "capture" => PaymobAdvancedOperation.Capture,
            "transaction_inquiry" or "transaction-inquiry" or "transactioninquiry" or "inquiry" => PaymobAdvancedOperation.TransactionInquiry,
            "payment_status" or "payment-status" or "paymentstatus" => PaymobAdvancedOperation.PaymentStatus,
            _ => throw new ArgumentException("Canonical Paymob operation must be refund, partial_refund, void, capture, transaction_inquiry, or payment_status.")
        };
    }

    public static PaymobOperationMetadata FromAction(PaymentAction action, PaymobAdvancedOperation operation)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = PaymobProviderReference.ProviderName,
            ["adapter"] = nameof(PaymobAdvancedOperationsAdapter),
            ["operation"] = operation.ToString(),
            ["payment_action_id"] = action.Id.Value.ToString("N"),
            ["payment_session_id"] = action.SessionId.Value.ToString("N"),
            ["payment_attempt_id"] = action.AttemptId.Value.ToString("N"),
            ["correlation_id"] = action.CorrelationId.Value,
            ["action_reference"] = action.Reference.Value
        };

        foreach (var (key, value) in action.Metadata.Values)
        {
            if (!PaymobOperationMetadataKeys.AdapterOnlyKeys.Contains(key))
            {
                values[$"canonical_{key}"] = value;
            }
        }

        return new PaymobOperationMetadata(new ReadOnlyDictionary<string, string>(
            values
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.Ordinal)));
    }
}

public static class PaymobOperationMetadataKeys
{
    public const string Operation = "Operation";
    public const string Amount = "Amount";
    public const string Currency = "Currency";
    public const string ProviderReference = "ProviderReference";
    public const string Reason = "Reason";
    public const string IdempotencyKey = "IdempotencyKey";
    public const string OperationReference = "OperationReference";

    public static readonly ISet<string> AdapterOnlyKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        Operation,
        Amount,
        Currency,
        ProviderReference,
        Reason,
        IdempotencyKey,
        OperationReference
    };
}
