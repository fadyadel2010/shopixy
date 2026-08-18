using System.Net.Http.Headers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Security;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis.Services;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubPaymentService
{
    Task<CommerceHubPaymentInitiateResponseDto> InitiateAsync(
        CommerceHubPaymentInitiateRequestDto request,
        Guid? authenticatedCustomerId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubPaymentWebhookResultDto> ProcessWebhookAsync(
        string? provider,
        string? signature,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubPaymentStatusDto> GetPaymentStatusAsync(
        string? provider,
        Guid orderId,
        Guid? authenticatedCustomerId,
        string? orderAccessToken,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubPaymentConfigurationStatusDto> GetConfigurationStatusAsync(
        string? provider,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubPaymentCallbackResultDto> ValidateCallbackAsync(
        string? provider,
        IReadOnlyDictionary<string, string> callbackParams,
        string correlationId,
        CancellationToken cancellationToken = default);
}

internal sealed class CommerceHubProviderSessionPersistenceException : Exception
{
    public CommerceHubProviderSessionPersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class CommerceHubProviderSessionOutcomeUnknownException : Exception
{
    public CommerceHubProviderSessionOutcomeUnknownException(
        string message,
        string providerIntentId,
        Exception innerException)
        : base(message, innerException)
    {
        ProviderIntentId = providerIntentId;
    }

    public string ProviderIntentId { get; }
}

public class CommerceHubPaymentService : ICommerceHubPaymentService
{
    private const string PendingTenantResolutionStatus = "PendingTenantResolution";
    private const string RoutedToSuspenseStatus = "RoutedToSuspense";
    private const string SuspenseMatchedPendingClearanceStatus = "SuspenseMatchedPaymentPendingClearance";
    private const string DeferredCapturedPaymentAuthorityPersistenceMessage =
        "Durable provider payment evidence was recorded while the CommerceHub payment authority lock was unavailable. Automatic suspense routing and marketplace hold were deferred until reconciliation can safely re-enter the payment authority boundary.";
    private const string DeferredRefundAuthorityPersistenceMessage =
        "Durable provider refund evidence was recorded while the CommerceHub payment authority lock was unavailable. Automatic payment linkage was deferred until reconciliation can safely re-enter the payment authority boundary.";
    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubFinancialWorkflowService _financialWorkflowService;
    private readonly IInventoryConsistencyService _inventoryConsistencyService;
    private readonly ICommerceHubFinancialReadinessGate _financialReadinessGate;
    private readonly ICommerceHubOrderAccessService _orderAccessService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IDistributedLockService _lockService;
    private readonly IReadOnlyDictionary<string, IPaymentProviderAdapter> _providerAdapters;
    private readonly CommerceHubPaymentsOptions _paymentsOptions;
    private readonly CommerceHubPaymentOrchestrationTelemetry _telemetry;
    private readonly ILogger<CommerceHubPaymentService> _logger;
    private readonly IPaymobSensitiveArtifactProtector? _paymobArtifactProtector;

    public CommerceHubPaymentService(
        ApplicationDbContext dbContext,
        ICommerceHubFinancialWorkflowService financialWorkflowService,
        IInventoryConsistencyService inventoryConsistencyService,
        ICommerceHubFinancialReadinessGate financialReadinessGate,
        ICommerceHubOrderAccessService orderAccessService,
        IEventPublisher eventPublisher,
        IDistributedLockService lockService,
        IEnumerable<IPaymentProviderAdapter> providerAdapters,
        IOptions<CommerceHubPaymentsOptions> paymentsOptions,
        CommerceHubPaymentOrchestrationTelemetry telemetry,
        ILogger<CommerceHubPaymentService> logger,
        IPaymobSensitiveArtifactProtector? paymobArtifactProtector = null)
    {
        _dbContext = dbContext;
        _financialWorkflowService = financialWorkflowService;
        _inventoryConsistencyService = inventoryConsistencyService;
        _financialReadinessGate = financialReadinessGate;
        _orderAccessService = orderAccessService;
        _eventPublisher = eventPublisher;
        _lockService = lockService;
        _providerAdapters = providerAdapters.ToDictionary(
            adapter => adapter.ProviderCode.Normalized,
            StringComparer.OrdinalIgnoreCase);
        _paymentsOptions = paymentsOptions.Value;
        _telemetry = telemetry;
        _logger = logger;
        _paymobArtifactProtector = paymobArtifactProtector;
    }

    public async Task<CommerceHubPaymentInitiateResponseDto> InitiateAsync(
        CommerceHubPaymentInitiateRequestDto request,
        Guid? authenticatedCustomerId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginPayment();
        ArgumentNullException.ThrowIfNull(request);

        var validation = request.Validate();
        if (validation.Code != null)
        {
            throw new CommerceHubCheckoutException(
                validation.Code,
                validation.Message!,
                StatusCodes.Status400BadRequest);
        }

        var (providerCode, provider) = NormalizeProvider(request.Provider);
        var marketplaceOrder = await ResolveMarketplaceOrderAsync(request.OrderId, cancellationToken);
        if (marketplaceOrder == null)
        {
            throw new CommerceHubCheckoutException(
                "ORDER_NOT_FOUND",
                "Marketplace order was not found.",
                StatusCodes.Status404NotFound);
        }

        EnsureInitiationAccessAllowed(marketplaceOrder, authenticatedCustomerId, request.OrderAccessToken);
        RequireCommerceHubPaymentCurrency(marketplaceOrder.Currency, "payment initiation order");
        await EnsureInitiationAllowedAsync(marketplaceOrder, providerCode, cancellationToken);

        await _inventoryConsistencyService.EnsureMarketplaceOrderReservationsActiveAsync(
            marketplaceOrder.Id,
            cancellationToken);

        var paymentLockKey = BuildPaymentLockKey(marketplaceOrder.Id, providerCode);
        var paymentLockToken = Guid.NewGuid().ToString("N");
        var paymentLockResult = await _lockService.TryAcquireLockAsync(
            paymentLockKey,
            paymentLockToken,
            TimeSpan.FromMinutes(2));

        if (paymentLockResult.IsInfrastructureFailure)
        {
            _logger.LogError(
                "CommerceHub payment initiation lock acquisition failed due to infrastructure failure | OrderId: {OrderId} | Provider: {Provider} | CorrelationId: {CorrelationId} | Metric: CommerceHub.PaymentInitiation.InfrastructureFailure",
                marketplaceOrder.Id,
                providerCode,
                correlationId);
            throw new CommerceHubCheckoutException(
                "LOCK_INFRASTRUCTURE_FAILURE",
                "Payment initiation is temporarily unavailable because the lock infrastructure could not be reached.",
                StatusCodes.Status503ServiceUnavailable);
        }

        if (!paymentLockResult.IsAcquired)
        {
            _logger.LogWarning(
                "CommerceHub payment initiation could not acquire payment lock (already held) | OrderId: {OrderId} | Provider: {Provider} | CorrelationId: {CorrelationId} | Metric: CommerceHub.PaymentInitiation.LockAlreadyHeld",
                marketplaceOrder.Id,
                providerCode,
                correlationId);
            throw new CommerceHubCheckoutException(
                "PAYMENT_IN_PROGRESS",
                "A payment session is already being created for this order. Please wait a moment and try again.",
                StatusCodes.Status409Conflict);
        }

        try
        {
        // Re-check for unresolved payment webhooks after acquiring lock to prevent race condition
        await EnsureNoUnresolvedPaymentAsync(marketplaceOrder, providerCode, cancellationToken);
        await _inventoryConsistencyService.EnsureMarketplaceOrderReservationsActiveAsync(
            marketplaceOrder.Id,
            cancellationToken);

        var payments = await _dbContext.CommerceHubPayments
            .Include(payment => payment.Items)
            .Include(payment => payment.Attempts)
            .Include(payment => payment.Logs)
            .AsSplitQuery()
            .Where(payment =>
                payment.OrderId == marketplaceOrder.Id &&
                !payment.IsDeleted)
            .OrderByDescending(payment => payment.CreatedAt)
            .ToListAsync(cancellationToken);

        if (payments.Any(payment => payment.Status == CommerceHubPaymentRecordStatus.Paid))
        {
            throw new CommerceHubCheckoutException(
                "ORDER_ALREADY_PAID",
                "This marketplace order is already marked as paid.",
                StatusCodes.Status409Conflict);
        }

        var providerPayments = payments
            .Where(payment => payment.Provider == provider)
            .ToList();

        var reusablePayment = providerPayments.FirstOrDefault(payment =>
            payment.Status == CommerceHubPaymentRecordStatus.Pending &&
            CommerceHubPaymentSessionRecoveryAuthority.HasReusableSession(payment));

        if (reusablePayment != null)
        {
            _logger.LogInformation(
                "CommerceHub payment initiation reused an existing pending session | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | CorrelationId: {CorrelationId}",
                reusablePayment.Id,
                marketplaceOrder.Id,
                providerCode,
                correlationId);

            return MapInitiateResponse(
                reusablePayment,
                marketplaceOrder,
                providerCode,
                isReusable: true,
                message: "Existing pending payment session reused.");
        }

        var incompletePendingPayment = providerPayments.FirstOrDefault(
            CommerceHubPaymentSessionRecoveryAuthority.HasProviderSessionPersistenceGap);

        if (incompletePendingPayment != null)
        {
            var recoveredResponse = await TryRecoverIncompletePendingPaymentAsync(
                marketplaceOrder,
                incompletePendingPayment,
                providerCode,
                correlationId,
                cancellationToken);
            if (recoveredResponse != null)
            {
                return recoveredResponse;
            }

            _logger.LogError(
                "CommerceHub payment initiation blocked because a prior provider session outcome is unresolved | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | ProviderReference: {ProviderReference} | Status: {PaymentStatus} | FailureReason: {FailureReason} | CorrelationId: {CorrelationId}",
                incompletePendingPayment.Id,
                marketplaceOrder.Id,
                providerCode,
                incompletePendingPayment.ProviderReference,
                incompletePendingPayment.Status,
                incompletePendingPayment.FailureReason,
                correlationId);

            throw new CommerceHubCheckoutException(
                "PAYMENT_PROVIDER_SESSION_RECONCILIATION_REQUIRED",
                "A previous payment session attempt may already exist at the payment provider. This order is blocked from creating another external session until payment reconciliation completes.",
                StatusCodes.Status409Conflict);
        }

        var payment = providerPayments.FirstOrDefault(payment => payment.Status == CommerceHubPaymentRecordStatus.Pending);
        var isNewPayment = payment == null;
        if (payment == null)
        {
            EnsureRetryBudget(marketplaceOrder, payments);
            payment = CreatePendingPayment(marketplaceOrder, provider);
            _dbContext.CommerceHubPayments.Add(payment);
        }

        var itemsAdded = EnsurePaymentItems(payment, marketplaceOrder.SubOrders);
        var paymentAttempt = CreatePaymentAttempt(payment);
        _dbContext.CommerceHubPaymentAttempts.Add(paymentAttempt);
        var providerSessionReinitFenceReason = BuildProviderSessionReinitFenceReason(providerCode);

        payment.Status = CommerceHubPaymentRecordStatus.Pending;
        payment.FailureReason = providerSessionReinitFenceReason;
        payment.ProviderIntentId = $"{CommerceHubPaymentSessionRecoveryAuthority.ProviderSessionReinitFencePrefix}{payment.Id}"; // Unique sentinel to prevent recovery from creating new session
        payment.CheckoutUrl = null;
        payment.PaymentToken = null;
        payment.ProviderTransactionId = null;
        payment.PaidAtUtc = null;
        payment.IsProcessed = false;
        payment.ProcessedAtUtc = null;

        paymentAttempt.Status = CommerceHubPaymentRecordStatus.Pending;
        paymentAttempt.FailureReason = providerSessionReinitFenceReason;
        paymentAttempt.ProviderIntentId = $"{CommerceHubPaymentSessionRecoveryAuthority.ProviderSessionReinitFencePrefix}{paymentAttempt.Id}"; // Unique sentinel to prevent recovery from creating new session
        paymentAttempt.CompletedAtUtc = null;

        await _financialReadinessGate.EnsureCommerceIntakeAllowedAsync(
            marketplaceOrder.GetAffectedStoreIds(),
            "CommerceHubPayment.Initiate",
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await RecomposeMarketplaceReservationAuthorityBeforeProviderSessionAsync(
            marketplaceOrder,
            "CommerceHubPayment.Initiate.ProviderSession",
            cancellationToken);

        CommerceHubPaymentProviderSession? acceptedProviderSession = null;
        var initiationEventType = $"initiate.{providerCode.ToLowerInvariant()}";

        try
        {
            var session = provider switch
            {
                PaymentProvider.Paymob => await CreatePaymobSessionAsync(
                    marketplaceOrder,
                    payment,
                    correlationId,
                    cancellationToken),
                PaymentProvider.Stripe => await CreateStripeSessionAsync(
                    marketplaceOrder,
                    payment,
                    correlationId,
                    cancellationToken),
                _ => throw new CommerceHubCheckoutException(
                    "PAYMENT_PROVIDER_INVALID",
                    "Unsupported CommerceHub payment provider.",
                    StatusCodes.Status400BadRequest)
            };
            acceptedProviderSession = session;

            await EnsureProviderIdentityNotLinkedToAnotherPaymentAsync(
                provider,
                session.ProviderIntentId,
                null,
                payment.Id,
                cancellationToken);

            payment.Status = CommerceHubPaymentRecordStatus.Pending;
            payment.FailureReason = null;
            payment.ProviderIntentId = session.ProviderIntentId;
            payment.CheckoutUrl = session.PaymentUrl;
            payment.PaymentToken = session.PaymentToken;

            paymentAttempt.ProviderIntentId = session.ProviderIntentId;
            paymentAttempt.Status = CommerceHubPaymentRecordStatus.Pending;
            paymentAttempt.CompletedAtUtc = DateTime.UtcNow;
            paymentAttempt.FailureReason = null;

            _dbContext.CommerceHubPaymentLogs.Add(new CommerceHubPaymentLog
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                EventType = initiationEventType,
                ProviderEventId = session.ProviderIntentId,
                RawResponse = session.RawResponse
            });

            await PersistProviderSessionAsync(
                payment,
                paymentAttempt,
                marketplaceOrder,
                session,
                providerCode,
                initiationEventType,
                correlationId,
                cancellationToken);

            _logger.LogInformation(
                "CommerceHub payment initiated | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | CorrelationId: {CorrelationId}",
                payment.Id,
                marketplaceOrder.Id,
                providerCode,
                correlationId);

            return MapInitiateResponse(
                payment,
                marketplaceOrder,
                providerCode,
                isReusable: false,
                message: "Payment session created successfully.");
        }
        catch (CommerceHubProviderSessionOutcomeUnknownException ex)
        {
            payment.ProviderIntentId = ex.ProviderIntentId;
            paymentAttempt.ProviderIntentId = ex.ProviderIntentId;

            await RecordInitiationUnknownOutcomeAsync(
                payment,
                paymentAttempt,
                providerCode,
                ex.Message,
                correlationId,
                cancellationToken);

            _logger.LogError(
                ex,
                "CommerceHub payment provider session outcome is unknown after provider accepted reusable identity | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | ProviderIntentId: {ProviderIntentId} | CorrelationId: {CorrelationId}",
                payment.Id,
                marketplaceOrder.Id,
                providerCode,
                ex.ProviderIntentId,
                correlationId);

            throw new CommerceHubCheckoutException(
                "PAYMENT_PROVIDER_SESSION_RECONCILIATION_REQUIRED",
                "The payment provider accepted an order, but POS could not complete the customer payment session. Retrying will recover the accepted provider order instead of creating another one.",
                StatusCodes.Status503ServiceUnavailable,
                ex.Message);
        }
        catch (CommerceHubProviderSessionPersistenceException ex)
        {
            if (acceptedProviderSession != null)
            {
                await BestEffortRecordProviderSessionPersistenceGapAsync(
                    payment,
                    paymentAttempt,
                    marketplaceOrder,
                    acceptedProviderSession,
                    providerCode,
                    initiationEventType,
                    correlationId);
            }

            _logger.LogCritical(
                ex,
                "CommerceHub payment provider session persistence failed after provider accepted session | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | CorrelationId: {CorrelationId}",
                payment.Id,
                marketplaceOrder.Id,
                providerCode,
                correlationId);

            throw new CommerceHubCheckoutException(
                "PAYMENT_PROVIDER_SESSION_RECONCILIATION_REQUIRED",
                "The payment provider accepted a session, but POS could not durably persist the provider identity. Creating another session is blocked until reconciliation completes.",
                StatusCodes.Status503ServiceUnavailable,
                ex.Message);
        }
        catch (CommerceHubCheckoutException ex)
        {
            await RecordInitiationFailureAsync(payment, paymentAttempt, providerCode, ex.Message, correlationId, cancellationToken);
            throw;
        }
        catch (HttpRequestException ex)
        {
            await RecordInitiationUnknownOutcomeAsync(payment, paymentAttempt, providerCode, ex.Message, correlationId, cancellationToken);

            throw new CommerceHubCheckoutException(
                "PAYMENT_PROVIDER_SESSION_RECONCILIATION_REQUIRED",
                "The payment provider request ended without a trustworthy outcome. Creating another session is blocked until reconciliation confirms whether the provider created a payment session.",
                StatusCodes.Status503ServiceUnavailable,
                ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            await RecordInitiationUnknownOutcomeAsync(payment, paymentAttempt, providerCode, ex.Message, correlationId, cancellationToken);

            throw new CommerceHubCheckoutException(
                "PAYMENT_PROVIDER_SESSION_RECONCILIATION_REQUIRED",
                "The payment provider request timed out. Creating another session is blocked until reconciliation confirms whether the provider created a payment session.",
                StatusCodes.Status503ServiceUnavailable,
                ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RecordInitiationFailureAsync(payment, paymentAttempt, providerCode, ex.Message, correlationId, cancellationToken);

            _logger.LogError(
                ex,
                "CommerceHub payment initiation failed unexpectedly | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | CorrelationId: {CorrelationId}",
                payment.Id,
                marketplaceOrder.Id,
                providerCode,
                correlationId);

            throw new CommerceHubCheckoutException(
                "PAYMENT_INITIATE_FAILED",
                "Unable to initiate the payment session right now.",
                StatusCodes.Status500InternalServerError);
        }
        }
        finally
        {
            await _lockService.ReleaseLockAsync(paymentLockKey, paymentLockToken);
        }
    }

    private async Task BestEffortRecordProviderSessionPersistenceGapAsync(
        CommerceHubPayment payment,
        CommerceHubPaymentAttempt paymentAttempt,
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubPaymentProviderSession session,
        string providerCode,
        string initiationEventType,
        string correlationId)
    {
        try
        {
            using var paymentBoundary = CommerceHubBoundaryMutationScope.BeginPayment();
            _dbContext.ChangeTracker.Clear();

            var nowUtc = DateTime.UtcNow;
            var failureReason =
                "PROVIDER_SESSION_OUTCOME_UNKNOWN: Provider accepted a payment session, but POS could not prove durable persistence of the full session payload.";

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                CancellationToken.None);

            var paymentRows = await _dbContext.CommerceHubPayments
                .Where(item =>
                    item.Id == payment.Id &&
                    item.OrderId == marketplaceOrder.Id &&
                    !item.IsDeleted &&
                    item.Status == CommerceHubPaymentRecordStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ProviderIntentId, session.ProviderIntentId)
                    .SetProperty(item => item.ProviderTransactionId, (string?)null)
                    .SetProperty(item => item.CheckoutUrl, (string?)null)
                    .SetProperty(item => item.PaymentToken, (string?)null)
                    .SetProperty(item => item.FailureReason, failureReason)
                    .SetProperty(item => item.UpdatedAt, nowUtc),
                    CancellationToken.None);

            if (paymentRows != 1)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogCritical(
                    "CommerceHub provider-session persistence gap marker could not claim payment row | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | ProviderIntentId: {ProviderIntentId} | Rows: {Rows} | CorrelationId: {CorrelationId}",
                    payment.Id,
                    marketplaceOrder.Id,
                    providerCode,
                    session.ProviderIntentId,
                    paymentRows,
                    correlationId);
                return;
            }

            await _dbContext.CommerceHubPaymentAttempts
                .Where(item => item.Id == paymentAttempt.Id && item.PaymentId == payment.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ProviderIntentId, session.ProviderIntentId)
                    .SetProperty(item => item.Status, CommerceHubPaymentRecordStatus.Pending)
                    .SetProperty(item => item.FailureReason, failureReason)
                    .SetProperty(item => item.CompletedAtUtc, nowUtc)
                    .SetProperty(item => item.UpdatedAt, nowUtc),
                    CancellationToken.None);

            _dbContext.CommerceHubPaymentLogs.Add(new CommerceHubPaymentLog
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                EventType = $"{initiationEventType}.persistence_gap",
                ProviderEventId = session.ProviderIntentId,
                RawResponse = JsonSerializer.Serialize(new
                {
                    paymentId = payment.Id,
                    provider = providerCode,
                    marketplaceOrderId = marketplaceOrder.Id,
                    providerIntentId = session.ProviderIntentId,
                    correlationId,
                    reconciliationRequired = true
                })
            });

            await _dbContext.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);

            _logger.LogCritical(
                "CommerceHub provider-session persistence gap marked for reconciliation | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | ProviderIntentId: {ProviderIntentId} | CorrelationId: {CorrelationId}",
                payment.Id,
                marketplaceOrder.Id,
                providerCode,
                session.ProviderIntentId,
                correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "CommerceHub provider-session persistence gap could not be marked; duplicate provider session creation must be prevented operationally | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | ProviderIntentId: {ProviderIntentId} | CorrelationId: {CorrelationId}",
                payment.Id,
                marketplaceOrder.Id,
                providerCode,
                session.ProviderIntentId,
                correlationId);
        }
    }

    public Task<CommerceHubPaymentWebhookResultDto> ProcessWebhookAsync(
        string? provider,
        string? signature,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (providerCode, providerValue) = NormalizeProvider(provider);

        return providerValue switch
        {
            PaymentProvider.Paymob => ProcessPaymobWebhookAsync(signature, rawPayload, correlationId, cancellationToken),
            PaymentProvider.Stripe => ProcessStripeWebhookAsync(signature, rawPayload, correlationId, cancellationToken),
            _ => throw new CommerceHubCheckoutException(
                "PAYMENT_PROVIDER_INVALID",
                $"Unsupported payment provider '{providerCode}'.",
                StatusCodes.Status400BadRequest)
        };
    }

    public async Task<CommerceHubPaymentStatusDto> GetPaymentStatusAsync(
        string? provider,
        Guid orderId,
        Guid? authenticatedCustomerId,
        string? orderAccessToken,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (providerCode, providerValue) = NormalizeProvider(provider);
        var marketplaceOrder = await _dbContext.CommerceHubMarketplaceOrders
            .Include(order => order.Payments)
            .FirstOrDefaultAsync(order => order.Id == orderId && !order.IsDeleted, cancellationToken);

        if (marketplaceOrder == null)
        {
            throw new CommerceHubCheckoutException(
                "ORDER_NOT_FOUND",
                "Marketplace order was not found.",
                StatusCodes.Status404NotFound);
        }

        EnsureInitiationAccessAllowed(marketplaceOrder, authenticatedCustomerId, orderAccessToken);

        var payment = marketplaceOrder.Payments
            .Where(item => item.Provider == providerValue && !item.IsDeleted)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefault();

        if (payment == null)
        {
            return new CommerceHubPaymentStatusDto
            {
                OrderId = marketplaceOrder.Id,
                OrderNumber = marketplaceOrder.OrderNumber,
                Provider = providerCode,
                PaymentStatus = "NotStarted",
                ProviderStatus = "none",
                IsPaid = false,
                IsPending = false,
                RequiresAction = false
            };
        }

        return new CommerceHubPaymentStatusDto
        {
            OrderId = marketplaceOrder.Id,
            OrderNumber = marketplaceOrder.OrderNumber,
            Provider = providerCode,
            PaymentStatus = marketplaceOrder.PaymentStatus.ToString(),
            ProviderStatus = payment.Status.ToString().ToLowerInvariant(),
            IsPaid = CanonicalPaymentStatusResolver.IsPaidState(marketplaceOrder.PaymentStatus),
            IsPending = payment.Status == CommerceHubPaymentRecordStatus.Pending,
            RequiresAction = payment.Status == CommerceHubPaymentRecordStatus.Pending,
            FailureReason = payment.FailureReason,
            TransactionId = payment.ProviderTransactionId
        };
    }

    public async Task<CommerceHubPaymentConfigurationStatusDto> GetConfigurationStatusAsync(
        string? provider,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var (providerCode, _) = NormalizeProvider(provider);
        var paymentMethod = await _dbContext.TenantPaymentMethods
            .FirstOrDefaultAsync(method =>
                method.TenantId == tenantId &&
                method.Code == providerCode,
                cancellationToken);

        if (paymentMethod == null)
        {
            return new CommerceHubPaymentConfigurationStatusDto
            {
                Provider = providerCode,
                IsEnabled = false,
                IsConfigured = false,
                IsSandbox = true,
                ValidationMessage = $"{providerCode} payment method not found"
            };
        }

        var adapterConfigured = IsProviderGloballyConfigured(providerCode);
        return new CommerceHubPaymentConfigurationStatusDto
        {
            Provider = providerCode,
            IsEnabled = paymentMethod.IsEnabled && adapterConfigured,
            IsConfigured = adapterConfigured,
            IsSandbox = paymentMethod.IsSandbox,
            ValidationMessage = adapterConfigured ? null : $"{providerCode} provider adapter is not configured"
        };
    }

    public async Task<CommerceHubPaymentCallbackResultDto> ValidateCallbackAsync(
        string? provider,
        IReadOnlyDictionary<string, string> callbackParams,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (providerCode, providerValue) = NormalizeProvider(provider);
        if (providerValue != PaymentProvider.Paymob)
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_PROVIDER_INVALID",
                "Only Paymob callbacks are supported.",
                StatusCodes.Status400BadRequest);
        }

        var callback = await GetProviderAdapter(providerCode).ParseCallbackAsync(
            new PaymentProviderCallbackRequest(callbackParams, correlationId),
            cancellationToken);

        if (!callback.IsValid)
        {
            return new CommerceHubPaymentCallbackResultDto
            {
                IsValid = false,
                IsSuccess = false,
                TransactionId = callback.TransactionId,
                Message = callback.Message ?? "Invalid payment callback."
            };
        }

        var payment = await ResolveProviderCallbackCommerceHubPaymentAsync(
            providerValue,
            callback.ProviderReference,
            callback.ProviderIntentId,
            callback.TransactionId,
            cancellationToken);
        if (payment != null)
        {
            return new CommerceHubPaymentCallbackResultDto
            {
                IsValid = true,
                IsSuccess = callback.IsSuccess,
                OrderId = payment.OrderId,
                PaymentId = payment.Id,
                TransactionId = callback.TransactionId,
                Message = callback.Message ?? "Payment callback verified."
            };
        }

        return new CommerceHubPaymentCallbackResultDto
        {
            IsValid = false,
            IsSuccess = false,
            TransactionId = callback.TransactionId,
            Message = "CommerceHub payment callback context was not found."
        };
    }

    private bool IsProviderGloballyConfigured(string providerCode)
    {
        if (string.Equals(providerCode, CommerceHubPaymentMethods.Stripe, StringComparison.OrdinalIgnoreCase))
        {
            return _paymentsOptions.Stripe.Enabled &&
                   !string.IsNullOrWhiteSpace(_paymentsOptions.Stripe.SecretKey) &&
                   !string.IsNullOrWhiteSpace(_paymentsOptions.Stripe.PublishableKey);
        }

        if (string.Equals(providerCode, CommerceHubPaymentMethods.Paymob, StringComparison.OrdinalIgnoreCase))
        {
            if (!_providerAdapters.TryGetValue(new ProviderCode(providerCode).Normalized, out var adapter) || adapter is null)
            {
                return false;
            }

            if (adapter.Capabilities is not { } capabilities)
            {
                return false;
            }

            return capabilities.SupportsPaymentSessionCreation &&
                   capabilities.SupportsWebhookVerification &&
                   capabilities.SupportsCallbackParsing;
        }

        return false;
    }

    private async Task<CommerceHubPayment?> ResolveProviderCallbackCommerceHubPaymentAsync(
        PaymentProvider provider,
        string? providerReference,
        string? providerIntentId,
        string? transactionId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubPayments
            .Include(payment => payment.Order)
            .Where(payment =>
                payment.Provider == provider &&
                !payment.IsDeleted &&
                ((!string.IsNullOrWhiteSpace(transactionId) && payment.ProviderTransactionId == transactionId) ||
                 (!string.IsNullOrWhiteSpace(providerIntentId) && payment.ProviderIntentId == providerIntentId) ||
                 (!string.IsNullOrWhiteSpace(providerReference) && payment.ProviderReference == providerReference)))
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<CommerceHubPaymentWebhookResultDto> ProcessPaymobWebhookAsync(
        string? signature,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken)
    {
        return ProcessPaymobWebhookCoreAsync(signature, rawPayload, correlationId, cancellationToken);
    }

    private Task<CommerceHubPaymentWebhookResultDto> ProcessStripeWebhookAsync(
        string? signature,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken)
    {
        return ProcessStripeWebhookCoreAsync(signature, rawPayload, correlationId, cancellationToken);
    }

    private async Task<CommerceHubPaymentWebhookResultDto> ProcessPaymobWebhookCoreAsync(
        string? signature,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var callback = await GetProviderAdapter(CommerceHubPaymentMethods.Paymob).ParseWebhookAsync(
                new PaymentProviderWebhookRequest(signature ?? string.Empty, rawPayload, correlationId),
                cancellationToken);

            return await ProcessVerifiedPaymobWebhookAsync(callback, rawPayload, correlationId, cancellationToken);
        }
        catch (PaymentProviderAdapterException ex) when (ex.Error.Type == CanonicalProviderErrorType.Configuration)
        {
            _logger.LogWarning(
                ex,
                "Paymob webhook received but provider is not configured | Code: {Code} | CorrelationId: {CorrelationId}",
                ex.Error.Code,
                correlationId);

            return new CommerceHubPaymentWebhookResultDto
            {
                Processed = false,
                Provider = CommerceHubPaymentMethods.Paymob,
                Message = "CommerceHub Paymob webhook is not configured. Retry until configuration is restored.",
                ShouldRetry = true
            };
        }
        catch (PaymentProviderAdapterException ex) when (ex.Error.Code.Contains("HMAC", StringComparison.OrdinalIgnoreCase) ||
                                                         ex.Error.Code.Contains("SIGNATURE", StringComparison.OrdinalIgnoreCase))
        {
            throw new CommerceHubCheckoutException(
                "PAYMOB_INVALID_SIGNATURE",
                ex.Error.Message,
                StatusCodes.Status401Unauthorized,
                ex.Error.Code);
        }
        catch (PaymentProviderAdapterException ex)
        {
            throw new CommerceHubCheckoutException(
                "PAYMOB_PAYLOAD_INVALID",
                ex.Error.Message,
                StatusCodes.Status400BadRequest,
                ex.Error.Code);
        }
    }

    private async Task<CommerceHubPaymentWebhookResultDto> ProcessVerifiedPaymobWebhookAsync(
        PaymentProviderWebhookResult callback,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var durablePayload = _paymobArtifactProtector?.ProtectWebhookPayload(rawPayload) ??
            WebhookDataRedactor.RedactPayload(rawPayload);
        var providerReference = callback.ProviderReference?.Trim();
        var providerIntentId = callback.ProviderIntentId;
        var transactionId = callback.TransactionId;
        var providerEventId = callback.IsRefunded
            ? $"{transactionId}:refund:{callback.AmountMinorUnits}"
            : transactionId;

        var payment = await ResolvePaymentAsync(
            PaymentProvider.Paymob,
            providerReference,
            providerIntentId,
            paymentId: null,
            cancellationToken,
            providerTransactionId: transactionId);

        if (payment == null && !string.IsNullOrWhiteSpace(providerReference))
        {
            // S38C: Wrap legacy payment creation in payment mutation scope
            // CreateLegacyPaymobPaymentAsync persists CommerceHubPayment via SaveChangesAsync
            using var legacyPaymentBoundary = CommerceHubBoundaryMutationScope.BeginPayment();
            payment = await CreateLegacyPaymobPaymentAsync(
                providerReference,
                providerIntentId,
                cancellationToken);
        }

        if (payment == null)
        {
            var unresolvedCurrency = RequireCommerceHubPaymentCurrency(callback.Currency, "unresolved Paymob webhook");
            var unresolvedAmount = ConvertFromMinorUnits(callback.AmountMinorUnits, unresolvedCurrency);
            var unresolvedSuccess = callback.Success &&
                !callback.AuthorizationOnly &&
                !callback.IsVoided &&
                !callback.IsRefunded;
            var unresolvedRefund = callback.IsRefunded;

            var durableUnresolvedEvidenceStored = false;
            if (unresolvedSuccess)
            {
                durableUnresolvedEvidenceStored = await PersistUnresolvedPaymentWebhookAsync(
                    PaymentProvider.Paymob,
                    CommerceHubPaymentMethods.Paymob,
                    callback.EventType,
                    transactionId,
                    providerReference,
                    null,
                    providerIntentId,
                    providerEventId,
                    unresolvedAmount,
                    unresolvedCurrency,
                    rawPayload,
                    correlationId,
                    cancellationToken);
            }
            else if (unresolvedRefund)
            {
                durableUnresolvedEvidenceStored = await PersistUnresolvedPaymentWebhookAsync(
                    PaymentProvider.Paymob,
                    CommerceHubPaymentMethods.Paymob,
                    callback.EventType,
                    transactionId,
                    providerReference,
                    null,
                    providerIntentId,
                    providerEventId,
                    unresolvedAmount,
                    unresolvedCurrency,
                    rawPayload,
                    correlationId,
                    cancellationToken,
                    requiresSuspenseRouting: false,
                    unresolvedStatus: "ProviderRefundPendingReconciliation",
                    unresolvedLastError: "Provider refund webhook could not be linked to a CommerceHub payment row.");
            }

            return new CommerceHubPaymentWebhookResultDto
            {
                Processed = false,
                Provider = CommerceHubPaymentMethods.Paymob,
                ProviderReference = providerReference,
                TransactionId = transactionId,
                Message = unresolvedSuccess
                    ? "Marketplace payment was not found for this Paymob webhook. Captured payment evidence was persisted for suspense/reconciliation."
                    : unresolvedRefund
                        ? "Marketplace payment was not found for this Paymob refund webhook. Provider refund evidence was persisted for reconciliation."
                    : "Marketplace payment was not found for this Paymob webhook.",
                ShouldRetry = (unresolvedSuccess || unresolvedRefund) && !durableUnresolvedEvidenceStored
            };
        }

        if (callback.Pending)
        {
            _dbContext.CommerceHubPaymentLogs.Add(new CommerceHubPaymentLog
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                EventType = callback.EventType,
                ProviderEventId = providerEventId,
                RawResponse = durablePayload
            });

            await _dbContext.SaveChangesAsync(cancellationToken);

            return BuildWebhookResult(
                payment,
                CommerceHubPaymentMethods.Paymob,
                processed: false,
                message: "Paymob transaction is still pending.",
                transactionId: transactionId,
                shouldRetry: false); // Don't retry pending transactions
        }

        var needsManualCurrencyReview = false;
        if (string.IsNullOrWhiteSpace(callback.Currency))
        {
            _logger.LogCritical(
                "Webhook missing currency field — manual review required | PaymentId: {PaymentId}",
                payment.Id);
            needsManualCurrencyReview = true;
        }

        var currency = NormalizeCurrency(callback.Currency, payment.Currency);
        var amount = ConvertFromMinorUnits(callback.AmountMinorUnits, currency);
        EnsureAmountMatches(payment, amount, currency, CommerceHubPaymentMethods.Paymob);

        if (callback.IsRefunded)
        {
            return await ApplyProviderRefundWebhookAsync(
                payment,
                providerCode: CommerceHubPaymentMethods.Paymob,
                providerAmount: amount,
                providerCurrency: currency,
                providerIntentId: providerIntentId,
                transactionId: transactionId,
                refundReferenceId: $"paymob-refund:{transactionId}",
                eventType: callback.EventType,
                providerEventId: providerEventId,
                rawPayload: durablePayload,
                reason: "Paymob reported that an already-captured CommerceHub payment was refunded by the provider.",
                needsManualCurrencyReview: needsManualCurrencyReview,
                providerAmountIsCumulativeRefunded: true,
                correlationId: correlationId,
                cancellationToken: cancellationToken);
        }

        if (callback.AuthorizationOnly)
        {
            _dbContext.CommerceHubPaymentLogs.Add(new CommerceHubPaymentLog
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                EventType = "payment_authorized",
                ProviderEventId = providerEventId,
                RawResponse = durablePayload
            });
            await _dbContext.SaveChangesAsync(cancellationToken);

            return BuildWebhookResult(
                payment,
                CommerceHubPaymentMethods.Paymob,
                processed: true,
                message: "Paymob authorized the transaction; capture is still required.",
                transactionId: transactionId,
                shouldRetry: false);
        }

        var isSuccess = callback.Success && !callback.IsVoided;

        return await ApplyWebhookOutcomeAsync(
            payment,
            providerCode: CommerceHubPaymentMethods.Paymob,
            isSuccess: isSuccess,
            providerAmount: amount,
            providerCurrency: currency,
            providerIntentId: providerIntentId,
            transactionId: transactionId,
            eventType: callback.EventType,
            providerEventId: providerEventId,
            rawPayload: durablePayload,
            failureReason: callback.IsRefunded
                ? "Paymob reported the transaction as refunded."
                : callback.IsVoided
                    ? "Paymob reported the transaction as voided."
                    : callback.FailureReason ?? "Paymob reported the transaction as failed.",
            needsManualCurrencyReview: needsManualCurrencyReview,
            correlationId: correlationId,
            cancellationToken: cancellationToken);
    }

    private async Task<CommerceHubPaymentWebhookResultDto> ProcessStripeWebhookCoreAsync(
        string? signature,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var callback = await GetProviderAdapter(CommerceHubPaymentMethods.Stripe).ParseWebhookAsync(
                new PaymentProviderWebhookRequest(signature ?? string.Empty, rawPayload, correlationId),
                cancellationToken);

            return await ProcessVerifiedStripeWebhookAsync(callback, rawPayload, correlationId, cancellationToken);
        }
        catch (PaymentProviderAdapterException ex) when (ex.Error.Type == CanonicalProviderErrorType.Configuration)
        {
            _logger.LogWarning(
                ex,
                "Stripe webhook received but provider is not configured | Code: {Code} | CorrelationId: {CorrelationId}",
                ex.Error.Code,
                correlationId);

            return new CommerceHubPaymentWebhookResultDto
            {
                Processed = false,
                Provider = CommerceHubPaymentMethods.Stripe,
                Message = "CommerceHub Stripe webhook is not configured. Retry until configuration is restored.",
                ShouldRetry = true
            };
        }
        catch (PaymentProviderAdapterException ex) when (ex.Error.Code.Contains("SIGNATURE", StringComparison.OrdinalIgnoreCase))
        {
            throw new CommerceHubCheckoutException(
                "STRIPE_INVALID_SIGNATURE",
                ex.Error.Message,
                StatusCodes.Status401Unauthorized,
                ex.Error.Code);
        }
        catch (PaymentProviderAdapterException ex)
        {
            throw new CommerceHubCheckoutException(
                "STRIPE_PAYLOAD_INVALID",
                ex.Error.Message,
                StatusCodes.Status400BadRequest,
                ex.Error.Code);
        }
    }

    private async Task<CommerceHubPaymentWebhookResultDto> ProcessVerifiedStripeWebhookAsync(
        PaymentProviderWebhookResult callback,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var payment = await ResolvePaymentAsync(
            PaymentProvider.Stripe,
            callback.ProviderReference,
            callback.ProviderIntentId,
            paymentId: null,
            cancellationToken,
            providerTransactionId: callback.TransactionId);

        if (payment == null && !string.IsNullOrWhiteSpace(callback.TransactionId))
        {
            payment = await ResolvePaymentByProviderTransactionAsync(
                PaymentProvider.Stripe,
                callback.TransactionId,
                cancellationToken);
        }

        if (payment == null)
        {
            var durableEvidenceStored = false;
            var isCaptured = string.Equals(callback.EventType, "payment_intent.succeeded", StringComparison.Ordinal);
            if (isCaptured || callback.IsRefunded)
            {
                var unresolvedCurrency = RequireCommerceHubPaymentCurrency(callback.Currency, "unresolved Stripe webhook");
                var unresolvedAmount = ConvertFromMinorUnits(callback.AmountMinorUnits, unresolvedCurrency);
                durableEvidenceStored = await PersistUnresolvedPaymentWebhookAsync(
                    PaymentProvider.Stripe,
                    CommerceHubPaymentMethods.Stripe,
                    callback.EventType,
                    callback.TransactionId,
                    callback.ProviderReference,
                    null,
                    callback.ProviderIntentId,
                    callback.TransactionId,
                    unresolvedAmount,
                    unresolvedCurrency,
                    rawPayload,
                    correlationId,
                    cancellationToken,
                    requiresSuspenseRouting: !callback.IsRefunded,
                    unresolvedStatus: callback.IsRefunded ? "ProviderRefundPendingReconciliation" : null,
                    unresolvedLastError: callback.IsRefunded
                        ? "Provider refund webhook could not be linked to a CommerceHub payment row."
                        : null);
            }

            return new CommerceHubPaymentWebhookResultDto
            {
                Processed = false,
                Provider = CommerceHubPaymentMethods.Stripe,
                ProviderReference = callback.ProviderReference,
                TransactionId = callback.TransactionId,
                Message = isCaptured
                    ? "Marketplace payment was not found for this Stripe webhook. Captured payment evidence was persisted for suspense/reconciliation."
                    : callback.IsRefunded
                        ? "Marketplace payment was not found for this Stripe provider refund webhook. Provider refund evidence was persisted for reconciliation."
                        : "Marketplace payment was not found for this Stripe webhook.",
                ShouldRetry = (isCaptured || callback.IsRefunded) && !durableEvidenceStored
            };
        }

        var currency = NormalizeCurrency(callback.Currency, payment.Currency);
        var amount = ConvertFromMinorUnits(callback.AmountMinorUnits, currency);
        EnsureAmountMatches(payment, amount, currency, CommerceHubPaymentMethods.Stripe);

        if (callback.IsRefunded)
        {
            return await ApplyProviderRefundWebhookAsync(
                payment,
                providerCode: CommerceHubPaymentMethods.Stripe,
                providerAmount: amount,
                providerCurrency: currency,
                providerIntentId: callback.ProviderIntentId,
                transactionId: callback.TransactionId,
                refundReferenceId: $"stripe-refund:{callback.TransactionId}:{callback.EventType}",
                eventType: callback.EventType,
                providerEventId: callback.TransactionId,
                rawPayload: rawPayload,
                reason: "Stripe reported that an already-captured CommerceHub payment was refunded by the provider.",
                needsManualCurrencyReview: false,
                providerAmountIsCumulativeRefunded: false,
                correlationId: correlationId,
                cancellationToken: cancellationToken);
        }

        if (string.Equals(callback.EventType, "payment_intent.succeeded", StringComparison.Ordinal))
        {
            return await ApplyWebhookOutcomeAsync(
                payment,
                CommerceHubPaymentMethods.Stripe,
                isSuccess: true,
                providerAmount: amount,
                providerCurrency: currency,
                providerIntentId: callback.ProviderIntentId,
                transactionId: callback.TransactionId,
                eventType: callback.EventType,
                providerEventId: callback.TransactionId,
                rawPayload: rawPayload,
                failureReason: null,
                needsManualCurrencyReview: false,
                correlationId: correlationId,
                cancellationToken: cancellationToken);
        }

        if (string.Equals(callback.EventType, "payment_intent.payment_failed", StringComparison.Ordinal) ||
            string.Equals(callback.EventType, "payment_intent.canceled", StringComparison.Ordinal))
        {
            return await ApplyWebhookOutcomeAsync(
                payment,
                CommerceHubPaymentMethods.Stripe,
                isSuccess: false,
                providerAmount: amount,
                providerCurrency: currency,
                providerIntentId: callback.ProviderIntentId,
                transactionId: callback.TransactionId,
                eventType: callback.EventType,
                providerEventId: callback.TransactionId,
                rawPayload: rawPayload,
                failureReason: callback.FailureReason ?? $"Stripe reported payment event '{callback.EventType}'.",
                needsManualCurrencyReview: false,
                correlationId: correlationId,
                cancellationToken: cancellationToken);
        }

        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginPayment();
        _dbContext.CommerceHubPaymentLogs.Add(new CommerceHubPaymentLog
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            EventType = callback.EventType,
            ProviderEventId = callback.TransactionId,
            RawResponse = rawPayload
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return BuildWebhookResult(
            payment,
            CommerceHubPaymentMethods.Stripe,
            processed: false,
            message: $"Stripe event '{callback.EventType}' does not change CommerceHub payment state.",
            transactionId: callback.TransactionId,
            shouldRetry: false);
    }
    private async Task<CommerceHubPaymentWebhookResultDto> ApplyProviderRefundWebhookAsync(
        CommerceHubPayment payment,
        string providerCode,
        decimal providerAmount,
        string providerCurrency,
        string? providerIntentId,
        string? transactionId,
        string refundReferenceId,
        string eventType,
        string? providerEventId,
        string rawPayload,
        string reason,
        bool needsManualCurrencyReview,
        bool providerAmountIsCumulativeRefunded,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginPayment();
        using var refundBoundary = CommerceHubBoundaryMutationScope.BeginRefund();
        var paymentReferenceId = refundReferenceId;
        var paymentLockKey = BuildPaymentProcessingLockKey(payment.Id);
        var paymentLockToken = Guid.NewGuid().ToString("N");
        var paymentLockResult = await _lockService.TryAcquireLockAsync(
            paymentLockKey,
            paymentLockToken,
            TimeSpan.FromMinutes(2));

        if (paymentLockResult.IsInfrastructureFailure)
        {
            _logger.LogError(
                "CommerceHub refund webhook lock acquisition failed due to infrastructure failure | Provider: {Provider} | PaymentId: {PaymentId} | CorrelationId: {CorrelationId} | Metric: CommerceHub.RefundWebhook.InfrastructureFailure",
                providerCode,
                payment.Id,
                correlationId);
            return BuildWebhookResult(
                payment,
                providerCode,
                processed: false,
                message: "Payment refund reconciliation is temporarily unavailable because the lock infrastructure could not be reached.",
                transactionId: paymentReferenceId,
                shouldRetry: true);
        }

        if (!paymentLockResult.IsAcquired)
        {
            _logger.LogWarning(
                "CommerceHub refund webhook could not acquire payment lock (already held) | Provider: {Provider} | PaymentId: {PaymentId} | CorrelationId: {CorrelationId} | Metric: CommerceHub.RefundWebhook.LockAlreadyHeld",
                providerCode,
                payment.Id,
                correlationId);
            return BuildWebhookResult(
                payment,
                providerCode,
                processed: false,
                message: "Payment is already being processed by another worker.",
                transactionId: paymentReferenceId,
                shouldRetry: true);
        }

        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            _dbContext.ChangeTracker.Clear();

            var processedPayment = await _dbContext.CommerceHubPayments
                .Include(item => item.Order)
                .ThenInclude(order => order.SubOrders)
                .ThenInclude(subOrder => subOrder.Items)
                .Include(item => item.Items)
                .Include(item => item.Attempts)
                .Include(item => item.Refunds)
                .FirstOrDefaultAsync(
                    item => item.Id == payment.Id &&
                            !item.IsDeleted &&
                            !item.Order.IsDeleted,
                    cancellationToken);

            if (processedPayment == null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new CommerceHubPaymentWebhookResultDto
                {
                    Processed = false,
                    Provider = providerCode,
                    ProviderReference = payment.ProviderReference,
                    TransactionId = paymentReferenceId,
                    Message = "Marketplace payment was not found while applying the provider refund webhook.",
                    ShouldRetry = true
                };
            }

            if (!string.Equals(processedPayment.Currency, providerCurrency, StringComparison.OrdinalIgnoreCase))
            {
                throw new CommerceHubCheckoutException(
                    "PAYMENT_REFUND_CURRENCY_MISMATCH",
                    $"{providerCode} refund webhook currency does not match the marketplace payment record.",
                    StatusCodes.Status409Conflict);
            }

            if (providerAmount <= 0m)
            {
                throw new CommerceHubCheckoutException(
                    "PAYMENT_REFUND_AMOUNT_INVALID",
                    $"{providerCode} refund webhook amount must be greater than zero.",
                    StatusCodes.Status409Conflict);
            }

            if (processedPayment.Status is not (CommerceHubPaymentRecordStatus.Paid or
                CommerceHubPaymentRecordStatus.RefundRequired or
                CommerceHubPaymentRecordStatus.Refunded))
            {
                _dbContext.CommerceHubPaymentLogs.Add(new CommerceHubPaymentLog
                {
                    Id = Guid.NewGuid(),
                    PaymentId = processedPayment.Id,
                    EventType = "provider_evidence_pending_capture",
                    ProviderEventId = providerEventId,
                    RawResponse = rawPayload
                });
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return BuildWebhookResult(
                    processedPayment,
                    providerCode,
                    processed: false,
                    message: "Provider refund evidence arrived before captured payment authority and remains pending reconciliation.",
                    transactionId: paymentReferenceId,
                    shouldRetry: true);
            }

            EnsurePaymentMatchesMarketplaceOrder(processedPayment, processedPayment.Order);
            await EnsureProviderIdentityNotLinkedToAnotherPaymentAsync(
                processedPayment.Provider,
                providerIntentId,
                transactionId,
                processedPayment.Id,
                cancellationToken);

            var nowUtc = DateTime.UtcNow;
            var refundAmount = providerAmountIsCumulativeRefunded
                ? Math.Round(providerAmount - processedPayment.RefundedAmount - processedPayment.ProvisionalRefundedAmount, 2, MidpointRounding.AwayFromZero)
                : Math.Round(providerAmount, 2, MidpointRounding.AwayFromZero);

            // DUPLICATE DETECTION: Check if this specific refund webhook has already been processed
            // by looking for an existing CommerceHubPaymentLog with the same PaymentId and ProviderEventId,
            // scoped to refund events only (to avoid treating capture webhooks as duplicates).
            // or by checking if the compensation outbox message already exists.
            // This prevents replayed webhooks from incrementing ProvisionalRefundedAmount multiple times.
            var isDuplicateWebhook = !string.IsNullOrEmpty(providerEventId) &&
                await _dbContext.CommerceHubPaymentLogs
                    .AnyAsync(
                        log => log.PaymentId == processedPayment.Id && 
                               log.ProviderEventId == providerEventId &&
                               EF.Functions.ILike(log.EventType, "%refund%"),
                        cancellationToken);

            if (!isDuplicateWebhook && refundAmount > 0m)
            {
                // Also check if the compensation outbox message already exists (more robust duplicate detection)
                var idempotencyKey = BuildPaymentProviderRefundCompensationIdempotencyKey(processedPayment, refundReferenceId, refundAmount);
                isDuplicateWebhook = await _dbContext.CommerceHubPaymentCompensationOutboxMessages
                    .IgnoreQueryFilters()
                    .AnyAsync(message => message.IdempotencyKey == idempotencyKey, cancellationToken);
            }

            _dbContext.CommerceHubPaymentLogs.Add(new CommerceHubPaymentLog
            {
                Id = Guid.NewGuid(),
                PaymentId = processedPayment.Id,
                EventType = eventType,
                ProviderEventId = providerEventId,
                RawResponse = rawPayload
            });

            if (isDuplicateWebhook)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "CommerceHub provider refund webhook is a duplicate | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | ProviderEventId: {ProviderEventId} | RefundReferenceId: {RefundReferenceId} | Amount: {Amount} {Currency} | CorrelationId: {CorrelationId}",
                    processedPayment.Id,
                    processedPayment.OrderId,
                    providerCode,
                    providerEventId,
                    refundReferenceId,
                    refundAmount,
                    providerCurrency,
                    correlationId);

                return BuildWebhookResult(
                    processedPayment,
                    providerCode,
                    processed: true,
                    message: "Provider refund webhook was already processed (duplicate).",
                    transactionId: paymentReferenceId,
                    shouldRetry: false);
            }

            if (refundAmount <= 0m)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return BuildWebhookResult(
                    processedPayment,
                    providerCode,
                    processed: true,
                    message: "Provider refund webhook was already reconciled.",
                    transactionId: paymentReferenceId,
                    shouldRetry: false);
            }

            var remainingRefundable = Math.Round(
                processedPayment.Amount - processedPayment.RefundedAmount - processedPayment.ProvisionalRefundedAmount,
                2,
                MidpointRounding.AwayFromZero);
            if (refundAmount > remainingRefundable)
            {
                throw new CommerceHubCheckoutException(
                    "PAYMENT_REFUND_AMOUNT_EXCEEDS_REMAINING",
                    $"{providerCode} refund webhook would over-refund the marketplace payment record.",
                    StatusCodes.Status409Conflict);
            }

            // IMMUTABILITY GUARD: Check for missing payment items but DO NOT synthesize them.
            // Refund reconciliation must use immutable historical payment items, not live order state.
            var existingSubOrderIds = processedPayment.Items
                .Select(item => item.SubOrderId)
                .ToHashSet();

            var missingSubOrderIds = processedPayment.Order.SubOrders
                .Where(IsOnlineSubOrder)
                .Where(subOrder => !existingSubOrderIds.Contains(subOrder.Id))
                .Select(subOrder => subOrder.Id)
                .ToList();

            if (missingSubOrderIds.Any())
            {
                _logger.LogError(
                    "CommerceHub refund reconciliation lacks immutable payment-item authority | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | MissingSubOrderIds: {MissingSubOrderIds} | RefundReferenceId: {RefundReferenceId} | Amount: {Amount} {Currency} | CorrelationId: {CorrelationId}",
                    processedPayment.Id,
                    processedPayment.OrderId,
                    string.Join(", ", missingSubOrderIds),
                    refundReferenceId,
                    refundAmount,
                    providerCurrency,
                    correlationId);

                // Mark as needing manual review since we lack complete payment item history
                processedPayment.NeedsManualCurrencyReview = true;
            }

            var paymentAttempt = ResolveOrCreateWebhookAttempt(processedPayment);
            if (needsManualCurrencyReview)
            {
                processedPayment.NeedsManualCurrencyReview = true;
            }

            // Increment provisional refund amount to track this webhook-confirmed refund
            // before reconciliation merges it into RefundedAmount.
            // This is only reached for non-duplicate refunds after duplicate detection above.
            processedPayment.ProvisionalRefundedAmount = Math.Round(
                processedPayment.ProvisionalRefundedAmount + refundAmount,
                2,
                MidpointRounding.AwayFromZero);

            await MarkPaymentProviderRefundConfirmedAsync(
                processedPayment,
                paymentAttempt,
                providerIntentId,
                transactionId,
                refundReferenceId,
                refundAmount,
                reason,
                correlationId,
                nowUtc,
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogWarning(
                "CommerceHub provider refund queued for confirmed accounting reconciliation | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | RefundReferenceId: {RefundReferenceId} | Amount: {Amount} {Currency} | CorrelationId: {CorrelationId}",
                processedPayment.Id,
                processedPayment.OrderId,
                providerCode,
                refundReferenceId,
                refundAmount,
                providerCurrency,
                correlationId);

            return BuildWebhookResult(
                processedPayment,
                providerCode,
                processed: true,
                message: "Provider refund confirmed by gateway and queued for accounting reconciliation.",
                transactionId: paymentReferenceId,
                shouldRetry: false);
        }
        finally
        {
            await _lockService.ReleaseLockAsync(paymentLockKey, paymentLockToken);
        }
    }

    private async Task<CommerceHubPaymentWebhookResultDto> ApplyWebhookOutcomeAsync(
        CommerceHubPayment payment,
        string providerCode,
        bool isSuccess,
        decimal providerAmount,
        string providerCurrency,
        string? providerIntentId,
        string? transactionId,
        string eventType,
        string? providerEventId,
        string rawPayload,
        string? failureReason,
        bool needsManualCurrencyReview,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginPayment();
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();
        var paymentReferenceId = transactionId ?? providerIntentId ?? payment.ProviderReference;
        var paymentLockKey = BuildPaymentProcessingLockKey(payment.Id);
        var paymentLockToken = Guid.NewGuid().ToString("N");
        var paymentLockResult = await _lockService.TryAcquireLockAsync(
            paymentLockKey,
            paymentLockToken,
            TimeSpan.FromMinutes(2));

        if (paymentLockResult.IsInfrastructureFailure)
        {
            _logger.LogError(
                "CommerceHub payment webhook lock acquisition failed due to infrastructure failure | Provider: {Provider} | PaymentId: {PaymentId} | CorrelationId: {CorrelationId} | Metric: CommerceHub.PaymentWebhook.InfrastructureFailure",
                providerCode,
                payment.Id,
                correlationId);
            return BuildWebhookResult(
                payment,
                providerCode,
                processed: false,
                message: "Payment processing is temporarily unavailable because the lock infrastructure could not be reached.",
                transactionId: paymentReferenceId,
                shouldRetry: true);
        }

        if (!paymentLockResult.IsAcquired)
        {
            _logger.LogWarning(
                "CommerceHub payment webhook could not acquire payment lock (already held) | Provider: {Provider} | PaymentId: {PaymentId} | CorrelationId: {CorrelationId} | Metric: CommerceHub.PaymentWebhook.LockAlreadyHeld",
                providerCode,
                payment.Id,
                correlationId);
            return BuildWebhookResult(
                payment,
                providerCode,
                processed: false,
                message: "Payment is already being processed by another worker.",
                transactionId: paymentReferenceId,
                shouldRetry: true); // Retry when lock is unavailable
        }

        try
        {
        CommerceHubPayment? processedPayment = null;
        OrderStatus oldMarketplaceStatus = default;
        PaymentStatus oldMarketplacePaymentStatus = default;
        var oldSubOrderStates = new Dictionary<Guid, OrderStatus>();
        var oldSubOrderPaymentStates = new Dictionary<Guid, PaymentStatus>();
        string resultMessage;
        bool processedResult;
        var completionStarted = false;
        var completionStartedTimestamp = 0L;
        var finalizedReservationCount = 0;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            _dbContext.ChangeTracker.Clear();

            processedPayment = await LoadCommerceHubPaymentForProviderCallbackMutationAsync(
                payment.Id,
                cancellationToken);

            if (processedPayment == null)
            {
                await transaction.CommitAsync(cancellationToken);

                return new CommerceHubPaymentWebhookResultDto
                {
                    Processed = false,
                    Provider = providerCode,
                    ProviderReference = payment.ProviderReference,
                    TransactionId = paymentReferenceId,
                    Message = "Marketplace payment was not found while applying the webhook.",
                    ShouldRetry = true // Retry when payment not found during application
                };
            }

            var marketplaceOrder = await LoadMarketplaceOrderForProviderCallbackMutationAsync(
                processedPayment.OrderId,
                cancellationToken);
            if (marketplaceOrder == null)
            {
                await transaction.CommitAsync(cancellationToken);

                return new CommerceHubPaymentWebhookResultDto
                {
                    Processed = false,
                    Provider = providerCode,
                    ProviderReference = processedPayment.ProviderReference,
                    TransactionId = paymentReferenceId,
                    Message = "Marketplace order was not found while applying the webhook.",
                    ShouldRetry = true
                };
            }

            processedPayment.Order = marketplaceOrder;
            EnsurePaymentMatchesMarketplaceOrder(processedPayment, marketplaceOrder);
            EnsureAmountMatches(processedPayment, providerAmount, providerCurrency, providerCode);
            EnsureIncomingProviderIdentityMatchesPayment(processedPayment, providerIntentId, transactionId, isSuccess);
            await EnsureProviderIdentityNotLinkedToAnotherPaymentAsync(
                processedPayment.Provider,
                providerIntentId,
                transactionId,
                processedPayment.Id,
                cancellationToken);

            var nowUtc = DateTime.UtcNow;
            var paymentAttempt = ResolveOrCreateWebhookAttempt(processedPayment);
            if (needsManualCurrencyReview)
            {
                processedPayment.NeedsManualCurrencyReview = true;
            }

            _dbContext.CommerceHubPaymentLogs.Add(new CommerceHubPaymentLog
            {
                Id = Guid.NewGuid(),
                PaymentId = processedPayment.Id,
                EventType = eventType,
                ProviderEventId = providerEventId,
                RawResponse = rawPayload
            });

            var isReconcilingEarlierFailure =
                processedPayment.IsProcessed &&
                isSuccess &&
                CanReconcileProcessedFailureToSuccess(processedPayment, providerIntentId);

            if (processedPayment.IsProcessed && !isReconcilingEarlierFailure)
            {
                paymentAttempt.ProviderIntentId ??= providerIntentId;
                paymentAttempt.ProviderTransactionId ??= paymentReferenceId;
                paymentAttempt.Status = processedPayment.Status;
                paymentAttempt.FailureReason = processedPayment.FailureReason;
                paymentAttempt.CompletedAtUtc ??= nowUtc;

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                processedResult = isSuccess
                    ? processedPayment.Status == CommerceHubPaymentRecordStatus.Paid &&
                      MatchesExistingSuccess(processedPayment, transactionId, providerIntentId)
                    : processedPayment.Status == CommerceHubPaymentRecordStatus.Failed &&
                      MatchesExistingFailure(processedPayment, transactionId, providerIntentId);

                resultMessage = processedResult
                    ? "Webhook was already applied."
                    : "Payment webhook was ignored because this payment was already processed.";

                _logger.LogInformation(
                    "CommerceHub duplicate webhook ignored after processed guard | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | Success: {Success} | TransactionId: {TransactionId} | CorrelationId: {CorrelationId}",
                    processedPayment.Id,
                    marketplaceOrder.Id,
                    providerCode,
                    isSuccess,
                    paymentReferenceId,
                    correlationId);

                return BuildWebhookResult(
                    processedPayment,
                    providerCode,
                    processedResult,
                    resultMessage,
                    paymentReferenceId,
                    shouldRetry: false); // Don't retry already processed webhooks
            }

            if (isReconcilingEarlierFailure)
            {
                _logger.LogWarning(
                    "CommerceHub payment webhook reconciling earlier failed provider event to captured success | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | ProviderIntentId: {ProviderIntentId} | PreviousTransactionId: {PreviousTransactionId} | SuccessTransactionId: {SuccessTransactionId} | CorrelationId: {CorrelationId}",
                    processedPayment.Id,
                    marketplaceOrder.Id,
                    providerCode,
                    providerIntentId,
                    processedPayment.ProviderTransactionId,
                    paymentReferenceId,
                    correlationId);
            }

            if (CommerceHubOrderStateHelper.HasCollectedPayment(marketplaceOrder.PaymentStatus))
            {
                if (isSuccess)
                {
                    MarkPaymentRequiresCompensation(
                        processedPayment,
                        paymentAttempt,
                        providerIntentId,
                        paymentReferenceId,
                        "Marketplace order is already marked as paid.",
                        correlationId,
                        nowUtc);
                }
                else
                {
                    MarkPaymentIgnored(
                        processedPayment,
                        paymentAttempt,
                        providerIntentId,
                        paymentReferenceId,
                        "Marketplace order is already marked as paid.",
                        nowUtc);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogWarning(
                    "CommerceHub webhook ignored because marketplace order is already paid | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | TransactionId: {TransactionId} | CorrelationId: {CorrelationId}",
                    processedPayment.Id,
                    marketplaceOrder.Id,
                    providerCode,
                    paymentReferenceId,
                    correlationId);

                return BuildWebhookResult(
                    processedPayment,
                    providerCode,
                    processed: false,
                    message: isSuccess
                        ? "Marketplace order is already paid. Captured duplicate payment queued for refund compensation."
                        : "Marketplace order is already paid. Failed webhook ignored.",
                    transactionId: paymentReferenceId,
                    shouldRetry: false); // Don't retry when already paid
            }

            if (!CanApplyPaymentWebhook(marketplaceOrder))
            {
                var ignoredReason =
                    $"Marketplace order is in state {marketplaceOrder.Status} and can no longer accept a payment webhook.";

                if (isSuccess)
                {
                    MarkPaymentRequiresCompensation(
                        processedPayment,
                        paymentAttempt,
                        providerIntentId,
                        paymentReferenceId,
                        ignoredReason,
                        correlationId,
                        nowUtc);
                }
                else
                {
                    MarkPaymentIgnored(
                        processedPayment,
                        paymentAttempt,
                        providerIntentId,
                        paymentReferenceId,
                        ignoredReason,
                        nowUtc);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogWarning(
                    "CommerceHub webhook ignored due to order state guard | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | MarketplaceStatus: {MarketplaceStatus} | Provider: {Provider} | TransactionId: {TransactionId} | CorrelationId: {CorrelationId}",
                    processedPayment.Id,
                    marketplaceOrder.Id,
                    marketplaceOrder.Status,
                    providerCode,
                    paymentReferenceId,
                    correlationId);

                return BuildWebhookResult(
                    processedPayment,
                    providerCode,
                    processed: false,
                    message: isSuccess
                        ? $"{ignoredReason} Captured payment queued for refund compensation."
                        : ignoredReason,
                    transactionId: paymentReferenceId,
                    shouldRetry: false); // Don't retry when order state prevents processing
            }

            oldMarketplaceStatus = marketplaceOrder.Status;
            oldMarketplacePaymentStatus = marketplaceOrder.PaymentStatus;
            oldSubOrderStates = marketplaceOrder.SubOrders.ToDictionary(subOrder => subOrder.Id, subOrder => subOrder.Status);
            oldSubOrderPaymentStates = marketplaceOrder.SubOrders.ToDictionary(subOrder => subOrder.Id, subOrder => subOrder.PaymentStatus);

            processedPayment.ProviderIntentId ??= providerIntentId;
            processedPayment.ProviderTransactionId = paymentReferenceId;

            // When reconciling an earlier failure, create a new attempt record to preserve the historical failure
            if (isReconcilingEarlierFailure)
            {
                var nextAttemptNumber = processedPayment.Attempts.Count == 0
                    ? 1
                    : processedPayment.Attempts.Max(a => a.AttemptNumber) + 1;
                
                var reconciledAttempt = new CommerceHubPaymentAttempt
                {
                    Id = Guid.NewGuid(),
                    PaymentId = processedPayment.Id,
                    Provider = processedPayment.Provider,
                    AttemptNumber = nextAttemptNumber,
                    ProviderReference = processedPayment.ProviderReference,
                    ProviderIntentId = providerIntentId,
                    ProviderTransactionId = paymentReferenceId,
                    Status = CommerceHubPaymentRecordStatus.Paid,
                    FailureReason = null,
                    CompletedAtUtc = nowUtc,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                };

                _dbContext.CommerceHubPaymentAttempts.Add(reconciledAttempt);
                paymentAttempt = reconciledAttempt; // Use the new attempt for subsequent operations
            }
            else
            {
                paymentAttempt.ProviderIntentId ??= providerIntentId;
                paymentAttempt.ProviderTransactionId = paymentReferenceId;
                paymentAttempt.CompletedAtUtc = nowUtc;
            }

            if (isSuccess)
            {
                completionStarted = true;
                completionStartedTimestamp = Stopwatch.GetTimestamp();
                _telemetry.RecordPaymentCompletionStarted(providerCode);
                _logger.LogInformation(
                    "CommerceHub canonical payment completion started | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | TransactionId: {TransactionId} | CorrelationId: {CorrelationId} | Metric: PaymentCompletionStarted",
                    processedPayment.Id,
                    marketplaceOrder.Id,
                    providerCode,
                    paymentReferenceId,
                    correlationId);

                try
                {
                    await _inventoryConsistencyService.EnsureMarketplaceOrderReservationsActiveAsync(
                        marketplaceOrder.Id,
                        cancellationToken);
                }
                catch (CommerceHubCheckoutException ex)
                {
                    var compensationReason =
                        $"Marketplace order no longer has active reserved stock before payment recognition. {ex.Code}: {ex.Message}";

                    MarkPaymentRequiresCompensation(
                        processedPayment,
                        paymentAttempt,
                        providerIntentId,
                        paymentReferenceId,
                        compensationReason,
                        correlationId,
                        nowUtc);

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    _logger.LogCritical(
                        "CommerceHub provider success queued for refund because inventory authority was missing | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | TransactionId: {TransactionId} | Reason: {Reason} | CorrelationId: {CorrelationId}",
                        processedPayment.Id,
                        marketplaceOrder.Id,
                        providerCode,
                        paymentReferenceId,
                        compensationReason,
                        correlationId);

                    return BuildWebhookResult(
                        processedPayment,
                        providerCode,
                        processed: false,
                        message: "Payment provider reported success, but the order no longer has active reserved stock. Captured payment queued for refund compensation.",
                        transactionId: paymentReferenceId,
                        shouldRetry: false);
                }

                processedPayment.Status = CommerceHubPaymentRecordStatus.Paid;
                processedPayment.PaidAtUtc ??= nowUtc;
                processedPayment.FailureReason = null;
                processedPayment.IsProcessed = true;
                processedPayment.ProcessedAtUtc ??= nowUtc;

                paymentAttempt.Status = CommerceHubPaymentRecordStatus.Paid;
                paymentAttempt.FailureReason = null;

                _dbContext.CommerceHubPaymentLogs.Add(new CommerceHubPaymentLog
                {
                    Id = Guid.NewGuid(),
                    PaymentId = processedPayment.Id,
                    EventType = "CanonicalPaymentCompletionEvidence",
                    ProviderEventId = paymentReferenceId,
                    RawResponse = JsonSerializer.Serialize(new
                    {
                        providerCode,
                        providerIntentId,
                        providerTransactionId = paymentReferenceId,
                        providerAmount,
                        providerCurrency,
                        completedAtUtc = nowUtc,
                        correlationId
                    })
                });

                finalizedReservationCount = await _inventoryConsistencyService.FinalizeMarketplaceOrderReservationsForPaymentAsync(
                    marketplaceOrder.Id,
                    processedPayment.Id,
                    processedPayment.Id,
                    correlationId,
                    "CommerceHubCanonicalPaymentCompletion",
                    cancellationToken);
                _telemetry.RecordInventoryFinalized(providerCode, finalizedReservationCount);

                var onlineSubOrders = marketplaceOrder.SubOrders.Where(IsOnlineSubOrder).ToList();
                CommerceHubPaymentLifecycleAuthority.MarkOnlinePaymentCollected(
                    marketplaceOrder,
                    onlineSubOrders,
                    paymentReferenceId,
                    nowUtc);
                _telemetry.RecordOrderMarkedPaid(providerCode);

                await _financialWorkflowService.ApplyOnlinePaymentSuccessAsync(
                    marketplaceOrder,
                    processedPayment,
                    nowUtc,
                    cancellationToken);
                _telemetry.RecordVendorEarningsCreated(providerCode, onlineSubOrders.Count);
                _telemetry.RecordSettlementCreated(providerCode, onlineSubOrders.Count);

                resultMessage = "Marketplace payment collected successfully.";
            }
            else
            {
                var normalizedFailureReason = string.IsNullOrWhiteSpace(failureReason)
                    ? "Payment provider reported a failed transaction."
                    : failureReason.Trim();

                processedPayment.Status = CommerceHubPaymentRecordStatus.Failed;
                processedPayment.PaidAtUtc = null;
                processedPayment.FailureReason = normalizedFailureReason;
                processedPayment.IsProcessed = true;
                processedPayment.ProcessedAtUtc ??= nowUtc;

                paymentAttempt.Status = CommerceHubPaymentRecordStatus.Failed;
                paymentAttempt.FailureReason = normalizedFailureReason;

                marketplaceOrder.Status = MoveOrderToPaymentRetryHold(marketplaceOrder.Status);

                var onlineSubOrders = marketplaceOrder.SubOrders.Where(IsOnlineSubOrder).ToList();
                foreach (var subOrder in onlineSubOrders)
                {
                    subOrder.Status = MoveOrderToPaymentRetryHold(subOrder.Status);
                }

                CommerceHubPaymentLifecycleAuthority.ResetOnlinePaymentToPendingForRetry(
                    marketplaceOrder,
                    onlineSubOrders);

                await _inventoryConsistencyService.ReleaseMarketplaceOrderReservationsAsync(
                    marketplaceOrder.Id,
                    processedPayment.Id,
                    correlationId,
                    "CommerceHubPaymentFailedWebhook",
                    cancellationToken);

                marketplaceOrder.Status = CommerceHubOrderStateHelper.AggregateMarketplaceStatus(
                    marketplaceOrder.SubOrders,
                    marketplaceOrder.Status);

                resultMessage = "Marketplace payment failed. Reserved stock was released immediately; the customer must restart checkout before retrying payment.";
            }

            // Publish events BEFORE SaveChanges so they are included in the same transaction (transactional outbox pattern)
            var marketplaceStatusChanged = oldMarketplaceStatus != marketplaceOrder.Status ||
                oldMarketplacePaymentStatus != marketplaceOrder.PaymentStatus;
            var changedSubOrders = marketplaceOrder.SubOrders
                .Where(subOrder =>
                {
                    var oldStatus = oldSubOrderStates.GetValueOrDefault(subOrder.Id);
                    var oldPaymentStatus = oldSubOrderPaymentStates.GetValueOrDefault(subOrder.Id);
                    return oldStatus != subOrder.Status || oldPaymentStatus != subOrder.PaymentStatus;
                })
                .ToList();

            if (marketplaceStatusChanged || changedSubOrders.Count > 0)
            {
                marketplaceOrder.TouchProjectionMutationVersion();
            }

            foreach (var subOrder in changedSubOrders)
            {
                subOrder.TouchProjectionMutationVersion();
            }

            if (isSuccess)
            {
                await WriteCanonicalPaymentCompletionOutboxEventsAsync(
                    marketplaceOrder,
                    processedPayment,
                    providerCode,
                    paymentReferenceId,
                    finalizedReservationCount,
                    correlationId,
                    cancellationToken);
            }

            if (marketplaceStatusChanged)
            {
                var affectedStoreIds = marketplaceOrder.GetAffectedStoreIds()
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();

                if (affectedStoreIds.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Invariant violation: CommerceHubMarketplaceOrderStatusUpdatedEvent could not be published via _eventPublisher.PublishAsync for marketplaceOrder {marketplaceOrder.Id} because affectedStoreIds was empty. Missing AffectedStoreIds prevents authoritative status propagation and must roll back.");
                }

                var eventVersion = CommerceHubOrderEventMetadata.ResolveAggregateEventVersion(marketplaceOrder);

                await _eventPublisher.PublishAsync(new CommerceHubMarketplaceOrderStatusUpdatedEvent
                {
                    MarketplaceOrderId = marketplaceOrder.Id,
                    TenantId = affectedStoreIds.First(),
                    AffectedStoreIds = affectedStoreIds,
                    CorrelationId = correlationId,
                    IdempotencyKey = CommerceHubOrderEventMetadata.BuildMarketplacePaymentIdempotencyKey(
                        marketplaceOrder,
                        eventVersion),
                    OccurredAtUtc = DateTime.UtcNow,
                    EventVersion = eventVersion
                }, cancellationToken);
            }

            var subOrderEventVersion = CommerceHubOrderEventMetadata.ResolveAggregateEventVersion(marketplaceOrder);
            foreach (var subOrder in changedSubOrders)
            {
                if (subOrder.StoreId == Guid.Empty)
                {
                    throw new InvalidOperationException(
                        $"Cannot publish CommerceHub payment sub-order status event for marketplace order {marketplaceOrder.Id}: sub-order {subOrder.Id} has empty StoreId.");
                }

                await _eventPublisher.PublishAsync(new CommerceHubSubOrderStatusUpdatedEvent
                {
                    MarketplaceOrderId = marketplaceOrder.Id,
                    SubOrderId = subOrder.Id,
                    StoreId = subOrder.StoreId,
                    TenantId = subOrder.StoreId,
                    CorrelationId = correlationId,
                    IdempotencyKey = CommerceHubOrderEventMetadata.BuildSubOrderPaymentIdempotencyKey(
                        subOrder,
                        subOrderEventVersion),
                    OccurredAtUtc = DateTime.UtcNow,
                    EventVersion = subOrderEventVersion
                }, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            processedResult = true;
            if (completionStarted)
            {
                var elapsedMs = Stopwatch.GetElapsedTime(completionStartedTimestamp).TotalMilliseconds;
                _telemetry.RecordPaymentCompletionSucceeded(providerCode, elapsedMs);
                _logger.LogInformation(
                    "CommerceHub canonical payment completion succeeded | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | TransactionId: {TransactionId} | DurationMs: {DurationMs} | CorrelationId: {CorrelationId} | Metric: PaymentCompletionSucceeded",
                    processedPayment.Id,
                    marketplaceOrder.Id,
                    providerCode,
                    paymentReferenceId,
                    elapsedMs,
                    correlationId);
            }
        }
        catch (DbUpdateException ex) when (IsProviderPaymentIdentityUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CommerceHubCheckoutException(
                "PAYMENT_PROVIDER_IDENTITY_CONFLICT",
                "This provider payment identifier is already linked to another CommerceHub payment.",
                StatusCodes.Status409Conflict,
                ex.Message);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            if (completionStarted)
            {
                _telemetry.RecordPaymentCompletionFailed(providerCode, "TRANSACTION_ROLLED_BACK");
            }
            throw;
        }

        _logger.LogInformation(
            "CommerceHub payment webhook applied | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | Success: {Success} | TransactionId: {TransactionId} | CorrelationId: {CorrelationId}",
            processedPayment.Id,
            processedPayment.Order.Id,
            providerCode,
            isSuccess,
            paymentReferenceId,
            correlationId);

        return BuildWebhookResult(
            processedPayment,
            providerCode,
            processedResult,
            resultMessage,
            paymentReferenceId,
            shouldRetry: false); // Don't retry successful processing
        }
        finally
        {
            await _lockService.ReleaseLockAsync(paymentLockKey, paymentLockToken);
        }
    }

    private static string BuildPaymentLockKey(Guid marketplaceOrderId, string providerCode)
    {
        return $"lock:payment:initiate:{marketplaceOrderId:N}:{providerCode.Trim().ToLowerInvariant()}";
    }

    private async Task WriteCanonicalPaymentCompletionOutboxEventsAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubPayment payment,
        string providerCode,
        string paymentReferenceId,
        int finalizedReservationCount,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var affectedStoreIds = marketplaceOrder.GetAffectedStoreIds()
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (affectedStoreIds.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot write canonical payment completion outbox events for marketplace order {marketplaceOrder.Id}: affected store scope is empty.");
        }

        var eventVersion = CommerceHubOrderEventMetadata.ResolveAggregateEventVersion(marketplaceOrder);
        var occurredAtUtc = DateTime.UtcNow;
        var tenantId = affectedStoreIds.First();
        var subOrderIds = marketplaceOrder.SubOrders.Select(subOrder => subOrder.Id).ToList();
        var earningCount = await _dbContext.CommerceHubVendorEarnings
            .CountAsync(
                earning => earning.PaymentId == payment.Id &&
                           earning.OriginalEarningId == null &&
                           !earning.IsDeleted,
                cancellationToken);
        var settlementEntryCount = await _dbContext.CommerceHubFinancialTransactions
            .CountAsync(
                transaction => transaction.ReferenceId == marketplaceOrder.Id ||
                               subOrderIds.Contains(transaction.ReferenceId),
                cancellationToken);

        var paymentCompleted = CreatePaymentCompletionEvent<CommerceHubPaymentCompletedEvent>(
            marketplaceOrder,
            payment,
            providerCode,
            paymentReferenceId,
            affectedStoreIds,
            tenantId,
            eventVersion,
            occurredAtUtc,
            correlationId,
            "PaymentCompleted");

        var orderPaid = CreatePaymentCompletionEvent<CommerceHubOrderPaidEvent>(
            marketplaceOrder,
            payment,
            providerCode,
            paymentReferenceId,
            affectedStoreIds,
            tenantId,
            eventVersion,
            occurredAtUtc,
            correlationId,
            "OrderPaid");

        var vendorEarningsCreated = CreatePaymentCompletionEvent<CommerceHubVendorEarningsCreatedEvent>(
            marketplaceOrder,
            payment,
            providerCode,
            paymentReferenceId,
            affectedStoreIds,
            tenantId,
            eventVersion,
            occurredAtUtc,
            correlationId,
            "VendorEarningsCreated");
        vendorEarningsCreated.EarningCount = earningCount;

        var settlementCreated = CreatePaymentCompletionEvent<CommerceHubSettlementCreatedEvent>(
            marketplaceOrder,
            payment,
            providerCode,
            paymentReferenceId,
            affectedStoreIds,
            tenantId,
            eventVersion,
            occurredAtUtc,
            correlationId,
            "SettlementCreated");
        settlementCreated.SettlementEntryCount = settlementEntryCount;

        var inventoryFinalized = CreatePaymentCompletionEvent<CommerceHubInventoryFinalizedEvent>(
            marketplaceOrder,
            payment,
            providerCode,
            paymentReferenceId,
            affectedStoreIds,
            tenantId,
            eventVersion,
            occurredAtUtc,
            correlationId,
            "InventoryFinalized");
        inventoryFinalized.ReservationCount = finalizedReservationCount;

        await _eventPublisher.PublishAsync(paymentCompleted, cancellationToken);
        await _eventPublisher.PublishAsync(orderPaid, cancellationToken);
        await _eventPublisher.PublishAsync(vendorEarningsCreated, cancellationToken);
        await _eventPublisher.PublishAsync(settlementCreated, cancellationToken);
        await _eventPublisher.PublishAsync(inventoryFinalized, cancellationToken);
        await _eventPublisher.PublishAsync(new CommerceHubPaymentNotificationsRequestedEvent
        {
            MarketplaceOrderId = marketplaceOrder.Id,
            CommerceHubPaymentId = payment.Id,
            OrderNumber = marketplaceOrder.OrderNumber,
            CustomerName = marketplaceOrder.FullName,
            CustomerPhone = marketplaceOrder.PhoneNumber,
            Amount = payment.Amount,
            Currency = payment.Currency,
            TenantId = tenantId,
            CorrelationId = correlationId,
            IdempotencyKey = BuildPaymentCompletionEventIdempotencyKey(payment.Id, eventVersion, "NotificationsRequested"),
            OccurredAtUtc = occurredAtUtc,
            EventVersion = eventVersion
        }, cancellationToken);

        _telemetry.RecordOutboxPublished(providerCode, 6);
        _logger.LogInformation(
            "CommerceHub canonical payment completion outbox events written | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | EventVersion: {EventVersion} | EarningCount: {EarningCount} | SettlementEntryCount: {SettlementEntryCount} | FinalizedReservationCount: {FinalizedReservationCount} | CorrelationId: {CorrelationId} | Metric: OutboxPublished",
            payment.Id,
            marketplaceOrder.Id,
            eventVersion,
            earningCount,
            settlementEntryCount,
            finalizedReservationCount,
            correlationId);
    }

    private static T CreatePaymentCompletionEvent<T>(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubPayment payment,
        string providerCode,
        string paymentReferenceId,
        List<Guid> affectedStoreIds,
        Guid tenantId,
        int eventVersion,
        DateTime occurredAtUtc,
        string correlationId,
        string eventName)
        where T : CommerceHubPaymentCompletionEvent, new()
    {
        return new T
        {
            MarketplaceOrderId = marketplaceOrder.Id,
            CommerceHubPaymentId = payment.Id,
            OrderNumber = marketplaceOrder.OrderNumber,
            Provider = providerCode,
            ProviderIntentId = payment.ProviderIntentId,
            ProviderTransactionId = paymentReferenceId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            AffectedStoreIds = affectedStoreIds,
            TenantId = tenantId,
            CorrelationId = correlationId,
            IdempotencyKey = BuildPaymentCompletionEventIdempotencyKey(payment.Id, eventVersion, eventName),
            OccurredAtUtc = occurredAtUtc,
            EventVersion = eventVersion
        };
    }

    private static string BuildPaymentCompletionEventIdempotencyKey(
        Guid paymentId,
        int eventVersion,
        string eventName) =>
        $"commercehub:payment-completion:{paymentId:N}:{eventVersion}:{eventName}";

    private static string BuildPaymentProcessingLockKey(Guid paymentId)
    {
        return $"lock:payment:{paymentId:N}";
    }

    private async Task<CommerceHubMarketplaceOrder?> ResolveMarketplaceOrderAsync(
        string orderIdOrNumber,
        CancellationToken cancellationToken)
    {
        var normalized = orderIdOrNumber.Trim();

        IQueryable<CommerceHubMarketplaceOrder> query = _dbContext.CommerceHubMarketplaceOrders
            .Include(order => order.SubOrders)
            .ThenInclude(subOrder => subOrder.Items)
            .Where(order => !order.IsDeleted);

        if (Guid.TryParse(normalized, out var marketplaceOrderId))
        {
            var byId = await query.FirstOrDefaultAsync(order => order.Id == marketplaceOrderId, cancellationToken);
            if (byId != null)
            {
                return byId;
            }
        }

        return await query.FirstOrDefaultAsync(order =>
            order.OrderNumber == normalized ||
            order.PaymentReference == normalized,
            cancellationToken);
    }

    private async Task<CommerceHubPayment?> ResolvePaymentAsync(
        PaymentProvider provider,
        string? providerReference,
        string? providerIntentId,
        string? paymentId,
        CancellationToken cancellationToken,
        string? providerTransactionId = null)
    {
        var query = _dbContext.CommerceHubPayments
            .Include(payment => payment.Order)
            .ThenInclude(order => order.SubOrders)
            .ThenInclude(subOrder => subOrder.Items)
            .Include(payment => payment.Attempts)
            .Where(payment =>
                payment.Provider == provider &&
                !payment.IsDeleted &&
                !payment.Order.IsDeleted);

        if (!string.IsNullOrWhiteSpace(providerReference))
        {
            var byReference = await query.FirstOrDefaultAsync(
                payment => payment.ProviderReference == providerReference,
                cancellationToken);

            if (byReference != null)
            {
                return byReference;
            }
        }

        if (Guid.TryParse(paymentId, out var paymentEntityId))
        {
            var byPaymentId = await query.FirstOrDefaultAsync(
                payment => payment.Id == paymentEntityId,
                cancellationToken);

            if (byPaymentId != null)
            {
                return byPaymentId;
            }
        }

        if (!string.IsNullOrWhiteSpace(providerIntentId))
        {
            var byProviderIntent = await query.FirstOrDefaultAsync(
                payment => payment.ProviderIntentId == providerIntentId,
                cancellationToken);

            if (byProviderIntent != null)
            {
                return byProviderIntent;
            }
        }

        if (!string.IsNullOrWhiteSpace(providerTransactionId))
        {
            return await ResolvePaymentByProviderTransactionAsync(
                provider,
                providerTransactionId,
                cancellationToken);
        }

        return null;
    }

    private async Task<CommerceHubPayment?> ResolvePaymentByProviderTransactionAsync(
        PaymentProvider provider,
        string providerTransactionId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubPayments
            .Include(payment => payment.Order)
            .ThenInclude(order => order.SubOrders)
            .ThenInclude(subOrder => subOrder.Items)
            .Include(payment => payment.Attempts)
            .Where(payment =>
                payment.Provider == provider &&
                !payment.IsDeleted &&
                !payment.Order.IsDeleted)
            .FirstOrDefaultAsync(
                payment =>
                    payment.ProviderTransactionId == providerTransactionId ||
                    payment.Attempts.Any(attempt => attempt.ProviderTransactionId == providerTransactionId),
                cancellationToken);
    }

    private async Task<CommerceHubPayment?> CreateLegacyPaymobPaymentAsync(
        string legacyReference,
        string? providerIntentId,
        CancellationToken cancellationToken)
    {
        var marketplaceOrder = await RecomposeLegacyPaymobPaymentCreationAuthorityAsync(
            legacyReference,
            cancellationToken);
        if (marketplaceOrder == null)
        {
            return null;
        }

        var existing = await _dbContext.CommerceHubPayments
            .FirstOrDefaultAsync(payment =>
                payment.Provider == PaymentProvider.Paymob &&
                payment.ProviderReference == legacyReference &&
                !payment.IsDeleted,
                cancellationToken);

        if (existing != null)
        {
            return await ResolvePaymentAsync(
                PaymentProvider.Paymob,
                existing.ProviderReference,
                existing.ProviderIntentId,
                existing.Id.ToString(),
                cancellationToken);
        }

        var payment = new CommerceHubPayment
        {
            Id = Guid.NewGuid(),
            OrderId = marketplaceOrder.Id,
            Amount = marketplaceOrder.TotalAmount,
            Currency = RequireCommerceHubPaymentCurrency(marketplaceOrder.Currency, "payment record creation"),
            Provider = PaymentProvider.Paymob,
            ProviderReference = legacyReference,
            ProviderIntentId = providerIntentId,
            Status = CommerceHubPaymentRecordStatus.Pending
        };

        _dbContext.CommerceHubPayments.Add(payment);
        EnsurePaymentItems(payment, marketplaceOrder.SubOrders);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await ResolvePaymentAsync(
            PaymentProvider.Paymob,
            payment.ProviderReference,
            payment.ProviderIntentId,
            payment.Id.ToString(),
            cancellationToken);
    }

    private async Task<CommerceHubMarketplaceOrder?> RecomposeLegacyPaymobPaymentCreationAuthorityAsync(
        string legacyReference,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var marketplaceOrder = await ResolveMarketplaceOrderAsync(legacyReference, cancellationToken);
        if (marketplaceOrder == null)
        {
            return null;
        }

        try
        {
            await EnsureInitiationAllowedAsync(
                marketplaceOrder,
                CommerceHubPaymentMethods.Paymob,
                cancellationToken);

            await _inventoryConsistencyService.EnsureMarketplaceOrderReservationsActiveAsync(
                marketplaceOrder.Id,
                cancellationToken);
        }
        catch (CommerceHubCheckoutException ex)
        {
            _logger.LogWarning(
                ex,
                "Paymob legacy webhook fallback refused to create stale payment authority | MarketplaceOrderId: {MarketplaceOrderId} | OrderNumber: {OrderNumber} | PaymentStatus: {PaymentStatus} | Status: {Status} | FailureCode: {FailureCode}",
                marketplaceOrder.Id,
                marketplaceOrder.OrderNumber,
                marketplaceOrder.PaymentStatus,
                marketplaceOrder.Status,
                ex.Code);

            return null;
        }

        _logger.LogDebug(
            "Paymob legacy webhook fallback recomposed SQL order and reservation authority before payment row creation | MarketplaceOrderId: {MarketplaceOrderId} | OrderNumber: {OrderNumber}",
            marketplaceOrder.Id,
            marketplaceOrder.OrderNumber);

        return marketplaceOrder;
    }

    private static CommerceHubPayment CreatePendingPayment(
        CommerceHubMarketplaceOrder marketplaceOrder,
        PaymentProvider provider)
    {
        var paymentId = Guid.NewGuid();
        return new CommerceHubPayment
        {
            Id = paymentId,
            OrderId = marketplaceOrder.Id,
            Amount = marketplaceOrder.TotalAmount,
            Currency = RequireCommerceHubPaymentCurrency(marketplaceOrder.Currency, "payment record creation"),
            Provider = provider,
            Status = CommerceHubPaymentRecordStatus.Pending,
            ProviderReference = paymentId.ToString("N")
        };
    }

    private static CommerceHubPaymentAttempt CreatePaymentAttempt(CommerceHubPayment payment)
    {
        var nextAttemptNumber = payment.Attempts.Count == 0
            ? 1
            : payment.Attempts.Max(item => item.AttemptNumber) + 1;

        return new CommerceHubPaymentAttempt
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Payment = payment,
            Provider = payment.Provider,
            AttemptNumber = nextAttemptNumber,
            ProviderReference = payment.ProviderReference,
            Status = CommerceHubPaymentRecordStatus.Pending
        };
    }

    private CommerceHubPaymentAttempt ResolveOrCreateWebhookAttempt(CommerceHubPayment payment)
    {
        var existingAttempt = payment.Attempts
            .OrderByDescending(item => item.AttemptNumber)
            .FirstOrDefault(item => item.Status == CommerceHubPaymentRecordStatus.Pending) ??
            payment.Attempts
                .OrderByDescending(item => item.AttemptNumber)
                .FirstOrDefault();

        if (existingAttempt != null)
        {
            return existingAttempt;
        }

        var attempt = CreatePaymentAttempt(payment);
        _dbContext.CommerceHubPaymentAttempts.Add(attempt);
        return attempt;
    }

    private static bool EnsurePaymentItems(
        CommerceHubPayment payment,
        IEnumerable<CommerceHubSubOrder> subOrders)
    {
        var existingSubOrderIds = payment.Items
            .Select(item => item.SubOrderId)
            .ToHashSet();

        var added = false;
        foreach (var subOrder in subOrders.Where(IsOnlineSubOrder))
        {
            if (!existingSubOrderIds.Add(subOrder.Id))
            {
                continue;
            }

            payment.Items.Add(new CommerceHubPaymentItem
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                SubOrderId = subOrder.Id,
                Amount = subOrder.Total
            });

            added = true;
        }

        return added;
    }

    private static bool IsOnlineSubOrder(CommerceHubSubOrder subOrder) =>
        subOrder.PaymentMethod != CommerceHubPaymentMethod.CashOnDelivery &&
        !CommerceHubPaymentMethods.IsCod(subOrder.PaymentMethodCode);

    private static string BuildProviderSessionReinitFenceReason(string providerCode) =>
        $"{CommerceHubPaymentSessionRecoveryAuthority.ProviderSessionReinitFencePrefix} A {providerCode} provider session attempt may already exist externally. Reconcile or persist provider identity before creating another session.";

    /// <summary>
    /// Checks if the given provider intent ID is a reinit fence sentinel.
    /// The sentinel prevents recovery from creating duplicate provider sessions.
    /// </summary>
    private static bool IsProviderSessionReinitFenceIntentId(string? providerIntentId)
    {
        return CommerceHubPaymentSessionRecoveryAuthority.IsProviderSessionReinitFence(providerIntentId);
    }

    private async Task EnsureInitiationAllowedAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        string providerCode,
        CancellationToken cancellationToken)
    {
        RequireCommerceHubPaymentCurrency(marketplaceOrder.Currency, "payment session order");

        if (!CommerceHubPaymentMethods.IsOnline(marketplaceOrder.PaymentMethodCode))
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_NOT_APPLICABLE",
                "Online payment cannot be initiated for cash-on-delivery marketplace orders.",
                StatusCodes.Status409Conflict);
        }

        if (!string.Equals(marketplaceOrder.PaymentMethodCode, providerCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_PROVIDER_MISMATCH",
                $"This marketplace order was created for {marketplaceOrder.PaymentMethodCode}, not {providerCode}.",
                StatusCodes.Status409Conflict);
        }

        if (CommerceHubOrderStateHelper.HasCollectedPayment(marketplaceOrder.PaymentStatus))
        {
            throw new CommerceHubCheckoutException(
                "ORDER_ALREADY_PAID",
                "This marketplace order is already marked as paid.",
                StatusCodes.Status409Conflict);
        }

        if (marketplaceOrder.PaymentStatus != PaymentStatus.Pending)
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_NOT_RETRYABLE",
                "This marketplace order is not in a retryable payment state.",
                StatusCodes.Status409Conflict);
        }

        if (marketplaceOrder.Status is not OrderStatus.Pending and not OrderStatus.OnHold)
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_NOT_RETRYABLE",
                "This marketplace order is no longer waiting for online payment.",
                StatusCodes.Status409Conflict);
        }

        await EnsureNoUnresolvedPaymentAsync(marketplaceOrder, providerCode, cancellationToken);
    }

    private async Task EnsureNoUnresolvedPaymentAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        string providerCode,
        CancellationToken cancellationToken)
    {
        var hasUnresolvedCapturedPayment = await _dbContext.CommerceHubUnresolvedPaymentWebhooks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                webhook =>
                    !webhook.IsDeleted &&
                    webhook.OrderId == marketplaceOrder.Id &&
                    webhook.Provider == ResolvePaymentProvider(providerCode) &&
                    (webhook.Status == PendingTenantResolutionStatus ||
                     webhook.Status == RoutedToSuspenseStatus ||
                     webhook.Status == SuspenseMatchedPendingClearanceStatus),
                cancellationToken);

        if (hasUnresolvedCapturedPayment)
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_RECONCILIATION_REQUIRED",
                "Payment is under manual review because the provider already reported a captured or ambiguous payment outcome. Do not retry payment until reconciliation completes.",
                StatusCodes.Status409Conflict);
        }
    }

    private void EnsureInitiationAccessAllowed(
        CommerceHubMarketplaceOrder marketplaceOrder,
        Guid? authenticatedCustomerId,
        string? orderAccessToken)
    {
        if (authenticatedCustomerId.HasValue && marketplaceOrder.CustomerId == authenticatedCustomerId.Value)
        {
            return;
        }

        if (_orderAccessService.ValidateAccessToken(
                marketplaceOrder.Id,
                marketplaceOrder.OrderNumber,
                orderAccessToken))
        {
            return;
        }

        throw new CommerceHubCheckoutException(
            "ORDER_ACCESS_DENIED",
            "You do not have permission to initiate payment for this order.",
            StatusCodes.Status403Forbidden);
    }

    private void EnsureRetryBudget(
        CommerceHubMarketplaceOrder marketplaceOrder,
        IReadOnlyCollection<CommerceHubPayment> payments)
    {
        if (_paymentsOptions.MaxAttemptsPerOrder <= 0)
        {
            return;
        }

        var orderAttemptCount = payments.Count(payment =>
            payment.Status != CommerceHubPaymentRecordStatus.Ignored);

        if (orderAttemptCount < _paymentsOptions.MaxAttemptsPerOrder)
        {
            return;
        }

        _logger.LogWarning(
            "CommerceHub payment attempt limit reached | MarketplaceOrderId: {MarketplaceOrderId} | AttemptCount: {AttemptCount} | MaxAttempts: {MaxAttempts}",
            marketplaceOrder.Id,
            orderAttemptCount,
            _paymentsOptions.MaxAttemptsPerOrder);

        throw new CommerceHubCheckoutException(
            "PAYMENT_ATTEMPTS_EXCEEDED",
            $"This marketplace order has reached the maximum of {_paymentsOptions.MaxAttemptsPerOrder} payment attempts.",
            StatusCodes.Status409Conflict);
    }

    private static (string ProviderCode, PaymentProvider Provider) NormalizeProvider(string? provider)
    {
        var normalized = provider?.Trim();

        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(normalized, CommerceHubPaymentMethods.Paymob, StringComparison.OrdinalIgnoreCase))
        {
            return (CommerceHubPaymentMethods.Paymob, PaymentProvider.Paymob);
        }

        if (string.Equals(normalized, CommerceHubPaymentMethods.Stripe, StringComparison.OrdinalIgnoreCase))
        {
            return (CommerceHubPaymentMethods.Stripe, PaymentProvider.Stripe);
        }

        throw new CommerceHubCheckoutException(
            "PAYMENT_PROVIDER_INVALID",
            "Payment provider must be PAYMOB or STRIPE.",
            StatusCodes.Status400BadRequest);
    }

    private static PaymentProvider ResolvePaymentProvider(string providerCode)
    {
        return string.Equals(providerCode, CommerceHubPaymentMethods.Stripe, StringComparison.OrdinalIgnoreCase)
            ? PaymentProvider.Stripe
            : PaymentProvider.Paymob;
    }

    private IPaymentProviderAdapter GetProviderAdapter(string providerCode)
    {
        var normalized = new ProviderCode(providerCode).Normalized;
        if (!_providerAdapters.TryGetValue(normalized, out var adapter) || adapter is null)
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_PROVIDER_ADAPTER_NOT_REGISTERED",
                $"Payment provider adapter '{providerCode}' is not registered.",
                StatusCodes.Status500InternalServerError);
        }

        return adapter;
    }

    private static PaymentProviderSessionRequest BuildPaymobProviderSessionRequest(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubPayment payment,
        string marketplaceOrderCountryCode,
        string correlationId)
    {
        return new PaymentProviderSessionRequest
        {
            PaymentId = payment.Id,
            MarketplaceOrderId = marketplaceOrder.Id,
            OrderNumber = marketplaceOrder.OrderNumber,
            ProviderReference = payment.ProviderReference,
            ExistingProviderIntentId = IsProviderSessionReinitFenceIntentId(payment.ProviderIntentId)
                ? null
                : payment.ProviderIntentId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            CustomerName = marketplaceOrder.FullName,
            Email = "customer@example.com",
            PhoneNumber = marketplaceOrder.PhoneNumber,
            AddressLine = marketplaceOrder.AddressLine,
            City = marketplaceOrder.City,
            Area = marketplaceOrder.Area,
            CountryCode = marketplaceOrderCountryCode,
            CorrelationId = correlationId
        };
    }

    private static PaymentProviderSessionRequest BuildStripeProviderSessionRequest(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubPayment payment,
        string correlationId)
    {
        return new PaymentProviderSessionRequest
        {
            PaymentId = payment.Id,
            MarketplaceOrderId = marketplaceOrder.Id,
            OrderNumber = marketplaceOrder.OrderNumber,
            ProviderReference = payment.ProviderReference,
            ExistingProviderIntentId = IsProviderSessionReinitFenceIntentId(payment.ProviderIntentId)
                ? null
                : payment.ProviderIntentId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            CustomerName = marketplaceOrder.FullName,
            PhoneNumber = marketplaceOrder.PhoneNumber,
            AddressLine = marketplaceOrder.AddressLine,
            City = marketplaceOrder.City,
            Area = marketplaceOrder.Area,
            CountryCode = string.Empty,
            CorrelationId = correlationId
        };
    }

    private static CommerceHubCheckoutException MapProviderAdapterException(PaymentProviderAdapterException exception)
    {
        var statusCode = exception.Error.Type switch
        {
            CanonicalProviderErrorType.Configuration => StatusCodes.Status400BadRequest,
            CanonicalProviderErrorType.Validation => StatusCodes.Status400BadRequest,
            CanonicalProviderErrorType.Authentication or CanonicalProviderErrorType.Authorization => StatusCodes.Status502BadGateway,
            CanonicalProviderErrorType.Network or CanonicalProviderErrorType.ProviderUnavailable or CanonicalProviderErrorType.RateLimited => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status502BadGateway
        };

        return new CommerceHubCheckoutException(
            exception.Error.Code,
            exception.Error.Message,
            statusCode,
            exception.Message);
    }

    private async Task<CommerceHubPaymentProviderSession> CreatePaymobSessionAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubPayment payment,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var marketplaceOrderCountryCode = await ResolveMarketplaceOrderCountryCodeAsync(
            marketplaceOrder,
            cancellationToken);

        try
        {
            var session = await GetProviderAdapter(CommerceHubPaymentMethods.Paymob).CreateSessionAsync(
                BuildPaymobProviderSessionRequest(
                    marketplaceOrder,
                    payment,
                    marketplaceOrderCountryCode,
                    correlationId),
                cancellationToken);

            if (_paymobArtifactProtector is null ||
                string.IsNullOrWhiteSpace(session.PaymentUrl) ||
                string.IsNullOrWhiteSpace(session.PaymentToken))
            {
                throw new PaymentProviderAdapterException(CanonicalProviderError.Configuration(
                    PaymobProviderReference.ProviderName,
                    "PAYMOB_SESSION_PROTECTION_UNAVAILABLE",
                    "CommerceHub Paymob checkout material could not be protected."));
            }

            return new CommerceHubPaymentProviderSession(
                null,
                _paymobArtifactProtector.ProtectPaymentSession(session.PaymentToken, session.PaymentUrl),
                session.ProviderIntentId,
                session.RawResponse);
        }
        catch (PaymentProviderAdapterException ex) when (ex.OutcomeUnknown && !string.IsNullOrWhiteSpace(ex.ProviderIntentId))
        {
            payment.ProviderIntentId = ex.ProviderIntentId;
            throw new CommerceHubProviderSessionOutcomeUnknownException(
                "Paymob intention creation has an unknown outcome. Reconcile the provider intention before creating another session.",
                ex.ProviderIntentId,
                ex);
        }
        catch (PaymentProviderAdapterException ex)
        {
            throw MapProviderAdapterException(ex);
        }
    }

    private async Task<CommerceHubPaymentProviderSession> CreateStripeSessionAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubPayment payment,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await GetProviderAdapter(CommerceHubPaymentMethods.Stripe).CreateSessionAsync(
                BuildStripeProviderSessionRequest(
                    marketplaceOrder,
                    payment,
                    correlationId),
                cancellationToken);

            _logger.LogInformation(
                "CommerceHub Stripe payment session created | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | StripePaymentIntentId: {StripePaymentIntentId} | CorrelationId: {CorrelationId}",
                payment.Id,
                marketplaceOrder.Id,
                session.ProviderIntentId,
                correlationId);

            return new CommerceHubPaymentProviderSession(
                session.PaymentUrl,
                session.PaymentToken,
                session.ProviderIntentId,
                session.RawResponse);
        }
        catch (PaymentProviderAdapterException ex)
        {
            throw MapProviderAdapterException(ex);
        }
    }

    private async Task<CommerceHubPaymentInitiateResponseDto?> TryRecoverIncompletePendingPaymentAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubPayment payment,
        string providerCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await RecomposeMarketplaceReservationAuthorityBeforeProviderSessionAsync(
            marketplaceOrder,
            "CommerceHubPayment.Recover.ProviderSession",
            cancellationToken);

        if (CommerceHubPaymentSessionRecoveryAuthority.HasReusableSession(payment))
        {
            return MapInitiateResponse(
                payment,
                marketplaceOrder,
                providerCode,
                isReusable: true,
                message: "Existing pending payment session reused.");
        }

        var paymentAttempt = payment.Attempts
            .OrderByDescending(item => item.AttemptNumber)
            .FirstOrDefault();
        if (paymentAttempt == null)
        {
            _logger.LogCritical(
                "CommerceHub payment recovery cannot proceed because no payment attempt exists | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | CorrelationId: {CorrelationId}",
                payment.Id,
                marketplaceOrder.Id,
                providerCode,
                correlationId);
            return null;
        }

        CommerceHubPaymentProviderSession? recoveredSession;
        try
        {
            recoveredSession = payment.Provider switch
            {
                PaymentProvider.Stripe => await TryRecoverStripeSessionAsync(
                    marketplaceOrder,
                    payment,
                    correlationId,
                    cancellationToken),
                PaymentProvider.Paymob => await TryRecoverPaymobSessionAsync(
                    marketplaceOrder,
                    payment,
                    correlationId,
                    cancellationToken),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "CommerceHub payment recovery could not reconstruct the external provider session | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | CorrelationId: {CorrelationId}",
                payment.Id,
                marketplaceOrder.Id,
                providerCode,
                correlationId);
            return null;
        }

        if (recoveredSession == null)
        {
            return null;
        }

        if (!await TryPersistProviderSessionDirectlyAsync(
                payment,
                paymentAttempt,
                marketplaceOrder,
                recoveredSession,
                providerCode,
                $"initiate.{providerCode.ToLowerInvariant()}.recovered",
                correlationId,
                cancellationToken))
        {
            return null;
        }

        payment.ProviderIntentId = recoveredSession.ProviderIntentId;
        payment.CheckoutUrl = recoveredSession.PaymentUrl;
        payment.PaymentToken = recoveredSession.PaymentToken;
        payment.FailureReason = null;

        _logger.LogInformation(
            "CommerceHub payment initiation recovered an incomplete pending provider session | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | CorrelationId: {CorrelationId}",
            payment.Id,
            marketplaceOrder.Id,
            providerCode,
            correlationId);

        return MapInitiateResponse(
            payment,
            marketplaceOrder,
            providerCode,
            isReusable: true,
            message: "Recovered existing pending payment session.");
    }

    private async Task RecomposeMarketplaceReservationAuthorityBeforeProviderSessionAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        string authorityPoint,
        CancellationToken cancellationToken)
    {
        await _inventoryConsistencyService.EnsureMarketplaceOrderReservationsActiveAsync(
            marketplaceOrder.Id,
            cancellationToken);

        _logger.LogDebug(
            "CommerceHub payment recomposed SQL reservation authority before provider session side effect | MarketplaceOrderId: {MarketplaceOrderId} | AuthorityPoint: {AuthorityPoint}",
            marketplaceOrder.Id,
            authorityPoint);
    }

    private async Task<CommerceHubPaymentProviderSession?> TryRecoverStripeSessionAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubPayment payment,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // SECURITY: Treat reinit fence sentinel as a gated state - do not create new session or query Stripe
        if (IsProviderSessionReinitFenceIntentId(payment.ProviderIntentId))
        {
            _logger.LogWarning(
                "[CommerceHubPayment] Recovery blocked by reinit fence | OrderId: {OrderId} | PaymentId: {PaymentId}",
                marketplaceOrder.Id,
                payment.Id);
            return null;
        }

        try
        {
            var session = await GetProviderAdapter(CommerceHubPaymentMethods.Stripe).CreateSessionAsync(
                BuildStripeProviderSessionRequest(
                    marketplaceOrder,
                    payment,
                    correlationId),
                cancellationToken);

            if (string.IsNullOrWhiteSpace(session.PaymentToken))
            {
                return null;
            }

            return new CommerceHubPaymentProviderSession(
                session.PaymentUrl,
                session.PaymentToken,
                session.ProviderIntentId,
                session.RawResponse);
        }
        catch (PaymentProviderAdapterException ex)
        {
            _logger.LogWarning(
                ex,
                "Stripe provider adapter could not recover payment session | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Code: {Code} | CorrelationId: {CorrelationId}",
                payment.Id,
                marketplaceOrder.Id,
                ex.Error.Code,
                correlationId);
            return null;
        }
    }

    private async Task<CommerceHubPaymentProviderSession?> TryRecoverPaymobSessionAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubPayment payment,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.ProviderIntentId))
        {
            return null;
        }

        // SECURITY: Treat reinit fence sentinel as a gated state - do not create new session or query Paymob
        if (IsProviderSessionReinitFenceIntentId(payment.ProviderIntentId))
        {
            _logger.LogWarning(
                "[CommerceHubPayment] Recovery blocked by reinit fence | OrderId: {OrderId} | PaymentId: {PaymentId}",
                marketplaceOrder.Id,
                payment.Id);
            return null;
        }

        var marketplaceOrderCountryCode = await ResolveMarketplaceOrderCountryCodeAsync(
            marketplaceOrder,
            cancellationToken);

        try
        {
            var session = await GetProviderAdapter(CommerceHubPaymentMethods.Paymob).CreateSessionAsync(
                BuildPaymobProviderSessionRequest(
                    marketplaceOrder,
                    payment,
                    marketplaceOrderCountryCode,
                    correlationId),
                cancellationToken);

            return new CommerceHubPaymentProviderSession(
                session.PaymentUrl,
                session.PaymentToken,
                session.ProviderIntentId,
                session.RawResponse);
        }
        catch (PaymentProviderAdapterException ex)
        {
            _logger.LogWarning(
                ex,
                "Paymob provider adapter could not recover payment session | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Code: {Code} | CorrelationId: {CorrelationId}",
                payment.Id,
                marketplaceOrder.Id,
                ex.Error.Code,
                correlationId);
            return null;
        }
    }

    /// <summary>
    /// Persists provider session data to the database with retry logic.
    /// Handles transient database failures by clearing and rehydrating the EF change tracker.
    /// </summary>
    /// <param name="payment">The payment entity to persist (Modified state)</param>
    /// <param name="paymentAttempt">The payment attempt entity to persist (Modified state)</param>
    /// <param name="marketplaceOrder">The marketplace order (used for logging)</param>
    /// <param name="session">The provider session data (used for logging)</param>
    /// <param name="providerCode">The provider code (used for logging)</param>
    /// <param name="initiationEventType">The initiation event type (used for cleanup)</param>
    /// <param name="correlationId">The correlation ID (used for logging)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task PersistProviderSessionAsync(
        CommerceHubPayment payment,
        CommerceHubPaymentAttempt paymentAttempt,
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubPaymentProviderSession session,
        string providerCode,
        string initiationEventType,
        string correlationId,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (IsProviderPaymentIdentityUniqueViolation(ex))
            {
                payment.ProviderIntentId = null;
                paymentAttempt.ProviderIntentId = null;
                DetachPendingPaymentLogs(payment.Id, initiationEventType);

                throw new CommerceHubCheckoutException(
                    "PAYMENT_PROVIDER_IDENTITY_CONFLICT",
                    "The payment provider session is already linked to another CommerceHub payment.",
                    StatusCodes.Status409Conflict,
                    ex.Message);
            }
            catch (DbUpdateException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Retrying CommerceHub provider session persistence after database failure | Attempt: {Attempt}/{MaxAttempts} | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | ProviderIntentId: {ProviderIntentId} | CorrelationId: {CorrelationId}",
                    attempt,
                    maxAttempts,
                    payment.Id,
                    marketplaceOrder.Id,
                    providerCode,
                    session.ProviderIntentId,
                    correlationId);

                // Clear change tracker to avoid inconsistent tracked state after DbUpdateException
                _dbContext.ChangeTracker.Clear();

                // Rehydrate entities with correct states for retry
                _dbContext.Entry(payment).State = EntityState.Modified;
                _dbContext.Entry(paymentAttempt).State = EntityState.Modified;

                // Re-add the payment log that was cleared
                _dbContext.CommerceHubPaymentLogs.Add(new CommerceHubPaymentLog
                {
                    Id = Guid.NewGuid(),
                    PaymentId = payment.Id,
                    EventType = initiationEventType,
                    ProviderEventId = session.ProviderIntentId,
                    RawResponse = session.RawResponse
                });

                await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1)), cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                if (await TryPersistProviderSessionDirectlyAsync(
                        payment,
                        paymentAttempt,
                        marketplaceOrder,
                        session,
                        providerCode,
                        initiationEventType,
                        correlationId,
                        cancellationToken))
                {
                    return;
                }

                throw new CommerceHubProviderSessionPersistenceException(
                    "Provider session was accepted externally but POS could not persist provider identity.",
                    ex);
            }
        }
    }

    private async Task<bool> TryPersistProviderSessionDirectlyAsync(
        CommerceHubPayment payment,
        CommerceHubPaymentAttempt paymentAttempt,
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubPaymentProviderSession session,
        string providerCode,
        string initiationEventType,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var paymentBoundary = CommerceHubBoundaryMutationScope.BeginPayment();
            _dbContext.ChangeTracker.Clear();
            var nowUtc = DateTime.UtcNow;
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            var paymentRows = await _dbContext.CommerceHubPayments
                .Where(item =>
                    item.Id == payment.Id &&
                    item.OrderId == marketplaceOrder.Id &&
                    !item.IsDeleted &&
                    item.Status == CommerceHubPaymentRecordStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ProviderIntentId, session.ProviderIntentId)
                    .SetProperty(item => item.CheckoutUrl, session.PaymentUrl)
                    .SetProperty(item => item.PaymentToken, session.PaymentToken)
                    .SetProperty(item => item.FailureReason, (string?)null)
                    .SetProperty(item => item.UpdatedAt, nowUtc),
                    cancellationToken);

            if (paymentRows != 1)
            {
                _logger.LogCritical(
                    "CommerceHub direct provider-session persistence could not find exactly one pending payment | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | UpdatedRows: {UpdatedRows} | CorrelationId: {CorrelationId}",
                    payment.Id,
                    marketplaceOrder.Id,
                    providerCode,
                    paymentRows,
                    correlationId);
                return false;
            }

            var attemptRows = await _dbContext.CommerceHubPaymentAttempts
                .Where(item => item.Id == paymentAttempt.Id && item.PaymentId == payment.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ProviderIntentId, session.ProviderIntentId)
                    .SetProperty(item => item.Status, CommerceHubPaymentRecordStatus.Pending)
                    .SetProperty(item => item.FailureReason, (string?)null)
                    .SetProperty(item => item.CompletedAtUtc, nowUtc)
                    .SetProperty(item => item.UpdatedAt, nowUtc),
                    cancellationToken);

            if (attemptRows != 1)
            {
                throw new CommerceHubProviderSessionPersistenceException(
                    $"Provider session direct recovery updated {paymentRows} payment row(s) but {attemptRows} attempt row(s).",
                    new InvalidOperationException(
                        $"Expected exactly one CommerceHubPaymentAttempt row for PaymentId {payment.Id} and AttemptId {paymentAttempt.Id}."));
            }

            _dbContext.CommerceHubPaymentLogs.Add(new CommerceHubPaymentLog
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                EventType = initiationEventType,
                ProviderEventId = session.ProviderIntentId,
                RawResponse = session.RawResponse
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogCritical(
                "CommerceHub provider session persistence recovered through atomic direct durable update | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | ProviderIntentId: {ProviderIntentId} | CorrelationId: {CorrelationId}",
                payment.Id,
                marketplaceOrder.Id,
                providerCode,
                session.ProviderIntentId,
                correlationId);

            return true;
        }
        catch (DbUpdateException ex) when (IsProviderPaymentIdentityUniqueViolation(ex))
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_PROVIDER_IDENTITY_CONFLICT",
                "The payment provider session is already linked to another CommerceHub payment.",
                StatusCodes.Status409Conflict,
                ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CommerceHubProviderSessionPersistenceException ex)
        {
            _logger.LogCritical(
                ex,
                "CommerceHub direct provider-session persistence recovery was rejected because atomic payment/attempt/log persistence could not be proven | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | ProviderIntentId: {ProviderIntentId} | CorrelationId: {CorrelationId}",
                payment.Id,
                marketplaceOrder.Id,
                providerCode,
                session.ProviderIntentId,
                correlationId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "CommerceHub direct provider-session persistence recovery failed | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | Provider: {Provider} | ProviderIntentId: {ProviderIntentId} | CorrelationId: {CorrelationId}",
                payment.Id,
                marketplaceOrder.Id,
                providerCode,
                session.ProviderIntentId,
                correlationId);
            return false;
        }
    }

    private Task RecordInitiationFailureAsync(
        CommerceHubPayment payment,
        CommerceHubPaymentAttempt paymentAttempt,
        string providerCode,
        string failureReason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        payment.Status = CommerceHubPaymentRecordStatus.Failed;
        payment.FailureReason = string.IsNullOrWhiteSpace(failureReason)
            ? "Payment initiation failed."
            : failureReason.Trim();
        payment.PaidAtUtc = null;
        payment.IsProcessed = false;
        payment.ProcessedAtUtc = null;

        paymentAttempt.Status = CommerceHubPaymentRecordStatus.Failed;
        paymentAttempt.FailureReason = payment.FailureReason;
        paymentAttempt.CompletedAtUtc ??= DateTime.UtcNow;

        _dbContext.CommerceHubPaymentLogs.Add(new CommerceHubPaymentLog
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            EventType = $"initiate.{providerCode.ToLowerInvariant()}.failed",
            RawResponse = JsonSerializer.Serialize(new
            {
                paymentId = payment.Id,
                provider = providerCode,
                correlationId,
                failureReason = payment.FailureReason
            })
        });

        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task RecordInitiationUnknownOutcomeAsync(
        CommerceHubPayment payment,
        CommerceHubPaymentAttempt paymentAttempt,
        string providerCode,
        string failureReason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(failureReason)
            ? "Provider request ended without a trustworthy outcome."
            : failureReason.Trim();

        payment.Status = CommerceHubPaymentRecordStatus.Pending;
        payment.FailureReason = $"PROVIDER_SESSION_OUTCOME_UNKNOWN: {normalizedReason}";
        payment.CheckoutUrl = null;
        payment.PaymentToken = null;
        payment.PaidAtUtc = null;
        payment.IsProcessed = false;
        payment.ProcessedAtUtc = null;

        paymentAttempt.Status = CommerceHubPaymentRecordStatus.Pending;
        paymentAttempt.FailureReason = payment.FailureReason;
        paymentAttempt.CompletedAtUtc ??= DateTime.UtcNow;

        _dbContext.CommerceHubPaymentLogs.Add(new CommerceHubPaymentLog
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            EventType = $"initiate.{providerCode.ToLowerInvariant()}.outcome_unknown",
            RawResponse = JsonSerializer.Serialize(new
            {
                paymentId = payment.Id,
                provider = providerCode,
                providerReference = payment.ProviderReference,
                providerIntentId = payment.ProviderIntentId,
                correlationId,
                failureReason = normalizedReason,
                reconciliationRequired = true
            })
        });

        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool CanApplyPaymentWebhook(CommerceHubMarketplaceOrder marketplaceOrder) =>
        marketplaceOrder.Status is OrderStatus.Pending or OrderStatus.OnHold;

    private async Task<CommerceHubPayment?> LoadCommerceHubPaymentForProviderCallbackMutationAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        IQueryable<CommerceHubPayment> query;

        if (IsPostgresProvider())
        {
            query = _dbContext.CommerceHubPayments.FromSqlInterpolated($@"
                SELECT * FROM ""CommerceHubPayments""
                WHERE ""Id"" = {paymentId}
                  AND ""IsDeleted"" = false
                FOR UPDATE");
        }
        else if (IsSqlServerProvider())
        {
            query = _dbContext.CommerceHubPayments.FromSqlInterpolated($@"
                SELECT * FROM [CommerceHubPayments] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [Id] = {paymentId}
                  AND [IsDeleted] = 0");
        }
        else
        {
            query = _dbContext.CommerceHubPayments
                .Where(item => item.Id == paymentId && !item.IsDeleted);
        }

        return await query
            .Include(item => item.Attempts)
            .FirstOrDefaultAsync(item => item.Id == paymentId, cancellationToken);
    }

    private async Task<CommerceHubMarketplaceOrder?> LoadMarketplaceOrderForProviderCallbackMutationAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken)
    {
        IQueryable<CommerceHubMarketplaceOrder> query;

        if (IsPostgresProvider())
        {
            query = _dbContext.CommerceHubMarketplaceOrders.FromSqlInterpolated($@"
                SELECT * FROM ""CommerceHubMarketplaceOrders""
                WHERE ""Id"" = {marketplaceOrderId}
                  AND ""IsDeleted"" = false
                FOR UPDATE");
        }
        else if (IsSqlServerProvider())
        {
            query = _dbContext.CommerceHubMarketplaceOrders.FromSqlInterpolated($@"
                SELECT * FROM [CommerceHubMarketplaceOrders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [Id] = {marketplaceOrderId}
                  AND [IsDeleted] = 0");
        }
        else
        {
            query = _dbContext.CommerceHubMarketplaceOrders
                .Where(order => order.Id == marketplaceOrderId && !order.IsDeleted);
        }

        return await query
            .Include(order => order.SubOrders)
            .ThenInclude(subOrder => subOrder.Items)
            .FirstOrDefaultAsync(order => order.Id == marketplaceOrderId, cancellationToken);
    }

    private bool IsPostgresProvider() =>
        _dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _dbContext.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private static OrderStatus MoveOrderToPaymentRetryHold(OrderStatus currentStatus)
    {
        return currentStatus switch
        {
            OrderStatus.Pending => OrderStatus.OnHold,
            OrderStatus.Confirmed => OrderStatus.OnHold,
            OrderStatus.OnHold => OrderStatus.OnHold,
            _ => currentStatus
        };
    }

    private static void EnsurePaymentMatchesMarketplaceOrder(
        CommerceHubPayment payment,
        CommerceHubMarketplaceOrder marketplaceOrder)
    {
        if (!string.Equals(payment.Currency, marketplaceOrder.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_ORDER_CURRENCY_MISMATCH",
                "Marketplace payment currency does not match the marketplace order currency.",
                StatusCodes.Status409Conflict);
        }

        if (payment.Amount != marketplaceOrder.TotalAmount)
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_ORDER_AMOUNT_MISMATCH",
                "Marketplace payment amount does not match the marketplace order total.",
                StatusCodes.Status409Conflict);
        }
    }

    private static void EnsureIncomingProviderIdentityMatchesPayment(
        CommerceHubPayment payment,
        string? providerIntentId,
        string? providerTransactionId,
        bool isSuccessWebhook)
    {
        if (!string.IsNullOrWhiteSpace(payment.ProviderIntentId) &&
            !string.IsNullOrWhiteSpace(providerIntentId) &&
            !string.Equals(payment.ProviderIntentId, providerIntentId, StringComparison.OrdinalIgnoreCase))
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_PROVIDER_INTENT_MISMATCH",
                "The provider intent in the webhook does not match the CommerceHub payment session.",
                StatusCodes.Status409Conflict);
        }

        if (!string.IsNullOrWhiteSpace(payment.ProviderTransactionId) &&
            !string.IsNullOrWhiteSpace(providerTransactionId) &&
            !string.Equals(payment.ProviderTransactionId, providerTransactionId, StringComparison.OrdinalIgnoreCase) &&
            !CanAcceptSuccessTransactionAfterFailure(payment, providerIntentId, isSuccessWebhook))
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_PROVIDER_TRANSACTION_MISMATCH",
                "The provider transaction in the webhook does not match the CommerceHub payment record.",
                StatusCodes.Status409Conflict);
        }
    }

    private static bool CanAcceptSuccessTransactionAfterFailure(
        CommerceHubPayment payment,
        string? providerIntentId,
        bool isSuccessWebhook)
    {
        return isSuccessWebhook &&
               payment.Status == CommerceHubPaymentRecordStatus.Failed &&
               payment.IsProcessed &&
               !string.IsNullOrWhiteSpace(payment.ProviderIntentId) &&
               !string.IsNullOrWhiteSpace(providerIntentId) &&
               string.Equals(payment.ProviderIntentId, providerIntentId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanReconcileProcessedFailureToSuccess(
        CommerceHubPayment payment,
        string? providerIntentId)
    {
        return payment.Status == CommerceHubPaymentRecordStatus.Failed &&
               !string.IsNullOrWhiteSpace(payment.ProviderIntentId) &&
               !string.IsNullOrWhiteSpace(providerIntentId) &&
               string.Equals(payment.ProviderIntentId, providerIntentId, StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureProviderIdentityNotLinkedToAnotherPaymentAsync(
        PaymentProvider provider,
        string? providerIntentId,
        string? providerTransactionId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(providerIntentId))
        {
            var providerIntentInUse = await _dbContext.CommerceHubPayments
                .IgnoreQueryFilters()
                .AnyAsync(other =>
                    other.Id != paymentId &&
                    other.Provider == provider &&
                    other.ProviderIntentId == providerIntentId,
                    cancellationToken);

            if (providerIntentInUse)
            {
                throw new CommerceHubCheckoutException(
                    "PAYMENT_PROVIDER_INTENT_ALREADY_LINKED",
                    "The provider intent is already linked to another CommerceHub payment.",
                    StatusCodes.Status409Conflict);
            }
        }

        if (!string.IsNullOrWhiteSpace(providerTransactionId))
        {
            var providerTransactionInUse = await _dbContext.CommerceHubPayments
                .IgnoreQueryFilters()
                .AnyAsync(other =>
                    other.Id != paymentId &&
                    other.ProviderTransactionId == providerTransactionId,
                    cancellationToken);

            if (providerTransactionInUse)
            {
                throw new CommerceHubCheckoutException(
                    "PAYMENT_PROVIDER_TRANSACTION_ALREADY_LINKED",
                    "The provider transaction is already linked to another CommerceHub payment.",
                    StatusCodes.Status409Conflict);
            }
        }
    }

    private async Task<bool> PersistUnresolvedPaymentWebhookAsync(
        PaymentProvider provider,
        string providerCode,
        string eventType,
        string providerEventId,
        string? providerReference,
        string? paymentId,
        string? providerIntentId,
        string? providerTransactionId,
        decimal amount,
        string currency,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken,
        bool requiresSuspenseRouting = true,
        string? unresolvedStatus = null,
        string? unresolvedLastError = null)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginPayment();
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        var nowUtc = DateTime.UtcNow;
        var payloadHash = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload)))
            .ToLowerInvariant();

        // Resolve marketplace order early to determine if we need to acquire payment lock
        var marketplaceOrder = await ResolveMarketplaceOrderFromPaymentIdentityAsync(
            providerReference,
            paymentId,
            cancellationToken);

        if (marketplaceOrder == null && !string.IsNullOrWhiteSpace(providerReference))
        {
            marketplaceOrder = await ResolveMarketplaceOrderAsync(providerReference, cancellationToken);
        }

        // Acquire payment lock to serialize with InitiateAsync and prevent race condition
        string? paymentLockKey = null;
        string? paymentLockToken = null;
        string? deferredAuthorityReason = null;
        if (marketplaceOrder != null)
        {
            var attemptedPaymentLockKey = BuildPaymentLockKey(marketplaceOrder.Id, providerCode);
            var attemptedPaymentLockToken = Guid.NewGuid().ToString("N");
            var paymentLockResult = await _lockService.TryAcquireLockAsync(
                attemptedPaymentLockKey,
                attemptedPaymentLockToken,
                TimeSpan.FromMinutes(2));

            if (paymentLockResult.IsInfrastructureFailure)
            {
                _logger.LogError(
                    "CommerceHub unresolved payment webhook lock acquisition failed due to infrastructure failure | Provider: {Provider} | ProviderEventId: {ProviderEventId} | MarketplaceOrderId: {MarketplaceOrderId} | CorrelationId: {CorrelationId} | Metric: CommerceHub.DeferredAuthority.InfrastructureFailure",
                    providerCode,
                    providerEventId,
                    marketplaceOrder.Id,
                    correlationId);
                deferredAuthorityReason = "The payment authority lock infrastructure was unavailable during the first durable webhook persist attempt.";
            }
            else if (!paymentLockResult.IsAcquired)
            {
                _logger.LogWarning(
                    "CommerceHub unresolved payment webhook could not acquire payment lock (payment initiation in progress) | Provider: {Provider} | ProviderEventId: {ProviderEventId} | MarketplaceOrderId: {MarketplaceOrderId} | CorrelationId: {CorrelationId} | Metric: CommerceHub.DeferredAuthority.AlreadyHeld",
                    providerCode,
                    providerEventId,
                    marketplaceOrder.Id,
                    correlationId);
                deferredAuthorityReason = "Payment initiation still held the payment authority lock when the first durable webhook persist attempt arrived.";
            }
            else
            {
                paymentLockKey = attemptedPaymentLockKey;
                paymentLockToken = attemptedPaymentLockToken;
            }
        }

        var startedTransaction = _dbContext.Database.CurrentTransaction == null;
        await using var transaction = startedTransaction
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        try
        {
            var existing = await _dbContext.CommerceHubUnresolvedPaymentWebhooks
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    item => item.Provider == provider && item.ProviderEventId == providerEventId,
                    cancellationToken);

            var currentAffectedStoreIds = marketplaceOrder?.GetAffectedStoreIds()
                .Where(storeId => storeId != Guid.Empty)
                .Distinct()
                .OrderBy(storeId => storeId)
                .ToList() ?? [];
            var affectedStoreIds = ResolveFrozenAffectedStoreIds(existing, currentAffectedStoreIds);
            var tenantId = ResolveFrozenTenantAuthority(existing, affectedStoreIds);
            var orderId = ResolveFrozenOrderAuthority(existing, marketplaceOrder);
            var authoritativeCurrency = ResolveFrozenUnresolvedCurrency(existing, currency);
            var suspenseAllocations = BuildUnresolvedPaymentSuspenseAllocations(
                marketplaceOrder,
                affectedStoreIds,
                amount,
                existing);
            var expectedRoutedTenantIds = GetPositiveAllocationTenantIds(suspenseAllocations);
            var preserveExistingSuspenseRouting = ShouldPreserveExistingSuspenseRouting(existing);
            var canRouteToSuspense =
                deferredAuthorityReason == null &&
                requiresSuspenseRouting &&
                suspenseAllocations.Count > 0 &&
                SuspenseAllocationsCoverAffectedStores(suspenseAllocations, affectedStoreIds) &&
                amount > 0m &&
                HasAuthoritativeCurrency(authoritativeCurrency);
            var pendingRoutingError = CombineDistinctErrors(
                unresolvedLastError ??
                BuildDefaultUnresolvedPaymentRoutingError(affectedStoreIds, authoritativeCurrency),
                deferredAuthorityReason == null
                    ? null
                    : requiresSuspenseRouting
                        ? $"{deferredAuthorityReason} {DeferredCapturedPaymentAuthorityPersistenceMessage}"
                        : $"{deferredAuthorityReason} {DeferredRefundAuthorityPersistenceMessage}");

            var unresolved = existing ?? new CommerceHubUnresolvedPaymentWebhook
            {
                Id = InventoryEventIdFactory.Create("commercehub-unresolved-payment-webhook", providerEventId),
                Provider = provider,
                ProviderEventId = providerEventId,
                CreatedAt = nowUtc
            };

            unresolved.ProviderReference = providerReference;
            unresolved.LocalPaymentId = Guid.TryParse(paymentId, out var localPaymentId)
                ? localPaymentId
                : null;
            unresolved.ProviderIntentId = providerIntentId;
            unresolved.ProviderTransactionId = providerTransactionId;
            unresolved.TenantId = tenantId;
            unresolved.OrderId = orderId;
            unresolved.AffectedStoreIdsJson = SerializeTenantIds(affectedStoreIds);
            unresolved.Amount = amount;
            unresolved.Currency = authoritativeCurrency;
            unresolved.EventType = eventType;
            unresolved.PayloadHash = payloadHash;
            unresolved.RawPayload = provider == PaymentProvider.Paymob && _paymobArtifactProtector is not null
                ? _paymobArtifactProtector.ProtectWebhookPayload(rawPayload)
                : PointOfSale.Core.Security.WebhookDataRedactor.RedactPayload(rawPayload);
            unresolved.CorrelationId = correlationId;
            unresolved.LastResolutionAttemptAtUtc = nowUtc;
            unresolved.ResolutionAttemptCount += 1;
            unresolved.Status = canRouteToSuspense
                ? RoutedToSuspenseStatus
                : preserveExistingSuspenseRouting
                    ? existing!.Status
                    : unresolvedStatus ?? PendingTenantResolutionStatus;
            unresolved.LastError = canRouteToSuspense
                ? null
                : preserveExistingSuspenseRouting
                    ? existing!.LastError
                    : pendingRoutingError;
            unresolved.UpdatedAt = nowUtc;

            // PRODUCTION HARDENING: Clear stale suspense metadata when downgrading to PendingTenantResolution
            if (!canRouteToSuspense && !preserveExistingSuspenseRouting)
            {
                unresolved.RoutedToSuspenseAtUtc = null;
                unresolved.SuspenseRoutedStoreIdsJson = "[]";
            }

            if (existing == null)
            {
                _dbContext.CommerceHubUnresolvedPaymentWebhooks.Add(unresolved);
            }

            if (canRouteToSuspense)
            {
                var routedTenantIds = new List<Guid>();
                foreach (var allocation in suspenseAllocations)
                {
                    var idempotencyKey = $"CH_UNRESOLVED_PAYMENT:{allocation.TenantId}:{providerCode}:{providerEventId}";
                    var outboxExists = await _dbContext.AccountingOutboxMessages
                        .IgnoreQueryFilters()
                        .AnyAsync(
                            message => message.TenantId == allocation.TenantId &&
                                       message.IdempotencyKey == idempotencyKey,
                            cancellationToken);

                    if (outboxExists)
                    {
                        routedTenantIds.Add(allocation.TenantId);
                        continue;
                    }

                    var unresolvedPayload = JsonSerializer.Serialize(new
                    {
                        Gateway = providerCode,
                        Amount = allocation.Amount,
                        Currency = authoritativeCurrency,
                        ProviderEventId = providerEventId,
                        PaymentReference = providerReference ?? providerIntentId ?? providerTransactionId,
                        MarketplaceOrderId = orderId,
                        AffectedStoreIds = affectedStoreIds,
                        SuspenseTenantId = allocation.TenantId
                    });

                    _dbContext.AccountingOutboxMessages.Add(AccountingOutboxMessage.Create(
                        tenantId: allocation.TenantId,
                        eventType: "UnresolvedOnlinePayment",
                        businessEntityId: orderId ?? Guid.Empty,
                        entityTypeName: "UnresolvedPayment",
                        payloadJson: unresolvedPayload,
                        idempotencyKey: idempotencyKey,
                        correlationId: correlationId,
                        sourceType: "UnresolvedPayment"));

                    routedTenantIds.Add(allocation.TenantId);
                }

                var normalizedRoutedTenantIds = routedTenantIds
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();

                if (!normalizedRoutedTenantIds.SequenceEqual(expectedRoutedTenantIds))
                {
                    unresolved.Status = "PendingTenantResolution";
                    unresolved.SuspenseRoutedStoreIdsJson = "[]";
                    unresolved.RoutedToSuspenseAtUtc = null;
                    unresolved.LastError =
                        "Provider-success CommerceHub webhook was not routed to suspense because not every financially exposed store received a durable accounting outbox route.";
                    canRouteToSuspense = false;
                }
                else
                {
                    unresolved.SuspenseRoutedStoreIdsJson = SerializeTenantIds(normalizedRoutedTenantIds);
                    unresolved.RoutedToSuspenseAtUtc ??= nowUtc;
                }
            }

            if (deferredAuthorityReason == null &&
                requiresSuspenseRouting &&
                marketplaceOrder != null &&
                affectedStoreIds.Count > 0)
            {
                await MarkKnownUnresolvedProviderSuccessOrderOnHoldAsync(
                    marketplaceOrder,
                    correlationId,
                    cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            _logger.LogCritical(
                "CommerceHub provider-success webhook could not be linked to a local payment row | Provider: {Provider} | EventType: {EventType} | ProviderEventId: {ProviderEventId} | ProviderReference: {ProviderReference} | ProviderIntentId: {ProviderIntentId} | RoutedToSuspense: {RoutedToSuspense} | TenantId: {TenantId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                providerCode,
                eventType,
                providerEventId,
                providerReference,
                providerIntentId,
                canRouteToSuspense,
                tenantId,
                orderId,
                correlationId);

            return true;
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (paymentLockKey != null && paymentLockToken != null)
            {
                await _lockService.ReleaseLockAsync(paymentLockKey, paymentLockToken);
            }
        }
    }

    private async Task MarkKnownUnresolvedProviderSuccessOrderOnHoldAsync(
        CommerceHubMarketplaceOrder? marketplaceOrder,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (marketplaceOrder == null ||
            marketplaceOrder.PaymentStatus != PaymentStatus.Pending ||
            marketplaceOrder.Status is OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.Completed or OrderStatus.OnHold)
        {
            return;
        }

        marketplaceOrder.Status = OrderStatus.OnHold;
        marketplaceOrder.TouchProjectionMutationVersion();

        var affectedStoreIds = marketplaceOrder.GetAffectedStoreIds()
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (affectedStoreIds.Count == 0)
        {
            throw new InvalidOperationException(
                $"Invariant violation: unresolved provider-success hold for marketplace order {marketplaceOrder.Id} has no affected store authorities.");
        }

        var eventVersion = CommerceHubOrderEventMetadata.ResolveAggregateEventVersion(marketplaceOrder);
        await _eventPublisher.PublishAsync(new CommerceHubMarketplaceOrderStatusUpdatedEvent
        {
            MarketplaceOrderId = marketplaceOrder.Id,
            TenantId = affectedStoreIds.First(),
            AffectedStoreIds = affectedStoreIds,
            CorrelationId = correlationId,
            IdempotencyKey = CommerceHubOrderEventMetadata.BuildMarketplacePaymentIdempotencyKey(
                marketplaceOrder,
                eventVersion),
            OccurredAtUtc = DateTime.UtcNow,
            EventVersion = eventVersion
        }, cancellationToken);
    }

    private static bool SuspenseAllocationsCoverAffectedStores(
        IReadOnlyCollection<UnresolvedPaymentSuspenseAllocation> allocations,
        IReadOnlyCollection<Guid> affectedStoreIds)
    {
        if (affectedStoreIds.Count == 0 || allocations.Count == 0)
        {
            return false;
        }

        var allocationTenantIds = allocations
            .Where(allocation => allocation.Amount > 0m && allocation.TenantId != Guid.Empty)
            .Select(allocation => allocation.TenantId)
            .Distinct()
            .OrderBy(tenantId => tenantId)
            .ToList();

        return allocationTenantIds.Count > 0 &&
               allocationTenantIds.All(affectedStoreIds.Contains);
    }

    private static List<UnresolvedPaymentSuspenseAllocation> BuildUnresolvedPaymentSuspenseAllocations(
        CommerceHubMarketplaceOrder? marketplaceOrder,
        IReadOnlyCollection<Guid> affectedStoreIds,
        decimal amount,
        CommerceHubUnresolvedPaymentWebhook? existingUnresolved = null)
    {
        if (amount <= 0m || affectedStoreIds.Count == 0)
        {
            return [];
        }

        if (marketplaceOrder == null)
        {
            // Preserve existing routed-to-suspense allocations if available
            if (existingUnresolved != null && existingUnresolved.RoutedToSuspenseAtUtc.HasValue)
            {
                var reconstructed = ReconstructSuspenseAllocationsFromJson(
                    amount,
                    existingUnresolved.SuspenseRoutedStoreIdsJson);
                
                if (reconstructed.Count > 0)
                {
                    return reconstructed;
                }
            }
            
            return [];
        }

        var subOrderTotals = marketplaceOrder.SubOrders
            .Where(subOrder => !subOrder.IsDeleted && subOrder.StoreId != Guid.Empty)
            .GroupBy(subOrder => subOrder.StoreId)
            .Select(group => new
            {
                TenantId = group.Key,
                Total = group.Sum(subOrder => Math.Max(0m, subOrder.Total))
            })
            .Where(row => row.Total > 0m)
            .OrderBy(row => row.TenantId)
            .ToList();

        if (subOrderTotals.Count == 0)
        {
            // Preserve existing routed-to-suspense allocations if available
            if (existingUnresolved != null && existingUnresolved.RoutedToSuspenseAtUtc.HasValue)
            {
                var reconstructed = ReconstructSuspenseAllocationsFromJson(
                    amount,
                    existingUnresolved.SuspenseRoutedStoreIdsJson);
                
                if (reconstructed.Count > 0)
                {
                    return reconstructed;
                }
            }
            
            return [];
        }

        var total = subOrderTotals.Sum(row => row.Total);
        var allocated = 0m;
        var allocations = new List<UnresolvedPaymentSuspenseAllocation>(subOrderTotals.Count);
        for (var index = 0; index < subOrderTotals.Count; index++)
        {
            var row = subOrderTotals[index];
            var allocation = index == subOrderTotals.Count - 1
                ? Math.Round(amount - allocated, 2, MidpointRounding.AwayFromZero)
                : Math.Round(amount * (row.Total / total), 2, MidpointRounding.AwayFromZero);

            allocated += allocation;
            if (allocation > 0m)
            {
                allocations.Add(new UnresolvedPaymentSuspenseAllocation(row.TenantId, allocation));
            }
        }

        return allocations;
    }

    /// <summary>
    /// Reconstructs suspense allocations from a frozen JSON snapshot of routed tenant IDs.
    /// Used when marketplace order data is unavailable but we have a historical routing record.
    /// </summary>
    private static List<UnresolvedPaymentSuspenseAllocation> ReconstructSuspenseAllocationsFromJson(
        decimal amount,
        string? suspenseRoutedStoreIdsJson)
    {
        if (string.IsNullOrWhiteSpace(suspenseRoutedStoreIdsJson) || suspenseRoutedStoreIdsJson == "[]")
        {
            return [];
        }

        try
        {
            var routedTenantIds = JsonSerializer.Deserialize<List<Guid>>(suspenseRoutedStoreIdsJson);
            if (routedTenantIds == null || routedTenantIds.Count == 0)
            {
                return [];
            }

            // Filter out invalid (Guid.Empty) and deduplicate tenant IDs while preserving order
            var validTenantIds = routedTenantIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (validTenantIds.Count == 0)
            {
                return [];
            }

            // Reconstruct allocations from frozen routed tenant IDs
            var equalShare = Math.Round(amount / validTenantIds.Count, 2, MidpointRounding.ToZero);
            var remainder = amount - (equalShare * validTenantIds.Count);

            return validTenantIds
                .Select((tenantId, index) => new UnresolvedPaymentSuspenseAllocation(
                    tenantId,
                    index == 0 ? equalShare + remainder : equalShare))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool HasAuthoritativeCurrency(string currency) =>
        string.Equals(currency, FinancialCurrencyGuard.PlatformBaseCurrencyCode, StringComparison.OrdinalIgnoreCase);

    private static List<Guid> ResolveFrozenAffectedStoreIds(
        CommerceHubUnresolvedPaymentWebhook? existing,
        IReadOnlyCollection<Guid> incomingAffectedStoreIds)
    {
        var existingAffectedStoreIds = ParseTenantIds(existing?.AffectedStoreIdsJson);
        if (existingAffectedStoreIds.Count > 0)
        {
            return existingAffectedStoreIds;
        }

        return incomingAffectedStoreIds
            .Where(storeId => storeId != Guid.Empty)
            .Distinct()
            .OrderBy(storeId => storeId)
            .ToList();
    }

    private static Guid? ResolveFrozenTenantAuthority(
        CommerceHubUnresolvedPaymentWebhook? existing,
        IReadOnlyCollection<Guid> affectedStoreIds)
    {
        if (existing?.TenantId.HasValue == true && existing.TenantId.Value != Guid.Empty)
        {
            return existing.TenantId.Value;
        }

        return affectedStoreIds.Count == 1 ? affectedStoreIds.First() : null;
    }

    private static Guid? ResolveFrozenOrderAuthority(
        CommerceHubUnresolvedPaymentWebhook? existing,
        CommerceHubMarketplaceOrder? marketplaceOrder)
    {
        if (existing?.OrderId.HasValue == true && existing.OrderId.Value != Guid.Empty)
        {
            return existing.OrderId.Value;
        }

        return marketplaceOrder?.Id;
    }

    private static string ResolveFrozenUnresolvedCurrency(
        CommerceHubUnresolvedPaymentWebhook? existing,
        string incomingCurrency)
    {
        if (existing != null && HasAuthoritativeCurrency(existing.Currency))
        {
            return existing.Currency;
        }

        return RequireCommerceHubPaymentCurrency(incomingCurrency, "unresolved payment reconciliation");
    }

    private static List<Guid> GetPositiveAllocationTenantIds(
        IReadOnlyCollection<UnresolvedPaymentSuspenseAllocation> allocations)
    {
        return allocations
            .Where(allocation => allocation.Amount > 0m && allocation.TenantId != Guid.Empty)
            .Select(allocation => allocation.TenantId)
            .Distinct()
            .OrderBy(tenantId => tenantId)
            .ToList();
    }

    private static string BuildDefaultUnresolvedPaymentRoutingError(
        IReadOnlyCollection<Guid> affectedStoreIds,
        string currency)
    {
        if (!HasAuthoritativeCurrency(currency))
        {
            return "Provider-success CommerceHub webhook did not include a valid ISO currency and cannot be routed to suspense automatically.";
        }

        if (affectedStoreIds.Count == 0)
        {
            return "Provider-success CommerceHub webhook could not be routed to suspense because the affected tenant scope is unknown.";
        }

        return "Provider-success CommerceHub webhook could not be routed to suspense because the authoritative store allocation could not be durably established.";
    }

    private static bool ShouldPreserveExistingSuspenseRouting(CommerceHubUnresolvedPaymentWebhook? existing) =>
        existing != null &&
        existing.RoutedToSuspenseAtUtc.HasValue &&
        !string.IsNullOrWhiteSpace(existing.SuspenseRoutedStoreIdsJson) &&
        !string.Equals(existing.SuspenseRoutedStoreIdsJson, "[]", StringComparison.Ordinal);

    private static string? CombineDistinctErrors(string? primary, string? secondary)
    {
        if (string.IsNullOrWhiteSpace(primary))
        {
            return secondary;
        }

        if (string.IsNullOrWhiteSpace(secondary) ||
            string.Equals(primary, secondary, StringComparison.Ordinal))
        {
            return primary;
        }

        return $"{primary} {secondary}";
    }

    private static List<Guid> ParseTenantIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var tenantIds = new List<Guid>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(item.GetString(), out var tenantId) &&
                    tenantId != Guid.Empty)
                {
                    tenantIds.Add(tenantId);
                }
            }

            return tenantIds
                .Distinct()
                .OrderBy(tenantId => tenantId)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string SerializeTenantIds(IReadOnlyCollection<Guid> tenantIds) =>
        JsonSerializer.Serialize(
            tenantIds
                .Where(tenantId => tenantId != Guid.Empty)
                .Distinct()
                .OrderBy(tenantId => tenantId)
                .ToList());

    private static string SerializeStoreAllocationSnapshot(
        IReadOnlyCollection<UnresolvedPaymentSuspenseAllocation> allocations) =>
        JsonSerializer.Serialize(
            allocations
                .Where(allocation => allocation.TenantId != Guid.Empty)
                .OrderBy(allocation => allocation.TenantId)
                .Select(allocation => new StoreAllocationSnapshotEntry(
                    allocation.TenantId,
                    Math.Round(allocation.Amount, 2, MidpointRounding.AwayFromZero)))
                .ToList());

    private static string SerializeRefundAllocationSnapshot(
        IReadOnlyCollection<ProviderRefundAllocationSnapshotEntry> allocations) =>
        JsonSerializer.Serialize(
            allocations
                .OrderBy(allocation => allocation.SubOrderId)
                .Select(allocation => new
                {
                    allocation.SubOrderId,
                    allocation.TenantId,
                    allocation.Amount,
                    allocation.IsFullRefund,
                    allocation.RefundedAmountBefore,
                    allocation.CommissionReversalAmount,
                    allocation.PromoDiscountReversalAmount,
                    allocation.VendorEarningReversalAmount
                })
                .ToList());

    private async Task<IReadOnlyCollection<ProviderRefundAllocationSnapshotEntry>> BuildProviderRefundAllocationSnapshotAsync(
        CommerceHubPayment payment,
        decimal refundAmount,
        CancellationToken cancellationToken)
    {
        if (payment.Order == null)
        {
            throw new InvalidOperationException(
                $"CommerceHub provider refund reconciliation cannot snapshot allocations because payment {payment.Id} has no marketplace order loaded.");
        }

        var paymentItems = payment.Items
            .Where(item => item.Amount > 0m)
            .OrderBy(item => item.SubOrderId)
            .ToList();

        if (paymentItems.Count == 0)
        {
            throw new InvalidOperationException(
                $"CommerceHub provider refund reconciliation cannot snapshot allocations because payment {payment.Id} has no immutable CommerceHubPaymentItem records.");
        }

        // CURRENCY FIX: Use currency-aware rounding for zero-decimal (JPY, KRW) and three-decimal (BHD, KWD) currencies
        var refundedBySubOrder = payment.Refunds
            .Where(refund =>
                refund.Status != CommerceHubRefundStatus.Failed &&
                !refund.IsDeleted)
            .GroupBy(refund => refund.SubOrderId)
            .ToDictionary(
                group => group.Key,
                group => RoundCurrencyAware(group.Sum(refund => refund.Amount), payment.Currency));

        var provisionalBySubOrder = await GetPendingProviderRefundAllocationsAsync(payment.Id, payment.Currency, cancellationToken);

        var subOrdersById = payment.Order.SubOrders.ToDictionary(subOrder => subOrder.Id);
        var remaining = RoundCurrencyAware(refundAmount, payment.Currency);
        var allocations = new List<ProviderRefundAllocationSnapshotEntry>();

        foreach (var paymentItem in paymentItems)
        {
            if (!subOrdersById.TryGetValue(paymentItem.SubOrderId, out var subOrder) ||
                !IsOnlineSubOrder(subOrder))
            {
                continue;
            }

            var refundedAmountBefore = RoundCurrencyAware(
                refundedBySubOrder.GetValueOrDefault(subOrder.Id) +
                provisionalBySubOrder.GetValueOrDefault(subOrder.Id),
                payment.Currency);
            var remainingForSubOrder = RoundCurrencyAware(paymentItem.Amount - refundedAmountBefore, payment.Currency);
            if (remainingForSubOrder <= 0m)
            {
                continue;
            }

            var allocationAmount = RoundCurrencyAware(Math.Min(remaining, remainingForSubOrder), payment.Currency);
            if (allocationAmount <= 0m)
            {
                continue;
            }

            var totalAfterRefund = RoundCurrencyAware(refundedAmountBefore + allocationAmount, payment.Currency);
            var isFullRefund = totalAfterRefund >= RoundCurrencyAware(subOrder.Total, payment.Currency);
            var breakdown = RefundFinancialCalculator.CalculateSubOrderBreakdown(
                subOrder,
                refundedAmountBefore,
                allocationAmount,
                isFullRefund);

            allocations.Add(new ProviderRefundAllocationSnapshotEntry(
                subOrder.Id,
                subOrder.StoreId,
                allocationAmount,
                isFullRefund,
                refundedAmountBefore,
                breakdown.CommissionReversalAmount,
                breakdown.PromoDiscountReversalAmount,
                breakdown.VendorEarningReversalAmount));

            remaining = RoundCurrencyAware(remaining - allocationAmount, payment.Currency);
            if (remaining <= 0m)
            {
                break;
            }
        }

        if (remaining > 0m)
        {
            throw new InvalidOperationException(
                $"CommerceHub provider refund reconciliation could not allocate {remaining:0.00} {payment.Currency} across immutable payment items for payment {payment.Id}.");
        }

        return allocations;
    }

    private async Task<Dictionary<Guid, decimal>> GetPendingProviderRefundAllocationsAsync(
        Guid paymentId,
        string currency,
        CancellationToken cancellationToken)
    {
        var pendingMessages = await _dbContext.CommerceHubPaymentCompensationOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.PaymentId == paymentId &&
                message.ProcessedAtUtc == null &&
                !message.IsDeleted &&
                !message.RequiresOperatorReconciliation &&
                EF.Functions.ILike(message.IdempotencyKey, "chpay-provider-refund:%"))
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Select(message => new
            {
                message.Id,
                message.RefundAllocationSnapshotJson
            })
            .ToListAsync(cancellationToken);

        var allocations = new Dictionary<Guid, decimal>();
        foreach (var pendingMessage in pendingMessages)
        {
            foreach (var allocation in ParseRefundAllocationSnapshot(
                         pendingMessage.RefundAllocationSnapshotJson,
                         pendingMessage.Id,
                         currency))
            {
                allocations[allocation.SubOrderId] = RoundCurrencyAware(
                    allocations.GetValueOrDefault(allocation.SubOrderId) + allocation.Amount,
                    currency);
            }
        }

        return allocations;
    }

    private static IReadOnlyCollection<ProviderRefundAllocationSnapshotEntry> ParseRefundAllocationSnapshot(
        string? json,
        Guid messageId,
        string currency)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                $"CommerceHub provider refund compensation {messageId} is missing an immutable refund allocation snapshot.");
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"CommerceHub provider refund compensation {messageId} contains a malformed refund allocation snapshot.");
        }

        var allocations = new List<ProviderRefundAllocationSnapshotEntry>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("SubOrderId", out var subOrderIdProperty) ||
                subOrderIdProperty.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(subOrderIdProperty.GetString(), out var subOrderId) ||
                subOrderId == Guid.Empty)
            {
                throw new InvalidOperationException("Provider refund allocation snapshot contains an invalid sub-order id.");
            }

            if (!item.TryGetProperty("TenantId", out var tenantIdProperty) ||
                tenantIdProperty.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(tenantIdProperty.GetString(), out var tenantId) ||
                tenantId == Guid.Empty)
            {
                throw new InvalidOperationException("Provider refund allocation snapshot contains an invalid tenant id.");
            }

            if (!item.TryGetProperty("Amount", out var amountProperty) ||
                amountProperty.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidOperationException("Provider refund allocation snapshot contains an invalid amount.");
            }

            if (!item.TryGetProperty("IsFullRefund", out var isFullRefundProperty) ||
                (isFullRefundProperty.ValueKind != JsonValueKind.True &&
                 isFullRefundProperty.ValueKind != JsonValueKind.False))
            {
                throw new InvalidOperationException("Provider refund allocation snapshot contains an invalid full-refund flag.");
            }

            allocations.Add(new ProviderRefundAllocationSnapshotEntry(
                subOrderId,
                tenantId,
                RoundCurrencyAware(amountProperty.GetDecimal(), currency),
                isFullRefundProperty.GetBoolean(),
                ReadRoundedSnapshotDecimal(item, "RefundedAmountBefore", currency),
                ReadRoundedSnapshotDecimal(item, "CommissionReversalAmount", currency),
                ReadRoundedSnapshotDecimal(item, "PromoDiscountReversalAmount", currency),
                ReadRoundedSnapshotDecimal(item, "VendorEarningReversalAmount", currency)));
        }

        if (allocations.Count == 0)
        {
            throw new InvalidOperationException(
                $"CommerceHub provider refund compensation {messageId} has an empty immutable refund allocation snapshot.");
        }

        return allocations;
    }

    private static decimal ReadRoundedSnapshotDecimal(JsonElement item, string propertyName, string currency)
    {
        if (!item.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidOperationException(
                $"Provider refund allocation snapshot is missing numeric property '{propertyName}'.");
        }

        return RoundCurrencyAware(property.GetDecimal(), currency);
    }

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundCurrencyAware(decimal amount, string currency)
    {
        var decimalPlaces = PointOfSale.Infrastructure.Payments.CurrencyHelper.GetDecimalPlaces(currency);
        return Math.Round(amount, decimalPlaces, MidpointRounding.AwayFromZero);
    }

    private sealed record UnresolvedPaymentSuspenseAllocation(Guid TenantId, decimal Amount);

    private async Task<CommerceHubMarketplaceOrder?> ResolveMarketplaceOrderFromPaymentIdentityAsync(
        string? providerReference,
        string? paymentId,
        CancellationToken cancellationToken)
    {
        var candidateIds = new[] { providerReference, paymentId }
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidateId in candidateIds)
        {
            if (!Guid.TryParse(candidateId, out var commerceHubPaymentId))
            {
                continue;
            }

            var payment = await _dbContext.CommerceHubPayments
                .IgnoreQueryFilters()
                .Include(item => item.Order)
                .ThenInclude(order => order.SubOrders)
                .ThenInclude(subOrder => subOrder.Items)
                .FirstOrDefaultAsync(
                    item =>
                        item.Id == commerceHubPaymentId &&
                        !item.IsDeleted &&
                        !item.Order.IsDeleted,
                    cancellationToken);

            if (payment?.Order != null)
            {
                return payment.Order;
            }
        }

        return null;
    }

    private static void MarkPaymentIgnored(
        CommerceHubPayment payment,
        CommerceHubPaymentAttempt paymentAttempt,
        string? providerIntentId,
        string paymentReferenceId,
        string reason,
        DateTime nowUtc)
    {
        payment.Status = CommerceHubPaymentRecordStatus.Ignored;
        payment.ProviderIntentId ??= providerIntentId;
        payment.ProviderTransactionId = paymentReferenceId;
        payment.FailureReason = reason;
        payment.PaidAtUtc = null;
        payment.IsProcessed = true;
        payment.ProcessedAtUtc ??= nowUtc;

        paymentAttempt.ProviderIntentId ??= providerIntentId;
        paymentAttempt.ProviderTransactionId = paymentReferenceId;
        paymentAttempt.Status = CommerceHubPaymentRecordStatus.Ignored;
        paymentAttempt.FailureReason = reason;
        paymentAttempt.CompletedAtUtc ??= nowUtc;
    }

    private void MarkPaymentRequiresCompensation(
        CommerceHubPayment payment,
        CommerceHubPaymentAttempt paymentAttempt,
        string? providerIntentId,
        string paymentReferenceId,
        string reason,
        string correlationId,
        DateTime nowUtc)
    {
        payment.Status = CommerceHubPaymentRecordStatus.RefundRequired;
        payment.ProviderIntentId ??= providerIntentId;
        payment.ProviderTransactionId = paymentReferenceId;
        payment.FailureReason = $"REFUND_COMPENSATION_REQUIRED: {reason}";
        payment.PaidAtUtc = null;
        payment.IsProcessed = true;
        payment.ProcessedAtUtc ??= nowUtc;

        paymentAttempt.ProviderIntentId ??= providerIntentId;
        paymentAttempt.ProviderTransactionId = paymentReferenceId;
        paymentAttempt.Status = CommerceHubPaymentRecordStatus.RefundRequired;
        paymentAttempt.FailureReason = payment.FailureReason;
        paymentAttempt.CompletedAtUtc ??= nowUtc;

        var idempotencyKey = BuildPaymentCompensationIdempotencyKey(payment, paymentReferenceId);
        var exists = _dbContext.CommerceHubPaymentCompensationOutboxMessages
            .Local
            .Any(message => message.IdempotencyKey == idempotencyKey) ||
            _dbContext.CommerceHubPaymentCompensationOutboxMessages
                .IgnoreQueryFilters()
                .Any(message => message.IdempotencyKey == idempotencyKey);

        if (!exists)
        {
            var affectedStoreIds = payment.Order.SubOrders
                .Select(subOrder => subOrder.StoreId)
                .Where(storeId => storeId != Guid.Empty)
                .Distinct()
                .OrderBy(storeId => storeId)
                .ToList();

            _dbContext.CommerceHubPaymentCompensationOutboxMessages.Add(new CommerceHubPaymentCompensationOutboxMessage
            {
                Id = InventoryEventIdFactory.Create("commercehub-payment-compensation", idempotencyKey),
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                TenantId = affectedStoreIds.Count == 1 ? affectedStoreIds[0] : null,
                AffectedStoreIdsJson = JsonSerializer.Serialize(affectedStoreIds),
                StoreAllocationSnapshotJson = SerializeStoreAllocationSnapshot(
                    BuildUnresolvedPaymentSuspenseAllocations(
                        payment.Order,
                        affectedStoreIds,
                        payment.Amount)),
                Provider = payment.Provider,
                ProviderIntentId = providerIntentId ?? payment.ProviderIntentId,
                ProviderTransactionId = paymentReferenceId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                Reason = reason,
                IdempotencyKey = idempotencyKey,
                CorrelationId = correlationId,
                CreatedAt = nowUtc
            });
        }
    }

    private async Task MarkPaymentProviderRefundConfirmedAsync(
        CommerceHubPayment payment,
        CommerceHubPaymentAttempt paymentAttempt,
        string? providerIntentId,
        string? providerTransactionId,
        string refundReferenceId,
        decimal refundAmount,
        string reason,
        string correlationId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        payment.Status = CommerceHubPaymentRecordStatus.RefundRequired;
        payment.ProviderIntentId ??= providerIntentId;
        payment.ProviderTransactionId ??= providerTransactionId;
        payment.FailureReason = $"PROVIDER_REFUND_ACCOUNTING_REQUIRED: {reason}";
        payment.IsProcessed = true;
        payment.ProcessedAtUtc ??= nowUtc;

        paymentAttempt.ProviderIntentId ??= providerIntentId;
        paymentAttempt.ProviderTransactionId ??= providerTransactionId ?? refundReferenceId;
        paymentAttempt.Status = CommerceHubPaymentRecordStatus.RefundRequired;
        paymentAttempt.FailureReason = payment.FailureReason;
        paymentAttempt.CompletedAtUtc ??= nowUtc;

        var idempotencyKey = BuildPaymentProviderRefundCompensationIdempotencyKey(payment, refundReferenceId, refundAmount);
        var exists = _dbContext.CommerceHubPaymentCompensationOutboxMessages
            .Local
            .Any(message => message.IdempotencyKey == idempotencyKey) ||
            _dbContext.CommerceHubPaymentCompensationOutboxMessages
                .IgnoreQueryFilters()
                .Any(message => message.IdempotencyKey == idempotencyKey);

        if (!exists)
        {
            var affectedStoreIds = payment.Order.SubOrders
                .Select(subOrder => subOrder.StoreId)
                .Where(storeId => storeId != Guid.Empty)
                .Distinct()
                .OrderBy(storeId => storeId)
                .ToList();

            _dbContext.CommerceHubPaymentCompensationOutboxMessages.Add(new CommerceHubPaymentCompensationOutboxMessage
            {
                Id = InventoryEventIdFactory.Create("commercehub-provider-refund-reconciliation", idempotencyKey),
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                TenantId = affectedStoreIds.Count == 1 ? affectedStoreIds[0] : null,
                AffectedStoreIdsJson = JsonSerializer.Serialize(affectedStoreIds),
                StoreAllocationSnapshotJson = SerializeStoreAllocationSnapshot(
                    BuildUnresolvedPaymentSuspenseAllocations(
                        payment.Order,
                        affectedStoreIds,
                        refundAmount)),
                RefundAllocationSnapshotJson = SerializeRefundAllocationSnapshot(
                    await BuildProviderRefundAllocationSnapshotAsync(payment, refundAmount, cancellationToken)),
                Provider = payment.Provider,
                ProviderIntentId = providerIntentId ?? payment.ProviderIntentId,
                ProviderTransactionId = providerTransactionId ?? payment.ProviderTransactionId,
                Amount = refundAmount,
                Currency = payment.Currency,
                Reason = reason,
                IdempotencyKey = idempotencyKey,
                CorrelationId = correlationId,
                GatewayReference = refundReferenceId,
                GatewayStatus = "confirmed",
                CreatedAt = nowUtc
            });
        }
    }

    private static string BuildPaymentCompensationIdempotencyKey(
        CommerceHubPayment payment,
        string paymentReferenceId)
    {
        var referenceHash = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paymentReferenceId)))
            .ToLowerInvariant()[..24];
        return $"chpay-comp:{payment.Id:N}:{referenceHash}";
    }

    private static string BuildPaymentProviderRefundCompensationIdempotencyKey(
        CommerceHubPayment payment,
        string refundReferenceId,
        decimal refundAmount)
    {
        var rawKey = FormattableString.Invariant($"{refundReferenceId}:{refundAmount:0.00}");
        var referenceHash = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)))
            .ToLowerInvariant()[..24];
        return $"chpay-provider-refund:{payment.Id:N}:{referenceHash}";
    }

    private void DetachPendingPaymentLogs(Guid paymentId, string eventType)
    {
        foreach (var entry in _dbContext.ChangeTracker
                     .Entries<CommerceHubPaymentLog>()
                     .Where(entry =>
                         entry.State == EntityState.Added &&
                         entry.Entity.PaymentId == paymentId &&
                         string.Equals(entry.Entity.EventType, eventType, StringComparison.Ordinal)))
        {
            entry.State = EntityState.Detached;
        }
    }

    private async Task<string> ResolveMarketplaceOrderCountryCodeAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CancellationToken cancellationToken)
    {
        // Get all unique store IDs from active online suborders
        var storeIds = marketplaceOrder.SubOrders
            .Where(subOrder => !subOrder.IsDeleted && IsOnlineSubOrder(subOrder))
            .Select(subOrder => subOrder.StoreId)
            .Where(storeId => storeId != Guid.Empty)
            .Distinct()
            .ToList();

        if (storeIds.Count == 0)
        {
            _logger.LogError(
                "No valid store IDs found in marketplace order | MarketplaceOrderId: {MarketplaceOrderId}",
                marketplaceOrder.Id);
            
            throw new CommerceHubCheckoutException(
                "NO_VALID_STORES",
                "Cannot process payment: no valid online stores found in the order.",
                StatusCodes.Status400BadRequest);
        }

        // Fetch country codes for all stores
        var storeCountries = await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => storeIds.Contains(tenant.Id))
            .Select(tenant => new { tenant.Id, tenant.Country })
            .ToListAsync(cancellationToken);

        // Validate all stores were found
        if (storeCountries.Count != storeIds.Count)
        {
            var foundStoreIds = storeCountries.Select(s => s.Id).ToHashSet();
            var missingStoreIds = storeIds.Except(foundStoreIds).ToList();
            
            _logger.LogError(
                "Store tenants not found in database | MarketplaceOrderId: {MarketplaceOrderId} | MissingStoreIds: [{MissingStoreIds}] | ExpectedCount: {ExpectedCount} | FoundCount: {FoundCount}",
                marketplaceOrder.Id,
                string.Join(", ", missingStoreIds),
                storeIds.Count,
                storeCountries.Count);

            throw new CommerceHubCheckoutException(
                "STORE_NOT_FOUND",
                "One or more stores in the order could not be found.",
                StatusCodes.Status400BadRequest);
        }

        // Normalize all country codes and validate they exist
        var normalizedCountries = new List<(Guid Id, string NormalizedCountry)>();
        var storesWithMissingCountry = new List<Guid>();
        
        foreach (var store in storeCountries)
        {
            if (CountryCodeHelper.TryNormalizeCountryCode(store.Country, out var normalizedCountry))
            {
                normalizedCountries.Add((store.Id, normalizedCountry));
            }
            else
            {
                storesWithMissingCountry.Add(store.Id);
            }
        }

        if (storesWithMissingCountry.Any())
        {
            _logger.LogError(
                "Stores lack authoritative country codes | MarketplaceOrderId: {MarketplaceOrderId} | StoresWithMissingCountry: [{StoreIds}]",
                marketplaceOrder.Id,
                string.Join(", ", storesWithMissingCountry));

            throw new CommerceHubCheckoutException(
                "MISSING_STORE_COUNTRY",
                "Cannot process payment: one or more stores do not have a valid country code configured.",
                StatusCodes.Status400BadRequest);
        }

        // Get distinct countries
        var distinctCountries = normalizedCountries
            .Select(store => store.NormalizedCountry)
            .Distinct()
            .ToList();

        // Validate all stores are in the same country
        if (distinctCountries.Count > 1)
        {
            var storeCountryDetails = string.Join(", ", normalizedCountries.Select(s => $"Store {s.Id}: {s.NormalizedCountry}"));
            _logger.LogError(
                "Marketplace order spans multiple countries | MarketplaceOrderId: {MarketplaceOrderId} | Countries: [{Countries}] | StoreDetails: {StoreDetails}",
                marketplaceOrder.Id,
                string.Join(", ", distinctCountries),
                storeCountryDetails);

            throw new CommerceHubCheckoutException(
                "MIXED_COUNTRY_ORDER",
                "Cannot process payment for orders spanning multiple countries. Please place separate orders for each country.",
                StatusCodes.Status400BadRequest);
        }

        return distinctCountries.First();
    }

    private static bool MatchesExistingSuccess(
        CommerceHubPayment payment,
        string? transactionId,
        string? providerIntentId)
    {
        return string.Equals(payment.ProviderTransactionId, transactionId, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(providerIntentId) &&
                string.Equals(payment.ProviderIntentId, providerIntentId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesExistingFailure(
        CommerceHubPayment payment,
        string? transactionId,
        string? providerIntentId)
    {
        return string.Equals(payment.ProviderTransactionId, transactionId, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(providerIntentId) &&
                string.Equals(payment.ProviderIntentId, providerIntentId, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureAmountMatches(
        CommerceHubPayment payment,
        decimal providerAmount,
        string currency,
        string providerCode)
    {
        if (!string.Equals(payment.Currency, currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_CURRENCY_MISMATCH",
                $"{providerCode} webhook currency does not match the marketplace payment record.",
                StatusCodes.Status409Conflict);
        }

        if (payment.Amount != providerAmount)
        {
            throw new CommerceHubCheckoutException(
                "PAYMENT_AMOUNT_MISMATCH",
                $"{providerCode} webhook amount does not match the marketplace payment record.",
                StatusCodes.Status409Conflict);
        }
    }

    private static bool IsProviderPaymentIdentityUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
               (string.Equals(postgresException.ConstraintName, "UX_CommerceHubPayments_Provider_ProviderIntentId", StringComparison.Ordinal) ||
                string.Equals(postgresException.ConstraintName, "IX_CommerceHubPayments_ProviderTransactionId", StringComparison.Ordinal) ||
                string.Equals(postgresException.ConstraintName, "IX_CommerceHubPayments_ProviderReference", StringComparison.Ordinal));
    }

    private CommerceHubPaymentInitiateResponseDto MapInitiateResponse(
        CommerceHubPayment payment,
        CommerceHubMarketplaceOrder marketplaceOrder,
        string providerCode,
        bool isReusable,
        string message)
    {
        var publishableKey = string.Equals(providerCode, CommerceHubPaymentMethods.Stripe, StringComparison.OrdinalIgnoreCase)
            ? _paymentsOptions.Stripe.PublishableKey
            : null;

        var paymentUrl = payment.CheckoutUrl;
        var paymentToken = payment.PaymentToken;
        if (string.Equals(providerCode, CommerceHubPaymentMethods.Paymob, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(paymentToken) &&
            paymentToken.StartsWith(PaymobSensitiveArtifactProtector.PaymentSessionPrefix, StringComparison.Ordinal))
        {
            var protectedSession = _paymobArtifactProtector?.UnprotectPaymentSession(paymentToken)
                ?? throw new InvalidOperationException("CommerceHub Paymob session protector is unavailable.");
            paymentUrl = protectedSession.IframeUrl;
            paymentToken = null;
        }

        var handoffType = ResolveHandoffType(providerCode, paymentUrl, paymentToken, publishableKey);

        return new CommerceHubPaymentInitiateResponseDto
        {
            PaymentId = payment.Id,
            MarketplaceOrderId = marketplaceOrder.Id,
            OrderNumber = marketplaceOrder.OrderNumber,
            Provider = providerCode,
            Status = payment.Status.ToString().ToLowerInvariant(),
            Amount = payment.Amount,
            Currency = payment.Currency,
            HandoffType = handoffType,
            PaymentUrl = paymentUrl,
            PaymentToken = paymentToken,
            PublishableKey = publishableKey,
            ProviderReference = payment.ProviderReference,
            ProviderIntentId = payment.ProviderIntentId,
            IsReusable = isReusable,
            Message = message
        };
    }

    private static string ResolveHandoffType(
        string providerCode,
        string? paymentUrl,
        string? paymentToken,
        string? publishableKey)
    {
        // Only return client_secret for Stripe when both payment token and publishable key are available
        if (string.Equals(providerCode, CommerceHubPaymentMethods.Stripe, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(paymentToken) &&
            !string.IsNullOrWhiteSpace(publishableKey))
        {
            return "client_secret";
        }

        if (!string.IsNullOrWhiteSpace(paymentUrl))
        {
            return "redirect_url";
        }

        return "unknown";
    }

    private static CommerceHubPaymentWebhookResultDto BuildWebhookResult(
        CommerceHubPayment payment,
        string providerCode,
        bool processed,
        string message,
        string? transactionId,
        bool shouldRetry = false)
    {
        return new CommerceHubPaymentWebhookResultDto
        {
            Processed = processed,
            Message = message,
            ShouldRetry = shouldRetry,
            PaymentId = payment.Id,
            MarketplaceOrderId = payment.OrderId,
            OrderNumber = payment.Order.OrderNumber,
            Provider = providerCode,
            ProviderReference = payment.ProviderReference,
            PaymentReferenceId = payment.Order.PaymentReferenceId,
            TransactionId = transactionId
        };
    }

    private static decimal ConvertFromMinorUnits(long amount, string currency)
    {
        RequireCommerceHubPaymentCurrency(currency, "gateway amount conversion");
        return amount / 100m;
    }

    private static long ConvertToMinorUnits(decimal amount, string currency)
    {
        RequireCommerceHubPaymentCurrency(currency, "gateway amount conversion");
        return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeCurrency(string? providerCurrency, string fallbackCurrency)
    {
        RequireCommerceHubPaymentCurrency(fallbackCurrency, "payment record");
        return RequireCommerceHubPaymentCurrency(providerCurrency, "gateway callback");
    }

    private static string RequireCommerceHubPaymentCurrency(string? currency, string context)
    {
        var normalized = currency?.Trim().ToUpperInvariant();
        if (string.Equals(normalized, FinancialCurrencyGuard.PlatformBaseCurrencyCode, StringComparison.Ordinal))
        {
            return FinancialCurrencyGuard.PlatformBaseCurrencyCode;
        }

        throw new CommerceHubCheckoutException(
            "COMMERCEHUB_PAYMENT_CURRENCY_INVALID",
            $"CommerceHub payment currency must be {FinancialCurrencyGuard.PlatformBaseCurrencyCode}; {context} supplied '{currency ?? "(missing)"}'.",
            StatusCodes.Status409Conflict);
    }

    private static (string FirstName, string LastName) SplitFullName(string? fullName)
    {
        var normalized = string.IsNullOrWhiteSpace(fullName)
            ? "CommerceHub Customer"
            : fullName.Trim();

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return ("CommerceHub", "Customer");
        }

        if (parts.Length == 1)
        {
            return (parts[0], parts[0]);
        }

        return (parts[0], parts[^1]);
    }
}

internal sealed record CommerceHubPaymentProviderSession(
    string? PaymentUrl,
    string? PaymentToken,
    string? ProviderIntentId,
    string RawResponse);

internal sealed record StoreAllocationSnapshotEntry(Guid TenantId, decimal Amount);

internal sealed record ProviderRefundAllocationSnapshotEntry(
    Guid SubOrderId,
    Guid TenantId,
    decimal Amount,
    bool IsFullRefund,
    decimal RefundedAmountBefore,
    decimal CommissionReversalAmount,
    decimal PromoDiscountReversalAmount,
    decimal VendorEarningReversalAmount);

