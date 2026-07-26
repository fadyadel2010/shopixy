using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.API.Services.Payments.Fawry;

public interface IFawryAdvancedOperationsAdapter
{
    Task<FawryAdvancedOperationResult> ExecuteAsync(
        PaymentAction action,
        FawryPaymentCredentials credentials,
        CancellationToken cancellationToken = default);
}

public sealed class FawryAdvancedOperationsAdapter : IFawryAdvancedOperationsAdapter
{
    private const string RefundEndpoint = "ECommerceWeb/Fawry/payments/refund";
    private const string CaptureEndpoint = "ECommerceWeb/api/payment/capture";
    private const string CancelAuthorizationEndpoint = "ECommerceWeb/api/payment/cancel";
    private const string StatusEndpoint = "ECommerceWeb/Fawry/payments/status/v2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public FawryAdvancedOperationsAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<FawryAdvancedOperationResult> ExecuteAsync(
        PaymentAction action,
        FawryPaymentCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        FawryAdvancedOperationRequest? request = null;
        try
        {
            request = FawryAdvancedOperationFactory.Create(action, credentials);
            if (!FawryAdvancedOperationCapabilityMatrix.IsSupported(request.Operation))
            {
                return FawryAdvancedOperationResult.Failed(
                    FawryAdvancedOperationEvidenceFactory.Rejected(action, request, FawryAdvancedOperationErrors.NotSupported(request.Operation)),
                    FawryAdvancedOperationErrors.NotSupported(request.Operation),
                    request);
            }

            using var response = request.Operation switch
            {
                FawryAdvancedOperation.Refund or FawryAdvancedOperation.PartialRefund => await _httpClient.PostAsJsonAsync(RefundEndpoint, request.RefundRequest, JsonOptions, cancellationToken),
                FawryAdvancedOperation.Capture => await _httpClient.PostAsJsonAsync(CaptureEndpoint, request.CaptureRequest, JsonOptions, cancellationToken),
                FawryAdvancedOperation.Void => await _httpClient.PostAsJsonAsync(CancelAuthorizationEndpoint, request.CancelAuthorizationRequest, JsonOptions, cancellationToken),
                FawryAdvancedOperation.TransactionInquiry => await _httpClient.GetAsync(BuildStatusUri(request.StatusRequest!), cancellationToken),
                _ => throw new ArgumentException("Unsupported Fawry advanced operation.")
            };

            if (!response.IsSuccessStatusCode)
            {
                var error = FawryAdvancedOperationErrors.FromHttp(response.StatusCode, await SafeReadBodyAsync(response, cancellationToken));
                return FawryAdvancedOperationResult.Failed(
                    FawryAdvancedOperationEvidenceFactory.Rejected(action, request, error),
                    error,
                    request);
            }

            var providerResponse = await ReadProviderResponseAsync(response, request.Operation, cancellationToken);
            if (!FawryAdvancedOperationResponseMapper.IsAccepted(providerResponse))
            {
                var error = FawryAdvancedOperationErrors.FromOfficialResponse(providerResponse);
                return FawryAdvancedOperationResult.Failed(
                    FawryAdvancedOperationEvidenceFactory.Rejected(action, request, error, providerResponse),
                    error,
                    request,
                    providerResponse);
            }

            return FawryAdvancedOperationResult.Success(
                FawryAdvancedOperationEvidenceFactory.FromProviderResponse(action, request, providerResponse),
                request,
                providerResponse);
        }
        catch (HttpRequestException exception)
        {
            var error = FawryAdvancedOperationErrors.Transport(exception);
            return FawryAdvancedOperationResult.Failed(FawryAdvancedOperationEvidenceFactory.Rejected(action, request, error), error, request);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            var error = FawryAdvancedOperationErrors.Timeout(exception);
            return FawryAdvancedOperationResult.Failed(FawryAdvancedOperationEvidenceFactory.Rejected(action, request, error), error, request);
        }
        catch (JsonException exception)
        {
            var error = CanonicalProviderError.Validation(FawryProviderReference.ProviderName, "FAWRY_OPERATION_RESPONSE_INVALID", exception.Message);
            return FawryAdvancedOperationResult.Failed(FawryAdvancedOperationEvidenceFactory.Rejected(action, request, error), error, request);
        }
        catch (ArgumentException exception)
        {
            var error = CanonicalProviderError.Validation(FawryProviderReference.ProviderName, "FAWRY_OPERATION_REQUEST_INVALID", exception.Message);
            return FawryAdvancedOperationResult.Failed(FawryAdvancedOperationEvidenceFactory.Rejected(action, request, error), error, request);
        }
        catch (InvalidOperationException exception)
        {
            var error = CanonicalProviderError.Configuration(FawryProviderReference.ProviderName, "FAWRY_OPERATION_ADAPTER_INVALID", exception.Message);
            return FawryAdvancedOperationResult.Failed(FawryAdvancedOperationEvidenceFactory.Rejected(action, request, error), error, request);
        }
    }

    private static string BuildStatusUri(FawryPaymentStatusRequest request)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{StatusEndpoint}?merchantCode={Uri.EscapeDataString(request.MerchantCode)}&merchantRefNumber={Uri.EscapeDataString(request.MerchantRefNumber)}&signature={Uri.EscapeDataString(request.Signature)}");
    }

    private static async Task<FawryAdvancedOperationResponse> ReadProviderResponseAsync(
        HttpResponseMessage response,
        FawryAdvancedOperation operation,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return FawryAdvancedOperationResponse.EmptySuccess(operation);
        }

        return JsonSerializer.Deserialize<FawryAdvancedOperationResponse>(body, JsonOptions)
            ?? throw new JsonException("Fawry advanced operation response was empty.");
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Length <= 512 ? body : body[..512];
    }
}

public static class FawryAdvancedOperationFactory
{
    public static FawryAdvancedOperationRequest Create(PaymentAction action, FawryPaymentCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(credentials);
        credentials.Validate();

        var operation = FawryAdvancedOperationMetadata.ResolveOperation(action);
        var providerReference = Required(action.Metadata.Values, FawryAdvancedOperationMetadataKeys.ProviderReference);
        var amount = Optional(action.Metadata.Values, FawryAdvancedOperationMetadataKeys.Amount);
        var formattedAmount = string.IsNullOrWhiteSpace(amount) ? null : FawryAmount.Format(amount);
        var reason = Optional(action.Metadata.Values, FawryAdvancedOperationMetadataKeys.Reason);
        var operationReference = Optional(action.Metadata.Values, FawryAdvancedOperationMetadataKeys.OperationReference) ?? $"{operation}:{providerReference}";
        var canonicalMetadata = FawryAdvancedOperationMetadata.FromAction(action, operation).Values;

        return operation switch
        {
            FawryAdvancedOperation.Refund or FawryAdvancedOperation.PartialRefund => FawryAdvancedOperationRequest.ForRefund(
                operation,
                new FawryOperationReference(providerReference),
                new FawryRefundRequest
                {
                    MerchantCode = credentials.MerchantCode,
                    ReferenceNumber = providerReference,
                    RefundAmount = FawryAmount.Format(Required(action.Metadata.Values, FawryAdvancedOperationMetadataKeys.Amount)),
                    Reason = reason,
                    Signature = FawryOperationSignature.Refund(credentials.MerchantCode, providerReference, FawryAmount.Format(Required(action.Metadata.Values, FawryAdvancedOperationMetadataKeys.Amount)), reason, credentials.SecureKey)
                },
                new FawryOperationReference(operationReference),
                canonicalMetadata),
            FawryAdvancedOperation.Capture => FawryAdvancedOperationRequest.ForCapture(
                operation,
                new FawryOperationReference(providerReference),
                new FawryCaptureRequest
                {
                    MerchantCode = credentials.MerchantCode,
                    MerchantRefNum = providerReference,
                    CaptureAmount = formattedAmount,
                    RequestSignature = FawryOperationSignature.Capture(providerReference, formattedAmount, credentials.MerchantCode, credentials.SecureKey)
                },
                new FawryOperationReference(operationReference),
                canonicalMetadata),
            FawryAdvancedOperation.Void => FawryAdvancedOperationRequest.ForCancelAuthorization(
                operation,
                new FawryOperationReference(providerReference),
                new FawryCancelAuthorizationRequest
                {
                    MerchantCode = credentials.MerchantCode,
                    MerchantRefNum = providerReference,
                    RequestSignature = FawryOperationSignature.CancelAuthorization(providerReference, credentials.MerchantCode, credentials.SecureKey)
                },
                new FawryOperationReference(operationReference),
                canonicalMetadata),
            FawryAdvancedOperation.TransactionInquiry => FawryAdvancedOperationRequest.ForStatus(
                operation,
                new FawryOperationReference(providerReference),
                new FawryPaymentStatusRequest
                {
                    MerchantCode = credentials.MerchantCode,
                    MerchantRefNumber = providerReference,
                    Signature = FawryOperationSignature.Status(credentials.MerchantCode, providerReference, credentials.SecureKey)
                },
                new FawryOperationReference(operationReference),
                canonicalMetadata),
            FawryAdvancedOperation.SettlementInquiry or FawryAdvancedOperation.OperationStatus or FawryAdvancedOperation.CancelUnpaidOrder => FawryAdvancedOperationRequest.ForUnsupported(
                operation,
                new FawryOperationReference(providerReference),
                new FawryOperationReference(operationReference),
                canonicalMetadata),
            _ => throw new ArgumentException("Canonical Fawry operation must be refund, partial_refund, void, capture, transaction_inquiry, settlement_inquiry, operation_status, or cancel_unpaid_order.")
        };
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key)
    {
        var value = Optional(values, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Canonical Fawry operation field '{key}' is required.");
        }

        return value;
    }

    private static string? Optional(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }
}

public static class FawryAdvancedOperationEvidenceFactory
{
    public static ProviderEvidence FromProviderResponse(
        PaymentAction action,
        FawryAdvancedOperationRequest request,
        FawryAdvancedOperationResponse response)
    {
        var reference = FawryAdvancedOperationResponseMapper.ProviderReference(response, request.ProviderReference.Value);
        var status = FawryAdvancedOperationResponseMapper.ProviderStatus(response);
        var timestamp = FawryAdvancedOperationResponseMapper.Timestamp(response) ?? action.LastUpdatedUtc;
        var metadata = FawryAdvancedOperationResponseMetadata.Create(action, request, response, status);
        return Create(action, request, response, reference, status, EvidenceStatus.Recorded, timestamp, metadata);
    }

    public static ProviderEvidence Rejected(
        PaymentAction action,
        FawryAdvancedOperationRequest? request,
        CanonicalProviderError error,
        FawryAdvancedOperationResponse? response = null)
    {
        var operation = request?.Operation.ToString() ?? "Unknown";
        var reference = request?.ProviderReference.Value ?? action.Reference.Value;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = FawryProviderReference.ProviderName,
            ["adapter"] = nameof(FawryAdvancedOperationsAdapter),
            ["operation"] = operation,
            ["provider_reference"] = reference,
            ["correlation_id"] = action.CorrelationId.Value,
            ["payment_action_id"] = action.Id.Value.ToString("N"),
            ["error_type"] = error.Type.ToString(),
            ["error_code"] = error.Code,
            ["provider_status"] = response is null ? string.Empty : FawryAdvancedOperationResponseMapper.ProviderStatus(response)
        };
        var effectiveRequest = request ?? FawryAdvancedOperationRequest.ForUnsupported(
            FawryAdvancedOperation.Unknown,
            new FawryOperationReference(reference),
            new FawryOperationReference($"Unknown:{reference}"),
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>()));

        return Create(action, effectiveRequest, response, reference, error.Code, EvidenceStatus.Rejected, action.LastUpdatedUtc, metadata);
    }

    private static ProviderEvidence Create(
        PaymentAction action,
        FawryAdvancedOperationRequest request,
        FawryAdvancedOperationResponse? response,
        string reference,
        string status,
        EvidenceStatus evidenceStatus,
        DateTimeOffset timestamp,
        IReadOnlyDictionary<string, string> metadata)
    {
        var hash = FawryHash.Create(
            request.Operation.ToString(),
            request.ProviderReference.Value,
            reference,
            status,
            response?.StatusCode ?? response?.Code ?? string.Empty,
            action.CorrelationId.Value);
        var evidenceId = FawryDeterministicId.Create(
            "fawry-advanced-operation-evidence",
            action.Id.Value.ToString("N"),
            request.Operation.ToString(),
            request.ProviderReference.Value,
            reference,
            evidenceStatus.ToString(),
            hash);

        return ProviderEvidenceFactory.Create(
            new EvidenceId(evidenceId),
            action.Id,
            EvidenceType.ProviderNotification,
            evidenceStatus,
            new EvidenceReference(reference),
            new EvidenceSource(FawryProviderReference.ProviderName),
            new EvidenceTimestamp(timestamp),
            new EvidenceHash(hash),
            new ProviderPayloadReference($"fawry:advanced:{request.Operation}:{reference}"),
            action.CorrelationId,
            new EvidenceMetadata(metadata));
    }
}

public static class FawryAdvancedOperationResponseMetadata
{
    public static IReadOnlyDictionary<string, string> Create(
        PaymentAction action,
        FawryAdvancedOperationRequest request,
        FawryAdvancedOperationResponse response,
        string providerStatus)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = FawryProviderReference.ProviderName,
            ["adapter"] = nameof(FawryAdvancedOperationsAdapter),
            ["operation"] = request.Operation.ToString(),
            ["operation_reference"] = request.OperationReference.Value,
            ["provider_reference"] = FawryAdvancedOperationResponseMapper.ProviderReference(response, request.ProviderReference.Value),
            ["provider_status"] = providerStatus,
            ["canonical_status"] = FawryChargeResponseMapper.MapStatus(providerStatus).ToString(),
            ["correlation_id"] = action.CorrelationId.Value,
            ["payment_action_id"] = action.Id.Value.ToString("N"),
            ["status_code"] = response.StatusCode ?? response.Code ?? string.Empty,
            ["status_description"] = response.StatusDescription ?? response.Description ?? string.Empty,
            ["amount"] = response.PaymentAmount ?? response.OrderAmount ?? response.CaptureAmount ?? string.Empty,
            ["currency"] = response.CurrencyCode ?? string.Empty,
            ["payment_method"] = response.PaymentMethod ?? string.Empty,
            ["merchant_reference"] = response.MerchantRefNum ?? response.MerchantRefNumber ?? request.ProviderReference.Value,
            ["fawry_ref_number"] = response.FawryRefNumber ?? response.ReferenceNumber ?? string.Empty,
            ["payment_reference_number"] = response.PaymentReferenceNumber ?? response.PaymentRefrenceNumber ?? string.Empty
        };

        foreach (var (key, value) in request.CanonicalMetadata)
        {
            values[$"canonical_{key}"] = value;
        }

        return new ReadOnlyDictionary<string, string>(
            values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.Ordinal));
    }
}

public static class FawryAdvancedOperationResponseMapper
{
    private static readonly ISet<string> SuccessCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "200", "0", "SUCCESS" };

    public static bool IsAccepted(FawryAdvancedOperationResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.StatusCode))
        {
            return SuccessCodes.Contains(response.StatusCode.Trim());
        }

        if (!string.IsNullOrWhiteSpace(response.Code))
        {
            return SuccessCodes.Contains(response.Code.Trim());
        }

        return string.Equals(response.Type, "EmptySuccess", StringComparison.OrdinalIgnoreCase);
    }

    public static string ProviderReference(FawryAdvancedOperationResponse response, string fallback)
    {
        return FirstNonBlank(response.FawryRefNumber, response.ReferenceNumber, response.PaymentReferenceNumber, response.PaymentRefrenceNumber, response.MerchantRefNum, response.MerchantRefNumber, fallback);
    }

    public static string ProviderStatus(FawryAdvancedOperationResponse response)
    {
        return FirstNonBlank(response.OrderStatus, response.PaymentStatus, response.StatusDescription, response.Description, response.Type, response.Code, "UNKNOWN");
    }

    public static DateTimeOffset? Timestamp(FawryAdvancedOperationResponse response)
    {
        if (DateTimeOffset.TryParse(response.PaymentTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}

public static class FawryAdvancedOperationErrors
{
    public static CanonicalProviderError NotSupported(FawryAdvancedOperation operation)
    {
        return CanonicalProviderError.NotSupported(
            FawryProviderReference.ProviderName,
            "FAWRY_OPERATION_NOT_SUPPORTED",
            $"Fawry advanced operation '{operation}' is not officially supported by this adapter capability matrix.");
    }

    public static CanonicalProviderError FromHttp(HttpStatusCode statusCode, string responseBody)
    {
        var type = statusCode switch
        {
            HttpStatusCode.Unauthorized => CanonicalProviderErrorType.Authentication,
            HttpStatusCode.Forbidden => CanonicalProviderErrorType.Authorization,
            HttpStatusCode.TooManyRequests => CanonicalProviderErrorType.RateLimited,
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => CanonicalProviderErrorType.Validation,
            _ when (int)statusCode >= 500 => CanonicalProviderErrorType.ProviderUnavailable,
            _ => CanonicalProviderErrorType.ProviderRejected
        };

        return CanonicalProviderError.Create(
            FawryProviderReference.ProviderName,
            type,
            $"FAWRY_OPERATION_HTTP_{(int)statusCode}",
            "Fawry advanced operation failed.",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["http_status"] = ((int)statusCode).ToString(CultureInfo.InvariantCulture),
                ["provider_diagnostic"] = Redact(responseBody)
            });
    }

    public static CanonicalProviderError FromOfficialResponse(FawryAdvancedOperationResponse response)
    {
        var code = FirstNonBlank(response.StatusCode, response.Code, response.FailureErrorCode, "FAWRY_OPERATION_REJECTED");
        return CanonicalProviderError.Create(
            FawryProviderReference.ProviderName,
            MapOfficialType(code),
            code,
            FirstNonBlank(response.StatusDescription, response.Description, response.FailureReason, response.Reason, "Fawry rejected the advanced operation."),
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider_code"] = code,
                ["provider_status"] = FawryAdvancedOperationResponseMapper.ProviderStatus(response)
            });
    }

    public static CanonicalProviderError Transport(HttpRequestException exception)
    {
        return CanonicalProviderError.Create(
            FawryProviderReference.ProviderName,
            CanonicalProviderErrorType.Network,
            "FAWRY_OPERATION_NETWORK_ERROR",
            "Fawry advanced operation transport failed.",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["exception"] = exception.GetType().Name });
    }

    public static CanonicalProviderError Timeout(TaskCanceledException exception)
    {
        return CanonicalProviderError.Create(
            FawryProviderReference.ProviderName,
            CanonicalProviderErrorType.ProviderUnavailable,
            "FAWRY_OPERATION_TIMEOUT",
            "Fawry advanced operation timed out.",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["exception"] = exception.GetType().Name });
    }

    private static CanonicalProviderErrorType MapOfficialType(string code)
    {
        return code.Trim() switch
        {
            "9901" or "9946" => CanonicalProviderErrorType.Authentication,
            "9935" or "9938" or "9954" or "9984" => CanonicalProviderErrorType.InvalidState,
            _ => CanonicalProviderErrorType.ProviderRejected
        };
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace("signature", "redacted", StringComparison.OrdinalIgnoreCase)
            .Replace("requestSignature", "redacted", StringComparison.OrdinalIgnoreCase)
            .Replace("secureKey", "redacted", StringComparison.OrdinalIgnoreCase)
            .Replace("apiKey", "redacted", StringComparison.OrdinalIgnoreCase);
    }
}

public static class FawryAdvancedOperationCapabilityMatrix
{
    public static IReadOnlyDictionary<FawryAdvancedOperation, FawryAdvancedOperationCapability> Operations { get; } =
        new ReadOnlyDictionary<FawryAdvancedOperation, FawryAdvancedOperationCapability>(
            new Dictionary<FawryAdvancedOperation, FawryAdvancedOperationCapability>
            {
                [FawryAdvancedOperation.Refund] = new(true, "POST ECommerceWeb/Fawry/payments/refund", "Full refund of a paid captured order."),
                [FawryAdvancedOperation.PartialRefund] = new(true, "POST ECommerceWeb/Fawry/payments/refund", "Partial refund using refundAmount."),
                [FawryAdvancedOperation.Void] = new(true, "POST ECommerceWeb/api/payment/cancel", "Cancel an uncaptured authorization by merchantRefNum."),
                [FawryAdvancedOperation.Capture] = new(true, "POST ECommerceWeb/api/payment/capture", "Capture an authorized payment; captureAmount is optional for full capture."),
                [FawryAdvancedOperation.TransactionInquiry] = new(true, "GET ECommerceWeb/Fawry/payments/status/v2", "Pull payment status by merchantRefNumber."),
                [FawryAdvancedOperation.CancelUnpaidOrder] = new(false, "POST ECommerceWeb/api/orders/cancel-unpaid-order", "Officially supported by Fawry but not mapped to canonical void/capture/refund semantics in this adapter."),
                [FawryAdvancedOperation.SettlementInquiry] = new(false, string.Empty, "No official settlement inquiry API was discovered in the reviewed Fawry server API surface."),
                [FawryAdvancedOperation.OperationStatus] = new(false, string.Empty, "No separate operation-status API was discovered; use transaction inquiry for payment status only.")
            });

    public static bool IsSupported(FawryAdvancedOperation operation)
    {
        return Operations.TryGetValue(operation, out var capability) && capability.Supported;
    }
}

public sealed record FawryAdvancedOperationResult
{
    public bool Succeeded { get; init; }
    public ProviderEvidence? Evidence { get; init; }
    public FawryAdvancedOperationRequest? Request { get; init; }
    public FawryAdvancedOperationResponse? ProviderResponse { get; init; }
    public CanonicalProviderError? Error { get; init; }

    private FawryAdvancedOperationResult()
    {
    }

    public static FawryAdvancedOperationResult Success(
        ProviderEvidence evidence,
        FawryAdvancedOperationRequest request,
        FawryAdvancedOperationResponse providerResponse)
    {
        return new FawryAdvancedOperationResult { Succeeded = true, Evidence = evidence, Request = request, ProviderResponse = providerResponse };
    }

    public static FawryAdvancedOperationResult Failed(
        ProviderEvidence evidence,
        CanonicalProviderError error,
        FawryAdvancedOperationRequest? request = null,
        FawryAdvancedOperationResponse? providerResponse = null)
    {
        return new FawryAdvancedOperationResult { Succeeded = false, Evidence = evidence, Error = error, Request = request, ProviderResponse = providerResponse };
    }
}

public sealed record FawryAdvancedOperationRequest
{
    public FawryAdvancedOperation Operation { get; init; }
    public FawryOperationReference ProviderReference { get; init; }
    public FawryOperationReference OperationReference { get; init; }
    public FawryRefundRequest? RefundRequest { get; init; }
    public FawryCaptureRequest? CaptureRequest { get; init; }
    public FawryCancelAuthorizationRequest? CancelAuthorizationRequest { get; init; }
    public FawryPaymentStatusRequest? StatusRequest { get; init; }
    public IReadOnlyDictionary<string, string> CanonicalMetadata { get; init; }

    private FawryAdvancedOperationRequest(
        FawryAdvancedOperation operation,
        FawryOperationReference providerReference,
        FawryOperationReference operationReference,
        IReadOnlyDictionary<string, string> canonicalMetadata,
        FawryRefundRequest? refundRequest = null,
        FawryCaptureRequest? captureRequest = null,
        FawryCancelAuthorizationRequest? cancelAuthorizationRequest = null,
        FawryPaymentStatusRequest? statusRequest = null)
    {
        Operation = operation;
        ProviderReference = providerReference;
        OperationReference = operationReference;
        RefundRequest = refundRequest;
        CaptureRequest = captureRequest;
        CancelAuthorizationRequest = cancelAuthorizationRequest;
        StatusRequest = statusRequest;
        CanonicalMetadata = new ReadOnlyDictionary<string, string>(canonicalMetadata.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.Ordinal));
    }

    public static FawryAdvancedOperationRequest ForRefund(FawryAdvancedOperation operation, FawryOperationReference providerReference, FawryRefundRequest request, FawryOperationReference operationReference, IReadOnlyDictionary<string, string> canonicalMetadata) =>
        new(operation, providerReference, operationReference, canonicalMetadata, refundRequest: request);

    public static FawryAdvancedOperationRequest ForCapture(FawryAdvancedOperation operation, FawryOperationReference providerReference, FawryCaptureRequest request, FawryOperationReference operationReference, IReadOnlyDictionary<string, string> canonicalMetadata) =>
        new(operation, providerReference, operationReference, canonicalMetadata, captureRequest: request);

    public static FawryAdvancedOperationRequest ForCancelAuthorization(FawryAdvancedOperation operation, FawryOperationReference providerReference, FawryCancelAuthorizationRequest request, FawryOperationReference operationReference, IReadOnlyDictionary<string, string> canonicalMetadata) =>
        new(operation, providerReference, operationReference, canonicalMetadata, cancelAuthorizationRequest: request);

    public static FawryAdvancedOperationRequest ForStatus(FawryAdvancedOperation operation, FawryOperationReference providerReference, FawryPaymentStatusRequest request, FawryOperationReference operationReference, IReadOnlyDictionary<string, string> canonicalMetadata) =>
        new(operation, providerReference, operationReference, canonicalMetadata, statusRequest: request);

    public static FawryAdvancedOperationRequest ForUnsupported(FawryAdvancedOperation operation, FawryOperationReference providerReference, FawryOperationReference operationReference, IReadOnlyDictionary<string, string> canonicalMetadata) =>
        new(operation, providerReference, operationReference, canonicalMetadata);
}

public sealed record FawryRefundRequest
{
    [JsonPropertyName("merchantCode")]
    public string MerchantCode { get; init; } = string.Empty;

    [JsonPropertyName("referenceNumber")]
    public string ReferenceNumber { get; init; } = string.Empty;

    [JsonPropertyName("refundAmount")]
    public string RefundAmount { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    [JsonPropertyName("signature")]
    public string Signature { get; init; } = string.Empty;
}

public sealed record FawryCaptureRequest
{
    [JsonPropertyName("merchantCode")]
    public string MerchantCode { get; init; } = string.Empty;

    [JsonPropertyName("merchantRefNum")]
    public string MerchantRefNum { get; init; } = string.Empty;

    [JsonPropertyName("captureAmount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CaptureAmount { get; init; }

    [JsonPropertyName("requestSignature")]
    public string RequestSignature { get; init; } = string.Empty;
}

public sealed record FawryCancelAuthorizationRequest
{
    [JsonPropertyName("merchantCode")]
    public string MerchantCode { get; init; } = string.Empty;

    [JsonPropertyName("merchantRefNum")]
    public string MerchantRefNum { get; init; } = string.Empty;

    [JsonPropertyName("requestSignature")]
    public string RequestSignature { get; init; } = string.Empty;
}

public sealed record FawryPaymentStatusRequest
{
    [JsonPropertyName("merchantCode")]
    public string MerchantCode { get; init; } = string.Empty;

    [JsonPropertyName("merchantRefNumber")]
    public string MerchantRefNumber { get; init; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; init; } = string.Empty;
}

public sealed record FawryAdvancedOperationResponse
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("statusCode")]
    public string? StatusCode { get; init; }

    [JsonPropertyName("statusDescription")]
    public string? StatusDescription { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("fawryRefNumber")]
    public string? FawryRefNumber { get; init; }

    [JsonPropertyName("referenceNumber")]
    public string? ReferenceNumber { get; init; }

    [JsonPropertyName("merchantCode")]
    public string? MerchantCode { get; init; }

    [JsonPropertyName("merchantRefNum")]
    public string? MerchantRefNum { get; init; }

    [JsonPropertyName("merchantRefNumber")]
    public string? MerchantRefNumber { get; init; }

    [JsonPropertyName("orderStatus")]
    public string? OrderStatus { get; init; }

    [JsonPropertyName("paymentStatus")]
    public string? PaymentStatus { get; init; }

    [JsonPropertyName("paymentAmount")]
    public string? PaymentAmount { get; init; }

    [JsonPropertyName("orderAmount")]
    public string? OrderAmount { get; init; }

    [JsonPropertyName("captureAmount")]
    public string? CaptureAmount { get; init; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; init; }

    [JsonPropertyName("paymentMethod")]
    public string? PaymentMethod { get; init; }

    [JsonPropertyName("paymentTime")]
    public string? PaymentTime { get; init; }

    [JsonPropertyName("paymentReferenceNumber")]
    public string? PaymentReferenceNumber { get; init; }

    [JsonPropertyName("paymentRefrenceNumber")]
    public string? PaymentRefrenceNumber { get; init; }

    [JsonPropertyName("failureErrorCode")]
    public string? FailureErrorCode { get; init; }

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; init; }

    public static FawryAdvancedOperationResponse EmptySuccess(FawryAdvancedOperation operation) => new()
    {
        Type = "EmptySuccess",
        StatusCode = "200",
        StatusDescription = $"Fawry {operation} returned HTTP success with an empty body."
    };
}

public enum FawryAdvancedOperation
{
    Unknown = 0,
    Refund = 1,
    PartialRefund = 2,
    Void = 3,
    Capture = 4,
    TransactionInquiry = 5,
    CancelUnpaidOrder = 6,
    SettlementInquiry = 7,
    OperationStatus = 8
}

public sealed record FawryAdvancedOperationCapability(bool Supported, string OfficialOperation, string Notes);

public sealed record FawryOperationReference(string Value)
{
    public FawryOperationReference() : this(string.Empty)
    {
    }
}

public sealed record FawryAdvancedOperationMetadata(IReadOnlyDictionary<string, string> Values)
{
    public static FawryAdvancedOperation ResolveOperation(PaymentAction action)
    {
        var raw = action.Metadata.Values.TryGetValue(FawryAdvancedOperationMetadataKeys.Operation, out var value)
            ? value
            : action.Reference.Value;

        return raw.Trim().ToLowerInvariant() switch
        {
            "refund" => FawryAdvancedOperation.Refund,
            "partial_refund" or "partial-refund" or "partialrefund" => FawryAdvancedOperation.PartialRefund,
            "void" or "cancel_authorization" or "cancel-authorization" => FawryAdvancedOperation.Void,
            "capture" => FawryAdvancedOperation.Capture,
            "transaction_inquiry" or "transaction-inquiry" or "transactioninquiry" or "inquiry" => FawryAdvancedOperation.TransactionInquiry,
            "cancel_unpaid_order" or "cancel-unpaid-order" => FawryAdvancedOperation.CancelUnpaidOrder,
            "settlement_inquiry" or "settlement-inquiry" or "settlementinquiry" => FawryAdvancedOperation.SettlementInquiry,
            "operation_status" or "operation-status" or "operationstatus" => FawryAdvancedOperation.OperationStatus,
            _ => throw new ArgumentException("Canonical Fawry operation is not recognized.")
        };
    }

    public static FawryAdvancedOperationMetadata FromAction(PaymentAction action, FawryAdvancedOperation operation)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["operation"] = operation.ToString(),
            ["payment_action_id"] = action.Id.Value.ToString("N"),
            ["payment_session_id"] = action.SessionId.Value.ToString("N"),
            ["payment_attempt_id"] = action.AttemptId.Value.ToString("N"),
            ["correlation_id"] = action.CorrelationId.Value,
            ["action_reference"] = action.Reference.Value
        };

        foreach (var (key, value) in action.Metadata.Values)
        {
            if (!FawryAdvancedOperationMetadataKeys.AdapterOnlyKeys.Contains(key))
            {
                values[key] = value;
            }
        }

        return new FawryAdvancedOperationMetadata(new ReadOnlyDictionary<string, string>(
            values.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.Ordinal)));
    }
}

public static class FawryAdvancedOperationMetadataKeys
{
    public const string Operation = "Operation";
    public const string ProviderReference = "ProviderReference";
    public const string Amount = "Amount";
    public const string Currency = "Currency";
    public const string Reason = "Reason";
    public const string OperationReference = "OperationReference";

    public static readonly ISet<string> AdapterOnlyKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        Operation,
        ProviderReference,
        Amount,
        Currency,
        Reason,
        OperationReference
    };
}

internal static class FawryOperationSignature
{
    public static string Refund(string merchantCode, string referenceNumber, string refundAmount, string? reason, string secureKey)
    {
        return FawryHash.CreateRaw(string.Concat(merchantCode, referenceNumber, refundAmount, reason ?? string.Empty, secureKey));
    }

    public static string Capture(string merchantRefNum, string? captureAmount, string merchantCode, string secureKey)
    {
        return FawryHash.CreateRaw(string.Concat(merchantRefNum, captureAmount ?? string.Empty, merchantCode, secureKey));
    }

    public static string CancelAuthorization(string merchantRefNum, string merchantCode, string secureKey)
    {
        return FawryHash.CreateRaw(string.Concat(merchantRefNum, merchantCode, secureKey));
    }

    public static string Status(string merchantCode, string merchantRefNumber, string secureKey)
    {
        return FawryHash.CreateRaw(string.Concat(merchantCode, merchantRefNumber, secureKey));
    }
}
