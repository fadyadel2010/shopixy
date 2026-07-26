using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.Payments.ProviderContracts;

namespace PointOfSale.API.Services.Payments.Paymob;

public sealed class PaymobAdapter : IPaymentProviderAdapter
{
    private readonly IPaymobOfficialApiClient _client;
    private readonly CommerceHubPaymobOptions _options;
    private readonly IPaymobCapabilityProvider _capabilityProvider;
    private readonly PaymobOperationalTelemetry _telemetry;
    private readonly ILogger<PaymobAdapter> _logger;

    public PaymobAdapter(
        IPaymobOfficialApiClient client,
        IOptions<CommerceHubPaymobOptions> options,
        IPaymobCapabilityProvider capabilityProvider,
        PaymobOperationalTelemetry telemetry,
        ILogger<PaymobAdapter> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options.Value;
        _capabilityProvider = capabilityProvider ?? throw new ArgumentNullException(nameof(capabilityProvider));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ProviderCode ProviderCode { get; } = new(CommerceHubPaymentMethods.Paymob);

    public PaymentProviderCapabilities Capabilities
    {
        get
        {
            var report = _capabilityProvider.GetCapabilities();
            return new PaymentProviderCapabilities(
                SupportsAuthentication: true,
                SupportsPaymentIntentionCreation: report.SupportedCapabilities.Contains(PaymobCanonicalCapability.Authorize),
                SupportsPaymentSessionCreation: report.SupportedCapabilities.Contains(PaymobCanonicalCapability.Authorize),
                SupportsHostedCheckout: true,
                SupportsPaymentStatusLookup: report.SupportedCapabilities.Contains(PaymobCanonicalCapability.StatusInquiry),
                SupportsWebhookVerification: report.SupportedCapabilities.Contains(PaymobCanonicalCapability.HmacVerification),
                SupportsCallbackParsing: report.SupportedCapabilities.Contains(PaymobCanonicalCapability.WebhookProcessing),
                SupportsRefunds: report.SupportedCapabilities.Contains(PaymobCanonicalCapability.Refund),
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["provider"] = PaymobProviderReference.ProviderName,
                    ["environment"] = _options.Environment.ToString(),
                    ["sandbox"] = _options.SandboxMode.ToString(CultureInfo.InvariantCulture),
                    ["base_url"] = _options.BaseUrl.ToString(),
                    ["runtime_capabilities"] = report.RuntimeCapabilities.ToString(),
                    ["runtime_constraints"] = report.RuntimeConstraints.ToString()
                });
        }
    }

    public async Task<PaymentProviderSessionResult> CreateSessionAsync(
        PaymentProviderSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsurePaymentConfiguration();

        var stopwatch = Stopwatch.StartNew();
        _telemetry.RecordPaymentCreateAttempt(Guid.Empty, "commercehub");
        _logger.LogInformation(
            "Paymob provider session creation started | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | HasExistingProviderIntentId: {HasExistingProviderIntentId} | CorrelationId: {CorrelationId}",
            request.PaymentId,
            request.MarketplaceOrderId,
            !string.IsNullOrWhiteSpace(request.ExistingProviderIntentId),
            request.CorrelationId);

        string? providerOrderId = null;
        try
        {
            _logger.LogInformation(
                "PaymobAuthenticationStarted | CorrelationId: {CorrelationId}",
                request.CorrelationId);
            var auth = await _client.AuthenticateAsync(
                new PaymobAuthRequest(_options.ApiKey),
                cancellationToken);
            if (string.IsNullOrWhiteSpace(auth.Token))
            {
                throw new PaymentProviderAdapterException(ConfigurationError(
                    "PAYMOB_AUTH_TOKEN_MISSING",
                    "Paymob authentication did not return a token."));
            }

            _logger.LogInformation(
                "PaymobAuthenticationSucceeded | CorrelationId: {CorrelationId}",
                request.CorrelationId);

            var amountCents = ConvertToMinorUnits(request.Amount, request.Currency);
            if (string.IsNullOrWhiteSpace(request.ExistingProviderIntentId))
            {
                var orderResponse = await _client.RegisterOrderAsync(
                    BuildOrderRequest(request, amountCents),
                    auth.Token,
                    cancellationToken);
                if (orderResponse.Id <= 0)
                {
                    throw new PaymentProviderAdapterException(ProviderError(
                        CanonicalProviderErrorType.ProviderRejected,
                        "PAYMOB_ORDER_ID_MISSING",
                        "Paymob order registration did not return an order id."));
                }

                providerOrderId = orderResponse.Id.ToString(CultureInfo.InvariantCulture);
                _logger.LogInformation(
                    "PaymentIntentionCreated | ProviderOrderId: {ProviderOrderId} | CorrelationId: {CorrelationId}",
                    providerOrderId,
                    request.CorrelationId);
            }
            else
            {
                providerOrderId = request.ExistingProviderIntentId.Trim();
            }

            PaymobAdapterPaymentKeyResponse paymentKey;
            try
            {
                paymentKey = await _client.CreatePaymentKeyAsync(
                    BuildPaymentKeyRequest(request, providerOrderId, amountCents),
                    auth.Token,
                    cancellationToken);
            }
            catch (Exception exception) when (IsInfrastructureFailure(exception) && !string.IsNullOrWhiteSpace(providerOrderId))
            {
                throw new PaymentProviderAdapterException(
                    TranslateException(exception),
                    outcomeUnknown: true,
                    providerIntentId: providerOrderId,
                    innerException: exception);
            }

            if (string.IsNullOrWhiteSpace(paymentKey.Token))
            {
                throw new PaymentProviderAdapterException(ProviderError(
                    CanonicalProviderErrorType.ProviderRejected,
                    "PAYMOB_PAYMENT_KEY_MISSING",
                    "Paymob payment session creation did not return a payment key."));
            }

            var iframeUrl = CreateIframeUrl(_options.BaseUrl, _options.IframeId, paymentKey.Token);
            var rawResponse = JsonSerializer.Serialize(new
            {
                paymobOrderId = providerOrderId,
                paymentKey = paymentKey.Token,
                iframeUrl,
                recovered = !string.IsNullOrWhiteSpace(request.ExistingProviderIntentId)
            });

            _telemetry.RecordPaymentCreateSuccess(Guid.Empty, "commercehub");
            _telemetry.RecordProviderRequestLatency("create_session", stopwatch.Elapsed);
            _logger.LogInformation(
                "HostedCheckoutCreated | ProviderOrderId: {ProviderOrderId} | DurationMs: {DurationMs} | CorrelationId: {CorrelationId}",
                providerOrderId,
                stopwatch.ElapsedMilliseconds,
                request.CorrelationId);

            return new PaymentProviderSessionResult(
                iframeUrl,
                paymentKey.Token,
                providerOrderId,
                rawResponse);
        }
        catch (PaymentProviderAdapterException exception)
        {
            _telemetry.RecordProviderFailure(Guid.Empty, "create_session", exception.Error.Code);
            _telemetry.RecordProviderRequestLatency("create_session", stopwatch.Elapsed);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var error = TranslateException(exception);
            _telemetry.RecordProviderFailure(Guid.Empty, "create_session", error.Code);
            _telemetry.RecordProviderRequestLatency("create_session", stopwatch.Elapsed);
            throw new PaymentProviderAdapterException(error, innerException: exception);
        }
    }

    public Task<PaymentProviderWebhookResult> ParseWebhookAsync(
        PaymentProviderWebhookRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        EnsureWebhookConfiguration();
        var verification = PaymobWebhookHmacVerifier.Verify(new PaymobWebhookEvidenceRequest
        {
            RawPayload = request.RawPayload,
            HmacSignature = request.Signature,
            HmacSecret = PaymobOptionsValidator.ResolveWebhookSecret(_options)
        });

        if (!verification.Succeeded)
        {
            _telemetry.RecordProviderFailure(Guid.Empty, "parse_webhook", verification.Error?.Code ?? "PAYMOB_WEBHOOK_INVALID");
            _logger.LogWarning(
                "WebhookRejected | ReasonCode: {ReasonCode} | CorrelationId: {CorrelationId}",
                verification.Error?.Code,
                request.CorrelationId);
            throw new PaymentProviderAdapterException(verification.Error ?? ValidationError(
                "PAYMOB_WEBHOOK_INVALID",
                "Paymob webhook verification failed."));
        }

        var callback = verification.Callback!;
        _logger.LogInformation(
            "WebhookValidated | TransactionId: {TransactionId} | EventType: {EventType} | CorrelationId: {CorrelationId}",
            callback.TransactionId,
            callback.EventType,
            request.CorrelationId);

        var providerError = PaymobWebhookErrorTranslator.TranslateProviderFailure(callback);
        return Task.FromResult(new PaymentProviderWebhookResult
        {
            EventType = callback.EventType,
            TransactionId = callback.TransactionId,
            ProviderReference = callback.MerchantOrderId,
            ProviderIntentId = callback.OrderId.ToString(CultureInfo.InvariantCulture),
            Pending = callback.Payload.Pending,
            Success = callback.Payload.Success,
            IsVoided = callback.Payload.IsVoided,
            IsRefunded = callback.Payload.IsRefunded,
            AmountMinorUnits = callback.Payload.AmountCents ?? 0L,
            Currency = callback.Payload.Currency ?? string.Empty,
            FailureReason = providerError?.Message
        });
    }

    public Task<PaymentProviderCallbackResult> ParseCallbackAsync(
        PaymentProviderCallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        EnsureWebhookConfiguration();

        request.Parameters.TryGetValue("hmac", out var hmacSignature);
        request.Parameters.TryGetValue("id", out var transactionId);

        if (string.IsNullOrWhiteSpace(hmacSignature))
        {
            return Task.FromResult(new PaymentProviderCallbackResult
            {
                IsValid = false,
                IsSuccess = false,
                TransactionId = transactionId,
                Message = "Missing callback signature."
            });
        }

        var rawPayload = string.Join(
            "&",
            request.Parameters
                .Where(parameter => !string.Equals(parameter.Key, "hmac", StringComparison.OrdinalIgnoreCase))
                .Select(parameter =>
                    $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));

        var verification = PaymobWebhookHmacVerifier.Verify(new PaymobWebhookEvidenceRequest
        {
            RawPayload = rawPayload,
            HmacSignature = hmacSignature,
            HmacSecret = PaymobOptionsValidator.ResolveWebhookSecret(_options)
        });

        if (!verification.Succeeded)
        {
            _telemetry.RecordProviderFailure(Guid.Empty, "parse_callback", verification.Error?.Code ?? "PAYMOB_CALLBACK_INVALID");
            return Task.FromResult(new PaymentProviderCallbackResult
            {
                IsValid = false,
                IsSuccess = false,
                TransactionId = transactionId,
                Message = verification.Error?.Message ?? "Invalid callback signature."
            });
        }

        var callback = verification.Callback!;
        return Task.FromResult(new PaymentProviderCallbackResult
        {
            IsValid = true,
            IsSuccess = callback.Payload.Success,
            TransactionId = callback.TransactionId,
            ProviderIntentId = callback.OrderId.ToString(CultureInfo.InvariantCulture),
            ProviderReference = callback.MerchantOrderId,
            Message = "Payment callback verified."
        });
    }

    public async Task<PaymentProviderStatusResult> GetStatusAsync(
        PaymentProviderStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsurePaymentConfiguration();

        try
        {
            var auth = await _client.AuthenticateAsync(new PaymobAuthRequest(_options.ApiKey), cancellationToken);
            var response = await _client.RetrieveTransactionAsync(
                request.ProviderTransactionId,
                auth.Token,
                cancellationToken);

            return new PaymentProviderStatusResult
            {
                ProviderTransactionId = response.Id?.ToString(CultureInfo.InvariantCulture) ?? request.ProviderTransactionId,
                ProviderIntentId = response.Order?.Id?.ToString(CultureInfo.InvariantCulture),
                Pending = response.Pending,
                Success = response.Success,
                IsVoided = response.IsVoided,
                IsRefunded = response.IsRefunded,
                AmountMinorUnits = response.AmountCents ?? 0L,
                Currency = response.Currency ?? string.Empty
            };
        }
        catch (PaymentProviderAdapterException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new PaymentProviderAdapterException(TranslateException(exception), innerException: exception);
        }
    }

    private PaymobOrderRegistrationRequest BuildOrderRequest(
        PaymentProviderSessionRequest request,
        long amountCents)
    {
        return new PaymobOrderRegistrationRequest(
            AuthToken: string.Empty,
            DeliveryNeeded: "false",
            AmountCents: amountCents,
            Currency: NormalizeCurrency(request.Currency),
            MerchantOrderId: Required(request.ProviderReference, "provider reference"),
            Items:
            [
                new PaymobOrderItem(
                    $"CommerceHub {Required(request.OrderNumber, "order number")}",
                    amountCents,
                    1)
            ]);
    }

    private PaymobPaymentKeyRequest BuildPaymentKeyRequest(
        PaymentProviderSessionRequest request,
        string providerOrderId,
        long amountCents)
    {
        if (!int.TryParse(_options.IntegrationId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integrationId) || integrationId <= 0)
        {
            throw new PaymentProviderAdapterException(ConfigurationError(
                "PAYMOB_CONFIG_INVALID",
                "CommerceHub Paymob integration ID is invalid."));
        }

        var (firstName, lastName) = SplitFullName(request.CustomerName);
        return new PaymobPaymentKeyRequest(
            AuthToken: string.Empty,
            AmountCents: amountCents,
            Expiration: 3600,
            OrderId: Required(providerOrderId, "provider order id"),
            BillingData: new PaymobBillingData(
                Apartment: "NA",
                Email: FirstNonBlank(request.Email, "customer@example.com"),
                Floor: "NA",
                FirstName: firstName,
                LastName: lastName,
                Street: FirstNonBlank(request.AddressLine, "NA"),
                Building: "NA",
                PhoneNumber: FirstNonBlank(request.PhoneNumber, "+20000000000"),
                ShippingMethod: "NA",
                PostalCode: "NA",
                City: FirstNonBlank(request.City, "NA"),
                Country: Required(request.CountryCode, "country code"),
                State: FirstNonBlank(request.Area, request.City, "NA")),
            Currency: NormalizeCurrency(request.Currency),
            IntegrationId: integrationId,
            LockOrderWhenPaid: true);
    }

    private void EnsurePaymentConfiguration()
    {
        if (!_options.Enabled ||
            string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.IntegrationId) ||
            string.IsNullOrWhiteSpace(_options.IframeId))
        {
            throw new PaymentProviderAdapterException(ConfigurationError(
                "PAYMOB_NOT_CONFIGURED",
                "CommerceHub Paymob integration is not configured."));
        }
    }

    private void EnsureWebhookConfiguration()
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(PaymobOptionsValidator.ResolveWebhookSecret(_options)))
        {
            throw new PaymentProviderAdapterException(ConfigurationError(
                "PAYMOB_WEBHOOK_NOT_CONFIGURED",
                "CommerceHub Paymob webhook verification is not configured."));
        }
    }

    private static string CreateIframeUrl(Uri baseUrl, string iframeId, string paymentKey)
    {
        var normalizedBase = baseUrl.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUrl
            : new Uri(baseUrl.AbsoluteUri + "/");
        return new Uri(
            normalizedBase,
            $"acceptance/iframes/{Uri.EscapeDataString(Required(iframeId, "iframe id"))}?payment_token={Uri.EscapeDataString(Required(paymentKey, "payment key"))}")
            .ToString();
    }

    private static long ConvertToMinorUnits(decimal amount, string currency)
    {
        if (amount <= 0m)
        {
            throw new PaymentProviderAdapterException(ValidationError(
                "PAYMOB_AMOUNT_INVALID",
                "Paymob payment amount must be positive."));
        }

        var minor = amount * 100m;
        if (decimal.Truncate(minor) != minor)
        {
            throw new PaymentProviderAdapterException(ValidationError(
                "PAYMOB_AMOUNT_PRECISION_INVALID",
                $"Paymob amount has more precision than supported for {currency}."));
        }

        return checked((long)minor);
    }

    private static string NormalizeCurrency(string currency)
    {
        var normalized = Required(currency, "currency").ToUpperInvariant();
        if (normalized.Length != 3)
        {
            throw new PaymentProviderAdapterException(ValidationError(
                "PAYMOB_CURRENCY_INVALID",
                "Paymob currency must be a three-letter ISO code."));
        }

        return normalized;
    }

    private static string Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new PaymentProviderAdapterException(ValidationError(
                "PAYMOB_REQUEST_INVALID",
                $"Paymob {name} is required."));
        }

        return value.Trim();
    }

    private static (string FirstName, string LastName) SplitFullName(string? fullName)
    {
        var normalized = string.IsNullOrWhiteSpace(fullName)
            ? "CommerceHub Customer"
            : fullName.Trim();
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return ("CommerceHub", "Customer");
        }

        return parts.Length == 1 ? (parts[0], parts[0]) : (parts[0], parts[^1]);
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static bool IsInfrastructureFailure(Exception exception) =>
        exception is PaymobOfficialApiException or HttpRequestException or TaskCanceledException or JsonException;

    private static CanonicalProviderError TranslateException(Exception exception)
    {
        return exception switch
        {
            PaymentProviderAdapterException adapterException => adapterException.Error,
            PaymobOfficialApiException official => PaymobProviderErrorTranslator.Translate(official),
            HttpRequestException http => PaymobProviderErrorTranslator.Translate(http),
            JsonException json => ValidationError("PAYMOB_RESPONSE_INVALID", json.Message),
            TaskCanceledException timeout => ProviderError(CanonicalProviderErrorType.Network, "PAYMOB_TIMEOUT", timeout.Message),
            ArgumentException argument => ValidationError("PAYMOB_REQUEST_INVALID", argument.Message),
            InvalidOperationException invalid => ConfigurationError("PAYMOB_ADAPTER_INVALID", invalid.Message),
            _ => ProviderError(CanonicalProviderErrorType.Unknown, "PAYMOB_PROVIDER_FAILURE", exception.Message)
        };
    }

    private static CanonicalProviderError ValidationError(string code, string message) =>
        CanonicalProviderError.Validation(PaymobProviderReference.ProviderName, code, message);

    private static CanonicalProviderError ConfigurationError(string code, string message) =>
        CanonicalProviderError.Configuration(PaymobProviderReference.ProviderName, code, message);

    private static CanonicalProviderError ProviderError(
        CanonicalProviderErrorType type,
        string code,
        string message) =>
        CanonicalProviderError.Create(PaymobProviderReference.ProviderName, type, code, message);
}
