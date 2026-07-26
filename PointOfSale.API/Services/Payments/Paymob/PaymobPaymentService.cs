using System.Diagnostics;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.Core.Helpers;
using PointOfSale.API.Security;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Payments;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Redis.Services;
using CorePaymentMethod = PointOfSale.Core.Enums.PaymentMethod;
using CorePaymentIntent = PointOfSale.Core.Entities.PaymentIntent;
using PointOfSale.Core.Payments;

// Import Payment exceptions from parent namespace
using PaymentOrderNotFoundException = PointOfSale.API.Services.Payments.PaymentOrderNotFoundException;
using PaymentOrderInvalidStateException = PointOfSale.API.Services.Payments.PaymentOrderInvalidStateException;
using BasePaymentException = PointOfSale.API.Services.Payments.PaymentException;
using PointOfSale.API.Exceptions;

namespace PointOfSale.API.Services.Payments.Paymob;

/// <summary>
/// Paymob Payment Service implementation.
/// 
/// PHASE 10.4.D5: Paymob Integration (Production-Grade, Stripe-Level)
/// 
/// This service integrates with Paymob using their Intention API.
/// Each tenant has their own Paymob configuration (multi-tenant).
/// 
/// ARCHITECTURAL RULES:
/// - Order is created BEFORE calling Paymob
/// - Paymob payment amount = order.Total (NO recalculation)
/// - Webhooks are the ONLY source of truth for payment status
/// - All operations are idempotent
/// - Secrets are decrypted only when needed
/// 
/// Paymob Flow:
/// 1. Authenticate (get auth token)
/// 2. Create Order on Paymob
/// 3. Create Payment Key
/// 4. Customer pays via iframe/redirect
/// 5. Webhook confirms payment
/// </summary>
public class PaymobPaymentService : IPaymobPaymentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDataProtector _dataProtector;
    private readonly RedisConnectionFactory _redisFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly IPaymentLifecycleService _paymentLifecycleService;
    private readonly IPaymobTenantConfigurationResolver _tenantConfigurationResolver;
    private readonly IPaymobRuntimeApplicationService _paymobRuntimeApplicationService;
    private readonly IPaymobWebhookRuntimeIntegrationService _paymobWebhookRuntimeIntegrationService;
    private readonly IPaymobSensitiveArtifactProtector _artifactProtector;
    private readonly StorefrontUrlResolver _storefrontUrlResolver;
    private readonly ILogger<PaymobPaymentService> _logger;
    private readonly CommerceReliabilityTelemetry? _reliabilityTelemetry;

    private const string DataProtectorPurpose = "PaymentMethodSecrets";
    private static readonly TimeSpan StorefrontPaymentSessionTtl = TimeSpan.FromMinutes(30);

    public PaymobPaymentService(
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        RedisConnectionFactory redisFactory,
        IEventPublisher eventPublisher,
        IPaymentLifecycleService paymentLifecycleService,
        IPaymobTenantConfigurationResolver tenantConfigurationResolver,
        IPaymobRuntimeApplicationService paymobRuntimeApplicationService,
        IPaymobWebhookRuntimeIntegrationService paymobWebhookRuntimeIntegrationService,
        IPaymobSensitiveArtifactProtector artifactProtector,
        StorefrontUrlResolver storefrontUrlResolver,
        ILogger<PaymobPaymentService> logger,
        CommerceReliabilityTelemetry? reliabilityTelemetry = null)
    {
        _dbContext = dbContext;
        _dataProtector = dataProtectionProvider.CreateProtector(DataProtectorPurpose);
        _redisFactory = redisFactory;
        _eventPublisher = eventPublisher;
        _paymentLifecycleService = paymentLifecycleService;
        _tenantConfigurationResolver = tenantConfigurationResolver;
        _paymobRuntimeApplicationService = paymobRuntimeApplicationService;
        _paymobWebhookRuntimeIntegrationService = paymobWebhookRuntimeIntegrationService;
        _artifactProtector = artifactProtector;
        _storefrontUrlResolver = storefrontUrlResolver;
        _logger = logger;
        _reliabilityTelemetry = reliabilityTelemetry;
    }

    #region Create Payment Intent

    /// <inheritdoc />
    public async Task<PaymobPaymentIntentResponseDto> CreatePaymentIntentAsync(
        Guid tenantId,
        Guid orderId,
        Guid? customerId,
        bool promotionCalculationFailed,
        string correlationId,
        CancellationToken cancellationToken = default,
        string? idempotencyKey = null)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "CreatePaymobPaymentIntent starting | TenantId: {TenantId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
            tenantId, orderId, correlationId);

        // CRITICAL: Start transaction and acquire row lock on Order before checking PaymentIntents
        // to prevent race condition where concurrent payment workers create duplicate PaymentIntents
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Step 1: Load and validate order (MUST happen before idempotency check for security) with row lock
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
        await ValidateCustomerOwnershipAsync(order, tenantId, orderId, customerId, correlationId, cancellationToken);

        idempotencyKey = NormalizeCheckoutPaymentIdempotencyKey(
            idempotencyKey,
            $"paymob:intent:{tenantId:N}:{orderId:N}");

        // Validate order status
        if (order.Status != OrderStatus.Pending || order.PaymentStatus != PaymentStatus.Pending)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new PaymentOrderInvalidStateException(
                orderId,
                $"Order:{order.Status}/Payment:{order.PaymentStatus}",
                "Order:Pending/Payment:Pending");
        }

        // Validate payment method is Paymob
        if (!order.PaymentMethodCode.Equals("PAYMOB", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new PaymentException("INVALID_PAYMENT_METHOD",
                $"Order {orderId} uses {order.PaymentMethodCode}, not PAYMOB");
        }

        // PHASE 10.4.X: Currency Authority Lockdown
        // Validate the immutable order currency before sending to Paymob.
        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId && t.DeletedAtUtc == null, cancellationToken);
        
        if (tenant == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new PaymentException("TENANT_NOT_FOUND", $"Tenant {tenantId} not found");
        }

        string validatedOrderCurrency;
        try
        {
            validatedOrderCurrency = FinancialCurrencyGuard.RequireCurrency(
                order.Currency,
                "order.currency",
                $"tenantId:{tenantId:D};orderId:{orderId:D};provider:Paymob");
        }
        catch (FinancialCurrencyMissingException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(
                ex,
                "CRITICAL: Order has no currency set | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                orderId, correlationId);
            throw new PaymentException("CURRENCY_NOT_SET", 
                "Order currency is not configured. This is a critical error.");
        }
        catch (FinancialCurrencyInvalidException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(
                ex,
                "CRITICAL: Order has invalid currency set | OrderId: {OrderId} | Currency: {Currency} | CorrelationId: {CorrelationId}",
                orderId, order.Currency, correlationId);
            throw new PaymentException("CURRENCY_INVALID",
                "Order currency is invalid. This is a critical error.");
        }

        _logger.LogInformation(
            "Currency validated for Paymob payment | OrderId: {OrderId} | Currency: {Currency} | CorrelationId: {CorrelationId}",
            orderId, validatedOrderCurrency, correlationId);

        await CheckoutInventoryAuthorityGuard.EnsureStorefrontOrderReservationsActiveAsync(
            _dbContext,
            order,
            cancellationToken);

        var storefrontBaseUrl = _storefrontUrlResolver.Resolve(tenant);

        CorePaymentIntent? seededPendingIntent = null;

        // Step 4: Check if PaymentIntent already exists for this order while holding row lock
        var existingIntent = await _dbContext.PaymentIntents
            .FirstOrDefaultAsync(
                pi => pi.OrderId == orderId && pi.TenantId == tenantId && pi.Provider == PaymentProvider.Paymob,
                cancellationToken);

        if (existingIntent != null && existingIntent.Status == PaymentIntentStatus.Pending)
        {
            var existingSession = GetStoredPaymentSession(existingIntent);
            var existingPaymentKey = existingSession?.PaymentKey;
            
            // A checkpointed Paymob artifact is resumed on this same durable local intent.
            // Never replace the intent or create a second provider order during recovery.
            if (string.IsNullOrWhiteSpace(existingIntent.ProviderIntentId) ||
                string.IsNullOrWhiteSpace(existingIntent.RedirectUrl) ||
                string.IsNullOrWhiteSpace(existingPaymentKey) ||
                PaymentIntentRoutingMetadataSerializer.HasPendingPaymobCheckpoint(existingIntent.ProviderMetadata))
            {
                // Different idempotency keys cannot take over the durable provider checkpoint.
                if (!string.IsNullOrWhiteSpace(existingIntent.IdempotencyKey) &&
                    !string.Equals(existingIntent.IdempotencyKey, idempotencyKey, StringComparison.Ordinal))
                {
                    var existingKeyHash = ComputeShortHash(existingIntent.IdempotencyKey);
                    var incomingKeyHash = ComputeShortHash(idempotencyKey);

                    _logger.LogWarning(
                        "Payment intent exists with different idempotency key, cannot resume provider checkpoint | TenantId: {TenantId} | PaymentIntentId: {PaymentIntentId} | OrderId: {OrderId} | ExistingKeyHash: {ExistingKeyHash} | IncomingKeyHash: {IncomingKeyHash} | KeysMatch: false | CorrelationId: {CorrelationId}",
                        tenantId, existingIntent.Id, orderId, existingKeyHash, incomingKeyHash, correlationId);

                    await transaction.RollbackAsync(cancellationToken);
                    _reliabilityTelemetry?.RecordPaymentIntentDuplicateAttempt("paymob", "idempotency_key_mismatch");
                    throw new PaymentException(
                        "PAYMENT_INTENT_IN_PROGRESS",
                        "A payment intent is already in progress for this order with a different request. Please retry with the original request.");
                }

                seededPendingIntent = existingIntent;
                _logger.LogInformation(
                    "Resuming durable Paymob provider checkpoint | PaymentIntentId: {PaymentIntentId} | OrderId: {OrderId} | ProviderOrderId: {ProviderOrderId} | CheckpointState: {CheckpointState} | CorrelationId: {CorrelationId}",
                    existingIntent.Id,
                    orderId,
                    existingIntent.ProviderIntentId,
                    PaymentIntentRoutingMetadataSerializer.Deserialize(existingIntent.ProviderMetadata)?.PaymobCheckpointState,
                    correlationId);
            }
            else
            {
                // Intent is complete, check if routing metadata needs update
                var routingMetadata = PaymentIntentRoutingMetadataSerializer.Deserialize(existingIntent.ProviderMetadata);
                if (!PaymentIntentRoutingMetadataSerializer.MatchesStoredIntent(routingMetadata, tenantId, orderId) ||
                    string.IsNullOrWhiteSpace(routingMetadata?.ProtectedPaymobSession))
                {
                    var protectedSession = _artifactProtector.ProtectPaymentSession(
                        existingSession!.PaymentKey,
                        existingSession.IframeUrl);
                    existingIntent.RedirectUrl = null;
                    existingIntent.ProviderMetadata = BuildRoutingMetadata(
                        tenantId,
                        orderId,
                        storefrontBaseUrl,
                        protectedSession);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                else
                {
                    // No changes made, just release the lock
                    await transaction.CommitAsync(cancellationToken);
                }

                // Return existing intent if still pending
                _logger.LogInformation(
                    "Returning existing Paymob PaymentIntent | OrderId: {OrderId} | IntentId: {IntentId} | CorrelationId: {CorrelationId}",
                    orderId, existingIntent.ProviderIntentId, correlationId);

                return new PaymobPaymentIntentResponseDto
                {
                    IframeUrl = existingSession?.IframeUrl ?? string.Empty,
                    PaymentKey = existingPaymentKey ?? string.Empty,
                    IntentionId = existingIntent.ProviderIntentId ?? string.Empty,
                    PaymentIntentId = existingIntent.Id,
                    OrderId = orderId,
                    OrderNumber = order.OrderNumber,
                    Amount = order.Total,
                    Currency = order.Currency
                };
            }
        }

        var paymentReference = seededPendingIntent?.PaymentReference ?? GeneratePaymentReference(orderId);
        var paymentIntent = seededPendingIntent ?? new CorePaymentIntent
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            TenantId = tenantId,
            Amount = order.Total,
            Currency = order.Currency,
            PaymentMethod = CorePaymentMethod.Card,
            Provider = PaymentProvider.Paymob,
            PaymentReference = paymentReference,
            IdempotencyKey = idempotencyKey,
            Status = PaymentIntentStatus.Pending,
            ProviderMetadata = BuildRoutingMetadata(
                tenantId,
                orderId,
                storefrontBaseUrl,
                protectedPaymobSession: null),
            CreatedAt = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(StorefrontPaymentSessionTtl)
        };

        if (seededPendingIntent == null)
        {
            _dbContext.PaymentIntents.Add(paymentIntent);
        }

        order.PaymentReference = paymentReference;
        order.PaymentGateway = "paymob";

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _reliabilityTelemetry?.RecordPaymentIntentDuplicateAttempt("paymob", "concurrent_claim");
            _dbContext.ChangeTracker.Clear();

            var claimedIntent = await _dbContext.PaymentIntents
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    pi => pi.OrderId == orderId && pi.TenantId == tenantId && pi.Provider == PaymentProvider.Paymob,
                    cancellationToken);

            var claimedMetadata = PaymentIntentRoutingMetadataSerializer.Deserialize(claimedIntent?.ProviderMetadata);
            var claimedSession = claimedIntent == null ? null : GetStoredPaymentSession(claimedIntent);
            var claimedPaymentKey = claimedSession?.PaymentKey;
            if (claimedIntent != null &&
                claimedIntent.Status == PaymentIntentStatus.Pending &&
                !string.IsNullOrWhiteSpace(claimedIntent.ProviderIntentId) &&
                !string.IsNullOrWhiteSpace(claimedPaymentKey) &&
                !string.IsNullOrWhiteSpace(claimedSession?.IframeUrl) &&
                !string.IsNullOrWhiteSpace(claimedMetadata?.ProtectedPaymobSession) &&
                !PaymentIntentRoutingMetadataSerializer.HasPendingPaymobCheckpoint(claimedIntent.ProviderMetadata))
            {
                // PRODUCTION HARDENING: Commit transaction before returning claimed intent
                await transaction.CommitAsync(cancellationToken);
                
                return new PaymobPaymentIntentResponseDto
                {
                    IframeUrl = claimedSession.IframeUrl,
                    PaymentKey = claimedPaymentKey,
                    IntentionId = claimedIntent.ProviderIntentId,
                    PaymentIntentId = claimedIntent.Id,
                    OrderId = orderId,
                    OrderNumber = order.OrderNumber,
                    Amount = order.Total,
                    Currency = order.Currency
                };
            }

            throw new PaymentException(
                "PAYMENT_INTENT_IN_PROGRESS",
                "Payment intent creation is already in progress. Please retry shortly.");
        }

        await transaction.CommitAsync(cancellationToken);

        string paymobOrderId;
        string paymentKey;
        string iframeUrl;

        try
        {
            var paymobRuntimeResult = await CreatePaymobProviderIntentAsync(
                tenantId,
                order,
                paymentIntent.Id,
                storefrontBaseUrl,
                customerId,
                paymentReference,
                idempotencyKey,
                correlationId,
                cancellationToken);

            paymobOrderId = paymobRuntimeResult.PaymobOrderId;
            paymentKey = paymobRuntimeResult.PaymentKey;
            iframeUrl = paymobRuntimeResult.IframeUrl;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MarkPersistedPaymobIntentFailedAsync(
                paymentIntent.Id,
                "PAYMOB_INTENT_CREATE_FAILED",
                ex.Message,
                cancellationToken);
            throw;
        }

        await PersistPaymobProviderIntentAsync(
            tenantId,
            orderId,
            paymentIntent.Id,
            paymentReference,
            promotionCalculationFailed,
            correlationId,
            storefrontBaseUrl,
            paymobOrderId,
            paymentKey,
            iframeUrl,
            cancellationToken);

        // Step 11: Build response
        var response = new PaymobPaymentIntentResponseDto
        {
            IframeUrl = iframeUrl,
            PaymentKey = paymentKey,
            IntentionId = paymobOrderId,
            PaymentIntentId = paymentIntent.Id,
            OrderId = orderId,
            OrderNumber = order.OrderNumber,
            Amount = order.Total,
            Currency = order.Currency
        };

        stopwatch.Stop();

        _logger.LogInformation(
            "Paymob intent created | TenantId: {TenantId} | OrderId: {OrderId} | PaymobOrderId: {PaymobOrderId} | DurationMs: {DurationMs} | CorrelationId: {CorrelationId}",
            tenantId, orderId, paymobOrderId, stopwatch.ElapsedMilliseconds, correlationId);

        return response;

    }

    private async Task<PaymobRuntimeApplicationResult> CreatePaymobProviderIntentAsync(
        Guid tenantId,
        Order order,
        Guid localPaymentIntentId,
        string storefrontBaseUrl,
        Guid? customerId,
        string paymentReference,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var nameParts = (order.GuestName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var country = await ResolveOrderCountryCodeAsync(order.TenantId, order.ShippingCountry, cancellationToken);

        var result = await _paymobRuntimeApplicationService.CreatePaymentIntentAsync(
            new PaymobRuntimePaymentIntentApplicationRequest(
                tenantId,
                order.Id,
                localPaymentIntentId,
                order.OrderNumber,
                order.Total,
                order.Currency,
                storefrontBaseUrl,
                customerId,
                paymentReference,
                idempotencyKey,
                correlationId,
                DateTime.UtcNow,
                DateTimeOffset.UtcNow,
                order.GuestEmail ?? "customer@example.com",
                nameParts.FirstOrDefault() ?? "Customer",
                nameParts.LastOrDefault() ?? "User",
                order.ShippingAddress ?? "NA",
                order.GuestPhone ?? "+20000000000",
                order.ShippingZipCode ?? "NA",
                order.ShippingCity ?? "NA",
                country,
                order.ShippingState ?? "NA"),
            cancellationToken);

        _logger.LogInformation(
            "Paymob provider intent created through runtime application boundary | PaymobOrderId: {PaymobOrderId} | Amount: {Amount} {Currency} | CorrelationId: {CorrelationId}",
            result.PaymobOrderId,
            order.Total,
            order.Currency,
            correlationId);

        return result;
    }

    private async Task PersistPaymobProviderIntentAsync(
        Guid tenantId,
        Guid orderId,
        Guid paymentIntentId,
        string paymentReference,
        bool promotionCalculationFailed,
        string correlationId,
        string storefrontBaseUrl,
        string paymobOrderId,
        string paymentKey,
        string iframeUrl,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 3;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            _dbContext.ChangeTracker.Clear();
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

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
                    throw new BasePaymentException(
                        "PAYMENT_INTENT_NOT_FOUND",
                        $"Persisted Paymob payment intent {paymentIntentId} was not found for order {orderId}.");
                }

                if (string.IsNullOrWhiteSpace(paymentIntent.ProviderIntentId) ||
                    string.IsNullOrWhiteSpace(PaymentIntentRoutingMetadataSerializer.Deserialize(paymentIntent.ProviderMetadata)?.ProtectedPaymobSession) ||
                    PaymentIntentRoutingMetadataSerializer.HasPendingPaymobCheckpoint(paymentIntent.ProviderMetadata))
                {
                    var protectedSession = _artifactProtector.ProtectPaymentSession(paymentKey, iframeUrl);
                    paymentIntent.ProviderIntentId = paymobOrderId;
                    paymentIntent.RedirectUrl = null;
                    paymentIntent.ProviderMetadata = BuildRoutingMetadata(
                        tenantId,
                        orderId,
                        storefrontBaseUrl,
                        protectedSession);

                    await _eventPublisher.PublishAsync(new PaymentIntentCreatedEvent
                    {
                        TenantId = tenantId,
                        OrderId = orderId,
                        PaymentIntentId = paymentIntent.Id,
                        OrderNumber = order.OrderNumber,
                        PaymentReference = paymentReference,
                        Provider = "paymob",
                        PaymentMethod = "PAYMOB",
                        Amount = order.Total,
                        Currency = order.Currency,
                        IsOfflineCollection = false,
                        RedirectUrl = null,
                        PromotionCalculationFailed = promotionCalculationFailed,
                        CorrelationId = correlationId,
                        OccurredAtUtc = DateTime.UtcNow
                    }, cancellationToken);

                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (attempt < maxRetries)
            {
                await transaction.RollbackAsync(cancellationToken);

                _logger.LogWarning(
                    ex,
                    "Failed to persist Paymob provider fields after provider session creation (attempt {Attempt}/{MaxRetries}) | OrderId: {OrderId} | PaymobOrderId: {PaymobOrderId} | CorrelationId: {CorrelationId}",
                    attempt,
                    maxRetries,
                    orderId,
                    paymobOrderId,
                    correlationId);

                await Task.Delay(100 * (int)Math.Pow(2, attempt - 1), cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        throw new BasePaymentException(
            "PAYMENT_INTENT_PERSISTENCE_FAILED",
            $"Paymob provider state for order {orderId} could not be durably persisted after retries.");
    }

    private async Task MarkPersistedPaymobIntentFailedAsync(
        Guid paymentIntentId,
        string failureCode,
        string failureReason,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var paymentIntent = await _dbContext.PaymentIntents
                .FirstOrDefaultAsync(pi => pi.Id == paymentIntentId, cancellationToken);

            if (paymentIntent != null && paymentIntent.Status == PaymentIntentStatus.Pending)
            {
                var hasDurableProviderCheckpoint =
                    !string.IsNullOrWhiteSpace(paymentIntent.ProviderIntentId) ||
                    PaymentIntentRoutingMetadataSerializer.HasPendingPaymobCheckpoint(paymentIntent.ProviderMetadata);

                if (hasDurableProviderCheckpoint)
                {
                    paymentIntent.FailedAt = null;
                    paymentIntent.FailureCode = "PAYMOB_PROVIDER_SESSION_RECOVERY_REQUIRED";
                    paymentIntent.FailureReason = failureReason;
                }
                else
                {
                    paymentIntent.Status = PaymentIntentStatus.Failed;
                    paymentIntent.FailedAt = DateTime.UtcNow;
                    paymentIntent.FailureCode = failureCode;
                    paymentIntent.FailureReason = failureReason;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    #endregion

    #region Get Payment Status

    /// <inheritdoc />
    public async Task<PaymobPaymentStatusDto> GetPaymentStatusAsync(
        Guid tenantId,
        Guid orderId,
        Guid? customerId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // Load order
        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == tenantId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            throw new PaymentOrderNotFoundException(orderId);
        }

        await EnsureOrderCustomerBelongsToTenantAsync(order, tenantId, correlationId, cancellationToken);
        await ValidateCustomerOwnershipAsync(order, tenantId, orderId, customerId, correlationId, cancellationToken);

        // Load latest payment intent
        var paymentIntent = await _dbContext.PaymentIntents
            .Where(pi => pi.OrderId == orderId && pi.TenantId == tenantId && pi.Provider == PaymentProvider.Paymob)
            .OrderByDescending(pi => pi.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (paymentIntent == null)
        {
            return new PaymobPaymentStatusDto
            {
                OrderId = orderId,
                OrderNumber = order.OrderNumber,
                PaymentStatus = "NotStarted",
                PaymobStatus = "none",
                IsPaid = false,
                IsPending = false
            };
        }

        var paymobStatus = paymentIntent.Status switch
        {
            PaymentIntentStatus.Pending => "pending",
            PaymentIntentStatus.Authorized => "authorized",
            PaymentIntentStatus.Paid => "captured",
            PaymentIntentStatus.Failed => "failed",
            _ => "unknown"
        };

        return new PaymobPaymentStatusDto
        {
            OrderId = orderId,
            OrderNumber = order.OrderNumber,
            PaymentStatus = order.PaymentStatus.ToString(),
            PaymobStatus = paymobStatus,
            IsPaid = CanonicalPaymentStatusResolver.IsPaidState(order.PaymentStatus),
            IsPending = paymentIntent.Status == PaymentIntentStatus.Pending,
            FailureReason = paymentIntent.FailureReason,
            TransactionId = paymentIntent.ProviderPaymentId
        };
    }

    #endregion

    #region Process Webhook

    /// <inheritdoc />
    public async Task<PaymobWebhookResultDto> ProcessWebhookAsync(
        Guid tenantId,
        string hmacSignature,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "ProcessPaymobWebhook starting | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            tenantId, correlationId);

        // Step 1: The current, non-deleted tenant authority must admit webhook capability,
        // environment, and credentials before any provider evidence is processed.
        var paymobConfig = await _tenantConfigurationResolver.ResolveCredentialsAsync(
            tenantId,
            PaymobOperationalCapability.Webhook,
            cancellationToken);

        // Step 2: Verify HMAC signature against the raw payload before business deserialization
        try
        {
            var canonicalHmacPayload = BuildWebhookHmacPayload(rawPayload);
            var isValidSignature = VerifyHmacSignature(canonicalHmacPayload, paymobConfig.HmacSecret, hmacSignature);
            if (!isValidSignature)
            {
                _logger.LogWarning(
                    "Paymob webhook HMAC invalid | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    tenantId, correlationId);

                throw new PaymobWebhookSignatureException();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse Paymob webhook payload during signature verification | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId, correlationId);

            throw new PaymobApiException("Invalid webhook payload format", ex);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "Paymob webhook payload missing required HMAC fields | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId, correlationId);

            throw new PaymobApiException(ex.Message, ex);
        }

        // Step 3: Parse payload after signature verification
        PaymobWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PaymobWebhookPayload>(rawPayload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse Paymob webhook payload after signature verification | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId, correlationId);
            
            throw new PaymobApiException("Invalid webhook payload format", ex);
        }

        if (payload?.Obj == null)
        {
            throw new PaymobApiException("Missing transaction object in webhook payload");
        }

        _logger.LogInformation(
            "Paymob webhook verified | TransactionId: {TransactionId} | Success: {Success} | IsRefunded: {IsRefunded} | CorrelationId: {CorrelationId}",
            payload.Obj.Id, payload.Obj.Success, payload.Obj.IsRefunded, correlationId);

        var transactionId = payload.Obj.Id.ToString();
        
        // PRODUCTION HARDENING: Include refund-specific suffix in idempotency key when IsRefunded is true
        // to allow incremental refund webhooks for the same transaction to be processed.
        // Without this, subsequent refund updates would be rejected as duplicates.
        var webhookIdempotencyKey = payload.Obj.IsRefunded
            ? $"paymob:webhook:{transactionId}:refund:{payload.Obj.AmountCents ?? 0}"
            : $"paymob:webhook:{transactionId}";
            
        var eventType = payload.Type ?? "transaction_processed";
        PaymentWebhookLog? webhookLog = null;
        var wasPreviouslyFailed = false;

        var runtimeAdmission = await _paymobWebhookRuntimeIntegrationService.EvaluateAndExecuteAsync(
            new PaymobWebhookRuntimeAdmissionRequest(
                tenantId,
                rawPayload,
                hmacSignature,
                paymobConfig.HmacSecret,
                correlationId,
                ReceivedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);

        if (runtimeAdmission.RejectedAsReplay || runtimeAdmission.RejectedAsDuplicate)
        {
            _logger.LogInformation(
                "Paymob webhook rejected before application persistence | TransactionId: {TransactionId} | Replay: {Replay} | Duplicate: {Duplicate} | CorrelationId: {CorrelationId}",
                transactionId,
                runtimeAdmission.RejectedAsReplay,
                runtimeAdmission.RejectedAsDuplicate,
                correlationId);

            return new PaymobWebhookResultDto
            {
                Processed = true,
                Message = runtimeAdmission.RejectedAsReplay ? "Replay event ignored" : "Duplicate event ignored",
                TransactionId = transactionId,
                EventType = eventType
            };
        }

        if (runtimeAdmission.InvalidCallback)
        {
            return new PaymobWebhookResultDto
            {
                Processed = false,
                Message = runtimeAdmission.Admission.ReasonCode,
                TransactionId = transactionId,
                EventType = eventType
            };
        }

        if (runtimeAdmission.Admission.Classification is PaymobWebhookOutcomeClassification.NonTerminal or PaymobWebhookOutcomeClassification.Ignored)
        {
            _logger.LogInformation(
                "Paymob webhook outcome not admitted to Runtime or lifecycle persistence | TransactionId: {TransactionId} | Classification: {Classification} | CorrelationId: {CorrelationId}",
                transactionId,
                runtimeAdmission.Admission.Classification,
                correlationId);

            return new PaymobWebhookResultDto
            {
                Processed = true,
                Message = runtimeAdmission.Admission.ReasonCode,
                TransactionId = transactionId,
                EventType = eventType
            };
        }

        if (PaymobRuntimeOutcomeGuard.IsRequiredOutcomeMissing(runtimeAdmission))
        {
            var runtimeResult = runtimeAdmission.AdapterResult?.RuntimeResult;
            _logger.LogError(
                "Paymob webhook Runtime execution rejected | TransactionId: {TransactionId} | EventType: {EventType} | RuntimeStatus: {RuntimeStatus} | FailureStage: {FailureStage} | FailureReason: {FailureReason} | CorrelationId: {CorrelationId}",
                transactionId,
                eventType,
                runtimeResult?.OverallStatus,
                runtimeResult?.Failure?.FailureStage,
                runtimeResult?.Failure?.FailureReason.Value,
                correlationId);
        }

        PaymobRuntimeOutcomeGuard.EnsureRequiredOutcome(runtimeAdmission);

        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var preparation = await PrepareWebhookLogAsync(
                    tenantId,
                    transactionId,
                    eventType,
                    rawPayload,
                    hmacSignature,
                    cancellationToken);

                webhookLog = preparation.Log;
                wasPreviouslyFailed = preparation.WasPreviouslyFailed;

                if (preparation.IsDuplicate)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    await StoreWebhookIdempotencyAsync(webhookIdempotencyKey, webhookLog.Id, correlationId);

                    _logger.LogInformation(
                        "Duplicate Paymob webhook | TransactionId: {TransactionId} | CorrelationId: {CorrelationId}",
                        transactionId, correlationId);

                    return new PaymobWebhookResultDto
                    {
                        Processed = true,
                        Message = "Duplicate event ignored",
                        EventLogId = webhookLog.Id,
                        TransactionId = transactionId,
                        EventType = eventType
                    };
                }

                if (preparation.IsInFlight)
                {
                    await transaction.RollbackAsync(cancellationToken);

                    _logger.LogWarning(
                        "Paymob webhook already in flight | TransactionId: {TransactionId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                        transactionId, tenantId, correlationId);

                    return new PaymobWebhookResultDto
                    {
                        Processed = false,
                        Message = "Webhook event is already being processed. Please retry.",
                        EventLogId = webhookLog.Id,
                        TransactionId = transactionId,
                        EventType = eventType
                    };
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();

                var existingLog = await TryGetWebhookLogAsync(tenantId, transactionId, cancellationToken);
                if (existingLog != null &&
                    (existingLog.ProcessingStatus == WebhookProcessingStatus.Completed ||
                     existingLog.ProcessingStatus == WebhookProcessingStatus.Skipped))
                {
                    await StoreWebhookIdempotencyAsync(webhookIdempotencyKey, existingLog.Id, correlationId);

                    _logger.LogInformation(
                        "Paymob webhook idempotency enforced by database | TransactionId: {TransactionId} | CorrelationId: {CorrelationId}",
                        transactionId, correlationId);

                    return new PaymobWebhookResultDto
                    {
                        Processed = true,
                        Message = "Duplicate event ignored",
                        EventLogId = existingLog.Id,
                        TransactionId = transactionId,
                        EventType = eventType
                    };
                }

                if (existingLog != null && existingLog.ProcessingStatus == WebhookProcessingStatus.Failed)
                {
                    _logger.LogCritical(
                        "Paymob webhook retry encountered previously failed event | EventId: {EventId} | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                        transactionId,
                        existingLog.OrderId,
                        tenantId,
                        correlationId);
                }

                return new PaymobWebhookResultDto
                {
                    Processed = false,
                    Message = "Webhook event is already being processed or awaiting retry.",
                    EventLogId = existingLog?.Id,
                    TransactionId = transactionId,
                    EventType = eventType
                };
            }

            var result = await ProcessPaymobTransactionAsync(
                payload.Obj, webhookLog!, tenantId, correlationId, cancellationToken);

            await _paymobWebhookRuntimeIntegrationService.PersistReplayRecordAsync(
                runtimeAdmission,
                webhookLog.PaymentIntentId,
                webhookLog.OrderId,
                cancellationToken);

            webhookLog.ProcessingStatus = WebhookProcessingStatus.Completed;
            webhookLog.ProcessedAt = DateTime.UtcNow;
            webhookLog.ProcessingDurationMs = stopwatch.ElapsedMilliseconds;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await _dbContext.Database.CommitTransactionAsync(cancellationToken);

            var redisStored = await StoreWebhookIdempotencyAsync(webhookIdempotencyKey, webhookLog.Id, correlationId);
            if (!redisStored)
            {
                _logger.LogWarning(
                    "Paymob webhook Redis idempotency store failed, relying on database log | EventId: {EventId} | WebhookLogId: {WebhookLogId} | CorrelationId: {CorrelationId}",
                    transactionId,
                    webhookLog.Id,
                    correlationId);
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "ProcessPaymobWebhook completed | TransactionId: {TransactionId} | DurationMs: {DurationMs} | CorrelationId: {CorrelationId}",
                transactionId, stopwatch.ElapsedMilliseconds, correlationId);

            return result;
        }
        catch (Exception ex)
        {
            try
            {
                if (_dbContext.Database.CurrentTransaction != null)
                {
                    await _dbContext.Database.RollbackTransactionAsync(CancellationToken.None);
                }
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(
                    rollbackEx,
                    "Transaction rollback failed | TenantId: {TenantId} | TransactionId: {TransactionId} | CorrelationId: {CorrelationId}",
                    tenantId,
                    transactionId,
                    correlationId);
            }

            if (webhookLog != null)
            {
                await PersistWebhookFailureAsync(
                    webhookLog,
                    tenantId,
                    transactionId,
                    eventType,
                    rawPayload,
                    hmacSignature,
                    ex.Message,
                    stopwatch.ElapsedMilliseconds,
                    wasPreviouslyFailed,
                    correlationId,
                    CancellationToken.None);
            }

            throw;
        }
    }

    private async Task<PaymobWebhookResultDto> ProcessPaymobTransactionAsync(
        PaymobTransactionObject transaction,
        PaymentWebhookLog webhookLog,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Extract order ID from merchant_order_id or metadata
        var paymobOrderId = transaction.Order?.Id?.ToString();
        
        if (string.IsNullOrEmpty(paymobOrderId))
        {
            throw new InvalidOperationException(
                $"Paymob transaction {transaction.Id} is missing an order reference.");
        }

        // Find our payment intent by Paymob order ID
        var intent = await _dbContext.PaymentIntents
            .Include(pi => pi.Order)
            .FirstOrDefaultAsync(pi => pi.ProviderIntentId == paymobOrderId && pi.TenantId == tenantId, cancellationToken);

        // HARDENING: Fallback lookup by scanning pending Paymob intents if ProviderIntentId lookup fails.
        // This handles the edge case where SaveChangesAsync failed after receiving Paymob response,
        // leaving ProviderIntentId blank. We can backfill it from the webhook.
        if (intent == null)
        {
            _logger.LogWarning(
                "Payment intent not found by ProviderIntentId, attempting fallback lookup by scanning pending intents | PaymobOrderId: {PaymobOrderId} | TransactionId: {TransactionId} | CorrelationId: {CorrelationId}",
                paymobOrderId, transaction.Id, correlationId);

            // Extract MerchantOrderId from webhook for deterministic matching
            var merchantOrderId = transaction.Order?.MerchantOrderId;
            
            if (string.IsNullOrWhiteSpace(merchantOrderId))
            {
                _logger.LogError(
                    "Webhook missing MerchantOrderId, cannot perform deterministic fallback lookup | PaymobOrderId: {PaymobOrderId} | TransactionId: {TransactionId} | CorrelationId: {CorrelationId}",
                    paymobOrderId, transaction.Id, correlationId);
                
                throw new PaymentIntentNotFoundException(paymobOrderId);
            }

            // Parse MerchantOrderId as Order.Id (Guid)
            if (!Guid.TryParse(merchantOrderId, out var orderId))
            {
                _logger.LogError(
                    "MerchantOrderId is not a valid Guid, cannot perform deterministic fallback lookup | MerchantOrderId: {MerchantOrderId} | PaymobOrderId: {PaymobOrderId} | CorrelationId: {CorrelationId}",
                    merchantOrderId, paymobOrderId, correlationId);
                
                throw new PaymentIntentNotFoundException(paymobOrderId);
            }

            // Deterministically match by OrderId from webhook's MerchantOrderId
            // Include Cancelled intents to handle rehydration scenarios where the original intent
            // was superseded but the original Paymob session succeeded
            intent = await _dbContext.PaymentIntents
                .Include(pi => pi.Order)
                .FirstOrDefaultAsync(pi => 
                    pi.OrderId == orderId &&
                    pi.TenantId == tenantId && 
                    pi.Provider == PaymentProvider.Paymob && 
                    (pi.Status == PaymentIntentStatus.Pending || pi.Status == PaymentIntentStatus.Cancelled) &&
                    (pi.ProviderIntentId == null || pi.ProviderIntentId == string.Empty),
                    cancellationToken);

            if (intent != null)
            {
                var statusNote = intent.Status == PaymentIntentStatus.Cancelled 
                    ? " (was Cancelled/superseded, will be updated)" 
                    : string.Empty;
                    
                _logger.LogInformation(
                    "Payment intent found via deterministic fallback (matched by OrderId from MerchantOrderId), backfilling ProviderIntentId{StatusNote} | PaymentIntentId: {PaymentIntentId} | OrderId: {OrderId} | PaymobOrderId: {PaymobOrderId} | Status: {Status} | CorrelationId: {CorrelationId}",
                    statusNote, intent.Id, orderId, paymobOrderId, intent.Status, correlationId);
                
                intent.ProviderIntentId = paymobOrderId;

                // Clear stale failure metadata when reviving a Cancelled intent
                if (intent.Status == PaymentIntentStatus.Cancelled && 
                    intent.FailureCode == "REHYDRATION_SUPERSEDED")
                {
                    _logger.LogInformation(
                        "Clearing stale failure metadata from revived intent | PaymentIntentId: {PaymentIntentId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                        intent.Id, orderId, correlationId);
                    
                    intent.FailureCode = null;
                    intent.FailureReason = null;
                    intent.FailedAt = null;
                }

                // Note: SaveChangesAsync will be called later in the success/failure handlers
            }
            else
            {
                _logger.LogError(
                    "No matching payment intent found via deterministic fallback | OrderId: {OrderId} | PaymobOrderId: {PaymobOrderId} | CorrelationId: {CorrelationId}",
                    orderId, paymobOrderId, correlationId);
                
                throw new PaymentIntentNotFoundException(paymobOrderId);
            }
        }

        if (intent == null || intent.Order == null)
        {
            throw new PaymentIntentNotFoundException(paymobOrderId);
        }

        webhookLog.PaymentIntentId = intent.Id;
        webhookLog.OrderId = intent.OrderId;

        if (transaction.IsRefunded && CanReconcileProviderRefund(intent.Order.PaymentStatus))
        {
            return await HandlePaymentRefundedAsync(intent, transaction, webhookLog, correlationId, cancellationToken);
        }

        return transaction.Success && !transaction.IsVoided && !transaction.IsRefunded
            ? await HandlePaymentSuccessAsync(intent, transaction, webhookLog, correlationId, cancellationToken)
            : await HandlePaymentFailureAsync(intent, transaction, webhookLog, correlationId, cancellationToken);
    }

    /// <summary>
    /// PHASE 11.4.2.A: Payment success now allocates money to OrderItems.
    /// MONEY IS TRACKED ON ORDER ITEMS (VARIANTS), NOT ON ORDERS.
    /// </summary>
    private async Task<PaymobWebhookResultDto> HandlePaymentSuccessAsync(
        CorePaymentIntent intent,
        PaymobTransactionObject transaction,
        PaymentWebhookLog webhookLog,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var order = intent.Order!;
        var now = DateTime.UtcNow;
        var providerCurrency = transaction.Currency?.Trim().ToUpperInvariant();
        var providerAmount = ResolvePaymobProviderAmount(transaction, providerCurrency ?? intent.Currency);

        var lifecycleResult = await _paymentLifecycleService.HandlePaymentResultAsync(
            new HandlePaymentResultRequest
            {
                OrderId = intent.OrderId,
                PaymentIntentId = intent.Id,
                Outcome = PaymentLifecycleOutcome.Succeeded,
                Source = PaymentLifecycleSource.GatewayWebhook,
                Gateway = "paymob",
                PaymentReference = intent.ProviderIntentId ?? intent.PaymentReference,
                ProviderPaymentId = transaction.Id.ToString(),
                ProviderAmount = providerAmount,
                ProviderCurrency = providerCurrency,
                WebhookEventId = transaction.Id.ToString(),
                ProcessedAtUtc = now
            },
            cancellationToken);

        if (!lifecycleResult.Success)
        {
            if (lifecycleResult.RequiresManualReview)
            {
                StorefrontSuspenseRecoveryPolicy.ApplyManualReviewMarker(
                    lifecycleResult,
                    webhookLog,
                    intent.ProviderIntentId ?? intent.PaymentReference);

                _logger.LogCritical(
                    "[PAYMOB-WEBHOOK] Payment success quarantined for manual review | OrderId: {OrderId} | TransactionId: {TransactionId} | FailureCode: {FailureCode} | Reason: {Reason}",
                    intent.OrderId,
                    transaction.Id,
                    lifecycleResult.FailureCode,
                    lifecycleResult.ErrorMessage);

                return new PaymobWebhookResultDto
                {
                    Processed = true,
                    Message = "Payment received after reservation expiry; quarantined for manual review.",
                    EventLogId = webhookLog.Id,
                    OrderId = intent.OrderId.ToString(),
                    TransactionId = transaction.Id.ToString(),
                    EventType = "payment_quarantined"
                };
            }

            throw new InvalidOperationException(
                $"Failed to capture Paymob payment: {lifecycleResult.ErrorMessage}");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Authoritative outcome events are staged inside PaymentLifecycleService.
        // ═══════════════════════════════════════════════════════════════════════
        // Recovery path: ensure inventory commit and downstream events fire even if
        // payment state was already committed on a prior attempt.
        
        if (!lifecycleResult.StateChanged)
        {
            _logger.LogInformation(
                "Paymob success replay - state unchanged; authoritative outcome events are owned by PaymentLifecycleService | OrderId: {OrderId} | TransactionId: {TransactionId}",
                intent.OrderId, transaction.Id);
        }
        else
        {
            _logger.LogInformation(
                "Paymob payment succeeded via lifecycle service | OrderId: {OrderId} | OrderNumber: {OrderNumber} | ProviderAmount: {ProviderAmount} | CorrelationId: {CorrelationId}",
                intent.OrderId, order.OrderNumber, providerAmount, correlationId);
        }

        return new PaymobWebhookResultDto
        {
            Processed = true,
            Message = "Payment captured successfully",
            EventLogId = webhookLog.Id,
            OrderId = intent.OrderId.ToString(),
            TransactionId = transaction.Id.ToString(),
            EventType = "payment_succeeded"
        };
    }

    private async Task<PaymobWebhookResultDto> HandlePaymentRefundedAsync(
        CorePaymentIntent intent,
        PaymobTransactionObject transaction,
        PaymentWebhookLog webhookLog,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var providerCurrency = transaction.Currency?.Trim().ToUpperInvariant();
        var currency = providerCurrency ?? intent.Currency;
        var providerAmount = ResolvePaymobProviderAmount(transaction, currency);
        if (!providerAmount.HasValue || providerAmount.Value <= 0m)
        {
            throw new InvalidOperationException(
                $"Paymob refund webhook {transaction.Id} did not include a valid refund amount.");
        }

        var refundAmount = await ResolvePaymobCumulativeRefundDeltaAsync(
            intent.TenantId,
            intent.OrderId,
            providerAmount.Value,
            cancellationToken);
        
        // PRODUCTION HARDENING: Use currency-specific precision for rounding and refund ID formatting
        // to ensure consistency with ResolvePaymobProviderAmount and support 3-decimal currencies
        var decimalPlaces = PointOfSale.Infrastructure.Payments.CurrencyHelper.GetDecimalPlaces(currency);
        refundAmount = Math.Round(refundAmount, decimalPlaces, MidpointRounding.AwayFromZero);
        
        if (refundAmount <= 0m)
        {
            _logger.LogInformation(
                "Paymob refund webhook already reflected locally | OrderId: {OrderId} | TransactionId: {TransactionId} | CorrelationId: {CorrelationId}",
                intent.OrderId,
                transaction.Id,
                correlationId);

            return new PaymobWebhookResultDto
            {
                Processed = true,
                Message = "Provider refund was already reconciled",
                EventLogId = webhookLog.Id,
                OrderId = intent.OrderId.ToString(),
                TransactionId = transaction.Id.ToString(),
                EventType = "payment_refunded"
            };
        }

        // Format refund ID with currency-specific precision for deterministic idempotency
        var formatString = "0." + new string('0', decimalPlaces);
        var refundId = FormattableString.Invariant($"paymob:refund:{transaction.Id}:{refundAmount.ToString(formatString, System.Globalization.CultureInfo.InvariantCulture)}");

        var lifecycleResult = await _paymentLifecycleService.RecordRefundAsync(
            intent.OrderId,
            refundAmount,
            refundId,
            $"Paymob provider refund reconciled from webhook transaction {transaction.Id}",
            cancellationToken);

        if (!lifecycleResult.Success)
        {
            throw new InvalidOperationException(
                $"Failed to reconcile Paymob provider refund {transaction.Id}: {lifecycleResult.ErrorMessage}");
        }

        _logger.LogWarning(
            "Paymob provider refund reconciled through payment lifecycle | OrderId: {OrderId} | TransactionId: {TransactionId} | Amount: {Amount} {Currency} | CorrelationId: {CorrelationId}",
            intent.OrderId,
            transaction.Id,
            refundAmount,
            currency,
            correlationId);

        return new PaymobWebhookResultDto
        {
            Processed = true,
            Message = "Provider refund reconciled successfully",
            EventLogId = webhookLog.Id,
            OrderId = intent.OrderId.ToString(),
            TransactionId = transaction.Id.ToString(),
            EventType = "payment_refunded"
        };
    }

    private static bool CanReconcileProviderRefund(PaymentStatus status) =>
        status is PaymentStatus.Collected
            or PaymentStatus.Settled
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded;

    private async Task<decimal> ResolvePaymobCumulativeRefundDeltaAsync(
        Guid tenantId,
        Guid orderId,
        decimal providerRefundedAmount,
        CancellationToken cancellationToken)
    {
        var existingLocalRefunded = await _dbContext.OrderRefunds
            .IgnoreQueryFilters()
            .Where(refund =>
                refund.TenantId == tenantId &&
                refund.OrderId == orderId &&
                !refund.IsDeleted)
            .SumAsync(refund => (decimal?)refund.Amount, cancellationToken) ?? 0m;

        return providerRefundedAmount - existingLocalRefunded;
    }

    private async Task<PaymobWebhookResultDto> HandlePaymentFailureAsync(
        CorePaymentIntent intent,
        PaymobTransactionObject transaction,
        PaymentWebhookLog webhookLog,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var order = intent.Order!;
        var now = DateTime.UtcNow;

        // Build failure reason
        var failureReason = transaction.DataMessage ?? transaction.Data?.Message ?? "Payment failed";
        if (transaction.IsVoided)
        {
            failureReason = "Payment voided";
        }
        else if (transaction.IsRefunded)
        {
            failureReason = "Payment refunded";
        }

        var lifecycleResult = await _paymentLifecycleService.HandlePaymentResultAsync(
            new HandlePaymentResultRequest
            {
                OrderId = intent.OrderId,
                PaymentIntentId = intent.Id,
                Outcome = PaymentLifecycleOutcome.Failed,
                Source = PaymentLifecycleSource.GatewayWebhook,
                Gateway = "paymob",
                PaymentReference = intent.ProviderIntentId ?? intent.PaymentReference,
                ProviderPaymentId = transaction.Id.ToString(),
                Amount = intent.Amount,
                Currency = intent.Currency,
                Reason = failureReason,
                FailureCode = transaction.Data?.TxnResponseCode,
                WebhookEventId = transaction.Id.ToString(),
                ProcessedAtUtc = now
            },
            cancellationToken);

        if (!lifecycleResult.Success)
        {
            throw new InvalidOperationException(
                $"Failed to record Paymob payment failure: {lifecycleResult.ErrorMessage}");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Authoritative outcome events are staged inside PaymentLifecycleService.
        // ═══════════════════════════════════════════════════════════════════════
        // Recovery path: ensure inventory release and downstream events fire even if
        // payment state was already committed on a prior attempt.
        
        if (!lifecycleResult.StateChanged)
        {
            _logger.LogInformation(
                "Paymob failure replay - state unchanged; authoritative outcome events are owned by PaymentLifecycleService | OrderId: {OrderId} | TransactionId: {TransactionId}",
                intent.OrderId, transaction.Id);
        }

        _logger.LogWarning(
            "Paymob payment failed | OrderId: {OrderId} | Reason: {Reason} | CorrelationId: {CorrelationId}",
            intent.OrderId, failureReason, correlationId);

        return new PaymobWebhookResultDto
        {
            Processed = true,
            Message = $"Payment failed: {failureReason}",
            EventLogId = webhookLog.Id,
            OrderId = intent.OrderId.ToString(),
            TransactionId = transaction.Id.ToString(),
            EventType = "payment_failed"
        };
    }

    #endregion

    #region Validate Callback

    /// <inheritdoc />
    public async Task<PaymobCallbackResultDto> ValidateCallbackAsync(
        Dictionary<string, string> callbackParams,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("ValidatePaymobCallback | CorrelationId: {CorrelationId}", correlationId);

        // Extract callback parameters
        callbackParams.TryGetValue("id", out var transactionId);
        callbackParams.TryGetValue("success", out var successStr);
        callbackParams.TryGetValue("hmac", out var hmacSignature);

        var isSuccess = successStr?.ToLowerInvariant() == "true";

        if (string.IsNullOrWhiteSpace(hmacSignature))
        {
            _logger.LogWarning(
                "Paymob callback missing HMAC signature | CorrelationId: {CorrelationId}",
                correlationId);

            return new PaymobCallbackResultDto
            {
                IsValid = false,
                IsSuccess = false,
                TransactionId = transactionId,
                Message = "Missing callback signature."
            };
        }

        // Get all matching payment intent candidates across all tenants
        var candidates = await ResolveCallbackPaymentIntentCandidatesAsync(callbackParams, cancellationToken);
        if (candidates.Count == 0)
        {
            _logger.LogWarning(
                "Paymob callback could not resolve any stored payment intent | CorrelationId: {CorrelationId}",
                correlationId);

            return new PaymobCallbackResultDto
            {
                IsValid = false,
                IsSuccess = false,
                TransactionId = transactionId,
                Message = "Payment callback context was not found."
            };
        }

        // Verify HMAC against each candidate until we find the correct tenant.
        // Routing metadata is required for a valid callback, but legacy pending
        // intents can lack it; verify the tenant HMAC first so invalid results
        // still carry the order context needed by the storefront recovery path.
        CorePaymentIntent? validIntent = null;
        PaymentIntentRoutingMetadata? validRoutingMetadata = null;
        CorePaymentIntent? verifiedIntentWithIncompleteRouting = null;

        foreach (var candidate in candidates)
        {
            PaymobTenantRuntimeCredentials paymobConfig;
            try
            {
                paymobConfig = await _tenantConfigurationResolver.ResolveCredentialsAsync(
                    candidate.TenantId,
                    PaymobOperationalCapability.Callback,
                    cancellationToken);
            }
            catch (PaymobNotConfiguredException)
            {
                _logger.LogDebug(
                    "Skipping candidate rejected by tenant Paymob authority | TenantId: {TenantId} | PaymentIntentId: {PaymentIntentId}",
                    candidate.TenantId,
                    candidate.Id);
                continue;
            }

            var isValidSignature = VerifyCallbackHmac(callbackParams, paymobConfig.HmacSecret, hmacSignature);
            if (!isValidSignature)
            {
                _logger.LogDebug(
                    "HMAC verification failed for candidate | PaymentIntentId: {PaymentIntentId} | TenantId: {TenantId}",
                    candidate.Id,
                    candidate.TenantId);
                continue;
            }

            var routingMetadata = PaymentIntentRoutingMetadataSerializer.Deserialize(candidate.ProviderMetadata);
            if (!PaymentIntentRoutingMetadataSerializer.MatchesStoredIntent(routingMetadata, candidate.TenantId, candidate.OrderId))
            {
                _logger.LogWarning(
                    "Paymob callback HMAC verified but stored routing metadata is incomplete, skipping candidate | PaymentIntentId: {PaymentIntentId} | TenantId: {TenantId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                    candidate.Id,
                    candidate.TenantId,
                    candidate.OrderId,
                    correlationId);
                verifiedIntentWithIncompleteRouting ??= candidate;
                continue;
            }

            validIntent = candidate;
            validRoutingMetadata = routingMetadata;
            _logger.LogInformation(
                "HMAC verified successfully for tenant | TenantId: {TenantId} | PaymentIntentId: {PaymentIntentId} | CorrelationId: {CorrelationId}",
                candidate.TenantId,
                candidate.Id,
                correlationId);
            break;
        }

        if (validIntent == null)
        {
            if (verifiedIntentWithIncompleteRouting != null)
            {
                _logger.LogWarning(
                    "Payment callback context is incomplete - HMAC verified but routing metadata missing | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | TransactionId: {TransactionId} | CorrelationId: {CorrelationId}",
                    verifiedIntentWithIncompleteRouting.TenantId,
                    verifiedIntentWithIncompleteRouting.OrderId,
                    verifiedIntentWithIncompleteRouting.Id,
                    transactionId,
                    correlationId);
                
                return new PaymobCallbackResultDto
                {
                    IsValid = false,
                    IsSuccess = false,
                    TenantId = verifiedIntentWithIncompleteRouting.TenantId,
                    OrderId = verifiedIntentWithIncompleteRouting.OrderId,
                    PaymentIntentId = verifiedIntentWithIncompleteRouting.Id,
                    TransactionId = transactionId,
                    Message = "Payment callback context is incomplete."
                };
            }

            _logger.LogWarning(
                "Invalid HMAC in Paymob callback - no matching tenant found | Candidates: {CandidateCount} | CorrelationId: {CorrelationId}",
                candidates.Count,
                correlationId);

            return new PaymobCallbackResultDto
            {
                IsValid = false,
                IsSuccess = false,
                TransactionId = transactionId,
                Message = "Invalid callback signature."
            };
        }

        return new PaymobCallbackResultDto
        {
            IsValid = true,
            IsSuccess = isSuccess,
            TenantId = validIntent.TenantId,
            OrderId = validIntent.OrderId,
            PaymentIntentId = validIntent.Id,
            StorefrontBaseUrl = validRoutingMetadata!.StorefrontBaseUrl,
            TransactionId = transactionId,
            Message = isSuccess ? "Payment successful" : "Payment pending or failed"
        };
    }

    #endregion

    #region Configuration Status

    /// <inheritdoc />
    public async Task<PaymobConfigurationStatusDto> GetConfigurationStatusAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var paymentMethod = await _dbContext.TenantPaymentMethods
            .FirstOrDefaultAsync(pm => pm.TenantId == tenantId && pm.Code == "PAYMOB", cancellationToken);

        if (paymentMethod == null)
        {
            return new PaymobConfigurationStatusDto
            {
                IsEnabled = false,
                IsConfigured = false,
                IsSandbox = true,
                ValidationMessage = "Paymob payment method not found"
            };
        }

        var configuration = EvaluatePaymentMethodConfiguration(paymentMethod, "status");
        var validationMessage = configuration.IsConfigurationValid
            ? null
            : configuration.ValidationErrors.FirstOrDefault() ?? "Paymob configuration is incomplete";

        return new PaymobConfigurationStatusDto
        {
            IsEnabled = paymentMethod.IsEnabled,
            IsConfigured = configuration.IsConfigurationValid,
            IsSandbox = paymentMethod.IsSandbox,
            ValidationMessage = validationMessage
        };
    }

    #endregion

    #region Helper Methods

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
                "Paymob order/customer tenant mismatch detected | TenantId: {TenantId} | OrderId: {OrderId} | OrderCustomerId: {OrderCustomerId} | CorrelationId: {CorrelationId}",
                tenantId,
                order.Id,
                order.OnlineCustomerId.Value,
                correlationId);
            throw new UnauthorizedAccessException(TenantErrorCodes.TenantMismatch);
        }
    }

    private async Task<string> ResolveOrderCountryCodeAsync(
        Guid tenantId,
        string? explicitCountryCode,
        CancellationToken cancellationToken)
    {
        var tenantCountryCode = await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Country)
            .FirstOrDefaultAsync(cancellationToken);

        return CountryCodeHelper.NormalizeOrDefault(explicitCountryCode, tenantCountryCode);
    }

    private async Task ValidateCustomerOwnershipAsync(
        Order order,
        Guid tenantId,
        Guid orderId,
        Guid? customerId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!customerId.HasValue)
        {
            if (order.OnlineCustomerId.HasValue)
            {
                _logger.LogWarning(
                    "Unauthenticated Paymob access attempt for authenticated customer order | OrderId: {OrderId} | OrderCustomerId: {OrderCustomerId} | CorrelationId: {CorrelationId}",
                    orderId,
                    order.OnlineCustomerId.Value,
                    correlationId);
                throw new PaymentUnauthorizedAccessException(orderId, null);
            }

            return;
        }

        var customer = await _dbContext.OnlineCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId.Value, cancellationToken);

        if (customer == null || customer.TenantId != tenantId)
        {
            _logger.LogWarning(
                "Paymob customer tenant mismatch detected | TenantId: {TenantId} | OrderId: {OrderId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
                tenantId,
                orderId,
                customerId.Value,
                correlationId);
            throw new UnauthorizedAccessException(TenantErrorCodes.TenantMismatch);
        }

        // Allow authenticated customers to retry guest orders (order.OnlineCustomerId is null)
        // This enables scenarios where a logged-in user retries a payment for a guest order
        if (!order.OnlineCustomerId.HasValue)
        {
            _logger.LogInformation(
                "Authenticated customer accessing guest order for Paymob payment | OrderId: {OrderId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
                orderId,
                customerId.Value,
                correlationId);
            return;
        }

        if (order.OnlineCustomerId != customerId.Value)
        {
            _logger.LogWarning(
                "Paymob customer ownership validation failed | OrderId: {OrderId} | RequestingCustomerId: {RequestingCustomerId} | OrderCustomerId: {OrderCustomerId} | CorrelationId: {CorrelationId}",
                orderId,
                customerId.Value,
                order.OnlineCustomerId,
                correlationId);
            throw new PaymentUnauthorizedAccessException(orderId, customerId);
        }
    }

    private PaymentMethodConfigurationValidationResult EvaluatePaymentMethodConfiguration(
        TenantPaymentMethod paymentMethod,
        string operation)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod.ConfigEncrypted))
        {
            var result = PaymentMethodConfigurationValidator.Validate(paymentMethod.Code, null);
            if (!result.IsConfigurationValid)
            {
                _reliabilityTelemetry?.RecordPaymentConfigInvalid("paymob", operation, "config_missing");
            }

            return result;
        }

        try
        {
            var decrypted = _dataProtector.Unprotect(paymentMethod.ConfigEncrypted);
            var configuration = JsonSerializer.Deserialize<Dictionary<string, string>>(decrypted, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var result = PaymentMethodConfigurationValidator.Validate(paymentMethod.Code, configuration);

            if (!result.IsConfigurationValid)
            {
                _reliabilityTelemetry?.RecordPaymentConfigInvalid("paymob", operation, result.ValidationErrors.FirstOrDefault() ?? "config_invalid");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt Paymob configuration");
            _reliabilityTelemetry?.RecordPaymentConfigInvalid("paymob", operation, "decrypt_failed");

            return new PaymentMethodConfigurationValidationResult(
                paymentMethod.Code?.Trim().ToUpperInvariant() ?? string.Empty,
                PaymentMethodConfigurationValidator.RequiresConfiguration(paymentMethod.Code),
                false,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new[] { "Stored Paymob configuration could not be decrypted." },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private async Task<List<CorePaymentIntent>> ResolveCallbackPaymentIntentCandidatesAsync(
        IReadOnlyDictionary<string, string> callbackParams,
        CancellationToken cancellationToken)
    {
        var candidates = new List<CorePaymentIntent>();

        callbackParams.TryGetValue("order", out var paymobOrderId);
        if (!string.IsNullOrWhiteSpace(paymobOrderId))
        {
            var intentsByProviderIntentId = await _dbContext.PaymentIntents
                .IgnoreQueryFilters()
                .Where(pi => pi.Provider == PaymentProvider.Paymob &&
                             pi.ProviderIntentId == paymobOrderId)
                .ToListAsync(cancellationToken);

            candidates.AddRange(intentsByProviderIntentId);
        }

        callbackParams.TryGetValue("id", out var providerPaymentId);
        if (!string.IsNullOrWhiteSpace(providerPaymentId))
        {
            var intentsByProviderPaymentId = await _dbContext.PaymentIntents
                .IgnoreQueryFilters()
                .Where(pi => pi.Provider == PaymentProvider.Paymob &&
                             pi.ProviderPaymentId == providerPaymentId)
                .ToListAsync(cancellationToken);

            // Add only candidates not already in the list (avoid duplicates if both IDs match)
            foreach (var intent in intentsByProviderPaymentId)
            {
                if (!candidates.Any(c => c.Id == intent.Id))
                {
                    candidates.Add(intent);
                }
            }
        }

        return candidates;
    }

    private static string BuildRoutingMetadata(
        Guid tenantId,
        Guid orderId,
        string storefrontBaseUrl,
        string? protectedPaymobSession = null)
    {
        return PaymentIntentRoutingMetadataSerializer.Serialize(
            tenantId,
            orderId,
            storefrontBaseUrl,
            protectedPaymobSession: protectedPaymobSession);
    }

    private PaymobProtectedPaymentSession? GetStoredPaymentSession(CorePaymentIntent paymentIntent)
    {
        var metadata = PaymentIntentRoutingMetadataSerializer.Deserialize(paymentIntent.ProviderMetadata);
        if (!string.IsNullOrWhiteSpace(metadata?.ProtectedPaymobSession))
        {
            return _artifactProtector.UnprotectPaymentSession(metadata.ProtectedPaymobSession);
        }

        if (!string.IsNullOrWhiteSpace(metadata?.PaymentKey) &&
            !string.IsNullOrWhiteSpace(paymentIntent.RedirectUrl))
        {
            return new PaymobProtectedPaymentSession(metadata.PaymentKey, paymentIntent.RedirectUrl);
        }

        return null;
    }

    private static decimal? ResolvePaymobProviderAmount(PaymobTransactionObject transaction, string currency)
    {
        if (transaction.AmountCents.HasValue)
        {
            return ConvertFromPaymobAmount(transaction.AmountCents.Value, currency);
        }

        if (transaction.PaidAmount.HasValue)
        {
            return ConvertFromPaymobAmount((long)Math.Round(transaction.PaidAmount.Value), currency);
        }

        return null;
    }

    private static decimal ConvertFromPaymobAmount(long amount, string currency)
    {
        var decimalPlaces = PointOfSale.Infrastructure.Payments.CurrencyHelper.GetDecimalPlaces(currency);
        return amount / (decimal)Math.Pow(10, decimalPlaces);
    }

    private static string GeneratePaymentReference(Guid orderId)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var orderIdShort = orderId.ToString("N")[..8].ToUpperInvariant();
        return $"PMB-{timestamp}-{orderIdShort}";
    }

    private static string NormalizeCheckoutPaymentIdempotencyKey(string? requestedKey, string fallbackKey)
    {
        var key = string.IsNullOrWhiteSpace(requestedKey)
            ? fallbackKey
            : requestedKey.Trim();

        if (key.Length <= 255)
        {
            return key;
        }

        // When key exceeds 255 chars, create a deterministic shortened version:
        // Keep a stable prefix + append a hash suffix to preserve uniqueness
        // Format: [prefix (191 chars)]...[SHA256 hash (64 chars hex)]
        // Total: 191 + 3 (ellipsis) + 64 = 258, but we'll use 187 + 3 + 64 = 254 to stay under 255
        const int prefixLength = 187;
        const int hashLength = 64; // SHA256 produces 32 bytes = 64 hex chars
        
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key));
        var hashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        
        var prefix = key[..prefixLength];
        return $"{prefix}...{hashHex}";
    }

    private static string BuildWebhookHmacPayload(string rawPayload)
    {
        using var document = JsonDocument.Parse(rawPayload);
        if (!document.RootElement.TryGetProperty("obj", out var transaction) ||
            transaction.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Missing transaction object in webhook payload");
        }

        return string.Concat(
            GetJsonNumericString(transaction, "amount_cents"),
            GetJsonString(transaction, "created_at"),
            GetJsonString(transaction, "currency"),
            GetJsonBooleanString(transaction, "error_occured"),
            GetJsonBooleanString(transaction, "has_parent_transaction"),
            GetJsonNumericString(transaction, "id"),
            GetJsonNumericString(transaction, "integration_id"),
            GetJsonBooleanString(transaction, "is_3d_secure"),
            GetJsonBooleanString(transaction, "is_auth"),
            GetJsonBooleanString(transaction, "is_capture"),
            GetJsonBooleanString(transaction, "is_refunded"),
            GetJsonBooleanString(transaction, "is_standalone_payment"),
            GetJsonBooleanString(transaction, "is_voided"),
            GetJsonNestedNumericString(transaction, "order", "id"),
            GetJsonNumericString(transaction, "owner"),
            GetJsonBooleanString(transaction, "pending"),
            GetJsonNestedString(transaction, "source_data", "pan"),
            GetJsonNestedString(transaction, "source_data", "sub_type"),
            GetJsonNestedString(transaction, "source_data", "type"),
            GetJsonBooleanString(transaction, "success"));
    }

    private bool VerifyHmacSignature(string canonicalPayload, string hmacSecret, string providedHmac)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(hmacSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalPayload));
        var calculatedHmac = BitConverter.ToString(hash).Replace("-", "").ToLower();

        return FixedTimeHexEquals(calculatedHmac, providedHmac);
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => property.GetRawText().Trim('"')
        };
    }

    private static string GetJsonNumericString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => string.Empty
        };
    }

    private static string GetJsonBooleanString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.String when bool.TryParse(property.GetString(), out var booleanValue) => booleanValue
                ? "true"
                : "false",
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => string.Empty
        };
    }

    private static string GetJsonNestedString(JsonElement element, string parentPropertyName, string propertyName)
    {
        if (!element.TryGetProperty(parentPropertyName, out var parent) ||
            parent.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return GetJsonString(parent, propertyName);
    }

    private static string GetJsonNestedNumericString(JsonElement element, string parentPropertyName, string propertyName)
    {
        if (!element.TryGetProperty(parentPropertyName, out var parent) ||
            parent.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return GetJsonNumericString(parent, propertyName);
    }

    private bool VerifyCallbackHmac(Dictionary<string, string> callbackParams, string hmacSecret, string providedHmac)
    {
        // Callback HMAC verification - simpler than transaction HMAC
        // Concatenate specific query parameters
        var fieldsToHash = new[] { "amount_cents", "created_at", "currency", "error_occured", 
            "has_parent_transaction", "id", "integration_id", "is_3d_secure", "is_auth", 
            "is_capture", "is_refunded", "is_standalone_payment", "is_voided", "order", 
            "owner", "pending", "source_data.pan", "source_data.sub_type", "source_data.type", "success" };

        var values = fieldsToHash.Select(f => callbackParams.GetValueOrDefault(f, ""));
        var concatenated = string.Join("", values);

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(hmacSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(concatenated));
        var calculatedHmac = BitConverter.ToString(hash).Replace("-", "").ToLower();

        return FixedTimeHexEquals(calculatedHmac, providedHmac);
    }

    private static bool FixedTimeHexEquals(string expectedHex, string? actualHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex) || string.IsNullOrWhiteSpace(actualHex))
        {
            return false;
        }

        try
        {
            var expectedBytes = Convert.FromHexString(expectedHex);
            var actualBytes = Convert.FromHexString(actualHex.Trim());
            return expectedBytes.Length == actualBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async Task<PaymentWebhookLog?> TryGetWebhookLogAsync(
        Guid tenantId,
        string eventId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.PaymentWebhookLogs
            .FirstOrDefaultAsync(
                log => log.TenantId == tenantId &&
                       log.Provider == "paymob" &&
                       log.ProviderEventId == eventId,
                cancellationToken);
    }

    private async Task<(PaymentWebhookLog Log, bool IsDuplicate, bool IsInFlight, bool WasPreviouslyFailed)> PrepareWebhookLogAsync(
        Guid tenantId,
        string eventId,
        string eventType,
        string rawPayload,
        string signature,
        CancellationToken cancellationToken)
    {
        var protectedPayload = _artifactProtector.ProtectWebhookPayload(rawPayload);
        var signatureDigest = _artifactProtector.DigestHmacSignature(signature);
        var existingLog = await TryGetWebhookLogAsync(tenantId, eventId, cancellationToken);
        if (existingLog != null)
        {
            if (existingLog.ProcessingStatus == WebhookProcessingStatus.Completed ||
                existingLog.ProcessingStatus == WebhookProcessingStatus.Skipped)
            {
                return (existingLog, true, false, false);
            }

            if (existingLog.ProcessingStatus == WebhookProcessingStatus.Processing)
            {
                return (existingLog, false, true, false);
            }

            var wasPreviouslyFailed = existingLog.ProcessingStatus == WebhookProcessingStatus.Failed;
            existingLog.EventType = eventType;
            existingLog.RawPayload = protectedPayload;
            existingLog.Signature = signatureDigest;
            existingLog.SignatureVerified = true;
            existingLog.ProcessingStatus = WebhookProcessingStatus.Processing;
            existingLog.ProcessingError = null;
            existingLog.ProcessedAt = null;
            existingLog.ProcessingDurationMs = null;
            return (existingLog, false, false, wasPreviouslyFailed);
        }

        var webhookLog = new PaymentWebhookLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = "paymob",
            ProviderEventId = eventId,
            EventType = eventType,
            RawPayload = protectedPayload,
            Signature = signatureDigest,
            SignatureVerified = true,
            ProcessingStatus = WebhookProcessingStatus.Processing,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PaymentWebhookLogs.Add(webhookLog);
        return (webhookLog, false, false, false);
    }

    private async Task PersistWebhookFailureAsync(
        PaymentWebhookLog webhookLog,
        Guid tenantId,
        string eventId,
        string eventType,
        string rawPayload,
        string signature,
        string error,
        long durationMs,
        bool wasPreviouslyFailed,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var protectedPayload = _artifactProtector.ProtectWebhookPayload(rawPayload);
            var signatureDigest = _artifactProtector.DigestHmacSignature(signature);
            _dbContext.ChangeTracker.Clear();

            var persistedLog = await TryGetWebhookLogAsync(tenantId, eventId, cancellationToken);
            if (persistedLog == null)
            {
                persistedLog = new PaymentWebhookLog
                {
                    Id = webhookLog.Id,
                    TenantId = tenantId,
                    Provider = "paymob",
                    ProviderEventId = eventId,
                    EventType = eventType,
                    PaymentIntentId = webhookLog.PaymentIntentId,
                    OrderId = webhookLog.OrderId,
                    RawPayload = protectedPayload,
                    Signature = signatureDigest,
                    SignatureVerified = true,
                    ProcessingStatus = WebhookProcessingStatus.Failed,
                    ProcessingError = error,
                    ProcessingDurationMs = durationMs,
                    ProcessedAt = DateTime.UtcNow,
                    CreatedAt = webhookLog.CreatedAt == default ? DateTime.UtcNow : webhookLog.CreatedAt
                };

                _dbContext.PaymentWebhookLogs.Add(persistedLog);
            }
            else
            {
                persistedLog.EventType = eventType;
                persistedLog.PaymentIntentId = webhookLog.PaymentIntentId ?? persistedLog.PaymentIntentId;
                persistedLog.OrderId = webhookLog.OrderId ?? persistedLog.OrderId;
                persistedLog.RawPayload = protectedPayload;
                persistedLog.Signature = signatureDigest;
                persistedLog.SignatureVerified = true;
                persistedLog.ProcessingStatus = WebhookProcessingStatus.Failed;
                persistedLog.ProcessingError = error;
                persistedLog.ProcessingDurationMs = durationMs;
                persistedLog.ProcessedAt = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (wasPreviouslyFailed)
            {
                _logger.LogCritical(
                    "Paymob webhook repeatedly failed | EventId: {EventId} | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    eventId,
                    persistedLog.OrderId,
                    tenantId,
                    correlationId);
            }
        }
        catch (Exception persistenceEx)
        {
            _logger.LogError(
                persistenceEx,
                "Failed to persist Paymob webhook failure log | EventId: {EventId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                eventId,
                tenantId,
                correlationId);
        }
    }

    private async Task<bool> StoreWebhookIdempotencyAsync(string key, Guid webhookLogId, string correlationId)
    {
        try
        {
            var db = _redisFactory.GetDatabase();
            if (db == null) return false;

            await db.StringSetAsync(key, webhookLogId.ToString(), TimeSpan.FromHours(72));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook idempotency store error | Key: {Key} | CorrelationId: {CorrelationId}", key, correlationId);
            return false;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation;

    /// <summary>
    /// Computes a short hash (first 8 characters of SHA256) for logging purposes.
    /// Used to mask sensitive idempotency keys while maintaining debuggability.
    /// </summary>
    private static string ComputeShortHash(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "[empty]";
        
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hashHex = Convert.ToHexString(hashBytes);
        return hashHex.Substring(0, Math.Min(8, hashHex.Length)).ToLowerInvariant();
    }

    #endregion
}

#region Paymob API Models

internal class PaymobWebhookPayload
{
    public string? Type { get; set; }
    public PaymobTransactionObject? Obj { get; set; }
}

internal class PaymobTransactionObject
{
    public long Id { get; set; }
    public bool Pending { get; set; }
    [JsonPropertyName("amount_cents")]
    public long? AmountCents { get; set; }
    [JsonPropertyName("paid_amount")]
    public decimal? PaidAmount { get; set; }
    public bool Success { get; set; }
    [JsonPropertyName("is_auth")]
    public bool IsAuth { get; set; }
    [JsonPropertyName("is_capture")]
    public bool IsCapture { get; set; }
    [JsonPropertyName("is_standalone_payment")]
    public bool IsStandalonePayment { get; set; }
    [JsonPropertyName("is_voided")]
    public bool IsVoided { get; set; }
    [JsonPropertyName("is_refunded")]
    public bool IsRefunded { get; set; }
    [JsonPropertyName("is_3d_secure")]
    public bool Is3dSecure { get; set; }
    [JsonPropertyName("integration_id")]
    public long? IntegrationId { get; set; }
    [JsonPropertyName("has_parent_transaction")]
    public bool HasParentTransaction { get; set; }
    public PaymobOrderObject? Order { get; set; }
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
    public string? Currency { get; set; }
    [JsonPropertyName("source_data")]
    public PaymobSourceData? SourceData { get; set; }
    public long? Owner { get; set; }
    [JsonPropertyName("error_occured")]
    public bool ErrorOccured { get; set; }
    public PaymobDataObject? Data { get; set; }
    [JsonPropertyName("data.message")]
    public string? DataMessage { get; set; }
}

internal class PaymobOrderObject
{
    public long? Id { get; set; }
    [JsonPropertyName("merchant_order_id")]
    public string? MerchantOrderId { get; set; }
}

internal class PaymobSourceData
{
    public string? Type { get; set; }
    public string? Pan { get; set; }
    [JsonPropertyName("sub_type")]
    public string? SubType { get; set; }
}

internal class PaymobDataObject
{
    public string? Message { get; set; }
    [JsonPropertyName("txn_response_code")]
    public string? TxnResponseCode { get; set; }
}

#endregion
