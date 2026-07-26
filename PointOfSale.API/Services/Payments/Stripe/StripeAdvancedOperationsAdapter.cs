using System.Collections.ObjectModel;
using System.Globalization;
using PointOfSale.Core.Payments.Runtime.Domain;
using Stripe;
using StripeCharge = Stripe.Charge;
using StripeException = Stripe.StripeException;
using StripePaymentIntent = Stripe.PaymentIntent;
using StripeRefund = Stripe.Refund;

namespace PointOfSale.API.Services.Payments.Stripe;

public interface IStripeAdvancedOperationsAdapter
{
    Task<StripeAdvancedOperationResult> ExecuteAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);
}

public interface IStripeAdvancedOperationsClient
{
    Task<StripePaymentIntent> CapturePaymentIntentAsync(
        string paymentIntentId,
        PaymentIntentCaptureOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken);

    Task<StripePaymentIntent> CancelPaymentIntentAsync(
        string paymentIntentId,
        PaymentIntentCancelOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken);

    Task<StripeRefund> CreateRefundAsync(
        RefundCreateOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken);

    Task<StripePaymentIntent> RetrievePaymentIntentAsync(
        string paymentIntentId,
        PaymentIntentGetOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken);

    Task<StripeCharge> RetrieveChargeAsync(
        string chargeId,
        ChargeGetOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken);
}

public sealed class StripeAdvancedOperationsAdapter : IStripeAdvancedOperationsAdapter
{
    private readonly IStripeAdvancedOperationsClient _client;

    public StripeAdvancedOperationsAdapter(IStripeAdvancedOperationsClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<StripeAdvancedOperationResult> ExecuteAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = StripeOperationFactory.Create(action);
            var evidence = request.Operation switch
            {
                StripeAdvancedOperation.Capture => StripeOperationEvidenceFactory.FromPaymentIntent(
                    action,
                    request,
                    await _client.CapturePaymentIntentAsync(
                        request.ProviderReference.Value,
                        request.CaptureOptions!,
                        request.RequestOptions,
                        cancellationToken)),
                StripeAdvancedOperation.Cancel => StripeOperationEvidenceFactory.FromPaymentIntent(
                    action,
                    request,
                    await _client.CancelPaymentIntentAsync(
                        request.ProviderReference.Value,
                        request.CancelOptions!,
                        request.RequestOptions,
                        cancellationToken)),
                StripeAdvancedOperation.Refund or StripeAdvancedOperation.PartialRefund => StripeOperationEvidenceFactory.FromRefund(
                    action,
                    request,
                    await _client.CreateRefundAsync(
                        request.RefundOptions!,
                        request.RequestOptions,
                        cancellationToken)),
                StripeAdvancedOperation.RetrievePaymentIntent => StripeOperationEvidenceFactory.FromPaymentIntent(
                    action,
                    request,
                    await _client.RetrievePaymentIntentAsync(
                        request.ProviderReference.Value,
                        request.PaymentIntentGetOptions!,
                        request.RequestOptions,
                        cancellationToken)),
                StripeAdvancedOperation.RetrieveCharge => StripeOperationEvidenceFactory.FromCharge(
                    action,
                    request,
                    await _client.RetrieveChargeAsync(
                        request.ProviderReference.Value,
                        request.ChargeGetOptions!,
                        request.RequestOptions,
                        cancellationToken)),
                _ => throw new ArgumentException("Unsupported Stripe advanced operation.")
            };

            return StripeAdvancedOperationResult.Success(evidence, request);
        }
        catch (StripeException exception)
        {
            return StripeAdvancedOperationResult.Failed(StripeProviderErrorTranslator.Translate(exception));
        }
        catch (ArgumentException exception)
        {
            return StripeAdvancedOperationResult.Failed(
                CanonicalProviderError.Validation("STRIPE_OPERATION_REQUEST_INVALID", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return StripeAdvancedOperationResult.Failed(
                CanonicalProviderError.Configuration("STRIPE_OPERATION_ADAPTER_INVALID", exception.Message));
        }
    }
}

public sealed class StripeAdvancedOperationsSdkClient : IStripeAdvancedOperationsClient
{
    private readonly PaymentIntentService _paymentIntents;
    private readonly RefundService _refunds;
    private readonly ChargeService _charges;

    public StripeAdvancedOperationsSdkClient(StripeClient client)
    {
        _paymentIntents = new PaymentIntentService(client);
        _refunds = new RefundService(client);
        _charges = new ChargeService(client);
    }

    public Task<StripePaymentIntent> CapturePaymentIntentAsync(
        string paymentIntentId,
        PaymentIntentCaptureOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken)
    {
        return _paymentIntents.CaptureAsync(paymentIntentId, options, requestOptions, cancellationToken);
    }

    public Task<StripePaymentIntent> CancelPaymentIntentAsync(
        string paymentIntentId,
        PaymentIntentCancelOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken)
    {
        return _paymentIntents.CancelAsync(paymentIntentId, options, requestOptions, cancellationToken);
    }

    public Task<StripeRefund> CreateRefundAsync(
        RefundCreateOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken)
    {
        return _refunds.CreateAsync(options, requestOptions, cancellationToken);
    }

    public Task<StripePaymentIntent> RetrievePaymentIntentAsync(
        string paymentIntentId,
        PaymentIntentGetOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken)
    {
        return _paymentIntents.GetAsync(paymentIntentId, options, requestOptions, cancellationToken);
    }

    public Task<StripeCharge> RetrieveChargeAsync(
        string chargeId,
        ChargeGetOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken)
    {
        return _charges.GetAsync(chargeId, options, requestOptions, cancellationToken);
    }
}

public static class StripeOperationFactory
{
    public static StripeAdvancedOperationRequest Create(PaymentAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var operation = StripeOperationMetadata.ResolveOperation(action);
        var providerReference = new StripeOperationReference(Required(action.Metadata.Values, StripeOperationMetadataKeys.ProviderReference));
        ValidateProviderReference(operation, providerReference.Value);
        var idempotencyKey = StripeOperationIdempotencyKey.Build(action, operation).Value;
        var metadata = StripeOperationMetadata.FromAction(action, operation).Values;
        var requestOptions = new RequestOptions { IdempotencyKey = idempotencyKey };
        var operationReference = Optional(action.Metadata.Values, StripeOperationMetadataKeys.OperationReference);

        if (operation == StripeAdvancedOperation.PartialRefund &&
            string.IsNullOrWhiteSpace(Optional(action.Metadata.Values, StripeOperationMetadataKeys.Amount)))
        {
            throw new ArgumentException("Canonical partial refund amount is required.");
        }

        return operation switch
        {
            StripeAdvancedOperation.Capture => StripeAdvancedOperationRequest.ForCapture(
                operation,
                providerReference,
                new PaymentIntentCaptureOptions
                {
                    AmountToCapture = OptionalAmount(action, StripeOperationMetadataKeys.Amount, StripeOperationMetadataKeys.Currency),
                    Metadata = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
                },
                requestOptions,
                new StripeIdempotencyKey(idempotencyKey),
                operationReference),
            StripeAdvancedOperation.Cancel => StripeAdvancedOperationRequest.ForCancel(
                operation,
                providerReference,
                new PaymentIntentCancelOptions
                {
                    CancellationReason = NormalizeCancelReason(Optional(action.Metadata.Values, StripeOperationMetadataKeys.Reason))
                },
                requestOptions,
                new StripeIdempotencyKey(idempotencyKey),
                operationReference),
            StripeAdvancedOperation.Refund or StripeAdvancedOperation.PartialRefund => StripeAdvancedOperationRequest.ForRefund(
                operation,
                providerReference,
                new RefundCreateOptions
                {
                    PaymentIntent = providerReference.Value.StartsWith("pi_", StringComparison.Ordinal) ? providerReference.Value : null,
                    Charge = providerReference.Value.StartsWith("ch_", StringComparison.Ordinal) ? providerReference.Value : null,
                    Amount = OptionalAmount(action, StripeOperationMetadataKeys.Amount, StripeOperationMetadataKeys.Currency),
                    Currency = Optional(action.Metadata.Values, StripeOperationMetadataKeys.Currency)?.ToLowerInvariant(),
                    Reason = NormalizeRefundReason(Optional(action.Metadata.Values, StripeOperationMetadataKeys.Reason)),
                    Metadata = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
                },
                requestOptions,
                new StripeIdempotencyKey(idempotencyKey),
                operationReference),
            StripeAdvancedOperation.RetrievePaymentIntent => StripeAdvancedOperationRequest.ForRetrievePaymentIntent(
                operation,
                providerReference,
                new PaymentIntentGetOptions(),
                requestOptions,
                new StripeIdempotencyKey(idempotencyKey),
                operationReference),
            StripeAdvancedOperation.RetrieveCharge => StripeAdvancedOperationRequest.ForRetrieveCharge(
                operation,
                providerReference,
                new ChargeGetOptions(),
                requestOptions,
                new StripeIdempotencyKey(idempotencyKey),
                operationReference),
            _ => throw new ArgumentException("Unsupported Stripe advanced operation.")
        };
    }

    private static long? OptionalAmount(PaymentAction action, string amountKey, string currencyKey)
    {
        var amount = Optional(action.Metadata.Values, amountKey);
        if (string.IsNullOrWhiteSpace(amount))
        {
            return null;
        }

        var currency = Optional(action.Metadata.Values, currencyKey)
            ?? throw new ArgumentException("Currency is required when amount is supplied.");
        return StripeMoneyMapper.ToMinorUnits(amount, currency);
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key)
    {
        var value = Optional(values, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Canonical Stripe operation field '{key}' is required.");
        }

        return value;
    }

    private static string? Optional(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static string? NormalizeCancelReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        return reason.Trim().ToLowerInvariant() switch
        {
            "duplicate" => "duplicate",
            "fraudulent" => "fraudulent",
            "requested_by_customer" => "requested_by_customer",
            "abandoned" => "abandoned",
            _ => throw new ArgumentException("Stripe cancel reason must be duplicate, fraudulent, requested_by_customer, or abandoned.")
        };
    }

    private static string? NormalizeRefundReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        return reason.Trim().ToLowerInvariant() switch
        {
            "duplicate" => "duplicate",
            "fraudulent" => "fraudulent",
            "requested_by_customer" => "requested_by_customer",
            _ => throw new ArgumentException("Stripe refund reason must be duplicate, fraudulent, or requested_by_customer.")
        };
    }

    private static void ValidateProviderReference(StripeAdvancedOperation operation, string providerReference)
    {
        var valid = operation switch
        {
            StripeAdvancedOperation.Capture or StripeAdvancedOperation.Cancel or StripeAdvancedOperation.RetrievePaymentIntent
                => providerReference.StartsWith("pi_", StringComparison.Ordinal),
            StripeAdvancedOperation.RetrieveCharge
                => providerReference.StartsWith("ch_", StringComparison.Ordinal),
            StripeAdvancedOperation.Refund or StripeAdvancedOperation.PartialRefund
                => providerReference.StartsWith("pi_", StringComparison.Ordinal) || providerReference.StartsWith("ch_", StringComparison.Ordinal),
            _ => false
        };

        if (!valid)
        {
            throw new ArgumentException($"Stripe provider reference is invalid for {operation}.");
        }
    }
}

public static class StripeOperationEvidenceFactory
{
    public static ProviderEvidence FromPaymentIntent(
        PaymentAction action,
        StripeAdvancedOperationRequest request,
        StripePaymentIntent paymentIntent)
    {
        ArgumentNullException.ThrowIfNull(paymentIntent);
        var id = Required(paymentIntent.Id, "Stripe PaymentIntent id is required.");
        var timestamp = paymentIntent.Created == default ? action.LastUpdatedUtc : Utc(paymentIntent.Created);
        var metadata = CreateResponseMetadata(
            action,
            request,
            id,
            paymentIntent.Status,
            paymentIntent.Metadata,
            paymentIntent.Amount,
            paymentIntent.Currency);

        return CreateEvidence(action, request, id, paymentIntent.Status, timestamp, "payment_intent", metadata);
    }

    public static ProviderEvidence FromRefund(
        PaymentAction action,
        StripeAdvancedOperationRequest request,
        StripeRefund refund)
    {
        ArgumentNullException.ThrowIfNull(refund);
        var id = Required(refund.Id, "Stripe Refund id is required.");
        var providerReference = FirstNonBlank(refund.PaymentIntentId, refund.ChargeId, request.ProviderReference.Value);
        var timestamp = refund.Created == default ? action.LastUpdatedUtc : Utc(refund.Created);
        var metadata = CreateResponseMetadata(
            action,
            request,
            id,
            refund.Status,
            refund.Metadata,
            refund.Amount,
            refund.Currency);
        metadata["provider_reference"] = providerReference;
        metadata["refund_reference"] = id;

        return CreateEvidence(action, request, id, refund.Status, timestamp, "refund", metadata);
    }

    public static ProviderEvidence FromCharge(
        PaymentAction action,
        StripeAdvancedOperationRequest request,
        StripeCharge charge)
    {
        ArgumentNullException.ThrowIfNull(charge);
        var id = Required(charge.Id, "Stripe Charge id is required.");
        var timestamp = charge.Created == default ? action.LastUpdatedUtc : Utc(charge.Created);
        var metadata = CreateResponseMetadata(
            action,
            request,
            id,
            charge.Status,
            charge.Metadata,
            charge.Amount,
            charge.Currency);

        return CreateEvidence(action, request, id, charge.Status, timestamp, "charge", metadata);
    }

    private static ProviderEvidence CreateEvidence(
        PaymentAction action,
        StripeAdvancedOperationRequest request,
        string operationId,
        string? operationStatus,
        DateTimeOffset timestamp,
        string objectType,
        IReadOnlyDictionary<string, string> metadata)
    {
        var evidenceHash = StripeHash.Create(
            request.Operation.ToString(),
            request.ProviderReference.Value,
            operationId,
            operationStatus ?? string.Empty,
            action.CorrelationId.Value);
        var evidenceId = StripeDeterministicId.Create(
            "stripe-advanced-operation-evidence",
            action.Id.Value.ToString("N"),
            request.Operation.ToString(),
            request.ProviderReference.Value,
            operationId,
            evidenceHash);

        return ProviderEvidenceFactory.Create(
            new EvidenceId(evidenceId),
            action.Id,
            EvidenceType.ProviderNotification,
            EvidenceStatus.Recorded,
            new EvidenceReference(operationId),
            new EvidenceSource(StripeProviderReference.ProviderName),
            new EvidenceTimestamp(timestamp),
            new EvidenceHash(evidenceHash),
            new ProviderPayloadReference($"stripe:{objectType}:{operationId}"),
            action.CorrelationId,
            new EvidenceMetadata(metadata));
    }

    private static Dictionary<string, string> CreateResponseMetadata(
        PaymentAction action,
        StripeAdvancedOperationRequest request,
        string operationId,
        string? operationStatus,
        IDictionary<string, string>? stripeMetadata,
        long? amount,
        string? currency)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = StripeProviderReference.ProviderName,
            ["adapter"] = nameof(StripeAdvancedOperationsAdapter),
            ["operation"] = request.Operation.ToString(),
            ["operation_id"] = operationId,
            ["operation_status"] = operationStatus ?? string.Empty,
            ["provider_reference"] = request.ProviderReference.Value,
            ["correlation_id"] = action.CorrelationId.Value,
            ["payment_action_id"] = action.Id.Value.ToString("N"),
            ["operation_reference"] = request.OperationReference.Value,
            ["idempotency_key"] = request.IdempotencyKey.Value
        };

        if (amount.HasValue)
        {
            values["amount"] = amount.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(currency))
        {
            values["currency"] = currency;
        }

        foreach (var (key, value) in (stripeMetadata ?? new Dictionary<string, string>()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            values[$"stripe_metadata_{key}"] = value ?? string.Empty;
        }

        return values
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static DateTimeOffset Utc(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static string Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message);
        }

        return value.Trim();
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
    }
}

public sealed record StripeAdvancedOperationResult
{
    public bool Succeeded { get; init; }
    public ProviderEvidence? Evidence { get; init; }
    public StripeAdvancedOperationRequest? Request { get; init; }
    public CanonicalProviderError? Error { get; init; }

    private StripeAdvancedOperationResult()
    {
    }

    public static StripeAdvancedOperationResult Success(
        ProviderEvidence evidence,
        StripeAdvancedOperationRequest request)
    {
        return new StripeAdvancedOperationResult
        {
            Succeeded = true,
            Evidence = evidence,
            Request = request
        };
    }

    public static StripeAdvancedOperationResult Failed(CanonicalProviderError error)
    {
        return new StripeAdvancedOperationResult
        {
            Succeeded = false,
            Error = error
        };
    }
}

public sealed record StripeAdvancedOperationRequest
{
    public StripeAdvancedOperation Operation { get; init; }
    public StripeOperationReference ProviderReference { get; init; }
    public StripeOperationReference OperationReference { get; init; }
    public RequestOptions RequestOptions { get; init; }
    public StripeIdempotencyKey IdempotencyKey { get; init; }
    public PaymentIntentCaptureOptions? CaptureOptions { get; init; }
    public PaymentIntentCancelOptions? CancelOptions { get; init; }
    public RefundCreateOptions? RefundOptions { get; init; }
    public PaymentIntentGetOptions? PaymentIntentGetOptions { get; init; }
    public ChargeGetOptions? ChargeGetOptions { get; init; }

    private StripeAdvancedOperationRequest(
        StripeAdvancedOperation operation,
        StripeOperationReference providerReference,
        RequestOptions requestOptions,
        StripeIdempotencyKey idempotencyKey,
        string? operationReference = null,
        PaymentIntentCaptureOptions? captureOptions = null,
        PaymentIntentCancelOptions? cancelOptions = null,
        RefundCreateOptions? refundOptions = null,
        PaymentIntentGetOptions? paymentIntentGetOptions = null,
        ChargeGetOptions? chargeGetOptions = null)
    {
        Operation = operation == StripeAdvancedOperation.Unknown
            ? throw new ArgumentException("Stripe operation is required.", nameof(operation))
            : operation;
        ProviderReference = providerReference;
        OperationReference = new StripeOperationReference(
            string.IsNullOrWhiteSpace(operationReference)
                ? $"{operation}:{providerReference.Value}"
                : operationReference.Trim());
        RequestOptions = requestOptions;
        IdempotencyKey = idempotencyKey;
        CaptureOptions = captureOptions;
        CancelOptions = cancelOptions;
        RefundOptions = refundOptions;
        PaymentIntentGetOptions = paymentIntentGetOptions;
        ChargeGetOptions = chargeGetOptions;
    }

    public static StripeAdvancedOperationRequest ForCapture(
        StripeAdvancedOperation operation,
        StripeOperationReference providerReference,
        PaymentIntentCaptureOptions options,
        RequestOptions requestOptions,
        StripeIdempotencyKey idempotencyKey,
        string? operationReference = null)
    {
        return new StripeAdvancedOperationRequest(operation, providerReference, requestOptions, idempotencyKey, operationReference, captureOptions: options);
    }

    public static StripeAdvancedOperationRequest ForCancel(
        StripeAdvancedOperation operation,
        StripeOperationReference providerReference,
        PaymentIntentCancelOptions options,
        RequestOptions requestOptions,
        StripeIdempotencyKey idempotencyKey,
        string? operationReference = null)
    {
        return new StripeAdvancedOperationRequest(operation, providerReference, requestOptions, idempotencyKey, operationReference, cancelOptions: options);
    }

    public static StripeAdvancedOperationRequest ForRefund(
        StripeAdvancedOperation operation,
        StripeOperationReference providerReference,
        RefundCreateOptions options,
        RequestOptions requestOptions,
        StripeIdempotencyKey idempotencyKey,
        string? operationReference = null)
    {
        return new StripeAdvancedOperationRequest(operation, providerReference, requestOptions, idempotencyKey, operationReference, refundOptions: options);
    }

    public static StripeAdvancedOperationRequest ForRetrievePaymentIntent(
        StripeAdvancedOperation operation,
        StripeOperationReference providerReference,
        PaymentIntentGetOptions options,
        RequestOptions requestOptions,
        StripeIdempotencyKey idempotencyKey,
        string? operationReference = null)
    {
        return new StripeAdvancedOperationRequest(operation, providerReference, requestOptions, idempotencyKey, operationReference, paymentIntentGetOptions: options);
    }

    public static StripeAdvancedOperationRequest ForRetrieveCharge(
        StripeAdvancedOperation operation,
        StripeOperationReference providerReference,
        ChargeGetOptions options,
        RequestOptions requestOptions,
        StripeIdempotencyKey idempotencyKey,
        string? operationReference = null)
    {
        return new StripeAdvancedOperationRequest(operation, providerReference, requestOptions, idempotencyKey, operationReference, chargeGetOptions: options);
    }
}

public enum StripeAdvancedOperation
{
    Unknown = 0,
    Capture = 1,
    Cancel = 2,
    Refund = 3,
    PartialRefund = 4,
    RetrievePaymentIntent = 5,
    RetrieveCharge = 6
}

public sealed record StripeOperationReference(string Value)
{
    public StripeOperationReference() : this(string.Empty)
    {
    }
}

public sealed record StripeRefundReference(string Value);

public sealed record StripeCaptureReference(string Value);

public sealed record OperationCorrelation(string Value);

public sealed record StripeOperationMetadata(IReadOnlyDictionary<string, string> Values)
{
    public static StripeAdvancedOperation ResolveOperation(PaymentAction action)
    {
        var raw = action.Metadata.Values.TryGetValue(StripeOperationMetadataKeys.Operation, out var value)
            ? value
            : action.Reference.Value;

        return raw.Trim().ToLowerInvariant() switch
        {
            "capture" => StripeAdvancedOperation.Capture,
            "cancel" => StripeAdvancedOperation.Cancel,
            "refund" => StripeAdvancedOperation.Refund,
            "partial_refund" or "partial-refund" or "partialrefund" => StripeAdvancedOperation.PartialRefund,
            "retrieve_payment_intent" or "retrieve-payment-intent" or "retrievepaymentintent" => StripeAdvancedOperation.RetrievePaymentIntent,
            "retrieve_charge" or "retrieve-charge" or "retrievecharge" => StripeAdvancedOperation.RetrieveCharge,
            _ => throw new ArgumentException("Canonical Stripe operation must be capture, cancel, refund, partial_refund, retrieve_payment_intent, or retrieve_charge.")
        };
    }

    public static StripeOperationMetadata FromAction(PaymentAction action, StripeAdvancedOperation operation)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = StripeProviderReference.ProviderName,
            ["adapter"] = nameof(StripeAdvancedOperationsAdapter),
            ["operation"] = operation.ToString(),
            ["payment_action_id"] = action.Id.Value.ToString("N"),
            ["payment_session_id"] = action.SessionId.Value.ToString("N"),
            ["payment_attempt_id"] = action.AttemptId.Value.ToString("N"),
            ["correlation_id"] = action.CorrelationId.Value,
            ["action_reference"] = action.Reference.Value
        };

        foreach (var (key, value) in action.Metadata.Values)
        {
            if (!StripeOperationMetadataKeys.AdapterOnlyKeys.Contains(key))
            {
                values[$"canonical_{key}"] = value;
            }
        }

        return new StripeOperationMetadata(new ReadOnlyDictionary<string, string>(
            values
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.Ordinal)));
    }
}

public sealed record StripeOperationIdempotencyKey(string Value)
{
    private const int MaxStripeIdempotencyKeyLength = 255;

    public static StripeIdempotencyKey Build(PaymentAction action, StripeAdvancedOperation operation)
    {
        if (action.Metadata.Values.TryGetValue(StripeOperationMetadataKeys.IdempotencyKey, out var supplied) &&
            !string.IsNullOrWhiteSpace(supplied))
        {
            return new StripeIdempotencyKey(TrimToStripeLimit(supplied.Trim()));
        }

        var providerReference = action.Metadata.Values.TryGetValue(StripeOperationMetadataKeys.ProviderReference, out var reference)
            ? reference.Trim()
            : action.Reference.Value;
        var amount = action.Metadata.Values.TryGetValue(StripeOperationMetadataKeys.Amount, out var suppliedAmount)
            ? suppliedAmount.Trim()
            : string.Empty;
        var source = string.Join(
            "|",
            "stripe",
            "advanced_operation",
            operation,
            action.SessionId.Value.ToString("N"),
            action.AttemptId.Value.ToString("N"),
            action.Id.Value.ToString("N"),
            providerReference,
            amount,
            action.CorrelationId.Value);
        var hash = StripeHash.Create(source);

        return new StripeIdempotencyKey($"stripe:op:{operation}:{action.Id.Value:N}:{hash[..24]}");
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

public static class StripeOperationMetadataKeys
{
    public const string Operation = "Operation";
    public const string Amount = "Amount";
    public const string Currency = "Currency";
    public const string ProviderReference = "ProviderReference";
    public const string CorrelationId = "CorrelationId";
    public const string Metadata = "Metadata";
    public const string Reason = "Reason";
    public const string IdempotencyKey = "IdempotencyKey";
    public const string OperationReference = "OperationReference";

    public static readonly ISet<string> AdapterOnlyKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        Operation,
        Amount,
        Currency,
        ProviderReference,
        CorrelationId,
        Metadata,
        Reason,
        IdempotencyKey,
        OperationReference
    };
}
