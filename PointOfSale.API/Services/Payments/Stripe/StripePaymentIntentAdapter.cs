using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using PointOfSale.Core.Payments.Runtime.Domain;
using Stripe;
using StripeException = Stripe.StripeException;
using StripePaymentIntent = Stripe.PaymentIntent;

namespace PointOfSale.API.Services.Payments.Stripe;

public interface IStripePaymentIntentAdapter
{
    Task<StripePaymentIntentAdapterResult> CreateAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);
}

public interface IStripePaymentIntentClient
{
    Task<StripePaymentIntent> CreateAsync(
        PaymentIntentCreateOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken);
}

public sealed class StripePaymentIntentAdapter : IStripePaymentIntentAdapter
{
    private readonly IStripePaymentIntentClient _client;

    public StripePaymentIntentAdapter(IStripePaymentIntentClient client)
    {
        _client = client;
    }

    public async Task<StripePaymentIntentAdapterResult> CreateAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = StripePaymentIntentFactory.Create(action);
            var paymentIntent = await _client.CreateAsync(
                request.Options,
                request.RequestOptions,
                cancellationToken);
            var evidence = StripeEvidenceFactory.FromPaymentIntent(action, paymentIntent);

            return StripePaymentIntentAdapterResult.Success(evidence, request);
        }
        catch (StripeException exception)
        {
            return StripePaymentIntentAdapterResult.Failed(
                StripeProviderErrorTranslator.Translate(exception));
        }
        catch (ArgumentException exception)
        {
            return StripePaymentIntentAdapterResult.Failed(
                CanonicalProviderError.Validation("STRIPE_REQUEST_INVALID", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return StripePaymentIntentAdapterResult.Failed(
                CanonicalProviderError.Configuration("STRIPE_ADAPTER_INVALID", exception.Message));
        }
    }
}

public sealed class StripePaymentIntentSdkClient : IStripePaymentIntentClient
{
    private readonly PaymentIntentService _service;

    public StripePaymentIntentSdkClient(StripeClient client)
    {
        _service = new PaymentIntentService(client);
    }

    public Task<StripePaymentIntent> CreateAsync(
        PaymentIntentCreateOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken)
    {
        return _service.CreateAsync(options, requestOptions, cancellationToken);
    }
}

public static class StripePaymentIntentFactory
{
    public static StripePaymentIntentRequest Create(PaymentAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var values = action.Metadata.Values;
        var currency = Required(values, StripeRequestMetadataKeys.Currency).ToLowerInvariant();
        var amount = StripeMoneyMapper.ToMinorUnits(
            Required(values, StripeRequestMetadataKeys.Amount),
            currency);
        var canonicalMetadata = StripeRequestMetadata.FromAction(action).Values;
        var idempotencyKey = StripeIdempotencyKey.Build(action).Value;

        var options = new PaymentIntentCreateOptions
        {
            Amount = amount,
            Currency = currency,
            Description = Optional(values, StripeRequestMetadataKeys.Description),
            Metadata = new Dictionary<string, string>(canonicalMetadata, StringComparer.Ordinal),
            Customer = Optional(values, StripeRequestMetadataKeys.CustomerReference),
            PaymentMethodTypes = ParseCsv(values, StripeRequestMetadataKeys.PaymentMethodTypes),
            CaptureMethod = MapCaptureMode(Optional(values, StripeRequestMetadataKeys.CaptureMode)),
            StatementDescriptor = Optional(values, StripeRequestMetadataKeys.StatementDescriptor),
            ReceiptEmail = Optional(values, StripeRequestMetadataKeys.ReceiptEmail)
        };

        var requestOptions = new RequestOptions
        {
            IdempotencyKey = idempotencyKey
        };

        return new StripePaymentIntentRequest(options, requestOptions, new StripeIdempotencyKey(idempotencyKey));
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key)
    {
        var value = Optional(values, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Canonical Stripe PaymentIntent field '{key}' is required.");
        }

        return value;
    }

    private static string? Optional(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static List<string>? ParseCsv(IReadOnlyDictionary<string, string> values, string key)
    {
        var raw = Optional(values, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string? MapCaptureMode(string? captureMode)
    {
        if (string.IsNullOrWhiteSpace(captureMode))
        {
            return null;
        }

        return captureMode.Trim().ToLowerInvariant() switch
        {
            "automatic" => "automatic",
            "automatic_async" => "automatic_async",
            "manual" => "manual",
            _ => throw new ArgumentException("Canonical capture mode must be automatic, automatic_async, or manual.")
        };
    }
}

public static class StripeEvidenceFactory
{
    public static ProviderEvidence FromPaymentIntent(PaymentAction action, StripePaymentIntent paymentIntent)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(paymentIntent);

        if (string.IsNullOrWhiteSpace(paymentIntent.Id))
        {
            throw new ArgumentException("Stripe PaymentIntent id is required.", nameof(paymentIntent));
        }

        var timestamp = paymentIntent.Created == default
            ? action.LastUpdatedUtc
            : new DateTimeOffset(DateTime.SpecifyKind(paymentIntent.Created, DateTimeKind.Utc));
        var reference = new StripePaymentIntentReference(paymentIntent.Id);
        var metadata = StripeResponseMetadata.FromPaymentIntent(action, paymentIntent).Values;
        var evidenceHash = StripeHash.Create(
            paymentIntent.Id,
            paymentIntent.Status ?? string.Empty,
            paymentIntent.Amount.ToString(CultureInfo.InvariantCulture),
            paymentIntent.Currency ?? string.Empty,
            action.CorrelationId.Value);
        var evidenceId = StripeDeterministicId.Create(
            "stripe-payment-intent-evidence",
            action.Id.Value.ToString("N"),
            paymentIntent.Id,
            paymentIntent.Status ?? string.Empty,
            timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        return ProviderEvidenceFactory.Create(
            new EvidenceId(evidenceId),
            action.Id,
            EvidenceType.ProviderNotification,
            EvidenceStatus.Recorded,
            new EvidenceReference(reference.Value),
            new EvidenceSource(StripeProviderReference.ProviderName),
            new EvidenceTimestamp(timestamp),
            new EvidenceHash(evidenceHash),
            new ProviderPayloadReference($"stripe:payment_intent:{paymentIntent.Id}"),
            action.CorrelationId,
            new EvidenceMetadata(metadata));
    }
}

public static class StripeProviderErrorTranslator
{
    public static CanonicalProviderError Translate(StripeException exception)
    {
        var stripeError = exception.StripeError;
        var code = FirstNonBlank(stripeError?.Code, stripeError?.Type, exception.GetType().Name);
        var message = FirstNonBlank(stripeError?.Message, exception.Message, "Stripe provider error.");
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = StripeProviderReference.ProviderName
        };

        AddIfPresent(metadata, "stripe_error_type", stripeError?.Type);
        AddIfPresent(metadata, "stripe_error_code", stripeError?.Code);
        AddIfPresent(metadata, "stripe_decline_code", stripeError?.DeclineCode);
        AddIfPresent(metadata, "stripe_error_param", stripeError?.Param);
        return new CanonicalProviderError(
            MapType(stripeError?.Type, stripeError?.Code, exception.HttpStatusCode),
            code,
            message,
            null,
            metadata);
    }

    private static CanonicalProviderErrorType MapType(string? stripeType, string? stripeCode, HttpStatusCode httpStatusCode)
    {
        if (string.Equals(stripeCode, "charge_already_captured", StringComparison.Ordinal) ||
            string.Equals(stripeCode, "payment_intent_unexpected_state", StringComparison.Ordinal) &&
            stripeType?.Contains("capture", StringComparison.OrdinalIgnoreCase) == true)
        {
            return CanonicalProviderErrorType.AlreadyCaptured;
        }

        if (string.Equals(stripeCode, "charge_already_refunded", StringComparison.Ordinal) ||
            string.Equals(stripeCode, "refund_already_exists", StringComparison.Ordinal))
        {
            return CanonicalProviderErrorType.AlreadyRefunded;
        }

        if (string.Equals(stripeCode, "payment_intent_unexpected_state", StringComparison.Ordinal) ||
            string.Equals(stripeCode, "charge_expired_for_capture", StringComparison.Ordinal) ||
            string.Equals(stripeCode, "refund_charge_failure", StringComparison.Ordinal))
        {
            return CanonicalProviderErrorType.InvalidState;
        }

        if (httpStatusCode is HttpStatusCode.Unauthorized)
        {
            return CanonicalProviderErrorType.Authentication;
        }

        if (httpStatusCode is HttpStatusCode.Forbidden)
        {
            return CanonicalProviderErrorType.Authorization;
        }

        if ((int)httpStatusCode == 429 || string.Equals(stripeType, "rate_limit_error", StringComparison.Ordinal))
        {
            return CanonicalProviderErrorType.RateLimited;
        }

        if (string.Equals(stripeType, "invalid_request_error", StringComparison.Ordinal) ||
            string.Equals(stripeType, "idempotency_error", StringComparison.Ordinal))
        {
            return CanonicalProviderErrorType.Validation;
        }

        if (string.Equals(stripeType, "authentication_error", StringComparison.Ordinal))
        {
            return CanonicalProviderErrorType.Authentication;
        }

        if (string.Equals(stripeType, "api_error", StringComparison.Ordinal) || (int)httpStatusCode >= 500)
        {
            return CanonicalProviderErrorType.ProviderUnavailable;
        }

        if (string.Equals(stripeType, "card_error", StringComparison.Ordinal))
        {
            return CanonicalProviderErrorType.ProviderRejected;
        }

        return CanonicalProviderErrorType.Unknown;
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

public sealed record StripePaymentIntentAdapterResult
{
    public bool Succeeded { get; init; }
    public ProviderEvidence? Evidence { get; init; }
    public StripePaymentIntentRequest? Request { get; init; }
    public CanonicalProviderError? Error { get; init; }

    private StripePaymentIntentAdapterResult()
    {
    }

    public static StripePaymentIntentAdapterResult Success(
        ProviderEvidence evidence,
        StripePaymentIntentRequest request)
    {
        return new StripePaymentIntentAdapterResult
        {
            Succeeded = true,
            Evidence = evidence,
            Request = request
        };
    }

    public static StripePaymentIntentAdapterResult Failed(CanonicalProviderError error)
    {
        return new StripePaymentIntentAdapterResult
        {
            Succeeded = false,
            Error = error
        };
    }
}

public sealed record StripePaymentIntentRequest(
    PaymentIntentCreateOptions Options,
    RequestOptions RequestOptions,
    StripeIdempotencyKey IdempotencyKey);

public sealed record StripeProviderReference(string Value)
{
    public const string ProviderName = "stripe";
}

public sealed record StripePaymentIntentReference(string Value);

public sealed record StripeIdempotencyKey(string Value)
{
    private const int MaxStripeIdempotencyKeyLength = 255;

    public static StripeIdempotencyKey Build(PaymentAction action)
    {
        if (action.Metadata.Values.TryGetValue(StripeRequestMetadataKeys.IdempotencyKey, out var supplied) &&
            !string.IsNullOrWhiteSpace(supplied))
        {
            return new StripeIdempotencyKey(TrimToStripeLimit(supplied.Trim()));
        }

        var canonicalSource = string.Join(
            "|",
            "stripe",
            "payment_intent",
            action.SessionId.Value.ToString("N"),
            action.AttemptId.Value.ToString("N"),
            action.Id.Value.ToString("N"),
            action.Type.ToString(),
            action.Reference.Value,
            action.CorrelationId.Value);
        var hash = StripeHash.Create(canonicalSource);

        return new StripeIdempotencyKey($"stripe:pi:{action.Id.Value:N}:{hash[..32]}");
    }

    private static string TrimToStripeLimit(string value)
    {
        if (value.Length <= MaxStripeIdempotencyKeyLength)
        {
            return value;
        }

        var hash = StripeHash.Create(value);
        var prefixLength = MaxStripeIdempotencyKeyLength - hash.Length - 1;
        return $"{value[..Math.Max(0, prefixLength)]}:{hash}";
    }
}

public sealed record StripeRequestMetadata(IReadOnlyDictionary<string, string> Values)
{
    public static StripeRequestMetadata FromAction(PaymentAction action)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = StripeProviderReference.ProviderName,
            ["payment_action_id"] = action.Id.Value.ToString("N"),
            ["payment_session_id"] = action.SessionId.Value.ToString("N"),
            ["payment_attempt_id"] = action.AttemptId.Value.ToString("N"),
            ["payment_action_type"] = action.Type.ToString(),
            ["correlation_id"] = action.CorrelationId.Value,
            ["action_reference"] = action.Reference.Value
        };

        foreach (var (key, value) in action.Metadata.Values)
        {
            if (!StripeRequestMetadataKeys.AdapterOnlyKeys.Contains(key))
            {
                values[$"canonical_{key}"] = value;
            }
        }

        return new StripeRequestMetadata(Normalize(values));
    }

    private static IReadOnlyDictionary<string, string> Normalize(IDictionary<string, string> values)
    {
        return values
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.Ordinal);
    }
}

public sealed record StripeResponseMetadata(IReadOnlyDictionary<string, string> Values)
{
    public static StripeResponseMetadata FromPaymentIntent(PaymentAction action, StripePaymentIntent paymentIntent)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = StripeProviderReference.ProviderName,
            ["provider_reference"] = paymentIntent.Id,
            ["provider_status"] = paymentIntent.Status ?? string.Empty,
            ["correlation_id"] = action.CorrelationId.Value,
            ["payment_action_id"] = action.Id.Value.ToString("N"),
            ["amount"] = paymentIntent.Amount.ToString(CultureInfo.InvariantCulture),
            ["currency"] = paymentIntent.Currency ?? string.Empty
        };

        if (paymentIntent.Metadata is not null)
        {
            foreach (var (key, value) in paymentIntent.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                values[$"stripe_metadata_{key}"] = value ?? string.Empty;
            }
        }

        return new StripeResponseMetadata(values);
    }
}

public sealed record CanonicalProviderError(
    CanonicalProviderErrorType Type,
    string Code,
    string Message,
    string? ProviderRequestId,
    IReadOnlyDictionary<string, string> Metadata)
{
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
                ["provider"] = StripeProviderReference.ProviderName
            });
    }
}

public enum CanonicalProviderErrorType
{
    Unknown = 0,
    Configuration = 1,
    Validation = 2,
    Authentication = 3,
    Authorization = 4,
    RateLimited = 5,
    Network = 6,
    ProviderUnavailable = 7,
    ProviderRejected = 8,
    AlreadyCaptured = 9,
    AlreadyRefunded = 10,
    InvalidState = 11
}

public static class StripeRequestMetadataKeys
{
    public const string Amount = "Amount";
    public const string Currency = "Currency";
    public const string Description = "Description";
    public const string CustomerReference = "CustomerReference";
    public const string PaymentMethodTypes = "PaymentMethodTypes";
    public const string CaptureMode = "CaptureMode";
    public const string IdempotencyKey = "IdempotencyKey";
    public const string StatementDescriptor = "StatementDescriptor";
    public const string ReceiptEmail = "ReceiptEmail";

    public static readonly ISet<string> AdapterOnlyKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        Amount,
        Currency,
        Description,
        CustomerReference,
        PaymentMethodTypes,
        CaptureMode,
        IdempotencyKey,
        StatementDescriptor,
        ReceiptEmail
    };
}

internal static class StripeMoneyMapper
{
    private static readonly ISet<string> ZeroDecimalCurrencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    public static long ToMinorUnits(string amount, string currency)
    {
        if (!decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException("Canonical amount must be an invariant decimal value.");
        }

        if (parsed <= 0m)
        {
            throw new ArgumentException("Canonical amount must be positive.");
        }

        var multiplier = ZeroDecimalCurrencies.Contains(currency) ? 1m : 100m;
        var minor = parsed * multiplier;
        if (decimal.Truncate(minor) != minor)
        {
            throw new ArgumentException("Canonical amount has more precision than Stripe supports for the currency.");
        }

        return checked((long)minor);
    }
}

internal static class StripeDeterministicId
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

internal static class StripeHash
{
    public static string Create(params string[] components)
    {
        var source = string.Join('|', components);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }
}
