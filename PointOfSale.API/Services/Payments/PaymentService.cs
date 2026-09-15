using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Events;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Security;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.API.Services.Payments.ManualPayment;

namespace PointOfSale.API.Services.Payments;

/// <summary>
/// Implementation of Payment service.
/// 
/// PHASE 4.1: PAYMENTS API
/// 
/// ARCHITECTURAL BOUNDARY (STRICT):
/// - Order is Source of Truth
/// - PaymentIntent is child of Order
/// - Webhooks are source of truth for online payments
/// - COD has NO gateway, NO webhook
/// - Payments are idempotent
/// - Inventory confirmation happens after payment success OR COD
/// - No refunds in this phase
/// </summary>
public class PaymentService : IPaymentService
{
    private sealed class ManualWalletProviderMetadata
    {
        public string? MerchantWalletNumber { get; set; }
        public string? WalletInstructions { get; set; }
    }

    private readonly ApplicationDbContext _dbContext;
    private readonly RedisConnectionFactory _redisFactory;
    private readonly RedisSettings _redisSettings;
    private readonly IEventPublisher _eventPublisher;
    private readonly IEnumerable<IPaymentAdapter> _paymentAdapters;
    private readonly IManualPaymentRuntimeApplicationService? _manualPaymentRuntimeApplicationService;
    private readonly IManualPaymentEvidenceApplicationService? _manualPaymentEvidenceApplicationService;
    private readonly IManualPaymentRetentionAuthority? _manualPaymentRetentionAuthority;
    private readonly IManualPaymentDurableEvidenceAuthority? _manualPaymentDurableEvidenceAuthority;
    private readonly IFawryRuntimeApplicationService? _fawryRuntimeApplicationService;
    private readonly IFawryProviderCheckpointStore? _fawryProviderCheckpointStore;
    private readonly IPaymentLifecycleService _paymentLifecycleService;
    private readonly IDataProtector _dataProtector;
    private readonly R2Options _r2Options;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<PaymentService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    // =============================================================================
    // PAYMENT CONFIGURATION CONSTANTS
    // =============================================================================

    /// <summary>
    /// Webhook idempotency TTL in hours
    /// </summary>
    private const int WebhookIdempotencyTtlHours = 72;

    /// <summary>
    /// COD provider name
    /// </summary>
    private const string CodProvider = "cod";
    private const string FawryDefaultLanguage = "en-gb";
    private const string DataProtectorPurpose = "PaymentMethodSecrets";
    private const string ActiveIntentUniqueIndexName = "IX_PaymentIntents_Active_OrderId_Provider_Unique";
    private static readonly PaymentIntentStatus[] ActiveIntentStatuses =
    {
        PaymentIntentStatus.Pending,
        PaymentIntentStatus.Authorized,
        PaymentIntentStatus.Paid,
        PaymentIntentStatus.AwaitingVerification
    };

    private enum OrderRequestIdempotencyClaimStatus
    {
        Claimed = 0,
        Replay = 1,
        InProgress = 2,
        PayloadConflict = 3
    }

    private sealed record OrderRequestIdempotencyClaimResult(
        OrderRequestIdempotencyClaimStatus Status,
        OrderRequestIdempotency? Record = null,
        CodConfirmResponseDto? ReplayResponse = null,
        string? ConflictMessage = null);

    public PaymentService(
        ApplicationDbContext dbContext,
        RedisConnectionFactory redisFactory,
        IOptions<RedisSettings> redisSettings,
        IEventPublisher eventPublisher,
        IEnumerable<IPaymentAdapter> paymentAdapters,
        IPaymentLifecycleService paymentLifecycleService,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<R2Options> r2Options,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<PaymentService> logger,
        IFawryRuntimeApplicationService? fawryRuntimeApplicationService = null,
        IFawryProviderCheckpointStore? fawryProviderCheckpointStore = null,
        IManualPaymentRuntimeApplicationService? manualPaymentRuntimeApplicationService = null,
        IManualPaymentEvidenceApplicationService? manualPaymentEvidenceApplicationService = null,
        IManualPaymentRetentionAuthority? manualPaymentRetentionAuthority = null,
        IManualPaymentDurableEvidenceAuthority? manualPaymentDurableEvidenceAuthority = null)
    {
        _dbContext = dbContext;
        _redisFactory = redisFactory;
        _redisSettings = redisSettings.Value;
        _eventPublisher = eventPublisher;
        _paymentAdapters = paymentAdapters;
        _manualPaymentRuntimeApplicationService = manualPaymentRuntimeApplicationService;
        _manualPaymentEvidenceApplicationService = manualPaymentEvidenceApplicationService;
        _manualPaymentDurableEvidenceAuthority = manualPaymentDurableEvidenceAuthority;
        _fawryRuntimeApplicationService = fawryRuntimeApplicationService;
        _fawryProviderCheckpointStore = fawryProviderCheckpointStore;
        _manualPaymentRetentionAuthority = manualPaymentRetentionAuthority;
        _paymentLifecycleService = paymentLifecycleService;
        _dataProtector = dataProtectionProvider.CreateProtector(DataProtectorPurpose);
        _r2Options = r2Options.Value;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    #region Create Payment Intent

    /// <inheritdoc />
    public async Task<PaymentIntentResponseDto> CreatePaymentIntentAsync(
        Guid tenantId,
        Guid orderId,
        CreatePaymentIntentRequestDto request,
        bool promotionCalculationFailed,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        DomainGuard.NonEmpty(tenantId, "PAYMENT_TENANT_REQUIRED", "Payment intent creation requires a valid tenant.");
        DomainGuard.NonEmpty(orderId, "PAYMENT_ORDER_REQUIRED", "Payment intent creation requires a valid order.");
        DomainGuard.Required(requestId, "PAYMENT_REQUEST_ID_REQUIRED", "Payment intent creation requires a request identifier.");
        correlationId = DomainGuard.Required(correlationId, "PAYMENT_CORRELATION_REQUIRED", "Payment intent creation requires a correlation identifier.");
        DomainGuard.Against(request == null, "PAYMENT_REQUEST_REQUIRED", "Payment intent creation requires a request payload.");

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "CreatePaymentIntent starting | TenantId: {TenantId} | OrderId: {OrderId} | RequestId: {RequestId} | CorrelationId: {CorrelationId}",
            tenantId, orderId, requestId, correlationId);

        // CRITICAL: Start transaction and acquire row lock on Order before checking PaymentIntents
        // to prevent race condition where concurrent payment workers create duplicate PaymentIntents
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        // Step 1: Load and validate order with row lock (tenant-scoped for security)
        var order = await OrderLockingHelper.LoadOrderWithLockAsync(
            _dbContext,
            tenantId,
            orderId,
            cancellationToken);

        if (order == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new PaymentOrderNotFoundException(orderId);
        }

        await EnsureOrderCustomerBelongsToTenantAsync(order, tenantId, correlationId, cancellationToken);

        var provider = DetermineProvider(order.PaymentMethod, request.Provider);

        // T-09: Fawry has no certified financial settlement authority. Reject
        // before any order, reservation, intent, checkpoint, provider call, or
        // outbox mutation.
        if (provider == PaymentProvider.Fawry ||
            FawryFinancialAuthorityPolicy.IsFawry(order.PaymentMethodCode) ||
            FawryFinancialAuthorityPolicy.IsFawry(order.PaymentGateway))
        {
            await transaction.RollbackAsync(cancellationToken);
            _reliabilityTelemetry.RecordPaymentIntentRejected(
                "fawry_financial_authority_unavailable",
                provider.ToString());
            throw new PaymentFawryFinancialAuthorityException();
        }

        var validatedCurrency = await EnsurePaymentCurrencyConsistencyAsync(
            tenantId,
            orderId,
            order.Currency,
            provider.ToString(),
            cancellationToken);

        // Validate order status
        if (order.Status != OrderStatus.Pending || order.PaymentStatus != PaymentStatus.Pending)
        {
            await transaction.RollbackAsync(cancellationToken);
            _reliabilityTelemetry.RecordPaymentIntentRejected("non_pending_state", provider.ToString());
            throw new PaymentOrderInvalidStateException(
                orderId, 
                $"Order:{order.Status}/Payment:{order.PaymentStatus}", 
                "Order:Pending/Payment:Pending",
                "PAYMENT_ALREADY_FINALIZED");
        }

        await CheckoutInventoryAuthorityGuard.EnsureStorefrontOrderReservationsActiveAsync(
            _dbContext,
            order,
            cancellationToken);

        // Re-check for existing PaymentIntents while holding the Order row lock
        var existingActiveIntent = await FindExistingActiveIntentAsync(
            tenantId,
            orderId,
            provider,
            cancellationToken);

        if (existingActiveIntent != null)
        {
            if (existingActiveIntent.Status != PaymentIntentStatus.Paid &&
                string.Equals(existingActiveIntent.IdempotencyKey, requestId, StringComparison.Ordinal))
            {
                if (IsProviderInitiationIncomplete(existingActiveIntent))
                {
                    await transaction.RollbackAsync(cancellationToken);

                    var resumedResponse = await EnsurePersistedPaymentIntentReadyAsync(
                        tenantId,
                        orderId,
                        existingActiveIntent.Id,
                        provider,
                        promotionCalculationFailed,
                        requestId,
                        correlationId,
                        cancellationToken);

                    _logger.LogInformation(
                        "CreatePaymentIntent replay resumed persisted intent authority | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | Provider: {Provider} | CorrelationId: {CorrelationId}",
                        tenantId,
                        orderId,
                        existingActiveIntent.Id,
                        provider,
                        correlationId);

                    return resumedResponse;
                }
                else
                {
                    // No changes needed, just release the lock
                    await transaction.RollbackAsync(cancellationToken);
                }

                _logger.LogInformation(
                    "CreatePaymentIntent replay matched existing active intent | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | Provider: {Provider} | CorrelationId: {CorrelationId}",
                    tenantId,
                    orderId,
                    existingActiveIntent.Id,
                    provider,
                    correlationId);

                return BuildExistingPaymentIntentResponse(order, existingActiveIntent);
            }

            await transaction.RollbackAsync(cancellationToken);
            _reliabilityTelemetry.RecordRejectedDuplicateIntent(
                provider.ToString(),
                existingActiveIntent.Status.ToString());

            throw new PaymentOrderInvalidStateException(
                orderId,
                $"Provider:{provider}/Intent:{existingActiveIntent.Status}",
                "NoExistingActiveIntent",
                "DUPLICATE_PAYMENT_INTENT");
        }

        var isCod = provider == PaymentProvider.Cod;
        // Step 4: Create PaymentIntent
        var paymentReference = GeneratePaymentReference(orderId);
        var paymentIntentId = Guid.NewGuid();
        var paymentIntentCreatedAtUtc = DateTime.UtcNow;
        string? normalizedManualWalletProofFileKey = null;
        DateTime? manualWalletProofRetentionExpiresAtUtc = null;

        if (provider == PaymentProvider.ManualWallet)
        {
            normalizedManualWalletProofFileKey = NormalizeManualWalletProofFileKeyOrThrow(tenantId, request.ProofFileKey);
            manualWalletProofRetentionExpiresAtUtc = ResolveManualPaymentProofRetentionDeadline(
                ManualPaymentRetentionArtifactClass.SubmittedProof,
                paymentIntentCreatedAtUtc);
        }

        string? manualWalletProviderMetadata = null;

        // SECURITY: Derive merchant wallet details from tenant payment settings (never from request)
        if (provider == PaymentProvider.ManualWallet)
        {
            var walletDetails = await DeriveManualWalletDetailsAsync(
                tenantId,
                request.WalletType,
                order.Total,
                validatedCurrency,
                correlationId,
                cancellationToken);

            // FAIL CLOSED: Manual wallet requires trusted merchant metadata
            if (walletDetails == null)
            {
                _logger.LogError(
                    "Manual wallet payment failed: merchant wallet details not configured | " +
                    "TenantId: {TenantId} | OrderId: {OrderId} | WalletType: {WalletType} | CorrelationId: {CorrelationId}",
                    tenantId, orderId, request.WalletType, correlationId);

                throw new PaymentManualWalletNotConfiguredException(tenantId);
            }

            manualWalletProviderMetadata = JsonSerializer.Serialize(
                new ManualWalletProviderMetadata
                {
                    MerchantWalletNumber = walletDetails.Value.MerchantWalletNumber,
                    WalletInstructions = walletDetails.Value.WalletInstructions
                },
                _jsonOptions);
        }

        // Step 5: Save PaymentIntent before any provider call. This makes provider
        // initiation a resumable step and prevents external payment state from
        // existing without a durable POS PaymentIntent authority record.
        var paymentIntent = provider == PaymentProvider.ManualWallet
            ? await _paymentLifecycleService.CreateReviewablePaymentIntentAsync(
                new ReviewablePaymentIntentCreateRequest
                {
                    Id = paymentIntentId,
                    OrderId = orderId,
                    TenantId = tenantId,
                    Amount = order.Total,
                    Currency = validatedCurrency,
                    PaymentMethod = order.PaymentMethod,
                    ProviderCode = PaymentProvider.ManualWallet.ToString(),
                    PaymentReference = paymentReference,
                    IdempotencyKey = requestId,
                    CreatedAtUtc = paymentIntentCreatedAtUtc,
                    WalletType = request.WalletType,
                    WalletPhoneNumber = request.WalletPhoneNumber,
                    ProofImageUrl = normalizedManualWalletProofFileKey,
                    ProofImageRetentionExpiresAtUtc = manualWalletProofRetentionExpiresAtUtc,
                    ProviderMetadata = manualWalletProviderMetadata
                },
                cancellationToken)
            : new PaymentIntent
            {
                Id = paymentIntentId,
                OrderId = orderId,
                TenantId = tenantId,
                Amount = order.Total,
                Currency = validatedCurrency,
                PaymentMethod = order.PaymentMethod,
                Provider = provider,
                PaymentReference = paymentReference,
                IdempotencyKey = requestId,
                Status = isCod ? PaymentIntentStatus.Skipped : PaymentIntentStatus.Pending,
                CreatedAt = paymentIntentCreatedAtUtc
            };

        if (provider != PaymentProvider.ManualWallet)
        {
            _dbContext.PaymentIntents.Add(paymentIntent);
        }

        // Step 6: For COD, confirm order immediately
        using var orderStatusScope = isCod
            ? OrderStatusSaveChangesInterceptor.BeginAuthorizedContext()
            : null;
        if (isCod)
        {
            order.Status = OrderStatus.Confirmed;
            order.PaymentReference = paymentReference;
            order.PaymentGateway = CodProvider;

            _logger.LogInformation(
                "COD order confirmed | OrderId: {OrderId} | OrderNumber: {OrderNumber} | CorrelationId: {CorrelationId}",
                orderId, order.OrderNumber, correlationId);
        }
        else
        {
            order.PaymentReference = paymentReference;
            order.PaymentGateway = provider.ToString();
        }

        if (!isCod)
        {
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsActiveIntentUniqueViolation(ex))
            {
                _reliabilityTelemetry.RecordRejectedDuplicateIntent(provider.ToString(), "concurrent_insert");
                _dbContext.ChangeTracker.Clear();

                throw new PaymentOrderInvalidStateException(
                    orderId,
                    $"Provider:{provider}/Intent:concurrent_insert",
                    "NoExistingActiveIntent",
                    "DUPLICATE_PAYMENT_INTENT");
            }

            await transaction.CommitAsync(cancellationToken);

            var finalizedResponse = await EnsurePersistedPaymentIntentReadyAsync(
                tenantId,
                orderId,
                paymentIntent.Id,
                provider,
                promotionCalculationFailed,
                requestId,
                correlationId,
                cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "CreatePaymentIntent succeeded after durable authority commit | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | Provider: {Provider} | DurationMs: {DurationMs} | CorrelationId: {CorrelationId}",
                tenantId,
                orderId,
                paymentIntent.Id,
                provider,
                stopwatch.ElapsedMilliseconds,
                correlationId);

            return finalizedResponse;
        }

        // Step 7: Stage integration events before the state mutation is committed so
        // the payment intent, order mutation, and outbox records persist atomically.
        await EmitPaymentIntentCreatedEventAsync(
            tenantId, orderId, paymentIntent.Id, order.OrderNumber,
            paymentReference, order.PaymentMethod.ToString(), provider.ToString(),
            order.Total, validatedCurrency, paymentIntent.RedirectUrl, isCod,
            promotionCalculationFailed,
            requestId, correlationId, cancellationToken);

        if (provider == PaymentProvider.ManualWallet)
        {
            await EmitManualPaymentSubmittedEventAsync(
                tenantId, orderId, paymentIntent.Id, order.OrderNumber,
                paymentIntent.WalletType ?? "Unknown", 
                !string.IsNullOrWhiteSpace(paymentIntent.ProofImageUrl),
                requestId, correlationId, cancellationToken);

            // Audit Timeline: Manual wallet submitted
            await _eventPublisher.PublishAsync(new OrderStatusChangedEvent
            {
                TenantId = tenantId,
                OrderId = orderId,
                OrderNumber = order.OrderNumber,
                OldStatus = order.Status,
                NewStatus = order.Status,
                Reason = "Manual wallet payment submitted for review",
                CorrelationId = correlationId,
                IdempotencyKey = requestId,
                OccurredAtUtc = DateTime.UtcNow
            }, cancellationToken);

            // WhatsApp: Notify admin of payment submission
            await EmitWhatsAppPaymentApprovalRequestedAsync(
                tenantId, orderId, order.OrderNumber, paymentIntent.Id,
                order.Total, validatedCurrency, order.PaymentMethod.ToString(),
                paymentIntent.WalletType, correlationId, cancellationToken);
        }

        if (isCod)
        {
            await EmitPaymentSkippedEventAsync(
                tenantId, orderId, paymentIntent.Id, order.OrderNumber,
                order.PaymentMethod.ToString(), order.Total, validatedCurrency,
                requestId, correlationId, cancellationToken);

            await EmitOrderConfirmedEventAsync(
                tenantId, orderId, paymentIntent.Id, order.OrderNumber,
                order.PaymentMethod.ToString(), CodProvider, order.Total, validatedCurrency,
                "CashOnDelivery", OrderStatus.Confirmed,
                requestId, correlationId, cancellationToken);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsActiveIntentUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            _reliabilityTelemetry.RecordRejectedDuplicateIntent(provider.ToString(), "concurrent_insert");
            _dbContext.ChangeTracker.Clear();

            throw new PaymentOrderInvalidStateException(
                orderId,
                $"Provider:{provider}/Intent:concurrent_insert",
                "NoExistingActiveIntent",
                "DUPLICATE_PAYMENT_INTENT");
        }

        var response = new PaymentIntentResponseDto
        {
            PaymentIntentId = paymentIntent.Id,
            OrderId = orderId,
            OrderNumber = order.OrderNumber,
            Amount = paymentIntent.Amount,
            Currency = paymentIntent.Currency,
            PaymentMethod = order.PaymentMethod.ToString(),
            Provider = provider.ToString(),
            Status = paymentIntent.Status.ToString(),
            PaymentReference = paymentReference,
            ProviderIntentId = paymentIntent.ProviderIntentId,
            RedirectUrl = paymentIntent.RedirectUrl,
            IsCod = isCod,
            ReferenceCode = paymentIntent.ReferenceCode,
            ExpiresAtUtc = paymentIntent.ExpiresAtUtc,
            CreatedAt = paymentIntent.CreatedAt
        };

        stopwatch.Stop();

        _logger.LogInformation(
            "CreatePaymentIntent completed | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | Provider: {Provider} | IsCod: {IsCod} | DurationMs: {DurationMs} | CorrelationId: {CorrelationId}",
            tenantId, orderId, paymentIntent.Id, provider, isCod, stopwatch.ElapsedMilliseconds, correlationId);

        return response;
    }

    public Task<PaymentIntentResponseDto> ResumeExistingPaymentIntentAsync(
        Guid tenantId,
        Guid orderId,
        Guid paymentIntentId,
        PaymentProvider provider,
        bool promotionCalculationFailed,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty ||
            orderId == Guid.Empty ||
            paymentIntentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(requestId) ||
            string.IsNullOrWhiteSpace(correlationId))
        {
            throw new PaymentOrderInvalidStateException(
                orderId,
                "RecoveryResumeRequest:Invalid",
                "ExistingDurablePaymentIntent",
                "PAYMENT_RECOVERY_FACTS_INVALID");
        }

        // T-09: Fawry recovery must never initiate or resume provider execution.
        if (provider == PaymentProvider.Fawry)
        {
            throw new PaymentFawryFinancialAuthorityException();
        }

        return EnsurePersistedPaymentIntentReadyAsync(
            tenantId,
            orderId,
            paymentIntentId,
            provider,
            promotionCalculationFailed,
            requestId,
            correlationId,
            cancellationToken);
    }

    #endregion

    #region Process Webhook

    /// <inheritdoc />
    public async Task<WebhookResponseDto> ProcessWebhookAsync(
        Guid tenantId,
        string provider,
        string? signature,
        string rawPayload,
        string correlationId,
        string? sourceIp,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        DomainGuard.NonEmpty(tenantId, "PAYMENT_WEBHOOK_TENANT_REQUIRED", "Payment webhook processing requires a valid tenant.");
        provider = DomainGuard.Required(provider, "PAYMENT_WEBHOOK_PROVIDER_REQUIRED", "Payment webhook processing requires a provider.");
        rawPayload = DomainGuard.Required(rawPayload, "PAYMENT_WEBHOOK_PAYLOAD_REQUIRED", "Payment webhook processing requires a payload.");
        correlationId = DomainGuard.Required(correlationId, "PAYMENT_WEBHOOK_CORRELATION_REQUIRED", "Payment webhook processing requires a correlation identifier.");

        var stopwatch = Stopwatch.StartNew();
        string? recordedWebhookOutcome = null;

        void RecordWebhookOutcome(string outcome)
        {
            if (recordedWebhookOutcome != null)
            {
                return;
            }

            _reliabilityTelemetry.RecordWebhookProcessing(provider, "payment", outcome);
            recordedWebhookOutcome = outcome;
        }

        _logger.LogInformation(
            "ProcessWebhook starting | TenantId: {TenantId} | Provider: {Provider} | SourceIp: {SourceIp} | CorrelationId: {CorrelationId}",
            tenantId, provider, sourceIp, correlationId);

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 2: BLOCK COD WEBHOOKS (SECURITY)
        // ═══════════════════════════════════════════════════════════════════════
        // COD has NO gateway, therefore COD webhooks should NEVER exist.
        // Any attempt to send a COD webhook is a security violation.
        if (provider.Equals("cod", StringComparison.OrdinalIgnoreCase) ||
            provider.Equals("cash", StringComparison.OrdinalIgnoreCase) ||
            provider.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
            provider.Contains("cod", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogCritical(
                "[SECURITY_VIOLATION] COD webhook attempt blocked | " +
                "Provider: {Provider} | TenantId: {TenantId} | SourceIP: {SourceIP} | " +
                "UserAgent: {UserAgent} | CorrelationId: {CorrelationId}",
                provider, tenantId, sourceIp, userAgent, correlationId);

            RecordWebhookOutcome("rejected");
            return new WebhookResponseDto
            {
                Processed = false,
                Message = "COD webhooks are not supported. COD collection is triggered by physical delivery events only."
            };
        }

        // Step 1: Parse payload to get event ID
        var webhookEvent = ParseWebhookPayload(provider, rawPayload);
        if (webhookEvent == null)
        {
            _logger.LogWarning(
                "Failed to parse webhook payload | Provider: {Provider} | CorrelationId: {CorrelationId}",
                provider, correlationId);
            RecordWebhookOutcome("rejected");
            return new WebhookResponseDto
            {
                Processed = false,
                Message = "Failed to parse payload"
            };
        }

        // Step 2: Verify signature BEFORE any persistence or state mutation
        if (!VerifyWebhookSignature(provider, signature, rawPayload))
        {
            _logger.LogWarning(
                "Invalid webhook signature | Provider: {Provider} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                provider, webhookEvent.ProviderEventId, correlationId);

            RecordWebhookOutcome("rejected");
            throw new PaymentInvalidSignatureException(provider);
        }

        // Step 3: Check webhook idempotency (Redis + DB fallback)
        var webhookIdempotencyKey = $"tenant:{tenantId}:provider:{provider}:event:{webhookEvent.ProviderEventId}";
        var isDuplicate = await CheckWebhookIdempotencyAsync(webhookIdempotencyKey, provider, webhookEvent.ProviderEventId, tenantId, correlationId);

        // Step 4: Create webhook log
        var webhookLog = new PaymentWebhookLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = provider,
            ProviderEventId = webhookEvent.ProviderEventId,
            EventType = webhookEvent.EventType,
            RawPayload = rawPayload,
            Signature = signature,
            SignatureVerified = true,
            SourceIp = sourceIp,
            UserAgent = userAgent,
            ProcessingStatus = isDuplicate ? WebhookProcessingStatus.Skipped : WebhookProcessingStatus.Processing,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PaymentWebhookLogs.Add(webhookLog);
        
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        {
            // Unique constraint violation - duplicate Provider + ProviderEventId
            // This is the DB-backed idempotency fallback when Redis fails
            _logger.LogInformation(
                "Webhook idempotency enforced by DB unique constraint | Provider: {Provider} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                provider, webhookEvent.ProviderEventId, correlationId);
            
            // Fetch the existing log to check its processing state
            var existingLog = await _dbContext.PaymentWebhookLogs
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    log => log.Provider == provider && 
                           log.ProviderEventId == webhookEvent.ProviderEventId && 
                           log.TenantId == tenantId,
                    cancellationToken);
            
            // Only acknowledge as duplicate if the existing log is in a completed state
            // If it's still Processing or Failed, this is a legitimate retry
            if (existingLog != null && 
                (existingLog.ProcessingStatus == WebhookProcessingStatus.Completed || 
                 existingLog.ProcessingStatus == WebhookProcessingStatus.Skipped))
            {
                RecordWebhookOutcome("duplicate");
                return new WebhookResponseDto
                {
                    Processed = true,
                    Message = "Duplicate event detected by database idempotency",
                    EventLogId = existingLog.Id
                };
            }
            
            // If the existing log is in Processing or Failed state, treat as in-flight/transient
            // Return a transient error to allow retry
            _logger.LogWarning(
                "Webhook event already exists but not completed | Provider: {Provider} | EventId: {EventId} | Status: {Status} | CorrelationId: {CorrelationId}",
                provider, webhookEvent.ProviderEventId, existingLog?.ProcessingStatus, correlationId);

            if (existingLog?.ProcessingStatus == WebhookProcessingStatus.Failed)
            {
                _logger.LogCritical(
                    "Webhook repeatedly failed and requires retry | Provider: {Provider} | EventId: {EventId} | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    provider,
                    webhookEvent.ProviderEventId,
                    existingLog.OrderId,
                    tenantId,
                    correlationId);
            }

            RecordWebhookOutcome("failed");
            return new WebhookResponseDto
            {
                Processed = false,
                Message = "Webhook event is already being processed or previously failed. Please retry later.",
                EventLogId = existingLog?.Id
            };
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(
                ex,
                "Webhook log creation concurrency conflict | Provider: {Provider} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                provider, webhookEvent.ProviderEventId, correlationId);

            var durableDuplicate = await TryResolveCommittedWebhookDuplicateAsync(
                tenantId,
                provider,
                webhookEvent.ProviderEventId,
                cancellationToken);

            if (durableDuplicate != null)
            {
                RecordWebhookOutcome("duplicate");
                return new WebhookResponseDto
                {
                    Processed = true,
                    Message = "Webhook event already completed by concurrent operation",
                    EventLogId = durableDuplicate.Id
                };
            }

            RecordWebhookOutcome("failed");
            return new WebhookResponseDto
            {
                Processed = false,
                Message = "Webhook log creation was not durably completed. Provider should retry.",
                EventLogId = null
            };
        }

        // Step 5: If duplicate, return OK (no-op)
        if (isDuplicate)
        {
            _logger.LogInformation(
                "Duplicate webhook event | Provider: {Provider} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                provider, webhookEvent.ProviderEventId, correlationId);

            RecordWebhookOutcome("duplicate");
            return new WebhookResponseDto
            {
                Processed = true,
                Message = "Duplicate event ignored",
                EventLogId = webhookLog.Id
            };
        }

        try
        {
            // ═══════════════════════════════════════════════════════════════════════
            // TRANSACTION BOUNDARY: Wrap payment processing + inventory operations
            // ═══════════════════════════════════════════════════════════════════════
            // This ensures inventory commits (CommitOrderReservationsAsync/ReleaseOrderReservationsAsync)
            // participate in the same transaction as payment/order mutations.
            // Without this, inventory could be committed while payment state fails to persist.
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                // Step 6: Resolve PaymentIntent
                PaymentIntent? paymentIntent = null;
                Order? order = null;

            if (!string.IsNullOrEmpty(webhookEvent.PaymentReference))
            {
                paymentIntent = await _dbContext.PaymentIntents
                    .Include(pi => pi.Order)
                    .FirstOrDefaultAsync(
                        pi => pi.PaymentReference == webhookEvent.PaymentReference && pi.TenantId == tenantId,
                        cancellationToken);
                order = paymentIntent?.Order;
            }
            else if (!string.IsNullOrEmpty(webhookEvent.ProviderIntentId))
            {
                paymentIntent = await _dbContext.PaymentIntents
                    .Include(pi => pi.Order)
                    .FirstOrDefaultAsync(
                        pi => pi.ProviderIntentId == webhookEvent.ProviderIntentId && pi.TenantId == tenantId,
                        cancellationToken);
                order = paymentIntent?.Order;
            }

            if (paymentIntent == null || order == null)
            {
                // PRODUCTION HARDENING: Emit UnresolvedOnlinePayment to outbox to route to suspense ledger.
                // Use IsSuccessEvent() to catch all gateway success variants ("succeeded", "completed", "paid",
                // "captured", "charge.success") - the same predicate used for resolved intent processing.
                bool routedToSuspense = false;
                if (IsSuccessEvent(webhookEvent.EventType.ToLowerInvariant()))
                {
                    var unresolvedPayload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Gateway = provider,
                        Amount = webhookEvent.Amount,
                        Currency = webhookEvent.Currency,
                        ProviderEventId = webhookEvent.ProviderEventId,
                        PaymentReference = webhookEvent.PaymentReference ?? webhookEvent.ProviderIntentId
                    });
                    
                    var outboxMessage = PointOfSale.Core.Entities.Accounting.AccountingOutboxMessage.Create(
                        tenantId: tenantId,
                        eventType: "UnresolvedOnlinePayment",
                        businessEntityId: Guid.Empty,
                        entityTypeName: "UnresolvedPayment",
                        payloadJson: unresolvedPayload,
                        idempotencyKey: $"UNRESOLVED_PAYMENT:{tenantId}:{provider}:{webhookEvent.ProviderEventId}",
                        correlationId: correlationId
                    );
                    _dbContext.AccountingOutboxMessages.Add(outboxMessage);
                    
                    // Mark as processed since we successfully routed to suspense - prevents duplicate retries
                    webhookLog.ProcessingStatus = WebhookProcessingStatus.Completed;
                    webhookLog.ProcessedAt = DateTime.UtcNow;
                    webhookLog.ProcessingError = StorefrontUnresolvedPaymentLogState.RoutedToSuspense(
                        webhookEvent.PaymentReference ?? webhookEvent.ProviderIntentId);
                    routedToSuspense = true;
                }
                else
                {
                    // Non-success event with no intent - mark as failed
                    webhookLog.ProcessingStatus = WebhookProcessingStatus.Failed;
                    webhookLog.ProcessingError = "Payment intent not found";
                }
                
                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    // CRITICAL: For success-type payment events, we MUST re-throw to ensure the
                    // UnresolvedOnlinePayment outbox message is persisted. Swallowing the exception
                    // would drop the outbox message and return Processed=true, causing loss of funds.
                    // The gateway will retry and we'll eventually persist the suspense ledger entry.
                    if (routedToSuspense)
                    {
                        _logger.LogError(
                            ex,
                            "Webhook payment intent not found concurrency conflict for SUCCESS event - re-throwing to trigger gateway retry | Provider: {Provider} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                            provider, webhookEvent.ProviderEventId, correlationId);
                        throw new UnresolvedOnlinePaymentConcurrencyException(
                            "Concurrency conflict while routing unresolved online payment to suspense - gateway must retry",
                            ex);
                    }
                    
                    // For non-success events, webhook log update conflict is non-critical
                    _logger.LogWarning(
                        ex,
                        "Webhook payment intent not found concurrency conflict for non-success event | Provider: {Provider} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                        provider, webhookEvent.ProviderEventId, correlationId);
                    // Continue with response - RecordWebhookOutcome will still execute
                }

                if (routedToSuspense)
                {
                    _logger.LogWarning(
                        "Payment intent not found for webhook - routed to suspense | Provider: {Provider} | EventId: {EventId} | Reference: {Reference} | CorrelationId: {CorrelationId}",
                        provider, webhookEvent.ProviderEventId, webhookEvent.PaymentReference ?? webhookEvent.ProviderIntentId, correlationId);

                    RecordWebhookOutcome("accepted");
                    return new WebhookResponseDto
                    {
                        Processed = true,
                        Message = "Payment intent not found - routed to suspense ledger",
                        EventLogId = webhookLog.Id
                    };
                }
                else
                {
                    _logger.LogWarning(
                        "Payment intent not found for webhook | Provider: {Provider} | EventId: {EventId} | Reference: {Reference} | CorrelationId: {CorrelationId}",
                        provider, webhookEvent.ProviderEventId, webhookEvent.PaymentReference ?? webhookEvent.ProviderIntentId, correlationId);

                    RecordWebhookOutcome("rejected");
                    return new WebhookResponseDto
                    {
                        Processed = false,
                        Message = "Payment intent not found",
                        EventLogId = webhookLog.Id
                    };
                }
            }

            EnsurePaymentIntentTenantMatch(paymentIntent, order, tenantId, correlationId);
            await EnsureOrderCustomerBelongsToTenantAsync(order, tenantId, correlationId, cancellationToken);

            webhookLog.PaymentIntentId = paymentIntent.Id;
            webhookLog.OrderId = order.Id;

            // Step 7: Process based on event type. Success paths are validated inside
            // PaymentLifecycleService using provider-confirmed amount and currency.
            await ProcessPaymentEventAsync(
                webhookEvent, paymentIntent, order, webhookLog,
                correlationId, cancellationToken);

            // Step 9: Mark as processed
            webhookLog.ProcessingStatus = WebhookProcessingStatus.Completed;
            webhookLog.ProcessedAt = DateTime.UtcNow;
            webhookLog.ProcessingDurationMs = stopwatch.ElapsedMilliseconds;

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Webhook processing concurrency conflict | PaymentIntentId: {PaymentIntentId} | Provider: {Provider} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                    paymentIntent.Id, provider, webhookEvent.ProviderEventId, correlationId);

                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();

                var durableDuplicate = await TryResolveCommittedWebhookDuplicateAsync(
                    tenantId,
                    provider,
                    webhookEvent.ProviderEventId,
                    cancellationToken);

                if (durableDuplicate != null)
                {
                    RecordWebhookOutcome("duplicate");
                    return new WebhookResponseDto
                    {
                        Processed = true,
                        Message = "Webhook event already completed by concurrent operation",
                        EventLogId = durableDuplicate.Id
                    };
                }

                throw new PaymentException(
                    "WEBHOOK_CONCURRENCY_RETRY_REQUIRED",
                    "Webhook processing concurrency conflict before durable completion. Provider should retry.");
            }

            // Store webhook idempotency after successful DB commit
            var redisStored = await StoreWebhookIdempotencyAsync(webhookIdempotencyKey, webhookLog.Id, correlationId);
            
            if (!redisStored)
            {
                // Redis failed - create DB-backed idempotency record as durable fallback
                // The unique constraint on PaymentWebhookLog (Provider + ProviderEventId) will prevent replays
                _logger.LogWarning(
                    "Redis idempotency storage failed, relying on DB unique constraint | WebhookLogId: {WebhookLogId} | Provider: {Provider} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                    webhookLog.Id, provider, webhookEvent.ProviderEventId, correlationId);
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "ProcessWebhook completed | Provider: {Provider} | EventId: {EventId} | EventType: {EventType} | OrderId: {OrderId} | DurationMs: {DurationMs} | CorrelationId: {CorrelationId}",
                provider, webhookEvent.ProviderEventId, webhookEvent.EventType, order.Id, stopwatch.ElapsedMilliseconds, correlationId);

            RecordWebhookOutcome("processed");
            return new WebhookResponseDto
            {
                Processed = true,
                Message = "Event processed successfully",
                EventLogId = webhookLog.Id
            };
            }
            catch (Exception)
            {
                // Transaction will auto-rollback on exception via using statement
                throw;
            }
        }
        catch (UnresolvedOnlinePaymentConcurrencyException ex)
        {
            // CRITICAL: This exception must propagate to the controller so the gateway retries.
            // Do NOT catch and swallow - the UnresolvedOnlinePayment outbox message must be persisted.
            _logger.LogError(
                ex,
                "Unresolved online payment concurrency conflict - re-throwing to ensure gateway retry | Provider: {Provider} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                provider, webhookEvent.ProviderEventId, correlationId);
            throw;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Catch any DbUpdateConcurrencyException that wasn't caught in the inner try-catch blocks
            _logger.LogWarning(
                ex,
                "Webhook processing concurrency conflict (outer catch) | Provider: {Provider} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                provider, webhookEvent.ProviderEventId, correlationId);

            _dbContext.ChangeTracker.Clear();
            var durableDuplicate = await TryResolveCommittedWebhookDuplicateAsync(
                tenantId,
                provider,
                webhookEvent.ProviderEventId,
                cancellationToken);

            if (durableDuplicate != null)
            {
                RecordWebhookOutcome("duplicate");
                return new WebhookResponseDto
                {
                    Processed = true,
                    Message = "Webhook event already completed by concurrent operation",
                    EventLogId = durableDuplicate.Id
                };
            }

            RecordWebhookOutcome("failed");
            throw new PaymentException(
                "WEBHOOK_CONCURRENCY_RETRY_REQUIRED",
                "Webhook processing concurrency conflict before durable completion. Provider should retry.");
        }
        catch (PaymentException)
        {
            RecordWebhookOutcome("failed");
            throw;
        }
        catch (Exception ex)
        {
            RecordWebhookOutcome("failed");
            webhookLog.ProcessingStatus = WebhookProcessingStatus.Failed;
            webhookLog.ProcessingError = ex.Message;
            webhookLog.ProcessedAt = DateTime.UtcNow;
            
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException concurrencyEx)
            {
                _logger.LogWarning(
                    concurrencyEx,
                    "Webhook error logging concurrency conflict | Provider: {Provider} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                    provider, webhookEvent.ProviderEventId, correlationId);
                // Webhook log update conflict is non-critical, continue with original exception
            }

            _logger.LogError(ex,
                "Webhook processing failed | Provider: {Provider} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                provider, webhookEvent.ProviderEventId, correlationId);

            throw;
        }
    }

    private async Task<PaymentWebhookLog?> TryResolveCommittedWebhookDuplicateAsync(
        Guid tenantId,
        string provider,
        string providerEventId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.PaymentWebhookLogs
            .AsNoTracking()
            .Where(log =>
                log.TenantId == tenantId &&
                log.Provider == provider &&
                log.ProviderEventId == providerEventId &&
                (log.ProcessingStatus == WebhookProcessingStatus.Completed ||
                 log.ProcessingStatus == WebhookProcessingStatus.Skipped))
            .OrderByDescending(log => log.ProcessedAt)
            .ThenByDescending(log => log.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task ProcessPaymentEventAsync(
        PaymentWebhookEventDto webhookEvent,
        PaymentIntent paymentIntent,
        Order order,
        PaymentWebhookLog webhookLog,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var eventType = webhookEvent.EventType.ToLowerInvariant();
        
        // Determine action based on event type
        // Different providers use different event names
        if (IsSuccessEvent(eventType))
        {
            await HandlePaymentSuccessAsync(
                webhookEvent, paymentIntent, order, webhookLog,
                correlationId, cancellationToken);
        }
        else if (IsFailureEvent(eventType))
        {
            await HandlePaymentFailureAsync(
                webhookEvent, paymentIntent, order, webhookLog,
                correlationId, cancellationToken);
        }
        else if (IsAuthorizationEvent(eventType))
        {
            await HandlePaymentAuthorizationAsync(
                webhookEvent, paymentIntent, order, webhookLog,
                correlationId, cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "Unhandled webhook event type | EventType: {EventType} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                webhookEvent.EventType, order.Id, correlationId);
        }
    }

    private static bool IsSuccessEvent(string eventType) =>
        StorefrontPaymentWebhookParser.IsSuccessEvent(eventType);

    private static bool IsFailureEvent(string eventType) =>
        StorefrontPaymentWebhookParser.IsFailureEvent(eventType);

    private static bool IsAuthorizationEvent(string eventType) =>
        StorefrontPaymentWebhookParser.IsAuthorizationEvent(eventType);

    /// <summary>
    /// PHASE 11.4.2.A: Payment success now allocates money to OrderItems.
    /// MONEY IS TRACKED ON ORDER ITEMS (VARIANTS), NOT ON ORDERS.
    /// </summary>
    private async Task HandlePaymentSuccessAsync(
        PaymentWebhookEventDto webhookEvent,
        PaymentIntent paymentIntent,
        Order order,
        PaymentWebhookLog webhookLog,
        string correlationId,
        CancellationToken cancellationToken)
    {
        webhookLog.PaymentIntentId = paymentIntent.Id;
        webhookLog.OrderId = order.Id;

        var provider = paymentIntent.Provider.ToString().ToLowerInvariant();
        var lifecycleResult = await _paymentLifecycleService.HandlePaymentResultAsync(
            new HandlePaymentResultRequest
            {
                OrderId = order.Id,
                PaymentIntentId = paymentIntent.Id,
                Outcome = PaymentLifecycleOutcome.Succeeded,
                Source = PaymentLifecycleSource.GatewayWebhook,
                Gateway = provider,
                PaymentReference = webhookEvent.ProviderIntentId ??
                                   paymentIntent.ProviderIntentId ??
                                   paymentIntent.PaymentReference,
                ProviderPaymentId = webhookEvent.ProviderPaymentId,
                ProviderAmount = webhookEvent.Amount,
                ProviderCurrency = webhookEvent.Currency,
                WebhookEventId = webhookEvent.ProviderEventId
            },
            cancellationToken);

        if (!lifecycleResult.Success)
        {
            if (lifecycleResult.RequiresManualReview)
            {
                StorefrontSuspenseRecoveryPolicy.ApplyManualReviewMarker(
                    lifecycleResult,
                    webhookLog,
                    webhookEvent.ProviderIntentId ??
                    paymentIntent.ProviderIntentId ??
                    paymentIntent.PaymentReference);

                _logger.LogCritical(
                    "[PAYMENT-WEBHOOK] Payment success quarantined for manual review | Provider: {Provider} | OrderId: {OrderId} | EventId: {EventId} | FailureCode: {FailureCode} | Reason: {Reason}",
                    provider,
                    order.Id,
                    webhookEvent.ProviderEventId,
                    lifecycleResult.FailureCode,
                    lifecycleResult.ErrorMessage);
                return;
            }

            throw new DomainGuardException(
                "PAYMENT_CAPTURE_INVALID_STATE",
                $"Failed to capture payment: {lifecycleResult.ErrorMessage}",
                details: new { OrderId = order.Id, PaymentIntentId = paymentIntent.Id });
        }

        if (!lifecycleResult.StateChanged)
        {
            _logger.LogInformation(
                "Payment success replay ignored by lifecycle service | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                order.Id, correlationId);
            return;
        }

    }

    private async Task HandlePaymentFailureAsync(
        PaymentWebhookEventDto webhookEvent,
        PaymentIntent paymentIntent,
        Order order,
        PaymentWebhookLog webhookLog,
        string correlationId,
        CancellationToken cancellationToken)
    {
        webhookLog.PaymentIntentId = paymentIntent.Id;
        webhookLog.OrderId = order.Id;

        var provider = paymentIntent.Provider.ToString().ToLowerInvariant();
        var lifecycleOutcome = webhookEvent.EventType.Contains("cancel", StringComparison.OrdinalIgnoreCase)
            ? PaymentLifecycleOutcome.Cancelled
            : PaymentLifecycleOutcome.Failed;

        var lifecycleResult = await _paymentLifecycleService.HandlePaymentResultAsync(
            new HandlePaymentResultRequest
            {
                OrderId = order.Id,
                PaymentIntentId = paymentIntent.Id,
                Outcome = lifecycleOutcome,
                Source = PaymentLifecycleSource.GatewayWebhook,
                Gateway = provider,
                PaymentReference = webhookEvent.ProviderIntentId ??
                                   paymentIntent.ProviderIntentId ??
                                   paymentIntent.PaymentReference,
                ProviderPaymentId = webhookEvent.ProviderPaymentId,
                Amount = paymentIntent.Amount,
                Currency = paymentIntent.Currency,
                Reason = webhookEvent.FailureReason,
                FailureCode = webhookEvent.FailureCode,
                WebhookEventId = webhookEvent.ProviderEventId
            },
            cancellationToken);

        if (!lifecycleResult.Success)
        {
            throw new DomainGuardException(
                "PAYMENT_FAILURE_INVALID_STATE",
                $"Failed to record payment failure: {lifecycleResult.ErrorMessage}",
                details: new { OrderId = order.Id, PaymentIntentId = paymentIntent.Id });
        }

        if (!lifecycleResult.StateChanged)
        {
            _logger.LogInformation(
                "Payment failure replay ignored by lifecycle service | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                order.Id, correlationId);
            return;
        }

    }

    private async Task HandlePaymentAuthorizationAsync(
        PaymentWebhookEventDto webhookEvent,
        PaymentIntent paymentIntent,
        Order order,
        PaymentWebhookLog webhookLog,
        string correlationId,
        CancellationToken cancellationToken)
    {
        webhookLog.PaymentIntentId = paymentIntent.Id;
        webhookLog.OrderId = order.Id;

        var provider = paymentIntent.Provider.ToString().ToLowerInvariant();
        var lifecycleResult = await _paymentLifecycleService.HandlePaymentResultAsync(
            new HandlePaymentResultRequest
            {
                OrderId = order.Id,
                PaymentIntentId = paymentIntent.Id,
                Outcome = PaymentLifecycleOutcome.Authorized,
                Source = PaymentLifecycleSource.GatewayWebhook,
                Gateway = provider,
                PaymentReference = webhookEvent.ProviderIntentId ??
                                   paymentIntent.ProviderIntentId ??
                                   paymentIntent.PaymentReference,
                ProviderPaymentId = webhookEvent.ProviderPaymentId ?? webhookEvent.ProviderIntentId,
                Amount = paymentIntent.Amount,
                Currency = paymentIntent.Currency,
                WebhookEventId = webhookEvent.ProviderEventId
            },
            cancellationToken);

        if (!lifecycleResult.Success)
        {
            throw new DomainGuardException(
                "PAYMENT_AUTHORIZATION_INVALID_STATE",
                $"Failed to record payment authorization: {lifecycleResult.ErrorMessage}",
                details: new { OrderId = order.Id, PaymentIntentId = paymentIntent.Id });
        }

        if (!lifecycleResult.StateChanged)
        {
            _logger.LogInformation(
                "Payment authorization replay ignored by lifecycle service | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                order.Id, correlationId);
            return;
        }

    }

    #endregion

    private Task<string> EnsurePaymentCurrencyConsistencyAsync(
        Guid tenantId,
        Guid orderId,
        string? orderCurrency,
        string provider,
        CancellationToken cancellationToken)
    {
        var context = $"tenantId:{tenantId:D};orderId:{orderId:D};provider:{provider}";

        try
        {
            return Task.FromResult(FinancialCurrencyGuard.RequireCurrency(
                orderCurrency,
                "order.currency",
                context));
        }
        catch (FinancialCurrencyMismatchException ex)
        {
            _reliabilityTelemetry.RecordCurrencyMismatch(tenantId, orderId, "payment");
            _logger.LogError(
                ex,
                "Payment intent rejected due to currency mismatch | TenantId: {TenantId} | OrderId: {OrderId} | Provider: {Provider}",
                tenantId,
                orderId,
                provider);

            var normalizedOrderCurrency = string.IsNullOrWhiteSpace(orderCurrency)
                ? "(missing)"
                : orderCurrency.Trim().ToUpperInvariant();

            throw new Exceptions.PaymentCurrencyMismatchException(
                normalizedOrderCurrency,
                "(payment)",
                provider);
        }
        catch (FinancialCurrencyMissingException ex)
        {
            _reliabilityTelemetry.RecordCurrencyMissing(tenantId, orderId, "payment");
            _logger.LogError(
                ex,
                "Payment intent rejected due to missing currency | TenantId: {TenantId} | OrderId: {OrderId} | Provider: {Provider} | Component: {Component}",
                tenantId,
                orderId,
                provider,
                ex.Component);
            
            if (ex.Component == "order.currency")
            {
                throw new PaymentException(
                    "ORDER_CURRENCY_NOT_SET",
                    "Order currency is not configured. Payment intent creation aborted.");
            }
            else
            {
                // Fallback for unexpected component values
                throw new PaymentException(
                    "CURRENCY_NOT_SET",
                    $"Currency is not configured for {ex.Component}. Payment intent creation aborted.");
            }
        }
        catch (FinancialCurrencyInvalidException ex)
        {
            _reliabilityTelemetry.RecordCurrencyInvalid(tenantId, orderId, "payment");
            _logger.LogError(
                ex,
                "Payment intent rejected due to invalid currency | TenantId: {TenantId} | OrderId: {OrderId} | Provider: {Provider} | Component: {Component}",
                tenantId,
                orderId,
                provider,
                ex.Component);
            
            if (ex.Component == "order.currency")
            {
                throw new PaymentException(
                    "ORDER_CURRENCY_INVALID",
                    "Order currency is invalid. Payment intent creation aborted.");
            }
            else
            {
                // Fallback for unexpected component values
                throw new PaymentException(
                    "CURRENCY_INVALID",
                    $"Currency is invalid for {ex.Component}. Payment intent creation aborted.");
            }
        }
    }

    #region Confirm COD Order

    /// <inheritdoc />
    public async Task<CodConfirmResponseDto> ConfirmCodOrderAsync(
        Guid tenantId,
        Guid orderId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "ConfirmCodOrder starting | TenantId: {TenantId} | OrderId: {OrderId} | RequestId: {RequestId} | CorrelationId: {CorrelationId}",
            tenantId, orderId, requestId, correlationId);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var claim = await TryClaimCodConfirmIdempotencyAsync(tenantId, orderId, requestId, cancellationToken);
        switch (claim.Status)
        {
            case OrderRequestIdempotencyClaimStatus.Replay:
                _logger.LogInformation(
                    "ConfirmCodOrder idempotent replay | OrderId: {OrderId} | RequestId: {RequestId} | CorrelationId: {CorrelationId}",
                    claim.ReplayResponse!.OrderId,
                    requestId,
                    correlationId);
                return claim.ReplayResponse!;

            case OrderRequestIdempotencyClaimStatus.PayloadConflict:
                throw new PaymentOrderInvalidStateException(
                    orderId,
                    "CodConfirm:IdempotencyConflict",
                    "CodConfirm:RequestHashMatch",
                    "IDEMPOTENCY_KEY_REUSED");

            case OrderRequestIdempotencyClaimStatus.InProgress:
                throw new PaymentOrderInvalidStateException(
                    orderId,
                    "CodConfirm:InProgress",
                    "CodConfirm:Completed",
                    "COD_CONFIRMATION_IN_PROGRESS");
        }

        // Defensive null check: Ensure claim.Record is not null before proceeding
        if (claim.Record == null)
        {
            _logger.LogError(
                "ConfirmCodOrder failed - claim.Record is unexpectedly null | TenantId: {TenantId} | OrderId: {OrderId} | RequestId: {RequestId} | CorrelationId: {CorrelationId}",
                tenantId, orderId, requestId, correlationId);
            
            throw new PaymentOrderInvalidStateException(
                orderId,
                "CodConfirm:NullRecord",
                "CodConfirm:ValidRecord",
                "IDEMPOTENCY_RECORD_NULL");
        }

        // Step 2: Load and validate order (tenant-scoped for security)
        var order = await OrderLockingHelper.LoadOrderWithLockAsync(
            _dbContext,
            tenantId,
            orderId,
            cancellationToken);

        if (order == null)
        {
            throw new PaymentOrderNotFoundException(orderId);
        }

        await EnsureOrderCustomerBelongsToTenantAsync(order, tenantId, correlationId, cancellationToken);

        // Validate payment method is COD
        if (order.PaymentMethod != PaymentMethod.Cash)
        {
            throw new CodNotApplicableException(orderId, order.PaymentMethod.ToString());
        }

        var confirmedAt = order.UpdatedAt ?? order.CreatedAt;
        var stateChanged = false;

        // Begin authorized context for order status changes (must span SaveChangesAsync)
        using var orderStatusScope = OrderStatusSaveChangesInterceptor.BeginAuthorizedContext();

        // Step 3: Confirm order if needed and stage events before commit.
        if (order.Status != OrderStatus.Confirmed)
        {
            // Validate currency consistency only when actually confirming
            var validatedCurrency = await EnsurePaymentCurrencyConsistencyAsync(
                tenantId,
                orderId,
                order.Currency,
                CodProvider,
                cancellationToken);

            stateChanged = true;
            confirmedAt = DateTime.UtcNow;
            order.Status = OrderStatus.Confirmed;
            order.UpdatedAt = confirmedAt;

            await EmitPaymentSkippedEventAsync(
                tenantId,
                orderId,
                null,
                order.OrderNumber,
                order.PaymentMethod.ToString(),
                order.Total,
                validatedCurrency,
                requestId,
                correlationId,
                cancellationToken);

            await EmitOrderConfirmedEventAsync(
                tenantId,
                orderId,
                null,
                order.OrderNumber,
                order.PaymentMethod.ToString(),
                CodProvider,
                order.Total,
                validatedCurrency,
                "CashOnDelivery",
                OrderStatus.Confirmed,
                requestId,
                correlationId,
                cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "COD order already confirmed during idempotent confirmation | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                orderId,
                correlationId);
        }

        var response = new CodConfirmResponseDto
        {
            OrderId = orderId,
            OrderNumber = order.OrderNumber,
            OrderStatus = OrderStatus.Confirmed.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            TotalAmount = order.Total,
            Currency = order.Currency,
            ConfirmedAt = confirmedAt,
            InventoryConfirmed = false
        };

        CompleteCodConfirmIdempotencyRecord(claim.Record, order.Id, response);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        stopwatch.Stop();

        _logger.LogInformation(
            "ConfirmCodOrder completed | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber} | StateChanged: {StateChanged} | DurationMs: {DurationMs} | CorrelationId: {CorrelationId}",
            tenantId,
            orderId,
            order.OrderNumber,
            stateChanged,
            stopwatch.ElapsedMilliseconds,
            correlationId);

        return response;
    }

    #endregion

    #region Get Order Payment Status

    /// <inheritdoc />
    public async Task<OrderPaymentStatusDto> GetOrderPaymentStatusAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // Tenant-scoped query for security
        // PHASE 11.4.2.A: Include Items to compute TotalPaidAmount
        var order = await _dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == tenantId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            throw new PaymentOrderNotFoundException(orderId);
        }

        await EnsureOrderCustomerBelongsToTenantAsync(order, tenantId, correlationId, cancellationToken);

        var latestPaymentIntent = await _dbContext.PaymentIntents
            .Where(pi => pi.OrderId == orderId && pi.TenantId == tenantId)
            .OrderByDescending(pi => pi.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // =================================================================
        // PHASE 11.4.2.A: Use TotalPaidAmount from OrderItems
        // =================================================================
        // AmountPaid reflects actual collected amount from OrderItems,
        // consistent with HandlePaymentSuccessAsync and manual wallet flow.
        // This supports Collected, PartiallyPaid, and Paid statuses.
        return new OrderPaymentStatusDto
        {
            OrderId = orderId,
            OrderNumber = order.OrderNumber,
            OrderStatus = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            TotalAmount = order.Total,
            AmountPaid = order.TotalPaidAmount,
            Currency = order.Currency,
            LatestPaymentIntent = latestPaymentIntent != null
                ? new PaymentIntentSummaryDto
                {
                    PaymentIntentId = latestPaymentIntent.Id,
                    Status = latestPaymentIntent.Status.ToString(),
                    PaymentReference = latestPaymentIntent.PaymentReference,
                    CreatedAt = latestPaymentIntent.CreatedAt,
                    PaidAt = latestPaymentIntent.CapturedAt
                }
                : null
        };
    }

    /// <inheritdoc />
    public async Task<CanonicalPaymentStatusDto> GetCanonicalPaymentStatusAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == tenantId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            throw new PaymentOrderNotFoundException(orderId);
        }

        await EnsureOrderCustomerBelongsToTenantAsync(order, tenantId, correlationId, cancellationToken);

        var latestPaymentIntent = await _dbContext.PaymentIntents
            .AsNoTracking()
            .Where(pi => pi.OrderId == orderId && pi.TenantId == tenantId)
            .OrderByDescending(pi => pi.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new CanonicalPaymentStatusDto
        {
            OrderId = orderId,
            OrderNumber = order.OrderNumber,
            Status = CanonicalPaymentStatusResolver.Resolve(order.PaymentStatus, latestPaymentIntent?.Status),
            OrderStatus = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            PaymentIntentStatus = latestPaymentIntent?.Status.ToString(),
            PaymentMethod = order.PaymentMethodCode,
            TotalAmount = order.Total,
            Currency = order.Currency
        };
    }

    /// <inheritdoc />
    public async Task<PaymentOrderAccessInfo> GetOrderAccessInfoAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var orderAccess = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId && o.TenantId == tenantId && !o.IsDeleted)
            .Select(o => new PaymentOrderAccessInfo(
                o.CustomerId,
                o.OnlineCustomerId,
                o.SessionId,
                o.IsGuestCheckout))
            .FirstOrDefaultAsync(cancellationToken);

        if (orderAccess == null)
        {
            throw new PaymentOrderNotFoundException(orderId);
        }

        return orderAccess;
    }

    private async Task EnsureOrderCustomerBelongsToTenantAsync(
        Order order,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!order.OnlineCustomerId.HasValue)
        {
            return;
        }

        var orderCustomer = await _dbContext.OnlineCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == order.OnlineCustomerId.Value, cancellationToken);

        if (orderCustomer == null || orderCustomer.TenantId != tenantId)
        {
            _logger.LogWarning(
                "Payment order/customer tenant mismatch detected | TenantId: {TenantId} | OrderId: {OrderId} | OnlineCustomerId: {OnlineCustomerId} | CorrelationId: {CorrelationId}",
                tenantId,
                order.Id,
                order.OnlineCustomerId.Value,
                correlationId);
            throw new UnauthorizedAccessException(TenantErrorCodes.TenantMismatch);
        }
    }

    private void EnsurePaymentIntentTenantMatch(
        PaymentIntent paymentIntent,
        Order order,
        Guid tenantId,
        string correlationId)
    {
        if (paymentIntent.TenantId == tenantId && order.TenantId == tenantId)
        {
            return;
        }

        _logger.LogWarning(
            "Payment intent tenant mismatch detected | ExpectedTenantId: {ExpectedTenantId} | PaymentIntentTenantId: {PaymentIntentTenantId} | OrderTenantId: {OrderTenantId} | PaymentIntentId: {PaymentIntentId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
            tenantId,
            paymentIntent.TenantId,
            order.TenantId,
            paymentIntent.Id,
            order.Id,
            correlationId);
        throw new UnauthorizedAccessException(TenantErrorCodes.TenantMismatch);
    }

    #endregion


    #region Manual Wallet Management (DEPRECATED - Use IPaymentLifecycleService)

    /// <inheritdoc />
    [Obsolete("LEDGER-FIRST VIOLATION: This method is disabled. Use IPaymentLifecycleService.ApproveReviewedPaymentAsync(). " +
              "Use IPaymentLifecycleService.ApproveReviewedPaymentAsync() instead, which ensures: " +
              "1) PaymentStatus changes via authorized context, " +
              "2) Outbox message enqueued for journal creation (DEBIT 1140 Wallet Clearing, CREDIT 4020 Revenue), " +
              "3) ReviewedPaymentApprovedEvent published. " +
              "AdminManualPaymentController has been migrated. Remove this method when no callers remain.", true)]
    public Task ConfirmManualPaymentAsync(
        Guid tenantId,
        Guid paymentIntentId,
        string adminId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        throw CreateLegacyManualPaymentBypassException(nameof(ConfirmManualPaymentAsync));
    }

    /// <inheritdoc />
    [Obsolete("LEDGER-FIRST VIOLATION: This method is disabled. Use IPaymentLifecycleService.RejectReviewedPaymentAsync(). " +
              "Use IPaymentLifecycleService.RejectReviewedPaymentAsync() instead. " +
              "AdminManualPaymentController has been migrated. Remove this method when no callers remain.", true)]
    public Task RejectManualPaymentAsync(
        Guid tenantId,
        Guid paymentIntentId,
        string adminId,
        string rejectionReason,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        throw CreateLegacyManualPaymentBypassException(nameof(RejectManualPaymentAsync));
    }
    /// <inheritdoc />
    public async Task<List<PointOfSale.API.DTOs.Admin.PendingManualPaymentDto>> GetPendingManualPaymentsAsync(
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var payments = await _dbContext.PaymentIntents
            .Include(pi => pi.Order)
                .ThenInclude(o => o.Customer)
            .Where(pi => 
                pi.TenantId == tenantId && 
                pi.Provider == PaymentProvider.ManualWallet && 
                (
                    pi.Status == PaymentIntentStatus.AwaitingVerification || 
                    pi.Status == PaymentIntentStatus.Pending ||
                    (pi.Status == PaymentIntentStatus.Paid && pi.Order.PaymentStatus != PaymentStatus.Collected)
                ))
            .OrderByDescending(pi => pi.CreatedAt)
            .Select(pi => new PointOfSale.API.DTOs.Admin.PendingManualPaymentDto
            {
                PaymentIntentId = pi.Id,
                OrderId = pi.OrderId,
                OrderNumber = pi.Order.OrderNumber,
                CustomerName = pi.Order.Customer != null ? pi.Order.Customer.Name : pi.Order.GuestName ?? "Guest",
                Amount = pi.Amount,
                Currency = pi.Currency,
                WalletType = pi.WalletType ?? "Unknown",
                WalletPhoneNumber = pi.WalletPhoneNumber ?? "N/A",
                HasProofFile = !string.IsNullOrWhiteSpace(pi.ProofImageUrl),
                CreatedAtUtc = pi.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return payments;
    }

    #endregion

    #region Helper Methods

    private static bool LegacyManualPaymentBypassDisabled => true;

    private static NotSupportedException CreateLegacyManualPaymentBypassException(string methodName) =>
        new(
            $"{methodName} is disabled because manual wallet approval/rejection must go through IPaymentLifecycleService. " +
            "Use ApproveReviewedPaymentAsync or RejectReviewedPaymentAsync so payment status, inventory, accounting, and events remain atomic.");

    private PaymentProvider DetermineProvider(PaymentMethod paymentMethod, string? requestedProvider)
    {
        // If COD, always use COD provider
        if (paymentMethod == PaymentMethod.Cash)
        {
            return PaymentProvider.Cod;
        }

        // For card payments, use requested provider or default
        if (!string.IsNullOrEmpty(requestedProvider) && Enum.TryParse<PaymentProvider>(requestedProvider, true, out var result))
        {
            return result;
        }

        // Default provider based on payment method
        return paymentMethod switch
        {
            PaymentMethod.Card => PaymentProvider.Stripe, // Default card provider
            _ => PaymentProvider.Stripe
        };
    }

    private string NormalizeManualWalletProofFileKeyOrThrow(Guid tenantId, string? proofFileKey)
    {
        if (string.IsNullOrWhiteSpace(proofFileKey))
        {
            throw new PaymentManualWalletProofException(
                "Manual wallet proof is required. Re-upload the transfer proof and try again.");
        }

        if (ManualWalletProofStorage.IsExternalAbsoluteUrl(proofFileKey) ||
            !ManualWalletProofStorage.TryNormalizeTenantScopedObjectKey(
                proofFileKey,
                tenantId,
                _r2Options,
                out var normalizedProofFileKey))
        {
            throw new PaymentManualWalletProofException(
                "Manual wallet proof must reference a trusted private upload for this tenant.");
        }

        return normalizedProofFileKey;
    }

    private string GeneratePaymentReference(Guid orderId)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var orderIdShort = orderId.ToString("N")[..8].ToUpperInvariant();
        return $"PAY-{timestamp}-{orderIdShort}";
    }

    private async Task<PaymentIntent?> FindExistingActiveIntentAsync(
        Guid tenantId,
        Guid orderId,
        PaymentProvider provider,
        CancellationToken cancellationToken)
    {
        return await _dbContext.PaymentIntents
            .Where(pi =>
                pi.TenantId == tenantId &&
                pi.OrderId == orderId &&
                pi.Provider == provider &&
                ActiveIntentStatuses.Contains(pi.Status))
            .OrderByDescending(pi => pi.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task ResumePersistedPaymentIntentInitiationAsync(
        Order order,
        PaymentIntent paymentIntent,
        bool promotionCalculationFailed,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Resuming persisted payment intent initiation | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | Provider: {Provider} | CorrelationId: {CorrelationId}",
            paymentIntent.TenantId,
            paymentIntent.OrderId,
            paymentIntent.Id,
            paymentIntent.Provider,
            correlationId);

        await InitiatePersistedPaymentIntentAsync(paymentIntent, paymentIntent.Provider, cancellationToken);

        await EmitPaymentIntentCreatedEventAsync(
            paymentIntent.TenantId,
            paymentIntent.OrderId,
            paymentIntent.Id,
            order.OrderNumber,
            paymentIntent.PaymentReference,
            order.PaymentMethod.ToString(),
            paymentIntent.Provider.ToString(),
            paymentIntent.Amount,
            paymentIntent.Currency,
            paymentIntent.RedirectUrl,
            paymentIntent.Provider == PaymentProvider.Cod,
            promotionCalculationFailed,
            requestId,
            correlationId,
            cancellationToken);

        if (paymentIntent.Provider == PaymentProvider.ManualWallet)
        {
            await EmitManualPaymentSubmittedEventAsync(
                paymentIntent.TenantId,
                paymentIntent.OrderId,
                paymentIntent.Id,
                order.OrderNumber,
                paymentIntent.WalletType ?? "Unknown",
                !string.IsNullOrWhiteSpace(paymentIntent.ProofImageUrl),
                requestId,
                correlationId,
                cancellationToken);

            await EmitWhatsAppPaymentApprovalRequestedAsync(
                paymentIntent.TenantId,
                paymentIntent.OrderId,
                order.OrderNumber,
                paymentIntent.Id,
                paymentIntent.Amount,
                paymentIntent.Currency,
                order.PaymentMethod.ToString(),
                paymentIntent.WalletType,
                correlationId,
                cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<PaymentIntentResponseDto> EnsurePersistedPaymentIntentReadyAsync(
        Guid tenantId,
        Guid orderId,
        Guid paymentIntentId,
        PaymentProvider provider,
        bool promotionCalculationFailed,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (provider == PaymentProvider.Fawry)
        {
            throw new PaymentFawryFinancialAuthorityException();
        }

        _dbContext.ChangeTracker.Clear();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var order = await OrderLockingHelper.LoadOrderWithLockAsync(
                _dbContext,
                tenantId,
                orderId,
                cancellationToken);

            if (order == null)
            {
                throw new PaymentOrderNotFoundException(orderId);
            }

            var paymentIntent = await _dbContext.PaymentIntents
                .FirstOrDefaultAsync(
                    pi => pi.Id == paymentIntentId &&
                          pi.TenantId == tenantId &&
                          pi.OrderId == orderId,
                    cancellationToken);

            if (paymentIntent == null)
            {
                throw new PaymentOrderInvalidStateException(
                    orderId,
                    $"PaymentIntent:{paymentIntentId}",
                    "PersistedPaymentIntent",
                    "PAYMENT_INTENT_NOT_FOUND");
            }

            if (paymentIntent.Provider == PaymentProvider.Fawry ||
                FawryFinancialAuthorityPolicy.IsFawry(order.PaymentMethodCode) ||
                FawryFinancialAuthorityPolicy.IsFawry(order.PaymentGateway))
            {
                throw new PaymentFawryFinancialAuthorityException();
            }

            if (IsProviderInitiationIncomplete(paymentIntent))
            {
                await ResumePersistedPaymentIntentInitiationAsync(
                    order,
                    paymentIntent,
                    promotionCalculationFailed,
                    requestId,
                    correlationId,
                    cancellationToken);
            }
            else if (paymentIntent.Provider == PaymentProvider.ManualWallet)
            {
                ValidateManualPaymentDurableEvidence(
                    paymentIntent,
                    ManualPaymentDurableEvidencePurpose.Replay);
            }

            await transaction.CommitAsync(cancellationToken);
            return BuildExistingPaymentIntentResponse(order, paymentIntent);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<PaymentIntentResponseDto> EnsurePersistedFawryPaymentIntentReadyAsync(
        Guid tenantId,
        Guid orderId,
        Guid paymentIntentId,
        bool promotionCalculationFailed,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (_fawryRuntimeApplicationService is null || _fawryProviderCheckpointStore is null)
        {
            throw new PaymentOrderInvalidStateException(
                orderId,
                "Provider:Fawry/ApplicationCheckpointServiceMissing",
                "FawryRuntimeApplicationServiceAndCheckpointStore",
                "PAYMENT_PROVIDER_INITIATION_FAILED");
        }

        PaymentIntent? intentToExecute = null;

        _dbContext.ChangeTracker.Clear();
        await using (var checkpointTransaction = await _dbContext.Database.BeginTransactionAsync(
                         IsolationLevel.Serializable,
                         cancellationToken))
        {
            try
            {
                var order = await OrderLockingHelper.LoadOrderWithLockAsync(
                    _dbContext,
                    tenantId,
                    orderId,
                    cancellationToken)
                    ?? throw new PaymentOrderNotFoundException(orderId);

                var paymentIntent = await LoadPersistedFawryIntentAsync(
                    tenantId,
                    orderId,
                    paymentIntentId,
                    cancellationToken);

                var checkpoint = LoadFawryCheckpointOrThrow(paymentIntent);
                switch (checkpoint.State)
                {
                    case FawryProviderCheckpointState.Completed:
                        await checkpointTransaction.CommitAsync(cancellationToken);
                        return BuildExistingPaymentIntentResponse(order, paymentIntent);

                    case FawryProviderCheckpointState.ReferenceAccepted:
                        await FinalizeAcceptedFawryCheckpointAsync(
                            order,
                            paymentIntent,
                            checkpoint,
                            promotionCalculationFailed,
                            requestId,
                            correlationId,
                            cancellationToken);
                        await checkpointTransaction.CommitAsync(cancellationToken);
                        return BuildExistingPaymentIntentResponse(order, paymentIntent);

                    case FawryProviderCheckpointState.RegistrationStarted:
                        throw CreateFawryCheckpointException(
                            paymentIntent,
                            "FAWRY_PROVIDER_CHARGE_OUTCOME_UNKNOWN",
                            "RegistrationStartedWithoutAcceptedReference",
                            "ProviderInquiryOrWebhookReconciliation");

                    case FawryProviderCheckpointState.NotStarted:
                        await _fawryProviderCheckpointStore.BeginChargeRegistrationAsync(
                            paymentIntent,
                            cancellationToken);
                        await checkpointTransaction.CommitAsync(cancellationToken);
                        intentToExecute = paymentIntent;
                        break;

                    default:
                        throw CreateFawryCheckpointException(
                            paymentIntent,
                            "FAWRY_PROVIDER_CHECKPOINT_INVALID",
                            "UnknownCheckpointState",
                            "CompleteDurableCheckpoint");
                }
            }
            catch
            {
                await checkpointTransaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        // The registration-started checkpoint transaction is committed before this
        // method is allowed to cross the irreversible provider mutation boundary.
        await InitiateFawryPersistedPaymentIntentAsync(intentToExecute!, cancellationToken);

        return await FinalizePersistedFawryCheckpointAsync(
            tenantId,
            orderId,
            paymentIntentId,
            promotionCalculationFailed,
            requestId,
            correlationId,
            cancellationToken);
    }

    private async Task<PaymentIntentResponseDto> FinalizePersistedFawryCheckpointAsync(
        Guid tenantId,
        Guid orderId,
        Guid paymentIntentId,
        bool promotionCalculationFailed,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var order = await OrderLockingHelper.LoadOrderWithLockAsync(
                _dbContext,
                tenantId,
                orderId,
                cancellationToken)
                ?? throw new PaymentOrderNotFoundException(orderId);

            var paymentIntent = await LoadPersistedFawryIntentAsync(
                tenantId,
                orderId,
                paymentIntentId,
                cancellationToken);
            var checkpoint = LoadFawryCheckpointOrThrow(paymentIntent);

            if (checkpoint.State == FawryProviderCheckpointState.ReferenceAccepted)
            {
                await FinalizeAcceptedFawryCheckpointAsync(
                    order,
                    paymentIntent,
                    checkpoint,
                    promotionCalculationFailed,
                    requestId,
                    correlationId,
                    cancellationToken);
            }
            else if (checkpoint.State == FawryProviderCheckpointState.RegistrationStarted)
            {
                throw CreateFawryCheckpointException(
                    paymentIntent,
                    "FAWRY_PROVIDER_CHARGE_OUTCOME_UNKNOWN",
                    "RegistrationStartedWithoutAcceptedReference",
                    "ProviderInquiryOrWebhookReconciliation");
            }
            else if (checkpoint.State != FawryProviderCheckpointState.Completed)
            {
                throw CreateFawryCheckpointException(
                    paymentIntent,
                    "FAWRY_PROVIDER_CHECKPOINT_INVALID",
                    checkpoint.State.ToString(),
                    "AcceptedOrCompletedCheckpoint");
            }

            await transaction.CommitAsync(cancellationToken);
            return BuildExistingPaymentIntentResponse(order, paymentIntent);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task FinalizeAcceptedFawryCheckpointAsync(
        Order order,
        PaymentIntent paymentIntent,
        FawryProviderCheckpoint checkpoint,
        bool promotionCalculationFailed,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        _fawryProviderCheckpointStore!.ApplyAcceptedReference(paymentIntent, checkpoint);

        await EmitPaymentIntentCreatedEventAsync(
            paymentIntent.TenantId,
            paymentIntent.OrderId,
            paymentIntent.Id,
            order.OrderNumber,
            paymentIntent.PaymentReference,
            order.PaymentMethod.ToString(),
            paymentIntent.Provider.ToString(),
            paymentIntent.Amount,
            paymentIntent.Currency,
            paymentIntent.RedirectUrl,
            isCod: false,
            promotionCalculationFailed,
            requestId,
            correlationId,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<PaymentIntent> LoadPersistedFawryIntentAsync(
        Guid tenantId,
        Guid orderId,
        Guid paymentIntentId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.PaymentIntents
            .SingleOrDefaultAsync(
                intent => intent.Id == paymentIntentId &&
                          intent.TenantId == tenantId &&
                          intent.OrderId == orderId &&
                          intent.Provider == PaymentProvider.Fawry &&
                          intent.Status == PaymentIntentStatus.Pending &&
                          !intent.IsDeleted,
                cancellationToken)
            ?? throw new PaymentOrderInvalidStateException(
                orderId,
                $"PaymentIntent:{paymentIntentId}",
                "PendingFawryPaymentIntent",
                "PAYMENT_INTENT_NOT_FOUND");
    }

    private FawryProviderCheckpoint LoadFawryCheckpointOrThrow(PaymentIntent paymentIntent)
    {
        try
        {
            return _fawryProviderCheckpointStore!.Load(paymentIntent);
        }
        catch (FawryProviderCheckpointException exception)
        {
            throw CreateFawryCheckpointException(
                paymentIntent,
                exception.Code,
                "IncompleteOrIndeterminateCheckpoint",
                "CompleteDurableCheckpoint");
        }
    }

    private static PaymentOrderInvalidStateException CreateFawryCheckpointException(
        PaymentIntent paymentIntent,
        string code,
        string currentState,
        string expectedState)
    {
        return new PaymentOrderInvalidStateException(
            paymentIntent.OrderId,
            $"FawryCheckpoint:{currentState}",
            expectedState,
            code);
    }

    private async Task InitiatePersistedPaymentIntentAsync(
        PaymentIntent paymentIntent,
        PaymentProvider provider,
        CancellationToken cancellationToken)
    {
        if (provider == PaymentProvider.Fawry)
        {
            await InitiateFawryPersistedPaymentIntentAsync(paymentIntent, cancellationToken);
            return;
        }

        if (provider == PaymentProvider.ManualWallet)
        {
            await InitiateManualPaymentPersistedPaymentIntentAsync(paymentIntent, cancellationToken);
            return;
        }

        var adapter = _paymentAdapters.FirstOrDefault(a => a.Provider == provider);
        if (adapter != null)
        {
            var initResult = await adapter.InitiateAsync(paymentIntent, cancellationToken);
            if (initResult.IsSuccess)
            {
                paymentIntent.ReferenceCode = initResult.ReferenceCode;
                paymentIntent.ExpiresAtUtc = initResult.ExpiresAtUtc;
                paymentIntent.RedirectUrl = initResult.RedirectUrl;
                paymentIntent.ProviderIntentId = initResult.ProviderIntentId;
                return;
            }

            _logger.LogWarning(
                "Payment adapter initiation failed | Provider: {Provider} | PaymentIntentId: {PaymentIntentId} | Error: {Error}",
                provider,
                paymentIntent.Id,
                initResult.ErrorMessage);

            if (provider == PaymentProvider.ManualWallet)
            {
                throw new PaymentManualWalletProofException(
                    initResult.ErrorMessage ?? "Manual wallet payment proof could not be verified.");
            }

            throw new PaymentOrderInvalidStateException(
                paymentIntent.OrderId,
                $"Provider:{provider}/InitiationFailed",
                "ProviderInitiated",
                "PAYMENT_PROVIDER_INITIATION_FAILED");
        }

        paymentIntent.ProviderIntentId = BuildDeterministicProviderIntentId(provider, paymentIntent);
        paymentIntent.RedirectUrl = $"https://checkout.{provider.ToString().ToLowerInvariant()}.com/pay/{paymentIntent.ProviderIntentId}";
    }

    private async Task InitiateManualPaymentPersistedPaymentIntentAsync(
        PaymentIntent paymentIntent,
        CancellationToken cancellationToken)
    {
        if (_manualPaymentRuntimeApplicationService is null)
        {
            throw new PaymentOrderInvalidStateException(
                paymentIntent.OrderId,
                "Provider:ManualPayment/ApplicationServiceMissing",
                "ManualPaymentRuntimeApplicationService",
                "PAYMENT_PROVIDER_INITIATION_FAILED");
        }

        if (_manualPaymentEvidenceApplicationService is null)
        {
            throw new PaymentOrderInvalidStateException(
                paymentIntent.OrderId,
                "Provider:ManualPayment/EvidenceApplicationServiceMissing",
                "ManualPaymentEvidenceApplicationService",
                "PAYMENT_PROVIDER_INITIATION_FAILED");
        }

        if (_manualPaymentDurableEvidenceAuthority is null)
        {
            throw new PaymentOrderInvalidStateException(
                paymentIntent.OrderId,
                "Provider:ManualPayment/DurableEvidenceAuthorityMissing",
                nameof(ManualPaymentDurableEvidenceAuthority),
                "MANUAL_PAYMENT_DURABLE_EVIDENCE_AUTHORITY_MISSING");
        }

        var order = paymentIntent.Order
            ?? await _dbContext.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    entity => entity.Id == paymentIntent.OrderId &&
                              entity.TenantId == paymentIntent.TenantId,
                    cancellationToken)
            ?? throw new PaymentOrderNotFoundException(paymentIntent.OrderId);

        var now = DateTimeOffset.UtcNow;
        var channel = ResolveManualPaymentChannel(paymentIntent.WalletType);
        var result = await _manualPaymentRuntimeApplicationService.InitializeReferencePaymentAsync(
            new ManualPaymentRuntimeApplicationRequest(
                paymentIntent.TenantId,
                paymentIntent.OrderId,
                paymentIntent.Id,
                order.OrderNumber,
                paymentIntent.Amount,
                paymentIntent.Currency,
                paymentIntent.PaymentReference,
                paymentIntent.IdempotencyKey ?? paymentIntent.Id.ToString("N"),
                paymentIntent.IdempotencyKey ?? paymentIntent.Id.ToString("N"),
                paymentIntent.CreatedAt,
                now,
                channel,
                order.OnlineCustomerId?.ToString("N") ?? order.GuestPhone ?? order.GuestEmail ?? order.OrderNumber,
                order.GuestEmail,
                order.GuestPhone),
            cancellationToken);

        var proofIdentifier = paymentIntent.ProofImageUrl?.Trim();
        var proofMetadata = new ManualPaymentProofMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["proof_identifier"] = proofIdentifier ?? string.Empty,
            ["proof_source"] = "checkout_manual_wallet",
            ["payment_intent_id"] = paymentIntent.Id.ToString("N")
        });
        var evidenceAdmission = await _manualPaymentEvidenceApplicationService.AdmitEvidenceAsync(
            new ManualPaymentEvidenceApplicationRequest(
                paymentIntent.TenantId,
                paymentIntent.OrderId,
                paymentIntent.Id,
                paymentIntent.PaymentReference,
                paymentIntent.Amount,
                paymentIntent.Currency,
                paymentIntent.IdempotencyKey ?? paymentIntent.Id.ToString("N"),
                paymentIntent.CreatedAt,
                now,
                channel,
                paymentIntent.WalletPhoneNumber ?? paymentIntent.PaymentReference,
                proofIdentifier,
                proofMetadata,
                result.ExpiresAtUtc),
            cancellationToken);

        paymentIntent.ProviderMetadata = _manualPaymentDurableEvidenceAuthority.WriteReceiptEvidence(
            paymentIntent,
            paymentIntent.ProviderMetadata,
            result,
            evidenceAdmission,
            proofMetadata);
        ValidateManualPaymentDurableEvidence(
            paymentIntent,
            ManualPaymentDurableEvidencePurpose.Replay);

        var lifecycleResult = await _paymentLifecycleService.AdmitPaymentReviewAsync(
            new PaymentReviewAdmissionRequest
            {
                TenantId = paymentIntent.TenantId,
                OrderId = paymentIntent.OrderId,
                PaymentIntentId = paymentIntent.Id,
                ReferenceCode = result.InstructionIdentifier,
                ExpiresAtUtc = result.ExpiresAtUtc.UtcDateTime,
                ProviderIntentId = result.InstructionIdentifier,
                ProofImageRetentionExpiresAtUtc = ResolveManualPaymentProofRetentionDeadline(
                    ManualPaymentRetentionArtifactClass.SubmittedProof,
                    now.UtcDateTime),
                Actor = nameof(PaymentService)
            },
            cancellationToken);

        if (!lifecycleResult.IsSuccess)
        {
            throw new PaymentOrderInvalidStateException(
                paymentIntent.OrderId,
                lifecycleResult.ErrorMessage ?? "Manual Wallet review admission failed",
                "ManualWalletReviewAdmission",
                lifecycleResult.FailureCode ?? "MANUAL_WALLET_REVIEW_ADMISSION_FAILED");
        }
    }

    private void ValidateManualPaymentDurableEvidence(
        PaymentIntent paymentIntent,
        ManualPaymentDurableEvidencePurpose purpose)
    {
        if (_manualPaymentDurableEvidenceAuthority is null)
        {
            throw new PaymentOrderInvalidStateException(
                paymentIntent.OrderId,
                "Provider:ManualPayment/DurableEvidenceAuthorityMissing",
                nameof(ManualPaymentDurableEvidenceAuthority),
                "MANUAL_PAYMENT_DURABLE_EVIDENCE_AUTHORITY_MISSING");
        }

        try
        {
            _manualPaymentDurableEvidenceAuthority.ValidateReceiptEvidence(paymentIntent, purpose);
        }
        catch (ManualPaymentApplicationException exception)
        {
            throw new PaymentOrderInvalidStateException(
                paymentIntent.OrderId,
                $"ManualPaymentDurableEvidence:{exception.Code}",
                "CompleteDurableManualPaymentReceiptEvidence",
                exception.Code);
        }
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

    private DateTime ResolveManualPaymentProofRetentionDeadline(
        ManualPaymentRetentionArtifactClass artifactClass,
        DateTime observedAtUtc)
    {
        if (_manualPaymentRetentionAuthority is null)
        {
            throw new PaymentOrderInvalidStateException(
                Guid.Empty,
                "Manual Payment retention authority is missing.",
                nameof(ManualPaymentRetentionAuthority),
                "MANUAL_PAYMENT_RETENTION_AUTHORITY_MISSING");
        }

        return _manualPaymentRetentionAuthority
            .ClassifyAndRetain(artifactClass, new DateTimeOffset(DateTime.SpecifyKind(observedAtUtc, DateTimeKind.Utc)))
            .ExpiresAtUtc;
    }

    private async Task InitiateFawryPersistedPaymentIntentAsync(
        PaymentIntent paymentIntent,
        CancellationToken cancellationToken)
    {
        if (_fawryRuntimeApplicationService is null)
        {
            throw new PaymentOrderInvalidStateException(
                paymentIntent.OrderId,
                "Provider:Fawry/ApplicationServiceMissing",
                "FawryRuntimeApplicationService",
                "PAYMENT_PROVIDER_INITIATION_FAILED");
        }

        var order = paymentIntent.Order
            ?? await _dbContext.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    entity => entity.Id == paymentIntent.OrderId &&
                              entity.TenantId == paymentIntent.TenantId,
                    cancellationToken)
            ?? throw new PaymentOrderNotFoundException(paymentIntent.OrderId);

        var customer = await ResolveFawryCustomerContractAsync(order, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var result = await _fawryRuntimeApplicationService.CreatePaymentIntentAsync(
            new FawryRuntimePaymentIntentApplicationRequest(
                paymentIntent.TenantId,
                paymentIntent.OrderId,
                paymentIntent.Id,
                order.OrderNumber,
                paymentIntent.Amount,
                paymentIntent.Currency,
                paymentIntent.PaymentReference,
                paymentIntent.IdempotencyKey ?? paymentIntent.Id.ToString("N"),
                paymentIntent.IdempotencyKey ?? paymentIntent.Id.ToString("N"),
                paymentIntent.CreatedAt,
                now,
                now.AddMinutes(30),
                CustomerName: customer.Name,
                CustomerMobile: customer.Mobile,
                CustomerEmail: customer.Email,
                Language: FawryDefaultLanguage),
            cancellationToken);

        await _fawryProviderCheckpointStore!.PersistAcceptedReferenceAsync(
            paymentIntent,
            result.ReferenceCode,
            result.ExpiresAtUtc?.UtcDateTime ?? now.AddMinutes(30).UtcDateTime,
            result.PaymentUrl,
            cancellationToken);
    }

    private async Task<FawryCustomerContract> ResolveFawryCustomerContractAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        var name = order.GuestName?.Trim();
        var mobile = order.GuestPhone?.Trim();
        var email = order.GuestEmail?.Trim();

        if (order.OnlineCustomerId.HasValue &&
            (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(mobile) || string.IsNullOrWhiteSpace(email)))
        {
            var onlineCustomer = await _dbContext.OnlineCustomers
                .AsNoTracking()
                .Where(customer => customer.Id == order.OnlineCustomerId.Value &&
                                   customer.TenantId == order.TenantId)
                .Select(customer => new { customer.DisplayName, customer.Phone, customer.Email })
                .SingleOrDefaultAsync(cancellationToken);

            name ??= onlineCustomer?.DisplayName?.Trim();
            mobile ??= onlineCustomer?.Phone?.Trim();
            email ??= onlineCustomer?.Email?.Trim();
        }

        if (order.CustomerId.HasValue &&
            (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(mobile) || string.IsNullOrWhiteSpace(email)))
        {
            var posCustomer = await _dbContext.Customers
                .AsNoTracking()
                .Where(customer => customer.Id == order.CustomerId.Value &&
                                   customer.TenantId == order.TenantId)
                .Select(customer => new { customer.Name, customer.Phone, customer.Email })
                .SingleOrDefaultAsync(cancellationToken);

            name ??= posCustomer?.Name?.Trim();
            mobile ??= posCustomer?.Phone?.Trim();
            email ??= posCustomer?.Email?.Trim();
        }

        if (string.IsNullOrWhiteSpace(mobile) || string.IsNullOrWhiteSpace(email))
        {
            throw new PaymentOrderInvalidStateException(
                order.Id,
                "Provider:Fawry/CustomerContractIncomplete",
                "CustomerMobileAndEmail",
                "FAWRY_CUSTOMER_DETAILS_REQUIRED");
        }

        return new FawryCustomerContract(name, mobile, email);
    }

    private sealed record FawryCustomerContract(string? Name, string Mobile, string Email);

    private static bool IsProviderInitiationIncomplete(PaymentIntent intent)
    {
        if (intent.Provider == PaymentProvider.Cod)
        {
            return false;
        }

        if (intent.Provider == PaymentProvider.ManualWallet)
        {
            return intent.Status == PaymentIntentStatus.Pending;
        }

        return string.IsNullOrWhiteSpace(intent.ProviderIntentId) ||
               (intent.Provider == PaymentProvider.Fawry && string.IsNullOrWhiteSpace(intent.ReferenceCode));
    }

    private static string BuildDeterministicProviderIntentId(PaymentProvider provider, PaymentIntent intent)
    {
        var stableKey = string.IsNullOrWhiteSpace(intent.IdempotencyKey)
            ? intent.Id.ToString("N")
            : intent.IdempotencyKey;
        var token = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{provider}:{intent.TenantId:N}:{intent.OrderId:N}:{stableKey}")))[..24];
        return $"{provider}_{token}"[..Math.Min(32, provider.ToString().Length + 1 + token.Length)];
    }

    private static PaymentIntentResponseDto BuildExistingPaymentIntentResponse(Order order, PaymentIntent existingIntent)
    {
        return new PaymentIntentResponseDto
        {
            PaymentIntentId = existingIntent.Id,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Amount = existingIntent.Amount,
            Currency = existingIntent.Currency,
            PaymentMethod = order.PaymentMethod.ToString(),
            Provider = existingIntent.Provider.ToString(),
            Status = existingIntent.Status.ToString(),
            PaymentReference = existingIntent.PaymentReference,
            ProviderIntentId = existingIntent.ProviderIntentId,
            RedirectUrl = existingIntent.RedirectUrl,
            IsCod = existingIntent.Provider == PaymentProvider.Cod,
            ReferenceCode = existingIntent.ReferenceCode,
            ExpiresAtUtc = existingIntent.ExpiresAtUtc,
            CreatedAt = existingIntent.CreatedAt
        };
    }

    private static bool IsActiveIntentUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
               string.Equals(postgresException.ConstraintName, ActiveIntentUniqueIndexName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Derives manual wallet merchant details from tenant payment method configuration.
    /// SECURITY: These values MUST come from trusted server-side configuration, never from client requests.
    /// </summary>
    private async Task<(string MerchantWalletNumber, string WalletInstructions)?> DeriveManualWalletDetailsAsync(
        Guid tenantId,
        string? walletType,
        decimal orderTotal,
        string currency,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(walletType))
        {
            _logger.LogWarning(
                "Manual wallet details derivation skipped: WalletType is null | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId, correlationId);
            return null;
        }

        try
        {
            var walletMethod = await _dbContext.TenantPaymentMethods
                .AsNoTracking()
                .Where(pm => pm.TenantId == tenantId &&
                            pm.Code == "MANUALWALLET" &&
                            pm.IsEnabled &&
                            !pm.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (walletMethod == null)
            {
                _logger.LogWarning(
                    "Manual wallet payment method not found or inactive | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    tenantId, correlationId);
                return null;
            }

            var configuration = EvaluatePaymentMethodConfiguration(walletMethod, correlationId);
            if (!configuration.IsConfigurationValid)
            {
                _logger.LogWarning(
                    "Manual wallet configuration is invalid | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | CorrelationId: {CorrelationId}",
                    tenantId, walletMethod.Id, correlationId);
                _reliabilityTelemetry.RecordPaymentConfigInvalid("manualwallet", "payment_service", "config_invalid");
                return null;
            }

            var merchantWalletNumber = configuration.NormalizedConfiguration.GetValueOrDefault("walletNumber")?.Trim();
            if (string.IsNullOrWhiteSpace(merchantWalletNumber))
            {
                _logger.LogWarning(
                    "Merchant wallet number not found in configuration | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | CorrelationId: {CorrelationId}",
                    tenantId, walletMethod.Id, correlationId);
                return null;
            }

            // Generate wallet instructions based on wallet type
            var walletInstructions = GenerateWalletInstructions(walletType, orderTotal, currency, merchantWalletNumber);

            _logger.LogInformation(
                "Manual wallet details derived from configuration | TenantId: {TenantId} | WalletType: {WalletType} | CorrelationId: {CorrelationId}",
                tenantId, walletType, correlationId);

            return (merchantWalletNumber, walletInstructions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to derive manual wallet details | TenantId: {TenantId} | WalletType: {WalletType} | CorrelationId: {CorrelationId}",
                tenantId, walletType, correlationId);
            return null;
        }
    }

    private PaymentMethodConfigurationValidationResult EvaluatePaymentMethodConfiguration(
        TenantPaymentMethod method,
        string correlationId)
    {
        if (string.IsNullOrWhiteSpace(method.ConfigEncrypted))
        {
            return PaymentMethodConfigurationValidator.Validate(method.Code, null);
        }

        try
        {
            var decrypted = _dataProtector.Unprotect(method.ConfigEncrypted);
            var configuration = JsonSerializer.Deserialize<Dictionary<string, string>>(decrypted);
            return PaymentMethodConfigurationValidator.Validate(method.Code, configuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to decrypt payment method configuration | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | CorrelationId: {CorrelationId}",
                method.TenantId,
                method.Id,
                correlationId);

            _reliabilityTelemetry.RecordPaymentConfigInvalid(method.Code, "payment_service", "decrypt_failed");

            return new PaymentMethodConfigurationValidationResult(
                method.Code?.Trim().ToUpperInvariant() ?? string.Empty,
                PaymentMethodConfigurationValidator.RequiresConfiguration(method.Code),
                false,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new[] { "Stored payment configuration could not be decrypted." },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static string GenerateWalletInstructions(string walletType, decimal total, string currency, string merchantWalletNumber) =>
        walletType.ToUpperInvariant() switch
        {
            "VODAFONECASH" => $"Transfer {total} {currency} to Vodafone Cash number: {merchantWalletNumber} and upload proof.",
            "INSTAPAY" => $"Transfer {total} {currency} via InstaPay to: {merchantWalletNumber} and upload proof.",
            "ORANGECASH" => $"Transfer {total} {currency} to Orange Cash number: {merchantWalletNumber} and upload proof.",
            _ => $"Transfer {total} {currency} to wallet: {merchantWalletNumber} and upload proof."
        };

    private PaymentWebhookEventDto? ParseWebhookPayload(string provider, string rawPayload) =>
        StorefrontPaymentWebhookParser.Parse(provider, rawPayload, _logger);

    private bool VerifyWebhookSignature(string provider, string? signature, string payload)
    {
        // SECURITY: Fail closed - reject webhooks with missing or unverified signatures
        // Provider-specific endpoints (Stripe, Paymob) are responsible for full verification
        
        if (string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogWarning(
                "[SECURITY] Webhook rejected: No signature provided | Provider: {Provider}. " +
                "Use provider-specific webhook endpoints (e.g., /api/webhooks/stripe, /api/webhooks/paymob) for proper signature verification.",
                provider);
            return false;
        }

        // Generic webhook endpoint does not implement provider-specific signature verification
        // This is intentional - each provider has different signature schemes
        // Webhooks MUST use provider-specific endpoints for security
        _logger.LogWarning(
            "[SECURITY] Webhook rejected: Generic endpoint cannot verify signatures | Provider: {Provider}. " +
            "Use provider-specific webhook endpoints (e.g., /api/webhooks/stripe, /api/webhooks/paymob) for proper signature verification.",
            provider);

        return false;
    }

    #endregion

    #region Idempotency

    private async Task<OrderRequestIdempotencyClaimResult> TryClaimCodConfirmIdempotencyAsync(
        Guid tenantId,
        Guid orderId,
        string requestId,
        CancellationToken cancellationToken)
    {
        var storageKey = BuildCodConfirmStorageKey(tenantId, requestId);
        var requestHash = BuildCodConfirmRequestHash(orderId);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claimId = Guid.NewGuid();
            var claimToken = Guid.NewGuid().ToString("N");
            var nowUtc = DateTime.UtcNow;

            var rowsInserted = await TryInsertOrderRequestClaimAsync(
                claimId,
                tenantId,
                storageKey,
                requestHash,
                orderId,
                claimToken,
                nowUtc,
                cancellationToken);

            if (rowsInserted > 0)
            {
                var claimedRecord = _dbContext.OrderRequestIdempotencyRecords
                    .Local
                    .FirstOrDefault(record =>
                        record.TenantId == tenantId &&
                        record.Key == storageKey);

                claimedRecord ??= await _dbContext.OrderRequestIdempotencyRecords
                    .IgnoreQueryFilters()
                    .FirstAsync(record =>
                        record.TenantId == tenantId &&
                        record.Key == storageKey,
                        cancellationToken);

                return new OrderRequestIdempotencyClaimResult(
                    OrderRequestIdempotencyClaimStatus.Claimed,
                    Record: claimedRecord);
            }

            var existingRecord = await _dbContext.OrderRequestIdempotencyRecords
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(record =>
                    record.TenantId == tenantId &&
                    record.Key == storageKey,
                    cancellationToken);

            if (existingRecord == null)
            {
                continue;
            }

            if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return new OrderRequestIdempotencyClaimResult(
                    OrderRequestIdempotencyClaimStatus.PayloadConflict,
                    ConflictMessage: "The supplied idempotency key was already used for a different COD confirmation request.");
            }

            if (existingRecord.ResponseStatusCode == StatusCodes.Status200OK &&
                TryDeserializeCodConfirmResponse(existingRecord.ResponsePayload, out var replayResponse))
            {
                return new OrderRequestIdempotencyClaimResult(
                    OrderRequestIdempotencyClaimStatus.Replay,
                    ReplayResponse: replayResponse);
            }

            return new OrderRequestIdempotencyClaimResult(OrderRequestIdempotencyClaimStatus.InProgress);
        }

        return new OrderRequestIdempotencyClaimResult(OrderRequestIdempotencyClaimStatus.InProgress);
    }

    private async Task<int> TryInsertOrderRequestClaimAsync(
        Guid claimId,
        Guid tenantId,
        string storageKey,
        string requestHash,
        Guid orderId,
        string claimToken,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""OrderRequestIdempotency""
                (""Id"", ""TenantId"", ""Key"", ""RequestHash"", ""OrderId"", ""ClaimToken"", ""ClaimTimestampUtc"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES
                ({claimId}, {tenantId}, {storageKey}, {requestHash}, {orderId}, {claimToken}, {nowUtc}, {nowUtc}, {nowUtc}, {false})
                ON CONFLICT (""TenantId"", ""Key"") DO NOTHING",
                cancellationToken);
        }

        if (_dbContext.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO [OrderRequestIdempotency]
                    ([Id], [TenantId], [Key], [RequestHash], [OrderId], [ClaimToken], [ClaimTimestampUtc], [CreatedAt], [UpdatedAt], [IsDeleted])
                SELECT
                    {claimId}, {tenantId}, {storageKey}, {requestHash}, {orderId}, {claimToken}, {nowUtc}, {nowUtc}, {nowUtc}, {false}
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [OrderRequestIdempotency] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Key] = {storageKey})",
                cancellationToken);
        }

        var existingPersistedClaim = await _dbContext.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(record =>
                record.TenantId == tenantId &&
                record.Key == storageKey,
                cancellationToken);

        if (existingPersistedClaim != null)
        {
            return 0;
        }

        var existingTracked = _dbContext.OrderRequestIdempotencyRecords
            .Local
            .FirstOrDefault(record =>
                record.TenantId == tenantId &&
                string.Equals(record.Key, storageKey, StringComparison.Ordinal));

        if (existingTracked != null)
        {
            return 0;
        }

        _dbContext.OrderRequestIdempotencyRecords.Add(new OrderRequestIdempotency
        {
            Id = claimId,
            TenantId = tenantId,
            Key = storageKey,
            RequestHash = requestHash,
            OrderId = orderId,
            ClaimToken = claimToken,
            ClaimTimestampUtc = nowUtc,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
            IsDeleted = false
        });

        return 1;
    }

    private void CompleteCodConfirmIdempotencyRecord(
        OrderRequestIdempotency record,
        Guid orderId,
        CodConfirmResponseDto response)
    {
        record.OrderId = orderId;
        record.ResponseStatusCode = StatusCodes.Status200OK;
        record.ResponsePayload = JsonSerializer.Serialize(response, _jsonOptions);
        record.CompletedAtUtc ??= DateTime.UtcNow;
        record.UpdatedAt = record.CompletedAtUtc.Value;
    }

    private static string BuildCodConfirmStorageKey(Guid tenantId, string requestId) =>
        $"PAYMENT:COD_CONFIRM:{tenantId:N}:{requestId}";

    private static string BuildCodConfirmRequestHash(Guid orderId) =>
        DeterministicRequestHasher.ComputeHash(new
        {
            Operation = "COD_CONFIRM",
            OrderId = orderId
        });

    private bool TryDeserializeCodConfirmResponse(
        string? payload,
        out CodConfirmResponseDto? response)
    {
        response = null;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            response = JsonSerializer.Deserialize<CodConfirmResponseDto>(payload, _jsonOptions);
            return response != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<bool> CheckWebhookIdempotencyAsync(string key, string provider, string providerEventId, Guid tenantId, string correlationId)
    {
        // First check Redis (fast path)
        try
        {
            var db = _redisFactory.GetDatabase();
            if (db != null && await db.KeyExistsAsync(key))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis webhook idempotency check error | Key: {Key} | CorrelationId: {CorrelationId}", key, correlationId);
        }

        // Fallback to DB check (durable idempotency)
        // Check if a PaymentWebhookLog already exists for this Provider + ProviderEventId
        // Only treat as duplicate if the existing log is in a completed state
        try
        {
            var existingLog = await _dbContext.PaymentWebhookLogs
                .FirstOrDefaultAsync(log => 
                    log.Provider == provider && 
                    log.ProviderEventId == providerEventId && 
                    log.TenantId == tenantId);

            if (existingLog != null)
            {
                // Only treat as duplicate if the webhook was successfully processed or explicitly skipped
                // If it's still Processing or Failed, allow retry
                if (existingLog.ProcessingStatus == WebhookProcessingStatus.Completed || 
                    existingLog.ProcessingStatus == WebhookProcessingStatus.Skipped)
                {
                    _logger.LogInformation(
                        "Webhook idempotency detected via DB (completed) | Provider: {Provider} | EventId: {EventId} | Status: {Status} | ExistingLogId: {LogId} | CorrelationId: {CorrelationId}",
                        provider, providerEventId, existingLog.ProcessingStatus, existingLog.Id, correlationId);
                    return true;
                }
                else
                {
                    _logger.LogWarning(
                        "Webhook exists but not completed | Provider: {Provider} | EventId: {EventId} | Status: {Status} | ExistingLogId: {LogId} | CorrelationId: {CorrelationId}",
                        provider, providerEventId, existingLog.ProcessingStatus, existingLog.Id, correlationId);
                    // Return false to allow retry for in-progress or failed webhooks
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DB webhook idempotency check error | Provider: {Provider} | EventId: {EventId} | CorrelationId: {CorrelationId}", 
                provider, providerEventId, correlationId);
            // On DB error, return false to allow processing (fail open for availability)
        }

        return false;
    }

    private async Task<bool> StoreWebhookIdempotencyAsync(string key, Guid logId, string correlationId)
    {
        try
        {
            var db = _redisFactory.GetDatabase();
            if (db == null) return false;

            await db.StringSetAsync(key, logId.ToString(), TimeSpan.FromHours(WebhookIdempotencyTtlHours));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook idempotency store error | Key: {Key} | CorrelationId: {CorrelationId}", key, correlationId);
            return false;
        }
    }

    #endregion

    #region Event Emission

    private async Task EmitPaymentIntentCreatedEventAsync(
        Guid tenantId, Guid orderId, Guid paymentIntentId, string orderNumber,
        string paymentReference, string paymentMethod, string provider,
        decimal amount, string currency, string? redirectUrl, bool isCod,
        bool promotionCalculationFailed,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(new PaymentIntentCreatedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            PaymentIntentId = paymentIntentId,
            OrderNumber = orderNumber,
            PaymentReference = paymentReference,
            PaymentMethod = paymentMethod,
            Provider = provider,
            Amount = amount,
            Currency = currency,
            RedirectUrl = redirectUrl,
            IsOfflineCollection = isCod,
            PromotionCalculationFailed = promotionCalculationFailed,
            IdempotencyKey = requestId,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task EmitManualPaymentSubmittedEventAsync(
        Guid tenantId, Guid orderId, Guid paymentIntentId, string orderNumber,
        string walletType, bool hasProofFile,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(new ManualPaymentSubmittedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            PaymentIntentId = paymentIntentId,
            OrderNumber = orderNumber,
            WalletType = walletType,
            HasProofFile = hasProofFile,
#pragma warning disable CS0618 // Type or member is obsolete
            ProofImageUrl = null, // Deprecated field kept for backward compatibility, always null for security
#pragma warning restore CS0618 // Type or member is obsolete
            IdempotencyKey = requestId,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task EmitManualPaymentConfirmedEventAsync(
        Guid tenantId, Guid orderId, Guid paymentIntentId, string orderNumber,
        string adminId, string correlationId, CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(new ManualPaymentConfirmedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            PaymentIntentId = paymentIntentId,
            OrderNumber = orderNumber,
            VerifiedByAdmin = adminId,
            VerifiedAtUtc = DateTime.UtcNow,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task EmitManualPaymentRejectedEventAsync(
        Guid tenantId, Guid orderId, Guid paymentIntentId, string orderNumber,
        string adminId, string rejectionReason, string correlationId, CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(new ManualPaymentRejectedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            PaymentIntentId = paymentIntentId,
            OrderNumber = orderNumber,
            VerifiedByAdmin = adminId,
            VerifiedAtUtc = DateTime.UtcNow,
            RejectionReason = rejectionReason,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task EmitPaymentSkippedEventAsync(
        Guid tenantId, Guid orderId, Guid? paymentIntentId, string orderNumber,
        string paymentMethod, decimal amount, string currency,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(new PaymentSkippedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            PaymentIntentId = paymentIntentId,
            OrderNumber = orderNumber,
            PaymentMethod = paymentMethod,
            Provider = CodProvider,
            Amount = amount,
            Currency = currency,
            Reason = "CashOnDelivery",
            IdempotencyKey = requestId,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task EmitOrderConfirmedEventAsync(
        Guid tenantId, Guid orderId, Guid? paymentIntentId, string orderNumber,
        string paymentMethod, string provider, decimal amount, string currency,
        string confirmationMethod, OrderStatus newStatus,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(new OrderConfirmedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            PaymentIntentId = paymentIntentId,
            OrderNumber = orderNumber,
            PaymentMethod = paymentMethod,
            Provider = provider,
            TotalAmount = amount,
            Currency = currency,
            ConfirmationMethod = confirmationMethod,
            NewOrderStatus = newStatus,
            ConfirmedAt = DateTime.UtcNow,
            IdempotencyKey = requestId,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task EmitPaymentCapturedEventAsync(
        Guid tenantId, Guid orderId, Guid paymentIntentId, string orderNumber,
        string paymentMethod, string provider, decimal amount, string currency,
        string? providerPaymentId, string requestId, string correlationId, CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(new PaymentCapturedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            PaymentIntentId = paymentIntentId,
            OrderNumber = orderNumber,
            PaymentMethod = paymentMethod,
            Provider = provider,
            Amount = amount,
            Currency = currency,
            ProviderPaymentId = providerPaymentId,
            CapturedAt = DateTime.UtcNow,
            IdempotencyKey = requestId,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task EmitOrderPaidEventAsync(
        Guid tenantId, Guid orderId, Guid paymentIntentId, string orderNumber,
        string paymentMethod, string provider, decimal amount, string currency,
        OrderStatus newOrderStatus, PaymentStatus newPaymentStatus,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(new OrderPaidEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            PaymentIntentId = paymentIntentId,
            OrderNumber = orderNumber,
            PaymentMethod = paymentMethod,
            Provider = provider,
            AmountPaid = amount,
            Currency = currency,
            NewOrderStatus = newOrderStatus,
            NewPaymentStatus = newPaymentStatus,
            PaidAt = DateTime.UtcNow,
            IdempotencyKey = requestId,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task EmitPaymentFailedEventAsync(
        Guid tenantId, Guid orderId, Guid paymentIntentId, string orderNumber,
        string paymentMethod, string provider, decimal amount, string currency,
        string failureReason, string? failureCode,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(new PaymentFailedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            PaymentIntentId = paymentIntentId,
            OrderNumber = orderNumber,
            PaymentMethod = paymentMethod,
            Provider = provider,
            Amount = amount,
            Currency = currency,
            FailureReason = failureReason,
            FailureCode = failureCode,
            FailedAt = DateTime.UtcNow,
            IdempotencyKey = requestId,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task EmitPaymentAuthorizedEventAsync(
        Guid tenantId, Guid orderId, Guid paymentIntentId, string orderNumber,
        string paymentMethod, string provider, decimal amount, string currency,
        string? providerAuthId, string requestId, string correlationId, CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(new PaymentAuthorizedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            PaymentIntentId = paymentIntentId,
            OrderNumber = orderNumber,
            PaymentMethod = paymentMethod,
            Provider = provider,
            Amount = amount,
            Currency = currency,
            ProviderAuthorizationId = providerAuthId,
            AuthorizedAt = DateTime.UtcNow,
            IdempotencyKey = requestId,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task EmitInventoryConfirmedEventAsync(
        Guid tenantId, Guid orderId, Guid? paymentIntentId, string orderNumber,
        Guid? reservationId, List<InventoryConfirmationItem> items,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(new InventoryConfirmedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            PaymentIntentId = paymentIntentId,
            OrderNumber = orderNumber,
            ReservationId = reservationId,
            Items = items,
            ConfirmedAt = DateTime.UtcNow,
            IdempotencyKey = requestId,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task EmitInventoryReleasedEventAsync(
        Guid tenantId, Guid orderId, Guid? paymentIntentId, string orderNumber,
        Guid? reservationId, string releaseReason, List<InventoryConfirmationItem> items,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(new InventoryReleasedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            PaymentIntentId = paymentIntentId,
            OrderNumber = orderNumber,
            ReservationId = reservationId,
            ReleaseReason = releaseReason,
            Items = items,
            ReleasedAt = DateTime.UtcNow,
            IdempotencyKey = requestId,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    #endregion

    #region WhatsApp Payment Notifications

    /// <summary>
    /// Emits WhatsApp payment approval requested event.
    /// Publish through the guaranteed event publisher; failures are propagated to caller.
    /// </summary>
    private async Task EmitWhatsAppPaymentApprovalRequestedAsync(
        Guid tenantId,
        Guid orderId,
        string orderNumber,
        Guid paymentIntentId,
        decimal amount,
        string currency,
        string paymentMethod,
        string? walletType,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var evt = new Core.Events.WhatsApp.WhatsAppPaymentApprovalRequestedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            OrderNumber = orderNumber,
            PaymentIntentId = paymentIntentId,
            Amount = amount,
            Currency = currency,
            PaymentMethod = paymentMethod,
            WalletType = walletType,
            AdminPhoneNumber = null, // TODO: Get from tenant settings if needed
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        };

        try
        {
            await _eventPublisher.PublishAsync(evt, cancellationToken);

            _logger.LogDebug(
                "[WhatsApp Payment] Approval requested event staged | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                orderId,
                orderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[WhatsApp Payment] Failed to publish approval requested event | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | CorrelationId: {CorrelationId}",
                tenantId,
                orderId,
                paymentIntentId,
                correlationId);
            throw;
        }
    }

    /// <summary>
    /// Emits WhatsApp payment approved notification event.
    /// Publish through the guaranteed event publisher; failures are propagated to caller.
    /// </summary>
    private async Task EmitWhatsAppPaymentApprovedNotificationAsync(
        Guid tenantId,
        Guid orderId,
        string orderNumber,
        Guid paymentIntentId,
        string customerPhoneNumber,
        decimal amount,
        string currency,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerPhoneNumber))
        {
            _logger.LogDebug(
                "[WhatsApp Payment] No customer phone number, skipping approval notification | OrderId: {OrderId}",
                orderId);
            return;
        }

        var evt = new Core.Events.WhatsApp.WhatsAppPaymentApprovedNotificationEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            OrderNumber = orderNumber,
            PaymentIntentId = paymentIntentId,
            CustomerPhoneNumber = customerPhoneNumber,
            Amount = amount,
            Currency = currency,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        };

        try
        {
            await _eventPublisher.PublishSensitiveAsync(evt, cancellationToken);

            _logger.LogDebug(
                "[WhatsApp Payment] Approved notification event staged | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                orderId,
                orderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[WhatsApp Payment] Failed to publish approved notification event | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | CorrelationId: {CorrelationId}",
                tenantId,
                orderId,
                paymentIntentId,
                correlationId);
            throw;
        }
    }

    /// <summary>
    /// Emits WhatsApp payment rejected notification event.
    /// Publish through the guaranteed event publisher; failures are propagated to caller.
    /// </summary>
    private async Task EmitWhatsAppPaymentRejectedNotificationAsync(
        Guid tenantId,
        Guid orderId,
        string orderNumber,
        Guid paymentIntentId,
        string customerPhoneNumber,
        string rejectionReason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[WhatsApp Payment] Preparing rejection notification | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber} | CustomerPhone: {CustomerPhone}",
            tenantId,
            orderId,
            orderNumber,
            string.IsNullOrWhiteSpace(customerPhoneNumber) ? "EMPTY" : "PROVIDED");

        if (string.IsNullOrWhiteSpace(customerPhoneNumber))
        {
            _logger.LogWarning(
                "[WhatsApp Payment] No customer phone number, skipping rejection notification | OrderId: {OrderId}",
                orderId);
            return;
        }

        var evt = new Core.Events.WhatsApp.WhatsAppPaymentRejectedNotificationEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            OrderNumber = orderNumber,
            PaymentIntentId = paymentIntentId,
            CustomerPhoneNumber = customerPhoneNumber,
            RejectionReason = rejectionReason,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        };

        try
        {
            await _eventPublisher.PublishAsync(evt, cancellationToken);

            _logger.LogInformation(
                "[WhatsApp Payment] Rejected notification event staged | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                orderId,
                orderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[WhatsApp Payment] Failed to publish rejected notification event | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | CorrelationId: {CorrelationId}",
                tenantId,
                orderId,
                paymentIntentId,
                correlationId);
            throw;
        }
    }

    #endregion
}

/// <summary>
/// Exception thrown when a DbUpdateConcurrencyException occurs while routing an unresolved online payment to suspense.
/// This exception must not be caught and swallowed by outer handlers - it must propagate to the controller
/// to ensure the gateway retries the webhook and the UnresolvedOnlinePayment outbox message is eventually persisted.
/// </summary>
public sealed class UnresolvedOnlinePaymentConcurrencyException : Exception
{
    public UnresolvedOnlinePaymentConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
