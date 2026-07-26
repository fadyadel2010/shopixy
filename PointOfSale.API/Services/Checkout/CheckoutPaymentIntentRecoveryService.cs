using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Security;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;

namespace PointOfSale.API.Services.Checkout;

/// <summary>
/// Repairs orders that committed successfully but crashed before a PaymentIntent
/// was durably created. This closes the checkout post-commit gap without waiting
/// for the customer to retry the idempotency request.
/// </summary>
public sealed class CheckoutPaymentIntentRecoveryService
{
    private static readonly PaymentIntentStatus[] ActiveIntentStatuses =
    {
        PaymentIntentStatus.Pending,
        PaymentIntentStatus.Authorized,
        PaymentIntentStatus.Paid,
        PaymentIntentStatus.AwaitingVerification
    };

    private static readonly TimeSpan RepairGracePeriod = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ReservationExpiryGracePeriod = TimeSpan.FromMinutes(12);
    private const int MaxBatchSize = 25;

    private readonly ApplicationDbContext _dbContext;
    private readonly IPaymentService _paymentService;
    private readonly IPaymentRuntime _paymentRuntime;
    private readonly ICheckoutSessionTokenService _checkoutSessionTokenService;
    private readonly IProductVariantStockRepository _stockRepository;
    private readonly ICouponValidationService _couponValidationService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IManualPaymentDurableEvidenceAuthority _manualPaymentDurableEvidenceAuthority;
    private readonly ILogger<CheckoutPaymentIntentRecoveryService> _logger;

    public CheckoutPaymentIntentRecoveryService(
        ApplicationDbContext dbContext,
        IPaymentService paymentService,
        IPaymentRuntime paymentRuntime,
        ICheckoutSessionTokenService checkoutSessionTokenService,
        IProductVariantStockRepository stockRepository,
        ICouponValidationService couponValidationService,
        IEventPublisher eventPublisher,
        IManualPaymentDurableEvidenceAuthority manualPaymentDurableEvidenceAuthority,
        ILogger<CheckoutPaymentIntentRecoveryService> logger)
    {
        _dbContext = dbContext;
        _paymentService = paymentService;
        _paymentRuntime = paymentRuntime;
        _checkoutSessionTokenService = checkoutSessionTokenService;
        _stockRepository = stockRepository;
        _couponValidationService = couponValidationService;
        _eventPublisher = eventPublisher;
        _manualPaymentDurableEvidenceAuthority = manualPaymentDurableEvidenceAuthority;
        _logger = logger;
    }

    public async Task<int> RepairPendingPaymentIntentOrdersAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        var repairCutoffUtc = nowUtc.Subtract(RepairGracePeriod);

        // Query only orders that actually need recovery:
        // 1. PendingPaymentIntent state (no intent created yet)
        // 2. PaymentPending state BUT either:
        //    a) No active payment intent exists, OR
        //    b) Active intent exists but provider initiation is incomplete
        var orders = await _dbContext.Orders
            .IgnoreQueryFilters()
            .Where(order =>
                !order.IsDeleted &&
                order.Status == OrderStatus.Pending &&
                order.PaymentStatus == PaymentStatus.Pending &&
                order.CreatedAt <= repairCutoffUtc &&
                (order.CheckoutState == CheckoutState.PendingPaymentIntent ||
                 (order.CheckoutState == CheckoutState.PaymentPending &&
                  // Only include PaymentPending orders that lack a complete active intent
                  !_dbContext.PaymentIntents
                      .IgnoreQueryFilters()
                      .Any(intent =>
                          intent.TenantId == order.TenantId &&
                          intent.OrderId == order.Id &&
                          !intent.IsDeleted &&
                          ActiveIntentStatuses.Contains(intent.Status) &&
                          // Exclude intents with incomplete provider initiation
                          (intent.Provider == PaymentProvider.Cod ||
                           intent.Provider == PaymentProvider.ManualWallet && intent.Status != PaymentIntentStatus.Pending ||
                           (!string.IsNullOrWhiteSpace(intent.ProviderIntentId) &&
                            (intent.Provider != PaymentProvider.Fawry || !string.IsNullOrWhiteSpace(intent.ReferenceCode))))))))
            .OrderBy(order => order.CreatedAt)
            .Take(MaxBatchSize)
            .ToListAsync(cancellationToken);

        var repaired = 0;

        foreach (var order in orders)
        {
            try
            {
                if (await EnsureOrderHasPaymentIntentAsync(order, nowUtc, cancellationToken))
                {
                    repaired++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Checkout payment-intent recovery failed | TenantId: {TenantId} | OrderId: {OrderId} | PaymentMethodCode: {PaymentMethodCode}",
                    order.TenantId,
                    order.Id,
                    order.PaymentMethodCode);

                if (IsLocalPaymentConfigurationFailure(ex) &&
                    await NoPaymentIntentExistsAsync(order, cancellationToken))
                {
                    await ExpireUnrecoverableOrderIfReservationWindowElapsedAsync(order, nowUtc, cancellationToken);
                }
            }
        }

        return repaired;
    }

    private async Task<bool> EnsureOrderHasPaymentIntentAsync(
        Order order,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var activeIntents = await _dbContext.PaymentIntents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(intent =>
                intent.TenantId == order.TenantId &&
                intent.OrderId == order.Id &&
                ActiveIntentStatuses.Contains(intent.Status))
            .OrderByDescending(intent => intent.CreatedAt)
            .ToListAsync(cancellationToken);

        var isManualWalletOrder = IsManualWalletPaymentMethod(order.PaymentMethodCode);
        var manualWalletIntentCount = activeIntents.Count(intent => intent.Provider == PaymentProvider.ManualWallet);

        if (manualWalletIntentCount > 1)
        {
            throw new PaymentOrderInvalidStateException(
                order.Id,
                "ManualWalletActiveIntent:Ambiguous",
                "ExactlyOneDurableManualWalletIntent",
                "MANUAL_WALLET_RECOVERY_AMBIGUOUS");
        }

        if (isManualWalletOrder &&
            activeIntents.Any(intent => intent.Provider != PaymentProvider.ManualWallet))
        {
            throw new PaymentOrderInvalidStateException(
                order.Id,
                "ManualWalletActiveIntent:ConflictingProvider",
                "ManualWalletDurablePaymentFacts",
                "MANUAL_WALLET_RECOVERY_CONFLICTING_STATE");
        }

        var existingIntent = isManualWalletOrder
            ? activeIntents.FirstOrDefault(intent => intent.Provider == PaymentProvider.ManualWallet)
            : activeIntents.FirstOrDefault();

        if (existingIntent != null)
        {
            await CheckoutInventoryAuthorityGuard.EnsureStorefrontOrderReservationsActiveAsync(
                _dbContext,
                order,
                cancellationToken);

            if (IsProviderInitiationIncomplete(existingIntent))
            {
                var resumeIdempotencyKey = await ResolveProviderIdempotencyKeyAsync(order, cancellationToken);
                var resumeCorrelationId = $"checkout-intent-recovery:{order.Id:N}";
                if (existingIntent.Provider == PaymentProvider.ManualWallet)
                {
                    _manualPaymentDurableEvidenceAuthority.ValidateReceiptEvidence(
                        existingIntent,
                        ManualPaymentDurableEvidencePurpose.Recovery);

                    await _paymentService.ResumeExistingPaymentIntentAsync(
                        order.TenantId,
                        order.Id,
                        existingIntent.Id,
                        PaymentProvider.ManualWallet,
                        order.PromotionCalculationFailed,
                        resumeIdempotencyKey,
                        resumeCorrelationId,
                        cancellationToken);
                }
                else
                {
                    await CreateProviderAccuratePaymentIntentAsync(order, resumeIdempotencyKey, resumeCorrelationId, cancellationToken);
                }
            }

            if (order.CheckoutState == CheckoutState.PendingPaymentIntent)
            {
                order.TransitionCheckoutState(CheckoutState.PaymentPending);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Checkout payment-intent recovery advanced existing intent | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId}",
                order.TenantId,
                order.Id,
                existingIntent.Id);
            return true;
        }

        if (isManualWalletOrder)
        {
            _logger.LogWarning(
                "Manual Wallet checkout recovery rejected because durable payment facts are missing | TenantId: {TenantId} | OrderId: {OrderId}",
                order.TenantId,
                order.Id);
            return false;
        }

        if (!IsRecoverablePaymentMethod(order.PaymentMethodCode))
        {
            return await ExpireUnrecoverableOrderIfReservationWindowElapsedAsync(order, nowUtc, cancellationToken);
        }

        var idempotencyKey = await ResolveProviderIdempotencyKeyAsync(order, cancellationToken);
        var correlationId = $"checkout-intent-recovery:{order.Id:N}";

        await CheckoutInventoryAuthorityGuard.EnsureStorefrontOrderReservationsActiveAsync(
            _dbContext,
            order,
            cancellationToken);

        await CreateProviderAccuratePaymentIntentAsync(order, idempotencyKey, correlationId, cancellationToken);

        if (order.CheckoutState == CheckoutState.PendingPaymentIntent)
        {
            order.TransitionCheckoutState(CheckoutState.PaymentPending);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogWarning(
            "Recovered missing checkout payment intent | TenantId: {TenantId} | OrderId: {OrderId} | Provider: {Provider}",
            order.TenantId,
            order.Id,
            order.PaymentMethodCode);

        return true;
    }

    private async Task CreateProviderAccuratePaymentIntentAsync(
        Order order,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var paymentMethodCode = order.PaymentMethodCode?.Trim().ToUpperInvariant();

        switch (paymentMethodCode)
        {
            case "STRIPE":
                var checkoutToken = _checkoutSessionTokenService.CreatePaymentToken(order.TenantId, order.Id);
                await _paymentRuntime.InitiateAsync(
                    new CommerceHubPaymentInitiateRequestDto
                    {
                        OrderId = order.Id.ToString(),
                        Provider = CommerceHubPaymentMethods.Stripe,
                        OrderAccessToken = checkoutToken
                    },
                    order.OnlineCustomerId,
                    correlationId,
                    cancellationToken);
                return;
            case "PAYMOB":
                await _paymentRuntime.InitiateAsync(
                    new CommerceHubPaymentInitiateRequestDto
                    {
                        OrderId = order.Id.ToString(),
                        Provider = CommerceHubPaymentMethods.Paymob,
                        OrderAccessToken = _checkoutSessionTokenService.CreatePaymentToken(order.TenantId, order.Id)
                    },
                    order.OnlineCustomerId,
                    correlationId,
                    cancellationToken);
                return;
            case "FAWRY":
                await _paymentService.CreatePaymentIntentAsync(
                    order.TenantId,
                    order.Id,
                    new CreatePaymentIntentRequestDto { Provider = PaymentProvider.Fawry.ToString() },
                    order.PromotionCalculationFailed,
                    idempotencyKey,
                    correlationId,
                    cancellationToken);
                return;
            case "COD":
                await _paymentService.CreatePaymentIntentAsync(
                    order.TenantId,
                    order.Id,
                    new CreatePaymentIntentRequestDto { Provider = PaymentProvider.Cod.ToString() },
                    order.PromotionCalculationFailed,
                    idempotencyKey,
                    correlationId,
                    cancellationToken);
                return;
            case "MANUALWALLET":
                throw new PaymentOrderInvalidStateException(
                    order.Id,
                    "ManualWalletIntent:Missing",
                    "ExistingDurableManualWalletIntent",
                    "MANUAL_WALLET_RECOVERY_FACTS_MISSING");
            default:
                throw new InvalidOperationException(
                    $"Payment method '{order.PaymentMethodCode}' is not recoverable without the original checkout payload.");
        }
    }

    private async Task<bool> ExpireUnrecoverableOrderIfReservationWindowElapsedAsync(
        Order order,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (order.CreatedAt > nowUtc.Subtract(ReservationExpiryGracePeriod))
        {
            _logger.LogWarning(
                "Checkout order has no recoverable payment-intent payload yet; waiting for reservation expiry window | TenantId: {TenantId} | OrderId: {OrderId} | PaymentMethodCode: {PaymentMethodCode}",
                order.TenantId,
                order.Id,
                order.PaymentMethodCode);
            return false;
        }

        // Detach only the specific order being processed to avoid tracking conflicts
        // when reloading with a lock. Other orders in the batch remain tracked.
        _dbContext.Entry(order).State = EntityState.Detached;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var lockedOrder = await LoadOrderForPaymentIntentRecoveryAsync(
            order.TenantId,
            order.Id,
            cancellationToken);

        if (lockedOrder == null ||
            (lockedOrder.CheckoutState != CheckoutState.PendingPaymentIntent &&
             lockedOrder.CheckoutState != CheckoutState.PaymentPending) ||
            lockedOrder.Status != OrderStatus.Pending ||
            lockedOrder.PaymentStatus != PaymentStatus.Pending)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var paymentIntentExists = await _dbContext.PaymentIntents
            .IgnoreQueryFilters()
            .AnyAsync(intent =>
                intent.TenantId == lockedOrder.TenantId &&
                intent.OrderId == lockedOrder.Id &&
                ActiveIntentStatuses.Contains(intent.Status),
                cancellationToken);

        if (paymentIntentExists)
        {
            lockedOrder.TransitionCheckoutState(CheckoutState.PaymentPending);
            lockedOrder.UpdatedAt = nowUtc;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        lockedOrder.TransitionCheckoutState(CheckoutState.Expired);
        using var orderStatusScope = OrderStatusSaveChangesInterceptor.BeginAuthorizedContext();
        lockedOrder.Status = OrderStatus.Cancelled;
        lockedOrder.CancellationReason = "Payment intent could not be recovered before reservation expiry.";
        lockedOrder.CancelledAt = nowUtc;
        lockedOrder.UpdatedAt = nowUtc;
        var inventoryReleased = await _stockRepository.ReleaseOrderReservationsAsync(lockedOrder.TenantId, lockedOrder.Id, cancellationToken);
        await _couponValidationService.ReleaseCouponReservationAsync(
            lockedOrder.TenantId,
            lockedOrder.Id,
            $"checkout-intent-recovery:{lockedOrder.Id:N}",
            "PaymentIntentRecoveryExpired",
            cancellationToken);
        await _eventPublisher.PublishAsync(new OrderCancelledEvent
        {
            TenantId = lockedOrder.TenantId,
            OrderId = lockedOrder.Id,
            OrderNumber = lockedOrder.OrderNumber,
            CancellationReason = lockedOrder.CancellationReason,
            CancelledAt = nowUtc,
            RestoreInventory = false,
            CorrelationId = $"checkout-intent-recovery:{lockedOrder.Id:N}",
            IdempotencyKey = $"checkout-intent-recovery-expired:{lockedOrder.TenantId:N}:{lockedOrder.Id:N}",
            OccurredAtUtc = nowUtc
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogCritical(
            "Expired unrecoverable checkout order and released inventory | TenantId: {TenantId} | OrderId: {OrderId} | PaymentMethodCode: {PaymentMethodCode} | InventoryReleased: {InventoryReleased}",
            lockedOrder.TenantId,
            lockedOrder.Id,
            lockedOrder.PaymentMethodCode,
            inventoryReleased);

        return true;
    }

    private async Task<Order?> LoadOrderForPaymentIntentRecoveryAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var providerName = _dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""Orders""
                    WHERE ""Id"" = {orderId}
                      AND ""TenantId"" = {tenantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {orderId}
                      AND [TenantId] = {tenantId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.Orders
            .FirstOrDefaultAsync(
                candidate => candidate.Id == orderId &&
                             candidate.TenantId == tenantId &&
                             !candidate.IsDeleted,
                cancellationToken);
    }

    private static bool IsRecoverablePaymentMethod(string? paymentMethodCode)
    {
        return paymentMethodCode?.Trim().ToUpperInvariant() switch
        {
            "STRIPE" or "PAYMOB" or "FAWRY" or "COD" => true,
            "MANUALWALLET" => false,
            _ => false
        };
    }

    private static bool IsManualWalletPaymentMethod(string? paymentMethodCode) =>
        string.Equals(paymentMethodCode?.Trim(), "MANUALWALLET", StringComparison.OrdinalIgnoreCase);

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

    private static bool IsLocalPaymentConfigurationFailure(Exception ex) =>
        ex is PaymentManualWalletNotConfiguredException
            or PaymentProviderNotSupportedException;

    private async Task<bool> NoPaymentIntentExistsAsync(Order order, CancellationToken cancellationToken)
    {
        return !await _dbContext.PaymentIntents
            .IgnoreQueryFilters()
            .AnyAsync(intent =>
                intent.TenantId == order.TenantId &&
                intent.OrderId == order.Id,
                cancellationToken);
    }

    private static string BuildRecoveryIdempotencyKey(Order order) =>
        $"checkout-payment-intent-recovery:{order.TenantId:N}:{order.Id:N}";

    private async Task<string> ResolveProviderIdempotencyKeyAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        var idempotencyStorageKey = await _dbContext.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == order.TenantId &&
                record.OrderId == order.Id &&
                !record.IsDeleted)
            .OrderByDescending(record => record.CompletedAtUtc.HasValue)
            .ThenByDescending(record => record.CompletedAtUtc)
            .ThenByDescending(record => record.CreatedAt)
            .Select(record => record.Key)
            .FirstOrDefaultAsync(cancellationToken);

        var checkoutId = ExtractCheckoutIdFromStorageKey(idempotencyStorageKey);
        return !string.IsNullOrWhiteSpace(checkoutId)
            ? checkoutId
            : BuildRecoveryIdempotencyKey(order);
    }

    private static string? ExtractCheckoutIdFromStorageKey(string? storageKey)
    {
        // Return the full storage key verbatim to preserve the original provider-facing
        // idempotency key. The "storefront:checkout:" prefix is part of the idempotency
        // key that was used when creating the payment intent, so recovery must use the
        // identical key to target the same provider-side idempotent request.
        const string prefix = "storefront:checkout:";
        return !string.IsNullOrWhiteSpace(storageKey) &&
               storageKey.StartsWith(prefix, StringComparison.Ordinal)
            ? storageKey
            : null;
    }
}

public sealed class CheckoutPaymentIntentRecoveryHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CheckoutPaymentIntentRecoveryHostedService> _logger;

    public CheckoutPaymentIntentRecoveryHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<CheckoutPaymentIntentRecoveryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var recoveryService = scope.ServiceProvider.GetRequiredService<CheckoutPaymentIntentRecoveryService>();
                var repaired = await recoveryService.RepairPendingPaymentIntentOrdersAsync(stoppingToken);

                if (repaired > 0)
                {
                    _logger.LogWarning(
                        "Checkout payment-intent recovery repaired {Count} order(s)",
                        repaired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Checkout payment-intent recovery sweep failed");
            }
        }
    }
}
