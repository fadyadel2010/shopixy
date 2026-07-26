using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.API.Services.Payments.Adapters;

public interface IFawryPaymentAdapter
{
    Task<FawryPaymentAdapterResult> InitializePaymentAsync(
        PaymentAction action,
        FawryPaymentCredentials credentials,
        CancellationToken cancellationToken = default);
}

public sealed class FawryPaymentAdapter : IFawryPaymentAdapter
{
    private const string ChargeEndpoint = "ECommerceWeb/Fawry/payments/charge";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public FawryPaymentAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<FawryPaymentAdapterResult> InitializePaymentAsync(
        PaymentAction action,
        FawryPaymentCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = FawryPaymentRequestFactory.Create(action, credentials);
            using var response = await _httpClient.PostAsJsonAsync(
                ChargeEndpoint,
                request.OfficialRequest,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return FawryPaymentAdapterResult.Failed(
                    FawryProviderErrorTranslator.FromHttp(response.StatusCode));
            }

            var officialResponse = await response.Content.ReadFromJsonAsync<FawryChargeResponse>(JsonOptions, cancellationToken);
            if (officialResponse is null)
            {
                return FawryPaymentAdapterResult.Failed(
                    CanonicalProviderError.Validation(FawryProviderReference.ProviderName, "FAWRY_EMPTY_RESPONSE", "Fawry returned an empty response."));
            }

            if (!FawryChargeResponseMapper.IsAccepted(officialResponse))
            {
                return FawryPaymentAdapterResult.Failed(
                    FawryProviderErrorTranslator.FromOfficialResponse(officialResponse));
            }

            var integrityError = FawryChargeResponseIntegrityValidator.Validate(
                officialResponse,
                request.OfficialRequest,
                credentials);
            if (integrityError is not null)
            {
                return FawryPaymentAdapterResult.Failed(integrityError);
            }

            var evidence = FawryEvidenceFactory.FromChargeResponse(action, officialResponse);
            return FawryPaymentAdapterResult.Success(evidence, request, officialResponse);
        }
        catch (HttpRequestException exception)
        {
            return FawryPaymentAdapterResult.Failed(FawryProviderErrorTranslator.FromTransport(exception));
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return FawryPaymentAdapterResult.Failed(FawryProviderErrorTranslator.FromTimeout(exception));
        }
        catch (JsonException exception)
        {
            return FawryPaymentAdapterResult.Failed(
                CanonicalProviderError.Validation(FawryProviderReference.ProviderName, "FAWRY_RESPONSE_INVALID", exception.Message));
        }
        catch (ArgumentException exception)
        {
            return FawryPaymentAdapterResult.Failed(
                CanonicalProviderError.Validation(FawryProviderReference.ProviderName, "FAWRY_REQUEST_INVALID", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return FawryPaymentAdapterResult.Failed(
                CanonicalProviderError.Configuration(FawryProviderReference.ProviderName, "FAWRY_ADAPTER_INVALID", exception.Message));
        }
    }

}

public static class FawryPaymentRequestFactory
{
    public static FawryPaymentRequest Create(PaymentAction action, FawryPaymentCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(credentials);

        credentials.Validate();

        var values = action.Metadata.Values;
        var merchantRefNum = FawryMerchantReference.Build(action).Value;
        var customerReference = FawryCustomerProfileReference.Normalize(
            Optional(values, FawryRequestMetadataKeys.CustomerReference));
        var amount = FawryAmount.Format(Required(values, FawryRequestMetadataKeys.Amount));
        var paymentMethod = FawryPaymentMethodMapper.Map(Optional(values, FawryRequestMetadataKeys.PaymentMethod));
        var currency = Required(values, FawryRequestMetadataKeys.Currency).ToUpperInvariant();
        var customerMobile = FawryCustomerMobile.Normalize(
            Required(values, FawryRequestMetadataKeys.CustomerMobile));
        var customerEmail = FawryCustomerEmail.Normalize(
            Required(values, FawryRequestMetadataKeys.CustomerEmail));
        var description = Required(values, FawryRequestMetadataKeys.Description);
        var language = FawryLanguageMapper.Map(
            Required(values, FawryRequestMetadataKeys.Language));
        var signature = FawrySignature.CreatePayAtFawryChargeSignature(
            credentials.MerchantCode,
            merchantRefNum,
            customerReference,
            paymentMethod,
            amount,
            credentials.SecureKey);

        var officialRequest = new FawryChargeRequest
        {
            MerchantCode = credentials.MerchantCode,
            MerchantRefNum = merchantRefNum,
            CustomerProfileId = customerReference,
            CustomerName = Optional(values, FawryRequestMetadataKeys.CustomerName),
            CustomerMobile = customerMobile,
            CustomerEmail = customerEmail,
            PaymentMethod = paymentMethod,
            Amount = amount,
            CurrencyCode = currency,
            Description = description,
            PaymentExpiry = FawryExpirationMapper.ToOfficialExpiry(Optional(values, FawryRequestMetadataKeys.ExpirationUtc)),
            OrderWebHookUrl = Optional(values, FawryRequestMetadataKeys.OrderWebHookUrl),
            ReturnUrl = Optional(values, FawryRequestMetadataKeys.RedirectUrl),
            Language = language,
            Signature = signature,
            ChargeItems = FawryChargeItemMapper.FromAction(values, amount)
        };

        return new FawryPaymentRequest(
            officialRequest,
            new FawryIdempotencyProjection(merchantRefNum),
            new FawryMerchantReference(merchantRefNum),
            signature);
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key)
    {
        var value = Optional(values, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Canonical Fawry payment initialization field '{key}' is required.");
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

public static class FawryEvidenceFactory
{
    public static ProviderEvidence FromChargeResponse(PaymentAction action, FawryChargeResponse response)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(response);

        var providerReference = FawryChargeResponseMapper.ProviderReference(response);
        if (string.IsNullOrWhiteSpace(providerReference))
        {
            throw new ArgumentException("Fawry response reference number or merchant reference is required.", nameof(response));
        }

        var timestamp = FawryChargeResponseMapper.Timestamp(response) ?? action.LastUpdatedUtc;
        var metadata = FawryResponseMetadata.FromChargeResponse(action, response).Values;
        var evidenceHash = FawryHash.Create(
            providerReference,
            response.MerchantRefNumber ?? string.Empty,
            response.OrderStatus ?? string.Empty,
            response.StatusCode ?? string.Empty,
            response.PaymentAmount ?? response.OrderAmount ?? string.Empty,
            response.CurrencyCode ?? string.Empty,
            action.CorrelationId.Value);
        var evidenceId = FawryDeterministicId.Create(
            "fawry-charge-evidence",
            action.Id.Value.ToString("N"),
            providerReference,
            response.OrderStatus ?? string.Empty,
            timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        return ProviderEvidenceFactory.Create(
            new EvidenceId(evidenceId),
            action.Id,
            EvidenceType.ProviderNotification,
            EvidenceStatus.Recorded,
            new EvidenceReference(providerReference),
            new EvidenceSource(FawryProviderReference.ProviderName),
            new EvidenceTimestamp(timestamp),
            new EvidenceHash(evidenceHash),
            new ProviderPayloadReference($"fawry:charge:{providerReference}"),
            action.CorrelationId,
            new EvidenceMetadata(metadata));
    }
}

public static class FawryProviderErrorTranslator
{
    public static CanonicalProviderError FromHttp(HttpStatusCode statusCode)
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
            $"FAWRY_HTTP_{(int)statusCode}",
            "Fawry payment initialization failed.",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["http_status"] = ((int)statusCode).ToString(CultureInfo.InvariantCulture),
                ["provider_diagnostic"] = "Fawry returned a non-success HTTP status. Provider response body was discarded."
            });
    }

    public static CanonicalProviderError FromOfficialResponse(FawryChargeResponse response)
    {
        var code = FirstNonBlank(response.StatusCode, response.ResponseCode, response.OrderStatus, "FAWRY_REJECTED");
        var message = FirstNonBlank(response.StatusDescription, response.ResponseDescription, "Fawry rejected payment initialization.");
        return CanonicalProviderError.Create(
            FawryProviderReference.ProviderName,
            MapOfficialType(response),
            code,
            message,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider_status"] = response.OrderStatus ?? string.Empty,
                ["provider_code"] = code
            });
    }

    public static CanonicalProviderError FromTransport(HttpRequestException exception)
    {
        return CanonicalProviderError.Create(
            FawryProviderReference.ProviderName,
            CanonicalProviderErrorType.Network,
            "FAWRY_NETWORK_ERROR",
            "Fawry transport failed.",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["exception"] = exception.GetType().Name
            });
    }

    public static CanonicalProviderError FromTimeout(TaskCanceledException exception)
    {
        return CanonicalProviderError.Create(
            FawryProviderReference.ProviderName,
            CanonicalProviderErrorType.ProviderUnavailable,
            "FAWRY_TIMEOUT",
            "Fawry payment initialization timed out.",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["exception"] = exception.GetType().Name
            });
    }

    private static CanonicalProviderErrorType MapOfficialType(FawryChargeResponse response)
    {
        var code = FirstNonBlank(response.StatusCode, response.ResponseCode, string.Empty);
        if (code.Contains("AUTH", StringComparison.OrdinalIgnoreCase))
        {
            return CanonicalProviderErrorType.Authentication;
        }

        if (code.Contains("SIGN", StringComparison.OrdinalIgnoreCase))
        {
            return CanonicalProviderErrorType.Authentication;
        }

        if (code.Contains("INVALID", StringComparison.OrdinalIgnoreCase))
        {
            return CanonicalProviderErrorType.Validation;
        }

        return CanonicalProviderErrorType.ProviderRejected;
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
    }

}

public static class FawryChargeResponseMapper
{
    public static bool IsAccepted(FawryChargeResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Type) &&
            response.Type.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (int.TryParse(response.StatusCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var statusCode) &&
            statusCode != 200)
        {
            return false;
        }

        if (int.TryParse(response.ResponseCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var responseCode) &&
            responseCode != 200)
        {
            return false;
        }

        return true;
    }

    public static string ProviderReference(FawryChargeResponse response)
    {
        return response.ReferenceNumber?.Trim() ?? string.Empty;
    }

    public static DateTimeOffset? Timestamp(FawryChargeResponse response)
    {
        if (long.TryParse(response.PaymentTime, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        if (DateTimeOffset.TryParse(response.PaymentTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    public static PaymentStatus MapStatus(string? providerStatus)
    {
        return providerStatus?.Trim().ToUpperInvariant() switch
        {
            "PAID" or "CAPTURED" => PaymentStatus.Completed,
            "AUTHORIZED" => PaymentStatus.Authorized,
            "NEW" or "UNPAID" or "PENDING" => PaymentStatus.AwaitingCustomer,
            "EXPIRED" => PaymentStatus.Expired,
            "CANCELED" or "CANCELLED" => PaymentStatus.Cancelled,
            "FAILED" or "DECLINED" => PaymentStatus.Failed,
            "REFUNDED" => PaymentStatus.Refunded,
            "PARTIAL_REFUNDED" or "PARTIALLY_REFUNDED" => PaymentStatus.PartiallyRefunded,
            _ => PaymentStatus.ReconciliationRequired
        };
    }

}

public static class FawryChargeResponseIntegrityValidator
{
    private static readonly ISet<string> AcceptedOrderStatuses = new HashSet<string>(StringComparer.Ordinal)
    {
        "PENDING",
        "PAID"
    };

    public static CanonicalProviderError? Validate(
        FawryChargeResponse response,
        FawryChargeRequest request,
        FawryPaymentCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);

        if (!RequiredExact(response.Type, "ChargeResponse"))
        {
            return Invalid("FAWRY_RESPONSE_TYPE_INVALID", "Fawry charge response type is missing or unsupported.");
        }

        if (!RequiredExact(response.StatusCode, "200"))
        {
            return Invalid("FAWRY_RESPONSE_STATUS_CODE_INVALID", "Fawry charge response status code is missing or unsupported.");
        }

        if (!string.IsNullOrWhiteSpace(response.ResponseCode) &&
            !RequiredExact(response.ResponseCode, "200"))
        {
            return Invalid("FAWRY_RESPONSE_STATUS_CODE_INVALID", "Fawry charge response contains a conflicting response code.");
        }

        if (string.IsNullOrWhiteSpace(response.ReferenceNumber))
        {
            return Invalid("FAWRY_RESPONSE_REFERENCE_MISSING", "Fawry charge response reference number is required.");
        }

        if (string.IsNullOrWhiteSpace(response.MerchantRefNumber))
        {
            return Invalid("FAWRY_RESPONSE_MERCHANT_REFERENCE_MISSING", "Fawry charge response merchant reference number is required.");
        }

        if (!TryAmount(response.PaymentAmount, out var paymentAmount) ||
            !TryAmount(response.OrderAmount, out var orderAmount))
        {
            return Invalid("FAWRY_RESPONSE_AMOUNT_INVALID", "Fawry charge response payment and order amounts are required decimals.");
        }

        if (!TryOptionalAmount(response.FawryFees, out var fawryFees) ||
            !TryOptionalAmount(response.ShippingFees, out var shippingFees))
        {
            return Invalid("FAWRY_RESPONSE_FEES_INVALID", "Fawry charge response fees must be valid decimals when supplied.");
        }

        if (!RequiredExact(response.PaymentMethod, request.PaymentMethod))
        {
            return Invalid("FAWRY_RESPONSE_PAYMENT_METHOD_MISMATCH", "Fawry charge response payment method does not match the submitted request.");
        }

        if (string.IsNullOrWhiteSpace(response.OrderStatus) ||
            !AcceptedOrderStatuses.Contains(response.OrderStatus.Trim()))
        {
            return Invalid("FAWRY_RESPONSE_ORDER_STATUS_INVALID", "Fawry charge response order status is missing or unsupported.");
        }

        if (string.IsNullOrWhiteSpace(response.Signature))
        {
            return Authentication("FAWRY_RESPONSE_SIGNATURE_MISSING", "Fawry charge response signature is required.");
        }

        var signatureSource = string.Concat(
            response.ReferenceNumber.Trim(),
            response.MerchantRefNumber.Trim(),
            paymentAmount,
            orderAmount,
            response.OrderStatus.Trim(),
            response.PaymentMethod.Trim(),
            fawryFees,
            shippingFees,
            OptionalTrim(response.AuthNumber),
            OptionalTrim(response.CustomerMail),
            OptionalTrim(response.CustomerMobile),
            credentials.SecureKey);
        var expectedSignature = SHA256.HashData(Encoding.UTF8.GetBytes(signatureSource));
        if (!TryDecodeSha256(response.Signature, out var suppliedSignature) ||
            !CryptographicOperations.FixedTimeEquals(expectedSignature, suppliedSignature))
        {
            return Authentication("FAWRY_RESPONSE_SIGNATURE_INVALID", "Fawry charge response signature is invalid.");
        }

        if (!string.Equals(response.MerchantRefNumber.Trim(), request.MerchantRefNum, StringComparison.Ordinal))
        {
            return Invalid("FAWRY_RESPONSE_MERCHANT_REFERENCE_MISMATCH", "Fawry charge response merchant reference does not match the submitted request.");
        }

        if (!TryAmount(request.Amount, out var requestedAmount) ||
            !string.Equals(paymentAmount, requestedAmount, StringComparison.Ordinal) ||
            !string.Equals(orderAmount, requestedAmount, StringComparison.Ordinal))
        {
            return Invalid("FAWRY_RESPONSE_AMOUNT_MISMATCH", "Fawry charge response amount does not match the submitted request.");
        }

        if (!string.IsNullOrWhiteSpace(response.CurrencyCode) &&
            !RequiredExact(response.CurrencyCode, request.CurrencyCode))
        {
            return Invalid("FAWRY_RESPONSE_CURRENCY_MISMATCH", "Fawry charge response currency does not match the submitted request.");
        }

        if (!string.IsNullOrWhiteSpace(response.MerchantCode) &&
            !RequiredExact(response.MerchantCode, credentials.MerchantCode))
        {
            return Authentication("FAWRY_RESPONSE_MERCHANT_IDENTITY_MISMATCH", "Fawry charge response merchant identity does not match the active credentials.");
        }

        if (!OptionalMatches(response.CustomerProfileId, request.CustomerProfileId) ||
            !OptionalMatches(response.CustomerMobile, request.CustomerMobile) ||
            !OptionalMatches(response.CustomerMail, request.CustomerEmail))
        {
            return Invalid("FAWRY_RESPONSE_CUSTOMER_MISMATCH", "Fawry charge response customer fields do not match the submitted request.");
        }

        return null;
    }

    private static bool RequiredExact(string? actual, string expected) =>
        !string.IsNullOrWhiteSpace(actual) &&
        string.Equals(actual, actual.Trim(), StringComparison.Ordinal) &&
        string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool OptionalMatches(string? actual, string? expected) =>
        string.IsNullOrWhiteSpace(actual) ||
        (!string.IsNullOrWhiteSpace(expected) &&
         string.Equals(actual, actual.Trim(), StringComparison.Ordinal) &&
         string.Equals(actual, expected, StringComparison.Ordinal));

    private static bool TryAmount(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            !decimal.TryParse(
                value,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var amount) ||
            amount < 0m ||
            ((decimal.GetBits(amount)[3] >> 16) & 0xFF) > 2)
        {
            return false;
        }

        normalized = amount.ToString("0.00", CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryOptionalAmount(string? value, out string normalized)
    {
        normalized = string.Empty;
        return string.IsNullOrWhiteSpace(value) || TryAmount(value, out normalized);
    }

    private static string OptionalTrim(string? value) => value?.Trim() ?? string.Empty;

    private static bool TryDecodeSha256(string signature, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        var normalized = signature.Trim();
        if (normalized.Length != 64)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(normalized);
            return bytes.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static CanonicalProviderError Invalid(string code, string message) =>
        CanonicalProviderError.Validation(FawryProviderReference.ProviderName, code, message);

    private static CanonicalProviderError Authentication(string code, string message) =>
        CanonicalProviderError.Create(
            FawryProviderReference.ProviderName,
            CanonicalProviderErrorType.Authentication,
            code,
            message);
}

public sealed record FawryPaymentAdapterResult
{
    public bool Succeeded { get; init; }
    public ProviderEvidence? Evidence { get; init; }
    public FawryPaymentRequest? Request { get; init; }
    public FawryChargeResponse? OfficialResponse { get; init; }
    public CanonicalProviderError? Error { get; init; }

    private FawryPaymentAdapterResult()
    {
    }

    public static FawryPaymentAdapterResult Success(
        ProviderEvidence evidence,
        FawryPaymentRequest request,
        FawryChargeResponse officialResponse)
    {
        return new FawryPaymentAdapterResult
        {
            Succeeded = true,
            Evidence = evidence,
            Request = request,
            OfficialResponse = officialResponse
        };
    }

    public static FawryPaymentAdapterResult Failed(CanonicalProviderError error)
    {
        return new FawryPaymentAdapterResult
        {
            Succeeded = false,
            Error = error
        };
    }
}

public sealed record FawryPaymentRequest(
    FawryChargeRequest OfficialRequest,
    FawryIdempotencyProjection IdempotencyProjection,
    FawryMerchantReference MerchantReference,
    string Signature);

public sealed record FawryPaymentCredentials(string MerchantCode, string ApiKey, string SecureKey)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(MerchantCode))
        {
            throw new InvalidOperationException("Fawry merchant code is required.");
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Fawry API key is required.");
        }

        if (string.IsNullOrWhiteSpace(SecureKey))
        {
            throw new InvalidOperationException("Fawry secure key is required.");
        }
    }
}

public sealed record FawryChargeRequest
{
    [JsonPropertyName("merchantCode")]
    public string MerchantCode { get; init; } = string.Empty;

    [JsonPropertyName("merchantRefNum")]
    public string MerchantRefNum { get; init; } = string.Empty;

    [JsonPropertyName("customerProfileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomerProfileId { get; init; }

    [JsonPropertyName("customerName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomerName { get; init; }

    [JsonPropertyName("customerMobile")]
    public string CustomerMobile { get; init; } = string.Empty;

    [JsonPropertyName("customerEmail")]
    public string CustomerEmail { get; init; } = string.Empty;

    [JsonPropertyName("paymentMethod")]
    public string PaymentMethod { get; init; } = string.Empty;

    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    [JsonPropertyName("currencyCode")]
    public string CurrencyCode { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("paymentExpiry")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaymentExpiry { get; init; }

    [JsonPropertyName("orderWebHookUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OrderWebHookUrl { get; init; }

    [JsonPropertyName("returnUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReturnUrl { get; init; }

    [JsonPropertyName("language")]
    public string Language { get; init; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; init; } = string.Empty;

    [JsonPropertyName("chargeItems")]
    public IReadOnlyCollection<FawryChargeItem> ChargeItems { get; init; } = Array.AsReadOnly(Array.Empty<FawryChargeItem>());
}

public sealed record FawryChargeItem
{
    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("price")]
    public string Price { get; init; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; } = 1;
}

public sealed record FawryChargeResponse
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("statusCode")]
    [JsonConverter(typeof(FawryFlexibleStringJsonConverter))]
    public string? StatusCode { get; init; }

    [JsonPropertyName("statusDescription")]
    public string? StatusDescription { get; init; }

    [JsonPropertyName("responseCode")]
    [JsonConverter(typeof(FawryFlexibleStringJsonConverter))]
    public string? ResponseCode { get; init; }

    [JsonPropertyName("responseDescription")]
    public string? ResponseDescription { get; init; }

    [JsonPropertyName("merchantCode")]
    public string? MerchantCode { get; init; }

    [JsonPropertyName("merchantRefNumber")]
    public string? MerchantRefNumber { get; init; }

    [JsonPropertyName("merchantRefNum")]
    public string? MerchantRefNum { get; init; }

    [JsonPropertyName("fawryRefNumber")]
    public string? FawryRefNumber { get; init; }

    [JsonPropertyName("referenceNumber")]
    public string? ReferenceNumber { get; init; }

    [JsonPropertyName("paymentReferenceNumber")]
    public string? PaymentReferenceNumber { get; init; }

    [JsonPropertyName("orderStatus")]
    public string? OrderStatus { get; init; }

    [JsonPropertyName("paymentAmount")]
    [JsonConverter(typeof(FawryFlexibleStringJsonConverter))]
    public string? PaymentAmount { get; init; }

    [JsonPropertyName("orderAmount")]
    [JsonConverter(typeof(FawryFlexibleStringJsonConverter))]
    public string? OrderAmount { get; init; }

    [JsonPropertyName("fawryFees")]
    [JsonConverter(typeof(FawryFlexibleStringJsonConverter))]
    public string? FawryFees { get; init; }

    [JsonPropertyName("shippingFees")]
    [JsonConverter(typeof(FawryFlexibleStringJsonConverter))]
    public string? ShippingFees { get; init; }

    [JsonPropertyName("authNumber")]
    [JsonConverter(typeof(FawryFlexibleStringJsonConverter))]
    public string? AuthNumber { get; init; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; init; }

    [JsonPropertyName("paymentMethod")]
    public string? PaymentMethod { get; init; }

    [JsonPropertyName("paymentTime")]
    [JsonConverter(typeof(FawryFlexibleStringJsonConverter))]
    public string? PaymentTime { get; init; }

    [JsonPropertyName("customerMobile")]
    public string? CustomerMobile { get; init; }

    [JsonPropertyName("customerMail")]
    public string? CustomerMail { get; init; }

    [JsonPropertyName("customerProfileId")]
    [JsonConverter(typeof(FawryFlexibleStringJsonConverter))]
    public string? CustomerProfileId { get; init; }

    [JsonPropertyName("signature")]
    public string? Signature { get; init; }

    [JsonPropertyName("expirationTime")]
    public string? ExpirationTime { get; init; }

    [JsonPropertyName("paymentUrl")]
    public string? PaymentUrl { get; init; }

    [JsonPropertyName("redirectUrl")]
    public string? RedirectUrl { get; init; }
}

public sealed class FawryFlexibleStringJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.GetRawText();
        }

        throw new JsonException("Fawry response field must be a string or number.");
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}

public sealed record FawryProviderReference(string Value)
{
    public const string ProviderName = "fawry";
}

public sealed record FawryMerchantReference(string Value)
{
    public static FawryMerchantReference Build(PaymentAction action)
    {
        var supplied = action.Metadata.Values.TryGetValue(FawryRequestMetadataKeys.MerchantReference, out var value)
            ? value?.Trim()
            : null;
        var sourceHash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', new[]
        {
            "fawry",
            "charge",
            supplied ?? string.Empty,
            action.SessionId.Value.ToString("N"),
            action.AttemptId.Value.ToString("N"),
            action.Id.Value.ToString("N"),
            action.Type.ToString(),
            action.Reference.Value,
            action.CorrelationId.Value
        })));
        var numeric = (BinaryPrimitives.ReadUInt64BigEndian(sourceHash) % 900_000_000_000_000_000UL)
            + 100_000_000_000_000_000UL;
        return new FawryMerchantReference(numeric.ToString(CultureInfo.InvariantCulture));
    }
}

public sealed record FawryIdempotencyProjection(string Value);

public sealed record FawryResponseMetadata(IReadOnlyDictionary<string, string> Values)
{
    public static FawryResponseMetadata FromChargeResponse(PaymentAction action, FawryChargeResponse response)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = FawryProviderReference.ProviderName,
            ["provider_reference"] = FawryChargeResponseMapper.ProviderReference(response),
            ["merchant_reference"] = response.MerchantRefNumber ?? string.Empty,
            ["provider_status"] = response.OrderStatus ?? string.Empty,
            ["canonical_status"] = FawryChargeResponseMapper.MapStatus(response.OrderStatus).ToString(),
            ["correlation_id"] = action.CorrelationId.Value,
            ["payment_action_id"] = action.Id.Value.ToString("N"),
            ["amount"] = response.PaymentAmount ?? response.OrderAmount ?? string.Empty,
            ["currency"] = response.CurrencyCode ?? string.Empty,
            ["payment_method"] = response.PaymentMethod ?? string.Empty,
            ["reference_number"] = response.ReferenceNumber ?? string.Empty,
            ["fawry_ref_number"] = response.FawryRefNumber ?? string.Empty,
            ["payment_url"] = response.PaymentUrl ?? response.RedirectUrl ?? string.Empty,
            ["expiration_time"] = response.ExpirationTime ?? string.Empty
        };

        return new FawryResponseMetadata(values);
    }
}

public static class FawryRequestMetadataKeys
{
    public const string Amount = "Amount";
    public const string Currency = "Currency";
    public const string MerchantReference = "MerchantReference";
    public const string CustomerReference = "CustomerReference";
    public const string CustomerName = "CustomerName";
    public const string CustomerMobile = "CustomerMobile";
    public const string CustomerEmail = "CustomerEmail";
    public const string PaymentMethod = "PaymentMethod";
    public const string Description = "Description";
    public const string ExpirationUtc = "ExpirationUtc";
    public const string RedirectUrl = "RedirectUrl";
    public const string OrderWebHookUrl = "OrderWebHookUrl";
    public const string Language = "Language";
    public const string ItemId = "ItemId";
    public const string ItemDescription = "ItemDescription";
}

internal static class FawryPaymentMethodMapper
{
    public static string Map(string? paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            return "PayAtFawry";
        }

        return paymentMethod.Trim().ToUpperInvariant() switch
        {
            "PAYATFAWRY" or "PAY_AT_FAWRY" or "REFERENCE" => "PayAtFawry",
            "CARD" or "CARD_3DS" or "MWALLET" or "VALU" => paymentMethod.Trim().ToUpperInvariant(),
            _ => throw new ArgumentException("Canonical Fawry payment method is not supported by payment initialization mapping.")
        };
    }
}

internal static class FawryCustomerProfileReference
{
    public static string? Normalize(string? customerProfileId)
    {
        if (string.IsNullOrWhiteSpace(customerProfileId))
        {
            return null;
        }

        var normalized = customerProfileId.Trim();
        if (!normalized.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Fawry customer profile id must be an integer when supplied.");
        }

        return normalized;
    }
}

internal static class FawryCustomerMobile
{
    public static string Normalize(string customerMobile)
    {
        var normalized = new string(customerMobile.Where(char.IsAsciiDigit).ToArray());
        if (normalized.StartsWith("20", StringComparison.Ordinal) && normalized.Length == 12)
        {
            normalized = $"0{normalized[2..]}";
        }

        if (normalized.Length != 11 || !normalized.StartsWith("01", StringComparison.Ordinal))
        {
            throw new ArgumentException("Fawry customer mobile must be an Egyptian mobile number in 01xxxxxxxxx format.");
        }

        return normalized;
    }
}

internal static class FawryCustomerEmail
{
    public static string Normalize(string customerEmail)
    {
        var normalized = customerEmail.Trim().ToLowerInvariant();
        if (!MailAddress.TryCreate(normalized, out var parsed) ||
            !string.Equals(parsed.Address, normalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Fawry customer email must be a valid e-mail address.");
        }

        return normalized;
    }
}

internal static class FawryLanguageMapper
{
    public static string Map(string language)
    {
        return language.Trim().ToLowerInvariant() switch
        {
            "ar" or "ar-eg" => "ar-eg",
            "en" or "en-gb" => "en-gb",
            _ => throw new ArgumentException("Fawry language must be 'ar-eg' or 'en-gb'.")
        };
    }
}

internal static class FawryAmount
{
    public static string Format(string amount)
    {
        if (!decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException("Canonical amount must be an invariant decimal value.");
        }

        if (parsed <= 0m)
        {
            throw new ArgumentException("Canonical amount must be positive.");
        }

        return parsed.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

internal static class FawryExpirationMapper
{
    public static string? ToOfficialExpiry(string? expirationUtc)
    {
        if (string.IsNullOrWhiteSpace(expirationUtc))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(expirationUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            throw new ArgumentException("Canonical Fawry expiration must be an ISO-8601 UTC timestamp.");
        }

        return parsed.ToUniversalTime().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
    }
}

internal static class FawryChargeItemMapper
{
    public static IReadOnlyCollection<FawryChargeItem> FromAction(IReadOnlyDictionary<string, string> values, string amount)
    {
        var itemId = values.TryGetValue(FawryRequestMetadataKeys.ItemId, out var suppliedItemId) &&
            !string.IsNullOrWhiteSpace(suppliedItemId)
            ? suppliedItemId.Trim()
            : "payment";
        var description = values.TryGetValue(FawryRequestMetadataKeys.ItemDescription, out var suppliedDescription) &&
            !string.IsNullOrWhiteSpace(suppliedDescription)
            ? suppliedDescription.Trim()
            : "Payment";

        return Array.AsReadOnly(new[]
        {
            new FawryChargeItem
            {
                ItemId = itemId,
                Description = description,
                Price = amount,
                Quantity = 1
            }
        });
    }
}

internal static class FawrySignature
{
    public static string CreatePayAtFawryChargeSignature(
        string merchantCode,
        string merchantRefNum,
        string? customerProfileId,
        string paymentMethod,
        string amount,
        string secureKey)
    {
        var source = string.Concat(
            merchantCode,
            merchantRefNum,
            customerProfileId ?? string.Empty,
            paymentMethod,
            amount,
            secureKey);
        return FawryHash.CreateRaw(source);
    }
}

internal static class FawryDeterministicId
{
    public static Guid Create(string scope, params string[] components)
    {
        var source = string.Join('|', new[] { scope }.Concat(components));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        var bytes = hash.Take(16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

internal static class FawryHash
{
    public static string Create(params string[] components)
    {
        return CreateRaw(string.Join('|', components));
    }

    public static string CreateRaw(string source)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }
}
