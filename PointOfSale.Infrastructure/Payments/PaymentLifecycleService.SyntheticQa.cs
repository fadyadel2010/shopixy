using System.Data;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Payments;

public partial class PaymentLifecycleService
{
    public async Task<PaymentTransitionResult> RecordSyntheticQaCollectionAsync(
        Guid tenantId, Guid orderId, Guid verifierId, Guid evidenceId, CancellationToken ct = default)
    {
        if (_syntheticQa == null) throw new InvalidOperationException("SYNTHETIC_QA_UNAVAILABLE");
        await _syntheticQa.EnsureVerifierAsync(tenantId, verifierId, ct);
        // ReadCommitted + locked order serializes concurrent consumption without a stale snapshot.
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var order = await _db.Orders.FromSqlInterpolated($@"SELECT *, xmin FROM ""Orders""
            WHERE ""Id"" = {orderId} AND ""TenantId"" = {tenantId} AND NOT ""IsDeleted"" FOR UPDATE")
            .IgnoreQueryFilters().SingleOrDefaultAsync(ct) ?? throw new InvalidOperationException("ORDER_NOT_FOUND");
        await _db.Entry(order).ReloadAsync(ct);
        var evidence = await _syntheticQa.RequireEvidenceAsync(order, evidenceId, "Collection", ct);
        await _db.Entry(evidence).ReloadAsync(ct);
        if (evidence.VerifierId != verifierId) throw new UnauthorizedAccessException("SYNTHETIC_QA_VERIFIER_REQUIRED");
        if (evidence.ConsumedAtUtc.HasValue)
        {
            await _syntheticQa.EnsureSyntheticCollectionAsync(order, ct);
            return PaymentTransitionResult.Succeeded(PaymentStatus.SyntheticQaCollected, wasIdempotent: true, stateChanged: false);
        }
        if (order.PaymentStatus is not (PaymentStatus.Pending or PaymentStatus.AwaitingVerification or PaymentStatus.CashOnDelivery) ||
            order.TotalPaidAmount != 0 || order.Items.Any(x => x.RefundedAmount != 0) || order.IsPaid)
            throw new InvalidOperationException("SYNTHETIC_QA_PAYMENT_NOT_ELIGIBLE");
        if (!await _db.TenantPaymentMethods.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId &&
            x.Code == order.PaymentMethodCode && x.IsEnabled && !x.IsDeleted, ct))
            throw new InvalidOperationException("SYNTHETIC_QA_PAYMENT_METHOD_DISABLED");
        var intent = await _db.PaymentIntents.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.OrderId == orderId && x.TenantId == tenantId, ct);
        if (order.PaymentMethodCode == "MANUALWALLET" && (intent == null || intent.Amount != order.Total || intent.Currency != order.Currency ||
            intent.Status is not (PaymentIntentStatus.Pending or PaymentIntentStatus.AwaitingVerification) ||
            ValidateManualWalletExpirationAuthority(intent, DateTime.UtcNow, allowElapsed: false) != null))
            throw new InvalidOperationException("SYNTHETIC_QA_WALLET_INTENT_INVALID");
        var request = new HandlePaymentResultRequest { TenantId = tenantId, OrderId = orderId,
            Source = PaymentLifecycleSource.PaymentReviewApproval, Outcome = PaymentLifecycleOutcome.Succeeded,
            Currency = order.Currency, Amount = order.Total };
        var reservation = await ValidateReservationCanAcceptPaymentAsync(order, request, DateTime.UtcNow, ct);
        if (!reservation.IsValid) throw new InvalidOperationException("SYNTHETIC_QA_RESERVATION_INVALID");
        ValidateSuccessfulAmount(order, evidence.Amount);
        await _stockRepository.CommitOrderReservationsAsync(tenantId, orderId, ct);
        await _couponValidationService.ConfirmCouponUsageAsync(tenantId, orderId, _correlationIdAccessor.GetCorrelationId(), ct);
        await ConfirmOfferPromotionUsageAsync(order, _correlationIdAccessor.GetCorrelationId(), ct);
        PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext();
        try
        {
            // Distinct status: never allocate real PaidAmount or assert IsPaid/PaidAt.
            order.PaymentStatus = PaymentStatus.SyntheticQaCollected;
            order.AcceptPaymentStatusChange();
            order.PaymentGateway = "SYNTHETIC_QA_NO_MONEY";
            order.PaymentReference = $"SYNTHETIC-QA:{evidence.Id:N}";
            order.FinancialPostingStatus = FinancialPostingStatus.NotRequired;
            if (intent != null)
            {
                intent.Status = PaymentIntentStatus.SyntheticQaCollected;
                intent.InventoryCommitted = true;
            }
            evidence.ConsumedAtUtc = DateTime.UtcNow;
            await _eventPublisher.PublishAsync(new PointOfSale.Core.Events.SyntheticQaCollectionRecordedEvent {
                EventId = evidence.Id, IdempotencyKey = $"SYNTHETIC_QA_COLLECTION:{evidence.Id:N}",
                TenantId = tenantId, OrderId = orderId, EvidenceId = evidence.Id, RunId = evidence.RunId,
                Amount = evidence.Amount, Currency = evidence.Currency }, ct);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        finally { PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext(); }
        return PaymentTransitionResult.Succeeded(PaymentStatus.SyntheticQaCollected, stateChanged: true,
            inventoryAction: PaymentInventoryAction.Commit);
    }
}
