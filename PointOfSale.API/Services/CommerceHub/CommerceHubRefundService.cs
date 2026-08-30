using System.Data;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis.Services;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubRefundService
{
    Task<IReadOnlyList<CommerceHubRefundDto>> GetRefundsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubRefundDto> CreateRefundAsync(
        Guid tenantId,
        CommerceHubCreateRefundRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubRefundDto> ConfirmRefundAsync(
        Guid tenantId,
        Guid refundId,
        CommerceHubConfirmRefundRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubRefundDto> FailRefundAsync(
        Guid tenantId,
        Guid refundId,
        CommerceHubFailRefundRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public class CommerceHubRefundService : ICommerceHubRefundService
{
    private const int RecoveryBatchSize = 25;
    private static readonly TimeSpan ProcessingRecoveryThreshold = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RecoveryLockTtl = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubRefundGatewayService _refundGatewayService;
    private readonly ICommerceHubFinancialLedgerService _financialLedgerService;
    private readonly ICommerceHubFinancialAuditService _auditService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IRefundCompletionValidator _refundCompletionValidator;
    private readonly ICommerceHubFinancialSafetyGuardService _financialSafetyGuardService;
    private readonly ICommerceHubFinancialReadinessGate _financialReadinessGate;
    private readonly IDistributedLockService _lockService;
    private readonly ILogger<CommerceHubRefundService> _logger;

    public CommerceHubRefundService(
        ApplicationDbContext dbContext,
        ICommerceHubRefundGatewayService refundGatewayService,
        ICommerceHubFinancialLedgerService financialLedgerService,
        ICommerceHubFinancialAuditService auditService,
        IEventPublisher eventPublisher,
        IRefundCompletionValidator refundCompletionValidator,
        ICommerceHubFinancialSafetyGuardService financialSafetyGuardService,
        ICommerceHubFinancialReadinessGate financialReadinessGate,
        IDistributedLockService lockService,
        ILogger<CommerceHubRefundService> logger)
    {
        _dbContext = dbContext;
        _refundGatewayService = refundGatewayService;
        _financialLedgerService = financialLedgerService;
        _auditService = auditService;
        _eventPublisher = eventPublisher;
        _refundCompletionValidator = refundCompletionValidator;
        _financialSafetyGuardService = financialSafetyGuardService;
        _financialReadinessGate = financialReadinessGate;
        _lockService = lockService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CommerceHubRefundDto>> GetRefundsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var refunds = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Include(refund => refund.SubOrder)
            .Where(refund => refund.TenantId == tenantId && !refund.IsDeleted)
            .OrderByDescending(refund => refund.CreatedAt)
            .ToListAsync(cancellationToken);

        return refunds.Select(MapRefund).ToList();
    }

    public async Task<int> ReconcileProcessingRefundsAsync(CancellationToken cancellationToken = default)
    {
        var recoveryCutoffUtc = DateTime.UtcNow.Subtract(ProcessingRecoveryThreshold);

        var candidates = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Where(refund =>
                !refund.IsDeleted &&
                refund.Status == CommerceHubRefundStatus.Processing &&
                refund.ProcessedAtUtc == null &&
                ((refund.ProcessingStartedAtUtc ?? refund.CreatedAt) <= recoveryCutoffUtc ||
                 refund.GatewayStatus == "execution_unknown"))
            .OrderBy(refund => refund.ProcessingStartedAtUtc ?? refund.CreatedAt)
            .ThenBy(refund => refund.Id)
            .Take(RecoveryBatchSize)
            .Select(refund => new { refund.TenantId, refund.Id })
            .ToListAsync(cancellationToken);

        var recoveredCount = 0;
        var failureCount = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                var recovered = await ResumePersistedRefundAsync(
                    candidate.TenantId,
                    candidate.Id,
                    correlationId: $"refund-recovery:{candidate.Id:N}",
                    cancellationToken,
                    allowProcessingRecovery: true);

                if (recovered.Status is nameof(CommerceHubRefundStatus.Completed) or nameof(CommerceHubRefundStatus.Failed))
                {
                    recoveredCount++;
                }
            }
            catch (Exception ex)
            {
                // Preserve cooperative cancellation - rethrow if operation was cancelled
                if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                
                failureCount++;
                _logger.LogError(
                    ex,
                    "Failed to resume refund recovery for candidate. RefundId={RefundId}, TenantId={TenantId}",
                    candidate.Id,
                    candidate.TenantId);
            }
        }

        return recoveredCount;
    }

    public async Task<CommerceHubRefundDto> CreateRefundAsync(
        Guid tenantId,
        CommerceHubCreateRefundRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginRefund();
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        ArgumentNullException.ThrowIfNull(request);

        if (request.SubOrderId == Guid.Empty)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_SUBORDER_REQUIRED",
                "Sub-order id is required.",
                StatusCodes.Status400BadRequest);
        }

        if (request.Amount <= 0)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_AMOUNT_INVALID",
                "Refund amount must be greater than zero.",
                StatusCodes.Status400BadRequest);
        }

        var idempotencyKey = NormalizeRequired(
            request.IdempotencyKey,
            "REFUND_IDEMPOTENCY_REQUIRED",
            "Refund idempotency key is required.");
        var normalizedReason = NormalizeRequired(
            request.Reason,
            "REFUND_REASON_REQUIRED",
            "Refund reason is required.");

        var existing = await FindRefundByIdempotencyAsync(tenantId, idempotencyKey, cancellationToken);
        if (existing != null)
        {
            return await ResumePersistedRefundAsync(tenantId, existing.Id, correlationId, cancellationToken);
        }

        CommerceHubRefund? trackedRefund = null;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            existing = await FindRefundByIdempotencyAsync(tenantId, idempotencyKey, cancellationToken);
            if (existing != null)
            {
                await transaction.CommitAsync(cancellationToken);
                return await ResumePersistedRefundAsync(tenantId, existing.Id, correlationId, cancellationToken);
            }

            var subOrder = await _dbContext.CommerceHubSubOrders
                .Include(item => item.MarketplaceOrder)
                .ThenInclude(order => order.SubOrders)
                .FirstOrDefaultAsync(
                    item => item.Id == request.SubOrderId &&
                            item.StoreId == tenantId &&
                            !item.IsDeleted &&
                            !item.MarketplaceOrder.IsDeleted,
                    cancellationToken);

            if (subOrder == null)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_SUBORDER_NOT_FOUND",
                    "Sub-order was not found for the current tenant.",
                    StatusCodes.Status404NotFound);
            }

            var marketplaceOrder = subOrder.MarketplaceOrder;

            await _financialSafetyGuardService.CaptureStateForUpdateAsync(
                tenantId,
                marketplaceOrder.Currency,
                cancellationToken);

            var subOrderEarnings = await _dbContext.CommerceHubVendorEarnings
                .Where(item => item.SubOrderId == subOrder.Id && !item.IsDeleted)
                .OrderBy(item => item.CreatedAt)
                .ToListAsync(cancellationToken);

            await EnsureNoProcessingPayoutForRefundEarningsAsync(
                tenantId,
                subOrderEarnings,
                cancellationToken);

            var primaryEarning = subOrderEarnings.FirstOrDefault(item => item.OriginalEarningId == null);

            var payment = await ResolveRefundPaymentAsync(marketplaceOrder.Id, primaryEarning?.PaymentId, cancellationToken);

            ValidateRefundEligibility(subOrder, payment);

            var refundedAmountSoFar = await _dbContext.CommerceHubRefunds
                .Where(refund =>
                    refund.SubOrderId == subOrder.Id &&
                    refund.Status != CommerceHubRefundStatus.Failed &&
                    !refund.IsDeleted)
                .SumAsync(refund => refund.Amount, cancellationToken);

            var refundableAmount = RoundCurrency(subOrder.Total - refundedAmountSoFar);
            if (request.Amount > refundableAmount)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_AMOUNT_EXCEEDED",
                    "Refund amount exceeds the refundable amount for this sub-order.",
                    StatusCodes.Status409Conflict);
            }

            var paymentRefundedOrReservedAmount = payment == null
                ? 0m
                : await _dbContext.CommerceHubRefunds
                    .Where(refund =>
                        refund.PaymentId == payment.Id &&
                        refund.Status != CommerceHubRefundStatus.Failed &&
                        !refund.IsDeleted)
                    .SumAsync(refund => refund.Amount, cancellationToken);

            if (payment != null && RoundCurrency(payment.Amount - paymentRefundedOrReservedAmount) < request.Amount)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_PAYMENT_LIMIT_EXCEEDED",
                    "Refund amount exceeds the remaining refundable payment amount.",
                    StatusCodes.Status409Conflict);
            }

            var totalAfterRefund = RoundCurrency(refundedAmountSoFar + request.Amount);
            var isFullRefund = totalAfterRefund >= RoundCurrency(subOrder.Total);
            var refundBreakdown = RefundFinancialCalculator.CalculateSubOrderBreakdown(
                subOrder,
                refundedAmountSoFar,
                request.Amount,
                isFullRefund);
            var commissionReversalAmount = refundBreakdown.CommissionReversalAmount;
            var promoDiscountReversalAmount = refundBreakdown.PromoDiscountReversalAmount;
            var vendorEarningReversalAmount = refundBreakdown.VendorEarningReversalAmount;

            trackedRefund = new CommerceHubRefund
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SubOrderId = subOrder.Id,
                PaymentId = payment?.Id,
                Amount = RoundCurrency(request.Amount),
                CommissionReversalAmount = commissionReversalAmount,
                PromoDiscountReversalAmount = promoDiscountReversalAmount,
                VendorEarningReversalAmount = vendorEarningReversalAmount,
                Currency = subOrder.Currency,
                Status = CommerceHubRefundStatus.Pending,
                Reason = normalizedReason,
                GatewayStatus = "pending",
                IdempotencyKey = idempotencyKey,
                IsFullRefund = isFullRefund
            };

            _dbContext.CommerceHubRefunds.Add(trackedRefund);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        if (trackedRefund == null)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PROCESSING_FAILED",
                "Refund could not be created.",
                StatusCodes.Status500InternalServerError);
        }

        _dbContext.ChangeTracker.Clear();
        return await ResumePersistedRefundAsync(tenantId, trackedRefund.Id, correlationId, cancellationToken);
    }

    private async Task<CommerceHubRefundDto> ResumePersistedRefundAsync(
        Guid tenantId,
        Guid refundId,
        string correlationId,
        CancellationToken cancellationToken,
        bool allowProcessingRecovery = false)
    {
        var refund = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Include(item => item.SubOrder)
            .FirstOrDefaultAsync(
                item => item.Id == refundId &&
                        item.TenantId == tenantId &&
                        !item.IsDeleted,
                cancellationToken);

        if (refund == null)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_NOT_FOUND",
                "Refund was not found.",
                StatusCodes.Status404NotFound);
        }

        if (refund.Status == CommerceHubRefundStatus.Pending)
        {
            return await ExecutePersistedRefundAsync(
                tenantId,
                refundId,
                correlationId,
                cancellationToken,
                allowProcessingRecovery: false);
        }

        if (refund.Status == CommerceHubRefundStatus.Processing &&
            (allowProcessingRecovery || ShouldAttemptProcessingRecovery(refund)))
        {
            return await RecoverProcessingRefundWithLockAsync(
                tenantId,
                refundId,
                correlationId,
                cancellationToken);
        }

        return MapRefund(refund, refund.SubOrder.OrderNumber);
    }

    private async Task<CommerceHubRefundDto> ExecutePersistedRefundAsync(
        Guid tenantId,
        Guid refundId,
        string correlationId,
        CancellationToken cancellationToken,
        bool allowProcessingRecovery = false)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginRefund();
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();
        using var financialWorkflowBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();

        var refundSnapshot = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Include(refund => refund.SubOrder)
            .ThenInclude(subOrder => subOrder.MarketplaceOrder)
            .FirstOrDefaultAsync(
                refund => refund.Id == refundId &&
                          refund.TenantId == tenantId &&
                          !refund.IsDeleted,
                cancellationToken);

        if (refundSnapshot == null)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_NOT_FOUND",
                "Refund was not found.",
                StatusCodes.Status404NotFound);
        }

        if (refundSnapshot.Status is CommerceHubRefundStatus.Completed or CommerceHubRefundStatus.Failed or CommerceHubRefundStatus.ReconciliationRequired)
        {
            return MapRefund(refundSnapshot, refundSnapshot.SubOrder.OrderNumber);
        }

        if (refundSnapshot.Status == CommerceHubRefundStatus.Processing &&
            !allowProcessingRecovery)
        {
            return MapRefund(refundSnapshot, refundSnapshot.SubOrder.OrderNumber);
        }

        var snapshotEarnings = await _dbContext.CommerceHubVendorEarnings
            .AsNoTracking()
            .Where(item => item.SubOrderId == refundSnapshot.SubOrderId && !item.IsDeleted)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        var primaryEarning = snapshotEarnings.FirstOrDefault(item => item.OriginalEarningId == null);
        var paymentSnapshot = await ResolveRefundPaymentAsync(
            refundSnapshot.SubOrder.MarketplaceOrder.Id,
            refundSnapshot.PaymentId ?? primaryEarning?.PaymentId,
            cancellationToken);

        CommerceHubRefund? trackedRefund = null;

        var processingRecoveryActivated = false;

        _dbContext.ChangeTracker.Clear();
        await using (var claimTransaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            try
            {
                // A recovery attempt is the operation that resolves this refund's existing
                // uncertain external state. Applying the new-money readiness gate here would
                // make the refund permanently self-blocking. New refund executions remain gated.
                if (!(allowProcessingRecovery && refundSnapshot.Status == CommerceHubRefundStatus.Processing))
                {
                    await _financialReadinessGate.EnsureOutwardMoneyMovementAllowedAsync(
                        tenantId,
                        "CommerceHubRefund.GatewayExecute",
                        cancellationToken);
                }

                // Read financial state before invoking the external refund gateway without taking
                // FOR UPDATE locks. Serializable completion reacquires the locks after gateway response.
                await _financialSafetyGuardService.GetStateSnapshotAsync(
                    tenantId,
                    refundSnapshot.Currency,
                    cancellationToken);

                // CONCURRENCY GUARD: Check for processing payouts INSIDE the claim transaction
                // to prevent race with MarkProcessingAsync. The previous check was outside the
                // transaction, allowing a concurrent MarkProcessingAsync to set payouts to Processing
                // between the check and the gateway call, causing refund-payout conflicts.
                //
                // By checking inside the Serializable transaction, we guarantee mutual exclusion
                // with MarkProcessingAsync's transaction, preventing the race condition.
                //
                // CRITICAL: Re-load earnings inside the transaction to ensure we check against
                // current state, not the stale snapshotEarnings loaded before the transaction.
                var currentEarnings = await _dbContext.CommerceHubVendorEarnings
                    .AsNoTracking()
                    .Where(item => item.SubOrderId == refundSnapshot.SubOrderId && !item.IsDeleted)
                    .OrderBy(item => item.CreatedAt)
                    .ToListAsync(cancellationToken);

                await EnsureNoProcessingPayoutForRefundEarningsAsync(
                    tenantId,
                    currentEarnings,
                    cancellationToken);

                var claimRowsAffected = await ClaimPendingRefundForGatewayExecutionAsync(
                    tenantId,
                    refundId,
                    cancellationToken);

                if (claimRowsAffected == 0)
                {
                    trackedRefund = await _dbContext.CommerceHubRefunds
                        .Include(refund => refund.SubOrder)
                        .ThenInclude(subOrder => subOrder.MarketplaceOrder)
                        .FirstOrDefaultAsync(
                            refund => refund.Id == refundId &&
                                      refund.TenantId == tenantId &&
                                      !refund.IsDeleted,
                            cancellationToken);

                    if (trackedRefund == null)
                    {
                        throw new CommerceHubCheckoutException(
                            "REFUND_NOT_FOUND",
                            "Refund was not found.",
                            StatusCodes.Status404NotFound);
                    }

                    if (trackedRefund.Status == CommerceHubRefundStatus.Processing &&
                        allowProcessingRecovery &&
                        ShouldAttemptProcessingRecovery(trackedRefund))
                    {
                        refundSnapshot = trackedRefund;
                        processingRecoveryActivated = true;
                        await claimTransaction.RollbackAsync(cancellationToken);
                    }
                    else
                    {
                        await claimTransaction.RollbackAsync(cancellationToken);
                        return MapRefund(trackedRefund, trackedRefund.SubOrder.OrderNumber);
                    }
                }

                if (!processingRecoveryActivated)
                {
                    await claimTransaction.CommitAsync(cancellationToken);
                }
            }
            catch
            {
                await claimTransaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        CommerceHubGatewayRefundResult gatewayResult;
        try
        {
            gatewayResult = await ResolveGatewayExecutionResultAsync(
                paymentSnapshot,
                refundSnapshot,
                allowProcessingRecovery,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MarkRefundGatewayOutcomeUnknownAsync(
                tenantId,
                refundId,
                $"Refund gateway execution outcome is unknown: {ex.Message}",
                cancellationToken);

            _logger.LogError(
                ex,
                "CommerceHub refund gateway outcome unknown after durable processing claim | RefundId: {RefundId} | TenantId: {TenantId}",
                refundId,
                tenantId);

            var pendingRefund = await _dbContext.CommerceHubRefunds
                .AsNoTracking()
                .Include(refund => refund.SubOrder)
                .FirstAsync(refund => refund.Id == refundId, cancellationToken);

            return MapRefund(pendingRefund, pendingRefund.SubOrder.OrderNumber);
        }

        var shouldPublishEvents = false;
        var completionApplied = false;
        OrderStatus oldMarketplaceStatus = default;
        PaymentStatus oldMarketplacePaymentStatus = default;
        Dictionary<Guid, OrderStatus> oldSubOrderStates = new();
        Dictionary<Guid, PaymentStatus> oldSubOrderPaymentStates = new();
        CommerceHubMarketplaceOrder? marketplaceOrder = null;
        CommerceHubSubOrder? subOrder = null;

        _dbContext.ChangeTracker.Clear();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            trackedRefund = await _dbContext.CommerceHubRefunds
                .FirstOrDefaultAsync(
                    refund => refund.Id == refundId &&
                              refund.TenantId == tenantId &&
                              !refund.IsDeleted,
                    cancellationToken);

            if (trackedRefund == null)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_NOT_FOUND",
                    "Refund was not found.",
                    StatusCodes.Status404NotFound);
            }

            if (trackedRefund.Status is CommerceHubRefundStatus.Completed or CommerceHubRefundStatus.Failed or CommerceHubRefundStatus.ReconciliationRequired)
            {
                await transaction.CommitAsync(cancellationToken);
                return MapRefund(trackedRefund, refundSnapshot.SubOrder.OrderNumber);
            }

            subOrder = await _dbContext.CommerceHubSubOrders
                .Include(item => item.MarketplaceOrder)
                .ThenInclude(order => order.SubOrders)
                .FirstOrDefaultAsync(
                    item => item.Id == trackedRefund.SubOrderId &&
                            item.StoreId == tenantId &&
                            !item.IsDeleted &&
                            !item.MarketplaceOrder.IsDeleted,
                    cancellationToken);

            if (subOrder == null)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_SUBORDER_NOT_FOUND",
                    "Sub-order was not found for the current tenant.",
                    StatusCodes.Status404NotFound);
            }

            marketplaceOrder = subOrder.MarketplaceOrder;
            oldMarketplaceStatus = marketplaceOrder.Status;
            oldMarketplacePaymentStatus = marketplaceOrder.PaymentStatus;
            oldSubOrderStates = marketplaceOrder.SubOrders.ToDictionary(item => item.Id, item => item.Status);
            oldSubOrderPaymentStates = marketplaceOrder.SubOrders.ToDictionary(item => item.Id, item => item.PaymentStatus);

            var subOrderEarnings = await _dbContext.CommerceHubVendorEarnings
                .Where(item => item.SubOrderId == subOrder.Id && !item.IsDeleted)
                .OrderBy(item => item.CreatedAt)
                .ToListAsync(cancellationToken);

            await EnsureNoProcessingPayoutForRefundEarningsAsync(
                tenantId,
                subOrderEarnings,
                cancellationToken);

            var primaryTrackedEarning = subOrderEarnings.FirstOrDefault(item => item.OriginalEarningId == null);
            var payment = await ResolveRefundPaymentAsync(
                marketplaceOrder.Id,
                trackedRefund.PaymentId ?? primaryTrackedEarning?.PaymentId,
                cancellationToken);

            await _financialSafetyGuardService.CaptureStateForUpdateAsync(
                tenantId,
                refundSnapshot.Currency,
                cancellationToken);

            trackedRefund.Status = CommerceHubRefundStatus.Processing;
            trackedRefund.ProcessingStartedAtUtc ??= DateTime.UtcNow;
            trackedRefund.Reference = gatewayResult.Reference ?? trackedRefund.Reference;
            trackedRefund.GatewayStatus = gatewayResult.GatewayStatus ?? trackedRefund.GatewayStatus ?? "processing";

            if (!gatewayResult.Accepted)
            {
                trackedRefund.Status = CommerceHubRefundStatus.Failed;
                trackedRefund.GatewayStatus = gatewayResult.GatewayStatus ?? "failed";
                trackedRefund.FailureReason = gatewayResult.FailureReason ?? "Refund gateway execution failed.";
                trackedRefund.ProcessedAtUtc = DateTime.UtcNow;

                _auditService.RecordRefundFailed(
                    tenantId,
                    subOrder.Id,
                    trackedRefund.Id,
                    payment?.Id,
                    subOrder.Currency,
                    $"Refund failed before financial reversal | Reason: {trackedRefund.FailureReason}",
                    correlationId);
            }
            else if (gatewayResult.IsConfirmed)
            {
                completionApplied = true;
                shouldPublishEvents = await CompleteRefundAsync(
                    tenantId,
                    marketplaceOrder,
                    subOrder,
                    payment,
                    trackedRefund,
                    subOrderEarnings,
                    correlationId,
                    cancellationToken);
            }

            if (shouldPublishEvents && marketplaceOrder != null)
            {
                await PublishRefundStatusEventsAsync(
                    marketplaceOrder,
                    correlationId,
                    oldMarketplaceStatus,
                    oldMarketplacePaymentStatus,
                    oldSubOrderStates,
                    oldSubOrderPaymentStates,
                    cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception completionException)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                _logger.LogError(
                    rollbackException,
                    "CommerceHub refund completion rollback failed after gateway response | RefundId: {RefundId} | TenantId: {TenantId}",
                    refundId,
                    tenantId);
            }

            if (gatewayResult.Accepted)
            {
                try
                {
                    await MarkRefundGatewayOutcomeUnknownAsync(
                        tenantId,
                        refundId,
                        $"Refund gateway accepted the refund, but POS could not complete local financial reversal: {completionException.Message}",
                        CancellationToken.None,
                        gatewayResult.Reference,
                        gatewayResult.GatewayStatus ?? (gatewayResult.IsConfirmed
                            ? "confirmed_local_completion_failed"
                            : "accepted_local_completion_failed"),
                        requiresOperatorReconciliation: true);
                }
                catch (Exception markerException)
                {
                    _logger.LogCritical(
                        markerException,
                        "CommerceHub refund gateway-accepted completion gap could not be marked for reconciliation | RefundId: {RefundId} | TenantId: {TenantId} | GatewayReference: {GatewayReference}",
                        refundId,
                        tenantId,
                        gatewayResult.Reference);
                }
            }

            throw;
        }

        if (trackedRefund == null || subOrder == null)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PROCESSING_FAILED",
                "Refund could not be processed.",
                StatusCodes.Status500InternalServerError);
        }

        _logger.LogInformation(
            "CommerceHub refund {RefundOutcome} | RefundId: {RefundId} | SubOrderId: {SubOrderId} | TenantId: {TenantId} | Amount: {Amount} | Full: {IsFullRefund}",
            completionApplied ? "completed" : trackedRefund.Status.ToString().ToLowerInvariant(),
            trackedRefund.Id,
            trackedRefund.SubOrderId,
            tenantId,
            trackedRefund.Amount,
            trackedRefund.IsFullRefund);

        return MapRefund(trackedRefund, subOrder.OrderNumber);
    }

    private async Task<CommerceHubRefundDto> RecoverProcessingRefundWithLockAsync(
        Guid tenantId,
        Guid refundId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var lockKey = BuildRefundRecoveryLockKey(refundId);
        var lockToken = Guid.NewGuid().ToString("N");
        var lockResult = await _lockService.TryAcquireLockAsync(lockKey, lockToken, RecoveryLockTtl);

        if (!lockResult.IsAcquired)
        {
            _logger.LogDebug(
                "CommerceHub refund recovery lock unavailable | RefundId: {RefundId} | TenantId: {TenantId} | Status: {Status}",
                refundId,
                tenantId,
                lockResult.Status);

            var currentRefund = await _dbContext.CommerceHubRefunds
                .AsNoTracking()
                .Include(item => item.SubOrder)
                .FirstAsync(
                    item => item.Id == refundId &&
                            item.TenantId == tenantId &&
                            !item.IsDeleted,
                    cancellationToken);

            return MapRefund(currentRefund, currentRefund.SubOrder.OrderNumber);
        }

        try
        {
            return await ExecutePersistedRefundAsync(
                tenantId,
                refundId,
                correlationId,
                cancellationToken,
                allowProcessingRecovery: true);
        }
        finally
        {
            await _lockService.ReleaseLockAsync(lockKey, lockToken);
        }
    }

    private async Task<CommerceHubGatewayRefundResult> ResolveGatewayExecutionResultAsync(
        CommerceHubPayment? paymentSnapshot,
        CommerceHubRefund refundSnapshot,
        bool allowProcessingRecovery,
        CancellationToken cancellationToken)
    {
        if (allowProcessingRecovery && refundSnapshot.Status == CommerceHubRefundStatus.Processing)
        {
            if (ShouldRefreshGatewayOutcome(refundSnapshot))
            {
                var refreshResult = await _refundGatewayService.RefreshRefundStatusAsync(
                    paymentSnapshot,
                    refundSnapshot.Reference,
                    refundSnapshot.GatewayStatus,
                    cancellationToken);

                if (!refreshResult.Accepted &&
                    !IsFailedGatewayRefundStatus(refreshResult.GatewayStatus))
                {
                    throw new InvalidOperationException(
                        $"Refund gateway status refresh could not prove a terminal outcome for refund {refundSnapshot.Id}.");
                }

                return refreshResult;
            }
        }

        CommerceHubRefundReturnCurrencyAuthority.RequireConsistentExecutionCurrency(
            refundSnapshot.SubOrder.MarketplaceOrder,
            refundSnapshot.SubOrder,
            refundSnapshot,
            paymentSnapshot,
            $"gateway-refund:{refundSnapshot.Id:D}");

        return await _refundGatewayService.RefundAsync(
            paymentSnapshot,
            refundSnapshot.Amount,
            refundSnapshot.Currency,
            refundSnapshot.Reason,
            refundSnapshot.IdempotencyKey,
            cancellationToken);
    }

    private async Task MarkRefundGatewayOutcomeUnknownAsync(
        Guid tenantId,
        Guid refundId,
        string failureReason,
        CancellationToken cancellationToken,
        string? gatewayReference = null,
        string? gatewayStatus = null,
        bool requiresOperatorReconciliation = false)
    {
        _dbContext.ChangeTracker.Clear();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var trackedRefund = await _dbContext.CommerceHubRefunds
                .FirstOrDefaultAsync(
                    refund => refund.Id == refundId &&
                              refund.TenantId == tenantId &&
                              !refund.IsDeleted,
                    cancellationToken);

            if (trackedRefund != null)
            {
                // Update Reference and GatewayStatus for all non-terminal states
                if (trackedRefund.Status is not (CommerceHubRefundStatus.Completed or CommerceHubRefundStatus.Failed))
                {
                    trackedRefund.Reference = string.IsNullOrWhiteSpace(gatewayReference)
                        ? trackedRefund.Reference
                        : gatewayReference.Trim();
                    trackedRefund.GatewayStatus = string.IsNullOrWhiteSpace(gatewayStatus)
                        ? (requiresOperatorReconciliation ? "execution_reconciliation_required" : "execution_unknown")
                        : gatewayStatus.Trim();
                    trackedRefund.FailureReason = failureReason.Length > 1000
                        ? failureReason[..1000]
                        : failureReason;
                    trackedRefund.ProcessingStartedAtUtc ??= DateTime.UtcNow;
                    trackedRefund.ProcessedAtUtc = null;
                }

                // Only update Status if not in a sticky state (Completed, Failed, or ReconciliationRequired)
                if (trackedRefund.Status is not (CommerceHubRefundStatus.Completed or CommerceHubRefundStatus.Failed or CommerceHubRefundStatus.ReconciliationRequired))
                {
                    trackedRefund.Status = requiresOperatorReconciliation
                        ? CommerceHubRefundStatus.ReconciliationRequired
                        : CommerceHubRefundStatus.Processing;
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

    public async Task<CommerceHubRefundDto> ConfirmRefundAsync(
        Guid tenantId,
        Guid refundId,
        CommerceHubConfirmRefundRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginRefund();
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();
        request ??= new CommerceHubConfirmRefundRequestDto();

        CommerceHubRefund? trackedRefund = null;
        CommerceHubMarketplaceOrder? marketplaceOrder = null;
        CommerceHubSubOrder? subOrder = null;
        CommerceHubPayment? payment = null;
        OrderStatus oldMarketplaceStatus = default;
        PaymentStatus oldMarketplacePaymentStatus = default;
        Dictionary<Guid, OrderStatus> oldSubOrderStates = new();
        Dictionary<Guid, PaymentStatus> oldSubOrderPaymentStates = new();
        var shouldPublishEvents = false;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            trackedRefund = await LoadTrackedRefundAsync(tenantId, refundId, cancellationToken);
            if (trackedRefund.Status == CommerceHubRefundStatus.Completed)
            {
                await transaction.CommitAsync(cancellationToken);
                return MapRefund(trackedRefund, trackedRefund.SubOrder.OrderNumber);
            }

            if (trackedRefund.Status == CommerceHubRefundStatus.Failed)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_ALREADY_FAILED",
                    "A failed refund cannot be confirmed without creating a new refund request.",
                    StatusCodes.Status409Conflict);
            }

            marketplaceOrder = trackedRefund.SubOrder.MarketplaceOrder;
            subOrder = trackedRefund.SubOrder;
            await _financialSafetyGuardService.CaptureStateForUpdateAsync(
                tenantId,
                marketplaceOrder.Currency,
                cancellationToken);

            payment = await ResolveRefundPaymentAsync(marketplaceOrder.Id, trackedRefund.PaymentId, cancellationToken);
            oldMarketplaceStatus = marketplaceOrder.Status;
            oldMarketplacePaymentStatus = marketplaceOrder.PaymentStatus;
            oldSubOrderStates = marketplaceOrder.SubOrders.ToDictionary(item => item.Id, item => item.Status);
            oldSubOrderPaymentStates = marketplaceOrder.SubOrders.ToDictionary(item => item.Id, item => item.PaymentStatus);

            var subOrderEarnings = await _dbContext.CommerceHubVendorEarnings
                .Where(item => item.SubOrderId == subOrder.Id && !item.IsDeleted)
                .OrderBy(item => item.CreatedAt)
                .ToListAsync(cancellationToken);

            await EnsureNoProcessingPayoutForRefundEarningsAsync(
                tenantId,
                subOrderEarnings,
                cancellationToken);

            var confirmation = ValidateGatewayRefundConfirmation(trackedRefund, payment, request);

            trackedRefund.Status = CommerceHubRefundStatus.Processing;
            trackedRefund.Reference = confirmation.Reference ?? trackedRefund.Reference;
            trackedRefund.GatewayStatus = confirmation.GatewayStatus ?? "confirmed";
            trackedRefund.ProcessingStartedAtUtc ??= DateTime.UtcNow;

            shouldPublishEvents = await CompleteRefundAsync(
                tenantId,
                marketplaceOrder,
                subOrder,
                payment,
                trackedRefund,
                subOrderEarnings,
                correlationId,
                cancellationToken);

            if (shouldPublishEvents)
            {
                await PublishRefundStatusEventsAsync(
                    marketplaceOrder,
                    correlationId,
                    oldMarketplaceStatus,
                    oldMarketplacePaymentStatus,
                    oldSubOrderStates,
                    oldSubOrderPaymentStates,
                    cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        if (trackedRefund == null || marketplaceOrder == null || subOrder == null)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_NOT_FOUND",
                "Refund was not found.",
                StatusCodes.Status404NotFound);
        }

        return MapRefund(trackedRefund, subOrder.OrderNumber);
    }

    public async Task<CommerceHubRefundDto> FailRefundAsync(
        Guid tenantId,
        Guid refundId,
        CommerceHubFailRefundRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginRefund();
        request ??= new CommerceHubFailRefundRequestDto();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var trackedRefund = await LoadTrackedRefundAsync(tenantId, refundId, cancellationToken);
            if (trackedRefund.Status == CommerceHubRefundStatus.Completed)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_ALREADY_COMPLETED",
                    "A completed refund cannot be failed.",
                    StatusCodes.Status409Conflict);
            }

            if (trackedRefund.Status == CommerceHubRefundStatus.Failed)
            {
                await transaction.CommitAsync(cancellationToken);
                return MapRefund(trackedRefund, trackedRefund.SubOrder.OrderNumber);
            }

            if (trackedRefund.Status == CommerceHubRefundStatus.ReconciliationRequired)
            {
                var requestedGatewayStatus = NormalizeOptional(request.GatewayStatus);
                if (!IsFailedGatewayRefundStatus(requestedGatewayStatus) ||
                    string.IsNullOrWhiteSpace(request.FailureReason))
                {
                    throw new CommerceHubCheckoutException(
                        "REFUND_RECONCILIATION_REQUIRED",
                        "A reconciliation-required refund cannot be failed without explicit external non-execution proof.",
                        StatusCodes.Status409Conflict);
                }
            }

            trackedRefund.Status = CommerceHubRefundStatus.Failed;
            trackedRefund.Reference = NormalizeOptional(request.Reference) ?? trackedRefund.Reference;
            trackedRefund.GatewayStatus = NormalizeOptional(request.GatewayStatus) ?? "failed";
            trackedRefund.FailureReason = NormalizeOptional(request.FailureReason) ?? "Refund confirmation failed.";
            trackedRefund.ProcessedAtUtc ??= DateTime.UtcNow;

            _auditService.RecordRefundFailed(
                tenantId,
                trackedRefund.SubOrderId,
                trackedRefund.Id,
                trackedRefund.PaymentId,
                trackedRefund.Currency,
                $"Refund failed after gateway processing | Reason: {trackedRefund.FailureReason}",
                correlationId);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return MapRefund(trackedRefund, trackedRefund.SubOrder.OrderNumber);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<CommerceHubPayment?> ResolveRefundPaymentAsync(
        Guid marketplaceOrderId,
        Guid? paymentId,
        CancellationToken cancellationToken)
    {
        if (paymentId.HasValue)
        {
            var payment = await _dbContext.CommerceHubPayments
                .FirstOrDefaultAsync(item => item.Id == paymentId.Value && !item.IsDeleted, cancellationToken);
            if (payment != null)
            {
                return payment;
            }
        }

        return await _dbContext.CommerceHubPayments
            .Where(payment =>
                payment.OrderId == marketplaceOrderId &&
                payment.Status == CommerceHubPaymentRecordStatus.Paid &&
                !payment.IsDeleted)
            .OrderByDescending(payment => payment.PaidAtUtc ?? payment.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<int> ClaimPendingRefundForGatewayExecutionAsync(
        Guid tenantId,
        Guid refundId,
        CancellationToken cancellationToken)
    {
        var claimStartedAtUtc = DateTime.UtcNow;

        if (_dbContext.Database.IsRelational())
        {
            return await _dbContext.CommerceHubRefunds
                .Where(refund => refund.Id == refundId &&
                                 refund.TenantId == tenantId &&
                                 !refund.IsDeleted &&
                                 refund.Status == CommerceHubRefundStatus.Pending)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(refund => refund.Status, CommerceHubRefundStatus.Processing)
                        .SetProperty(refund => refund.GatewayStatus, "processing")
                        .SetProperty(refund => refund.ProcessingStartedAtUtc, refund => refund.ProcessingStartedAtUtc ?? (DateTime?)claimStartedAtUtc)
                        .SetProperty(refund => refund.FailureReason, refund => null),
                    cancellationToken);
        }

        var refund = await _dbContext.CommerceHubRefunds
            .FirstOrDefaultAsync(
                candidate => candidate.Id == refundId &&
                             candidate.TenantId == tenantId &&
                             !candidate.IsDeleted &&
                             candidate.Status == CommerceHubRefundStatus.Pending,
                cancellationToken);

        if (refund == null)
        {
            return 0;
        }

        refund.Status = CommerceHubRefundStatus.Processing;
        refund.GatewayStatus = "processing";
        refund.ProcessingStartedAtUtc ??= claimStartedAtUtc;
        refund.FailureReason = null;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return 1;
    }

    private static void ValidateRefundEligibility(CommerceHubSubOrder subOrder, CommerceHubPayment? payment)
    {
        var isCod = subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery;
        if (isCod)
        {
            if (subOrder.PaymentStatus is not (PaymentStatus.Collected or PaymentStatus.Settled or PaymentStatus.PartiallyRefunded))
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_NOT_ALLOWED",
                    $"Cash-on-delivery refunds require a collected payment. Current state is {subOrder.PaymentStatus}.",
                    StatusCodes.Status409Conflict);
            }

            return;
        }

        if (payment == null)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYMENT_NOT_FOUND",
                "The paid marketplace payment could not be located for this refund.",
                StatusCodes.Status409Conflict);
        }

        if (payment.Status != CommerceHubPaymentRecordStatus.Paid)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYMENT_NOT_SETTLED",
                "Only paid marketplace payments can be refunded.",
                StatusCodes.Status409Conflict);
        }

        if (subOrder.PaymentStatus is not (PaymentStatus.Collected or PaymentStatus.Settled or PaymentStatus.PartiallyRefunded))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_NOT_ALLOWED",
                $"Sub-order payment state {subOrder.PaymentStatus} cannot be refunded.",
                StatusCodes.Status409Conflict);
        }
    }

    private async Task EnsureNoProcessingPayoutForRefundEarningsAsync(
        Guid tenantId,
        IReadOnlyCollection<CommerceHubVendorEarning> earnings,
        CancellationToken cancellationToken)
    {
        var payoutIds = earnings
            .Where(item => item.Status == CommerceHubVendorEarningStatus.InPayout && item.PayoutId.HasValue)
            .Select(item => item.PayoutId!.Value)
            .Distinct()
            .ToList();

        if (payoutIds.Count == 0)
        {
            return;
        }

        var processingPayout = await _dbContext.CommerceHubPayouts
            .AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId &&
                payoutIds.Contains(item.Id) &&
                (item.Status == CommerceHubPayoutStatus.Processing ||
                 item.Status == CommerceHubPayoutStatus.ReconciliationRequired) &&
                !item.IsDeleted)
            .OrderBy(item => item.ProcessedAtUtc ?? item.CreatedAt)
            .Select(item => new
            {
                item.Id,
                item.Reference,
                item.ExternalReferenceId,
                item.BankTransactionId,
                item.ExternalTransferIdempotencyKey
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (processingPayout == null)
        {
            return;
        }

        throw new CommerceHubCheckoutException(
            "REFUND_BLOCKED_BY_PROCESSING_PAYOUT",
            "Refund execution is blocked because a payout containing these earnings is already processing externally or awaiting payout reconciliation. Complete or reconcile the payout before refunding.",
            StatusCodes.Status409Conflict);
    }

    private async Task ReleasePendingPayoutAsync(
        Guid tenantId,
        Guid payoutId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var payoutBoundary = CommerceHubBoundaryMutationScope.BeginPayout();
        var payout = await _dbContext.CommerceHubPayouts
            .Include(item => item.Items)
            .ThenInclude(item => item.Earning)
            .FirstOrDefaultAsync(
                item => item.Id == payoutId &&
                        item.TenantId == tenantId &&
                        !item.IsDeleted,
                cancellationToken);

        if (payout == null || payout.Status == CommerceHubPayoutStatus.Completed || payout.Status == CommerceHubPayoutStatus.Failed)
        {
            return;
        }

        if (payout.Status is CommerceHubPayoutStatus.Processing or CommerceHubPayoutStatus.ReconciliationRequired)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_BLOCKED_BY_PROCESSING_PAYOUT",
                "Refund execution is blocked because a payout containing these earnings is already processing externally or awaiting payout reconciliation. Complete or reconcile the payout before refunding.",
                StatusCodes.Status409Conflict);
        }

        foreach (var payoutItem in payout.Items)
        {
            if (payoutItem.Earning.Status == CommerceHubVendorEarningStatus.InPayout &&
                payoutItem.Earning.PayoutId == payout.Id)
            {
                if (payoutItem.Earning.Amount > 0m)
                {
                    var allocation = RoundCurrency(Math.Max(0m, payoutItem.Amount));
                    payoutItem.Earning.PayoutReservedAmount = RoundCurrency(Math.Max(0m, payoutItem.Earning.PayoutReservedAmount - allocation));
                }

                payoutItem.Earning.Status = CommerceHubVendorEarningStatus.Available;
                payoutItem.Earning.PayoutId = null;
            }
        }

        payout.Status = CommerceHubPayoutStatus.Failed;
        payout.FailureReason = "Refund processing required earnings to be released from payout.";
        payout.ProcessedBy = "system:refund";
        payout.ProcessedAtUtc = DateTime.UtcNow;

        _auditService.RecordPayoutFailed(
            tenantId,
            payout.Id,
            payout.Currency,
            "Payout failed because a refund reversed one of its reserved earnings.",
            correlationId);
    }

    private async Task<bool> CompleteRefundAsync(
        Guid tenantId,
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        CommerceHubPayment? payment,
        CommerceHubRefund trackedRefund,
        IReadOnlyCollection<CommerceHubVendorEarning> subOrderEarnings,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();
        using var financialWorkflowBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();

        foreach (var payoutId in subOrderEarnings
                     .Where(item => item.Status == CommerceHubVendorEarningStatus.InPayout && item.PayoutId.HasValue)
                     .Select(item => item.PayoutId!.Value)
                     .Distinct()
                     .ToList())
        {
            await ReleasePendingPayoutAsync(tenantId, payoutId, correlationId, cancellationToken);
        }

        await EnsureRefundCompletionDoesNotOverrunAsync(
            subOrder,
            payment,
            trackedRefund,
            cancellationToken);

        trackedRefund.Status = CommerceHubRefundStatus.Completed;
        trackedRefund.GatewayStatus = trackedRefund.GatewayStatus ?? "confirmed";
        trackedRefund.FailureReason = null;
        trackedRefund.ProcessedAtUtc ??= DateTime.UtcNow;

        if (payment != null)
        {
            payment.RefundedAmount = RoundCurrency(payment.RefundedAmount + trackedRefund.Amount);
        }

        ApplyRefundStatuses(marketplaceOrder, subOrder, trackedRefund.IsFullRefund);
        ApplyEarningReversal(
            tenantId,
            marketplaceOrder,
            subOrder,
            payment,
            trackedRefund,
            subOrderEarnings,
            trackedRefund.VendorEarningReversalAmount,
            correlationId);

        _auditService.RecordRefundProcessed(
            tenantId,
            marketplaceOrder.Id,
            subOrder.Id,
            trackedRefund.Id,
            payment?.Id,
            trackedRefund.Amount,
            trackedRefund.Currency,
            correlationId);

        if (trackedRefund.CommissionReversalAmount > 0)
        {
            _auditService.RecordCommissionReversed(
                tenantId,
                marketplaceOrder.Id,
                subOrder.Id,
                trackedRefund.Id,
                trackedRefund.CommissionReversalAmount,
                trackedRefund.Currency,
                correlationId);
        }

        await _financialLedgerService.RecordRefundAsync(
            marketplaceOrder,
            subOrder,
            trackedRefund,
            cancellationToken);

        return true;
    }

    private async Task EnsureRefundCompletionDoesNotOverrunAsync(
        CommerceHubSubOrder subOrder,
        CommerceHubPayment? payment,
        CommerceHubRefund trackedRefund,
        CancellationToken cancellationToken)
    {
        await _refundCompletionValidator.ValidateRefundDoesNotExceedTotalsAsync(
            subOrder,
            payment,
            trackedRefund,
            cancellationToken);
    }

    private static (string? Reference, string? GatewayStatus) ValidateGatewayRefundConfirmation(
        CommerceHubRefund trackedRefund,
        CommerceHubPayment? payment,
        CommerceHubConfirmRefundRequestDto request)
    {
        var reference = NormalizeOptional(request.Reference);
        var gatewayStatus = NormalizeOptional(request.GatewayStatus);

        if (gatewayStatus != null && !IsConfirmedGatewayRefundStatus(gatewayStatus))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_GATEWAY_CONFIRMATION_REQUIRED",
                $"Refund gateway status '{gatewayStatus}' is not a terminal successful confirmation.",
                StatusCodes.Status409Conflict,
                new { refundId = trackedRefund.Id, gatewayStatus });
        }

        if (payment == null)
        {
            return (reference, gatewayStatus);
        }

        if (reference == null)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PROVIDER_REFERENCE_REQUIRED",
                "External refunds require the provider refund reference before local financial reversal can complete.",
                StatusCodes.Status409Conflict,
                new { refundId = trackedRefund.Id });
        }

        if (gatewayStatus == null)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_GATEWAY_CONFIRMATION_REQUIRED",
                "External refunds require a terminal successful gateway status before local financial reversal can complete.",
                StatusCodes.Status409Conflict,
                new { refundId = trackedRefund.Id, reference });
        }

        if (!string.IsNullOrWhiteSpace(trackedRefund.Reference) &&
            !string.Equals(trackedRefund.Reference, reference, StringComparison.OrdinalIgnoreCase))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PROVIDER_REFERENCE_MISMATCH",
                "Refund confirmation reference does not match the provider refund reference recorded at gateway execution.",
                StatusCodes.Status409Conflict,
                new
                {
                    refundId = trackedRefund.Id,
                    expectedReference = trackedRefund.Reference,
                    actualReference = reference
                });
        }

        return (reference, gatewayStatus);
    }

    private static bool IsConfirmedGatewayRefundStatus(string gatewayStatus) =>
        string.Equals(gatewayStatus, "confirmed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(gatewayStatus, "succeeded", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(gatewayStatus, "success", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(gatewayStatus, "completed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(gatewayStatus, "complete", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(gatewayStatus, "refunded", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailedGatewayRefundStatus(string? gatewayStatus) =>
        string.Equals(gatewayStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(gatewayStatus, "canceled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(gatewayStatus, "cancelled", StringComparison.OrdinalIgnoreCase);

    private static bool HasDurableGatewayOutcome(CommerceHubRefund refund) =>
        !string.IsNullOrWhiteSpace(refund.Reference) ||
        (!string.IsNullOrWhiteSpace(refund.GatewayStatus) &&
         !string.Equals(refund.GatewayStatus, "processing", StringComparison.OrdinalIgnoreCase));

    private static bool ShouldRefreshGatewayOutcome(CommerceHubRefund refund) =>
        refund.Status == CommerceHubRefundStatus.Processing &&
        HasDurableGatewayOutcome(refund);

    private static bool ShouldAttemptProcessingRecovery(CommerceHubRefund refund)
    {
        if (refund.Status != CommerceHubRefundStatus.Processing || refund.ProcessedAtUtc != null)
        {
            return false;
        }

        if (string.Equals(refund.GatewayStatus, "execution_unknown", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (ShouldRefreshGatewayOutcome(refund))
        {
            return true;
        }

        var recoveryCutoffUtc = DateTime.UtcNow.Subtract(ProcessingRecoveryThreshold);
        return (refund.ProcessingStartedAtUtc ?? refund.CreatedAt) <= recoveryCutoffUtc;
    }

    private static string BuildRefundRecoveryLockKey(Guid refundId) =>
        $"commercehub:refund:recovery:{refundId:N}";

    private void ApplyEarningReversal(
        Guid tenantId,
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        CommerceHubPayment? payment,
        CommerceHubRefund refund,
        IReadOnlyCollection<CommerceHubVendorEarning> subOrderEarnings,
        decimal reversalAmount,
        string correlationId)
    {
        if (reversalAmount <= 0 || subOrderEarnings.Count == 0)
        {
            return;
        }

        var primaryEarning = subOrderEarnings.FirstOrDefault(item => item.OriginalEarningId == null);
        if (primaryEarning == null)
        {
            return;
        }

        var hasPaidOutEarning = subOrderEarnings.Any(item => item.Status == CommerceHubVendorEarningStatus.PaidOut);
        refund.IsPostPayoutAdjustment = hasPaidOutEarning;

        if (!hasPaidOutEarning && refund.IsFullRefund)
        {
            foreach (var earning in subOrderEarnings.Where(item => item.Status != CommerceHubVendorEarningStatus.PaidOut))
            {
                earning.Status = CommerceHubVendorEarningStatus.Reversed;
                earning.RefundId = refund.Id;
                earning.PayoutId = null;
                earning.ReversedAtUtc = DateTime.UtcNow;
            }

            _auditService.RecordVendorEarningReversed(
                tenantId,
                marketplaceOrder.Id,
                subOrder.Id,
                primaryEarning.Id,
                refund.Id,
                reversalAmount,
                subOrder.Currency,
                correlationId);

            return;
        }

        if (hasPaidOutEarning)
        {
            var adjustment = new CommerceHubVendorEarning
            {
                Id = Guid.NewGuid(),
                SubOrderId = subOrder.Id,
                PaymentId = payment?.Id,
                TenantId = tenantId,
                RefundId = refund.Id,
                OriginalEarningId = primaryEarning.Id,
                Amount = -RoundCurrency(reversalAmount),
                Status = CommerceHubVendorEarningStatus.Available,
                AvailableAtUtc = DateTime.UtcNow
            };

            _dbContext.CommerceHubVendorEarnings.Add(adjustment);

            _auditService.RecordRefundReversal(
                tenantId,
                marketplaceOrder.Id,
                subOrder.Id,
                adjustment.Id,
                refund.Id,
                Math.Abs(adjustment.Amount),
                subOrder.Currency,
                correlationId);

            return;
        }

        var adjustmentStatus = primaryEarning.Status == CommerceHubVendorEarningStatus.Pending
            ? CommerceHubVendorEarningStatus.Pending
            : CommerceHubVendorEarningStatus.Available;

        var partialAdjustment = new CommerceHubVendorEarning
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            PaymentId = payment?.Id,
            TenantId = tenantId,
            RefundId = refund.Id,
            OriginalEarningId = primaryEarning.Id,
            Amount = -RoundCurrency(reversalAmount),
            Status = adjustmentStatus,
            AvailableAtUtc = adjustmentStatus == CommerceHubVendorEarningStatus.Available
                ? DateTime.UtcNow
                : null
        };

        _dbContext.CommerceHubVendorEarnings.Add(partialAdjustment);

        _auditService.RecordVendorAdjustmentCreated(
            tenantId,
            marketplaceOrder.Id,
            subOrder.Id,
            partialAdjustment.Id,
            refund.Id,
            partialAdjustment.Amount,
            subOrder.Currency,
            correlationId);
    }

    private static void ApplyRefundStatuses(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        bool isFullRefund)
    {
        CommerceHubPaymentLifecycleAuthority.ApplyRefundPaymentState(
            marketplaceOrder,
            subOrder,
            isFullRefund);

        if (isFullRefund)
        {
            subOrder.Status = OrderStatus.Refunded;
        }

        marketplaceOrder.Status = CommerceHubOrderStateHelper.AggregateMarketplaceStatus(
            marketplaceOrder.SubOrders,
            marketplaceOrder.Status);
    }

    private async Task PublishRefundStatusEventsAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        string correlationId,
        OrderStatus oldMarketplaceStatus,
        PaymentStatus oldMarketplacePaymentStatus,
        Dictionary<Guid, OrderStatus> oldSubOrderStates,
        Dictionary<Guid, PaymentStatus> oldSubOrderPaymentStates,
        CancellationToken cancellationToken)
    {
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

        if (marketplaceStatusChanged)
        {
            var affectedStoreIds = marketplaceOrder.GetAffectedStoreIds();
            if (affectedStoreIds.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Cannot publish CommerceHub refund status event for marketplace order {marketplaceOrder.Id}: affected store authority is empty.");
            }

            var eventVersion = CommerceHubOrderEventMetadata.ResolveAggregateEventVersion(marketplaceOrder);

            await _eventPublisher.PublishAsync(new CommerceHubMarketplaceOrderStatusUpdatedEvent
            {
                MarketplaceOrderId = marketplaceOrder.Id,
                TenantId = affectedStoreIds.First(),
                AffectedStoreIds = affectedStoreIds,
                CorrelationId = correlationId,
                IdempotencyKey = CommerceHubOrderEventMetadata.BuildMarketplaceStatusIdempotencyKey(
                    marketplaceOrder,
                    eventVersion,
                    "refund"),
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
                    $"Cannot publish CommerceHub refund sub-order status event for marketplace order {marketplaceOrder.Id}: sub-order {subOrder.Id} has empty StoreId.");
            }

            await _eventPublisher.PublishAsync(new CommerceHubSubOrderStatusUpdatedEvent
            {
                MarketplaceOrderId = marketplaceOrder.Id,
                SubOrderId = subOrder.Id,
                StoreId = subOrder.StoreId,
                TenantId = subOrder.StoreId,
                CorrelationId = correlationId,
                IdempotencyKey = CommerceHubOrderEventMetadata.BuildSubOrderStatusIdempotencyKey(
                    subOrder,
                    subOrderEventVersion,
                    "refund"),
                OccurredAtUtc = DateTime.UtcNow,
                EventVersion = subOrderEventVersion
            }, cancellationToken);
        }
    }

    private async Task<CommerceHubRefund?> FindRefundByIdempotencyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Include(refund => refund.SubOrder)
            .FirstOrDefaultAsync(
                refund => refund.TenantId == tenantId &&
                          refund.IdempotencyKey == idempotencyKey &&
                          !refund.IsDeleted,
                cancellationToken);
    }

    private async Task<CommerceHubRefund> LoadTrackedRefundAsync(
        Guid tenantId,
        Guid refundId,
        CancellationToken cancellationToken)
    {
        var refund = await _dbContext.CommerceHubRefunds
            .Include(item => item.SubOrder)
            .ThenInclude(item => item.MarketplaceOrder)
            .ThenInclude(item => item.SubOrders)
            .FirstOrDefaultAsync(
                item => item.Id == refundId &&
                        item.TenantId == tenantId &&
                        !item.IsDeleted,
                cancellationToken);

        if (refund == null)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_NOT_FOUND",
                "Refund was not found.",
                StatusCodes.Status404NotFound);
        }

        return refund;
    }

    private static PaymentStatus AggregateMarketplacePaymentStatus(
        IEnumerable<CommerceHubSubOrder> subOrders,
        PaymentStatus fallbackStatus)
    {
        var statuses = subOrders.Select(item => item.PaymentStatus).ToList();
        if (statuses.Count == 0)
        {
            return fallbackStatus;
        }

        if (statuses.All(status => status == PaymentStatus.Refunded))
        {
            return PaymentStatus.Refunded;
        }

        if (statuses.Any(status => status is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded))
        {
            return PaymentStatus.PartiallyRefunded;
        }

        if (statuses.All(status => status == PaymentStatus.Settled))
        {
            return PaymentStatus.Settled;
        }

        if (statuses.All(status => status == PaymentStatus.Collected))
        {
            return PaymentStatus.Collected;
        }

        return fallbackStatus;
    }

    private static CommerceHubRefundDto MapRefund(CommerceHubRefund refund) =>
        MapRefund(refund, refund.SubOrder.OrderNumber);

    private static CommerceHubRefundDto MapRefund(CommerceHubRefund refund, string orderNumber)
    {
        return new CommerceHubRefundDto
        {
            RefundId = refund.Id,
            TenantId = refund.TenantId,
            SubOrderId = refund.SubOrderId,
            PaymentId = refund.PaymentId,
            OrderNumber = orderNumber,
            Amount = refund.Amount,
            CommissionReversalAmount = refund.CommissionReversalAmount,
            PromoDiscountReversalAmount = refund.PromoDiscountReversalAmount,
            VendorEarningReversalAmount = refund.VendorEarningReversalAmount,
            Currency = CommerceHubRefundReturnCurrencyAuthority.RequireRefundCurrency(
                refund,
                $"refund-projection:{refund.Id:D}"),
            Status = refund.Status.ToString(),
            Reason = refund.Reason,
            Reference = refund.Reference,
            GatewayStatus = refund.GatewayStatus,
            FailureReason = refund.FailureReason,
            IdempotencyKey = refund.IdempotencyKey,
            IsFullRefund = refund.IsFullRefund,
            CreatedAtUtc = refund.CreatedAt,
            ProcessingStartedAtUtc = refund.ProcessingStartedAtUtc,
            ProcessedAtUtc = refund.ProcessedAtUtc
        };
    }

    private static string NormalizeRequired(string? value, string code, string message)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        throw new CommerceHubCheckoutException(code, message, StatusCodes.Status400BadRequest);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal RoundCurrency(decimal amount) =>
        CommissionCalculator.RoundAmount(amount);
}
