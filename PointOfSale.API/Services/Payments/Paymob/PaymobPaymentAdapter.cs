using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.API.Services.Payments.Paymob;

public interface IPaymobPaymentAdapter
{
    Task<PaymobPaymentAdapterResult> CreateAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);
}

public partial interface IPaymobOfficialApiClient
{
    Task CheckAvailabilityAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("This Paymob client does not support availability checks.");

    Task<PaymobIntentionResponse> CreateIntentionAsync(
        PaymobIntentionRequest request,
        string secretKey,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("This Paymob client does not support the Intention API.");

    Task<PaymobAdapterAuthResponse> AuthenticateAsync(
        PaymobAuthRequest request,
        CancellationToken cancellationToken);

    Task<PaymobOrderRegistrationResponse> RegisterOrderAsync(
        PaymobOrderRegistrationRequest request,
        string authToken,
        CancellationToken cancellationToken);

    Task<PaymobAdapterPaymentKeyResponse> CreatePaymentKeyAsync(
        PaymobPaymentKeyRequest request,
        string authToken,
        CancellationToken cancellationToken);
}

public sealed class PaymobPaymentAdapter : IPaymobPaymentAdapter
{
    private readonly IPaymobOfficialApiClient _client;
    private readonly IPaymobAdapterCredentialHandoff _credentialHandoff;
    private readonly IPaymobProviderCheckpointStore? _checkpointStore;

    public PaymobPaymentAdapter(
        IPaymobOfficialApiClient client,
        IPaymobAdapterCredentialHandoff credentialHandoff,
        IPaymobProviderCheckpointStore? checkpointStore = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _credentialHandoff = credentialHandoff ?? throw new ArgumentNullException(nameof(credentialHandoff));
        _checkpointStore = checkpointStore;
    }

    public async Task<PaymobPaymentAdapterResult> CreateAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var credentials = _credentialHandoff.ConsumeFor(action);
            var request = PaymobPaymentRequestFactory.Create(action, credentials);
            var checkpoint = _checkpointStore is null
                ? PaymobProviderCheckpoint.Unmanaged
                : await _checkpointStore.LoadAsync(
                    action,
                    request.OrderRequest.MerchantOrderId,
                    cancellationToken);

            if (checkpoint.PaymentKey is not null)
            {
                return CreateCheckpointResult(action, request, checkpoint);
            }

            if (checkpoint.ProviderOrderId is not null)
            {
                throw new PaymobProviderCheckpointException(
                    "PAYMOB_INTENTION_RECOVERY_REQUIRED",
                    "The Paymob intention was accepted without a durable checkout secret. Automatic intention recreation is blocked until provider reconciliation completes.");
            }

            if (_checkpointStore is not null)
            {
                await _checkpointStore.BeginOrderRegistrationAsync(
                    action,
                    request.OrderRequest.MerchantOrderId,
                    cancellationToken);
            }

            var intentionResponse = await _client.CreateIntentionAsync(
                request.IntentionRequest ?? throw new InvalidOperationException("Paymob intention request is required."),
                credentials.SecretKey,
                cancellationToken);
            var providerOrderId = intentionResponse.ResolveOrderId();
            var orderResponse = new PaymobOrderRegistrationResponse(
                providerOrderId,
                request.OrderRequest.MerchantOrderId);

            if (_checkpointStore is not null)
            {
                await _checkpointStore.PersistAcceptedOrderAsync(
                    action,
                    request.OrderRequest.MerchantOrderId,
                    orderResponse,
                    cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(intentionResponse.ClientSecret) ||
                string.IsNullOrWhiteSpace(intentionResponse.Id))
            {
                throw new JsonException("Paymob intention response did not include its id and client secret.");
            }

            var paymentKeyResponse = new PaymobAdapterPaymentKeyResponse(intentionResponse.ClientSecret);
            var iframeUrl = PaymobRedirectFactory.CreateUnifiedCheckoutUrl(
                credentials.PublicKey,
                intentionResponse.ClientSecret);

            if (_checkpointStore is not null)
            {
                await _checkpointStore.PersistPaymentKeyAsync(
                    action,
                    request.OrderRequest.MerchantOrderId,
                    orderResponse.Id.ToString(CultureInfo.InvariantCulture),
                    paymentKeyResponse.Token,
                    iframeUrl,
                    cancellationToken);
            }

            var providerResponse = new PaymobPaymentPreparationResponse(
                new PaymobAdapterAuthResponse(string.Empty),
                orderResponse,
                paymentKeyResponse,
                iframeUrl,
                intentionResponse.Id);
            var evidence = PaymobEvidenceFactory.FromPaymentPreparation(
                action,
                request,
                providerResponse);

            return PaymobPaymentAdapterResult.Success(evidence, request, providerResponse);
        }
        catch (PaymobProviderCheckpointException exception)
        {
            return PaymobPaymentAdapterResult.Failed(
                PaymobProviderErrorTranslator.Configuration(exception.Code, exception.Message));
        }
        catch (PaymobOfficialApiException exception)
        {
            return PaymobPaymentAdapterResult.Failed(PaymobProviderErrorTranslator.Translate(exception));
        }
        catch (HttpRequestException exception)
        {
            return PaymobPaymentAdapterResult.Failed(PaymobProviderErrorTranslator.Translate(exception));
        }
        catch (JsonException exception)
        {
            return PaymobPaymentAdapterResult.Failed(
                PaymobProviderErrorTranslator.Validation("PAYMOB_RESPONSE_INVALID", exception.Message));
        }
        catch (ArgumentException exception)
        {
            return PaymobPaymentAdapterResult.Failed(
                PaymobProviderErrorTranslator.Validation("PAYMOB_REQUEST_INVALID", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return PaymobPaymentAdapterResult.Failed(
                PaymobProviderErrorTranslator.Configuration("PAYMOB_ADAPTER_INVALID", exception.Message));
        }
    }

    private static PaymobPaymentAdapterResult CreateCheckpointResult(
        PaymentAction action,
        PaymobPaymentPreparationRequest request,
        PaymobProviderCheckpoint checkpoint)
    {
        if (checkpoint.ProviderOrderId is null || checkpoint.IframeUrl is null || checkpoint.PaymentKey is null)
        {
            throw new PaymobProviderCheckpointException(
                "PAYMOB_PROVIDER_CHECKPOINT_INVALID",
                "The durable Paymob payment-key checkpoint is incomplete.");
        }

        var orderResponse = CreateCheckpointedOrderResponse(
            checkpoint.ProviderOrderId,
            request.OrderRequest.MerchantOrderId);
        var checkpointedRequest = request with
        {
            PaymentKeyRequest = request.PaymentKeyRequest with
            {
                OrderId = checkpoint.ProviderOrderId
            }
        };
        var providerResponse = new PaymobPaymentPreparationResponse(
            new PaymobAdapterAuthResponse(string.Empty),
            orderResponse,
            new PaymobAdapterPaymentKeyResponse(checkpoint.PaymentKey),
            checkpoint.IframeUrl,
            $"checkpoint:{checkpoint.ProviderOrderId}");
        var evidence = PaymobEvidenceFactory.FromPaymentPreparation(
            action,
            checkpointedRequest,
            providerResponse);

        return PaymobPaymentAdapterResult.Success(evidence, checkpointedRequest, providerResponse);
    }

    private static PaymobOrderRegistrationResponse CreateCheckpointedOrderResponse(
        string providerOrderId,
        string merchantOrderId)
    {
        if (!long.TryParse(providerOrderId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new PaymobProviderCheckpointException(
                "PAYMOB_PROVIDER_CHECKPOINT_INVALID",
                "The durable Paymob provider order id is invalid.");
        }

        return new PaymobOrderRegistrationResponse(parsed, merchantOrderId);
    }
}

public sealed partial class PaymobOfficialApiHttpClient : IPaymobOfficialApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _client;

    public PaymobOfficialApiHttpClient(IHttpClientFactory httpClientFactory)
    {
        _client = httpClientFactory.CreateClient(PaymobFoundationServiceCollectionExtensions.HttpClientName);
    }

    public async Task<PaymobAdapterAuthResponse> AuthenticateAsync(
        PaymobAuthRequest request,
        CancellationToken cancellationToken)
    {
        return await PostAsync<PaymobAuthRequest, PaymobAdapterAuthResponse>(
            "auth/tokens",
            request,
            authToken: null,
            cancellationToken);
    }

    public async Task<PaymobOrderRegistrationResponse> RegisterOrderAsync(
        PaymobOrderRegistrationRequest request,
        string authToken,
        CancellationToken cancellationToken)
    {
        return await PostAsync<PaymobOrderRegistrationRequest, PaymobOrderRegistrationResponse>(
            "ecommerce/orders",
            request,
            authToken,
            cancellationToken);
    }

    public async Task<PaymobAdapterPaymentKeyResponse> CreatePaymentKeyAsync(
        PaymobPaymentKeyRequest request,
        string authToken,
        CancellationToken cancellationToken)
    {
        return await PostAsync<PaymobPaymentKeyRequest, PaymobAdapterPaymentKeyResponse>(
            "acceptance/payment_keys",
            request,
            authToken,
            cancellationToken);
    }

    public async Task<PaymobIntentionResponse> CreateIntentionAsync(
        PaymobIntentionRequest request,
        string secretKey,
        CancellationToken cancellationToken)
    {
        return await PostAsync<PaymobIntentionRequest, PaymobIntentionResponse>(
            "../v1/intention/",
            request,
            authToken: secretKey,
            cancellationToken,
            authenticationScheme: "Token");
    }

    public async Task CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "../");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PaymobOfficialApiException(response.StatusCode, string.Empty, null);
        }
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string relativePath,
        TRequest request,
        string? authToken,
        CancellationToken cancellationToken,
        string authenticationScheme = "Bearer")
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        if (!string.IsNullOrWhiteSpace(authToken))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue(authenticationScheme, authToken);
        }

        using var response = await _client.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PaymobOfficialApiException(
                response.StatusCode,
                body,
                response.Headers.TryGetValues("X-Request-Id", out var requestIds)
                    ? requestIds.FirstOrDefault()
                    : null);
        }

        var result = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
        if (result is null)
        {
            throw new JsonException("Paymob response body could not be deserialized.");
        }

        return result;
    }

    private async Task<TResponse> GetAsync<TResponse>(
        string relativePath,
        string authToken,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, relativePath);

        if (!string.IsNullOrWhiteSpace(authToken))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        }

        using var response = await _client.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PaymobOfficialApiException(
                response.StatusCode,
                body,
                response.Headers.TryGetValues("X-Request-Id", out var requestIds)
                    ? requestIds.FirstOrDefault()
                    : null);
        }

        var result = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
        if (result is null)
        {
            throw new JsonException("Paymob response body could not be deserialized.");
        }

        return result;
    }
}

public static class PaymobPaymentRequestFactory
{
    public static PaymobPaymentPreparationRequest Create(
        PaymentAction action,
        PaymobTenantRuntimeCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(credentials);

        var values = action.Metadata.Values;
        var amountCents = PaymobMoneyMapper.ToMinorUnits(
            Required(values, PaymobRequestMetadataKeys.Amount),
            Required(values, PaymobRequestMetadataKeys.Currency));
        var currency = Required(values, PaymobRequestMetadataKeys.Currency).ToUpperInvariant();
        var merchantOrderId = Optional(values, PaymobRequestMetadataKeys.MerchantOrderId)
            ?? PaymobIdempotencyKey.Build(action).Value;
        if (!PaymentMethodConfigurationValidator.TryParsePaymobIntegrationIds(
                credentials.IntegrationId,
                out var integrationIds))
        {
            throw new InvalidOperationException(
                "Paymob integration IDs must be one or more positive integers separated by commas.");
        }

        var integrationId = integrationIds[0];
        var expiration = ParseOptionalInt(values, PaymobRequestMetadataKeys.ExpirationSeconds) ?? 3600;
        var billingData = PaymobBillingData.FromActionMetadata(values);
        var canonicalMetadata = PaymobRequestMetadata.FromAction(action).Values;

        var authRequest = new PaymobAuthRequest(credentials.ApiKey ?? string.Empty);
        var orderRequest = new PaymobOrderRegistrationRequest(
            AuthToken: string.Empty,
            DeliveryNeeded: "false",
            AmountCents: amountCents,
            Currency: currency,
            MerchantOrderId: merchantOrderId,
            Items:
            [
                new PaymobOrderItem(
                    Optional(values, PaymobRequestMetadataKeys.ItemName) ?? action.Reference.Value,
                    amountCents,
                    1)
            ]);
        var paymentKeyRequest = new PaymobPaymentKeyRequest(
            AuthToken: string.Empty,
            AmountCents: amountCents,
            Expiration: expiration,
            OrderId: string.Empty,
            BillingData: billingData,
            Currency: currency,
            IntegrationId: integrationId,
            LockOrderWhenPaid: ParseOptionalBool(values, PaymobRequestMetadataKeys.LockOrderWhenPaid) ?? true);
        var intentionRequest = new PaymobIntentionRequest(
            amountCents,
            currency,
            integrationIds,
            [new PaymobIntentionItem(
                Optional(values, PaymobRequestMetadataKeys.ItemName) ?? action.Reference.Value,
                amountCents,
                action.Reference.Value,
                1)],
            billingData,
            new PaymobIntentionCustomer(
                billingData.FirstName,
                billingData.LastName,
                billingData.Email),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["internal_reference"] = merchantOrderId
            },
            merchantOrderId,
            expiration,
            credentials.NotificationUrl.ToString(),
            credentials.RedirectionUrl.ToString());

        return new PaymobPaymentPreparationRequest(
            authRequest,
            orderRequest,
            paymentKeyRequest,
            new PaymobIdempotencyKey(merchantOrderId),
            canonicalMetadata,
            intentionRequest);
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key)
    {
        var value = Optional(values, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Canonical Paymob field '{key}' is required.");
        }

        return value;
    }

    private static string? Optional(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static int ParseInt(string value, string name)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"{name} must be a positive integer.");
        }

        return parsed;
    }

    private static int? ParseOptionalInt(IReadOnlyDictionary<string, string> values, string key)
    {
        var value = Optional(values, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new ArgumentException($"Canonical Paymob field '{key}' must be a positive integer.");
        }

        return parsed;
    }

    private static bool? ParseOptionalBool(IReadOnlyDictionary<string, string> values, string key)
    {
        var value = Optional(values, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!bool.TryParse(value, out var parsed))
        {
            throw new ArgumentException($"Canonical Paymob field '{key}' must be true or false.");
        }

        return parsed;
    }
}

public static class PaymobEvidenceFactory
{
    public static ProviderEvidence FromPaymentPreparation(
        PaymentAction action,
        PaymobPaymentPreparationRequest request,
        PaymobPaymentPreparationResponse response)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        if (response.Order.Id <= 0)
        {
            throw new ArgumentException("Paymob order id is required.", nameof(response));
        }

        if (string.IsNullOrWhiteSpace(response.IntentionId))
        {
            throw new ArgumentException("Paymob intention id is required.", nameof(response));
        }

        var reference = new PaymobProviderReference(response.Order.Id.ToString(CultureInfo.InvariantCulture));
        var timestamp = action.LastUpdatedUtc;
        var metadata = PaymobResponseMetadata.FromPaymentPreparation(action, request, response).Values;
        var evidenceHash = PaymobHash.Create(
            response.Order.Id.ToString(CultureInfo.InvariantCulture),
            response.Order.MerchantOrderId ?? string.Empty,
            response.IntentionId,
            request.PaymentKeyRequest.AmountCents.ToString(CultureInfo.InvariantCulture),
            request.PaymentKeyRequest.Currency,
            action.CorrelationId.Value);
        var evidenceId = PaymobDeterministicId.Create(
            "paymob-payment-preparation-evidence",
            action.Id.Value.ToString("N"),
            response.Order.Id.ToString(CultureInfo.InvariantCulture),
            response.Order.MerchantOrderId ?? string.Empty,
            action.CorrelationId.Value);

        return ProviderEvidenceFactory.Create(
            new EvidenceId(evidenceId),
            action.Id,
            EvidenceType.ProviderNotification,
            EvidenceStatus.Recorded,
            new EvidenceReference(reference.Value),
            new EvidenceSource(PaymobProviderReference.ProviderName),
            new EvidenceTimestamp(timestamp),
            new EvidenceHash(evidenceHash),
            new ProviderPayloadReference($"paymob:intention:{response.IntentionId}"),
            action.CorrelationId,
            new EvidenceMetadata(metadata));
    }
}

public static class PaymobProviderErrorTranslator
{
    public static CanonicalProviderError Translate(PaymobOfficialApiException exception)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = PaymobProviderReference.ProviderName,
            ["http_status"] = ((int)exception.StatusCode).ToString(CultureInfo.InvariantCulture)
        };

        AddIfPresent(metadata, "provider_body", exception.ResponseBody);

        return new CanonicalProviderError(
            MapStatus(exception.StatusCode),
            $"PAYMOB_HTTP_{(int)exception.StatusCode}",
            FirstNonBlank(exception.ProviderMessage, $"Paymob API failed with HTTP {(int)exception.StatusCode}."),
            exception.ProviderRequestId,
            metadata);
    }

    public static CanonicalProviderError Translate(HttpRequestException exception)
    {
        return new CanonicalProviderError(
            CanonicalProviderErrorType.Network,
            "PAYMOB_NETWORK_ERROR",
            FirstNonBlank(exception.Message, "Paymob network error."),
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider"] = PaymobProviderReference.ProviderName,
                ["diagnostic"] = exception.GetType().Name
            });
    }

    public static CanonicalProviderError Validation(string code, string message)
    {
        return Create(CanonicalProviderErrorType.Validation, code, message);
    }

    public static CanonicalProviderError Configuration(string code, string message)
    {
        return Create(CanonicalProviderErrorType.Configuration, code, message);
    }

    private static CanonicalProviderError Create(CanonicalProviderErrorType type, string code, string message)
    {
        return new CanonicalProviderError(
            type,
            code,
            message,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider"] = PaymobProviderReference.ProviderName
            });
    }

    private static CanonicalProviderErrorType MapStatus(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => CanonicalProviderErrorType.Authentication,
            HttpStatusCode.Forbidden => CanonicalProviderErrorType.Authorization,
            HttpStatusCode.TooManyRequests => CanonicalProviderErrorType.RateLimited,
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => CanonicalProviderErrorType.Validation,
            _ when (int)statusCode >= 500 => CanonicalProviderErrorType.ProviderUnavailable,
            _ => CanonicalProviderErrorType.Unknown
        };
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
    }

    private static void AddIfPresent(IDictionary<string, string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value.Trim();
        }
    }
}

public sealed record PaymobPaymentAdapterResult
{
    public bool Succeeded { get; init; }
    public ProviderEvidence? Evidence { get; init; }
    public PaymobPaymentPreparationRequest? Request { get; init; }
    public PaymobPaymentPreparationResponse? ProviderResponse { get; init; }
    public CanonicalProviderError? Error { get; init; }

    private PaymobPaymentAdapterResult()
    {
    }

    public static PaymobPaymentAdapterResult Success(
        ProviderEvidence evidence,
        PaymobPaymentPreparationRequest request,
        PaymobPaymentPreparationResponse providerResponse)
    {
        return new PaymobPaymentAdapterResult
        {
            Succeeded = true,
            Evidence = evidence,
            Request = request,
            ProviderResponse = providerResponse
        };
    }

    public static PaymobPaymentAdapterResult Failed(CanonicalProviderError error)
    {
        return new PaymobPaymentAdapterResult
        {
            Succeeded = false,
            Error = error
        };
    }
}

public sealed record PaymobPaymentPreparationRequest(
    PaymobAuthRequest AuthRequest,
    PaymobOrderRegistrationRequest OrderRequest,
    PaymobPaymentKeyRequest PaymentKeyRequest,
    PaymobIdempotencyKey IdempotencyKey,
    IReadOnlyDictionary<string, string> CanonicalMetadata,
    PaymobIntentionRequest? IntentionRequest = null);

public sealed record PaymobPaymentPreparationResponse(
    PaymobAdapterAuthResponse Auth,
    PaymobOrderRegistrationResponse Order,
    PaymobAdapterPaymentKeyResponse PaymentKey,
    string IframeUrl,
    string? IntentionId = null);

public sealed record PaymobIntentionRequest(
    [property: JsonPropertyName("amount")] long Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("payment_methods")] IReadOnlyList<int> PaymentMethods,
    [property: JsonPropertyName("items")] IReadOnlyList<PaymobIntentionItem> Items,
    [property: JsonPropertyName("billing_data")] PaymobBillingData BillingData,
    [property: JsonPropertyName("customer")] PaymobIntentionCustomer Customer,
    [property: JsonPropertyName("extras")] IReadOnlyDictionary<string, string> Extras,
    [property: JsonPropertyName("special_reference")] string SpecialReference,
    [property: JsonPropertyName("expiration")] int Expiration,
    [property: JsonPropertyName("notification_url")] string NotificationUrl,
    [property: JsonPropertyName("redirection_url")] string RedirectionUrl);

public sealed record PaymobIntentionItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("amount")] long Amount,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("quantity")] int Quantity);

public sealed record PaymobIntentionCustomer(
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("email")] string Email);

public sealed record PaymobIntentionResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; init; }

    [JsonPropertyName("intention_order_id")]
    public long? IntentionOrderId { get; init; }

    [JsonPropertyName("payment_keys")]
    public IReadOnlyList<PaymobIntentionPaymentKey> PaymentKeys { get; init; } = Array.Empty<PaymobIntentionPaymentKey>();

    public long ResolveOrderId()
    {
        var orderId = IntentionOrderId ?? PaymentKeys.Select(key => key.OrderId).FirstOrDefault(value => value > 0);
        return orderId > 0
            ? orderId
            : throw new JsonException("Paymob intention response did not include an order id.");
    }
}

public sealed record PaymobIntentionPaymentKey(
    [property: JsonPropertyName("order_id")] long OrderId,
    [property: JsonPropertyName("integration")] long Integration);

public sealed record PaymobAuthRequest(
    [property: JsonPropertyName("api_key")] string ApiKey);

public sealed record PaymobAdapterAuthResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("profile")] PaymobMerchantProfileResponse? Profile = null);

public sealed record PaymobMerchantProfileResponse
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("merchant_id")]
    public long? MerchantId { get; init; }
}

public sealed record PaymobOrderRegistrationRequest(
    [property: JsonPropertyName("auth_token")] string AuthToken,
    [property: JsonPropertyName("delivery_needed")] string DeliveryNeeded,
    [property: JsonPropertyName("amount_cents")] long AmountCents,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("merchant_order_id")] string MerchantOrderId,
    [property: JsonPropertyName("items")] IReadOnlyList<PaymobOrderItem> Items);

public sealed record PaymobOrderItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("amount_cents")] long AmountCents,
    [property: JsonPropertyName("quantity")] int Quantity);

public sealed record PaymobOrderRegistrationResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("merchant_order_id")] string? MerchantOrderId);

public sealed record PaymobPaymentKeyRequest(
    [property: JsonPropertyName("auth_token")] string AuthToken,
    [property: JsonPropertyName("amount_cents")] long AmountCents,
    [property: JsonPropertyName("expiration")] int Expiration,
    [property: JsonPropertyName("order_id")] string OrderId,
    [property: JsonPropertyName("billing_data")] PaymobBillingData BillingData,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("integration_id")] int IntegrationId,
    [property: JsonPropertyName("lock_order_when_paid")] bool LockOrderWhenPaid);

public sealed record PaymobAdapterPaymentKeyResponse(
    [property: JsonPropertyName("token")] string Token);

public sealed record PaymobBillingData(
    [property: JsonPropertyName("apartment")] string Apartment,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("floor")] string Floor,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("street")] string Street,
    [property: JsonPropertyName("building")] string Building,
    [property: JsonPropertyName("phone_number")] string PhoneNumber,
    [property: JsonPropertyName("shipping_method")] string ShippingMethod,
    [property: JsonPropertyName("postal_code")] string PostalCode,
    [property: JsonPropertyName("city")] string City,
    [property: JsonPropertyName("country")] string Country,
    [property: JsonPropertyName("state")] string State)
{
    public static PaymobBillingData FromActionMetadata(IReadOnlyDictionary<string, string> values)
    {
        return new PaymobBillingData(
            RequiredOrDefault(values, PaymobRequestMetadataKeys.Apartment),
            RequiredOrDefault(values, PaymobRequestMetadataKeys.Email, "customer@example.com"),
            RequiredOrDefault(values, PaymobRequestMetadataKeys.Floor),
            RequiredOrDefault(values, PaymobRequestMetadataKeys.FirstName, "Customer"),
            RequiredOrDefault(values, PaymobRequestMetadataKeys.LastName, "Customer"),
            RequiredOrDefault(values, PaymobRequestMetadataKeys.Street),
            RequiredOrDefault(values, PaymobRequestMetadataKeys.Building),
            RequiredOrDefault(values, PaymobRequestMetadataKeys.PhoneNumber, "NA"),
            RequiredOrDefault(values, PaymobRequestMetadataKeys.ShippingMethod),
            RequiredOrDefault(values, PaymobRequestMetadataKeys.PostalCode),
            RequiredOrDefault(values, PaymobRequestMetadataKeys.City),
            NormalizeCountry(RequiredOrDefault(values, PaymobRequestMetadataKeys.Country, "EG")),
            RequiredOrDefault(values, PaymobRequestMetadataKeys.State));
    }

    private static string NormalizeCountry(string value) =>
        string.Equals(value, "EG", StringComparison.OrdinalIgnoreCase)
            ? "EGY"
            : value.ToUpperInvariant();

    private static string RequiredOrDefault(
        IReadOnlyDictionary<string, string> values,
        string key,
        string fallback = "NA")
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }
}

public sealed class PaymobOfficialApiException : Exception
{
    public PaymobOfficialApiException(
        HttpStatusCode statusCode,
        string responseBody,
        string? providerRequestId)
        : base($"Paymob API request failed with HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        ProviderRequestId = providerRequestId;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
    public string? ProviderRequestId { get; }
    public string? ProviderMessage => ExtractProviderMessage(ResponseBody);

    private static string? ExtractProviderMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }

            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString();
            }
        }
        catch (JsonException)
        {
            return responseBody;
        }

        return responseBody;
    }
}

public sealed record PaymobProviderReference(string Value)
{
    public const string ProviderName = "paymob";
}

public sealed record PaymobIdempotencyKey(string Value)
{
    public static PaymobIdempotencyKey Build(PaymentAction action)
    {
        if (action.Metadata.Values.TryGetValue(PaymobRequestMetadataKeys.MerchantOrderId, out var supplied) &&
            !string.IsNullOrWhiteSpace(supplied))
        {
            return new PaymobIdempotencyKey(supplied.Trim());
        }

        var canonicalSource = string.Join(
            "|",
            "paymob",
            "order_registration",
            action.SessionId.Value.ToString("N"),
            action.AttemptId.Value.ToString("N"),
            action.Id.Value.ToString("N"),
            action.Type.ToString(),
            action.Reference.Value,
            action.CorrelationId.Value);
        var hash = PaymobHash.Create(canonicalSource);

        return new PaymobIdempotencyKey($"paymob:{action.Id.Value:N}:{hash[..32]}");
    }
}

public sealed record PaymobRequestMetadata(IReadOnlyDictionary<string, string> Values)
{
    public static PaymobRequestMetadata FromAction(PaymentAction action)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = PaymobProviderReference.ProviderName,
            ["payment_action_id"] = action.Id.Value.ToString("N"),
            ["payment_session_id"] = action.SessionId.Value.ToString("N"),
            ["payment_attempt_id"] = action.AttemptId.Value.ToString("N"),
            ["payment_action_type"] = action.Type.ToString(),
            ["correlation_id"] = action.CorrelationId.Value,
            ["action_reference"] = action.Reference.Value
        };

        foreach (var (key, value) in action.Metadata.Values)
        {
            if (!PaymobRequestMetadataKeys.AdapterOnlyKeys.Contains(key))
            {
                values[$"canonical_{key}"] = value;
            }
        }

        return new PaymobRequestMetadata(Normalize(values));
    }

    private static IReadOnlyDictionary<string, string> Normalize(IDictionary<string, string> values)
    {
        return values
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.Ordinal);
    }
}

public sealed record PaymobResponseMetadata(IReadOnlyDictionary<string, string> Values)
{
    public static PaymobResponseMetadata FromPaymentPreparation(
        PaymentAction action,
        PaymobPaymentPreparationRequest request,
        PaymobPaymentPreparationResponse response)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = PaymobProviderReference.ProviderName,
            ["provider_reference"] = response.Order.Id.ToString(CultureInfo.InvariantCulture),
            ["provider_order_id"] = response.Order.Id.ToString(CultureInfo.InvariantCulture),
            ["provider_merchant_order_id"] = response.Order.MerchantOrderId ?? string.Empty,
            ["provider_status"] = "intention_created",
            ["transaction_reference"] = response.IntentionId ?? string.Empty,
            ["external_status"] = "intention_created",
            ["iframe_url"] = response.IframeUrl,
            ["correlation_id"] = action.CorrelationId.Value,
            ["payment_action_id"] = action.Id.Value.ToString("N"),
            ["amount_cents"] = request.PaymentKeyRequest.AmountCents.ToString(CultureInfo.InvariantCulture),
            ["currency"] = request.PaymentKeyRequest.Currency,
            ["idempotency_key"] = request.IdempotencyKey.Value
        };

        foreach (var (key, value) in request.CanonicalMetadata)
        {
            values[$"canonical_{key}"] = value;
        }

        return new PaymobResponseMetadata(values);
    }
}

public static class PaymobRedirectFactory
{
    private static readonly Uri EgyptUnifiedCheckoutUri = new("https://accept.paymob.com/unifiedcheckout/");

    public static string CreateUnifiedCheckoutUrl(string publicKey, string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            throw new InvalidOperationException("Paymob public key is required.");
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ArgumentException("Paymob intention client secret is required.", nameof(clientSecret));
        }

        return $"{EgyptUnifiedCheckoutUri}?publicKey={Uri.EscapeDataString(publicKey.Trim())}&clientSecret={Uri.EscapeDataString(clientSecret.Trim())}";
    }

    public static string CreateIframeUrl(string iframeId, string paymentKey)
    {
        if (string.IsNullOrWhiteSpace(iframeId))
        {
            throw new InvalidOperationException("Paymob iframe ID is required.");
        }

        if (string.IsNullOrWhiteSpace(paymentKey))
        {
            throw new ArgumentException("Paymob payment key is required.", nameof(paymentKey));
        }

        return $"https://accept.paymob.com/api/acceptance/iframes/{iframeId.Trim()}?payment_token={Uri.EscapeDataString(paymentKey)}";
    }
}

public static class PaymobRequestMetadataKeys
{
    public const string Amount = "Amount";
    public const string Currency = "Currency";
    public const string MerchantOrderId = "MerchantOrderId";
    public const string ItemName = "ItemName";
    public const string ExpirationSeconds = "ExpirationSeconds";
    public const string LockOrderWhenPaid = "LockOrderWhenPaid";
    public const string Apartment = "Apartment";
    public const string Email = "Email";
    public const string Floor = "Floor";
    public const string FirstName = "FirstName";
    public const string LastName = "LastName";
    public const string Street = "Street";
    public const string Building = "Building";
    public const string PhoneNumber = "PhoneNumber";
    public const string ShippingMethod = "ShippingMethod";
    public const string PostalCode = "PostalCode";
    public const string City = "City";
    public const string Country = "Country";
    public const string State = "State";

    public static readonly ISet<string> AdapterOnlyKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        Amount,
        Currency,
        MerchantOrderId,
        ItemName,
        ExpirationSeconds,
        LockOrderWhenPaid,
        Apartment,
        Email,
        Floor,
        FirstName,
        LastName,
        Street,
        Building,
        PhoneNumber,
        ShippingMethod,
        PostalCode,
        City,
        Country,
        State
    };
}

internal static class PaymobMoneyMapper
{
    public static long ToMinorUnits(string amount, string currency)
    {
        if (!string.Equals(currency?.Trim(), "EGP", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The configured Paymob Egypt integration only supports EGP checkout currency.");
        }

        if (!decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException("Canonical amount must be an invariant decimal value.");
        }

        if (parsed <= 0m)
        {
            throw new ArgumentException("Canonical amount must be positive.");
        }

        var minor = parsed * 100m;
        if (decimal.Truncate(minor) != minor)
        {
            throw new ArgumentException($"Canonical amount has more precision than Paymob supports for {currency}.");
        }

        return checked((long)minor);
    }
}

internal static class PaymobDeterministicId
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

internal static class PaymobHash
{
    public static string Create(params string[] components)
    {
        var source = string.Join('|', components);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }
}
