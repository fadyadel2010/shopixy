using System.Diagnostics;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Security;
using PointOfSale.API.Services;
using PointOfSale.API.Services.BackgroundTasks;
using PointOfSale.API.Services.Payments;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Redis.Services;
using Stripe;
using StripeEvent = Stripe.Event;
using StripePaymentIntentEntity = Stripe.PaymentIntent;
using CorePaymentMethod = PointOfSale.Core.Enums.PaymentMethod;
using CorePaymentIntent = PointOfSale.Core.Entities.PaymentIntent;
using PointOfSale.Core.Payments;

namespace PointOfSale.API.Services.Payments.Stripe;

/// <summary>
/// Stripe Payment Service implementation.
/// 
/// PHASE 10.4.D4: Stripe Integration (Global, Intent-Based, Production-Grade)
/// 
/// This service integrates with Stripe using Payment Intents API.
/// Each tenant has their own Stripe configuration (multi-tenant).
/// 
/// ARCHITECTURAL RULES:
/// - Order is created BEFORE calling Stripe
/// - Stripe PaymentIntent amount = order.Total (NO recalculation)
/// - Webhooks are the ONLY source of truth for payment status
/// - All operations are idempotent
/// - Secrets are decrypted only when needed
/// </summary>
public class StripePaymentService : IStripePaymentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDataProtector _dataProtector;
    private readonly IPaymentIntentClientSecretProtector _clientSecretProtector;
    private readonly RedisConnectionFactory _redisFactory;
    private readonly ICheckoutSessionTokenService _checkoutSessionTokenService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IPaymentLifecycleService _paymentLifecycleService;
    private readonly IStripeTenantConfigurationResolver _stripeTenantConfigurationResolver;
    private readonly IStripeRuntimeApplicationService _stripeRuntimeApplicationService;
    private readonly IStripeWebhookRuntimeIntegrationService _stripeWebhookRuntimeIntegrationService;
    private readonly StorefrontUrlResolver _storefrontUrlResolver;
    private readonly ILogger<StripePaymentService> _logger;
    private readonly CommerceReliabilityTelemetry? _reliabilityTelemetry;
    private readonly StripeOperationalTelemetry? _stripeTelemetry;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IBackgroundTaskQueue<RedirectUrlRecoveryWorkItem> _recoveryQueue;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string DataProtectorPurpose = "PaymentMethodSecrets";
    private const int IdempotencyTtlHours = 24;
    private static readonly TimeSpan StorefrontPaymentSessionTtl = TimeSpan.FromMinutes(30);

    public StripePaymentService(
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        IPaymentIntentClientSecretProtector clientSecretProtector,
        RedisConnectionFactory redisFactory,
        ICheckoutSessionTokenService checkoutSessionTokenService,
        IEventPublisher eventPublisher,
        IPaymentLifecycleService paymentLifecycleService,
        IStripeTenantConfigurationResolver stripeTenantConfigurationResolver,
        IStripeRuntimeApplicationService stripeRuntimeApplicationService,
        IStripeWebhookRuntimeIntegrationService stripeWebhookRuntimeIntegrationService,
        StorefrontUrlResolver storefrontUrlResolver,
        ILogger<StripePaymentService> logger,
        IServiceScopeFactory serviceScopeFactory,
        IBackgroundTaskQueue<RedirectUrlRecoveryWorkItem> recoveryQueue,
        CommerceReliabilityTelemetry? reliabilityTelemetry = null,
        StripeOperationalTelemetry? stripeTelemetry = null)
    {
        _dbContext = dbContext;
        _dataProtector = dataProtectionProvider.CreateProtector(DataProtectorPurpose);
        _clientSecretProtector = clientSecretProtector;
        _redisFactory = redisFactory;
        _checkoutSessionTokenService = checkoutSessionTokenService;
        _eventPublisher = eventPublisher;
        _paymentLifecycleService = paymentLifecycleService;
        _stripeTenantConfigurationResolver = stripeTenantConfigurationResolver;
        _stripeRuntimeApplicationService = stripeRuntimeApplicationService;
        _stripeWebhookRuntimeIntegrationService = stripeWebhookRuntimeIntegrationService;
        _storefrontUrlResolver = storefrontUrlResolver;
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _recoveryQueue = recoveryQueue;
        _reliabilityTelemetry = reliabilityTelemetry;
        _stripeTelemetry = stripeTelemetry;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    #region Create Payment Intent

    /// <inheritdoc />
    public async Task<StripePaymentIntentResponseDto> CreatePaymentIntentAsync(
        Guid tenantId,
        Guid orderId,
        string checkoutToken,
        Guid? customerId,
        bool promotionCalculationFailed,
        string correlationId,
        CancellationToken cancellationToken = default,
        string? idempotencyKey = null)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "CreateStripePaymentIntent starting | TenantId: {TenantId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
            tenantId, orderId, correlationId);
        _stripeTelemetry?.RecordPaymentCreated(tenantId, "storefront");

        // CRITICAL: Start transaction and acquire row lock on Order before checking PaymentIntents
        // to prevent race condition where concurrent payment workers create duplicate PaymentIntents
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Step 1: Load and validate order with row lock
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

        // Step 1.5: Validate checkout session ownership BEFORE any cache lookup
        await ValidateCheckoutSessionOwnershipAsync(checkoutToken, tenantId, orderId, order.SessionId, correlationId, cancellationToken);

        idempotencyKey = NormalizeCheckoutPaymentIdempotencyKey(
            idempotencyKey,
            $"stripe:intent:{tenantId:N}:{orderId:N}");

        // Step 3: Validate order status
        if (order.Status != OrderStatus.Pending || order.PaymentStatus != PaymentStatus.Pending)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new PaymentOrderInvalidStateException(
                orderId,
                $"Order:{order.Status}/Payment:{order.PaymentStatus}",
                "Order:Pending/Payment:Pending");
        }

        // Validate payment method is Stripe
        if (!order.PaymentMethodCode.Equals("STRIPE", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new PaymentException("INVALID_PAYMENT_METHOD",
                $"Order {orderId} uses {order.PaymentMethodCode}, not STRIPE");
        }

        // PHASE 10.4.X: Currency Authority Lockdown
        // Validate the immutable order currency before sending to Stripe.
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
                $"tenantId:{tenantId:D};orderId:{orderId:D};provider:Stripe");
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
            "Currency validated for Stripe payment | OrderId: {OrderId} | Currency: {Currency} | CorrelationId: {CorrelationId}",
            orderId, validatedOrderCurrency, correlationId);

        await CheckoutInventoryAuthorityGuard.EnsureStorefrontOrderReservationsActiveAsync(
            _dbContext,
            order,
            cancellationToken);

        // Step 4: Get Stripe configuration for tenant
        var stripeConfig = await GetStripeConfigurationAsync(
            tenantId,
            StripeOperationalCapability.PaymentIntent,
            cancellationToken);
        if (stripeConfig == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new StripeNotConfiguredException(tenantId);
        }

        // Step 5: Check if PaymentIntent already exists for this order while holding row lock
        var existingIntent = await _dbContext.PaymentIntents
            .FirstOrDefaultAsync(
                pi => pi.OrderId == orderId && pi.TenantId == tenantId && pi.Provider == PaymentProvider.Stripe,
                cancellationToken);

        if (existingIntent != null && existingIntent.Status == PaymentIntentStatus.Pending)
        {
            if (string.IsNullOrWhiteSpace(existingIntent.ProviderIntentId) ||
                string.IsNullOrWhiteSpace(existingIntent.ClientSecretEncrypted))
            {
                // RECOVERY PATH: If ProviderIntentId exists but ClientSecretEncrypted is missing, attempt to recover from Stripe
                if (!string.IsNullOrWhiteSpace(existingIntent.ProviderIntentId) && 
                    string.IsNullOrWhiteSpace(existingIntent.ClientSecretEncrypted))
                {
                    _logger.LogWarning(
                        "Payment intent has ProviderIntentId but missing ClientSecretEncrypted, attempting recovery from Stripe | OrderId: {OrderId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                        orderId, existingIntent.ProviderIntentId, correlationId);

                    try
                    {
                        var recoveredStripeIntent = await RehydrateStripeProviderIntentAsync(
                            tenantId,
                            order,
                            existingIntent,
                            storefrontBaseUrl: _storefrontUrlResolver.Resolve(tenant),
                            customerId,
                            correlationId,
                            cancellationToken);

                        if (!string.Equals(recoveredStripeIntent.StripePaymentIntentId, existingIntent.ProviderIntentId, StringComparison.Ordinal))
                        {
                            _logger.LogError(
                                "Stripe recovery returned a different provider intent than the persisted authority | OrderId: {OrderId} | ExistingStripeIntentId: {ExistingStripeIntentId} | RecoveredStripeIntentId: {RecoveredStripeIntentId} | CorrelationId: {CorrelationId}",
                                orderId,
                                existingIntent.ProviderIntentId,
                                recoveredStripeIntent.StripePaymentIntentId,
                                correlationId);

                            _reliabilityTelemetry?.RecordPaymentIntentDuplicateAttempt("stripe", "recovery_mismatch");
                            throw new PaymentException(
                                "PAYMENT_INTENT_RECOVERY_FAILED",
                                "Payment intent recovery returned inconsistent provider authority.");
                        }

                        SetClientSecret(existingIntent, recoveredStripeIntent.ClientSecret);
                        await _dbContext.SaveChangesAsync(cancellationToken);

                        _logger.LogInformation(
                            "Successfully recovered client secret through canonical Stripe runtime application path | OrderId: {OrderId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                            orderId, existingIntent.ProviderIntentId, correlationId);
                    }
                    catch (StripeApiException ex)
                    {
                        _logger.LogError(ex,
                            "Stripe runtime application error during client secret recovery | OrderId: {OrderId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                            orderId, existingIntent.ProviderIntentId, correlationId);

                        _reliabilityTelemetry?.RecordPaymentIntentDuplicateAttempt("stripe", "recovery_stripe_error");
                        throw new PaymentException(
                            "PAYMENT_INTENT_RECOVERY_FAILED",
                            "Payment intent recovery failed due to Stripe API error. Please retry.");
                    }
                    catch (Exception ex) when (ex is not PaymentException)
                    {
                        _logger.LogError(ex,
                            "Unexpected error during client secret recovery | OrderId: {OrderId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                            orderId, existingIntent.ProviderIntentId, correlationId);

                        _reliabilityTelemetry?.RecordPaymentIntentDuplicateAttempt("stripe", "recovery_error");
                        throw new PaymentException(
                            "PAYMENT_INTENT_RECOVERY_FAILED",
                            "Payment intent recovery failed. Please retry.");
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Payment intent has no ProviderIntentId after local claim; rehydrating Stripe intent with the original idempotency key | PaymentIntentId: {PaymentIntentId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                        existingIntent.Id, orderId, correlationId);

                    var storefrontBaseUrlForRecovery = _storefrontUrlResolver.Resolve(tenant);
                    existingIntent.ProviderMetadata = BuildRoutingMetadata(tenantId, orderId, storefrontBaseUrlForRecovery);
                    if (string.IsNullOrWhiteSpace(order.PaymentReference))
                    {
                        order.PaymentReference = existingIntent.PaymentReference;
                    }
                    order.PaymentGateway = "stripe";

                    StripeRuntimeApplicationResult recoveredStripeIntent;
                    try
                    {
                        recoveredStripeIntent = await RehydrateStripeProviderIntentAsync(
                            tenantId,
                            order,
                            existingIntent,
                            storefrontBaseUrlForRecovery,
                            customerId,
                            correlationId,
                            cancellationToken);
                    }
                    catch (StripeApiException ex)
                    {
                        _logger.LogError(ex,
                            "Stripe runtime application error during missing-provider-intent recovery | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                            orderId, correlationId);

                        _reliabilityTelemetry?.RecordPaymentIntentDuplicateAttempt("stripe", "missing_provider_recovery_failed");
                        throw;
                    }

                    existingIntent.ProviderIntentId = recoveredStripeIntent.StripePaymentIntentId;
                    SetClientSecret(existingIntent, recoveredStripeIntent.ClientSecret);

                    await _eventPublisher.PublishAsync(new PaymentIntentCreatedEvent
                    {
                        TenantId = tenantId,
                        OrderId = orderId,
                        PaymentIntentId = existingIntent.Id,
                        OrderNumber = order.OrderNumber,
                        PaymentReference = existingIntent.PaymentReference,
                        Provider = "stripe",
                        PaymentMethod = "STRIPE",
                        Amount = order.Total,
                        Currency = order.Currency,
                        IsOfflineCollection = false,
                        PromotionCalculationFailed = promotionCalculationFailed,
                        CorrelationId = correlationId,
                        OccurredAtUtc = DateTime.UtcNow
                    }, cancellationToken);

                    const int recoveryMaxRetries = 3;
                    for (int attempt = 1; attempt <= recoveryMaxRetries; attempt++)
                    {
                        try
                        {
                            await _dbContext.SaveChangesAsync(cancellationToken);
                            break;
                        }
                        catch (DbUpdateException ex) when (attempt < recoveryMaxRetries)
                        {
                            _logger.LogWarning(ex,
                                "Failed to persist recovered Stripe provider fields (attempt {Attempt}/{MaxRetries}) | OrderId: {OrderId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                                attempt, recoveryMaxRetries, orderId, recoveredStripeIntent.StripePaymentIntentId, correlationId);

                            await Task.Delay(100 * (int)Math.Pow(2, attempt - 1), cancellationToken);
                            await _dbContext.Entry(existingIntent).ReloadAsync(cancellationToken);
                            existingIntent.ProviderIntentId = recoveredStripeIntent.StripePaymentIntentId;
                            SetClientSecret(existingIntent, recoveredStripeIntent.ClientSecret);
                            existingIntent.ProviderMetadata = BuildRoutingMetadata(tenantId, orderId, storefrontBaseUrlForRecovery);
                        }
                    }

                    _logger.LogInformation(
                        "Recovered Stripe PaymentIntent from local pending claim | OrderId: {OrderId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                        orderId, recoveredStripeIntent.StripePaymentIntentId, correlationId);

                    await transaction.CommitAsync(cancellationToken);

                    return new StripePaymentIntentResponseDto
                    {
                        ClientSecret = recoveredStripeIntent.ClientSecret,
                        PublishableKey = stripeConfig.PublishableKey,
                        PaymentIntentId = existingIntent.Id,
                        OrderId = orderId,
                        OrderNumber = order.OrderNumber,
                        Amount = order.Total,
                        Currency = order.Currency,
                        StripePaymentIntentId = recoveredStripeIntent.StripePaymentIntentId
                    };
                }
            }

            // Best-effort metadata repair - only resolve URL if repair is needed
            var routingMetadata = PaymentIntentRoutingMetadataSerializer.Deserialize(existingIntent.ProviderMetadata);
            if (!PaymentIntentRoutingMetadataSerializer.MatchesStoredIntent(routingMetadata, tenantId, orderId))
            {
                try
                {
                    var repairStorefrontUrl = _storefrontUrlResolver.Resolve(tenant);
                    existingIntent.ProviderMetadata = BuildRoutingMetadata(tenantId, orderId, repairStorefrontUrl);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw; // Propagate cancellation
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Best-effort metadata repair failed on existing intent - continuing with existing intent | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                        orderId, correlationId);
                }
            }

            // Return existing intent if still pending
            _logger.LogInformation(
                "Returning existing Stripe PaymentIntent | OrderId: {OrderId} | IntentId: {IntentId} | CorrelationId: {CorrelationId}",
                orderId, existingIntent.ProviderIntentId, correlationId);

            // Retrieve and validate the client secret
            var clientSecret = GetClientSecret(existingIntent);
            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(
                    "Failed to retrieve client secret for existing PaymentIntent | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | ProviderIntentId: {ProviderIntentId} | CorrelationId: {CorrelationId}",
                    orderId, existingIntent.Id, existingIntent.ProviderIntentId, correlationId);

                throw new PaymentException(
                    "PAYMENT_INTENT_SECRET_UNAVAILABLE",
                    $"Unable to retrieve payment secret for order {orderId}. The payment intent exists but the client secret is missing or corrupted. Please contact support.");
            }

            await transaction.CommitAsync(cancellationToken);

            return new StripePaymentIntentResponseDto
            {
                ClientSecret = clientSecret,
                PublishableKey = stripeConfig.PublishableKey,
                PaymentIntentId = existingIntent.Id,
                OrderId = orderId,
                OrderNumber = order.OrderNumber,
                Amount = order.Total,
                Currency = order.Currency,
                StripePaymentIntentId = existingIntent.ProviderIntentId ?? string.Empty
            };
        }

        // Only resolve storefront URL when actually creating a new PaymentIntent
        var storefrontBaseUrl = _storefrontUrlResolver.Resolve(tenant);

        var paymentReference = GeneratePaymentReference(orderId);
        var paymentIntent = new CorePaymentIntent
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            TenantId = tenantId,
            Amount = order.Total,
            Currency = order.Currency,
            PaymentMethod = CorePaymentMethod.Card,
            Provider = PaymentProvider.Stripe,
            PaymentReference = paymentReference,
            IdempotencyKey = idempotencyKey,
            Status = PaymentIntentStatus.Pending,
            ProviderMetadata = BuildRoutingMetadata(tenantId, orderId, storefrontBaseUrl),
            CreatedAt = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(StorefrontPaymentSessionTtl)
        };

        _dbContext.PaymentIntents.Add(paymentIntent);
        order.PaymentReference = paymentReference;
        order.PaymentGateway = "stripe";

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _reliabilityTelemetry?.RecordPaymentIntentDuplicateAttempt("stripe", "concurrent_claim");
            _dbContext.ChangeTracker.Clear();

            // PRODUCTION HARDENING: Rollback and dispose faulted transaction before recovery operations.
            // PostgreSQL won't accept new operations on a faulted transaction.
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();

            // Start a new transaction for recovery operations
            await using var recoveryTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var claimedIntent = await _dbContext.PaymentIntents
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    pi => pi.OrderId == orderId && pi.TenantId == tenantId && pi.Provider == PaymentProvider.Stripe,
                    cancellationToken);

            if (claimedIntent != null &&
                claimedIntent.Status == PaymentIntentStatus.Pending &&
                !string.IsNullOrWhiteSpace(claimedIntent.ProviderIntentId) &&
                GetClientSecret(claimedIntent) != null)
            {
                await recoveryTransaction.CommitAsync(cancellationToken);
                return new StripePaymentIntentResponseDto
                {
                    ClientSecret = GetClientSecret(claimedIntent)!,
                    PublishableKey = stripeConfig.PublishableKey,
                    PaymentIntentId = claimedIntent.Id,
                    OrderId = orderId,
                    OrderNumber = order.OrderNumber,
                    Amount = order.Total,
                    Currency = order.Currency,
                    StripePaymentIntentId = claimedIntent.ProviderIntentId
                };
            }

            // RECOVERY PATH: If ProviderIntentId exists but ClientSecret is missing, attempt to recover from Stripe
            if (claimedIntent != null &&
                claimedIntent.Status == PaymentIntentStatus.Pending &&
                !string.IsNullOrWhiteSpace(claimedIntent.ProviderIntentId) &&
                GetClientSecret(claimedIntent) == null)
            {
                _logger.LogWarning(
                    "Concurrent claim: Payment intent has ProviderIntentId but missing client secret, attempting recovery from Stripe | OrderId: {OrderId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                    orderId, claimedIntent.ProviderIntentId, correlationId);

                try
                {
                    var recoveredStripeIntent = await RehydrateStripeProviderIntentAsync(
                        tenantId,
                        order,
                        claimedIntent,
                        _storefrontUrlResolver.Resolve(tenant),
                        customerId,
                        correlationId,
                        cancellationToken);

                    if (!string.Equals(recoveredStripeIntent.StripePaymentIntentId, claimedIntent.ProviderIntentId, StringComparison.Ordinal))
                    {
                        _logger.LogError(
                            "Concurrent claim recovery returned a different provider intent than the persisted authority | OrderId: {OrderId} | ExistingStripeIntentId: {ExistingStripeIntentId} | RecoveredStripeIntentId: {RecoveredStripeIntentId} | CorrelationId: {CorrelationId}",
                            orderId,
                            claimedIntent.ProviderIntentId,
                            recoveredStripeIntent.StripePaymentIntentId,
                            correlationId);

                        _reliabilityTelemetry?.RecordPaymentIntentDuplicateAttempt("stripe", "concurrent_recovery_mismatch");
                        await recoveryTransaction.RollbackAsync(cancellationToken);
                        throw new PaymentException(
                            "PAYMENT_INTENT_RECOVERY_FAILED",
                            "Concurrent payment intent recovery returned inconsistent provider authority.");
                    }

                    _dbContext.PaymentIntents.Attach(claimedIntent);
                    SetClientSecret(claimedIntent, recoveredStripeIntent.ClientSecret);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await recoveryTransaction.CommitAsync(cancellationToken);

                    _logger.LogInformation(
                        "Concurrent claim: Successfully recovered client secret through canonical Stripe runtime application path | OrderId: {OrderId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                        orderId, claimedIntent.ProviderIntentId, correlationId);

                    return new StripePaymentIntentResponseDto
                    {
                        ClientSecret = recoveredStripeIntent.ClientSecret,
                        PublishableKey = stripeConfig.PublishableKey,
                        PaymentIntentId = claimedIntent.Id,
                        OrderId = orderId,
                        OrderNumber = order.OrderNumber,
                        Amount = order.Total,
                        Currency = order.Currency,
                        StripePaymentIntentId = claimedIntent.ProviderIntentId
                    };
                }
                catch (StripeApiException stripeEx)
                {
                    _logger.LogError(stripeEx,
                        "Concurrent claim: Stripe runtime application error during client secret recovery | OrderId: {OrderId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                        orderId, claimedIntent.ProviderIntentId, correlationId);
                }
                catch (Exception unexpectedEx)
                {
                    _logger.LogError(unexpectedEx,
                        "Concurrent claim: Unexpected error during client secret recovery | OrderId: {OrderId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                        orderId, claimedIntent.ProviderIntentId, correlationId);
                }
            }

            // Recovery failed, rollback recovery transaction before throwing
            await recoveryTransaction.RollbackAsync(cancellationToken);

            throw new PaymentException(
                "PAYMENT_INTENT_IN_PROGRESS",
                "Payment intent creation is already in progress. Please retry shortly.");
        }

        await transaction.CommitAsync(cancellationToken);

        StripeRuntimeApplicationResult stripePaymentIntent;
        var providerStopwatch = Stopwatch.StartNew();
        try
        {
            stripePaymentIntent = await CreateStripeProviderIntentAsync(
                tenantId,
                orderId,
                order.OrderNumber,
                order.Total,
                order.Currency,
                storefrontBaseUrl,
                customerId,
                paymentReference,
                idempotencyKey,
                paymentIntent.Id,
                stripeConfig,
                correlationId,
                cancellationToken);
            providerStopwatch.Stop();
            _stripeTelemetry?.RecordProviderLatency(tenantId, "create_payment_intent", providerStopwatch.Elapsed, "success");
            _stripeTelemetry?.RecordProviderAvailability(tenantId, "create_payment_intent", true, "OK");
        }
        catch (StripeException ex)
        {
            providerStopwatch.Stop();
            _stripeTelemetry?.RecordProviderLatency(tenantId, "create_payment_intent", providerStopwatch.Elapsed, "stripe_exception");
            _stripeTelemetry?.RecordProviderAvailability(tenantId, "create_payment_intent", false, ex.StripeError?.Code ?? "STRIPE_EXCEPTION");
            _stripeTelemetry?.RecordPaymentDuration(tenantId, "create_payment_intent", stopwatch.Elapsed, "failed");

            await MarkPersistedStripeIntentFailedAsync(
                paymentIntent.Id,
                ex.StripeError?.Code,
                ex.Message,
                cancellationToken);

            _logger.LogError(ex,
                "Stripe API error | OrderId: {OrderId} | ErrorCode: {ErrorCode} | CorrelationId: {CorrelationId}",
                orderId, ex.StripeError?.Code, correlationId);

            throw new StripeApiException(
                ex.Message,
                ex.StripeError?.Code,
                ex.StripeError?.Type);
        }
        catch (StripeApiException ex)
        {
            providerStopwatch.Stop();
            _stripeTelemetry?.RecordProviderLatency(tenantId, "create_payment_intent", providerStopwatch.Elapsed, "provider_error");
            _stripeTelemetry?.RecordProviderAvailability(tenantId, "create_payment_intent", false, ex.StripeErrorCode ?? "PROVIDER_ERROR");
            _stripeTelemetry?.RecordPaymentDuration(tenantId, "create_payment_intent", stopwatch.Elapsed, "failed");

            await MarkPersistedStripeIntentFailedAsync(
                paymentIntent.Id,
                ex.StripeErrorCode,
                ex.Message,
                cancellationToken);

            _logger.LogError(ex,
                "Stripe runtime application error | OrderId: {OrderId} | ErrorCode: {ErrorCode} | CorrelationId: {CorrelationId}",
                orderId, ex.StripeErrorCode, correlationId);

            throw;
        }

        var response = await PersistStripeProviderIntentAsync(
            tenantId,
            orderId,
            paymentIntent.Id,
            paymentReference,
            promotionCalculationFailed,
            correlationId,
            stripeConfig,
            stripePaymentIntent,
            cancellationToken);

        stopwatch.Stop();

        _logger.LogInformation(
            "CreateStripePaymentIntent completed | OrderId: {OrderId} | StripeId: {StripeId} | DurationMs: {DurationMs} | CorrelationId: {CorrelationId}",
            orderId, stripePaymentIntent.StripePaymentIntentId, stopwatch.ElapsedMilliseconds, correlationId);
        _stripeTelemetry?.RecordPaymentDuration(tenantId, "create_payment_intent", stopwatch.Elapsed, "success");

        return response;
    }

    private async Task<StripeRuntimeApplicationResult> CreateStripeProviderIntentAsync(
        Guid tenantId,
        Guid orderId,
        string orderNumber,
        decimal amount,
        string currency,
        string storefrontBaseUrl,
        Guid? customerId,
        string paymentReference,
        string idempotencyKey,
        Guid localPaymentIntentId,
        TenantStripeConfiguration stripeConfig,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var stripePaymentIntent = await _stripeRuntimeApplicationService.CreatePaymentIntentAsync(
            new StripeRuntimePaymentIntentApplicationRequest(
                tenantId,
                orderId,
                localPaymentIntentId,
                orderNumber,
                amount,
                currency,
                storefrontBaseUrl,
                customerId,
                paymentReference,
                idempotencyKey,
                correlationId,
                DateTime.UtcNow,
                DateTimeOffset.UtcNow),
            cancellationToken);

        _logger.LogInformation(
            "Stripe PaymentIntent created after durable POS authority commit | StripeId: {StripeId} | Amount: {Amount} {Currency} | CorrelationId: {CorrelationId}",
            stripePaymentIntent.StripePaymentIntentId,
            amount,
            currency,
            correlationId);

        return stripePaymentIntent;
    }

    private async Task<StripeRuntimeApplicationResult> RehydrateStripeProviderIntentAsync(
        Guid tenantId,
        Order order,
        CorePaymentIntent paymentIntent,
        string storefrontBaseUrl,
        Guid? customerId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = NormalizeCheckoutPaymentIdempotencyKey(
            paymentIntent.IdempotencyKey,
            $"stripe:intent:{tenantId:N}:{order.Id:N}");

        return await CreateStripeProviderIntentAsync(
            tenantId,
            order.Id,
            order.OrderNumber,
            order.Total,
            order.Currency,
            storefrontBaseUrl,
            customerId,
            paymentIntent.PaymentReference,
            idempotencyKey,
            paymentIntent.Id,
            stripeConfig: new TenantStripeConfiguration(),
            correlationId,
            cancellationToken);
    }

    private async Task<StripePaymentIntentResponseDto> PersistStripeProviderIntentAsync(
        Guid tenantId,
        Guid orderId,
        Guid paymentIntentId,
        string paymentReference,
        bool promotionCalculationFailed,
        string correlationId,
        TenantStripeConfiguration stripeConfig,
        StripeRuntimeApplicationResult stripePaymentIntent,
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
                    throw new PaymentException(
                        "PAYMENT_INTENT_NOT_FOUND",
                        $"Persisted Stripe payment intent {paymentIntentId} was not found for order {orderId}.");
                }

                if (string.IsNullOrWhiteSpace(paymentIntent.ProviderIntentId) ||
                    string.IsNullOrWhiteSpace(GetClientSecret(paymentIntent)))
                {
                    paymentIntent.ProviderIntentId = stripePaymentIntent.StripePaymentIntentId;
                    SetClientSecret(paymentIntent, stripePaymentIntent.ClientSecret);

                    await _eventPublisher.PublishAsync(new PaymentIntentCreatedEvent
                    {
                        TenantId = tenantId,
                        OrderId = orderId,
                        PaymentIntentId = paymentIntent.Id,
                        OrderNumber = order.OrderNumber,
                        PaymentReference = paymentReference,
                        Provider = "stripe",
                        PaymentMethod = "STRIPE",
                        Amount = order.Total,
                        Currency = order.Currency,
                        IsOfflineCollection = false,
                        PromotionCalculationFailed = promotionCalculationFailed,
                        CorrelationId = correlationId,
                        OccurredAtUtc = DateTime.UtcNow
                    }, cancellationToken);

                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                return new StripePaymentIntentResponseDto
                {
                    ClientSecret = stripePaymentIntent.ClientSecret,
                    PublishableKey = stripeConfig.PublishableKey,
                    PaymentIntentId = paymentIntent.Id,
                    OrderId = orderId,
                    OrderNumber = order.OrderNumber,
                    Amount = order.Total,
                    Currency = order.Currency,
                    StripePaymentIntentId = paymentIntent.ProviderIntentId ?? stripePaymentIntent.StripePaymentIntentId
                };
            }
            catch (DbUpdateException ex) when (attempt < maxRetries)
            {
                await transaction.RollbackAsync(cancellationToken);

                _logger.LogWarning(
                    ex,
                    "Failed to persist Stripe provider fields after provider intent creation (attempt {Attempt}/{MaxRetries}) | OrderId: {OrderId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                    attempt,
                    maxRetries,
                    orderId,
                    stripePaymentIntent.StripePaymentIntentId,
                    correlationId);

                await Task.Delay(100 * (int)Math.Pow(2, attempt - 1), cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        throw new PaymentException(
            "PAYMENT_INTENT_PERSISTENCE_FAILED",
            $"Stripe provider state for order {orderId} could not be durably persisted after retries.");
    }

    private async Task MarkPersistedStripeIntentFailedAsync(
        Guid paymentIntentId,
        string? failureCode,
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
                paymentIntent.Status = PaymentIntentStatus.Failed;
                paymentIntent.FailedAt = DateTime.UtcNow;
                paymentIntent.FailureCode = failureCode;
                paymentIntent.FailureReason = failureReason;
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

    #region Get Payment Intent Status

    /// <inheritdoc />
    public async Task<StripePaymentStatusDto> GetPaymentIntentStatusAsync(
        Guid tenantId,
        Guid orderId,
        string checkoutToken,
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

        // Validate checkout session ownership
        await ValidateCheckoutSessionOwnershipAsync(checkoutToken, tenantId, orderId, order.SessionId, correlationId, cancellationToken);

        // Load latest payment intent
        var paymentIntent = await _dbContext.PaymentIntents
            .Where(pi => pi.OrderId == orderId && pi.TenantId == tenantId && pi.Provider == PaymentProvider.Stripe)
            .OrderByDescending(pi => pi.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (paymentIntent == null)
        {
            return new StripePaymentStatusDto
            {
                OrderId = orderId,
                OrderNumber = order.OrderNumber,
                PaymentStatus = "NotStarted",
                StripeStatus = "none",
                IsPaid = false,
                RequiresAction = false
            };
        }

        // Optionally refresh from Stripe (for polling scenarios)
        var stripeStatus = paymentIntent.Status switch
        {
            PaymentIntentStatus.Pending => "requires_payment_method",
            PaymentIntentStatus.Authorized => "requires_capture",
            PaymentIntentStatus.Paid => "succeeded",
            PaymentIntentStatus.Failed => "failed",
            _ => "unknown"
        };

        return new StripePaymentStatusDto
        {
            OrderId = orderId,
            OrderNumber = order.OrderNumber,
            PaymentStatus = order.PaymentStatus.ToString(),
            StripeStatus = stripeStatus,
            IsPaid = CanonicalPaymentStatusResolver.IsPaidState(order.PaymentStatus),
            RequiresAction = paymentIntent.Status == PaymentIntentStatus.Pending,
            FailureReason = paymentIntent.FailureReason
        };
    }

    #endregion

    #region Process Webhook

    /// <inheritdoc />
    public async Task<StripeWebhookResultDto> ProcessWebhookAsync(
        Guid tenantId,
        string signature,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "ProcessStripeWebhook starting | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            tenantId, correlationId);

        // Step 1: Get Stripe configuration for webhook secret
        var stripeConfig = await GetStripeConfigurationAsync(
            tenantId,
            StripeOperationalCapability.Webhook,
            cancellationToken);
        if (stripeConfig == null || string.IsNullOrEmpty(stripeConfig.WebhookSecret))
        {
            _logger.LogWarning(
                "Stripe webhook secret not configured | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId, correlationId);

            throw new StripeNotConfiguredException(tenantId);
        }

        // Step 2: Verify webhook signature
        StripeEvent stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                rawPayload,
                signature,
                stripeConfig.WebhookSecret
            );
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(
                "Stripe webhook signature invalid | TenantId: {TenantId} | Error: {Error} | CorrelationId: {CorrelationId}",
                tenantId, ex.Message, correlationId);
            _stripeTelemetry?.RecordWebhookFailure(tenantId, "signature_verification", "INVALID_SIGNATURE");
            _stripeTelemetry?.RecordWebhookProcessing(tenantId, "signature_verification", stopwatch.Elapsed, "failed");

            throw new StripeWebhookSignatureException();
        }

        _logger.LogInformation(
            "Stripe webhook verified | EventId: {EventId} | EventType: {EventType} | CorrelationId: {CorrelationId}",
            stripeEvent.Id, stripeEvent.Type, correlationId);
        _stripeTelemetry?.RecordWebhookReceived(tenantId, stripeEvent.Type);

        var runtimeAdmission = await _stripeWebhookRuntimeIntegrationService.EvaluateAndExecuteAsync(
            new StripeWebhookRuntimeAdmissionRequest(
                tenantId,
                stripeEvent,
                rawPayload,
                signature,
                stripeConfig.WebhookSecret,
                correlationId),
            cancellationToken);

        if (runtimeAdmission.RejectedAsReplay)
        {
            _logger.LogInformation(
                "Stripe webhook rejected by application replay store | EventId: {EventId} | EventType: {EventType} | Reason: {Reason} | CorrelationId: {CorrelationId}",
                stripeEvent.Id,
                stripeEvent.Type,
                runtimeAdmission.ReplayContext.ReasonCode,
                correlationId);
            _stripeTelemetry?.RecordReplayPrevented(tenantId, runtimeAdmission.ReplayContext.ReasonCode);
            _stripeTelemetry?.RecordWebhookReplay(tenantId, stripeEvent.Type, runtimeAdmission.ReplayContext.ReasonCode);
            _stripeTelemetry?.RecordWebhookProcessing(tenantId, stripeEvent.Type, stopwatch.Elapsed, "replay_rejected");

            return new StripeWebhookResultDto
            {
                Processed = true,
                Message = "Duplicate event ignored",
                EventType = stripeEvent.Type
            };
        }

        if (!runtimeAdmission.AdmittedToRuntime)
        {
            _logger.LogInformation(
                "Stripe webhook not admitted to Runtime | EventId: {EventId} | EventType: {EventType} | Classification: {Classification} | Reason: {Reason} | CorrelationId: {CorrelationId}",
                stripeEvent.Id,
                stripeEvent.Type,
                runtimeAdmission.Admission.Classification,
                runtimeAdmission.Admission.ReasonCode,
                correlationId);
        }

        if (runtimeAdmission.Admission.AdmittedToRuntime && !runtimeAdmission.AdmittedToRuntime)
        {
            var runtimeResult = runtimeAdmission.AdapterResult?.RuntimeResult;
            _logger.LogError(
                "Stripe webhook Runtime execution rejected | EventId: {EventId} | EventType: {EventType} | RuntimeStatus: {RuntimeStatus} | FailureStage: {FailureStage} | FailureReason: {FailureReason} | CorrelationId: {CorrelationId}",
                stripeEvent.Id,
                stripeEvent.Type,
                runtimeResult?.OverallStatus,
                runtimeResult?.Failure?.FailureStage,
                runtimeResult?.Failure?.FailureReason.Value,
                correlationId);

            throw new PaymentException(
                "STRIPE_RUNTIME_OUTCOME_REJECTED",
                "Stripe webhook did not produce the required canonical Runtime outcome.");
        }

        var webhookIdempotencyKey = $"stripe:webhook:{stripeEvent.Id}";
        PaymentWebhookLog? webhookLog = null;
        var wasPreviouslyFailed = false;

        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var preparation = await PrepareWebhookLogAsync(
                    tenantId,
                    stripeEvent.Id,
                    stripeEvent.Type,
                    rawPayload,
                    signature,
                    cancellationToken);

                webhookLog = preparation.Log;
                wasPreviouslyFailed = preparation.WasPreviouslyFailed;

                if (preparation.IsDuplicate)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    await StoreWebhookIdempotencyAsync(webhookIdempotencyKey, webhookLog.Id, correlationId);

                    _logger.LogInformation(
                        "Duplicate Stripe webhook | EventId: {EventId} | CorrelationId: {CorrelationId}",
                        stripeEvent.Id, correlationId);
                    _stripeTelemetry?.RecordWebhookDuplicate(tenantId, stripeEvent.Type);
                    _stripeTelemetry?.RecordWebhookReplay(tenantId, stripeEvent.Type, "DUPLICATE_WEBHOOK_LOG");
                    _stripeTelemetry?.RecordWebhookProcessing(tenantId, stripeEvent.Type, stopwatch.Elapsed, "duplicate");

                    return new StripeWebhookResultDto
                    {
                        Processed = true,
                        Message = "Duplicate event ignored",
                        EventLogId = webhookLog.Id,
                        EventType = stripeEvent.Type
                    };
                }

                if (preparation.IsInFlight)
                {
                    await transaction.RollbackAsync(cancellationToken);

                    _logger.LogWarning(
                        "Stripe webhook already in flight | EventId: {EventId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                        stripeEvent.Id, tenantId, correlationId);
                    _stripeTelemetry?.RecordProviderRetry(tenantId, "webhook", "WEBHOOK_IN_FLIGHT");
                    _stripeTelemetry?.RecordWebhookProcessing(tenantId, stripeEvent.Type, stopwatch.Elapsed, "retry");

                    return new StripeWebhookResultDto
                    {
                        Processed = false,
                        Message = "Webhook event is already being processed. Please retry.",
                        EventLogId = webhookLog.Id,
                        EventType = stripeEvent.Type
                    };
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();

                var existingLog = await TryGetWebhookLogAsync(tenantId, stripeEvent.Id, cancellationToken);
                if (existingLog != null &&
                    (existingLog.ProcessingStatus == WebhookProcessingStatus.Completed ||
                     existingLog.ProcessingStatus == WebhookProcessingStatus.Skipped))
                {
                    await StoreWebhookIdempotencyAsync(webhookIdempotencyKey, existingLog.Id, correlationId);

                    _logger.LogInformation(
                        "Stripe webhook idempotency enforced by database | EventId: {EventId} | CorrelationId: {CorrelationId}",
                        stripeEvent.Id, correlationId);
                    _stripeTelemetry?.RecordWebhookReplay(tenantId, stripeEvent.Type, "DATABASE_UNIQUE_CONSTRAINT");
                    _stripeTelemetry?.RecordWebhookProcessing(tenantId, stripeEvent.Type, stopwatch.Elapsed, "duplicate");

                    return new StripeWebhookResultDto
                    {
                        Processed = true,
                        Message = "Duplicate event ignored",
                        EventLogId = existingLog.Id,
                        EventType = stripeEvent.Type
                    };
                }

                if (existingLog != null && existingLog.ProcessingStatus == WebhookProcessingStatus.Failed)
                {
                    _logger.LogCritical(
                        "Stripe webhook retry encountered previously failed event | EventId: {EventId} | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                        stripeEvent.Id,
                        existingLog.OrderId,
                        tenantId,
                        correlationId);
                }

                _stripeTelemetry?.RecordProviderRetry(tenantId, "webhook", "WEBHOOK_DUPLICATE_OR_RETRY");
                _stripeTelemetry?.RecordWebhookProcessing(tenantId, stripeEvent.Type, stopwatch.Elapsed, "retry");

                return new StripeWebhookResultDto
                {
                    Processed = false,
                    Message = "Webhook event is already being processed or awaiting retry.",
                    EventLogId = existingLog?.Id,
                    EventType = stripeEvent.Type
                };
            }

            var result = await ProcessStripeEventAsync(
                stripeEvent, webhookLog, tenantId, correlationId, cancellationToken);

            await _stripeWebhookRuntimeIntegrationService.PersistReplayRecordAsync(
                runtimeAdmission,
                webhookLog.PaymentIntentId,
                webhookLog.OrderId,
                cancellationToken);

            webhookLog.ProcessingStatus = WebhookProcessingStatus.Completed;
            webhookLog.ProcessedAt = DateTime.UtcNow;
            webhookLog.ProcessingDurationMs = stopwatch.ElapsedMilliseconds;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await _dbContext.Database.CommitTransactionAsync(cancellationToken);

            // POST-COMMIT: Trigger background RedirectUrl recovery if needed
            // This runs outside the transaction to avoid holding DB locks during Stripe API calls
            if (result.OrderId != null && Guid.TryParse(result.OrderId, out var orderId))
            {
                var workItem = new RedirectUrlRecoveryWorkItem
                {
                    TenantId = tenantId,
                    OrderId = orderId,
                    CorrelationId = correlationId,
                    EnqueuedAt = DateTime.UtcNow
                };

                try
                {
                    // NOTE: For RedirectUrlRecoveryWorkItem, QueueAsync may BLOCK if queue is full
                    // (uses Wait mode to ensure critical payment recovery tasks are never dropped).
                    // Returns false only on cancellation, not when queue is full.
                    var enqueued = await _recoveryQueue.QueueAsync(workItem, cancellationToken);
                    
                    if (enqueued)
                    {
                        _logger.LogInformation(
                            "Enqueued RedirectUrl recovery work item | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                            orderId, correlationId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to enqueue RedirectUrl recovery work item - operation cancelled | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                            orderId, correlationId);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning(
                        "RedirectUrl recovery enqueue cancelled | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                        orderId, tenantId, correlationId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to enqueue RedirectUrl recovery work item (non-fatal, webhook already committed) | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                        orderId, tenantId, correlationId);
                }
            }

            var redisStored = await StoreWebhookIdempotencyAsync(webhookIdempotencyKey, webhookLog.Id, correlationId);
            if (!redisStored)
            {
                _logger.LogWarning(
                    "Stripe webhook Redis idempotency store failed, relying on database log | EventId: {EventId} | WebhookLogId: {WebhookLogId} | CorrelationId: {CorrelationId}",
                    stripeEvent.Id,
                    webhookLog.Id,
                    correlationId);
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "ProcessStripeWebhook completed | EventId: {EventId} | EventType: {EventType} | DurationMs: {DurationMs} | CorrelationId: {CorrelationId}",
                stripeEvent.Id, stripeEvent.Type, stopwatch.ElapsedMilliseconds, correlationId);
            _stripeTelemetry?.RecordWebhookProcessing(tenantId, stripeEvent.Type, stopwatch.Elapsed, "success");

            return result;
        }
        catch (Exception ex)
        {
            if (_dbContext.Database.CurrentTransaction != null)
            {
                await _dbContext.Database.RollbackTransactionAsync(CancellationToken.None);
            }

            if (webhookLog != null)
            {
                await PersistWebhookFailureAsync(
                    webhookLog,
                    tenantId,
                    stripeEvent.Id,
                    stripeEvent.Type,
                    rawPayload,
                    signature,
                    ex.Message,
                    stopwatch.ElapsedMilliseconds,
                    wasPreviouslyFailed,
                    correlationId,
                    CancellationToken.None);
            }

            _stripeTelemetry?.RecordWebhookFailure(tenantId, stripeEvent.Type, ex.GetType().Name);
            _stripeTelemetry?.RecordWebhookProcessing(tenantId, stripeEvent.Type, stopwatch.Elapsed, "failed");

            throw;
        }
    }

    private async Task<StripeWebhookResultDto> ProcessStripeEventAsync(
        StripeEvent stripeEvent,
        PaymentWebhookLog webhookLog,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        string? orderId = null;

        switch (stripeEvent.Type)
        {
            case "payment_intent.succeeded":
                orderId = await HandlePaymentIntentSucceededAsync(stripeEvent, webhookLog, tenantId, correlationId, cancellationToken);
                break;

            case "payment_intent.payment_failed":
                orderId = await HandlePaymentIntentFailedAsync(stripeEvent, webhookLog, tenantId, correlationId, cancellationToken);
                break;

            case "payment_intent.canceled":
                orderId = await HandlePaymentIntentCanceledAsync(stripeEvent, webhookLog, tenantId, correlationId, cancellationToken);
                break;

            case "charge.refunded":
                orderId = await HandleChargeRefundedAsync(stripeEvent, webhookLog, tenantId, correlationId, cancellationToken);
                break;

            default:
                _logger.LogInformation(
                    "Unhandled Stripe event type | EventType: {EventType} | CorrelationId: {CorrelationId}",
                    stripeEvent.Type, correlationId);
                break;
        }

        return new StripeWebhookResultDto
        {
            Processed = true,
            Message = $"Event {stripeEvent.Type} processed",
            EventLogId = webhookLog.Id,
            OrderId = orderId,
            EventType = stripeEvent.Type
        };
    }

    private async Task<string?> HandlePaymentIntentSucceededAsync(
        StripeEvent stripeEvent,
        PaymentWebhookLog webhookLog,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var paymentIntent = stripeEvent.Data.Object as StripePaymentIntentEntity;
        if (paymentIntent == null)
        {
            throw new InvalidOperationException($"Stripe event {stripeEvent.Id} did not contain a payment intent payload.");
        }

        // Extract order ID from metadata
        if (!TryResolveStripeOrderId(paymentIntent.Metadata, out var orderId))
        {
            throw new InvalidOperationException(
                $"Stripe event {stripeEvent.Id} is missing a valid orderId in metadata.");
        }

        // Load our payment intent and order (with fallback recovery for transient SaveChangesAsync failures)
        var intent = await FindPaymentIntentWithFallbackAsync(
            paymentIntent.Id, orderId, tenantId, correlationId, cancellationToken);

        if (intent == null || intent.Order == null)
        {
            throw new PaymentIntentNotFoundException(paymentIntent.Id);
        }

        // NOTE: Removed early-exit guard for already-paid orders.
        // The PaymentLifecycleService now handles idempotency via StateChanged flag,
        // and we need to allow replayed success webhooks to reach HandlePaymentResultAsync
        // so recovery paths (inventory commit, downstream outcome publication) can run.
        
        var now = DateTime.UtcNow;

        // Update payment intent

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 2: MIGRATE TO PAYMENTLIFECYCLESERVICE (LEDGER-FIRST)
        // ═══════════════════════════════════════════════════════════════════════
        // Use PaymentLifecycleService to ensure:
        // 1. PaymentStatus changes via authorized context (interceptor bypass)
        // 2. Outbox message enqueued for journal creation (DEBIT Stripe Clearing, CREDIT Revenue)
        // 3. Domain events published
        // 4. Atomic: PaymentStatus + Ledger or neither
        
        var order = intent.Order;
        var providerCurrency = paymentIntent.Currency?.Trim().ToUpperInvariant();
        var providerAmount = ConvertFromStripeAmount(paymentIntent.AmountReceived, providerCurrency ?? intent.Currency);
        
        _logger.LogInformation(
            "[STRIPE-WEBHOOK] Capturing payment via PaymentLifecycleService | " +
            "OrderId: {OrderId} | OrderNumber: {OrderNumber} | ProviderAmount: {ProviderAmount} | ProviderCurrency: {ProviderCurrency}",
            orderId, order.OrderNumber, providerAmount, providerCurrency);
        
        var lifecycleResult = await _paymentLifecycleService.HandlePaymentResultAsync(
            new HandlePaymentResultRequest
            {
                OrderId = orderId,
                PaymentIntentId = intent.Id,
                Outcome = PaymentLifecycleOutcome.Succeeded,
                Source = PaymentLifecycleSource.GatewayWebhook,
                Gateway = "stripe",
                PaymentReference = paymentIntent.Id,
                ProviderPaymentId = paymentIntent.LatestChargeId,
                ProviderAmount = providerAmount,
                ProviderCurrency = providerCurrency,
                WebhookEventId = stripeEvent.Id,
                ProcessedAtUtc = now
            },
            cancellationToken);
        
        if (!lifecycleResult.Success)
        {
            if (lifecycleResult.RequiresManualReview)
            {
                webhookLog.PaymentIntentId = intent.Id;
                webhookLog.OrderId = orderId;
                StorefrontSuspenseRecoveryPolicy.ApplyManualReviewMarker(
                    lifecycleResult,
                    webhookLog,
                    paymentIntent.Id);

                _logger.LogCritical(
                    "[STRIPE-WEBHOOK] Payment success quarantined for manual review | OrderId: {OrderId} | StripePaymentIntentId: {StripePaymentIntentId} | FailureCode: {FailureCode} | Reason: {Reason}",
                    orderId,
                    paymentIntent.Id,
                    lifecycleResult.FailureCode,
                    lifecycleResult.ErrorMessage);

                return orderId.ToString();
            }

            _stripeTelemetry?.RecordPaymentFailed(tenantId, "webhook", lifecycleResult.FailureCode ?? "LIFECYCLE_FAILURE");
            _logger.LogError(
                "[STRIPE-WEBHOOK] Failed to capture payment via lifecycle service | " +
                "OrderId: {OrderId} | Error: {Error}",
                orderId, lifecycleResult.ErrorMessage);
            throw new InvalidOperationException(
                $"Failed to capture payment: {lifecycleResult.ErrorMessage}");
        }
        
        _logger.LogInformation(
            "[STRIPE-WEBHOOK] Payment captured successfully | " +
            "OrderId: {OrderId} | NewStatus: {NewStatus} | OutboxStatus: {OutboxStatus}",
            orderId, lifecycleResult.NewStatus, lifecycleResult.OutboxStatus);
        _stripeTelemetry?.RecordPaymentSucceeded(tenantId, "webhook");
        _stripeTelemetry?.RecordRuntimeDecision(tenantId, "succeeded");

        webhookLog.PaymentIntentId = intent.Id;
        webhookLog.OrderId = orderId;

        // ═══════════════════════════════════════════════════════════════════════
        // Authoritative outcome events are staged inside PaymentLifecycleService.
        // ═══════════════════════════════════════════════════════════════════════
        // Scenario: First webhook commits payment state, then crashes before outcome publication.
        // Retry webhook returns StateChanged=false but MUST still publish outcome events
        // to trigger inventory commit and downstream processing.
        // The outcome publisher has its own idempotency via IdempotencyKey.
        
        if (!lifecycleResult.StateChanged)
        {
            _logger.LogInformation(
                "Stripe success replay - state unchanged; authoritative outcome events are owned by PaymentLifecycleService | OrderId: {OrderId} | StripeEventId: {StripeEventId}",
                orderId, stripeEvent.Id);
        }

        _logger.LogInformation(
            "Stripe payment succeeded | OrderId: {OrderId} | OrderNumber: {OrderNumber} | ProviderAmount: {ProviderAmount} | CorrelationId: {CorrelationId}",
            orderId, order.OrderNumber, providerAmount, correlationId);

        return orderId.ToString();
    }

    private async Task<string?> HandlePaymentIntentFailedAsync(
        StripeEvent stripeEvent,
        PaymentWebhookLog webhookLog,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var paymentIntent = stripeEvent.Data.Object as StripePaymentIntentEntity;
        if (paymentIntent == null)
        {
            throw new InvalidOperationException($"Stripe event {stripeEvent.Id} did not contain a payment intent payload.");
        }

        if (!TryResolveStripeOrderId(paymentIntent.Metadata, out var orderId))
        {
            throw new InvalidOperationException(
                $"Stripe event {stripeEvent.Id} is missing a valid orderId in metadata.");
        }

        // Load our payment intent and order (with fallback recovery for transient SaveChangesAsync failures)
        var intent = await FindPaymentIntentWithFallbackAsync(
            paymentIntent.Id, orderId, tenantId, correlationId, cancellationToken);

        if (intent == null || intent.Order == null)
        {
            throw new PaymentIntentNotFoundException(paymentIntent.Id);
        }

        webhookLog.PaymentIntentId = intent.Id;
        webhookLog.OrderId = orderId;

        var failureReason = paymentIntent.LastPaymentError?.Message ?? "Payment failed";
        var failureCode = paymentIntent.LastPaymentError?.Code;
        // Stripe emits payment_intent.payment_failed for a failed confirmation attempt
        // while the same PaymentIntent can still be confirmed again. Keep local payment,
        // order, and inventory authority pending until cancellation or another terminal event.
        intent.FailureReason = failureReason;
        intent.FailureCode = failureCode;
        intent.LastWebhookEventId = stripeEvent.Id;
        intent.WebhookEventCount++;
        intent.UpdatedAt = DateTime.UtcNow;
        _stripeTelemetry?.RecordPaymentFailed(tenantId, "webhook", failureCode ?? "PAYMENT_FAILED");
        _stripeTelemetry?.RecordRuntimeDecision(tenantId, "retryable_failure");

        _logger.LogWarning(
            "Stripe payment attempt failed and remains retryable | OrderId: {OrderId} | Reason: {Reason} | CorrelationId: {CorrelationId}",
            orderId, failureReason, correlationId);

        return orderId.ToString();
    }

    private async Task<string?> HandlePaymentIntentCanceledAsync(
        StripeEvent stripeEvent,
        PaymentWebhookLog webhookLog,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var paymentIntent = stripeEvent.Data.Object as StripePaymentIntentEntity;
        if (paymentIntent == null)
        {
            throw new InvalidOperationException($"Stripe event {stripeEvent.Id} did not contain a payment intent payload.");
        }

        if (!TryResolveStripeOrderId(paymentIntent.Metadata, out var orderId))
        {
            throw new InvalidOperationException(
                $"Stripe event {stripeEvent.Id} is missing a valid orderId in metadata.");
        }

        // Load our payment intent and order (with fallback recovery for transient SaveChangesAsync failures)
        var intent = await FindPaymentIntentWithFallbackAsync(
            paymentIntent.Id, orderId, tenantId, correlationId, cancellationToken);

        if (intent == null || intent.Order == null)
        {
            throw new PaymentIntentNotFoundException(paymentIntent.Id);
        }

        var now = DateTime.UtcNow;

        webhookLog.PaymentIntentId = intent.Id;
        webhookLog.OrderId = orderId;

        var lifecycleResult = await _paymentLifecycleService.HandlePaymentResultAsync(
            new HandlePaymentResultRequest
            {
                OrderId = orderId,
                PaymentIntentId = intent.Id,
                Outcome = PaymentLifecycleOutcome.Cancelled,
                Source = PaymentLifecycleSource.GatewayWebhook,
                Gateway = "stripe",
                PaymentReference = paymentIntent.Id,
                ProviderPaymentId = paymentIntent.LatestChargeId,
                Amount = intent.Amount,
                Currency = intent.Currency,
                Reason = "Payment canceled",
                WebhookEventId = stripeEvent.Id,
                ProcessedAtUtc = now
            },
            cancellationToken);

        if (!lifecycleResult.Success)
        {
            _stripeTelemetry?.RecordPaymentFailed(tenantId, "webhook", lifecycleResult.FailureCode ?? "CANCEL_FAILED");
            throw new InvalidOperationException(
                $"Failed to record Stripe payment cancellation: {lifecycleResult.ErrorMessage}");
        }
        _stripeTelemetry?.RecordRuntimeDecision(tenantId, "cancelled");

        // ═══════════════════════════════════════════════════════════════════════
        // Authoritative outcome events are staged inside PaymentLifecycleService.
        // ═══════════════════════════════════════════════════════════════════════
        // Recovery path: ensure inventory release and downstream events fire even if
        // payment state was already committed on a prior attempt.
        
        if (!lifecycleResult.StateChanged)
        {
            _logger.LogInformation(
                "Stripe cancellation replay - state unchanged; authoritative outcome events are owned by PaymentLifecycleService | OrderId: {OrderId} | StripeEventId: {StripeEventId}",
                orderId, stripeEvent.Id);
        }

        _logger.LogInformation(
            "Stripe payment canceled | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
            orderId, correlationId);

        return orderId.ToString();
    }

    private async Task<string?> HandleChargeRefundedAsync(
        StripeEvent stripeEvent,
        PaymentWebhookLog webhookLog,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var refundStopwatch = Stopwatch.StartNew();
        var charge = stripeEvent.Data.Object as Charge;
        if (charge == null)
        {
            _stripeTelemetry?.RecordRefundLatency(tenantId, "webhook", refundStopwatch.Elapsed, "invalid_payload");
            throw new InvalidOperationException($"Stripe event {stripeEvent.Id} did not contain a charge payload.");
        }

        var stripePaymentIntentId = charge.PaymentIntentId;
        if (string.IsNullOrWhiteSpace(charge.Id) && string.IsNullOrWhiteSpace(stripePaymentIntentId))
        {
            _stripeTelemetry?.RecordRefundLatency(tenantId, "webhook", refundStopwatch.Elapsed, "missing_provider_reference");
            throw new InvalidOperationException($"Stripe refund event {stripeEvent.Id} is missing both charge and payment intent identifiers.");
        }

        var intent = await _dbContext.PaymentIntents
            .Include(pi => pi.Order)
            .FirstOrDefaultAsync(pi =>
                pi.TenantId == tenantId &&
                pi.Provider == PaymentProvider.Stripe &&
                !pi.IsDeleted &&
                ((charge.Id != null && pi.ProviderPaymentId == charge.Id) ||
                 (stripePaymentIntentId != null && pi.ProviderIntentId == stripePaymentIntentId)),
                cancellationToken);

        if (intent == null || intent.Order == null)
        {
            _stripeTelemetry?.RecordRefundLatency(tenantId, "webhook", refundStopwatch.Elapsed, "payment_intent_not_found");
            throw new PaymentIntentNotFoundException(stripePaymentIntentId ?? charge.Id);
        }

        // Populate webhook log with order linkage immediately after intent validation
        // so failed reconciliation attempts still record the relationship
        webhookLog.PaymentIntentId = intent.Id;
        webhookLog.OrderId = intent.OrderId;

        var currency = charge.Currency?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(currency))
        {
            currency = intent.Currency;
        }

        var latestRefund = charge.Refunds?.Data?
            .OrderByDescending(refund => refund.Created)
            .FirstOrDefault();

        var refundAmount = latestRefund != null
            ? ConvertFromStripeAmount(latestRefund.Amount, currency)
            : await ResolveStripeCumulativeRefundDeltaAsync(
                tenantId,
                intent.OrderId,
                charge.AmountRefunded,
                currency,
                cancellationToken);

        // PRODUCTION HARDENING: Do not round here - ConvertFromStripeAmount and
        // ResolveStripeCumulativeRefundDeltaAsync already use currency-specific precision.
        // Let the payment lifecycle service handle currency normalization to preserve
        // precision for currencies with >2 decimal places (e.g., KWD, BHD, JOD).
        if (refundAmount <= 0m)
        {
            _logger.LogInformation(
                "Stripe refund webhook already reflected locally | OrderId: {OrderId} | ChargeId: {ChargeId} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                intent.OrderId,
                charge.Id,
                stripeEvent.Id,
                correlationId);
            _stripeTelemetry?.RecordRefundLatency(tenantId, "webhook", refundStopwatch.Elapsed, "already_reflected");

            return intent.OrderId.ToString();
        }

        var providerRefundId = !string.IsNullOrWhiteSpace(latestRefund?.Id)
            ? latestRefund.Id
            : $"charge-refunded:{charge.Id}:{stripeEvent.Id}";

        var lifecycleResult = await _paymentLifecycleService.RecordRefundAsync(
            intent.OrderId,
            refundAmount,
            $"stripe:{providerRefundId}",
            $"Stripe provider refund reconciled from webhook {stripeEvent.Id}",
            cancellationToken);

        if (!lifecycleResult.Success)
        {
            _stripeTelemetry?.RecordRefundFailure(tenantId, "webhook", "REFUND_RECONCILIATION_FAILED");
            _stripeTelemetry?.RecordRefundLatency(tenantId, "webhook", refundStopwatch.Elapsed, "failed");
            throw new InvalidOperationException(
                $"Failed to reconcile Stripe provider refund {providerRefundId}: {lifecycleResult.ErrorMessage}");
        }
        _stripeTelemetry?.RecordRefundSuccess(tenantId, "webhook");
        _stripeTelemetry?.RecordRuntimeDecision(tenantId, "refund_reconciled");
        _stripeTelemetry?.RecordRefundLatency(tenantId, "webhook", refundStopwatch.Elapsed, "success");


        _logger.LogWarning(
            "Stripe provider refund reconciled through payment lifecycle | OrderId: {OrderId} | RefundId: {RefundId} | Amount: {Amount} {Currency} | EventId: {EventId} | CorrelationId: {CorrelationId}",
            intent.OrderId,
            providerRefundId,
            refundAmount,
            currency,
            stripeEvent.Id,
            correlationId);

        return intent.OrderId.ToString();
    }

    private async Task<decimal> ResolveStripeCumulativeRefundDeltaAsync(
        Guid tenantId,
        Guid orderId,
        long amountRefunded,
        string currency,
        CancellationToken cancellationToken)
    {
        var cumulativeProviderRefunded = ConvertFromStripeAmount(amountRefunded, currency);
        var existingLocalRefunded = await _dbContext.OrderRefunds
            .IgnoreQueryFilters()
            .Where(refund =>
                refund.TenantId == tenantId &&
                refund.OrderId == orderId &&
                !refund.IsDeleted)
            .SumAsync(refund => (decimal?)refund.Amount, cancellationToken) ?? 0m;

        return cumulativeProviderRefunded - existingLocalRefunded;
    }

    #endregion

    #region Configuration Status

    /// <inheritdoc />
    public async Task<StripeConfigurationStatusDto> GetConfigurationStatusAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var paymentMethod = await _dbContext.TenantPaymentMethods
            .FirstOrDefaultAsync(pm => pm.TenantId == tenantId && pm.Code == "STRIPE", cancellationToken);

        if (paymentMethod == null)
        {
            return new StripeConfigurationStatusDto
            {
                IsEnabled = false,
                IsConfigured = false,
                IsSandbox = true,
                ValidationMessage = "Stripe payment method not found"
            };
        }

        var configuration = EvaluatePaymentMethodConfiguration(paymentMethod, "status");
        if (!paymentMethod.IsEnabled)
        {
            return new StripeConfigurationStatusDto
            {
                IsEnabled = false,
                IsConfigured = false,
                IsSandbox = paymentMethod.IsSandbox,
                ValidationMessage = "STRIPE_PAYMENT_METHOD_DISABLED"
            };
        }

        if (!configuration.IsConfigurationValid)
        {
            return new StripeConfigurationStatusDto
            {
                IsEnabled = true,
                IsConfigured = false,
                IsSandbox = paymentMethod.IsSandbox,
                ValidationMessage = configuration.ValidationErrors.FirstOrDefault() ?? "Stripe configuration is incomplete"
            };
        }

        try
        {
            await _stripeTenantConfigurationResolver.ResolveCredentialsAsync(
                tenantId,
                StripeOperationalCapability.PaymentIntent,
                cancellationToken);
            await _stripeTenantConfigurationResolver.ResolveCredentialsAsync(
                tenantId,
                StripeOperationalCapability.Webhook,
                cancellationToken);
        }
        catch (PaymentException exception)
        {
            return new StripeConfigurationStatusDto
            {
                IsEnabled = true,
                IsConfigured = false,
                IsSandbox = paymentMethod.IsSandbox,
                ValidationMessage = exception.ErrorCode
            };
        }

        return new StripeConfigurationStatusDto
        {
            IsEnabled = true,
            IsConfigured = true,
            IsSandbox = paymentMethod.IsSandbox,
            ValidationMessage = null
        };
    }

    #endregion

    #region Helper Methods

    private async Task<TenantStripeConfiguration?> GetStripeConfigurationAsync(
        Guid tenantId,
        StripeOperationalCapability capability,
        CancellationToken cancellationToken)
    {
        try
        {
            var credentials = await _stripeTenantConfigurationResolver.ResolveCredentialsAsync(
                tenantId,
                capability,
                cancellationToken);

            if (credentials is null)
            {
                return null;
            }

            return new TenantStripeConfiguration
            {
                PublishableKey = credentials.PublishableKey,
                SecretKey = credentials.SecretKey,
                WebhookSecret = credentials.WebhookSecret
            };
        }
        catch (StripeNotConfiguredException)
        {
            _reliabilityTelemetry?.RecordPaymentConfigInvalid("stripe", capability.ToString(), "configuration_authority_rejected");
            return null;
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
                _reliabilityTelemetry?.RecordPaymentConfigInvalid("stripe", operation, "config_missing");
            }

            return result;
        }

        try
        {
            var decrypted = _dataProtector.Unprotect(paymentMethod.ConfigEncrypted);
            var configuration = JsonSerializer.Deserialize<Dictionary<string, string>>(decrypted, _jsonOptions);
            var result = PaymentMethodConfigurationValidator.Validate(paymentMethod.Code, configuration);

            if (!result.IsConfigurationValid)
            {
                _reliabilityTelemetry?.RecordPaymentConfigInvalid("stripe", operation, result.ValidationErrors.FirstOrDefault() ?? "config_invalid");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt Stripe configuration");
            _reliabilityTelemetry?.RecordPaymentConfigInvalid("stripe", operation, "decrypt_failed");

            return new PaymentMethodConfigurationValidationResult(
                paymentMethod.Code?.Trim().ToUpperInvariant() ?? string.Empty,
                PaymentMethodConfigurationValidator.RequiresConfiguration(paymentMethod.Code),
                false,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new[] { "Stored Stripe configuration could not be decrypted." },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static string BuildRoutingMetadata(
        Guid tenantId,
        Guid orderId,
        string storefrontBaseUrl)
    {
        return PaymentIntentRoutingMetadataSerializer.Serialize(
            tenantId,
            orderId,
            storefrontBaseUrl);
    }

    private async Task BackfillRoutingMetadataAsync(
        Guid tenantId,
        Guid orderId,
        Tenant tenant,
        CancellationToken cancellationToken)
    {
        // Target the latest pending intent, not an arbitrary historical row
        var existingIntent = await _dbContext.PaymentIntents
            .Where(pi => pi.OrderId == orderId &&
                         pi.TenantId == tenantId &&
                         pi.Provider == PaymentProvider.Stripe &&
                         pi.Status == PaymentIntentStatus.Pending)
            .OrderByDescending(pi => pi.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingIntent == null)
        {
            return;
        }

        var routingMetadata = PaymentIntentRoutingMetadataSerializer.Deserialize(existingIntent.ProviderMetadata);
        if (PaymentIntentRoutingMetadataSerializer.MatchesStoredIntent(routingMetadata, tenantId, orderId))
        {
            return;
        }

        // Only resolve storefront URL when metadata repair is actually required
        var storefrontBaseUrl = _storefrontUrlResolver.Resolve(tenant);
        existingIntent.ProviderMetadata = BuildRoutingMetadata(tenantId, orderId, storefrontBaseUrl);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Validates that the checkout token matches the order's session.
    /// Prevents unauthorized payment intent creation by validating session ownership.
    /// </summary>
    private async Task ValidateCheckoutSessionOwnershipAsync(
        string checkoutToken,
        Guid tenantId,
        Guid orderId,
        string? orderSessionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(checkoutToken))
        {
            _logger.LogWarning(
                "Checkout token validation failed: empty token | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                orderId, correlationId);
            throw new UnauthorizedAccessException("Invalid checkout token");
        }

        var validation = _checkoutSessionTokenService.ValidatePaymentToken(checkoutToken, tenantId);
        if (validation.IsExpired)
        {
            _logger.LogWarning(
                "Checkout token validation failed: token expired | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                orderId, correlationId);
            throw new UnauthorizedAccessException("Checkout token expired");
        }

        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "Checkout token validation failed: invalid token | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                orderId,
                correlationId);
            throw new UnauthorizedAccessException("Invalid checkout token");
        }

        // SECURITY: Validate token is bound to THIS specific order, not just the browser session.
        // This prevents token reuse across multiple orders from the same session.
        // Parse the token subject as a Guid for robust validation independent of string format.
        if (!Guid.TryParse(validation.Subject, out var parsedSubject) || parsedSubject != orderId)
        {
            _logger.LogWarning(
                "Checkout token validation failed: order mismatch | OrderId: {OrderId} | TokenSubject: {TokenSubject} | CorrelationId: {CorrelationId}",
                orderId,
                validation.Subject,
                correlationId);
            throw new UnauthorizedAccessException("Checkout token does not match order");
        }

        _logger.LogInformation(
            "Checkout token validated successfully | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
            orderId,
            correlationId);
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
                "Stripe order/customer tenant mismatch detected | TenantId: {TenantId} | OrderId: {OrderId} | OrderCustomerId: {OrderCustomerId} | CorrelationId: {CorrelationId}",
                tenantId,
                order.Id,
                order.OnlineCustomerId.Value,
                correlationId);
            throw new UnauthorizedAccessException(TenantErrorCodes.TenantMismatch);
        }
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
                    "Unauthenticated Stripe payment intent creation attempt for authenticated customer order | OrderId: {OrderId} | OrderCustomerId: {OrderCustomerId} | CorrelationId: {CorrelationId}",
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
                "Stripe customer tenant mismatch detected | TenantId: {TenantId} | OrderId: {OrderId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
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
                "Authenticated customer accessing guest order for Stripe payment | OrderId: {OrderId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
                orderId,
                customerId.Value,
                correlationId);
            return;
        }

        if (order.OnlineCustomerId != customerId.Value)
        {
            _logger.LogWarning(
                "Stripe customer ownership validation failed | OrderId: {OrderId} | RequestingCustomerId: {RequestingCustomerId} | OrderCustomerId: {OrderCustomerId} | CorrelationId: {CorrelationId}",
                orderId,
                customerId.Value,
                order.OnlineCustomerId,
                correlationId);
            throw new PaymentUnauthorizedAccessException(orderId, customerId);
        }
    }

    private TenantStripeConfiguration? DecryptConfiguration(string encryptedConfig)
    {
        try
        {
            var decrypted = _dataProtector.Unprotect(encryptedConfig);
            return JsonSerializer.Deserialize<TenantStripeConfiguration>(decrypted, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt Stripe configuration");
            return null;
        }
    }

    private static bool TryResolveStripeOrderId(
        IReadOnlyDictionary<string, string> metadata,
        out Guid orderId)
    {
        if ((metadata.TryGetValue("orderId", out var value) ||
             metadata.TryGetValue("canonical_orderId", out value)) &&
            Guid.TryParse(value, out orderId))
        {
            return true;
        }

        orderId = Guid.Empty;
        return false;
    }

    private static long ConvertToStripeAmount(decimal amount, string currency)
    {
        // Stripe expects amounts in the smallest currency unit (cents for USD)
        // Use CurrencyHelper to get the correct decimal places for the currency
        var decimalPlaces = PointOfSale.Infrastructure.Payments.CurrencyHelper.GetDecimalPlaces(currency?.ToUpperInvariant());
        var multiplier = (decimal)Math.Pow(10, decimalPlaces);
        return (long)Math.Round(amount * multiplier);
    }

    private static decimal ConvertFromStripeAmount(long amount, string currency)
    {
        var decimalPlaces = PointOfSale.Infrastructure.Payments.CurrencyHelper.GetDecimalPlaces(currency);
        return amount / (decimal)Math.Pow(10, decimalPlaces);
    }

    private static string TruncateForStripe(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
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

    private static string GeneratePaymentReference(Guid orderId)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var orderIdShort = orderId.ToString("N")[..8].ToUpperInvariant();
        return $"STR-{timestamp}-{orderIdShort}";
    }

    /// <summary>
    /// Stores the client secret securely in the payment intent using encryption.
    /// </summary>
    private void SetClientSecret(CorePaymentIntent paymentIntent, string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ArgumentException("Client secret cannot be null or empty", nameof(clientSecret));
        }

        paymentIntent.ClientSecretEncrypted = _clientSecretProtector.Protect(clientSecret);
        paymentIntent.RedirectUrl = null; // Clear RedirectUrl to avoid confusion
    }

    /// <summary>
    /// Retrieves the decrypted client secret from the payment intent.
    /// Returns null if no client secret is stored.
    /// </summary>
    private string? GetClientSecret(CorePaymentIntent paymentIntent)
    {
        // Migration path: Check old RedirectUrl field first for backward compatibility
        if (!string.IsNullOrWhiteSpace(paymentIntent.ClientSecretEncrypted))
        {
            try
            {
                return _clientSecretProtector.Unprotect(paymentIntent.ClientSecretEncrypted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Failed to decrypt client secret for PaymentIntent {PaymentIntentId}", 
                    paymentIntent.Id);
                return null;
            }
        }

        // Fallback to RedirectUrl for backward compatibility (will be removed after migration)
        if (!string.IsNullOrWhiteSpace(paymentIntent.RedirectUrl) && 
            paymentIntent.RedirectUrl.StartsWith("pi_") && 
            paymentIntent.RedirectUrl.Contains("_secret_"))
        {
            _logger.LogWarning(
                "PaymentIntent {PaymentIntentId} still using RedirectUrl for client_secret - migration needed",
                paymentIntent.Id);
            return paymentIntent.RedirectUrl;
        }

        return null;
    }

    private async Task<T?> GetIdempotencyResultAsync<T>(string key, string correlationId) where T : class
    {
        try
        {
            var db = _redisFactory.GetDatabase();
            if (db == null) return null;

            var json = await db.StringGetAsync(key);
            if (json.IsNullOrEmpty) return null;

            return JsonSerializer.Deserialize<T>(json!, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Idempotency check error | Key: {Key} | CorrelationId: {CorrelationId}", key, correlationId);
            return null;
        }
    }

    private async Task StoreIdempotencyResultAsync<T>(string key, T response, int ttlHours, string correlationId)
    {
        try
        {
            var db = _redisFactory.GetDatabase();
            if (db == null) return;

            var json = JsonSerializer.Serialize(response, _jsonOptions);
            await db.StringSetAsync(key, json, TimeSpan.FromHours(ttlHours));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Idempotency store error | Key: {Key} | CorrelationId: {CorrelationId}", key, correlationId);
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
                       log.Provider == "stripe" &&
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
        var auditPayload = WebhookDataRedactor.RedactPayload(rawPayload);
        var signatureDigest = HashWebhookSignature(signature);
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
            existingLog.RawPayload = auditPayload;
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
            Provider = "stripe",
            ProviderEventId = eventId,
            EventType = eventType,
            RawPayload = auditPayload,
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
            var auditPayload = WebhookDataRedactor.RedactPayload(rawPayload);
            var signatureDigest = HashWebhookSignature(signature);
            _dbContext.ChangeTracker.Clear();

            var persistedLog = await TryGetWebhookLogAsync(tenantId, eventId, cancellationToken);
            if (persistedLog == null)
            {
                persistedLog = new PaymentWebhookLog
                {
                    Id = webhookLog.Id,
                    TenantId = tenantId,
                    Provider = "stripe",
                    ProviderEventId = eventId,
                    EventType = eventType,
                    PaymentIntentId = webhookLog.PaymentIntentId,
                    OrderId = webhookLog.OrderId,
                    RawPayload = auditPayload,
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
                persistedLog.RawPayload = auditPayload;
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
                    "Stripe webhook repeatedly failed | EventId: {EventId} | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
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
                "Failed to persist Stripe webhook failure log | EventId: {EventId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
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

    /// <summary>
    /// HARDENING: Lookup payment intent with fallback for recovery from transient SaveChangesAsync failures.
    /// If ProviderIntentId lookup fails, attempts to find a pending intent for the orderId with blank ProviderIntentId
    /// and backfills it from the webhook. This handles the edge case where SaveChangesAsync failed after Stripe
    /// returned the intent, leaving ProviderIntentId blank.
    /// NOTE: RedirectUrl recovery is now handled by RecoverRedirectUrlAsync as a post-commit background operation
    /// to avoid holding DB locks during Stripe API calls.
    /// </summary>
    private async Task<CorePaymentIntent?> FindPaymentIntentWithFallbackAsync(
        string stripeIntentId,
        Guid orderId,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Primary lookup by ProviderIntentId
        var intent = await _dbContext.PaymentIntents
            .Include(pi => pi.Order)
            .FirstOrDefaultAsync(pi => pi.ProviderIntentId == stripeIntentId && pi.TenantId == tenantId, cancellationToken);

        // Fallback lookup if primary fails
        if (intent == null)
        {
            _logger.LogWarning(
                "Payment intent not found by ProviderIntentId, attempting fallback lookup by scanning pending intents | StripeIntentId: {StripeIntentId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                stripeIntentId, orderId, correlationId);

            // Get pending Stripe intent for this tenant with blank ProviderIntentId matching the orderId
            var pendingIntent = await _dbContext.PaymentIntents
                .Include(pi => pi.Order)
                .FirstOrDefaultAsync(pi => 
                    pi.TenantId == tenantId && 
                    pi.OrderId == orderId &&
                    pi.Provider == PaymentProvider.Stripe && 
                    pi.Status == PaymentIntentStatus.Pending &&
                    (pi.ProviderIntentId == null || pi.ProviderIntentId == string.Empty),
                    cancellationToken);

            if (pendingIntent != null)
            {
                intent = pendingIntent;
                
                _logger.LogInformation(
                    "Payment intent found via fallback (pending intent for orderId), backfilling ProviderIntentId | PaymentIntentId: {PaymentIntentId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                    intent.Id, stripeIntentId, correlationId);
                
                intent.ProviderIntentId = stripeIntentId;
                // Note: SaveChangesAsync will be called later in the webhook handler
            }
        }

        // NOTE: RedirectUrl recovery is now handled by RecoverRedirectUrlAsync as a post-commit background operation
        // to avoid holding DB locks during Stripe API calls within the webhook transaction.
        if (intent != null && string.IsNullOrWhiteSpace(intent.ClientSecretEncrypted))
        {
            _logger.LogInformation(
                "Payment intent missing client secret (ClientSecretEncrypted), will be recovered in background | PaymentIntentId: {PaymentIntentId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                intent.Id, stripeIntentId, correlationId);
        }

        return intent;
    }

    private static string HashWebhookSignature(string signature)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(signature ?? string.Empty));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation;

    #endregion
}
