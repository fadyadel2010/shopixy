using PointOfSale.API.DTOs.Payments;

namespace PointOfSale.API.Services.Payments.ProviderContracts;

public sealed record ProviderCode(string Value)
{
    public ProviderCode() : this(string.Empty)
    {
    }

    public string Normalized => Value.Trim().ToUpperInvariant();
}

public sealed record GatewayCode(string Value)
{
    public GatewayCode() : this(string.Empty)
    {
    }

    public string Normalized => Value.Trim().ToUpperInvariant();
}

public sealed record CanonicalPaymentRequest
{
    public Guid TenantId { get; init; }

    public Guid OrderId { get; init; }

    public ProviderCode ProviderCode { get; init; } = new();

    public string? CheckoutToken { get; init; }

    public Guid? CustomerId { get; init; }

    public bool PromotionCalculationFailed { get; init; }

    public string RequestId { get; init; } = string.Empty;

    public string CorrelationId { get; init; } = string.Empty;

    public string? WalletType { get; init; }

    public string? WalletPhoneNumber { get; init; }

    public string? ProofFileKey { get; init; }
}

public sealed record CanonicalPaymentSession
{
    public Guid? PaymentIntentId { get; init; }

    public string? PaymentReference { get; init; }

    public string? RedirectUrl { get; init; }

    public string? ReferenceCode { get; init; }

    public DateTime? ExpiresAtUtc { get; init; }

    public bool IsManualPayment { get; init; }

    public IReadOnlyDictionary<string, string?> CompatibilityFields { get; init; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    public static CanonicalPaymentSession FromPaymentIntent(PaymentIntentResponseDto response) =>
        new()
        {
            PaymentIntentId = response.PaymentIntentId,
            PaymentReference = response.PaymentReference,
            RedirectUrl = response.RedirectUrl,
            ReferenceCode = response.ReferenceCode,
            ExpiresAtUtc = response.ExpiresAtUtc
        };
}

public interface IProviderPaymentGateway
{
    ProviderCode ProviderCode { get; }

    Task<CanonicalPaymentSession> CreateSessionAsync(
        CanonicalPaymentRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPaymentGateway
{
    Task<CanonicalPaymentSession> CreateSessionAsync(
        CanonicalPaymentRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPaymentProviderAdapter
{
    ProviderCode ProviderCode { get; }

    PaymentProviderCapabilities Capabilities { get; }

    Task<PaymentProviderSessionResult> CreateSessionAsync(
        PaymentProviderSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentProviderWebhookResult> ParseWebhookAsync(
        PaymentProviderWebhookRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentProviderStatusResult> GetStatusAsync(
        PaymentProviderStatusRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentProviderCallbackResult> ParseCallbackAsync(
        PaymentProviderCallbackRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromException<PaymentProviderCallbackResult>(
            new PaymentProviderAdapterException(
                CanonicalProviderError.Validation(
                    ProviderCode.Normalized,
                    "PROVIDER_CALLBACK_UNSUPPORTED",
                    "This provider adapter does not support browser callback validation.")));
}

public sealed record PaymentProviderSessionRequest
{
    public Guid PaymentId { get; init; }
    public Guid MarketplaceOrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string ProviderReference { get; init; } = string.Empty;
    public string? ExistingProviderIntentId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? CustomerName { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? AddressLine { get; init; }
    public string? City { get; init; }
    public string? Area { get; init; }
    public string CountryCode { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
}

public sealed record PaymentProviderSessionResult(
    string? PaymentUrl,
    string? PaymentToken,
    string? ProviderIntentId,
    string RawResponse);

public sealed record PaymentProviderWebhookRequest(
    string Signature,
    string RawPayload,
    string CorrelationId);

public sealed record PaymentProviderCallbackRequest(
    IReadOnlyDictionary<string, string> Parameters,
    string CorrelationId);

public sealed record PaymentProviderWebhookResult
{
    public string EventType { get; init; } = string.Empty;
    public string TransactionId { get; init; } = string.Empty;
    public string? ProviderReference { get; init; }
    public string? ProviderIntentId { get; init; }
    public bool Pending { get; init; }
    public bool Success { get; init; }
    public bool AuthorizationOnly { get; init; }
    public bool IsVoided { get; init; }
    public bool IsRefunded { get; init; }
    public long AmountMinorUnits { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
}

public sealed record PaymentProviderCallbackResult
{
    public bool IsValid { get; init; }
    public bool IsSuccess { get; init; }
    public bool IsPending { get; init; }
    public bool IsTerminalFailure { get; init; }
    public string? TransactionId { get; init; }
    public string? ProviderIntentId { get; init; }
    public string? ProviderReference { get; init; }
    public long AmountMinorUnits { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
    public string? Message { get; init; }
}

public sealed record PaymentProviderStatusRequest(
    string ProviderTransactionId,
    string CorrelationId);

public sealed record PaymentProviderStatusResult
{
    public string ProviderTransactionId { get; init; } = string.Empty;
    public string? ProviderIntentId { get; init; }
    public bool Pending { get; init; }
    public bool Success { get; init; }
    public bool IsVoided { get; init; }
    public bool IsRefunded { get; init; }
    public long AmountMinorUnits { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed record PaymentProviderCapabilities(
    bool SupportsAuthentication,
    bool SupportsPaymentIntentionCreation,
    bool SupportsPaymentSessionCreation,
    bool SupportsHostedCheckout,
    bool SupportsPaymentStatusLookup,
    bool SupportsWebhookVerification,
    bool SupportsCallbackParsing,
    bool SupportsRefunds,
    IReadOnlyDictionary<string, string> Metadata);

public sealed class PaymentProviderAdapterException : Exception
{
    public PaymentProviderAdapterException(
        CanonicalProviderError error,
        bool outcomeUnknown = false,
        string? providerIntentId = null,
        Exception? innerException = null)
        : base(error.Message, innerException)
    {
        Error = error;
        OutcomeUnknown = outcomeUnknown;
        ProviderIntentId = providerIntentId;
    }

    public CanonicalProviderError Error { get; }
    public bool OutcomeUnknown { get; }
    public string? ProviderIntentId { get; }
}

public sealed class PaymentGateway : IPaymentGateway
{
    private readonly IReadOnlyDictionary<string, IProviderPaymentGateway> _gateways;

    public PaymentGateway(IEnumerable<IProviderPaymentGateway> gateways)
    {
        _gateways = gateways.ToDictionary(
            gateway => gateway.ProviderCode.Normalized,
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<CanonicalPaymentSession> CreateSessionAsync(
        CanonicalPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var providerCode = request.ProviderCode.Normalized;
        if (string.IsNullOrWhiteSpace(providerCode))
        {
            throw new PaymentProviderNotSupportedException("UNKNOWN");
        }

        if (!_gateways.TryGetValue(providerCode, out var gateway))
        {
            throw new PaymentProviderNotSupportedException(providerCode);
        }

        return gateway.CreateSessionAsync(request, cancellationToken);
    }
}
