using System.Collections.ObjectModel;
using PointOfSale.Core.Payments.Runtime.Contracts;
using PointOfSale.Core.Payments.Runtime.Domain;
using Stripe;

namespace PointOfSale.API.Services.Payments.Stripe;

public interface IStripePaymentProvider : IPaymentProvider
{
    string ProviderName { get; }

    StripeCapabilityReport Capabilities { get; }

    Task<StripeProviderResult> AuthorizeAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);

    Task<StripeProviderResult> CaptureAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);

    Task<StripeProviderResult> CancelAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);

    Task<StripeProviderResult> RefundAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);

    Task<StripeProviderResult> PartialRefundAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);

    Task<StripeWebhookVerificationResult> VerifyWebhookAsync(
        StripeWebhookVerificationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class StripePaymentProvider : IStripePaymentProvider
{
    private readonly IStripePaymentIntentAdapter _paymentIntentAdapter;
    private readonly IStripeAdvancedOperationsAdapter _advancedOperationsAdapter;
    private readonly IStripeCapabilityProvider _capabilityProvider;
    private readonly IStripeWebhookVerifier _webhookVerifier;

    public StripePaymentProvider(
        IStripePaymentIntentAdapter paymentIntentAdapter,
        IStripeAdvancedOperationsAdapter advancedOperationsAdapter,
        IStripeCapabilityProvider capabilityProvider,
        IStripeWebhookVerifier webhookVerifier)
    {
        _paymentIntentAdapter = paymentIntentAdapter ?? throw new ArgumentNullException(nameof(paymentIntentAdapter));
        _advancedOperationsAdapter = advancedOperationsAdapter ?? throw new ArgumentNullException(nameof(advancedOperationsAdapter));
        _capabilityProvider = capabilityProvider ?? throw new ArgumentNullException(nameof(capabilityProvider));
        _webhookVerifier = webhookVerifier ?? throw new ArgumentNullException(nameof(webhookVerifier));
    }

    public string ProviderName => StripeProviderReference.ProviderName;

    public StripeCapabilityReport Capabilities => _capabilityProvider.GetCapabilities();

    public async Task<StripeProviderResult> AuthorizeAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default)
    {
        var result = await _paymentIntentAdapter.CreateAsync(action, cancellationToken);
        return StripeProviderResult.FromAdapter(result.Evidence, result.Error);
    }

    public Task<StripeProviderResult> CaptureAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAdvancedOperationAsync(action, StripeAdvancedOperation.Capture, cancellationToken);
    }

    public Task<StripeProviderResult> CancelAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAdvancedOperationAsync(action, StripeAdvancedOperation.Cancel, cancellationToken);
    }

    public Task<StripeProviderResult> RefundAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAdvancedOperationAsync(action, StripeAdvancedOperation.Refund, cancellationToken);
    }

    public Task<StripeProviderResult> PartialRefundAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAdvancedOperationAsync(action, StripeAdvancedOperation.PartialRefund, cancellationToken);
    }

    public Task<StripeWebhookVerificationResult> VerifyWebhookAsync(
        StripeWebhookVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_webhookVerifier.Verify(request));
    }

    private async Task<StripeProviderResult> ExecuteAdvancedOperationAsync(
        PaymentAction action,
        StripeAdvancedOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var mappedAction = StripeRequestMapper.WithOperation(action, operation);
        var result = await _advancedOperationsAdapter.ExecuteAsync(mappedAction, cancellationToken);
        return StripeProviderResult.FromAdapter(result.Evidence, result.Error);
    }
}

public sealed record StripeProviderResult
{
    public bool Succeeded { get; init; }

    public ProviderEvidence? Evidence { get; init; }

    public CanonicalProviderError? Error { get; init; }

    public PaymentFailureReason FailureReason { get; init; }

    private StripeProviderResult()
    {
        FailureReason = PaymentFailureReason.Unknown;
    }

    public static StripeProviderResult FromAdapter(
        ProviderEvidence? evidence,
        CanonicalProviderError? error)
    {
        if (error is not null)
        {
            return new StripeProviderResult
            {
                Succeeded = false,
                Error = error,
                FailureReason = StripeErrorMapper.ToPaymentFailureReason(error)
            };
        }

        return new StripeProviderResult
        {
            Succeeded = true,
            Evidence = evidence ?? throw new ArgumentException("Canonical provider evidence is required for a successful Stripe operation."),
            FailureReason = PaymentFailureReason.Unknown
        };
    }
}

public interface IStripeCapabilityProvider
{
    StripeCapabilityReport GetCapabilities();
}

public sealed class StripeCapabilityProvider : IStripeCapabilityProvider
{
    private static readonly IReadOnlySet<StripeCanonicalCapability> CanonicalCapabilities =
        new ReadOnlySet<StripeCanonicalCapability>(new HashSet<StripeCanonicalCapability>
        {
            StripeCanonicalCapability.Authorize,
            StripeCanonicalCapability.Capture,
            StripeCanonicalCapability.Cancel,
            StripeCanonicalCapability.Refund,
            StripeCanonicalCapability.PartialRefund,
            StripeCanonicalCapability.Void,
            StripeCanonicalCapability.Tokenization,
            StripeCanonicalCapability.WebhookProcessing,
            StripeCanonicalCapability.CapabilityReporting
        });

    public StripeCapabilityReport GetCapabilities()
    {
        return new StripeCapabilityReport(
            StripeProviderReference.ProviderName,
            CanonicalCapabilities,
            ProviderCapability.ClientConfirmation
                | ProviderCapability.EmbeddedCheckout
                | ProviderCapability.Capture
                | ProviderCapability.Refund
                | ProviderCapability.PartialRefund
                | ProviderCapability.Void
                | ProviderCapability.AsyncConfirmation
                | ProviderCapability.StatusInquiry
                | ProviderCapability.Recovery
                | ProviderCapability.ReplayProtection
                | ProviderCapability.Idempotency
                | ProviderCapability.MultiCurrency
                | ProviderCapability.Metadata
                | ProviderCapability.Audit
                | ProviderCapability.Evidence
                | ProviderCapability.Retry
                | ProviderCapability.Quarantine
                | ProviderCapability.Reconciliation,
            ProviderConstraint.RequiresNotification
                | ProviderConstraint.RequiresMessageAuthentication
                | ProviderConstraint.RequiresProviderSession
                | ProviderConstraint.RequiresEvidenceRetention);
    }
}

public sealed record StripeCapabilityReport(
    string Provider,
    IReadOnlySet<StripeCanonicalCapability> CanonicalCapabilities,
    ProviderCapability RuntimeCapabilities,
    ProviderConstraint RuntimeConstraints);

public enum StripeCanonicalCapability
{
    Authorize = 1,
    Capture = 2,
    Cancel = 3,
    Refund = 4,
    PartialRefund = 5,
    Void = 6,
    Tokenization = 7,
    WebhookProcessing = 8,
    CapabilityReporting = 9
}

public static class StripeRequestMapper
{
    public static StripePaymentIntentRequest ToPaymentIntentRequest(PaymentAction action)
    {
        return StripePaymentIntentFactory.Create(action);
    }

    public static StripeAdvancedOperationRequest ToAdvancedOperationRequest(PaymentAction action)
    {
        return StripeOperationFactory.Create(action);
    }

    public static PaymentAction WithOperation(PaymentAction action, StripeAdvancedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (operation == StripeAdvancedOperation.Unknown)
        {
            throw new ArgumentException("Stripe operation is required.", nameof(operation));
        }

        var metadata = action.Metadata.Values.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        metadata[StripeOperationMetadataKeys.Operation] = operation.ToString();

        return action with
        {
            Metadata = new PaymentActionMetadata(metadata)
        };
    }
}

public static class StripeResponseMapper
{
    public static ProviderEvidence ToProviderEvidence(PaymentAction action, PaymentIntent paymentIntent)
    {
        return StripeEvidenceFactory.FromPaymentIntent(action, paymentIntent);
    }

    public static ProviderEvidence ToWebhookEvidence(
        PaymentAction action,
        Event stripeEvent,
        string rawPayload)
    {
        var translation = StripeWebhookEvidenceTranslator.Translate(action, stripeEvent, rawPayload);
        if (!translation.Succeeded || translation.Evidence is null)
        {
            throw new ArgumentException(translation.Error?.Message ?? "Stripe webhook event could not be translated.");
        }

        return translation.Evidence;
    }
}

public static class StripeErrorMapper
{
    public static CanonicalProviderError ToCanonicalProviderError(StripeException exception)
    {
        return StripeProviderErrorTranslator.Translate(exception);
    }

    public static PaymentFailureReason ToPaymentFailureReason(CanonicalProviderError error)
    {
        return error.Type switch
        {
            CanonicalProviderErrorType.Configuration => PaymentFailureReason.ProviderConfiguration,
            CanonicalProviderErrorType.Validation => PaymentFailureReason.InvalidRequest,
            CanonicalProviderErrorType.Authentication => PaymentFailureReason.AuthenticationFailed,
            CanonicalProviderErrorType.Authorization => PaymentFailureReason.AuthorizationFailed,
            CanonicalProviderErrorType.RateLimited => PaymentFailureReason.RateLimited,
            CanonicalProviderErrorType.Network => PaymentFailureReason.NetworkFailure,
            CanonicalProviderErrorType.ProviderUnavailable => PaymentFailureReason.ProviderUnavailable,
            CanonicalProviderErrorType.ProviderRejected => PaymentFailureReason.ProviderDeclined,
            CanonicalProviderErrorType.AlreadyCaptured => PaymentFailureReason.AlreadyCaptured,
            CanonicalProviderErrorType.AlreadyRefunded => PaymentFailureReason.AlreadyRefunded,
            CanonicalProviderErrorType.InvalidState => PaymentFailureReason.InvalidState,
            _ => PaymentFailureReason.Unknown
        };
    }
}

public enum PaymentFailureReason
{
    Unknown = 0,
    ProviderConfiguration = 1,
    InvalidRequest = 2,
    AuthenticationFailed = 3,
    AuthorizationFailed = 4,
    RateLimited = 5,
    NetworkFailure = 6,
    ProviderUnavailable = 7,
    ProviderDeclined = 8,
    AlreadyCaptured = 9,
    AlreadyRefunded = 10,
    InvalidState = 11
}

public interface IStripeWebhookVerifier
{
    StripeWebhookVerificationResult Verify(StripeWebhookVerificationRequest request);
}

public sealed class StripeWebhookVerifier : IStripeWebhookVerifier
{
    public StripeWebhookVerificationResult Verify(StripeWebhookVerificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StripeWebhookSignatureVerifier.Verify(new StripeWebhookEvidenceRequest
        {
            RawPayload = request.RawPayload,
            SignatureHeader = request.SignatureHeader,
            WebhookSecret = request.WebhookSecret,
            TimestampToleranceSeconds = request.TimestampToleranceSeconds
        });
    }
}

public sealed record StripeWebhookVerificationRequest(
    string RawPayload,
    string SignatureHeader,
    string WebhookSecret,
    long TimestampToleranceSeconds = StripeWebhookSignatureVerifier.DefaultToleranceSeconds);

public static class StripeProviderServiceCollectionExtensions
{
    public static IServiceCollection AddStripeCanonicalProvider(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IStripePaymentProvider)))
        {
            throw new InvalidOperationException("Stripe canonical provider duplicate registration is not allowed.");
        }

        services.AddSingleton<IStripeCapabilityProvider, StripeCapabilityProvider>();
        services.AddSingleton<IStripeWebhookVerifier, StripeWebhookVerifier>();
        services.AddScoped<IStripePaymentProvider, StripePaymentProvider>();

        return services;
    }
}
