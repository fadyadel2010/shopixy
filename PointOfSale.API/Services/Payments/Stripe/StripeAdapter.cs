using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.Payments.ProviderContracts;
using Stripe;
using ProviderError = PointOfSale.API.Services.Payments.ProviderContracts.CanonicalProviderError;
using ProviderErrorType = PointOfSale.API.Services.Payments.ProviderContracts.CanonicalProviderErrorType;
using StripeEvent = Stripe.Event;
using StripePaymentIntent = Stripe.PaymentIntent;

namespace PointOfSale.API.Services.Payments.Stripe;

public sealed class StripeAdapter : IPaymentProviderAdapter
{
    private readonly CommerceHubPaymentsOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StripeAdapter> _logger;

    public StripeAdapter(
        IOptions<CommerceHubPaymentsOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<StripeAdapter> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public ProviderCode ProviderCode { get; } = new(CommerceHubPaymentMethods.Stripe);

    public PaymentProviderCapabilities Capabilities => new(
        SupportsAuthentication: true,
        SupportsPaymentIntentionCreation: true,
        SupportsPaymentSessionCreation: true,
        SupportsHostedCheckout: false,
        SupportsPaymentStatusLookup: true,
        SupportsWebhookVerification: true,
        SupportsCallbackParsing: false,
        SupportsRefunds: true,
        Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = "stripe",
            ["handoff"] = "client_secret"
        });

    public async Task<PaymentProviderSessionResult> CreateSessionAsync(
        PaymentProviderSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsurePaymentConfiguration();

        try
        {
            var service = CreatePaymentIntentService();
            StripePaymentIntent paymentIntent;
            var recovered = !string.IsNullOrWhiteSpace(request.ExistingProviderIntentId);

            if (recovered)
            {
                paymentIntent = await service.GetAsync(
                    request.ExistingProviderIntentId,
                    requestOptions: CreateRequestOptions(idempotencyKey: null),
                    cancellationToken: cancellationToken);
            }
            else
            {
                paymentIntent = await service.CreateAsync(
                    BuildPaymentIntentCreateOptions(request),
                    requestOptions: CreateRequestOptions(request.PaymentId.ToString()),
                    cancellationToken: cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(paymentIntent.Id) ||
                string.IsNullOrWhiteSpace(paymentIntent.ClientSecret))
            {
                throw new PaymentProviderAdapterException(ProviderError.Validation(
                    "stripe",
                    "STRIPE_PAYMENT_INTENT_INCOMPLETE",
                    "Stripe PaymentIntent response was missing required session artifacts."));
            }

            var rawResponse = JsonSerializer.Serialize(new
            {
                stripePaymentIntentId = paymentIntent.Id,
                clientSecret = paymentIntent.ClientSecret,
                publishableKey = _options.Stripe.PublishableKey,
                recovered
            });

            _logger.LogInformation(
                "Stripe provider session created | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | StripePaymentIntentId: {StripePaymentIntentId} | CorrelationId: {CorrelationId}",
                request.PaymentId,
                request.MarketplaceOrderId,
                paymentIntent.Id,
                request.CorrelationId);

            return new PaymentProviderSessionResult(
                PaymentUrl: null,
                PaymentToken: paymentIntent.ClientSecret,
                ProviderIntentId: paymentIntent.Id,
                RawResponse: rawResponse);
        }
        catch (PaymentProviderAdapterException)
        {
            throw;
        }
        catch (StripeException exception)
        {
            throw new PaymentProviderAdapterException(Translate(exception), innerException: exception);
        }
    }

    public Task<PaymentProviderWebhookResult> ParseWebhookAsync(
        PaymentProviderWebhookRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWebhookConfiguration();

        StripeEvent stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                request.RawPayload,
                request.Signature,
                _options.Stripe.WebhookSecret);
        }
        catch (StripeException exception)
        {
            throw new PaymentProviderAdapterException(TranslateWebhook(exception), innerException: exception);
        }
        catch (JsonException exception)
        {
            throw new PaymentProviderAdapterException(ProviderError.Validation(
                "stripe",
                "STRIPE_WEBHOOK_MALFORMED_PAYLOAD",
                exception.Message), innerException: exception);
        }

        return Task.FromResult(TranslateWebhook(stripeEvent));
    }

    public async Task<PaymentProviderStatusResult> GetStatusAsync(
        PaymentProviderStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsurePaymentConfiguration();

        try
        {
            var service = CreatePaymentIntentService();
            var paymentIntent = await service.GetAsync(
                request.ProviderTransactionId,
                requestOptions: CreateRequestOptions(idempotencyKey: null),
                cancellationToken: cancellationToken);

            return new PaymentProviderStatusResult
            {
                ProviderTransactionId = paymentIntent.LatestChargeId ?? paymentIntent.Id ?? request.ProviderTransactionId,
                ProviderIntentId = paymentIntent.Id,
                Pending = IsPending(paymentIntent.Status),
                Success = string.Equals(paymentIntent.Status, "succeeded", StringComparison.Ordinal),
                AmountMinorUnits = paymentIntent.AmountReceived > 0 ? paymentIntent.AmountReceived : paymentIntent.Amount,
                Currency = paymentIntent.Currency ?? string.Empty,
                IsRefunded = false,
                IsVoided = string.Equals(paymentIntent.Status, "canceled", StringComparison.Ordinal)
            };
        }
        catch (StripeException exception)
        {
            throw new PaymentProviderAdapterException(Translate(exception), innerException: exception);
        }
    }

    private PaymentIntentService CreatePaymentIntentService()
    {
        var stripeOptions = _options.Stripe;
        var httpClient = _httpClientFactory.CreateClient(StripeFoundationServiceCollectionExtensions.HttpClientName);
        var stripeClient = new StripeClient(
            stripeOptions.SecretKey,
            httpClient: new SystemNetHttpClient(httpClient));

        return new PaymentIntentService(stripeClient);
    }

    private PaymentIntentCreateOptions BuildPaymentIntentCreateOptions(PaymentProviderSessionRequest request)
    {
        return new PaymentIntentCreateOptions
        {
            Amount = ConvertToMinorUnits(request.Amount),
            Currency = RequiredCurrency(request.Currency).ToLowerInvariant(),
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true
            },
            Metadata = new Dictionary<string, string>
            {
                ["paymentId"] = request.PaymentId.ToString(),
                ["providerReference"] = request.ProviderReference,
                ["marketplaceOrderId"] = request.MarketplaceOrderId.ToString(),
                ["orderNumber"] = request.OrderNumber
            },
            Description = $"CommerceHub {request.OrderNumber}"
        };
    }

    private RequestOptions CreateRequestOptions(string? idempotencyKey)
    {
        return new RequestOptions
        {
            ApiKey = _options.Stripe.SecretKey,
            IdempotencyKey = idempotencyKey
        };
    }

    private PaymentProviderWebhookResult TranslateWebhook(StripeEvent stripeEvent)
    {
        return stripeEvent.Data.Object switch
        {
            StripePaymentIntent paymentIntent => FromPaymentIntent(stripeEvent, paymentIntent),
            Charge charge when string.Equals(stripeEvent.Type, "charge.refunded", StringComparison.Ordinal) => FromRefundedCharge(stripeEvent, charge),
            _ => new PaymentProviderWebhookResult
            {
                EventType = stripeEvent.Type,
                TransactionId = stripeEvent.Id,
                Pending = false,
                Success = false,
                AmountMinorUnits = 0L,
                Currency = string.Empty,
                FailureReason = $"Unsupported Stripe event payload '{stripeEvent.Type}'."
            }
        };
    }

    private static PaymentProviderWebhookResult FromPaymentIntent(
        StripeEvent stripeEvent,
        StripePaymentIntent paymentIntent)
    {
        paymentIntent.Metadata.TryGetValue("providerReference", out var providerReference);

        return new PaymentProviderWebhookResult
        {
            EventType = stripeEvent.Type,
            TransactionId = paymentIntent.LatestChargeId ?? paymentIntent.Id ?? stripeEvent.Id,
            ProviderReference = providerReference,
            ProviderIntentId = paymentIntent.Id,
            Pending = IsPending(paymentIntent.Status),
            Success = string.Equals(stripeEvent.Type, "payment_intent.succeeded", StringComparison.Ordinal),
            IsVoided = string.Equals(stripeEvent.Type, "payment_intent.canceled", StringComparison.Ordinal),
            IsRefunded = false,
            AmountMinorUnits = paymentIntent.AmountReceived > 0 ? paymentIntent.AmountReceived : paymentIntent.Amount,
            Currency = paymentIntent.Currency ?? string.Empty,
            FailureReason = paymentIntent.LastPaymentError?.Message
        };
    }

    private static PaymentProviderWebhookResult FromRefundedCharge(
        StripeEvent stripeEvent,
        Charge charge)
    {
        charge.Metadata.TryGetValue("providerReference", out var providerReference);
        var latestRefund = charge.Refunds?.Data?
            .OrderByDescending(refund => refund.Created)
            .FirstOrDefault();
        var refundAmount = latestRefund?.Amount ?? charge.AmountRefunded;

        return new PaymentProviderWebhookResult
        {
            EventType = stripeEvent.Type,
            TransactionId = charge.Id ?? stripeEvent.Id,
            ProviderReference = providerReference,
            ProviderIntentId = charge.PaymentIntentId,
            Pending = false,
            Success = false,
            IsVoided = false,
            IsRefunded = true,
            AmountMinorUnits = refundAmount,
            Currency = charge.Currency ?? string.Empty
        };
    }

    private void EnsurePaymentConfiguration()
    {
        if (!_options.Stripe.Enabled ||
            string.IsNullOrWhiteSpace(_options.Stripe.SecretKey) ||
            string.IsNullOrWhiteSpace(_options.Stripe.PublishableKey))
        {
            throw new PaymentProviderAdapterException(ProviderError.Configuration(
                "stripe",
                "STRIPE_NOT_CONFIGURED",
                "CommerceHub Stripe integration is not configured."));
        }
    }

    private void EnsureWebhookConfiguration()
    {
        if (!_options.Stripe.Enabled || string.IsNullOrWhiteSpace(_options.Stripe.WebhookSecret))
        {
            throw new PaymentProviderAdapterException(ProviderError.Configuration(
                "stripe",
                "STRIPE_WEBHOOK_NOT_CONFIGURED",
                "CommerceHub Stripe webhook verification is not configured."));
        }
    }

    private static string RequiredCurrency(string currency)
    {
        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3)
        {
            throw new PaymentProviderAdapterException(ProviderError.Validation(
                "stripe",
                "STRIPE_CURRENCY_INVALID",
                "Stripe currency must be a three-letter ISO code."));
        }

        return normalized;
    }

    private static long ConvertToMinorUnits(decimal amount)
    {
        var minor = amount * 100m;
        if (amount <= 0m || decimal.Truncate(minor) != minor)
        {
            throw new PaymentProviderAdapterException(ProviderError.Validation(
                "stripe",
                "STRIPE_AMOUNT_INVALID",
                "Stripe amount must be positive and convertible to minor units."));
        }

        return checked((long)minor);
    }

    private static bool IsPending(string? status) =>
        string.IsNullOrWhiteSpace(status) ||
        status.Contains("requires", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "processing", StringComparison.OrdinalIgnoreCase);

    private static ProviderError Translate(StripeException exception)
    {
        var stripeError = exception.StripeError;
        var code = FirstNonBlank(stripeError?.Code, stripeError?.Type, exception.GetType().Name);
        var message = FirstNonBlank(stripeError?.Message, exception.Message, "Stripe provider error.");

        return ProviderError.Create(
            "stripe",
            MapType(stripeError?.Type, exception.HttpStatusCode),
            code,
            message);
    }

    private static ProviderError TranslateWebhook(StripeException exception)
    {
        var message = exception.Message ?? "Stripe webhook verification failed.";
        var code = message.Contains("timestamp", StringComparison.OrdinalIgnoreCase)
            ? "STRIPE_WEBHOOK_TIMESTAMP_EXPIRED"
            : "STRIPE_WEBHOOK_SIGNATURE_INVALID";

        return ProviderError.Validation("stripe", code, message);
    }

    private static ProviderErrorType MapType(string? stripeType, System.Net.HttpStatusCode httpStatusCode)
    {
        if (httpStatusCode is System.Net.HttpStatusCode.Unauthorized)
        {
            return ProviderErrorType.Authentication;
        }

        if (httpStatusCode is System.Net.HttpStatusCode.Forbidden)
        {
            return ProviderErrorType.Authorization;
        }

        if ((int)httpStatusCode == 429 || string.Equals(stripeType, "rate_limit_error", StringComparison.Ordinal))
        {
            return ProviderErrorType.RateLimited;
        }

        if (string.Equals(stripeType, "invalid_request_error", StringComparison.Ordinal) ||
            string.Equals(stripeType, "idempotency_error", StringComparison.Ordinal))
        {
            return ProviderErrorType.Validation;
        }

        if (string.Equals(stripeType, "api_error", StringComparison.Ordinal) || (int)httpStatusCode >= 500)
        {
            return ProviderErrorType.ProviderUnavailable;
        }

        if (string.Equals(stripeType, "card_error", StringComparison.Ordinal))
        {
            return ProviderErrorType.ProviderRejected;
        }

        return ProviderErrorType.Unknown;
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
    }
}
