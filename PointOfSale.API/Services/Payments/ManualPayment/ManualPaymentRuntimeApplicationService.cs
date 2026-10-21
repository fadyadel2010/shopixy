using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Payments.ManualPayment;

public enum ManualPaymentOperationalCapability
{
    ReferencePaymentInitialization = 0,
    EvidenceAdmission = 1,
    OperatorApproval = 2,
    OperatorRejection = 3,
    ProofUpload = 4,
    ProofClear = 5,
    PendingReviewRead = 6,
    ProofAccess = 7
}

public interface IManualPaymentCapabilityAdmissionAuthority
{
    Task<ManualPaymentTenantConfiguration> AdmitAsync(
        Guid tenantId,
        ManualPaymentOperationalCapability capability,
        ManualPaymentChannel? requestedChannel = null,
        CancellationToken cancellationToken = default);
}

public interface IManualPaymentTenantConfigurationResolver
{
    Task<ManualPaymentTenantConfiguration> ResolveAsync(
        Guid tenantId,
        ManualPaymentOperationalCapability capability,
        ManualPaymentChannel requestedChannel,
        CancellationToken cancellationToken = default);
}

public interface IManualPaymentRuntimeApplicationService
{
    Task<ManualPaymentRuntimeApplicationResult> InitializeReferencePaymentAsync(
        ManualPaymentRuntimeApplicationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IManualPaymentEvidenceApplicationService
{
    Task<ManualPaymentEvidenceApplicationResult> AdmitEvidenceAsync(
        ManualPaymentEvidenceApplicationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IManualPaymentReviewApplicationService
{
    Task<ManualPaymentReviewApplicationResult> ApproveAsync(
        ManualPaymentReviewApplicationRequest request,
        CancellationToken cancellationToken = default);

    Task<ManualPaymentReviewApplicationResult> RejectAsync(
        ManualPaymentReviewApplicationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IManualPaymentExpirationAuthority
{
    Task ExpireElapsedPendingPaymentsAsync(
        Guid tenantId,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default);

    Task EnsureReviewAdmissionAsync(
        PaymentIntent paymentIntent,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed class ManualPaymentTenantConfigurationResolver :
    IManualPaymentTenantConfigurationResolver,
    IManualPaymentCapabilityAdmissionAuthority
{
    public const string TenantPaymentMethodCode = "MANUALWALLET";

    private readonly ApplicationDbContext _dbContext;
    private readonly IOptions<ManualPaymentOptions> _options;

    public ManualPaymentTenantConfigurationResolver(
        ApplicationDbContext dbContext,
        IOptions<ManualPaymentOptions> options)
    {
        _dbContext = dbContext;
        _options = options;
    }

    public async Task<ManualPaymentTenantConfiguration> ResolveAsync(
        Guid tenantId,
        ManualPaymentOperationalCapability capability,
        ManualPaymentChannel requestedChannel,
        CancellationToken cancellationToken = default)
    {
        return await AdmitAsync(tenantId, capability, requestedChannel, cancellationToken);
    }

    public async Task<ManualPaymentTenantConfiguration> AdmitAsync(
        Guid tenantId,
        ManualPaymentOperationalCapability capability,
        ManualPaymentChannel? requestedChannel = null,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Trusted tenant identity is required.");
        }

        var options = _options.Value;
        var admittedChannel = ResolveRequestedChannel(options, requestedChannel);
        EnsureProviderConfiguration(options, capability, admittedChannel);

        var paymentMethod = await _dbContext.TenantPaymentMethods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(method =>
                method.TenantId == tenantId &&
                method.Code == TenantPaymentMethodCode,
                cancellationToken);

        if (paymentMethod is null)
        {
            throw ManualPaymentApplicationException.ConfigurationMissing();
        }

        if (paymentMethod.IsDeleted)
        {
            throw ManualPaymentApplicationException.ConfigurationDeleted();
        }

        if (!paymentMethod.IsEnabled)
        {
            throw ManualPaymentApplicationException.ProviderDisabled();
        }

        if (!paymentMethod.SupportsOffline)
        {
            throw ManualPaymentApplicationException.CapabilityDisabled();
        }

        return new ManualPaymentTenantConfiguration(
            tenantId,
            paymentMethod.Id,
            admittedChannel,
            options.Channels.Where(channel => channel.Enabled).ToArray(),
            options.Instructions.RequireReferenceNumber,
            options.Instructions.RequireCustomerProof,
            options.Instructions.Configured,
            options.Instructions.DefaultInstructions,
            options.Expiration.Configured,
            options.Expiration.PendingPaymentTtl,
            BuildInstructionVersion(paymentMethod));
    }

    private static void EnsureProviderConfiguration(
        ManualPaymentOptions options,
        ManualPaymentOperationalCapability capability,
        ManualPaymentChannel requestedChannel)
    {
        // Manual Wallet is a backend-owned workflow provider with no gateway, so the
        // runtime admission is decided by the genuine foundation prerequisites (manual
        // channel, customer instructions, customer proof, expiration policy and the
        // certified review/approval/rejection workflows) and by the tenant-owned
        // payment method state. There is no independent deployment-level runtime-enable
        // switch that can suppress an otherwise correctly configured wallet.
        var validation = ManualPaymentConfigurationValidator.Validate(options);
        if (!validation.IsValid)
        {
            throw ManualPaymentApplicationException.InvalidConfiguration(string.Join(" ", validation.Errors));
        }

        if (!IsCapabilitySupported(options, capability) ||
            !options.Capabilities.SupportsTenantScopedConfiguration ||
            !options.Capabilities.RuntimeBridgeCertified ||
            !options.Capabilities.SupportsInstructionConfiguration)
        {
            throw ManualPaymentApplicationException.CapabilityDisabled();
        }

        if (capability == ManualPaymentOperationalCapability.ReferencePaymentInitialization &&
            !options.Capabilities.SupportsExpirationPolicy)
        {
            throw ManualPaymentApplicationException.CapabilityDisabled();
        }

        if (!Enum.IsDefined(requestedChannel) ||
            !IsChannelCapabilitySupported(options.Capabilities, requestedChannel) ||
            !options.Channels.Any(channel => channel.Enabled && channel.Channel == requestedChannel))
        {
            throw ManualPaymentApplicationException.UnsupportedChannel();
        }
    }

    private static bool IsCapabilitySupported(
        ManualPaymentOptions options,
        ManualPaymentOperationalCapability capability) => capability switch
    {
        ManualPaymentOperationalCapability.ReferencePaymentInitialization => options.Capabilities.ProviderAdapterCertified,
        ManualPaymentOperationalCapability.EvidenceAdmission => options.Capabilities.EvidenceAdapterCertified,
        ManualPaymentOperationalCapability.OperatorApproval => options.Capabilities.ReviewWorkflowCertified &&
                                                               options.Capabilities.ApprovalWorkflowCertified &&
                                                               options.FeatureFlags.ReviewExecutionEnabled &&
                                                               options.FeatureFlags.ApprovalExecutionEnabled,
        ManualPaymentOperationalCapability.OperatorRejection => options.Capabilities.ReviewWorkflowCertified &&
                                                               options.Capabilities.ApprovalWorkflowCertified &&
                                                               options.FeatureFlags.ReviewExecutionEnabled &&
                                                               options.FeatureFlags.RejectionExecutionEnabled,
        ManualPaymentOperationalCapability.ProofUpload => options.FeatureFlags.ProofUploadEnabled &&
                                                         options.Capabilities.SupportsReceiptProofUpload &&
                                                         options.Capabilities.EvidenceAdapterCertified,
        ManualPaymentOperationalCapability.ProofClear => options.FeatureFlags.ProofUploadEnabled &&
                                                        options.Capabilities.SupportsReceiptProofUpload &&
                                                        options.Capabilities.EvidenceAdapterCertified,
        ManualPaymentOperationalCapability.PendingReviewRead => options.FeatureFlags.ReviewExecutionEnabled &&
                                                               options.Capabilities.ReviewWorkflowCertified &&
                                                               options.Capabilities.EvidenceAdapterCertified,
        ManualPaymentOperationalCapability.ProofAccess => options.FeatureFlags.ReviewExecutionEnabled &&
                                                         options.Capabilities.ReviewWorkflowCertified &&
                                                         options.Capabilities.EvidenceAdapterCertified,
        _ => false
    };

    private static ManualPaymentChannel ResolveRequestedChannel(
        ManualPaymentOptions options,
        ManualPaymentChannel? requestedChannel)
    {
        if (requestedChannel.HasValue)
        {
            return requestedChannel.Value;
        }

        var enabledChannel = options.Channels.FirstOrDefault(channel => channel.Enabled);
        return enabledChannel is null
            ? ManualPaymentChannel.CustomInstructions
            : enabledChannel.Channel;
    }

    private static bool IsChannelCapabilitySupported(
        ManualPaymentCapabilityModel capabilities,
        ManualPaymentChannel channel) => channel switch
    {
        ManualPaymentChannel.BankTransfer => capabilities.SupportsBankTransfer,
        ManualPaymentChannel.VodafoneCash => capabilities.SupportsVodafoneCash,
        ManualPaymentChannel.Instapay => capabilities.SupportsInstapay,
        ManualPaymentChannel.WalletTransfer => capabilities.SupportsWalletTransfer,
        ManualPaymentChannel.CashDeposit => capabilities.SupportsCashDeposit,
        ManualPaymentChannel.CustomInstructions => capabilities.SupportsCustomInstructions,
        ManualPaymentChannel.OfflineInvoice => capabilities.SupportsOfflineInvoice,
        _ => false
    };

    private static string BuildInstructionVersion(TenantPaymentMethod paymentMethod)
    {
        var updated = paymentMethod.UpdatedAtUtc ?? paymentMethod.UpdatedAt ?? paymentMethod.CreatedAt;
        var source = string.Join(
            '|',
            paymentMethod.Id.ToString("N"),
            paymentMethod.Version.ToString(CultureInfo.InvariantCulture),
            updated.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));

        return $"manual-config-{Hash(source)[..12]}";
    }

    private static string Hash(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
}

internal static class ManualPaymentTelemetryEmitter
{
    public static void EmitFailure(
        IManualPaymentOperationalTelemetryAuthority telemetryAuthority,
        Guid tenantId,
        string operation,
        string operationId,
        ManualPaymentChannel channel,
        ManualPaymentApplicationException exception)
    {
        var eventName = exception.FailureType switch
        {
            ManualPaymentApplicationFailureType.Configuration => ManualPaymentOperationalTelemetry.ConfigurationRejectedEventName,
            ManualPaymentApplicationFailureType.Capability when exception.Code == "MANUAL_PAYMENT_CHANNEL_UNSUPPORTED" =>
                ManualPaymentOperationalTelemetry.ChannelRejectedEventName,
            ManualPaymentApplicationFailureType.Capability => ManualPaymentOperationalTelemetry.CapabilityRejectedEventName,
            ManualPaymentApplicationFailureType.Environment => ManualPaymentOperationalTelemetry.EnvironmentRejectedEventName,
            ManualPaymentApplicationFailureType.Runtime => ManualPaymentOperationalTelemetry.RuntimeRejectedEventName,
            _ => ManualPaymentOperationalTelemetry.RuntimeRejectedEventName
        };

        telemetryAuthority.Emit(new ManualPaymentOperationalTelemetryEvent(
            eventName,
            tenantId,
            operation,
            operationId,
            Channel: ManualPaymentChannelMapper.ToCanonical(channel),
            Capability: operation,
            ReasonCode: exception.Code));
    }
}

public sealed class ManualPaymentRuntimeApplicationService : IManualPaymentRuntimeApplicationService
{
    private readonly IManualPaymentCapabilityAdmissionAuthority _capabilityAdmissionAuthority;
    private readonly IManualPaymentRuntimeBridge _runtimeBridge;
    private readonly IManualPaymentOperationalTelemetryAuthority _telemetryAuthority;
    private readonly ILogger<ManualPaymentRuntimeApplicationService> _logger;

    public ManualPaymentRuntimeApplicationService(
        IManualPaymentCapabilityAdmissionAuthority capabilityAdmissionAuthority,
        IManualPaymentRuntimeBridge runtimeBridge,
        IManualPaymentOperationalTelemetryAuthority telemetryAuthority,
        ILogger<ManualPaymentRuntimeApplicationService> logger)
    {
        _capabilityAdmissionAuthority = capabilityAdmissionAuthority;
        _runtimeBridge = runtimeBridge;
        _telemetryAuthority = telemetryAuthority;
        _logger = logger;
    }

    public async Task<ManualPaymentRuntimeApplicationResult> InitializeReferencePaymentAsync(
        ManualPaymentRuntimeApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        try
        {
            var tenantConfiguration = await _capabilityAdmissionAuthority.AdmitAsync(
                request.TenantId,
                ManualPaymentOperationalCapability.ReferencePaymentInitialization,
                request.Channel,
                cancellationToken);

            var session = BuildRuntimeSession(request);
            var action = BuildRuntimeAction(request, session);
            var expirationUtc = request.ExecutionTimestampUtc.Add(tenantConfiguration.PendingPaymentTtl);
            var canonicalRequest = new ManualPaymentCanonicalRequest(
                request.TenantId,
                request.LocalPaymentIntentId,
                action.Id,
                action.CorrelationId,
                request.Amount,
                request.Currency,
                new ManualPaymentCustomer(request.CustomerReference, request.CustomerEmail, request.CustomerPhone),
                tenantConfiguration.ReferenceNumberRequired,
                tenantConfiguration.ProofRequired,
                tenantConfiguration.Channel,
                tenantConfiguration.SupportedChannels,
                tenantConfiguration.InstructionsConfigured,
                tenantConfiguration.DefaultInstructions,
                tenantConfiguration.ExpirationConfigured,
                expirationUtc,
                tenantConfiguration.InstructionVersion,
                request.ExecutionTimestampUtc);
            var runtimeRequest = ManualPaymentExecutionFactory.CreateRequest(
                action,
                session,
                canonicalRequest,
                request.ExecutionTimestampUtc,
                new ManualPaymentExecutionMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["application_boundary"] = nameof(ManualPaymentRuntimeApplicationService),
                    ["tenant_id"] = request.TenantId.ToString("N"),
                    ["order_id"] = request.OrderId.ToString("N"),
                    ["local_payment_intent_id"] = request.LocalPaymentIntentId.ToString("N"),
                    ["tenant_payment_method_id"] = tenantConfiguration.TenantPaymentMethodId.ToString("N")
                }));

            var execution = await _runtimeBridge.ExecuteAsync(runtimeRequest, cancellationToken);

            if (execution.ProviderError is not null)
            {
                throw MapProviderError(execution.ProviderError);
            }

            if (!ManualPaymentRuntimeOutcomeValidator.IsExpectedReferencePaymentInitialization(execution.RuntimeResult))
            {
                _logger.LogError(
                    "Manual Payment Runtime rejected reference-payment initialization | OrderId: {OrderId} | RuntimeStatus: {RuntimeStatus} | FailureStage: {FailureStage} | FailureReason: {FailureReason}",
                    request.OrderId,
                    execution.RuntimeResult.OverallStatus,
                    execution.RuntimeResult.Failure?.FailureStage,
                    execution.RuntimeResult.Failure?.FailureReason.Value);

                throw ManualPaymentApplicationException.RuntimeRejected();
            }

            _telemetryAuthority.Emit(new ManualPaymentOperationalTelemetryEvent(
                ManualPaymentOperationalTelemetry.InitializedEventName,
                request.TenantId,
                "reference_payment_initialization",
                request.LocalPaymentIntentId.ToString("N"),
                Channel: ManualPaymentChannelMapper.ToCanonical(request.Channel)));

            return ProjectExecutionResult(execution, tenantConfiguration, expirationUtc);
        }
        catch (ManualPaymentApplicationException exception)
        {
            ManualPaymentTelemetryEmitter.EmitFailure(
                _telemetryAuthority,
                request.TenantId,
                "reference_payment_initialization",
                request.LocalPaymentIntentId.ToString("N"),
                request.Channel,
                exception);
            throw;
        }
    }

    private static ManualPaymentRuntimeApplicationResult ProjectExecutionResult(
        ManualPaymentExecutionResult execution,
        ManualPaymentTenantConfiguration tenantConfiguration,
        DateTimeOffset expirationUtc)
    {
        var metadata = execution.ProviderEvidence.Metadata.Values;
        var instructionIdentifier = RequiredMetadata(metadata, "instruction_identifier");
        var channel = RequiredMetadata(metadata, "channel");

        return new ManualPaymentRuntimeApplicationResult(
            instructionIdentifier,
            tenantConfiguration.Channel,
            channel,
            expirationUtc,
            tenantConfiguration.ReferenceNumberRequired,
            tenantConfiguration.ProofRequired,
            execution.ProviderEvidence,
            execution.RuntimeResult,
            execution);
    }

    private static PaymentSession BuildRuntimeSession(ManualPaymentRuntimeApplicationRequest request)
    {
        var sessionId = new PaymentSessionId(DeterministicGuid(
            "manual-app-session",
            request.TenantId.ToString("N"),
            request.OrderId.ToString("N"),
            request.LocalPaymentIntentId.ToString("N")));
        var attemptId = new PaymentAttemptId(DeterministicGuid(
            "manual-app-attempt",
            sessionId.Value.ToString("N"),
            request.LocalPaymentIntentId.ToString("N")));
        var created = new DateTimeOffset(request.CreatedAtUtc, TimeSpan.Zero);
        var attempt = PaymentAttemptFactory.Create(
            attemptId,
            sessionId,
            new AttemptNumber(1),
            created,
            new ProviderReference(ManualPaymentProviderReference.ProviderName),
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
            new PaymentReference("MANUAL_PAYMENT"),
            new Money(request.Amount, new PointOfSale.Core.Payments.Runtime.Domain.Currency(request.Currency)),
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
        ManualPaymentRuntimeApplicationRequest request,
        PaymentSession session)
    {
        var attempt = session.Attempts.Single();
        var actionId = new PaymentActionId(DeterministicGuid(
            "manual-app-action",
            session.Id.Value.ToString("N"),
            attempt.Id.Value.ToString("N"),
            request.IdempotencyKey));
        var created = new DateTimeOffset(request.CreatedAtUtc, TimeSpan.Zero);

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
            metadata: new PaymentActionMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["amount"] = request.Amount.ToString(CultureInfo.InvariantCulture),
                ["currency"] = request.Currency,
                ["orderId"] = request.OrderId.ToString("D"),
                ["orderNumber"] = request.OrderNumber,
                ["tenantId"] = request.TenantId.ToString("D"),
                ["local_payment_intent_id"] = request.LocalPaymentIntentId.ToString("D"),
                ["paymentReference"] = request.PaymentReference,
                ["idempotencyKey"] = request.IdempotencyKey,
                ["manual_channel"] = ManualPaymentChannelMapper.ToCanonical(request.Channel)
            }));
    }

    private static void ValidateRequest(ManualPaymentRuntimeApplicationRequest request)
    {
        if (request.TenantId == Guid.Empty ||
            request.OrderId == Guid.Empty ||
            request.LocalPaymentIntentId == Guid.Empty ||
            request.Amount <= 0m ||
            string.IsNullOrWhiteSpace(request.Currency) ||
            string.IsNullOrWhiteSpace(request.OrderNumber) ||
            string.IsNullOrWhiteSpace(request.PaymentReference) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            string.IsNullOrWhiteSpace(request.CorrelationId) ||
            string.IsNullOrWhiteSpace(request.CustomerReference) ||
            request.ExecutionTimestampUtc == default)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment application execution request is invalid.");
        }
    }

    private static ManualPaymentApplicationException MapProviderError(CanonicalProviderError error)
    {
        return error.Type switch
        {
            CanonicalProviderErrorType.Validation => ManualPaymentApplicationException.ProviderFailure(
                "MANUAL_PAYMENT_PROVIDER_VALIDATION_FAILED", ManualPaymentApplicationFailureType.Provider, false, error),
            CanonicalProviderErrorType.NotSupported => ManualPaymentApplicationException.ProviderFailure(
                "MANUAL_PAYMENT_PROVIDER_OPERATION_UNSUPPORTED", ManualPaymentApplicationFailureType.Capability, false, error),
            CanonicalProviderErrorType.Configuration => ManualPaymentApplicationException.ProviderFailure(
                "MANUAL_PAYMENT_PROVIDER_CONFIGURATION_INVALID", ManualPaymentApplicationFailureType.Configuration, false, error),
            _ => ManualPaymentApplicationException.ProviderFailure(
                "MANUAL_PAYMENT_PROVIDER_REJECTED", ManualPaymentApplicationFailureType.Provider, false, error)
        };
    }

    private static string RequiredMetadata(IReadOnlyDictionary<string, string> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw ManualPaymentApplicationException.ProjectionFailed();
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

public sealed class ManualPaymentEvidenceApplicationService : IManualPaymentEvidenceApplicationService
{
    private readonly IManualPaymentCapabilityAdmissionAuthority _capabilityAdmissionAuthority;
    private readonly IManualPaymentEvidenceAdapter _evidenceAdapter;
    private readonly IManualPaymentRuntimeBridge _runtimeBridge;
    private readonly IManualPaymentOperationalTelemetryAuthority _telemetryAuthority;
    private readonly ILogger<ManualPaymentEvidenceApplicationService> _logger;

    public ManualPaymentEvidenceApplicationService(
        IManualPaymentCapabilityAdmissionAuthority capabilityAdmissionAuthority,
        IManualPaymentEvidenceAdapter evidenceAdapter,
        IManualPaymentRuntimeBridge runtimeBridge,
        IManualPaymentOperationalTelemetryAuthority telemetryAuthority,
        ILogger<ManualPaymentEvidenceApplicationService> logger)
    {
        _capabilityAdmissionAuthority = capabilityAdmissionAuthority;
        _evidenceAdapter = evidenceAdapter;
        _runtimeBridge = runtimeBridge;
        _telemetryAuthority = telemetryAuthority;
        _logger = logger;
    }

    public async Task<ManualPaymentEvidenceApplicationResult> AdmitEvidenceAsync(
        ManualPaymentEvidenceApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        try
        {
            var tenantConfiguration = await _capabilityAdmissionAuthority.AdmitAsync(
                request.TenantId,
                ManualPaymentOperationalCapability.EvidenceAdmission,
                request.Channel,
                cancellationToken);

            var action = BuildRuntimeAction(request);
            var submission = new ManualPaymentEvidenceSubmission(
                request.TenantId,
                request.LocalPaymentIntentId,
                action.Id,
                action.CorrelationId,
                tenantConfiguration.Channel,
                tenantConfiguration.SupportedChannels.Select(channel => channel.Channel).ToArray(),
                tenantConfiguration.ReferenceNumberRequired,
                request.ReferenceNumber,
                request.ProofIdentifier,
                request.ProofMetadata,
                request.SubmittedAtUtc,
                request.ExpiresAtUtc,
                request.CustomerNotes,
                tenantConfiguration.InstructionVersion);

            var adapterResult = _evidenceAdapter.Translate(submission);
            if (adapterResult.Error is not null)
            {
                throw MapProviderError(adapterResult.Error);
            }

            var evidence = adapterResult.Evidence ?? throw ManualPaymentApplicationException.ProjectionFailed();
            if (adapterResult.Rejected)
            {
                throw MapRejectedEvidence(evidence);
            }

            var session = BuildRuntimeSession(request);
            var execution = await _runtimeBridge.ExecuteEvidenceAsync(
                new ManualPaymentEvidenceExecutionRequest(
                    ManualPaymentExecutionFactory.CreateContext(action, session, request.SubmittedAtUtc),
                    action,
                    session,
                    evidence,
                    new ManualPaymentExecutionMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["application_boundary"] = nameof(ManualPaymentEvidenceApplicationService),
                        ["tenant_id"] = request.TenantId.ToString("N"),
                        ["local_payment_intent_id"] = request.LocalPaymentIntentId.ToString("N"),
                        ["tenant_payment_method_id"] = tenantConfiguration.TenantPaymentMethodId.ToString("N")
                    })),
                cancellationToken);

            if (!IsExpectedEvidenceAdmission(execution.RuntimeResult))
            {
                _logger.LogError(
                    "Manual Payment Runtime rejected evidence admission | PaymentIntentId: {PaymentIntentId} | RuntimeStatus: {RuntimeStatus} | FailureStage: {FailureStage} | FailureReason: {FailureReason}",
                    request.LocalPaymentIntentId,
                    execution.RuntimeResult.OverallStatus,
                    execution.RuntimeResult.Failure?.FailureStage,
                    execution.RuntimeResult.Failure?.FailureReason.Value);

                throw ManualPaymentApplicationException.RuntimeRejected();
            }

            _telemetryAuthority.Emit(new ManualPaymentOperationalTelemetryEvent(
                ManualPaymentOperationalTelemetry.EvidenceSubmittedEventName,
                request.TenantId,
                "evidence_admission",
                request.LocalPaymentIntentId.ToString("N"),
                Channel: ManualPaymentChannelMapper.ToCanonical(request.Channel),
                Outcome: "pending_review"));

            return new ManualPaymentEvidenceApplicationResult(evidence, execution.RuntimeResult, execution);
        }
        catch (ManualPaymentApplicationException exception)
        {
            ManualPaymentTelemetryEmitter.EmitFailure(
                _telemetryAuthority,
                request.TenantId,
                "evidence_admission",
                request.LocalPaymentIntentId.ToString("N"),
                request.Channel,
                exception);
            throw;
        }
    }

    private static PaymentSession BuildRuntimeSession(ManualPaymentEvidenceApplicationRequest request)
    {
        var created = request.CreatedAtUtc == default ? request.SubmittedAtUtc.AddMinutes(-1) : request.CreatedAtUtc;
        var sessionId = new PaymentSessionId(DeterministicGuid(
            "manual-evidence-app-session",
            request.TenantId.ToString("N"),
            request.LocalPaymentIntentId.ToString("N")));
        var attemptId = new PaymentAttemptId(DeterministicGuid(
            "manual-evidence-app-attempt",
            sessionId.Value.ToString("N"),
            request.LocalPaymentIntentId.ToString("N")));
        var attempt = PaymentAttemptFactory.Create(
            attemptId,
            sessionId,
            new AttemptNumber(1),
            created,
            new ProviderReference(ManualPaymentProviderReference.ProviderName),
            PaymentStatus.AwaitingCustomer,
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
            new PaymentReference(request.PaymentReference),
            new Money(request.Amount, new PointOfSale.Core.Payments.Runtime.Domain.Currency(request.Currency)),
            PaymentStatus.AwaitingCustomer,
            created,
            request.SubmittedAtUtc,
            new PaymentMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["manual_channel"] = ManualPaymentChannelMapper.ToCanonical(request.Channel)
            }),
            new AuditReferences(new[] { new CorrelationId(request.CorrelationId) }),
            new[] { attempt },
            Array.Empty<ProviderSession>());
    }

    private static PaymentAction BuildRuntimeAction(ManualPaymentEvidenceApplicationRequest request)
    {
        var sessionId = new PaymentSessionId(DeterministicGuid(
            "manual-evidence-app-session",
            request.TenantId.ToString("N"),
            request.LocalPaymentIntentId.ToString("N")));
        var attemptId = new PaymentAttemptId(DeterministicGuid(
            "manual-evidence-app-attempt",
            sessionId.Value.ToString("N"),
            request.LocalPaymentIntentId.ToString("N")));
        var actionId = new PaymentActionId(DeterministicGuid(
            "manual-evidence-app-action",
            sessionId.Value.ToString("N"),
            attemptId.Value.ToString("N"),
            request.ProofIdentifier ?? string.Empty,
            request.CorrelationId));

        return PaymentActionFactory.Create(
            actionId,
            sessionId,
            attemptId,
            PaymentActionType.ReceiptUpload,
            PaymentActionStatus.Submitted,
            new ActionReference(request.PaymentReference),
            new ActionCorrelationId(request.CorrelationId),
            request.CreatedAtUtc == default ? request.SubmittedAtUtc.AddMinutes(-1) : request.CreatedAtUtc,
            request.SubmittedAtUtc,
            metadata: new PaymentActionMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tenantId"] = request.TenantId.ToString("D"),
                ["local_payment_intent_id"] = request.LocalPaymentIntentId.ToString("D"),
                ["paymentReference"] = request.PaymentReference,
                ["manual_channel"] = ManualPaymentChannelMapper.ToCanonical(request.Channel)
            }));
    }

    private static bool IsExpectedEvidenceAdmission(RuntimeResult runtimeResult)
    {
        return runtimeResult.OverallStatus == RuntimeOverallStatus.Failed &&
               runtimeResult.Failure?.FailureStage == FailureStage.Transaction &&
               runtimeResult.AdmissionResult?.Admitted == true &&
               runtimeResult.VerificationResult?.Status == VerificationStatus.Verified &&
               runtimeResult.DecisionResult?.Status == DecisionStatus.Produced &&
               runtimeResult.PaymentDecision?.DecisionType == DecisionType.Approved &&
               runtimeResult.TransitionResult?.Status == TransitionStatus.Applied &&
               runtimeResult.TransitionResult.Session.Status == PaymentStatus.Verified &&
               runtimeResult.TransactionResult?.Status == TransactionStatus.Rejected &&
               runtimeResult.PaymentTransaction is null &&
               runtimeResult.SettlementResult is null &&
               runtimeResult.PaymentSettlement is null;
    }

    private static void ValidateRequest(ManualPaymentEvidenceApplicationRequest request)
    {
        if (request.TenantId == Guid.Empty ||
            request.OrderId == Guid.Empty ||
            request.LocalPaymentIntentId == Guid.Empty ||
            request.Amount <= 0m ||
            string.IsNullOrWhiteSpace(request.Currency) ||
            string.IsNullOrWhiteSpace(request.PaymentReference) ||
            string.IsNullOrWhiteSpace(request.CorrelationId) ||
            request.SubmittedAtUtc == default)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment evidence application request is invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.ProofIdentifier) || request.ProofMetadata.Values.Count == 0)
        {
            throw ManualPaymentApplicationException.InvalidEvidenceProofMissing();
        }
    }

    private static ManualPaymentApplicationException MapRejectedEvidence(ProviderEvidence evidence)
    {
        var code = evidence.Metadata.Values.TryGetValue("provider_error_code", out var value)
            ? value
            : "MANUAL_EVIDENCE_REJECTED";

        return code switch
        {
            "MANUAL_EVIDENCE_PROOF_MISSING" => ManualPaymentApplicationException.InvalidEvidenceProofMissing(),
            "MANUAL_EVIDENCE_REFERENCE_MISSING" => ManualPaymentApplicationException.InvalidEvidenceReferenceMissing(),
            "MANUAL_EVIDENCE_CHANNEL_UNSUPPORTED" => ManualPaymentApplicationException.UnsupportedChannel(),
            _ => ManualPaymentApplicationException.InvalidEvidence(code)
        };
    }

    private static ManualPaymentApplicationException MapProviderError(CanonicalProviderError error)
    {
        return error.Type switch
        {
            CanonicalProviderErrorType.Configuration => ManualPaymentApplicationException.ProviderFailure(
                "MANUAL_PAYMENT_EVIDENCE_CONFIGURATION_INVALID", ManualPaymentApplicationFailureType.Configuration, false, error),
            CanonicalProviderErrorType.Validation => ManualPaymentApplicationException.ProviderFailure(
                "MANUAL_PAYMENT_EVIDENCE_INVALID", ManualPaymentApplicationFailureType.Validation, false, error),
            _ => ManualPaymentApplicationException.ProviderFailure(
                "MANUAL_PAYMENT_EVIDENCE_PROVIDER_REJECTED", ManualPaymentApplicationFailureType.Provider, false, error)
        };
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

public static class ManualPaymentRuntimeOutcomeValidator
{
    public static bool IsExpectedReferencePaymentInitialization(RuntimeResult runtimeResult)
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

public sealed record ManualPaymentTenantConfiguration(
    Guid TenantId,
    Guid TenantPaymentMethodId,
    ManualPaymentChannel Channel,
    IReadOnlyList<ManualPaymentChannelConfiguration> SupportedChannels,
    bool ReferenceNumberRequired,
    bool ProofRequired,
    bool InstructionsConfigured,
    string DefaultInstructions,
    bool ExpirationConfigured,
    TimeSpan PendingPaymentTtl,
    string InstructionVersion);

public sealed record ManualPaymentRuntimeApplicationRequest(
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
    ManualPaymentChannel Channel,
    string CustomerReference,
    string? CustomerEmail = null,
    string? CustomerPhone = null);

public sealed record ManualPaymentRuntimeApplicationResult(
    string InstructionIdentifier,
    ManualPaymentChannel Channel,
    string CanonicalChannel,
    DateTimeOffset ExpiresAtUtc,
    bool ReferenceNumberRequired,
    bool ProofRequired,
    ProviderEvidence ProviderEvidence,
    RuntimeResult RuntimeResult,
    ManualPaymentExecutionResult ExecutionResult);

public sealed record ManualPaymentEvidenceApplicationRequest(
    Guid TenantId,
    Guid OrderId,
    Guid LocalPaymentIntentId,
    string PaymentReference,
    decimal Amount,
    string Currency,
    string CorrelationId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset SubmittedAtUtc,
    ManualPaymentChannel Channel,
    string? ReferenceNumber,
    string? ProofIdentifier,
    ManualPaymentProofMetadata ProofMetadata,
    DateTimeOffset? ExpiresAtUtc = null,
    string? CustomerNotes = null);

public sealed record ManualPaymentEvidenceApplicationResult(
    ProviderEvidence ProviderEvidence,
    RuntimeResult RuntimeResult,
    ManualPaymentExecutionResult ExecutionResult);

public sealed class ManualPaymentReviewApplicationService : IManualPaymentReviewApplicationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IManualPaymentCapabilityAdmissionAuthority _capabilityAdmissionAuthority;
    private readonly IManualPaymentExpirationAuthority _expirationAuthority;
    private readonly IManualPaymentRetentionAuthority _retentionAuthority;
    private readonly IManualPaymentDurableEvidenceAuthority _durableEvidenceAuthority;
    private readonly IManualPaymentRuntimeBridge _runtimeBridge;
    private readonly IPaymentLifecycleService _paymentLifecycleService;
    private readonly IManualPaymentOperationalTelemetryAuthority _telemetryAuthority;
    private readonly ILogger<ManualPaymentReviewApplicationService> _logger;

    public ManualPaymentReviewApplicationService(
        ApplicationDbContext dbContext,
        IManualPaymentCapabilityAdmissionAuthority capabilityAdmissionAuthority,
        IManualPaymentExpirationAuthority expirationAuthority,
        IManualPaymentRetentionAuthority retentionAuthority,
        IManualPaymentDurableEvidenceAuthority durableEvidenceAuthority,
        IManualPaymentRuntimeBridge runtimeBridge,
        IPaymentLifecycleService paymentLifecycleService,
        IManualPaymentOperationalTelemetryAuthority telemetryAuthority,
        ILogger<ManualPaymentReviewApplicationService> logger)
    {
        _dbContext = dbContext;
        _capabilityAdmissionAuthority = capabilityAdmissionAuthority;
        _expirationAuthority = expirationAuthority;
        _retentionAuthority = retentionAuthority;
        _durableEvidenceAuthority = durableEvidenceAuthority;
        _runtimeBridge = runtimeBridge;
        _paymentLifecycleService = paymentLifecycleService;
        _telemetryAuthority = telemetryAuthority;
        _logger = logger;
    }

    public Task<ManualPaymentReviewApplicationResult> ApproveAsync(
        ManualPaymentReviewApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        return ReviewAsync(request, ManualPaymentReviewOutcome.Approved, cancellationToken);
    }

    public Task<ManualPaymentReviewApplicationResult> RejectAsync(
        ManualPaymentReviewApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment rejection requires a reason.");
        }

        return ReviewAsync(request, ManualPaymentReviewOutcome.Rejected, cancellationToken);
    }

    private async Task<ManualPaymentReviewApplicationResult> ReviewAsync(
        ManualPaymentReviewApplicationRequest request,
        ManualPaymentReviewOutcome outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var operation = outcome == ManualPaymentReviewOutcome.Approved
            ? "operator_approval"
            : "operator_rejection";
        var telemetryChannel = ManualPaymentChannel.CustomInstructions;

        try
        {
            var paymentIntent = await _dbContext.PaymentIntents
                .AsNoTracking()
                .Include(intent => intent.Order)
                .FirstOrDefaultAsync(intent =>
                    intent.Id == request.PaymentIntentId &&
                    intent.TenantId == request.TenantId &&
                    intent.Provider == PointOfSale.Core.Enums.PaymentProvider.ManualWallet,
                    cancellationToken)
                ?? throw ManualPaymentApplicationException.InvalidRequest("Manual Payment intent was not found.");

            if (paymentIntent.Order is null || paymentIntent.Order.TenantId != request.TenantId)
            {
                throw ManualPaymentApplicationException.InvalidRequest("Manual Payment order authority is invalid.");
            }

            if (paymentIntent.Status != PointOfSale.Core.Enums.PaymentIntentStatus.AwaitingVerification ||
                paymentIntent.Order.PaymentStatus != PointOfSale.Core.Enums.PaymentStatus.AwaitingVerification)
            {
                throw ManualPaymentApplicationException.InvalidRequest("Manual Payment is not awaiting operator review.");
            }

            await _expirationAuthority.EnsureReviewAdmissionAsync(paymentIntent, request.ReviewedAtUtc, cancellationToken);

            if (string.IsNullOrWhiteSpace(paymentIntent.ProofImageUrl))
            {
                throw ManualPaymentApplicationException.InvalidEvidenceProofMissing();
            }

            _durableEvidenceAuthority.ValidateReceiptEvidence(
                paymentIntent,
                ManualPaymentDurableEvidencePurpose.OperatorReview);

            var channel = ResolveManualPaymentChannel(paymentIntent.WalletType);
            telemetryChannel = channel;
            await _capabilityAdmissionAuthority.AdmitAsync(
                request.TenantId,
                outcome == ManualPaymentReviewOutcome.Approved
                    ? ManualPaymentOperationalCapability.OperatorApproval
                    : ManualPaymentOperationalCapability.OperatorRejection,
                channel,
                cancellationToken);

            var retention = _retentionAuthority.ClassifyAndRetain(
                outcome == ManualPaymentReviewOutcome.Approved
                    ? ManualPaymentRetentionArtifactClass.ApprovedProof
                    : ManualPaymentRetentionArtifactClass.RejectedProof,
                request.ReviewedAtUtc);

            var action = BuildRuntimeAction(request, paymentIntent, outcome);
            var session = BuildRuntimeSession(request, paymentIntent);
            var evidence = BuildOperatorEvidence(request, paymentIntent, action, outcome);
            var execution = await _runtimeBridge.ExecuteEvidenceAsync(
                new ManualPaymentEvidenceExecutionRequest(
                    ManualPaymentExecutionFactory.CreateContext(action, session, request.ReviewedAtUtc),
                    action,
                    session,
                    evidence,
                    new ManualPaymentExecutionMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["application_boundary"] = nameof(ManualPaymentReviewApplicationService),
                        ["tenant_id"] = request.TenantId.ToString("N"),
                        ["payment_intent_id"] = request.PaymentIntentId.ToString("N"),
                        ["operator_outcome"] = outcome.ToString()
                    })),
                cancellationToken);

            if (!IsExpectedReviewAdmission(execution.RuntimeResult))
            {
                _logger.LogError(
                    "Manual Payment Runtime rejected operator review | PaymentIntentId: {PaymentIntentId} | Outcome: {Outcome} | RuntimeStatus: {RuntimeStatus} | FailureStage: {FailureStage} | FailureReason: {FailureReason}",
                    request.PaymentIntentId,
                    outcome,
                    execution.RuntimeResult.OverallStatus,
                    execution.RuntimeResult.Failure?.FailureStage,
                    execution.RuntimeResult.Failure?.FailureReason.Value);

                throw ManualPaymentApplicationException.RuntimeRejected();
            }

            var lifecycleResult = await _paymentLifecycleService.HandlePaymentResultAsync(
                new HandlePaymentResultRequest
                {
                    TenantId = request.TenantId,
                    OrderId = paymentIntent.OrderId,
                    PaymentIntentId = paymentIntent.Id,
                    Outcome = outcome == ManualPaymentReviewOutcome.Approved
                        ? PaymentLifecycleOutcome.Succeeded
                        : PaymentLifecycleOutcome.Rejected,
                    Source = outcome == ManualPaymentReviewOutcome.Approved
                        ? PaymentLifecycleSource.PaymentReviewApproval
                        : PaymentLifecycleSource.PaymentReviewRejection,
                    PaymentReference = paymentIntent.PaymentReference,
                    ProviderPaymentId = paymentIntent.ProviderPaymentId ?? paymentIntent.PaymentReference,
                    Amount = paymentIntent.Amount,
                    Currency = paymentIntent.Currency,
                    Reason = request.Reason,
                    Actor = request.Actor,
                    ProcessedAtUtc = request.ReviewedAtUtc.UtcDateTime,
                    ReviewProofRetentionExpiresAtUtc = retention.ExpiresAtUtc,
                    IgnoreQueryFilters = true
                },
                cancellationToken);

            if (!lifecycleResult.IsSuccess)
            {
                throw ManualPaymentApplicationException.InvalidRequest(
                    lifecycleResult.ErrorMessage ?? "Manual Payment lifecycle admission was denied.");
            }

            _telemetryAuthority.Emit(new ManualPaymentOperationalTelemetryEvent(
                ManualPaymentOperationalTelemetry.EvidenceSubmittedEventName,
                request.TenantId,
                operation,
                request.PaymentIntentId.ToString("N"),
                Channel: ManualPaymentChannelMapper.ToCanonical(channel),
                Outcome: outcome == ManualPaymentReviewOutcome.Approved ? "approved" : "rejected"));

            return new ManualPaymentReviewApplicationResult(evidence, execution.RuntimeResult, execution, lifecycleResult);
        }
        catch (ManualPaymentApplicationException exception)
        {
            ManualPaymentTelemetryEmitter.EmitFailure(
                _telemetryAuthority,
                request.TenantId,
                operation,
                request.PaymentIntentId.ToString("N"),
                telemetryChannel,
                exception);
            throw;
        }
    }

    private static void ValidateRequest(ManualPaymentReviewApplicationRequest request)
    {
        if (request.TenantId == Guid.Empty ||
            request.PaymentIntentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Actor) ||
            request.ReviewedAtUtc == default)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment review request is invalid.");
        }
    }

    private static PaymentSession BuildRuntimeSession(
        ManualPaymentReviewApplicationRequest request,
        PaymentIntent paymentIntent)
    {
        var created = new DateTimeOffset(paymentIntent.CreatedAt, TimeSpan.Zero);
        var sessionId = new PaymentSessionId(DeterministicGuid(
            "manual-review-app-session",
            paymentIntent.TenantId.ToString("N"),
            paymentIntent.Id.ToString("N")));
        var attemptId = new PaymentAttemptId(DeterministicGuid(
            "manual-review-app-attempt",
            sessionId.Value.ToString("N"),
            paymentIntent.Id.ToString("N")));
        var attempt = PaymentAttemptFactory.Create(
            attemptId,
            sessionId,
            new AttemptNumber(1),
            created,
            new ProviderReference(ManualPaymentProviderReference.ProviderName),
            PaymentStatus.Verified,
            new FailureReason(string.Empty),
            0,
            new PaymentMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["local_payment_intent_id"] = paymentIntent.Id.ToString("N")
            }));

        return PaymentSessionFactory.Create(
            sessionId,
            new PaymentId(paymentIntent.Id),
            paymentIntent.OrderId,
            paymentIntent.TenantId,
            paymentIntent.TenantId,
            new PaymentReference(paymentIntent.PaymentReference),
            new Money(paymentIntent.Amount, new PointOfSale.Core.Payments.Runtime.Domain.Currency(paymentIntent.Currency)),
            PaymentStatus.Verified,
            created,
            request.ReviewedAtUtc,
            new PaymentMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["manual_channel"] = ManualPaymentChannelMapper.ToCanonical(ResolveManualPaymentChannel(paymentIntent.WalletType))
            }),
            new AuditReferences(new[] { new CorrelationId(request.CorrelationId) }),
            new[] { attempt },
            Array.Empty<ProviderSession>());
    }

    private static PaymentAction BuildRuntimeAction(
        ManualPaymentReviewApplicationRequest request,
        PaymentIntent paymentIntent,
        ManualPaymentReviewOutcome outcome)
    {
        var sessionId = new PaymentSessionId(DeterministicGuid(
            "manual-review-app-session",
            paymentIntent.TenantId.ToString("N"),
            paymentIntent.Id.ToString("N")));
        var attemptId = new PaymentAttemptId(DeterministicGuid(
            "manual-review-app-attempt",
            sessionId.Value.ToString("N"),
            paymentIntent.Id.ToString("N")));
        var actionId = new PaymentActionId(DeterministicGuid(
            "manual-review-app-action",
            sessionId.Value.ToString("N"),
            attemptId.Value.ToString("N"),
            outcome.ToString(),
            request.CorrelationId));

        return PaymentActionFactory.Create(
            actionId,
            sessionId,
            attemptId,
            PaymentActionType.OperatorReview,
            PaymentActionStatus.Submitted,
            new ActionReference(paymentIntent.PaymentReference),
            new ActionCorrelationId(request.CorrelationId),
            request.ReviewedAtUtc,
            request.ReviewedAtUtc,
            metadata: new PaymentActionMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tenantId"] = request.TenantId.ToString("D"),
                ["local_payment_intent_id"] = paymentIntent.Id.ToString("D"),
                ["paymentReference"] = paymentIntent.PaymentReference,
                ["operator_outcome"] = outcome.ToString()
            }));
    }

    private static ProviderEvidence BuildOperatorEvidence(
        ManualPaymentReviewApplicationRequest request,
        PaymentIntent paymentIntent,
        PaymentAction action,
        ManualPaymentReviewOutcome outcome)
    {
        var reference = string.Concat("manual_payment:operator_review:", paymentIntent.Id.ToString("N"), ":", outcome);
        var hashSource = string.Join(
            "|",
            paymentIntent.TenantId.ToString("N"),
            paymentIntent.Id.ToString("N"),
            action.Id.Value.ToString("N"),
            request.CorrelationId,
            reference,
            outcome,
            request.Actor,
            request.ReviewedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        return ProviderEvidenceFactory.Create(
            new EvidenceId(DeterministicGuid("manual-review-evidence", hashSource)),
            action.Id,
            EvidenceType.OperatorDecision,
            EvidenceStatus.Recorded,
            new EvidenceReference(reference),
            new EvidenceSource(ManualPaymentProviderReference.ProviderName),
            new EvidenceTimestamp(request.ReviewedAtUtc),
            new EvidenceHash(ManualPaymentProviderReference.Hash(hashSource)),
            new ProviderPayloadReference(string.Concat("manual_payment:operator_review:", paymentIntent.Id.ToString("N"))),
            action.CorrelationId,
            new EvidenceMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider_status"] = "OperatorReviewed",
                ["canonical_status"] = "OperatorReview",
                ["operator_outcome"] = outcome.ToString(),
                ["tenant_id"] = paymentIntent.TenantId.ToString("N"),
                ["payment_id"] = paymentIntent.Id.ToString("N"),
                ["actor_present"] = (!string.IsNullOrWhiteSpace(request.Actor)).ToString(CultureInfo.InvariantCulture),
                ["reason_present"] = (!string.IsNullOrWhiteSpace(request.Reason)).ToString(CultureInfo.InvariantCulture)
            }));
    }

    private static bool IsExpectedReviewAdmission(RuntimeResult runtimeResult)
    {
        return runtimeResult.OverallStatus == RuntimeOverallStatus.Succeeded &&
               runtimeResult.AdmissionResult?.Admitted == true &&
               runtimeResult.VerificationResult?.Status == VerificationStatus.Verified &&
               runtimeResult.DecisionResult?.Status == DecisionStatus.Produced &&
               runtimeResult.PaymentDecision?.DecisionType == DecisionType.Approved &&
               runtimeResult.TransitionResult?.Status == TransitionStatus.Applied &&
               runtimeResult.TransitionResult.Session.Status == PaymentStatus.Approved &&
               runtimeResult.TransactionResult?.Status == TransactionStatus.Created &&
               runtimeResult.PaymentTransaction is not null;
    }

    private static ManualPaymentChannel ResolveManualPaymentChannel(string? walletType)
    {
        return walletType?.Trim().Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant() switch
        {
            "VODAFONECASH" => ManualPaymentChannel.VodafoneCash,
            "INSTAPAY" => ManualPaymentChannel.Instapay,
            "ORANGECASH" or "WALLETTRANSFER" or "WALLET" => ManualPaymentChannel.WalletTransfer,
            "BANKTRANSFER" => ManualPaymentChannel.BankTransfer,
            "CASHDEPOSIT" => ManualPaymentChannel.CashDeposit,
            "OFFLINEINVOICE" => ManualPaymentChannel.OfflineInvoice,
            "CUSTOMINSTRUCTIONS" => ManualPaymentChannel.CustomInstructions,
            _ => ManualPaymentChannel.WalletTransfer
        };
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

public sealed class ManualPaymentExpirationAuthority : IManualPaymentExpirationAuthority
{
    private const string ExpiredReason = "Manual Payment expired before operator review.";

    private readonly ApplicationDbContext _dbContext;
    private readonly IPaymentLifecycleService _paymentLifecycleService;

    public ManualPaymentExpirationAuthority(
        ApplicationDbContext dbContext,
        IPaymentLifecycleService paymentLifecycleService)
    {
        _dbContext = dbContext;
        _paymentLifecycleService = paymentLifecycleService;
    }

    public async Task ExpireElapsedPendingPaymentsAsync(
        Guid tenantId,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || observedAtUtc == default)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment expiration admission request is invalid.");
        }

        var observedUtc = observedAtUtc.UtcDateTime;
        var expiredOrderIds = await _dbContext.PaymentIntents
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(intent =>
                intent.TenantId == tenantId &&
                intent.Provider == PointOfSale.Core.Enums.PaymentProvider.ManualWallet &&
                !intent.IsDeleted &&
                intent.ExpiresAtUtc.HasValue &&
                intent.ExpiresAtUtc.Value <= observedUtc &&
                (intent.Status == PointOfSale.Core.Enums.PaymentIntentStatus.Pending ||
                 intent.Status == PointOfSale.Core.Enums.PaymentIntentStatus.AwaitingVerification))
            .OrderBy(intent => intent.ExpiresAtUtc)
            .Select(intent => intent.OrderId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var orderId in expiredOrderIds)
        {
            var result = await _paymentLifecycleService.MarkPaymentExpiredAsync(
                tenantId,
                orderId,
                ExpiredReason,
                cancellationToken);

            if (!result.IsSuccess && !result.WasIdempotent)
            {
                throw ManualPaymentApplicationException.InvalidRequest(
                    result.ErrorMessage ?? "Manual Payment expiration lifecycle admission was denied.");
            }
        }
    }

    public async Task EnsureReviewAdmissionAsync(
        PaymentIntent paymentIntent,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paymentIntent);

        var failure = ValidateExpiration(paymentIntent, observedAtUtc);
        if (failure is null)
        {
            return;
        }

        if (failure == ManualPaymentExpirationFailure.Expired)
        {
            var result = await _paymentLifecycleService.MarkPaymentExpiredAsync(
                paymentIntent.TenantId,
                paymentIntent.OrderId,
                ExpiredReason,
                cancellationToken);

            if (!result.IsSuccess && !result.WasIdempotent)
            {
                throw ManualPaymentApplicationException.InvalidRequest(
                    result.ErrorMessage ?? "Manual Payment expiration lifecycle admission was denied.");
            }
        }

        throw failure switch
        {
            ManualPaymentExpirationFailure.Missing => ManualPaymentApplicationException.ExpirationMissing(),
            ManualPaymentExpirationFailure.Invalid => ManualPaymentApplicationException.ExpirationInvalid(),
            ManualPaymentExpirationFailure.ClockInconsistent => ManualPaymentApplicationException.ClockInconsistent(),
            _ => ManualPaymentApplicationException.Expired()
        };
    }

    private static ManualPaymentExpirationFailure? ValidateExpiration(
        PaymentIntent paymentIntent,
        DateTimeOffset observedAtUtc)
    {
        if (!paymentIntent.ExpiresAtUtc.HasValue)
        {
            return ManualPaymentExpirationFailure.Missing;
        }

        var expiresAtUtc = paymentIntent.ExpiresAtUtc.Value;
        if (expiresAtUtc == default)
        {
            return ManualPaymentExpirationFailure.Invalid;
        }

        if (paymentIntent.CreatedAt != default && expiresAtUtc <= paymentIntent.CreatedAt)
        {
            return ManualPaymentExpirationFailure.ClockInconsistent;
        }

        if (observedAtUtc.UtcDateTime < paymentIntent.CreatedAt)
        {
            return ManualPaymentExpirationFailure.ClockInconsistent;
        }

        if (expiresAtUtc <= observedAtUtc.UtcDateTime)
        {
            return ManualPaymentExpirationFailure.Expired;
        }

        return null;
    }

    private enum ManualPaymentExpirationFailure
    {
        Missing,
        Invalid,
        ClockInconsistent,
        Expired
    }
}

public enum ManualPaymentReviewOutcome
{
    Approved = 0,
    Rejected = 1
}

public sealed record ManualPaymentReviewApplicationRequest(
    Guid TenantId,
    Guid PaymentIntentId,
    string Actor,
    string CorrelationId,
    DateTimeOffset ReviewedAtUtc,
    string? Reason = null);

public sealed record ManualPaymentReviewApplicationResult(
    ProviderEvidence ProviderEvidence,
    RuntimeResult RuntimeResult,
    ManualPaymentExecutionResult ExecutionResult,
    PaymentTransitionResult LifecycleResult);

public enum ManualPaymentApplicationFailureType
{
    Validation = 0,
    Configuration = 1,
    Capability = 2,
    Environment = 3,
    Provider = 4,
    Runtime = 5,
    Projection = 6
}

public sealed class ManualPaymentApplicationException : Exception
{
    private ManualPaymentApplicationException(
        string code,
        string message,
        ManualPaymentApplicationFailureType failureType,
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

    public ManualPaymentApplicationFailureType FailureType { get; }

    public bool Retryable { get; }

    public string? ProviderCode { get; }

    public static ManualPaymentApplicationException InvalidRequest(string message) =>
        new("MANUAL_PAYMENT_REQUEST_INVALID", message, ManualPaymentApplicationFailureType.Validation, false);

    public static ManualPaymentApplicationException ConfigurationMissing() =>
        new("MANUAL_PAYMENT_CONFIGURATION_MISSING", "Manual Payment tenant configuration is missing.", ManualPaymentApplicationFailureType.Configuration, false);

    public static ManualPaymentApplicationException ConfigurationDeleted() =>
        new("MANUAL_PAYMENT_CONFIGURATION_DELETED", "Manual Payment tenant configuration is deleted.", ManualPaymentApplicationFailureType.Configuration, false);

    public static ManualPaymentApplicationException ProviderDisabled() =>
        new("MANUAL_PAYMENT_PROVIDER_DISABLED", "Manual Payment is disabled for the tenant.", ManualPaymentApplicationFailureType.Configuration, false);

    public static ManualPaymentApplicationException CapabilityDisabled() =>
        new("MANUAL_PAYMENT_CAPABILITY_DISABLED", "Manual Payment capability is not enabled for this operation.", ManualPaymentApplicationFailureType.Capability, false);

    public static ManualPaymentApplicationException UnsupportedChannel() =>
        new("MANUAL_PAYMENT_CHANNEL_UNSUPPORTED", "Manual Payment channel is not supported by the tenant configuration.", ManualPaymentApplicationFailureType.Capability, false);

    public static ManualPaymentApplicationException InvalidConfiguration(string reason = "Manual Payment configuration is invalid.") =>
        new("MANUAL_PAYMENT_CONFIGURATION_INVALID", reason, ManualPaymentApplicationFailureType.Configuration, false);

    public static ManualPaymentApplicationException RuntimeRejected() =>
        new("MANUAL_PAYMENT_RUNTIME_OUTCOME_REJECTED", "Manual Payment initialization did not produce the required canonical Runtime outcome.", ManualPaymentApplicationFailureType.Runtime, false);

    public static ManualPaymentApplicationException InvalidEvidenceProofMissing() =>
        new("MANUAL_PAYMENT_EVIDENCE_PROOF_MISSING", "Manual Payment evidence proof metadata is missing.", ManualPaymentApplicationFailureType.Validation, false);

    public static ManualPaymentApplicationException InvalidEvidenceReferenceMissing() =>
        new("MANUAL_PAYMENT_EVIDENCE_REFERENCE_MISSING", "Manual Payment required evidence reference is missing.", ManualPaymentApplicationFailureType.Validation, false);

    public static ManualPaymentApplicationException InvalidEvidence(string providerCode) =>
        new("MANUAL_PAYMENT_EVIDENCE_INVALID", "Manual Payment evidence was rejected.", ManualPaymentApplicationFailureType.Validation, false, providerCode);

    public static ManualPaymentApplicationException ExpirationMissing() =>
        new("MANUAL_PAYMENT_EXPIRATION_MISSING", "Manual Payment expiration is missing.", ManualPaymentApplicationFailureType.Validation, false);

    public static ManualPaymentApplicationException ExpirationInvalid() =>
        new("MANUAL_PAYMENT_EXPIRATION_INVALID", "Manual Payment expiration is invalid.", ManualPaymentApplicationFailureType.Validation, false);

    public static ManualPaymentApplicationException ClockInconsistent() =>
        new("MANUAL_PAYMENT_CLOCK_INCONSISTENT", "Manual Payment expiration clock authority is inconsistent.", ManualPaymentApplicationFailureType.Validation, false);

    public static ManualPaymentApplicationException Expired() =>
        new("MANUAL_PAYMENT_EXPIRED", "Manual Payment is expired and cannot be reviewed.", ManualPaymentApplicationFailureType.Validation, false);

    public static ManualPaymentApplicationException ProjectionFailed() =>
        new("MANUAL_PAYMENT_PROJECTION_FAILED", "Manual Payment initialization result is missing required projection metadata.", ManualPaymentApplicationFailureType.Projection, false);

    public static ManualPaymentApplicationException ProviderFailure(
        string code,
        ManualPaymentApplicationFailureType failureType,
        bool retryable,
        CanonicalProviderError error) =>
        new(code, error.Message, failureType, retryable, error.Code);
}
