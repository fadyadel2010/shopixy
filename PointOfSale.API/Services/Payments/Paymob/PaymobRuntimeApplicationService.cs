using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;

namespace PointOfSale.API.Services.Payments.Paymob;

public interface IPaymobRuntimeApplicationService
{
    Task<PaymobRuntimeApplicationResult> CreatePaymentIntentAsync(
        PaymobRuntimePaymentIntentApplicationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PaymobRuntimeApplicationService : IPaymobRuntimeApplicationService
{
    private readonly IPaymobTenantConfigurationResolver _tenantConfigurationResolver;
    private readonly IPaymobAdapterCredentialHandoff _adapterCredentialHandoff;
    private readonly IPaymobRuntimeBridge _runtimeBridge;
    private readonly ILogger<PaymobRuntimeApplicationService> _logger;

    public PaymobRuntimeApplicationService(
        IPaymobTenantConfigurationResolver tenantConfigurationResolver,
        IPaymobAdapterCredentialHandoff adapterCredentialHandoff,
        IPaymobRuntimeBridge runtimeBridge,
        ILogger<PaymobRuntimeApplicationService> logger)
    {
        _tenantConfigurationResolver = tenantConfigurationResolver;
        _adapterCredentialHandoff = adapterCredentialHandoff;
        _runtimeBridge = runtimeBridge;
        _logger = logger;
    }

    public async Task<PaymobRuntimeApplicationResult> CreatePaymentIntentAsync(
        PaymobRuntimePaymentIntentApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var credentials = await _tenantConfigurationResolver.ResolveCredentialsAsync(
            request.TenantId,
            PaymobOperationalCapability.PaymentIntent,
            cancellationToken);
        using var credentialHandoff = _adapterCredentialHandoff.Begin(credentials);

        _logger.LogInformation(
            "Paymob payment initialization context resolved | TenantId: {TenantId} | OrderId: {OrderId} | LocalPaymentIntentId: {LocalPaymentIntentId} | IntegrationId: {IntegrationId} | Amount: {Amount} | Currency: {Currency} | CorrelationId: {CorrelationId}",
            request.TenantId,
            request.OrderId,
            request.LocalPaymentIntentId,
            credentials.IntegrationId,
            request.Amount,
            request.Currency,
            request.CorrelationId);

        var session = BuildRuntimeSession(request);
        var action = BuildRuntimeAction(request, session);
        var runtimeRequest = PaymobExecutionFactory.CreateRequest(
            action,
            session,
            request.ExecutionTimestampUtc,
            new ExecutionMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["application_boundary"] = nameof(PaymobRuntimeApplicationService),
                ["tenant_id"] = request.TenantId.ToString("N"),
                ["order_id"] = request.OrderId.ToString("N"),
                ["local_payment_intent_id"] = request.LocalPaymentIntentId.ToString("N")
            }));

        var execution = await _runtimeBridge.ExecuteAsync(runtimeRequest, cancellationToken);

        if (execution.ProviderError is not null)
        {
            throw MapProviderError(execution.ProviderError);
        }

        if (!PaymobRuntimeOutcomeValidator.IsExpectedRedirectPreparation(execution.RuntimeResult))
        {
            _logger.LogError(
                "Paymob Runtime rejected redirect preparation execution | OrderId: {OrderId} | RuntimeStatus: {RuntimeStatus} | FailureStage: {FailureStage} | FailureReason: {FailureReason}",
                request.OrderId,
                execution.RuntimeResult.OverallStatus,
                execution.RuntimeResult.Failure?.FailureStage,
                execution.RuntimeResult.Failure?.FailureReason.Value);

            throw new PaymobApiException(
                "Paymob payment creation did not produce the required canonical Runtime outcome.",
                "PAYMOB_RUNTIME_OUTCOME_REJECTED",
                "runtime_validation");
        }

        var metadata = execution.ProviderEvidence.Metadata.Values;
        var paymobOrderId = RequiredMetadata(metadata, "provider_order_id", "Paymob order id");
        var iframeUrl = RequiredMetadata(metadata, "iframe_url", "iframe URL");
        var paymentKey = ResolveUnifiedCheckoutClientSecret(iframeUrl);

        _logger.LogInformation(
            "Paymob payment initialization handoff materialized | TenantId: {TenantId} | OrderId: {OrderId} | LocalPaymentIntentId: {LocalPaymentIntentId} | IntegrationId: {IntegrationId} | ProviderOrderIdPresent: {ProviderOrderIdPresent} | ClientSecretPresent: {ClientSecretPresent} | HandoffUrlPresent: {HandoffUrlPresent} | CorrelationId: {CorrelationId}",
            request.TenantId,
            request.OrderId,
            request.LocalPaymentIntentId,
            credentials.IntegrationId,
            !string.IsNullOrWhiteSpace(paymobOrderId),
            !string.IsNullOrWhiteSpace(paymentKey),
            !string.IsNullOrWhiteSpace(iframeUrl),
            request.CorrelationId);

        return new PaymobRuntimeApplicationResult(
            iframeUrl,
            paymentKey,
            paymobOrderId,
            execution.ProviderEvidence,
            execution.RuntimeResult,
            execution);
    }

    private static string ResolveUnifiedCheckoutClientSecret(string checkoutUrl)
    {
        if (!Uri.TryCreate(checkoutUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "accept.paymob.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.AbsolutePath.TrimEnd('/'), "/unifiedcheckout", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymobApiException(
                "Paymob payment creation returned an invalid Unified Checkout URL.",
                "PAYMOB_CHECKOUT_URL_INVALID",
                "runtime_validation");
        }

        var values = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        var clientSecret = values.TryGetValue("clientSecret", out var candidate)
            ? candidate.ToString()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new PaymobApiException(
                "Paymob payment creation did not return a Unified Checkout client secret.",
                "PAYMOB_CHECKOUT_SECRET_MISSING",
                "runtime_validation");
        }

        return clientSecret;
    }

    private static PaymentSession BuildRuntimeSession(PaymobRuntimePaymentIntentApplicationRequest request)
    {
        var sessionId = new PaymentSessionId(DeterministicGuid(
            "paymob-app-session",
            request.TenantId.ToString("N"),
            request.OrderId.ToString("N"),
            request.LocalPaymentIntentId.ToString("N")));
        var attemptId = new PaymentAttemptId(DeterministicGuid(
            "paymob-app-attempt",
            sessionId.Value.ToString("N"),
            request.LocalPaymentIntentId.ToString("N")));
        var created = new DateTimeOffset(request.CreatedAtUtc, TimeSpan.Zero);

        var attempt = PaymentAttemptFactory.Create(
            attemptId,
            sessionId,
            new AttemptNumber(1),
            created,
            new ProviderReference(PaymobProviderReference.ProviderName),
            PointOfSale.Core.Payments.Runtime.Domain.PaymentStatus.Pending,
            new FailureReason(string.Empty),
            0,
            new PaymentMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["local_payment_intent_id"] = request.LocalPaymentIntentId.ToString("N")
            }));

        return PaymentSessionFactory.Create(
            sessionId,
            new PaymentId(request.LocalPaymentIntentId),
            request.OrderId,
            request.TenantId,
            request.TenantId,
            new PaymentReference("PAYMOB"),
            new Money(request.Amount, new PointOfSale.Core.Payments.Runtime.Domain.Currency(request.Currency)),
            PointOfSale.Core.Payments.Runtime.Domain.PaymentStatus.Pending,
            created,
            request.ExecutionTimestampUtc,
            new PaymentMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["order_number"] = request.OrderNumber,
                ["payment_reference"] = request.PaymentReference
            }),
            new AuditReferences(new[] { new CorrelationId(request.CorrelationId) }),
            new[] { attempt },
            Array.Empty<ProviderSession>());
    }

    private static PaymentAction BuildRuntimeAction(
        PaymobRuntimePaymentIntentApplicationRequest request,
        PaymentSession session)
    {
        var attempt = session.Attempts.Single();
        var actionId = new PaymentActionId(DeterministicGuid(
            "paymob-app-action",
            session.Id.Value.ToString("N"),
            attempt.Id.Value.ToString("N"),
            request.IdempotencyKey));
        var created = new DateTimeOffset(request.CreatedAtUtc, TimeSpan.Zero);

        return PaymentActionFactory.Create(
            actionId,
            session.Id,
            attempt.Id,
            PaymentActionType.Redirect,
            PaymentActionStatus.Pending,
            new ActionReference(request.PaymentReference),
            new ActionCorrelationId(request.CorrelationId),
            created,
            request.ExecutionTimestampUtc,
            metadata: new PaymentActionMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PaymobRequestMetadataKeys.Amount] = request.Amount.ToString(CultureInfo.InvariantCulture),
                [PaymobRequestMetadataKeys.Currency] = request.Currency,
                [PaymobRequestMetadataKeys.MerchantOrderId] = request.LocalPaymentIntentId.ToString("D"),
                [PaymobRequestMetadataKeys.ItemName] = $"Order {request.OrderNumber}",
                [PaymobRequestMetadataKeys.ExpirationSeconds] = "3600",
                [PaymobRequestMetadataKeys.Email] = request.Email,
                [PaymobRequestMetadataKeys.FirstName] = request.FirstName,
                [PaymobRequestMetadataKeys.LastName] = request.LastName,
                [PaymobRequestMetadataKeys.Street] = request.Street,
                [PaymobRequestMetadataKeys.PhoneNumber] = request.PhoneNumber,
                [PaymobRequestMetadataKeys.PostalCode] = request.PostalCode,
                [PaymobRequestMetadataKeys.City] = request.City,
                [PaymobRequestMetadataKeys.Country] = request.Country,
                [PaymobRequestMetadataKeys.State] = request.State,
                ["orderId"] = request.OrderId.ToString(),
                ["orderNumber"] = request.OrderNumber,
                ["tenantId"] = request.TenantId.ToString(),
                ["local_payment_intent_id"] = request.LocalPaymentIntentId.ToString(),
                ["customerId"] = request.CustomerId?.ToString() ?? string.Empty,
                ["paymentReference"] = request.PaymentReference,
                ["storefrontBaseUrl"] = request.StorefrontBaseUrl,
                ["idempotencyKey"] = request.IdempotencyKey
            }));
    }

    private static PaymobApiException MapProviderError(CanonicalProviderError error)
    {
        return new PaymobApiException(error.Message, error.Code, error.Type.ToString());
    }

    private static string RequiredMetadata(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        string artifactName)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new PaymobApiException($"Paymob response was missing required application artifact: {artifactName}.");
        }

        return value;
    }

    private static Guid DeterministicGuid(params string[] components)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', components)));
        var bytes = hash.Take(16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

public static class PaymobRuntimeOutcomeValidator
{
    public static bool IsExpectedRedirectPreparation(RuntimeResult runtimeResult)
    {
        ArgumentNullException.ThrowIfNull(runtimeResult);

        return runtimeResult.OverallStatus == RuntimeOverallStatus.Failed &&
               runtimeResult.Failure?.FailureStage == FailureStage.Transaction &&
               string.Equals(
                   runtimeResult.Failure.FailureReason.Value,
                   "TRANSACTION_FAILED",
                   StringComparison.Ordinal) &&
               runtimeResult.AdmissionResult?.Admitted == true &&
               runtimeResult.VerificationResult?.Status == VerificationStatus.Verified &&
               runtimeResult.DecisionResult?.Status == DecisionStatus.Produced &&
               runtimeResult.PaymentDecision?.DecisionType == DecisionType.Approved &&
               runtimeResult.TransitionResult?.Status == TransitionStatus.Applied &&
               runtimeResult.TransitionResult.Session.Status == PointOfSale.Core.Payments.Runtime.Domain.PaymentStatus.AwaitingCustomer &&
               runtimeResult.TransactionResult?.Status == TransactionStatus.Rejected &&
               runtimeResult.PaymentTransaction is null &&
               runtimeResult.SettlementResult is null &&
               runtimeResult.PaymentSettlement is null;
    }
}

public sealed record PaymobRuntimePaymentIntentApplicationRequest(
    Guid TenantId,
    Guid OrderId,
    Guid LocalPaymentIntentId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string StorefrontBaseUrl,
    Guid? CustomerId,
    string PaymentReference,
    string IdempotencyKey,
    string CorrelationId,
    DateTime CreatedAtUtc,
    DateTimeOffset ExecutionTimestampUtc,
    string Email,
    string FirstName,
    string LastName,
    string Street,
    string PhoneNumber,
    string PostalCode,
    string City,
    string Country,
    string State);

public sealed record PaymobRuntimeApplicationResult(
    string IframeUrl,
    string PaymentKey,
    string PaymobOrderId,
    ProviderEvidence ProviderEvidence,
    RuntimeResult RuntimeResult,
    PaymobExecutionResult ExecutionResult);

public sealed record PaymobTenantRuntimeCredentials(
    Guid TenantId,
    Guid TenantPaymentMethodId,
    string PublicKey,
    string SecretKey,
    string IntegrationId,
    string HmacSecret,
    bool IsSandbox,
    Uri NotificationUrl,
    Uri RedirectionUrl,
    string? MerchantId = null,
    string? IframeId = null,
    string? ApiKey = null);
