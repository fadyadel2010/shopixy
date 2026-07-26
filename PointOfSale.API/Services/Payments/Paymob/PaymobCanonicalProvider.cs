using System.Collections.ObjectModel;
using System.Net;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.API.Services.Payments.Paymob;

public interface IPaymobPaymentProvider
{
    string ProviderName { get; }

    PaymobCapabilityReport Capabilities { get; }

    Task<PaymobProviderResult> CreateCheckoutAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);

    Task<PaymobProviderResult> CaptureAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);

    Task<PaymobProviderResult> VoidAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);

    Task<PaymobProviderResult> RefundAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);

    Task<PaymobProviderResult> PartialRefundAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);

    Task<PaymobProviderResult> InquiryAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default);

    Task<PaymobWebhookVerifierResult> VerifyWebhookAsync(
        PaymobWebhookVerifierRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PaymobPaymentProvider : IPaymobPaymentProvider
{
    private readonly IPaymobPaymentAdapter _paymentAdapter;
    private readonly IPaymobAdvancedOperationsAdapter _advancedOperationsAdapter;
    private readonly IPaymobCapabilityProvider _capabilityProvider;
    private readonly IPaymobWebhookVerifier _webhookVerifier;

    public PaymobPaymentProvider(
        IPaymobPaymentAdapter paymentAdapter,
        IPaymobAdvancedOperationsAdapter advancedOperationsAdapter,
        IPaymobCapabilityProvider capabilityProvider,
        IPaymobWebhookVerifier webhookVerifier)
    {
        _paymentAdapter = paymentAdapter ?? throw new ArgumentNullException(nameof(paymentAdapter));
        _advancedOperationsAdapter = advancedOperationsAdapter ?? throw new ArgumentNullException(nameof(advancedOperationsAdapter));
        _capabilityProvider = capabilityProvider ?? throw new ArgumentNullException(nameof(capabilityProvider));
        _webhookVerifier = webhookVerifier ?? throw new ArgumentNullException(nameof(webhookVerifier));
    }

    public string ProviderName => PaymobProviderReference.ProviderName;

    public PaymobCapabilityReport Capabilities => _capabilityProvider.GetCapabilities();

    public async Task<PaymobProviderResult> CreateCheckoutAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default)
    {
        var result = await _paymentAdapter.CreateAsync(action, cancellationToken);
        return PaymobProviderResult.FromAdapter(result.Evidence, result.Error);
    }

    public Task<PaymobProviderResult> CaptureAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default) =>
        ExecuteAdvancedOperationAsync(action, PaymobAdvancedOperation.Capture, cancellationToken);

    public Task<PaymobProviderResult> VoidAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default) =>
        ExecuteAdvancedOperationAsync(action, PaymobAdvancedOperation.Void, cancellationToken);

    public Task<PaymobProviderResult> RefundAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default) =>
        ExecuteAdvancedOperationAsync(action, PaymobAdvancedOperation.Refund, cancellationToken);

    public Task<PaymobProviderResult> PartialRefundAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default) =>
        ExecuteAdvancedOperationAsync(action, PaymobAdvancedOperation.PartialRefund, cancellationToken);

    public Task<PaymobProviderResult> InquiryAsync(
        PaymentAction action,
        CancellationToken cancellationToken = default) =>
        ExecuteAdvancedOperationAsync(action, PaymobAdvancedOperation.TransactionInquiry, cancellationToken);

    public Task<PaymobWebhookVerifierResult> VerifyWebhookAsync(
        PaymobWebhookVerifierRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_webhookVerifier.Verify(request));
    }

    private async Task<PaymobProviderResult> ExecuteAdvancedOperationAsync(
        PaymentAction action,
        PaymobAdvancedOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var mappedAction = PaymobRequestMapper.WithOperation(action, operation);
        var result = await _advancedOperationsAdapter.ExecuteAsync(mappedAction, cancellationToken);
        return PaymobProviderResult.FromAdapter(result.Evidence, result.Error);
    }
}

public sealed record PaymobProviderResult
{
    public bool Succeeded { get; init; }

    public ProviderEvidence? Evidence { get; init; }

    public CanonicalProviderError? Error { get; init; }

    public PaymentFailureReason FailureReason { get; init; } = PaymentFailureReason.Unknown;

    public static PaymobProviderResult FromAdapter(
        ProviderEvidence? evidence,
        CanonicalProviderError? error)
    {
        if (error is not null)
        {
            return new PaymobProviderResult
            {
                Succeeded = false,
                Error = error,
                FailureReason = PaymobErrorMapper.ToPaymentFailureReason(error)
            };
        }

        return new PaymobProviderResult
        {
            Succeeded = true,
            Evidence = evidence ?? throw new ArgumentException("Canonical provider evidence is required for a successful Paymob operation.")
        };
    }
}

public static class PaymobRequestMapper
{
    public static PaymobPaymentPreparationRequest ToCheckoutRequest(
        PaymentAction action,
        PaymobTenantRuntimeCredentials credentials)
    {
        return PaymobPaymentRequestFactory.Create(action, credentials);
    }

    public static PaymobAdvancedOperationRequest ToAdvancedOperationRequest(
        PaymentAction action,
        PaymobTenantRuntimeCredentials credentials)
    {
        return PaymobOperationFactory.Create(action, credentials);
    }

    public static PaymentAction WithOperation(PaymentAction action, PaymobAdvancedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (operation == PaymobAdvancedOperation.Unknown)
        {
            throw new ArgumentException("Paymob operation is required.", nameof(operation));
        }

        var metadata = action.Metadata.Values.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        metadata[PaymobOperationMetadataKeys.Operation] = operation switch
        {
            PaymobAdvancedOperation.PartialRefund => "partial_refund",
            PaymobAdvancedOperation.TransactionInquiry => "transaction_inquiry",
            _ => operation.ToString().ToLowerInvariant()
        };

        return action with
        {
            Metadata = new PaymentActionMetadata(metadata)
        };
    }
}

public static class PaymobResponseMapper
{
    public static ProviderEvidence ToProviderEvidence(
        PaymentAction action,
        PaymobPaymentPreparationRequest request,
        PaymobPaymentPreparationResponse response)
    {
        return PaymobEvidenceFactory.FromPaymentPreparation(action, request, response);
    }

    public static ProviderEvidence ToOperationEvidence(
        PaymentAction action,
        PaymobAdvancedOperationRequest request,
        PaymobOperationResponse response)
    {
        return PaymobOperationEvidenceFactory.FromOperationResponse(action, request, response);
    }

    public static PaymobWebhookTranslationResult ToWebhookEvidence(
        PaymentAction action,
        PaymobCallback callback,
        string rawPayload)
    {
        return PaymobWebhookEvidenceTranslator.Translate(action, callback, rawPayload);
    }
}

public static class PaymobErrorMapper
{
    public static CanonicalProviderError ToCanonicalProviderError(PaymobOfficialApiException exception)
    {
        return PaymobProviderErrorTranslator.Translate(exception);
    }

    public static CanonicalProviderError ToCanonicalProviderError(HttpRequestException exception)
    {
        return PaymobProviderErrorTranslator.Translate(exception);
    }

    public static CanonicalProviderError InvalidSignature()
    {
        return new CanonicalProviderError(
            CanonicalProviderErrorType.Authentication,
            "PAYMOB_WEBHOOK_HMAC_INVALID",
            "Paymob HMAC signature is invalid.",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider"] = PaymobProviderReference.ProviderName
            });
    }

    public static CanonicalProviderError UnsupportedCapability(string capability)
    {
        var normalized = string.IsNullOrWhiteSpace(capability) ? "unknown" : capability.Trim();
        return new CanonicalProviderError(
            CanonicalProviderErrorType.Validation,
            "PAYMOB_CAPABILITY_UNSUPPORTED",
            $"Paymob capability '{normalized}' is not supported by the tenant configuration.",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider"] = PaymobProviderReference.ProviderName,
                ["unsupported_capability"] = normalized
            });
    }

    public static PaymentFailureReason ToPaymentFailureReason(CanonicalProviderError error)
    {
        if (error.Code.Contains("HMAC", StringComparison.OrdinalIgnoreCase) ||
            error.Code.Contains("SIGNATURE", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentFailureReason.AuthenticationFailed;
        }

        if (error.Code.Contains("DUPLICATE", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentFailureReason.InvalidState;
        }

        if (error.Code.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentFailureReason.InvalidState;
        }

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

public interface IPaymobCapabilityProvider
{
    PaymobCapabilityReport GetCapabilities();

    PaymobCapabilitySupport GetCapabilitySupport(PaymobCanonicalCapability capability);
}

public sealed class PaymobCapabilityProvider : IPaymobCapabilityProvider
{
    private readonly PaymobCapabilityModel _configuration;

    public PaymobCapabilityProvider(IOptions<PaymobOptions> options)
        : this(options.Value.Capabilities)
    {
    }

    public PaymobCapabilityProvider(PaymobCapabilityModel configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public PaymobCapabilityReport GetCapabilities()
    {
        var support = Enum.GetValues<PaymobCanonicalCapability>()
            .Where(capability => capability != PaymobCanonicalCapability.Unknown)
            .Select(GetCapabilitySupport)
            .ToArray();
        var supported = support
            .Where(item => item.Supported)
            .Select(item => item.Capability)
            .ToHashSet();
        var unsupported = support
            .Where(item => !item.Supported)
            .ToDictionary(item => item.Capability, item => item.Reason, EqualityComparer<PaymobCanonicalCapability>.Default);

        return new PaymobCapabilityReport(
            PaymobProviderReference.ProviderName,
            new ReadOnlySet<PaymobCanonicalCapability>(supported),
            new ReadOnlyDictionary<PaymobCanonicalCapability, string>(unsupported),
            BuildRuntimeCapabilities(supported),
            BuildRuntimeConstraints());
    }

    public PaymobCapabilitySupport GetCapabilitySupport(PaymobCanonicalCapability capability)
    {
        return capability switch
        {
            PaymobCanonicalCapability.Authorize => SupportedIf(_configuration.ProviderAdapterCertified && _configuration.PaymentExecutionCertified, capability, "Payment execution is not certified for this tenant configuration."),
            PaymobCanonicalCapability.Capture => SupportedIf(_configuration.ProviderAdapterCertified, capability, "Provider adapter certification is required for capture translation."),
            PaymobCanonicalCapability.Void => SupportedIf(_configuration.ProviderAdapterCertified, capability, "Provider adapter certification is required for void translation."),
            PaymobCanonicalCapability.Refund => SupportedIf(_configuration.ProviderAdapterCertified, capability, "Provider adapter certification is required for refund translation."),
            PaymobCanonicalCapability.PartialRefund => SupportedIf(_configuration.ProviderAdapterCertified, capability, "Provider adapter certification is required for partial refund translation."),
            PaymobCanonicalCapability.StatusInquiry => SupportedIf(_configuration.ProviderAdapterCertified, capability, "Provider adapter certification is required for inquiry translation."),
            PaymobCanonicalCapability.WebhookProcessing => SupportedIf(_configuration.CallbackCertified && _configuration.SupportsHmacVerification, capability, "Callback certification and HMAC support are required."),
            PaymobCanonicalCapability.HmacVerification => SupportedIf(_configuration.SupportsHmacVerification, capability, "HMAC verification is disabled for this tenant configuration."),
            PaymobCanonicalCapability.OfficialHttpsApi => SupportedIf(_configuration.SupportsOfficialHttpsApi, capability, "Official HTTPS API support is disabled for this tenant configuration."),
            PaymobCanonicalCapability.TenantScopedCredentials => SupportedIf(_configuration.SupportsTenantScopedCredentials, capability, "Tenant-scoped credentials are required."),
            PaymobCanonicalCapability.CapabilityReporting => PaymobCapabilitySupport.CreateSupported(capability),
            _ => PaymobCapabilitySupport.CreateUnsupported(capability, "Capability is unknown.")
        };
    }

    private static PaymobCapabilitySupport SupportedIf(
        bool condition,
        PaymobCanonicalCapability capability,
        string unsupportedReason)
    {
        return condition
            ? PaymobCapabilitySupport.CreateSupported(capability)
            : PaymobCapabilitySupport.CreateUnsupported(capability, unsupportedReason);
    }

    private static ProviderCapability BuildRuntimeCapabilities(IReadOnlySet<PaymobCanonicalCapability> supported)
    {
        var capabilities = ProviderCapability.Redirect
            | ProviderCapability.AsyncConfirmation
            | ProviderCapability.StatusInquiry
            | ProviderCapability.Recovery
            | ProviderCapability.ReplayProtection
            | ProviderCapability.Idempotency
            | ProviderCapability.Metadata
            | ProviderCapability.Audit
            | ProviderCapability.Evidence
            | ProviderCapability.Quarantine
            | ProviderCapability.Reconciliation;

        if (supported.Contains(PaymobCanonicalCapability.Capture))
        {
            capabilities |= ProviderCapability.Capture;
        }

        if (supported.Contains(PaymobCanonicalCapability.Refund))
        {
            capabilities |= ProviderCapability.Refund;
        }

        if (supported.Contains(PaymobCanonicalCapability.PartialRefund))
        {
            capabilities |= ProviderCapability.PartialRefund;
        }

        if (supported.Contains(PaymobCanonicalCapability.Void))
        {
            capabilities |= ProviderCapability.Void;
        }

        return capabilities;
    }

    private static ProviderConstraint BuildRuntimeConstraints()
    {
        return ProviderConstraint.RequiresNotification
            | ProviderConstraint.RequiresMessageAuthentication
            | ProviderConstraint.RequiresProviderSession
            | ProviderConstraint.RequiresEvidenceRetention;
    }
}

public sealed record PaymobCapabilityReport(
    string Provider,
    IReadOnlySet<PaymobCanonicalCapability> SupportedCapabilities,
    IReadOnlyDictionary<PaymobCanonicalCapability, string> UnsupportedCapabilities,
    ProviderCapability RuntimeCapabilities,
    ProviderConstraint RuntimeConstraints);

public sealed record PaymobCapabilitySupport(
    PaymobCanonicalCapability Capability,
    bool Supported,
    string Reason)
{
    public static PaymobCapabilitySupport CreateSupported(PaymobCanonicalCapability capability) =>
        new(capability, true, string.Empty);

    public static PaymobCapabilitySupport CreateUnsupported(PaymobCanonicalCapability capability, string reason) =>
        new(capability, false, reason);
}

public enum PaymobCanonicalCapability
{
    Unknown = 0,
    Authorize = 1,
    Capture = 2,
    Void = 3,
    Refund = 4,
    PartialRefund = 5,
    StatusInquiry = 6,
    WebhookProcessing = 7,
    HmacVerification = 8,
    OfficialHttpsApi = 9,
    TenantScopedCredentials = 10,
    CapabilityReporting = 11
}

public interface IPaymobWebhookVerifier
{
    PaymobWebhookVerifierResult Verify(PaymobWebhookVerifierRequest request);
}

public sealed class PaymobWebhookVerifier : IPaymobWebhookVerifier
{
    public PaymobWebhookVerifierResult Verify(PaymobWebhookVerifierRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var verificationRequest = new PaymobWebhookEvidenceRequest
        {
            RawPayload = request.RawPayload,
            HmacSignature = request.HmacSignature,
            HmacSecret = request.HmacSecret,
            TimestampTolerance = request.TimestampTolerance,
            ReceivedAtUtc = request.ReceivedAtUtc
        };
        var verification = PaymobWebhookHmacVerifier.Verify(verificationRequest);
        if (!verification.Succeeded)
        {
            var error = verification.Error?.Code == "PAYMOB_WEBHOOK_HMAC_INVALID"
                ? PaymobErrorMapper.InvalidSignature()
                : verification.Error!;
            return PaymobWebhookVerifierResult.Failed(error);
        }

        var translation = PaymobResponseMapper.ToWebhookEvidence(
            request.Action,
            verification.Callback!,
            request.RawPayload);
        if (!translation.Succeeded)
        {
            return PaymobWebhookVerifierResult.Failed(translation.Error!);
        }

        return PaymobWebhookVerifierResult.Success(
            translation.Evidence!,
            translation.Translation,
            translation.ProviderError);
    }
}

public sealed record PaymobWebhookVerifierRequest(
    string RawPayload,
    string HmacSignature,
    string HmacSecret,
    PaymentAction Action,
    TimeSpan? TimestampTolerance = null,
    DateTimeOffset? ReceivedAtUtc = null);

public sealed record PaymobWebhookVerifierResult
{
    public bool Succeeded { get; init; }

    public ProviderEvidence? Evidence { get; init; }

    public CanonicalProviderError? Error { get; init; }

    public CanonicalProviderError? ProviderError { get; init; }

    public PaymobWebhookTranslationMetadata Translation { get; init; } = new();

    public PaymentFailureReason FailureReason { get; init; } = PaymentFailureReason.Unknown;

    public static PaymobWebhookVerifierResult Success(
        ProviderEvidence evidence,
        PaymobWebhookTranslationMetadata translation,
        CanonicalProviderError? providerError)
    {
        return new PaymobWebhookVerifierResult
        {
            Succeeded = true,
            Evidence = evidence,
            ProviderError = providerError,
            Translation = translation,
            FailureReason = providerError is null
                ? PaymentFailureReason.Unknown
                : PaymobErrorMapper.ToPaymentFailureReason(providerError)
        };
    }

    public static PaymobWebhookVerifierResult Failed(CanonicalProviderError error)
    {
        return new PaymobWebhookVerifierResult
        {
            Succeeded = false,
            Error = error,
            FailureReason = PaymobErrorMapper.ToPaymentFailureReason(error)
        };
    }
}
