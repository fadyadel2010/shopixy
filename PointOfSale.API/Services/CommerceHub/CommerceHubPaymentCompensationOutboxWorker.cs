using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using System.Text.Json;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub;

public sealed class CommerceHubPaymentCompensationOutboxWorker : BackgroundService
{
    private const int BatchSize = 25;
    private const int EscalationAttemptThreshold = 10;
    private const string ProviderRefundCompensationIdempotencyKeyPrefix = "chpay-provider-refund:";
    private static readonly TimeSpan ClaimLeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommerceHubPaymentCompensationOutboxWorker> _logger;

    public CommerceHubPaymentCompensationOutboxWorker(
        IServiceProvider serviceProvider,
        ILogger<CommerceHubPaymentCompensationOutboxWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommerceHub payment compensation worker failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var nowUtc = DateTime.UtcNow;

        var messageIds = await dbContext.CommerceHubPaymentCompensationOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.ProcessedAtUtc == null &&
                !message.IsDeleted &&
                ((message.ProviderTransactionId != null && message.ProviderTransactionId.StartsWith("DEV-SIM:ONLINE:")) ||
                 (!message.RequiresOperatorReconciliation &&
                  (message.NextRetryAtUtc == null || message.NextRetryAtUtc <= nowUtc))) &&
                (message.ProcessingLeaseExpiresAtUtc == null || message.ProcessingLeaseExpiresAtUtc <= nowUtc))
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(BatchSize)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken);

        foreach (var messageId in messageIds)
        {
            await ProcessMessageAsync(messageId, cancellationToken);
        }
    }

    private async Task ProcessMessageAsync(Guid messageId, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var initialMessage = await dbContext.CommerceHubPaymentCompensationOutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == messageId, cancellationToken);

        if (initialMessage == null || !IsEligibleForProcessing(initialMessage, DateTime.UtcNow))
        {
            return;
        }

        var claimResult = await TryClaimMessageAsync(dbContext, initialMessage, cancellationToken);
        if (claimResult == ClaimOutcome.Deferred)
        {
            _logger.LogDebug(
                "CommerceHub payment compensation message deferred until earlier same-payment compensation is resolved | MessageId: {MessageId} | PaymentId: {PaymentId}",
                initialMessage.Id,
                initialMessage.PaymentId);
            return;
        }

        if (!claimResult.IsClaimed)
        {
            return;
        }

        dbContext.ChangeTracker.Clear();

        var message = await dbContext.CommerceHubPaymentCompensationOutboxMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item =>
                item.Id == messageId &&
                item.ProcessingLeaseId == claimResult.LeaseId,
                cancellationToken);

        if (message == null)
        {
            return;
        }

        var payment = await dbContext.CommerceHubPayments
            .IgnoreQueryFilters()
            .Include(item => item.Order)
                .ThenInclude(order => order.SubOrders)
                    .ThenInclude(subOrder => subOrder.Items)
            .Include(item => item.Items)
            .Include(item => item.Attempts)
            .FirstOrDefaultAsync(item => item.Id == message.PaymentId && !item.IsDeleted, cancellationToken);

        if (payment == null)
        {
            await RecordFailureAsync(dbContext, message, "Payment no longer exists for compensation refund.", cancellationToken);
            return;
        }

        if (IsDevelopmentSimulationCompensation(message, payment))
        {
            await CloseDevelopmentSimulationCompensationAsync(
                dbContext,
                message,
                payment,
                cancellationToken);
            return;
        }

        var refundGateway = scope.ServiceProvider.GetRequiredService<ICommerceHubRefundGatewayService>();
        var financialLedger = scope.ServiceProvider.GetRequiredService<ICommerceHubFinancialLedgerService>();
        var auditService = scope.ServiceProvider.GetRequiredService<ICommerceHubFinancialAuditService>();
        var refundCompletionValidator = scope.ServiceProvider.GetRequiredService<IRefundCompletionValidator>();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        try
        {
            if (RequiresGatewayOutcomeReconciliation(message))
            {
                await RecordGatewayOutcomeUncertainAsync(
                    dbContext,
                    message,
                    payment,
                    "A previous refund request was durably marked as started, but no gateway reference/status was recorded. Automatic retry is blocked to prevent a duplicate external refund.",
                    cancellationToken);
                return;
            }

            var result = IsConfirmedGatewayStatus(message.GatewayStatus)
                ? BuildGatewayResultFromPersistedOutcome(message, payment.Provider)
                : HasDurableGatewayOutcome(message)
                    ? await refundGateway.RefreshRefundStatusAsync(
                        payment,
                        message.GatewayReference,
                        message.GatewayStatus,
                        cancellationToken)
                    : await ExecuteRefundWithDurableStartMarkerAsync(
                        dbContext,
                        refundGateway,
                        payment,
                        message,
                        cancellationToken);

            if (!result.Accepted)
            {
                await RecordFailureAsync(
                    dbContext,
                    message,
                    result.FailureReason ?? "Payment compensation refund was rejected by the gateway.",
                    cancellationToken);
                return;
            }

            if (!HasDurableGatewayOutcome(message) ||
                !string.Equals(message.GatewayStatus, result.GatewayStatus, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(message.GatewayReference, result.Reference, StringComparison.OrdinalIgnoreCase))
            {
                await PersistGatewayOutcomeAsync(dbContext, message, result, cancellationToken);
            }

            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;

            try
            {
                using (CommerceHubBoundaryMutationScope.BeginPayment())
                using (CommerceHubBoundaryMutationScope.BeginRefund())
                {
                    // Apply payment state mutations within transactional scope
                    if (result.IsConfirmed)
                    {
                        if (IsProviderRefundReconciliationMessage(message))
                        {
                            await ReconcileConfirmedProviderRefundAsync(
                                dbContext,
                                financialLedger,
                                auditService,
                                refundCompletionValidator,
                                eventPublisher,
                                payment,
                                message,
                                result.Reference,
                                cancellationToken);
                        }
                        else
                        {
                            payment.Status = CommerceHubPaymentRecordStatus.Refunded;
                            payment.RefundedAmount = Math.Round(payment.RefundedAmount + message.Amount, 2, MidpointRounding.AwayFromZero);

                            if (payment.ProvisionalRefundedAmount > 0)
                            {
                                var provisionalToMerge = Math.Min(payment.ProvisionalRefundedAmount, message.Amount);
                                payment.ProvisionalRefundedAmount = Math.Round(
                                    payment.ProvisionalRefundedAmount - provisionalToMerge,
                                    2,
                                    MidpointRounding.AwayFromZero);
                            }

                            payment.FailureReason = null;

                            await RecordCompensationRefundAccountingAsync(
                                financialLedger,
                                payment,
                                message,
                                result.Reference,
                                cancellationToken);
                        }

                        message.ProcessedAtUtc = DateTime.UtcNow;
                        message.NextRetryAtUtc = null;
                        message.LastError = null;
                    }
                    else
                    {
                        payment.Status = CommerceHubPaymentRecordStatus.RefundRequired;
                        message.AttemptCount++;
                        message.ProcessedAtUtc = null;
                        message.NextRetryAtUtc = DateTime.UtcNow.AddMinutes(15);
                        message.LastError = $"Refund compensation accepted by {result.Provider} but still requires gateway confirmation. Reference: {result.Reference ?? "n/a"}.";
                        payment.FailureReason = message.LastError;
                    }

                    payment.UpdatedAt = DateTime.UtcNow;
                    message.ProcessingLeaseId = null;
                    message.ProcessingLeaseExpiresAtUtc = null;
                    message.IsFailed = false;
                    message.UpdatedAt = DateTime.UtcNow;

                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            }
            catch
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                throw;
            }

            _logger.LogWarning(
                "CommerceHub captured payment compensation refund accepted | PaymentId: {PaymentId} | MessageId: {MessageId} | Provider: {Provider} | Reference: {Reference} | Confirmed: {Confirmed} | Processed: {Processed}",
                payment.Id,
                message.Id,
                result.Provider,
                result.Reference,
                result.IsConfirmed,
                message.ProcessedAtUtc != null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("store allocation snapshot", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("refund allocation snapshot", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("provider refund compensation", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("cannot be accounted because no affected store tenant", StringComparison.OrdinalIgnoreCase))
        {
            // Corrupt or missing immutable allocation snapshots are data integrity issues
            // that cannot be fixed by retrying - route to operator reconciliation
            dbContext.ChangeTracker.Clear();
            var failedMessage = await dbContext.CommerceHubPaymentCompensationOutboxMessages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == message.Id, cancellationToken);

            if (failedMessage != null)
            {
                // Reload payment entity after ChangeTracker.Clear() so changes are tracked
                var reloadedPayment = await dbContext.CommerceHubPayments
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.Id == payment.Id, cancellationToken);

                if (reloadedPayment != null)
                {
                    await RecordGatewayOutcomeUncertainAsync(dbContext, failedMessage, reloadedPayment, ex.Message, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            dbContext.ChangeTracker.Clear();
            var failedMessage = await dbContext.CommerceHubPaymentCompensationOutboxMessages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == message.Id, cancellationToken);

            if (failedMessage != null)
            {
                if (RequiresGatewayOutcomeReconciliation(failedMessage))
                {
                    // Reload payment entity after ChangeTracker.Clear() so changes are tracked
                    var reloadedPayment = await dbContext.CommerceHubPayments
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(p => p.Id == payment.Id, cancellationToken);

                    if (reloadedPayment != null)
                    {
                        await RecordGatewayOutcomeUncertainAsync(dbContext, failedMessage, reloadedPayment, ex.Message, cancellationToken);
                    }
                }
                else
                {
                    await RecordFailureAsync(dbContext, failedMessage, ex.Message, cancellationToken);
                }
            }
        }
    }

    private static bool IsProviderRefundReconciliationMessage(
        CommerceHubPaymentCompensationOutboxMessage message) =>
        message.IdempotencyKey.StartsWith(
            ProviderRefundCompensationIdempotencyKeyPrefix,
            StringComparison.Ordinal);

    private async Task ReconcileConfirmedProviderRefundAsync(
        ApplicationDbContext dbContext,
        ICommerceHubFinancialLedgerService financialLedger,
        ICommerceHubFinancialAuditService auditService,
        IRefundCompletionValidator refundCompletionValidator,
        IEventPublisher eventPublisher,
        CommerceHubPayment payment,
        CommerceHubPaymentCompensationOutboxMessage message,
        string? gatewayReference,
        CancellationToken cancellationToken)
    {
        var allocations = ParseRefundAllocationSnapshot(message.RefundAllocationSnapshotJson, message.Amount, message.Id);
        if (payment.Order == null)
        {
            throw new InvalidOperationException(
                $"CommerceHub provider refund compensation {message.Id} cannot reconcile because payment {payment.Id} has no marketplace order loaded.");
        }

        var marketplaceOrder = payment.Order;
        var correlationId = message.CorrelationId ?? message.Id.ToString("N");
        var oldMarketplaceStatus = marketplaceOrder.Status;
        var oldMarketplacePaymentStatus = marketplaceOrder.PaymentStatus;
        var oldSubOrderStates = marketplaceOrder.SubOrders.ToDictionary(item => item.Id, item => item.Status);
        var oldSubOrderPaymentStates = marketplaceOrder.SubOrders.ToDictionary(item => item.Id, item => item.PaymentStatus);
        var subOrderIds = allocations.Select(item => item.SubOrderId).Distinct().ToList();
        var subOrders = marketplaceOrder.SubOrders
            .Where(subOrder => subOrderIds.Contains(subOrder.Id))
            .ToDictionary(subOrder => subOrder.Id);
        var earningsBySubOrder = await dbContext.CommerceHubVendorEarnings
            .Where(item => subOrderIds.Contains(item.SubOrderId) && !item.IsDeleted)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();
        using var financialWorkflowBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();

        foreach (var payoutId in earningsBySubOrder
                     .Where(item => item.Status == CommerceHubVendorEarningStatus.InPayout && item.PayoutId.HasValue)
                     .Select(item => new { item.TenantId, PayoutId = item.PayoutId!.Value })
                     .Distinct()
                     .ToList())
        {
            await ReleasePendingPayoutAsync(
                dbContext,
                auditService,
                payoutId.TenantId,
                payoutId.PayoutId,
                correlationId,
                cancellationToken);
        }

        foreach (var allocation in allocations)
        {
            if (!subOrders.TryGetValue(allocation.SubOrderId, out var subOrder))
            {
                throw new InvalidOperationException(
                    $"CommerceHub provider refund compensation {message.Id} references sub-order {allocation.SubOrderId} that does not belong to payment order {payment.OrderId}.");
            }

            var refund = await GetOrCreateProviderRefundAsync(
                dbContext,
                payment,
                message,
                subOrder,
                allocation,
                gatewayReference,
                cancellationToken);

            var subOrderEarnings = earningsBySubOrder
                .Where(item => item.SubOrderId == subOrder.Id)
                .ToList();

            await refundCompletionValidator.ValidateRefundDoesNotExceedTotalsAsync(
                subOrder,
                payment,
                refund,
                cancellationToken);

            refund.Status = CommerceHubRefundStatus.Completed;
            refund.GatewayStatus = message.GatewayStatus ?? "confirmed";
            refund.Reference = gatewayReference ?? message.GatewayReference ?? message.ProviderTransactionId;
            refund.FailureReason = null;
            refund.ProcessedAtUtc = DateTime.UtcNow;
            refund.IsPostPayoutAdjustment = subOrderEarnings.Any(item => item.Status == CommerceHubVendorEarningStatus.PaidOut);

            ApplyRefundStatuses(marketplaceOrder, subOrder, allocation.IsFullRefund);
            ApplyEarningReversal(
                dbContext,
                auditService,
                allocation.TenantId,
                marketplaceOrder,
                subOrder,
                payment,
                refund,
                subOrderEarnings,
                allocation.VendorEarningReversalAmount,
                correlationId);

            auditService.RecordRefundProcessed(
                allocation.TenantId,
                marketplaceOrder.Id,
                subOrder.Id,
                refund.Id,
                payment.Id,
                refund.Amount,
                refund.Currency,
                correlationId);

            if (refund.CommissionReversalAmount > 0m)
            {
                auditService.RecordCommissionReversed(
                    allocation.TenantId,
                    marketplaceOrder.Id,
                    subOrder.Id,
                    refund.Id,
                    refund.CommissionReversalAmount,
                    refund.Currency,
                    correlationId);
            }

            await financialLedger.RecordRefundAsync(
                marketplaceOrder,
                subOrder,
                refund,
                cancellationToken);
        }

        payment.Status = CommerceHubPaymentRecordStatus.Paid;
        payment.RefundedAmount = RoundCurrency(payment.RefundedAmount + message.Amount);
        if (payment.ProvisionalRefundedAmount > 0m)
        {
            var provisionalToMerge = Math.Min(payment.ProvisionalRefundedAmount, message.Amount);
            payment.ProvisionalRefundedAmount = RoundCurrency(payment.ProvisionalRefundedAmount - provisionalToMerge);
        }

        payment.FailureReason = null;
        payment.UpdatedAt = DateTime.UtcNow;

        var paymentAttempt = payment.Attempts
            .OrderByDescending(item => item.AttemptNumber)
            .FirstOrDefault(item => item.Status == CommerceHubPaymentRecordStatus.RefundRequired) ??
            payment.Attempts.OrderByDescending(item => item.AttemptNumber).FirstOrDefault();
        if (paymentAttempt != null)
        {
            paymentAttempt.Status = CommerceHubPaymentRecordStatus.Paid;
            paymentAttempt.FailureReason = null;
            paymentAttempt.ProviderIntentId ??= message.ProviderIntentId;
            paymentAttempt.ProviderTransactionId ??= gatewayReference ?? message.ProviderTransactionId ?? message.GatewayReference;
            paymentAttempt.CompletedAtUtc ??= DateTime.UtcNow;
        }

        await PublishRefundStatusEventsAsync(
            eventPublisher,
            marketplaceOrder,
            correlationId,
            oldMarketplaceStatus,
            oldMarketplacePaymentStatus,
            oldSubOrderStates,
            oldSubOrderPaymentStates,
            cancellationToken);
    }

    private static async Task<CommerceHubRefund> GetOrCreateProviderRefundAsync(
        ApplicationDbContext dbContext,
        CommerceHubPayment payment,
        CommerceHubPaymentCompensationOutboxMessage message,
        CommerceHubSubOrder subOrder,
        ProviderRefundAllocationSnapshotEntry allocation,
        string? gatewayReference,
        CancellationToken cancellationToken)
    {
        var refundIdempotencyKey = BuildProviderRefundIdempotencyKey(message.Id, subOrder.Id);
        var refund = await dbContext.CommerceHubRefunds
            .FirstOrDefaultAsync(
                item => item.IdempotencyKey == refundIdempotencyKey && !item.IsDeleted,
                cancellationToken);

        if (refund == null)
        {
            refund = new CommerceHubRefund
            {
                Id = InventoryEventIdFactory.Create("commercehub-provider-refund", message.Id, subOrder.Id),
                TenantId = allocation.TenantId,
                SubOrderId = subOrder.Id,
                PaymentId = payment.Id,
                Amount = allocation.Amount,
                CommissionReversalAmount = allocation.CommissionReversalAmount,
                PromoDiscountReversalAmount = allocation.PromoDiscountReversalAmount,
                VendorEarningReversalAmount = allocation.VendorEarningReversalAmount,
                Currency = subOrder.Currency,
                Status = CommerceHubRefundStatus.Pending,
                Reason = message.Reason,
                Reference = gatewayReference ?? message.GatewayReference,
                GatewayStatus = message.GatewayStatus ?? "confirmed",
                IdempotencyKey = refundIdempotencyKey,
                IsFullRefund = allocation.IsFullRefund
            };
            dbContext.CommerceHubRefunds.Add(refund);
            return refund;
        }

        refund.TenantId = allocation.TenantId;
        refund.PaymentId = payment.Id;
        refund.Amount = allocation.Amount;
        refund.CommissionReversalAmount = allocation.CommissionReversalAmount;
        refund.PromoDiscountReversalAmount = allocation.PromoDiscountReversalAmount;
        refund.VendorEarningReversalAmount = allocation.VendorEarningReversalAmount;
        refund.Currency = subOrder.Currency;
        refund.Reason = message.Reason;
        refund.Reference = gatewayReference ?? message.GatewayReference;
        refund.GatewayStatus = message.GatewayStatus ?? "confirmed";
        refund.IsFullRefund = allocation.IsFullRefund;
        refund.FailureReason = null;
        return refund;
    }

    private static async Task ReleasePendingPayoutAsync(
        ApplicationDbContext dbContext,
        ICommerceHubFinancialAuditService auditService,
        Guid tenantId,
        Guid payoutId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var payoutBoundary = CommerceHubBoundaryMutationScope.BeginPayout();
        var payout = await dbContext.CommerceHubPayouts
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
        payout.ProcessedBy = "system:provider-refund";
        payout.ProcessedAtUtc = DateTime.UtcNow;

        auditService.RecordPayoutFailed(
            tenantId,
            payout.Id,
            payout.Currency,
            "Payout failed because a provider-confirmed refund reversed one of its reserved earnings.",
            correlationId);
    }

    private static void ApplyEarningReversal(
        ApplicationDbContext dbContext,
        ICommerceHubFinancialAuditService auditService,
        Guid tenantId,
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        CommerceHubPayment? payment,
        CommerceHubRefund refund,
        IReadOnlyCollection<CommerceHubVendorEarning> subOrderEarnings,
        decimal reversalAmount,
        string correlationId)
    {
        if (reversalAmount <= 0m || subOrderEarnings.Count == 0)
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

            auditService.RecordVendorEarningReversed(
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

            dbContext.CommerceHubVendorEarnings.Add(adjustment);

            auditService.RecordRefundReversal(
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

        dbContext.CommerceHubVendorEarnings.Add(partialAdjustment);

        auditService.RecordVendorAdjustmentCreated(
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
        IEventPublisher eventPublisher,
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

            await eventPublisher.PublishAsync(new CommerceHubMarketplaceOrderStatusUpdatedEvent
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

            await eventPublisher.PublishAsync(new CommerceHubSubOrderStatusUpdatedEvent
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

    private static List<ProviderRefundAllocationSnapshotEntry> ParseRefundAllocationSnapshot(
        string? json,
        decimal expectedAmount,
        Guid messageId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                $"CommerceHub provider refund compensation {messageId} is missing an immutable refund allocation snapshot.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"CommerceHub provider refund compensation {messageId} contains a malformed refund allocation snapshot with invalid JSON syntax.",
                ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"CommerceHub provider refund compensation {messageId} contains a malformed refund allocation snapshot.");
            }

            var allocations = new List<ProviderRefundAllocationSnapshotEntry>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                allocations.Add(new ProviderRefundAllocationSnapshotEntry(
                    ReadRequiredGuid(item, "SubOrderId", "Provider refund allocation snapshot contains an invalid sub-order id."),
                    ReadRequiredGuid(item, "TenantId", "Provider refund allocation snapshot contains an invalid tenant id."),
                    ReadRequiredDecimal(item, "Amount", "Provider refund allocation snapshot contains an invalid amount."),
                    ReadRequiredBoolean(item, "IsFullRefund", "Provider refund allocation snapshot contains an invalid full-refund flag."),
                    ReadRequiredDecimal(item, "RefundedAmountBefore", "Provider refund allocation snapshot contains an invalid refunded-before amount."),
                    ReadRequiredDecimal(item, "CommissionReversalAmount", "Provider refund allocation snapshot contains an invalid commission reversal amount."),
                    ReadRequiredDecimal(item, "PromoDiscountReversalAmount", "Provider refund allocation snapshot contains an invalid promotion reversal amount."),
                    ReadRequiredDecimal(item, "VendorEarningReversalAmount", "Provider refund allocation snapshot contains an invalid vendor earning reversal amount.")));
            }

            if (allocations.Count == 0)
            {
                throw new InvalidOperationException(
                    $"CommerceHub provider refund compensation {messageId} has an empty immutable refund allocation snapshot.");
            }

            var allocatedAmount = RoundCurrency(allocations.Sum(item => item.Amount));
            var expectedRoundedAmount = RoundCurrency(expectedAmount);
            if (allocatedAmount != expectedRoundedAmount)
            {
                throw new InvalidOperationException(
                    $"CommerceHub provider refund compensation {messageId} allocation total {allocatedAmount} does not match expected amount {expectedRoundedAmount}.");
            }

            return allocations;
        }
    }

    private static Guid ReadRequiredGuid(JsonElement item, string propertyName, string error)
    {
        if (!item.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(property.GetString(), out var value) ||
            value == Guid.Empty)
        {
            throw new InvalidOperationException(error);
        }

        return value;
    }

    private static decimal ReadRequiredDecimal(JsonElement item, string propertyName, string error)
    {
        if (!item.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidOperationException(error);
        }

        return RoundCurrency(property.GetDecimal());
    }

    private static bool ReadRequiredBoolean(JsonElement item, string propertyName, string error)
    {
        if (!item.TryGetProperty(propertyName, out var property) ||
            (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False))
        {
            throw new InvalidOperationException(error);
        }

        return property.GetBoolean();
    }

    private static string BuildProviderRefundIdempotencyKey(Guid messageId, Guid subOrderId) =>
        $"CH_PROVIDER_REFUND:{messageId:N}:{subOrderId:N}";

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static async Task RecordCompensationRefundAccountingAsync(
        ICommerceHubFinancialLedgerService financialLedger,
        CommerceHubPayment payment,
        CommerceHubPaymentCompensationOutboxMessage message,
        string? gatewayReference,
        CancellationToken cancellationToken)
    {
        var allocations = ResolveStoreAllocations(message);

        if (allocations.Count == 0)
        {
            if (!message.TenantId.HasValue || message.TenantId.Value == Guid.Empty)
            {
                throw new InvalidOperationException(
                    $"CommerceHub payment compensation {message.Id} cannot be accounted because no affected store tenant could be resolved.");
            }

            await financialLedger.RecordTransactionAsync(
                CommerceHubFinancialEntityType.Platform,
                null,
                CommerceHubFinancialTransactionType.Adjustment,
                CommerceHubFinancialTransactionDirection.Credit,
                message.Amount,
                message.Currency,
                CommerceHubFinancialReferenceType.Adjustment,
                message.Id,
                BuildCompensationRefundDescription(payment, message, gatewayReference),
                idempotencyKey: BuildCompensationLedgerIdempotencyKey(message.Id, message.TenantId.Value),
                correlationId: message.CorrelationId ?? message.Id.ToString("N"),
                reason: message.Reason,
                financialBucketOverride: CommerceHubFinancialBucket.Cash,
                accountingTenantId: message.TenantId.Value,
                cancellationToken: cancellationToken);
            return;
        }

        foreach (var allocation in allocations)
        {
            var amount = allocation.Amount;
            if (amount <= 0m)
            {
                continue;
            }

            await financialLedger.RecordTransactionAsync(
                CommerceHubFinancialEntityType.Platform,
                null,
                CommerceHubFinancialTransactionType.Adjustment,
                CommerceHubFinancialTransactionDirection.Credit,
                amount,
                message.Currency,
                CommerceHubFinancialReferenceType.Adjustment,
                message.Id,
                BuildCompensationRefundDescription(payment, message, gatewayReference),
                idempotencyKey: BuildCompensationLedgerIdempotencyKey(message.Id, allocation.TenantId),
                correlationId: message.CorrelationId ?? message.Id.ToString("N"),
                reason: message.Reason,
                financialBucketOverride: CommerceHubFinancialBucket.Cash,
                accountingTenantId: allocation.TenantId,
                cancellationToken: cancellationToken);
        }
    }

    private static List<CompensationStoreAllocation> ResolveStoreAllocations(
        CommerceHubPaymentCompensationOutboxMessage message)
    {
        var snapshotAllocations = ParseStoreAllocationSnapshot(message.StoreAllocationSnapshotJson, message.Amount);
        if (snapshotAllocations.Count > 0)
        {
            return snapshotAllocations;
        }

        if (message.TenantId.HasValue && message.TenantId.Value != Guid.Empty)
        {
            return
            [
                new CompensationStoreAllocation(
                    message.TenantId.Value,
                    Math.Round(message.Amount, 2, MidpointRounding.AwayFromZero))
            ];
        }

        var affectedStoreIds = ParseTenantIds(message.AffectedStoreIdsJson);
        if (affectedStoreIds.Count == 1)
        {
            return
            [
                new CompensationStoreAllocation(
                    affectedStoreIds[0],
                    Math.Round(message.Amount, 2, MidpointRounding.AwayFromZero))
            ];
        }

        throw new InvalidOperationException(
            $"CommerceHub payment compensation {message.Id} is missing an immutable store allocation snapshot for a multi-store refund compensation.");
    }

    private static List<CompensationStoreAllocation> ParseStoreAllocationSnapshot(string? json, decimal expectedAmount)
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

            var allocations = new List<CompensationStoreAllocation>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("TenantId", out var tenantIdProperty) ||
                    tenantIdProperty.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(tenantIdProperty.GetString(), out var tenantId) ||
                    tenantId == Guid.Empty)
                {
                    throw new InvalidOperationException("Compensation store allocation snapshot contains an invalid tenant id.");
                }

                if (!item.TryGetProperty("Amount", out var amountProperty) ||
                    amountProperty.ValueKind != JsonValueKind.Number)
                {
                    throw new InvalidOperationException("Compensation store allocation snapshot contains an invalid amount.");
                }

                var amount = Math.Round(amountProperty.GetDecimal(), 2, MidpointRounding.AwayFromZero);
                if (amount < 0m)
                {
                    throw new InvalidOperationException("Compensation store allocation snapshot contains a negative amount.");
                }

                allocations.Add(new CompensationStoreAllocation(tenantId, amount));
            }

            if (allocations.Count == 0)
            {
                return [];
            }

            var expectedRoundedAmount = Math.Round(expectedAmount, 2, MidpointRounding.AwayFromZero);
            var allocatedAmount = Math.Round(allocations.Sum(allocation => allocation.Amount), 2, MidpointRounding.AwayFromZero);
            if (allocatedAmount != expectedRoundedAmount)
            {
                throw new InvalidOperationException(
                    $"Compensation store allocation snapshot total {allocatedAmount} does not match expected amount {expectedRoundedAmount}.");
            }

            return allocations
                .GroupBy(allocation => allocation.TenantId)
                .Select(group => new CompensationStoreAllocation(
                    group.Key,
                    Math.Round(group.Sum(item => item.Amount), 2, MidpointRounding.AwayFromZero)))
                .OrderBy(allocation => allocation.TenantId)
                .ToList();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Compensation store allocation snapshot is not valid JSON.", ex);
        }
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

    private static string BuildCompensationLedgerIdempotencyKey(Guid messageId, Guid accountingTenantId) =>
        $"CH_PAYMENT_COMP_REFUND:{messageId:N}:{accountingTenantId:N}";

    private static string BuildCompensationRefundDescription(
        CommerceHubPayment payment,
        CommerceHubPaymentCompensationOutboxMessage message,
        string? gatewayReference)
    {
        var orderNumber = payment.Order?.OrderNumber;
        var reference = string.IsNullOrWhiteSpace(gatewayReference)
            ? message.ProviderTransactionId ?? message.ProviderIntentId ?? "n/a"
            : gatewayReference;

        return string.IsNullOrWhiteSpace(orderNumber)
            ? $"Provider compensation refund for captured CommerceHub payment {payment.Id:D}. Reference: {reference}"
            : $"Provider compensation refund for captured CommerceHub payment on order {orderNumber}. Reference: {reference}";
    }

    private sealed record CompensationStoreAllocation(Guid TenantId, decimal Amount);

    private async Task RecordFailureAsync(
        ApplicationDbContext dbContext,
        CommerceHubPaymentCompensationOutboxMessage message,
        string error,
        CancellationToken cancellationToken)
    {
        message.AttemptCount++;
        message.LastError = error.Length > 2000 ? error[..2000] : error;
        message.ProcessingLeaseId = null;
        message.ProcessingLeaseExpiresAtUtc = null;
        message.IsFailed = false;
        message.UpdatedAt = DateTime.UtcNow;

        var delayMinutes = Math.Min(MaxRetryDelay.TotalMinutes, Math.Pow(2, Math.Min(message.AttemptCount - 1, 6)));
        message.NextRetryAtUtc = DateTime.UtcNow.AddMinutes(delayMinutes);

        var payment = await dbContext.CommerceHubPayments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == message.PaymentId && !item.IsDeleted, cancellationToken);

        if (payment != null && payment.Status is CommerceHubPaymentRecordStatus.RefundRequired or CommerceHubPaymentRecordStatus.RefundFailed)
        {
            payment.Status = CommerceHubPaymentRecordStatus.RefundRequired;
            payment.FailureReason = message.AttemptCount >= EscalationAttemptThreshold
                ? $"Refund compensation still unresolved after {message.AttemptCount} attempts and requires operator review: {message.LastError}"
                : message.LastError;
            payment.UpdatedAt = DateTime.UtcNow;
        }

        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task RecordGatewayOutcomeUncertainAsync(
        ApplicationDbContext dbContext,
        CommerceHubPaymentCompensationOutboxMessage message,
        CommerceHubPayment payment,
        string error,
        CancellationToken cancellationToken)
    {
        message.AttemptCount++;
        message.LastError = error.Length > 2000 ? error[..2000] : error;
        message.ProcessingLeaseId = null;
        message.ProcessingLeaseExpiresAtUtc = null;
        message.NextRetryAtUtc = null;
        message.IsFailed = true;
        message.RequiresOperatorReconciliation = true;
        message.GatewayOutcomeUncertainAtUtc ??= DateTime.UtcNow;
        message.UpdatedAt = DateTime.UtcNow;

        if (payment.Status is CommerceHubPaymentRecordStatus.RefundRequired or CommerceHubPaymentRecordStatus.RefundFailed)
        {
            payment.Status = CommerceHubPaymentRecordStatus.RefundRequired;
            payment.FailureReason = message.LastError;
            payment.UpdatedAt = DateTime.UtcNow;
        }

        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<CommerceHubGatewayRefundResult> ExecuteRefundWithDurableStartMarkerAsync(
        ApplicationDbContext dbContext,
        ICommerceHubRefundGatewayService refundGateway,
        CommerceHubPayment payment,
        CommerceHubPaymentCompensationOutboxMessage message,
        CancellationToken cancellationToken)
    {
        message.GatewayRequestStartedAtUtc = DateTime.UtcNow;
        message.LastError = null;
        message.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return await refundGateway.RefundAsync(
            payment,
            message.Amount,
            message.Currency,
            message.Reason,
            message.IdempotencyKey,
            cancellationToken);
    }

    private static async Task PersistGatewayOutcomeAsync(
        ApplicationDbContext dbContext,
        CommerceHubPaymentCompensationOutboxMessage message,
        CommerceHubGatewayRefundResult result,
        CancellationToken cancellationToken)
    {
        message.GatewayReference = result.Reference ?? message.GatewayReference;
        message.GatewayStatus = result.GatewayStatus ?? (result.IsConfirmed ? "confirmed" : message.GatewayStatus);
        message.GatewayOutcomeUncertainAtUtc = null;
        message.RequiresOperatorReconciliation = false;
        message.LastError = null;
        message.IsFailed = false;
        message.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool HasDurableGatewayOutcome(CommerceHubPaymentCompensationOutboxMessage message) =>
        !string.IsNullOrWhiteSpace(message.GatewayReference) ||
        !string.IsNullOrWhiteSpace(message.GatewayStatus);

    private static bool RequiresGatewayOutcomeReconciliation(CommerceHubPaymentCompensationOutboxMessage message) =>
        message.GatewayRequestStartedAtUtc.HasValue &&
        !HasDurableGatewayOutcome(message);

    private static CommerceHubGatewayRefundResult BuildGatewayResultFromPersistedOutcome(
        CommerceHubPaymentCompensationOutboxMessage message,
        PaymentProvider provider) =>
        new(
            Accepted: true,
            IsConfirmed: IsConfirmedGatewayStatus(message.GatewayStatus),
            Provider: provider.ToString(),
            Reference: message.GatewayReference,
            GatewayStatus: message.GatewayStatus,
            FailureReason: null);

    private static bool IsConfirmedGatewayStatus(string? gatewayStatus) =>
        gatewayStatus is not null &&
        (string.Equals(gatewayStatus, "confirmed", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(gatewayStatus, "succeeded", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(gatewayStatus, "success", StringComparison.OrdinalIgnoreCase));

    private static bool IsDevelopmentSimulationCompensation(
        CommerceHubPaymentCompensationOutboxMessage message,
        CommerceHubPayment payment) =>
        (message.ProviderTransactionId?.StartsWith("DEV-SIM:ONLINE:", StringComparison.Ordinal) ?? false) ||
        (payment.ProviderTransactionId?.StartsWith("DEV-SIM:ONLINE:", StringComparison.Ordinal) ?? false);

    internal static async Task CloseDevelopmentSimulationCompensationAsync(
        ApplicationDbContext dbContext,
        CommerceHubPaymentCompensationOutboxMessage message,
        CommerceHubPayment payment,
        CancellationToken cancellationToken)
    {
        const string reason =
            "DEVELOPMENT_PAYMENT_SIMULATION_NOT_COLLECTED: No external money movement existed; compensation closed without a gateway refund.";
        var nowUtc = DateTime.UtcNow;

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            payment.Status = CommerceHubPaymentRecordStatus.Failed;
            payment.FailureReason = reason;
            payment.PaidAtUtc = null;
            payment.IsProcessed = true;
            payment.ProcessedAtUtc ??= nowUtc;
            payment.UpdatedAt = nowUtc;

            foreach (var attempt in payment.Attempts.Where(attempt =>
                         attempt.Status == CommerceHubPaymentRecordStatus.RefundRequired &&
                         string.Equals(attempt.ProviderTransactionId, message.ProviderTransactionId, StringComparison.Ordinal)))
            {
                attempt.Status = CommerceHubPaymentRecordStatus.Failed;
                attempt.FailureReason = reason;
                attempt.CompletedAtUtc ??= nowUtc;
                attempt.UpdatedAt = nowUtc;
            }

            message.ProcessedAtUtc = nowUtc;
            message.NextRetryAtUtc = null;
            message.ProcessingLeaseId = null;
            message.ProcessingLeaseExpiresAtUtc = null;
            message.IsFailed = false;
            message.RequiresOperatorReconciliation = false;
            message.LastError = reason;
            message.UpdatedAt = nowUtc;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task<ClaimOutcome> TryClaimMessageAsync(
        ApplicationDbContext dbContext,
        CommerceHubPaymentCompensationOutboxMessage initialMessage,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                if (dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // Convert PaymentId GUID to deterministic 64-bit advisory lock key
                    // Using first 8 bytes of GUID ensures stable, unique lock per payment
                    var guidBytes = initialMessage.PaymentId.ToByteArray();
                    var lockId = BitConverter.ToInt64(guidBytes, 0);
                    await dbContext.Database.ExecuteSqlAsync(
                        $"SELECT pg_advisory_xact_lock({lockId})",
                        cancellationToken);
                }

                dbContext.ChangeTracker.Clear();
                var nowUtc = DateTime.UtcNow;
                var message = await dbContext.CommerceHubPaymentCompensationOutboxMessages
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(item => item.Id == initialMessage.Id, cancellationToken);

                if (message == null || !IsEligibleForProcessing(message, nowUtc))
                {
                    await tx.RollbackAsync(cancellationToken);
                    return ClaimOutcome.NotEligible;
                }

                if (await HasUnprocessedEarlierPaymentCompensationAsync(dbContext, message, cancellationToken))
                {
                    await tx.RollbackAsync(cancellationToken);
                    return ClaimOutcome.Deferred;
                }

                var leaseId = Guid.NewGuid();
                message.ProcessingLeaseId = leaseId;
                message.ProcessingLeaseExpiresAtUtc = nowUtc.Add(ClaimLeaseDuration);
                message.UpdatedAt = nowUtc;

                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return ClaimOutcome.ClaimedWith(leaseId);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(cancellationToken);
                return ClaimOutcome.NotEligible;
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    private static Task<bool> HasUnprocessedEarlierPaymentCompensationAsync(
        ApplicationDbContext dbContext,
        CommerceHubPaymentCompensationOutboxMessage message,
        CancellationToken cancellationToken) =>
        dbContext.CommerceHubPaymentCompensationOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(other =>
                other.Id != message.Id &&
                other.PaymentId == message.PaymentId &&
                !other.IsDeleted &&
                other.ProcessedAtUtc == null &&
                (other.CreatedAt < message.CreatedAt ||
                 (other.CreatedAt == message.CreatedAt && other.Id.CompareTo(message.Id) < 0)),
                cancellationToken);

    private static bool IsEligibleForProcessing(
        CommerceHubPaymentCompensationOutboxMessage message,
        DateTime nowUtc) =>
        message.ProcessedAtUtc == null &&
        !message.IsDeleted &&
        ((message.ProviderTransactionId?.StartsWith("DEV-SIM:ONLINE:", StringComparison.Ordinal) ?? false) ||
         (!message.RequiresOperatorReconciliation &&
          (message.NextRetryAtUtc == null || message.NextRetryAtUtc <= nowUtc))) &&
        (message.ProcessingLeaseExpiresAtUtc == null || message.ProcessingLeaseExpiresAtUtc <= nowUtc);

    private readonly record struct ClaimOutcome(string Status, Guid? LeaseId)
    {
        public static ClaimOutcome ClaimedWith(Guid leaseId) => new("claimed", leaseId);
        public static ClaimOutcome Deferred { get; } = new("deferred", null);
        public static ClaimOutcome NotEligible { get; } = new("not_eligible", null);
        public bool IsClaimed => Status == "claimed";
    }
}
