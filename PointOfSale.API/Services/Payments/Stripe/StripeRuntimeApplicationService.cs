using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments;
using PointOfSale.Core.Payments;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;
using Stripe;
using StripePaymentIntent = Stripe.PaymentIntent;

namespace PointOfSale.API.Services.Payments.Stripe;

public interface IStripeRuntimeApplicationService
{
    Task<StripeRuntimeApplicationResult> CreatePaymentIntentAsync(
        StripeRuntimePaymentIntentApplicationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class StripeRuntimeApplicationService : IStripeRuntimeApplicationService
{
    private readonly IStripeTenantConfigurationResolver _configurationResolver;
    private readonly IStripeRuntimeCredentialAccessor _credentialAccessor;
    private readonly IStripeRuntimeBridge _runtimeBridge;
    private readonly ILogger<StripeRuntimeApplicationService> _logger;

    public StripeRuntimeApplicationService(
        IStripeTenantConfigurationResolver configurationResolver,
        IStripeRuntimeCredentialAccessor credentialAccessor,
        IStripeRuntimeBridge runtimeBridge,
        ILogger<StripeRuntimeApplicationService> logger)
    {
        _configurationResolver = configurationResolver;
        _credentialAccessor = credentialAccessor;
        _runtimeBridge = runtimeBridge;
        _logger = logger;
    }

    public async Task<StripeRuntimeApplicationResult> CreatePaymentIntentAsync(
        StripeRuntimePaymentIntentApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var credentials = await ResolveTenantCredentialsAsync(
            request.TenantId,
            cancellationToken);

        _credentialAccessor.SetCredentials(credentials);

        var session = BuildRuntimeSession(request);
        var action = BuildRuntimeAction(request, session);
        var runtimeRequest = StripeExecutionFactory.CreateRequest(
            action,
            session,
            request.ExecutionTimestampUtc,
            new ExecutionMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["application_boundary"] = nameof(StripeRuntimeApplicationService),
                ["tenant_id"] = request.TenantId.ToString("N"),
                ["order_id"] = request.OrderId.ToString("N"),
                ["local_payment_intent_id"] = request.LocalPaymentIntentId.ToString("N")
            }));

        var execution = await _runtimeBridge.ExecuteAsync(runtimeRequest, cancellationToken);
        var artifact = _credentialAccessor.ConsumePaymentIntentArtifact();

        if (execution.ProviderError is not null)
        {
            throw MapProviderError(execution.ProviderError);
        }

        if (!StripeRuntimeOutcomeValidator.IsExpectedClientConfirmation(execution.RuntimeResult))
        {
            _logger.LogError(
                "Stripe Runtime rejected client-confirmation execution | OrderId: {OrderId} | RuntimeStatus: {RuntimeStatus} | FailureStage: {FailureStage} | FailureReason: {FailureReason}",
                request.OrderId,
                execution.RuntimeResult.OverallStatus,
                execution.RuntimeResult.Failure?.FailureStage,
                execution.RuntimeResult.Failure?.FailureReason.Value);

            throw new StripeApiException(
                "Stripe payment creation did not produce the required canonical Runtime outcome.",
                "STRIPE_RUNTIME_OUTCOME_REJECTED",
                "runtime_validation");
        }

        if (artifact is null ||
            string.IsNullOrWhiteSpace(artifact.ClientSecret) ||
            string.IsNullOrWhiteSpace(artifact.StripePaymentIntentId))
        {
            _logger.LogError(
                "Stripe runtime application boundary did not receive required provider artifact | OrderId: {OrderId} | RuntimeStatus: {RuntimeStatus}",
                request.OrderId,
                execution.ExecutionStatus);

            throw new StripeApiException("Stripe PaymentIntent response was missing required application artifacts.");
        }

        return new StripeRuntimeApplicationResult(
            credentials.PublishableKey,
            artifact.ClientSecret,
            artifact.StripePaymentIntentId,
            execution.ProviderEvidence,
            execution.RuntimeResult,
            execution);
    }

    private async Task<StripeTenantRuntimeCredentials> ResolveTenantCredentialsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return await _configurationResolver.ResolveCredentialsAsync(
            tenantId,
            StripeOperationalCapability.PaymentIntent,
            cancellationToken);
    }

    private static PaymentSession BuildRuntimeSession(StripeRuntimePaymentIntentApplicationRequest request)
    {
        var sessionId = new PaymentSessionId(DeterministicGuid(
            "stripe-app-session",
            request.TenantId.ToString("N"),
            request.OrderId.ToString("N"),
            request.LocalPaymentIntentId.ToString("N")));
        var attemptId = new PaymentAttemptId(DeterministicGuid(
            "stripe-app-attempt",
            sessionId.Value.ToString("N"),
            request.LocalPaymentIntentId.ToString("N")));
        var created = new DateTimeOffset(request.CreatedAtUtc, TimeSpan.Zero);

        var attempt = PaymentAttemptFactory.Create(
            attemptId,
            sessionId,
            new AttemptNumber(1),
            created,
            new ProviderReference(StripeProviderReference.ProviderName),
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
            new PaymentReference("STRIPE"),
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
        StripeRuntimePaymentIntentApplicationRequest request,
        PaymentSession session)
    {
        var attempt = session.Attempts.Single();
        var actionId = new PaymentActionId(DeterministicGuid(
            "stripe-app-action",
            session.Id.Value.ToString("N"),
            attempt.Id.Value.ToString("N"),
            request.IdempotencyKey));
        var created = new DateTimeOffset(request.CreatedAtUtc, TimeSpan.Zero);

        return PaymentActionFactory.Create(
            actionId,
            session.Id,
            attempt.Id,
            PaymentActionType.ClientConfirmation,
            PaymentActionStatus.Pending,
            new ActionReference(request.PaymentReference),
            new ActionCorrelationId(request.CorrelationId),
            created,
            request.ExecutionTimestampUtc,
            metadata: new PaymentActionMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [StripeRequestMetadataKeys.Amount] = request.Amount.ToString(CultureInfo.InvariantCulture),
                [StripeRequestMetadataKeys.Currency] = request.Currency,
                [StripeRequestMetadataKeys.Description] = $"Order {request.OrderNumber}",
                [StripeRequestMetadataKeys.PaymentMethodTypes] = "card",
                [StripeRequestMetadataKeys.IdempotencyKey] = request.IdempotencyKey,
                [StripeRequestMetadataKeys.StatementDescriptor] = TruncateForStripe(request.OrderNumber, 22),
                ["orderId"] = request.OrderId.ToString(),
                ["orderNumber"] = request.OrderNumber,
                ["tenantId"] = request.TenantId.ToString(),
                ["customerId"] = request.CustomerId?.ToString() ?? string.Empty,
                ["paymentReference"] = request.PaymentReference,
                ["storefrontBaseUrl"] = request.StorefrontBaseUrl
            }));
    }

    private static StripeApiException MapProviderError(CanonicalProviderError error)
    {
        return new StripeApiException(error.Message, error.Code, error.Type.ToString());
    }

    private static string TruncateForStripe(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
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

public static class StripeRuntimeOutcomeValidator
{
    public static bool IsExpectedClientConfirmation(
        PointOfSale.Core.Payments.Runtime.Results.RuntimeResult runtimeResult)
    {
        ArgumentNullException.ThrowIfNull(runtimeResult);

        return runtimeResult.OverallStatus == RuntimeOverallStatus.Failed
            && runtimeResult.Failure?.FailureStage == FailureStage.Transaction
            && string.Equals(
                runtimeResult.Failure.FailureReason.Value,
                "TRANSACTION_FAILED",
                StringComparison.Ordinal)
            && runtimeResult.AdmissionResult?.Admitted == true
            && runtimeResult.VerificationResult?.Status == VerificationStatus.Verified
            && runtimeResult.DecisionResult?.Status == DecisionStatus.Produced
            && runtimeResult.PaymentDecision?.DecisionType == DecisionType.Approved
            && runtimeResult.TransitionResult?.Status == TransitionStatus.Applied
            && runtimeResult.TransitionResult.Session.Status == PointOfSale.Core.Payments.Runtime.Domain.PaymentStatus.AwaitingCustomer
            && runtimeResult.TransactionResult?.Status == TransactionStatus.Rejected
            && runtimeResult.PaymentTransaction is null
            && runtimeResult.SettlementResult is null
            && runtimeResult.PaymentSettlement is null;
    }
}

public sealed record StripeRuntimePaymentIntentApplicationRequest(
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
    DateTimeOffset ExecutionTimestampUtc);

public sealed record StripeRuntimeApplicationResult(
    string PublishableKey,
    string ClientSecret,
    string StripePaymentIntentId,
    ProviderEvidence ProviderEvidence,
    RuntimeResult RuntimeResult,
    StripeExecutionResult ExecutionResult);

public sealed record StripeTenantRuntimeCredentials(
    Guid TenantId,
    Guid TenantPaymentMethodId,
    string PublishableKey,
    string SecretKey,
    string WebhookSecret,
    bool IsSandbox);

public sealed record StripePaymentIntentProviderArtifact(
    string StripePaymentIntentId,
    string ClientSecret,
    string Status,
    long Amount,
    string Currency);

public interface IStripeRuntimeCredentialAccessor
{
    StripeTenantRuntimeCredentials Current { get; }

    void SetCredentials(StripeTenantRuntimeCredentials credentials);

    void SetPaymentIntentArtifact(StripePaymentIntentProviderArtifact artifact);

    StripePaymentIntentProviderArtifact? ConsumePaymentIntentArtifact();
}

public sealed class StripeRuntimeCredentialAccessor : IStripeRuntimeCredentialAccessor
{
    private StripeTenantRuntimeCredentials? _current;
    private StripePaymentIntentProviderArtifact? _artifact;

    public StripeTenantRuntimeCredentials Current =>
        _current ?? throw new InvalidOperationException("Tenant Stripe runtime credentials were not resolved.");

    public void SetCredentials(StripeTenantRuntimeCredentials credentials)
    {
        _current = credentials;
    }

    public void SetPaymentIntentArtifact(StripePaymentIntentProviderArtifact artifact)
    {
        _artifact = artifact;
    }

    public StripePaymentIntentProviderArtifact? ConsumePaymentIntentArtifact()
    {
        var artifact = _artifact;
        _artifact = null;
        return artifact;
    }
}

public sealed class TenantScopedStripePaymentIntentClient : IStripePaymentIntentClient
{
    private readonly IStripeRuntimeCredentialAccessor _credentialAccessor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<StripeOptions> _stripeOptions;

    public TenantScopedStripePaymentIntentClient(
        IStripeRuntimeCredentialAccessor credentialAccessor,
        IHttpClientFactory httpClientFactory,
        IOptions<StripeOptions> stripeOptions)
    {
        _credentialAccessor = credentialAccessor;
        _httpClientFactory = httpClientFactory;
        _stripeOptions = stripeOptions;
    }

    public async Task<StripePaymentIntent> CreateAsync(
        PaymentIntentCreateOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken)
    {
        var credentials = _credentialAccessor.Current;
        var stripeOptions = _stripeOptions.Value;
        StripeConfiguration.MaxNetworkRetries = stripeOptions.RetryCount;
        StripeConfiguration.EnableTelemetry = stripeOptions.TelemetryEnabled;

        var httpClient = _httpClientFactory.CreateClient(StripeFoundationServiceCollectionExtensions.HttpClientName);
        var stripeClient = new StripeClient(
            credentials.SecretKey,
            httpClient: new SystemNetHttpClient(httpClient));
        var service = new PaymentIntentService(stripeClient);
        var effectiveRequestOptions = new RequestOptions
        {
            ApiKey = credentials.SecretKey,
            IdempotencyKey = requestOptions.IdempotencyKey
        };

        var paymentIntent = await service.CreateAsync(options, effectiveRequestOptions, cancellationToken);
        _credentialAccessor.SetPaymentIntentArtifact(new StripePaymentIntentProviderArtifact(
            paymentIntent.Id,
            paymentIntent.ClientSecret,
            paymentIntent.Status ?? string.Empty,
            paymentIntent.Amount,
            paymentIntent.Currency ?? string.Empty));

        return paymentIntent;
    }
}
