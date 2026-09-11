using System.Data;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.Services.Governance;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.API.Services.CommerceHub.Wallet;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.CommerceHub;

public interface IRefundExecutionService
{
    Task ExecuteAsync(
        Guid refundRequestId,
        SuperAdminExecuteRefundRequest request,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<DevelopmentRefundSimulationResult> ExecuteDevelopmentCodDebtReductionAsync(
        Guid refundRequestId,
        string idempotencyKey,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<DevelopmentRefundSimulationResult> ExecuteDevelopmentOnlineBeforePayoutAsync(
        Guid refundRequestId,
        string idempotencyKey,
        string? reason,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed record DevelopmentRefundSimulationResult(
    Guid RefundId,
    string IdempotencyKey,
    string ExecutionPath,
    string EvidenceType,
    bool IsDevelopmentSimulation,
    bool ExternalMoneyMovementConfirmed,
    string TransactionReference);

public interface IRefundExecutionRecoveryService
{
    Task<int> RecoverReconciliationRequiredRefundsAsync(CancellationToken cancellationToken = default);
}

public class RefundExecutionService : IRefundExecutionService, IRefundExecutionRecoveryService
{
    private const int MaxExecutionRetries = 3;
    private const int RecoveryBatchSize = 25;

    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubFinancialLedgerService _financialLedgerService;
    private readonly ICommerceHubFinancialAuditService _financialAuditService;
    private readonly ICommerceHubVendorDebtService _vendorDebtService;
    private readonly ICommerceHubWalletService _walletService;
    private readonly ICommerceHubFinancialSafetyGuardService _financialSafetyGuardService;
    private readonly ICommerceHubFinancialReadinessGate _financialReadinessGate;
    private readonly IUserGovernanceService _userGovernanceService;
    private readonly IRefundCompletionValidator _refundCompletionValidator;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<RefundExecutionService> _logger;

    public RefundExecutionService(
        ApplicationDbContext dbContext,
        ICommerceHubFinancialLedgerService financialLedgerService,
        ICommerceHubFinancialAuditService financialAuditService,
        ICommerceHubVendorDebtService vendorDebtService,
        ICommerceHubWalletService walletService,
        ICommerceHubFinancialSafetyGuardService financialSafetyGuardService,
        ICommerceHubFinancialReadinessGate financialReadinessGate,
        IUserGovernanceService userGovernanceService,
        IRefundCompletionValidator refundCompletionValidator,
        IEventPublisher eventPublisher,
        ILogger<RefundExecutionService> logger)
    {
        _dbContext = dbContext;
        _financialLedgerService = financialLedgerService;
        _financialAuditService = financialAuditService;
        _vendorDebtService = vendorDebtService;
        _walletService = walletService;
        _financialSafetyGuardService = financialSafetyGuardService;
        _financialReadinessGate = financialReadinessGate;
        _userGovernanceService = userGovernanceService;
        _refundCompletionValidator = refundCompletionValidator;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        Guid refundRequestId,
        SuperAdminExecuteRefundRequest request,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginRefund();
        if (!actorId.HasValue || actorId.Value == Guid.Empty)
        {
            throw new CommerceHubCheckoutException(
                "SUPER_ADMIN_CONTEXT_REQUIRED",
                "A valid Super Admin identity is required to confirm a manual refund payout.",
                StatusCodes.Status403Forbidden);
        }

        var normalizedRequest = NormalizeExecutionRequest(request);

        for (var attempt = 1; attempt <= MaxExecutionRetries; attempt++)
        {
            try
            {
                await ExecuteCoreAsync(
                    refundRequestId,
                    normalizedRequest,
                    actorId.Value,
                    actorEmail,
                    correlationId,
                    cancellationToken);
                return;
            }
            catch (Exception ex) when (IsSerializationFailure(ex) && attempt < MaxExecutionRetries)
            {
                _dbContext.ChangeTracker.Clear();
                _logger.LogWarning(
                    ex,
                    "Refund execution serialization conflict, retrying | RefundRequestId: {RefundRequestId} | Attempt: {Attempt}/{MaxAttempts}",
                    refundRequestId,
                    attempt,
                    MaxExecutionRetries);

                await Task.Delay(50 * attempt, cancellationToken);
            }
            catch (Exception ex) when (attempt == MaxExecutionRetries && ShouldPersistFailure(ex))
            {
                await TryMarkFailedAsync(
                    refundRequestId,
                    normalizedRequest,
                    actorId.Value,
                    actorEmail,
                    correlationId,
                    ex,
                    cancellationToken);
                throw;
            }
        }
    }

    public async Task<DevelopmentRefundSimulationResult> ExecuteDevelopmentCodDebtReductionAsync(
        Guid refundRequestId,
        string idempotencyKey,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!actorId.HasValue || actorId.Value == Guid.Empty)
        {
            throw new CommerceHubCheckoutException(
                "SUPER_ADMIN_CONTEXT_REQUIRED",
                "A valid Super Admin identity is required to execute a Development COD refund simulation.",
                StatusCodes.Status403Forbidden);
        }

        var normalizedIdempotencyKey = NormalizeDevelopmentIdempotencyKey(idempotencyKey);

        var payoutMethod = await _dbContext.CommerceHubRefundRequests
            .AsNoTracking()
            .Where(refund => refund.Id == refundRequestId && !refund.IsDeleted)
            .Select(refund => refund.PayoutMethodSnapshot)
            .FirstOrDefaultAsync(cancellationToken);
        if (!payoutMethod.HasValue)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYOUT_SNAPSHOT_REQUIRED",
                "The Development COD simulation requires the existing locked payout snapshot.",
                StatusCodes.Status409Conflict);
        }

        var command = new ManualRefundExecutionCommand(
            payoutMethod.Value,
            $"DEV-SIM:{normalizedIdempotencyKey}",
            "Development-only simulated COD customer refund evidence. No external money movement occurred.",
            ConfirmMoneySent: false,
            ConfirmMethodOverride: false,
            SimulationKind: DevelopmentRefundSimulationKind.CodDebtReduction,
            RequestIdempotencyKey: normalizedIdempotencyKey);

        for (var attempt = 1; attempt <= MaxExecutionRetries; attempt++)
        {
            try
            {
                await ExecuteCoreAsync(
                    refundRequestId,
                    command,
                    actorId.Value,
                    actorEmail,
                    correlationId,
                    cancellationToken);
                return await LoadDevelopmentSimulationResultAsync(
                    refundRequestId,
                    normalizedIdempotencyKey,
                    DevelopmentRefundSimulationKind.CodDebtReduction,
                    cancellationToken);
            }
            catch (Exception ex) when (IsSerializationFailure(ex) && attempt < MaxExecutionRetries)
            {
                _dbContext.ChangeTracker.Clear();
                await Task.Delay(50 * attempt, cancellationToken);
            }
        }

        throw new InvalidOperationException("Development COD refund simulation exhausted its retry policy.");
    }

    public async Task<DevelopmentRefundSimulationResult> ExecuteDevelopmentOnlineBeforePayoutAsync(
        Guid refundRequestId,
        string idempotencyKey,
        string? reason,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!actorId.HasValue || actorId.Value == Guid.Empty)
        {
            throw new CommerceHubCheckoutException(
                "SUPER_ADMIN_CONTEXT_REQUIRED",
                "A valid Super Admin identity is required to execute a Development online refund simulation.",
                StatusCodes.Status403Forbidden);
        }

        var normalizedIdempotencyKey = NormalizeDevelopmentIdempotencyKey(idempotencyKey);
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? "Development certification"
            : reason.Trim();
        if (normalizedReason.Length > 500)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_EXECUTION_REASON_TOO_LONG",
                "The Development simulation reason cannot exceed 500 characters.",
                StatusCodes.Status400BadRequest);
        }

        var payoutMethod = await _dbContext.CommerceHubRefundRequests
            .AsNoTracking()
            .Where(refund => refund.Id == refundRequestId && !refund.IsDeleted)
            .Select(refund => refund.PayoutMethodSnapshot)
            .FirstOrDefaultAsync(cancellationToken);
        if (!payoutMethod.HasValue)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYOUT_SNAPSHOT_REQUIRED",
                "The Development online simulation requires the existing locked payout snapshot.",
                StatusCodes.Status409Conflict);
        }

        var command = new ManualRefundExecutionCommand(
            payoutMethod.Value,
            $"DEV-SIM:ONLINE-REFUND:{normalizedIdempotencyKey}",
            $"Development-only simulated online customer refund. No external money movement occurred. Reason: {normalizedReason}",
            ConfirmMoneySent: false,
            ConfirmMethodOverride: false,
            SimulationKind: DevelopmentRefundSimulationKind.OnlineBeforePayout,
            RequestIdempotencyKey: normalizedIdempotencyKey);

        for (var attempt = 1; attempt <= MaxExecutionRetries; attempt++)
        {
            try
            {
                await ExecuteCoreAsync(
                    refundRequestId,
                    command,
                    actorId.Value,
                    actorEmail,
                    correlationId,
                    cancellationToken);
                return await LoadDevelopmentSimulationResultAsync(
                    refundRequestId,
                    normalizedIdempotencyKey,
                    DevelopmentRefundSimulationKind.OnlineBeforePayout,
                    cancellationToken);
            }
            catch (Exception ex) when (IsSerializationFailure(ex) && attempt < MaxExecutionRetries)
            {
                _dbContext.ChangeTracker.Clear();
                await Task.Delay(50 * attempt, cancellationToken);
            }
        }

        throw new InvalidOperationException("Development online refund simulation exhausted its retry policy.");
    }

    public async Task<int> RecoverReconciliationRequiredRefundsAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Where(refund =>
                !refund.IsDeleted &&
                refund.Status == CommerceHubRefundStatus.ReconciliationRequired &&
                refund.ProcessedAtUtc == null &&
                refund.RefundRequestId.HasValue &&
                refund.RefundRequestId.Value != Guid.Empty &&
                refund.GatewayStatus == "manual_transfer_reconciliation_required")
            .OrderBy(refund => refund.ProcessingStartedAtUtc ?? refund.CreatedAt)
            .ThenBy(refund => refund.Id)
            .Take(RecoveryBatchSize)
            .Select(refund => refund.RefundRequestId!.Value)
            .ToListAsync(cancellationToken);

        var recoveredCount = 0;
        foreach (var refundRequestId in candidates)
        {
            try
            {
                if (await RecoverManualTransferReconciliationAsync(
                        refundRequestId,
                        $"refund-reconciliation:{refundRequestId:N}",
                        cancellationToken))
                {
                    recoveredCount++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Failed to recover reconciliation-required manual refund confirmation | RefundRequestId: {RefundRequestId}",
                    refundRequestId);
                
                // Clear the change tracker to prevent stale tracked entities from the failed
                // transaction from being persisted in subsequent loop iterations.
                _dbContext.ChangeTracker.Clear();
            }
        }

        return recoveredCount;
    }

    private async Task ExecuteCoreAsync(
        Guid refundRequestId,
        ManualRefundExecutionCommand request,
        Guid actorId,
        string? actorEmail,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginRefund();
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();
        using var financialWorkflowBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();

        CommerceHubRefundRequest? refundRequest = null;
        CommerceHubRefund? trackedRefund = null;
        CommerceHubRefundExecution? executionRecord = null;
        CommerceHubMarketplaceOrder? marketplaceOrder = null;
        CommerceHubSubOrder? subOrder = null;
        CommerceHubSubOrderItem? orderItem = null;
        CommerceHubPayment? payment = null;
        OrderStatus oldMarketplaceStatus = default;
        PaymentStatus oldMarketplacePaymentStatus = default;
        Dictionary<Guid, OrderStatus> oldSubOrderStates = new();
        Dictionary<Guid, PaymentStatus> oldSubOrderPaymentStates = new();
        var shouldPublishEvents = false;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            refundRequest = await LoadRefundRequestForUpdateAsync(refundRequestId, cancellationToken);
            if (refundRequest == null)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_NOT_FOUND",
                    "Refund request was not found.",
                    StatusCodes.Status404NotFound);
            }

            await LockRefundOrderGraphForUpdateAsync(refundRequest, cancellationToken);

            if (CommerceHubRefundRequestWorkflowPolicy.IsExecuted(refundRequest.Status))
            {
                marketplaceOrder = refundRequest.Order;
                oldMarketplaceStatus = marketplaceOrder.Status;
                oldMarketplacePaymentStatus = marketplaceOrder.PaymentStatus;
                oldSubOrderStates = marketplaceOrder.SubOrders.ToDictionary(item => item.Id, item => item.Status);
                oldSubOrderPaymentStates = marketplaceOrder.SubOrders.ToDictionary(item => item.Id, item => item.PaymentStatus);

                marketplaceOrder.Status = CommerceHubOrderStateHelper.AggregateMarketplaceStatus(
                    marketplaceOrder.SubOrders,
                    marketplaceOrder.Status);

                if (marketplaceOrder.Status != oldMarketplaceStatus)
                {
                    await PublishRefundStatusEventsAsync(
                        marketplaceOrder,
                        correlationId,
                        oldMarketplaceStatus,
                        oldMarketplacePaymentStatus,
                        oldSubOrderStates,
                        oldSubOrderPaymentStates,
                        cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (!CommerceHubRefundRequestWorkflowPolicy.CanExecute(refundRequest))
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_EXECUTION_NOT_ALLOWED",
                    $"Refund request status {CommerceHubRefundRequestWorkflowPolicy.NormalizeStatus(refundRequest.Status)} cannot be confirmed for payout.",
                    StatusCodes.Status409Conflict);
            }

            ValidatePayoutSnapshot(refundRequest, request);
            ValidateReturnExecutionEligibility(refundRequest);
            try
            {
                await _userGovernanceService.EnsureVendorRefundExecutionAllowedAsync(
                    refundRequest.VendorId,
                    cancellationToken);
            }
            catch (UserGovernanceException exception)
            {
                throw new CommerceHubCheckoutException(
                    exception.Code,
                    exception.Message,
                    exception.StatusCode);
            }

            await _financialReadinessGate.EnsureOutwardMoneyMovementAllowedAsync(
                refundRequest.VendorId,
                "CommerceHubRefund.Execute",
                cancellationToken);

            orderItem = refundRequest.OrderItem;
            subOrder = orderItem.SubOrder;
            marketplaceOrder = refundRequest.Order;

            if (request.SimulationKind == DevelopmentRefundSimulationKind.CodDebtReduction &&
                subOrder.PaymentMethod != CommerceHubPaymentMethod.CashOnDelivery)
            {
                throw new CommerceHubCheckoutException(
                    "DEVELOPMENT_COD_REFUND_ONLY",
                    "The Development certification mechanism can execute only cash-on-delivery debt-reduction refunds.",
                    StatusCodes.Status409Conflict);
            }

            if (request.SimulationKind == DevelopmentRefundSimulationKind.OnlineBeforePayout &&
                subOrder.PaymentMethod != CommerceHubPaymentMethod.Online)
            {
                throw new CommerceHubCheckoutException(
                    "DEVELOPMENT_ONLINE_REFUND_ONLY",
                    "The Development online certification mechanism can execute only online refunds before payout.",
                    StatusCodes.Status409Conflict);
            }

            if (orderItem.IsDeleted || subOrder.IsDeleted || marketplaceOrder.IsDeleted)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_NOT_FOUND",
                    "Refund request references an order item that no longer exists.",
                    StatusCodes.Status404NotFound);
            }

            var subOrderEarnings = await _dbContext.CommerceHubVendorEarnings
                .Where(item => item.SubOrderId == subOrder.Id && !item.IsDeleted)
                .OrderBy(item => item.CreatedAt)
                .ToListAsync(cancellationToken);

            await EnsureNoProcessingPayoutForRefundEarningsAsync(
                refundRequest.VendorId,
                subOrderEarnings,
                cancellationToken);

            trackedRefund = await LoadTrackedRefundForUpdateAsync(refundRequestId, cancellationToken);
            executionRecord = await LoadExecutionForUpdateAsync(refundRequestId, cancellationToken);
            payment = await ResolveRefundPaymentAsync(marketplaceOrder.Id, subOrderEarnings, cancellationToken);
            CommerceHubRefundReturnCurrencyAuthority.RequireConsistentExecutionCurrency(
                marketplaceOrder,
                subOrder,
                trackedRefund,
                payment,
                $"manual-refund-execution:{refundRequest.Id:D}");
            ValidateRefundEligibility(subOrder, payment);

            var refundAmount = RefundFinancialCalculator.RoundCurrency(refundRequest.TotalAmount);
            var refundBreakdown = RefundFinancialCalculator.CalculateItemBreakdown(orderItem, refundRequest.Quantity);
            var grossCommissionReversalAmount = refundBreakdown.GrossCommissionReversalAmount;
            var commissionReversalAmount = refundBreakdown.CommissionReversalAmount;
            var promoDiscountReversalAmount = refundBreakdown.PromoDiscountReversalAmount;
            var vendorEarningReversalAmount = refundBreakdown.VendorEarningReversalAmount;
            var refundedAmountSoFar = await _dbContext.CommerceHubRefunds
                .Where(refund =>
                    refund.SubOrderId == subOrder.Id &&
                    refund.Id != (trackedRefund != null ? trackedRefund.Id : Guid.Empty) &&
                    refund.Status != CommerceHubRefundStatus.Failed &&
                    !refund.IsDeleted)
                .SumAsync(refund => (decimal?)refund.Amount, cancellationToken) ?? 0m;

            var refundableAmount = RoundCurrency(subOrder.Total - refundedAmountSoFar);
            if (refundAmount > refundableAmount)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_AMOUNT_EXCEEDED",
                    "Refund amount exceeds the remaining refundable amount for this vendor order.",
                    StatusCodes.Status409Conflict);
            }

            var paymentRefundedOrReservedAmount = payment == null
                ? 0m
                : await _dbContext.CommerceHubRefunds
                    .Where(refund =>
                        refund.PaymentId == payment.Id &&
                        refund.Id != (trackedRefund != null ? trackedRefund.Id : Guid.Empty) &&
                        refund.Status != CommerceHubRefundStatus.Failed &&
                        !refund.IsDeleted)
                    .SumAsync(refund => (decimal?)refund.Amount, cancellationToken) ?? 0m;

            if (payment != null && RoundCurrency(payment.Amount - paymentRefundedOrReservedAmount) < refundAmount)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_PAYMENT_LIMIT_EXCEEDED",
                    "Refund amount exceeds the remaining refundable payment amount.",
                    StatusCodes.Status409Conflict);
            }

            var totalAfterRefund = RoundCurrency(refundedAmountSoFar + refundAmount);
            var isFullRefund = totalAfterRefund >= RoundCurrency(subOrder.Total);
            var hasPaidOutEarning = subOrderEarnings.Any(item => item.Status == CommerceHubVendorEarningStatus.PaidOut);
            var vendorPayableBefore = RoundCurrency(
                subOrderEarnings
                    .Where(item => item.Status is not CommerceHubVendorEarningStatus.PaidOut)
                    .Sum(item => item.Amount));
            var decision = RefundFinancialDecisionEngine.Create(
                subOrder.PaymentMethod,
                hasPaidOutEarning,
                refundAmount,
                vendorEarningReversalAmount,
                commissionReversalAmount,
                grossCommissionReversalAmount,
                marketplaceOrder.Currency);
            if (request.SimulationKind == DevelopmentRefundSimulationKind.OnlineBeforePayout &&
                decision.Scenario != RefundFinancialScenario.OnlineBeforePayout)
            {
                throw new CommerceHubCheckoutException(
                    "DEVELOPMENT_ONLINE_BEFORE_PAYOUT_REFUND_ONLY",
                    "The Development online certification mechanism cannot execute a refund after vendor payout.",
                    StatusCodes.Status409Conflict);
            }
            var stateBefore = await _financialSafetyGuardService.CaptureStateForUpdateAsync(
                refundRequest.VendorId,
                marketplaceOrder.Currency,
                cancellationToken);

            if (decision.PlatformCashDelta < 0m &&
                stateBefore.PlatformCash < Math.Abs(decision.PlatformCashDelta))
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_PLATFORM_CASH_INSUFFICIENT",
                    "Platform cash is insufficient to execute this refund safely.",
                    StatusCodes.Status409Conflict);
            }

            if (decision.PlatformPayableDelta < 0m &&
                stateBefore.VendorPayable < Math.Abs(decision.PlatformPayableDelta))
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_VENDOR_PAYABLE_INSUFFICIENT",
                    "Refund amount exceeds the vendor payable balance still backed by the ledger.",
                    StatusCodes.Status409Conflict);
            }

            if (decision.VendorDebtDelta < 0m &&
                stateBefore.VendorDebt < Math.Abs(decision.VendorDebtDelta))
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_VENDOR_DEBT_INSUFFICIENT",
                    "Refund would reduce vendor debt below zero.",
                    StatusCodes.Status409Conflict);
            }

            var executionStartedAtUtc = DateTime.UtcNow;

            oldMarketplaceStatus = marketplaceOrder.Status;
            oldMarketplacePaymentStatus = marketplaceOrder.PaymentStatus;
            oldSubOrderStates = marketplaceOrder.SubOrders.ToDictionary(item => item.Id, item => item.Status);
            oldSubOrderPaymentStates = marketplaceOrder.SubOrders.ToDictionary(item => item.Id, item => item.PaymentStatus);

            if (trackedRefund == null)
            {
                trackedRefund = new CommerceHubRefund
                {
                    Id = Guid.NewGuid(),
                    TenantId = refundRequest.VendorId,
                    SubOrderId = subOrder.Id,
                    RefundRequestId = refundRequest.Id,
                    OrderItemId = orderItem.Id,
                    Quantity = refundRequest.Quantity,
                    PaymentId = payment?.Id,
                    Amount = refundAmount,
                    CommissionReversalAmount = commissionReversalAmount,
                    PromoDiscountReversalAmount = promoDiscountReversalAmount,
                    VendorEarningReversalAmount = vendorEarningReversalAmount,
                    Currency = marketplaceOrder.Currency,
                    Status = CommerceHubRefundStatus.Pending,
                    Reason = refundRequest.Reason,
                    Reference = request.TransactionReference,
                    GatewayStatus = "manual-pending",
                    IdempotencyKey = BuildExecutionIdempotencyKey(refundRequest),
                    IsFullRefund = isFullRefund,
                    IsPostPayoutAdjustment = hasPaidOutEarning,
                    CreatedBy = actorId
                };

                _dbContext.CommerceHubRefunds.Add(trackedRefund);
            }
            else if (trackedRefund.Status == CommerceHubRefundStatus.Completed)
            {
                refundRequest.Status = CommerceHubRefundRequestStatus.Paid;
                refundRequest.CompletedAtUtc ??= trackedRefund.ProcessedAtUtc ?? DateTime.UtcNow;
                refundRequest.FailedAtUtc = null;
                refundRequest.FailureReason = null;

                if (executionRecord != null)
                {
                    executionRecord.IsSuccessful = true;
                    executionRecord.FailureReason = null;
                    executionRecord.TransactionReference = request.TransactionReference;
                    executionRecord.Method = request.Method;
                    executionRecord.ExecutedBy = actorId;
                    executionRecord.ExecutedAt = executionStartedAtUtc;
                    executionRecord.Notes = request.Notes;
                    executionRecord.UpdatedBy = actorId;
                }

                AppendExecutionAuditLog(
                    action: "REFUND_PAYOUT_ALREADY_CONFIRMED",
                    description: "CommerceHub refund payout confirmation was requested after the refund had already been paid.",
                    refundRequestId: refundRequest.Id,
                    orderNumber: marketplaceOrder.OrderNumber,
                    actorId: actorId,
                    actorEmail: actorEmail,
                    success: true,
                    details: new
                    {
                        RefundId = refundRequest.Id,
                        FinancialRefundId = trackedRefund.Id,
                        ExecutionId = executionRecord?.Id,
                        Amount = trackedRefund.Amount,
                        Currency = trackedRefund.Currency,
                        Method = executionRecord?.Method ?? request.Method,
                        MaskedAccount = refundRequest.PayoutAccountMaskedSnapshot,
                        ActorId = actorId,
                        TransactionReference = executionRecord?.TransactionReference ?? trackedRefund.Reference,
                        ExistingExecutedAt = executionRecord?.ExecutedAt ?? trackedRefund.ProcessedAtUtc
                    });

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            else
            {
                trackedRefund.PaymentId = payment?.Id;
                trackedRefund.Amount = refundAmount;
                trackedRefund.CommissionReversalAmount = commissionReversalAmount;
                trackedRefund.PromoDiscountReversalAmount = promoDiscountReversalAmount;
                trackedRefund.VendorEarningReversalAmount = vendorEarningReversalAmount;
                trackedRefund.Quantity = refundRequest.Quantity;
                trackedRefund.IsFullRefund = isFullRefund;
                trackedRefund.IsPostPayoutAdjustment = hasPaidOutEarning;
                trackedRefund.Reference = request.TransactionReference;
                trackedRefund.FailureReason = null;
                trackedRefund.GatewayStatus = "manual-pending";
                trackedRefund.UpdatedBy = actorId;
            }

            if (executionRecord == null)
            {
                executionRecord = new CommerceHubRefundExecution
                {
                    Id = Guid.NewGuid(),
                    RefundId = refundRequest.Id,
                    Method = request.Method,
                    AccountSnapshot = refundRequest.PayoutAccountSnapshot!,
                    TransactionReference = request.TransactionReference,
                    Amount = refundAmount,
                    Currency = marketplaceOrder.Currency,
                    ExecutedBy = actorId,
                    ExecutedAt = executionStartedAtUtc,
                    Notes = request.Notes,
                    IsSuccessful = false,
                    FailureReason = null,
                    CreatedBy = actorId
                };

                _dbContext.CommerceHubRefundExecutions.Add(executionRecord);
            }
            else
            {
                executionRecord.Method = request.Method;
                executionRecord.AccountSnapshot = refundRequest.PayoutAccountSnapshot!;
                executionRecord.TransactionReference = request.TransactionReference;
                executionRecord.Amount = refundAmount;
                executionRecord.Currency = marketplaceOrder.Currency;
                executionRecord.ExecutedBy = actorId;
                executionRecord.ExecutedAt = executionStartedAtUtc;
                executionRecord.Notes = request.Notes;
                executionRecord.IsSuccessful = false;
                executionRecord.FailureReason = null;
                executionRecord.UpdatedBy = actorId;
            }

            trackedRefund.Status = CommerceHubRefundStatus.Processing;
            trackedRefund.ProcessingStartedAtUtc ??= executionStartedAtUtc;
            trackedRefund.ProcessedAtUtc = null;

            refundRequest.ProcessingStartedAtUtc ??= executionStartedAtUtc;
            refundRequest.FailedAtUtc = null;
            refundRequest.FailureReason = null;
            refundRequest.ExecutionAttempts += 1;
            refundRequest.UpdatedAt = executionStartedAtUtc;
            refundRequest.UpdatedBy = actorId;

            AppendExecutionAuditLog(
                action: GetInitiatedAuditAction(request.SimulationKind),
                description: GetInitiatedAuditDescription(request.SimulationKind),
                refundRequestId: refundRequest.Id,
                orderNumber: marketplaceOrder.OrderNumber,
                actorId: actorId,
                actorEmail: actorEmail,
                success: true,
                details: new
                {
                    RefundId = refundRequest.Id,
                    Amount = refundAmount,
                    Currency = marketplaceOrder.Currency,
                    Method = request.Method,
                    MaskedAccount = refundRequest.PayoutAccountMaskedSnapshot,
                    ActorId = actorId,
                    TransactionReference = request.TransactionReference,
                     ConfirmMoneySent = request.ConfirmMoneySent,
                     ConfirmMethodOverride = request.ConfirmMethodOverride,
                     request.IsDevelopmentSimulation,
                     request.RequestIdempotencyKey,
                     ExternalMoneyMovementConfirmed = request.ConfirmMoneySent && !request.IsDevelopmentSimulation,
                    FinancialEffect = BuildFinancialEffectAuditPayload(
                        decision,
                        stateBefore.PlatformCash,
                        stateBefore.VendorPayable,
                        stateBefore.VendorDebt,
                        marketplaceOrder.Currency,
                        effectState: "Projected",
                        balancesApplied: true)
                });

            shouldPublishEvents = await CompleteRefundAsync(
                refundRequest,
                marketplaceOrder,
                subOrder,
                payment,
                trackedRefund,
                executionRecord,
                subOrderEarnings,
                actorId,
                request,
                ledgerRefundOverride: null,
                correlationId,
                cancellationToken);

            AppendExecutionAuditLog(
                action: GetCompletedAuditAction(request.SimulationKind),
                description: GetCompletedAuditDescription(request.SimulationKind),
                refundRequestId: refundRequest.Id,
                orderNumber: marketplaceOrder.OrderNumber,
                actorId: actorId,
                actorEmail: actorEmail,
                success: true,
                details: new
                {
                    RefundId = refundRequest.Id,
                    FinancialRefundId = trackedRefund.Id,
                    ExecutionId = executionRecord.Id,
                    Amount = refundAmount,
                    Currency = marketplaceOrder.Currency,
                    Method = request.Method,
                    MaskedAccount = refundRequest.PayoutAccountMaskedSnapshot,
                    ActorId = actorId,
                     TransactionReference = request.TransactionReference,
                     request.IsDevelopmentSimulation,
                     request.RequestIdempotencyKey,
                     ExternalMoneyMovementConfirmed = request.ConfirmMoneySent && !request.IsDevelopmentSimulation,
                    FinancialEffect = BuildFinancialEffectAuditPayload(
                        decision,
                        stateBefore.PlatformCash,
                        stateBefore.VendorPayable,
                        stateBefore.VendorDebt,
                        marketplaceOrder.Currency,
                        effectState: "Applied",
                        balancesApplied: true)
                });

            await _dbContext.SaveChangesAsync(cancellationToken);
            var ledgerCorrelationId = trackedRefund.Id.ToString("D");
            var stateAfter = await _financialSafetyGuardService.VerifyStateTransitionAsync(
                refundRequest.VendorId,
                marketplaceOrder.Currency,
                stateBefore,
                new CommerceHubFinancialStateDelta(
                    decision.PlatformCashDelta,
                    decision.PlatformPayableDelta,
                    decision.VendorDebtDelta),
                ledgerCorrelationId,
                ComputeExpectedRefundLedgerEntryCount(decision, trackedRefund),
                CommerceHubFinancialReferenceType.Refund,
                trackedRefund.Id,
                cancellationToken);

            _financialSafetyGuardService.AppendCommittedOperationAudit(
                refundRequest.VendorId,
                actorId,
                string.Equals(actorEmail, "system:return-automation@commercehub.local", StringComparison.OrdinalIgnoreCase)
                    ? "SYSTEM"
                    : "SUPER_ADMIN",
                "Refund",
                refundAmount,
                marketplaceOrder.Currency,
                stateBefore,
                stateAfter,
                ledgerCorrelationId,
                trackedRefund.IdempotencyKey,
                CommerceHubFinancialReferenceType.Refund,
                trackedRefund.Id,
                request.TransactionReference);

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
                await PublishRefundFinanciallyExecutedEventAsync(
                    refundRequest,
                    marketplaceOrder,
                    subOrder,
                    trackedRefund,
                    request,
                    decision,
                    correlationId,
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

        if (marketplaceOrder == null)
        {
            return;
        }

    }

    private async Task<bool> RecoverManualTransferReconciliationAsync(
        Guid refundRequestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginRefund();
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();
        using var financialWorkflowBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var refundRequest = await LoadRefundRequestForUpdateAsync(refundRequestId, cancellationToken);
            if (refundRequest == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await LockRefundOrderGraphForUpdateAsync(refundRequest, cancellationToken);

            var trackedRefund = await LoadTrackedRefundForUpdateAsync(refundRequestId, cancellationToken);
            var executionRecord = await LoadExecutionForUpdateAsync(refundRequestId, cancellationToken);
            if (trackedRefund == null ||
                executionRecord == null ||
                trackedRefund.Status != CommerceHubRefundStatus.ReconciliationRequired)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            var orderItem = refundRequest.OrderItem;
            var subOrder = orderItem.SubOrder;
            var marketplaceOrder = refundRequest.Order;

            var subOrderEarnings = await _dbContext.CommerceHubVendorEarnings
                .Where(item => item.SubOrderId == subOrder.Id && !item.IsDeleted)
                .OrderBy(item => item.CreatedAt)
                .ToListAsync(cancellationToken);

            var refundedAmountSoFar = await _dbContext.CommerceHubRefunds
                .Where(refund =>
                    refund.SubOrderId == subOrder.Id &&
                    refund.Id != trackedRefund.Id &&
                    refund.Status != CommerceHubRefundStatus.Failed &&
                    !refund.IsDeleted)
                .SumAsync(refund => (decimal?)refund.Amount, cancellationToken) ?? 0m;

            var totalAfterRefund = RoundCurrency(refundedAmountSoFar + trackedRefund.Amount);
            var fallbackBreakdown = ResolveAuthoritativeRefundBreakdown(
                refundRequest.OrderItem,
                subOrder,
                refundRequest.Quantity,
                refundedAmountSoFar,
                trackedRefund.Amount,
                totalAfterRefund >= RoundCurrency(subOrder.Total));
            var refundBreakdown = NormalizeTrackedRefundBreakdown(trackedRefund, fallbackBreakdown);

            await EnsureNoProcessingPayoutForRefundEarningsAsync(
                refundRequest.VendorId,
                subOrderEarnings,
                cancellationToken);

            var payment = await ResolveRefundPaymentAsync(marketplaceOrder.Id, subOrderEarnings, cancellationToken);
            CommerceHubRefundReturnCurrencyAuthority.RequireConsistentExecutionCurrency(
                marketplaceOrder,
                subOrder,
                trackedRefund,
                payment,
                $"manual-refund-recovery:{refundRequest.Id:D}");

            var stateBefore = await _financialSafetyGuardService.CaptureStateForUpdateAsync(
                refundRequest.VendorId,
                marketplaceOrder.Currency,
                cancellationToken);

            var hasPaidOutEarning = subOrderEarnings.Any(item => item.Status == CommerceHubVendorEarningStatus.PaidOut);
            var ledgerRefund = CreateLedgerRefundSnapshot(
                trackedRefund,
                refundBreakdown,
                hasPaidOutEarning);
            trackedRefund.IsPostPayoutAdjustment = hasPaidOutEarning;

            var decision = RefundFinancialDecisionEngine.Create(
                subOrder.PaymentMethod,
                hasPaidOutEarning,
                ledgerRefund.Amount,
                refundBreakdown.VendorEarningReversalAmount,
                refundBreakdown.CommissionReversalAmount,
                refundBreakdown.GrossCommissionReversalAmount,
                marketplaceOrder.Currency);

            var oldMarketplaceStatus = marketplaceOrder.Status;
            var oldMarketplacePaymentStatus = marketplaceOrder.PaymentStatus;
            var oldSubOrderStates = marketplaceOrder.SubOrders.ToDictionary(item => item.Id, item => item.Status);
            var oldSubOrderPaymentStates = marketplaceOrder.SubOrders.ToDictionary(item => item.Id, item => item.PaymentStatus);

            var actorId = executionRecord.ExecutedBy != Guid.Empty
                ? executionRecord.ExecutedBy
                : executionRecord.CreatedBy ?? Guid.Empty;
        var recoveryCommand = new ManualRefundExecutionCommand(
                executionRecord.Method,
                executionRecord.TransactionReference,
                executionRecord.Notes,
                 ConfirmMoneySent: true,
                 ConfirmMethodOverride: true,
                 SimulationKind: DevelopmentRefundSimulationKind.None,
                 RequestIdempotencyKey: null);

            var shouldPublishEvents = await CompleteRefundAsync(
                refundRequest,
                marketplaceOrder,
                subOrder,
                payment,
                trackedRefund,
                executionRecord,
                subOrderEarnings,
                actorId,
                recoveryCommand,
                ledgerRefund,
                correlationId,
                cancellationToken);

            AppendExecutionAuditLog(
                action: "REFUND_PAYOUT_RECONCILED",
                description: "CommerceHub manual refund payout local recognition was recovered from persisted external transfer evidence.",
                refundRequestId: refundRequest.Id,
                orderNumber: marketplaceOrder.OrderNumber,
                actorId: actorId == Guid.Empty ? Guid.Empty : actorId,
                actorEmail: "system:refund-recovery@commercehub.local",
                success: true,
                details: new
                {
                    RefundId = refundRequest.Id,
                    FinancialRefundId = trackedRefund.Id,
                    ExecutionId = executionRecord.Id,
                    Amount = trackedRefund.Amount,
                    Currency = trackedRefund.Currency,
                    Method = executionRecord.Method,
                    TransactionReference = executionRecord.TransactionReference,
                    RecoverySource = "ReconciliationRequired",
                    FinancialEffect = BuildFinancialEffectAuditPayload(
                        decision,
                        stateBefore.PlatformCash,
                        stateBefore.VendorPayable,
                        stateBefore.VendorDebt,
                        marketplaceOrder.Currency,
                        effectState: "Recovered",
                        balancesApplied: true)
                });

            await _dbContext.SaveChangesAsync(cancellationToken);

            var ledgerCorrelationId = trackedRefund.Id.ToString("D");
            var stateAfter = await _financialSafetyGuardService.VerifyStateTransitionAsync(
                refundRequest.VendorId,
                marketplaceOrder.Currency,
                stateBefore,
                new CommerceHubFinancialStateDelta(
                    decision.PlatformCashDelta,
                    decision.PlatformPayableDelta,
                    decision.VendorDebtDelta),
                ledgerCorrelationId,
                ComputeExpectedRefundLedgerEntryCount(decision, ledgerRefund),
                CommerceHubFinancialReferenceType.Refund,
                trackedRefund.Id,
                cancellationToken);

            _financialSafetyGuardService.AppendCommittedOperationAudit(
                refundRequest.VendorId,
                actorId,
                "SYSTEM",
                "RefundRecovery",
                trackedRefund.Amount,
                marketplaceOrder.Currency,
                stateBefore,
                stateAfter,
                ledgerCorrelationId,
                trackedRefund.IdempotencyKey,
                CommerceHubFinancialReferenceType.Refund,
                trackedRefund.Id,
                executionRecord.TransactionReference);

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
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<CommerceHubRefundRequest?> LoadRefundRequestForUpdateAsync(
        Guid refundRequestId,
        CancellationToken cancellationToken)
    {
        IQueryable<CommerceHubRefundRequest> query = _dbContext.Database.IsRelational()
            ? _dbContext.CommerceHubRefundRequests.FromSqlInterpolated($@"
                SELECT * FROM ""CommerceHubRefundRequests""
                WHERE ""Id"" = {refundRequestId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE")
            : _dbContext.CommerceHubRefundRequests.Where(item => item.Id == refundRequestId && !item.IsDeleted);

        return await query
            .Include(item => item.Order)
            .Include(item => item.Customer)
            .Include(item => item.ReturnRequest)
            .Include(item => item.OrderItem)
            .ThenInclude(item => item.SubOrder)
            .ThenInclude(item => item.MarketplaceOrder)
            .ThenInclude(item => item.SubOrders)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task LockRefundOrderGraphForUpdateAsync(
        CommerceHubRefundRequest refundRequest,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return;
        }

        var marketplaceOrder = await _dbContext.CommerceHubMarketplaceOrders
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""CommerceHubMarketplaceOrders""
                WHERE ""Id"" = {refundRequest.OrderId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);

        if (marketplaceOrder == null)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_NOT_FOUND",
                "Refund request references an order that no longer exists.",
                StatusCodes.Status404NotFound);
        }

        await _dbContext.CommerceHubSubOrders
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""CommerceHubSubOrders""
                WHERE ""MarketplaceOrderId"" = {refundRequest.OrderId}
                  AND ""IsDeleted"" = FALSE
                ORDER BY ""Id""
                FOR UPDATE")
            .LoadAsync(cancellationToken);

        await _dbContext.CommerceHubSubOrderItems
            .FromSqlInterpolated($@"
                SELECT item.*
                FROM ""CommerceHubSubOrderItems"" item
                INNER JOIN ""CommerceHubSubOrders"" sub_order
                    ON sub_order.""Id"" = item.""SubOrderId""
                WHERE sub_order.""MarketplaceOrderId"" = {refundRequest.OrderId}
                  AND sub_order.""IsDeleted"" = FALSE
                  AND item.""IsDeleted"" = FALSE
                ORDER BY item.""Id""
                FOR UPDATE")
            .LoadAsync(cancellationToken);

        await _dbContext.Entry(refundRequest.Order).ReloadAsync(cancellationToken);
        await _dbContext.Entry(refundRequest.OrderItem).ReloadAsync(cancellationToken);
        await _dbContext.Entry(refundRequest.OrderItem.SubOrder).ReloadAsync(cancellationToken);
        await _dbContext.Entry(refundRequest.Order)
            .Collection(order => order.SubOrders)
            .LoadAsync(cancellationToken);
    }

    private async Task<CommerceHubRefund?> LoadTrackedRefundForUpdateAsync(
        Guid refundRequestId,
        CancellationToken cancellationToken)
    {
        IQueryable<CommerceHubRefund> query = _dbContext.Database.IsRelational()
            ? _dbContext.CommerceHubRefunds.FromSqlInterpolated($@"
                SELECT * FROM ""CommerceHubRefunds""
                WHERE ""RefundRequestId"" = {refundRequestId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE")
            : _dbContext.CommerceHubRefunds.Where(item => item.RefundRequestId == refundRequestId && !item.IsDeleted);

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<CommerceHubRefundExecution?> LoadExecutionForUpdateAsync(
        Guid refundRequestId,
        CancellationToken cancellationToken)
    {
        IQueryable<CommerceHubRefundExecution> query = _dbContext.Database.IsRelational()
            ? _dbContext.CommerceHubRefundExecutions.FromSqlInterpolated($@"
                SELECT * FROM ""CommerceHubRefundExecutions""
                WHERE ""RefundId"" = {refundRequestId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE")
            : _dbContext.CommerceHubRefundExecutions.Where(item => item.RefundId == refundRequestId && !item.IsDeleted);

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<CommerceHubPayment?> ResolveRefundPaymentAsync(
        Guid marketplaceOrderId,
        IReadOnlyCollection<CommerceHubVendorEarning> subOrderEarnings,
        CancellationToken cancellationToken)
    {
        var primaryPaymentId = subOrderEarnings
            .Where(item => item.OriginalEarningId == null && item.PaymentId.HasValue)
            .Select(item => item.PaymentId)
            .FirstOrDefault();

        if (primaryPaymentId.HasValue)
        {
            var payment = await _dbContext.CommerceHubPayments
                .FirstOrDefaultAsync(item => item.Id == primaryPaymentId.Value && !item.IsDeleted, cancellationToken);
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

    private static void ValidatePayoutSnapshot(
        CommerceHubRefundRequest refundRequest,
        ManualRefundExecutionCommand request)
    {
        if (!refundRequest.PayoutMethodSnapshot.HasValue ||
            string.IsNullOrWhiteSpace(refundRequest.PayoutAccountSnapshot) ||
            string.IsNullOrWhiteSpace(refundRequest.PayoutAccountMaskedSnapshot))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYOUT_SNAPSHOT_REQUIRED",
                "Manual payout confirmation requires a locked payout snapshot.",
                StatusCodes.Status409Conflict);
        }

        if (refundRequest.PayoutMethodSnapshot.Value != request.Method && !request.ConfirmMethodOverride)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYOUT_METHOD_OVERRIDE_CONFIRMATION_REQUIRED",
                $"The requested method does not match the payout snapshot ({refundRequest.PayoutMethodSnapshot.Value}). Confirm the override before confirming the payout.",
                StatusCodes.Status409Conflict);
        }
    }

    private static void ValidateReturnExecutionEligibility(CommerceHubRefundRequest refundRequest)
    {
        if (refundRequest.ReturnRequest == null)
        {
            return;
        }

        if (CommerceHubReturnPolicy.CanExecuteRefund(
                refundRequest.RefundExecutionTrigger,
                refundRequest.ReturnRequest.Status))
        {
            return;
        }

        throw new CommerceHubCheckoutException(
            "REFUND_RETURN_TRIGGER_NOT_REACHED",
            $"Manual payout confirmation is blocked until the return is {CommerceHubReturnPolicy.DescribeExecutionTrigger(refundRequest.RefundExecutionTrigger)}. Current return status is {refundRequest.ReturnRequest.Status}.",
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
        payout.ProcessedBy = "system:refund-control";
        payout.ProcessedAtUtc = DateTime.UtcNow;

        _financialAuditService.RecordPayoutFailed(
            tenantId,
            payout.Id,
            payout.Currency,
            "Payout failed because a refund reversed one of its reserved earnings.",
            correlationId);
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

    private async Task<bool> CompleteRefundAsync(
        CommerceHubRefundRequest refundRequest,
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        CommerceHubPayment? payment,
        CommerceHubRefund trackedRefund,
        CommerceHubRefundExecution executionRecord,
        IReadOnlyCollection<CommerceHubVendorEarning> subOrderEarnings,
        Guid actorId,
        ManualRefundExecutionCommand request,
        CommerceHubRefund? ledgerRefundOverride,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();
        using var financialWorkflowBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();
        var effectiveRefund = ledgerRefundOverride ?? trackedRefund;

        foreach (var payoutId in subOrderEarnings
                     .Where(item => item.Status == CommerceHubVendorEarningStatus.InPayout && item.PayoutId.HasValue)
                     .Select(item => item.PayoutId!.Value)
                     .Distinct()
                     .ToList())
        {
            await ReleasePendingPayoutAsync(refundRequest.VendorId, payoutId, correlationId, cancellationToken);
        }

        await EnsureRefundCompletionDoesNotOverrunAsync(
            subOrder,
            payment,
            trackedRefund,
            cancellationToken);

        var completedAtUtc = DateTime.UtcNow;

        executionRecord.IsSuccessful = true;
        executionRecord.FailureReason = null;
        executionRecord.Method = request.Method;
        executionRecord.TransactionReference = request.TransactionReference;
        executionRecord.Notes = request.Notes;
        executionRecord.ExecutedBy = actorId;
        executionRecord.ExecutedAt = completedAtUtc;
        executionRecord.UpdatedBy = actorId;

        trackedRefund.Status = CommerceHubRefundStatus.Completed;
        trackedRefund.Reference = request.TransactionReference;
        trackedRefund.GatewayStatus = request.SimulationKind switch
        {
            DevelopmentRefundSimulationKind.CodDebtReduction => "development-simulated-cod-refund",
            DevelopmentRefundSimulationKind.OnlineBeforePayout => "development-simulated-online-refund",
            _ => "manual-confirmed"
        };
        trackedRefund.FailureReason = null;
        trackedRefund.ProcessedAtUtc = completedAtUtc;

        effectiveRefund.Status = CommerceHubRefundStatus.Completed;
        effectiveRefund.Reference = request.TransactionReference;
        effectiveRefund.GatewayStatus = trackedRefund.GatewayStatus;
        effectiveRefund.FailureReason = null;
        effectiveRefund.ProcessedAtUtc = completedAtUtc;

        refundRequest.Status = CommerceHubRefundRequestStatus.Paid;
        refundRequest.CompletedAtUtc = completedAtUtc;
        refundRequest.FailedAtUtc = null;
        refundRequest.FailureReason = null;
        refundRequest.UpdatedAt = completedAtUtc;
        refundRequest.UpdatedBy = actorId;

        if (payment != null)
        {
            payment.RefundedAmount = RoundCurrency(payment.RefundedAmount + trackedRefund.Amount);
        }

        ApplyRefundStatuses(marketplaceOrder, subOrder, trackedRefund.IsFullRefund);
        ApplyEarningReversal(
            refundRequest.VendorId,
            marketplaceOrder,
            subOrder,
            payment,
            trackedRefund,
            subOrderEarnings,
            effectiveRefund.VendorEarningReversalAmount,
            correlationId);

        _financialAuditService.RecordRefundProcessed(
            refundRequest.VendorId,
            marketplaceOrder.Id,
            subOrder.Id,
            trackedRefund.Id,
            payment?.Id,
            trackedRefund.Amount,
            trackedRefund.Currency,
            correlationId);

        if (effectiveRefund.CommissionReversalAmount > 0)
        {
            _financialAuditService.RecordCommissionReversed(
                refundRequest.VendorId,
                marketplaceOrder.Id,
                subOrder.Id,
                trackedRefund.Id,
                effectiveRefund.CommissionReversalAmount,
                trackedRefund.Currency,
                correlationId);
        }

        await _financialLedgerService.RecordRefundAsync(
            marketplaceOrder,
            subOrder,
            effectiveRefund,
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

            _financialAuditService.RecordVendorEarningReversed(
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

            _financialAuditService.RecordRefundReversal(
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

        _financialAuditService.RecordVendorAdjustmentCreated(
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
                    $"Cannot publish CommerceHub refund-control status event for marketplace order {marketplaceOrder.Id}: affected store authority is empty.");
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
                    "refund-control"),
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
                    $"Cannot publish CommerceHub refund-control sub-order status event for marketplace order {marketplaceOrder.Id}: sub-order {subOrder.Id} has empty StoreId.");
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
                    "refund-control"),
                OccurredAtUtc = DateTime.UtcNow,
                EventVersion = subOrderEventVersion
            }, cancellationToken);
        }
    }

    private async Task PublishRefundFinanciallyExecutedEventAsync(
        CommerceHubRefundRequest refundRequest,
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        CommerceHubRefund trackedRefund,
        ManualRefundExecutionCommand request,
        RefundFinancialDecision decision,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var eventVersion = CommerceHubOrderEventMetadata.ResolveAggregateEventVersion(marketplaceOrder);
        await _eventPublisher.PublishAsync(new CommerceHubRefundFinanciallyExecutedEvent
        {
            MarketplaceOrderId = marketplaceOrder.Id,
            OrderNumber = marketplaceOrder.OrderNumber,
            SubOrderId = subOrder.Id,
            VendorId = refundRequest.VendorId,
            RefundRequestId = refundRequest.Id,
            FinancialRefundId = trackedRefund.Id,
            ReturnId = refundRequest.ReturnRequestId,
            Amount = trackedRefund.Amount,
            VendorDebtDelta = decision.VendorDebtDelta,
            VendorDebtIncreaseAmount = Math.Max(0m, decision.VendorDebtDelta),
            VendorDebtReductionAmount = Math.Max(0m, -decision.VendorDebtDelta),
            Currency = trackedRefund.Currency,
            ExecutionPath = decision.ExecutionPath,
            ExecutionEvidenceType = request.SimulationKind switch
            {
                DevelopmentRefundSimulationKind.CodDebtReduction => "DevelopmentSimulatedCodCustomerRefund",
                DevelopmentRefundSimulationKind.OnlineBeforePayout => "DevelopmentSimulatedOnlineCustomerRefund",
                _ => "ConfirmedExternalTransfer"
            },
            ExternalMoneyMovementConfirmed = request.ConfirmMoneySent && !request.IsDevelopmentSimulation,
            IsDevelopmentSimulation = request.IsDevelopmentSimulation,
            Producer = nameof(RefundExecutionService),
            TenantId = refundRequest.VendorId,
            CorrelationId = correlationId,
            IdempotencyKey = request.RequestIdempotencyKey ?? trackedRefund.IdempotencyKey,
            OccurredAtUtc = DateTime.UtcNow,
            EventVersion = eventVersion
        }, cancellationToken);
    }

    private async Task TryMarkFailedAsync(
        Guid refundRequestId,
        ManualRefundExecutionCommand request,
        Guid actorId,
        string? actorEmail,
        string correlationId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var refundRequest = await LoadRefundRequestForUpdateAsync(refundRequestId, cancellationToken);
            if (refundRequest == null || CommerceHubRefundRequestWorkflowPolicy.IsExecuted(refundRequest.Status))
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            var trackedRefund = await LoadTrackedRefundForUpdateAsync(refundRequestId, cancellationToken);
            var executionRecord = await LoadExecutionForUpdateAsync(refundRequestId, cancellationToken);
            var failureReason = NormalizeFailureReason(exception);
            var executedAtUtc = DateTime.UtcNow;
            var subOrder = refundRequest.OrderItem.SubOrder;
            var subOrderEarnings = await _dbContext.CommerceHubVendorEarnings
                .Where(item => item.SubOrderId == subOrder.Id && !item.IsDeleted)
                .ToListAsync(cancellationToken);
            var refundAmount = trackedRefund?.Amount ?? RefundFinancialCalculator.RoundCurrency(refundRequest.TotalAmount);
            
            // Compute cumulative refund total to determine if this is a full refund
            var refundedAmountSoFar = await _dbContext.CommerceHubRefunds
                .Where(refund =>
                    refund.SubOrderId == subOrder.Id &&
                    refund.Id != (trackedRefund != null ? trackedRefund.Id : Guid.Empty) &&
                    refund.Status != CommerceHubRefundStatus.Failed &&
                    !refund.IsDeleted)
                .SumAsync(refund => (decimal?)refund.Amount, cancellationToken) ?? 0m;
            var totalAfterRefund = RoundCurrency(refundedAmountSoFar + refundAmount);
            var isFullRefund = totalAfterRefund >= RoundCurrency(subOrder.Total);
            
            var fallbackBreakdown = ResolveAuthoritativeRefundBreakdown(
                refundRequest.OrderItem,
                subOrder,
                refundRequest.Quantity,
                refundedAmountSoFar,
                refundAmount,
                isFullRefund);
            var resolvedBreakdown = trackedRefund == null
                ? fallbackBreakdown
                : NormalizeTrackedRefundBreakdown(trackedRefund, fallbackBreakdown);
            var grossCommissionReversalAmount = resolvedBreakdown.GrossCommissionReversalAmount;
            var commissionReversalAmount = resolvedBreakdown.CommissionReversalAmount;
            var promoDiscountReversalAmount = resolvedBreakdown.PromoDiscountReversalAmount;
            var vendorEarningReversalAmount = resolvedBreakdown.VendorEarningReversalAmount;
            var hasPaidOutEarning = trackedRefund?.IsPostPayoutAdjustment
                ?? subOrderEarnings.Any(item => item.Status == CommerceHubVendorEarningStatus.PaidOut);
            var payment = await ResolveRefundPaymentAsync(refundRequest.Order.Id, subOrderEarnings, cancellationToken);
            var vendorPayableBefore = RoundCurrency(
                subOrderEarnings
                    .Where(item => item.Status is not CommerceHubVendorEarningStatus.PaidOut)
                    .Sum(item => item.Amount));
            var vendorDebtSnapshot = await _vendorDebtService.GetVendorDebtSnapshotAsync(refundRequest.VendorId, cancellationToken);
            var platformCashBalance = await _walletService.GetPlatformBalanceAsync(cancellationToken);
            var decision = RefundFinancialDecisionEngine.Create(
                subOrder.PaymentMethod,
                hasPaidOutEarning,
                refundAmount,
                vendorEarningReversalAmount,
                commissionReversalAmount,
                grossCommissionReversalAmount,
                refundRequest.Order.Currency);

            var reconciliationReason = NormalizeManualTransferReconciliationReason(failureReason);

            refundRequest.Status = CommerceHubRefundRequestStatus.Processing;
            refundRequest.FailedAtUtc = null;
            refundRequest.FailureReason = reconciliationReason;
            refundRequest.UpdatedAt = executedAtUtc;
            refundRequest.UpdatedBy = actorId;

            if (executionRecord == null && !string.IsNullOrWhiteSpace(refundRequest.PayoutAccountSnapshot))
            {
                executionRecord = new CommerceHubRefundExecution
                {
                    Id = Guid.NewGuid(),
                    RefundId = refundRequest.Id,
                    Method = request.Method,
                    AccountSnapshot = refundRequest.PayoutAccountSnapshot,
                    TransactionReference = request.TransactionReference,
                    Amount = RefundFinancialCalculator.RoundCurrency(refundRequest.TotalAmount),
                    Currency = refundRequest.Order.Currency,
                    ExecutedBy = actorId,
                    ExecutedAt = executedAtUtc,
                    Notes = request.Notes,
                    IsSuccessful = false,
                    FailureReason = reconciliationReason,
                    CreatedBy = actorId
                };

                _dbContext.CommerceHubRefundExecutions.Add(executionRecord);
            }
            else if (executionRecord != null)
            {
                executionRecord.Method = request.Method;
                executionRecord.TransactionReference = request.TransactionReference;
                executionRecord.Amount = RefundFinancialCalculator.RoundCurrency(refundRequest.TotalAmount);
                executionRecord.Currency = refundRequest.Order.Currency;
                executionRecord.ExecutedBy = actorId;
                executionRecord.ExecutedAt = executedAtUtc;
                executionRecord.Notes = request.Notes;
                executionRecord.IsSuccessful = false;
                executionRecord.FailureReason = reconciliationReason;
                executionRecord.UpdatedBy = actorId;
            }

            if (trackedRefund == null)
            {
                trackedRefund = new CommerceHubRefund
                {
                    Id = Guid.NewGuid(),
                    TenantId = refundRequest.VendorId,
                    SubOrderId = subOrder.Id,
                    RefundRequestId = refundRequest.Id,
                    OrderItemId = refundRequest.OrderItemId,
                    Quantity = refundRequest.Quantity,
                    PaymentId = payment?.Id,
                    Amount = refundAmount,
                    CommissionReversalAmount = commissionReversalAmount,
                    PromoDiscountReversalAmount = promoDiscountReversalAmount,
                    VendorEarningReversalAmount = vendorEarningReversalAmount,
                    Currency = refundRequest.Order.Currency,
                    Status = CommerceHubRefundStatus.ReconciliationRequired,
                    Reason = refundRequest.Reason,
                    Reference = request.TransactionReference,
                    GatewayStatus = "manual_transfer_reconciliation_required",
                    FailureReason = reconciliationReason,
                    IdempotencyKey = BuildExecutionIdempotencyKey(refundRequest),
                    IsFullRefund = isFullRefund,
                    IsPostPayoutAdjustment = hasPaidOutEarning,
                    ProcessingStartedAtUtc = executedAtUtc,
                    CreatedBy = actorId
                };

                _dbContext.CommerceHubRefunds.Add(trackedRefund);
            }
            else if (trackedRefund.Status != CommerceHubRefundStatus.Completed)
            {
                trackedRefund.Status = CommerceHubRefundStatus.ReconciliationRequired;
                trackedRefund.Reference = request.TransactionReference;
                trackedRefund.GatewayStatus = "manual_transfer_reconciliation_required";
                trackedRefund.FailureReason = reconciliationReason;
                trackedRefund.IsFullRefund = isFullRefund;
                trackedRefund.ProcessingStartedAtUtc ??= executedAtUtc;
                trackedRefund.ProcessedAtUtc = null;
                trackedRefund.UpdatedBy = actorId;
            }

            AppendExecutionAuditLog(
                action: "REFUND_PAYOUT_RECONCILIATION_REQUIRED",
                description: "CommerceHub manual refund payout confirmation requires reconciliation after external transfer evidence was supplied.",
                refundRequestId: refundRequest.Id,
                orderNumber: refundRequest.Order.OrderNumber,
                actorId: actorId,
                actorEmail: actorEmail,
                success: false,
                details: new
                {
                    RefundId = refundRequest.Id,
                    Amount = refundRequest.TotalAmount,
                    Currency = refundRequest.Order.Currency,
                    Method = request.Method,
                    MaskedAccount = refundRequest.PayoutAccountMaskedSnapshot,
                    ActorId = actorId,
                    TransactionReference = request.TransactionReference,
                    FailureReason = reconciliationReason,
                    ErrorType = exception.GetType().Name,
                    FinancialEffect = BuildFinancialEffectAuditPayload(
                        decision,
                        platformCashBalance.Balance,
                        vendorPayableBefore,
                        vendorDebtSnapshot.AmountOwedByVendor,
                        refundRequest.Order.Currency,
                        effectState: "NotApplied",
                        balancesApplied: false)
                });

            if (trackedRefund != null)
            {
                _financialAuditService.RecordFinanceReconciliation(
                    "RefundReconciliationRequired",
                    $"Manual refund payout confirmation requires reconciliation after external transfer evidence | TenantId: {refundRequest.VendorId} | SubOrderId: {refundRequest.OrderItem.SubOrder.Id} | RefundId: {trackedRefund.Id} | PaymentId: {trackedRefund.PaymentId} | Currency: {refundRequest.Order.Currency} | Reason: {reconciliationReason}",
                    correlationId);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception markFailureException)
        {
            _logger.LogWarning(
                markFailureException,
                "Failed to persist refund reconciliation-required state | RefundRequestId: {RefundRequestId} | CorrelationId: {CorrelationId}",
                refundRequestId,
                correlationId);
        }
    }

    private void AppendExecutionAuditLog(
        string action,
        string description,
        Guid refundRequestId,
        string? orderNumber,
        Guid actorId,
        string? actorEmail,
        bool success,
        object details)
    {
        var source = string.Equals(actorEmail, "system:return-automation@commercehub.local", StringComparison.OrdinalIgnoreCase)
            ? "ReturnAutomation"
            : "SuperAdminAPI";
        var actorType = source == "ReturnAutomation"
            ? "SYSTEM"
            : SuperAdminSecurityConstants.Role;

        var metadata = new SuperAdminAuditMetadata
        {
            ActorType = actorType,
            ActorId = actorId,
            ActorEmail = actorEmail,
            Action = action,
            EntityType = "CommerceHubRefundRequest",
            EntityId = refundRequestId.ToString(),
            RequestId = orderNumber,
            TimestampUtc = DateTime.UtcNow,
            Details = details
        };

        _dbContext.AuditLogs.Add(new AuditLog
        {
            TenantId = null,
            TimestampUtc = DateTime.UtcNow,
            UserId = actorId,
            UserNameSnapshot = actorEmail ?? SuperAdminSecurityConstants.Role,
            ActionType = action,
            EntityType = "CommerceHubRefundRequest",
            EntityId = refundRequestId.ToString(),
            Description = description,
            AfterDataJson = JsonSerializer.Serialize(metadata),
            ReferenceNumber = string.IsNullOrWhiteSpace(orderNumber) ? refundRequestId.ToString("N") : orderNumber,
            Source = source,
            SuccessFlag = success
        });
    }

    private static object BuildFinancialEffectAuditPayload(
        RefundFinancialDecision decision,
        decimal platformCashBefore,
        decimal vendorPayableBefore,
        decimal vendorDebtBefore,
        string currency,
        string effectState,
        bool balancesApplied)
    {
        var platformCashAfter = balancesApplied
            ? RoundCurrency(platformCashBefore + decision.PlatformCashDelta)
            : RoundCurrency(platformCashBefore);
        var vendorPayableAfter = balancesApplied
            ? RoundCurrency(Math.Max(0m, vendorPayableBefore + decision.PlatformPayableDelta))
            : RoundCurrency(vendorPayableBefore);
        var vendorDebtAfter = balancesApplied
            ? RoundCurrency(Math.Max(0m, vendorDebtBefore + decision.VendorDebtDelta))
            : RoundCurrency(vendorDebtBefore);

        return new
        {
            decision.Scenario,
            decision.PaymentType,
            decision.HasPayoutOccurred,
            decision.ExecutionPath,
            decision.ImpactSummary,
            EffectState = effectState,
            BalancesApplied = balancesApplied,
            Currency = currency,
            PlatformCashDelta = decision.PlatformCashDelta,
            PlatformCashBefore = RoundCurrency(platformCashBefore),
            PlatformCashAfter = platformCashAfter,
            PlatformPayableDelta = decision.PlatformPayableDelta,
            VendorPayableBefore = RoundCurrency(vendorPayableBefore),
            VendorPayableAfter = vendorPayableAfter,
            VendorDebtDelta = decision.VendorDebtDelta,
            VendorDebtBefore = RoundCurrency(vendorDebtBefore),
            VendorDebtAfter = vendorDebtAfter,
            CommissionReceivableDelta = decision.CommissionReceivableDelta
        };
    }

    private static int ComputeExpectedRefundLedgerEntryCount(
        RefundFinancialDecision decision,
        CommerceHubRefund refund)
    {
        var count = 0;
        var grossCommissionReversalAmount = RoundCurrency(
            Math.Max(0m, refund.CommissionReversalAmount + refund.PromoDiscountReversalAmount));

        if (decision.PlatformCashDelta < 0m)
        {
            count += 1;
        }

        if (decision.Scenario is RefundFinancialScenario.OnlineBeforePayout or RefundFinancialScenario.OnlineAfterPayout &&
            refund.VendorEarningReversalAmount > 0m)
        {
            count += 1;
        }

        if (decision.Scenario == RefundFinancialScenario.OnlineBeforePayout &&
            refund.VendorEarningReversalAmount > 0m)
        {
            count += 1;
        }

        if (refund.CommissionReversalAmount > 0m)
        {
            count += 1;
        }

        if (decision.Scenario == RefundFinancialScenario.CashOnDelivery &&
            grossCommissionReversalAmount > 0m)
        {
            count += 1;
        }

        if (refund.PromoDiscountReversalAmount > 0m)
        {
            count += 1;
        }

        return count;
    }

    private static RefundFinancialBreakdown ResolveAuthoritativeRefundBreakdown(
        CommerceHubSubOrderItem orderItem,
        CommerceHubSubOrder subOrder,
        int quantity,
        decimal refundedAmountSoFar,
        decimal refundAmount,
        bool isFullRefund)
    {
        var itemBreakdown = RefundFinancialCalculator.CalculateItemBreakdown(orderItem, quantity);
        if (IsBreakdownConsistent(itemBreakdown, refundAmount))
        {
            return itemBreakdown;
        }

        return RefundFinancialCalculator.CalculateSubOrderBreakdown(
            subOrder,
            refundedAmountSoFar,
            refundAmount,
            isFullRefund);
    }

    private static RefundFinancialBreakdown NormalizeTrackedRefundBreakdown(
        CommerceHubRefund trackedRefund,
        RefundFinancialBreakdown authoritativeBreakdown)
    {
        var persistedBreakdown = new RefundFinancialBreakdown(
            GrossCommissionReversalAmount: RoundCurrency(
                Math.Max(0m, trackedRefund.CommissionReversalAmount + trackedRefund.PromoDiscountReversalAmount)),
            CommissionReversalAmount: RoundCurrency(Math.Max(0m, trackedRefund.CommissionReversalAmount)),
            PromoDiscountReversalAmount: RoundCurrency(Math.Max(0m, trackedRefund.PromoDiscountReversalAmount)),
            VendorEarningReversalAmount: RoundCurrency(Math.Max(0m, trackedRefund.VendorEarningReversalAmount)));

        return IsBreakdownConsistent(persistedBreakdown, trackedRefund.Amount)
            ? persistedBreakdown
            : authoritativeBreakdown;
    }

    private static CommerceHubRefund CreateLedgerRefundSnapshot(
        CommerceHubRefund trackedRefund,
        RefundFinancialBreakdown breakdown,
        bool isPostPayoutAdjustment)
    {
        return new CommerceHubRefund
        {
            Id = trackedRefund.Id,
            TenantId = trackedRefund.TenantId,
            SubOrderId = trackedRefund.SubOrderId,
            RefundRequestId = trackedRefund.RefundRequestId,
            OrderItemId = trackedRefund.OrderItemId,
            Quantity = trackedRefund.Quantity,
            PaymentId = trackedRefund.PaymentId,
            Amount = trackedRefund.Amount,
            CommissionReversalAmount = breakdown.CommissionReversalAmount,
            PromoDiscountReversalAmount = breakdown.PromoDiscountReversalAmount,
            VendorEarningReversalAmount = breakdown.VendorEarningReversalAmount,
            Currency = trackedRefund.Currency,
            Status = trackedRefund.Status,
            Reason = trackedRefund.Reason,
            Reference = trackedRefund.Reference,
            GatewayStatus = trackedRefund.GatewayStatus,
            FailureReason = trackedRefund.FailureReason,
            IdempotencyKey = trackedRefund.IdempotencyKey,
            IsFullRefund = trackedRefund.IsFullRefund,
            IsPostPayoutAdjustment = isPostPayoutAdjustment,
            ProcessingStartedAtUtc = trackedRefund.ProcessingStartedAtUtc,
            ProcessedAtUtc = trackedRefund.ProcessedAtUtc,
            CreatedAt = trackedRefund.CreatedAt,
            CreatedBy = trackedRefund.CreatedBy,
            UpdatedAt = trackedRefund.UpdatedAt,
            UpdatedBy = trackedRefund.UpdatedBy
        };
    }

    private static bool IsBreakdownConsistent(
        RefundFinancialBreakdown breakdown,
        decimal refundAmount)
    {
        if (breakdown.GrossCommissionReversalAmount < 0m ||
            breakdown.CommissionReversalAmount < 0m ||
            breakdown.PromoDiscountReversalAmount < 0m ||
            breakdown.VendorEarningReversalAmount < 0m)
        {
            return false;
        }

        if (RoundCurrency(breakdown.CommissionReversalAmount + breakdown.PromoDiscountReversalAmount) !=
            RoundCurrency(breakdown.GrossCommissionReversalAmount))
        {
            return false;
        }

        return RoundCurrency(breakdown.VendorEarningReversalAmount + breakdown.GrossCommissionReversalAmount) ==
               RoundCurrency(refundAmount);
    }

    private static ManualRefundExecutionCommand NormalizeExecutionRequest(SuperAdminExecuteRefundRequest request)
    {
        if (!request.Method.HasValue || !Enum.IsDefined(request.Method.Value))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_EXECUTION_METHOD_REQUIRED",
                "A valid payout method is required to confirm the refund payout.",
                StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.TransactionReference))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_EXECUTION_REFERENCE_REQUIRED",
                "Transaction reference is required to confirm the refund payout.",
                StatusCodes.Status400BadRequest);
        }

        if (!request.ConfirmMoneySent)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_EXECUTION_CONFIRMATION_REQUIRED",
                "You must confirm that the money has already been sent before marking this refund as paid.",
                StatusCodes.Status400BadRequest);
        }

        var transactionReference = request.TransactionReference.Trim();
        if (transactionReference.Length > 200)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_EXECUTION_REFERENCE_TOO_LONG",
                "Transaction reference cannot exceed 200 characters.",
                StatusCodes.Status400BadRequest);
        }

        string? notes = null;
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            notes = request.Notes.Trim();
            if (notes.Length > 1000)
            {
                throw new CommerceHubCheckoutException(
                    "REFUND_EXECUTION_NOTES_TOO_LONG",
                    "Payout notes cannot exceed 1000 characters.",
                    StatusCodes.Status400BadRequest);
            }
        }

        return new ManualRefundExecutionCommand(
            request.Method.Value,
            transactionReference,
            notes,
            request.ConfirmMoneySent,
            request.ConfirmMethodOverride,
            SimulationKind: DevelopmentRefundSimulationKind.None,
            RequestIdempotencyKey: null);
    }

    private static string NormalizeDevelopmentIdempotencyKey(string idempotencyKey)
    {
        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? throw new CommerceHubCheckoutException(
                "REFUND_EXECUTION_IDEMPOTENCY_REQUIRED",
                "A certification idempotency key is required.",
                StatusCodes.Status400BadRequest)
            : idempotencyKey.Trim();
        if (normalizedIdempotencyKey.Length > 150)
        {
            throw new CommerceHubCheckoutException(
                "REFUND_EXECUTION_IDEMPOTENCY_TOO_LONG",
                "The certification idempotency key cannot exceed 150 characters.",
                StatusCodes.Status400BadRequest);
        }

        return normalizedIdempotencyKey;
    }

    private async Task<DevelopmentRefundSimulationResult> LoadDevelopmentSimulationResultAsync(
        Guid refundRequestId,
        string idempotencyKey,
        DevelopmentRefundSimulationKind simulationKind,
        CancellationToken cancellationToken)
    {
        var expectedReference = simulationKind switch
        {
            DevelopmentRefundSimulationKind.CodDebtReduction => $"DEV-SIM:{idempotencyKey}",
            DevelopmentRefundSimulationKind.OnlineBeforePayout => $"DEV-SIM:ONLINE-REFUND:{idempotencyKey}",
            _ => throw new InvalidOperationException("A Development simulation kind is required.")
        };
        var expectedGatewayStatus = simulationKind switch
        {
            DevelopmentRefundSimulationKind.CodDebtReduction => "development-simulated-cod-refund",
            DevelopmentRefundSimulationKind.OnlineBeforePayout => "development-simulated-online-refund",
            _ => throw new InvalidOperationException("A Development simulation kind is required.")
        };

        var evidence = await _dbContext.CommerceHubRefundRequests
            .AsNoTracking()
            .Where(request => request.Id == refundRequestId && !request.IsDeleted)
            .Select(request => new
            {
                RequestStatus = request.Status,
                PaymentMethod = request.OrderItem.SubOrder.PaymentMethod,
                Refund = _dbContext.CommerceHubRefunds
                    .Where(refund => refund.RefundRequestId == request.Id && !refund.IsDeleted)
                    .Select(refund => new
                    {
                        refund.Status,
                        refund.GatewayStatus,
                        refund.Reference,
                        refund.IsPostPayoutAdjustment
                    })
                    .FirstOrDefault(),
                Execution = _dbContext.CommerceHubRefundExecutions
                    .Where(execution => execution.RefundId == request.Id && !execution.IsDeleted)
                    .Select(execution => new
                    {
                        execution.IsSuccessful,
                        execution.TransactionReference
                    })
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        var expectedPaymentMethod = simulationKind == DevelopmentRefundSimulationKind.CodDebtReduction
            ? CommerceHubPaymentMethod.CashOnDelivery
            : CommerceHubPaymentMethod.Online;
        if (evidence == null ||
            evidence.RequestStatus != CommerceHubRefundRequestStatus.Paid ||
            evidence.PaymentMethod != expectedPaymentMethod ||
            evidence.Refund == null ||
            evidence.Refund.Status != CommerceHubRefundStatus.Completed ||
            evidence.Refund.IsPostPayoutAdjustment ||
            !string.Equals(evidence.Refund.GatewayStatus, expectedGatewayStatus, StringComparison.Ordinal) ||
            !string.Equals(evidence.Refund.Reference, expectedReference, StringComparison.Ordinal) ||
            evidence.Execution == null ||
            !evidence.Execution.IsSuccessful ||
            !string.Equals(evidence.Execution.TransactionReference, expectedReference, StringComparison.Ordinal))
        {
            throw new CommerceHubCheckoutException(
                "DEVELOPMENT_REFUND_SIMULATION_EVIDENCE_MISMATCH",
                "The persisted refund evidence does not match this Development simulation request.",
                StatusCodes.Status409Conflict);
        }

        return new DevelopmentRefundSimulationResult(
            refundRequestId,
            idempotencyKey,
            simulationKind == DevelopmentRefundSimulationKind.CodDebtReduction
                ? "CashOnDeliveryDebtReduction"
                : "OnlineBeforePayout",
            simulationKind == DevelopmentRefundSimulationKind.CodDebtReduction
                ? "DevelopmentSimulatedCodCustomerRefund"
                : "DevelopmentSimulatedOnlineCustomerRefund",
            IsDevelopmentSimulation: true,
            ExternalMoneyMovementConfirmed: false,
            expectedReference);
    }

    private static string GetInitiatedAuditAction(DevelopmentRefundSimulationKind simulationKind) => simulationKind switch
    {
        DevelopmentRefundSimulationKind.CodDebtReduction => "REFUND_COD_DEBT_REDUCTION_SIMULATION_INITIATED",
        DevelopmentRefundSimulationKind.OnlineBeforePayout => "REFUND_ONLINE_BEFORE_PAYOUT_SIMULATION_INITIATED",
        _ => "REFUND_PAYOUT_INITIATED"
    };

    private static string GetInitiatedAuditDescription(DevelopmentRefundSimulationKind simulationKind) => simulationKind switch
    {
        DevelopmentRefundSimulationKind.CodDebtReduction => "Development-only CommerceHub COD debt-reduction simulation started; no external money movement was confirmed.",
        DevelopmentRefundSimulationKind.OnlineBeforePayout => "Development-only CommerceHub online before-payout refund simulation started; no external money movement was confirmed.",
        _ => "CommerceHub manual refund payout confirmation started."
    };

    private static string GetCompletedAuditAction(DevelopmentRefundSimulationKind simulationKind) => simulationKind switch
    {
        DevelopmentRefundSimulationKind.CodDebtReduction => "REFUND_COD_DEBT_REDUCTION_SIMULATION_COMPLETED",
        DevelopmentRefundSimulationKind.OnlineBeforePayout => "REFUND_ONLINE_BEFORE_PAYOUT_SIMULATION_COMPLETED",
        _ => "REFUND_PAYOUT_CONFIRMED"
    };

    private static string GetCompletedAuditDescription(DevelopmentRefundSimulationKind simulationKind) => simulationKind switch
    {
        DevelopmentRefundSimulationKind.CodDebtReduction => "Development-only CommerceHub COD debt reduction and internal refund recognition completed; no external money movement was confirmed.",
        DevelopmentRefundSimulationKind.OnlineBeforePayout => "Development-only CommerceHub online before-payout refund recognition completed; no external money movement was confirmed.",
        _ => "CommerceHub manual refund payout was confirmed successfully."
    };

    private static string NormalizeFailureReason(Exception exception)
    {
        var reason = exception switch
        {
            CommerceHubCheckoutException checkoutException => checkoutException.Message,
            _ => "Manual refund payout confirmation failed unexpectedly."
        };

        return reason.Length <= 2000 ? reason : reason[..2000];
    }

    private static string NormalizeManualTransferReconciliationReason(string failureReason)
    {
        const string prefix = "External manual refund transfer was confirmed by the operator, but POS could not complete local financial recognition. Reconciliation required.";
        var reason = string.IsNullOrWhiteSpace(failureReason)
            ? prefix
            : $"{prefix} Local failure: {failureReason}";

        return reason.Length <= 1000 ? reason : reason[..1000];
    }

    private static string BuildExecutionIdempotencyKey(CommerceHubRefundRequest refundRequest) =>
        $"refund-exec-{refundRequest.Id:N}";

    private static bool IsSerializationFailure(Exception ex)
    {
        return ex switch
        {
            PostgresException postgresException when postgresException.SqlState == "40001" => true,
            DbUpdateException dbUpdateException when dbUpdateException.InnerException is PostgresException postgresException &&
                                                   postgresException.SqlState == "40001" => true,
            _ => false
        };
    }

    private static bool ShouldPersistFailure(Exception ex) =>
        ex is not CommerceHubCheckoutException;

    private static decimal RoundCurrency(decimal amount) =>
        CommissionCalculator.RoundAmount(amount);

    private sealed record ManualRefundExecutionCommand(
        RefundPayoutMethod Method,
        string TransactionReference,
        string? Notes,
        bool ConfirmMoneySent,
        bool ConfirmMethodOverride,
        DevelopmentRefundSimulationKind SimulationKind,
        string? RequestIdempotencyKey)
    {
        public bool IsDevelopmentSimulation => SimulationKind != DevelopmentRefundSimulationKind.None;
    }

    private enum DevelopmentRefundSimulationKind
    {
        None,
        CodDebtReduction,
        OnlineBeforePayout
    }
}
