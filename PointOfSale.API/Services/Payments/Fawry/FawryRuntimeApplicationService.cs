using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;

namespace PointOfSale.API.Services.Payments.Fawry;

public interface IFawryRuntimeApplicationService
{
    Task<FawryRuntimeApplicationResult> CreatePaymentIntentAsync(
        FawryRuntimePaymentIntentApplicationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class FawryRuntimeApplicationService : IFawryRuntimeApplicationService
{
    private readonly IFawryTenantConfigurationResolver _tenantConfigurationResolver;
    private readonly IFawryRuntimeBridge _runtimeBridge;
    private readonly ILogger<FawryRuntimeApplicationService> _logger;

    public FawryRuntimeApplicationService(
        IFawryTenantConfigurationResolver tenantConfigurationResolver,
        IFawryRuntimeBridge runtimeBridge,
        ILogger<FawryRuntimeApplicationService> logger)
    {
        _tenantConfigurationResolver = tenantConfigurationResolver;
        _runtimeBridge = runtimeBridge;
        _logger = logger;
    }

    public async Task<FawryRuntimeApplicationResult> CreatePaymentIntentAsync(
        FawryRuntimePaymentIntentApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var tenantCredentials = await _tenantConfigurationResolver.ResolveCredentialsAsync(
            request.TenantId,
            FawryOperationalCapability.PaymentExecution,
            cancellationToken);

        var session = BuildRuntimeSession(request);
        var action = BuildRuntimeAction(request, session);
        var oneOperationCredentials = new FawryPaymentCredentials(
            tenantCredentials.MerchantCode,
            tenantCredentials.ApiKey,
            tenantCredentials.SecureKey);
        var runtimeRequest = FawryExecutionFactory.CreateRequest(
            action,
            session,
            oneOperationCredentials,
            request.ExecutionTimestampUtc,
            new FawryExecutionMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["application_boundary"] = nameof(FawryRuntimeApplicationService),
                ["tenant_id"] = request.TenantId.ToString("N"),
                ["order_id"] = request.OrderId.ToString("N"),
                ["local_payment_intent_id"] = request.LocalPaymentIntentId.ToString("N"),
                ["tenant_payment_method_id"] = tenantCredentials.TenantPaymentMethodId.ToString("N")
            }));

        var execution = await _runtimeBridge.ExecuteAsync(runtimeRequest, cancellationToken);

        if (execution.ProviderError is not null)
        {
            throw MapProviderError(execution.ProviderError);
        }

        if (!FawryRuntimeOutcomeValidator.IsExpectedReferencePreparation(execution.RuntimeResult))
        {
            _logger.LogError(
                "Fawry Runtime rejected reference preparation | OrderId: {OrderId} | RuntimeStatus: {RuntimeStatus} | FailureStage: {FailureStage} | FailureReason: {FailureReason}",
                request.OrderId,
                execution.RuntimeResult.OverallStatus,
                execution.RuntimeResult.Failure?.FailureStage,
                execution.RuntimeResult.Failure?.FailureReason.Value);

            throw FawryApplicationException.RuntimeRejected();
        }

        return ProjectExecutionResult(execution);
    }

    private static FawryRuntimeApplicationResult ProjectExecutionResult(FawryExecutionResult execution)
    {
        var metadata = execution.ProviderEvidence.Metadata.Values;
        var referenceCode = FirstRequired(
            metadata,
            execution.ProviderEvidence.Reference.Value,
            "reference_number",
            "provider_reference");
        var providerStatus = Optional(metadata, "provider_status");
        var paymentUrl = Optional(metadata, "payment_url");
        var expiration = ParseOptionalTimestamp(Optional(metadata, "expiration_time"));

        return new FawryRuntimeApplicationResult(
            referenceCode,
            expiration,
            paymentUrl,
            providerStatus,
            execution.ProviderEvidence,
            execution.RuntimeResult,
            execution);
    }

    private static PaymentSession BuildRuntimeSession(FawryRuntimePaymentIntentApplicationRequest request)
    {
        var sessionId = new PaymentSessionId(DeterministicGuid(
            "fawry-app-session",
            request.TenantId.ToString("N"),
            request.OrderId.ToString("N"),
            request.LocalPaymentIntentId.ToString("N")));
        var attemptId = new PaymentAttemptId(DeterministicGuid(
            "fawry-app-attempt",
            sessionId.Value.ToString("N"),
            request.LocalPaymentIntentId.ToString("N")));
        var created = new DateTimeOffset(request.CreatedAtUtc, TimeSpan.Zero);

        var attempt = PaymentAttemptFactory.Create(
            attemptId,
            sessionId,
            new AttemptNumber(1),
            created,
            new ProviderReference(FawryProviderReference.ProviderName),
            PaymentStatus.Pending,
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
            new PaymentReference("FAWRY"),
            new Money(request.Amount, new Currency(request.Currency)),
            PaymentStatus.Pending,
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
        FawryRuntimePaymentIntentApplicationRequest request,
        PaymentSession session)
    {
        var attempt = session.Attempts.Single();
        var actionId = new PaymentActionId(DeterministicGuid(
            "fawry-app-action",
            session.Id.Value.ToString("N"),
            attempt.Id.Value.ToString("N"),
            request.IdempotencyKey));
        var created = new DateTimeOffset(request.CreatedAtUtc, TimeSpan.Zero);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FawryRequestMetadataKeys.Amount] = request.Amount.ToString(CultureInfo.InvariantCulture),
            [FawryRequestMetadataKeys.Currency] = request.Currency,
            [FawryRequestMetadataKeys.MerchantReference] = request.PaymentReference,
            [FawryRequestMetadataKeys.PaymentMethod] = "PayAtFawry",
            [FawryRequestMetadataKeys.Description] = $"Order {request.OrderNumber}",
            [FawryRequestMetadataKeys.ExpirationUtc] = request.ExpirationUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            [FawryRequestMetadataKeys.Language] = request.Language,
            ["orderId"] = request.OrderId.ToString("D"),
            ["orderNumber"] = request.OrderNumber,
            ["tenantId"] = request.TenantId.ToString("D"),
            ["local_payment_intent_id"] = request.LocalPaymentIntentId.ToString("D"),
            ["paymentReference"] = request.PaymentReference,
            ["idempotencyKey"] = request.IdempotencyKey
        };

        AddOptional(metadata, FawryRequestMetadataKeys.CustomerReference, request.CustomerReference);
        AddOptional(metadata, FawryRequestMetadataKeys.CustomerName, request.CustomerName);
        AddOptional(metadata, FawryRequestMetadataKeys.CustomerMobile, request.CustomerMobile);
        AddOptional(metadata, FawryRequestMetadataKeys.CustomerEmail, request.CustomerEmail);
        AddOptional(metadata, FawryRequestMetadataKeys.RedirectUrl, request.ReturnUrl);
        AddOptional(metadata, FawryRequestMetadataKeys.OrderWebHookUrl, request.OrderWebhookUrl);

        return PaymentActionFactory.Create(
            actionId,
            session.Id,
            attempt.Id,
            PaymentActionType.ReferencePayment,
            PaymentActionStatus.Pending,
            new ActionReference(request.PaymentReference),
            new ActionCorrelationId(request.CorrelationId),
            created,
            request.ExecutionTimestampUtc,
            metadata: new PaymentActionMetadata(metadata));
    }

    private static void ValidateRequest(FawryRuntimePaymentIntentApplicationRequest request)
    {
        if (request.TenantId == Guid.Empty ||
            request.OrderId == Guid.Empty ||
            request.LocalPaymentIntentId == Guid.Empty ||
            request.Amount <= 0 ||
            string.IsNullOrWhiteSpace(request.Currency) ||
            string.IsNullOrWhiteSpace(request.PaymentReference) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            string.IsNullOrWhiteSpace(request.CorrelationId) ||
            string.IsNullOrWhiteSpace(request.CustomerMobile) ||
            string.IsNullOrWhiteSpace(request.CustomerEmail) ||
            string.IsNullOrWhiteSpace(request.Language) ||
            request.ExpirationUtc <= request.ExecutionTimestampUtc)
        {
            throw FawryApplicationException.InvalidRequest("Fawry application execution request is invalid.");
        }
    }

    private static FawryApplicationException MapProviderError(CanonicalProviderError error)
    {
        return error.Type switch
        {
            CanonicalProviderErrorType.Authentication => FawryApplicationException.ProviderFailure(
                "FAWRY_PROVIDER_AUTHENTICATION_FAILED", FawryApplicationFailureType.Credentials, false, error),
            CanonicalProviderErrorType.Authorization => FawryApplicationException.ProviderFailure(
                "FAWRY_PROVIDER_AUTHORIZATION_FAILED", FawryApplicationFailureType.Provider, false, error),
            CanonicalProviderErrorType.RateLimited => FawryApplicationException.ProviderFailure(
                "FAWRY_PROVIDER_RATE_LIMITED", FawryApplicationFailureType.Provider, true, error),
            CanonicalProviderErrorType.Network or CanonicalProviderErrorType.ProviderUnavailable => FawryApplicationException.ProviderFailure(
                "FAWRY_PROVIDER_UNAVAILABLE", FawryApplicationFailureType.Provider, true, error),
            CanonicalProviderErrorType.Validation => FawryApplicationException.ProviderFailure(
                "FAWRY_PROVIDER_VALIDATION_FAILED", FawryApplicationFailureType.Provider, false, error),
            CanonicalProviderErrorType.NotSupported => FawryApplicationException.ProviderFailure(
                "FAWRY_PROVIDER_OPERATION_UNSUPPORTED", FawryApplicationFailureType.Capability, false, error),
            _ => FawryApplicationException.ProviderFailure(
                "FAWRY_PROVIDER_REJECTED", FawryApplicationFailureType.Provider, false, error)
        };
    }

    private static string FirstRequired(
        IReadOnlyDictionary<string, string> metadata,
        string fallback,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Optional(metadata, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        throw FawryApplicationException.ProjectionInvalid();
    }

    private static string? Optional(IReadOnlyDictionary<string, string> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static DateTimeOffset? ParseOptionalTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var timestamp))
        {
            throw FawryApplicationException.ProjectionInvalid();
        }

        return timestamp.ToUniversalTime();
    }

    private static void AddOptional(IDictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value.Trim();
        }
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

public static class FawryRuntimeOutcomeValidator
{
    public static bool IsExpectedReferencePreparation(RuntimeResult runtimeResult)
    {
        ArgumentNullException.ThrowIfNull(runtimeResult);

        return runtimeResult.OverallStatus == RuntimeOverallStatus.Failed &&
               runtimeResult.Failure?.FailureStage == FailureStage.Transaction &&
               string.Equals(runtimeResult.Failure.FailureReason.Value, "TRANSACTION_FAILED", StringComparison.Ordinal) &&
               runtimeResult.AdmissionResult?.Admitted == true &&
               runtimeResult.VerificationResult?.Status == VerificationStatus.Verified &&
               runtimeResult.DecisionResult?.Status == DecisionStatus.Produced &&
               runtimeResult.PaymentDecision?.DecisionType == DecisionType.Approved &&
               runtimeResult.TransitionResult?.Status == TransitionStatus.Applied &&
               runtimeResult.TransitionResult.Session.Status == PaymentStatus.AwaitingCustomer &&
               runtimeResult.TransactionResult?.Status == TransactionStatus.Rejected &&
               runtimeResult.PaymentTransaction is null &&
               runtimeResult.SettlementResult is null &&
               runtimeResult.PaymentSettlement is null;
    }
}

public sealed record FawryRuntimePaymentIntentApplicationRequest(
    Guid TenantId,
    Guid OrderId,
    Guid LocalPaymentIntentId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string PaymentReference,
    string IdempotencyKey,
    string CorrelationId,
    DateTime CreatedAtUtc,
    DateTimeOffset ExecutionTimestampUtc,
    DateTimeOffset ExpirationUtc,
    string? CustomerReference = null,
    string? CustomerName = null,
    string? CustomerMobile = null,
    string? CustomerEmail = null,
    string? ReturnUrl = null,
    string? OrderWebhookUrl = null,
    string Language = "en-gb");

public sealed record FawryRuntimeApplicationResult(
    string ReferenceCode,
    DateTimeOffset? ExpiresAtUtc,
    string? PaymentUrl,
    string? ProviderStatus,
    ProviderEvidence ProviderEvidence,
    RuntimeResult RuntimeResult,
    FawryExecutionResult ExecutionResult);

public enum FawryApplicationFailureType
{
    Validation = 0,
    Configuration = 1,
    Capability = 2,
    Environment = 3,
    Credentials = 4,
    Provider = 5,
    Runtime = 6,
    Projection = 7
}

public sealed class FawryApplicationException : Exception
{
    private FawryApplicationException(
        string code,
        string message,
        FawryApplicationFailureType failureType,
        bool retryable,
        string? providerCode = null)
        : base(message)
    {
        Code = code;
        FailureType = failureType;
        Retryable = retryable;
        ProviderCode = providerCode;
    }

    public string Code { get; }

    public FawryApplicationFailureType FailureType { get; }

    public bool Retryable { get; }

    public string? ProviderCode { get; }

    public static FawryApplicationException InvalidRequest(string message) =>
        new("FAWRY_APPLICATION_REQUEST_INVALID", message, FawryApplicationFailureType.Validation, false);

    public static FawryApplicationException ConfigurationMissing() =>
        new("FAWRY_CONFIGURATION_MISSING", "Fawry is not configured for this tenant.", FawryApplicationFailureType.Configuration, false);

    public static FawryApplicationException ConfigurationDeleted() =>
        new("FAWRY_CONFIGURATION_DELETED", "Fawry is not configured for this tenant.", FawryApplicationFailureType.Configuration, false);

    public static FawryApplicationException ProviderDisabled() =>
        new("FAWRY_PROVIDER_DISABLED", "Fawry is disabled for this tenant.", FawryApplicationFailureType.Configuration, false);

    public static FawryApplicationException CapabilityDisabled() =>
        new("FAWRY_CAPABILITY_DISABLED", "Fawry payment execution is not enabled.", FawryApplicationFailureType.Capability, false);

    public static FawryApplicationException EnvironmentMismatch() =>
        new("FAWRY_ENVIRONMENT_MISMATCH", "Fawry tenant environment does not match application environment.", FawryApplicationFailureType.Environment, false);

    public static FawryApplicationException InvalidCredentials() =>
        new("FAWRY_CREDENTIALS_INVALID", "Fawry tenant credentials are invalid.", FawryApplicationFailureType.Credentials, false);

    public static FawryApplicationException RuntimeRejected() =>
        new("FAWRY_RUNTIME_OUTCOME_REJECTED", "Fawry execution did not produce the required canonical Runtime outcome.", FawryApplicationFailureType.Runtime, false);

    public static FawryApplicationException ProjectionInvalid() =>
        new("FAWRY_EXECUTION_PROJECTION_INVALID", "Fawry execution response was missing required application artifacts.", FawryApplicationFailureType.Projection, false);

    public static FawryApplicationException ProviderFailure(
        string code,
        FawryApplicationFailureType failureType,
        bool retryable,
        CanonicalProviderError providerError) =>
        new(code, providerError.Message, failureType, retryable, providerError.Code);
}
